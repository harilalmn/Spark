using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class AngleTests
{
    [Fact]
    public void FromDegreesAndFromRadiansAgreeOnAQuarterTurn()
    {
        Assert.Equal(Math.PI / 2.0, Angle.FromDegrees(90).Radians, 12);
        Assert.Equal(90.0, Angle.FromRadians(Math.PI / 2.0).Degrees, 12);
    }

    [Fact]
    public void ADefaultConstructedAngleIsZero()
    {
        Assert.Equal(Angle.Zero, default(Angle));
        Assert.Equal(0.0, default(Angle).Radians);
    }

    [Fact]
    public void TheNamedTurnsHaveTheExpectedDegreeValues()
    {
        Assert.Equal(0.0, Angle.Zero.Degrees);
        Assert.Equal(90.0, Angle.QuarterTurn.Degrees, 12);
        Assert.Equal(180.0, Angle.HalfTurn.Degrees, 12);
        Assert.Equal(360.0, Angle.FullTurn.Degrees, 12);
    }

    [Fact]
    public void NormalisingWrapsANegativeAngleUpwards()
    {
        Assert.Equal(270.0, Angle.FromDegrees(-90).Normalised().Degrees, 9);
    }

    [Fact]
    public void NormalisingAWholeNumberOfTurnsGivesZero()
    {
        Assert.Equal(0.0, Angle.FromDegrees(720).Normalised().Degrees, 9);
        Assert.Equal(0.0, Angle.FromDegrees(-1080).Normalised().Degrees, 9);
    }

    [Fact]
    public void NormalisingKeepsTheResultBelowAFullTurn()
    {
        Angle justUnderZero = Angle.FromRadians(-1e-18);

        Assert.True(justUnderZero.Normalised().Radians < 2.0 * Math.PI);
        Assert.True(justUnderZero.Normalised().Radians >= 0.0);
    }

    [Fact]
    public void SignedNormalisationMapsThreeQuartersOfATurnToMinusAQuarter()
    {
        Assert.Equal(-90.0, Angle.FromDegrees(270).NormalisedSigned().Degrees, 9);
    }

    [Fact]
    public void SignedNormalisationMapsExactlyHalfATurnToPositivePi()
    {
        Assert.Equal(180.0, Angle.FromDegrees(180).NormalisedSigned().Degrees, 9);
        Assert.Equal(180.0, Angle.FromDegrees(-180).NormalisedSigned().Degrees, 9);
    }

    [Fact]
    public void NormalisingANonFiniteAngleGivesNaN()
    {
        Assert.True(double.IsNaN(Angle.FromRadians(double.NaN).Normalised().Radians));
        Assert.True(double.IsNaN(Angle.FromRadians(double.PositiveInfinity).Normalised().Radians));
        Assert.True(double.IsNaN(Angle.FromRadians(double.NaN).NormalisedSigned().Radians));
    }

    [Fact]
    public void ArithmeticOperatorsDoNotNormalise()
    {
        Angle sum = Angle.FromDegrees(350) + Angle.FromDegrees(20);

        Assert.Equal(370.0, sum.Degrees, 9);
    }

    [Fact]
    public void NegationReversesTheDirectionOfRotation()
    {
        Assert.Equal(-45.0, (-Angle.FromDegrees(45)).Degrees, 9);
    }

    [Fact]
    public void ScalingAndDividingAnAngleBehaveLikeScalarArithmetic()
    {
        Assert.Equal(90.0, (Angle.FromDegrees(45) * 2.0).Degrees, 9);
        Assert.Equal(90.0, (2.0 * Angle.FromDegrees(45)).Degrees, 9);
        Assert.Equal(45.0, (Angle.FromDegrees(90) / 2.0).Degrees, 9);
    }

    [Fact]
    public void ComparisonOperatorsOrderByStoredRadianValue()
    {
        Angle small = Angle.FromDegrees(10);
        Angle alsoSmall = Angle.FromDegrees(10);
        Angle large = Angle.FromDegrees(400);

        Assert.True(small < large);
        Assert.True(large > small);
        Assert.True(small <= alsoSmall);
        Assert.True(small >= alsoSmall);
        Assert.False(small < alsoSmall);
        Assert.Equal(-1, small.CompareTo(large));
    }

    [Fact]
    public void EqualityIsExactAndDoesNotAccountForWraparound()
    {
        Assert.False(Angle.Zero == Angle.FullTurn);
        Assert.True(Angle.Zero != Angle.FullTurn);
    }

    [Fact]
    public void EqualsWithinAccountsForWraparound()
    {
        Assert.True(Angle.Zero.EqualsWithin(Angle.FullTurn));
        Assert.True(Angle.FromDegrees(359.9999).EqualsWithin(Angle.FromDegrees(0.00005)));
        Assert.False(Angle.FromDegrees(0).EqualsWithin(Angle.FromDegrees(1)));
    }

    [Fact]
    public void EqualsWithinIsFalseForANonFiniteAngle()
    {
        Assert.False(Angle.FromRadians(double.NaN).EqualsWithin(Angle.Zero));
        Assert.False(Angle.Zero.EqualsWithin(Angle.FromRadians(double.NaN)));
    }

    [Fact]
    public void AbsDiscardsTheSign()
    {
        Assert.Equal(45.0, Angle.FromDegrees(-45).Abs().Degrees, 9);
    }

    [Fact]
    public void EqualsTreatsNaNAsEqualToItselfSoAnglesCanBeDictionaryKeys()
    {
        Angle notANumber = Angle.FromRadians(double.NaN);

        Assert.True(notANumber.Equals(Angle.FromRadians(double.NaN)));
        Assert.False(notANumber == Angle.FromRadians(double.NaN));
    }

    [Fact]
    public void EqualAnglesShareAHashCode()
    {
        Assert.Equal(Angle.FromDegrees(30).GetHashCode(), Angle.FromDegrees(30).GetHashCode());
    }

    [Fact]
    public void ToStringReportsDegreesUsingTheInvariantCulture()
    {
        Assert.Equal("0°", Angle.Zero.ToString());
        Assert.DoesNotContain(",", Angle.FromRadians(0.5).ToString(), StringComparison.Ordinal);
        Assert.EndsWith("°", Angle.FromRadians(0.5).ToString(), StringComparison.Ordinal);
    }
}
