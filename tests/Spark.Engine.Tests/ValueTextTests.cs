using System;
using System.Linq;
using Spark.Api;

namespace Spark.Engine.Tests;

/// <summary>
/// The one rendering of a value that the canvas, the properties pane and <c>spark run</c> all use.
/// </summary>
public sealed class ValueTextTests
{
    /// <summary>
    /// A single value and an empty list are the two things the shape line exists to tell apart, so
    /// they must never be worded alike.
    /// </summary>
    [Fact]
    public void AScalarSaysOneValueAndAnEmptyListSaysZeroItems()
    {
        Assert.Equal("rank 0 · one value", ValueText.Shape(4.0));
        Assert.Equal("rank 1 · 0 items", ValueText.Shape(SparkList.Empty(1)));
    }

    [Fact]
    public void OneItemIsSingular()
    {
        Assert.Equal("rank 1 · 1 item", ValueText.Shape(SparkList.Of(7.0)));
    }

    /// <summary>
    /// The case the line is for: a list of lists renders almost identically to a flat list and is
    /// a completely different thing under lacing.
    /// </summary>
    [Fact]
    public void ListsOfListsReportRankTwo()
    {
        Assert.Equal("rank 2 · 2 items", ValueText.Shape(SparkList.Of(SparkList.Of(1.0), SparkList.Of(2.0))));
    }

    [Fact]
    public void ANegativeRankOrCountIsAProgrammingError()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ValueText.Shape(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValueText.Shape(1, -1));
    }

    [Fact]
    public void ASummaryIsCutToFitUnderANode()
    {
        SparkList long_ = new([.. Enumerable.Range(0, 500).Select(i => (object?)(double)i)], 1);

        string summary = ValueText.Summary(long_)!;

        Assert.Equal(ValueText.SummaryLength, summary.Length);
        Assert.EndsWith("…", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueThatFitsIsNotCutAtAll()
    {
        Assert.Equal("hello", ValueText.Summary("hello"));
        Assert.Equal("hello", ValueText.Full("hello"));
    }

    [Fact]
    public void NothingRendersAsNothing()
    {
        Assert.Null(ValueText.Summary(null));
        Assert.Equal(string.Empty, ValueText.Full(null));
    }

    /// <summary>
    /// The cut announces itself. A truncation that trails off is one a reader mistakes for the end
    /// of their data, and acting on a value you believe is complete shows up much later, in
    /// geometry.
    /// </summary>
    [Fact]
    public void AnEnormousValueSaysHowMuchIsMissing()
    {
        string enormous = new('x', ValueText.FullLength + 42);

        Assert.Contains("42 more characters not shown", ValueText.Full(enormous), StringComparison.Ordinal);
    }
}
