using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// Offsetting a curve, and filleting between two.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are separated from <see cref="Curve"/> because their honest signatures do not fit on
/// it.</b> An offset needs a plane to be offset in — <i>offset by 5</i> is meaningless in three
/// dimensions without one — and a fillet is a relationship between two curves rather than a
/// property of either. Putting them on the base class would mean either a wrong signature or a
/// method most curves throw from.
/// </para>
/// <para>
/// <b>The offset of a NURBS curve is not a NURBS curve.</b> That is a fact about the mathematics,
/// not a limitation here: the offset of a polynomial curve is generally not polynomial, so an exact
/// answer does not exist in the representation. <see cref="Offset"/> therefore takes a
/// <see cref="Tolerance"/> and returns an approximation good to it, built on
/// <see cref="NurbsCurve.FitPoints"/> — and the shapes that <i>do</i> offset exactly, lines and
/// circles and arcs, are recognised and answered exactly rather than fitted.
/// </para>
/// </remarks>
public static class CurveOffset
{
    /// <summary>
    /// How many points are sampled along a curve before an offset is fitted through them.
    /// </summary>
    /// <remarks>
    /// The samples are the data the fit sees, so this is the ceiling on how much shape an offset
    /// can reproduce, and the tolerance cannot be met below it however many control points are
    /// used. Generous, because sampling is cheap and refitting is not.
    /// </remarks>
    public const int OffsetSamples = 200;

    /// <summary>
    /// Offsets a curve within a plane, exactly where that is possible and to a tolerance otherwise.
    /// </summary>
    /// <param name="curve">The curve to offset.</param>
    /// <param name="distance">
    /// How far to move it. Positive offsets towards the left of the direction of travel, seen from
    /// the <paramref name="normal"/> side; negative offsets the other way.
    /// </param>
    /// <param name="normal">The plane normal the offset happens in. Need not be unit length.</param>
    /// <param name="tolerance">How closely an approximated offset must follow the true one.</param>
    /// <returns>The offset curve, and whether it is exact rather than fitted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="curve"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="distance"/> is not finite.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="normal"/> has no length, or the curve does not lie in a plane with that
    /// normal.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>The sign convention is stated because every offset API gets asked about it.</b> Positive
    /// is towards the left of travel when the normal points at you, which is the same handedness
    /// the rest of this assembly uses for rotation.
    /// </para>
    /// <para>
    /// <b>An offset can self-intersect and this does not repair it.</b> Offsetting a curve inwards
    /// by more than its smallest radius of curvature produces loops, and the honest answer to that
    /// is trimming those loops away — which needs curve-curve intersection, and is not built.
    /// The result is the true offset locus, loops included; a caller who offsets a wiggly curve a
    /// long way gets exactly what the mathematics says and should expect it.
    /// </para>
    /// </remarks>
    public static (Curve Curve, bool Exact) Offset(
        Curve curve, double distance, in Vector3d normal, in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(curve);

        if (!double.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance), distance, "An offset distance must be finite.");
        }

        if (!normal.TryNormalise(out Vector3d unit))
        {
            throw new ArgumentException(
                "An offset needs a plane to happen in, and the normal given has no length. "
                + "In three dimensions 'offset by 5' does not name a curve on its own.",
                nameof(normal));
        }

        // The shapes whose offset is the same kind of shape. Answering these exactly matters more
        // than it looks: a fitted circle is a circle to within a tolerance, and everything
        // downstream that asks "is this an arc?" would start saying no.
        switch (curve)
        {
            case Line line:
                {
                    Vector3d along = line.EndPoint - line.StartPoint;
                    Vector3d sideways = unit.Cross(along).Normalised() * distance;

                    return (new Line(line.StartPoint + sideways, line.EndPoint + sideways), true);
                }

            case Circle circle when circle.Plane.Normal.IsParallelTo(unit):
                {
                    double radius = circle.Plane.Normal.Dot(unit) > 0 ? circle.Radius - distance : circle.Radius + distance;

                    if (radius > 0)
                    {
                        return (Circle.ByPlaneRadius(circle.Plane, radius), true);
                    }

                    break;
                }

            case Arc arc when arc.Plane.Normal.IsParallelTo(unit):
                {
                    double radius = arc.Plane.Normal.Dot(unit) > 0 ? arc.Radius - distance : arc.Radius + distance;

                    if (radius > 0)
                    {
                        return (Arc.ByPlaneRadiusAngles(arc.Plane, radius, arc.StartAngle, arc.SweepAngle), true);
                    }

                    break;
                }

            default:
                break;
        }

        return (FitOffset(curve, distance, unit, tolerance), false);
    }

    /// <summary>
    /// Samples the true offset locus and fits a curve through it.
    /// </summary>
    /// <remarks>
    /// The offset of a point on a curve is that point moved perpendicular to the tangent, within
    /// the plane — which is <c>normal × tangent</c>, already unit because both of those are. The
    /// locus is exact at every sample and the approximation is entirely in what happens between
    /// them, which is what the tolerance governs.
    /// </remarks>
    private static Curve FitOffset(
        Curve curve, double distance, in Vector3d normal, in Tolerance tolerance)
    {
        Interval domain = curve.Domain;
        List<Point3d> offsetPoints = new(OffsetSamples + 1);

        for (int i = 0; i <= OffsetSamples; i++)
        {
            double t = domain.Min + (domain.Length * i / OffsetSamples);
            Vector3d sideways = normal.Cross(curve.TangentAt(t));

            if (!sideways.TryNormalise(out Vector3d unitSideways))
            {
                throw new ArgumentException(
                    $"The curve's tangent at {t.ToString("R", CultureInfo.InvariantCulture)} is "
                    + "parallel to the offset normal, so it does not lie in that plane. An offset "
                    + "needs a curve and a plane that contain each other.",
                    nameof(curve));
            }

            Point3d moved = curve.PointAt(t) + (unitSideways * distance);

            // Two identical consecutive samples would give one parameter two positions and the fit
            // would be singular. A curve that stalls is a curve with a cusp in it.
            if (offsetPoints.Count == 0 || !offsetPoints[^1].EqualsWithin(moved, tolerance))
            {
                offsetPoints.Add(moved);
            }
        }

        if (offsetPoints.Count < 3)
        {
            throw new ArgumentException(
                "The offset collapsed to fewer than three distinct points, which is not a curve. "
                + "That happens when the offset distance cancels the curve's own extent.",
                nameof(curve));
        }

        return NurbsCurve.FitPoints(offsetPoints, tolerance).Curve;
    }

    /// <summary>
    /// The arc of a given radius tangent to two lines, and the two lines trimmed back to meet it.
    /// </summary>
    /// <param name="first">The line the fillet leaves.</param>
    /// <param name="second">The line it arrives at.</param>
    /// <param name="radius">The fillet radius. Positive.</param>
    /// <returns>The arc, and the two trimmed lines, in order.</returns>
    /// <exception cref="ArgumentNullException">Either line is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is not positive and finite.</exception>
    /// <exception cref="ArgumentException">
    /// The lines are parallel, do not meet, or are too short for a fillet of that radius.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Two lines only, and that is a deliberate stopping point rather than an oversight.</b> A
    /// fillet between two general curves is a tangency problem solved by iteration, and it needs
    /// curve-curve intersection to know where the corner is at all — neither of which exists yet.
    /// Two straight edges meeting at a corner is the overwhelmingly common case and it has a
    /// closed-form answer; approximating the general case now would produce something that looks
    /// like the feature and is not.
    /// </para>
    /// <para>
    /// The lines are returned trimmed because a fillet that leaves the original corner in place is
    /// not what anybody asked for — the caller wants three curves that join, and joining is what
    /// the operation is for.
    /// </para>
    /// </remarks>
    public static (Arc Fillet, Line First, Line Second) FilletLines(
        Line first, Line second, double radius)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (!double.IsFinite(radius) || radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius), radius, "A fillet radius must be positive and finite.");
        }

        // The corner is where the two lines meet. Both are taken as rays from it, which is what
        // makes the arithmetic below independent of which end of each line was drawn first.
        if (!TryCorner(first, second, out Point3d corner, out Vector3d awayFromFirst, out Vector3d towardsSecond))
        {
            throw new ArgumentException(
                "The two lines do not meet at a corner, so there is nothing to fillet. They are "
                + "parallel, skew, or share no endpoint.",
                nameof(second));
        }

        double half = awayFromFirst.AngleTo(towardsSecond).Radians / 2.0;

        if (half <= 0.0 || half >= Math.PI / 2.0)
        {
            throw new ArgumentException(
                "The two lines are collinear, so the corner has no angle to fillet.", nameof(second));
        }

        // How far back along each line the arc's tangent points sit.
        double setback = radius / Math.Tan(half);

        if (setback > first.Length || setback > second.Length)
        {
            throw new ArgumentException(
                $"A fillet of radius {radius.ToString("R", CultureInfo.InvariantCulture)} needs "
                + $"{setback.ToString("G6", CultureInfo.InvariantCulture)} of each line to work with, "
                + "and one of them is shorter than that. Use a smaller radius or longer lines.",
                nameof(radius));
        }

        Point3d tangentOnFirst = corner + (awayFromFirst * setback);
        Point3d tangentOnSecond = corner + (towardsSecond * setback);

        // The centre lies along the bisector, at the distance that puts it `radius` from both.
        Vector3d bisector = (awayFromFirst + towardsSecond).Normalised();
        Point3d centre = corner + (bisector * (radius / Math.Sin(half)));

        Arc fillet = Arc.ByThreePoints(tangentOnFirst, MidArcPoint(centre, tangentOnFirst, tangentOnSecond, radius), tangentOnSecond);

        return (
            fillet,
            new Line(FarEnd(first, corner), tangentOnFirst),
            new Line(tangentOnSecond, FarEnd(second, corner)));
    }

    /// <summary>The point halfway round the fillet, which is what pins the arc's direction.</summary>
    private static Point3d MidArcPoint(
        in Point3d centre, in Point3d from, in Point3d to, double radius)
    {
        Vector3d towardsMiddle = ((from - centre).Normalised() + (to - centre).Normalised()).Normalised();

        return centre + (towardsMiddle * radius);
    }

    /// <summary>Whichever end of a line is not the corner.</summary>
    private static Point3d FarEnd(Line line, in Point3d corner) =>
        line.StartPoint.EqualsWithin(corner) ? line.EndPoint : line.StartPoint;

    /// <summary>
    /// Finds the corner two lines share and the unit direction away from it along each.
    /// </summary>
    private static bool TryCorner(
        Line first, Line second, out Point3d corner, out Vector3d awayFromFirst, out Vector3d towardsSecond)
    {
        corner = default;
        awayFromFirst = default;
        towardsSecond = default;

        // The shared endpoint, in whichever of the four arrangements it appears.
        (Point3d Corner, Point3d AlongFirst, Point3d AlongSecond)[] arrangements =
        [
            (first.EndPoint, first.StartPoint, second.EndPoint),
            (first.EndPoint, first.StartPoint, second.StartPoint),
            (first.StartPoint, first.EndPoint, second.EndPoint),
            (first.StartPoint, first.EndPoint, second.StartPoint),
        ];

        foreach ((Point3d shared, Point3d alongFirst, Point3d alongSecond) in arrangements)
        {
            bool secondTouches = second.StartPoint.EqualsWithin(shared) || second.EndPoint.EqualsWithin(shared);

            if (!secondTouches || alongSecond.EqualsWithin(shared))
            {
                continue;
            }

            if (!(alongFirst - shared).TryNormalise(out Vector3d a)
                || !(alongSecond - shared).TryNormalise(out Vector3d b))
            {
                continue;
            }

            corner = shared;
            awayFromFirst = a;
            towardsSecond = b;
            return true;
        }

        return false;
    }
}
