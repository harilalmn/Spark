using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// The library search's ranking rules.
/// </summary>
/// <remarks>
/// The order is from the plan — exact, prefix, camel-hump, substring, category, description — and
/// these tests are what keep it from decaying into "contains", which is what it was before and
/// which cannot find <c>Circle.FromCenterRadius</c> from <c>cfcr</c>.
/// </remarks>
public sealed class NodeSearchTests
{
    /// <summary>Typing the capitals finds the node. This is the feature.</summary>
    [Fact]
    public void TheCapitalsFindTheNode()
    {
        NodeSearchResult result = NodeSearch.Score("Circle.FromCenterRadius", "Geometry", null, "cfcr");

        Assert.Equal(NodeMatch.CamelHump, result.Kind);
        Assert.Equal(0, result.Distance);
    }

    /// <summary>A partial run of capitals matches, and ranks behind a complete one.</summary>
    [Fact]
    public void APartialRunOfCapitalsRanksBehindACompleteOne()
    {
        NodeSearchResult whole = NodeSearch.Score("Circle.FromCenterRadius", "Geometry", null, "cfcr");
        NodeSearchResult partial = NodeSearch.Score("Circle.FromCenterRadius", "Geometry", null, "cfc");

        Assert.Equal(NodeMatch.CamelHump, partial.Kind);
        Assert.True(partial.Distance > whole.Distance);
    }

    /// <summary>The capitals of a name, which is what a camel-hump query is matched against.</summary>
    [Theory]
    [InlineData("Circle.FromCenterRadius", "CFCR")]
    [InlineData("Point.FromCoordinates", "PFC")]
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
        Assert.Equal(NodeMatch.Prefix, NodeSearch.Score("Circle.FromCenterRadius", "Geometry", null, "circ").Kind);
        Assert.Equal(NodeMatch.CamelHump, NodeSearch.Score("Circle.FromCenterRadius", "Geometry", null, "cfc").Kind);
        Assert.Equal(NodeMatch.Substring, NodeSearch.Score("Arc.FromPlaneRadiusAngles", "Geometry", null, "radiusa").Kind);
        Assert.Equal(NodeMatch.Category, NodeSearch.Score("Math.Sin", "Maths and logic", null, "logic").Kind);
        Assert.Equal(
            NodeMatch.Description,
            NodeSearch.Score("Math.Sin", "Math", "The sine of an angle in degrees.", "angle").Kind);
    }

    /// <summary>The part after the dot is searchable on its own, and ranks just behind the whole name.</summary>
    [Fact]
    public void TheMemberNameIsSearchableAndRanksBehindTheWholeName()
    {
        NodeSearchResult whole = NodeSearch.Score("Circle.FromCenterRadius", "Geometry", null, "circle");
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
        NodeSearchResult result = NodeSearch.Score("Circle.FromCenterRadius", "Geometry", null, "cle.fr");

        Assert.Equal(NodeMatch.Substring, result.Kind);
    }

    /// <summary>
    /// Two equally good matches of the same kind are alphabetical, and the order is total.
    /// </summary>
    /// <remarks>
    /// <b>This is the screenful the client photographed.</b> Typing <c>circ</c> matched four
    /// <c>Create</c> nodes equally well and they came out ordered by <i>name length</i> —
    /// <c>FromPlaneRadius</c>, <c>FromThreePoints</c>, <c>FromCenterRadius</c>,
    /// <c>FromCenterNormalRadius</c> — which is a rule nobody reading the list can see. Alphabetical
    /// is the one order a user can predict without being told it. Totality matters just as much: a
    /// result list that reshuffles between keystrokes cannot be clicked.
    /// </remarks>
    [Fact]
    public void EquallyGoodMatchesOfOneKindAreAlphabetical()
    {
        string[] names =
        [
            "Circle.FromPlaneRadius",
            "Circle.FromThreePoints",
            "Circle.FromCenterRadius",
            "Circle.FromCenterNormalRadius",
        ];

        List<string> ordered = Order(names, "circ", NodeMemberKind.Create);

        Assert.Equal(
            [
                "Circle.FromCenterNormalRadius",
                "Circle.FromCenterRadius",
                "Circle.FromPlaneRadius",
                "Circle.FromThreePoints",
            ],
            ordered);
    }

    /// <summary>
    /// Between equal matches, <b>Create</b> comes before <b>Action</b> before <b>Query</b> — and
    /// alphabetical order does not get to cross that boundary.
    /// </summary>
    /// <remarks>
    /// Every name here is a prefix match on <c>circle</c> at the same distance, so relevance has
    /// declared a draw and the kind is the only thing left to decide it. <c>Circle.Area</c> sorts
    /// first alphabetically and last by kind, which is what makes this test able to fail.
    /// </remarks>
    [Fact]
    public void CreateComesBeforeActionComesBeforeQuery()
    {
        NodeSearchCandidate[] candidates =
        [
            Candidate("Circle.Area", "circle", NodeMemberKind.Query),
            Candidate("Circle.Offset", "circle", NodeMemberKind.Action),
            Candidate("Circle.FromThreePoints", "circle", NodeMemberKind.Create),
            Candidate("Circle.Length", "circle", NodeMemberKind.Query),
            Candidate("Circle.FromCenterRadius", "circle", NodeMemberKind.Create),
        ];

        List<string> ordered = candidates
            .OrderBy(candidate => candidate, Comparer<NodeSearchCandidate>.Create(NodeSearch.Compare))
            .Select(candidate => candidate.DisplayName)
            .ToList();

        Assert.Equal(
            [
                "Circle.FromCenterRadius",
                "Circle.FromThreePoints",
                "Circle.Offset",
                "Circle.Area",
                "Circle.Length",
            ],
            ordered);
    }

    /// <summary>
    /// The kind is a tie-break and never outranks relevance: a <c>Query</c> whose name <i>is</i>
    /// the query beats a <c>Create</c> that only mentions it in its description.
    /// </summary>
    /// <remarks>
    /// This is the whole of the design decision, and it is the one a test has to hold. Sorting by
    /// Create/Action/Query <i>above</i> the match strength would read tidily and would bury the
    /// node the user actually named, which is the failure this class exists to prevent.
    /// </remarks>
    [Fact]
    public void KindNeverOutranksTheStrengthOfTheMatch()
    {
        NodeSearchCandidate named = new(
            NodeSearch.Score("Circle.Radius", "Curve", null, "radius"),
            "Circle.Radius",
            NodeMemberKind.Query);

        NodeSearchCandidate mentioned = new(
            NodeSearch.Score("Cone.ByHeight", "Solid", "Swept about an axis at a radius.", "radius"),
            "Cone.ByHeight",
            NodeMemberKind.Create);

        Assert.Equal(NodeMatch.Exact, named.Result.Kind);
        Assert.Equal(NodeMatch.Description, mentioned.Result.Kind);
        Assert.True(NodeSearch.Compare(named, mentioned) < 0);
        Assert.True(NodeSearch.Compare(mentioned, named) > 0);
    }

    /// <summary>
    /// An <see cref="NodeMemberKind.Auto"/> that escapes the importer sorts last rather than
    /// ahead of <c>Create</c>, which its enum value would otherwise give it.
    /// </summary>
    [Fact]
    public void AnUnresolvedKindSortsLastRatherThanFirst()
    {
        NodeSearchCandidate unresolved = Candidate("Circle.Aaa", "circle", NodeMemberKind.Auto);
        NodeSearchCandidate query = Candidate("Circle.Zzz", "circle", NodeMemberKind.Query);

        Assert.True(NodeSearch.Compare(unresolved, query) > 0);
    }

    private static NodeSearchCandidate Candidate(string name, string query, NodeMemberKind kind) =>
        new(NodeSearch.Score(name, "Geometry", null, query), name, kind);

    private static List<string> Order(IEnumerable<string> names, string query, NodeMemberKind kind) =>
        [.. names
            .Select(name => Candidate(name, query, kind))
            .OrderBy(candidate => candidate, Comparer<NodeSearchCandidate>.Create(NodeSearch.Compare))
            .Select(candidate => candidate.DisplayName)];

    [Fact]
    public void NullArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => NodeSearch.Score(null!, null, null, "x"));
        Assert.Throws<ArgumentNullException>(() => NodeSearch.Humps(null!));
    }
}
