using Spark.Api;

namespace Spark.Nodes.Core;

/// <summary>
/// The node that shows what is going through it.
/// </summary>
/// <remarks>
/// <para>
/// A watch changes nothing. It takes a value, returns the same value, and exists so that a readout
/// can be <b>pinned</b> where a preview bubble is only a glance: a bubble answers <i>what is this
/// node under my pointer doing</i>, and a watch answers <i>what is happening here</i>, while you
/// go and look at something else.
/// </para>
/// <para>
/// <b><see cref="KeepStructureAttribute"/> is the whole node.</b> A plain <c>object</c> port is
/// rank 0, so without it the engine would replicate the watch once per element and hand it one
/// item at a time — and the list is precisely what the user opened a watch to look at. Keeping the
/// structure is also what makes a watch honest about rank, which is what
/// <c>E8-T10</c> exists for.
/// </para>
/// </remarks>
[SparkNode(Category = NodeCategories.Display)]
public static class Watch
{
    /// <summary>Passes a value through unchanged, and shows it on the canvas.</summary>
    /// <param name="value">Anything at all, at any rank.</param>
    /// <returns>The same value, structure included.</returns>
    [ShowsValue]
    [return: NodePort("value")]
    public static object? Value([KeepStructure] object? value) => value;
}
