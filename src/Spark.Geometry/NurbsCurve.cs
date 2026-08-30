using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A non-uniform rational B-spline curve: control points, weights and a
/// <see cref="KnotVector"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The curve is stored and evaluated in homogeneous coordinates.</b> Each control point is held
/// as <c>(w·x, w·y, w·z, w)</c>, de Boor's algorithm runs on those four components, and the result
/// is projected once at the end. Evaluating the rational form directly — dividing by the weight
/// sum at every step of the recurrence — is both slower and less accurate, and the derivative
/// formulae only come out clean in homogeneous form, where the quotient rule is applied once to
/// <c>C(t) = A(t) / w(t)</c> rather than threaded through the recursion.
/// </para>
/// <para>
/// <b>Weights must be positive.</b> A zero or negative weight lets the denominator reach zero
/// somewhere inside the domain, and the curve then has a pole in it: <see cref="Curve.PointAt(double)"/> returns
/// infinities at a parameter that looks no different from its neighbours. Refusing at construction
/// is the only place that failure can be attributed to its cause.
/// </para>
/// <para>
/// A curve with every weight equal is a plain (non-rational) B-spline; nothing special is done for
/// that case because the homogeneous arithmetic already reduces to it, and a separate code path
/// would be a second thing to keep correct.
/// </para>
/// </remarks>
public sealed class NurbsCurve : Curve
{
    private readonly Point3d[] _controlPoints;
    private readonly double[] _weights;

    /// <summary>The homogeneous control points, as (wx, wy, wz, w).</summary>
    private readonly double[,] _homogeneous;

    /// <summary>
    /// Creates a curve from control points, weights and a knot vector.
    /// </summary>
    /// <param name="degree">The degree. At least 1.</param>
    /// <param name="controlPoints">The control points. Copied.</param>
    /// <param name="knots">The knot vector, whose length must match the control-point count.</param>
    /// <param name="weights">
    /// The weights, one per control point, all positive — or <see langword="null"/> for a
    /// non-rational curve, which is every weight equal to one.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="controlPoints"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="degree"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// The knots do not match the control-point count, a control point is not finite, or a weight
    /// is not positive and finite.
    /// </exception>
    public NurbsCurve(
        int degree,
        IReadOnlyList<Point3d> controlPoints,
        IReadOnlyList<double> knots,
        IReadOnlyList<double>? weights = null)
        : this(controlPoints, new KnotVector(degree, knots), weights)
    {
    }

    /// <summary>
    /// Creates a curve from control points and an already-validated knot vector.
    /// </summary>
    /// <param name="controlPoints">The control points. Copied.</param>
    /// <param name="knots">The knot vector, which carries the degree.</param>
    /// <param name="weights">The weights, or <see langword="null"/> for a non-rational curve.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="controlPoints"/> or <paramref name="knots"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The knots do not match the control-point count, a control point is not finite, or a weight
    /// is not positive and finite.
    /// </exception>
    public NurbsCurve(
        IReadOnlyList<Point3d> controlPoints,
        KnotVector knots,
        IReadOnlyList<double>? weights = null)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);
        ArgumentNullException.ThrowIfNull(knots);

        // The defining relation, checked from the curve's side. The knot vector already knows how
        // many control points it implies; this is the one place the two facts meet.
        if (controlPoints.Count != knots.ControlPointCount)
        {
            throw new ArgumentException(
                $"A degree-{knots.Degree} curve with {knots.Count} knots needs exactly "
                + $"{knots.ControlPointCount} control points and was given {controlPoints.Count}. "
                + "A B-spline satisfies knots = controlPoints + degree + 1.",
                nameof(controlPoints));
        }

        if (weights is not null && weights.Count != controlPoints.Count)
        {
            throw new ArgumentException(
                $"There are {controlPoints.Count} control points and {weights.Count} weights. "
                + "Every control point carries exactly one weight.",
                nameof(weights));
        }

        Knots = knots;
        _controlPoints = new Point3d[controlPoints.Count];
        _weights = new double[controlPoints.Count];
        _homogeneous = new double[controlPoints.Count, 4];

        for (int i = 0; i < controlPoints.Count; i++)
        {
            Point3d point = controlPoints[i];

            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
            {
                throw new ArgumentException(
                    $"Control point {i} is {point}, which is not finite.", nameof(controlPoints));
            }

            double weight = weights?[i] ?? 1.0;

            if (!double.IsFinite(weight) || weight <= 0.0)
            {
                throw new ArgumentException(
                    $"Weight {i} is {weight.ToString("R", CultureInfo.InvariantCulture)}. Weights must "
                    + "be positive and finite: a weight of zero or less puts a pole inside the curve, "
                    + "where the denominator vanishes and the curve runs off to infinity.",
                    nameof(weights));
            }

            _controlPoints[i] = point;
            _weights[i] = weight;

            _homogeneous[i, 0] = point.X * weight;
            _homogeneous[i, 1] = point.Y * weight;
            _homogeneous[i, 2] = point.Z * weight;
            _homogeneous[i, 3] = weight;
        }
    }

    /// <summary>The knot vector, which carries the degree and the domain.</summary>
    public KnotVector Knots { get; }

    /// <summary>The degree.</summary>
    public int Degree => Knots.Degree;

    /// <inheritdoc/>
    public override Interval Domain => Knots.Domain;

    /// <summary>
    /// Whether the first and last control points coincide.
    /// </summary>
    /// <remarks>
    /// A geometric test rather than a structural one. A NURBS curve can be closed by repeating its
    /// first control points at the end (a periodic vector) or by placing the last control point on
    /// the first, and only the second is visible in the data — but both produce a curve whose ends
    /// meet, which is what every caller of this actually wants to know.
    /// </remarks>
    public override bool IsClosed =>
        _controlPoints[0].EqualsWithin(_controlPoints[^1]);

    /// <summary>Whether any weight differs from any other, making the curve genuinely rational.</summary>
    /// <remarks>
    /// Worth asking because a non-rational curve is exactly representable after operations that a
    /// rational one is not — degree elevation and knot insertion among them — and because a curve
    /// reported as rational when it is not sends a reader looking for a subtlety that is not there.
    /// </remarks>
    public bool IsRational
    {
        get
        {
            for (int i = 1; i < _weights.Length; i++)
            {
                if (_weights[i] != _weights[0])
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The control points, in order.</summary>
    /// <returns>A copy; the curve is immutable and an exposed array would not be.</returns>
    public Point3d[] ControlPoints() => [.. _controlPoints];

    /// <summary>The weights, in order.</summary>
    /// <returns>A copy.</returns>
    public double[] Weights() => [.. _weights];

    /// <summary>
    /// A degree-1 curve through a sequence of points — the NURBS spelling of a polyline.
    /// </summary>
    /// <param name="points">The points, at least two.</param>
    /// <returns>The curve.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
    /// <exception cref="ArgumentException">There are fewer than two points.</exception>
    /// <remarks>
    /// Chiefly a test fixture and a conversion target: a degree-1 NURBS curve must agree with
    /// <see cref="PolyLine"/> and with <see cref="Line"/>, and that agreement is worth more as a
    /// check than any number of self-consistent assertions about the spline arithmetic.
    /// </remarks>
    public static NurbsCurve ByPoints(IReadOnlyList<Point3d> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 2)
        {
            throw new ArgumentException(
                "A curve through points needs at least two of them.", nameof(points));
        }

        return new NurbsCurve(points, KnotVector.CreateClamped(1, points.Count));
    }

    /// <summary>
    /// Inserts a knot, leaving the curve's shape completely unchanged.
    /// </summary>
    /// <param name="knot">The parameter to insert at, inside the domain.</param>
    /// <param name="times">How many times to insert it. At least 1.</param>
    /// <returns>A curve with the same shape and more control points.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="knot"/> is outside the domain, or <paramref name="times"/> is less than 1.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The insertion would take the knot's multiplicity above the degree, which would leave a
    /// control point with no support at all.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Boehm's algorithm. The <c>degree</c> control points straddling the insertion are replaced by
    /// <c>degree + 1</c> new ones, each a linear blend of two neighbours — and the blend is done on
    /// the <b>homogeneous</b> points. Blending the projected points instead is the classic mistake:
    /// it is right for a non-rational curve, wrong for a rational one, and produces a curve that is
    /// visibly close to the original and not equal to it, which is the hardest kind of wrong to
    /// notice.
    /// </para>
    /// <para>
    /// <b>This is the foundation of several other operations.</b> Trimming and splitting are knot
    /// insertion to full multiplicity at the cut, closest-point search is repeated subdivision, and
    /// degree elevation is built on the same blending. Getting it exactly right — <i>shape
    /// unchanged</i>, not <i>shape nearly unchanged</i> — is what makes all of them trustworthy.
    /// </para>
    /// </remarks>
    public NurbsCurve WithKnotInserted(double knot, int times = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(times, 1);

        Interval domain = Domain;
        if (knot < domain.Min || knot > domain.Max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(knot), knot, $"The knot must be inside the domain {domain.Min} to {domain.Max}.");
        }

        int existing = Knots.Multiplicity(knot);
        if (existing + times > Degree)
        {
            throw new ArgumentException(
                $"Inserting {knot.ToString("R", CultureInfo.InvariantCulture)} {times} more time(s) would "
                + $"take its multiplicity to {existing + times}, above the degree {Degree}. A knot at full "
                + "multiplicity already splits the curve there; inserting past it would leave a control "
                + "point with no support.",
                nameof(times));
        }

        NurbsCurve current = this;
        for (int i = 0; i < times; i++)
        {
            current = current.InsertOnce(knot);
        }

        return current;
    }

    /// <summary>
    /// Raises the curve's degree, leaving its shape completely unchanged.
    /// </summary>
    /// <param name="by">How many degrees to add. At least 1.</param>
    /// <returns>A curve of higher degree occupying exactly the same points.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="by"/> is less than 1.</exception>
    /// <remarks>
    /// <para>
    /// Done by <b>Bézier decomposition</b>: insert knots until every interior knot has multiplicity
    /// <c>degree</c>, which turns the curve into a chain of Bézier segments sharing endpoints;
    /// elevate each segment, where the rule is a one-line blend
    /// (<c>Qᵢ = (i/(p+1))·Pᵢ₋₁ + (1 − i/(p+1))·Pᵢ</c>); then reassemble. The direct algorithm is
    /// faster and considerably longer, and its extra complexity is entirely in <i>avoiding</i> the
    /// decomposition — which is a trade worth making later, with a benchmark, and not now.
    /// </para>
    /// <para>
    /// <b>The result is exact but not minimal.</b> Decomposing raises every interior knot to full
    /// multiplicity and nothing here lowers it again, so a curve that was smooth across a knot comes
    /// back describing the same shape with more control points than it needs. That is a
    /// representation cost and not a geometric one — every sampled point is identical — and removing
    /// it needs knot removal, which is a separate operation with its own tolerance question:
    /// <i>how nearly equal must two curves be before a knot may be dropped?</i> Answering that
    /// casually here would put an approximation inside an operation whose whole promise is that it
    /// changes nothing.
    /// </para>
    /// </remarks>
    public NurbsCurve WithDegreeElevated(int by = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, 1);

        NurbsCurve current = this;
        for (int i = 0; i < by; i++)
        {
            current = current.ElevateOnce();
        }

        return current;
    }

    /// <summary>One degree of elevation, through a Bézier decomposition.</summary>
    private NurbsCurve ElevateOnce()
    {
        int p = Degree;
        NurbsCurve bezier = AsBezierSegments();

        int segments = (bezier._controlPoints.Length - 1) / p;
        int count = (segments * (p + 1)) + 1;
        double[,] raised = new double[count, 4];

        for (int segment = 0; segment < segments; segment++)
        {
            int from = segment * p;
            int to = segment * (p + 1);

            // The two endpoints are interpolated exactly; every interior point is a blend of the
            // two it sits between. Written on the homogeneous components, for the reason knot
            // insertion is: blending projected points is right for a non-rational curve only.
            for (int c = 0; c < 4; c++)
            {
                raised[to, c] = bezier._homogeneous[from, c];
                raised[to + p + 1, c] = bezier._homogeneous[from + p, c];
            }

            for (int i = 1; i <= p; i++)
            {
                double alpha = (double)i / (p + 1);

                for (int c = 0; c < 4; c++)
                {
                    raised[to + i, c] =
                        (alpha * bezier._homogeneous[from + i - 1, c])
                        + ((1.0 - alpha) * bezier._homogeneous[from + i, c]);
                }
            }
        }

        double[] breaks = bezier.DistinctSpans();
        double[] knots = new double[count + p + 2];
        int index = 0;

        for (int i = 0; i < p + 2; i++)
        {
            knots[index++] = breaks[0];
        }

        for (int b = 1; b < breaks.Length - 1; b++)
        {
            for (int i = 0; i < p + 1; i++)
            {
                knots[index++] = breaks[b];
            }
        }

        for (int i = 0; i < p + 2; i++)
        {
            knots[index++] = breaks[^1];
        }

        return FromHomogeneous(raised, new KnotVector(p + 1, knots));
    }

    /// <summary>
    /// The same curve with every interior knot at full multiplicity — a chain of Bézier segments.
    /// </summary>
    private NurbsCurve AsBezierSegments()
    {
        NurbsCurve current = this;

        foreach (double knot in DistinctSpans()[1..^1])
        {
            int missing = Degree - current.Knots.Multiplicity(knot);
            if (missing > 0)
            {
                current = current.WithKnotInserted(knot, missing);
            }
        }

        return current;
    }

    /// <summary>One application of Boehm's algorithm.</summary>
    private NurbsCurve InsertOnce(double knot)
    {
        int p = Degree;
        int span = Knots.FindSpan(knot);
        int n = _controlPoints.Length;

        double[] oldKnots = Knots.ToArray();
        double[] newKnots = new double[oldKnots.Length + 1];

        Array.Copy(oldKnots, newKnots, span + 1);
        newKnots[span + 1] = knot;
        Array.Copy(oldKnots, span + 1, newKnots, span + 2, oldKnots.Length - span - 1);

        double[,] blended = new double[n + 1, 4];

        // Everything before the affected window and everything after it is carried across
        // untouched. Only the p points ending at the span are replaced, by p + 1 new ones.
        for (int i = 0; i <= span - p; i++)
        {
            for (int c = 0; c < 4; c++)
            {
                blended[i, c] = _homogeneous[i, c];
            }
        }

        for (int i = span; i < n; i++)
        {
            for (int c = 0; c < 4; c++)
            {
                blended[i + 1, c] = _homogeneous[i, c];
            }
        }

        for (int i = span - p + 1; i <= span; i++)
        {
            double denominator = oldKnots[i + p] - oldKnots[i];
            double alpha = denominator == 0.0 ? 0.0 : (knot - oldKnots[i]) / denominator;

            for (int c = 0; c < 4; c++)
            {
                blended[i, c] = (alpha * _homogeneous[i, c]) + ((1.0 - alpha) * _homogeneous[i - 1, c]);
            }
        }

        return FromHomogeneous(blended, new KnotVector(p, newKnots));
    }

    /// <summary>Rebuilds a curve from homogeneous control points, projecting them back out.</summary>
    private static NurbsCurve FromHomogeneous(double[,] homogeneous, KnotVector knots)
    {
        int count = homogeneous.GetLength(0);
        Point3d[] points = new Point3d[count];
        double[] weights = new double[count];

        for (int i = 0; i < count; i++)
        {
            double w = homogeneous[i, 3];
            weights[i] = w;
            points[i] = new Point3d(homogeneous[i, 0] / w, homogeneous[i, 1] / w, homogeneous[i, 2] / w);
        }

        return new NurbsCurve(points, knots, weights);
    }

    /// <summary>
    /// The curve of a given degree that passes exactly through a sequence of points.
    /// </summary>
    /// <param name="points">The points to pass through, at least two, no two consecutive equal.</param>
    /// <param name="degree">The degree. At least 1, and less than the number of points.</param>
    /// <returns>A clamped curve interpolating every point.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="degree"/> is less than 1, or not less than the number of points.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// There are fewer than two points, a point is not finite, or two consecutive points coincide.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Global interpolation: choose a parameter for each point, build the knot vector from those
    /// parameters, then solve the linear system that says <i>the curve at parameter i is point
    /// i</i>. The unknowns are the control points, which are generally <b>not</b> the input points
    /// — that is the difference between interpolation and the polyline through them, and it is
    /// what a caller is asking for when they ask for a smooth curve through their data.
    /// </para>
    /// <para>
    /// <b>Parameters are chosen by chord length, not uniformly.</b> Uniform parameterisation is one
    /// line shorter and produces visible loops and cusps whenever the points are unevenly spaced —
    /// the curve has to travel the same amount of parameter across a long gap as a short one, so it
    /// speeds up and overshoots. Chord length is the standard answer and costs a square root per
    /// point. The knots are then averaged from those parameters, which is what keeps the system
    /// banded and well conditioned rather than merely solvable.
    /// </para>
    /// <para>
    /// The result is non-rational. Weights are a modelling choice, and inventing them to fit points
    /// would be answering a question nobody asked with an answer nobody could check.
    /// </para>
    /// </remarks>
    public static NurbsCurve InterpolatePoints(IReadOnlyList<Point3d> points, int degree = 3)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfLessThan(degree, 1);

        if (points.Count < 2)
        {
            throw new ArgumentException(
                "A curve through points needs at least two of them.", nameof(points));
        }

        if (degree >= points.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(degree),
                degree,
                $"A degree-{degree} curve needs more than {degree} points to interpolate and was "
                + $"given {points.Count}. Lower the degree, or supply more points.");
        }

        double[] parameters = ChordLengthParameters(points);
        KnotVector knots = AveragedKnots(parameters, degree);

        // The system is N · P = Q, where N is the matrix of basis values at each parameter, Q the
        // input points and P the control points sought. N is banded with bandwidth degree + 1, and
        // that is a consequence of the averaged knots rather than a coincidence.
        int n = points.Count;
        double[,] matrix = new double[n, n];

        for (int i = 0; i < n; i++)
        {
            int span = knots.FindSpan(parameters[i]);
            double[] basis = knots.BasisFunctions(span, parameters[i]);

            for (int j = 0; j <= degree; j++)
            {
                matrix[i, span - degree + j] = basis[j];
            }
        }

        double[,] rightHand = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            rightHand[i, 0] = points[i].X;
            rightHand[i, 1] = points[i].Y;
            rightHand[i, 2] = points[i].Z;
        }

        double[,] solved = SolveInPlace(matrix, rightHand);

        Point3d[] controlPoints = new Point3d[n];
        for (int i = 0; i < n; i++)
        {
            controlPoints[i] = new Point3d(solved[i, 0], solved[i, 1], solved[i, 2]);
        }

        return new NurbsCurve(controlPoints, knots);
    }

    /// <summary>
    /// A parameter for each point, spaced by the distance between them and normalised to 0..1.
    /// </summary>
    private static double[] ChordLengthParameters(IReadOnlyList<Point3d> points)
    {
        double[] parameters = new double[points.Count];
        double total = 0.0;

        for (int i = 1; i < points.Count; i++)
        {
            Point3d previous = points[i - 1];
            Point3d current = points[i];

            if (!double.IsFinite(current.X) || !double.IsFinite(current.Y) || !double.IsFinite(current.Z))
            {
                throw new ArgumentException($"Point {i} is {current}, which is not finite.", nameof(points));
            }

            double chord = previous.DistanceTo(current);

            // Two coincident points would give the same parameter to two different points, and the
            // system would be singular. Refusing beats solving something that has no answer.
            if (chord == 0.0)
            {
                throw new ArgumentException(
                    $"Points {i - 1} and {i} are the same point. An interpolating curve cannot pass "
                    + "through two different points at the same parameter.",
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
    /// The clamped knot vector whose interior knots are running averages of the parameters.
    /// </summary>
    /// <remarks>
    /// De Boor's averaging. It is what guarantees every diagonal entry of the interpolation matrix
    /// is non-zero — the Schoenberg–Whitney condition — which is the difference between a system
    /// that is banded and well conditioned and one that is merely square.
    /// </remarks>
    private static KnotVector AveragedKnots(double[] parameters, int degree)
    {
        int n = parameters.Length;
        double[] knots = new double[n + degree + 1];

        for (int i = 0; i <= degree; i++)
        {
            knots[i] = 0.0;
            knots[^(i + 1)] = 1.0;
        }

        for (int j = 1; j <= n - degree - 1; j++)
        {
            double sum = 0.0;
            for (int i = j; i <= j + degree - 1; i++)
            {
                sum += parameters[i];
            }

            knots[j + degree] = sum / degree;
        }

        return new KnotVector(degree, knots);
    }

    /// <summary>
    /// Gaussian elimination with partial pivoting, solving for three right-hand sides at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dense rather than banded. The matrix <i>is</i> banded and a band solver would be O(n·p²)
    /// against this O(n³), which matters at a thousand points and does not at the tens a person
    /// draws — and a band solver written now would be a second thing to be right about before
    /// anything needs it. The point where this becomes the bottleneck is measurable, and when it
    /// is measured the replacement has this to check against.
    /// </para>
    /// <para>
    /// <b>Partial pivoting is not optional</b>, even though the averaged knots make the matrix
    /// diagonally dominant in practice: "in practice" is not a guarantee, and the failure without
    /// it is a division by a zero pivot producing a curve full of infinities rather than an
    /// exception.
    /// </para>
    /// </remarks>
    private static double[,] SolveInPlace(double[,] matrix, double[,] rightHand)
    {
        int n = matrix.GetLength(0);
        int columns = rightHand.GetLength(1);

        for (int pivot = 0; pivot < n; pivot++)
        {
            int best = pivot;
            for (int row = pivot + 1; row < n; row++)
            {
                if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[best, pivot]))
                {
                    best = row;
                }
            }

            if (best != pivot)
            {
                for (int column = 0; column < n; column++)
                {
                    (matrix[pivot, column], matrix[best, column]) =
                        (matrix[best, column], matrix[pivot, column]);
                }

                for (int column = 0; column < columns; column++)
                {
                    (rightHand[pivot, column], rightHand[best, column]) =
                        (rightHand[best, column], rightHand[pivot, column]);
                }
            }

            double diagonal = matrix[pivot, pivot];
            if (diagonal == 0.0)
            {
                throw new InvalidOperationException(
                    "The interpolation system is singular. Two points share a parameter, which "
                    + "should have been refused when the chord lengths were measured.");
            }

            for (int row = pivot + 1; row < n; row++)
            {
                double factor = matrix[row, pivot] / diagonal;
                if (factor == 0.0)
                {
                    continue;
                }

                for (int column = pivot; column < n; column++)
                {
                    matrix[row, column] -= factor * matrix[pivot, column];
                }

                for (int column = 0; column < columns; column++)
                {
                    rightHand[row, column] -= factor * rightHand[pivot, column];
                }
            }
        }

        for (int row = n - 1; row >= 0; row--)
        {
            for (int column = 0; column < columns; column++)
            {
                double sum = rightHand[row, column];
                for (int k = row + 1; k < n; k++)
                {
                    sum -= matrix[row, k] * rightHand[k, column];
                }

                rightHand[row, column] = sum / matrix[row, row];
            }
        }

        return rightHand;
    }

    /// <inheritdoc/>
    public override Curve Reversed()
    {
        // The control points and weights reverse together, and the knots are mirrored within the
        // domain so that the new curve covers the same parameter range in the opposite direction.
        // Reparameterising to 0..1 instead would be simpler and would silently change what every
        // parameter a caller is holding refers to.
        Point3d[] points = new Point3d[_controlPoints.Length];
        double[] weights = new double[_weights.Length];

        for (int i = 0; i < points.Length; i++)
        {
            points[i] = _controlPoints[^(i + 1)];
            weights[i] = _weights[^(i + 1)];
        }

        double[] knots = new double[Knots.Count];
        double sum = Domain.Min + Domain.Max;

        for (int i = 0; i < knots.Length; i++)
        {
            knots[i] = sum - Knots[Knots.Count - 1 - i];
        }

        return new NurbsCurve(points, new KnotVector(Degree, knots), weights);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Exact, not approximate, and that is what knot insertion buys. Both ends of the requested
    /// range are raised to full multiplicity — <c>degree</c> — which makes the curve pass through a
    /// control point there and leaves the piece between them describable on its own. The control
    /// points outside that window are then simply dropped.
    /// </para>
    /// <para>
    /// The result keeps the requested parameter range as its domain rather than being
    /// reparameterised to 0..1, so a caller holding parameters from before the trim can still use
    /// them.
    /// </para>
    /// </remarks>
    public override Curve Trimmed(in Interval domain)
    {
        Interval wanted = domain.MakeIncreasing();
        Interval whole = Domain;

        if (wanted.Min < whole.Min - 1e-12 || wanted.Max > whole.Max + 1e-12)
        {
            throw new ArgumentOutOfRangeException(
                nameof(domain), domain, $"The trim range must lie inside {whole.Min} to {whole.Max}.");
        }

        if (wanted.Length <= 0.0)
        {
            throw new ArgumentException("A trim range must have length.", nameof(domain));
        }

        int p = Degree;
        NurbsCurve current = this;

        // The start first, then the end. Doing it the other way round is equally correct and
        // harder to read, because the start insertion shifts every index the end one would use.
        int atStart = p - current.Knots.Multiplicity(wanted.Min);
        if (atStart > 0)
        {
            current = current.WithKnotInserted(wanted.Min, atStart);
        }

        int atEnd = p - current.Knots.Multiplicity(wanted.Max);
        if (atEnd > 0)
        {
            current = current.WithKnotInserted(wanted.Max, atEnd);
        }

        return current.Extract(wanted);
    }

    /// <summary>
    /// Takes the piece of a curve between two parameters already at full multiplicity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With both ends repeated <c>degree</c> times, the piece between them is described entirely by
    /// a contiguous window of the control points, and the window is found from the knots rather
    /// than counted. Let <c>la</c> be the index of the <b>last</b> knot equal to the start and
    /// <c>fb</c> the index of the <b>first</b> knot equal to the end: the window runs from
    /// <c>la - degree</c> to <c>fb - 1</c>, and the knots strictly between them are the interior
    /// knots the piece keeps.
    /// </para>
    /// <para>
    /// Last-of-the-start and first-of-the-end rather than the other way round, because a clamped
    /// curve's own ends already repeat <c>degree + 1</c> times: taking the first knot equal to the
    /// start would land one index early on exactly the case where the range is the whole domain.
    /// </para>
    /// </remarks>
    private NurbsCurve Extract(in Interval range)
    {
        double[] knots = Knots.ToArray();
        int p = Degree;

        int lastAtStart = -1;
        for (int i = 0; i < knots.Length; i++)
        {
            if (knots[i] == range.Min)
            {
                lastAtStart = i;
            }
        }

        int firstAtEnd = -1;
        for (int i = 0; i < knots.Length; i++)
        {
            if (knots[i] == range.Max)
            {
                firstAtEnd = i;
                break;
            }
        }

        if (lastAtStart < p || firstAtEnd < 0)
        {
            throw new InvalidOperationException(
                "The trim range was not raised to full multiplicity before extraction. This is a "
                + "bug in NurbsCurve.Trimmed, not in the caller's input.");
        }

        int first = lastAtStart - p;
        int count = firstAtEnd - lastAtStart + p;

        Point3d[] points = new Point3d[count];
        double[] weights = new double[count];

        for (int i = 0; i < count; i++)
        {
            points[i] = _controlPoints[first + i];
            weights[i] = _weights[first + i];
        }

        double[] kept = new double[count + p + 1];
        for (int i = 0; i <= p; i++)
        {
            kept[i] = range.Min;
            kept[kept.Length - 1 - i] = range.Max;
        }

        for (int i = 0; i < firstAtEnd - lastAtStart - 1; i++)
        {
            kept[p + 1 + i] = knots[lastAtStart + 1 + i];
        }

        return new NurbsCurve(points, new KnotVector(p, kept), weights);
    }

    /// <inheritdoc/>
    public override Curve TransformedBy(in Transform transform)
    {
        // The weights are untouched. A transform acts on positions, and a NURBS curve's shape is
        // affine-invariant precisely because the weights are not positions - transforming the
        // homogeneous coordinates directly would scale the weights and change the curve.
        Point3d[] points = new Point3d[_controlPoints.Length];

        for (int i = 0; i < points.Length; i++)
        {
            points[i] = transform * _controlPoints[i];
        }

        return new NurbsCurve(points, Knots, _weights);
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"NurbsCurve(degree {Degree}, {_controlPoints.Length} points{(IsRational ? ", rational" : string.Empty)})");

    /// <summary>
    /// How many equal spans tessellation starts from: one per distinct knot span.
    /// </summary>
    /// <remarks>
    /// A NURBS curve is a different polynomial in every knot span, and its derivative is generally
    /// discontinuous where two spans meet. Starting the adaptive subdivision from one span apiece
    /// puts a seed at each of those joins, so the midpoint deviation test is never measuring across
    /// a corner — which is where it would decide a straight-looking chord was good enough and miss
    /// the kink in the middle of it.
    /// </remarks>
    protected override int TessellationSeedSpans => Math.Max(1, DistinctSpans().Length - 1);

    /// <summary>
    /// The arc length, integrated <b>one knot span at a time</b>.
    /// </summary>
    /// <remarks>
    /// The base class integrates over equal spans across the whole domain, which is right for a
    /// curve that is one smooth piece and wrong here: Gauss–Legendre assumes the integrand is
    /// well approximated by a polynomial over the interval it is given, and a NURBS curve's speed
    /// is generally discontinuous at every interior knot. A degree-1 curve is the extreme case —
    /// it is a polyline, its speed is piecewise constant, and integrating across a corner gets the
    /// length wrong by an amount that looks like rounding and is not. Split at the knots and each
    /// piece is smooth, so each piece is exact to the rule's order.
    /// </remarks>
    /// <returns>The arc length.</returns>
    protected override double ComputeLength()
    {
        double[] spans = DistinctSpans();
        double total = 0.0;

        for (int i = 1; i < spans.Length; i++)
        {
            total += IntegrateSpeed(spans[i - 1], spans[i]);
        }

        return total;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The distinct knots. A NURBS curve is a different polynomial in each span and its speed is
    /// generally discontinuous where two meet, which is exactly what the sweep needs to know.
    /// </remarks>
    protected override IReadOnlyList<double> SpanBoundaries() => DistinctSpans();

    /// <summary>The distinct knot values bounding the domain's spans, in order.</summary>
    private double[] DistinctSpans()
    {
        List<double> spans = [Domain.Min];

        for (int i = Degree + 1; i < Knots.Count - Degree - 1; i++)
        {
            if (Knots[i] > spans[^1])
            {
                spans.Add(Knots[i]);
            }
        }

        if (Domain.Max > spans[^1])
        {
            spans.Add(Domain.Max);
        }

        return [.. spans];
    }

    /// <inheritdoc/>
    protected override Point3d Evaluate(double parameter)
    {
        double[] homogeneous = EvaluateHomogeneous(parameter, 0)[0];

        return new Point3d(
            homogeneous[0] / homogeneous[3],
            homogeneous[1] / homogeneous[3],
            homogeneous[2] / homogeneous[3]);
    }

    /// <inheritdoc/>
    protected override Vector3d EvaluateDerivative(double parameter)
    {
        double[][] derivatives = EvaluateHomogeneous(parameter, 1);

        // The quotient rule, once, in homogeneous form: C = A/w, so C' = (A' - w'C) / w.
        double[] a = derivatives[0];
        double[] d = derivatives[1];
        double w = a[3];

        return new Vector3d(
            (d[0] - (d[3] * a[0] / w)) / w,
            (d[1] - (d[3] * a[1] / w)) / w,
            (d[2] - (d[3] * a[2] / w)) / w);
    }

    /// <inheritdoc/>
    protected override Vector3d EvaluateSecondDerivative(double parameter)
    {
        double[][] derivatives = EvaluateHomogeneous(parameter, 2);

        double[] a = derivatives[0];
        double[] d1 = derivatives[1];
        double[] d2 = derivatives[2];
        double w = a[3];

        // Differentiating C' = (A' - w'C)/w once more:
        //   C'' = (A'' - 2w'C' - w''C) / w
        // which needs C and C' first. Written out rather than folded, because the folded form is
        // where a sign error hides and nothing downstream would show it except a wrong curvature.
        double[] point = new double[3];
        double[] first = new double[3];

        for (int i = 0; i < 3; i++)
        {
            point[i] = a[i] / w;
            first[i] = (d1[i] - (d1[3] * point[i])) / w;
        }

        return new Vector3d(
            (d2[0] - (2 * d1[3] * first[0]) - (d2[3] * point[0])) / w,
            (d2[1] - (2 * d1[3] * first[1]) - (d2[3] * point[1])) / w,
            (d2[2] - (2 * d1[3] * first[2]) - (d2[3] * point[2])) / w);
    }

    /// <summary>
    /// The homogeneous point and its derivatives up to an order, by de Boor's A3.2.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    /// <param name="order">The highest derivative wanted.</param>
    /// <returns>
    /// <paramref name="order"/> + 1 four-component vectors: the homogeneous point, then its
    /// derivatives in order.
    /// </returns>
    /// <remarks>
    /// Derivatives above the degree are identically zero and are returned as zero rather than
    /// computed — the basis-function recurrence would divide by zero reaching for them, and a
    /// degree-1 curve being asked for its second derivative is an ordinary thing for
    /// <see cref="Curve.NormalAt"/> to do.
    /// </remarks>
    private double[][] EvaluateHomogeneous(double parameter, int order)
    {
        double t = Math.Clamp(parameter, Domain.Min, Domain.Max);
        int span = Knots.FindSpan(t);
        int available = Math.Min(order, Degree);

        double[][] basis = BasisDerivatives(span, t, available);
        double[][] result = new double[order + 1][];

        for (int k = 0; k <= order; k++)
        {
            result[k] = new double[4];

            if (k > available)
            {
                continue;
            }

            for (int j = 0; j <= Degree; j++)
            {
                int index = span - Degree + j;

                for (int component = 0; component < 4; component++)
                {
                    result[k][component] += basis[k][j] * _homogeneous[index, component];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The non-zero basis functions and their derivatives at a parameter — de Boor's A2.3.
    /// </summary>
    /// <param name="span">The knot span.</param>
    /// <param name="parameter">The parameter.</param>
    /// <param name="order">The highest derivative wanted, at most the degree.</param>
    /// <returns>
    /// A table indexed by derivative order then by basis index, <c>[order + 1][Degree + 1]</c>.
    /// </returns>
    private double[][] BasisDerivatives(int span, double parameter, int order)
    {
        int p = Degree;
        double[,] ndu = new double[p + 1, p + 1];
        double[] left = new double[p + 1];
        double[] right = new double[p + 1];

        ndu[0, 0] = 1.0;

        for (int j = 1; j <= p; j++)
        {
            left[j] = parameter - Knots[span + 1 - j];
            right[j] = Knots[span + j] - parameter;

            double saved = 0.0;
            for (int r = 0; r < j; r++)
            {
                // The lower triangle holds the knot differences; the upper holds the basis values.
                // Storing both in one table is what makes A2.3 cheap, and is also why it reads
                // like nothing else in this file.
                ndu[j, r] = right[r + 1] + left[j - r];
                double temp = ndu[j, r] == 0.0 ? 0.0 : ndu[r, j - 1] / ndu[j, r];

                ndu[r, j] = saved + (right[r + 1] * temp);
                saved = left[j - r] * temp;
            }

            ndu[j, j] = saved;
        }

        double[][] derivatives = new double[order + 1][];
        for (int k = 0; k <= order; k++)
        {
            derivatives[k] = new double[p + 1];
        }

        for (int j = 0; j <= p; j++)
        {
            derivatives[0][j] = ndu[j, p];
        }

        double[,] a = new double[2, p + 1];

        for (int r = 0; r <= p; r++)
        {
            int s1 = 0;
            int s2 = 1;
            a[0, 0] = 1.0;

            for (int k = 1; k <= order; k++)
            {
                double d = 0.0;
                int rk = r - k;
                int pk = p - k;

                if (r >= k)
                {
                    a[s2, 0] = a[s1, 0] / ndu[pk + 1, rk];
                    d = a[s2, 0] * ndu[rk, pk];
                }

                int j1 = rk >= -1 ? 1 : -rk;
                int j2 = r - 1 <= pk ? k - 1 : p - r;

                for (int j = j1; j <= j2; j++)
                {
                    a[s2, j] = (a[s1, j] - a[s1, j - 1]) / ndu[pk + 1, rk + j];
                    d += a[s2, j] * ndu[rk + j, pk];
                }

                if (r <= pk)
                {
                    a[s2, k] = -a[s1, k - 1] / ndu[pk + 1, r];
                    d += a[s2, k] * ndu[r, pk];
                }

                derivatives[k][r] = d;

                (s1, s2) = (s2, s1);
            }
        }

        // The factor p!/(p-k)!, applied last. Folding it into the recurrence would multiply it in
        // repeatedly, which is the classic way this algorithm comes out wrong by a factorial.
        int factor = p;
        for (int k = 1; k <= order; k++)
        {
            for (int j = 0; j <= p; j++)
            {
                derivatives[k][j] *= factor;
            }

            factor *= p - k;
        }

        return derivatives;
    }
}
