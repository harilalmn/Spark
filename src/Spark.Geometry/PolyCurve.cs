using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A chain of curves joined end to end, of any types in any mixture.
/// </summary>
/// <remarks>
/// <para>
/// <b>Parameterisation.</b> The domain is <c>[0, SegmentCount]</c> and the parameter counts
/// segments: segment <c>i</c> occupies <c>[i, i + 1]</c>, and the fractional part is mapped
/// linearly onto that segment's own domain. As with <see cref="PolyLine"/> the parameter is
/// therefore <b>not</b> arc length, and for the same reason — every joint sits at an exactly
/// representable parameter, which makes splitting at a joint exact.
/// </para>
/// <para>
/// <b>Contiguity is checked once, at construction.</b> Each segment's end point must coincide
/// with the next segment's start point within the tolerance passed in. Nothing afterwards
/// re-checks it, and nothing can invalidate it, because every curve involved is immutable.
/// </para>
/// <para>
/// <b>Nesting is allowed and is not flattened.</b> A <see cref="PolyCurve"/> may be a segment of
/// another one. Flattening would silently renumber the segments and change the domain, which
/// would break any parameter a caller was holding across the call.
/// </para>
/// </remarks>
public sealed class PolyCurve : Curve
{
    private readonly Curve[] _segments;

    /// <summary>
    /// Creates a polycurve from a sequence of curves joined end to end.
    /// </summary>
    /// <param name="segments">
    /// The curves, in order. Copied; the list is not retained. At least one is needed, none may
    /// be <see langword="null"/>, and each one's end point must meet the next one's start point
    /// within <paramref name="tolerance"/>.
    /// </param>
    /// <param name="tolerance">
    /// The tolerance the joints are checked against. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>. This is the only place the joints are checked, so a
    /// loose tolerance here is a promise the rest of the type takes at face value.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when the list, or any curve in it, is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the list is empty, or when two consecutive curves do not meet. The message
    /// names the joint and the gap, because "curves are not contiguous" on a chain of forty
    /// segments is not a message anyone can act on.
    /// </exception>
    public PolyCurve(IReadOnlyList<Curve> segments, in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count == 0)
        {
            throw new ArgumentException("A polycurve needs at least one segment.", nameof(segments));
        }

        _segments = new Curve[segments.Count];

        for (int i = 0; i < segments.Count; i++)
        {
            _segments[i] = segments[i] ?? throw new ArgumentNullException(
                nameof(segments),
                $"Segment {i} is null.");

            if (i > 0)
            {
                Point3d end = _segments[i - 1].EndPoint;
                Point3d start = _segments[i].StartPoint;

                if (!end.EqualsWithin(start, tolerance))
                {
                    throw new ArgumentException(
                        $"Segment {i - 1} ends at {end} but segment {i} starts at {start}, a gap of "
                        + $"{end.DistanceTo(start)}. A polycurve's segments must meet.",
                        nameof(segments));
                }
            }
        }
    }

    /// <summary>
    /// Creates a polycurve without re-checking contiguity, for results derived from a polycurve
    /// that has already been checked.
    /// </summary>
    /// <param name="segments">The segments, already known to be contiguous.</param>
    private PolyCurve(Curve[] segments)
    {
        _segments = segments;
    }

    /// <summary>How many segments the polycurve has. Always at least one.</summary>
    public int SegmentCount => _segments.Length;

    /// <inheritdoc/>
    /// <remarks>
    /// The domain is <c>[0, SegmentCount]</c>: segment <c>i</c> occupies <c>[i, i + 1]</c>.
    /// </remarks>
    public override Interval Domain => new(0.0, _segments.Length);

    /// <inheritdoc/>
    /// <remarks>
    /// <see langword="true"/> when the first segment's start point and the last segment's end
    /// point are exactly coincident. Note the asymmetry with the joints, which are checked
    /// within a tolerance: a chain that closes to within the join tolerance but not exactly
    /// reports <see langword="false"/> here, which is the honest answer to an exact question.
    /// </remarks>
    public override bool IsClosed => _segments[0].StartPoint == _segments[^1].EndPoint;

    /// <inheritdoc/>
    /// <remarks>Always <see langword="false"/>: a polycurve has a seam even when it closes.</remarks>
    public override bool IsPeriodic => false;

    /// <inheritdoc/>
    /// <remarks>The union of the segments' boxes, so it is tight exactly when theirs are.</remarks>
    public override BoundingBox BoundingBox
    {
        get
        {
            BoundingBox box = _segments[0].BoundingBox;

            for (int i = 1; i < _segments.Length; i++)
            {
                box = box.Union(_segments[i].BoundingBox);
            }

            return box;
        }
    }

    /// <summary>
    /// Creates a polycurve from a sequence of curves joined end to end. The factory form of the
    /// equivalent constructor.
    /// </summary>
    /// <param name="segments">The curves, in order.</param>
    /// <param name="tolerance">The tolerance the joints are checked against.</param>
    /// <returns>The polycurve.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the list or any curve in it is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the list is empty or the curves do not meet.</exception>
    public static PolyCurve ByJoinedCurves(IReadOnlyList<Curve> segments, in Tolerance tolerance = default) =>
        new(segments, tolerance);

    /// <summary>
    /// One of the polycurve's segments.
    /// </summary>
    /// <param name="index">
    /// The segment index, from zero to <see cref="SegmentCount"/> minus one. Segment <c>i</c>
    /// covers the parameters <c>[i, i + 1]</c>.
    /// </param>
    /// <returns>The segment, which is the same immutable curve that was passed in.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is out of range.</exception>
    public Curve SegmentAt(int index)
    {
        if (index < 0 || index >= _segments.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The segment index must be between zero and {_segments.Length - 1}.");
        }

        return _segments[index];
    }

    /// <inheritdoc/>
    public override Point3d PointAt(double parameter)
    {
        (int index, double local) = Locate(ClampParameter(parameter));

        return _segments[index].PointAt(_segments[index].Domain.Denormalise(local));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The segment's own derivative, scaled by the chain rule: this curve's parameter runs
    /// across a segment in one unit whatever the segment's own domain is, so the <c>k</c>-th
    /// derivative is the segment's multiplied by the <c>k</c>-th power of its domain length.
    /// <b>Derivatives are discontinuous at every joint</b> unless the segments happen to meet
    /// smoothly, and the value at an integer parameter is that of the segment after it.
    /// </remarks>
    public override Vector3d DerivativeAt(double parameter, int order)
    {
        ThrowIfOrderIsNegative(order);

        double t = ClampParameter(parameter);

        if (order == 0)
        {
            return (Vector3d)PointAt(t);
        }

        (int index, double local) = Locate(t);
        Curve segment = _segments[index];
        double span = segment.Domain.Length;

        return segment.DerivativeAt(segment.Domain.Denormalise(local), order) * Math.Pow(span, order);
    }

    /// <inheritdoc/>
    /// <remarks>The sum of the segments' lengths, each computed to the tolerance passed.</remarks>
    public override double Length(in Tolerance tolerance = default)
    {
        double total = 0.0;

        for (int i = 0; i < _segments.Length; i++)
        {
            total += _segments[i].Length(tolerance);
        }

        return total;
    }

    /// <inheritdoc/>
    /// <remarks>Whole segments plus the part of the one the parameter lands in.</remarks>
    public override double LengthAt(double parameter, in Tolerance tolerance = default)
    {
        (int index, double local) = Locate(ClampParameter(parameter));
        double total = 0.0;

        for (int i = 0; i < index; i++)
        {
            total += _segments[i].Length(tolerance);
        }

        Curve segment = _segments[index];

        return total + segment.LengthAt(segment.Domain.Denormalise(local), tolerance);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The segment containing the length is found by walking, then inverted within that segment,
    /// so a chain of analytic segments is inverted exactly rather than by a global solve.
    /// </remarks>
    public override double ParameterAtLength(double length, in Tolerance tolerance = default)
    {
        if (double.IsNaN(length))
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The arc length must not be NaN.");
        }

        if (length <= 0.0)
        {
            return 0.0;
        }

        double remaining = length;

        for (int i = 0; i < _segments.Length; i++)
        {
            Curve segment = _segments[i];
            double segmentLength = segment.Length(tolerance);

            if (remaining <= segmentLength)
            {
                double parameter = segment.ParameterAtLength(remaining, tolerance);

                return i + segment.Domain.Normalise(parameter);
            }

            remaining -= segmentLength;
        }

        return Domain.Max;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Each segment is asked for its own closest point and the best is taken, so a polycurve of
    /// analytic segments answers exactly rather than by sampling.
    /// </remarks>
    public override Point3d ClosestPoint(in Point3d point, out double parameter, in Tolerance tolerance = default)
    {
        if (!point.IsValid)
        {
            throw new ArgumentException("The point must be finite.", nameof(point));
        }

        double bestDistance = double.PositiveInfinity;
        double bestParameter = 0.0;
        Point3d best = _segments[0].StartPoint;

        for (int i = 0; i < _segments.Length; i++)
        {
            Curve segment = _segments[i];
            Point3d candidate = segment.ClosestPoint(point, out double local, tolerance);
            double distance = point.DistanceSquaredTo(candidate);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestParameter = i + segment.Domain.Normalise(local);
                best = candidate;
            }
        }

        parameter = bestParameter;

        return best;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The trimmed polycurve keeps whole segments inside the interval and trims the two the
    /// interval cuts. Its domain is <c>[0, newSegmentCount]</c>, which is not the same length as
    /// the interval trimmed — as for <see cref="PolyLine"/>, the parameter counts segments. When
    /// the interval falls inside a single segment the trimmed segment itself is returned, not a
    /// polycurve wrapping it.
    /// </remarks>
    public override Curve Trim(in Interval interval)
    {
        Interval clipped = ClipToDomain(interval);
        (int firstIndex, double firstLocal) = Locate(clipped.Min);
        (int lastIndex, double lastLocal) = LocateEnd(clipped.Max);

        Curve first = _segments[firstIndex];
        Curve last = _segments[lastIndex];

        if (firstIndex == lastIndex)
        {
            return first.Trim(new Interval(
                first.Domain.Denormalise(firstLocal),
                first.Domain.Denormalise(lastLocal)));
        }

        List<Curve> pieces =
        [
            firstLocal == 0.0
                ? first
                : first.Trim(new Interval(first.Domain.Denormalise(firstLocal), first.Domain.Max)),
        ];

        for (int i = firstIndex + 1; i < lastIndex; i++)
        {
            pieces.Add(_segments[i]);
        }

        pieces.Add(
            lastLocal == 1.0
                ? last
                : last.Trim(new Interval(last.Domain.Min, last.Domain.Denormalise(lastLocal))));

        return new PolyCurve([.. pieces]);
    }

    /// <inheritdoc/>
    /// <remarks>The segments are reversed in order and each one is reversed in itself.</remarks>
    public override PolyCurve Reverse()
    {
        Curve[] segments = new Curve[_segments.Length];

        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = _segments[^(i + 1)].Reverse();
        }

        return new PolyCurve(segments);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every segment is converted, raised to the highest degree among them, and reparameterised
    /// onto its slot in this curve's domain; the knot vectors are then concatenated with the
    /// joint knots at multiplicity <c>degree</c>, which is exactly the multiplicity that
    /// expresses a corner. The result therefore has this curve's domain and its shape, including
    /// its corners, with no approximation anywhere.
    /// </remarks>
    public override NurbsCurve ToNurbsCurve()
    {
        NurbsCurve[] parts = new NurbsCurve[_segments.Length];
        int degree = 1;

        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = _segments[i].ToNurbsCurve();
            degree = Math.Max(degree, parts[i].Degree);
        }

        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i].ElevateDegree(degree).WithDomain(i, i + 1);
        }

        if (parts.Length == 1)
        {
            return parts[0];
        }

        List<Point3d> controlPoints = [];
        List<double> weights = [];
        List<double> knots = [];

        for (int i = 0; i < parts.Length; i++)
        {
            NurbsCurve part = parts[i];
            ReadOnlySpan<Point3d> points = part.ControlPoints;
            ReadOnlySpan<double> partWeights = part.Weights;
            ReadOnlySpan<double> partKnots = part.Knots;

            // The joint control point belongs to the previous segment, which has already
            // contributed it, so every segment after the first skips its own first point.
            for (int j = i == 0 ? 0 : 1; j < points.Length; j++)
            {
                controlPoints.Add(points[j]);
                weights.Add(partWeights[j]);
            }

            // The joint knot appears degree + 1 times in each of the two segments that meet
            // there. Dropping one copy from the earlier segment leaves it at multiplicity
            // degree, which is a corner — exactly the continuity a polycurve joint has.
            int from = i == 0 ? 0 : degree + 1;
            int to = i == parts.Length - 1 ? partKnots.Length - 1 : partKnots.Length - 2;

            for (int j = from; j <= to; j++)
            {
                knots.Add(partKnots[j]);
            }
        }

        return new NurbsCurve(degree, controlPoints, weights, knots);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Each segment is transformed in turn, so segments that keep their type keep it and those
    /// that cannot become NURBS individually rather than dragging the whole chain with them.
    /// </remarks>
    public override PolyCurve Transform(in Transform transform, in Tolerance tolerance = default)
    {
        ValidateTransform(transform, tolerance);

        Curve[] segments = new Curve[_segments.Length];

        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = _segments[i].Transform(transform, tolerance);
        }

        return new PolyCurve(segments);
    }

    /// <summary>
    /// Formats the segment count and whether the chain closes, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>PolyCurve(Segments=3, Closed=False)</c>.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"PolyCurve(Segments={_segments.Length}, Closed={IsClosed})");

    /// <inheritdoc/>
    /// <remarks>Raised in proportion to the segment count, so the planarity fallback samples every segment.</remarks>
    private protected override int SeedCount => Math.Max(DefaultSeedCount, 32 * _segments.Length);

    /// <summary>
    /// Splits a parameter into the segment it lies on and how far along that segment it is.
    /// </summary>
    /// <param name="parameter">The parameter, already clamped into the domain.</param>
    /// <returns>
    /// The segment index and a fraction in <c>[0, 1]</c>. A parameter at an interior joint
    /// reports the segment <i>after</i> the joint with a fraction of zero.
    /// </returns>
    private (int Index, double Local) Locate(double parameter)
    {
        int index = Math.Clamp((int)Math.Floor(parameter), 0, _segments.Length - 1);

        return (index, parameter - index);
    }

    /// <summary>
    /// Splits a parameter into the segment it lies on, resolving a joint in favour of the
    /// segment <i>before</i> it.
    /// </summary>
    /// <param name="parameter">The parameter, already clamped into the domain and above zero.</param>
    /// <returns>The segment index and a fraction in <c>(0, 1]</c>.</returns>
    /// <remarks>
    /// This exists for the upper end of a trim. Resolving forwards there would name the segment
    /// after the joint with a fraction of zero, and trimming that segment to nothing is not a
    /// curve.
    /// </remarks>
    private (int Index, double Local) LocateEnd(double parameter)
    {
        int index = Math.Clamp((int)Math.Ceiling(parameter) - 1, 0, _segments.Length - 1);

        return (index, parameter - index);
    }
}
