using System;
using System.Collections.Generic;
using System.Globalization;
using Spark.Geometry;

namespace Spark.Api;

/// <summary>
/// The kernel a session has when no provider is installed: it can do nothing, and says so.
/// </summary>
/// <remarks>
/// <para>
/// <b>A null object rather than a null reference, and the difference is what a user sees.</b> With
/// no kernel at all, every call site would need a null check and the ones that forgot would throw a
/// <see cref="NullReferenceException"/> from inside an evaluation. With this, the node library sees
/// <see cref="BrepCapabilities.None"/>, greys every solid operation out, and a graph that reaches
/// one anyway gets a sentence naming what is missing and what to do about it.
/// </para>
/// <para>
/// <b>It is also the thing that keeps <c>Spark.Geometry</c> honest.</b>
/// [ADR-0021](../../docs/adr/0021-brep-kernel-residency.md) requires the geometry layer to remain
/// useful with no native component present — M1's demoable is `spark` writing an OBJ with no
/// provider anywhere — and a seam whose only implementation was the provider would make that
/// impossible to test.
/// </para>
/// </remarks>
public sealed class UnavailableBrepKernel : IBrepKernel
{
    /// <summary>The one instance. It has no state.</summary>
    public static UnavailableBrepKernel Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "none";

    /// <inheritdoc/>
    public BrepCapabilities Capabilities => BrepCapabilities.None;

    /// <inheritdoc/>
    public KernelResult<Brep> Union(Brep first, Brep second, in Tolerance tolerance) => Refuse<Brep>("union");

    /// <inheritdoc/>
    public KernelResult<Brep> Difference(Brep first, Brep second, in Tolerance tolerance) => Refuse<Brep>("difference");

    /// <inheritdoc/>
    public KernelResult<Brep> Intersection(Brep first, Brep second, in Tolerance tolerance) => Refuse<Brep>("intersection");

    /// <inheritdoc/>
    public KernelResult<Brep> Extrude(Curve profile, in Vector3d direction, bool cap, in Tolerance tolerance) =>
        Refuse<Brep>("extrude");

    /// <inheritdoc/>
    public KernelResult<Brep> Revolve(
        Curve profile, in Point3d axisOrigin, in Vector3d axisDirection, Angle angle, in Tolerance tolerance) =>
        Refuse<Brep>("revolve");

    /// <inheritdoc/>
    public KernelResult<Brep> Loft(IReadOnlyList<Curve> profiles, bool closed, in Tolerance tolerance) =>
        Refuse<Brep>("loft");

    /// <inheritdoc/>
    public KernelResult<Brep> Fillet(Brep solid, IReadOnlyList<int> edges, double radius, in Tolerance tolerance) =>
        Refuse<Brep>("fillet");

    /// <inheritdoc/>
    public KernelResult<Brep> Chamfer(Brep solid, IReadOnlyList<int> edges, double distance, in Tolerance tolerance) =>
        Refuse<Brep>("chamfer");

    /// <inheritdoc/>
    public KernelResult<Brep> Shell(
        Brep solid, IReadOnlyList<int> facesToOpen, double thickness, in Tolerance tolerance) =>
        Refuse<Brep>("shell");

    /// <inheritdoc/>
    public KernelResult<Brep> Sew(IReadOnlyList<Brep> pieces, in Tolerance tolerance) => Refuse<Brep>("sew");

    /// <inheritdoc/>
    public KernelResult<Brep> Heal(Brep shape, in Tolerance tolerance) => Refuse<Brep>("heal");

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>An untrimmed shape is tessellated without a provider, and that is not a special case
    /// sneaking through.</b> A face whose only loop is its surface's own boundary <i>is</i> a
    /// surface, and tessellating a surface is `E2-T26`'s work, which is in front of the seam. What
    /// needs a provider is a *trimmed* face, and that is what this refuses.
    /// </para>
    /// <para>
    /// <b>Each face is tessellated and flipped on its own.</b> A face may be the reverse of the
    /// surface it sits on — that is how one surface serves two walls — and a box has exactly one
    /// such face. Flipping the finished mesh instead, which is shorter and was the first attempt,
    /// turns the other five over as well and produces a solid whose volume comes out negative.
    /// </para>
    /// </remarks>
    public KernelResult<Mesh> Tessellate(Brep shape, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(shape);

        if (!shape.IsUntrimmed)
        {
            return Refuse<Mesh>("tessellate a trimmed shape");
        }

        List<Point3d> vertices = [];
        List<Vector3d> normals = [];
        List<UV> textures = [];
        List<MeshFace> faces = [];

        for (int index = 0; index < shape.FaceCount; index++)
        {
            BrepFaceView view = shape.Face(index);

            MeshBuilder builder = new();
            Tessellation.Tessellate(view.Surface, builder, tolerance);

            Mesh piece = builder.Build();
            int offset = vertices.Count;
            bool flip = view.IsReversed;

            vertices.AddRange(piece.Vertices());
            textures.AddRange(piece.TextureCoordinates()!);

            foreach (Vector3d normal in piece.Normals()!)
            {
                normals.Add(flip ? -normal : normal);
            }

            foreach (MeshFace face in piece.Faces())
            {
                faces.Add(Moved(face, offset, flip));
            }
        }

        return KernelResult<Mesh>.Success(new Mesh(vertices, faces, normals, textures, colours: null));
    }

    /// <summary>One face, shifted into the combined mesh and reversed if its own face was.</summary>
    private static MeshFace Moved(in MeshFace face, int offset, bool flip)
    {
        if (face.IsQuad)
        {
            return flip
                ? new MeshFace(face.D + offset, face.C + offset, face.B + offset, face.A + offset)
                : new MeshFace(face.A + offset, face.B + offset, face.C + offset, face.D + offset);
        }

        return flip
            ? new MeshFace(face.C + offset, face.B + offset, face.A + offset)
            : new MeshFace(face.A + offset, face.B + offset, face.C + offset);
    }

    private static KernelResult<T> Refuse<T>(string operation) =>
        KernelResult<T>.Failure(new SparkDiagnostic(
            DiagnosticSeverity.Error,
            KernelDiagnostics.Unavailable,
            string.Create(
                CultureInfo.InvariantCulture,
                $"No solid-modelling kernel is installed, so this build cannot {operation}."),
            detail: "Exact solid operations need a kernel provider. Spark's geometry, curves, "
                + "surfaces, meshes and every file format work without one; booleans, fillets and "
                + "the rest do not.",
            helpTopicId: KernelDiagnostics.SolidsTopic));
}
