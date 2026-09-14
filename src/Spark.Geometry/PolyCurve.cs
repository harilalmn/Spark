using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A chain of curves joined end to end, parameterised over [0, n] with one unit per segment, so
/// that its whole-number parameters are exactly its joints.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each segment keeps its own parameterisation, and the polycurve maps onto it.</b> A polycurve
/// made of a line and an arc has domain [0, 2]; parameter 1.5 is halfway along the arc's domain,
/// not halfway along the arc by length. Everything measured by length —
/// <see cref="Curve.DivideEqually(int)"/>, <see cref="Curve.PointAtLength(double)"/> — goes through
/// each segment's own arc-length machinery and is therefore exact wherever the segment is exact.
/// </para>
/// <para>
/// <b>Nested polycurves are flattened at construction.</b> Joining two polycurves gives a polycurve
/// of their segments rather than a polycurve of polycurves, so the parameterisation of a chain does
/// not depend on the order it was assembled in.
/// </para>
/// <para>
/// <b>The join tolerance is passed, never assumed.</b> <see cref="FromJoinedCurves"/> takes a
/// <see cref="Tolerance"/> and refuses a chain whose segments do not meet within it, naming the
/// index and the gap. Silently accepting a gap would produce a curve whose length is not the length
/// of the path it draws.
/// </para>
/// </remarks>
public sealed class PolyCurve : Curve
{
    private readonly Curve[] _segments;
    private double[]? _cumulative;

    private PolyCurve(Curve[] segments) => _segments = segments;

    /// <summary>Lifts an instance's state into a new one, so a constructor can call a factory.</summary>
    /// <param name="other">The instance to copy. Already validated by whatever produced it.</param>
    /// <remarks>
    /// <b>`E2-T59` asked for a constructor beside every library factory, and a class constructor
    /// cannot return.</b> This is what lets the public ones below read <c>: this(SomeFactory(x))</c>.
    /// The arithmetic stays in the factory, which remains its only copy, so the constructor cannot
    /// drift away from the method it mirrors.
    /// </remarks>
    private PolyCurve(PolyCurve other)
        : this(other._segments)
    {
    }

    /// <summary>Joins curves end to end into one curve.</summary>
    /// <param name="curves">The curves, in order. Nested polycurves are flattened.</param>
    /// <param name="tolerance">How far consecutive ends may sit apart. Defaults to the ambient tolerance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="curves"/> is null, or holds a null.</exception>
    /// <exception cref="ArgumentException">Thrown when there are no curves, or a gap is too wide.</exception>
    /// <remarks>Forwards to <see cref="FromJoinedCurves"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public PolyCurve(IEnumerable<Curve> curves, in Tolerance tolerance = default)
        : this(FromJoinedCurves(curves, tolerance))
    {
    }

    /// <inheritdoc/>
    public override Interval Domain => new(0.0, _segments.Length);

    /// <inheritdoc/>
    public override bool IsClosed => _segments[0].StartPoint == _segments[^1].EndPoint;

    /// <summary>How many segments the chain has.</summary>
    public int SegmentCount => _segments.Length;

    /// <summary>Joins curves end to end into a single curve.</summary>
    /// <param name="curves">
    /// The curves, in order. Each one's start point must be within <paramref name="tolerance"/> of
    /// the previous one's end point. Any polycurve among them is flattened into its own segments.
    /// </param>
    /// <param name="tolerance">
    /// How far apart consecutive ends may be. Its <see cref="Tolerance.Linear"/> component is the
    /// one that matters.
    /// </param>
    /// <returns>The polycurve.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="curves"/> is <see langword="null"/>, or one of them is.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when there are no curves, or when consecutive curves do not meet within the tolerance.
    /// The message names the index and the size of the gap.
    /// </exception>
    public static PolyCurve FromJoinedCurves(
        IEnumerable<Curve> curves, in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(curves);

        List<Curve> flattened = [];
        foreach (Curve curve in curves)
        {
            ArgumentNullException.ThrowIfNull(curve, nameof(curves));
            if (curve is PolyCurve nested)
            {
                flattened.AddRange(nested._segments);
            }
            else
            {
                flattened.Add(curve);
            }
        }

        if (flattened.Count == 0)
        {
            throw new ArgumentException("A polycurve needs at least one segment.", nameof(curves));
        }

        double linear = tolerance.Linear;
        for (int index = 1; index < flattened.Count; index++)
        {
            double gap = flattened[index - 1].EndPoint.DistanceTo(flattened[index].StartPoint);
            if (gap > linear)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Segments {index - 1} and {index} are {gap} apart, which is more than the "
                        + $"join tolerance of {linear}."),
                    nameof(curves));
            }
        }

        return new PolyCurve([.. flattened]);
    }

    /// <summary>
    /// Sorts a heap of curves into as many chains as it holds (`E2-T72`).
    /// </summary>
    /// <param name="curves">
    /// The curves, in any order and drawn in any direction. Nested polycurves are flattened.
    /// </param>
    /// <param name="tolerance">
    /// How near two ends must be to count as joined. Its <see cref="Tolerance.Linear"/> component
    /// is the one that matters.
    /// </param>
    /// <returns>
    /// One polycurve per chain, in the order their first curve appeared in the input. A curve that
    /// touches nothing comes back as a chain of one.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="curves"/> is <see langword="null"/>, or one of them is.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>This is <see cref="FromJoinedCurves"/>'s measurement put to the opposite use.</b> That
    /// member builds <i>one</i> chain and <b>refuses</b> a gap, naming the index and the distance;
    /// this builds <i>several</i> and treats a gap as the <b>boundary between them</b>. Same
    /// tolerance, opposite conclusion — which is why it is a separate member rather than a flag on
    /// that one.
    /// </para>
    /// <para>
    /// <b>The work is orientation, not grouping.</b> A heap of curves has no direction: the curve
    /// that continues a chain may need <b>reversing</b> to do it, and which of its two ends meets
    /// the chain is found rather than assumed. An implementation that only matches end to start
    /// chains the links that happen to have been drawn the same way round and leaves the rest as
    /// singletons — on ordinary input, not awkward input.
    /// </para>
    /// <para>
    /// <b>Each chain is walked forward from its seed's end and then backward from its start</b>, so
    /// a seed taken from the middle of a chain still produces the whole of it, in order.
    /// </para>
    /// <para>
    /// <b>A junction where three or more curves meet is resolved by input order, which is
    /// deterministic and arbitrary, and both of those words are meant.</b> A heap that branches is
    /// a <i>graph</i>, and grouping is not the member to decide what a graph's chains are — so it
    /// makes a repeatable choice rather than a clever one, and this paragraph is the warning that
    /// the choice carries no meaning.
    /// </para>
    /// <para>
    /// <b>A chain that returns to its own seed closes and stops.</b> Nothing is dropped: a curve
    /// touching no other comes back as a chain of one, because a caller who hands in geometry is
    /// entitled to get all of it back.
    /// </para>
    /// </remarks>
    public static PolyCurve[] FromGroupedCurves(
        IEnumerable<Curve> curves, in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(curves);

        List<Curve> heap = [];
        foreach (Curve curve in curves)
        {
            ArgumentNullException.ThrowIfNull(curve, nameof(curves));
            heap.Add(curve);
        }

        double linear = tolerance.Linear;
        bool[] used = new bool[heap.Count];
        List<PolyCurve> groups = [];

        for (int seed = 0; seed < heap.Count; seed++)
        {
            if (used[seed])
            {
                continue;
            }

            used[seed] = true;
            LinkedList<Curve> chain = new();
            chain.AddFirst(heap[seed]);

            Extend(chain, heap, used, linear, forwards: true);
            Extend(chain, heap, used, linear, forwards: false);

            groups.Add(FromJoinedCurves(chain, tolerance));
        }

        return [.. groups];
    }

    /// <summary>Grows a chain from one of its two ends until nothing else joins it.</summary>
    /// <param name="chain">The chain so far.</param>
    /// <param name="heap">Every curve handed in.</param>
    /// <param name="used">Which of them are already in a chain.</param>
    /// <param name="linear">How near two ends must be to count as joined.</param>
    /// <param name="forwards">
    /// <see langword="true"/> to grow from the chain's end, <see langword="false"/> from its start.
    /// </param>
    /// <remarks>
    /// <b>The used-set is what stops a ring.</b> A chain that comes back to its own seed finds only
    /// curves it has already taken, so the walk ends there rather than going round again — the
    /// closure falls out of the bookkeeping instead of needing a test of its own in the loop.
    /// </remarks>
    private static void Extend(
        LinkedList<Curve> chain, List<Curve> heap, bool[] used, double linear, bool forwards)
    {
        while (true)
        {
            Point3d open = forwards ? chain.Last!.Value.EndPoint : chain.First!.Value.StartPoint;
            int found = -1;
            bool reversed = false;

            for (int index = 0; index < heap.Count; index++)
            {
                if (used[index])
                {
                    continue;
                }

                Curve candidate = heap[index];

                // Which END of the candidate meets the chain is found, not assumed. Growing
                // forwards the chain wants a curve that STARTS at the open point; growing backwards
                // it wants one that ENDS there; and either way the other end will do just as well
                // once the curve is turned round.
                bool joinsAsDrawn = forwards
                    ? candidate.StartPoint.DistanceTo(open) <= linear
                    : candidate.EndPoint.DistanceTo(open) <= linear;
                bool joinsTurned = forwards
                    ? candidate.EndPoint.DistanceTo(open) <= linear
                    : candidate.StartPoint.DistanceTo(open) <= linear;

                if (!joinsAsDrawn && !joinsTurned)
                {
                    continue;
                }

                // Input order, deliberately: the first match wins, so a branch resolves the same
                // way every run without pretending the resolution means anything.
                found = index;
                reversed = !joinsAsDrawn;
                break;
            }

            if (found < 0)
            {
                return;
            }

            used[found] = true;
            Curve link = reversed ? heap[found].Reversed() : heap[found];

            if (forwards)
            {
                chain.AddLast(link);
            }
            else
            {
                chain.AddFirst(link);
            }
        }
    }

    /// <summary>
    /// The closed outline of a curve given a thickness, thickened <i>sideways</i> within a plane
    /// (`E2-T72`).
    /// </summary>
    /// <param name="curve">The centre line. Must be open.</param>
    /// <param name="thickness">How wide the result is. Positive. Half of it goes to each side.</param>
    /// <param name="planeNormal">The normal of the plane the thickening happens in.</param>
    /// <param name="tolerance">The tolerance for the offsets and for joining the loop.</param>
    /// <returns>The closed outline: one side, a cap, the other side, a cap.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="curve"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="thickness"/> is not positive and finite.</exception>
    /// <exception cref="ArgumentException">
    /// The curve is closed, or it does not lie in a plane with that normal.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>The curve is the centre line and half the thickness goes to each side</b>, which is what
    /// thickening means and is worth stating because the alternative — all of it on one side — is
    /// a perfectly reasonable operation with a different name.
    /// </para>
    /// <para>
    /// <b>The ends are capped with straight lines.</b> The signature does not say what a cap is, so
    /// this one chooses: a straight line between the two offset ends is the shape a caller can
    /// reason about and measure. A rounded cap is a different outline, and it would be a different
    /// member rather than a flag on this one.
    /// </para>
    /// <para>
    /// <b>A closed curve is refused rather than thickened.</b> A closed centre line thickened is an
    /// <i>annulus</i> — an outer loop and an inner one — which is two curves and not one polycurve,
    /// so there is nothing honest for this member to return.
    /// </para>
    /// <para>
    /// <b>A self-intersecting result is not repaired, and that is inherited rather than decided
    /// here.</b> <see cref="CurveOffset.Offset"/> states that it returns the true offset locus with
    /// its loops — trimming them needs a capability that is not built — so thickening a wiggly
    /// curve by more than twice its smallest radius of curvature gives an outline that crosses
    /// itself. A wrapper that refused what the member beneath it returns would be two stances on
    /// one question. <see cref="PolyLine.SelfIntersections"/> on a tessellation answers it for a
    /// caller who needs to know.
    /// </para>
    /// </remarks>
    public static PolyCurve FromThickenedCurve(
        Curve curve, double thickness, in Vector3d planeNormal, in Tolerance tolerance = default)
    {
        CheckThickening(curve, thickness);

        double half = thickness / 2.0;

        return CloseTheRibbon(
            CurveOffset.Offset(curve, half, planeNormal, tolerance).Curve,
            CurveOffset.Offset(curve, -half, planeNormal, tolerance).Curve,
            tolerance);
    }

    /// <summary>
    /// The closed outline of a curve given a thickness, thickened <i>along</i> a direction
    /// (`E2-T72`).
    /// </summary>
    /// <param name="curve">The centre line. Must be open.</param>
    /// <param name="thickness">How wide the result is. Positive. Half of it goes to each side.</param>
    /// <param name="direction">The direction to thicken along. Need not be unit length.</param>
    /// <param name="tolerance">The tolerance for joining the loop.</param>
    /// <returns>The closed outline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="curve"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="thickness"/> is not positive and finite.</exception>
    /// <exception cref="ArgumentException">The curve is closed, or the direction has no length.</exception>
    /// <remarks>
    /// <para>
    /// <b>This is the other reading of Dynamo's pair, and the reading is stated because the
    /// signatures do not give it.</b> <c>ByThickeningCurve</c> and <c>ByThickeningCurveNormal</c>
    /// both take a vector, so <i>supplied versus inferred</i> cannot be what separates them. The
    /// reading taken is the one in which **both** arguments have a job:
    /// <see cref="FromThickenedCurve"/> offsets <b>sideways, within</b> the plane the vector is
    /// normal to — a ribbon lying flat — and this offsets <b>along</b> the vector — a ribbon
    /// standing up. A reading that leaves an argument doing nothing is the less likely one.
    /// </para>
    /// <para>
    /// <b>It is a translation, not an offset, and so it is exact for every curve type.</b> Moving a
    /// curve bodily keeps its shape, where offsetting it generally does not — the offset of a
    /// polynomial curve is not polynomial, which is why <see cref="CurveOffset.Offset"/> fits an
    /// approximation for everything but lines, circles and arcs. This member fits nothing.
    /// </para>
    /// <para>
    /// <b>The direction may be any vector, including one in the curve's own plane.</b> Nothing here
    /// requires it to be perpendicular to anything; a direction lying along the curve gives a
    /// degenerate ribbon, which is the arithmetic being honest rather than a case to guard.
    /// </para>
    /// </remarks>
    public static PolyCurve FromThickenedCurveAlong(
        Curve curve, double thickness, in Vector3d direction, in Tolerance tolerance = default)
    {
        CheckThickening(curve, thickness);

        if (!direction.TryNormalise(out Vector3d unit))
        {
            throw new ArgumentException(
                "A thickening direction must have some length.", nameof(direction));
        }

        Vector3d half = unit * (thickness / 2.0);

        return CloseTheRibbon(
            curve.TransformedBy(Transform.Translation(half)),
            curve.TransformedBy(Transform.Translation(-half)),
            tolerance);
    }

    /// <summary>Closes two sides of a ribbon into one outline.</summary>
    /// <param name="first">One side, running the way the centre line runs.</param>
    /// <param name="second">The other side, running the same way.</param>
    /// <param name="tolerance">The join tolerance.</param>
    /// <returns>The closed outline.</returns>
    /// <remarks>
    /// <para>
    /// <b>The second side has to be turned round, and that is the whole of this helper.</b> Both
    /// sides come out running the same way as the centre line did. The loop is: the first side
    /// forward, a cap across the far end, the second side <b>reversed</b>, and a cap back across
    /// the near end.
    /// </para>
    /// <para>
    /// <b>Leaving out the reversal does not fail loudly, which is worth knowing.</b> The caps are
    /// built from whatever ends they find, so an unreversed return side still produces a chain that
    /// joins and still <i>closes</i> — it is a <b>bow-tie</b>, with two diagonal caps crossing in
    /// the middle, and it passes every test of continuity and closure. What catches it is the
    /// <i>perimeter</i>, and that a bow-tie crosses itself.
    /// </para>
    /// </remarks>
    private static PolyCurve CloseTheRibbon(Curve first, Curve second, in Tolerance tolerance)
    {
        Curve back = second.Reversed();

        return FromJoinedCurves(
            [
                first,
                new Line(first.EndPoint, back.StartPoint),
                back,
                new Line(back.EndPoint, first.StartPoint),
            ],
            tolerance);
    }

    /// <summary>Rejects the inputs neither thickening member can work with.</summary>
    /// <param name="curve">The centre line.</param>
    /// <param name="thickness">The thickness.</param>
    private static void CheckThickening(Curve curve, double thickness)
    {
        ArgumentNullException.ThrowIfNull(curve);

        if (!double.IsFinite(thickness) || thickness <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thickness), thickness, "A thickness must be positive and finite.");
        }

        if (curve.IsClosed)
        {
            throw new ArgumentException(
                "A closed curve thickened is an annulus - an outer loop and an inner one - which is "
                + "two curves and not one polycurve. Thicken an open curve, or offset the closed "
                + "one to both sides with CurveOffset.Offset and keep the two loops.",
                nameof(curve));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Each segment simplifies itself and the chain is rebuilt from the results.</b> Nothing
    /// here simplifies <i>across</i> a join: two collinear segments stay two segments, because a
    /// polycurve's joints are where its parameterisation changes and merging them would move every
    /// parameter in the chain. A caller who wants the joins gone has a different question.
    /// </remarks>
    public override Curve Simplified(in Tolerance tolerance = default)
    {
        Curve[] simplified = new Curve[_segments.Length];
        bool changed = false;

        for (int index = 0; index < _segments.Length; index++)
        {
            simplified[index] = _segments[index].Simplified(tolerance);
            changed |= !ReferenceEquals(simplified[index], _segments[index]);
        }

        return changed ? FromJoinedCurves(simplified, tolerance) : this;
    }

    /// <summary>The segments of the chain, in order.</summary>
    /// <returns>A copy of the array. The polycurve's own is never handed out.</returns>
    public Curve[] Segments() => [.. _segments];

    /// <summary>The segment at an index.</summary>
    /// <param name="index">The index, from zero to <see cref="SegmentCount"/> minus one.</param>
    /// <returns>The segment.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is out of range.</exception>
    public Curve SegmentAt(int index)
    {
        if (index < 0 || index >= _segments.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, "The segment index is outside the polycurve.");
        }

        return _segments[index];
    }

    /// <inheritdoc/>
    public override double LengthAt(double parameter)
    {
        double valid = CheckParameter(parameter);
        (int segment, double local) = Locate(valid);
        Curve curve = _segments[segment];
        return Cumulative()[segment] + curve.LengthAt(curve.Domain.Denormalise(local));
    }

    /// <inheritdoc/>
    public override double ParameterAtLength(double distance)
    {
        if (!double.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance), distance, "A distance along a curve must be finite.");
        }

        double[] cumulative = Cumulative();
        double target = Math.Clamp(distance, 0.0, cumulative[^1]);

        int low = 0;
        int high = cumulative.Length - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (cumulative[mid] <= target)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        Curve curve = _segments[low];
        double within = curve.ParameterAtLength(target - cumulative[low]);
        return low + curve.Domain.Normalise(within);
    }

    /// <inheritdoc/>
    public override Point3d[] Tessellate(in Tolerance tolerance = default)
    {
        List<Point3d> points = [];
        foreach (Curve segment in _segments)
        {
            Point3d[] part = segment.Tessellate(tolerance);

            // WITHIN THE TOLERANCE, NOT EXACTLY. FromJoinedCurves already accepts segments whose
            // ends sit up to `tolerance.Linear` apart, so an exact test here leaves a near-zero
            // segment at every join that was computed rather than typed — which is every join a
            // fillet or a tangent closure makes. Anything reading the result as a polyline then
            // sees the two sides of that stub as non-consecutive and reports the join as a
            // crossing. The tolerance is already a parameter of this method; using it is the whole
            // fix.
            int first = points.Count > 0
                && points[^1].DistanceTo(part[0]) <= tolerance.Linear ? 1 : 0;
            for (int index = first; index < part.Length; index++)
            {
                points.Add(part[index]);
            }
        }

        return [.. points];
    }

    /// <inheritdoc/>
    /// <remarks>One boundary per segment, since each is a different curve with its own speed.</remarks>
    protected override IReadOnlyList<double> SpanBoundaries()
    {
        double[] boundaries = new double[_segments.Length + 1];
        for (int i = 0; i < boundaries.Length; i++)
        {
            boundaries[i] = i;
        }

        return boundaries;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>Exact, when every segment is</b> — which is the ordinary case, because every curve type a
    /// polycurve usually holds converts exactly. The segments are converted, raised to a common
    /// degree, and joined into one curve; nothing is sampled and nothing is fitted.
    /// </para>
    /// <para>
    /// <b>A segment that cannot convert exactly makes the whole thing approximate</b>, and the
    /// member says so rather than claiming what one segment cannot support. A <see cref="Helix"/>
    /// is the case: it is <i>provably</i> not a NURBS curve at any degree
    /// ([N165](../../docs/NOTES.md)), so a polycurve containing one falls back to the base
    /// implementation's sampling and reports <see cref="NurbsConversion.IsExact"/> as
    /// <see langword="false"/>.
    /// </para>
    /// <para>
    /// <b>The seam keeps its corner.</b> At each join the knot is repeated <c>degree</c> times —
    /// one fewer than the <c>degree + 1</c> that clamps the end of a standalone curve. That is
    /// exactly the multiplicity that makes a curve continuous and not smooth, which is what a
    /// polycurve's joins are: repeating it once more would split the curve into two, and once fewer
    /// would round off a corner the user drew.
    /// </para>
    /// <para>
    /// <b>The result is exact and not minimal.</b> Raising a line to the degree of the arc beside it
    /// describes the same straight line with more control points than it needs, and nothing here
    /// lowers it again — which is <see cref="NurbsCurve.WithDegreeElevated"/>'s own trade, made for
    /// the same reason: removing them needs knot removal, and knot removal has a tolerance question
    /// in it that does not belong inside an operation whose whole promise is that it changes
    /// nothing.
    /// </para>
    /// </remarks>
    public override NurbsConversion ToNurbsCurve(in Tolerance tolerance = default)
    {
        NurbsCurve[] pieces = new NurbsCurve[_segments.Length];
        int degree = 1;
        bool preserved = true;

        for (int index = 0; index < _segments.Length; index++)
        {
            NurbsConversion converted = _segments[index].ToNurbsCurve(tolerance);

            // One inexact segment is enough. A Helix cannot be a NURBS curve at all, so a polycurve
            // holding one has no exact form either, and the honest answer is the base's sampling.
            // An unclamped segment - a periodic curve used as a piece of a chain - is refused here
            // for a different reason: the join arithmetic below assumes clamped ends, and quietly
            // producing a wrong curve would be worse than an approximate right one.
            if (!converted.IsExact || !converted.Curve.Knots.IsClamped)
            {
                return base.ToNurbsCurve(tolerance);
            }

            pieces[index] = converted.Curve;
            degree = Math.Max(degree, converted.Curve.Degree);
            preserved &= converted.PreservesParameterisation;
        }

        for (int index = 0; index < pieces.Length; index++)
        {
            if (pieces[index].Degree < degree)
            {
                pieces[index] = pieces[index].WithDegreeElevated(degree - pieces[index].Degree);
            }
        }

        // Each piece lands on its own unit of the domain by the same affine map the polycurve
        // itself uses, so the joined curve keeps the parameterisation exactly when every piece did.
        return new NurbsConversion(Join(pieces, degree), true, preserved);
    }

    /// <summary>Joins clamped curves of one degree end to end, over this polycurve's domain.</summary>
    /// <param name="pieces">The converted segments, all of <paramref name="degree"/>.</param>
    /// <param name="degree">The common degree.</param>
    /// <returns>One curve.</returns>
    /// <remarks>
    /// <para>
    /// <b>Each segment is reparameterised onto its own unit of the domain</b>, because a polycurve
    /// runs one unit per segment (<see cref="Domain"/>) and each converted piece arrives spanning
    /// whatever its own type used. Sliding and scaling a knot vector is an affine
    /// reparameterisation and leaves the curve alone.
    /// </para>
    /// <para>
    /// <b>The counting is where this goes wrong if it goes wrong.</b> Two clamped degree-<c>p</c>
    /// curves of <c>n₁</c> and <c>n₂</c> control points join to one of <c>n₁ + n₂ − 1</c> — the
    /// shared endpoint counted once — which needs <c>n₁ + n₂ + p</c> knots. Taking all of the first
    /// vector and all but the leading <c>p + 1</c> of the second gives one too many; dropping the
    /// first vector's <i>final</i> knot lands it, <b>and is the same edit that leaves the seam at
    /// multiplicity <c>p</c></b> rather than <c>p + 1</c>. The arithmetic and the continuity are
    /// one correction, which is why neither can be got right without the other.
    /// </para>
    /// </remarks>
    private static NurbsCurve Join(NurbsCurve[] pieces, int degree)
    {
        List<Point3d> controlPoints = [];
        List<double> weights = [];
        List<double> knots = [];
        bool rational = false;

        for (int index = 0; index < pieces.Length; index++)
        {
            NurbsCurve piece = pieces[index];
            Point3d[] points = piece.ControlPoints();
            double[] pieceWeights = piece.Weights();
            double[] pieceKnots = piece.Knots.ToArray();

            rational |= piece.IsRational;

            double first = pieceKnots[0];
            double span = pieceKnots[^1] - first;

            // The shared endpoint is the previous segment's last control point, so this one's first
            // is dropped - and with it the clamp that would otherwise make the seam a split.
            int skipPoints = index == 0 ? 0 : 1;
            int skipKnots = index == 0 ? 0 : degree + 1;

            if (index > 0)
            {
                knots.RemoveAt(knots.Count - 1);
            }

            for (int i = skipPoints; i < points.Length; i++)
            {
                controlPoints.Add(points[i]);
                weights.Add(pieceWeights[i]);
            }

            for (int i = skipKnots; i < pieceKnots.Length; i++)
            {
                knots.Add(index + (span > 0.0 ? (pieceKnots[i] - first) / span : 0.0));
            }
        }

        return new NurbsCurve(
            controlPoints, new KnotVector(degree, knots), rational ? weights : null);
    }

    /// <summary>
    /// Returns the chain with every corner rounded to a fillet of the same radius (`E2-T72`).
    /// </summary>
    /// <param name="radius">The fillet radius. Positive.</param>
    /// <param name="tolerance">
    /// The tolerance for finding the plane, the corners and the offsets. Defaults to the ambient one.
    /// </param>
    /// <returns>
    /// The rounded chain. A polycurve of one segment has no corners and is returned unchanged.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> is not positive and finite.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The chain is not planar, so there is no plane for its fillets to lie in.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>This is <see cref="CurveOffset.Fillet"/> over every join, and the chain is the whole of
    /// the work.</b> Each call trims <i>both</i> of the curves it is given, so the segment between
    /// two rounded corners is trimmed <b>twice</b> — and the second trim must act on the result of
    /// the first rather than on the original segment. The joins are therefore walked in order with
    /// the running trimmed segment carried forward. Rounding each corner against the original
    /// neighbours and reassembling afterwards produces a chain that does not join, and it fails
    /// most quietly on the segments that are shortest relative to the radius.
    /// </para>
    /// <para>
    /// <b>The plane is inferred here, where <see cref="CurveOffset.Fillet"/> has to ask for it.</b>
    /// That member takes a normal because two <i>straight</i> curves have no plane of their own to
    /// read; a chain of segments does, and <see cref="Curve.PlaneOf(in Tolerance)"/> supplies it.
    /// Which way the fitted normal points does not matter — the fillet search tries both sides of
    /// each curve anyway.
    /// </para>
    /// <para>
    /// <b>A corner too tight for the radius is left sharp rather than throwing the chain away.</b>
    /// A fillet of radius ten does not fit a corner two across, and a twenty-corner chain with one
    /// such corner should come back with nineteen rounded — a partial fillet is what the caller
    /// wants and an exception is not. A radius that fits nowhere therefore returns the chain
    /// unchanged, which is the same rule taken to its limit rather than a separate case.
    /// </para>
    /// <para>
    /// <b>A closed chain has one more corner than an open one</b>, the join from the last segment
    /// back to the first, and that one is not symmetric with the others: by the time it is reached
    /// the first segment has already been trimmed at its far end, so rounding the wrap replaces the
    /// front of the result rather than appending to the back of it.
    /// </para>
    /// <para>
    /// <b>A rounded closed chain closes to within rounding, and <see cref="Curve.IsClosed"/> will
    /// still say <see langword="false"/>.</b> That property is exact equality by doctrine — Spark's
    /// tolerance is passed rather than ambient (ADR-0010), so a parameterless property cannot ask a
    /// tolerant question — and the closed factories elsewhere keep it true by <i>repeating</i> the
    /// first point rather than by arithmetic. A fillet has no such option: both ends of the wrap
    /// are evaluated, from a trim parameter at one end and an arc's sweep at the other, so they
    /// agree to a few ulps and not to the bit. Measured on a rounded square the gap is under
    /// 1e-15. A caller who wants the tolerant answer compares <see cref="Curve.StartPoint"/> and
    /// <see cref="Curve.EndPoint"/> with the tolerance they mean, which is what
    /// <see cref="PolyLine"/> already tells them.
    /// </para>
    /// <para>
    /// <b>Dynamo's <c>PolyCurve.Fillet</c> carries a second, flag argument whose meaning its
    /// signature does not give</b> (§6.3 of the Dynamo coverage register). The capability is the
    /// radius; the flag is not guessed at here.
    /// </para>
    /// </remarks>
    public PolyCurve Filleted(double radius, in Tolerance tolerance = default)
    {
        // BEFORE the loop, and deliberately. The loop swallows ArgumentException to skip a corner
        // that cannot take the radius, and ArgumentOutOfRangeException IS an ArgumentException —
        // so a validation left inside would read a bad radius as every corner being too tight and
        // hand back the chain unchanged instead of saying what was wrong.
        if (!double.IsFinite(radius) || radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius), radius, "A fillet radius must be positive and finite.");
        }

        if (_segments.Length < 2)
        {
            return this;
        }

        Plane plane = PlaneOf(tolerance)
            ?? throw new InvalidOperationException(
                "A polycurve can only be filleted in a plane, and this chain is not planar. "
                + "Fillet its corners individually with CurveOffset.Fillet, which takes the plane "
                + "normal for each one.");

        Vector3d normal = plane.Normal;

        List<Curve> rounded = [];
        Curve current = _segments[0];

        for (int index = 1; index < _segments.Length; index++)
        {
            if (TryRound(
                current, _segments[index], radius, normal, tolerance,
                out Arc? arc, out Curve? before, out Curve? after))
            {
                rounded.Add(before!);
                rounded.Add(arc!);
                current = after!;
            }
            else
            {
                rounded.Add(current);
                current = _segments[index];
            }
        }

        // The wrap. `current` is the running last segment and `rounded[0]` is the first, already
        // trimmed where the very first corner took a bite out of its end.
        if (IsClosed
            && TryRound(
                current, rounded[0], radius, normal, tolerance,
                out Arc? wrap, out Curve? last, out Curve? first))
        {
            rounded[0] = first!;
            rounded.Add(last!);
            rounded.Add(wrap!);
        }
        else
        {
            rounded.Add(current);
        }

        return FromJoinedCurves(rounded, tolerance);
    }

    /// <summary>
    /// Returns an open chain closed by an arc, a straight run and a second arc, all tangent
    /// (`E2-T72`).
    /// </summary>
    /// <param name="startRadius">The radius of the arc that arrives at the chain's start. Positive.</param>
    /// <param name="endRadius">The radius of the arc that leaves the chain's end. Positive.</param>
    /// <param name="tolerance">
    /// The tolerance for finding the plane and for testing the closure against the chain.
    /// </param>
    /// <returns>The closed chain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either radius is not positive and finite.</exception>
    /// <exception cref="InvalidOperationException">
    /// The chain is already closed, it is not planar, or no closure of those two radii exists —
    /// which is what radii too large for the gap look like.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>This is not a fillet, and the difference is the whole construction.</b> A fillet rounds a
    /// corner that exists; this closes a gap that has <i>no corner in it</i>. The chain arrives at
    /// its end travelling in some direction and has to leave again, turn, run straight, turn again
    /// and arrive at its own start travelling in the direction the chain sets off in — so the
    /// unknowns are which side of each end its arc's centre sits on, and the constraint is that one
    /// straight line is tangent to both arcs. It is the Dubins <i>curve-straight-curve</i> path
    /// with two different radii.
    /// </para>
    /// <para>
    /// <b>There are exactly four candidates, not the sixteen the shape of the problem suggests.</b>
    /// Each centre is on one of two sides, which is four combinations — and for each combination
    /// the tangent line is <b>unique</b> rather than one of the four a pair of circles generally
    /// has. Writing <c>v</c> for the vector between the two centres and <c>k</c> for the signed
    /// difference of the radii, the direction of travel <c>d</c> must satisfy
    /// <c>v·(n×d) = k</c> and <c>v·d = L</c> with the run length <c>L</c> not negative, and that
    /// second condition picks one of the two roots. A combination is feasible when
    /// <c>|k| ≤ |v|</c>.
    /// </para>
    /// <para>
    /// <b>The shortest closure is the wrong rule, and it is worth saying why, because it is the
    /// obvious one.</b> Shortest is Dubins' answer to Dubins' question — the least distance a
    /// vehicle travels — and this is closing an <i>outline</i>. The shortest closure is free to cut
    /// straight through the shape it is closing. So the candidates that do not cross the chain are
    /// preferred, and the shortest of <i>those</i> is taken. When every candidate crosses, the
    /// shortest is returned anyway: a crossing closure is still an answer and an exception is not.
    /// </para>
    /// <para>
    /// <b>The crossing test is exact and not sampled</b>, run against the chain's own segments
    /// through <see cref="Curve.IntersectWith(Curve, in Tolerance)"/>. Contacts at the chain's own
    /// start and end are excluded by proximity, because the closure meets it at both of those
    /// points <i>by construction</i> and finding them proves nothing.
    /// </para>
    /// <para>
    /// <b>As with <see cref="Filleted"/>, the result closes to within rounding and
    /// <see cref="Curve.IsClosed"/> will still say <see langword="false"/></b> — closure is exact
    /// equality by doctrine (ADR-0010), and every point of this construction is evaluated.
    /// </para>
    /// </remarks>
    public PolyCurve ClosedWithLineAndTangentArcs(
        double startRadius, double endRadius, in Tolerance tolerance = default)
    {
        CheckClosureRadius(startRadius, nameof(startRadius));
        CheckClosureRadius(endRadius, nameof(endRadius));

        if (IsClosed)
        {
            throw new InvalidOperationException(
                "This chain is already closed, so there is no gap to close.");
        }

        Plane plane = PlaneOf(tolerance)
            ?? throw new InvalidOperationException(
                "A chain can only be closed with tangent arcs in a plane, and this one is not "
                + "planar.");

        Vector3d normal = plane.Normal;
        Point3d start = StartPoint;
        Point3d end = EndPoint;
        Vector3d arriving = TangentAt(Domain.Min);
        Vector3d leaving = TangentAt(Domain.Max);

        List<Curve>? best = null;
        double bestLength = double.MaxValue;
        bool bestIsClear = false;

        foreach (int endSide in (int[])[1, -1])
        {
            foreach (int startSide in (int[])[1, -1])
            {
                if (!TryClosure(
                    end, leaving, endRadius, endSide,
                    start, arriving, startRadius, startSide,
                    normal, out List<Curve>? candidate, out double length))
                {
                    continue;
                }

                bool clear = !CrossesAnything(candidate!, _segments, tolerance);

                // A candidate that keeps clear of the chain beats one that does not, whatever their
                // lengths; between two of the same kind the shorter wins.
                if ((clear && !bestIsClear) || (clear == bestIsClear && length < bestLength))
                {
                    best = candidate;
                    bestLength = length;
                    bestIsClear = clear;
                }
            }
        }

        if (best is null)
        {
            throw new InvalidOperationException(
                "No closure of those two radii exists for this chain. The arcs are too large for "
                + "the gap between its ends, which is what makes every one of the four candidates "
                + "infeasible at once.");
        }

        List<Curve> closed = [.. _segments];
        closed.AddRange(best);

        return FromJoinedCurves(closed, tolerance);
    }

    /// <summary>One of the four candidate closures, or the news that it does not exist.</summary>
    /// <param name="end">Where the chain ends.</param>
    /// <param name="leaving">The direction it is travelling there.</param>
    /// <param name="endRadius">The radius of the arc that leaves it.</param>
    /// <param name="endSide">Which side of the end its centre sits on, as a sign.</param>
    /// <param name="start">Where the chain starts.</param>
    /// <param name="arriving">The direction it sets off in there.</param>
    /// <param name="startRadius">The radius of the arc that arrives at it.</param>
    /// <param name="startSide">Which side of the start its centre sits on, as a sign.</param>
    /// <param name="normal">The unit plane normal.</param>
    /// <param name="pieces">The arc, the run and the second arc, in order.</param>
    /// <param name="length">Their total length, which is what candidates are ranked by.</param>
    /// <returns><see langword="false"/> when this combination of sides has no closure.</returns>
    /// <remarks>
    /// <b>A degenerate piece is dropped rather than built.</b> The straight run vanishes when the
    /// two arcs touch, and an arc vanishes when its tangent point is already its end point —
    /// building either would make a zero-length segment, which is not a curve.
    /// </remarks>
    private static bool TryClosure(
        in Point3d end,
        in Vector3d leaving,
        double endRadius,
        int endSide,
        in Point3d start,
        in Vector3d arriving,
        double startRadius,
        int startSide,
        in Vector3d normal,
        out List<Curve>? pieces,
        out double length)
    {
        pieces = null;
        length = double.MaxValue;

        Point3d fromCentre = end + (normal.Cross(leaving) * (endSide * endRadius));
        Point3d toCentre = start + (normal.Cross(arriving) * (startSide * startRadius));

        Vector3d between = toCentre - fromCentre;
        double signedRadii = (startSide * startRadius) - (endSide * endRadius);
        double span = between.Length;

        if (span <= 0.0 || Math.Abs(signedRadii) > span)
        {
            // The arcs are too large for the gap: the tangent line the two would need does not
            // exist, which is this combination of sides having no closure at all.
            return false;
        }

        // d turns v through the angle whose sine is -k/|v|, on the branch where the cosine is
        // positive so that the run goes forwards rather than backwards.
        Vector3d alongSpan = between / span;
        Vector3d acrossSpan = normal.Cross(alongSpan);
        double sine = -signedRadii / span;
        double cosine = Math.Sqrt(Math.Max(0.0, 1.0 - (sine * sine)));
        Vector3d travel = (alongSpan * cosine) + (acrossSpan * sine);

        Vector3d offset = normal.Cross(travel);
        Point3d leavesAt = fromCentre - (offset * (endSide * endRadius));
        Point3d arrivesAt = toCentre - (offset * (startSide * startRadius));

        double run = between.Dot(travel);

        if (run < 0.0)
        {
            return false;
        }

        pieces = [];
        length = 0.0;

        if (!AddTurn(pieces, ref length, fromCentre, end, leavesAt, normal * endSide, endRadius))
        {
            return false;
        }

        if (run > 0.0)
        {
            pieces.Add(new Line(leavesAt, arrivesAt));
            length += run;
        }

        return AddTurn(pieces, ref length, toCentre, arrivesAt, start, normal * startSide, startRadius);
    }

    /// <summary>Adds one of the two turns, unless it has nothing to turn through.</summary>
    /// <param name="pieces">The pieces so far.</param>
    /// <param name="length">Their running total length.</param>
    /// <param name="centre">The arc's centre.</param>
    /// <param name="from">Where the arc begins.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="axis">The rotation axis, whose sign is the direction of the turn.</param>
    /// <param name="radius">The arc's radius.</param>
    /// <returns><see langword="false"/> when the arc cannot be built at all.</returns>
    private static bool AddTurn(
        List<Curve> pieces,
        ref double length,
        in Point3d centre,
        in Point3d from,
        in Point3d to,
        in Vector3d axis,
        double radius)
    {
        if (!(from - centre).TryNormalise(out Vector3d atStart)
            || !(to - centre).TryNormalise(out Vector3d atEnd))
        {
            return false;
        }

        // Measured in the direction of the turn, so a turn of more than a half circle is a sweep of
        // more than pi rather than the smaller angle the other way round.
        double swept = Math.Atan2(axis.Cross(atStart).Dot(atEnd), atStart.Dot(atEnd));

        if (swept < 0.0)
        {
            swept += 2.0 * Math.PI;
        }

        if (swept == 0.0)
        {
            return true;
        }

        pieces.Add(Arc.FromCenterStartPointSweepAngle(centre, from, axis, Angle.FromRadians(swept)));
        length += radius * swept;
        return true;
    }

    /// <summary>
    /// Whether a candidate closure crosses the chain, or crosses itself.
    /// </summary>
    /// <param name="closure">The candidate's pieces, in order.</param>
    /// <param name="segments">The chain's own segments.</param>
    /// <param name="tolerance">The tolerance for the intersections and for the exclusion.</param>
    /// <returns><see langword="true"/> when the closed outline would cross itself.</returns>
    /// <remarks>
    /// <para>
    /// <b>Both halves of that question matter, and the second one is easy to forget.</b> A closure
    /// that keeps clear of the chain can still cross <i>itself</i> — its first arc can sweep round
    /// far enough to meet its second — and a candidate that does is no better than one that cuts
    /// through the shape.
    /// </para>
    /// <para>
    /// <b>Only the closure's pieces are compared, never the chain against itself</b>, which keeps
    /// this linear in the length of the chain rather than quadratic. A chain that already crossed
    /// itself before this member ran is the caller's, and closing it is not the moment to object.
    /// </para>
    /// <para>
    /// <b>Curves that share an end are meant to meet there</b>, so a hit that sits on an end of
    /// both is not a crossing. That rule needs no index bookkeeping and says exactly what it means:
    /// pieces join end to end, and anything else they do to each other is a crossing.
    /// </para>
    /// </remarks>
    private static bool CrossesAnything(List<Curve> closure, Curve[] segments, in Tolerance tolerance)
    {
        double excluded = Math.Max(tolerance.Linear, 1e-9);

        for (int index = 0; index < closure.Count; index++)
        {
            Curve piece = closure[index];

            foreach (Curve other in segments)
            {
                if (Meet(piece, other, excluded, tolerance))
                {
                    return true;
                }
            }

            for (int later = index + 1; later < closure.Count; later++)
            {
                if (Meet(piece, closure[later], excluded, tolerance))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether two curves meet anywhere but at an end they share.</summary>
    /// <param name="first">The first curve.</param>
    /// <param name="second">The second.</param>
    /// <param name="excluded">How near an end a hit may be and still count as a join.</param>
    /// <param name="tolerance">The intersection tolerance.</param>
    /// <returns><see langword="true"/> when they cross.</returns>
    private static bool Meet(Curve first, Curve second, double excluded, in Tolerance tolerance)
    {
        foreach (CurveIntersectionPoint hit in first.IntersectWith(second, tolerance).Points)
        {
            if (!AtAnEndOf(first, hit.Point, excluded) || !AtAnEndOf(second, hit.Point, excluded))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a point sits on one of a curve's two ends.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="point">The point.</param>
    /// <param name="excluded">How near counts as on.</param>
    /// <returns>Whether it does.</returns>
    private static bool AtAnEndOf(Curve curve, in Point3d point, double excluded) =>
        curve.StartPoint.DistanceTo(point) <= excluded || curve.EndPoint.DistanceTo(point) <= excluded;

    /// <summary>Rejects a closure radius that is not a radius.</summary>
    /// <param name="radius">The radius.</param>
    /// <param name="name">Which parameter it came from.</param>
    private static void CheckClosureRadius(double radius, string name)
    {
        if (!double.IsFinite(radius) || radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                name, radius, "A closing arc's radius must be positive and finite.");
        }
    }

    /// <summary>One corner rounded, or the news that this radius does not fit it.</summary>
    /// <param name="first">The curve the fillet leaves, already trimmed at its other end.</param>
    /// <param name="second">The curve it arrives at.</param>
    /// <param name="radius">The fillet radius.</param>
    /// <param name="normal">The plane normal.</param>
    /// <param name="tolerance">The tolerance.</param>
    /// <param name="arc">The fillet.</param>
    /// <param name="before">The first curve, trimmed back to the fillet.</param>
    /// <param name="after">The second curve, trimmed forward to it.</param>
    /// <returns><see langword="false"/> when no fillet of that radius fits the corner.</returns>
    /// <remarks>
    /// <para>
    /// <b>The refusal is <see cref="CurveOffset.Fillet"/>'s own and is not re-derived here.</b> It
    /// throws when the two curves do not cross, when they are not coplanar, and when their offsets
    /// never meet — which is what a radius too large for the corner looks like. All three mean the
    /// same thing to a chain: leave this corner alone.
    /// </para>
    /// <para>
    /// <b>The two trimmed pieces come back running <i>away from the corner</i>, and only one of
    /// those directions is the chain's.</b> That is the right contract for a pair of curves — it
    /// keeps the answer independent of which way the caller happened to draw the second one — but
    /// it means the second piece arrives running backwards along the chain, and joining it as it
    /// stands leaves a gap the width of the whole segment. So each piece is turned to meet its arc:
    /// the first must <i>end</i> where the fillet starts and the second must <i>start</i> where it
    /// finishes, which is the definition of a fillet rather than an assumption about direction.
    /// </para>
    /// </remarks>
    private static bool TryRound(
        Curve first,
        Curve second,
        double radius,
        in Vector3d normal,
        in Tolerance tolerance,
        out Arc? arc,
        out Curve? before,
        out Curve? after)
    {
        try
        {
            (Arc fillet, Curve trimmedFirst, Curve trimmedSecond) =
                CurveOffset.Fillet(first, second, radius, normal, tolerance);

            arc = fillet;
            before = TurnedTowards(trimmedFirst, fillet.StartPoint, atItsEnd: true);
            after = TurnedTowards(trimmedSecond, fillet.EndPoint, atItsEnd: false);
            return true;
        }
        catch (ArgumentException)
        {
            arc = null;
            before = null;
            after = null;
            return false;
        }
    }

    /// <summary>A curve turned so that the named end of it is the one nearest a point.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="meetingPoint">The point it has to meet.</param>
    /// <param name="atItsEnd">
    /// <see langword="true"/> when the curve must <i>end</i> at the point, <see langword="false"/>
    /// when it must <i>start</i> there.
    /// </param>
    /// <returns>The curve, or its reverse.</returns>
    private static Curve TurnedTowards(Curve curve, in Point3d meetingPoint, bool atItsEnd)
    {
        double atStart = curve.StartPoint.DistanceTo(meetingPoint);
        double atEnd = curve.EndPoint.DistanceTo(meetingPoint);

        return (atItsEnd ? atEnd <= atStart : atStart <= atEnd) ? curve : curve.Reversed();
    }

    /// <inheritdoc/>
    public override Curve Reversed()
    {
        Curve[] reversed = new Curve[_segments.Length];
        for (int index = 0; index < _segments.Length; index++)
        {
            reversed[index] = _segments[^(index + 1)].Reversed();
        }

        return new PolyCurve(reversed);
    }

    /// <inheritdoc/>
    public override Curve Trimmed(in Interval domain)
    {
        CheckTrimDomain(domain, Domain);
        Interval increasing = domain.MakeIncreasing();
        (int firstSegment, double firstLocal) = Locate(increasing.Min);
        (int lastSegment, double lastLocal) = Locate(increasing.Max);

        List<Curve> kept = [];
        if (firstSegment == lastSegment)
        {
            kept.Add(TrimSegment(_segments[firstSegment], firstLocal, lastLocal));
        }
        else
        {
            if (firstLocal < 1.0)
            {
                kept.Add(TrimSegment(_segments[firstSegment], firstLocal, 1.0));
            }

            for (int index = firstSegment + 1; index < lastSegment; index++)
            {
                kept.Add(_segments[index]);
            }

            if (lastLocal > 0.0)
            {
                kept.Add(TrimSegment(_segments[lastSegment], 0.0, lastLocal));
            }
        }

        PolyCurve trimmed = new([.. kept]);
        return domain.IsDecreasing ? trimmed.Reversed() : trimmed;
    }

    /// <inheritdoc/>
    public override Curve TransformedBy(in Transform transform)
    {
        Curve[] mapped = new Curve[_segments.Length];
        for (int index = 0; index < _segments.Length; index++)
        {
            mapped[index] = _segments[index].TransformedBy(transform);
        }

        return new PolyCurve(mapped);
    }

    /// <summary>A readable description of the polycurve, for diagnostics.</summary>
    /// <returns>The segment count and whether it is closed.</returns>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"PolyCurve({_segments.Length} segments, {(IsClosed ? "closed" : "open")})");

    /// <inheritdoc/>
    protected override double ComputeLength() => Cumulative()[^1];

    /// <inheritdoc/>
    protected override BoundingBox ComputeBoundingBox()
    {
        BoundingBox box = _segments[0].BoundingBox;
        for (int index = 1; index < _segments.Length; index++)
        {
            box = box.Union(_segments[index].BoundingBox);
        }

        return box;
    }

    /// <inheritdoc/>
    protected override Point3d Evaluate(double parameter)
    {
        (int segment, double local) = Locate(parameter);
        Curve curve = _segments[segment];
        return curve.PointAt(curve.Domain.Denormalise(local));
    }

    /// <inheritdoc/>
    protected override Vector3d EvaluateDerivative(double parameter)
    {
        (int segment, double local) = Locate(parameter);
        Curve curve = _segments[segment];

        // The chain rule: one unit of polycurve parameter covers the whole of a segment's domain, so
        // the segment's derivative is scaled by that domain's length. Forgetting this would leave
        // every tangent direction right and every arc-length integral wrong by a constant factor.
        return curve.DerivativeWithin(curve.Domain.Denormalise(local)) * curve.Domain.Length;
    }

    /// <inheritdoc/>
    protected override Vector3d EvaluateSecondDerivative(double parameter)
    {
        (int segment, double local) = Locate(parameter);
        Curve curve = _segments[segment];
        double length = curve.Domain.Length;
        return curve.SecondDerivativeWithin(curve.Domain.Denormalise(local)) * (length * length);
    }

    private static Curve TrimSegment(Curve segment, double fromLocal, double toLocal) =>
        fromLocal <= 0.0 && toLocal >= 1.0
            ? segment
            : segment.Trimmed(
                new Interval(
                    segment.Domain.Denormalise(fromLocal), segment.Domain.Denormalise(toLocal)));

    private (int Segment, double Local) Locate(double parameter)
    {
        int last = _segments.Length - 1;
        int segment = (int)Math.Floor(parameter);
        if (segment < 0)
        {
            return (0, 0.0);
        }

        if (segment > last)
        {
            return (last, 1.0);
        }

        return (segment, parameter - segment);
    }

    private double[] Cumulative()
    {
        if (_cumulative is not null)
        {
            return _cumulative;
        }

        double[] cumulative = new double[_segments.Length + 1];
        double running = 0.0;
        for (int index = 0; index < _segments.Length; index++)
        {
            running += _segments[index].Length;
            cumulative[index + 1] = running;
        }

        _cumulative = cumulative;
        return cumulative;
    }
}
