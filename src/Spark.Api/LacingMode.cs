namespace Spark.Api;

/// <summary>
/// How a node pairs up its inputs when more than one of them arrives deeper than the port that
/// wanted it. Four of these are replication algorithms; <see cref="Auto"/> is a sentinel.
/// </summary>
/// <remarks>
/// The full semantics, including the case table that is this enum's executable specification,
/// are in <c>docs/help/concepts/lacing.md</c>.
/// </remarks>
public enum LacingMode
{
    /// <summary>
    /// <b>Not a replication algorithm.</b> A sentinel meaning "I have not overridden this node's
    /// lacing; use whatever its author chose". It resolves to the node definition's default
    /// before replication begins and never reaches the replication procedure itself.
    /// <para>
    /// It is the value a freshly placed node carries, which is why it is the zero value here:
    /// placing a node does not express an opinion about lacing, and this is how the graph
    /// records the absence of one. Two nodes both set to <see cref="Auto"/> can therefore lace
    /// differently, because what they share is "not overridden" rather than a behaviour.
    /// </para>
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Zip, truncating to the shortest replicating input. Adds one level of rank per level of
    /// replication. The mode that respects "there were only two matches".
    /// </summary>
    Shortest = 1,

    /// <summary>
    /// Zip, with shorter inputs repeating their <b>last</b> element — <c>[1, 5]</c> extended to
    /// length four is <c>[1,5,5,5]</c>, never <c>[1,5,1,5]</c>. Adds one level of rank per level
    /// of replication. An empty replicating input makes the whole result empty rather than
    /// padding it, because "repeat the last element" is undefined for a list that has none.
    /// </summary>
    Longest = 2,

    /// <summary>
    /// Every combination, nested. Raises output rank by <i>k</i>, the number of replicating
    /// inputs — <b>not</b> by one. Ten values crossed with ten is a ten-by-ten nested list of
    /// rank 2, not a flat list of a hundred.
    /// </summary>
    CrossProduct = 3,

    /// <summary>
    /// No replication at all; values pass through whole. Rank reconciliation still happens, so a
    /// scalar fed to a list port is still promoted. This is what an inherently rank-1 node such
    /// as a list-count node needs, and it is the escape hatch when a node should see a list
    /// rather than its items.
    /// </summary>
    Disabled = 4,
}
