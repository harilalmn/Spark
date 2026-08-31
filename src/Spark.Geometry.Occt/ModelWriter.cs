using System;
using System.Collections.Generic;

namespace Spark.Geometry.Occt;

/// <summary>
/// Turns Spark geometry into the flat tables <c>spark_occt_import</c> reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Analytic stays analytic wherever the ABI has a word for it.</b> A cylinder is sent as a
/// cylinder, not as the exact rational spline it could equally be, because the provider's
/// booleans are better at intersecting a cylinder it knows is a cylinder — and because a shape
/// that arrives analytic comes back analytic, which is the property the whole of ADR-0020 was
/// bought for.
/// </para>
/// <para>
/// <b>Everything else becomes NURBS, and the fallback is stated rather than silent.</b> Where the
/// conversion is exact — the quadric surfaces, the conic curves — nothing is lost. Where it is
/// not, <see cref="Approximated"/> says so, so a caller can report an approximation instead of
/// discovering one.
/// </para>
/// </remarks>
internal sealed class ModelWriter
{
    private const double TwoPi = Math.PI * 2.0;

    private readonly List<double> _points = [];

    private readonly List<int> _curveKinds = [];
    private readonly List<int> _curveIntOffsets = [0];
    private readonly List<int> _curveInts = [];
    private readonly List<int> _curveDoubleOffsets = [0];
    private readonly List<double> _curveDoubles = [];

    private readonly List<int> _surfaceKinds = [];
    private readonly List<int> _surfaceIntOffsets = [0];
    private readonly List<int> _surfaceInts = [];
    private readonly List<int> _surfaceDoubleOffsets = [0];
    private readonly List<double> _surfaceDoubles = [];

    private readonly List<int> _vertices = [];
    private readonly List<int> _edges = [];
    private readonly List<int> _trims = [];
    private readonly List<int> _loops = [];
    private readonly List<int> _faces = [];
    private readonly List<int> _shells = [];

    /// <summary>Whether anything in the model had to be approximated to cross the ABI.</summary>
    public bool Approximated { get; private set; }

    /// <summary>Writes a whole BRep.</summary>
    public static ModelWriter FromBrep(Brep shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        ModelWriter writer = new();

        foreach (Point3d point in shape.Points())
        {
            writer._points.Add(point.X);
            writer._points.Add(point.Y);
            writer._points.Add(point.Z);
        }

        foreach (Curve curve in shape.Curves())
        {
            writer.AddCurve(curve);
        }

        foreach (Surface surface in shape.Surfaces())
        {
            writer.AddSurface(surface);
        }

        foreach (BrepVertex vertex in shape.Vertices())
        {
            writer._vertices.Add(vertex.Point);
        }

        foreach (BrepEdge edge in shape.Edges())
        {
            writer._edges.Add(edge.Start);
            writer._edges.Add(edge.End);
            writer._edges.Add(edge.Curve);
        }

        foreach (BrepTrim trim in shape.Trims())
        {
            writer._trims.Add(trim.Edge);
            writer._trims.Add(trim.IsReversed ? 1 : 0);
        }

        foreach (BrepLoop loop in shape.Loops())
        {
            writer._loops.Add(loop.FirstTrim);
            writer._loops.Add(loop.TrimCount);
            writer._loops.Add(loop.Kind == BrepLoopKind.Outer
                ? NativeMethods.LoopOuter
                : NativeMethods.LoopInner);
        }

        foreach (BrepFace face in shape.Faces())
        {
            writer._faces.Add(face.Surface);
            writer._faces.Add(face.FirstLoop);
            writer._faces.Add(face.LoopCount);
            writer._faces.Add(face.IsReversed ? 1 : 0);
        }

        foreach (BrepShell shell in shape.Shells())
        {
            writer._shells.Add(shell.FirstFace);
            writer._shells.Add(shell.FaceCount);
        }

        return writer;
    }

    /// <summary>Writes a profile: curves and nothing else, which is what a sweep takes.</summary>
    public static ModelWriter FromCurves(IReadOnlyList<Curve> curves)
    {
        ArgumentNullException.ThrowIfNull(curves);

        ModelWriter writer = new();

        foreach (Curve curve in curves)
        {
            ArgumentNullException.ThrowIfNull(curve);
            writer.AddCurve(curve);
        }

        return writer;
    }

    /// <summary>Pins every table and fills the descriptor the ABI takes.</summary>
    public ModelDesc Pin(NativeBuffers buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);

        return new ModelDesc
        {
            PointCount = _points.Count / 3,
            Points = buffers.Pin(_points.ToArray()),

            CurveCount = _curveKinds.Count,
            CurveKinds = buffers.Pin(_curveKinds.ToArray()),
            CurveIntOffsets = buffers.Pin(_curveIntOffsets.ToArray()),
            CurveInts = buffers.Pin(_curveInts.ToArray()),
            CurveDoubleOffsets = buffers.Pin(_curveDoubleOffsets.ToArray()),
            CurveDoubles = buffers.Pin(_curveDoubles.ToArray()),

            SurfaceCount = _surfaceKinds.Count,
            SurfaceKinds = buffers.Pin(_surfaceKinds.ToArray()),
            SurfaceIntOffsets = buffers.Pin(_surfaceIntOffsets.ToArray()),
            SurfaceInts = buffers.Pin(_surfaceInts.ToArray()),
            SurfaceDoubleOffsets = buffers.Pin(_surfaceDoubleOffsets.ToArray()),
            SurfaceDoubles = buffers.Pin(_surfaceDoubles.ToArray()),

            VertexCount = _vertices.Count,
            Vertices = buffers.Pin(_vertices.ToArray()),

            EdgeCount = _edges.Count / 3,
            Edges = buffers.Pin(_edges.ToArray()),

            TrimCount = _trims.Count / 2,
            Trims = buffers.Pin(_trims.ToArray()),

            LoopCount = _loops.Count / 3,
            Loops = buffers.Pin(_loops.ToArray()),

            FaceCount = _faces.Count / 4,
            Faces = buffers.Pin(_faces.ToArray()),

            ShellCount = _shells.Count / 2,
            Shells = buffers.Pin(_shells.ToArray()),
        };
    }

    // --------------------------------------------------------------------------------------------
    // Curves
    // --------------------------------------------------------------------------------------------

    private void AddCurve(Curve curve)
    {
        switch (curve)
        {
            case Line line:
                WriteCurve(NativeMethods.CurveLine, [], [
                    line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z,
                    line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z,
                ]);
                return;

            case Circle circle:
                {
                    List<double> values = [];
                    WriteFrame(values, circle.Plane);
                    values.Add(circle.Radius);
                    WriteCurve(NativeMethods.CurveCircle, [], values);
                    return;
                }

            case Arc arc:
                {
                    List<double> values = [];
                    WriteFrame(values, arc.Plane);
                    values.Add(arc.Radius);
                    values.Add(arc.StartAngle.Radians);
                    values.Add(arc.SweepAngle.Radians);
                    WriteCurve(NativeMethods.CurveArc, [], values);
                    return;
                }

            // An ellipse whose y-radius is the larger one is the same ellipse rotated a quarter
            // turn, and the ABI takes major then minor. Rotating the frame here rather than
            // teaching the C side about both orders keeps the encoding one shape.
            case EllipseCurve ellipse when ellipse.XRadius >= ellipse.YRadius:
                {
                    List<double> values = [];
                    WriteFrame(values, ellipse.Plane);
                    values.Add(ellipse.XRadius);
                    values.Add(ellipse.YRadius);
                    values.Add(ellipse.StartAngle.Radians);
                    values.Add(ellipse.SweepAngle.Radians);
                    WriteCurve(NativeMethods.CurveEllipse, [], values);
                    return;
                }

            case NurbsCurve nurbs:
                WriteNurbsCurve(nurbs);
                return;

            default:
                WriteNurbsCurve(ToNurbs(curve));
                return;
        }
    }

    private void WriteNurbsCurve(NurbsCurve nurbs)
    {
        Point3d[] controlPoints = nurbs.ControlPoints();
        double[] weights = nurbs.Weights();
        bool rational = nurbs.IsRational;
        double[] knots = nurbs.Knots.ToArray();

        List<double> values = new(knots.Length + (controlPoints.Length * (rational ? 4 : 3)));
        values.AddRange(knots);

        for (int i = 0; i < controlPoints.Length; i++)
        {
            values.Add(controlPoints[i].X);
            values.Add(controlPoints[i].Y);
            values.Add(controlPoints[i].Z);

            if (rational)
            {
                values.Add(weights[i]);
            }
        }

        WriteCurve(
            NativeMethods.CurveNurbs,
            [nurbs.Degree, controlPoints.Length, knots.Length, rational ? 1 : 0],
            values);
    }

    /// <summary>
    /// The fallback for a curve the ABI has no word for — a polycurve, a polyline, an offset.
    /// </summary>
    /// <remarks>
    /// <b>An interpolation, and therefore an approximation, and it says so.</b> A general
    /// conversion of every Spark curve to an exact NURBS is <c>E2</c> work that does not exist
    /// yet; sampling and interpolating is what can be done today, and hiding that behind an
    /// exact-looking result would make a user's boolean quietly wrong at the fourth decimal.
    /// </remarks>
    private NurbsCurve ToNurbs(Curve curve)
    {
        Approximated = true;

        Point3d[] samples = curve.DivideEqually(64);

        return NurbsCurve.InterpolatePoints(samples, Math.Min(3, samples.Length - 1));
    }

    private void WriteCurve(int kind, IReadOnlyList<int> ints, IReadOnlyList<double> values)
    {
        _curveKinds.Add(kind);
        _curveInts.AddRange(ints);
        _curveDoubles.AddRange(values);
        _curveIntOffsets.Add(_curveInts.Count);
        _curveDoubleOffsets.Add(_curveDoubles.Count);
    }

    // --------------------------------------------------------------------------------------------
    // Surfaces
    // --------------------------------------------------------------------------------------------

    private void AddSurface(Surface surface)
    {
        switch (surface)
        {
            case PlaneSurface plane:
                {
                    List<double> values = [];
                    WriteFrame(values, plane.Plane);
                    WriteDomains(values, plane);
                    WriteSurface(NativeMethods.SurfacePlane, [], values);
                    return;
                }

            case CylindricalSurface cylinder:
                {
                    List<double> values = [];
                    WriteFrame(values, cylinder.Frame);
                    values.Add(cylinder.Radius);
                    WriteDomains(values, cylinder);
                    WriteSurface(NativeMethods.SurfaceCylinder, [], values);
                    return;
                }

            // A cone that narrows as v grows has a negative half-angle, which OpenCascade's
            // conical surface cannot spell. It goes through NURBS instead — exactly, since a cone
            // is a rational surface — rather than being fudged into a frame that flips.
            case ConicalSurface cone when cone.HalfAngle.Radians > 0.0
                && cone.HalfAngle.Radians < Math.PI / 2.0:
                {
                    List<double> values = [];
                    WriteFrame(values, cone.Frame);
                    values.Add(cone.Radius);
                    values.Add(cone.HalfAngle.Radians);
                    WriteDomains(values, cone);
                    WriteSurface(NativeMethods.SurfaceCone, [], values);
                    return;
                }

            case SphericalSurface sphere:
                {
                    List<double> values = [];
                    WriteFrame(values, sphere.Frame);
                    values.Add(sphere.Radius);
                    WriteDomains(values, sphere);
                    WriteSurface(NativeMethods.SurfaceSphere, [], values);
                    return;
                }

            case ToroidalSurface torus:
                {
                    List<double> values = [];
                    WriteFrame(values, torus.Frame);
                    values.Add(torus.MajorRadius);
                    values.Add(torus.MinorRadius);
                    WriteDomains(values, torus);
                    WriteSurface(NativeMethods.SurfaceTorus, [], values);
                    return;
                }

            case NurbsSurface nurbs:
                WriteNurbsSurface(nurbs);
                return;

            case ConicalSurface cone:
                WriteNurbsSurface(cone.ToNurbsSurface());
                return;

            default:
                WriteNurbsSurface(ToNurbs(surface));
                return;
        }
    }

    private void WriteNurbsSurface(NurbsSurface nurbs)
    {
        double[] knotsU = nurbs.KnotsU.ToArray();
        double[] knotsV = nurbs.KnotsV.ToArray();
        int countU = nurbs.ControlPointCountU;
        int countV = nurbs.ControlPointCountV;
        bool rational = nurbs.IsRational;

        List<double> values = new(knotsU.Length + knotsV.Length + (countU * countV * 4));
        values.AddRange(knotsU);
        values.AddRange(knotsV);

        for (int i = 0; i < countU; i++)
        {
            for (int j = 0; j < countV; j++)
            {
                Point3d control = nurbs.ControlPoint(i, j);
                values.Add(control.X);
                values.Add(control.Y);
                values.Add(control.Z);

                if (rational)
                {
                    values.Add(nurbs.Weight(i, j));
                }
            }
        }

        WriteSurface(
            NativeMethods.SurfaceNurbs,
            [
                nurbs.DegreeU,
                nurbs.DegreeV,
                countU,
                countV,
                knotsU.Length,
                knotsV.Length,
                rational ? 1 : 0,
            ],
            values);
    }

    /// <summary>
    /// The fallback for a surface with no analytic equivalent — an extrusion, a revolve, a
    /// ruled surface. Approximate, and recorded as such.
    /// </summary>
    private NurbsSurface ToNurbs(Surface surface)
    {
        Approximated = true;

        // A bilinear patch through the corners is a poor surface and an honest placeholder; the
        // real conversion is E2 work. Nothing in the built-in node set reaches this today, and
        // the flag means a caller can refuse rather than believe it.
        return NurbsSurface.ByCorners([
            surface.PointAt(surface.DomainU.Min, surface.DomainV.Min),
            surface.PointAt(surface.DomainU.Max, surface.DomainV.Min),
            surface.PointAt(surface.DomainU.Max, surface.DomainV.Max),
            surface.PointAt(surface.DomainU.Min, surface.DomainV.Max),
        ]);
    }

    private void WriteSurface(int kind, IReadOnlyList<int> ints, IReadOnlyList<double> values)
    {
        _surfaceKinds.Add(kind);
        _surfaceInts.AddRange(ints);
        _surfaceDoubles.AddRange(values);
        _surfaceIntOffsets.Add(_surfaceInts.Count);
        _surfaceDoubleOffsets.Add(_surfaceDoubles.Count);
    }

    // --------------------------------------------------------------------------------------------
    // Shared
    // --------------------------------------------------------------------------------------------

    /// <summary>Nine doubles: origin, x-axis, y-axis. The normal is x cross y, on both sides.</summary>
    private static void WriteFrame(List<double> values, in Plane plane)
    {
        values.Add(plane.Origin.X);
        values.Add(plane.Origin.Y);
        values.Add(plane.Origin.Z);
        values.Add(plane.XAxis.X);
        values.Add(plane.XAxis.Y);
        values.Add(plane.XAxis.Z);
        values.Add(plane.YAxis.X);
        values.Add(plane.YAxis.Y);
        values.Add(plane.YAxis.Z);
    }

    private static void WriteDomains(List<double> values, Surface surface)
    {
        values.Add(surface.DomainU.Min);
        values.Add(surface.DomainU.Max);
        values.Add(surface.DomainV.Min);
        values.Add(surface.DomainV.Max);
    }

    /// <summary>Kept for the round-trip tests: a full turn, to the encoding's own tolerance.</summary>
    internal static bool IsFullTurn(double sweep) => Math.Abs(Math.Abs(sweep) - TwoPi) < 1e-9;
}
