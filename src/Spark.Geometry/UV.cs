using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A parameter pair addressing a location on a surface.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UV"/> is deliberately a distinct type from <see cref="Point2d"/> even though
/// both hold two numbers. A <see cref="Point2d"/> is a position in a plane and is measured in
/// the model's length units; a <see cref="UV"/> is a pair of surface parameters whose range
/// is whatever the surface's own parameterisation says it is. Confusing the two is how a
/// parameter ends up being treated as a distance.
/// </para>
/// <para>
/// Surfaces arrive at M5. This type exists now because it belongs to the value layer and
/// because every signature that will later take a surface parameter should take it from the
/// first day rather than take two loose doubles.
/// </para>
/// </remarks>
public readonly struct UV : IEquatable<UV>
{
    /// <summary>
    /// Creates a surface parameter pair.
    /// </summary>
    /// <param name="u">The parameter in the surface's first direction.</param>
    /// <param name="v">The parameter in the surface's second direction.</param>
    public UV(double u, double v)
    {
        U = u;
        V = v;
    }

    /// <summary>The parameter in the surface's first direction.</summary>
    public double U { get; }

    /// <summary>The parameter in the surface's second direction.</summary>
    public double V { get; }

    /// <summary>
    /// The parameter pair <c>(0, 0)</c>. This is also the value of a default-constructed
    /// <see cref="UV"/>. It is not necessarily a corner of any particular surface — that
    /// depends on the surface's own parameter domain.
    /// </summary>
    public static UV Zero => new(0.0, 0.0);

    /// <summary>
    /// A parameter pair whose components are both <see cref="double.NaN"/>, representing the
    /// absence of a parameter. Test for it with <see cref="IsValid"/>, never with <c>==</c>.
    /// </summary>
    public static UV Unset => new(double.NaN, double.NaN);

    /// <summary>
    /// <see langword="true"/> when both parameters are finite.
    /// </summary>
    public bool IsValid => double.IsFinite(U) && double.IsFinite(V);

    /// <summary>
    /// Tests whether this parameter pair and another are equal within a tolerance.
    /// </summary>
    /// <param name="other">The parameter pair to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both components agree within tolerance. This is a
    /// component-wise test rather than a distance test, because the two parameter directions
    /// of a surface are independent and generally not commensurate — a step of 0.1 in
    /// <see cref="U"/> need not cover the same distance on the surface as a step of 0.1 in
    /// <see cref="V"/>.
    /// </returns>
    public bool EqualsWithin(in UV other, in Tolerance tolerance = default) =>
        tolerance.AreEqual(U, other.U) && tolerance.AreEqual(V, other.V);

    /// <summary>Adds two parameter pairs component-wise.</summary>
    /// <param name="left">The first pair.</param>
    /// <param name="right">The second pair.</param>
    /// <returns>The component-wise sum.</returns>
    public static UV operator +(in UV left, in UV right) => new(left.U + right.U, left.V + right.V);

    /// <summary>Subtracts one parameter pair from another component-wise.</summary>
    /// <param name="left">The pair to subtract from.</param>
    /// <param name="right">The pair to subtract.</param>
    /// <returns>The component-wise difference.</returns>
    public static UV operator -(in UV left, in UV right) => new(left.U - right.U, left.V - right.V);

    /// <summary>Scales a parameter pair.</summary>
    /// <param name="value">The pair to scale.</param>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scaled pair.</returns>
    public static UV operator *(in UV value, double factor) => new(value.U * factor, value.V * factor);

    /// <summary>Scales a parameter pair.</summary>
    /// <param name="factor">The scale factor.</param>
    /// <param name="value">The pair to scale.</param>
    /// <returns>The scaled pair.</returns>
    public static UV operator *(double factor, in UV value) => value * factor;

    /// <summary>Adds two parameter pairs. The named alternate to <c>operator +</c>.</summary>
    /// <param name="left">The first pair.</param>
    /// <param name="right">The second pair.</param>
    /// <returns>The component-wise sum.</returns>
    public static UV Add(in UV left, in UV right) => left + right;

    /// <summary>Subtracts one parameter pair from another. The named alternate to <c>operator -</c>.</summary>
    /// <param name="left">The pair to subtract from.</param>
    /// <param name="right">The pair to subtract.</param>
    /// <returns>The component-wise difference.</returns>
    public static UV Subtract(in UV left, in UV right) => left - right;

    /// <summary>Scales a parameter pair. The named alternate to <c>operator *</c>.</summary>
    /// <param name="value">The pair to scale.</param>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scaled pair.</returns>
    public static UV Multiply(in UV value, double factor) => value * factor;

    /// <summary>
    /// Compares two parameter pairs for exact equality, following IEEE rules.
    /// </summary>
    /// <param name="left">The first pair.</param>
    /// <param name="right">The second pair.</param>
    /// <returns>
    /// <see langword="true"/> when both components are equal. Use
    /// <see cref="EqualsWithin(in UV, in Tolerance)"/> for comparison within tolerance.
    /// </returns>
    public static bool operator ==(in UV left, in UV right) => left.U == right.U && left.V == right.V;

    /// <summary>Compares two parameter pairs for exact inequality.</summary>
    /// <param name="left">The first pair.</param>
    /// <param name="right">The second pair.</param>
    /// <returns><see langword="true"/> when <c>operator ==</c> would return <see langword="false"/>.</returns>
    public static bool operator !=(in UV left, in UV right) => !(left == right);

    /// <summary>
    /// Tests exact equality, treating <see cref="double.NaN"/> as equal to itself so that
    /// parameter pairs remain usable as dictionary keys.
    /// </summary>
    /// <param name="other">The parameter pair to compare with.</param>
    /// <returns>
    /// <see langword="true"/> when both components are equal under
    /// <see cref="double.Equals(double)"/>.
    /// </returns>
    public bool Equals(UV other) => U.Equals(other.U) && V.Equals(other.V);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is UV other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(U, V);

    /// <summary>
    /// Formats the parameters, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>(0.5, 0.25)</c>.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({U}, {V})");
}
