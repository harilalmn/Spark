using System;
using Spark.Api;

namespace Spark.Engine.Tests;

/// <summary>
/// The rank contract on <see cref="SparkList"/>: O(1), explicit, and checked against contents.
/// </summary>
public sealed class SparkListTests
{
    /// <summary>
    /// Rank comes off the list, not off a walk of the data. Everything else in the engine relies on
    /// this being cheap, because excess is recomputed at every level of every replication.
    /// </summary>
    [Fact]
    public void RankOfAScalarIsZeroAndRankOfAListIsItsStoredRank()
    {
        Assert.Equal(0, SparkList.RankOf(null));
        Assert.Equal(0, SparkList.RankOf(5.0));
        Assert.Equal(0, SparkList.RankOf("a string is a scalar, not a list of characters"));
        Assert.Equal(0, SparkList.RankOf(new double[] { 1, 2, 3 }));
        Assert.Equal(1, SparkList.RankOf(SparkList.Of(1.0, 2.0)));
        Assert.Equal(2, SparkList.RankOf(SparkList.Of(SparkList.Of(1.0))));
    }

    /// <summary>
    /// Decision D9. The rank of a ragged list is the depth of its deepest branch, so
    /// <c>[1, [2, 3]]</c> is rank 2 whichever order the branches happen to come in. Taking the depth
    /// of the first element is the fast answer and is wrong the moment the shallow branch is first.
    /// </summary>
    [Fact]
    public void RaggedRankIsTheDeepestBranchRegardlessOfOrder()
    {
        Assert.Equal(2, SparkList.Of(1.0, SparkList.Of(2.0, 3.0)).Rank);
        Assert.Equal(2, SparkList.Of(SparkList.Of(2.0, 3.0), 1.0).Rank);
    }

    /// <summary>
    /// Decision D8. An empty list carries the rank of the structure that produced it, so a filter
    /// that happens to remove everything produces an empty result rather than a shape change.
    /// </summary>
    [Fact]
    public void AnEmptyListCarriesTheRankItWasGiven()
    {
        Assert.Equal(1, SparkList.Empty(1).Rank);
        Assert.Equal(2, SparkList.Empty(2).Rank);
        Assert.Equal(3, SparkList.Empty(3).Rank);
        Assert.Equal(1, SparkList.Of().Rank);
    }

    /// <summary>
    /// A non-empty list whose stored rank disagrees with its contents would make every replication
    /// decision below it wrong, invisibly. It is refused at construction instead.
    /// </summary>
    [Fact]
    public void ADeclaredRankThatDisagreesWithTheContentsIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new SparkList([1.0, 2.0], 2));
        Assert.Throws<ArgumentException>(() => new SparkList([SparkList.Of(1.0)], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SparkList.Empty(0));
    }

    /// <summary>The list copies what it is given, so a later mutation of the source cannot reshape it.</summary>
    [Fact]
    public void TheListCopiesItsItemsOnConstruction()
    {
        object?[] items = [1.0, 2.0];
        SparkList list = new(items, 1);

        items[0] = 99.0;

        Assert.Equal(1.0, list[0]);
    }

    /// <summary>The rendered form is the bracket notation the specification and the watch panel use.</summary>
    [Fact]
    public void ToStringRendersTheBracketNotationTheSpecificationUses()
    {
        Assert.Equal("[[1, 2], [3, 4]]", SparkList.Of(SparkList.Of(1, 2), SparkList.Of(3, 4)).ToString());
        Assert.Equal("[1, null, 3]", SparkList.Of(1, null, 3).ToString());
        Assert.Equal("[]", SparkList.Empty(2).ToString());
    }
}
