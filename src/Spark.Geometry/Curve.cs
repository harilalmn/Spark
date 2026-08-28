using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// The base of every curve: a bounded, continuous path through space, parameterised over
/// <see cref="Domain"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Curves are sealed classes, not structs.</b> A curve carries a variable amount of state — a
/// <see cref="PolyLine"/> can hold a hundred thousand points — and it is passed by reference
/// through the graph rather than copied per element. Every concrete curve is immutable and sealed,
/// its backing arrays are never handed out, and the constructor of this class is
/// <c>private protected</c>, so the set of curve types is closed to this assembly. A third-party
/// package extends Spark by writing nodes over these types, not by adding a curve type the
/// tessellator and the serialiser have never heard of.
/// </para>
/// <para>
/// <b>Every curve has its own domain, and none of them is assumed to be [0, 1].</b>
/// A <see cref="Line"/> runs over [0, 1], a <see cref="Circle"/> over [0, 2π] in radians, and a
/// <see cref="PolyLine"/> over [0, n] with one unit per segment, so that its integer parameters are
/// its vertices. Ask <see cref="Domain"/> rather than assuming, and use
/// <see cref="Interval.Normalise(double)"/> and <see cref="Interval.Denormalise(double)"/> to move
/// between a domain parameter and a fraction of the way along. The node library exposes the
/// normalised form, because that is what a Dynamo user expects; the kernel exposes the honest one,
/// because a NURBS curve's knot domain is not going to be [0, 1] either.
/// </para>
/// <para>
/// <b>Parameter, length and fraction are three different things, and the difference is not
/// cosmetic.</b> A parameter is where you are in the curve's own parameterisation; a length is a
/// distance measured along the curve from its start. On a <see cref="Line"/> and a
/// <see cref="Circle"/> the two are proportional; on an ellipse, and on every NURBS curve to come,
/// they are not — the parameter halfway along the domain of an ellipse is not the point halfway
/// along it. <i>Divide this curve into ten equal lengths</i> is the single most common thing anyone
/// asks of a curve, so arc-length reparameterisation is part of this contract from the first slice
/// rather than bolted on later: see <see cref="LengthAt(double)"/>,
/// <see cref="ParameterAtLength(double)"/>, <see cref="DivideEqually(int)"/> and
/// <see cref="DivideByLength(double)"/>.
/// </para>
/// <para>
/// <b>What this slice does not have, deliberately.</b> There is no closest-point query, no
/// intersection, no offset and no projection; those need the ray caster and the planar layer, and
/// they are M3 work. There is no value equality on curves either: two curves that draw the same
/// path through different parameterisations are a tolerance question rather than an
/// <see cref="object.Equals(object)"/> question, and answering it wrongly by default is worse than
/// not answering it.
/// </para>
/// </remarks>
public abstract class Curve
{
    /// <summary>
    /// The most points <see cref="Tessellate(in Tolerance)"/> will ever emit, whatever tolerance it
    /// is given. A tolerance far below a curve's size would otherwise ask for an unbounded array,
    /// and a viewport that hangs is worse than a chord that is a micron out.
    /// </summary>
    private const int MaximumTessellationPoints = 100_000;

    /// <summary>
    /// How many spans the arc-length table holds. Each span is integrated with a ten-point
    /// Gauss–Legendre rule, so the table is far more accurate than linear interpolation between its
    /// entries would suggest; the entries exist to bracket a length, and Newton refines from there.
    /// </summary>
    private const int ArcLengthTableSpans = 64;

    // Ten-point Gauss-Legendre nodes and weights on [-1, 1]. Ten points integrate a polynomial of
    // degree 19 exactly, which is more than any speed function here needs, and it costs ten
    // derivative evaluations per span rather than the hundreds an adaptive Simpson would take.
    private static readonly double[] GaussNodes =
    [
        -0.9739065285171717, -0.8650633666889845, -0.6794095682990244, -0.4333953941292472,
        -0.1488743389816312, 0.1488743389816312, 0.4333953941292472, 0.6794095682990244,
        0.8650633666889845, 0.9739065285171717,
    ];

    private static readonly double[] GaussWeights =
    [
        0.0666713443086881, 0.1494513491505806, 0.2190863625159820, 0.2692667193099963,
        0.2955242247147529, 0.2955242247147529, 0.2692667193099963, 0.2190863625159820,
        0.1494513491505806, 0.0666713443086881,
    ];

    private double[]? _arcLengths;
    private double _length = -1.0;
    private BoundingBox _boundingBox;
    private bool _boundingBoxComputed;

    /// <summary>
    /// Creates the base part of a curve. It is <c>private protected</c> so that the curve hierarchy
    /// is closed to this assembly; see the remarks on <see cref="Curve"/>.
    /// </summary>
    private protected Curve()
    {
    }

    /// <summary>
    /// The interval of parameters over which this curve is defined. Always increasing, and always
    /// of non-zero length.
    /// </summary>
    public abstract Interval Domain { get; }

    /// <summary>
    /// Whether the curve returns to where it started, so that <see cref="StartPoint"/> and
    /// <see cref="EndPoint"/> coincide.
    /// </summary>
    public abstract bool IsClosed { get; }

    /// <summary>The point at the start of the domain.</summary>
    public Point3d StartPoint => Evaluate(Domain.Min);

    /// <summary>The point at the end of the domain.</summary>
    public Point3d EndPoint => Evaluate(Domain.Max);

    /// <summary>
    /// The arc length of the whole curve. Computed once and remembered, which is part of why curves
    /// are immutable: a mutable curve would have to invalidate this, and every caller's copy of it.
    /// </summary>
    public double Length
    {
        get
        {
            if (_length < 0.0)
            {
                _length = ComputeLength();
            }

            return _length;
        }
    }

    /// <summary>
    /// The axis-aligned bounding box of the curve.
    /// </summary>
    /// <remarks>
    /// Analytic curves compute this exactly. Anything without a closed form falls back to the
    /// bounds of a fine tessellation, <b>inflated by that tessellation's tolerance</b>, so that the
    /// box is guaranteed to contain the curve rather than to hug it. A bounding box slightly too
    /// small is a culling bug that shows up as geometry vanishing at the edge of the screen; a box
    /// slightly too large costs nothing.
    /// </remarks>
    public BoundingBox BoundingBox
    {
        get
        {
            if (!_boundingBoxComputed)
            {
                _boundingBox = ComputeBoundingBox();
                _boundingBoxComputed = true;
            }

            return _boundingBox;
        }
    }

    /// <summary>The point at a parameter.</summary>
    /// <param name="parameter">
    /// A parameter in <see cref="Domain"/>. On a closed curve a parameter outside the domain wraps;
    /// on an open one it is an error rather than an extrapolation.
    /// </param>
    /// <returns>The point.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is not finite, or lies outside <see cref="Domain"/>
    /// on a curve that is not closed.
    /// </exception>
    public Point3d PointAt(double parameter) => Evaluate(CheckParameter(parameter));

    /// <summary>The unit tangent at a parameter, pointing along increasing parameter.</summary>
    /// <param name="parameter">A parameter in <see cref="Domain"/>.</param>
    /// <returns>A unit vector.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is not finite, or lies outside <see cref="Domain"/>
    /// on a curve that is not closed.
    /// </exception>
    public Vector3d TangentAt(double parameter) =>
        EvaluateDerivative(CheckParameter(parameter)).Normalised();

    /// <summary>
    /// The unit principal normal at a parameter: the direction the curve is turning towards.
    /// </summary>
    /// <remarks>
    /// A straight span has no principal normal — the second derivative vanishes, and every
    /// direction perpendicular to the tangent is equally deserving. Rather than return a zero
    /// vector, which would carry a meaningless value far from its cause, this returns a
    /// deterministic perpendicular chosen by the same rule <see cref="Plane"/> uses to seed a frame
    /// from a normal, so the same straight curve always yields the same normal.
    /// </remarks>
    /// <param name="parameter">A parameter in <see cref="Domain"/>.</param>
    /// <returns>A unit vector perpendicular to <see cref="TangentAt(double)"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is not finite, or lies outside <see cref="Domain"/>
    /// on a curve that is not closed.
    /// </exception>
    public Vector3d NormalAt(double parameter)
    {
        double valid = CheckParameter(parameter);
        Vector3d tangent = EvaluateDerivative(valid).Normalised();
        Vector3d second = EvaluateSecondDerivative(valid);

        // Gram-Schmidt: strip the component along the tangent and what is left is the turning
        // direction. The threshold is relative to the second derivative's own size, because on a
        // curve measured in kilometres a "small" residual is not small at all.
        Vector3d turning = second - (tangent * second.Dot(tangent));
        double scale = Math.Max(second.Length, 1.0);
        if (turning.Length > scale * 1e-9 && turning.TryNormalise(out Vector3d principal))
        {
            return principal;
        }

        Vector3d seed = Math.Abs(tangent.Z) > 0.9 ? Vector3d.YAxis : Vector3d.ZAxis;
        return seed.Cross(tangent).Normalised();
    }

    /// <summary>
    /// The plane through the point at a parameter whose normal is the curve's tangent — the plane a
    /// profile would be swept in.
    /// </summary>
    /// <param name="parameter">A parameter in <see cref="Domain"/>.</param>
    /// <returns>A plane whose origin is on the curve and whose normal is the unit tangent.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is not finite, or lies outside <see cref="Domain"/>
    /// on a curve that is not closed.
    /// </exception>
    public Plane PlaneAt(double parameter)
    {
        double valid = CheckParameter(parameter);
        Vector3d tangent = EvaluateDerivative(valid).Normalised();
        Vector3d normal = NormalAt(valid);
        return Plane.ByOriginXAxisYAxis(Evaluate(valid), normal, tangent.Cross(normal));
    }

    /// <summary>
    /// The Frenet frame at a parameter: x along the tangent, y along the principal normal, z along
    /// the binormal.
    /// </summary>
    /// <param name="parameter">A parameter in <see cref="Domain"/>.</param>
    /// <returns>A right-handed coordinate system with its origin on the curve.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is not finite, or lies outside <see cref="Domain"/>
    /// on a curve that is not closed.
    /// </exception>
    public CoordinateSystem CoordinateSystemAt(double parameter)
    {
        double valid = CheckParameter(parameter);
        Vector3d tangent = EvaluateDerivative(valid).Normalised();
        return CoordinateSystem.ByOriginXAxisYAxis(Evaluate(valid), tangent, NormalAt(valid));
    }

    /// <summary>The arc length from the start of the curve to a parameter.</summary>
    /// <param name="parameter">A parameter in <see cref="Domain"/>.</param>
    /// <returns>A distance along the curve, between zero and <see cref="Length"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is not finite, or lies outside <see cref="Domain"/>
    /// on a curve that is not closed.
    /// </exception>
    public virtual double LengthAt(double parameter) =>
        IntegrateSpeed(Domain.Min, CheckParameter(parameter));

    /// <summary>
    /// The parameter at which a given arc length from the start has been travelled — the inverse of
    /// <see cref="LengthAt(double)"/>.
    /// </summary>
    /// <remarks>
    /// This is the member that makes <i>divide into equal lengths</i> possible. Curves whose speed
    /// is constant — every one in this slice except <see cref="EllipseCurve"/> — override it with
    /// the exact expression. The fallback here brackets the answer in a precomputed table and
    /// refines it with Newton's method against the speed, falling back to bisection on any step
    /// that leaves the bracket.
    /// </remarks>
    /// <param name="distance">
    /// A distance along the curve from its start. Clamped to <c>[0, Length]</c>: asking for the
    /// point a micron past the end of a curve during a division is arithmetic noise rather than a
    /// caller error.
    /// </param>
    /// <returns>The parameter.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="distance"/> is not finite.
    /// </exception>
    public virtual double ParameterAtLength(double distance)
    {
        if (!double.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance), distance, "A distance along a curve must be finite.");
        }

        double total = Length;
        double target = Math.Clamp(distance, 0.0, total);
        if (target <= 0.0)
        {
            return Domain.Min;
        }

        if (target >= total)
        {
            return Domain.Max;
        }

        double[] table = ArcLengthTable();
        double step = Domain.Length / ArcLengthTableSpans;
        int span = LocateSpan(table, target);

        // The span's start and the length consumed before it stay fixed for the whole refinement,
        // so every error is measured against the same origin. Only the bracket moves.
        double spanStart = Domain.Min + (span * step);
        double consumed = table[span];
        double across = Math.Max(table[span + 1] - consumed, 1e-300);
        double low = spanStart;
        double high = Math.Min(spanStart + step, Domain.Max);
        double parameter = spanStart + ((target - consumed) / across * (high - spanStart));

        // Newton on length(t) - target, whose derivative is the speed. The bracket is not belt and
        // braces: where the speed approaches zero a Newton step can leap out of the span entirely.
        for (int iteration = 0; iteration < 32; iteration++)
        {
            // The convergence threshold is relative to the curve's own length rather than absolute.
            // An absolute one is a different test at every working scale: on a curve a nanometre
            // long, 1e-12 is a tenth of a percent, and on one a billion units long it is unreachable.
            double error = consumed + IntegrateSpeed(spanStart, parameter) - target;
            if (Math.Abs(error) <= Math.Max(total * 1e-14, 1e-300))
            {
                break;
            }

            if (error > 0.0)
            {
                high = parameter;
            }
            else
            {
                low = parameter;
            }

            double speed = EvaluateDerivative(parameter).Length;
            double next = speed > 1e-300 ? parameter - (error / speed) : (low + high) * 0.5;
            parameter = next > low && next < high ? next : (low + high) * 0.5;
        }

        return Math.Clamp(parameter, Domain.Min, Domain.Max);
    }

    /// <summary>The point at a given arc length from the start of the curve.</summary>
    /// <param name="distance">A distance along the curve, clamped to <c>[0, Length]</c>.</param>
    /// <returns>The point.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="distance"/> is not finite.
    /// </exception>
    public Point3d PointAtLength(double distance) => Evaluate(ParameterAtLength(distance));

    /// <summary>Divides the curve into equal arc lengths and returns the points between them.</summary>
    /// <param name="segments">How many equal pieces to divide into. At least one.</param>
    /// <returns>
    /// <paramref name="segments"/> + 1 points, <b>including both ends</b>, ordered from the start of
    /// the curve. On a closed curve the last point coincides with the first, which is what makes the
    /// result a closed loop rather than a loop with a gap in it.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="segments"/> is less than one.
    /// </exception>
    public Point3d[] DivideEqually(int segments)
    {
        if (segments < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(segments), segments, "A curve must be divided into at least one segment.");
        }

        double total = Length;
        Point3d[] points = new Point3d[segments + 1];
        for (int index = 0; index <= segments; index++)
        {
            points[index] = Evaluate(ParameterAtLength(total * index / segments));
        }

        return points;
    }

    /// <summary>
    /// Places points along the curve at a fixed spacing measured along it, starting at the start
    /// point.
    /// </summary>
    /// <param name="length">The spacing. Must be positive and finite.</param>
    /// <returns>
    /// The points, beginning with <see cref="StartPoint"/>. The end point appears only when the
    /// curve's length is a whole multiple of the spacing to within a part in a million; the
    /// remainder is otherwise dropped, because a caller who asked for a fixed spacing did not ask
    /// for one short segment at the end.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="length"/> is not positive and finite.
    /// </exception>
    public Point3d[] DivideByLength(double length)
    {
        if (!double.IsFinite(length) || length <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), length, "A division spacing must be positive and finite.");
        }

        double total = Length;
        int count = (int)Math.Floor((total / length) + 1e-6);
        List<Point3d> points = new(count + 1);
        for (int index = 0; index <= count; index++)
        {
            double distance = length * index;
            if (distance > total)
            {
                break;
            }

            points.Add(Evaluate(ParameterAtLength(distance)));
        }

        return [.. points];
    }

    /// <summary>
    /// Approximates the curve as a polyline whose chords stay within a tolerance of it.
    /// </summary>
    /// <remarks>
    /// The subdivision is adaptive on the chord's deviation from the curve, measured at the
    /// parameter halfway along each span, so a nearly straight stretch of a polycurve costs two
    /// points and a tight fillet costs many. The point count is capped at 100,000 however fine the
    /// tolerance: an unbounded array is a hang, and a hang is worse than a visible facet.
    /// </remarks>
    /// <param name="tolerance">
    /// The largest distance a chord may deviate from the curve. Its <see cref="Tolerance.Linear"/>
    /// component is the one that matters here.
    /// </param>
    /// <returns>
    /// At least two points, starting at <see cref="StartPoint"/> and ending at
    /// <see cref="EndPoint"/>.
    /// </returns>
    public virtual Point3d[] Tessellate(in Tolerance tolerance = default)
    {
        double sag = tolerance.Linear;
        int seeds = Math.Max(1, TessellationSeedSpans);
        double step = Domain.Length / seeds;

        List<Point3d> points = new(seeds + 1) { Evaluate(Domain.Min) };
        for (int index = 0; index < seeds; index++)
        {
            double low = Domain.Min + (index * step);
            double high = index == seeds - 1 ? Domain.Max : low + step;
            Subdivide(low, points[^1], high, Evaluate(high), sag, 0, points);
            points.Add(Evaluate(high));
        }

        return [.. points];
    }

    /// <summary>Returns the same curve traversed in the opposite direction.</summary>
    /// <returns>A new curve. The original is unchanged.</returns>
    public abstract Curve Reversed();

    /// <summary>Returns the part of the curve between two parameters.</summary>
    /// <param name="domain">
    /// The sub-domain to keep. Must have non-zero length and lie within <see cref="Domain"/>, except
    /// on a closed curve, where it may wrap past the seam.
    /// </param>
    /// <returns>A new curve. The original is unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="domain"/> is not valid, is empty, or is not contained in
    /// <see cref="Domain"/> on a curve that is not closed.
    /// </exception>
    public abstract Curve Trimmed(in Interval domain);

    /// <summary>Returns the curve mapped through a transform.</summary>
    /// <remarks>
    /// The method is not called <c>Transform</c> because <see cref="Spark.Geometry.Transform"/> is
    /// the type it takes, and a member whose name shadows a type in its own signature is a trap for
    /// the next reader rather than a convenience.
    /// </remarks>
    /// <param name="transform">The transform to apply.</param>
    /// <returns>A new curve. The original is unchanged.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="transform"/> is not affine, or when it is affine but would take
    /// the curve outside the set of shapes its own type can represent — a non-uniform scale applied
    /// to a <see cref="Circle"/>, for instance.
    /// </exception>
    public abstract Curve TransformedBy(in Transform transform);

    /// <summary>
    /// How many equal spans <see cref="Tessellate(in Tolerance)"/> starts from before it begins
    /// subdividing adaptively.
    /// </summary>
    /// <remarks>
    /// One is right for anything whose chord is a useful first approximation. Closed curves override
    /// it: the chord of a full circle is a point, its midpoint deviation test would be measuring the
    /// wrong thing entirely, and starting from four quadrant spans avoids the question.
    /// </remarks>
    protected virtual int TessellationSeedSpans => 1;

    /// <summary>The point at a parameter already known to be valid.</summary>
    /// <param name="parameter">A parameter inside <see cref="Domain"/>.</param>
    /// <returns>The point.</returns>
    protected abstract Point3d Evaluate(double parameter);

    /// <summary>
    /// The first derivative with respect to the parameter, at a parameter already known to be
    /// valid. It is not normalised: its length is the curve's speed, which is what the arc-length
    /// integral needs.
    /// </summary>
    /// <param name="parameter">A parameter inside <see cref="Domain"/>.</param>
    /// <returns>The derivative.</returns>
    protected abstract Vector3d EvaluateDerivative(double parameter);

    /// <summary>
    /// The second derivative with respect to the parameter, at a parameter already known to be
    /// valid. Zero on a straight span, which <see cref="NormalAt(double)"/> handles explicitly.
    /// </summary>
    /// <param name="parameter">A parameter inside <see cref="Domain"/>.</param>
    /// <returns>The second derivative.</returns>
    protected abstract Vector3d EvaluateSecondDerivative(double parameter);

    /// <summary>
    /// Computes the arc length. Overridden by every curve with a closed form; the fallback
    /// integrates the speed over the domain with a ten-point Gauss–Legendre rule per span.
    /// </summary>
    /// <returns>The arc length.</returns>
    protected virtual double ComputeLength() => IntegrateSpeed(Domain.Min, Domain.Max);

    /// <summary>
    /// Computes the bounding box. Overridden by every curve with a closed form; the fallback takes
    /// the bounds of a fine tessellation and inflates them by its tolerance, so that the result
    /// contains the curve.
    /// </summary>
    /// <returns>The bounding box.</returns>
    protected virtual BoundingBox ComputeBoundingBox()
    {
        double sag = Math.Max(Length * 1e-6, 1e-12);
        Point3d[] points = Tessellate(new Tolerance(sag, Angle.FromDegrees(0.001), 1e-12));
        return BoundingBox.FromPoints(points).Inflate(sag);
    }

    /// <summary>
    /// Validates a parameter and returns the one to evaluate at: the same value on an open curve,
    /// and the value wrapped into the domain on a closed one.
    /// </summary>
    /// <param name="parameter">The caller's parameter.</param>
    /// <returns>A parameter inside <see cref="Domain"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is not finite, or lies outside <see cref="Domain"/>
    /// on a curve that is not closed.
    /// </exception>
    protected double CheckParameter(double parameter)
    {
        if (!double.IsFinite(parameter))
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameter), parameter, "A curve parameter must be finite.");
        }

        Interval domain = Domain;
        if (IsClosed && (parameter < domain.Min || parameter > domain.Max))
        {
            // Only a parameter genuinely outside the domain wraps. The end of the domain must be
            // left alone even though it evaluates to the same point as the start: it is a different
            // *distance* along the curve, and wrapping it to the start made LengthAt(Domain.Max)
            // return zero instead of the full length, which broke the last step of every division.
            double span = domain.Length;
            double offset = (parameter - domain.Min) % span;
            return domain.Min + (offset < 0.0 ? offset + span : offset);
        }

        if (IsClosed)
        {
            return parameter;
        }

        // The slack is relative to the domain rather than absolute: a parameter one part in 1e12
        // past the end is an accumulated division, not a caller asking for extrapolation, and
        // rejecting it would make DivideEqually fail on its own last point.
        double slack = Math.Max(Math.Abs(domain.Length), 1.0) * 1e-12;
        if (parameter < domain.Min - slack || parameter > domain.Max + slack)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameter),
                parameter,
                $"The parameter is outside the curve's domain {domain}.");
        }

        return Math.Clamp(parameter, domain.Min, domain.Max);
    }

    /// <summary>
    /// The first derivative at a parameter already known to be valid, reachable from another curve
    /// in this assembly.
    /// </summary>
    /// <remarks>
    /// <see cref="EvaluateDerivative(double)"/> is <c>protected</c>, and C# only lets a derived type
    /// reach a protected member through an instance of its own type — so <see cref="PolyCurve"/>,
    /// which holds a <see cref="Curve"/> and needs its derivative for the chain rule, cannot call it.
    /// These two internal accessors are the seam. Without them a polycurve would have to
    /// differentiate its segments numerically, which is both slower and quietly less accurate.
    /// </remarks>
    /// <param name="parameter">A parameter inside <see cref="Domain"/>.</param>
    /// <returns>The derivative.</returns>
    internal Vector3d DerivativeWithin(double parameter) => EvaluateDerivative(parameter);

    /// <summary>
    /// The second derivative at a parameter already known to be valid, reachable from another curve
    /// in this assembly. See <see cref="DerivativeWithin(double)"/> for why it exists.
    /// </summary>
    /// <param name="parameter">A parameter inside <see cref="Domain"/>.</param>
    /// <returns>The second derivative.</returns>
    internal Vector3d SecondDerivativeWithin(double parameter) =>
        EvaluateSecondDerivative(parameter);

    /// <summary>
    /// Validates a trim domain against the curve's own domain. Shared by every curve that cannot be
    /// trimmed past its own ends, which is all of them except the closed ones.
    /// </summary>
    /// <param name="domain">The requested sub-domain. May be decreasing, which reverses the trim.</param>
    /// <param name="whole">The curve's own domain.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the sub-domain is invalid, empty, or not contained in the curve's domain.
    /// </exception>
    private protected static void CheckTrimDomain(in Interval domain, in Interval whole)
    {
        if (!double.IsFinite(domain.Min) || !double.IsFinite(domain.Max) || domain.Length == 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(domain), domain, "A trim domain must be finite and of non-zero length.");
        }

        Interval increasing = domain.MakeIncreasing();
        double slack = Math.Max(Math.Abs(whole.Length), 1.0) * 1e-12;
        if (increasing.Min < whole.Min - slack || increasing.Max > whole.Max + slack)
        {
            throw new ArgumentOutOfRangeException(
                nameof(domain), domain, $"A trim domain must lie inside the curve's domain {whole}.");
        }
    }

    /// <summary>
    /// Integrates the curve's speed between two parameters with a ten-point Gauss–Legendre rule per
    /// span, subdividing so that no span covers more than a sixty-fourth of the domain.
    /// </summary>
    /// <param name="from">The lower parameter.</param>
    /// <param name="to">The upper parameter.</param>
    /// <returns>
    /// The arc length between them, negative when <paramref name="to"/> precedes
    /// <paramref name="from"/>.
    /// </returns>
    private protected double IntegrateSpeed(double from, double to)
    {
        if (to == from)
        {
            return 0.0;
        }

        double sign = to < from ? -1.0 : 1.0;
        double low = Math.Min(from, to);
        double high = Math.Max(from, to);
        double widest = Domain.Length / ArcLengthTableSpans;
        int spans = Math.Max(1, (int)Math.Ceiling((high - low) / widest));
        double step = (high - low) / spans;
        double total = 0.0;

        for (int span = 0; span < spans; span++)
        {
            double half = step * 0.5;
            double mid = low + (span * step) + half;
            double sum = 0.0;
            for (int node = 0; node < GaussNodes.Length; node++)
            {
                sum += GaussWeights[node] * EvaluateDerivative(mid + (half * GaussNodes[node])).Length;
            }

            total += sum * half;
        }

        return total * sign;
    }

    private double[] ArcLengthTable()
    {
        if (_arcLengths is not null)
        {
            return _arcLengths;
        }

        double[] table = new double[ArcLengthTableSpans + 1];
        double step = Domain.Length / ArcLengthTableSpans;
        double running = 0.0;
        for (int span = 0; span < ArcLengthTableSpans; span++)
        {
            double low = Domain.Min + (span * step);
            running += IntegrateSpeed(low, low + step);
            table[span + 1] = running;
        }

        // The table's last entry and Length come from the same integral for the fallback, so this
        // scaling is a no-op there and a correction for a curve that overrode Length with an exact
        // expression. Either way ParameterAtLength(Length) lands on the end of the domain.
        double total = Length;
        if (running > 1e-300 && Math.Abs(total - running) > 1e-15 * Math.Max(1.0, total))
        {
            double factor = total / running;
            for (int index = 1; index <= ArcLengthTableSpans; index++)
            {
                table[index] *= factor;
            }
        }

        _arcLengths = table;
        return table;
    }

    private static int LocateSpan(double[] table, double target)
    {
        int low = 0;
        int high = table.Length - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (table[mid] <= target)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private void Subdivide(
        double low,
        in Point3d start,
        double high,
        in Point3d end,
        double sag,
        int depth,
        List<Point3d> points)
    {
        if (points.Count >= MaximumTessellationPoints)
        {
            return;
        }

        double mid = (low + high) * 0.5;
        Point3d curve = Evaluate(mid);
        Point3d chord = Point3d.Lerp(start, end, 0.5);

        // Depth 20 is a million spans per seed span, far past the point cap; it is here so that a
        // curve with a cusp cannot recurse forever on a deviation that never shrinks.
        if (depth >= 20 || curve.DistanceTo(chord) <= sag)
        {
            return;
        }

        Subdivide(low, start, mid, curve, sag, depth + 1, points);
        points.Add(curve);
        Subdivide(mid, curve, high, end, sag, depth + 1, points);
    }
}
