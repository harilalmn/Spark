using System;
using System.Collections.Generic;
using Clipper2Lib;

namespace Spark.Geometry.Planar;

/// <summary>
/// The one file in the kernel that touches Clipper2 (<c>E2-T13</c>, PRD FR-60).
/// </summary>
/// <remarks>
/// <para>
/// <b>Isolated on purpose.</b> Clipper2 is the only third-party dependency <c>Spark.Geometry</c> is
/// allowed (the architecture test <c>SparkGeometryTakesNoThirdPartyDependencyBeyondClipper</c> holds
/// the ceiling), and keeping every use of it in one internal file keeps that dependency replaceable
/// and its surface reviewable: nothing public mentions a Clipper type.
/// </para>
/// <para>
/// <b>Loops in, loops out.</b> A region is a list of closed loops of <see cref="Point2d"/>, without the
/// closing point repeated. Normalising with even-odd fill turns whatever loops a caller drew into
/// Clipper's canonical form - outer boundaries one way round, holes the other, none overlapping - and
/// every boolean between canonical regions then runs with non-zero fill.
/// </para>
/// </remarks>
internal static class ClipperBridge
{
    internal enum Operation
    {
        Union,
        Intersection,
        Difference,
        SymmetricDifference,
    }

    /// <summary>Clipper's precision, in decimal places, for a linear tolerance: 1e-6 is six.</summary>
    internal static int Precision(double linear) =>
        Math.Clamp((int)Math.Round(-Math.Log10(linear)), 0, 8);

    /// <summary>A caller's loops in canonical form: nesting decides what is a hole.</summary>
    internal static Point2d[][] Normalise(IReadOnlyList<Point2d[]> loops, int precision) =>
        From(Clipper.Union(To(loops), [], FillRule.EvenOdd, precision));

    internal static Point2d[][] Boolean(Operation operation, IReadOnlyList<Point2d[]> first, IReadOnlyList<Point2d[]> second, int precision)
    {
        PathsD subject = To(first);
        PathsD clip = To(second);

        PathsD result = operation switch
        {
            Operation.Union => Clipper.Union(subject, clip, FillRule.NonZero, precision),
            Operation.Intersection => Clipper.Intersect(subject, clip, FillRule.NonZero, precision),
            Operation.Difference => Clipper.Difference(subject, clip, FillRule.NonZero, precision),
            _ => Clipper.Xor(subject, clip, FillRule.NonZero, precision),
        };

        return From(result);
    }

    /// <summary>The area enclosed, holes subtracted. Canonical outer loops count positive and holes negative.</summary>
    internal static double Area(IReadOnlyList<Point2d[]> loops) => Clipper.Area(To(loops));

    /// <summary>
    /// Whether a point is in the region or on its boundary. In canonical form loops do not overlap
    /// and holes sit inside outers, so a point is inside exactly when an odd number of loops enclose it.
    /// </summary>
    internal static bool Contains(IReadOnlyList<Point2d[]> loops, in Point2d point, int precision)
    {
        PointD probe = new(point.X, point.Y);
        int enclosing = 0;

        foreach (Point2d[] loop in loops)
        {
            PointInPolygonResult result = Clipper.PointInPolygon(probe, Path(loop), precision);

            if (result == PointInPolygonResult.IsOn)
            {
                return true;
            }

            if (result == PointInPolygonResult.IsInside)
            {
                enclosing++;
            }
        }

        return enclosing % 2 == 1;
    }

    private static PathD Path(Point2d[] loop)
    {
        PathD path = [];

        foreach (Point2d point in loop)
        {
            path.Add(new PointD(point.X, point.Y));
        }

        return path;
    }

    private static PathsD To(IReadOnlyList<Point2d[]> loops)
    {
        PathsD paths = [];

        foreach (Point2d[] loop in loops)
        {
            paths.Add(Path(loop));
        }

        return paths;
    }

    private static Point2d[][] From(PathsD paths)
    {
        Point2d[][] loops = new Point2d[paths.Count][];

        for (int i = 0; i < loops.Length; i++)
        {
            PathD path = paths[i];
            Point2d[] loop = new Point2d[path.Count];

            for (int j = 0; j < loop.Length; j++)
            {
                loop[j] = new Point2d(path[j].x, path[j].y);
            }

            loops[i] = loop;
        }

        return loops;
    }
}
