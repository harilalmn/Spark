using System;
using System.Collections.Generic;
using System.Linq;

namespace Spark.UI.Canvas;

/// <summary>
/// Arranging wired nodes into columns that follow the flow of the graph.
/// </summary>
/// <remarks>
/// <para>
/// Pure by design, and it lives beside <see cref="CanvasAlignment"/> for the same reason that one
/// does: there is no Avalonia type in the signature, so every case is a unit test rather than a
/// window and a gesture. What <c>GraphCanvas</c> adds on top is the selection, the spatial index
/// and the undo label — none of which is arithmetic.
/// </para>
/// <para>
/// <b>The difference from an alignment is that this one reads the wires.</b> Aligning asks
/// <i>which edge do these share</i>, and the answer needs only the rectangles. A layout asks
/// <i>what feeds what</i>, so a node's column is one past the furthest column any of its inputs
/// sits in, and the arrangement means something about the graph rather than only about the
/// picture.
/// </para>
/// <para>
/// <b>Every operation moves nodes and nothing else</b>, so — exactly as with an alignment — no
/// layout can change what the graph evaluates to, and the canvas reports one as an edit that does
/// not require a run.
/// </para>
/// </remarks>
public static class CanvasLayout
{
    /// <summary>
    /// The smallest set a layout is meaningful over. One node is already laid out.
    /// </summary>
    public const int MinimumToLayOut = 2;

    /// <summary>The horizontal space left between one column and the next, in world units.</summary>
    /// <remarks>
    /// Wide enough that a wire leaving a node has somewhere to bend before it arrives at the next
    /// one. The canvas draws wires as curves whose control points reach across the gap, and a
    /// narrower column spacing makes a run of nodes read as one block with lines inside it.
    /// </remarks>
    public const double ColumnGap = 72;

    /// <summary>The vertical space left between two nodes stacked in the same column.</summary>
    public const double RowGap = 28;

    /// <summary>
    /// A distance below which two positions are the same position.
    /// </summary>
    /// <remarks>
    /// <b>This exists because a box's width is not stable under moving it.</b>
    /// <see cref="CanvasBounds"/> stores corners, so <c>Width</c> is <c>MaxX - MinX</c> — and
    /// <c>(520 + 209.2) - 520</c> is not bit-for-bit <c>(40 + 209.2) - 40</c>. Laying a graph out
    /// twice therefore accumulates column positions from widths that differ in their last bits, and
    /// the second pass lands a node some 1e-13 units from where the first one put it. Comparing
    /// exactly would make every press of the key record an undo step that undoes nothing visible,
    /// which is the failure <see cref="Description"/>'s caller is specifically written to avoid.
    /// A world unit is a pixel at 100% zoom, so a millionth of one is far below anything a user or
    /// a screenshot can see, and far above the noise.
    /// </remarks>
    public const double Negligible = 1e-6;

    /// <summary>Whether a node would actually move, rather than jitter in the last bits.</summary>
    /// <param name="from">Where it is.</param>
    /// <param name="to">Where it would go.</param>
    /// <returns>True when the two differ by more than <see cref="Negligible"/> on either axis.</returns>
    public static bool Moves((double X, double Y) from, (double X, double Y) to) =>
        Math.Abs(from.X - to.X) > Negligible || Math.Abs(from.Y - to.Y) > Negligible;

    /// <summary>The name an undo step is labelled with.</summary>
    /// <remarks>
    /// Without a node count, for the reason an alignment's label has none: it names an arrangement
    /// rather than an amount of work.
    /// </remarks>
    public const string Description = "Clean up layout";

    /// <summary>Whether a layout would be meaningful over a set of a given size.</summary>
    /// <param name="count">How many nodes are to be laid out.</param>
    /// <returns>True when there are at least <see cref="MinimumToLayOut"/> of them.</returns>
    public static bool IsApplicable(int count) => count >= MinimumToLayOut;

    /// <summary>
    /// Works out where each node should sit.
    /// </summary>
    /// <param name="boxes">Where the nodes are now, in any order.</param>
    /// <param name="links">
    /// Which node feeds which, as pairs of indices into <paramref name="boxes"/>. A pair naming an
    /// index outside the list, or naming the same node twice, is ignored — the caller is filtering
    /// a graph down to a selection, and a wire with one end outside it is not a link between two
    /// nodes that are being laid out.
    /// </param>
    /// <returns>
    /// The new top-left corner for each box, in the order they were given. A box that does not
    /// move is returned at the corner it already had, so the result is always the same length as
    /// the input and the caller never has to reconcile two orderings.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="boxes"/> or <paramref name="links"/> is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A set too small to lay out is returned unchanged rather than rejected, which leaves the
    /// caller one question — <i>did anything move?</i> — instead of two. That is also the question
    /// that decides whether an undo step is worth recording.
    /// </para>
    /// <para>
    /// <b>The arrangement is anchored at the top-left corner of where the nodes already are.</b> A
    /// tidy-up that also teleported the graph to the origin would be two operations wearing one
    /// name, and the second of them is one the user did not ask for.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(double X, double Y)> Apply(
        IReadOnlyList<CanvasBounds> boxes, IReadOnlyList<(int From, int To)> links)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        ArgumentNullException.ThrowIfNull(links);

        (double X, double Y)[] result = new (double, double)[boxes.Count];
        for (int i = 0; i < boxes.Count; i++)
        {
            result[i] = (boxes[i].MinX, boxes[i].MinY);
        }

        if (!IsApplicable(boxes.Count))
        {
            return result;
        }

        List<int>[] upstream = Upstream(boxes.Count, links);
        int[] column = Columns(upstream);
        Place(boxes, upstream, column, result);
        return result;
    }

    /// <summary>
    /// The nodes feeding each node, de-duplicated, as indices.
    /// </summary>
    /// <remarks>
    /// De-duplicated because two ports of one node can be fed by two outputs of the same upstream
    /// node, and counting that twice would weight it twice in the barycentre that orders a column.
    /// A user reading the canvas sees one relationship there, not two.
    /// </remarks>
    private static List<int>[] Upstream(int count, IReadOnlyList<(int From, int To)> links)
    {
        List<int>[] upstream = new List<int>[count];
        for (int i = 0; i < count; i++)
        {
            upstream[i] = [];
        }

        foreach ((int from, int to) in links)
        {
            if (from < 0 || from >= count || to < 0 || to >= count || from == to)
            {
                continue;
            }

            if (!upstream[to].Contains(from))
            {
                upstream[to].Add(from);
            }
        }

        return upstream;
    }

    /// <summary>
    /// Gives every node a column: one past the furthest column anything feeding it sits in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is longest-path layering, computed by relaxing the ranks until they stop moving. The
    /// alternative — one past the <i>nearest</i> input — would let a node sit level with something it
    /// consumes whenever a second, longer path also reaches it, and a wire that runs backwards is
    /// exactly what a cleanup is meant to remove.
    /// </para>
    /// <para>
    /// <b>A cycle is possible here and must not hang.</b> <c>TryConnect</c> refuses a wire that
    /// would close one, but a <c>.spark</c> file can be loaded with a cycle already in it — that is
    /// what <c>SPK1014</c> is for — so the relaxation is bounded by the node count, which is the
    /// most passes an acyclic graph can need. A cycle simply stops improving at that bound and the
    /// nodes on it land in whatever columns they had reached; they cannot all be to the right of
    /// each other, and no arrangement of rectangles can make a cycle read as a flow.
    /// </para>
    /// </remarks>
    private static int[] Columns(List<int>[] upstream)
    {
        int[] column = new int[upstream.Length];

        for (int pass = 0; pass < upstream.Length; pass++)
        {
            bool moved = false;

            for (int node = 0; node < upstream.Length; node++)
            {
                int wanted = 0;
                foreach (int feeder in upstream[node])
                {
                    wanted = Math.Max(wanted, column[feeder] + 1);
                }

                if (wanted > column[node])
                {
                    column[node] = wanted;
                    moved = true;
                }
            }

            if (!moved)
            {
                break;
            }
        }

        return column;
    }

    /// <summary>
    /// Stacks each column, left to right, ordering every column by where its inputs ended up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Columns are ordered by barycentre, which is what stops the wires crossing.</b> A node is
    /// placed level with the average of the nodes feeding it, so two chains that never meet stay
    /// two chains rather than being interleaved by whatever order they happen to occupy in the
    /// list. Nodes with nothing feeding them keep their relative vertical order, which is the only
    /// information about them the user has already given.
    /// </para>
    /// <para>
    /// The columns are placed left to right so that a column's ordering can read the positions the
    /// previous ones were given rather than the ones they came in with. That makes the result a
    /// function of the graph and the starting order, and <b>not</b> of the order the nodes appear in
    /// the list — two graphs that differ only in node order lay out identically.
    /// </para>
    /// <para>
    /// <b>Every column starts at the same top edge</b> rather than being centred on its inputs.
    /// Centring reads better on a wide graph and needs overlap resolution to be correct — nodes
    /// pushed apart until they stop colliding — and an arrangement that is *nearly* right about
    /// overlap is worse than one that is plainly regular. Top-aligned columns are what a cleanup
    /// promises: nothing overlaps, nothing runs backwards, and it looks the same every time.
    /// </para>
    /// </remarks>
    private static void Place(
        IReadOnlyList<CanvasBounds> boxes,
        List<int>[] upstream,
        int[] column,
        (double X, double Y)[] result)
    {
        double left = boxes.Min(box => box.MinX);
        double top = boxes.Min(box => box.MinY);
        int columns = column.Max() + 1;

        // The starting centre of every node, read once: ordering a column consults the placed
        // centres of earlier columns and the original centres of everything else, and reading a
        // centre out of `result` as it is written would mix the two silently.
        double[] centre = new double[boxes.Count];
        for (int i = 0; i < boxes.Count; i++)
        {
            centre[i] = boxes[i].MinY + (boxes[i].Height / 2);
        }

        double x = left;

        for (int index = 0; index < columns; index++)
        {
            int[] members = [.. Enumerable.Range(0, boxes.Count).Where(i => column[i] == index)];
            if (members.Length == 0)
            {
                continue;
            }

            // OrderBy is a stable sort, so the final tie-break is the list order and the ordering
            // is total: two nodes with the same barycentre and the same starting height keep the
            // order they arrived in rather than swapping between runs.
            int[] ordered = [.. members
                .OrderBy(i => Barycentre(i, upstream, column, centre))
                .ThenBy(i => centre[i])];

            double y = top;
            double widest = 0;

            foreach (int i in ordered)
            {
                result[i] = (x, y);
                centre[i] = y + (boxes[i].Height / 2);
                y += boxes[i].Height + RowGap;
                widest = Math.Max(widest, boxes[i].Width);
            }

            x += widest + ColumnGap;
        }
    }

    /// <summary>
    /// Where a node wants to sit vertically: the average height of the nodes feeding it from an
    /// earlier column.
    /// </summary>
    /// <param name="node">The node being placed.</param>
    /// <param name="upstream">The feeders of every node.</param>
    /// <param name="column">Every node's column.</param>
    /// <param name="centre">Every node's centre height — placed, for a column already done.</param>
    /// <returns>
    /// The average, or the node's own current centre when nothing in an earlier column feeds it.
    /// </returns>
    /// <remarks>
    /// <b>Only feeders in an earlier column count</b>, and on an acyclic graph that is all of them.
    /// It matters on a cycle, where a feeder can share this node's column or sit to its right: its
    /// position has not been decided yet, so consulting it would order this column against a number
    /// that is about to change.
    /// </remarks>
    private static double Barycentre(
        int node, List<int>[] upstream, int[] column, double[] centre)
    {
        double total = 0;
        int counted = 0;

        foreach (int feeder in upstream[node])
        {
            if (column[feeder] < column[node])
            {
                total += centre[feeder];
                counted++;
            }
        }

        return counted == 0 ? centre[node] : total / counted;
    }
}
