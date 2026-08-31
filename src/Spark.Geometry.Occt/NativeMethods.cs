using System;
using System.Runtime.InteropServices;

namespace Spark.Geometry.Occt;

/// <summary>
/// The `spark_occt` C ABI, declared once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every declaration here mirrors a line in <c>native/spark_occt/include/spark_occt.h</c>,</b>
/// and the two files are the same decision written twice, which is the cost of a hand-written
/// binding and the reason ADR-0020 kept the surface small. <see cref="AbiVersion"/> is checked
/// once at load so that a mismatch is a sentence rather than a stack corruption.
/// </para>
/// <para>
/// <b>Pointers are <see cref="IntPtr"/> rather than typed spans on purpose.</b> The caller pins
/// what it owns and passes the address, so there is exactly one place in this assembly that
/// decides how long a buffer must live — <see cref="NativeBuffers"/> — instead of that decision
/// being implicit in a marshaller's behaviour at each of thirty call sites.
/// </para>
/// </remarks>
internal static partial class NativeMethods
{
    /// <summary>The library name, resolved by the default probing rules plus our own resolver.</summary>
    internal const string Library = "spark_occt";

    /// <summary>The ABI this binding was written against. Must match the library's own.</summary>
    internal const int AbiVersion = 3;

    internal const int Ok = 0;
    internal const int ErrorArgument = 1;
    internal const int ErrorRefused = 2;
    internal const int ErrorUnsupported = 3;
    internal const int ErrorException = 4;

    internal const int BooleanUnion = 0;
    internal const int BooleanDifference = 1;
    internal const int BooleanIntersection = 2;

    internal const int CurveLine = 1;
    internal const int CurveCircle = 2;
    internal const int CurveArc = 3;
    internal const int CurveEllipse = 4;
    internal const int CurveNurbs = 5;

    internal const int SurfacePlane = 1;
    internal const int SurfaceCylinder = 2;
    internal const int SurfaceCone = 3;
    internal const int SurfaceSphere = 4;
    internal const int SurfaceTorus = 5;
    internal const int SurfaceNurbs = 6;

    internal const int FormatStep = 0;
    internal const int FormatIges = 1;

    internal const int LoopOuter = 0;
    internal const int LoopInner = 1;

    // Indices into the array spark_occt_model_sizes fills. The names match the header's macros.
    internal const int SizePoints = 0;
    internal const int SizeCurves = 1;
    internal const int SizeCurveInts = 2;
    internal const int SizeCurveDoubles = 3;
    internal const int SizeSurfaces = 4;
    internal const int SizeSurfaceInts = 5;
    internal const int SizeSurfaceDoubles = 6;
    internal const int SizeVertices = 7;
    internal const int SizeEdges = 8;
    internal const int SizeTrims = 9;
    internal const int SizeLoops = 10;
    internal const int SizeFaces = 11;
    internal const int SizeShells = 12;
    internal const int SizeCount = 16;

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_abi_version();

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_engine_version(IntPtr buffer, int capacity);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_last_error(IntPtr buffer, int capacity);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial void spark_occt_shape_release(IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial long spark_occt_shape_bytes(IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_shape_counts(IntPtr shape, IntPtr counts);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_shape_is_solid(IntPtr shape, IntPtr solid);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_make_box(
        IntPtr frame, double length, double width, double height, out IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_make_cylinder(
        IntPtr frame, double radius, double height, out IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_make_sphere(IntPtr frame, double radius, out IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_make_cone(
        IntPtr frame, double bottomRadius, double topRadius, double height, out IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_make_torus(
        IntPtr frame, double majorRadius, double minorRadius, out IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_import(IntPtr model, double tolerance, out IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_boolean(
        int operation, IntPtr first, IntPtr second, double tolerance, out IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_extrude(
        IntPtr profile, IntPtr direction, int cap, double tolerance, out IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_revolve(
        IntPtr profile,
        IntPtr axisOrigin,
        IntPtr axisDirection,
        double angle,
        double tolerance,
        out IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_loft(
        IntPtr profiles, int closed, double tolerance, out IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_fillet(
        IntPtr shape, IntPtr edges, int edgeCount, double radius, out IntPtr result);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_chamfer(
        IntPtr shape, IntPtr edges, int edgeCount, double distance, out IntPtr result);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_shell(
        IntPtr shape,
        IntPtr faces,
        int faceCount,
        double thickness,
        double tolerance,
        out IntPtr result);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_sew(
        IntPtr pieces, int count, double tolerance, out IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_heal(IntPtr shape, double tolerance, out IntPtr result);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_shape_contains(
        IntPtr shape, IntPtr point, double tolerance, IntPtr inside);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_shape_part_count(IntPtr shape, IntPtr count);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_shape_part(IntPtr shape, int index, out IntPtr part);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_split(
        IntPtr shape, IntPtr tools, int toolCount, double tolerance, out IntPtr result);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_offset(
        IntPtr shape, double distance, double tolerance, out IntPtr result);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_thicken(
        IntPtr shape, double thickness, double tolerance, out IntPtr result);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_write_file(int format, IntPtr shape, string path);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_read_file(
        int format, string path, double tolerance, out IntPtr shape);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_read(IntPtr shape, double tolerance, out IntPtr model);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial void spark_occt_model_release(IntPtr model);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_model_sizes(IntPtr model, IntPtr sizes);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_model_read(IntPtr model, IntPtr into);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_tessellate(
        IntPtr shape, double linear, double angular, out IntPtr mesh);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial void spark_occt_mesh_release(IntPtr mesh);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_mesh_sizes(IntPtr mesh, IntPtr sizes);

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial int spark_occt_mesh_read(
        IntPtr mesh, IntPtr positions, IntPtr normals, IntPtr triangles);
}

/// <summary>
/// The layout of <c>spark_model_desc</c>, field for field.
/// </summary>
/// <remarks>
/// <b>Sequential layout with the same field order as the header is the whole contract.</b> Each
/// count is an <see cref="int"/> followed by pointers, which on x64 pads exactly as the C struct
/// does. The round-trip test in <c>Spark.Geometry.Occt.Tests</c> is what actually proves it: an
/// off-by-one here would not be a compile error in either language.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct ModelDesc
{
    public int PointCount;
    public IntPtr Points;

    public int CurveCount;
    public IntPtr CurveKinds;
    public IntPtr CurveIntOffsets;
    public IntPtr CurveInts;
    public IntPtr CurveDoubleOffsets;
    public IntPtr CurveDoubles;

    public int SurfaceCount;
    public IntPtr SurfaceKinds;
    public IntPtr SurfaceIntOffsets;
    public IntPtr SurfaceInts;
    public IntPtr SurfaceDoubleOffsets;
    public IntPtr SurfaceDoubles;

    public int VertexCount;
    public IntPtr Vertices;

    public int EdgeCount;
    public IntPtr Edges;

    public int TrimCount;
    public IntPtr Trims;

    public int LoopCount;
    public IntPtr Loops;

    public int FaceCount;
    public IntPtr Faces;

    public int ShellCount;
    public IntPtr Shells;
}
