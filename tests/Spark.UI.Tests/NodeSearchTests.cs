using System;
using System.Collections.Generic;
using System.Linq;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// The library search's ranking rules.
/// </summary>
/// <remarks>
/// The order is from the plan — exact, prefix, camel-hump, substring, category, description — and
/// these tests are what keep it from decaying into "contains", which is what it was before and
/// which cannot find <c>Circle.ByCentreRadius</c> from <c>cbcr</c>.
/// </remarks>
public sealed class NodeSearchTests
{
    /// <summary>Typing the capitals finds the node. This is the feature.</summary>
    [Fact]
    public void TheCapitalsFindTheNode()
    {
        NodeSearchResult result = NodeSearch.Score("Circle.ByCentreRadius", "Geometry", null, "cbcr");

        Assert.Equal(NodeMatch.CamelHump, result.Kind);
        Assert.Equal(0, result.Distance);
    }

    /// <summary>A partial run of capitals matches, and ranks behind a complete one.</summary>
    [Fact]
    public void APartialRunOfCapitalsRanksBehindACompleteOne()
    {
        NodeSearchResult whole = NodeSearch.Score("Circle.ByCentreRadius", "Geometry", null, "cbcr");
        NodeSearchResult partial = NodeSearch.Score("Circle.ByCentreRadius", "Geometry", null, "cbc");

        Assert.Equal(NodeMatch.CamelHump, partial.Kind);
        Assert.True(partial.Distance > whole.Distance);
    }

    /// <summary>The capitals of a name, which is what a camel-hump query is matched against.</summary>
    [Theory]
    [InlineData("Circle.ByCentreRadius", "CBCR")]
    [InlineData("Point.ByCoordinates", "PBC")]
    [InlineData("Math.Sin", "MS")]
    [InlineData("Point2d", "P2")]
    public void HumpsAreTheCapitalsAndTheDigits(string name, string expected) =>
        Assert.Equal(expected, NodeSearch.Humps(name));

    /// <summary>Exact beats prefix beats camel-hump beats substring beats category.</summary>
    [Fact]
    public void TheRanksAreOrderedAsSpecified()
    {
        Assert.Equal(NodeMatch.Exact, NodeSearch.Score("Math.Sin", "Math", null, "Math.Sin").Kind);
        Assert.Equal(NodeMatch.Exact, NodeSearch.Score("Math.Sin", "Math", null, "sin").Kind);
        Assert.Equal(NodeMatch.Prefix, NodeSearch.Score("Circle.ByCentreRadius", "Geometry", null, "circ").Kind);
        Assert.Equal(NodeMatch.CamelHump, NodeSearch.Score("Circle.ByCentreRadius", "Geometry", null, "cbc").Kind);
        Assert.Equal(NodeMatch.Substring, NodeSearch.Score("Arc.ByPlaneRadiusAngles", "Geometry", null, "radiusa").Kind);
        Assert.Equal(NodeMatch.Category, NodeSearch.Score("Math.Sin", "Maths and logic", null, "logic").Kind);
        Assert.Equal(
            NodeMatch.Description,
            NodeSearch.Score("Math.Sin", "Math", "The sine of an angle in degrees.", "angle").Kind);
    }

    /// <summary>The part after the dot is searchable on its own, and ranks just behind the whole name.</summary>
    [Fact]
    public void TheMemberNameIsSearchableAndRanksBehindTheWholeName()
    {
        NodeSearchResult whole = NodeSearch.Score("Circle.ByCentreRadius", "Geometry", null, "circle");
        NodeSearchResult member = NodeSearch.Score("Arc.ByCircleAndPoint", "Geometry", null, "bycircle");

        Assert.Equal(NodeMatch.Prefix, whole.Kind);
        Assert.Equal(NodeMatch.Prefix, member.Kind);
        Assert.True(member.Distance > whole.Distance);
    }

    /// <summary>Nothing matching is reported as nothing, not as a weak match.</summary>
    [Fact]
    public void ANodeThatDoesNotMatchIsNotAResult()
    {
        NodeSearchResult result = NodeSearch.Score("Math.Sin", "Math", "The sine of an angle.", "zzz");

        Assert.False(result.IsMatch);
        Assert.Equal(NodeMatch.None, result.Kind);
    }

    /// <summary>An empty query is no search rather than a failed one.</summary>
    [Fact]
    public void AnEmptyQueryMatchesEverything()
    {
        Assert.True(NodeSearch.Score("Math.Sin", "Math", null, string.Empty).IsMatch);
        Assert.True(NodeSearch.Score("Math.Sin", "Math", null, "   ").IsMatch);
        Assert.True(NodeSearch.Score("Math.Sin", "Math", null, null).IsMatch);
    }

    /// <summary>
    /// A query with a separator in it is a name being typed, not capitals being typed.
    /// </summary>
    /// <remarks>
    /// Without this, <c>c.b</c> would camel-hump-match nothing sensible and quietly outrank the
    /// substring matches that are what the user actually meant.
    /// </remarks>
    [Fact]
    public void AQueryWithASeparatorIsNotACamelHumpQuery()
    {
        NodeSearchResult result = NodeSearch.Score("Circle.ByCentreRadius", "Geometry", null, "cle.by");

        Assert.Equal(NodeMatch.Substring, result.Kind);
    }

    /// <summary>
    /// The shorter of two equally good matches wins, and the order is total.
    /// </summary>
    /// <remarks>
    /// The length rule is what puts <c>Circle.ByCentreRadius</c> above
    /// <c>Circle.ByCentreNormalRadius</c> for <c>circle</c>. Totality matters just as much: a
    /// result list that reshuffles between keystrokes cannot be clicked.
    /// </remarks>
    [Fact]
    public void TheShorterOfTwoEqualMatchesComesFirst()
    {
        string[] names = ["Circle.ByCentreNormalRadius", "Circle.ByCentreRadius", "Circle.ByPlaneRadius"];

        List<string> ordered = names
            .OrderBy(name => name, Comparer<string>.Create((left, right) => NodeSearch.Compare(
                NodeSearch.Score(left, "Geometry", null, "circle"),
                left,
                NodeSearch.Score(right, "Geometry", null, "circle"),
                right)))
            .ToList();

        Assert.Equal("Circle.ByPlaneRadius", ordered[0]);
        Assert.Equal("Circle.ByCentreRadius", ordered[1]);
        Assert.Equal("Circle.ByCentreNormalRadius", ordered[2]);
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => NodeSearch.Score(null!, null, null, "x"));
        Assert.Throws<ArgumentNullException>(() => NodeSearch.Humps(null!));
    }
}
