using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class Vector3dTests
{
    [Fact]
    public void TheWorldAxesFormARightHandedSet()
    {
        Assert.Equal(Vector3d.ZAxis, Vector3d.XAxis.Cross(Vector3d.YAxis));
        Assert.Equal(Vector3d.XAxis, Vector3d.YAxis.Cross(Vector3d.ZAxis));
        Assert.Equal(Vector3d.YAxis, Vector3d.ZAxis.Cross(Vector3d.XAxis));
    }

    [Fact]
    public void LengthAndLengthSquaredAgree()
    {
        Vector3d vector = new(3.0, 4.0, 12.0);

        Assert.Equal(169.0, vector.LengthSquared, 12);
        Assert.Equal(13.0, vector.Length, 12);
    }

    [Fact]
    public void NormalisingGivesAUnitVectorInTheSameDirection()
    {
        Vector3d unit = new Vector3d(0.0, 0.0, -5.0).Normalised();

        Assert.Equal(new Vector3d(0.0, 0.0, -1.0), unit);
        Assert.True(unit.IsUnit());
    }

    [Fact]
    public void NormalisingAZeroVectorThrowsRatherThanReturningZero()
    {
        Assert.Throws<InvalidOperationException>(() => Vector3d.Zero.Normalised());
    }

    [Fact]
    public void NormalisingSucceedsForVectorsWhoseSquaredLengthWouldOverflow()
    {
        Vector3d huge = new(1e200, 1e200, 0.0);

        Assert.True(huge.TryNormalise(out Vector3d unit));
        Assert.True(unit.IsUnit());
    }

    [Fact]
    public void NormalisingSucceedsForVectorsWhoseSquaredLengthWouldUnderflow()
    {
        Vector3d tiny = new(1e-200, 0.0, 0.0);

        Assert.True(tiny.TryNormalise(out Vector3d unit));
        Assert.Equal(Vector3d.XAxis, unit);
    }

    [Fact]
    public void TryNormaliseReportsFailureAndYieldsZeroForADegenerateVector()
    {
        Assert.False(Vector3d.Zero.TryNormalise(out Vector3d fromZero));
        Assert.Equal(Vector3d.Zero, fromZero);

        Assert.False(new Vector3d(double.NaN, 0.0, 0.0).TryNormalise(out Vector3d fromNaN));
        Assert.Equal(Vector3d.Zero, fromNaN);
    }

    [Fact]
    public void IsZeroUsesTheLinearTolerance()
    {
        Assert.True(new Vector3d(1e-9, 0.0, 0.0).IsZero());
        Assert.False(new Vector3d(1e-3, 0.0, 0.0).IsZero());
    }

    [Fact]
    public void TheDotProductOfPerpendicularVectorsIsZero()
    {
        Assert.Equal(0.0, Vector3d.XAxis.Dot(Vector3d.YAxis));
        Assert.Equal(0.0, Vector3d.Dot(Vector3d.XAxis, Vector3d.YAxis));
    }

    [Fact]
    public void TheCrossProductOfParallelVectorsIsZero()
    {
        Assert.Equal(Vector3d.Zero, Vector3d.XAxis.Cross(Vector3d.XAxis * 3.0));
    }

    [Fact]
    public void TheTripleProductIsThePositiveVolumeForARightHandedSet()
    {
        Assert.Equal(
            1.0,
            Vector3d.TripleProduct(Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis),
            12);

        Assert.Equal(
            -1.0,
            Vector3d.TripleProduct(Vector3d.YAxis, Vector3d.XAxis, Vector3d.ZAxis),
            12);
    }

    [Fact]
    public void TheTripleProductOfCoplanarVectorsIsZero()
    {
        Assert.Equal(
            0.0,
            Vector3d.TripleProduct(Vector3d.XAxis, Vector3d.YAxis, new Vector3d(1.0, 1.0, 0.0)),
            12);
    }

    [Fact]
    public void AngleToIsUnsignedAndRunsFromZeroToHalfATurn()
    {
        Assert.Equal(0.0, Vector3d.XAxis.AngleTo(Vector3d.XAxis).Degrees, 9);
        Assert.Equal(90.0, Vector3d.XAxis.AngleTo(Vector3d.YAxis).Degrees, 9);
        Assert.Equal(90.0, Vector3d.XAxis.AngleTo(-Vector3d.YAxis).Degrees, 9);
        Assert.Equal(180.0, Vector3d.XAxis.AngleTo(-Vector3d.XAxis).Degrees, 9);
    }

    [Fact]
    public void AngleToStaysAccurateForNearlyParallelVectors()
    {
        Vector3d almost = new(1.0, 1e-9, 0.0);

        // The arc-cosine formulation loses nearly every significant figure here; the
        // arc-tangent one this kernel uses does not.
        Assert.Equal(1e-9, Vector3d.XAxis.AngleTo(almost).Radians, 15);
    }

    [Fact]
    public void AngleToAZeroVectorThrows()
    {
        Assert.Throws<InvalidOperationException>(() => Vector3d.XAxis.AngleTo(Vector3d.Zero));
    }

    [Fact]
    public void SignedAngleToIsPositiveForACounterClockwiseTurnAboutTheAxis()
    {
        Assert.Equal(
            90.0,
            Vector3d.XAxis.SignedAngleTo(Vector3d.YAxis, Vector3d.ZAxis).Degrees,
            9);

        Assert.Equal(
            -90.0,
            Vector3d.YAxis.SignedAngleTo(Vector3d.XAxis, Vector3d.ZAxis).Degrees,
            9);
    }

    [Fact]
    public void SignedAngleToOfAntiparallelVectorsIsPositiveHalfATurn()
    {
        Assert.Equal(
            180.0,
            Vector3d.XAxis.SignedAngleTo(-Vector3d.XAxis, Vector3d.ZAxis).Degrees,
            9);
    }

    [Fact]
    public void SignedAngleToKeepsItsSignAtVanishinglySmallScales()
    {
        // The cross product of two vectors around 1e-170 is around 1e-340, which underflows to
        // signed zero. The old implementation took the sign from the cross product of the raw
        // operands, and -0.0 is not less than 0.0, so the flip was skipped and a clockwise
        // turn reported +90 degrees. Normalising before crossing keeps the sign.
        Vector3d x = new(1e-170, 0.0, 0.0);
        Vector3d y = new(0.0, 1e-170, 0.0);

        Assert.Equal(-90.0, y.SignedAngleTo(x, Vector3d.ZAxis).Degrees, 9);
        Assert.Equal(90.0, x.SignedAngleTo(y, Vector3d.ZAxis).Degrees, 9);
    }

    [Theory]
    [InlineData(1e-170)]
    [InlineData(1e-30)]
    [InlineData(1.0)]
    [InlineData(1e30)]
    [InlineData(1e170)]
    public void SignedAngleToGivesTheSameAnswerAtEveryScale(double scale)
    {
        Vector3d from = new(scale, 0.0, 0.0);
        Vector3d to = new(scale * 0.5, scale * -0.5, 0.0);

        Assert.Equal(-45.0, from.SignedAngleTo(to, Vector3d.ZAxis).Degrees, 9);
        Assert.Equal(45.0, from.AngleTo(to).Degrees, 9);
    }

    [Fact]
    public void AngleToGivesTheSameAnswerWhateverTheOperandsLengths()
    {
        Vector3d shortArm = new(1e-120, 1e-120, 0.0);
        Vector3d longArm = new(1e120, 0.0, 0.0);

        Assert.Equal(45.0, shortArm.AngleTo(longArm).Degrees, 9);
    }

    [Fact]
    public void EqualsWithinStaysMeaningfulAtLargeCoordinates()
    {
        // An absolute-only comparison degenerates into bit-equality up here, because 1e-6 is
        // far below the spacing of doubles at 1e12.
        Vector3d far = new(1e12, 0.0, 0.0);

        Assert.True(far.EqualsWithin(new Vector3d(1e12 + 0.5, 0.0, 0.0)));
        Assert.False(far.EqualsWithin(new Vector3d(1e12 + 500.0, 0.0, 0.0)));
        Assert.True(new Vector3d(1e-9, 0.0, 0.0).EqualsWithin(new Vector3d(1e-9, 0.0, 0.0)));
    }

    [Fact]
    public void AntiparallelVectorsCountAsParallel()
    {
        Assert.True(Vector3d.XAxis.IsParallelTo(-Vector3d.XAxis));
        Assert.True(Vector3d.XAxis.IsParallelTo(Vector3d.XAxis * 7.0));
        Assert.False(Vector3d.XAxis.IsParallelTo(Vector3d.YAxis));
    }

    [Fact]
    public void ADegenerateVectorIsParallelToNothingAndPerpendicularToNothing()
    {
        Assert.False(Vector3d.Zero.IsParallelTo(Vector3d.XAxis));
        Assert.False(Vector3d.Zero.IsPerpendicularTo(Vector3d.XAxis));
    }

    [Fact]
    public void PerpendicularityIsDetectedWithinTheAngularTolerance()
    {
        Assert.True(Vector3d.XAxis.IsPerpendicularTo(Vector3d.YAxis));
        Assert.True(Vector3d.XAxis.IsPerpendicularTo(new Vector3d(1e-9, 1.0, 0.0)));
        Assert.False(Vector3d.XAxis.IsPerpendicularTo(new Vector3d(1.0, 1.0, 0.0)));
    }

    [Fact]
    public void ProjectingOntoADirectionSplitsAVectorIntoParallelAndPerpendicularParts()
    {
        Vector3d vector = new(3.0, 4.0, 0.0);
        Vector3d along = vector.ProjectOnto(Vector3d.XAxis);

        Assert.Equal(new Vector3d(3.0, 0.0, 0.0), along);
        Assert.Equal(new Vector3d(0.0, 4.0, 0.0), vector - along);
    }

    [Fact]
    public void ProjectingOntoAZeroDirectionThrows()
    {
        Assert.Throws<InvalidOperationException>(
            () => Vector3d.XAxis.ProjectOnto(Vector3d.Zero));
    }

    [Fact]
    public void ReflectionNegatesTheComponentAlongTheNormalAndKeepsTheRest()
    {
        Vector3d reflected = new Vector3d(1.0, 2.0, 3.0).Reflect(Vector3d.ZAxis);

        Assert.True(reflected.EqualsWithin(new Vector3d(1.0, 2.0, -3.0)));
    }

    [Fact]
    public void ReflectingAVectorLyingInThePlaneLeavesItUnchanged()
    {
        Vector3d inPlane = new(1.0, 2.0, 0.0);

        Assert.True(inPlane.Reflect(Vector3d.ZAxis).EqualsWithin(inPlane));
    }

    [Fact]
    public void RotatingAVectorByAQuarterTurnAboutZCarriesXToY()
    {
        Vector3d rotated = Vector3d.XAxis.Rotate(Vector3d.ZAxis, Angle.FromDegrees(90));

        Assert.True(rotated.EqualsWithin(Vector3d.YAxis));
    }

    [Fact]
    public void RotatingAVectorByAFullTurnReturnsTheOriginalVector()
    {
        Vector3d original = new(1.0, 2.0, 3.0);
        Vector3d rotated = original.Rotate(new Vector3d(1.0, 1.0, 1.0), Angle.FullTurn);

        Assert.True(rotated.EqualsWithin(original));
    }

    [Fact]
    public void RotatingAboutAVectorsOwnAxisLeavesItUnchanged()
    {
        Vector3d axis = new(0.0, 0.0, 4.0);

        Assert.True(axis.Rotate(axis, Angle.FromDegrees(37)).EqualsWithin(axis));
    }

    [Fact]
    public void RotatingAboutAZeroAxisThrows()
    {
        Assert.Throws<InvalidOperationException>(
            () => Vector3d.XAxis.Rotate(Vector3d.Zero, Angle.QuarterTurn));
    }

    [Fact]
    public void EqualityIsExactAndNotFuzzy()
    {
        Vector3d a = new(1.0, 0.0, 0.0);
        Vector3d b = new(1.0 + 1e-12, 0.0, 0.0);

        Assert.False(a == b);
        Assert.True(a.EqualsWithin(b));
    }

    [Fact]
    public void EqualsTreatsNaNAsEqualToItselfSoVectorsCanBeDictionaryKeys()
    {
        Vector3d notANumber = new(double.NaN, 0.0, 0.0);

        Assert.True(notANumber.Equals(new Vector3d(double.NaN, 0.0, 0.0)));
        Assert.False(notANumber == new Vector3d(double.NaN, 0.0, 0.0));
    }

    [Fact]
    public void EqualVectorsShareAHashCode()
    {
        Assert.Equal(new Vector3d(1.0, 2.0, 3.0).GetHashCode(), new Vector3d(1.0, 2.0, 3.0).GetHashCode());
    }

    [Fact]
    public void ArithmeticOperatorsMatchTheirNamedAlternates()
    {
        Vector3d a = new(1.0, 2.0, 3.0);
        Vector3d b = new(4.0, 5.0, 6.0);

        Assert.Equal(a + b, Vector3d.Add(a, b));
        Assert.Equal(a - b, Vector3d.Subtract(a, b));
        Assert.Equal(a * 2.0, Vector3d.Multiply(a, 2.0));
        Assert.Equal(2.0 * a, Vector3d.Multiply(a, 2.0));
        Assert.Equal(a / 2.0, Vector3d.Divide(a, 2.0));
        Assert.Equal(-a, Vector3d.Negate(a));
    }

    [Fact]
    public void ADefaultConstructedVectorIsZero()
    {
        Assert.Equal(Vector3d.Zero, default(Vector3d));
    }

    [Fact]
    public void ToStringUsesTheInvariantCulture()
    {
        Assert.Equal("(1, 0, 0)", Vector3d.XAxis.ToString());
        Assert.DoesNotContain(",5", new Vector3d(0.5, 0.0, 0.0).ToString(), StringComparison.Ordinal);
    }
}
