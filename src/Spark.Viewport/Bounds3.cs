using System;
using System.Numerics;

namespace Spark.Viewport;

/// <summary>
/// An axis-aligned bounding box in world space, in single precision because it exists to size a
/// camera rather than to answer a geometric question. The kernel's own double-precision
/// <c>BoundingBox</c> is the authority anywhere accuracy matters.
/// </summary>
public readonly struct Bounds3 : IEquatable<Bounds3>
{
    private readonly bool _hasValue;

    private Bounds3(Vector3 min, Vector3 max, bool hasValue)
    {
        Min = min;
        Max = max;
        _hasValue = hasValue;
    }

    /// <summary>An empty box. Unioning it with anything yields that thing.</summary>
    public static Bounds3 Empty => new(
        new Vector3(float.PositiveInfinity),
        new Vector3(float.NegativeInfinity),
        hasValue: false);

    /// <summary>The lower corner. Meaningless when <see cref="IsEmpty"/>.</summary>
    public Vector3 Min { get; }

    /// <summary>The upper corner. Meaningless when <see cref="IsEmpty"/>.</summary>
    public Vector3 Max { get; }

    /// <summary>True when no point has been added.</summary>
    public bool IsEmpty => !_hasValue;

    /// <summary>The centre of the box, or the origin when empty.</summary>
    public Vector3 Centre => _hasValue ? (Min + Max) * 0.5f : Vector3.Zero;

    /// <summary>The radius of the sphere enclosing the box, or zero when empty.</summary>
    public float Radius => _hasValue ? (Max - Min).Length() * 0.5f : 0f;

    /// <summary>A box containing exactly one point.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The degenerate box at <paramref name="point"/>.</returns>
    public static Bounds3 FromPoint(Vector3 point) => new(point, point, hasValue: true);

    /// <summary>This box grown to contain a point.</summary>
    /// <param name="point">The point to include.</param>
    /// <returns>The grown box.</returns>
    public Bounds3 Union(Vector3 point) =>
        _hasValue
            ? new Bounds3(Vector3.Min(Min, point), Vector3.Max(Max, point), hasValue: true)
            : FromPoint(point);

    /// <summary>This box grown to contain another box.</summary>
    /// <param name="other">The box to include. An empty box changes nothing.</param>
    /// <returns>The grown box.</returns>
    public Bounds3 Union(Bounds3 other)
    {
        if (other.IsEmpty)
        {
            return this;
        }

        return _hasValue
            ? new Bounds3(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max), hasValue: true)
            : other;
    }

    /// <inheritdoc/>
    public bool Equals(Bounds3 other) =>
        _hasValue == other._hasValue && Min.Equals(other.Min) && Max.Equals(other.Max);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Bounds3 other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_hasValue, Min, Max);

    /// <summary>Value equality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>True when the two boxes are the same.</returns>
    public static bool operator ==(Bounds3 left, Bounds3 right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>True when the two boxes differ.</returns>
    public static bool operator !=(Bounds3 left, Bounds3 right) => !left.Equals(right);
}
