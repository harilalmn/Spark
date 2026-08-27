using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class ToleranceTests
{
    [Fact]
    public void ADefaultConstructedToleranceBehavesAsTheDefault()
    {
        Tolerance implicitDefault = default;

        Assert.Equal(Tolerance.Default, implicitDefault);
        Assert.Equal(1e-6, implicitDefault.Linear);
        Assert.Equal(1e-12, implicitDefault.RelativeEpsilon);
        Assert.Equal(0.001, implicitDefault.Angular.Degrees, 12);
    }

    [Fact]
    public void ReadingLinearNeverReturnsTheZeroSentinel()
    {
        Assert.NotEqual(0.0, default(Tolerance).Linear);
        Assert.NotEqual(0.0, new Tolerance(0.0, Angle.Zero, 0.0).Linear);
    }

    [Fact]
    public void AnExplicitToleranceKeepsItsComponents()
    {
        Tolerance tolerance = new(1e-3, Angle.FromDegrees(0.5), 1e-9);

        Assert.Equal(1e-3, tolerance.Linear);
        Assert.Equal(0.5, tolerance.Angular.Degrees, 12);
        Assert.Equal(1e-9, tolerance.RelativeEpsilon);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ConstructingWithANegativeOrNonFiniteLinearComponentThrows(double linear)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Tolerance(linear, Angle.Zero, 1e-12));
    }

    [Fact]
    public void ConstructingWithANegativeRelativeEpsilonThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tolerance(1e-6, Angle.Zero, -1.0));
    }

    [Fact]
    public void ConstructingWithANegativeAngularComponentThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Tolerance(1e-6, Angle.FromDegrees(-1), 1e-12));
    }

    [Fact]
    public void ForScaleGrowsTheLinearToleranceInProportionToTheModel()
    {
        Assert.Equal(1e-3, Tolerance.ForScale(1000.0).Linear, 15);
        Assert.Equal(1e-9, Tolerance.ForScale(0.001).Linear, 15);
    }

    [Fact]
    public void ForScaleIgnoresTheSignOfTheCharacteristicLength()
    {
        Assert.Equal(Tolerance.ForScale(250.0), Tolerance.ForScale(-250.0));
    }

    [Fact]
    public void ForScaleAtUnitLengthGivesTheDefault()
    {
        Assert.Equal(Tolerance.Default, Tolerance.ForScale(1.0));
    }

    [Fact]
    public void ForScaleOfZeroGivesTheDefaultRatherThanACollapsedTolerance()
    {
        Assert.Equal(Tolerance.Default, Tolerance.ForScale(0.0));
    }

    [Fact]
    public void ForScaleLeavesTheDimensionlessComponentsAlone()
    {
        Tolerance scaled = Tolerance.ForScale(1e6);

        Assert.Equal(Tolerance.Default.Angular, scaled.Angular);
        Assert.Equal(Tolerance.Default.RelativeEpsilon, scaled.RelativeEpsilon);
    }

    [Fact]
    public void ForScaleRejectsANonFiniteCharacteristicLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tolerance.ForScale(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Tolerance.ForScale(double.PositiveInfinity));
    }

    [Fact]
    public void ScaledMultipliesOnlyTheLinearComponent()
    {
        Tolerance scaled = Tolerance.Default.Scaled(1000.0);

        Assert.Equal(1e-3, scaled.Linear, 15);
        Assert.Equal(Tolerance.Default.Angular, scaled.Angular);
        Assert.Equal(Tolerance.Default.RelativeEpsilon, scaled.RelativeEpsilon);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(double.NaN)]
    public void ScaledRejectsANonPositiveOrNonFiniteFactor(double factor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tolerance.Default.Scaled(factor));
    }

    [Fact]
    public void AreEqualUsesTheAbsoluteToleranceAtOrdinaryMagnitudes()
    {
        Tolerance tolerance = Tolerance.Default;

        Assert.True(tolerance.AreEqual(1.0, 1.0 + 1e-9));
        Assert.False(tolerance.AreEqual(1.0, 1.0 + 1e-3));
    }

    [Fact]
    public void AreEqualUsesTheRelativeEpsilonAtLargeMagnitudes()
    {
        Tolerance tolerance = Tolerance.Default;

        // At 1e9 the absolute tolerance of 1e-6 is below what a double can resolve, so the
        // relative term has to take over or nothing would ever compare equal up here.
        Assert.True(tolerance.AreEqual(1e9, 1e9 + 1e-4));
        Assert.False(tolerance.AreEqual(1e9, 1e9 + 1.0));
    }

    [Fact]
    public void AreEqualIsFalseForNaN()
    {
        Assert.False(Tolerance.Default.AreEqual(double.NaN, double.NaN));
        Assert.False(Tolerance.Default.AreEqual(1.0, double.NaN));
    }

    [Fact]
    public void AreEqualIsTrueForTwoInfinitiesOfTheSameSign()
    {
        Assert.True(Tolerance.Default.AreEqual(double.PositiveInfinity, double.PositiveInfinity));
        Assert.False(Tolerance.Default.AreEqual(double.PositiveInfinity, double.NegativeInfinity));
    }

    [Fact]
    public void ExactlyOneOfLessThanEqualAndGreaterThanHolds()
    {
        Tolerance tolerance = Tolerance.Default;

        AssertTrichotomy(tolerance, 1.0, 2.0);
        AssertTrichotomy(tolerance, 2.0, 1.0);
        AssertTrichotomy(tolerance, 1.0, 1.0 + 1e-9);
        AssertTrichotomy(tolerance, -5.0, -5.0);
        AssertTrichotomy(tolerance, 1e9, 1e9 + 1e-4);
    }

    [Fact]
    public void TheTrichotomyHoldsForPairsSittingExactlyOnTheThreshold()
    {
        // These are the pairs the old implementation got wrong, and they are not exotic.
        // It compared `a` against `b - threshold` while AreEqual compared `a - b` against the
        // threshold; the two subtractions round differently by one ulp, so (2, 2.000001) fell
        // into no bucket and (1e-30, -1e-6) fell into two.
        AssertTrichotomy(Tolerance.Default, 2.0, 2.000001);
        AssertTrichotomy(Tolerance.Default, 2.000001, 2.0);
        AssertTrichotomy(Tolerance.Default, 1e-30, -1e-6);
        AssertTrichotomy(Tolerance.Default, -1e-6, 1e-30);
    }

    [Fact]
    public void TheTrichotomyHoldsAcrossASweepOfTheThresholdBoundary()
    {
        // A sweep of b = a plus or minus exactly one threshold, which is where the two
        // roundings used to disagree. The old implementation failed several hundred of these.
        Tolerance tolerance = Tolerance.Default;

        for (int step = -60; step <= 60; step++)
        {
            double a = step * 1.7;

            foreach (double multiple in new[] { -1.0, -0.5, 0.0, 0.5, 1.0 })
            {
                AssertTrichotomy(tolerance, a, a + (multiple * 1e-6));
                AssertTrichotomy(tolerance, a + (multiple * 1e-6), a);
            }
        }
    }

    [Fact]
    public void TheTrichotomyHoldsAtExtremeMagnitudesWhereTheRelativeTermTakesOver()
    {
        Tolerance tolerance = Tolerance.Default;

        foreach (double scale in new[] { 1e-9, 1e-3, 1.0, 1e3, 1e9, 1e12 })
        {
            double threshold = Math.Max(1e-6, 1e-12 * scale);

            AssertTrichotomy(tolerance, scale, scale + threshold);
            AssertTrichotomy(tolerance, scale, scale - threshold);
            AssertTrichotomy(tolerance, -scale, -scale + threshold);
        }
    }

    [Fact]
    public void IsNegligibleAppliesTheSameHybridRuleAsAreEqual()
    {
        Tolerance tolerance = Tolerance.Default;

        Assert.True(tolerance.IsNegligible(1e-9, 1.0));
        Assert.False(tolerance.IsNegligible(1e-3, 1.0));

        // At a scale of 1e12 the absolute 1e-6 is below what a double can resolve, so the
        // relative term has to widen the comparison or the test degenerates to bit-equality.
        Assert.True(tolerance.IsNegligible(0.5, 1e12));
        Assert.False(tolerance.IsNegligible(500.0, 1e12));

        Assert.False(tolerance.IsNegligible(double.NaN, 1.0));
        Assert.True(tolerance.IsNegligible(1e-9, double.PositiveInfinity));
    }

    [Fact]
    public void ComparisonsAgainstInfinityFallBackToTheAbsoluteTolerance()
    {
        Assert.True(Tolerance.Default.IsLessThan(0.0, double.PositiveInfinity));
        Assert.True(Tolerance.Default.IsGreaterThan(0.0, double.NegativeInfinity));
    }

    [Fact]
    public void IsZeroUsesTheAbsoluteToleranceOnly()
    {
        Assert.True(Tolerance.Default.IsZero(1e-9));
        Assert.True(Tolerance.Default.IsZero(-1e-9));
        Assert.False(Tolerance.Default.IsZero(1e-3));
        Assert.False(Tolerance.Default.IsZero(double.NaN));
    }

    [Fact]
    public void AValueInsideTheToleranceBandIsNeitherPositiveNorNegative()
    {
        Tolerance tolerance = Tolerance.Default;

        Assert.False(tolerance.IsPositive(1e-9));
        Assert.False(tolerance.IsNegative(-1e-9));
        Assert.True(tolerance.IsPositive(1e-3));
        Assert.True(tolerance.IsNegative(-1e-3));
    }

    [Fact]
    public void EqualTolerancesShareAHashCode()
    {
        Assert.Equal(Tolerance.Default.GetHashCode(), default(Tolerance).GetHashCode());
    }

    [Fact]
    public void InequalityOperatorDistinguishesDifferentTolerances()
    {
        Assert.True(Tolerance.Default != Tolerance.ForScale(1000.0));
        Assert.True(Tolerance.Default == default);
    }

    private static void AssertTrichotomy(in Tolerance tolerance, double a, double b)
    {
        int trueCount = 0;

        if (tolerance.IsLessThan(a, b))
        {
            trueCount++;
        }

        if (tolerance.AreEqual(a, b))
        {
            trueCount++;
        }

        if (tolerance.IsGreaterThan(a, b))
        {
            trueCount++;
        }

        Assert.Equal(1, trueCount);
    }
}
