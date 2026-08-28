using System;

namespace Spark.Geometry;

/// <summary>
/// The numerical routines the curve layer shares: adaptive Gauss–Legendre quadrature for arc
/// length, and a safeguarded Newton solver for inverting a monotone increasing function.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is public. These are implementation details of <see cref="Curve"/> and its
/// subclasses, and the shape of them is expected to change as analytic curves take over more
/// of the work from the generic fallbacks.
/// </para>
/// <para>
/// Every routine takes a tolerance and a <c>scale</c>. The scale is a representative magnitude
/// for the quantity being computed — an arc length, a coordinate — and it is what lets a
/// convergence test mean the same thing at a working scale of 1e-9 and at 1e9. Comparing an
/// absolute residual against <see cref="Tolerance.Linear"/> alone would loop forever at large
/// scale and stop after one step at small scale.
/// </para>
/// </remarks>
internal static class CurveNumerics
{
    /// <summary>
    /// Abscissae of the ten-point Gauss–Legendre rule on <c>[-1, 1]</c>, given as the five
    /// positive values; the rule is symmetric so the negatives are implied.
    /// </summary>
    private static readonly double[] GaussAbscissae =
    [
        0.1488743389816312,
        0.4333953941292472,
        0.6794095682990244,
        0.8650633666889845,
        0.9739065285171717,
    ];

    /// <summary>Weights matching <see cref="GaussAbscissae"/>.</summary>
    private static readonly double[] GaussWeights =
    [
        0.2955242247147529,
        0.2692667193099963,
        0.2190863625159820,
        0.1494513491505806,
        0.0666713443086881,
    ];

    /// <summary>
    /// The deepest the adaptive quadrature will bisect. Twenty levels is a million
    /// subintervals, which is far past the point where a curve's own conditioning, rather than
    /// the rule, sets the accuracy. The cap exists so that a pathological integrand fails by
    /// returning a slightly wrong number rather than by exhausting the stack.
    /// </summary>
    private const int MaximumQuadratureDepth = 20;

    /// <summary>The most iterations a safeguarded Newton solve will take before giving up.</summary>
    private const int MaximumNewtonIterations = 64;

    /// <summary>
    /// Integrates <paramref name="integrand"/> over <c>[a, b]</c> by adaptive ten-point
    /// Gauss–Legendre quadrature.
    /// </summary>
    /// <param name="integrand">The function to integrate. Must be finite over the interval.</param>
    /// <param name="a">The lower limit.</param>
    /// <param name="b">The upper limit. May be below <paramref name="a"/>, in which case the
    /// result is negated in the usual way.</param>
    /// <param name="tolerance">The tolerance governing the bisection test.</param>
    /// <param name="scale">
    /// A representative magnitude for the value of the integral, used to make the error test
    /// relative rather than absolute. Pass the crude chord-length estimate of an arc length.
    /// </param>
    /// <returns>The integral, or zero when the limits coincide.</returns>
    internal static double Integrate(
        Func<double, double> integrand,
        double a,
        double b,
        in Tolerance tolerance,
        double scale)
    {
        if (a == b)
        {
            return 0.0;
        }

        return Refine(integrand, a, b, GaussRule(integrand, a, b), tolerance, scale, MaximumQuadratureDepth);
    }

    /// <summary>
    /// Solves <c>f(t) = target</c> for a function known to be non-decreasing on
    /// <c>[low, high]</c>, using Newton's method safeguarded by bisection.
    /// </summary>
    /// <param name="f">The monotone function.</param>
    /// <param name="derivative">
    /// The derivative of <paramref name="f"/>. It may be zero — a flat stretch of a monotone
    /// function is legitimate — and the solver falls back to bisection when it is.
    /// </param>
    /// <param name="target">The value to solve for.</param>
    /// <param name="low">The lower end of a bracket known to contain the root.</param>
    /// <param name="high">The upper end of that bracket.</param>
    /// <param name="tolerance">The tolerance governing convergence.</param>
    /// <param name="valueScale">A representative magnitude for <paramref name="target"/>.</param>
    /// <param name="parameterScale">A representative magnitude for the parameter.</param>
    /// <returns>
    /// The parameter at which <paramref name="f"/> reaches <paramref name="target"/>, always
    /// inside <c>[low, high]</c>. When the function never reaches the target the relevant
    /// bracket end is returned, which is what makes an out-of-range arc length clamp rather
    /// than diverge.
    /// </returns>
    internal static double SolveMonotone(
        Func<double, double> f,
        Func<double, double> derivative,
        double target,
        double low,
        double high,
        in Tolerance tolerance,
        double valueScale,
        double parameterScale)
    {
        if (!(high > low))
        {
            return low;
        }

        double lowerBound = low;
        double upperBound = high;
        double t = 0.5 * (low + high);

        for (int iteration = 0; iteration < MaximumNewtonIterations; iteration++)
        {
            double residual = f(t) - target;

            if (tolerance.IsNegligible(residual, valueScale))
            {
                return t;
            }

            if (residual > 0.0)
            {
                upperBound = t;
            }
            else
            {
                lowerBound = t;
            }

            double slope = derivative(t);
            double next = slope > 0.0 && double.IsFinite(slope) ? t - (residual / slope) : double.NaN;

            // Newton is only trusted while it stays inside the bracket bisection maintains.
            // Outside it, or on a zero derivative, fall back to the midpoint: that guarantees
            // the bracket halves every step, so the solve terminates even where the curve is
            // stationary and Newton has nothing to work with.
            if (!double.IsFinite(next) || next <= lowerBound || next >= upperBound)
            {
                next = 0.5 * (lowerBound + upperBound);
            }

            if (tolerance.IsNegligible(next - t, parameterScale))
            {
                return next;
            }

            t = next;
        }

        return t;
    }

    /// <summary>
    /// One application of the ten-point Gauss–Legendre rule, mapped from <c>[-1, 1]</c> onto
    /// <c>[a, b]</c>.
    /// </summary>
    /// <param name="integrand">The function to integrate.</param>
    /// <param name="a">The lower limit.</param>
    /// <param name="b">The upper limit.</param>
    /// <returns>The estimated integral over the interval.</returns>
    private static double GaussRule(Func<double, double> integrand, double a, double b)
    {
        double half = 0.5 * (b - a);
        double centre = 0.5 * (a + b);
        double sum = 0.0;

        for (int i = 0; i < GaussAbscissae.Length; i++)
        {
            double offset = half * GaussAbscissae[i];

            sum += GaussWeights[i] * (integrand(centre - offset) + integrand(centre + offset));
        }

        return sum * half;
    }

    /// <summary>
    /// Bisects until the two halves agree with the whole to within tolerance, or until the
    /// depth budget runs out.
    /// </summary>
    /// <param name="integrand">The function to integrate.</param>
    /// <param name="a">The lower limit.</param>
    /// <param name="b">The upper limit.</param>
    /// <param name="whole">The single-panel estimate over <c>[a, b]</c>, already computed.</param>
    /// <param name="tolerance">The tolerance governing the agreement test.</param>
    /// <param name="scale">A representative magnitude for the integral.</param>
    /// <param name="depth">The remaining bisection budget.</param>
    /// <returns>The refined integral.</returns>
    private static double Refine(
        Func<double, double> integrand,
        double a,
        double b,
        double whole,
        in Tolerance tolerance,
        double scale,
        int depth)
    {
        double middle = 0.5 * (a + b);

        // A subinterval that has shrunk to the point where its midpoint is one of its own ends
        // cannot be bisected again, whatever the depth budget says.
        if (middle <= a || middle >= b)
        {
            return whole;
        }

        double left = GaussRule(integrand, a, middle);
        double right = GaussRule(integrand, middle, b);
        double split = left + right;

        if (depth <= 0 || tolerance.IsNegligible(split - whole, scale))
        {
            return split;
        }

        return Refine(integrand, a, middle, left, tolerance, scale, depth - 1)
            + Refine(integrand, middle, b, right, tolerance, scale, depth - 1);
    }
}
