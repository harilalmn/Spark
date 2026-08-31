using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Geometry.Occt;

/// <summary>
/// The OpenCascade provider: exact booleans, sweeps, fillets, shelling and tessellation.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the assembly ADR-0020 bought.</b> Everything in front of it — points, curves,
/// surfaces, meshes, every file format — is Spark's own and works without it. Everything it adds
/// is the exact solid modelling that <c>DYNAMO-COVERAGE §6.1</c> showed 70 parity members cannot
/// exist without, and that no amount of managed code was going to produce on a schedule.
/// </para>
/// <para>
/// <b>A shape stays over there between operations.</b> Every method takes a <see cref="Brep"/>,
/// finds the provider shape it already is (<see cref="OcctResidency"/>) or imports it once, and
/// returns a <see cref="Brep"/> that is resident rather than read out. A chain of ten booleans
/// therefore crosses the ABI ten times and converts twice — in at the start, out when something
/// finally asks a structural question. That is ADR-0021, and it is the difference between a
/// kernel and a round-trip.
/// </para>
/// <para>
/// <b>Refusals are values.</b> Two solids that do not touch, a fillet radius that does not fit, a
/// loft between profiles that cannot be matched — the provider says so, and this turns that into
/// a <see cref="KernelResult{T}"/> carrying <see cref="KernelDiagnostics.Refused"/>. Only an
/// argument this layer should have caught becomes an exception.
/// </para>
/// </remarks>
public sealed class OcctBrepKernel : IBrepKernel
{
    /// <summary>Creates the provider. Use <see cref="OcctKernel.TryInstall"/> in an application.</summary>
    public OcctBrepKernel()
    {
        Version = NativeErrors.EngineVersion();
    }

    /// <summary>The OpenCascade version this provider is bound to.</summary>
    public string Version { get; }

    /// <inheritdoc/>
    public string Name => "opencascade";

    /// <inheritdoc/>
    /// <remarks>
    /// <b>What is absent is as deliberate as what is present.</b> <c>Sweep</c>, <c>Offset</c>,
    /// <c>Split</c>, <c>Step</c>, <c>Iges</c> and <c>MeshBoolean</c> are not claimed because the
    /// ABI has no entry point for them yet, and a capability flag is a promise the node library
    /// greys operations out on the strength of.
    /// </remarks>
    public BrepCapabilities Capabilities =>
        BrepCapabilities.Boolean
        | BrepCapabilities.Extrude
        | BrepCapabilities.Revolve
        | BrepCapabilities.Loft
        | BrepCapabilities.Fillet
        | BrepCapabilities.Chamfer
        | BrepCapabilities.Shell
        | BrepCapabilities.Split
        | BrepCapabilities.Offset
        | BrepCapabilities.Sew
        | BrepCapabilities.Heal
        | BrepCapabilities.Step
        | BrepCapabilities.Iges
        | BrepCapabilities.Tessellate;

    /// <inheritdoc/>
    public KernelResult<Brep> Union(Brep first, Brep second, in Tolerance tolerance) =>
        Boolean(NativeMethods.BooleanUnion, "union", first, second, tolerance);

    /// <inheritdoc/>
    public KernelResult<Brep> Difference(Brep first, Brep second, in Tolerance tolerance) =>
        Boolean(NativeMethods.BooleanDifference, "difference", first, second, tolerance);

    /// <inheritdoc/>
    public KernelResult<Brep> Intersection(Brep first, Brep second, in Tolerance tolerance) =>
        Boolean(NativeMethods.BooleanIntersection, "intersection", first, second, tolerance);

    /// <inheritdoc/>
    public KernelResult<Brep> Extrude(Curve profile, in Vector3d direction, bool cap, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(profile);

        double linear = tolerance.Linear;
        double[] along = [direction.X, direction.Y, direction.Z];

        return Sweep("extrude", [profile], (model, buffers) =>
        {
            ModelDesc[] holder = [model];
            int status = NativeMethods.spark_occt_extrude(
                buffers.Pin(holder), buffers.Pin(along), cap ? 1 : 0, linear, out IntPtr shape);

            return (status, status == NativeMethods.Ok ? shape : IntPtr.Zero);
        });
    }

    /// <inheritdoc/>
    public KernelResult<Brep> Revolve(
        Curve profile, in Point3d axisOrigin, in Vector3d axisDirection, Angle angle, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(profile);

        double linear = tolerance.Linear;
        double[] origin = [axisOrigin.X, axisOrigin.Y, axisOrigin.Z];
        double[] direction = [axisDirection.X, axisDirection.Y, axisDirection.Z];
        double radians = angle.Radians;

        return Sweep("revolve", [profile], (model, buffers) =>
        {
            ModelDesc[] holder = [model];
            int status = NativeMethods.spark_occt_revolve(
                buffers.Pin(holder),
                buffers.Pin(origin),
                buffers.Pin(direction),
                radians,
                linear,
                out IntPtr shape);

            return (status, status == NativeMethods.Ok ? shape : IntPtr.Zero);
        });
    }

    /// <inheritdoc/>
    public KernelResult<Brep> Loft(IReadOnlyList<Curve> profiles, bool closed, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        double linear = tolerance.Linear;

        return Sweep("loft", profiles, (model, buffers) =>
        {
            ModelDesc[] holder = [model];
            int status = NativeMethods.spark_occt_loft(
                buffers.Pin(holder), closed ? 1 : 0, linear, out IntPtr shape);

            return (status, status == NativeMethods.Ok ? shape : IntPtr.Zero);
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>The indices are the shape's own edge order</b>, which is the order a <see cref="Brep"/>
    /// that came out of this provider reports. A <see cref="Brep"/> built in managed code and
    /// imported here is re-sewn on the way in and may number its edges differently; the honest
    /// answer for that case is the empty list, which means every edge.
    /// </remarks>
    public KernelResult<Brep> Fillet(Brep solid, IReadOnlyList<int> edges, double radius, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(solid);
        ArgumentNullException.ThrowIfNull(edges);

        int[] chosen = [.. edges];
        double linear = tolerance.Linear;

        return Modify("fillet", solid, linear, (shape, buffers) =>
        {
            int status = NativeMethods.spark_occt_fillet(
                shape, buffers.Pin(chosen), chosen.Length, radius, out IntPtr result);

            return (status, status == NativeMethods.Ok ? result : IntPtr.Zero);
        });
    }

    /// <inheritdoc/>
    public KernelResult<Brep> Chamfer(Brep solid, IReadOnlyList<int> edges, double distance, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(solid);
        ArgumentNullException.ThrowIfNull(edges);

        int[] chosen = [.. edges];
        double linear = tolerance.Linear;

        return Modify("chamfer", solid, linear, (shape, buffers) =>
        {
            int status = NativeMethods.spark_occt_chamfer(
                shape, buffers.Pin(chosen), chosen.Length, distance, out IntPtr result);

            return (status, status == NativeMethods.Ok ? result : IntPtr.Zero);
        });
    }

    /// <inheritdoc/>
    public KernelResult<Brep> Shell(
        Brep solid, IReadOnlyList<int> facesToOpen, double thickness, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(solid);
        ArgumentNullException.ThrowIfNull(facesToOpen);

        int[] openings = [.. facesToOpen];
        double linear = tolerance.Linear;

        return Modify("shell", solid, linear, (shape, buffers) =>
        {
            int status = NativeMethods.spark_occt_shell(
                shape, buffers.Pin(openings), openings.Length, thickness, linear, out IntPtr result);

            return (status, status == NativeMethods.Ok ? result : IntPtr.Zero);
        });
    }

    /// <inheritdoc/>
    public KernelResult<IReadOnlyList<Brep>> Split(
        Brep shape, IReadOnlyList<Brep> tools, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(tools);

        if (tools.Count == 0)
        {
            return KernelResult<IReadOnlyList<Brep>>.Failure(
                Refusal("split", "There is nothing to cut with."));
        }

        double linear = tolerance.Linear;
        List<Borrowed> borrowed = [];

        try
        {
            using Borrowed subject = Borrow(shape, linear);

            if (subject.Problem is { } problem)
            {
                return KernelResult<IReadOnlyList<Brep>>.Failure(problem);
            }

            IntPtr[] handles = new IntPtr[tools.Count];

            for (int i = 0; i < tools.Count; i++)
            {
                ArgumentNullException.ThrowIfNull(tools[i]);

                Borrowed tool = Borrow(tools[i], linear);
                borrowed.Add(tool);

                if (tool.Problem is { } toolProblem)
                {
                    return KernelResult<IReadOnlyList<Brep>>.Failure(toolProblem);
                }

                handles[i] = tool.Shape!.Pointer;
            }

            using NativeBuffers buffers = new();
            int status = NativeMethods.spark_occt_split(
                subject.Shape!.Pointer, buffers.Pin(handles), handles.Length, linear, out IntPtr raw);

            if (status != NativeMethods.Ok || raw == IntPtr.Zero)
            {
                return KernelResult<IReadOnlyList<Brep>>.Failure(Diagnose(status, "split"));
            }

            using OcctShape result = OcctShape.Own(raw);

            return Pieces(result, "split");
        }
        finally
        {
            foreach (Borrowed tool in borrowed)
            {
                tool.Dispose();
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Built out of <see cref="Split"/> and a point test rather than out of a native entry
    /// point of its own.</b> The ABI is small on purpose, and *trim* is *split, then choose* — a
    /// seventh boolean-shaped entry point would repeat every argument split already handles, for
    /// the sake of one classification the provider can already do.
    /// </remarks>
    public KernelResult<Brep> Trim(
        Brep shape, IReadOnlyList<Brep> tools, in Point3d keep, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(tools);

        double linear = tolerance.Linear;
        double[] point = [keep.X, keep.Y, keep.Z];

        KernelResult<IReadOnlyList<Brep>> split = Split(shape, tools, tolerance);

        if (!split.TryGetValue(out IReadOnlyList<Brep>? pieces))
        {
            return KernelResult<Brep>.Failure(split.Diagnostic!);
        }

        foreach (Brep piece in pieces)
        {
            if (piece.Residency is not OcctResidency resident)
            {
                continue;
            }

            int[] inside = new int[1];

            using NativeBuffers buffers = new();
            int status = NativeMethods.spark_occt_shape_contains(
                resident.Shape.Pointer, buffers.Pin(point), linear, buffers.Pin(inside));

            if (status == NativeMethods.Ok && inside[0] != 0)
            {
                return KernelResult<Brep>.Success(piece);
            }
        }

        return KernelResult<Brep>.Failure(Refusal(
            "trim",
            "The cut produced "
            + pieces.Count.ToString(CultureInfo.InvariantCulture)
            + " piece(s) and the point to keep is in none of them."));
    }

    /// <inheritdoc/>
    public KernelResult<Brep> Offset(Brep shape, double distance, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(shape);

        double linear = tolerance.Linear;

        return Modify("offset", shape, linear, (native, buffers) =>
        {
            int status = NativeMethods.spark_occt_offset(native, distance, linear, out IntPtr result);

            return (status, status == NativeMethods.Ok ? result : IntPtr.Zero);
        });
    }

    /// <inheritdoc/>
    public KernelResult<Brep> Thicken(Brep sheet, double thickness, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        double linear = tolerance.Linear;

        return Modify("thicken", sheet, linear, (native, buffers) =>
        {
            int status = NativeMethods.spark_occt_thicken(native, thickness, linear, out IntPtr result);

            return (status, status == NativeMethods.Ok ? result : IntPtr.Zero);
        });
    }

    /// <inheritdoc/>
    public KernelResult<Brep> Sew(IReadOnlyList<Brep> pieces, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(pieces);

        if (pieces.Count == 0)
        {
            return Refused("sew", "There is nothing to sew.");
        }

        double linear = tolerance.Linear;
        List<Borrowed> borrowed = new(pieces.Count);

        try
        {
            IntPtr[] handles = new IntPtr[pieces.Count];

            for (int i = 0; i < pieces.Count; i++)
            {
                ArgumentNullException.ThrowIfNull(pieces[i]);

                Borrowed piece = Borrow(pieces[i], linear);
                borrowed.Add(piece);

                if (piece.Problem is { } problem)
                {
                    return KernelResult<Brep>.Failure(problem);
                }

                handles[i] = piece.Shape!.Pointer;
            }

            using NativeBuffers buffers = new();
            int status = NativeMethods.spark_occt_sew(
                buffers.Pin(handles), handles.Length, linear, out IntPtr result);

            return Wrap(status, result, "sew");
        }
        finally
        {
            foreach (Borrowed piece in borrowed)
            {
                piece.Dispose();
            }
        }
    }

    /// <inheritdoc/>
    public KernelResult<Brep> Heal(Brep shape, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(shape);

        double linear = tolerance.Linear;

        return Modify("heal", shape, linear, (native, buffers) =>
        {
            int status = NativeMethods.spark_occt_heal(native, linear, out IntPtr result);

            return (status, status == NativeMethods.Ok ? result : IntPtr.Zero);
        });
    }

    /// <inheritdoc/>
    public KernelResult<Brep> ReadFile(string path, in Tolerance tolerance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!TryFormat(path, out int format, out SparkDiagnostic? unknown))
        {
            return KernelResult<Brep>.Failure(unknown!);
        }

        int status = NativeMethods.spark_occt_read_file(
            format, path, tolerance.Linear, out IntPtr raw);

        return Wrap(status, raw, "read that file");
    }

    /// <inheritdoc/>
    public KernelResult<bool> WriteFile(Brep shape, string path, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!TryFormat(path, out int format, out SparkDiagnostic? unknown))
        {
            return KernelResult<bool>.Failure(unknown!);
        }

        using Borrowed borrowed = Borrow(shape, tolerance.Linear);

        if (borrowed.Problem is { } problem)
        {
            return KernelResult<bool>.Failure(problem);
        }

        int status = NativeMethods.spark_occt_write_file(format, borrowed.Shape!.Pointer, path);

        return status == NativeMethods.Ok
            ? KernelResult<bool>.Success(true)
            : KernelResult<bool>.Failure(Diagnose(status, "write that file"));
    }

    /// <summary>The interchange format a path names, or a diagnostic saying it names none.</summary>
    /// <remarks>
    /// <b>By extension, and the list is closed.</b> Sniffing the content would be cleverer and
    /// would make a mistyped extension silently succeed, which is a worse failure than the one it
    /// avoids: a user who typed `.stp` and got IGES has no way to find out.
    /// </remarks>
    private static bool TryFormat(string path, out int format, out SparkDiagnostic? problem)
    {
        string extension = Path.GetExtension(path);

        if (extension.Equals(".step", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".stp", StringComparison.OrdinalIgnoreCase))
        {
            format = NativeMethods.FormatStep;
            problem = null;

            return true;
        }

        if (extension.Equals(".iges", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".igs", StringComparison.OrdinalIgnoreCase))
        {
            format = NativeMethods.FormatIges;
            problem = null;

            return true;
        }

        format = -1;
        problem = Refusal(
            "use that file",
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{extension}' is not a solid-modelling interchange format this build knows. It reads and writes .step, .stp, .iges and .igs."));

        return false;
    }

    /// <inheritdoc/>
    public KernelResult<Mesh> Tessellate(Brep shape, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(shape);

        double linear = tolerance.Linear;
        double angular = tolerance.Angular.Radians;

        using Borrowed borrowed = Borrow(shape, linear);

        if (borrowed.Problem is { } problem)
        {
            return KernelResult<Mesh>.Failure(problem);
        }

        int status = NativeMethods.spark_occt_tessellate(
            borrowed.Shape!.Pointer, linear, angular, out IntPtr raw);

        if (status != NativeMethods.Ok)
        {
            return KernelResult<Mesh>.Failure(Diagnose(status, "tessellate"));
        }

        using OcctMesh mesh = OcctMesh.Own(raw);
        int[] sizes = new int[2];

        using (NativeBuffers sizing = new())
        {
            NativeMethods.spark_occt_mesh_sizes(mesh.Pointer, sizing.Pin(sizes));
        }

        double[] positions = new double[sizes[0] * 3];
        double[] normals = new double[sizes[0] * 3];
        int[] triangles = new int[sizes[1] * 3];

        using (NativeBuffers reading = new())
        {
            NativeMethods.spark_occt_mesh_read(
                mesh.Pointer, reading.Pin(positions), reading.Pin(normals), reading.Pin(triangles));
        }

        Point3d[] vertices = new Point3d[sizes[0]];
        Vector3d[] readNormals = new Vector3d[sizes[0]];

        for (int i = 0; i < sizes[0]; i++)
        {
            vertices[i] = new Point3d(positions[i * 3], positions[(i * 3) + 1], positions[(i * 3) + 2]);
            readNormals[i] = new Vector3d(normals[i * 3], normals[(i * 3) + 1], normals[(i * 3) + 2]);
        }

        MeshFace[] faces = new MeshFace[sizes[1]];

        for (int i = 0; i < sizes[1]; i++)
        {
            faces[i] = new MeshFace(triangles[i * 3], triangles[(i * 3) + 1], triangles[(i * 3) + 2]);
        }

        return KernelResult<Mesh>.Success(
            new Mesh(vertices, faces, readNormals, textureCoordinates: null, colours: null));
    }

    // --------------------------------------------------------------------------------------------
    // The shared shape
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// A provider shape for a <see cref="Brep"/> — borrowed when it is already resident, imported
    /// when it is not, and disposed only in the second case.
    /// </summary>
    private readonly struct Borrowed : IDisposable
    {
        private readonly bool _owned;

        public Borrowed(OcctShape? shape, bool owned, SparkDiagnostic? problem)
        {
            Shape = shape;
            Problem = problem;
            _owned = owned;
        }

        public OcctShape? Shape { get; }

        public SparkDiagnostic? Problem { get; }

        public void Dispose()
        {
            if (_owned)
            {
                Shape?.Dispose();
            }
        }
    }

    private static Borrowed Borrow(Brep shape, double tolerance)
    {
        // Already over there. This is the case that matters: it is what makes a chain of
        // operations one import rather than one per step.
        if (shape.Residency is OcctResidency resident && !resident.Shape.IsInvalid)
        {
            return new Borrowed(resident.Shape, owned: false, problem: null);
        }

        ModelWriter writer = ModelWriter.FromBrep(shape);

        using NativeBuffers buffers = new();
        ModelDesc[] holder = [writer.Pin(buffers)];

        int status = NativeMethods.spark_occt_import(buffers.Pin(holder), tolerance, out IntPtr raw);

        if (status != NativeMethods.Ok)
        {
            return new Borrowed(null, owned: false, problem: Diagnose(status, "import this shape"));
        }

        return new Borrowed(OcctShape.Own(raw), owned: true, problem: null);
    }

    private static KernelResult<Brep> Boolean(
        int operation, string name, Brep first, Brep second, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        double linear = tolerance.Linear;

        using Borrowed left = Borrow(first, linear);

        if (left.Problem is { } leftProblem)
        {
            return KernelResult<Brep>.Failure(leftProblem);
        }

        using Borrowed right = Borrow(second, linear);

        if (right.Problem is { } rightProblem)
        {
            return KernelResult<Brep>.Failure(rightProblem);
        }

        int status = NativeMethods.spark_occt_boolean(
            operation, left.Shape!.Pointer, right.Shape!.Pointer, linear, out IntPtr result);

        return Wrap(status, result, name);
    }

    private static KernelResult<Brep> Modify(
        string name, Brep solid, double tolerance, Func<IntPtr, NativeBuffers, (int Status, IntPtr Shape)> run)
    {
        using Borrowed borrowed = Borrow(solid, tolerance);

        if (borrowed.Problem is { } problem)
        {
            return KernelResult<Brep>.Failure(problem);
        }

        using NativeBuffers buffers = new();
        (int status, IntPtr result) = run(borrowed.Shape!.Pointer, buffers);

        return Wrap(status, result, name);
    }

    private static KernelResult<Brep> Sweep(
        string name,
        IReadOnlyList<Curve> profiles,
        Func<ModelDesc, NativeBuffers, (int Status, IntPtr Shape)> run)
    {
        ModelWriter writer = ModelWriter.FromCurves(profiles);

        using NativeBuffers buffers = new();
        (int status, IntPtr result) = run(writer.Pin(buffers), buffers);

        return Wrap(status, result, name);
    }

    private static KernelResult<Brep> Wrap(int status, IntPtr result, string operation)
    {
        if (status != NativeMethods.Ok || result == IntPtr.Zero)
        {
            return KernelResult<Brep>.Failure(Diagnose(status, operation));
        }

        return KernelResult<Brep>.Success(new Brep(new OcctResidency(OcctShape.Own(result))));
    }

    private static SparkDiagnostic Diagnose(int status, string operation) =>
        new(
            DiagnosticSeverity.Error,
            KernelDiagnostics.Refused,
            string.Create(CultureInfo.InvariantCulture, $"The kernel could not {operation}."),
            detail: NativeErrors.Describe(status, operation),
            helpTopicId: KernelDiagnostics.SolidsTopic);

    /// <summary>Every top-level piece of a shape, each resident in its own right.</summary>
    private static KernelResult<IReadOnlyList<Brep>> Pieces(OcctShape shape, string operation)
    {
        int[] count = new int[1];

        using (NativeBuffers buffers = new())
        {
            int status = NativeMethods.spark_occt_shape_part_count(shape.Pointer, buffers.Pin(count));

            if (status != NativeMethods.Ok)
            {
                return KernelResult<IReadOnlyList<Brep>>.Failure(Diagnose(status, operation));
            }
        }

        List<Brep> pieces = new(count[0]);

        for (int i = 0; i < count[0]; i++)
        {
            int status = NativeMethods.spark_occt_shape_part(shape.Pointer, i, out IntPtr raw);

            if (status != NativeMethods.Ok || raw == IntPtr.Zero)
            {
                return KernelResult<IReadOnlyList<Brep>>.Failure(Diagnose(status, operation));
            }

            pieces.Add(new Brep(new OcctResidency(OcctShape.Own(raw))));
        }

        return KernelResult<IReadOnlyList<Brep>>.Success(pieces);
    }

    private static SparkDiagnostic Refusal(string operation, string detail) =>
        new(
            DiagnosticSeverity.Error,
            KernelDiagnostics.Refused,
            string.Create(CultureInfo.InvariantCulture, $"The kernel could not {operation}."),
            detail: detail,
            helpTopicId: KernelDiagnostics.SolidsTopic);

    private static KernelResult<Brep> Refused(string operation, string detail) =>
        KernelResult<Brep>.Failure(new SparkDiagnostic(
            DiagnosticSeverity.Error,
            KernelDiagnostics.Refused,
            string.Create(CultureInfo.InvariantCulture, $"The kernel could not {operation}."),
            detail: detail,
            helpTopicId: KernelDiagnostics.SolidsTopic));
}
