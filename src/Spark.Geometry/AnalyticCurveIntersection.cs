using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// Curve/curve intersection in closed form, for the curves that have one: lines, circles and arcs
/// (<c>E2-T11</c>, step A).
/// </summary>
/// <remarks>
/// <para>
/// <b>Exact where exact is possible, and the oracle for everything else.</b> The general intersector
/// (step B) subdivides and refines, and a refinement can only be trusted against an answer that
/// cannot be wrong. These are that answer, so they are written as arithmetic, not as a search.
/// </para>
/// <para>
/// <b>In three dimensions, not only in a plane.</b> Two lines may be skew, a line may pierce a
/// circle's plane on the circle, and two circles in crossing planes meet only where the line their
/// planes share crosses both. Every case is decided to the tolerance's linear distance, and a
/// parameter just outside a domain by less than that distance is taken as its end.
/// </para>
/// <para>
/// <b>Tangency is one point, not two.</b> A line grazing a circle, or two circles touching, is decided
/// by comparing distances to the tolerance before any square root is taken, so a near-tangent never
/// produces two points a rounding error apart.
/// </para>
/// </remarks>
internal static class AnalyticCurveIntersection
{
    private const double FullTurn = Math.PI * 2.0;

    /// <summary>Intersects two curves in closed form.</summary>
    /// <param name="a">The first curve.</param>
    /// <param name="b">The second curve.</param>
    /// <param name="tolerance">How close counts as meeting.</param>
    /// <returns>What they have in common, or null when the pair has no closed form here.</returns>
    internal static CurveIntersections? TryIntersect(Curve a, Curve b, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a is Line la && b is Line lb)
        {
            return LineLine(la, lb, tolerance);
        }

        if (a is Line line && Circular.From(b) is { } onB)
        {
            return LineCircular(line, onB, tolerance);
        }

        if (b is Line other && Circular.From(a) is { } onA)
        {
            return Swapped(LineCircular(other, onA, tolerance));
        }

        if (Circular.From(a) is { } first && Circular.From(b) is { } second)
        {
            return CircularCircular(first, second, tolerance);
        }

        return null;
    }

    private static CurveIntersections LineLine(Line a, Line b, in Tolerance tolerance)
    {
        double tol = tolerance.Linear;
        Point3d a0 = a.StartPoint;
        Point3d b0 = b.StartPoint;
        Vector3d da = a.EndPoint - a0;
        Vector3d db = b.EndPoint - b0;
        double aa = da.Dot(da);
        double bb = db.Dot(db);
        double slackA = tol / Math.Sqrt(aa);
        double slackB = tol / Math.Sqrt(bb);

        double OnB(double s) => Math.Clamp(((a0 + (da * s)) - b0).Dot(db) / bb, 0.0, 1.0);

        if (da.Cross(db).Length <= Math.Sqrt(aa * bb) * Math.Sin(tolerance.Angular.Radians))
        {
            // Parallel: they share a stretch only when they share a line.
            double s0 = (b0 - a0).Dot(da) / aa;

            if ((a0 + (da * s0)).DistanceTo(b0) > tol)
            {
                return CurveIntersections.None;
            }

            double s1 = (b.EndPoint - a0).Dot(da) / aa;
            double low = Math.Max(0.0, Math.Min(s0, s1));
            double high = Math.Min(1.0, Math.Max(s0, s1));

            if (high < low - slackA)
            {
                return CurveIntersections.None;
            }

            if (high - low <= slackA)
            {
                // End to end: a point, not a stretch.
                double s = Math.Clamp((low + high) * 0.5, 0.0, 1.0);
                return One(new CurveIntersectionPoint(a0 + (da * s), s, OnB(s)));
            }

            return new CurveIntersections(
                [],
                [new CurveOverlap(new Interval(low, high), new Interval(OnB(low), OnB(high)))]);
        }

        // The closest points of the two infinite lines, then whether both lie on the segments and
        // on each other.
        Vector3d r = a0 - b0;
        double ab = da.Dot(db);
        double ar = da.Dot(r);
        double br = db.Dot(r);
        double denominator = (aa * bb) - (ab * ab);
        double sA = ((ab * br) - (bb * ar)) / denominator;
        double tB = ((aa * br) - (ab * ar)) / denominator;

        if (sA < -slackA || sA > 1.0 + slackA || tB < -slackB || tB > 1.0 + slackB)
        {
            return CurveIntersections.None;
        }

        sA = Math.Clamp(sA, 0.0, 1.0);
        tB = Math.Clamp(tB, 0.0, 1.0);

        Point3d pa = a0 + (da * sA);
        Point3d pb = b0 + (db * tB);

        return pa.DistanceTo(pb) > tol
            ? CurveIntersections.None
            : One(new CurveIntersectionPoint(pa.Midpoint(pb), sA, tB));
    }

    private static CurveIntersections LineCircular(Line line, Circular circle, in Tolerance tolerance)
    {
        double tol = tolerance.Linear;
        Point3d p0 = line.StartPoint;
        Vector3d d = line.EndPoint - p0;
        double length = d.Length;
        Vector3d normal = circle.Plane.Normal;
        double dn = d.Dot(normal);
        double offset = (p0 - circle.Plane.Origin).Dot(normal);
        double radius = circle.Radius;

        List<CurveIntersectionPoint> found = [];

        if (Math.Abs(dn) > length * Math.Sin(tolerance.Angular.Radians))
        {
            // The line crosses the circle's plane once; the question is whether it does so on the circle.
            Add(-offset / dn);
        }
        else if (Math.Abs(offset) <= tol)
        {
            // The line lies in the plane: nearest approach to the centre, then zero, one or two points.
            Vector3d w = p0 - circle.Plane.Origin;
            double nearestAt = -w.Dot(d) / d.Dot(d);
            double distance = (p0 + (d * nearestAt)).DistanceTo(circle.Plane.Origin);

            if (Math.Abs(distance - radius) <= tol)
            {
                Add(nearestAt);
            }
            else if (distance < radius)
            {
                double half = Math.Sqrt((radius * radius) - (distance * distance)) / length;
                Add(nearestAt - half);
                Add(nearestAt + half);
            }
        }

        found.Sort((x, y) => x.ParameterA.CompareTo(y.ParameterA));

        return found.Count == 0 ? CurveIntersections.None : new CurveIntersections(found, []);

        void Add(double s)
        {
            double slack = tol / length;

            if (s < -slack || s > 1.0 + slack)
            {
                return;
            }

            s = Math.Clamp(s, 0.0, 1.0);
            Point3d p = p0 + (d * s);

            if (circle.DistanceTo(p) > tol || circle.ParameterAt(circle.AngleOf(p), tol / radius) is not double on)
            {
                return;
            }

            found.Add(new CurveIntersectionPoint(p, s, on));
        }
    }

    private static CurveIntersections CircularCircular(Circular a, Circular b, in Tolerance tolerance)
    {
        double tol = tolerance.Linear;
        Vector3d na = a.Plane.Normal;
        Vector3d nb = b.Plane.Normal;
        Point3d oa = a.Plane.Origin;
        Point3d ob = b.Plane.Origin;
        double ra = a.Radius;
        double rb = b.Radius;

        List<Point3d> candidates = [];

        if (na.Cross(nb).Length <= Math.Sin(tolerance.Angular.Radians))
        {
            double offset = (ob - oa).Dot(na);

            if (Math.Abs(offset) > tol)
            {
                return CurveIntersections.None;
            }

            // One plane: the classic two-circle construction along the line of centres.
            Vector3d between = (ob - oa) - (na * offset);
            double d = between.Length;

            if (d <= tol)
            {
                return Math.Abs(ra - rb) <= tol ? Coincident(a, b, tol) : CurveIntersections.None;
            }

            if (d > ra + rb + tol || d < Math.Abs(ra - rb) - tol)
            {
                return CurveIntersections.None;
            }

            Vector3d u = between * (1.0 / d);
            Vector3d v = na.Cross(u);

            if (Math.Abs(d - (ra + rb)) <= tol)
            {
                candidates.Add(oa + (u * ra));
            }
            else if (Math.Abs(d - Math.Abs(ra - rb)) <= tol)
            {
                // Touching inside: towards the smaller circle's centre when the first is the larger.
                candidates.Add(oa + (u * (ra >= rb ? ra : -ra)));
            }
            else
            {
                double x = ((d * d) + (ra * ra) - (rb * rb)) / (2.0 * d);
                double y = Math.Sqrt(Math.Max(0.0, (ra * ra) - (x * x)));
                candidates.Add(oa + (u * x) + (v * y));
                candidates.Add(oa + (u * x) - (v * y));
            }
        }
        else
        {
            // Crossing planes: the curves can only meet on the line both planes share, so find where
            // that line meets the first circle and keep what is also on the second.
            Vector3d direction = na.Cross(nb);
            double ha = na.Dot(oa - Point3d.Origin);
            double hb = nb.Dot(ob - Point3d.Origin);
            Point3d onBoth = Point3d.Origin + (((nb * ha) - (na * hb)).Cross(direction) * (1.0 / direction.LengthSquared));
            Vector3d unit = direction * (1.0 / direction.Length);

            double nearestAt = -(onBoth - oa).Dot(unit);
            Point3d nearest = onBoth + (unit * nearestAt);
            double distance = nearest.DistanceTo(oa);

            if (Math.Abs(distance - ra) <= tol)
            {
                candidates.Add(nearest);
            }
            else if (distance < ra)
            {
                double half = Math.Sqrt((ra * ra) - (distance * distance));
                candidates.Add(nearest - (unit * half));
                candidates.Add(nearest + (unit * half));
            }
        }

        List<CurveIntersectionPoint> found = [];

        foreach (Point3d p in candidates)
        {
            if (a.DistanceTo(p) > tol || b.DistanceTo(p) > tol)
            {
                continue;
            }

            if (a.ParameterAt(a.AngleOf(p), tol / ra) is not double onA
                || b.ParameterAt(b.AngleOf(p), tol / rb) is not double onB)
            {
                continue;
            }

            found.Add(new CurveIntersectionPoint(p, onA, onB));
        }

        found.Sort((x, y) => x.ParameterA.CompareTo(y.ParameterA));

        return found.Count == 0 ? CurveIntersections.None : new CurveIntersections(found, []);
    }

    /// <summary>Two circular curves on the same circle: the stretch of arc they share.</summary>
    private static CurveIntersections Coincident(Circular a, Circular b, double tol)
    {
        if (a.Full && b.Full)
        {
            return new CurveIntersections(
                [],
                [new CurveOverlap(new Interval(0.0, FullTurn), new Interval(0.0, FullTurn))]);
        }

        // The second curve's stretch, measured in the first curve's angles from the first's start.
        bool sameWay = a.Plane.Normal.Dot(b.Plane.Normal) > 0.0;
        double startOfB = a.AngleOf(b.PointAtAngle(sameWay ? b.Start : b.Start + b.Sweep));
        double from = Mod(startOfB - a.Start);
        double slack = tol / a.Radius;

        List<CurveIntersectionPoint> points = [];
        List<CurveOverlap> overlaps = [];

        foreach (double shift in new[] { 0.0, -FullTurn })
        {
            double low = Math.Max(0.0, from + shift);
            double high = Math.Min(a.Sweep, from + shift + b.Sweep);

            if (high < low - slack)
            {
                continue;
            }

            double? lowOnB = b.ParameterAt(b.AngleOf(a.PointAtAngle(a.Start + low)), tol / b.Radius);
            double? highOnB = b.ParameterAt(b.AngleOf(a.PointAtAngle(a.Start + high)), tol / b.Radius);

            if (lowOnB is not double bLow || highOnB is not double bHigh)
            {
                continue;
            }

            if (high - low <= slack)
            {
                double at = Math.Clamp((low + high) * 0.5, 0.0, a.Sweep);
                points.Add(new CurveIntersectionPoint(a.PointAtAngle(a.Start + at), a.ToParameter(at), bLow));
                continue;
            }

            overlaps.Add(new CurveOverlap(
                new Interval(a.ToParameter(low), a.ToParameter(high)),
                new Interval(bLow, bHigh)));
        }

        return points.Count == 0 && overlaps.Count == 0
            ? CurveIntersections.None
            : new CurveIntersections(points, overlaps);
    }

    private static CurveIntersections One(CurveIntersectionPoint point) => new([point], []);

    private static CurveIntersections Swapped(CurveIntersections result)
    {
        List<CurveIntersectionPoint> points = [];

        foreach (CurveIntersectionPoint point in result.Points)
        {
            points.Add(new CurveIntersectionPoint(point.Point, point.ParameterB, point.ParameterA));
        }

        points.Sort((x, y) => x.ParameterA.CompareTo(y.ParameterA));

        List<CurveOverlap> overlaps = [];

        foreach (CurveOverlap overlap in result.Overlaps)
        {
            overlaps.Add(new CurveOverlap(overlap.OnB, overlap.OnA));
        }

        return new CurveIntersections(points, overlaps);
    }

    private static double Mod(double angle) => angle - (FullTurn * Math.Floor(angle / FullTurn));

    /// <summary>A circle or an arc, as the numbers the arithmetic needs.</summary>
    /// <param name="Plane">The plane; its origin is the centre.</param>
    /// <param name="Radius">The radius.</param>
    /// <param name="Start">Where the curve starts, as an angle from the plane's x axis.</param>
    /// <param name="Sweep">How far round it goes.</param>
    /// <param name="Full">Whether it is a whole circle, whose parameter is the angle itself.</param>
    private readonly record struct Circular(Plane Plane, double Radius, double Start, double Sweep, bool Full)
    {
        internal static Circular? From(Curve curve) => curve switch
        {
            Circle circle => new Circular(circle.Plane, circle.Radius, 0.0, FullTurn, true),
            Arc arc => new Circular(arc.Plane, arc.Radius, arc.StartAngle.Radians, arc.SweepAngle.Radians, false),
            _ => null,
        };

        /// <summary>The angle of a point about the centre, in [0, 2π).</summary>
        internal double AngleOf(in Point3d point)
        {
            Vector3d v = point - Plane.Origin;
            double angle = Math.Atan2(v.Dot(Plane.YAxis), v.Dot(Plane.XAxis));

            return angle < 0.0 ? angle + FullTurn : angle;
        }

        /// <summary>The point at an angle about the centre.</summary>
        internal Point3d PointAtAngle(double angle) =>
            Plane.Origin + (Plane.XAxis * (Radius * Math.Cos(angle))) + (Plane.YAxis * (Radius * Math.Sin(angle)));

        /// <summary>The curve parameter for an angle already measured from <see cref="Start"/>.</summary>
        internal double ToParameter(double fromStart) => Full ? Mod(Start + fromStart) : fromStart;

        /// <summary>
        /// The curve's parameter at an angle, or null when the curve does not reach it. An arc takes a
        /// point just past either end, by less than <paramref name="slack"/>, as that end.
        /// </summary>
        internal double? ParameterAt(double angle, double slack)
        {
            if (Full)
            {
                return angle;
            }

            double along = Mod(angle - Start);

            if (along <= Sweep + slack)
            {
                return Math.Min(along, Sweep);
            }

            return along >= FullTurn - slack ? 0.0 : null;
        }

        /// <summary>How far a point is from the whole circle this curve lies on.</summary>
        internal double DistanceTo(in Point3d point)
        {
            Vector3d v = point - Plane.Origin;
            double height = v.Dot(Plane.Normal);
            double inPlane = Math.Sqrt(Math.Max(0.0, v.Dot(v) - (height * height)));

            return Math.Sqrt(((inPlane - Radius) * (inPlane - Radius)) + (height * height));
        }
    }
}
