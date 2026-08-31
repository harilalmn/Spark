using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;

namespace Spark.Nodes.Core;

/// <summary>
/// Operations on lists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every input here is <see cref="KeepStructureAttribute"/>, and that is the whole category.</b>
/// A list port that replicates is handed one item at a time, which is exactly what a list operation
/// must not receive — <c>List.Count</c> would answer 1 for every element instead of the length
/// once. These nodes are the ones that look <i>at</i> a list rather than <i>through</i> it, so the
/// engine has to be told to stop lacing at their door.
/// </para>
/// <para>
/// The curated set, not the exhaustive one. `E5-T3` excludes operator nodes and the same reasoning
/// applies here: a library of two hundred list operations is a search problem, and the fifteen
/// people reach for are a tool.
/// </para>
/// </remarks>
[SparkNode(Category = NodeCategories.List)]
public static class ListNodes
{
    /// <summary>How many items a list holds.</summary>
    /// <param name="list">The list. A single value counts as one item.</param>
    /// <returns>The count.</returns>
    [SparkNode(Name = "List.Count")]
    [return: NodePort("count")]
    public static int Count([KeepStructure] object? list) =>
        list is SparkList items ? items.Count : 1;

    /// <summary>The item at an index, counting from zero.</summary>
    /// <param name="list">The list.</param>
    /// <param name="index">
    /// The index. Negative indices count back from the end, so −1 is the last item.
    /// </param>
    /// <returns>The item.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list.</exception>
    /// <remarks>
    /// Negative indices are supported because the alternative is
    /// <c>List.GetItemAtIndex(list, List.Count(list) - 1)</c> every time somebody wants the last
    /// item, which is three nodes for a thing that should be one.
    /// </remarks>
    [SparkNode(Name = "List.GetItemAtIndex")]
    [return: NodePort("item")]
    public static object? GetItemAtIndex([KeepStructure] object? list, int index = 0)
    {
        IReadOnlyList<object?> items = AsItems(list);
        int resolved = index < 0 ? items.Count + index : index;

        if (resolved < 0 || resolved >= items.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, $"The list has {items.Count} item(s), so {index} is outside it.");
        }

        return items[resolved];
    }

    /// <summary>The first item.</summary>
    /// <param name="list">The list.</param>
    /// <returns>The first item.</returns>
    /// <exception cref="ArgumentException">The list is empty.</exception>
    [SparkNode(Name = "List.FirstItem", Kind = NodeMemberKind.Query)]
    [return: NodePort("item")]
    public static object? FirstItem([KeepStructure] object? list) => Single(list, first: true);

    /// <summary>The last item.</summary>
    /// <param name="list">The list.</param>
    /// <returns>The last item.</returns>
    /// <exception cref="ArgumentException">The list is empty.</exception>
    [SparkNode(Name = "List.LastItem", Kind = NodeMemberKind.Query)]
    [return: NodePort("item")]
    public static object? LastItem([KeepStructure] object? list) => Single(list, first: false);

    /// <summary>The list in the opposite order.</summary>
    /// <param name="list">The list.</param>
    /// <returns>The reversed list.</returns>
    [SparkNode(Name = "List.Reverse", Kind = NodeMemberKind.Action)]
    [return: NodePort("list")]
    public static SparkList Reverse([KeepStructure] object? list)
    {
        IReadOnlyList<object?> items = AsItems(list);

        return new SparkList([.. items.Reverse()], SparkList.RankOf(list) is 0 ? 1 : SparkList.RankOf(list));
    }

    /// <summary>Two lists joined end to end.</summary>
    /// <param name="first">The list that comes first.</param>
    /// <param name="second">The list that follows it.</param>
    /// <returns>The joined list.</returns>
    [SparkNode(Name = "List.Join")]
    [return: NodePort("list")]
    public static SparkList Join([KeepStructure] object? first, [KeepStructure] object? second) =>
        new([.. AsItems(first), .. AsItems(second)], 1);

    /// <summary>The first <paramref name="count"/> items, or the last when it is negative.</summary>
    /// <param name="list">The list.</param>
    /// <param name="count">How many to take. Negative takes from the end.</param>
    /// <returns>The taken items.</returns>
    [SparkNode(Name = "List.TakeItems")]
    [return: NodePort("list")]
    public static SparkList TakeItems([KeepStructure] object? list, int count = 1)
    {
        IReadOnlyList<object?> items = AsItems(list);
        int size = System.Math.Min(System.Math.Abs(count), items.Count);

        return new SparkList(count < 0 ? [.. items.Skip(items.Count - size)] : [.. items.Take(size)], 1);
    }

    /// <summary>The list with duplicates removed, keeping the first of each.</summary>
    /// <param name="list">The list.</param>
    /// <returns>The list without repeats.</returns>
    /// <remarks>
    /// Compared by value, which is what makes it useful on geometry: the value layer is built of
    /// equatable structs, so two points computed by different routes are one point here.
    /// </remarks>
    [SparkNode(Name = "List.UniqueItems", Kind = NodeMemberKind.Action)]
    [return: NodePort("list")]
    public static SparkList UniqueItems([KeepStructure] object? list)
    {
        List<object?> unique = [];

        foreach (object? item in AsItems(list))
        {
            if (!unique.Contains(item))
            {
                unique.Add(item);
            }
        }

        return new SparkList(unique, 1);
    }

    /// <summary>A list of one value repeated.</summary>
    /// <param name="value">The value to repeat.</param>
    /// <param name="count">How many times. Not negative.</param>
    /// <returns>The list.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    [SparkNode(Name = "List.OfRepeatedItem", Kind = NodeMemberKind.Create)]
    [return: NodePort("list")]
    public static SparkList OfRepeatedItem([KeepStructure] object? value, int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        return new SparkList(Enumerable.Repeat(value, count), 1);
    }

    /// <summary>
    /// A rank-1 list flattened out of whatever nesting it had.
    /// </summary>
    /// <param name="list">The list.</param>
    /// <returns>Every leaf value, in order, as one flat list.</returns>
    /// <remarks>
    /// The escape hatch from a lacing that produced more structure than was wanted. It is here
    /// rather than hidden because flattening is a real modelling decision — it throws away the
    /// correspondence between items that the rank was carrying — and a node makes that visible in
    /// the graph.
    /// </remarks>
    [SparkNode(Name = "List.Flatten", Kind = NodeMemberKind.Action)]
    [return: NodePort("list")]
    public static SparkList Flatten([KeepStructure] object? list)
    {
        List<object?> flat = [];
        Gather(list, flat);

        return new SparkList(flat, 1);
    }

    private static void Gather(object? value, List<object?> into)
    {
        if (value is SparkList list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Gather(list[i], into);
            }

            return;
        }

        into.Add(value);
    }

    private static object? Single(object? list, bool first)
    {
        IReadOnlyList<object?> items = AsItems(list);

        if (items.Count == 0)
        {
            throw new ArgumentException(
                "The list is empty, so it has no first or last item.", nameof(list));
        }

        return first ? items[0] : items[^1];
    }

    /// <summary>
    /// A list's items, treating a single value as a list of one.
    /// </summary>
    /// <remarks>
    /// Promoting a single value rather than refusing it, because a graph that produces one item
    /// where it usually produces several is an ordinary Tuesday — a range that happened to have one
    /// step in it — and failing there would make every list node a source of intermittent errors.
    /// </remarks>
    private static IReadOnlyList<object?> AsItems(object? list)
    {
        if (list is not SparkList items)
        {
            return [list];
        }

        object?[] copy = new object?[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            copy[i] = items[i];
        }

        return copy;
    }
}
