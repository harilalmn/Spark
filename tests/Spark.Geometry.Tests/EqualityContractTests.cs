using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The two cases where <c>Equals</c> and <c>operator ==</c> are documented to disagree, and
/// where <c>GetHashCode</c> has to keep step with <c>Equals</c> rather than with the operator.
/// </summary>
/// <remarks>
/// <para>
/// <b>NaN</b> is the stated reason the two differ at all: <c>operator ==</c> follows IEEE, so
/// a value carrying a <see cref="double.NaN"/> is not equal to itself, while <c>Equals</c>
/// follows <see cref="double.Equals(double)"/> so that such values remain usable as dictionary
/// keys. <b>Negative zero</b> is the mirror case: <c>-0.0 == 0.0</c> is true and
/// <c>(-0.0).Equals(0.0)</c> is also true, and the hash must agree.
/// </para>
/// <para>
/// Asserting that a value's hash equals its own hash — which is all the earlier tests here
/// did — cannot fail and is not coverage. These are the assertions that can.
/// </para>
/// </remarks>
public sealed class EqualityContractTests
{
    [Fact]
    public void EveryValueTypeTreatsNaNAsEqualToItselfUnderEqualsButNotUnderTheOperator()
    {
        AssertEqualsAndHashAgree(Angle.FromRadians(double.NaN), Angle.FromRadians(double.NaN));
        AssertEqualsAndHashAgree(NaNVector3d, NaNVector3d);
        AssertEqualsAndHashAgree(Point3d.Unset, Point3d.Unset);
        AssertEqualsAndHashAgree(new Vector2d(double.NaN, double.NaN), new Vector2d(double.NaN, double.NaN));
        AssertEqualsAndHashAgree(Point2d.Unset, Point2d.Unset);
        AssertEqualsAndHashAgree(UV.Unset, UV.Unset);
        AssertEqualsAndHashAgree(NaNInterval, NaNInterval);
        AssertEqualsAndHashAgree(NaNBox, NaNBox);
        AssertEqualsAndHashAgree(NaNTransform, NaNTransform);

        Assert.False(Angle.FromRadians(double.NaN) == Angle.FromRadians(double.NaN));
        Assert.False(NaNVector3d == NaNVector3d);
        Assert.False(Point3d.Unset == Point3d.Unset);
        Assert.False(Point2d.Unset == Point2d.Unset);
        Assert.False(UV.Unset == UV.Unset);
        Assert.False(NaNInterval == NaNInterval);
        Assert.False(NaNBox == NaNBox);
        Assert.False(NaNTransform == NaNTransform);
    }

    [Fact]
    public void DifferentNaNBitPatternsStillHashAndCompareTheSame()
    {
        // A quiet NaN produced by 0/0 does not share a bit pattern with double.NaN on every
        // runtime. Equals and GetHashCode must not notice.
        double computed = 0.0 / 0.0;

        AssertEqualsAndHashAgree(
            new Point3d(computed, computed, computed),
            new Point3d(double.NaN, double.NaN, double.NaN));
    }

    [Fact]
    public void NegativeZeroComparesEqualAndHashesTheSameAsPositiveZero()
    {
        AssertEqualsAndHashAgree(Angle.FromRadians(-0.0), Angle.Zero);
        AssertEqualsAndHashAgree(new Vector3d(-0.0, -0.0, -0.0), Vector3d.Zero);
        AssertEqualsAndHashAgree(new Point3d(-0.0, -0.0, -0.0), Point3d.Origin);
        AssertEqualsAndHashAgree(new Vector2d(-0.0, -0.0), Vector2d.Zero);
        AssertEqualsAndHashAgree(new Point2d(-0.0, -0.0), Point2d.Origin);
        AssertEqualsAndHashAgree(new UV(-0.0, -0.0), UV.Zero);
        AssertEqualsAndHashAgree(new Interval(-0.0, -0.0), new Interval(0.0, 0.0));

        // Here the operator agrees with Equals, because IEEE says -0.0 == 0.0.
        Assert.True(new Point3d(-0.0, -0.0, -0.0) == Point3d.Origin);
        Assert.True(new Vector3d(-0.0, -0.0, -0.0) == Vector3d.Zero);
    }

    [Fact]
    public void DistinctlyConstructedButEqualValuesShareAHashCode()
    {
        AssertEqualsAndHashAgree(Angle.FromDegrees(90), Angle.FromRadians(Math.PI / 2.0) * 1.0);
        AssertEqualsAndHashAgree(Tolerance.Default, default);
        AssertEqualsAndHashAgree(new Vector3d(1.0, 2.0, 3.0), new Vector3d(0.5, 1.0, 1.5) * 2.0);
        AssertEqualsAndHashAgree(Point3d.Origin + new Vector3d(1.0, 0.0, 0.0), new Point3d(1.0, 0.0, 0.0));
        AssertEqualsAndHashAgree(Plane.WorldXY, Plane.ByOriginNormal(Point3d.Origin, Vector3d.ZAxis));
        AssertEqualsAndHashAgree(CoordinateSystem.Identity, CoordinateSystem.ByOrigin(Point3d.Origin));
        AssertEqualsAndHashAgree(Transform.Identity, Transform.Scale(1.0));
        AssertEqualsAndHashAgree(BoundingBox.Empty, BoundingBox.FromPoints(Array.Empty<Point3d>()));
    }

    [Fact]
    public void AToleranceComparesByItsResolvedComponentsSoTheZeroSentinelIsInvisible()
    {
        Tolerance sentinel = new(0.0, Angle.FromDegrees(45), 0.5);

        // Linear == 0 means "use the default", so every component resolves to the default and
        // the angular and relative values passed alongside it are discarded, not remembered.
        Assert.Equal(Tolerance.Default, sentinel);
        Assert.Equal(Tolerance.Default.GetHashCode(), sentinel.GetHashCode());
    }

    private static Vector3d NaNVector3d => new(double.NaN, double.NaN, double.NaN);

    private static Interval NaNInterval => new(double.NaN, double.NaN);

    private static BoundingBox NaNBox => new(Point3d.Unset, Point3d.Unset);

    private static Transform NaNTransform => new(
        double.NaN, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0);

    private static void AssertEqualsAndHashAgree<T>(T first, T second)
        where T : IEquatable<T>
    {
        Assert.True(first.Equals(second), $"{first} should equal {second}.");
        Assert.True(second.Equals(first), $"{second} should equal {first}.");
        Assert.True(first.Equals((object?)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
