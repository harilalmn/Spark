using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Spark.UI.Theming;

/// <summary>
/// The colours a node's header can be set to, and the names they are chosen by (`E8-T35`).
/// </summary>
/// <remarks>
/// <para>
/// <b>The ten category fills, and nothing else.</b> Those are the colours whose contrast against
/// the header text is already measured — at rest, hovered, and desaturated for a node that did not
/// run — so a node recoloured to one of them is legible by construction. An arbitrary picker would
/// need the title to flip between light and dark by luminance and would need every one of those
/// measurements taken again at runtime, which is a second pass and not this one.
/// </para>
/// <para>
/// <b><see cref="Default"/> is a choice rather than the absence of one</b>, and it is first for the
/// reason <c>any</c> is first in the input-type dropdown: it is what the node already is, and it is
/// the way back from a colour somebody regrets.
/// </para>
/// </remarks>
public static class NodeColourChoices
{
    /// <summary>The name of the entry that means "the colour this node's own category has".</summary>
    public const string Default = "Default";

    private static readonly string[] Names =
    [
        Default,
        nameof(NodeCategory.Input),
        nameof(NodeCategory.Logic),
        nameof(NodeCategory.Display),
        nameof(NodeCategory.Solid),
        nameof(NodeCategory.Curve),
        nameof(NodeCategory.Point),
        nameof(NodeCategory.Script),
        nameof(NodeCategory.List),
        nameof(NodeCategory.Math),
        nameof(NodeCategory.Custom),
    ];

    /// <summary>Every choice, in the order a dropdown should offer them.</summary>
    public static IReadOnlyList<string> All => Names;

    /// <summary>The category a choice names, or null for <see cref="Default"/>.</summary>
    /// <param name="choice">The name shown in the dropdown.</param>
    /// <returns>The category, or null.</returns>
    public static NodeCategory? Parse(string? choice) =>
        choice is not null && Enum.TryParse(choice, out NodeCategory category)
            ? category
            : null;

    /// <summary>The name for a chosen category, or <see cref="Default"/> for none.</summary>
    /// <param name="category">The chosen category, or null.</param>
    /// <returns>The name.</returns>
    public static string Of(NodeCategory? category) =>
        category is { } chosen ? chosen.ToString() : Default;
}

/// <summary>
/// Turns a name from <see cref="NodeColourChoices"/> into the swatch drawn beside it.
/// </summary>
/// <remarks>
/// A dropdown of ten colour names and no colours would be a puzzle. The swatch is the same fill the
/// node's header will take, so what the list shows and what the canvas draws cannot drift.
/// </remarks>
public sealed class NodeColourSwatchConverter : IValueConverter
{
    /// <summary>The one instance the XAML binds to.</summary>
    public static NodeColourSwatchConverter Instance { get; } = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        NodeColourChoices.Parse(value as string) is { } category
            ? NodeCategoryColours.BrushOf(category)
            : SparkPalette.TextMutedBrush;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always. A swatch is not edited.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A colour swatch is drawn from a name, never read back into one.");
}
