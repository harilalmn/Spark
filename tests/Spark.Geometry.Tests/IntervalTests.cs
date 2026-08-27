using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class IntervalTests
{
    [Fact]
    public void ADefaultConstructedIntervalIsTheSinglePointAtZero()
    {
        Assert.Equal(new Interval(0.0, 0.0), default(Interval));
        Assert.Equal(0.0, default(Interval).Length);
    }

    [Fact]
    public void TheUnitIntervalRunsFromZeroToOne()
    {
        Assert.Equal(0.0, Interval.Unit.Min);
        Assert.Equal(1.0, Interval.Unit.Max);
        Assert.Equal(1.0, Interval.Unit.Length);
        Assert.Equal(0.5, Interval.Unit.Mid);
    }

    [Fact]
    public void ADecreasingIntervalKeepsItsBoundsInTheOrderGiven()
    {
        Interval decreasing = new(10.0, 2.0);

        Assert.True(decreasing.IsDecreasing);
        Assert.Equal(10.0, decreasing.Min);
        Assert.Equal(2.0, decreasing.Max);
        Assert.Equal(-8.0, decreasing.Length);

        // Direction is not validity. A decreasing interval is what a reversed curve's domain
        // looks like, so the obvious `if (!domain.IsValid) throw` must not reject it.
        Assert.True(decreasing.IsValid);
    }

    [Fact]
    public void ValidityIsAboutFinitenessAndNotAboutDirection()
    {
        Assert.True(new Interval(2.0, 10.0).IsValid);
        Assert.True(new Interval(10.0, 2.0).IsValid);
        Assert.False(new Interval(0.0, double.NaN).IsValid);
        Assert.False(new Interval(double.NegativeInfinity, 0.0).IsValid);
    }

    [Fact]
    public void ASinglePointIntervalIsNotDecreasing()
    {
        Assert.False(new Interval(3.0, 3.0).IsDecreasing);
        Assert.True(new Interval(3.0, 3.0).IsValid);
    }

    [Fact]
    public void MakingAnIntervalIncreasingSwapsOnlyWhenItIsDecreasing()
    {
        Assert.Equal(new Interval(2.0, 10.0), new Interval(10.0, 2.0).MakeIncreasing());
        Assert.Equal(new Interval(2.0, 10.0), new Interval(2.0, 10.0).MakeIncreasing());
    }

    [Fact]
    public void ReversingSwapsTheBounds()
    {
        Assert.Equal(new Interval(10.0, 2.0), new Interval(2.0, 10.0).Reversed());
    }

    [Fact]
    public void IncludesIsInclusiveOfBothBounds()
    {
        Interval interval = new(2.0, 10.0);

        Assert.True(interval.Includes(2.0));
        Assert.True(interval.Includes(10.0));
        Assert.True(interval.Includes(6.0));
        Assert.False(interval.Includes(1.0));
        Assert.False(interval.Includes(11.0));
    }

    [Fact]
    public void IncludesIgnoresTheDirectionOfTheInterval()
    {
        Assert.True(new Interval(10.0, 2.0).Includes(6.0));
    }

    [Fact]
    public void IncludesWidensTheBoundsByTheTolerance()
    {
        Interval interval = new(0.0, 1.0);

        Assert.True(interval.Includes(-1e-9));
        Assert.False(interval.Includes(-1e-3));
    }

    [Fact]
    public void ClampBringsAValueInsideTheInterval()
    {
        Interval interval = new(2.0, 10.0);

        Assert.Equal(2.0, interval.Clamp(-5.0));
        Assert.Equal(10.0, interval.Clamp(50.0));
        Assert.Equal(6.0, interval.Clamp(6.0));
        Assert.Equal(6.0, new Interval(10.0, 2.0).Clamp(6.0));
    }

    [Fact]
    public void ClampingNaNGivesNaN()
    {
        Assert.True(double.IsNaN(Interval.Unit.Clamp(double.NaN)));
    }

    [Fact]
    public void NormaliseAndDenormaliseAreInverses()
    {
        Interval interval = new(4.0, 12.0);

        Assert.Equal(0.0, interval.Normalise(4.0), 12);
        Assert.Equal(1.0, interval.Normalise(12.0), 12);
        Assert.Equal(0.5, interval.Normalise(8.0), 12);
        Assert.Equal(8.0, interval.Denormalise(0.5), 12);
        Assert.Equal(7.0, interval.Denormalise(interval.Normalise(7.0)), 12);
    }

    [Fact]
    public void NormaliseExtrapolatesOutsideTheInterval()
    {
        Interval interval = new(0.0, 10.0);

        Assert.Equal(-0.5, interval.Normalise(-5.0), 12);
        Assert.Equal(1.5, interval.Normalise(15.0), 12);
    }

    [Fact]
    public void NormaliseOfADegenerateIntervalGivesZeroRatherThanNaN()
    {
        Assert.Equal(0.0, new Interval(3.0, 3.0).Normalise(3.0));
        Assert.Equal(0.0, new Interval(3.0, 3.0).Normalise(9.0));
    }

    [Fact]
    public void NormaliseOfADecreasingIntervalRunsBackwards()
    {
        Interval decreasing = new(10.0, 0.0);

        Assert.Equal(0.0, decreasing.Normalise(10.0), 12);
        Assert.Equal(1.0, decreasing.Normalise(0.0), 12);
    }

    [Fact]
    public void UnionSpansBothInputsAndIsAlwaysIncreasing()
    {
        Assert.Equal(new Interval(0.0, 10.0), new Interval(0.0, 4.0).Union(new Interval(6.0, 10.0)));
        Assert.Equal(new Interval(0.0, 10.0), new Interval(4.0, 0.0).Union(new Interval(10.0, 6.0)));
    }

    [Fact]
    public void IntersectReturnsTheOverlap()
    {
        Interval? overlap = new Interval(0.0, 10.0).Intersect(new Interval(5.0, 20.0));

        Assert.NotNull(overlap);
        Assert.Equal(new Interval(5.0, 10.0), overlap!.Value);
    }

    [Fact]
    public void IntersectReturnsNullWhenTheIntervalsAreDisjoint()
    {
        Assert.Null(new Interval(0.0, 1.0).Intersect(new Interval(2.0, 3.0)));
    }

    [Fact]
    public void IncludesRejectsNaN()
    {
        // Includes is built from two negated predicates, and every comparison against NaN is
        // false, so an unguarded version answers true here.
        Assert.False(Interval.Unit.Includes(double.NaN));
        Assert.False(new Interval(0.0, double.NaN).Includes(0.5));
        Assert.False(new Interval(double.NaN, 1.0).Includes(0.5));
    }

    [Fact]
    public void IntersectAgreesWithIncludesAboutTheEnds()
    {
        Interval left = new(0.0, 1.0);
        Interval right = new(1.0 + 1e-9, 2.0);

        // Includes says the gap is closed, so Intersect must not say the intervals are
        // disjoint. Before Intersect took a tolerance the two disagreed on exactly this pair.
        Assert.True(left.Includes(1.0 + 1e-9));
        Assert.True(right.Includes(1.0));

        Interval? overlap = left.Intersect(right);

        Assert.NotNull(overlap);
        Assert.True(left.Includes(overlap!.Value.Min));
        Assert.True(right.Includes(overlap.Value.Min));
    }

    [Fact]
    public void IntersectStillReportsARealGapAsDisjoint()
    {
        Assert.Null(new Interval(0.0, 1.0).Intersect(new Interval(1.1, 2.0)));
    }

    [Fact]
    public void IntersectHonoursAnExplicitTolerance()
    {
        Interval left = new(0.0, 1.0);
        Interval right = new(1.5, 2.0);

        Assert.Null(left.Intersect(right));
        Assert.NotNull(left.Intersect(right, Tolerance.Default.Scaled(1e6)));
    }

    [Fact]
    public void IntersectReturnsNullForAnIntervalWithANaNBound()
    {
        Assert.Null(new Interval(0.0, double.NaN).Intersect(Interval.Unit));
        Assert.Null(Interval.Unit.Intersect(new Interval(0.0, double.NaN)));
    }

    [Fact]
    public void RoundTrippingThroughNormaliseIsApproximateNotExact()
    {
        // The doc no longer claims exactness, and this is why. A unit-length interval based
        // at 1e10 sits where adjacent doubles are 1.9e-6 apart, so denormalising a parameter
        // rounds the result to that grid and normalising cannot recover what was lost.
        Interval farFromTheOrigin = new(1e10, 1e10 + 1.0);
        double roundTripped = farFromTheOrigin.Normalise(farFromTheOrigin.Denormalise(0.3));

        Assert.NotEqual(0.3, roundTripped);
        Assert.True(Math.Abs(0.3 - roundTripped) > 1e-7);

        // Still comfortably inside the default tolerance, which is the point of comparing
        // round-tripped values with a Tolerance rather than with ==.
        Assert.True(Tolerance.Default.AreEqual(0.3, roundTripped));
    }

    [Fact]
    public void IntervalsThatTouchAtOnePointIntersectAtThatPoint()
    {
        Interval? overlap = new Interval(0.0, 1.0).Intersect(new Interval(1.0, 2.0));

        Assert.NotNull(overlap);
        Assert.Equal(new Interval(1.0, 1.0), overlap!.Value);
    }

    [Fact]
    public void ExpandGrowsBothEndsAndANegativeAmountShrinks()
    {
        Assert.Equal(new Interval(-1.0, 11.0), new Interval(0.0, 10.0).Expand(1.0));
        Assert.Equal(new Interval(1.0, 9.0), new Interval(0.0, 10.0).Expand(-1.0));
    }

    [Fact]
    public void ShrinkingPastZeroLengthInvertsTheIntervalRatherThanThrowing()
    {
        Interval collapsed = new Interval(0.0, 10.0).Expand(-6.0);

        Assert.True(collapsed.IsDecreasing);
    }

    [Fact]
    public void EqualityIsExactAndDirectionSensitive()
    {
        Assert.False(new Interval(0.0, 1.0) == new Interval(1.0, 0.0));
        Assert.True(new Interval(0.0, 1.0) != new Interval(1.0, 0.0));
        Assert.True(new Interval(0.0, 1.0).EqualsWithin(new Interval(1e-9, 1.0 + 1e-9)));
    }

    [Fact]
    public void EqualIntervalsShareAHashCode()
    {
        Assert.Equal(new Interval(0.0, 1.0).GetHashCode(), new Interval(0.0, 1.0).GetHashCode());
    }

    [Fact]
    public void ToStringUsesTheInvariantCulture()
    {
        Assert.Equal("[0, 1]", Interval.Unit.ToString());
    }
}
