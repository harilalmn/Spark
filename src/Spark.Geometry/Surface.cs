using System;

namespace Spark.Geometry;

/// <summary>
/// The base of every surface: a bounded, continuous sheet through space, parameterised over
/// <see cref="DomainU"/> and <see cref="DomainV"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This mirrors <see cref="Curve"/> deliberately, and where it differs it is because a surface
/// differs.</b> Surfaces are sealed classes with a <c>private protected</c> base constructor, so
/// the set of surface types is closed to this assembly: a tessellator, a serialiser and a BRep
/// face all have to know every surface there is, and a third-party surface type would silently
/// break all three. Every concrete surface is immutable, and backing arrays are never handed out.
/// </para>
/// <para>
/// <b>Two domains, and neither is assumed to be [0, 1].</b> A <see cref="PlaneSurface"/> runs over
/// whatever rectangle it was given, a cylinder over [0, 2π] in one direction and a height in the
/// other. Ask <see cref="DomainU"/> and <see cref="DomainV"/> rather than assuming, exactly as with
/// a curve — and note that the two directions are genuinely independent: a cylinder is closed in u
/// and open in v, which is why <see cref="IsClosedU"/> and <see cref="IsClosedV"/> are two
/// questions rather than one.
/// </para>
/// <para>
/// <b>Derivatives have numeric defaults and analytic overrides, and that asymmetry is the design.</b>
/// Every surface can be differentiated by central differences, so nothing is *forced* to implement
/// them and a new surface type is cheap to add correctly. But a plane, a sphere and a cylinder all
/// know their derivatives exactly, and central differences on them lose about half the available
/// precision — so each overrides, and the base implementation exists to be correct rather than to
/// be used. The same split gives <see cref="NormalAt"/>, <see cref="Area"/>,
/// <see cref="ClosestPoint"/> and <see cref="PrincipalCurvatures"/> for free on any new type.
/// </para>
/// <para>
/// <b>What is deliberately not here, and why.</b> There is no <c>Trim</c>: a trimmed surface is a
/// surface plus a region in its own parameter space, and that region needs the planar layer
/// (`E2-T13`) which does not exist yet — inventing half of it here would have to be undone. There
/// is no <c>ToNurbsSurface</c>: `E2-T19` brings <c>NurbsSurface</c>, and a conversion to a type
/// that does not exist cannot be written honestly. There is no surface/surface intersection, which
/// is exact-kernel work behind `E2-T28`'s seam. Each of those is a row, not an omission.
/// </para>
/// <para>
/// <b>There is no value equality on surfaces</b>, for the reason there is none on curves: two
/// surfaces that describe the same sheet under different parameterisations are a tolerance
/// question, and answering it wrongly by default is worse than not answering it.
/// </para>
/// </remarks>
public abstract class Surface
{
    /// <summary>
    /// How many samples per direction seed a search or an estimate over the whole surface.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="BoundingBox"/> and by <see cref="ClosestPoint"/>'s first pass. Sixteen is
    /// enough to bracket the answer on any surface whose curvature is not pathological, and the
    /// Newton refinement that follows is what supplies the accuracy — a finer grid would cost
    /// quadratically and buy almost nothing.
    /// </remarks>
    private const int SampleCount = 16;

    /// <summary>
    /// How many spans each direction is integrated over when <see cref="Area"/> is computed.
    /// </summary>
    private const int AreaSpans = 16;

    // Ten-point Gauss-Legendre nodes and weights on [-1, 1], the same rule `Curve` integrates arc
    // length with. Ten points integrate a degree-19 polynomial exactly, which is more than the
    // area element of any surface here needs, and it costs ten evaluations per span rather than the
    // hundreds an adaptive rule would take.
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

    private BoundingBox _boundingBox;
    private bool _boundingBoxComputed;
    private double _area = -1.0;

    /// <summary>
    /// Creates the base part of a surface. <c>private protected</c>, so the surface hierarchy is
    /// closed to this assembly; see the remarks on <see cref="Surface"/>.
    /// </summary>
    private protected Surface()
    {
    }

    /// <summary>The interval of <c>u</c> parameters this surface is defined over.</summary>
    public abstract Interval DomainU { get; }

    /// <summary>The interval of <c>v</c> parameters this surface is defined over.</summary>
    public abstract Interval DomainV { get; }

    /// <summary>Whether the surface joins to itself across the <c>u</c> direction.</summary>
    public abstract bool IsClosedU { get; }

    /// <summary>Whether the surface joins to itself across the <c>v</c> direction.</summary>
    public abstract bool IsClosedV { get; }

    /// <summary>This surface moved by a transform.</summary>
    /// <param name="transform">The transform to apply.</param>
    /// <returns>A new surface of the same kind, wherever the transform allows.</returns>
    /// <remarks>
    /// <b>Every surface must answer this, and a few of them have to change type to do it.</b> A
    /// sphere under a non-uniform scale is not a sphere, and a type that pretended otherwise would
    /// be quietly wrong; the honest answers are a different surface type or a refusal, and each
    /// type says which on itself. It is on the base class rather than left to the concrete types
    /// because an iso-curve has to be transformable, and it can only be so if its surface is.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The transform cannot be applied to this kind of surface — a non-uniform scale on a sphere,
    /// for instance — and no other surface type in the kernel represents the result.
    /// </exception>
    public abstract Surface TransformedBy(in Transform transform);

    /// <summary>
    /// A box containing the whole surface. Computed once and remembered, which is part of why
    /// surfaces are immutable.
    /// </summary>
    /// <remarks>
    /// <b>The base implementation samples and is therefore not tight</b>, and a surface that knows
    /// better should override it: a plane's box is its four corners, exactly. Sampling can only
    /// under-estimate — it never reports a box that excludes a sampled point, but a bulge between
    /// samples can sit outside it — so the box is padded by the largest sample-to-sample step.
    /// A conservative box is the only kind worth having, because everything downstream uses it to
    /// decide what to *skip*.
    /// </remarks>
    public virtual BoundingBox BoundingBox
    {
        get
        {
            if (!_boundingBoxComputed)
            {
                _boundingBox = SampledBoundingBox();
                _boundingBoxComputed = true;
            }

            return _boundingBox;
        }
    }

    /// <summary>
    /// The surface's area. Computed once and remembered.
    /// </summary>
    /// <remarks>
    /// Integrated as the double integral of the area element |∂S/∂u × ∂S/∂v| over the two domains,
    /// with a ten-point Gauss–Legendre rule per span in each direction. That is exact for a plane
    /// and accurate to well below tolerance for everything else here; a surface with a closed form
    /// should override it and say so.
    /// </remarks>
    public virtual double Area
    {
        get
        {
            if (_area < 0.0)
            {
                _area = IntegrateArea();
            }

            return _area;
        }
    }

    /// <summary>The point at a parameter pair.</summary>
    /// <param name="u">A parameter in <see cref="DomainU"/>.</param>
    /// <param name="v">A parameter in <see cref="DomainV"/>.</param>
    /// <returns>The point on the surface.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A parameter is not finite, or lies outside its domain in a direction that is not closed.
    /// </exception>
    public Point3d PointAt(double u, double v) =>
        Evaluate(CheckU(u), CheckV(v));

    /// <summary>The point at a parameter pair.</summary>
    /// <param name="uv">The parameter pair.</param>
    /// <returns>The point on the surface.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either parameter is out of range.</exception>
    public Point3d PointAt(in UV uv) => PointAt(uv.U, uv.V);

    /// <summary>The two first derivatives at a parameter pair.</summary>
    /// <param name="u">A parameter in <see cref="DomainU"/>.</param>
    /// <param name="v">A parameter in <see cref="DomainV"/>.</param>
    /// <param name="derivativeU">The derivative along increasing <c>u</c>.</param>
    /// <param name="derivativeV">The derivative along increasing <c>v</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either parameter is out of range.</exception>
    public void DerivativeAt(double u, double v, out Vector3d derivativeU, out Vector3d derivativeV) =>
        EvaluateDerivatives(CheckU(u), CheckV(v), out derivativeU, out derivativeV);

    /// <summary>The unit normal at a parameter pair.</summary>
    /// <param name="u">A parameter in <see cref="DomainU"/>.</param>
    /// <param name="v">A parameter in <see cref="DomainV"/>.</param>
    /// <returns>A unit vector.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either parameter is out of range.</exception>
    /// <exception cref="InvalidOperationException">
    /// The surface is degenerate here — the two derivatives are parallel, so there is no plane to
    /// take a normal to. A cone's apex and a sphere's poles are the usual cases.
    /// </exception>
    /// <remarks>
    /// <b>The normal follows u × v, and that is the orientation convention for the whole
    /// kernel.</b> It is stated here rather than left to be inferred, because a surface whose
    /// normal points the other way from its neighbour's is how a solid ends up inside out, and the
    /// only defence is that every type answers the same question the same way.
    /// </remarks>
    public Vector3d NormalAt(double u, double v)
    {
        EvaluateDerivatives(CheckU(u), CheckV(v), out Vector3d du, out Vector3d dv);

        if (IsDegenerate(du, dv, out Vector3d normal))
        {
            throw new InvalidOperationException(
                "The surface is degenerate at this parameter, so it has no normal there. A cone's "
                + "apex and a sphere's poles are the usual cases; ask at a parameter just off them.");
        }

        return normal.Normalised();
    }

    /// <summary>The tangent plane at a parameter pair, origin on the surface.</summary>
    /// <param name="u">A parameter in <see cref="DomainU"/>.</param>
    /// <param name="v">A parameter in <see cref="DomainV"/>.</param>
    /// <returns>A plane whose normal is <see cref="NormalAt"/> and whose x-axis follows u.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either parameter is out of range.</exception>
    /// <exception cref="InvalidOperationException">The surface is degenerate here.</exception>
    public Plane FrameAt(double u, double v)
    {
        EvaluateDerivatives(CheckU(u), CheckV(v), out Vector3d du, out Vector3d dv);

        if (IsDegenerate(du, dv, out Vector3d normal))
        {
            throw new InvalidOperationException(
                "The surface is degenerate at this parameter, so it has no tangent plane there.");
        }

        return Plane.ByOriginNormalXAxis(Evaluate(u, v), normal, du);
    }

    /// <summary>
    /// The curve traced by holding <c>v</c> fixed and running <c>u</c> across its domain.
    /// </summary>
    /// <param name="v">A parameter in <see cref="DomainV"/>.</param>
    /// <returns>A curve over <see cref="DomainU"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="v"/> is out of range.</exception>
    /// <remarks>
    /// <b>A real <see cref="Curve"/>, not a sampled polyline.</b> Everything that already works on
    /// a curve — arc length, division, tessellation to a tolerance, the bounding box — then works
    /// on an iso-curve without a second implementation, and an iso-curve is how a BRep face's
    /// natural boundary is described.
    /// </remarks>
    public Curve IsoCurveU(double v) => new IsoCurve(this, CheckV(v), alongU: true);

    /// <summary>
    /// The curve traced by holding <c>u</c> fixed and running <c>v</c> across its domain.
    /// </summary>
    /// <param name="u">A parameter in <see cref="DomainU"/>.</param>
    /// <returns>A curve over <see cref="DomainV"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="u"/> is out of range.</exception>
    public Curve IsoCurveV(double u) => new IsoCurve(this, CheckU(u), alongU: false);

    /// <summary>
    /// The closest point on the surface to a point in space.
    /// </summary>
    /// <param name="point">The point to measure from.</param>
    /// <param name="u">The <c>u</c> parameter of the closest point.</param>
    /// <param name="v">Its <c>v</c> parameter.</param>
    /// <returns>The closest point on the surface.</returns>
    /// <remarks>
    /// <para>
    /// <b>Sample, then refine.</b> A grid of samples brackets the answer and a Newton iteration on
    /// the two orthogonality conditions — the vector from the surface to the point is perpendicular
    /// to both derivatives — converges on it. Starting Newton without the grid finds a *local*
    /// closest point, which on a cylinder is the wrong side.
    /// </para>
    /// <para>
    /// <b>It is not guaranteed to be the global closest point</b>, and saying so is the honest
    /// thing to do: a coarse grid can miss a narrow feature on a wildly curved surface. It is
    /// exact on the analytic surfaces at the size a graph uses, and the grid is the parameter to
    /// widen if that ever stops being true.
    /// </para>
    /// </remarks>
    public Point3d ClosestPoint(in Point3d point, out double u, out double v)
    {
        Seed(point, out u, out v);

        // Six iterations is comfortably past convergence for a quadratically-converging method
        // that starts inside the right cell; more would only cost evaluations.
        for (int iteration = 0; iteration < 8; iteration++)
        {
            if (!NewtonStep(point, ref u, ref v))
            {
                break;
            }
        }

        return Evaluate(u, v);
    }

    /// <summary>
    /// The two principal curvatures at a parameter pair, smallest first.
    /// </summary>
    /// <param name="u">A parameter in <see cref="DomainU"/>.</param>
    /// <param name="v">A parameter in <see cref="DomainV"/>.</param>
    /// <returns>The minimum and maximum normal curvature.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either parameter is out of range.</exception>
    /// <exception cref="InvalidOperationException">The surface is degenerate here.</exception>
    /// <remarks>
    /// <para>
    /// From the first and second fundamental forms. Mean curvature <c>H</c> and Gaussian curvature
    /// <c>K</c> come out of the two forms directly, and the principal curvatures are the roots of
    /// <c>k² − 2Hk + K = 0</c>.
    /// </para>
    /// <para>
    /// <b>The discriminant is clamped at zero before the square root, and that is not sloppiness.</b>
    /// <c>H² − K</c> is non-negative for every real surface, but on a sphere it is *exactly* zero
    /// and floating-point arithmetic will hand back a value a few epsilon below it — at which point
    /// an unclamped <c>Math.Sqrt</c> returns NaN and the curvature of a sphere becomes undefined.
    /// </para>
    /// </remarks>
    public (double Minimum, double Maximum) PrincipalCurvatures(double u, double v)
    {
        u = CheckU(u);
        v = CheckV(v);

        EvaluateDerivatives(u, v, out Vector3d du, out Vector3d dv);
        EvaluateSecondDerivatives(u, v, out Vector3d duu, out Vector3d duv, out Vector3d dvv);

        if (IsDegenerate(du, dv, out Vector3d cross))
        {
            throw new InvalidOperationException(
                "The surface is degenerate at this parameter, so its curvature is undefined there.");
        }

        Vector3d normal = cross.Normalised();

        // First fundamental form.
        double e = du.Dot(du);
        double f = du.Dot(dv);
        double g = dv.Dot(dv);

        // Second fundamental form.
        double l = duu.Dot(normal);
        double m = duv.Dot(normal);
        double n = dvv.Dot(normal);

        double determinant = (e * g) - (f * f);

        if (Math.Abs(determinant) <= 0.0)
        {
            throw new InvalidOperationException(
                "The surface's parameterisation is degenerate at this parameter.");
        }

        double gaussian = ((l * n) - (m * m)) / determinant;
        double mean = ((l * g) - (2.0 * m * f) + (n * e)) / (2.0 * determinant);

        // Clamped: see the remarks. On a sphere this is exactly zero and rounds negative.
        double discriminant = Math.Sqrt(Math.Max(0.0, (mean * mean) - gaussian));

        return (mean - discriminant, mean + discriminant);
    }

    /// <summary>The Gaussian curvature at a parameter pair.</summary>
    /// <param name="u">A parameter in <see cref="DomainU"/>.</param>
    /// <param name="v">A parameter in <see cref="DomainV"/>.</param>
    /// <returns>The product of the two principal curvatures.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either parameter is out of range.</exception>
    /// <exception cref="InvalidOperationException">The surface is degenerate here.</exception>
    public double GaussianCurvature(double u, double v)
    {
        (double minimum, double maximum) = PrincipalCurvatures(u, v);

        return minimum * maximum;
    }

    /// <summary>The mean curvature at a parameter pair.</summary>
    /// <param name="u">A parameter in <see cref="DomainU"/>.</param>
    /// <param name="v">A parameter in <see cref="DomainV"/>.</param>
    /// <returns>The average of the two principal curvatures.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either parameter is out of range.</exception>
    /// <exception cref="InvalidOperationException">The surface is degenerate here.</exception>
    public double MeanCurvature(double u, double v)
    {
        (double minimum, double maximum) = PrincipalCurvatures(u, v);

        return (minimum + maximum) * 0.5;
    }

    /// <summary>Evaluates the surface. Every parameter has already been checked.</summary>
    /// <param name="u">A parameter in <see cref="DomainU"/>.</param>
    /// <param name="v">A parameter in <see cref="DomainV"/>.</param>
    /// <returns>The point on the surface.</returns>
    protected abstract Point3d Evaluate(double u, double v);

    /// <summary>
    /// The two first derivatives. Overridden by any surface that knows them in closed form.
    /// </summary>
    /// <param name="u">A checked parameter in <see cref="DomainU"/>.</param>
    /// <param name="v">A checked parameter in <see cref="DomainV"/>.</param>
    /// <param name="derivativeU">The derivative along increasing <c>u</c>.</param>
    /// <param name="derivativeV">The derivative along increasing <c>v</c>.</param>
    /// <remarks>
    /// Central differences, with a step scaled to the domain so that a surface parameterised over
    /// [0, 2π] and one parameterised over [0, 0.001] both get a sensible one. The default exists so
    /// that a new surface type is correct before it is fast; every analytic surface overrides it.
    /// </remarks>
    protected virtual void EvaluateDerivatives(
        double u, double v, out Vector3d derivativeU, out Vector3d derivativeV)
    {
        double stepU = Step(DomainU);
        double stepV = Step(DomainV);

        derivativeU = (Sample(u + stepU, v) - Sample(u - stepU, v)) / (2.0 * stepU);
        derivativeV = (Sample(u, v + stepV) - Sample(u, v - stepV)) / (2.0 * stepV);
    }

    /// <summary>
    /// The three second derivatives. Overridden by any surface that knows them in closed form.
    /// </summary>
    /// <param name="u">A checked parameter in <see cref="DomainU"/>.</param>
    /// <param name="v">A checked parameter in <see cref="DomainV"/>.</param>
    /// <param name="secondU">∂²S/∂u².</param>
    /// <param name="mixed">∂²S/∂u∂v.</param>
    /// <param name="secondV">∂²S/∂v².</param>
    protected virtual void EvaluateSecondDerivatives(
        double u, double v, out Vector3d secondU, out Vector3d mixed, out Vector3d secondV)
    {
        double stepU = Step(DomainU);
        double stepV = Step(DomainV);

        Point3d centre = Sample(u, v);

        secondU = ((Sample(u + stepU, v) - centre) - (centre - Sample(u - stepU, v))) / (stepU * stepU);
        secondV = ((Sample(u, v + stepV) - centre) - (centre - Sample(u, v - stepV))) / (stepV * stepV);

        Vector3d plus = Sample(u + stepU, v + stepV) - Sample(u + stepU, v - stepV);
        Vector3d minus = Sample(u - stepU, v + stepV) - Sample(u - stepU, v - stepV);

        mixed = (plus - minus) / (4.0 * stepU * stepV);
    }

    /// <summary>Checks a <c>u</c> parameter and wraps it if the surface is closed in <c>u</c>.</summary>
    /// <param name="u">The parameter.</param>
    /// <returns>The parameter, wrapped into the domain when that is meaningful.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The parameter is not finite, or is outside a domain the surface does not wrap in.
    /// </exception>
    private protected double CheckU(double u) => Check(u, DomainU, IsClosedU, nameof(u));

    /// <summary>Checks a <c>v</c> parameter and wraps it if the surface is closed in <c>v</c>.</summary>
    /// <param name="v">The parameter.</param>
    /// <returns>The parameter, wrapped into the domain when that is meaningful.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The parameter is not finite, or is outside a domain the surface does not wrap in.
    /// </exception>
    private protected double CheckV(double v) => Check(v, DomainV, IsClosedV, nameof(v));

    /// <summary>
    /// Checks a parameter against a domain, wrapping when the direction closes.
    /// </summary>
    /// <remarks>
    /// <b>Wrapping a closed direction is the same decision <see cref="Curve"/> made</b>: on a
    /// cylinder, <c>u = 2π + 0.1</c> is a real point and refusing it would make every caller do
    /// modular arithmetic that the surface can do once, correctly.
    /// </remarks>
    private static double Check(double value, in Interval domain, bool closed, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "A surface parameter must be finite.");
        }

        if (domain.Includes(value))
        {
            return value;
        }

        if (!closed)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"The parameter is outside the surface's domain {domain}, and the surface does not close in that direction.");
        }

        double length = domain.Length;
        double wrapped = domain.Min + (((value - domain.Min) % length + length) % length);

        return wrapped;
    }

    /// <summary>
    /// Whether a parameter is a point where the surface has no tangent plane.
    /// </summary>
    /// <param name="du">The <c>u</c> derivative there.</param>
    /// <param name="dv">The <c>v</c> derivative there.</param>
    /// <param name="normal">Their cross product, valid only when this returns false.</param>
    /// <returns>True when there is no plane to take a normal to.</returns>
    /// <remarks>
    /// <para>
    /// <b>Two different degeneracies, and testing only the obvious one is not enough.</b> The
    /// obvious one is <i>the derivatives are parallel</i>, and it is caught by comparing the cross
    /// product to the product of the two lengths. The one that actually occurs is <i>one derivative
    /// has collapsed</i> — a sphere's pole, a cone's apex — and there the two are still perfectly
    /// perpendicular, so the cross product is a full-sized fraction of a product that is itself
    /// near zero, and a ratio test passes it.
    /// </para>
    /// <para>
    /// <b>It also cannot be a test against zero.</b> At <c>v = π/2</c> exactly, <c>cos v</c> is
    /// 6.1e-17 rather than 0, so the derivative is tiny rather than absent and a
    /// <c>&lt;= 0.0</c> check reports a perfectly good normal at a place that has none. The
    /// comparison has to be relative — one derivative against the other — which is also what keeps
    /// a legitimately anisotropic patch, a thin tall cylinder say, from being called degenerate.
    /// </para>
    /// </remarks>
    private static bool IsDegenerate(in Vector3d du, in Vector3d dv, out Vector3d normal)
    {
        normal = du.Cross(dv);

        double lengthU = du.Length;
        double lengthV = dv.Length;
        double largest = Math.Max(lengthU, lengthV);

        if (!double.IsFinite(largest) || largest <= 0.0)
        {
            return true;
        }

        // One derivative has collapsed relative to the other: a pole, or an apex.
        if (Math.Min(lengthU, lengthV) <= largest * DegeneracyRatio)
        {
            return true;
        }

        // The two are parallel, so they span a line rather than a plane.
        return normal.Length <= lengthU * lengthV * DegeneracyRatio;
    }

    /// <summary>
    /// How far below its partner a derivative has to fall before the surface is called degenerate.
    /// </summary>
    /// <remarks>
    /// Far below anything a real parameterisation produces and far above the 1e-16 that a
    /// trigonometric function returns where the exact answer is zero, which is the gap this
    /// constant has to sit in.
    /// </remarks>
    private const double DegeneracyRatio = 1e-12;

    /// <summary>Evaluates at a parameter pair, clamped into the domains.</summary>
    /// <remarks>
    /// Used by the numeric derivatives, whose stencil reaches past the edge at the boundary. A
    /// clamp there makes the difference one-sided rather than wrong, which is the correct
    /// behaviour for an open surface and irrelevant for a closed one — <see cref="Check"/> wraps
    /// before this is ever reached.
    /// </remarks>
    private Point3d Sample(double u, double v) =>
        Evaluate(
            IsClosedU ? Check(u, DomainU, closed: true, nameof(u)) : DomainU.Clamp(u),
            IsClosedV ? Check(v, DomainV, closed: true, nameof(v)) : DomainV.Clamp(v));

    /// <summary>A differentiation step for a domain: small against it, and never zero.</summary>
    private static double Step(in Interval domain) => Math.Max(domain.Length * 1e-6, 1e-9);

    private BoundingBox SampledBoundingBox()
    {
        BoundingBox box = BoundingBox.Empty;
        double largestStep = 0.0;
        Point3d previous = default;

        for (int i = 0; i <= SampleCount; i++)
        {
            double u = DomainU.Denormalise(i / (double)SampleCount);

            for (int j = 0; j <= SampleCount; j++)
            {
                double v = DomainV.Denormalise(j / (double)SampleCount);
                Point3d point = Evaluate(u, v);

                box = box.Union(point);

                if (j > 0)
                {
                    largestStep = Math.Max(largestStep, previous.DistanceTo(point));
                }

                previous = point;
            }
        }

        // Padded by the coarsest step, because a bulge between two samples can sit outside their
        // box and a bounding box that excludes part of its geometry is worse than a loose one:
        // everything downstream uses it to decide what to skip.
        return largestStep > 0.0 ? box.Inflated(largestStep * 0.5) : box;
    }

    private double IntegrateArea()
    {
        double total = 0.0;
        double spanU = DomainU.Length / AreaSpans;
        double spanV = DomainV.Length / AreaSpans;

        for (int i = 0; i < AreaSpans; i++)
        {
            double startU = DomainU.Min + (i * spanU);

            for (int j = 0; j < AreaSpans; j++)
            {
                double startV = DomainV.Min + (j * spanV);
                double cell = 0.0;

                for (int a = 0; a < GaussNodes.Length; a++)
                {
                    double u = startU + (spanU * 0.5 * (GaussNodes[a] + 1.0));

                    for (int b = 0; b < GaussNodes.Length; b++)
                    {
                        double v = startV + (spanV * 0.5 * (GaussNodes[b] + 1.0));

                        EvaluateDerivatives(u, v, out Vector3d du, out Vector3d dv);

                        cell += GaussWeights[a] * GaussWeights[b] * du.Cross(dv).Length;
                    }
                }

                total += cell * spanU * spanV * 0.25;
            }
        }

        return total;
    }

    /// <summary>Finds the sampled parameter pair nearest a point, to start Newton from.</summary>
    private void Seed(in Point3d point, out double u, out double v)
    {
        double best = double.PositiveInfinity;
        u = DomainU.Mid;
        v = DomainV.Mid;

        for (int i = 0; i <= SampleCount; i++)
        {
            double sampleU = DomainU.Denormalise(i / (double)SampleCount);

            for (int j = 0; j <= SampleCount; j++)
            {
                double sampleV = DomainV.Denormalise(j / (double)SampleCount);
                double distance = Evaluate(sampleU, sampleV).DistanceSquaredTo(point);

                if (distance < best)
                {
                    best = distance;
                    u = sampleU;
                    v = sampleV;
                }
            }
        }
    }

    /// <summary>
    /// One Newton step on the two orthogonality conditions.
    /// </summary>
    /// <returns>False when the step is not worth taking, which ends the iteration.</returns>
    /// <remarks>
    /// The conditions are <c>f = (S − P)·S_u = 0</c> and <c>g = (S − P)·S_v = 0</c>. The Jacobian
    /// needs the second derivatives, which is why they are part of this contract rather than an
    /// extra a curvature query alone would justify.
    /// </remarks>
    private bool NewtonStep(in Point3d point, ref double u, ref double v)
    {
        EvaluateDerivatives(u, v, out Vector3d du, out Vector3d dv);
        EvaluateSecondDerivatives(u, v, out Vector3d duu, out Vector3d duv, out Vector3d dvv);

        Vector3d offset = Evaluate(u, v) - point;

        double f = offset.Dot(du);
        double g = offset.Dot(dv);

        double fu = du.Dot(du) + offset.Dot(duu);
        double fv = du.Dot(dv) + offset.Dot(duv);
        double gu = fv;
        double gv = dv.Dot(dv) + offset.Dot(dvv);

        double determinant = (fu * gv) - (fv * gu);

        if (Math.Abs(determinant) < 1e-300 || !double.IsFinite(determinant))
        {
            return false;
        }

        double stepU = ((f * gv) - (g * fv)) / determinant;
        double stepV = ((g * fu) - (f * gu)) / determinant;

        if (!double.IsFinite(stepU) || !double.IsFinite(stepV))
        {
            return false;
        }

        u = IsClosedU ? Check(u - stepU, DomainU, closed: true, nameof(u)) : DomainU.Clamp(u - stepU);
        v = IsClosedV ? Check(v - stepV, DomainV, closed: true, nameof(v)) : DomainV.Clamp(v - stepV);

        return Math.Abs(stepU) > DomainU.Length * 1e-14 || Math.Abs(stepV) > DomainV.Length * 1e-14;
    }

    /// <summary>
    /// A curve along one parameter direction of a surface, with the other held fixed.
    /// </summary>
    /// <remarks>
    /// <b>Private to <see cref="Surface"/> because it is not a curve type in its own right.</b> It
    /// has no independent existence — it is a view of a surface — and adding it to the public curve
    /// hierarchy would oblige the serialiser and the tessellator to know about it. Every caller
    /// sees a <see cref="Curve"/>, and every curve operation works on it.
    /// </remarks>
    private sealed class IsoCurve : Curve
    {
        private readonly Surface _surface;
        private readonly double _fixed;
        private readonly bool _alongU;
        private readonly Interval _domain;
        private readonly bool _reversed;

        internal IsoCurve(Surface surface, double fixedParameter, bool alongU)
            : this(surface, fixedParameter, alongU, alongU ? surface.DomainU : surface.DomainV, reversed: false)
        {
        }

        private IsoCurve(Surface surface, double fixedParameter, bool alongU, in Interval domain, bool reversed)
        {
            _surface = surface;
            _fixed = fixedParameter;
            _alongU = alongU;
            _domain = domain;
            _reversed = reversed;
        }

        public override Interval Domain => _domain;

        public override bool IsClosed =>
            (_alongU ? _surface.IsClosedU : _surface.IsClosedV)
            && _domain.EqualsWithin(_alongU ? _surface.DomainU : _surface.DomainV);

        /// <inheritdoc/>
        public override Curve Reversed() =>
            new IsoCurve(_surface, _fixed, _alongU, _domain, !_reversed);

        /// <inheritdoc/>
        public override Curve Trimmed(in Interval domain)
        {
            Interval increasing = domain.MakeIncreasing();

            if (!_domain.Includes(increasing.Min) || !_domain.Includes(increasing.Max))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(domain), domain, $"The interval is outside this iso-curve's domain {_domain}.");
            }

            return new IsoCurve(_surface, _fixed, _alongU, increasing, _reversed);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <b>The surface is transformed and the iso-curve is taken again</b>, rather than the
        /// points being transformed one at a time. That keeps the result exact for a surface that
        /// can be transformed exactly, and it is the reason <see cref="Surface.TransformedBy"/> is
        /// on the base class.
        /// </remarks>
        public override Curve TransformedBy(in Transform transform) =>
            new IsoCurve(_surface.TransformedBy(transform), _fixed, _alongU, _domain, _reversed);

        protected override Point3d Evaluate(double parameter)
        {
            double mapped = Map(parameter);

            return _alongU ? _surface.Evaluate(mapped, _fixed) : _surface.Evaluate(_fixed, mapped);
        }

        protected override Vector3d EvaluateDerivative(double parameter)
        {
            double mapped = Map(parameter);
            double sign = _reversed ? -1.0 : 1.0;

            if (_alongU)
            {
                _surface.EvaluateDerivatives(mapped, _fixed, out Vector3d du, out _);

                return du * sign;
            }

            _surface.EvaluateDerivatives(_fixed, mapped, out _, out Vector3d dv);

            return dv * sign;
        }

        protected override Vector3d EvaluateSecondDerivative(double parameter)
        {
            double mapped = Map(parameter);

            // Unsigned: reversing a curve negates its first derivative and leaves the second
            // alone, because the chain rule squares the -1.
            if (_alongU)
            {
                _surface.EvaluateSecondDerivatives(mapped, _fixed, out Vector3d duu, out _, out _);

                return duu;
            }

            _surface.EvaluateSecondDerivatives(_fixed, mapped, out _, out _, out Vector3d dvv);

            return dvv;
        }

        /// <summary>The surface parameter a curve parameter stands for.</summary>
        private double Map(double parameter) =>
            _reversed ? _domain.Min + _domain.Max - parameter : parameter;
    }
}
