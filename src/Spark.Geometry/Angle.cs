using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// An angle. Stores radians internally and is constructed explicitly through
/// <see cref="FromDegrees(double)"/> or <see cref="FromRadians(double)"/>.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately <b>no implicit conversion from <see cref="double"/></b>. The whole
/// reason this type exists is to remove the degrees-versus-radians ambiguity from public
/// signatures; a conversion that can be triggered by accident would put the ambiguity
/// straight back. <c>Rotate(plane, 0.5)</c> must not compile, because a reader has no way to
/// tell whether the author meant half a radian or half a degree.
/// </para>
/// <para>
/// An <see cref="Angle"/> is an unbounded quantity, not a direction: it is perfectly valid
/// for it to hold ten full turns or minus three radians. Use <see cref="Normalised"/> or
/// <see cref="NormalisedSigned"/> when a canonical representative is wanted.
/// </para>
/// <para>
/// Positive angles are <b>counter-clockwise</b> when viewed from the positive end of the
/// rotation axis looking back towards the origin, consistent with Spark's right-handed
/// coordinate system.
/// </para>
/// </remarks>
public readonly struct Angle : IEquatable<Angle>, IComparable<Angle>
{
    private Angle(double radians) => Radians = radians;

    /// <summary>
    /// The angle measured in radians. This is the stored representation, so reading it is
    /// free and lossless.
    /// </summary>
    public double Radians { get; }

    /// <summary>
    /// The angle measured in degrees. Computed from <see cref="Radians"/> on every read, so
    /// it carries one multiplication of rounding error.
    /// </summary>
    public double Degrees => Radians * (180.0 / Math.PI);

    /// <summary>
    /// The zero angle. This is also the value of a default-constructed <see cref="Angle"/>.
    /// </summary>
    public static Angle Zero => new(0.0);

    /// <summary>
    /// A quarter turn: 90 degrees, or pi/2 radians.
    /// </summary>
    public static Angle QuarterTurn => new(Math.PI / 2.0);

    /// <summary>
    /// A half turn: 180 degrees, or pi radians.
    /// </summary>
    public static Angle HalfTurn => new(Math.PI);

    /// <summary>
    /// A full turn: 360 degrees, or 2*pi radians.
    /// </summary>
    public static Angle FullTurn => new(Math.Tau);

    /// <summary>
    /// Creates an angle from a measurement in radians.
    /// </summary>
    /// <param name="radians">
    /// The measurement in radians. Any finite value is accepted, including negative values
    /// and values beyond a full turn. <see cref="double.NaN"/> and the infinities are
    /// accepted and propagate; they are not rejected here because rejecting them would turn
    /// an arithmetic accident into an exception a long way from its cause.
    /// </param>
    /// <returns>An angle of the given size.</returns>
    public static Angle FromRadians(double radians) => new(radians);

    /// <summary>
    /// Creates an angle from a measurement in degrees.
    /// </summary>
    /// <param name="degrees">
    /// The measurement in degrees. Any finite value is accepted, including negative values
    /// and values beyond 360. <see cref="double.NaN"/> and the infinities propagate.
    /// </param>
    /// <returns>An angle of the given size.</returns>
    public static Angle FromDegrees(double degrees) => new(degrees * (Math.PI / 180.0));

    /// <summary>
    /// Returns the equivalent angle in the half-open range <c>[0, 2*pi)</c> radians, that is
    /// <c>[0, 360)</c> degrees.
    /// </summary>
    /// <returns>
    /// The normalised angle. A negative angle wraps upwards, so minus a quarter turn becomes
    /// three quarters of a turn. A whole number of turns normalises to <see cref="Zero"/>.
    /// If this angle is <see cref="double.NaN"/> or infinite the result is
    /// <see cref="double.NaN"/> radians.
    /// </returns>
    public Angle Normalised()
    {
        if (!double.IsFinite(Radians))
        {
            return new Angle(double.NaN);
        }

        double result = Radians % Math.Tau;

        if (result < 0.0)
        {
            result += Math.Tau;
        }

        // Adding a full turn to a tiny negative remainder can round up to exactly a full turn,
        // which is outside the promised half-open range. Snap that one case back to zero.
        if (result >= Math.Tau)
        {
            result = 0.0;
        }

        return new Angle(result);
    }

    /// <summary>
    /// Returns the equivalent angle in the range <c>(-pi, pi]</c> radians, that is
    /// <c>(-180, 180]</c> degrees.
    /// </summary>
    /// <returns>
    /// The signed normalised angle. Exactly half a turn maps to <c>+pi</c>, not <c>-pi</c>.
    /// A whole number of turns maps to <see cref="Zero"/>. If this angle is
    /// <see cref="double.NaN"/> or infinite the result is <see cref="double.NaN"/> radians.
    /// </returns>
    public Angle NormalisedSigned()
    {
        double normalised = Normalised().Radians;

        if (double.IsNaN(normalised))
        {
            return new Angle(double.NaN);
        }

        return new Angle(normalised > Math.PI ? normalised - Math.Tau : normalised);
    }

    /// <summary>
    /// Returns the absolute size of this angle, discarding its sign.
    /// </summary>
    /// <returns>An angle whose <see cref="Radians"/> is the absolute value of this one's.</returns>
    public Angle Abs() => new(Math.Abs(Radians));

    /// <summary>
    /// Tests whether this angle and <paramref name="other"/> describe the same direction,
    /// accounting for wraparound at a full turn.
    /// </summary>
    /// <param name="other">The angle to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use. Only <see cref="Tolerance.Angular"/> is consulted. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the two angles differ by less than the angular tolerance
    /// after both have been normalised to <c>[0, 2*pi)</c>. Because normalisation happens
    /// first, an angle of zero and an angle of one full turn compare equal. Returns
    /// <see langword="false"/> if either angle is <see cref="double.NaN"/> or infinite.
    /// </returns>
    public bool EqualsWithin(Angle other, in Tolerance tolerance = default)
    {
        double a = Normalised().Radians;
        double b = other.Normalised().Radians;

        if (double.IsNaN(a) || double.IsNaN(b))
        {
            return false;
        }

        double difference = Math.Abs(a - b);
        double angular = tolerance.Angular.Radians;

        return difference <= angular || Math.Abs(difference - Math.Tau) <= angular;
    }

    /// <summary>Adds two angles.</summary>
    /// <param name="left">The first angle.</param>
    /// <param name="right">The second angle.</param>
    /// <returns>The sum. No normalisation is applied.</returns>
    public static Angle operator +(Angle left, Angle right) => new(left.Radians + right.Radians);

    /// <summary>Subtracts one angle from another.</summary>
    /// <param name="left">The angle to subtract from.</param>
    /// <param name="right">The angle to subtract.</param>
    /// <returns>The difference. No normalisation is applied, so the result may be negative.</returns>
    public static Angle operator -(Angle left, Angle right) => new(left.Radians - right.Radians);

    /// <summary>Negates an angle, reversing its direction of rotation.</summary>
    /// <param name="value">The angle to negate.</param>
    /// <returns>The negated angle.</returns>
    public static Angle operator -(Angle value) => new(-value.Radians);

    /// <summary>Scales an angle by a factor.</summary>
    /// <param name="angle">The angle to scale.</param>
    /// <param name="factor">The scale factor. Negative factors reverse the direction.</param>
    /// <returns>The scaled angle.</returns>
    public static Angle operator *(Angle angle, double factor) => new(angle.Radians * factor);

    /// <summary>Scales an angle by a factor.</summary>
    /// <param name="factor">The scale factor. Negative factors reverse the direction.</param>
    /// <param name="angle">The angle to scale.</param>
    /// <returns>The scaled angle.</returns>
    public static Angle operator *(double factor, Angle angle) => new(angle.Radians * factor);

    /// <summary>Divides an angle by a divisor.</summary>
    /// <param name="angle">The angle to divide.</param>
    /// <param name="divisor">
    /// The divisor. A divisor of zero yields an infinite or <see cref="double.NaN"/> angle
    /// rather than an exception, matching <see cref="double"/> division.
    /// </param>
    /// <returns>The divided angle.</returns>
    public static Angle operator /(Angle angle, double divisor) => new(angle.Radians / divisor);

    /// <summary>Adds two angles. The named alternate to <c>operator +</c>.</summary>
    /// <param name="left">The first angle.</param>
    /// <param name="right">The second angle.</param>
    /// <returns>The sum.</returns>
    public static Angle Add(Angle left, Angle right) => left + right;

    /// <summary>Subtracts one angle from another. The named alternate to <c>operator -</c>.</summary>
    /// <param name="left">The angle to subtract from.</param>
    /// <param name="right">The angle to subtract.</param>
    /// <returns>The difference.</returns>
    public static Angle Subtract(Angle left, Angle right) => left - right;

    /// <summary>Scales an angle. The named alternate to <c>operator *</c>.</summary>
    /// <param name="angle">The angle to scale.</param>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scaled angle.</returns>
    public static Angle Multiply(Angle angle, double factor) => angle * factor;

    /// <summary>Divides an angle. The named alternate to <c>operator /</c>.</summary>
    /// <param name="angle">The angle to divide.</param>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The divided angle.</returns>
    public static Angle Divide(Angle angle, double divisor) => angle / divisor;

    /// <summary>Negates an angle. The named alternate to unary <c>operator -</c>.</summary>
    /// <param name="value">The angle to negate.</param>
    /// <returns>The negated angle.</returns>
    public static Angle Negate(Angle value) => -value;

    /// <summary>
    /// Compares two angles for exact equality of their stored radian values.
    /// </summary>
    /// <param name="left">The first angle.</param>
    /// <param name="right">The second angle.</param>
    /// <returns>
    /// <see langword="true"/> when the radian values are bit-for-bit comparable under IEEE
    /// equality. This is <b>exact</b>: zero and one full turn are not equal, and an angle
    /// holding <see cref="double.NaN"/> is not equal to itself. Use
    /// <see cref="EqualsWithin(Angle, in Tolerance)"/> for geometric comparison.
    /// </returns>
    public static bool operator ==(Angle left, Angle right) => left.Radians == right.Radians;

    /// <summary>Compares two angles for exact inequality.</summary>
    /// <param name="left">The first angle.</param>
    /// <param name="right">The second angle.</param>
    /// <returns><see langword="true"/> when <c>operator ==</c> would return <see langword="false"/>.</returns>
    public static bool operator !=(Angle left, Angle right) => !(left == right);

    /// <summary>Tests whether one angle is smaller than another.</summary>
    /// <param name="left">The first angle.</param>
    /// <param name="right">The second angle.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is strictly smaller.</returns>
    public static bool operator <(Angle left, Angle right) => left.Radians < right.Radians;

    /// <summary>Tests whether one angle is larger than another.</summary>
    /// <param name="left">The first angle.</param>
    /// <param name="right">The second angle.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is strictly larger.</returns>
    public static bool operator >(Angle left, Angle right) => left.Radians > right.Radians;

    /// <summary>Tests whether one angle is smaller than or equal to another.</summary>
    /// <param name="left">The first angle.</param>
    /// <param name="right">The second angle.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is not larger.</returns>
    public static bool operator <=(Angle left, Angle right) => left.Radians <= right.Radians;

    /// <summary>Tests whether one angle is larger than or equal to another.</summary>
    /// <param name="left">The first angle.</param>
    /// <param name="right">The second angle.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is not smaller.</returns>
    public static bool operator >=(Angle left, Angle right) => left.Radians >= right.Radians;

    /// <summary>
    /// Orders this angle against another by stored radian value, without normalisation.
    /// </summary>
    /// <param name="other">The angle to compare with.</param>
    /// <returns>
    /// A negative number when this angle is smaller, zero when they are equal, and a
    /// positive number when this angle is larger. <see cref="double.NaN"/> sorts before
    /// every other value, as it does for <see cref="double.CompareTo(double)"/>.
    /// </returns>
    public int CompareTo(Angle other) => Radians.CompareTo(other.Radians);

    /// <summary>
    /// Tests exact equality of the stored radian values, treating <see cref="double.NaN"/>
    /// as equal to itself so that angles remain usable as dictionary keys.
    /// </summary>
    /// <param name="other">The angle to compare with.</param>
    /// <returns>
    /// <see langword="true"/> when the radian values are equal under
    /// <see cref="double.Equals(double)"/>.
    /// </returns>
    public bool Equals(Angle other) => Radians.Equals(other.Radians);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Angle other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Radians.GetHashCode();

    /// <summary>
    /// Formats the angle in degrees, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>45°</c>.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Degrees}°");
}
