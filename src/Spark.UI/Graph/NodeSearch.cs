using System;

namespace Spark.UI.Graph;

/// <summary>
/// How well a query matched a node, strongest first.
/// </summary>
/// <remarks>
/// The order is the specification, not a preference: exact beats prefix beats camel-hump beats
/// substring beats the category beats the description. Sorting by this is what makes three
/// keystrokes land on the node somebody meant rather than on whichever node happens to contain
/// those letters first.
/// </remarks>
public enum NodeMatch
{
    /// <summary>The query matched nothing about this node.</summary>
    None = 0,

    /// <summary>The query appears in the node's description.</summary>
    Description = 1,

    /// <summary>The query appears in the node's category.</summary>
    Category = 2,

    /// <summary>The query appears somewhere in the node's name.</summary>
    Substring = 3,

    /// <summary>The query is the node's capitals: <c>cbcr</c> for <c>Circle.ByCentreRadius</c>.</summary>
    CamelHump = 4,

    /// <summary>The node's name, or the part after the dot, starts with the query.</summary>
    Prefix = 5,

    /// <summary>The query is the whole name, or the whole part after the dot.</summary>
    Exact = 6,
}

/// <summary>
/// How well one node answers one query, and how to break a tie between two that answer equally.
/// </summary>
/// <param name="Kind">The strength of the match.</param>
/// <param name="Distance">
/// How far off a perfect match of that kind this was — the index a substring was found at, or the
/// number of capitals a camel-hump query left unconsumed. Smaller is better, and it only ever
/// separates results of the same <see cref="Kind"/>.
/// </param>
public readonly record struct NodeSearchResult(NodeMatch Kind, int Distance)
{
    /// <summary>Whether the node should appear in the results at all.</summary>
    public bool IsMatch => Kind != NodeMatch.None;

    /// <summary>Nothing matched.</summary>
    public static NodeSearchResult NoMatch => new(NodeMatch.None, int.MaxValue);
}

/// <summary>
/// Ranks nodes against what somebody typed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ranking rather than filtering is the whole feature</b> (<c>E8-T8</c>). A library of
/// fifty-seven nodes can be filtered and skimmed; a library of thousands, which is what packages
/// produce, cannot. The search that works at that size is the one every code editor has: type the
/// capitals.
/// </para>
/// <para>
/// The order — exact, prefix, camel-hump, substring, category, description — is from the plan and
/// is not a matter of taste. What it buys is that <c>cbcr</c> finds <c>Circle.ByCentreRadius</c>,
/// <c>circle</c> finds every circle node with the shortest name first, and <c>radius</c> still
/// finds the nodes that only mention one in their description, ranked below both.
/// </para>
/// <para>
/// This is pure text, deliberately: it takes names and strings rather than view models or node
/// definitions, so it is testable on its own and both the library panel and the canvas creation
/// box rank with the same rules rather than each growing their own.
/// </para>
/// </remarks>
public static class NodeSearch
{
    /// <summary>
    /// Scores one node against a query.
    /// </summary>
    /// <param name="displayName">The node's name, such as <c>Circle.ByCentreRadius</c>.</param>
    /// <param name="category">The node's library category, or null.</param>
    /// <param name="description">The node's one-paragraph description, or null.</param>
    /// <param name="query">What the user typed. An empty query matches everything equally.</param>
    /// <returns>The match, or <see cref="NodeSearchResult.NoMatch"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="displayName"/> is <see langword="null"/>.</exception>
    public static NodeSearchResult Score(
        string displayName, string? category, string? description, string? query)
    {
        ArgumentNullException.ThrowIfNull(displayName);

        string trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            // An empty query is not a failed search, it is no search: everything matches equally
            // and the caller keeps its own order.
            return new NodeSearchResult(NodeMatch.Exact, 0);
        }

        // The part after the last dot is what a user usually means. Circle.ByCentreRadius is found
        // by "circle" and by "bycentre", and both should feel like a prefix match.
        int dot = displayName.LastIndexOf('.');
        string member = dot >= 0 && dot < displayName.Length - 1 ? displayName[(dot + 1)..] : displayName;

        if (Same(displayName, trimmed) || Same(member, trimmed))
        {
            return new NodeSearchResult(NodeMatch.Exact, 0);
        }

        if (StartsWith(displayName, trimmed))
        {
            return new NodeSearchResult(NodeMatch.Prefix, 0);
        }

        if (StartsWith(member, trimmed))
        {
            // One step behind a match on the whole name, so Circle.* outranks Arc.ByCircle* for
            // the query "circle" without needing a rank of its own.
            return new NodeSearchResult(NodeMatch.Prefix, 1);
        }

        if (CamelHumpDistance(displayName, trimmed) is { } humps)
        {
            return new NodeSearchResult(NodeMatch.CamelHump, humps);
        }

        int index = displayName.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return new NodeSearchResult(NodeMatch.Substring, index);
        }

        if (category is not null && category.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return new NodeSearchResult(NodeMatch.Category, 0);
        }

        if (description is not null && description.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return new NodeSearchResult(NodeMatch.Description, 0);
        }

        return NodeSearchResult.NoMatch;
    }

    /// <summary>
    /// Orders two matched nodes: stronger match first, then closer, then shorter, then by name.
    /// </summary>
    /// <remarks>
    /// The length tie-break is what puts <c>Circle.ByCentreRadius</c> above
    /// <c>Circle.ByCentreNormalRadius</c> for <c>circle</c>. Falling through to an ordinal
    /// comparison keeps the order total, which matters more than it sounds: a result list that
    /// reshuffles between keystrokes cannot be clicked.
    /// </remarks>
    /// <param name="first">The first node's match.</param>
    /// <param name="firstName">The first node's name.</param>
    /// <param name="second">The second node's match.</param>
    /// <param name="secondName">The second node's name.</param>
    /// <returns>Less than zero when the first should be shown above the second.</returns>
    /// <exception cref="ArgumentNullException">Either name is <see langword="null"/>.</exception>
    public static int Compare(
        NodeSearchResult first, string firstName, NodeSearchResult second, string secondName)
    {
        ArgumentNullException.ThrowIfNull(firstName);
        ArgumentNullException.ThrowIfNull(secondName);

        if (first.Kind != second.Kind)
        {
            return second.Kind.CompareTo(first.Kind);
        }

        if (first.Distance != second.Distance)
        {
            return first.Distance.CompareTo(second.Distance);
        }

        if (firstName.Length != secondName.Length)
        {
            return firstName.Length.CompareTo(secondName.Length);
        }

        return string.CompareOrdinal(firstName, secondName);
    }

    /// <summary>
    /// The capitals of a name, which is what a camel-hump query is matched against.
    /// </summary>
    /// <remarks>
    /// <c>Circle.ByCentreRadius</c> reduces to <c>CBCR</c>. A digit counts as a hump because
    /// <c>Point2d</c> is meaningfully searched as <c>p2</c>, and the first letter of every
    /// dot-separated part always counts so that a lower-case name is still reachable.
    /// </remarks>
    /// <param name="displayName">The node's name.</param>
    /// <returns>The capitals, in order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="displayName"/> is <see langword="null"/>.</exception>
    public static string Humps(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);

        char[] humps = new char[displayName.Length];
        int count = 0;
        bool boundary = true;

        foreach (char character in displayName)
        {
            if (!char.IsLetterOrDigit(character))
            {
                boundary = true;
                continue;
            }

            if (boundary || char.IsUpper(character) || char.IsDigit(character))
            {
                humps[count++] = char.ToUpperInvariant(character);
            }

            boundary = false;
        }

        return new string(humps, 0, count);
    }

    private static int? CamelHumpDistance(string displayName, string query)
    {
        // A query carrying a separator is not somebody typing capitals, it is somebody typing a
        // name out, and matching it against the capitals would only produce noise.
        foreach (char character in query)
        {
            if (!char.IsLetterOrDigit(character))
            {
                return null;
            }
        }

        string humps = Humps(displayName);
        return StartsWith(humps, query) ? humps.Length - query.Length : null;
    }

    private static bool Same(string value, string query) =>
        string.Equals(value, query, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWith(string value, string query) =>
        value.StartsWith(query, StringComparison.OrdinalIgnoreCase);
}
