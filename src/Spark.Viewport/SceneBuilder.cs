using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Viewport;

/// <summary>
/// Turns the values a graph produced into <see cref="RenderPackage"/>s, one per
/// <see cref="GeometryKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>One package per <c>(NodeId, PortIndex)</c>, however many values that port produced.</b> That
/// is not an optimisation, it is the identity rule: the scene is keyed by the tuple, so a node
/// whose output is a hundred points has to arrive as a hundred markers inside one buffer set. It is
/// what makes re-evaluating one node re-upload one buffer instead of rebuilding the scene.
/// </para>
/// <para>
/// <b>Marker size is decided once, after everything has been collected.</b> A point has no extent,
/// so it has to be drawn at a size, and a size fixed in advance is either invisible on a building
/// or enormous on a detail. Collecting first and sizing against the overall bounds is the reason
/// this is a builder rather than a function.
/// </para>
/// <para>
/// <b>What it understands.</b> <c>Point3d</c>, <c>Vector3d</c> (drawn from the origin),
/// <c>BoundingBox</c>, <c>Plane</c> (drawn as a patch), any <c>Curve</c> (tessellated),
/// <see cref="Displayable"/> (unwrapped, and its colour applied) and <see cref="SparkList"/> at any
/// rank. Anything else — a number, a string,
/// a value from a package Spark has never seen — is counted as unrenderable and contributes
/// nothing, which is how a graph full of arithmetic produces an empty viewport rather than an
/// error.
/// </para>
/// </remarks>
public sealed class SceneBuilder
{
    private const int MaximumValuesPerKey = 200_000;

    private readonly Dictionary<GeometryKey, Group> _groups = [];
    private readonly List<GeometryKey> _order = [];
    private readonly List<Drawable> _unprepared = [];
    private Bounds3 _bounds = Bounds3.Empty;
    private int _maximumParallelism = Environment.ProcessorCount;

    /// <summary>How many renderable values have been collected across every key.</summary>
    public int RenderableCount { get; private set; }

    /// <summary>How many values were seen that no rule here knows how to draw.</summary>
    public int UnrenderableCount { get; private set; }

    /// <summary>
    /// How many values may be tessellated at once (<c>E9-T7</c>). Defaults to the processor count.
    /// </summary>
    /// <remarks>
    /// <b>The packages do not depend on it.</b> Each value is tessellated on its own and emitted
    /// afterwards in the order its key arrived, so one thread and sixteen build the same buffers;
    /// this decides only how long it takes. One is the answer for a caller that must stay off the
    /// thread pool.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    public int MaximumParallelism
    {
        get => _maximumParallelism;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maximumParallelism = value;
        }
    }

    /// <summary>
    /// Called on the worker thread as each value starts tessellating. For tests, which cannot
    /// otherwise see that two tessellations overlap without timing them.
    /// </summary>
    internal Action? Preparing { get; set; }

    /// <summary>The keys that produced at least one renderable value, in the order they arrived.</summary>
    /// <returns>A snapshot.</returns>
    /// <remarks>
    /// A key whose only values were solids the kernel could not tessellate drew nothing, and is left
    /// out once <see cref="Build"/> has found that out.
    /// </remarks>
    public IReadOnlyList<GeometryKey> Keys() =>
        [.. _order.Where(key => _groups[key].Drawables.Exists(drawable => !drawable.Failed))];

    /// <summary>
    /// Collects a graph value under a key, walking lists to any depth.
    /// </summary>
    /// <param name="key">The <c>(NodeId, PortIndex)</c> the value came from.</param>
    /// <param name="value">The value, which may be a list, a <see cref="Displayable"/> or neither.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> has a null node id.</exception>
    public void Add(GeometryKey key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key.NodeId);
        Collect(key, value, Api.Appearance.Default.Colour, wrapped: false);
    }

    /// <summary>Builds one package per key that produced renderable geometry.</summary>
    /// <returns>The packages, in the order their keys first arrived.</returns>
    public IReadOnlyList<RenderPackage> Build()
    {
        Prepare();

        float marker = MarkerRadius();
        List<RenderPackage> packages = new(_order.Count);

        foreach (GeometryKey key in _order)
        {
            Group group = _groups[key];
            MeshAccumulator mesh = new();

            foreach (Drawable drawable in group.Drawables)
            {
                drawable.Emit(mesh, marker);
            }

            if (mesh.IsEmpty)
            {
                continue;
            }

            packages.Add(mesh.ToPackage(key, group.Appearance));
        }

        return packages;
    }

    /// <summary>
    /// Replaces the geometry of every key this builder collected, and removes the geometry of any
    /// key in <paramref name="retire"/> that produced nothing.
    /// </summary>
    /// <remarks>
    /// The two halves matter equally. Without the first, a re-evaluated node leaves its old
    /// geometry behind; without the second, a node that used to produce points and now produces
    /// none leaves them on screen — which reads as the graph not having run.
    /// </remarks>
    /// <param name="scene">The scene to publish into.</param>
    /// <param name="retire">Keys that must be removed unless this builder produced them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
    public void PublishTo(ViewportScene scene, IEnumerable<GeometryKey>? retire = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        HashSet<GeometryKey> published = [];
        foreach (RenderPackage package in Build())
        {
            scene.Set(package);
            published.Add(package.Key);
        }

        if (retire is null)
        {
            return;
        }

        foreach (GeometryKey key in retire)
        {
            if (!published.Contains(key))
            {
                scene.Remove(key);
            }
        }
    }

    private void Collect(GeometryKey key, object? value, Rgba colour, bool wrapped)
    {
        switch (value)
        {
            case null:
                return;

            case Displayable displayable:
                Collect(key, displayable.Geometry, displayable.Appearance.Colour, wrapped: true);
                return;

            case SparkList list:
                foreach (object? item in list)
                {
                    Collect(key, item, colour, wrapped);
                }

                return;

            case Point3d point:
                Record(key, new PointMarker(ToVector(point)), colour, wrapped);
                return;

            case Vector3d vector:
                Record(key, new Segment(Vector3.Zero, ToVector(vector)), colour, wrapped);
                return;

            case Spark.Geometry.BoundingBox box:
                Record(key, new BoxDrawable(ToVector(box.Min), ToVector(box.Max)), colour, wrapped);
                return;

            case Spark.Geometry.Plane plane:
                Record(key, new PlanePatch(plane), colour, wrapped);
                return;

            case Curve curve:
                Record(key, new CurveDrawable(curve), colour, wrapped);
                return;

            case Surface surface:
                Record(key, new SurfaceDrawable(surface), colour, wrapped);
                return;

            case PointCloud cloud:
                // One marker per point, as a list of points would be drawn - which is what it is,
                // seen from the viewport.
                foreach (Point3d point in cloud.Points())
                {
                    Record(key, new PointMarker(ToVector(point)), colour, wrapped);
                }

                return;

            case Spark.Geometry.Mesh drawn:
                Record(key, new MeshDrawable(drawn), colour, wrapped);
                return;

            case Brep solid:
                // Through the kernel, and alongside everything else in the build: see SolidDrawable.
                Record(key, new SolidDrawable(solid), colour, wrapped);
                return;

            default:
                UnrenderableCount++;
                return;
        }
    }

    private void Record(GeometryKey key, Drawable drawable, Rgba colour, bool wrapped)
    {
        if (!_groups.TryGetValue(key, out Group? group))
        {
            group = new Group();
            _groups[key] = group;
            _order.Add(key);
        }

        if (group.Drawables.Count >= MaximumValuesPerKey)
        {
            UnrenderableCount++;
            return;
        }

        // The first explicitly styled value decides the whole buffer set's colour, because a
        // package carries one appearance. Mixed colours on one port are a later slice; taking the
        // first stated one is at least the colour the user asked for somewhere.
        if (wrapped && !group.HasStatedColour)
        {
            group.Appearance = group.Appearance with { Surface = Convert(colour), Edge = Convert(colour) };
            group.HasStatedColour = true;
        }

        group.Drawables.Add(drawable);
        RenderableCount++;

        if (drawable.NeedsPreparing)
        {
            _unprepared.Add(drawable);
        }
        _bounds = drawable.Extend(_bounds);
    }

    /// <summary>
    /// Tessellates every value collected since the last build, in parallel (<c>E9-T7</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only the expensive half is parallel.</b> Tessellating a surface, a solid or a curve is the
    /// cost, and each is independent of every other; emitting into the accumulator is cheap and stays
    /// serial, in key order, which is what keeps the packages identical to a one-thread build.
    /// </para>
    /// <para>
    /// <b>Safe for the kernel.</b> Nothing serialises tessellation globally, and the one place two
    /// threads could meet - the same resident solid twice in a list - is the case `E12-T20` made safe:
    /// one call meshes the native shape in place and the others mesh a copy.
    /// </para>
    /// <para>
    /// A solid the kernel cannot tessellate moves from the renderable count to the unrenderable one
    /// here, where it is first known. The first failure a value throws is rethrown as itself, not
    /// wrapped, so a caller sees what it would have seen from a serial build.
    /// </para>
    /// </remarks>
    private void Prepare()
    {
        if (_unprepared.Count == 0)
        {
            return;
        }

        Drawable[] work = [.. _unprepared];
        _unprepared.Clear();

        try
        {
            Parallel.For(
                0,
                work.Length,
                new ParallelOptions { MaxDegreeOfParallelism = _maximumParallelism },
                index =>
                {
                    Preparing?.Invoke();
                    work[index].Prepare();
                });
        }
        catch (AggregateException failure) when (failure.InnerExceptions.Count > 0)
        {
            ExceptionDispatchInfo.Capture(failure.InnerExceptions[0]).Throw();
        }

        foreach (Drawable drawable in work)
        {
            if (drawable.Failed)
            {
                RenderableCount--;
                UnrenderableCount++;
            }
        }
    }

    private float MarkerRadius()
    {
        if (_bounds.IsEmpty)
        {
            return 0.05f;
        }

        Vector3 span = _bounds.Max - _bounds.Min;
        float diagonal = span.Length();

        // A single point has no span at all, and a row of points along one axis has none across it.
        // Falling back to a fixed size there is what stops a one-point graph rendering an invisible
        // dot or a scene-sized ball.
        return diagonal <= 1e-6f ? 0.05f : Math.Clamp(diagonal * 0.012f, 1e-4f, 1e6f);
    }

    private static Vector3 ToVector(in Point3d point) => new((float)point.X, (float)point.Y, (float)point.Z);

    private static Vector3 ToVector(in Vector3d vector) => new((float)vector.X, (float)vector.Y, (float)vector.Z);

    private static ViewportColor Convert(Rgba colour) =>
        new(colour.Red / 255f, colour.Green / 255f, colour.Blue / 255f, colour.Alpha / 255f);

    private sealed class Group
    {
        internal List<Drawable> Drawables { get; } = [];

        internal Appearance Appearance { get; set; } = Appearance.Default;

        internal bool HasStatedColour { get; set; }
    }

    private abstract class Drawable
    {
        /// <summary>Whether <see cref="Prepare"/> has expensive work to do; false for markers and boxes.</summary>
        internal virtual bool NeedsPreparing => false;

        /// <summary>Whether preparing found nothing to draw, which only a solid can.</summary>
        internal virtual bool Failed => false;

        /// <summary>Grows the scene's bounds by this value's, known before anything is tessellated.</summary>
        internal abstract Bounds3 Extend(Bounds3 bounds);

        /// <summary>The expensive half, run alongside every other value's (<c>E9-T7</c>).</summary>
        internal virtual void Prepare()
        {
        }

        internal abstract void Emit(MeshAccumulator mesh, float marker);
    }

    private sealed class PointMarker(Vector3 position) : Drawable
    {
        internal override Bounds3 Extend(Bounds3 bounds) => bounds.Union(position);

        internal override void Emit(MeshAccumulator mesh, float marker) =>
            mesh.AddOctahedron(position, marker);
    }

    private sealed class Segment(Vector3 start, Vector3 end) : Drawable
    {
        internal override Bounds3 Extend(Bounds3 bounds) => bounds.Union(start).Union(end);

        internal override void Emit(MeshAccumulator mesh, float marker)
        {
            mesh.AddEdge(start, end);
            mesh.AddOctahedron(end, marker);
        }
    }

    private sealed class BoxDrawable(Vector3 min, Vector3 max) : Drawable
    {
        internal override Bounds3 Extend(Bounds3 bounds) => bounds.Union(min).Union(max);

        internal override void Emit(MeshAccumulator mesh, float marker) => mesh.AddBox(min, max);
    }

    /// <summary>
    /// A curve, drawn as the polyline its own tessellator produces.
    /// </summary>
    /// <remarks>
    /// <b>The display tolerance is derived from the curve, not taken from the kernel default.</b>
    /// The kernel's default linear tolerance is 1e-6, and tessellating a one-unit circle to that
    /// would emit about 2,200 segments for something a few hundred pixels across. A sag of a
    /// thousandth of the curve's own length is invisible at any sane zoom and costs two orders of
    /// magnitude fewer segments. A viewport is allowed to be approximate; it is not allowed to be
    /// slow, and it must never be the thing that decides what the kernel's tolerance means.
    /// </remarks>
    private sealed class CurveDrawable : Drawable
    {
        private readonly Curve _curve;
        private readonly Spark.Geometry.BoundingBox _bounds;
        private Point3d[] _points = [];

        internal CurveDrawable(Curve curve)
        {
            _curve = curve;
            _bounds = curve.BoundingBox;
        }

        internal override bool NeedsPreparing => true;

        internal override Bounds3 Extend(Bounds3 bounds) =>
            bounds.Union(ToVector(_bounds.Min)).Union(ToVector(_bounds.Max));

        internal override void Prepare()
        {
            double sag = Math.Max(_curve.Length * 0.001, 1e-12);
            _points = _curve.Tessellate(new Tolerance(sag, Angle.FromDegrees(0.001), 1e-12));
        }

        internal override void Emit(MeshAccumulator mesh, float marker)
        {
            for (int index = 1; index < _points.Length; index++)
            {
                mesh.AddEdge(ToVector(_points[index - 1]), ToVector(_points[index]));
            }
        }
    }

    /// <summary>
    /// A mesh, drawn as its faces with the normals it carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The mesh is triangulated here rather than at the accumulator</b>, because a quad is two
    /// triangles and the accumulator's <c>AddQuad</c> also emits the quad's four edges — which is
    /// right for a plane patch and wrong for a surface, where it would draw the whole tessellation
    /// grid as wireframe over the shading.
    /// </para>
    /// <para>
    /// <b>Vertex normals are used when the mesh has them and computed per face when it does not.</b>
    /// A tessellated surface carries exact normals and shading it flat would make a sphere look
    /// faceted for no reason; a mesh from a file often has none, and a face normal is the only
    /// honest answer there.
    /// </para>
    /// </remarks>
    private sealed class MeshDrawable(Spark.Geometry.Mesh mesh) : Drawable
    {
        private Spark.Geometry.Mesh? _triangles;
        private Vector3d[]? _normals;

        internal override bool NeedsPreparing => true;

        internal override Bounds3 Extend(Bounds3 bounds) =>
            bounds.Union(ToVector(mesh.BoundingBox.Min)).Union(ToVector(mesh.BoundingBox.Max));

        internal override void Prepare()
        {
            _triangles = mesh.Triangulated();
            _normals = _triangles.Normals();
        }

        internal override void Emit(MeshAccumulator accumulator, float marker)
        {
            if (_triangles is not null)
            {
                EmitTriangles(accumulator, _triangles, _normals);
            }
        }
    }

    /// <summary>
    /// A surface, tessellated at a display tolerance derived from its own size.
    /// </summary>
    /// <remarks>
    /// The bounds are the surface's, taken when it is collected, because the marker size is decided
    /// from the whole scene before anything is tessellated; the tessellation waits for the parallel
    /// pass.
    /// </remarks>
    private sealed class SurfaceDrawable : Drawable
    {
        private readonly Surface _surface;
        private readonly Spark.Geometry.BoundingBox _bounds;
        private readonly Tolerance _tolerance;
        private Spark.Geometry.Mesh? _triangles;
        private Vector3d[]? _normals;

        internal SurfaceDrawable(Surface surface)
        {
            _surface = surface;
            _bounds = surface.BoundingBox;
            _tolerance = DisplayTolerance(_bounds);
        }

        internal override bool NeedsPreparing => true;

        internal override Bounds3 Extend(Bounds3 bounds) =>
            bounds.Union(ToVector(_bounds.Min)).Union(ToVector(_bounds.Max));

        internal override void Prepare()
        {
            _triangles = _surface.ToMesh(_tolerance).Triangulated();
            _normals = _triangles.Normals();
        }

        internal override void Emit(MeshAccumulator accumulator, float marker)
        {
            if (_triangles is not null)
            {
                EmitTriangles(accumulator, _triangles, _normals);
            }
        }
    }

    /// <summary>
    /// A solid, tessellated through whichever kernel is installed.
    /// </summary>
    /// <remarks>
    /// <b>Through the kernel, not around it.</b> Tessellating a trimmed face is behind the seam
    /// (ADR-0021), so the viewport asks whichever provider is installed - and with none, the
    /// no-provider kernel still draws an untrimmed shape, because that is a surface and surfaces are
    /// in front of the seam. A shape it cannot draw is counted unrenderable rather than drawn
    /// wrongly. Its box still counts towards the scene's extent, because that is known before the
    /// kernel is asked and it is geometry the user does have.
    /// </remarks>
    private sealed class SolidDrawable : Drawable
    {
        private readonly Brep _solid;
        private readonly Spark.Geometry.BoundingBox _bounds;
        private readonly Tolerance _tolerance;
        private Spark.Geometry.Mesh? _triangles;
        private Vector3d[]? _normals;
        private bool _failed;

        internal SolidDrawable(Brep solid)
        {
            _solid = solid;
            _bounds = solid.BoundingBox;
            _tolerance = DisplayTolerance(_bounds);
        }

        internal override bool NeedsPreparing => true;

        internal override bool Failed => _failed;

        internal override Bounds3 Extend(Bounds3 bounds) =>
            bounds.Union(ToVector(_bounds.Min)).Union(ToVector(_bounds.Max));

        internal override void Prepare()
        {
            KernelResult<Spark.Geometry.Mesh> tessellated = BrepKernel.Current.Tessellate(_solid, _tolerance);

            if (tessellated.TryGetValue(out Spark.Geometry.Mesh? mesh))
            {
                _triangles = mesh.Triangulated();
                _normals = _triangles.Normals();
            }
            else
            {
                _failed = true;
            }
        }

        internal override void Emit(MeshAccumulator accumulator, float marker)
        {
            if (_triangles is not null)
            {
                EmitTriangles(accumulator, _triangles, _normals);
            }
        }
    }

    /// <summary>Emits a triangulated mesh, shaded by its vertex normals when it has them.</summary>
    private static void EmitTriangles(MeshAccumulator accumulator, Spark.Geometry.Mesh triangles, Vector3d[]? normals)
    {
        for (int index = 0; index < triangles.FaceCount; index++)
        {
            MeshFace face = triangles.Face(index);

            if (normals is null)
            {
                accumulator.AddTriangle(
                    ToVector(triangles.Vertex(face.A)),
                    ToVector(triangles.Vertex(face.B)),
                    ToVector(triangles.Vertex(face.C)));

                continue;
            }

            accumulator.AddShadedTriangle(
                ToVector(triangles.Vertex(face.A)), ToVector(normals[face.A]),
                ToVector(triangles.Vertex(face.B)), ToVector(normals[face.B]),
                ToVector(triangles.Vertex(face.C)), ToVector(normals[face.C]));
        }
    }

    /// <summary>
    /// The sag a surface is tessellated to for display.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Derived from the geometry, not taken from the kernel default</b>, for the reason
    /// <see cref="CurveDrawable"/> records: the kernel's 1e-6 would tessellate a one-unit sphere
    /// into hundreds of thousands of facets for something a few hundred pixels across. A thousandth
    /// of the bounding box's diagonal is invisible at any sane zoom.
    /// </para>
    /// <para>
    /// <b>The angular deflection was 0.5 degrees and that was the whole of `E12-T19`.</b> It reads
    /// like a sensible smoothness figure and is not: it is fifty-seven times finer than the 0.5
    /// RADIANS a mesher of this kind conventionally defaults to, and the mesher's cost against it
    /// is not linear. Measured on the nine solids of `docs/examples/solids.spark`, sag held at a
    /// thousandth throughout:
    /// </para>
    /// <list type="table">
    /// <item><description>0.5 deg — 17,440 ms — 1,110,772 triangles</description></item>
    /// <item><description>2 deg — 266 ms — 79,092</description></item>
    /// <item><description><b>6 deg — 61 ms — 11,636</b></description></item>
    /// <item><description>12 deg — 42 ms — 3,924</description></item>
    /// </list>
    /// <para>
    /// Six degrees is <b>286 times faster</b> than half a degree and gives a cylinder sixty
    /// segments, which is smooth at any zoom this viewport reaches. It is chosen by looking at the
    /// render as well as at the number.
    /// </para>
    /// </remarks>
    private static Tolerance DisplayTolerance(in Spark.Geometry.BoundingBox bounds)
    {
        double diagonal = bounds.Min.DistanceTo(bounds.Max);

        return new Tolerance(Math.Max(diagonal * 0.001, 1e-12), Angle.FromDegrees(6.0), 1e-12);
    }

    private sealed class PlanePatch(Spark.Geometry.Plane plane) : Drawable
    {
        internal override Bounds3 Extend(Bounds3 bounds) => bounds.Union(ToVector(plane.Origin));

        internal override void Emit(MeshAccumulator mesh, float marker)
        {
            float half = Math.Max(marker * 20f, 0.5f);
            Vector3 origin = ToVector(plane.Origin);
            Vector3 x = ToVector(plane.XAxis) * half;
            Vector3 y = ToVector(plane.YAxis) * half;
            mesh.AddQuad(origin - x - y, origin + x - y, origin + x + y, origin - x + y, ToVector(plane.Normal));
        }
    }
}
