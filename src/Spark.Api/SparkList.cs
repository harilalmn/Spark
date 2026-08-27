using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Spark.Api;

/// <summary>
/// An immutable, ordered list of graph values that carries its own <see cref="Rank"/>.
/// <c>SparkList</c> is the only thing in Spark that counts as a list: everything else — a
/// <see cref="string"/>, a <c>Point3d</c>, a <c>double[]</c> handed over by a third-party
/// library — is a scalar of rank 0.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a dedicated type.</b> Replication is rank-based, so the question "is this value a
/// list, or an opaque thing the node wants whole?" has to have exactly one answer, in exactly
/// one place, and answering it must be O(1). Neither <c>List&lt;object&gt;</c> nor a raw
/// <see cref="IEnumerable{T}"/> can do that: a <see cref="string"/> is an
/// <c>IEnumerable&lt;char&gt;</c> and a <c>Point3d[]</c> may be a list of points or a single
/// opaque value depending on what the node meant. <c>SparkList</c> settles it at the boundary
/// and never re-derives it. See ADR-0012 and <c>docs/help/concepts/lacing.md</c> §2.1.
/// </para>
/// <para>
/// <b>Rank is stored, not derived.</b> Decision D8 of the lacing specification: an empty list
/// still has a rank, and it is the rank of the structure that produced it. The empty list a
/// two-dimensional Cross Product yields is rank 2, not rank 1, because otherwise the rank of a
/// graph would change when a filter happened to remove everything — turning an empty result
/// into a shape bug downstream rather than an empty one.
/// </para>
/// <para>
/// <b>Ragged lists.</b> Decision D9: the rank of a ragged list is the maximum depth of any
/// branch, so <c>[1, [2, 3]]</c> is rank 2. This is safe because replication re-evaluates
/// excess at every level, so shallow branches simply stop replicating sooner and arrive at
/// the node whole.
/// </para>
/// <para>
/// For a non-empty list that invariant is <i>checked</i>: the declared rank must equal one
/// more than the deepest item. A list whose stored rank disagrees with its contents would
/// make every replication decision downstream of it wrong in a way that is invisible in a
/// value preview, which is precisely the class of bug rank exists to prevent.
/// </para>
/// </remarks>
public sealed class SparkList : IReadOnlyList<object?>
{
    private readonly object?[] _items;

    /// <summary>
    /// Creates a list with an explicitly stated rank.
    /// </summary>
    /// <param name="items">The items, copied on construction. Items may be <see langword="null"/>.</param>
    /// <param name="rank">
    /// The rank of the list. Must be at least 1, and for a non-empty list must equal one more
    /// than the rank of its deepest item. An empty list may declare any rank of 1 or more,
    /// which is how a Cross Product over two empty dimensions produces an empty rank-2 list.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rank"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// The list is non-empty and <paramref name="rank"/> is not one more than the rank of its
    /// deepest item.
    /// </exception>
    public SparkList(IEnumerable<object?> items, int rank)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(rank, 1);

        _items = items is object?[] array ? (object?[])array.Clone() : [.. items];

        if (_items.Length > 0)
        {
            int expected = 1 + DeepestItemRank(_items);
            if (rank != expected)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "A non-empty list of rank {0} must contain an item of rank {1}; its deepest item is rank {2}.",
                        rank,
                        rank - 1,
                        expected - 1),
                    nameof(rank));
            }
        }

        Rank = rank;
    }

    /// <summary>
    /// How deeply nested this list is. A list of scalars is rank 1, a list of lists is rank 2,
    /// and a ragged list takes the depth of its deepest branch. Reading it is O(1) and never
    /// walks the data.
    /// </summary>
    public int Rank { get; }

    /// <summary>The number of items at this level. Not the total number of leaves.</summary>
    public int Count => _items.Length;

    /// <summary>Gets the item at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based index.</param>
    /// <returns>The item, which may itself be a <see cref="SparkList"/> or <see langword="null"/>.</returns>
    public object? this[int index] => _items[index];

    /// <summary>
    /// Creates a list from items, deriving the rank from their contents. This is the ordinary
    /// way to build a list whose shape is already known; use the constructor when the rank has
    /// to be stated because the list is empty.
    /// </summary>
    /// <param name="items">The items. An empty call produces an empty rank-1 list.</param>
    /// <returns>The list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public static SparkList Of(params object?[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new SparkList(items, items.Length == 0 ? 1 : 1 + DeepestItemRank(items));
    }

    /// <summary>
    /// Creates an empty list at a stated rank. Decision D8: emptiness does not erase shape.
    /// </summary>
    /// <param name="rank">The rank the structure would have had. Must be at least 1.</param>
    /// <returns>The empty list.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rank"/> is less than 1.</exception>
    public static SparkList Empty(int rank) => new([], rank);

    /// <summary>
    /// The rank of any graph value: <see cref="Rank"/> for a <see cref="SparkList"/>, and 0 for
    /// everything else including <see langword="null"/>, strings and arrays.
    /// </summary>
    /// <param name="value">The value to measure.</param>
    /// <returns>The rank.</returns>
    public static int RankOf(object? value) => value is SparkList list ? list.Rank : 0;

    /// <summary>Returns an enumerator over the items at this level.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<object?> GetEnumerator() => ((IEnumerable<object?>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <summary>
    /// Renders the list in the bracket notation the lacing specification and the watch panel
    /// use, for example <c>[[1, 2], [3, 4]]</c>.
    /// </summary>
    /// <returns>The rendered list.</returns>
    public override string ToString()
    {
        StringBuilder builder = new();
        Render(this, builder);
        return builder.ToString();
    }

    private static void Render(object? value, StringBuilder builder)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                return;

            case SparkList list:
                builder.Append('[');
                for (int index = 0; index < list.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(", ");
                    }

                    Render(list[index], builder);
                }

                builder.Append(']');
                return;

            case IFormattable formattable:
                builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;

            default:
                builder.Append(value.ToString());
                return;
        }
    }

    private static int DeepestItemRank(object?[] items)
    {
        int deepest = 0;
        foreach (object? item in items)
        {
            int rank = RankOf(item);
            if (rank > deepest)
            {
                deepest = rank;
            }
        }

        return deepest;
    }
}
