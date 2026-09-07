using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Nodes.Core;

namespace Spark.Engine.Tests;

/// <summary>
/// The two count-driven ranges — Dynamo's <c>start..end..#count</c> and
/// <c>start..#count..step</c> (`E10-T15`).
/// </summary>
/// <remarks>
/// <para>
/// <b>These are the forms `E10-T15` recorded as having no node.</b> That row documented the
/// arithmetic in <c>concepts/lists.md</c> and left users a LINQ incantation to copy; these are the
/// methods that make the incantation unnecessary, and the code-block sugar lowers onto them.
/// </para>
/// <para>
/// <b><see cref="TheEndsAreExact"/> is the load-bearing one</b>, and it is here because the
/// obvious implementation gets it wrong in a way that reading the code does not reveal:
/// <c>start + (index * span / (count - 1))</c> at the last index is arithmetically the bound and in
/// binary floating point is frequently the double next door.
/// </para>
/// </remarks>
public sealed class NumberRangeTests
{
    /// <summary>
    /// <b>Both bounds come out exactly as typed</b>, which is the promise the summary makes and
    /// the one floating point does not give away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>These two pairs were found by search, not chosen by eye, and that is the point.</b> The
    /// first version of this test used <c>0.1</c> to <c>0.7</c> and passed against the naive
    /// implementation as well as the right one — the arithmetic happens to land exactly there, so
    /// the test asserted a true thing while proving nothing. Two hundred thousand candidate triples
    /// were scanned for ones where <c>start + gap * (count - 1)</c> is not <c>end</c>; descending
    /// <c>0.7 → 0.1</c> is one at every count, and it yields <c>0.09999999999999998</c>.
    /// </para>
    /// <para>
    /// Reverting <c>values[count - 1] = end</c> to the computed form turns this red, which is the
    /// only reason to believe it is testing anything.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEndsAreExact()
    {
        IReadOnlyList<double> descending = Number.RangeByCount(0.7, 0.1, 7);

        Assert.Equal(7, descending.Count);

        // Not a tolerance: the claim is bit-for-bit equality with the argument. The naive
        // computation gives 0.09999999999999998 here, which reads as 0.1 in every UI.
        Assert.True(descending[0] == 0.7, "The first value must be the bound itself, not near it.");
        Assert.True(descending[^1] == 0.1, "The last value must be the bound itself, not near it.");

        IReadOnlyList<double> ascending = Number.RangeByCount(0.1, 0.9, 12);

        Assert.True(ascending[^1] == 0.9, "The last value must be the bound itself, not near it.");
    }

    /// <summary>
    /// <b>The divisor is <c>count - 1</c>.</b> Five values across zero to one are quarters, because
    /// five values including both ends have four gaps between them.
    /// </summary>
    /// <remarks>
    /// This is `0..1..#5`, the line the client asked for, and the mistake it guards is dividing by
    /// five — which gives 0, 0.2, 0.4, 0.6, 0.8 and stops short of the bound the user asked for.
    /// </remarks>
    [Fact]
    public void FiveValuesAcrossOneAreQuarters()
    {
        Assert.Equal([0, 0.25, 0.5, 0.75, 1], Number.RangeByCount(0, 1, 5));
    }

    /// <summary>Counting down is just a larger start than end.</summary>
    [Fact]
    public void ADescendingRangeCountsDown()
    {
        Assert.Equal([10, 7.5, 5, 2.5, 0], Number.RangeByCount(10, 0, 5));
    }

    /// <summary>
    /// <b>One value is the start, and none is the empty list.</b> Both would be a division by zero
    /// under the general formula, and neither is a mistake worth stopping a graph for.
    /// </summary>
    [Fact]
    public void OneAndZeroAreAnsweredRatherThanRefused()
    {
        Assert.Equal([3.0], Number.RangeByCount(3, 9, 1));
        Assert.Empty(Number.RangeByCount(3, 9, 0));
        Assert.Equal([3.0], Number.RangeByCountAndStep(3, 1, 0.25));
        Assert.Empty(Number.RangeByCountAndStep(3, 0, 0.25));
    }

    /// <summary>`0..#5..1` — a start, a count and a step, with no end to give.</summary>
    [Fact]
    public void TheStepFormWalksFromTheStart()
    {
        Assert.Equal([0, 1, 2, 3, 4], Number.RangeByCountAndStep(0, 5, 1));
        Assert.Equal([3, 3.25, 3.5, 3.75], Number.RangeByCountAndStep(3, 4, 0.25));
    }

    /// <summary>
    /// <b>A negative step counts downwards</b>, which is the whole point of allowing a sign here
    /// where <see cref="Number.Range(double, double, double)"/> ignores one.
    /// </summary>
    /// <remarks>
    /// `Range` has two bounds and they already say which way to walk, so a sign there could only
    /// contradict them. A count and a step have no end between them, so the sign is the only thing
    /// that can say "downwards".
    /// </remarks>
    [Fact]
    public void ANegativeStepIsTheOnlyWayToCountDown()
    {
        Assert.Equal([10, 8, 6, 4], Number.RangeByCountAndStep(10, 4, -2));
    }

    /// <summary>A count that cannot be honoured is refused, rather than allocated.</summary>
    [Fact]
    public void ImpossibleCountsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Number.RangeByCount(0, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Number.RangeByCount(0, 1, Number.MaximumRangeCount + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Number.RangeByCountAndStep(0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Number.RangeByCountAndStep(0, Number.MaximumRangeCount + 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Number.RangeByCount(double.NaN, 1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Number.RangeByCount(0, double.PositiveInfinity, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Number.RangeByCountAndStep(0, 5, double.NaN));
    }

    /// <summary>
    /// <b>The nodes are facades over one implementation, and the cap is one number.</b> A second
    /// copy of this arithmetic is how a node and a code block come to disagree about what
    /// <c>0..1..#5</c> means.
    /// </summary>
    [Fact]
    public void TheNodesAreFacadesOverTheSharedArithmetic()
    {
        Assert.Equal(NumberRange.MaximumCount, Number.MaximumRangeCount);

        Assert.Equal(NumberRange.ByStep(0, 4, 1), Number.Range(0, 4, 1));
        Assert.Equal(NumberRange.ByCount(0.7, 0.1, 7), Number.RangeByCount(0.7, 0.1, 7));
        Assert.Equal(NumberRange.ByCountAndStep(3, 4, 0.25), Number.RangeByCountAndStep(3, 4, 0.25));
    }

    /// <summary>
    /// <b>The count form agrees with the step form where they overlap</b>, which is what stops the
    /// two from drifting into two different ideas of what a range is.
    /// </summary>
    [Fact]
    public void TheThreeFormsAgreeWhereTheyOverlap()
    {
        Assert.Equal(
            Number.Range(0, 4, 1).ToList(),
            Number.RangeByCount(0, 4, 5).ToList());

        Assert.Equal(
            Number.RangeByCount(0, 4, 5).ToList(),
            Number.RangeByCountAndStep(0, 5, 1).ToList());
    }
}
