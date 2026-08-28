using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A non-uniform rational B-spline curve of arbitrary degree: the general free-form curve, and
/// the common representation every other curve type can convert to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Always clamped.</b> The knot vector's first and last <c>degree + 1</c> entries are each
/// repeated, so the curve begins at its first control point and ends at its last. Unclamped and
/// periodic knot vectors are not accepted. A closed curve is expressed by making the last
/// control point equal the first, which is what <see cref="Circle.ToNurbsCurve"/> produces, and
/// such a curve reports <see cref="Curve.IsClosed"/> but not <see cref="Curve.IsPeriodic"/>.
/// </para>
/// <para>
/// <b>Rational.</b> Every control point carries a positive weight. A curve whose weights are
/// all one is polynomial, and <see cref="IsRational"/> says so; the evaluation path is the same
/// either way, working in homogeneous coordinates and dividing through at the end.
/// </para>
/// <para>
/// <b>Parameterisation.</b> The domain is <c>[knots[degree], knots[count]]</c> — whatever the
/// knot vector says, not a normalised <c>[0, 1]</c>. That is what lets
/// <see cref="Line.ToNurbsCurve"/> hand back a curve that evaluates identically to the line it
/// came from, and it is why <see cref="Trim(in Interval)"/> has to state what it does to the
/// domain rather than leaving it implied.
/// </para>
/// <para>
/// <b>Immutability.</b> The arrays are copied on construction and are never handed back;
/// <see cref="ControlPoints"/>, <see cref="Weights"/> and <see cref="Knots"/> return
/// <see cref="ReadOnlySpan{T}"/> over the internal storage. Every operation — knot insertion,
/// removal, refinement, degree elevation, trimming — returns a new curve.
/// </para>
/// <para>
/// The algorithms are those of Piegl and Tiller, <i>The NURBS Book</i>: span location and basis
/// functions (A2.1–A2.3), derivatives (A3.2 and A4.2), knot insertion (A5.1) and knot removal
/// (A5.8). Degree elevation is done by Bézier decomposition, elevating each segment, and
/// removing the knots the decomposition introduced — which reuses knot removal rather than
/// repeating its index arithmetic in a second place.
/// </para>
/// </remarks>
public sealed class NurbsCurve : Curve
{
    private readonly int _degree;
    private readonly Point3d[] _controlPoints;
    private readonly double[] _weights;
    private readonly double[] _knots;

    /// <summary>
    /// Creates a NURBS curve from its degree, control points, weights and knot vector.
    /// </summary>
    /// <param name="degree">
    /// The degree. Must be at least one and at most <c>controlPoints.Count - 1</c>.
    /// </param>
    /// <param name="controlPoints">
    /// The control points. Copied; the list is not retained. At least <c>degree + 1</c> are
    /// needed, and every one must be finite.
    /// </param>
    /// <param name="weights">
    /// One weight per control point, each strictly positive and finite. A zero or negative
    /// weight makes the curve leave the convex hull of its control points and can put a pole in
    /// the middle of the domain, so it is rejected rather than accommodated.
    /// </param>
    /// <param name="knots">
    /// The knot vector, of length <c>controlPoints.Count + degree + 1</c>. Must be
    /// non-decreasing and finite, must be clamped — its first and last <c>degree + 1</c>
    /// entries each equal — and must leave a domain of positive length.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when any list is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the degree is out of range.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the counts disagree, when a weight is not positive and finite, when a
    /// control point is not finite, or when the knot vector is decreasing, not finite, not
    /// clamped, or leaves an empty domain.
    /// </exception>
    public NurbsCurve(
        int degree,
        IReadOnlyList<Point3d> controlPoints,
        IReadOnlyList<double> weights,
        IReadOnlyList<double> knots)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(knots);

        if (degree < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(degree), degree, "The degree must be at least one.");
        }

        if (controlPoints.Count < degree + 1)
        {
            throw new ArgumentException(
                $"A degree-{degree} curve needs at least {degree + 1} control points; "
                + $"{controlPoints.Count} were given.",
                nameof(controlPoints));
        }

        if (weights.Count != controlPoints.Count)
        {
            throw new ArgumentException(
                $"There must be exactly one weight per control point: {controlPoints.Count} control "
                + $"points and {weights.Count} weights were given.",
                nameof(weights));
        }

        if (knots.Count != controlPoints.Count + degree + 1)
        {
            throw new ArgumentException(
                $"A degree-{degree} curve with {controlPoints.Count} control points needs "
                + $"{controlPoints.Count + degree + 1} knots; {knots.Count} were given.",
                nameof(knots));
        }

        _degree = degree;
        _controlPoints = new Point3d[controlPoints.Count];
        _weights = new double[weights.Count];
        _knots = new double[knots.Count];

        for (int i = 0; i < controlPoints.Count; i++)
        {
            if (!controlPoints[i].IsValid)
            {
                throw new ArgumentException(
                    $"Control point {i} is not finite.",
                    nameof(controlPoints));
            }

            if (!double.IsFinite(weights[i]) || weights[i] <= 0.0)
            {
                throw new ArgumentException(
                    $"Weight {i} is {weights[i]}; every weight must be a positive finite number.",
                    nameof(weights));
            }

            _controlPoints[i] = controlPoints[i];
            _weights[i] = weights[i];
        }

        for (int i = 0; i < knots.Count; i++)
        {
            if (!double.IsFinite(knots[i]))
            {
                throw new ArgumentException($"Knot {i} is not finite.", nameof(knots));
            }

            if (i > 0 && knots[i] < knots[i - 1])
            {
                throw new ArgumentException(
                    $"The knot vector must be non-decreasing, but knot {i} is below knot {i - 1}.",
                    nameof(knots));
            }

            _knots[i] = knots[i];
        }

        for (int i = 1; i <= degree; i++)
        {
            if (_knots[i] != _knots[0] || _knots[_knots.Length - 1 - i] != _knots[^1])
            {
                throw new ArgumentException(
                    "The knot vector must be clamped: its first and last degree + 1 entries must "
                    + "each be equal. Unclamped and periodic knot vectors are not supported.",
                    nameof(knots));
            }
        }

        if (!(_knots[controlPoints.Count] > _knots[degree]))
        {
            throw new ArgumentException(
                "The knot vector leaves a domain of zero length, so it describes no curve.",
                nameof(knots));
        }

        // The end multiplicities must be exactly degree + 1, not merely at least. One more
        // than that puts a zero in the denominator of the very first basis function, and the
        // curve evaluates to NaN rather than failing anywhere a caller can see.
        if (_knots[degree + 1] == _knots[0] || _knots[controlPoints.Count - 1] == _knots[^1])
        {
            throw new ArgumentException(
                "The first and last knot values must each appear exactly degree + 1 times. A "
                + "higher end multiplicity makes the basis functions undefined.",
                nameof(knots));
        }
    }

    /// <summary>The degree. At least one; three is the usual free-form curve.</summary>
    public int Degree => _degree;

    /// <summary>How many control points the curve has. Always at least <c>Degree + 1</c>.</summary>
    public int ControlPointCount => _controlPoints.Length;

    /// <summary>
    /// The control points, in order. The first is <see cref="Curve.StartPoint"/> and the last is
    /// <see cref="Curve.EndPoint"/>, because the knot vector is clamped; the ones in between are
    /// generally not on the curve.
    /// </summary>
    public ReadOnlySpan<Point3d> ControlPoints => _controlPoints;

    /// <summary>One weight per control point, each strictly positive.</summary>
    public ReadOnlySpan<double> Weights => _weights;

    /// <summary>
    /// The knot vector, of length <c>ControlPointCount + Degree + 1</c>, non-decreasing and
    /// clamped at both ends.
    /// </summary>
    public ReadOnlySpan<double> Knots => _knots;

    /// <summary>
    /// <see langword="true"/> when any weight differs from one, so that the curve is genuinely
    /// rational rather than polynomial. The test is exact: a weight of <c>1 + 1e-15</c> counts
    /// as rational, because pretending otherwise would change what
    /// <see cref="ToNurbsCurve"/> round-trips.
    /// </summary>
    public bool IsRational
    {
        get
        {
            for (int i = 0; i < _weights.Length; i++)
            {
                if (_weights[i] != 1.0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>[knots[Degree], knots[ControlPointCount]]</c> — taken from the knot vector rather than
    /// normalised, so a curve converted from an analytic one keeps that curve's parameterisation.
    /// </remarks>
    public override Interval Domain => new(_knots[_degree], _knots[_controlPoints.Length]);

    /// <inheritdoc/>
    /// <remarks>
    /// Tested on the control points, which is exact: a clamped curve starts at its first
    /// control point and ends at its last, so the endpoints coincide exactly when those do.
    /// </remarks>
    public override bool IsClosed => _controlPoints[0] == _controlPoints[^1];

    /// <inheritdoc/>
    /// <remarks>
    /// Always <see langword="false"/>. Spark's NURBS curves are always clamped, so even a closed
    /// one has a genuine seam at which the parameterisation stops rather than wrapping.
    /// </remarks>
    public override bool IsPeriodic => false;

    /// <inheritdoc/>
    /// <remarks>
    /// The box of the control points. It contains the curve, by the convex-hull property, but
    /// is generally larger than the tightest box — for a quadratic arc bulging away from its
    /// middle control point, noticeably so.
    /// </remarks>
    public override BoundingBox BoundingBox => BoundingBox.FromPoints(ControlPoints);

    /// <summary>
    /// Creates a NURBS curve from its degree, control points, weights and knot vector. The
    /// factory form of the equivalent constructor, with the parameters in the same order so
    /// that node generation collapses the two into one node.
    /// </summary>
    /// <param name="degree">The degree.</param>
    /// <param name="controlPoints">The control points.</param>
    /// <param name="weights">One weight per control point.</param>
    /// <param name="knots">The knot vector.</param>
    /// <returns>The curve.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any list is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the degree is out of range.</exception>
    /// <exception cref="ArgumentException">Thrown when the inputs are inconsistent.</exception>
    public static NurbsCurve ByControlPointsWeightsKnots(
        int degree,
        IReadOnlyList<Point3d> controlPoints,
        IReadOnlyList<double> weights,
        IReadOnlyList<double> knots) =>
        new(degree, controlPoints, weights, knots);

    /// <summary>
    /// Creates a polynomial NURBS curve from control points, with weights of one and a uniform
    /// clamped knot vector over <c>[0, 1]</c>.
    /// </summary>
    /// <param name="controlPoints">
    /// The control points. The curve passes through the first and the last and is pulled
    /// towards the others without reaching them.
    /// </param>
    /// <param name="degree">
    /// The degree. Must be at least one and at most <c>controlPoints.Count - 1</c>; three is
    /// the usual choice.
    /// </param>
    /// <returns>The curve.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the list is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the degree is out of range.</exception>
    /// <exception cref="ArgumentException">Thrown when a control point is not finite.</exception>
    public static NurbsCurve ByControlPoints(IReadOnlyList<Point3d> controlPoints, int degree = 3)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);

        if (degree < 1 || degree > controlPoints.Count - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(degree),
                degree,
                $"The degree must be between one and {Math.Max(1, controlPoints.Count - 1)} for "
                + $"{controlPoints.Count} control points.");
        }

        double[] weights = new double[controlPoints.Count];

        Array.Fill(weights, 1.0);

        return new NurbsCurve(degree, controlPoints, weights, UniformClampedKnots(controlPoints.Count, degree));
    }

    /// <summary>
    /// Creates a NURBS curve that passes exactly through a sequence of points.
    /// </summary>
    /// <param name="points">
    /// The points to interpolate, in order. At least <c>degree + 1</c> are needed, and no two
    /// consecutive points may coincide — a repeated point gives a zero chord, and the
    /// parameterisation is built from chord lengths.
    /// </param>
    /// <param name="degree">The degree. Must be at least one and at most <c>points.Count - 1</c>.</param>
    /// <returns>
    /// The interpolating curve, over the domain <c>[0, 1]</c>. It has exactly as many control
    /// points as there are input points, and evaluating it at the computed parameters
    /// reproduces the input points.
    /// </returns>
    /// <remarks>
    /// Chord-length parameterisation with averaged knots, then a dense solve of the
    /// interpolation system — Piegl and Tiller A9.1. Chord length rather than uniform
    /// parameterisation because uniform spacing over unevenly spaced points produces
    /// conspicuous overshoot, and averaged knots because that is what keeps the system banded
    /// and non-singular.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when the list is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the degree is out of range.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a point is not finite, when two consecutive points coincide, or when the
    /// interpolation system turns out to be singular.
    /// </exception>
    public static NurbsCurve ByInterpolation(IReadOnlyList<Point3d> points, int degree = 3)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (degree < 1 || degree > points.Count - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(degree),
                degree,
                $"The degree must be between one and {Math.Max(1, points.Count - 1)} for "
                + $"{points.Count} points to interpolate.");
        }

        int n = points.Count - 1;
        double[] parameters = ChordLengthParameters(points);
        double[] knots = AveragedKnots(parameters, degree);
        double[,] system = new double[n + 1, n + 1];
        double[] span = new double[degree + 1];

        for (int k = 0; k <= n; k++)
        {
            int index = FindSpan(n, degree, parameters[k], knots);

            BasisFunctions(index, parameters[k], degree, knots, span);

            for (int j = 0; j <= degree; j++)
            {
                system[k, index - degree + j] = span[j];
            }
        }

        Point3d[] controlPoints = SolveInterpolation(system, points);
        double[] weights = new double[n + 1];

        Array.Fill(weights, 1.0);

        return new NurbsCurve(degree, controlPoints, weights, knots);
    }

    /// <summary>
    /// Creates a smooth NURBS curve through a sequence of points.
    /// </summary>
    /// <param name="points">
    /// The points to pass through, in order. At least two are needed and no two consecutive
    /// points may coincide.
    /// </param>
    /// <returns>
    /// The interpolating curve, cubic where there are enough points for one and of the highest
    /// degree the points support otherwise — quadratic for three points, a straight polyline
    /// for two. This is the convenience form of
    /// <see cref="ByInterpolation(IReadOnlyList{Point3d}, int)"/> for callers who want a curve
    /// through their points and have no opinion about its degree.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when the list is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when fewer than two points are given, a point is not finite, or two consecutive
    /// points coincide.
    /// </exception>
    public static NurbsCurve ByPoints(IReadOnlyList<Point3d> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 2)
        {
            throw new ArgumentException(
                "At least two points are needed to define a curve through them.",
                nameof(points));
        }

        return ByInterpolation(points, Math.Min(3, points.Count - 1));
    }

    /// <inheritdoc/>
    public override Point3d PointAt(double parameter)
    {
        double u = ClampParameter(parameter);
        int n = _controlPoints.Length - 1;
        int span = FindSpan(n, _degree, u, _knots);
        double[] basis = new double[_degree + 1];

        BasisFunctions(span, u, _degree, _knots, basis);

        Homogeneous sum = default;

        for (int j = 0; j <= _degree; j++)
        {
            sum = Homogeneous.Add(sum, Homogeneous.Scale(ControlPointAt(span - _degree + j), basis[j]));
        }

        return sum.ToPoint();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Computed from the homogeneous derivatives by the quotient rule — Piegl and Tiller A4.2 —
    /// so a rational curve's derivatives are exact rather than approximated by differencing.
    /// Orders above the degree are exactly <see cref="Vector3d.Zero"/> for a polynomial curve;
    /// for a rational one they are generally not zero, and are computed rather than assumed.
    /// </remarks>
    public override Vector3d DerivativeAt(double parameter, int order)
    {
        ThrowIfOrderIsNegative(order);

        double u = ClampParameter(parameter);

        if (order == 0)
        {
            return (Vector3d)PointAt(u);
        }

        Vector3d[] derivatives = RationalDerivatives(u, order);

        return derivatives[order];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Tested on the control points. That is exact in the direction that matters: a curve lies
    /// inside the convex hull of its control points, so coplanar control points guarantee a
    /// planar curve. The converse can fail only for a curve whose control points leave the
    /// plane while contributing nothing, which needs a zero weight — and zero weights are
    /// rejected by the constructor.
    /// </remarks>
    public override bool IsPlanar(out Plane plane, in Tolerance tolerance = default) =>
        TryFitPlane(_controlPoints, tolerance, out plane);

    /// <inheritdoc/>
    /// <remarks>
    /// The knot value is inserted at both ends to full multiplicity and the affected control
    /// points are extracted, so the result is exact rather than a refit. The knots are then
    /// shifted so the domain starts at zero, which makes <c>Trim(i).PointAt(u)</c> equal
    /// <c>PointAt(i.Min + u)</c>.
    /// </remarks>
    public override NurbsCurve Trim(in Interval interval)
    {
        Interval clipped = ClipToDomain(interval);
        double start = clipped.Min;
        double end = clipped.Max;
        NurbsCurve curve = this;

        if (start > Domain.Min)
        {
            curve = curve.InsertKnot(start, _degree - curve.KnotMultiplicity(start));
        }

        if (end < Domain.Max)
        {
            curve = curve.InsertKnot(end, _degree - curve.KnotMultiplicity(end));
        }

        int p = curve._degree;
        int n = curve._controlPoints.Length - 1;
        int first = start > curve.Domain.Min ? FindSpan(n, p, start, curve._knots) - p : 0;
        int last = end < curve.Domain.Max ? FindSpan(n, p, end, curve._knots) - p : n;

        int count = last - first + 1;
        Point3d[] controlPoints = new Point3d[count];
        double[] weights = new double[count];
        double[] knots = new double[count + p + 1];

        Array.Copy(curve._controlPoints, first, controlPoints, 0, count);
        Array.Copy(curve._weights, first, weights, 0, count);
        Array.Copy(curve._knots, first, knots, 0, knots.Length);

        for (int i = 0; i <= p; i++)
        {
            knots[i] = start;
            knots[knots.Length - 1 - i] = end;
        }

        for (int i = 0; i < knots.Length; i++)
        {
            knots[i] -= start;
        }

        return new NurbsCurve(p, controlPoints, weights, knots);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The control points, weights and knot spacing are reversed in place, so the reversed
    /// curve has the same domain and is exact rather than refitted.
    /// </remarks>
    public override NurbsCurve Reverse()
    {
        int count = _controlPoints.Length;
        Point3d[] controlPoints = new Point3d[count];
        double[] weights = new double[count];
        double[] knots = new double[_knots.Length];
        double sum = Domain.Min + Domain.Max;

        for (int i = 0; i < count; i++)
        {
            controlPoints[i] = _controlPoints[count - 1 - i];
            weights[i] = _weights[count - 1 - i];
        }

        for (int i = 0; i < knots.Length; i++)
        {
            knots[i] = sum - _knots[knots.Length - 1 - i];
        }

        return new NurbsCurve(_degree, controlPoints, weights, knots);
    }

    /// <inheritdoc/>
    /// <remarks>Returns this curve: it is already a NURBS curve, and it is immutable.</remarks>
    public override NurbsCurve ToNurbsCurve() => this;

    /// <inheritdoc/>
    /// <remarks>
    /// Exact for every affine transformation, because a NURBS curve is the image of its control
    /// points under a map that commutes with affine ones. The weights and the knot vector are
    /// untouched.
    /// </remarks>
    public override NurbsCurve Transform(in Transform transform, in Tolerance tolerance = default)
    {
        ValidateTransform(transform, tolerance);

        Point3d[] controlPoints = new Point3d[_controlPoints.Length];

        for (int i = 0; i < controlPoints.Length; i++)
        {
            controlPoints[i] = transform.OfPoint(_controlPoints[i]);
        }

        return new NurbsCurve(_degree, controlPoints, _weights, _knots);
    }

    /// <summary>
    /// How many times a value appears in the knot vector.
    /// </summary>
    /// <param name="knot">The knot value to count. Compared exactly.</param>
    /// <returns>
    /// The multiplicity, which is zero when the value is not a knot at all. At an interior knot
    /// this is what determines the curve's continuity there: a knot of multiplicity <c>s</c>
    /// leaves the curve <c>C^(degree - s)</c> continuous.
    /// </returns>
    public int KnotMultiplicity(double knot)
    {
        int count = 0;

        for (int i = 0; i < _knots.Length; i++)
        {
            if (_knots[i] == knot)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Inserts a knot, leaving the curve's shape unchanged.
    /// </summary>
    /// <param name="knot">
    /// The knot value to insert. Must lie strictly inside <see cref="Domain"/>: inserting at an
    /// end would raise the end multiplicity beyond <c>degree + 1</c>, which no clamped knot
    /// vector allows.
    /// </param>
    /// <param name="times">
    /// How many copies to insert. Zero or fewer returns this curve unchanged, which is what
    /// makes <c>InsertKnot(u, degree - Multiplicity(u))</c> safe to write without a guard. The
    /// total multiplicity after insertion may not exceed the degree, since a higher
    /// multiplicity would break the curve into disconnected pieces.
    /// </param>
    /// <returns>
    /// A curve occupying exactly the same positions with the same parameterisation, carried by
    /// more control points. The added control points are computed by de Boor's algorithm, so
    /// the geometry is unchanged to within floating-point rounding and not merely to within a
    /// tolerance.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the knot is not strictly inside the domain, is <see cref="double.NaN"/>, or
    /// when the requested multiplicity would exceed the degree.
    /// </exception>
    public NurbsCurve InsertKnot(double knot, int times = 1)
    {
        if (times <= 0)
        {
            return this;
        }

        if (double.IsNaN(knot) || knot <= Domain.Min || knot >= Domain.Max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(knot),
                knot,
                "A knot may only be inserted strictly inside the curve's domain.");
        }

        int p = _degree;
        int n = _controlPoints.Length - 1;
        int multiplicity = KnotMultiplicity(knot);

        if (multiplicity + times > p)
        {
            throw new ArgumentOutOfRangeException(
                nameof(times),
                times,
                $"The knot already has multiplicity {multiplicity}; inserting {times} more would "
                + $"take it past the degree {p}, which would break the curve into pieces.");
        }

        int k = FindSpan(n, p, knot, _knots);
        Homogeneous[] source = HomogeneousControlPoints();
        Homogeneous[] result = new Homogeneous[n + 1 + times];
        double[] knots = new double[_knots.Length + times];

        for (int i = 0; i <= k; i++)
        {
            knots[i] = _knots[i];
        }

        for (int i = 1; i <= times; i++)
        {
            knots[k + i] = knot;
        }

        for (int i = k + 1; i < _knots.Length; i++)
        {
            knots[i + times] = _knots[i];
        }

        for (int i = 0; i <= k - p; i++)
        {
            result[i] = source[i];
        }

        for (int i = k - multiplicity; i <= n; i++)
        {
            result[i + times] = source[i];
        }

        Homogeneous[] working = new Homogeneous[p - multiplicity + 1];

        for (int i = 0; i <= p - multiplicity; i++)
        {
            working[i] = source[k - p + i];
        }

        int lower = k - p;

        for (int j = 1; j <= times; j++)
        {
            lower = k - p + j;

            for (int i = 0; i <= p - j - multiplicity; i++)
            {
                double alpha = (knot - _knots[lower + i]) / (_knots[i + k + 1] - _knots[lower + i]);

                working[i] = Homogeneous.Add(
                    Homogeneous.Scale(working[i + 1], alpha),
                    Homogeneous.Scale(working[i], 1.0 - alpha));
            }

            result[lower] = working[0];
            result[k + times - j - multiplicity] = working[p - j - multiplicity];
        }

        for (int i = lower + 1; i < k - multiplicity; i++)
        {
            result[i] = working[i - lower];
        }

        return FromHomogeneous(p, result, knots);
    }

    /// <summary>
    /// Removes a knot where doing so does not move the curve.
    /// </summary>
    /// <param name="knot">The knot value to remove. Compared exactly against the knot vector.</param>
    /// <param name="times">How many copies to try to remove. Zero or fewer removes nothing.</param>
    /// <param name="removedCount">
    /// Receives how many copies were actually removed, which may be fewer than asked for and
    /// may be zero. Removal is only performed while the curve stays put, so this is the honest
    /// report of how much redundancy there was.
    /// </param>
    /// <param name="tolerance">
    /// The tolerance governing how far the curve is allowed to move. A default-constructed
    /// tolerance means <see cref="Tolerance.Default"/>. The threshold is scaled by the smallest
    /// weight and by the size of the control polygon, following Piegl and Tiller, so it means
    /// the same thing on a rational curve as on a polynomial one and at any working scale.
    /// </param>
    /// <returns>
    /// The curve with the removable copies gone, or this curve when none could be removed.
    /// </returns>
    public NurbsCurve RemoveKnot(double knot, int times, out int removedCount, in Tolerance tolerance = default)
    {
        removedCount = 0;

        int multiplicity = KnotMultiplicity(knot);

        if (times <= 0 || multiplicity == 0 || knot <= Domain.Min || knot >= Domain.Max)
        {
            return this;
        }

        int p = _degree;
        int n = _controlPoints.Length - 1;
        int m = _knots.Length - 1;
        int index = LastIndexOfKnot(knot);
        double threshold = RemovalThreshold(tolerance);

        // Asking to remove more copies than there are is not an error, it is a caller who does
        // not know the multiplicity. Capping keeps the working buffer's size bound honest.
        times = Math.Min(times, multiplicity);

        Homogeneous[] points = HomogeneousControlPoints();
        double[] knots = (double[])_knots.Clone();
        Homogeneous[] temp = new Homogeneous[(2 * p) + 1];

        int order = p + 1;
        int firstOut = ((2 * index) - multiplicity - p) / 2;
        int last = index - multiplicity;
        int first = index - p;
        int removed = 0;

        for (int t = 0; t < times; t++)
        {
            int offset = first - 1;

            temp[0] = points[offset];
            temp[last + 1 - offset] = points[last + 1];

            int i = first;
            int j = last;
            int ii = 1;
            int jj = last - offset;
            bool removable;

            while (j - i > t)
            {
                double alphaI = (knot - knots[i]) / (knots[i + order + t] - knots[i]);
                double alphaJ = (knot - knots[j - t]) / (knots[j + order] - knots[j - t]);

                temp[ii] = Homogeneous.Scale(
                    Homogeneous.Subtract(points[i], Homogeneous.Scale(temp[ii - 1], 1.0 - alphaI)),
                    1.0 / alphaI);
                temp[jj] = Homogeneous.Scale(
                    Homogeneous.Subtract(points[j], Homogeneous.Scale(temp[jj + 1], alphaJ)),
                    1.0 / (1.0 - alphaJ));

                i++;
                ii++;
                j--;
                jj--;
            }

            if (j - i < t)
            {
                removable = Homogeneous.Distance(temp[ii - 1], temp[jj + 1]) <= threshold;
            }
            else
            {
                double alphaI = (knot - knots[i]) / (knots[i + order + t] - knots[i]);
                Homogeneous blended = Homogeneous.Add(
                    Homogeneous.Scale(temp[ii + t + 1], alphaI),
                    Homogeneous.Scale(temp[ii - 1], 1.0 - alphaI));

                removable = Homogeneous.Distance(points[i], blended) <= threshold;
            }

            if (!removable)
            {
                break;
            }

            i = first;
            j = last;

            while (j - i > t)
            {
                points[i] = temp[i - offset];
                points[j] = temp[j - offset];
                i++;
                j--;
            }

            first--;
            last++;
            removed++;
        }

        if (removed == 0)
        {
            return this;
        }

        for (int k = index + 1; k <= m; k++)
        {
            knots[k - removed] = knots[k];
        }

        int destination = firstOut;
        int source = firstOut;

        for (int k = 1; k < removed; k++)
        {
            if ((k % 2) == 1)
            {
                source++;
            }
            else
            {
                destination--;
            }
        }

        for (int k = source + 1; k <= n; k++)
        {
            points[destination] = points[k];
            destination++;
        }

        removedCount = removed;

        Homogeneous[] kept = new Homogeneous[n + 1 - removed];
        double[] keptKnots = new double[_knots.Length - removed];

        Array.Copy(points, kept, kept.Length);
        Array.Copy(knots, keptKnots, keptKnots.Length);

        return FromHomogeneous(p, kept, keptKnots);
    }

    /// <summary>
    /// Inserts several knots at once, leaving the curve's shape unchanged.
    /// </summary>
    /// <param name="knots">
    /// The knot values to insert. Each must lie strictly inside <see cref="Domain"/>, and the
    /// resulting multiplicity of any value may not exceed the degree. Order does not matter and
    /// repeats are honoured — passing the same value twice inserts it twice.
    /// </param>
    /// <returns>
    /// A curve occupying exactly the same positions with the same parameterisation, carried by
    /// more control points.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when the list is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a value lies outside the domain, or when the multiplicity would exceed the
    /// degree.
    /// </exception>
    public NurbsCurve RefineKnots(IReadOnlyList<double> knots)
    {
        ArgumentNullException.ThrowIfNull(knots);

        NurbsCurve curve = this;

        for (int i = 0; i < knots.Count; i++)
        {
            curve = curve.InsertKnot(knots[i]);
        }

        return curve;
    }

    /// <summary>
    /// Raises the degree of the curve without changing its shape.
    /// </summary>
    /// <param name="targetDegree">
    /// The degree to raise to. Must be at least the current <see cref="Degree"/>; equal to it
    /// returns this curve unchanged.
    /// </param>
    /// <param name="tolerance">
    /// The tolerance used when removing the knots that the internal Bézier decomposition
    /// introduces. A default-constructed tolerance means <see cref="Tolerance.Default"/>. A
    /// tighter tolerance only ever costs extra control points; it cannot change the geometry.
    /// </param>
    /// <returns>
    /// A curve of the requested degree occupying the same positions with the same
    /// parameterisation. Its continuity is unchanged: elevating never smooths a corner and
    /// never introduces one.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="targetDegree"/> is below the current degree.
    /// </exception>
    public NurbsCurve ElevateDegree(int targetDegree, in Tolerance tolerance = default)
    {
        if (targetDegree < _degree)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetDegree),
                targetDegree,
                $"The target degree must be at least the curve's current degree of {_degree}. "
                + "Reducing the degree is an approximation, not an exact operation, and is a "
                + "different method.");
        }

        if (targetDegree == _degree)
        {
            return this;
        }

        int p = _degree;
        int steps = targetDegree - p;

        // Record the interior knots and their multiplicities before decomposition, because the
        // continuity they encode is exactly what has to be restored afterwards.
        List<double> interior = [];
        List<int> multiplicities = [];

        for (int i = p + 1; i < _controlPoints.Length; i++)
        {
            if (_knots[i] == _knots[i - 1])
            {
                multiplicities[^1]++;
            }
            else
            {
                interior.Add(_knots[i]);
                multiplicities.Add(1);
            }
        }

        // Decompose into Bézier segments: every interior knot to full multiplicity.
        NurbsCurve decomposed = this;

        for (int i = 0; i < interior.Count; i++)
        {
            decomposed = decomposed.InsertKnot(interior[i], p - multiplicities[i]);
        }

        int segments = interior.Count + 1;
        Homogeneous[] source = decomposed.HomogeneousControlPoints();
        Homogeneous[] elevated = new Homogeneous[(segments * (p + steps)) + 1];

        for (int segment = 0; segment < segments; segment++)
        {
            Homogeneous[] bezier = new Homogeneous[p + 1];

            Array.Copy(source, segment * p, bezier, 0, p + 1);

            for (int step = 0; step < steps; step++)
            {
                bezier = ElevateBezier(bezier);
            }

            Array.Copy(bezier, 0, elevated, segment * (p + steps), bezier.Length);
        }

        int elevatedDegree = p + steps;
        double[] elevatedKnots = new double[elevated.Length + elevatedDegree + 1];

        for (int i = 0; i <= elevatedDegree; i++)
        {
            elevatedKnots[i] = Domain.Min;
            elevatedKnots[elevatedKnots.Length - 1 - i] = Domain.Max;
        }

        for (int i = 0; i < interior.Count; i++)
        {
            for (int j = 0; j < elevatedDegree; j++)
            {
                elevatedKnots[elevatedDegree + 1 + (i * elevatedDegree) + j] = interior[i];
            }
        }

        NurbsCurve result = FromHomogeneous(elevatedDegree, elevated, elevatedKnots);

        // Restore the continuity the original had. A knot of multiplicity s at degree p leaves
        // the curve C^(p-s) continuous; at the elevated degree that same continuity needs
        // multiplicity s + steps, and the decomposition left it at the full elevated degree.
        for (int i = 0; i < interior.Count; i++)
        {
            result = result.RemoveKnot(interior[i], p - multiplicities[i], out _, tolerance);
        }

        return result;
    }

    /// <summary>
    /// Returns the same curve over a different parameter interval.
    /// </summary>
    /// <param name="min">The parameter the curve should start at.</param>
    /// <param name="max">The parameter it should end at. Must be above <paramref name="min"/>.</param>
    /// <returns>
    /// A curve occupying exactly the same positions, with its knot vector mapped affinely onto
    /// the requested domain. Only the parameterisation changes; the control points and weights
    /// are untouched.
    /// </returns>
    /// <remarks>
    /// Internal because it exists to serve <see cref="PolyCurve.ToNurbsCurve"/>, which has to
    /// line each segment's domain up with the slot it occupies before the knot vectors can be
    /// concatenated. It is a sound public operation and may become one, but adding it now would
    /// be a public member with one caller.
    /// </remarks>
    internal NurbsCurve WithDomain(double min, double max)
    {
        double scale = (max - min) / Domain.Length;
        double origin = Domain.Min;
        double[] knots = new double[_knots.Length];

        for (int i = 0; i < knots.Length; i++)
        {
            knots[i] = min + ((_knots[i] - origin) * scale);
        }

        for (int i = 0; i <= _degree; i++)
        {
            knots[i] = min;
            knots[knots.Length - 1 - i] = max;
        }

        return new NurbsCurve(_degree, _controlPoints, _weights, knots);
    }

    /// <summary>
    /// Compares this curve with another by degree, control points, weights and knots, within a
    /// tolerance.
    /// </summary>
    /// <param name="other">The curve to compare with. <see langword="null"/> is never equal.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the degrees match exactly and every control point, weight and
    /// knot agrees within tolerance. This compares the <i>representation</i>: a curve and the
    /// same curve with an extra inserted knot occupy identical positions and are not equal here.
    /// </returns>
    public bool EqualsWithin(NurbsCurve? other, in Tolerance tolerance = default)
    {
        if (other is null
            || other._degree != _degree
            || other._controlPoints.Length != _controlPoints.Length
            || other._knots.Length != _knots.Length)
        {
            return false;
        }

        for (int i = 0; i < _controlPoints.Length; i++)
        {
            if (!_controlPoints[i].EqualsWithin(other._controlPoints[i], tolerance)
                || !tolerance.AreEqual(_weights[i], other._weights[i]))
            {
                return false;
            }
        }

        for (int i = 0; i < _knots.Length; i++)
        {
            if (!tolerance.AreEqual(_knots[i], other._knots[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Formats the degree and the counts, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>NurbsCurve(Degree=3, ControlPoints=8, Rational=False)</c>.</returns>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"NurbsCurve(Degree={_degree}, ControlPoints={_controlPoints.Length}, Rational={IsRational})");

    /// <inheritdoc/>
    /// <remarks>
    /// Raised in proportion to the number of spans, because a curve with many spans has many
    /// local minima and a fixed grid would step over them.
    /// </remarks>
    private protected override int SeedCount =>
        Math.Max(DefaultSeedCount, 16 * (_controlPoints.Length - _degree));

    /// <summary>
    /// The index of the knot span containing a parameter — Piegl and Tiller A2.1.
    /// </summary>
    /// <param name="n">The index of the last control point.</param>
    /// <param name="p">The degree.</param>
    /// <param name="u">The parameter, which must lie in the domain.</param>
    /// <param name="knots">The knot vector.</param>
    /// <returns>The index <c>i</c> with <c>knots[i] &lt;= u &lt; knots[i + 1]</c>, clamped at the ends.</returns>
    private static int FindSpan(int n, int p, double u, double[] knots)
    {
        if (u >= knots[n + 1])
        {
            return n;
        }

        if (u <= knots[p])
        {
            return p;
        }

        int low = p;
        int high = n + 1;
        int mid = (low + high) / 2;

        while (u < knots[mid] || u >= knots[mid + 1])
        {
            if (u < knots[mid])
            {
                high = mid;
            }
            else
            {
                low = mid;
            }

            mid = (low + high) / 2;
        }

        return mid;
    }

    /// <summary>
    /// The non-zero basis functions at a parameter — Piegl and Tiller A2.2.
    /// </summary>
    /// <param name="span">The knot span index.</param>
    /// <param name="u">The parameter.</param>
    /// <param name="p">The degree.</param>
    /// <param name="knots">The knot vector.</param>
    /// <param name="basis">Receives the <c>p + 1</c> non-zero basis function values.</param>
    private static void BasisFunctions(int span, double u, int p, double[] knots, double[] basis)
    {
        double[] left = new double[p + 1];
        double[] right = new double[p + 1];

        basis[0] = 1.0;

        for (int j = 1; j <= p; j++)
        {
            left[j] = u - knots[span + 1 - j];
            right[j] = knots[span + j] - u;

            double saved = 0.0;

            for (int r = 0; r < j; r++)
            {
                double temp = basis[r] / (right[r + 1] + left[j - r]);

                basis[r] = saved + (right[r + 1] * temp);
                saved = left[j - r] * temp;
            }

            basis[j] = saved;
        }
    }

    /// <summary>
    /// The non-zero basis functions and their derivatives at a parameter — Piegl and Tiller
    /// A2.3.
    /// </summary>
    /// <param name="span">The knot span index.</param>
    /// <param name="u">The parameter.</param>
    /// <param name="p">The degree.</param>
    /// <param name="maxOrder">The highest derivative order wanted. Values above <paramref name="p"/> give zeros.</param>
    /// <param name="knots">The knot vector.</param>
    /// <returns>
    /// A jagged array indexed by order then by basis function, so <c>result[k][j]</c> is the
    /// <c>k</c>-th derivative of the <c>j</c>-th non-zero basis function.
    /// </returns>
    private static double[][] BasisDerivatives(int span, double u, int p, int maxOrder, double[] knots)
    {
        double[][] ndu = new double[p + 1][];
        double[][] derivatives = new double[maxOrder + 1][];
        double[] left = new double[p + 1];
        double[] right = new double[p + 1];

        for (int i = 0; i <= p; i++)
        {
            ndu[i] = new double[p + 1];
        }

        for (int i = 0; i <= maxOrder; i++)
        {
            derivatives[i] = new double[p + 1];
        }

        ndu[0][0] = 1.0;

        for (int j = 1; j <= p; j++)
        {
            left[j] = u - knots[span + 1 - j];
            right[j] = knots[span + j] - u;

            double saved = 0.0;

            for (int r = 0; r < j; r++)
            {
                ndu[j][r] = right[r + 1] + left[j - r];

                double temp = ndu[r][j - 1] / ndu[j][r];

                ndu[r][j] = saved + (right[r + 1] * temp);
                saved = left[j - r] * temp;
            }

            ndu[j][j] = saved;
        }

        for (int j = 0; j <= p; j++)
        {
            derivatives[0][j] = ndu[j][p];
        }

        int limit = Math.Min(maxOrder, p);
        double[][] work = [new double[p + 1], new double[p + 1]];

        for (int r = 0; r <= p; r++)
        {
            int s1 = 0;
            int s2 = 1;

            work[0][0] = 1.0;

            for (int k = 1; k <= limit; k++)
            {
                double d = 0.0;
                int rk = r - k;
                int pk = p - k;

                if (r >= k)
                {
                    work[s2][0] = work[s1][0] / ndu[pk + 1][rk];
                    d = work[s2][0] * ndu[rk][pk];
                }

                int j1 = rk >= -1 ? 1 : -rk;
                int j2 = r - 1 <= pk ? k - 1 : p - r;

                for (int j = j1; j <= j2; j++)
                {
                    work[s2][j] = (work[s1][j] - work[s1][j - 1]) / ndu[pk + 1][rk + j];
                    d += work[s2][j] * ndu[rk + j][pk];
                }

                if (r <= pk)
                {
                    work[s2][k] = -work[s1][k - 1] / ndu[pk + 1][r];
                    d += work[s2][k] * ndu[r][pk];
                }

                derivatives[k][r] = d;
                (s1, s2) = (s2, s1);
            }
        }

        int factor = p;

        for (int k = 1; k <= limit; k++)
        {
            for (int j = 0; j <= p; j++)
            {
                derivatives[k][j] *= factor;
            }

            factor *= p - k;
        }

        return derivatives;
    }

    /// <summary>
    /// One step of Bézier degree elevation on homogeneous control points.
    /// </summary>
    /// <param name="bezier">The control points of a single Bézier segment.</param>
    /// <returns>The control points of the same segment one degree higher.</returns>
    private static Homogeneous[] ElevateBezier(Homogeneous[] bezier)
    {
        int p = bezier.Length - 1;
        Homogeneous[] result = new Homogeneous[p + 2];

        result[0] = bezier[0];
        result[p + 1] = bezier[p];

        for (int i = 1; i <= p; i++)
        {
            double alpha = (double)i / (p + 1);

            result[i] = Homogeneous.Add(
                Homogeneous.Scale(bezier[i - 1], alpha),
                Homogeneous.Scale(bezier[i], 1.0 - alpha));
        }

        return result;
    }

    /// <summary>
    /// A uniform clamped knot vector over <c>[0, 1]</c>.
    /// </summary>
    /// <param name="count">How many control points the curve has.</param>
    /// <param name="degree">The degree.</param>
    /// <returns>The knot vector.</returns>
    private static double[] UniformClampedKnots(int count, int degree)
    {
        double[] knots = new double[count + degree + 1];
        int spans = count - degree;

        for (int i = 0; i <= degree; i++)
        {
            knots[i] = 0.0;
            knots[knots.Length - 1 - i] = 1.0;
        }

        for (int i = 1; i < spans; i++)
        {
            knots[degree + i] = (double)i / spans;
        }

        return knots;
    }

    /// <summary>
    /// Chord-length parameters over <c>[0, 1]</c> for a sequence of points to interpolate.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <returns>One parameter per point, increasing, starting at zero and ending at one.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when a point is not finite or two consecutive points coincide.
    /// </exception>
    private static double[] ChordLengthParameters(IReadOnlyList<Point3d> points)
    {
        double[] parameters = new double[points.Count];
        double total = 0.0;

        for (int i = 1; i < points.Count; i++)
        {
            if (!points[i].IsValid)
            {
                throw new ArgumentException($"Point {i} is not finite.", nameof(points));
            }

            double chord = points[i].DistanceTo(points[i - 1]);

            if (chord == 0.0)
            {
                throw new ArgumentException(
                    $"Points {i - 1} and {i} are coincident. A chord-length parameterisation "
                    + "cannot assign them different parameters, so the interpolation is not "
                    + "defined.",
                    nameof(points));
            }

            total += chord;
            parameters[i] = total;
        }

        for (int i = 1; i < points.Count - 1; i++)
        {
            parameters[i] /= total;
        }

        parameters[^1] = 1.0;

        return parameters;
    }

    /// <summary>
    /// The averaged knot vector for an interpolation — Piegl and Tiller equation 9.8.
    /// </summary>
    /// <param name="parameters">The parameters the points are to be interpolated at.</param>
    /// <param name="degree">The degree.</param>
    /// <returns>A clamped knot vector over <c>[0, 1]</c>.</returns>
    private static double[] AveragedKnots(double[] parameters, int degree)
    {
        int n = parameters.Length - 1;
        double[] knots = new double[n + degree + 2];

        for (int i = 0; i <= degree; i++)
        {
            knots[i] = 0.0;
            knots[knots.Length - 1 - i] = 1.0;
        }

        for (int j = 1; j <= n - degree; j++)
        {
            double sum = 0.0;

            for (int i = j; i <= j + degree - 1; i++)
            {
                sum += parameters[i];
            }

            knots[j + degree] = sum / degree;
        }

        return knots;
    }

    /// <summary>
    /// Solves the interpolation system by Gaussian elimination with partial pivoting.
    /// </summary>
    /// <param name="system">The basis function matrix, which is square and banded.</param>
    /// <param name="points">The points to interpolate, forming the three right-hand sides.</param>
    /// <returns>The control points.</returns>
    /// <exception cref="ArgumentException">Thrown when the system is singular.</exception>
    private static Point3d[] SolveInterpolation(double[,] system, IReadOnlyList<Point3d> points)
    {
        int n = points.Count;
        double[,] rhs = new double[n, 3];

        for (int i = 0; i < n; i++)
        {
            rhs[i, 0] = points[i].X;
            rhs[i, 1] = points[i].Y;
            rhs[i, 2] = points[i].Z;
        }

        for (int column = 0; column < n; column++)
        {
            int pivot = column;

            for (int row = column + 1; row < n; row++)
            {
                if (Math.Abs(system[row, column]) > Math.Abs(system[pivot, column]))
                {
                    pivot = row;
                }
            }

            if (system[pivot, column] == 0.0)
            {
                throw new ArgumentException(
                    "The interpolation system is singular, so no curve of this degree passes "
                    + "through these points.",
                    nameof(points));
            }

            if (pivot != column)
            {
                for (int k = 0; k < n; k++)
                {
                    (system[column, k], system[pivot, k]) = (system[pivot, k], system[column, k]);
                }

                for (int k = 0; k < 3; k++)
                {
                    (rhs[column, k], rhs[pivot, k]) = (rhs[pivot, k], rhs[column, k]);
                }
            }

            for (int row = column + 1; row < n; row++)
            {
                double factor = system[row, column] / system[column, column];

                if (factor == 0.0)
                {
                    continue;
                }

                for (int k = column; k < n; k++)
                {
                    system[row, k] -= factor * system[column, k];
                }

                for (int k = 0; k < 3; k++)
                {
                    rhs[row, k] -= factor * rhs[column, k];
                }
            }
        }

        double[,] solution = new double[n, 3];

        for (int row = n - 1; row >= 0; row--)
        {
            for (int k = 0; k < 3; k++)
            {
                double sum = rhs[row, k];

                for (int column = row + 1; column < n; column++)
                {
                    sum -= system[row, column] * solution[column, k];
                }

                solution[row, k] = sum / system[row, row];
            }
        }

        Point3d[] controlPoints = new Point3d[n];

        for (int i = 0; i < n; i++)
        {
            controlPoints[i] = new Point3d(solution[i, 0], solution[i, 1], solution[i, 2]);
        }

        return controlPoints;
    }

    /// <summary>
    /// Rebuilds a curve from homogeneous control points.
    /// </summary>
    /// <param name="degree">The degree.</param>
    /// <param name="points">The homogeneous control points.</param>
    /// <param name="knots">The knot vector.</param>
    /// <returns>The curve.</returns>
    private static NurbsCurve FromHomogeneous(int degree, Homogeneous[] points, double[] knots)
    {
        Point3d[] controlPoints = new Point3d[points.Length];
        double[] weights = new double[points.Length];

        for (int i = 0; i < points.Length; i++)
        {
            controlPoints[i] = points[i].ToPoint();
            weights[i] = points[i].W;
        }

        return new NurbsCurve(degree, controlPoints, weights, knots);
    }

    /// <summary>
    /// The derivatives of the rational curve up to a given order — Piegl and Tiller A4.2.
    /// </summary>
    /// <param name="u">The parameter, already clamped into the domain.</param>
    /// <param name="order">The highest order wanted.</param>
    /// <returns>The derivatives, indexed by order, with index zero being the position vector.</returns>
    private Vector3d[] RationalDerivatives(double u, int order)
    {
        int p = _degree;
        int n = _controlPoints.Length - 1;
        int span = FindSpan(n, p, u, _knots);
        double[][] basis = BasisDerivatives(span, u, p, order, _knots);

        Vector3d[] numerator = new Vector3d[order + 1];
        double[] denominator = new double[order + 1];

        for (int k = 0; k <= order; k++)
        {
            Vector3d sum = Vector3d.Zero;
            double weight = 0.0;

            // Orders above the degree have identically zero basis derivatives, which
            // BasisDerivatives leaves as the zeros it allocated, so no special case is needed
            // here: the sums simply come out zero.
            for (int j = 0; j <= p; j++)
            {
                Homogeneous point = ControlPointAt(span - p + j);

                sum += new Vector3d(point.X, point.Y, point.Z) * basis[k][j];
                weight += point.W * basis[k][j];
            }

            numerator[k] = sum;
            denominator[k] = weight;
        }

        Vector3d[] result = new Vector3d[order + 1];

        for (int k = 0; k <= order; k++)
        {
            Vector3d value = numerator[k];

            for (int i = 1; i <= k; i++)
            {
                value -= result[k - i] * (Binomial(k, i) * denominator[i]);
            }

            result[k] = value / denominator[0];
        }

        return result;
    }

    /// <summary>
    /// The binomial coefficient, computed iteratively so that it stays exact for the small
    /// orders a curve's derivatives ever reach.
    /// </summary>
    /// <param name="n">The upper index.</param>
    /// <param name="k">The lower index.</param>
    /// <returns>The coefficient.</returns>
    private static double Binomial(int n, int k)
    {
        double result = 1.0;

        for (int i = 1; i <= k; i++)
        {
            result = result * (n - k + i) / i;
        }

        return result;
    }

    /// <summary>
    /// The index of the last occurrence of a knot value.
    /// </summary>
    /// <param name="knot">The value to find.</param>
    /// <returns>The index, or minus one when the value is not a knot.</returns>
    private int LastIndexOfKnot(double knot)
    {
        for (int i = _knots.Length - 1; i >= 0; i--)
        {
            if (_knots[i] == knot)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The threshold a knot-removal step is judged against, scaled the way Piegl and Tiller
    /// scale it so that it means the same on a rational curve as on a polynomial one.
    /// </summary>
    /// <param name="tolerance">The caller's tolerance.</param>
    /// <returns>The threshold, in the units of a homogeneous distance.</returns>
    private double RemovalThreshold(in Tolerance tolerance)
    {
        double smallestWeight = double.PositiveInfinity;
        double largestPoint = 0.0;

        for (int i = 0; i < _weights.Length; i++)
        {
            smallestWeight = Math.Min(smallestWeight, _weights[i]);
            largestPoint = Math.Max(largestPoint, ((Vector3d)_controlPoints[i]).Length);
        }

        double deviation = Math.Max(tolerance.Linear, tolerance.RelativeEpsilon * largestPoint);

        return deviation * smallestWeight / (1.0 + largestPoint);
    }

    /// <summary>
    /// The homogeneous form of one control point.
    /// </summary>
    /// <param name="index">The control point index.</param>
    /// <returns>The weighted control point.</returns>
    private Homogeneous ControlPointAt(int index) => new(_controlPoints[index], _weights[index]);

    /// <summary>
    /// The homogeneous form of every control point, as a fresh array the caller may modify.
    /// </summary>
    /// <returns>The weighted control points.</returns>
    private Homogeneous[] HomogeneousControlPoints()
    {
        Homogeneous[] points = new Homogeneous[_controlPoints.Length];

        for (int i = 0; i < points.Length; i++)
        {
            points[i] = ControlPointAt(i);
        }

        return points;
    }

    /// <summary>
    /// A control point in homogeneous coordinates: the position premultiplied by its weight,
    /// plus the weight itself.
    /// </summary>
    /// <remarks>
    /// Every NURBS algorithm here is written on these rather than on positions and weights
    /// separately, because in homogeneous form a rational curve is a polynomial one in four
    /// dimensions and the algorithms have no rational special case at all.
    /// </remarks>
    private readonly struct Homogeneous
    {
        /// <summary>
        /// Creates a homogeneous control point from a position and a weight.
        /// </summary>
        /// <param name="point">The control point position.</param>
        /// <param name="weight">The weight, which must be positive.</param>
        internal Homogeneous(in Point3d point, double weight)
            : this(point.X * weight, point.Y * weight, point.Z * weight, weight)
        {
        }

        /// <summary>
        /// Creates a homogeneous control point from its four components.
        /// </summary>
        /// <param name="x">The weighted X coordinate.</param>
        /// <param name="y">The weighted Y coordinate.</param>
        /// <param name="z">The weighted Z coordinate.</param>
        /// <param name="w">The weight.</param>
        internal Homogeneous(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        /// <summary>The weighted X coordinate.</summary>
        internal double X { get; }

        /// <summary>The weighted Y coordinate.</summary>
        internal double Y { get; }

        /// <summary>The weighted Z coordinate.</summary>
        internal double Z { get; }

        /// <summary>The weight.</summary>
        internal double W { get; }

        /// <summary>Adds two homogeneous points componentwise.</summary>
        /// <param name="a">The first point.</param>
        /// <param name="b">The second point.</param>
        /// <returns>The sum.</returns>
        internal static Homogeneous Add(in Homogeneous a, in Homogeneous b) =>
            new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);

        /// <summary>Subtracts one homogeneous point from another componentwise.</summary>
        /// <param name="a">The point to subtract from.</param>
        /// <param name="b">The point to subtract.</param>
        /// <returns>The difference.</returns>
        internal static Homogeneous Subtract(in Homogeneous a, in Homogeneous b) =>
            new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);

        /// <summary>Scales a homogeneous point.</summary>
        /// <param name="a">The point.</param>
        /// <param name="factor">The factor.</param>
        /// <returns>The scaled point.</returns>
        internal static Homogeneous Scale(in Homogeneous a, double factor) =>
            new(a.X * factor, a.Y * factor, a.Z * factor, a.W * factor);

        /// <summary>The four-dimensional distance between two homogeneous points.</summary>
        /// <param name="a">The first point.</param>
        /// <param name="b">The second point.</param>
        /// <returns>The distance, weight included.</returns>
        internal static double Distance(in Homogeneous a, in Homogeneous b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            double dw = a.W - b.W;

            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz) + (dw * dw));
        }

        /// <summary>The Euclidean position this homogeneous point represents.</summary>
        /// <returns>The position, obtained by dividing through by the weight.</returns>
        internal Point3d ToPoint() => new(X / W, Y / W, Z / W);
    }
}
