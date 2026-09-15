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

                    // A LINE ALONG THE NORMAL HAS NO OFFSET IN THIS PLANE, and until 2026-09-15 it
                    // reported that as `InvalidOperationException: a zero-length vector cannot be
                    // normalised` from inside Normalised() - the same condition FitOffset reports
                    // as an ArgumentException naming the curve. One caller cannot catch both, and
                    // the message from the fast path described the arithmetic rather than the
                    // mistake.
                    if (!unit.Cross(along).TryNormalise(out Vector3d perpendicular))
                    {
                        throw new ArgumentException(
                            "This line runs along the offset normal, so it does not lie in that "
                            + "plane and has no offset in it. An offset needs a curve and a plane "
                            + "that contain each other.",
                            nameof(curve));
                    }

                    Vector3d sideways = perpendicular * distance;

                    return (new Line(line.StartPoint + sideways, line.EndPoint + sideways), true);
                }

            case Circle circle when circle.Plane.Normal.IsParallelTo(unit):
                {
                    double radius = circle.Plane.Normal.Dot(unit) > 0 ? circle.Radius - distance : circle.Radius + distance;

                    if (radius > 0)
                    {
                        return (Circle.FromPlaneRadius(circle.Plane, radius), true);
                    }

                    break;
                }

            case Arc arc when arc.Plane.Normal.IsParallelTo(unit):
                {
                    double radius = arc.Plane.Normal.Dot(unit) > 0 ? arc.Radius - distance : arc.Radius + distance;

                    if (radius > 0)
                    {
                        return (Arc.FromPlaneRadiusAngles(arc.Plane, radius, arc.StartAngle, arc.SweepAngle), true);
                    }

                    break;
                }

            default:
                break;
        }

        return (FitOffset(curve, distance, unit, tolerance), false);
    }

    /// <summary>
    /// Offsets a curve and hands back <b>every</b> piece the offset is made of, rather than one
    /// curve fitted through all of it (<c>E2-T71</c>).
    /// </summary>
    /// <param name="curve">The curve to offset.</param>
    /// <param name="distance">How far to move it. The sign convention is <see cref="Offset"/>'s.</param>
    /// <param name="normal">The plane normal the offset happens in. Need not be unit length.</param>
    /// <param name="tolerance">How closely an approximated offset must follow the true one.</param>
    /// <returns>
    /// The pieces, in order along the original curve, each with whether it is exact rather than
    /// fitted. Never empty: a curve whose offset collapses entirely is a refusal, not an empty list.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="curve"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="distance"/> is not finite.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="normal"/> has no length, the curve does not lie in that plane, or every
    /// piece of it collapsed.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Why this is a separate member and not an overload.</b> <see cref="Offset"/> answers *what
    /// is the offset of this curve*, and for anything but a <see cref="PolyCurve"/> the answer is
    /// one curve and this member returns exactly that one. For a polycurve it is the wrong
    /// question: <see cref="Offset"/> samples the whole thing and fits **one** curve through the
    /// samples, which quietly bridges the places where the offset is not continuous — a fitted
    /// curve is always connected, whatever it was fitted to.
    /// </para>
    /// <para>
    /// <b>An offset of a polycurve is genuinely several curves, and the arithmetic says where.</b>
    /// **The common cause is a corner**: the offsets of two segments meeting at an angle end at
    /// different points, so there is a wedge of nothing between them going outwards and an overlap
    /// going inwards. The other cause is a segment that **leaves the offset plane** — a run along
    /// the normal has no offset in that plane at all — which ends the run and lets the rest of the
    /// curve answer. **Fitting one curve through both sides of a hole draws a curve through a
    /// region where the offset does not exist**, and that is the defect this member exists to
    /// avoid.
    /// </para>
    /// <para>
    /// <b>Offsetting an arc past its own radius is *not* one of the causes</b>, which is worth
    /// saying because it is the first thing a reader expects. The exact branch declines a negative
    /// radius and the fitted branch then produces the true locus — an arc of the same centre on the
    /// other side. That is a real curve and a correct answer, so nothing collapses.
    /// </para>
    /// <para>
    /// <b>Pieces are joined where they still meet and separated where they do not</b>, decided on
    /// the tolerance rather than on the topology of the input: consecutive segments whose offset
    /// ends coincide continue one run, and anything else starts a new one. So a convex polyline
    /// offset outwards comes back as one run per contiguous group and not as one piece per segment
    /// — the member is about where the offset **breaks**, not about how the caller happened to
    /// build their curve.
    /// </para>
    /// <para>
    /// <b>This does not trim self-intersections</b>, exactly as <see cref="Offset"/> does not. A
    /// concave corner offset outwards produces overlapping loops, the true offset locus includes
    /// them, and removing them needs curve-curve intersection.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(Curve Curve, bool Exact)> OffsetMany(
        Curve curve, double distance, in Vector3d normal, in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(curve);

        // THE WHOLE-CALL MISTAKES ARE CHECKED HERE, BEFORE THE LOOP, and this is not tidiness.
        // Below, a segment whose offset does not exist ends the run instead of failing the call -
        // and that catch is written for ArgumentException, which is also what a zero-length normal
        // raises. Without these two lines a bad normal makes EVERY segment "collapse", and the
        // caller is told their curve collapsed under the distance when what was wrong was the
        // normal. The test named ABadNormalIsRefusedRatherThanTreatedAsACollapsedPiece caught
        // exactly that on the first run.
        if (!double.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance), distance, "An offset distance must be finite.");
        }

        if (!normal.TryNormalise(out _))
        {
            throw new ArgumentException(
                "An offset needs a plane to happen in, and the normal given has no length. "
                + "In three dimensions 'offset by 5' does not name a curve on its own.",
                nameof(normal));
        }

        // Anything that is not a polycurve has one piece by construction. Delegating rather than
        // duplicating means the two members can never disagree about the simple case, which is
        // nearly every case.
        if (curve is not PolyCurve polyCurve)
        {
            return [Offset(curve, distance, normal, tolerance)];
        }

        List<(Curve Curve, bool Exact)> pieces = [];
        List<Curve> run = [];
        bool runIsExact = true;

        // An `in` parameter cannot be captured by a local function, and the copy is free: Tolerance
        // is a small readonly struct, which is why it is passed `in` in the first place.
        Tolerance joining = tolerance;

        void CloseRun()
        {
            if (run.Count == 1)
            {
                pieces.Add((run[0], runIsExact));
            }
            else if (run.Count > 1)
            {
                pieces.Add((PolyCurve.FromJoinedCurves(run, joining), runIsExact));
            }

            run = [];
            runIsExact = true;
        }

        foreach (Curve segment in polyCurve.Segments())
        {
            Curve offset;
            bool exact;

            try
            {
                (offset, exact) = Offset(segment, distance, normal, tolerance);
            }
            catch (ArgumentException)
            {
                // THIS SEGMENT HAS NO OFFSET IN THIS PLANE, which is the whole reason this member
                // exists. A segment running along the normal is the case that reaches here - it
                // leaves the plane, so there is no offset of it to return - and it ends the run
                // rather than failing the call, because the offset of the REST of the curve is a
                // real answer and is what the caller asked for. The distance and the normal were
                // validated above, so nothing that reaches here is a mistake about the whole call.
                CloseRun();
                continue;
            }

            if (run.Count > 0 && !run[^1].EndPoint.EqualsWithin(offset.StartPoint, tolerance))
            {
                CloseRun();
            }

            run.Add(offset);
            runIsExact &= exact;
        }

        CloseRun();

        return pieces.Count > 0
            ? pieces
            : throw new ArgumentException(
                "No piece of this curve has an offset in the plane given, so there is nothing to "
                + "return. Every segment runs along the normal, which means the curve and the "
                + "plane do not contain each other - the normal is the thing to change, not the "
                + "distance of "
                + distance.ToString("R", CultureInfo.InvariantCulture)
                + ".",
                nameof(curve));
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
    /// The arc of a given radius tangent to two curves, and the two curves trimmed back to meet it
    /// (`E2-T72`).
    /// </summary>
    /// <param name="first">The curve the fillet leaves.</param>
    /// <param name="second">The curve it arrives at.</param>
    /// <param name="radius">The fillet radius. Positive.</param>
    /// <param name="normal">
    /// The normal of the plane the two curves lie in. <see cref="Curve.PlaneOf(in Tolerance)"/>
    /// supplies it for a curve that has one; it is asked for rather than inferred because a fillet
    /// between two <i>straight</i> curves has no plane of its own to read.
    /// </param>
    /// <param name="tolerance">The tolerance for the intersection and the offsets.</param>
    /// <returns>The arc, and the two trimmed curves, in order.</returns>
    /// <exception cref="ArgumentNullException">Either curve is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is not positive and finite.</exception>
    /// <exception cref="ArgumentException">
    /// The curves do not cross, they are not coplanar, or no fillet of that radius fits the corner.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b><see cref="FilletLines"/> stopped at two lines for a reason that has since expired.</b>
    /// Its remarks said a general fillet <i>needs curve-curve intersection to know where the corner
    /// is at all, and a tangency problem solved by iteration — neither of which exists yet</i>.
    /// <see cref="Curve.IntersectWith(Curve, in Tolerance)"/> arrived in `E2-T11`, and the
    /// iteration is below. The closed form is kept because two lines is the overwhelmingly common
    /// case and a closed form is worth having; this is what answers everything else.
    /// </para>
    /// <para>
    /// <b>The method is: the fillet's centre is the point at distance <c>radius</c> from both
    /// curves</b>, which is where their offsets cross. Offsetting each curve by <c>radius</c> and
    /// intersecting the two gives it — and there are <b>four</b> such points, one per combination
    /// of sides, of which exactly one is the fillet a user meant. <b>The nearest to the corner
    /// wins</b>, which is the rule, stated here rather than left implicit: a fillet is the arc that
    /// rounds off <i>this</i> corner, and the other three round off the corner's reflections.
    /// </para>
    /// <para>
    /// <b>That intersection is a seed and not the answer, because <see cref="Offset"/> is not
    /// exact.</b> For anything but a line it fits a NURBS curve through sampled offset points, so a
    /// centre read straight off it is accurate to the fit rather than to the arithmetic — and a
    /// fillet is a promise about <i>tangency</i>, which is exactly what an approximate centre
    /// breaks. So the centre is refined by Newton on the two equations
    /// <c>distance(c, first) = radius</c> and <c>distance(c, second) = radius</c>, in the plane.
    /// The gradient of a distance-to-curve is the unit vector from the closest point, which makes
    /// the Jacobian two rows of two and the step a 2×2 solve. It is `E2-T70` step B's shape:
    /// bracket by sampling, then converge by Newton.
    /// </para>
    /// <para>
    /// <b>A radius too large for the corner is refused rather than rounded down</b>, and the
    /// refusal falls out of the geometry: the two offsets simply do not cross. A caller asking for
    /// a fillet of radius ten in a corner two across has asked for something that does not exist,
    /// and an arc that overshoots both curves is a worse answer than a message.
    /// </para>
    /// </remarks>
    public static (Arc Fillet, Curve First, Curve Second) Fillet(
        Curve first, Curve second, double radius, in Vector3d normal, in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (!double.IsFinite(radius) || radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius), radius, "A fillet radius must be positive and finite.");
        }

        if (!normal.TryNormalise(out Vector3d unitNormal))
        {
            throw new ArgumentException(
                "A fillet needs the normal of the plane its two curves lie in, and this one has no "
                + "length. Curve.PlaneOf will supply it for a curve that has a plane.",
                nameof(normal));
        }

        CurveIntersections crossing = first.IntersectWith(second, tolerance);

        if (crossing.Points.Count == 0)
        {
            throw new ArgumentException(
                "The two curves do not cross, so there is no corner to fillet. Extend them until "
                + "they meet, or fillet a pair that does.",
                nameof(second));
        }

        Point3d corner = crossing.Points[0].Point;

        if (!TryFilletCentre(first, second, radius, corner, unitNormal, tolerance, out Point3d centre))
        {
            throw new ArgumentException(
                $"No fillet of radius {radius.ToString("R", CultureInfo.InvariantCulture)} fits "
                + "that corner. The two curves offset by it never meet, which is what a radius too "
                + "large for the corner looks like.",
                nameof(radius));
        }

        Point3d tangentOnFirst = first.ClosestPoint(centre);
        Point3d tangentOnSecond = second.ClosestPoint(centre);

        Arc fillet = Arc.FromThreePoints(
            tangentOnFirst,
            MidArcPoint(centre, tangentOnFirst, tangentOnSecond, radius),
            tangentOnSecond);

        return (
            fillet,
            TrimToCorner(first, tangentOnFirst, corner),
            TrimToCorner(second, tangentOnSecond, corner));
    }

    /// <summary>
    /// The arc tangent to two curves whose radius is decided by a third (`E2-T72`).
    /// </summary>
    /// <param name="first">The curve the arc leaves.</param>
    /// <param name="second">The curve it arrives at.</param>
    /// <param name="tangentTo">The curve that decides the radius by being tangent to it too.</param>
    /// <param name="normal">
    /// The normal of the plane all three curves lie in, for the reason
    /// <see cref="Fillet"/> asks for one.
    /// </param>
    /// <param name="tolerance">The tolerance for the crossings that seed the solve.</param>
    /// <returns>The arc, from its tangent point on <paramref name="first"/> to the one on
    /// <paramref name="second"/>.</returns>
    /// <exception cref="ArgumentNullException">Any of the three curves is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="normal"/> has no length, the three curves do not enclose a corner to seed
    /// the solve from, or no arc is tangent to all three.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>This is <see cref="Fillet"/>'s solve with one more row, which is exactly what the register
    /// predicted of it.</b> That member is given the radius and solves for a centre at that
    /// distance from two curves — two equations in two unknowns. Here the radius is <i>not</i>
    /// given, so the unknowns are the centre's two coordinates in the plane <b>and</b> the radius,
    /// and the equations are <c>distance(c, curveᵢ) = r</c> for all three curves. The gradient of a
    /// distance-to-curve is the unit vector from the closest point, so each row of the Jacobian is
    /// that vector's two in-plane components followed by <c>−1</c>, and the step is a 3×3 solve.
    /// </para>
    /// <para>
    /// <b>Three curves have several circles tangent to all of them — a triangle has four — so the
    /// seed decides which one comes back, and the seed is stated rather than left to the
    /// solver.</b> It is the <b>centroid of the three pairwise crossings</b>, with the radius
    /// seeded as the mean distance from there to the three curves. For a triangle that point is
    /// inside it, so the <i>incircle</i> is the answer and the three excircles are not.
    /// </para>
    /// <para>
    /// <b>Nothing is trimmed.</b> <see cref="Fillet"/> hands back the two curves cut to meet the
    /// arc, because rounding a corner is what it is for; this member answers a question about size
    /// and returns the arc alone. The third curve contributes no endpoint — it only fixes the
    /// radius — so the arc still runs between its tangent points on the first two.
    /// </para>
    /// </remarks>
    public static Arc FilletTangentTo(
        Curve first, Curve second, Curve tangentTo, in Vector3d normal, in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(tangentTo);

        if (!normal.TryNormalise(out Vector3d unitNormal))
        {
            throw new ArgumentException(
                "A fillet needs the normal of the plane its curves lie in, and this one has no "
                + "length.",
                nameof(normal));
        }

        if (!TrySeed(first, second, tangentTo, tolerance, out Point3d centre, out double radius))
        {
            throw new ArgumentException(
                "The three curves do not enclose a corner: at least two of their three pairwise "
                + "crossings are needed to seed the solve, and fewer than that were found.",
                nameof(tangentTo));
        }

        if (!ConvergeOnThree(first, second, tangentTo, unitNormal, ref centre, ref radius))
        {
            throw new ArgumentException(
                "No arc is tangent to all three curves near the corner they enclose.",
                nameof(tangentTo));
        }

        Point3d tangentOnFirst = first.ClosestPoint(centre);
        Point3d tangentOnSecond = second.ClosestPoint(centre);

        return Arc.FromThreePoints(
            tangentOnFirst,
            MidArcPoint(centre, tangentOnFirst, tangentOnSecond, radius),
            tangentOnSecond);
    }

    /// <summary>Where to start the three-way solve from.</summary>
    /// <param name="first">The first curve.</param>
    /// <param name="second">The second curve.</param>
    /// <param name="third">The third curve.</param>
    /// <param name="tolerance">The intersection tolerance.</param>
    /// <param name="centre">The seed centre.</param>
    /// <param name="radius">The seed radius.</param>
    /// <returns><see langword="false"/> when the three curves enclose nothing to seed from.</returns>
    /// <remarks>
    /// <b>The centroid of the pairwise crossings, because it is inside the corner they enclose.</b>
    /// Two crossings are enough to place it; three are better and are used when they exist. The
    /// radius seed is the mean distance from that point to the three curves, which is the value the
    /// three equations are trying to agree on.
    /// </remarks>
    private static bool TrySeed(
        Curve first, Curve second, Curve third, in Tolerance tolerance,
        out Point3d centre, out double radius)
    {
        centre = Point3d.Origin;
        radius = 0.0;

        Vector3d sum = Vector3d.Zero;
        int corners = 0;

        foreach ((Curve a, Curve b) in ((Curve, Curve)[])[(first, second), (first, third), (second, third)])
        {
            CurveIntersections crossing = a.IntersectWith(b, tolerance);

            if (crossing.Points.Count > 0)
            {
                sum += crossing.Points[0].Point - Point3d.Origin;
                corners++;
            }
        }

        if (corners < 2)
        {
            return false;
        }

        centre = Point3d.Origin + (sum / corners);
        radius = (first.ClosestPoint(centre).DistanceTo(centre)
            + second.ClosestPoint(centre).DistanceTo(centre)
            + third.ClosestPoint(centre).DistanceTo(centre)) / 3.0;

        return radius > 0.0;
    }

    /// <summary>
    /// Newton on <c>distance(c, curveᵢ) = r</c> for three curves, in the centre's two in-plane
    /// coordinates and the radius.
    /// </summary>
    /// <param name="first">The first curve.</param>
    /// <param name="second">The second curve.</param>
    /// <param name="third">The third curve.</param>
    /// <param name="normal">The unit plane normal, which the centre is kept in.</param>
    /// <param name="centre">The seed centre, refined in place.</param>
    /// <param name="radius">The seed radius, refined in place.</param>
    /// <returns><see langword="false"/> when the step is singular or the iteration does not settle.</returns>
    /// <remarks>
    /// <b>It is <see cref="Converge"/> with a third equation and a third unknown</b>, and the extra
    /// column is the same <c>−1</c> in every row: moving the radius moves all three residuals
    /// together, which is what makes the system solvable at all rather than over-determined. The
    /// convergence test is relative to the radius, for the reason every convergence test in this
    /// kernel is relative ([N143](../../docs/NOTES.md)).
    /// </remarks>
    private static bool ConvergeOnThree(
        Curve first, Curve second, Curve third, in Vector3d normal,
        ref Point3d centre, ref double radius)
    {
        Plane frame = Plane.FromOriginNormal(centre, normal);
        Vector3d axisU = frame.XAxis;
        Vector3d axisV = frame.YAxis;

        Span<double> rows = stackalloc double[12];

        for (int iteration = 0; iteration < 96; iteration++)
        {
            bool settled = true;

            for (int index = 0; index < 3; index++)
            {
                Curve curve = index switch { 0 => first, 1 => second, _ => third };
                Point3d near = curve.ClosestPoint(centre);
                double away = near.DistanceTo(centre);

                if (away <= 0.0)
                {
                    // The centre landed on a curve. There is no direction to step in.
                    return false;
                }

                Vector3d gradient = (centre - near) / away;
                double residual = away - radius;

                rows[(index * 4) + 0] = gradient.Dot(axisU);
                rows[(index * 4) + 1] = gradient.Dot(axisV);
                rows[(index * 4) + 2] = -1.0;
                rows[(index * 4) + 3] = -residual;

                if (Math.Abs(residual) > Math.Abs(radius) * 1e-13)
                {
                    settled = false;
                }
            }

            if (settled)
            {
                return radius > 0.0;
            }

            if (!Solve3(rows, out double stepU, out double stepV, out double stepR))
            {
                return false;
            }

            centre += (axisU * stepU) + (axisV * stepV);
            radius += stepR;
        }

        return false;
    }

    /// <summary>Solves a 3×3 system by Cramer's rule.</summary>
    /// <param name="rows">Three rows of four: the matrix and the right-hand side.</param>
    /// <param name="x">The first unknown.</param>
    /// <param name="y">The second.</param>
    /// <param name="z">The third.</param>
    /// <returns><see langword="false"/> when the matrix is singular.</returns>
    /// <remarks>
    /// <b>Cramer's rule rather than elimination, because three is small enough that the
    /// determinant is the clearest way to say "singular".</b> A zero determinant here means two of
    /// the three curves have parallel gradients at the centre — they are tangent to each other
    /// there — and a tangent pair bounds no corner.
    /// </remarks>
    private static bool Solve3(ReadOnlySpan<double> rows, out double x, out double y, out double z)
    {
        x = y = z = 0.0;

        double a = rows[0], b = rows[1], c = rows[2], p = rows[3];
        double d = rows[4], e = rows[5], f = rows[6], q = rows[7];
        double g = rows[8], h = rows[9], i = rows[10], r = rows[11];

        double determinant = (a * ((e * i) - (f * h)))
            - (b * ((d * i) - (f * g)))
            + (c * ((d * h) - (e * g)));

        if (determinant == 0.0 || !double.IsFinite(determinant))
        {
            return false;
        }

        x = ((p * ((e * i) - (f * h))) - (b * ((q * i) - (f * r))) + (c * ((q * h) - (e * r))))
            / determinant;
        y = ((a * ((q * i) - (f * r))) - (p * ((d * i) - (f * g))) + (c * ((d * r) - (q * g))))
            / determinant;
        z = ((a * ((e * r) - (q * h))) - (b * ((d * r) - (q * g))) + (p * ((d * h) - (e * g))))
            / determinant;

        return double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z);
    }

    /// <summary>
    /// The point at <paramref name="radius"/> from both curves nearest the corner, refined until it
    /// is exactly that.
    /// </summary>
    /// <param name="first">The first curve.</param>
    /// <param name="second">The second curve.</param>
    /// <param name="radius">The fillet radius.</param>
    /// <param name="corner">Where the two curves cross.</param>
    /// <param name="normal">The unit plane normal.</param>
    /// <param name="tolerance">The tolerance for the offsets and their intersection.</param>
    /// <param name="centre">The fillet centre.</param>
    /// <returns><see langword="false"/> when no such point exists near the corner.</returns>
    private static bool TryFilletCentre(
        Curve first,
        Curve second,
        double radius,
        in Point3d corner,
        in Vector3d normal,
        in Tolerance tolerance,
        out Point3d centre)
    {
        centre = Point3d.Origin;

        // The four side combinations. Only one rounds off THIS corner; the other three round off
        // its reflections, and they are all genuine points at `radius` from both curves — which is
        // why the choice cannot be made afterwards from the geometry alone and is made here, by
        // taking the one nearest the corner.
        double best = double.MaxValue;
        bool found = false;

        foreach (int firstSign in (int[])[1, -1])
        {
            Curve offsetFirst;

            try
            {
                offsetFirst = Offset(first, radius * firstSign, normal, tolerance).Curve;
            }
            catch (ArgumentException)
            {
                // An offset that collapses is a side this corner does not have.
                continue;
            }

            foreach (int secondSign in (int[])[1, -1])
            {
                Curve offsetSecond;

                try
                {
                    offsetSecond = Offset(second, radius * secondSign, normal, tolerance).Curve;
                }
                catch (ArgumentException)
                {
                    continue;
                }

                foreach (CurveIntersectionPoint candidate in
                    offsetFirst.IntersectWith(offsetSecond, tolerance).Points)
                {
                    double away = candidate.Point.DistanceTo(corner);

                    if (away < best)
                    {
                        best = away;
                        centre = candidate.Point;
                        found = true;
                    }
                }
            }
        }

        return found && Converge(first, second, radius, normal, ref centre);
    }

    /// <summary>
    /// Newton on <c>distance(c, first) = radius</c> and <c>distance(c, second) = radius</c>.
    /// </summary>
    /// <param name="first">The first curve.</param>
    /// <param name="second">The second curve.</param>
    /// <param name="radius">The radius both distances must reach.</param>
    /// <param name="normal">The unit plane normal, which the centre is kept in.</param>
    /// <param name="centre">The seed, refined in place.</param>
    /// <returns><see langword="false"/> when the step is singular and the seed is not already a solution.</returns>
    /// <remarks>
    /// <b>The gradient of a distance-to-curve is the unit vector from the closest point</b>, which
    /// is what makes this two equations in two unknowns rather than a general optimisation. The two
    /// unknowns are the centre's coordinates <i>in the plane</i>, so the step is taken in a basis
    /// built from the normal and the answer cannot drift out of the plane over the iterations.
    /// </remarks>
    private static bool Converge(
        Curve first, Curve second, double radius, in Vector3d normal, ref Point3d centre)
    {
        // A basis for the plane, taken from Plane's own frame builder rather than derived here:
        // picking a perpendicular to a normal is exactly what FromOriginNormal already does, and
        // which perpendicular it picks does not matter to a 2x2 solve.
        Plane frame = Plane.FromOriginNormal(centre, normal);
        Vector3d axisU = frame.XAxis;
        Vector3d axisV = frame.YAxis;

        for (int iteration = 0; iteration < 64; iteration++)
        {
            Point3d nearFirst = first.ClosestPoint(centre);
            Point3d nearSecond = second.ClosestPoint(centre);

            double toFirst = nearFirst.DistanceTo(centre);
            double toSecond = nearSecond.DistanceTo(centre);

            double errorFirst = toFirst - radius;
            double errorSecond = toSecond - radius;

            // Relative to the radius, for the reason every convergence test in this kernel is
            // relative: a fillet a micron across and one a kilometre across must converge by the
            // same standard ([N143](../../docs/NOTES.md)).
            if (Math.Abs(errorFirst) <= radius * 1e-14 && Math.Abs(errorSecond) <= radius * 1e-14)
            {
                return true;
            }

            if (toFirst <= 0.0 || toSecond <= 0.0)
            {
                // The centre landed on one of the curves. There is no direction to step in.
                return false;
            }

            Vector3d awayFromFirst = (centre - nearFirst) / toFirst;
            Vector3d awayFromSecond = (centre - nearSecond) / toSecond;

            double a = awayFromFirst.Dot(axisU);
            double b = awayFromFirst.Dot(axisV);
            double c = awayFromSecond.Dot(axisU);
            double d = awayFromSecond.Dot(axisV);

            double determinant = (a * d) - (b * c);

            if (determinant == 0.0 || !double.IsFinite(determinant))
            {
                // The two gradients are parallel: the curves are tangent to each other here, and a
                // tangent pair has no corner to round.
                return false;
            }

            double stepU = -((errorFirst * d) - (errorSecond * b)) / determinant;
            double stepV = -((errorSecond * a) - (errorFirst * c)) / determinant;

            centre += (axisU * stepU) + (axisV * stepV);
        }

        return false;
    }

    /// <summary>The part of a curve from its far end to the fillet's tangent point.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="tangentPoint">Where the fillet touches it.</param>
    /// <param name="corner">Where the two curves cross, which is the end being trimmed away.</param>
    /// <returns>The trimmed curve.</returns>
    /// <remarks>
    /// <b>The corner decides which side is kept</b>, not the curve's own direction: the piece
    /// wanted is the one running away from the corner, and a caller who drew the second curve
    /// backwards should still get a chain that joins.
    /// </remarks>
    private static Curve TrimToCorner(Curve curve, in Point3d tangentPoint, in Point3d corner)
    {
        double atTangent = curve.ClosestParameter(tangentPoint);
        double atCorner = curve.ClosestParameter(corner);

        return atCorner > atTangent
            ? curve.Trimmed(new Interval(curve.Domain.Min, atTangent))
            : curve.Trimmed(new Interval(curve.Domain.Max, atTangent));
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

        // The center lies along the bisector, at the distance that puts it `radius` from both.
        Vector3d bisector = (awayFromFirst + towardsSecond).Normalised();
        Point3d center = corner + (bisector * (radius / Math.Sin(half)));

        Arc fillet = Arc.FromThreePoints(tangentOnFirst, MidArcPoint(center, tangentOnFirst, tangentOnSecond, radius), tangentOnSecond);

        return (
            fillet,
            new Line(FarEnd(first, corner), tangentOnFirst),
            new Line(tangentOnSecond, FarEnd(second, corner)));
    }

    /// <summary>The point halfway round the fillet, which is what pins the arc's direction.</summary>
    private static Point3d MidArcPoint(
        in Point3d center, in Point3d from, in Point3d to, double radius)
    {
        Vector3d towardsMiddle = ((from - center).Normalised() + (to - center).Normalised()).Normalised();

        return center + (towardsMiddle * radius);
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
