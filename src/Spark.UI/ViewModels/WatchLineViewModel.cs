using Avalonia;

namespace Spark.UI.ViewModels;

/// <summary>
/// One line of the watch panel: its text and the left inset its nesting depth earns it.
/// </summary>
/// <remarks>
/// The indent is computed here rather than baked into the text, because a string padded with
/// spaces cannot be copied out of the panel and pasted anywhere useful, and because the panel's
/// font is proportional in every part except the numbers.
/// </remarks>
/// <param name="depth">How deep in the list structure the line sits. Zero is a port.</param>
/// <param name="text">The line.</param>
public sealed class WatchLineViewModel(int depth, string text)
{
    /// <summary>How deep in the list structure the line sits.</summary>
    public int Depth { get; } = depth;

    /// <summary>The line.</summary>
    public string Text { get; } = text;

    /// <summary>The left inset the nesting depth earns this line.</summary>
    public Thickness Margin { get; } = new(12.0 * depth, 0.0, 0.0, 0.0);
}
