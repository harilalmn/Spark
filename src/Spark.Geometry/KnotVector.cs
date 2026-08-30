using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// The knot vector of a B-spline or NURBS curve: a non-decreasing sequence of parameters that
/// says where each control point starts and stops influencing the shape.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists on its own, below <see cref="Curve"/>, because it is where NURBS goes wrong.</b>
/// Almost every hard-to-diagnose fault in a spline implementation is a knot-vector fault — a
/// multiplicity one too high, a span index off by one at the end of the domain, a vector whose
/// length does not match the control-point count — and every one of those is arithmetic that needs
/// no curve, no control points and no evaluation to test. Building the invariants here means a
/// <c>NurbsCurve</c> can be written assuming they hold.
/// </para>
/// <para>
/// <b>The invariants are enforced in the constructor and are not re-checked afterwards.</b> A knot
/// vector is immutable, so a valid one stays valid; the price of that is that every way of making
/// one has to go through the check, which is why the array is copied in rather than adopted.
/// </para>
/// <para>
/// <b>Only the interior of the domain is parameterised.</b> For a clamped vector of degree
/// <i>p</i>, the first and last <i>p + 1</i> knots repeat, and the curve runs from
/// <c>Knots[p]</c> to <c>Knots[Count - 1 - p]</c>. That is what <see cref="Domain"/> reports, and
/// it is not the first and last knots — a mistake worth naming because the two coincide for an
/// unclamped vector and differ for every clamped one, which is nearly all of them.
/// </para>
/// </remarks>
public sealed class KnotVector : IEquatable<KnotVector>
{
    private readonly double[] _knots;

    /// <summary>
    /// Creates a knot vector, checking every invariant.
    /// </summary>
    /// <param name="degree">The curve's degree. At least 1.</param>
    /// <param name="knots">
    /// The knots, non-decreasing. Copied, so the caller may keep and reuse the array.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="knots"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="degree"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// The knots are too few for the degree, are not all finite, decrease anywhere, or contain a
    /// multiplicity higher than the degree allows.
    /// </exception>
    public KnotVector(int degree, IReadOnlyList<double> knots)
    {
        ArgumentNullException.ThrowIfNull(knots);
        ArgumentOutOfRangeException.ThrowIfLessThan(degree, 1);

        // The shortest legal vector is one span: degree + 1 knots at each end. Anything shorter
        // cannot describe a curve of that degree at all, and catching it here means the span
        // search below never has to consider an empty domain.
        int minimum = (2 * degree) + 2;
        if (knots.Count < minimum)
        {
            throw new ArgumentException(
                $"A degree-{degree} knot vector needs at least {minimum} knots and was given {knots.Count}. "
                + "A B-spline of degree p needs p + 1 knots clamping each end of at least one span.",
                nameof(knots));
        }

        double[] copy = new double[knots.Count];
        for (int i = 0; i < knots.Count; i++)
        {
            double knot = knots[i];

            if (!double.IsFinite(knot))
            {
                throw new ArgumentException(
                    $"Knot {i} is {knot.ToString("R", CultureInfo.InvariantCulture)}, which is not finite.",
                    nameof(knots));
            }

            if (i > 0 && knot < copy[i - 1])
            {
                throw new ArgumentException(
                    $"Knots must not decrease: knot {i} is {knot.ToString("R", CultureInfo.InvariantCulture)} "
                    + $"after {copy[i - 1].ToString("R", CultureInfo.InvariantCulture)}.",
                    nameof(knots));
            }

            copy[i] = knot;
        }

        Degree = degree;
        _knots = copy;

        // The domain must not be a point. A vector whose first and last interior knots coincide
        // describes no curve, and every parameter in it would be simultaneously at both ends.
        if (copy[degree] >= copy[copy.Length - 1 - degree])
        {
            throw new ArgumentException(
                "The knot vector has an empty domain: its first and last interior knots coincide.",
                nameof(knots));
        }

        CheckMultiplicities(nameof(knots));
    }

    /// <summary>The degree of the curve this vector belongs to.</summary>
    public int Degree { get; }

    /// <summary>How many knots there are.</summary>
    public int Count => _knots.Length;

    /// <summary>
    /// How many control points a curve with this vector must have.
    /// </summary>
    /// <remarks>
    /// The defining relation of a B-spline: <c>knots = controlPoints + degree + 1</c>. Stated as a
    /// property rather than left for the caller to compute, because getting it wrong is the other
    /// classic NURBS fault and the arithmetic should exist exactly once.
    /// </remarks>
    public int ControlPointCount => _knots.Length - Degree - 1;

    /// <summary>
    /// The parameter range the curve actually occupies: <c>[Knots[p], Knots[Count - 1 - p]]</c>.
    /// </summary>
    /// <remarks>
    /// <b>Not the first and last knots.</b> For a clamped vector those repeat, and using them
    /// would put the domain's ends where the basis functions are not yet a partition of unity.
    /// </remarks>
    public Interval Domain => new(_knots[Degree], _knots[_knots.Length - 1 - Degree]);

    /// <summary>
    /// Whether the vector is clamped: both ends repeat <c>Degree + 1</c> times, so the curve
    /// passes through its first and last control points.
    /// </summary>
    public bool IsClamped =>
        Multiplicity(_knots[0]) >= Degree + 1
        && Multiplicity(_knots[^1]) >= Degree + 1;

    /// <summary>The knot at an index.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The knot.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the vector.</exception>
    public double this[int index] => _knots[index];

    /// <summary>The knots, in order.</summary>
    /// <returns>A copy, because the vector is immutable and an exposed array would not be.</returns>
    public double[] ToArray() => [.. _knots];

    /// <summary>
    /// Builds the clamped, uniform knot vector for a given degree and control-point count — the
    /// vector almost every curve starts life with.
    /// </summary>
    /// <param name="degree">The degree. At least 1.</param>
    /// <param name="controlPoints">How many control points. More than <paramref name="degree"/>.</param>
    /// <returns>A clamped vector over the domain 0 to 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The degree is less than 1, or there are not more control points than the degree.
    /// </exception>
    public static KnotVector CreateClamped(int degree, int controlPoints)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(degree, 1);

        // A degree-p curve is defined by at least p + 1 control points; with fewer, the basis
        // functions of the missing ones would be asked for and would not exist.
        ArgumentOutOfRangeException.ThrowIfLessThan(controlPoints, degree + 1);

        int count = controlPoints + degree + 1;
        double[] knots = new double[count];
        int interior = controlPoints - degree - 1;

        for (int i = 0; i <= degree; i++)
        {
            knots[i] = 0.0;
            knots[count - 1 - i] = 1.0;
        }

        for (int i = 1; i <= interior; i++)
        {
            knots[degree + i] = (double)i / (interior + 1);
        }

        return new KnotVector(degree, knots);
    }

    /// <summary>How many times a knot value is repeated.</summary>
    /// <param name="knot">The value to count.</param>
    /// <param name="tolerance">The tolerance two knots are counted as equal within.</param>
    /// <returns>The multiplicity, or zero when the value is not a knot.</returns>
    /// <remarks>
    /// Compared with a tolerance and never with <c>==</c>. A clamped vector produced by arithmetic
    /// — a refinement, a degree elevation, a reparameterisation — rarely has end knots that are
    /// bitwise equal, and a multiplicity check that demanded it would report 1 where the answer is
    /// <c>degree + 1</c> and reject a vector that is perfectly good.
    /// </remarks>
    public int Multiplicity(double knot, in Tolerance tolerance = default)
    {
        int count = 0;

        foreach (double candidate in _knots)
        {
            // Tolerance resolves its own default from `default`, so no unwrapping is needed here.
            if (tolerance.AreEqual(candidate, knot))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The index of the knot span containing a parameter — de Boor's A2.1.
    /// </summary>
    /// <param name="parameter">The parameter, which is clamped into <see cref="Domain"/>.</param>
    /// <returns>
    /// The span index <c>i</c> such that <c>Knots[i] &lt;= parameter &lt; Knots[i + 1]</c>, and for
    /// the parameter at the very end of the domain, the index of the last <b>non-empty</b> span.
    /// </returns>
    /// <remarks>
    /// <b>The end of the domain is the whole difficulty.</b> At the last parameter the half-open
    /// rule finds no span at all — there is no knot greater than it — and an implementation that
    /// returns the naive answer indexes one past the last control point and reads memory that is
    /// not the curve's. The special case is not a fudge; it is the statement that the domain is
    /// closed at its end while every interior span is half-open.
    /// </remarks>
    public int FindSpan(double parameter)
    {
        Interval domain = Domain;
        int last = _knots.Length - 1 - Degree;

        if (parameter >= domain.Max)
        {
            // Walk back over any repeated end knots to the last span that has width. Repeats at
            // the end are the normal case for a clamped vector, so this loop nearly always runs.
            int span = last - 1;
            while (span > Degree && _knots[span] >= domain.Max)
            {
                span--;
            }

            return span;
        }

        if (parameter <= domain.Min)
        {
            return Degree;
        }

        // Binary search over the interior spans. Linear search is correct and is what a first
        // implementation reaches for; it is also O(n) per evaluation, and evaluation happens once
        // per tessellated point.
        int low = Degree;
        int high = last;
        int middle = (low + high) / 2;

        while (parameter < _knots[middle] || parameter >= _knots[middle + 1])
        {
            if (parameter < _knots[middle])
            {
                high = middle;
            }
            else
            {
                low = middle;
            }

            middle = (low + high) / 2;
        }

        return middle;
    }

    /// <summary>
    /// The <c>Degree + 1</c> non-zero basis functions at a parameter — de Boor's A2.2.
    /// </summary>
    /// <param name="span">The span index, from <see cref="FindSpan"/>.</param>
    /// <param name="parameter">The parameter.</param>
    /// <returns>
    /// The basis function values <c>N[span - Degree] … N[span]</c>, in that order. They sum to 1.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="span"/> is not a valid span.</exception>
    /// <remarks>
    /// <para>
    /// Only <c>Degree + 1</c> of the basis functions are non-zero anywhere, which is the local
    /// support that makes B-splines useful: moving one control point changes the curve over a few
    /// spans and nowhere else. Computing the whole basis and discarding the zeros would be correct
    /// and would also be the reason a spline kernel is slow.
    /// </para>
    /// <para>
    /// The recurrence is written without divisions by zero rather than with guards against them —
    /// the <c>left</c> and <c>right</c> differences are constructed so that a zero denominator
    /// cannot arise for a valid span, which is one of the things the constructor's invariants buy.
    /// </para>
    /// </remarks>
    public double[] BasisFunctions(int span, double parameter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(span, Degree);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(span, _knots.Length - 2 - Degree);

        double[] basis = new double[Degree + 1];
        double[] left = new double[Degree + 1];
        double[] right = new double[Degree + 1];

        basis[0] = 1.0;

        for (int j = 1; j <= Degree; j++)
        {
            left[j] = parameter - _knots[span + 1 - j];
            right[j] = _knots[span + j] - parameter;

            double saved = 0.0;
            for (int r = 0; r < j; r++)
            {
                double denominator = right[r + 1] + left[j - r];
                double temp = denominator == 0.0 ? 0.0 : basis[r] / denominator;

                basis[r] = saved + (right[r + 1] * temp);
                saved = left[j - r] * temp;
            }

            basis[j] = saved;
        }

        return basis;
    }

    /// <summary>Whether another vector has the same degree and exactly the same knots.</summary>
    /// <param name="other">The other vector.</param>
    /// <returns>True when they are the same vector.</returns>
    /// <remarks>
    /// <b>Exact, not tolerant.</b> A knot vector is data — two vectors are the same vector or they
    /// are not, and a tolerant equality would make <c>a == b</c> and <c>b == c</c> fail to imply
    /// <c>a == c</c>, which is not a defensible thing for <see cref="object.Equals(object)"/> to
    /// do. Where a tolerant question is the right one — <i>is this end knot repeated
    /// <c>degree + 1</c> times</i>, after arithmetic has drifted — <see cref="Multiplicity"/> takes
    /// a <see cref="Tolerance"/> and answers it.
    /// </remarks>
    public bool Equals(KnotVector? other)
    {
        if (other is null || other.Degree != Degree || other._knots.Length != _knots.Length)
        {
            return false;
        }

        for (int i = 0; i < _knots.Length; i++)
        {
            if (_knots[i] != other._knots[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as KnotVector);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Degree);

        foreach (double knot in _knots)
        {
            hash.Add(knot);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"KnotVector(degree {Degree}, {Count} knots, domain {Domain.Min:G6}..{Domain.Max:G6})");

    /// <summary>
    /// Refuses a vector whose repeats are too high to describe a curve.
    /// </summary>
    /// <remarks>
    /// An interior knot repeated more than <c>Degree</c> times splits the curve into two curves
    /// that share a parameter, and one repeated more than <c>Degree + 1</c> times anywhere leaves a
    /// control point with no support at all. Both produce a curve that evaluates to nonsense rather
    /// than throwing, which is why they are refused at the door.
    /// </remarks>
    private void CheckMultiplicities(string parameterName)
    {
        int index = 0;
        while (index < _knots.Length)
        {
            int run = 1;
            while (index + run < _knots.Length && _knots[index + run] == _knots[index])
            {
                run++;
            }

            bool atEnd = index == 0 || index + run == _knots.Length;
            int allowed = atEnd ? Degree + 1 : Degree;

            if (run > allowed)
            {
                throw new ArgumentException(
                    $"Knot {_knots[index].ToString("R", CultureInfo.InvariantCulture)} is repeated {run} times; "
                    + $"a{(atEnd ? "n end" : "n interior")} knot of a degree-{Degree} curve may repeat at most "
                    + $"{allowed} times.",
                    parameterName);
            }

            index += run;
        }
    }
}
