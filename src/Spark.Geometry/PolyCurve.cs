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
            int first = points.Count > 0 && points[^1] == part[0] ? 1 : 0;
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
