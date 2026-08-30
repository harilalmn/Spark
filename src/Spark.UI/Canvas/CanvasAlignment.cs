using System;
using System.Collections.Generic;
using System.Linq;

namespace Spark.UI.Canvas;

/// <summary>
/// The ways a set of nodes can be lined up.
/// </summary>
/// <remarks>
/// The six alignments answer <i>which edge do these share</i>; the two distributions answer
/// <i>are the gaps equal</i>. They are separated because they have different preconditions — two
/// nodes can be aligned and cannot meaningfully be distributed — and a menu that offers an
/// operation which silently does nothing is worse than one that leaves it out.
/// </remarks>
public enum CanvasAlign
{
    /// <summary>Every node's left edge moves to the leftmost left edge.</summary>
    Left,

    /// <summary>Every node's horizontal centre moves to the centre of the selection's box.</summary>
    HorizontalCentres,

    /// <summary>Every node's right edge moves to the rightmost right edge.</summary>
    Right,

    /// <summary>Every node's top edge moves to the topmost top edge.</summary>
    Top,

    /// <summary>Every node's vertical centre moves to the middle of the selection's box.</summary>
    VerticalCentres,

    /// <summary>Every node's bottom edge moves to the bottommost bottom edge.</summary>
    Bottom,

    /// <summary>The horizontal gaps between neighbours are made equal, outermost two fixed.</summary>
    DistributeHorizontally,

    /// <summary>The vertical gaps between neighbours are made equal, outermost two fixed.</summary>
    DistributeVertically,
}

/// <summary>
/// Lining up nodes, as arithmetic over rectangles.
/// </summary>
/// <remarks>
/// <para>
/// Pure by design, and it lives beside <see cref="CanvasBounds"/> and <c>SceneIndex</c> for the
/// same reason they are pure: there is no Avalonia type in the signature, so every case is a unit
/// test rather than a window and a gesture. What <c>GraphCanvas</c> adds on top is the selection,
/// the spatial index and the undo label — none of which is arithmetic.
/// </para>
/// <para>
/// <b>Every operation moves nodes and nothing else.</b> A position is not part of a node's
/// provenance, so no alignment can change what the graph evaluates to; that is why the canvas
/// reports one as an edit that does not require a run.
/// </para>
/// </remarks>
public static class CanvasAlignment
{
    /// <summary>
    /// The smallest selection an alignment is meaningful over. One node is already aligned with
    /// itself.
    /// </summary>
    public const int MinimumToAlign = 2;

    /// <summary>
    /// The smallest selection a distribution is meaningful over. With two nodes there is one gap,
    /// and one gap is already equal to itself.
    /// </summary>
    public const int MinimumToDistribute = 3;

    /// <summary>Whether an operation can be applied to a selection of a given size.</summary>
    /// <param name="align">The operation.</param>
    /// <param name="count">How many nodes are selected.</param>
    /// <returns>True when the operation would be meaningful.</returns>
    public static bool IsApplicable(CanvasAlign align, int count) =>
        count >= (IsDistribution(align) ? MinimumToDistribute : MinimumToAlign);

    /// <summary>Whether an operation spreads nodes out rather than lining them up.</summary>
    /// <param name="align">The operation.</param>
    /// <returns>True for the two distributions.</returns>
    public static bool IsDistribution(CanvasAlign align) =>
        align is CanvasAlign.DistributeHorizontally or CanvasAlign.DistributeVertically;

    /// <summary>The name an undo step is labelled with.</summary>
    /// <param name="align">The operation.</param>
    /// <returns>A verb phrase, capitalised, with no node count in it.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="align"/> is not a known value.</exception>
    public static string Describe(CanvasAlign align) => align switch
    {
        CanvasAlign.Left => "Align left",
        CanvasAlign.HorizontalCentres => "Align centres",
        CanvasAlign.Right => "Align right",
        CanvasAlign.Top => "Align top",
        CanvasAlign.VerticalCentres => "Align middles",
        CanvasAlign.Bottom => "Align bottom",
        CanvasAlign.DistributeHorizontally => "Distribute horizontally",
        CanvasAlign.DistributeVertically => "Distribute vertically",
        _ => throw new ArgumentOutOfRangeException(nameof(align)),
    };

    /// <summary>
    /// Works out where each node should sit.
    /// </summary>
    /// <param name="align">The operation to apply.</param>
    /// <param name="boxes">Where the nodes are now, in any order.</param>
    /// <returns>
    /// The new top-left corner for each box, in the order they were given. A box that does not
    /// move is returned at the corner it already had, so the result is always the same length as
    /// the input and the caller never has to reconcile two orderings.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="boxes"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="align"/> is not a known value.</exception>
    /// <remarks>
    /// A selection too small for the operation is returned unchanged rather than rejected. The
    /// caller then has one question to ask — <i>did anything move?</i> — instead of two, and that
    /// is also the question that decides whether an undo step is worth recording.
    /// </remarks>
    public static IReadOnlyList<(double X, double Y)> Apply(
        CanvasAlign align, IReadOnlyList<CanvasBounds> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);

        (double X, double Y)[] result = new (double, double)[boxes.Count];
        for (int i = 0; i < boxes.Count; i++)
        {
            result[i] = (boxes[i].MinX, boxes[i].MinY);
        }

        if (!IsApplicable(align, boxes.Count))
        {
            return result;
        }

        if (IsDistribution(align))
        {
            Distribute(align, boxes, result);
            return result;
        }

        CanvasBounds extent = Extent(boxes);

        for (int i = 0; i < boxes.Count; i++)
        {
            CanvasBounds box = boxes[i];
            result[i] = align switch
            {
                CanvasAlign.Left => (extent.MinX, box.MinY),
                CanvasAlign.Right => (extent.MaxX - box.Width, box.MinY),
                CanvasAlign.HorizontalCentres =>
                    (((extent.MinX + extent.MaxX) / 2) - (box.Width / 2), box.MinY),
                CanvasAlign.Top => (box.MinX, extent.MinY),
                CanvasAlign.Bottom => (box.MinX, extent.MaxY - box.Height),
                CanvasAlign.VerticalCentres =>
                    (box.MinX, ((extent.MinY + extent.MaxY) / 2) - (box.Height / 2)),
                _ => throw new ArgumentOutOfRangeException(nameof(align)),
            };
        }

        return result;
    }

    /// <summary>The rectangle that contains every box.</summary>
    /// <param name="boxes">The boxes, of which there must be at least one.</param>
    /// <returns>Their union.</returns>
    private static CanvasBounds Extent(IReadOnlyList<CanvasBounds> boxes)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        foreach (CanvasBounds box in boxes)
        {
            minX = Math.Min(minX, box.MinX);
            minY = Math.Min(minY, box.MinY);
            maxX = Math.Max(maxX, box.MaxX);
            maxY = Math.Max(maxY, box.MaxY);
        }

        return new CanvasBounds(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Spreads the nodes between the two outermost ones so that the gaps between neighbours are
    /// equal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Equal gaps, not equal centres.</b> Nodes are not all the same size — a node's height is
    /// its port count — so spacing their centres evenly leaves a wide node visually crowding its
    /// neighbours while the arithmetic insists everything is even. Gaps are what the eye reads.
    /// </para>
    /// <para>
    /// The two outermost nodes stay exactly where they are. Anything else would move a selection
    /// the user had already placed, and the point of distribute is to tidy the inside of a run,
    /// not to relocate it.
    /// </para>
    /// <para>
    /// The gap can come out negative, when the nodes together are wider than the span they have to
    /// fit into. That is left alone deliberately: overlapping is the honest rendering of <i>these
    /// do not fit</i>, and silently widening the span would move the outermost nodes, which is the
    /// one thing this promises not to do.
    /// </para>
    /// </remarks>
    private static void Distribute(
        CanvasAlign align, IReadOnlyList<CanvasBounds> boxes, (double X, double Y)[] result)
    {
        bool horizontal = align == CanvasAlign.DistributeHorizontally;

        // Ordered by leading edge, and the ordering is over indices so that the answer can be
        // written back into the caller's original positions.
        int[] order = [.. Enumerable.Range(0, boxes.Count)
            .OrderBy(i => horizontal ? boxes[i].MinX : boxes[i].MinY)];

        double start = horizontal ? boxes[order[0]].MinX : boxes[order[0]].MinY;
        int last = order[^1];
        double end = horizontal ? boxes[last].MaxX : boxes[last].MaxY;

        double occupied = 0;
        foreach (int i in order)
        {
            occupied += horizontal ? boxes[i].Width : boxes[i].Height;
        }

        double gap = (end - start - occupied) / (boxes.Count - 1);

        double cursor = start;
        foreach (int i in order)
        {
            CanvasBounds box = boxes[i];
            result[i] = horizontal ? (cursor, box.MinY) : (box.MinX, cursor);
            cursor += (horizontal ? box.Width : box.Height) + gap;
        }
    }
}
