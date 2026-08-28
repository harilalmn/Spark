using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// The base of every curve in the kernel: a continuous map from a real parameter interval into
/// three-dimensional space.
/// </summary>
/// <remarks>
/// <para>
/// <b>Analytic curves are first class.</b> <see cref="Line"/>, <see cref="Arc"/>,
/// <see cref="Circle"/> and <see cref="EllipseCurve"/> carry their own defining data and
/// evaluate from closed forms; they are not <see cref="NurbsCurve"/>s wearing a different name.
/// That buys exactness — a circle's radius stays the number the user typed — memory, and the
/// ability to recognise a circle again later, which matters to everything from dimensioning to
/// export. <see cref="ToNurbsCurve"/> converts on demand; it does not define.
/// </para>
/// <para>
/// <b>Parameterisation is per type and is documented per type.</b> There is no repository-wide
/// promise that the domain is <c>[0, 1]</c> or that the parameter is arc length. What every
/// curve does promise is that <see cref="Domain"/> is increasing and finite, that
/// <see cref="PointAt(double)"/> is continuous over it, and that
/// <see cref="LengthAt(double, in Tolerance)"/> is non-decreasing. Use
/// <see cref="ParameterAtLength(double, in Tolerance)"/> to work in arc length.
/// </para>
/// <para>
/// <b>Parameters outside the domain are clamped, not rejected.</b> A parameter arrives at these
/// methods after arithmetic far more often than it arrives from a literal, and a domain end
/// missed by one unit in the last place is not a caller error worth an exception.
/// <see cref="double.NaN"/> <i>is</i> a caller error and throws. The one member that does not
/// clamp is <see cref="Split(double)"/>, because splitting at an end has no sensible answer.
/// </para>
/// <para>
/// <b>Immutability.</b> Every curve is immutable and every operation returns a new curve.
/// Backing arrays are never handed out; the collection properties return
/// <see cref="ReadOnlySpan{T}"/> or copies.
/// </para>
/// <para>
/// The hierarchy is closed for now: the constructor is <c>private protected</c>, so curve types
/// outside this assembly are not possible. That is deliberate and reversible — opening it later
/// is an additive change, while closing it later would not be.
/// </para>
/// </remarks>
public abstract class Curve
{
    /// <summary>
    /// How many parameters the generic closest-point and planarity fallbacks sample before
    /// refining. Sixty-four is enough to separate the local minima of a curve with a handful of
    /// inflections; types whose complexity is unbounded — <see cref="NurbsCurve"/>,
    /// <see cref="PolyCurve"/>, <see cref="PolyLine"/> — raise it in proportion to their span
    /// count rather than relying on this.
    /// </summary>
    private protected const int DefaultSeedCount = 64;

    /// <summary>
    /// Initialises the base of a curve. Not accessible outside this assembly: see the class
    /// remarks on why the hierarchy is closed.
    /// </summary>
    private protected Curve()
    {
    }

    /// <summary>
    /// The interval of parameters over which the curve is defined. Always increasing, always
    /// finite, and always of positive length: a curve with an empty domain cannot be
    /// constructed. The meaning of the parameter is documented on each concrete type.
    /// </summary>
    public abstract Interval Domain { get; }

    /// <summary>
    /// <see langword="true"/> when the curve's start and end points are exactly coincident,
    /// by the same IEEE-exact rule <c>operator ==</c> uses across the value layer.
    /// </summary>
    /// <remarks>
    /// This is an exact test on purpose. A tolerant "is this closed enough" question is a
    /// different question, it needs a tolerance, and answering it silently here would make a
    /// property of the curve depend on a number the caller never supplied.
    /// </remarks>
    public abstract bool IsClosed { get; }

    /// <summary>
    /// <see langword="true"/> when the curve closes on itself <i>and</i> its parameterisation
    /// wraps smoothly at the seam, so that evaluating either side of the join gives the same
    /// point and the same tangent direction.
    /// </summary>
    /// <remarks>
    /// A full <see cref="Circle"/> and a full <see cref="EllipseCurve"/> are periodic. A closed
    /// <see cref="PolyLine"/> is not — it has a corner at the seam. A closed
    /// <see cref="NurbsCurve"/> is not, because Spark's NURBS curves are always clamped and so
    /// have a genuine start and end even when those coincide.
    /// </remarks>
    public abstract bool IsPeriodic { get; }

    /// <summary>
    /// The curve's bounding box, axis-aligned in world coordinates.
    /// </summary>
    /// <remarks>
    /// Tight for every analytic curve, for <see cref="PolyLine"/> and for
    /// <see cref="PolyCurve"/> over tight segments. For <see cref="NurbsCurve"/> it is the box
    /// of the control points, which contains the curve by the convex-hull property but can be
    /// noticeably larger than the tightest box. The contract is therefore <i>contains</i>, not
    /// <i>equals</i>: never assume a point on the boundary of this box lies on the curve.
    /// </remarks>
    public abstract BoundingBox BoundingBox { get; }

    /// <summary>The point at the start of the domain.</summary>
    public Point3d StartPoint => PointAt(Domain.Min);

    /// <summary>The point at the end of the domain.</summary>
    public Point3d EndPoint => PointAt(Domain.Max);

    /// <summary>
    /// The point on the curve at a parameter.
    /// </summary>
    /// <param name="parameter">
    /// The parameter, in the units of <see cref="Domain"/>. Values outside the domain are
    /// clamped to it.
    /// </param>
    /// <returns>The point.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is <see cref="double.NaN"/>.
    /// </exception>
    public abstract Point3d PointAt(double parameter);

    /// <summary>
    /// A derivative of the curve with respect to its parameter.
    /// </summary>
    /// <param name="parameter">
    /// The parameter, in the units of <see cref="Domain"/>. Values outside the domain are
    /// clamped to it.
    /// </param>
    /// <param name="order">
    /// The order of the derivative. Zero returns the position as a vector from the world
    /// origin, one the velocity, two the acceleration, and so on. Orders beyond what the curve
    /// can supply return <see cref="Vector3d.Zero"/> — for a <see cref="Line"/> that is every
    /// order above one, and for a <see cref="NurbsCurve"/> every order above its degree.
    /// </param>
    /// <returns>The derivative vector.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="order"/> is negative, or when <paramref name="parameter"/>
    /// is <see cref="double.NaN"/>.
    /// </exception>
    public abstract Vector3d DerivativeAt(double parameter, int order);

    /// <summary>
    /// The unit tangent at a parameter, pointing in the direction of increasing parameter.
    /// </summary>
    /// <param name="parameter">
    /// The parameter, in the units of <see cref="Domain"/>. Values outside the domain are
    /// clamped to it.
    /// </param>
    /// <returns>A unit vector.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is <see cref="double.NaN"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown where the first derivative vanishes, which is a cusp or a stationary
    /// parameterisation and has no tangent direction. Analytic curves never do this; a
    /// <see cref="NurbsCurve"/> with repeated interior control points can.
    /// </exception>
    public virtual Vector3d TangentAt(double parameter)
    {
        if (!DerivativeAt(parameter, 1).TryNormalise(out Vector3d tangent))
        {
            throw new InvalidOperationException(
                "The curve's first derivative vanishes at this parameter, so it has no tangent "
                + "direction there.");
        }

        return tangent;
    }

    /// <summary>
    /// The Frenet frame at a parameter, as a <see cref="Plane"/>.
    /// </summary>
    /// <param name="parameter">
    /// The parameter, in the units of <see cref="Domain"/>. Values outside the domain are
    /// clamped to it.
    /// </param>
    /// <returns>
    /// A plane whose origin is <see cref="PointAt(double)"/>, whose <see cref="Plane.XAxis"/>
    /// is the unit tangent, whose <see cref="Plane.YAxis"/> is the principal normal — pointing
    /// towards the centre of curvature — and whose <see cref="Plane.Normal"/> is therefore the
    /// binormal. The plane of the frame is the osculating plane.
    /// </returns>
    /// <remarks>
    /// Where the curvature is zero the principal normal is undefined, because every direction
    /// perpendicular to the tangent is equally good. In that case the frame's Y axis is chosen
    /// the same deterministic way <see cref="Plane(in Point3d, in Vector3d)"/> chooses one, so
    /// the answer is at least reproducible. It is <b>not</b> continuous through such a point:
    /// a curve with an inflection will show the frame flip there. That is a property of the
    /// Frenet frame itself, not of this implementation.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is <see cref="double.NaN"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown where the first derivative vanishes and there is no tangent.
    /// </exception>
    public virtual Plane FrameAt(double parameter)
    {
        Point3d origin = PointAt(parameter);
        Vector3d tangent = TangentAt(parameter);
        Vector3d second = DerivativeAt(parameter, 2);
        Vector3d radial = second - (tangent * second.Dot(tangent));

        if (!radial.TryNormalise(out Vector3d normal))
        {
            normal = new Plane(origin, tangent).XAxis;
        }

        return Plane.ByOriginXAxisYAxis(origin, tangent, normal);
    }

    /// <summary>
    /// The curvature at a parameter: the reciprocal of the radius of the osculating circle.
    /// </summary>
    /// <param name="parameter">
    /// The parameter, in the units of <see cref="Domain"/>. Values outside the domain are
    /// clamped to it.
    /// </param>
    /// <returns>
    /// A non-negative number in units of one over length. Zero on a straight stretch;
    /// <c>1 / radius</c> everywhere on a <see cref="Circle"/> or <see cref="Arc"/>. The
    /// quantity is unsigned because sign is only meaningful for a plane curve with a chosen
    /// normal, and a curve in space has no such choice.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is <see cref="double.NaN"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown where the first derivative vanishes, since curvature is undefined there.
    /// </exception>
    public virtual double CurvatureAt(double parameter)
    {
        Vector3d first = DerivativeAt(parameter, 1);
        double speed = first.Length;

        if (speed == 0.0)
        {
            throw new InvalidOperationException(
                "The curve's first derivative vanishes at this parameter, so its curvature is "
                + "undefined there.");
        }

        return first.Cross(DerivativeAt(parameter, 2)).Length / (speed * speed * speed);
    }

    /// <summary>
    /// The length of the whole curve.
    /// </summary>
    /// <param name="tolerance">
    /// The tolerance the length is computed to. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>. Analytic curves ignore it because their answer is
    /// closed-form and exact.
    /// </param>
    /// <returns>The arc length, always non-negative.</returns>
    public virtual double Length(in Tolerance tolerance = default) => LengthAt(Domain.Max, tolerance);

    /// <summary>
    /// The length of the curve from the start of its domain to a parameter.
    /// </summary>
    /// <param name="parameter">
    /// The parameter to measure to, in the units of <see cref="Domain"/>. Values outside the
    /// domain are clamped to it, so this never exceeds <see cref="Length(in Tolerance)"/> and
    /// is never negative.
    /// </param>
    /// <param name="tolerance">
    /// The tolerance the length is computed to. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// The arc length. Non-decreasing in <paramref name="parameter"/>, which is the property
    /// <see cref="ParameterAtLength(double, in Tolerance)"/> relies on to invert it.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is <see cref="double.NaN"/>.
    /// </exception>
    public virtual double LengthAt(double parameter, in Tolerance tolerance = default)
    {
        double end = ClampParameter(parameter);
        double estimate = ChordEstimate();

        return CurveNumerics.Integrate(Speed, Domain.Min, end, tolerance, estimate);
    }

    /// <summary>
    /// The parameter at which the curve has run a given arc length from its start.
    /// </summary>
    /// <param name="length">
    /// The arc length measured from <see cref="StartPoint"/>. A negative value returns
    /// <c>Domain.Min</c> and a value beyond the curve's own length returns <c>Domain.Max</c>,
    /// so this always answers with a parameter on the curve.
    /// </param>
    /// <param name="tolerance">
    /// The tolerance the inversion is solved to. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>The parameter.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="length"/> is <see cref="double.NaN"/>.
    /// </exception>
    public virtual double ParameterAtLength(double length, in Tolerance tolerance = default)
    {
        if (double.IsNaN(length))
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The arc length must not be NaN.");
        }

        if (length <= 0.0)
        {
            return Domain.Min;
        }

        double total = Length(tolerance);

        if (length >= total)
        {
            return Domain.Max;
        }

        Tolerance solveTolerance = tolerance;

        return CurveNumerics.SolveMonotone(
            t => LengthAt(t, solveTolerance),
            Speed,
            length,
            Domain.Min,
            Domain.Max,
            tolerance,
            total,
            Domain.Length);
    }

    /// <summary>
    /// The point on the curve closest to a given point.
    /// </summary>
    /// <param name="point">The point to measure from.</param>
    /// <param name="parameter">
    /// Receives the parameter of the returned point, always inside <see cref="Domain"/>.
    /// </param>
    /// <param name="tolerance">
    /// The tolerance the refinement is solved to. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// The closest point. Where several points are equally close — the centre of a circle is
    /// the extreme case, being equidistant from every point of it — the one returned is
    /// whichever the seeded search reached first, which is deterministic for a given curve but
    /// is not otherwise meaningful.
    /// </returns>
    /// <remarks>
    /// The generic implementation seeds by subdivision, keeps every local minimum of the
    /// sampled distance rather than only the best one, and refines each by Newton's method on
    /// <c>(C(t) - P) · C'(t) = 0</c> before choosing between them. Keeping the local minima is
    /// what stops a seed grid that straddles two nearly equal basins from converging into the
    /// wrong one. Analytic curves override this with closed forms and do not sample at all.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="point"/> is not finite.
    /// </exception>
    public virtual Point3d ClosestPoint(in Point3d point, out double parameter, in Tolerance tolerance = default)
    {
        if (!point.IsValid)
        {
            throw new ArgumentException("The point must be finite.", nameof(point));
        }

        Point3d target = point;
        int samples = SeedCount;
        double min = Domain.Min;
        double step = Domain.Length / samples;

        double bestParameter = min;
        double bestDistance = double.PositiveInfinity;
        double previous = double.PositiveInfinity;
        double current = PointAt(min).DistanceSquaredTo(target);

        for (int i = 0; i <= samples; i++)
        {
            double next = i == samples
                ? double.PositiveInfinity
                : PointAt(min + ((i + 1) * step)).DistanceSquaredTo(target);

            // A sample is worth refining when it is at least as good as both of its
            // neighbours. The ends count as local minima because a curve is not periodic in
            // general and its extremes are genuine candidates.
            if (current <= previous && current <= next)
            {
                double refined = RefineClosestPoint(
                    target,
                    Math.Min(min + (i * step), Domain.Max),
                    Math.Max(min, min + ((i - 1) * step)),
                    Math.Min(Domain.Max, min + ((i + 1) * step)),
                    tolerance);
                double distance = PointAt(refined).DistanceSquaredTo(target);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestParameter = refined;
                }
            }

            previous = current;
            current = next;
        }

        parameter = bestParameter;

        return PointAt(bestParameter);
    }

    /// <summary>
    /// Tests whether the curve lies in a single plane, and reports that plane.
    /// </summary>
    /// <param name="plane">
    /// Receives the plane the curve lies in when the answer is <see langword="true"/>, and
    /// <c>default</c> otherwise. For a straight curve, which lies in infinitely many planes,
    /// one of them is chosen deterministically.
    /// </param>
    /// <param name="tolerance">
    /// The tolerance a point's distance from the candidate plane is judged against. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns><see langword="true"/> when the curve is planar within tolerance.</returns>
    /// <remarks>
    /// The generic implementation samples the curve and fits a plane to the samples, so it can
    /// in principle miss an excursion between two samples. Every analytic curve overrides it
    /// with an exact answer, and <see cref="NurbsCurve"/> overrides it with a test on the
    /// control points, which is exact in the direction that matters: coplanar control points
    /// guarantee a planar curve by the convex-hull property.
    /// </remarks>
    public virtual bool IsPlanar(out Plane plane, in Tolerance tolerance = default)
    {
        int samples = SeedCount;
        Point3d[] points = new Point3d[samples + 1];
        double step = Domain.Length / samples;

        for (int i = 0; i <= samples; i++)
        {
            points[i] = PointAt(Math.Min(Domain.Min + (i * step), Domain.Max));
        }

        return TryFitPlane(points, tolerance, out plane);
    }

    /// <summary>
    /// The part of the curve over a sub-interval of its domain.
    /// </summary>
    /// <param name="interval">
    /// The parameters to keep. Intersected with <see cref="Domain"/> first, so an interval that
    /// overhangs either end is simply clipped. A decreasing interval is read as the increasing
    /// one with the same ends — use <see cref="Reverse"/> to change direction, because a trim
    /// that silently reversed would make <c>Trim</c> two operations wearing one name.
    /// </param>
    /// <returns>The trimmed curve, of whichever type best represents the result.</returns>
    /// <remarks>
    /// <b>The domain of the result starts at zero</b>, and for every type except
    /// <see cref="PolyLine"/> and <see cref="PolyCurve"/> it has the length of the interval
    /// asked for, so that <c>Trim(i).PointAt(u)</c> is <c>PointAt(i.Min + u)</c>. Those two are
    /// the exception because their parameter counts segments rather than measuring anything: a
    /// trim that takes half of one segment and half of the next leaves one segment where it
    /// found two, and the domain shortens accordingly. Each type says so on its own override.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when the interval is not valid, or when its intersection with the domain has zero
    /// length — a curve of no extent is not a curve.
    /// </exception>
    public abstract Curve Trim(in Interval interval);

    /// <summary>
    /// Splits the curve at a parameter.
    /// </summary>
    /// <param name="parameter">
    /// The parameter to split at. It must be strictly inside <see cref="Domain"/>: unlike
    /// evaluation, splitting is not clamped, because a split at an end would have to return a
    /// piece of zero extent and there is no such curve.
    /// </param>
    /// <returns>
    /// Two curves, the piece before the parameter and the piece after it, in that order.
    /// Rejoining them reproduces the original to within tolerance.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is <see cref="double.NaN"/> or is not strictly
    /// inside the domain.
    /// </exception>
    public virtual Curve[] Split(double parameter)
    {
        if (double.IsNaN(parameter) || parameter <= Domain.Min || parameter >= Domain.Max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameter),
                parameter,
                "A split parameter must lie strictly inside the curve's domain.");
        }

        return
        [
            Trim(new Interval(Domain.Min, parameter)),
            Trim(new Interval(parameter, Domain.Max)),
        ];
    }

    /// <summary>
    /// Returns the same curve traversed in the opposite direction.
    /// </summary>
    /// <returns>
    /// A curve occupying exactly the same positions, with <see cref="StartPoint"/> and
    /// <see cref="EndPoint"/> exchanged and every tangent negated. The domain keeps its length
    /// and its position on the number line, so <c>Reverse()</c> twice gives back a curve with
    /// the original domain as well as the original geometry.
    /// </returns>
    public abstract Curve Reverse();

    /// <summary>
    /// Converts the curve to an equivalent NURBS curve.
    /// </summary>
    /// <returns>
    /// A NURBS curve occupying exactly the same positions, over the same domain, with the same
    /// start and end points.
    /// </returns>
    /// <remarks>
    /// <b>The point set is preserved; the parameterisation generally is not.</b>
    /// <see cref="Line"/> and <see cref="PolyLine"/> convert parameter for parameter, because a
    /// degree-one NURBS curve is exactly what they already are. A circular or elliptical arc
    /// becomes a rational quadratic, whose parameter is a rational function of the sweep angle
    /// rather than the angle itself — so the two agree at the ends and at each span boundary
    /// and disagree in between. No exact rational reparameterisation of a circle by angle
    /// exists, so this is a property of NURBS rather than a shortcut taken here.
    /// </remarks>
    public abstract NurbsCurve ToNurbsCurve();

    /// <summary>
    /// Applies a transformation to the curve.
    /// </summary>
    /// <param name="transform">The transformation to apply.</param>
    /// <param name="tolerance">
    /// The tolerance used to decide whether the transformation preserves the curve's type. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// The transformed curve. The type is preserved where the transformation allows it: a
    /// <see cref="Line"/> is always a line, and a <see cref="Circle"/> stays a circle under any
    /// similarity — a rigid motion combined with a uniform scale. Under anything else a circle
    /// is genuinely no longer a circle, and rather than quietly returning a wrong one the
    /// result is the transformed <see cref="ToNurbsCurve"/>, which is exact because NURBS
    /// curves are closed under affine maps.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="transform"/> is singular or is not affine — a projective
    /// transformation does not map these curves to curves of the same kind at all.
    /// </exception>
    public abstract Curve Transform(in Transform transform, in Tolerance tolerance = default);

    /// <summary>
    /// How many parameters the generic fallbacks sample. Overridden by the types whose
    /// complexity is not bounded in advance.
    /// </summary>
    private protected virtual int SeedCount => DefaultSeedCount;

    /// <summary>
    /// Clamps a parameter into the domain, rejecting <see cref="double.NaN"/>.
    /// </summary>
    /// <param name="parameter">The parameter to clamp.</param>
    /// <returns>The parameter, clamped into <see cref="Domain"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameter"/> is <see cref="double.NaN"/>.
    /// </exception>
    private protected double ClampParameter(double parameter)
    {
        if (double.IsNaN(parameter))
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameter),
                parameter,
                "A curve parameter must not be NaN.");
        }

        return Domain.Clamp(parameter);
    }

    /// <summary>
    /// Rejects a negative derivative order.
    /// </summary>
    /// <param name="order">The order to check.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="order"/> is negative.
    /// </exception>
    private protected static void ThrowIfOrderIsNegative(int order)
    {
        if (order < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(order),
                order,
                "A derivative order must be zero or positive.");
        }
    }

    /// <summary>
    /// Clips a requested trim interval against the domain and rejects a degenerate result.
    /// </summary>
    /// <param name="interval">The requested interval.</param>
    /// <returns>The increasing interval actually to be trimmed to.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the interval is not finite, or when the overlap with the domain has zero
    /// length.
    /// </exception>
    private protected Interval ClipToDomain(in Interval interval)
    {
        if (!interval.IsValid)
        {
            throw new ArgumentException("A trim interval must be finite.", nameof(interval));
        }

        Interval increasing = interval.MakeIncreasing();
        double min = Math.Max(Domain.Min, increasing.Min);
        double max = Math.Min(Domain.Max, increasing.Max);

        if (!(max > min))
        {
            throw new ArgumentException(
                "The trim interval does not overlap the curve's domain in anything of positive "
                + "length, so it describes no curve.",
                nameof(interval));
        }

        return new Interval(min, max);
    }

    /// <summary>
    /// Rejects a transformation the curve types cannot act on at all.
    /// </summary>
    /// <param name="transform">The transformation to check.</param>
    /// <param name="tolerance">The tolerance for the affine and singularity tests.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the transformation is not affine or is singular.
    /// </exception>
    private protected static void ValidateTransform(in Transform transform, in Tolerance tolerance)
    {
        if (!transform.IsAffine(tolerance))
        {
            throw new ArgumentException(
                "A curve can only be transformed by an affine transformation; a projective one "
                + "does not map it to a curve of the same kind.",
                nameof(transform));
        }

        if (!transform.TryGetInverse(out _))
        {
            throw new ArgumentException(
                "A singular transformation collapses the curve onto a plane, a line or a point, "
                + "and the result is not a curve.",
                nameof(transform));
        }
    }

    /// <summary>
    /// Reports whether a transformation is a similarity — a rigid motion composed with a
    /// uniform scale — and if so what its scale factor is.
    /// </summary>
    /// <param name="transform">The transformation to examine.</param>
    /// <param name="tolerance">The tolerance for the orthogonality and equal-length tests.</param>
    /// <param name="scale">Receives the uniform scale factor, or one when the answer is false.</param>
    /// <returns>
    /// <see langword="true"/> when the linear part maps a sphere to a sphere, which is exactly
    /// the condition under which a circle stays a circle and an ellipse keeps its shape.
    /// </returns>
    private protected static bool IsSimilarity(in Transform transform, in Tolerance tolerance, out double scale)
    {
        Vector3d x = new(transform.M00, transform.M10, transform.M20);
        Vector3d y = new(transform.M01, transform.M11, transform.M21);
        Vector3d z = new(transform.M02, transform.M12, transform.M22);

        double lengthX = x.Length;
        double lengthY = y.Length;
        double lengthZ = z.Length;

        scale = 1.0;

        if (lengthX == 0.0 || lengthY == 0.0 || lengthZ == 0.0)
        {
            return false;
        }

        // The three column lengths must agree and the columns must be mutually perpendicular.
        // Both tests are made dimensionless by dividing through by the column lengths, so the
        // decision does not change when the whole transformation is scaled up or down.
        if (!tolerance.AreEqual(lengthX / lengthY, 1.0) || !tolerance.AreEqual(lengthX / lengthZ, 1.0))
        {
            return false;
        }

        if (!tolerance.IsZero(x.Dot(y) / (lengthX * lengthY))
            || !tolerance.IsZero(x.Dot(z) / (lengthX * lengthZ))
            || !tolerance.IsZero(y.Dot(z) / (lengthY * lengthZ)))
        {
            return false;
        }

        scale = lengthX;

        return true;
    }

    /// <summary>
    /// Fits a plane through a set of points and reports whether they all lie in it.
    /// </summary>
    /// <param name="points">The points to fit. At least one is required.</param>
    /// <param name="tolerance">The tolerance a point's distance from the plane is judged against.</param>
    /// <param name="plane">Receives the fitted plane, or <c>default</c> on failure.</param>
    /// <returns>
    /// <see langword="true"/> when every point lies in the plane within tolerance. Points that
    /// are all coincident, or all collinear, are planar too: a plane containing them is chosen
    /// deterministically.
    /// </returns>
    private protected static bool TryFitPlane(
        IReadOnlyList<Point3d> points,
        in Tolerance tolerance,
        out Plane plane)
    {
        plane = default;

        if (points.Count == 0)
        {
            return false;
        }

        Point3d origin = points[0];
        double scale = 0.0;

        for (int i = 0; i < points.Count; i++)
        {
            scale = Math.Max(scale, (points[i] - origin).Length);
        }

        // Pick the two spanning directions by largest deviation rather than by taking the first
        // two points that differ. Two nearly coincident points span a direction whose error is
        // the same size as the direction itself, and every distance measured against the
        // resulting plane is then noise.
        Vector3d first = Vector3d.Zero;
        double bestFirst = 0.0;

        for (int i = 1; i < points.Count; i++)
        {
            Vector3d offset = points[i] - origin;

            if (offset.Length > bestFirst)
            {
                bestFirst = offset.Length;
                first = offset;
            }
        }

        if (bestFirst == 0.0)
        {
            // Every point is the same point. Any plane through it contains them all.
            plane = new Plane(origin, Vector3d.ZAxis);

            return true;
        }

        Vector3d normal = Vector3d.Zero;
        double bestNormal = 0.0;

        for (int i = 1; i < points.Count; i++)
        {
            Vector3d candidate = first.Cross(points[i] - origin);

            if (candidate.Length > bestNormal)
            {
                bestNormal = candidate.Length;
                normal = candidate;
            }
        }

        if (bestNormal == 0.0 || tolerance.IsNegligible(bestNormal / bestFirst, scale))
        {
            // Collinear: pick any plane containing the line, deterministically.
            plane = Plane.ByOriginXAxisYAxis(origin, first, new Plane(origin, first).XAxis);

            return true;
        }

        Plane candidatePlane = Plane.ByOriginXAxisYAxis(origin, first, normal.Cross(first));

        for (int i = 1; i < points.Count; i++)
        {
            if (!tolerance.IsNegligible(candidatePlane.DistanceTo(points[i]), scale))
            {
                return false;
            }
        }

        plane = candidatePlane;

        return true;
    }

    /// <summary>
    /// Refines a seeded closest-point parameter by Newton's method on the perpendicularity
    /// condition, kept inside a bracket so a bad step cannot walk off to a different basin.
    /// </summary>
    /// <param name="target">The point being measured from.</param>
    /// <param name="seed">The starting parameter.</param>
    /// <param name="lower">The lowest parameter the refinement may return.</param>
    /// <param name="upper">The highest parameter the refinement may return.</param>
    /// <param name="tolerance">The tolerance governing convergence.</param>
    /// <returns>The refined parameter, inside <c>[lower, upper]</c>.</returns>
    private double RefineClosestPoint(
        in Point3d target,
        double seed,
        double lower,
        double upper,
        in Tolerance tolerance)
    {
        double t = seed;
        double scale = Math.Max(Domain.Length, 0.0);

        for (int iteration = 0; iteration < 32; iteration++)
        {
            Vector3d offset = PointAt(t) - target;
            Vector3d first = DerivativeAt(t, 1);
            Vector3d second = DerivativeAt(t, 2);

            double value = offset.Dot(first);
            double slope = first.LengthSquared + offset.Dot(second);

            if (slope == 0.0 || !double.IsFinite(slope))
            {
                return t;
            }

            double next = Math.Clamp(t - (value / slope), lower, upper);

            if (tolerance.IsNegligible(next - t, scale))
            {
                return next;
            }

            t = next;
        }

        return t;
    }

    /// <summary>
    /// The magnitude of the first derivative — how fast the point moves per unit parameter.
    /// </summary>
    /// <param name="parameter">The parameter to evaluate at.</param>
    /// <returns>The speed, which is the integrand of arc length.</returns>
    private double Speed(double parameter) => DerivativeAt(parameter, 1).Length;

    /// <summary>
    /// A crude arc length from a handful of chords, used only to give the quadrature and the
    /// solvers a magnitude to make their error tests relative to.
    /// </summary>
    /// <returns>A length that is the right order of magnitude and is never zero for a real curve.</returns>
    private double ChordEstimate()
    {
        const int Steps = 8;
        double step = Domain.Length / Steps;
        double total = 0.0;
        Point3d previous = PointAt(Domain.Min);

        for (int i = 1; i <= Steps; i++)
        {
            Point3d current = PointAt(Math.Min(Domain.Min + (i * step), Domain.Max));

            total += previous.DistanceTo(current);
            previous = current;
        }

        return total;
    }
}
