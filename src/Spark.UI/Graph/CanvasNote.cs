using System;
using Spark.UI.Canvas;

namespace Spark.UI.Graph;

/// <summary>
/// A rectangle of text on the canvas: a label on a region of a graph, a reminder, a caveat.
/// </summary>
/// <remarks>
/// <para>
/// A note is <b>not a node</b>. It has no ports, nothing can be wired to it, it never evaluates and
/// it is not in anything's provenance. The engine does not know it exists — it travels through
/// <c>GraphDocument</c> the same way a node's position does, as something the file must remember
/// and the evaluator must never read.
/// </para>
/// <para>
/// It carries a <see cref="Guid"/> so that the saved file can be sorted by identity and stay
/// byte-stable across a save that changed nothing. Position and text are both mutable, because
/// both are things a user drags and edits constantly; the identity is not.
/// </para>
/// </remarks>
public sealed class CanvasNote
{
    /// <summary>The width a note is created at.</summary>
    public const double DefaultWidth = 220;

    /// <summary>The height a note is created at.</summary>
    public const double DefaultHeight = 96;

    /// <summary>
    /// The smallest a note may be made. Small enough to be a terse label, large enough that one
    /// cannot be resized into something that is impossible to find and click again.
    /// </summary>
    public const double MinimumSize = 48;

    private double _width = DefaultWidth;
    private double _height = DefaultHeight;
    private string _text = string.Empty;

    /// <summary>Creates a note with a fresh identity.</summary>
    public CanvasNote() : this(Guid.NewGuid())
    {
    }

    /// <summary>Creates a note with a known identity, which is what opening a file does.</summary>
    /// <param name="id">The identity to keep.</param>
    public CanvasNote(Guid id) => Id = id;

    /// <summary>The note's identity, stable across save and load.</summary>
    public Guid Id { get; }

    /// <summary>The left edge in world coordinates.</summary>
    public double X { get; set; }

    /// <summary>The top edge in world coordinates.</summary>
    public double Y { get; set; }

    /// <summary>The width, never below <see cref="MinimumSize"/>.</summary>
    public double Width
    {
        get => _width;
        set => _width = Math.Max(MinimumSize, value);
    }

    /// <summary>The height, never below <see cref="MinimumSize"/>.</summary>
    public double Height
    {
        get => _height;
        set => _height = Math.Max(MinimumSize, value);
    }

    /// <summary>
    /// What the note says. Never null — an empty note is one the user has created and not yet
    /// typed into, which is an ordinary state and not a missing value.
    /// </summary>
    public string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    /// <summary>The rectangle the note occupies.</summary>
    public CanvasBounds Bounds => CanvasBounds.FromSize(X, Y, Width, Height);
}
