using System;
using System.Collections.Generic;

namespace Spark.Geometry.Occt;

/// <summary>
/// Turns the flat tables <c>spark_occt_read</c> produces back into Spark geometry.
/// </summary>
/// <remarks>
/// <b>The exact return trip of <see cref="ModelWriter"/>, and it has to be.</b> The two files are
/// one encoding written twice, in the same order, and the only thing that proves they still agree
/// is the round-trip test — a shape sent out and read back, compared face for face. An encoding
/// checked by inspection is an encoding that is wrong on the fourth decimal for a year.
/// </remarks>
internal static class ModelReader
{
    /// <summary>Reads a native model handle into the nine arrays a <see cref="Brep"/> is made of.</summary>
    public static BrepData Read(IntPtr model)
    {
        if (model == IntPtr.Zero)
        {
            throw new ArgumentException("There is no model to read.", nameof(model));
        }

        int[] sizes = new int[NativeMethods.SizeCount];

        using (NativeBuffers sizing = new())
        {
            int status = NativeMethods.spark_occt_model_sizes(model, sizing.Pin(sizes));

            if (status != NativeMethods.Ok)
            {
                throw new InvalidOperationException(NativeErrors.Describe(status, "Sizing the model"));
            }
        }

        double[] points = new double[sizes[NativeMethods.SizePoints] * 3];
        int[] curveKinds = new int[sizes[NativeMethods.SizeCurves]];
        int[] curveIntOffsets = new int[sizes[NativeMethods.SizeCurves] + 1];
        int[] curveInts = new int[sizes[NativeMethods.SizeCurveInts]];
        int[] curveDoubleOffsets = new int[sizes[NativeMethods.SizeCurves] + 1];
        double[] curveDoubles = new double[sizes[NativeMethods.SizeCurveDoubles]];
        int[] surfaceKinds = new int[sizes[NativeMethods.SizeSurfaces]];
        int[] surfaceIntOffsets = new int[sizes[NativeMethods.SizeSurfaces] + 1];
        int[] surfaceInts = new int[sizes[NativeMethods.SizeSurfaceInts]];
        int[] surfaceDoubleOffsets = new int[sizes[NativeMethods.SizeSurfaces] + 1];
        double[] surfaceDoubles = new double[sizes[NativeMethods.SizeSurfaceDoubles]];
        int[] vertices = new int[sizes[NativeMethods.SizeVertices]];
        int[] edges = new int[sizes[NativeMethods.SizeEdges] * 3];
        int[] trims = new int[sizes[NativeMethods.SizeTrims] * 2];
        int[] loops = new int[sizes[NativeMethods.SizeLoops] * 3];
        int[] faces = new int[sizes[NativeMethods.SizeFaces] * 4];
        int[] shells = new int[sizes[NativeMethods.SizeShells] * 2];

        using (NativeBuffers buffers = new())
        {
            ModelDesc desc = new()
            {
                Points = buffers.Pin(points),
                CurveKinds = buffers.Pin(curveKinds),
                CurveIntOffsets = buffers.Pin(curveIntOffsets),
                CurveInts = buffers.Pin(curveInts),
                CurveDoubleOffsets = buffers.Pin(curveDoubleOffsets),
                CurveDoubles = buffers.Pin(curveDoubles),
                SurfaceKinds = buffers.Pin(surfaceKinds),
                SurfaceIntOffsets = buffers.Pin(surfaceIntOffsets),
                SurfaceInts = buffers.Pin(surfaceInts),
                SurfaceDoubleOffsets = buffers.Pin(surfaceDoubleOffsets),
                SurfaceDoubles = buffers.Pin(surfaceDoubles),
                Vertices = buffers.Pin(vertices),
                Edges = buffers.Pin(edges),
                Trims = buffers.Pin(trims),
                Loops = buffers.Pin(loops),
                Faces = buffers.Pin(faces),
                Shells = buffers.Pin(shells),
            };

            ModelDesc[] holder = [desc];
            int status = NativeMethods.spark_occt_model_read(model, buffers.Pin(holder));

            if (status != NativeMethods.Ok)
            {
                throw new InvalidOperationException(NativeErrors.Describe(status, "Reading the model"));
            }
        }

        Point3d[] readPoints = new Point3d[sizes[NativeMethods.SizePoints]];

        for (int i = 0; i < readPoints.Length; i++)
        {
            readPoints[i] = new Point3d(points[i * 3], points[(i * 3) + 1], points[(i * 3) + 2]);
        }

        Curve[] readCurves = new Curve[curveKinds.Length];

        for (int i = 0; i < readCurves.Length; i++)
        {
            readCurves[i] = ReadCurve(
                curveKinds[i],
                curveInts.AsSpan(curveIntOffsets[i], curveIntOffsets[i + 1] - curveIntOffsets[i]),
                curveDoubles.AsSpan(
                    curveDoubleOffsets[i], curveDoubleOffsets[i + 1] - curveDoubleOffsets[i]));
        }

        Surface[] readSurfaces = new Surface[surfaceKinds.Length];

        for (int i = 0; i < readSurfaces.Length; i++)
        {
            readSurfaces[i] = ReadSurface(
                surfaceKinds[i],
                surfaceInts.AsSpan(surfaceIntOffsets[i], surfaceIntOffsets[i + 1] - surfaceIntOffsets[i]),
                surfaceDoubles.AsSpan(
                    surfaceDoubleOffsets[i], surfaceDoubleOffsets[i + 1] - surfaceDoubleOffsets[i]));
        }

        BrepVertex[] readVertices = new BrepVertex[vertices.Length];

        for (int i = 0; i < readVertices.Length; i++)
        {
            readVertices[i] = new BrepVertex(vertices[i]);
        }

        BrepEdge[] readEdges = new BrepEdge[sizes[NativeMethods.SizeEdges]];

        for (int i = 0; i < readEdges.Length; i++)
        {
            readEdges[i] = new BrepEdge(edges[i * 3], edges[(i * 3) + 1], edges[(i * 3) + 2]);
        }

        BrepTrim[] readTrims = new BrepTrim[sizes[NativeMethods.SizeTrims]];

        for (int i = 0; i < readTrims.Length; i++)
        {
            readTrims[i] = new BrepTrim(trims[i * 2], trims[(i * 2) + 1] != 0);
        }

        BrepLoop[] readLoops = new BrepLoop[sizes[NativeMethods.SizeLoops]];

        for (int i = 0; i < readLoops.Length; i++)
        {
            readLoops[i] = new BrepLoop(
                loops[i * 3],
                loops[(i * 3) + 1],
                loops[(i * 3) + 2] == NativeMethods.LoopInner ? BrepLoopKind.Inner : BrepLoopKind.Outer);
        }

        BrepFace[] readFaces = new BrepFace[sizes[NativeMethods.SizeFaces]];

        for (int i = 0; i < readFaces.Length; i++)
        {
            readFaces[i] = new BrepFace(
                faces[i * 4], faces[(i * 4) + 1], faces[(i * 4) + 2], faces[(i * 4) + 3] != 0);
        }

        BrepShell[] readShells = new BrepShell[sizes[NativeMethods.SizeShells]];

        for (int i = 0; i < readShells.Length; i++)
        {
            readShells[i] = new BrepShell(shells[i * 2], shells[(i * 2) + 1]);
        }

        return new BrepData(
            readPoints,
            readCurves,
            readSurfaces,
            readVertices,
            readEdges,
            readTrims,
            readLoops,
            readFaces,
            readShells);
    }

    // --------------------------------------------------------------------------------------------
    // Geometry
    // --------------------------------------------------------------------------------------------

    private static Plane Frame(ReadOnlySpan<double> values) =>
        Plane.ByOriginXAxisYAxis(
            new Point3d(values[0], values[1], values[2]),
            new Vector3d(values[3], values[4], values[5]),
            new Vector3d(values[6], values[7], values[8]));

    private static Curve ReadCurve(int kind, ReadOnlySpan<int> ints, ReadOnlySpan<double> values)
    {
        switch (kind)
        {
            case NativeMethods.CurveLine:
                return new Line(
                    new Point3d(values[0], values[1], values[2]),
                    new Point3d(values[3], values[4], values[5]));

            case NativeMethods.CurveCircle:
                return new Circle(Frame(values), values[9]);

            case NativeMethods.CurveArc:
                return Arc.ByPlaneRadiusAngles(
                    Frame(values),
                    values[9],
                    Angle.FromRadians(values[10]),
                    Angle.FromRadians(values[11]));

            case NativeMethods.CurveEllipse:
                return EllipseCurve.ByPlaneRadiiAngles(
                    Frame(values),
                    values[9],
                    values[10],
                    Angle.FromRadians(values[11]),
                    Angle.FromRadians(values[12]));

            case NativeMethods.CurveNurbs:
                {
                    int degree = ints[0];
                    int poles = ints[1];
                    int knotCount = ints[2];
                    bool rational = ints[3] != 0;
                    int stride = rational ? 4 : 3;

                    double[] knots = values[..knotCount].ToArray();
                    Point3d[] controlPoints = new Point3d[poles];
                    double[]? weights = rational ? new double[poles] : null;

                    for (int i = 0; i < poles; i++)
                    {
                        int at = knotCount + (i * stride);
                        controlPoints[i] = new Point3d(values[at], values[at + 1], values[at + 2]);

                        if (weights is not null)
                        {
                            weights[i] = values[at + 3];
                        }
                    }

                    return new NurbsCurve(degree, controlPoints, knots, weights);
                }

            default:
                throw new InvalidOperationException(
                    $"The provider sent curve kind {kind}, which this build does not know.");
        }
    }

    private static Surface ReadSurface(int kind, ReadOnlySpan<int> ints, ReadOnlySpan<double> values)
    {
        switch (kind)
        {
            case NativeMethods.SurfacePlane:
                return new PlaneSurface(
                    Frame(values), new Interval(values[9], values[10]), new Interval(values[11], values[12]));

            case NativeMethods.SurfaceCylinder:
                return new CylindricalSurface(
                    Frame(values),
                    values[9],
                    new Interval(values[10], values[11]),
                    new Interval(values[12], values[13]));

            case NativeMethods.SurfaceCone:
                return new ConicalSurface(
                    Frame(values),
                    values[9],
                    Angle.FromRadians(values[10]),
                    new Interval(values[11], values[12]),
                    new Interval(values[13], values[14]));

            case NativeMethods.SurfaceSphere:
                return new SphericalSurface(
                    Frame(values),
                    values[9],
                    new Interval(values[10], values[11]),
                    new Interval(values[12], values[13]));

            case NativeMethods.SurfaceTorus:
                return new ToroidalSurface(
                    Frame(values),
                    values[9],
                    values[10],
                    new Interval(values[11], values[12]),
                    new Interval(values[13], values[14]));

            case NativeMethods.SurfaceNurbs:
                {
                    int degreeU = ints[0];
                    int degreeV = ints[1];
                    int countU = ints[2];
                    int countV = ints[3];
                    int knotCountU = ints[4];
                    int knotCountV = ints[5];
                    bool rational = ints[6] != 0;
                    int stride = rational ? 4 : 3;

                    double[] knotsU = values[..knotCountU].ToArray();
                    double[] knotsV = values.Slice(knotCountU, knotCountV).ToArray();

                    Point3d[,] controlPoints = new Point3d[countU, countV];
                    double[,]? weights = rational ? new double[countU, countV] : null;
                    int at = knotCountU + knotCountV;

                    for (int i = 0; i < countU; i++)
                    {
                        for (int j = 0; j < countV; j++)
                        {
                            controlPoints[i, j] = new Point3d(values[at], values[at + 1], values[at + 2]);

                            if (weights is not null)
                            {
                                weights[i, j] = values[at + 3];
                            }

                            at += stride;
                        }
                    }

                    return new NurbsSurface(
                        new KnotVector(degreeU, knotsU), new KnotVector(degreeV, knotsV), controlPoints, weights);
                }

            default:
                throw new InvalidOperationException(
                    $"The provider sent surface kind {kind}, which this build does not know.");
        }
    }

    /// <summary>The domain a Spark surface reports, as the ABI writes it.</summary>
    internal static IReadOnlyList<double> DomainOf(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        return [surface.DomainU.Min, surface.DomainU.Max, surface.DomainV.Min, surface.DomainV.Max];
    }
}
