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
/// <b>The join tolerance is passed, never assumed.</b> <see cref="ByJoinedCurves"/> takes a
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
    public static PolyCurve ByJoinedCurves(
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

    /// <summary>
    /// One seed span per segment, because a join is where this curve's derivative may jump.
    /// </summary>
    /// <remarks>
    /// <see cref="Tessellate(in Tolerance)"/> does not consult this — a polycurve tessellates by
    /// asking each segment. It is here for everything else that subdivides a curve and needs its
    /// pieces to stay inside one segment, and the proximity search behind
    /// <see cref="Curve.ClosestPoint(in Point3d, in Tolerance)"/> is the first of those.
    /// </remarks>
    protected override int TessellationSeedSpans => SegmentCount;

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
