using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A one-dimensional range between two bounds, used for curve and surface parameter domains
/// and for the axis extents of a <see cref="BoundingBox"/>.
/// </summary>
/// <remarks>
/// <para>
/// An interval is an <b>ordered pair</b>, not a set. The two bounds are stored exactly as
/// given, so an interval may be decreasing — <see cref="Min"/> larger than
/// <see cref="Max"/> — and <see cref="IsDecreasing"/> reports that. This matters because a
/// curve domain carries a direction: reversing a curve reverses its domain, and collapsing
/// the pair on construction would throw that information away. Where a directionless range
/// is wanted, call <see cref="MakeIncreasing"/> first.
/// </para>
/// <para>
/// Consequently <see cref="Length"/> is <b>signed</b>: it is negative exactly when the
/// interval is decreasing.
/// </para>
/// </remarks>
public readonly struct Interval : IEquatable<Interval>
{
    /// <summary>
    /// Creates an interval from its two bounds, storing them in the order given.
    /// </summary>
    /// <param name="min">
    /// The first bound. Named for the common increasing case; it is not required to be the
    /// smaller of the two.
    /// </param>
    /// <param name="max">The second bound.</param>
    public Interval(double min, double max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>
    /// The first bound, which is the lower one unless <see cref="IsDecreasing"/> is
    /// <see langword="true"/>.
    /// </summary>
    public double Min { get; }

    /// <summary>
    /// The second bound, which is the upper one unless <see cref="IsDecreasing"/> is
    /// <see langword="true"/>.
    /// </summary>
    public double Max { get; }

    /// <summary>
    /// The unit interval <c>[0, 1]</c>, the default parameter domain for a normalised curve.
    /// </summary>
    public static Interval Unit => new(0.0, 1.0);

    /// <summary>
    /// The signed extent of the interval, <see cref="Max"/> minus <see cref="Min"/>. Negative
    /// exactly when <see cref="IsDecreasing"/> is <see langword="true"/>, and zero for a
    /// single-point interval.
    /// </summary>
    public double Length => Max - Min;

    /// <summary>The value halfway between the two bounds.</summary>
    public double Mid => (Min + Max) * 0.5;

    /// <summary>
    /// <see langword="true"/> when <see cref="Min"/> is strictly greater than
    /// <see cref="Max"/>. A single-point interval is not decreasing.
    /// </summary>
    public bool IsDecreasing => Min > Max;

    /// <summary>
    /// <see langword="true"/> when both bounds are finite.
    /// </summary>
    /// <remarks>
    /// <b>Direction is not validity.</b> A decreasing interval is a perfectly good value —
    /// it is what a reversed curve's domain looks like — so <see cref="IsDecreasing"/>, not
    /// this property, is what asks about direction. Defining validity to require
    /// <c>Min &lt;= Max</c> would make the obvious <c>if (!domain.IsValid) throw</c> reject
    /// every reversed curve, which is exactly the guard a caller writes without thinking.
    /// </remarks>
    public bool IsValid => double.IsFinite(Min) && double.IsFinite(Max);

    /// <summary>
    /// Returns this interval with its bounds in increasing order.
    /// </summary>
    /// <returns>
    /// This interval unchanged if it is already increasing, or the same two bounds swapped if
    /// it is decreasing.
    /// </returns>
    public Interval MakeIncreasing() => IsDecreasing ? new Interval(Max, Min) : this;

    /// <summary>
    /// Tests whether a value falls inside the interval.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Linear"/> and
    /// <see cref="Tolerance.RelativeEpsilon"/> are consulted. A default-constructed tolerance
    /// means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value lies between the bounds, <b>inclusive</b> and
    /// widened by the tolerance at each end. The direction of the interval is ignored, so a
    /// decreasing interval includes the same values as its increasing form. Returns
    /// <see langword="false"/> for <see cref="double.NaN"/>, and for any interval that has a
    /// <see cref="double.NaN"/> bound.
    /// </returns>
    /// <remarks>
    /// The <see cref="double.NaN"/> guard is explicit rather than implied. This test is built
    /// from two <i>negated</i> predicates, and every comparison against a
    /// <see cref="double.NaN"/> is false, so both negations come out true and an unguarded
    /// version answers "yes, the interval includes NaN" — which it did, in an earlier version
    /// whose documentation already promised otherwise.
    /// </remarks>
    public bool Includes(double value, in Tolerance tolerance = default)
    {
        if (double.IsNaN(value) || double.IsNaN(Min) || double.IsNaN(Max))
        {
            return false;
        }

        Interval increasing = MakeIncreasing();

        return !tolerance.IsLessThan(value, increasing.Min)
            && !tolerance.IsGreaterThan(value, increasing.Max);
    }

    /// <summary>
    /// Clamps a value into the interval.
    /// </summary>
    /// <param name="value">The value to clamp.</param>
    /// <returns>
    /// The nearest value inside the interval. The direction of the interval is ignored, so a
    /// decreasing interval clamps to the same range as its increasing form. Returns
    /// <see cref="double.NaN"/> for a <see cref="double.NaN"/> input.
    /// </returns>
    public double Clamp(double value)
    {
        Interval increasing = MakeIncreasing();

        if (double.IsNaN(value))
        {
            return double.NaN;
        }

        return Math.Min(Math.Max(value, increasing.Min), increasing.Max);
    }

    /// <summary>
    /// Converts a value in this interval to its normalised position within it.
    /// </summary>
    /// <param name="value">The value to normalise.</param>
    /// <returns>
    /// Zero at <see cref="Min"/> and one at <see cref="Max"/>, interpolating in between and
    /// extrapolating outside — the result is not clamped. For a decreasing interval the
    /// result still runs from zero at <see cref="Min"/> to one at <see cref="Max"/>, which
    /// means it decreases as the value increases. Returns zero for a zero-length interval,
    /// where no meaningful position exists, rather than <see cref="double.NaN"/>.
    /// </returns>
    public double Normalise(double value)
    {
        double length = Length;

        return length == 0.0 ? 0.0 : (value - Min) / length;
    }

    /// <summary>
    /// Converts a normalised position into a value in this interval, inverting
    /// <see cref="Normalise(double)"/> for any interval of non-zero length.
    /// </summary>
    /// <remarks>
    /// The inversion is <b>to within floating-point rounding, not exact</b>. Normalising
    /// subtracts <see cref="Min"/> and denormalising adds it back, and when the interval sits
    /// far from the origin relative to its own length that subtraction discards significant
    /// figures the addition cannot restore: a unit-length interval based at 1e10 round-trips
    /// with an error near 1e-6, not zero. Compare round-tripped values with a
    /// <see cref="Tolerance"/>, never with <c>==</c>.
    /// </remarks>
    /// <param name="t">
    /// The normalised position: zero gives <see cref="Min"/> and one gives
    /// <see cref="Max"/>. Values outside <c>[0, 1]</c> are not clamped and extrapolate.
    /// </param>
    /// <returns>The corresponding value.</returns>
    public double Denormalise(double t) => Min + (t * Length);

    /// <summary>
    /// Returns the smallest increasing interval containing both this interval and another.
    /// </summary>
    /// <param name="other">The interval to combine with.</param>
    /// <returns>
    /// An increasing interval spanning both inputs, ignoring their directions. There is
    /// deliberately no empty-interval identity to seed an accumulation with: the inverted
    /// infinite range that a <see cref="BoundingBox"/> uses for that purpose would be
    /// indistinguishable here from a legitimately decreasing domain. Seed an accumulation
    /// with the first element instead.
    /// </returns>
    public Interval Union(in Interval other)
    {
        Interval a = MakeIncreasing();
        Interval b = other.MakeIncreasing();

        return new Interval(Math.Min(a.Min, b.Min), Math.Max(a.Max, b.Max));
    }

    /// <summary>
    /// Returns the overlap between this interval and another.
    /// </summary>
    /// <param name="other">The interval to intersect with.</param>
    /// <param name="tolerance">
    /// The tolerance to use, matching <see cref="Includes(double, in Tolerance)"/> so that
    /// the two agree about the ends. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// The increasing interval covered by both inputs, ignoring their directions, or
    /// <see langword="null"/> when they do not overlap. Intervals that touch at exactly one
    /// value overlap, and the result is the single-point interval at that value. Intervals
    /// separated by a gap no wider than the tolerance also count as touching, and the result
    /// is the single-point interval at the middle of that gap — which is the only answer
    /// consistent with <see cref="Includes(double, in Tolerance)"/>, since both intervals
    /// include that value. Returns <see langword="null"/> if either interval has a
    /// <see cref="double.NaN"/> bound.
    /// </returns>
    /// <remarks>
    /// Returning <see langword="null"/> rather than a sentinel interval is deliberate: no
    /// value of <see cref="Interval"/> unambiguously means "no overlap", and inventing one
    /// would make an empty result indistinguishable from a real degenerate one.
    /// </remarks>
    public Interval? Intersection(in Interval other, in Tolerance tolerance = default)
    {
        if (double.IsNaN(Min) || double.IsNaN(Max) || double.IsNaN(other.Min) || double.IsNaN(other.Max))
        {
            return null;
        }

        Interval a = MakeIncreasing();
        Interval b = other.MakeIncreasing();

        double min = Math.Max(a.Min, b.Min);
        double max = Math.Min(a.Max, b.Max);

        if (min <= max)
        {
            return new Interval(min, max);
        }

        if (tolerance.IsGreaterThan(min, max))
        {
            return null;
        }

        double touch = (min + max) * 0.5;

        return new Interval(touch, touch);
    }

    /// <summary>
    /// Returns the interval grown by the same amount at each end.
    /// </summary>
    /// <param name="amount">
    /// The amount to move each bound outwards. A negative amount shrinks the interval, and
    /// shrinking by more than half the length collapses it past a point and produces a
    /// decreasing interval rather than throwing.
    /// </param>
    /// <returns>
    /// The expanded interval, based on this interval's increasing form and therefore always
    /// returned in increasing order unless the shrink was large enough to invert it.
    /// </returns>
    public Interval Expanded(double amount)
    {
        Interval increasing = MakeIncreasing();

        return new Interval(increasing.Min - amount, increasing.Max + amount);
    }

    /// <summary>
    /// Returns this interval with its two bounds exchanged, reversing its direction.
    /// </summary>
    /// <returns>An interval running the other way.</returns>
    public Interval Reversed() => new(Max, Min);

    /// <summary>
    /// Tests whether this interval and another have the same bounds within a tolerance.
    /// </summary>
    /// <param name="other">The interval to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both bounds agree within tolerance. Direction is
    /// significant here: an increasing interval is not equal to its reverse.
    /// </returns>
    public bool EqualsWithin(in Interval other, in Tolerance tolerance = default) =>
        tolerance.AreEqual(Min, other.Min) && tolerance.AreEqual(Max, other.Max);

    /// <summary>
    /// Compares two intervals for exact equality of both bounds, following IEEE rules.
    /// </summary>
    /// <param name="left">The first interval.</param>
    /// <param name="right">The second interval.</param>
    /// <returns>
    /// <see langword="true"/> when both bounds are equal. Direction is significant. Use
    /// <see cref="EqualsWithin(in Interval, in Tolerance)"/> for comparison within tolerance.
    /// </returns>
    public static bool operator ==(in Interval left, in Interval right) =>
        left.Min == right.Min && left.Max == right.Max;

    /// <summary>Compares two intervals for exact inequality.</summary>
    /// <param name="left">The first interval.</param>
    /// <param name="right">The second interval.</param>
    /// <returns><see langword="true"/> when <c>operator ==</c> would return <see langword="false"/>.</returns>
    public static bool operator !=(in Interval left, in Interval right) => !(left == right);

    /// <summary>
    /// Tests exact equality of both bounds, treating <see cref="double.NaN"/> as equal to
    /// itself so that intervals remain usable as dictionary keys.
    /// </summary>
    /// <param name="other">The interval to compare with.</param>
    /// <returns><see langword="true"/> when both bounds are equal under <see cref="double.Equals(double)"/>.</returns>
    public bool Equals(Interval other) => Min.Equals(other.Min) && Max.Equals(other.Max);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Interval other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Min, Max);

    /// <summary>
    /// Formats the bounds, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>[0, 1]</c>.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{Min}, {Max}]");
}
