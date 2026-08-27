using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// The numerical tolerance a geometric predicate should use. Tolerance in Spark is always
/// an explicit, passed value: there is no ambient, static or thread-local default anywhere
/// in the kernel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is passed rather than ambient.</b> The evaluation cache is keyed by provenance,
/// and tolerance is part of every node's cache key. An ambient tolerance would be invisible
/// to that key, so changing it would not invalidate anything and the graph would go on
/// serving geometry computed at the old tolerance, silently and with no way for a user to
/// tell. See ADR-0010.
/// </para>
/// <para>
/// <b>The zero sentinel.</b> A default-constructed <see cref="Tolerance"/> — the one you get
/// from <c>default</c>, and therefore from every <c>in Tolerance tolerance = default</c>
/// parameter — has <c>Linear == 0</c> in its backing field, and that is the sentinel meaning
/// "use the default tolerance". Reading <see cref="Linear"/> on such a value returns the
/// default linear tolerance, never zero. It does <b>not</b> mean "compare exactly". If you
/// want exact comparison, use <c>operator ==</c>, which is exact on every value type here.
/// </para>
/// <para>
/// <b>Scale awareness.</b> Coordinates in Spark are unitless, so a fixed 1e-6 is wrong for a
/// model measured in kilometres and wrong for one measured in microns.
/// <see cref="ForScale(double)"/> derives a tolerance from a characteristic length, and
/// <see cref="Scaled(double)"/> rescales an existing one.
/// </para>
/// </remarks>
public readonly struct Tolerance : IEquatable<Tolerance>
{
    private readonly double _linear;
    private readonly double _angularRadians;
    private readonly double _relativeEpsilon;

    /// <summary>
    /// Creates a tolerance from its three components.
    /// </summary>
    /// <param name="linear">
    /// The linear tolerance: the largest distance at which two positions are considered
    /// coincident. Must be zero, positive and finite. <b>Zero is the sentinel meaning "use
    /// the default"</b> and makes the whole value behave as <see cref="Default"/>.
    /// </param>
    /// <param name="angular">
    /// The angular tolerance: the largest angle at which two directions are considered
    /// parallel. Must be zero, positive and finite.
    /// </param>
    /// <param name="relativeEpsilon">
    /// The relative epsilon used to widen scalar comparisons at large magnitudes, where the
    /// linear tolerance can fall below the representable precision of a
    /// <see cref="double"/>. Must be zero, positive and finite. A value of 1e-12 means
    /// "treat values agreeing to twelve significant figures as equal".
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any argument is negative, infinite or <see cref="double.NaN"/>.
    /// </exception>
    public Tolerance(double linear, Angle angular, double relativeEpsilon)
    {
        ThrowIfNotFiniteAndNonNegative(linear, nameof(linear));
        ThrowIfNotFiniteAndNonNegative(angular.Radians, nameof(angular));
        ThrowIfNotFiniteAndNonNegative(relativeEpsilon, nameof(relativeEpsilon));

        _linear = linear;
        _angularRadians = angular.Radians;
        _relativeEpsilon = relativeEpsilon;
    }

    /// <summary>
    /// The default tolerance: a linear tolerance of 1e-6, an angular tolerance of 0.001
    /// degrees, and a relative epsilon of 1e-12. These suit a model whose characteristic
    /// length is of order one.
    /// </summary>
    /// <remarks>
    /// This is exactly <c>default(Tolerance)</c>, so <c>Tolerance.Default == default</c> is
    /// <see langword="true"/>.
    /// </remarks>
    public static Tolerance Default => default;

    // The default components are deliberately not *public* constants. A public const bakes
    // into every consuming assembly at compile time, so a package built against 1.0 would
    // carry 1.0's epsilon forever even after the kernel changed it — an ADR-0009 hazard
    // hiding inside a constant. Private consts have no such reach, and the compiler folds
    // them, so they cost nothing on the comparison paths below.
    private const double DefaultLinearTolerance = 1e-6;

    private const double DefaultAngularToleranceInDegrees = 0.001;

    private const double DefaultRelativeEpsilon = 1e-12;

    // Guards ForScale and Scaled against producing a linear tolerance of zero — which would be
    // read straight back as the "use the default" sentinel — or a denormal at absurdly small
    // characteristic lengths.
    private const double MinimumScaledLinear = 1e-15;

    /// <summary>
    /// The largest distance at which two positions count as coincident. Never returns zero:
    /// a zero backing field is the sentinel for <see cref="Default"/>.
    /// </summary>
    public double Linear => _linear == 0.0 ? DefaultLinearTolerance : _linear;

    /// <summary>
    /// The largest angle at which two directions count as parallel. Never returns zero for a
    /// default-constructed tolerance.
    /// </summary>
    public Angle Angular => _linear == 0.0
        ? Angle.FromDegrees(DefaultAngularToleranceInDegrees)
        : Angle.FromRadians(_angularRadians);

    /// <summary>
    /// The relative epsilon that widens scalar comparisons at large magnitudes. Never
    /// returns zero for a default-constructed tolerance.
    /// </summary>
    public double RelativeEpsilon => _linear == 0.0 ? DefaultRelativeEpsilon : _relativeEpsilon;

    /// <summary>
    /// Derives a tolerance appropriate to a model of the given size.
    /// </summary>
    /// <param name="characteristicLength">
    /// A representative length for the geometry being worked on — typically the diagonal of
    /// its bounding box. The sign is ignored. Zero returns <see cref="Default"/>.
    /// </param>
    /// <returns>
    /// A tolerance whose linear component is the default linear tolerance multiplied by the
    /// magnitude of <paramref name="characteristicLength"/>, floored at 1e-15 so that it can
    /// never collapse to zero or to a denormal. The angular component and the relative
    /// epsilon are unchanged from <see cref="Default"/>, because both are dimensionless and
    /// therefore already scale-free.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="characteristicLength"/> is <see cref="double.NaN"/> or
    /// infinite.
    /// </exception>
    public static Tolerance ForScale(double characteristicLength)
    {
        if (!double.IsFinite(characteristicLength))
        {
            throw new ArgumentOutOfRangeException(
                nameof(characteristicLength),
                characteristicLength,
                "The characteristic length must be finite.");
        }

        double magnitude = Math.Abs(characteristicLength);

        if (magnitude == 0.0)
        {
            return Default;
        }

        double linear = Math.Max(DefaultLinearTolerance * magnitude, MinimumScaledLinear);

        return new Tolerance(linear, Angle.FromDegrees(DefaultAngularToleranceInDegrees), DefaultRelativeEpsilon);
    }

    /// <summary>
    /// Returns this tolerance with its linear component multiplied by a factor.
    /// </summary>
    /// <param name="factor">
    /// The factor to scale the linear tolerance by. Must be positive and finite.
    /// </param>
    /// <returns>
    /// A tolerance with the scaled linear component and an unchanged angular component and
    /// relative epsilon, both of which are dimensionless.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="factor"/> is zero, negative, infinite or
    /// <see cref="double.NaN"/>.
    /// </exception>
    public Tolerance Scaled(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(factor),
                factor,
                "The scale factor must be positive and finite.");
        }

        return new Tolerance(Math.Max(Linear * factor, MinimumScaledLinear), Angular, RelativeEpsilon);
    }

    /// <summary>
    /// Tests whether two scalars are equal within this tolerance.
    /// </summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns>
    /// <see langword="true"/> when the difference is at most the comparison threshold, which
    /// is the larger of <see cref="Linear"/> and <see cref="RelativeEpsilon"/> multiplied by
    /// the larger magnitude of the two operands. The relative term is what keeps the
    /// comparison meaningful at magnitudes where an absolute 1e-6 is below the precision a
    /// <see cref="double"/> can represent. Returns <see langword="false"/> if either operand
    /// is <see cref="double.NaN"/>. Two infinities of the same sign compare equal.
    /// </returns>
    /// <remarks>
    /// This is written as one subtraction compared against one threshold, and
    /// <see cref="IsLessThan(double, double)"/> and
    /// <see cref="IsGreaterThan(double, double)"/> are written against the <b>same</b> two
    /// quantities. That is what makes the three predicates a genuine partition rather than
    /// three separate approximations that agree most of the time: an earlier version
    /// compared <c>a</c> against <c>b - threshold</c>, and the rounding of that subtraction
    /// disagreed with the rounding of <c>a - b</c> by an ulp exactly on the boundary, so
    /// pairs such as <c>(2, 2.000001)</c> fell into no bucket at all and pairs such as
    /// <c>(1e-30, -1e-6)</c> fell into two.
    /// </remarks>
    public bool AreEqual(double a, double b)
    {
        if (a == b)
        {
            return true;
        }

        return Math.Abs(a - b) <= Threshold(a, b);
    }

    /// <summary>
    /// Tests whether a scalar is zero within this tolerance.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns>
    /// <see langword="true"/> when the magnitude of <paramref name="value"/> is at most
    /// <see cref="Linear"/>. This test is purely absolute — the relative epsilon plays no
    /// part, because nothing is relative to zero. Returns <see langword="false"/> for
    /// <see cref="double.NaN"/>.
    /// </returns>
    public bool IsZero(double value) => Math.Abs(value) <= Linear;

    /// <summary>
    /// Tests whether a separation measured between two things is negligible at the scale
    /// those things are at.
    /// </summary>
    /// <param name="separation">
    /// The distance between the two things. The sign is ignored.
    /// </param>
    /// <param name="scale">
    /// The magnitude of the larger of the two things — for positions, the larger distance
    /// from the origin. The sign is ignored. A scale of zero, or a non-finite scale, falls
    /// back to a purely absolute comparison against <see cref="Linear"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the separation is at most the larger of
    /// <see cref="Linear"/> and <see cref="RelativeEpsilon"/> multiplied by
    /// <paramref name="scale"/> — the same hybrid rule
    /// <see cref="AreEqual(double, double)"/> applies to scalars, expressed for a distance
    /// whose operands' magnitudes the caller already knows. Returns <see langword="false"/>
    /// when <paramref name="separation"/> is <see cref="double.NaN"/>.
    /// </returns>
    /// <remarks>
    /// This exists so that every geometric <c>EqualsWithin</c> in the kernel uses one rule.
    /// Comparing a distance against <see cref="Linear"/> alone is absolute-only, and at
    /// coordinates around 1e12 an absolute 1e-6 falls below what a <see cref="double"/> can
    /// resolve, so such a test silently degenerates into bit-equality.
    /// </remarks>
    public bool IsNegligible(double separation, double scale)
    {
        double magnitude = Math.Abs(scale);
        double threshold = double.IsFinite(magnitude)
            ? Math.Max(Linear, RelativeEpsilon * magnitude)
            : Linear;

        return Math.Abs(separation) <= threshold;
    }

    /// <summary>
    /// Tests whether one scalar is strictly less than another, allowing for tolerance.
    /// </summary>
    /// <param name="a">The value that may be smaller.</param>
    /// <param name="b">The value that may be larger.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="a"/> is below <paramref name="b"/> by more
    /// than the comparison threshold. Exactly one of
    /// <see cref="IsLessThan(double, double)"/>, <see cref="AreEqual(double, double)"/> and
    /// <see cref="IsGreaterThan(double, double)"/> is true for any pair of operands neither of
    /// which is <see cref="double.NaN"/>. All three compare the same single subtraction
    /// against the same single threshold, so that partition holds by construction rather than
    /// by the two roundings happening to agree.
    /// </returns>
    public bool IsLessThan(double a, double b) => a - b < -Threshold(a, b);

    /// <summary>
    /// Tests whether one scalar is strictly greater than another, allowing for tolerance.
    /// </summary>
    /// <param name="a">The value that may be larger.</param>
    /// <param name="b">The value that may be smaller.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="a"/> exceeds <paramref name="b"/> by more
    /// than the comparison threshold, computed exactly as
    /// <see cref="IsLessThan(double, double)"/> computes it.
    /// </returns>
    public bool IsGreaterThan(double a, double b) => a - b > Threshold(a, b);

    /// <summary>
    /// Tests whether a scalar is meaningfully positive.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="value"/> is greater than
    /// <see cref="Linear"/>. A value inside the tolerance band around zero is neither
    /// positive nor negative.
    /// </returns>
    public bool IsPositive(double value) => value > Linear;

    /// <summary>
    /// Tests whether a scalar is meaningfully negative.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="value"/> is less than the negation of
    /// <see cref="Linear"/>. A value inside the tolerance band around zero is neither
    /// positive nor negative.
    /// </returns>
    public bool IsNegative(double value) => value < -Linear;

    /// <summary>
    /// Compares two tolerances for exact equality of their resolved components.
    /// </summary>
    /// <param name="left">The first tolerance.</param>
    /// <param name="right">The second tolerance.</param>
    /// <returns>
    /// <see langword="true"/> when the resolved components match, so a default-constructed
    /// tolerance compares equal to <see cref="Default"/>.
    /// </returns>
    public static bool operator ==(Tolerance left, Tolerance right) => left.Equals(right);

    /// <summary>Compares two tolerances for inequality.</summary>
    /// <param name="left">The first tolerance.</param>
    /// <param name="right">The second tolerance.</param>
    /// <returns><see langword="true"/> when <c>operator ==</c> would return <see langword="false"/>.</returns>
    public static bool operator !=(Tolerance left, Tolerance right) => !left.Equals(right);

    /// <summary>
    /// Compares this tolerance with another by resolved component values, so that a
    /// default-constructed tolerance equals <see cref="Default"/>.
    /// </summary>
    /// <param name="other">The tolerance to compare with.</param>
    /// <returns><see langword="true"/> when all three resolved components are equal.</returns>
    public bool Equals(Tolerance other) =>
        Linear.Equals(other.Linear)
        && Angular.Equals(other.Angular)
        && RelativeEpsilon.Equals(other.RelativeEpsilon);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Tolerance other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Linear, Angular.Radians, RelativeEpsilon);

    /// <summary>
    /// Formats the resolved components, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>Tolerance(Linear=1E-06, Angular=0.001°, Relative=1E-12)</c>.</returns>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Tolerance(Linear={Linear}, Angular={Angular}, Relative={RelativeEpsilon})");

    private double Threshold(double a, double b)
    {
        double magnitude = Math.Max(Math.Abs(a), Math.Abs(b));

        // An infinite or NaN operand would make the relative term infinite or NaN, which
        // would then swallow every comparison it touched. Fall back to the absolute term so
        // that, for example, IsLessThan(x, PositiveInfinity) still answers true.
        return double.IsFinite(magnitude) ? Math.Max(Linear, RelativeEpsilon * magnitude) : Linear;
    }

    private static void ThrowIfNotFiniteAndNonNegative(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The tolerance component must be zero or a positive finite number.");
        }
    }
}
