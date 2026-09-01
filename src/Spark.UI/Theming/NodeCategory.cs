using System;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Spark.UI.Theming;

/// <summary>
/// The ten library categories from <c>docs/help/concepts/design-language.md</c> §7.2. There are
/// ten and not fifteen because ten mutually distinguishable hues inside a 60–81 L* band is close
/// to the limit of what is possible while keeping every one of them above 3:1 against the canvas
/// and separated in lightness as well as hue.
/// </summary>
public enum NodeCategory
{
    /// <summary>Input and constants. <c>cat.input</c> <c>#E8C45A</c>.</summary>
    Input,

    /// <summary>Logic. <c>cat.logic</c> <c>#B6C455</c>.</summary>
    Logic,

    /// <summary>Display and preview. <c>cat.display</c> <c>#71C862</c>.</summary>
    Display,

    /// <summary>Geometry — surface and solid. <c>cat.solid</c> <c>#33B992</c>.</summary>
    Solid,

    /// <summary>Geometry — curve. <c>cat.curve</c> <c>#4CBCD4</c>.</summary>
    Curve,

    /// <summary>Geometry — point and vector. <c>cat.point</c> <c>#5AA2EA</c>.</summary>
    Point,

    /// <summary>Script and code. <c>cat.script</c> <c>#7789EA</c>. The lowest at 5.39:1.</summary>
    Script,

    /// <summary>Lists. <c>cat.list</c> <c>#E489C4</c>.</summary>
    List,

    /// <summary>Math. <c>cat.math</c> <c>#DE7B50</c>.</summary>
    Math,

    /// <summary>Custom and uncategorised. <c>cat.custom</c> <c>#9AA3B2</c>.</summary>
    Custom,
}

/// <summary>
/// Maps the category <i>names</i> a node definition carries onto the ten
/// <see cref="NodeCategory"/> values the renderer colours.
/// </summary>
/// <remarks>
/// The engine's category is a string rather than an enum so a third-party package can file its
/// nodes under a name Spark has never heard of. An unrecognised name resolves to
/// <see cref="NodeCategory.Custom"/> — a legible grey-blue node — rather than throwing, because a
/// package with an odd category should still be usable.
/// </remarks>
public static class NodeCategoryNames
{
    /// <summary>Resolves a category name to the value that carries its colour.</summary>
    /// <param name="name">The name from the node definition. May be null.</param>
    /// <returns>The category, or <see cref="NodeCategory.Custom"/> when the name is not one of the ten.</returns>
    public static NodeCategory Parse(string? name) => name switch
    {
        Spark.Api.NodeCategories.Input => NodeCategory.Input,
        Spark.Api.NodeCategories.Logic => NodeCategory.Logic,
        Spark.Api.NodeCategories.Display => NodeCategory.Display,
        Spark.Api.NodeCategories.Solid => NodeCategory.Solid,
        Spark.Api.NodeCategories.Curve => NodeCategory.Curve,
        Spark.Api.NodeCategories.Point => NodeCategory.Point,
        Spark.Api.NodeCategories.Script => NodeCategory.Script,
        Spark.Api.NodeCategories.List => NodeCategory.List,
        Spark.Api.NodeCategories.Math => NodeCategory.Math,
        _ => NodeCategory.Custom,
    };

    /// <summary>The name a category is written as, in a file and in a menu.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The name <see cref="Parse"/> turns back into it.</returns>
    /// <remarks>
    /// The inverse of <see cref="Parse"/>, and it exists because a node can borrow another
    /// category's colour (`E8-T35`) and that choice is saved. A token rather than a hex value, so
    /// the palette can be re-tuned for contrast without rewriting everybody's graphs.
    /// </remarks>
    public static string NameOf(NodeCategory category) => category switch
    {
        NodeCategory.Input => Spark.Api.NodeCategories.Input,
        NodeCategory.Logic => Spark.Api.NodeCategories.Logic,
        NodeCategory.Display => Spark.Api.NodeCategories.Display,
        NodeCategory.Solid => Spark.Api.NodeCategories.Solid,
        NodeCategory.Curve => Spark.Api.NodeCategories.Curve,
        NodeCategory.Point => Spark.Api.NodeCategories.Point,
        NodeCategory.Script => Spark.Api.NodeCategories.Script,
        NodeCategory.List => Spark.Api.NodeCategories.List,
        NodeCategory.Math => Spark.Api.NodeCategories.Math,
        _ => "custom",
    };
}

/// <summary>
/// Category colours, and the two derived values a node renderer needs on every frame.
/// </summary>
/// <remarks>
/// <b>Category colours are only ever fills, never strokes</b> (Principle 4). That is what stops
/// <c>cat.display</c> green from being read as a success state and <c>cat.math</c> orange from
/// being read as a warning, even though they sit in neighbouring hues to the semantic set.
/// </remarks>
public static class NodeCategoryColours
{
    private static readonly Color[] Rest =
    [
        Color.FromRgb(0xE8, 0xC4, 0x5A),   // Input
        Color.FromRgb(0xB6, 0xC4, 0x55),   // Logic
        Color.FromRgb(0x71, 0xC8, 0x62),   // Display
        Color.FromRgb(0x33, 0xB9, 0x92),   // Solid
        Color.FromRgb(0x4C, 0xBC, 0xD4),   // Curve
        Color.FromRgb(0x5A, 0xA2, 0xEA),   // Point
        Color.FromRgb(0x77, 0x89, 0xEA),   // Script
        Color.FromRgb(0xE4, 0x89, 0xC4),   // List
        Color.FromRgb(0xDE, 0x7B, 0x50),   // Math
        Color.FromRgb(0x9A, 0xA3, 0xB2),   // Custom
    ];

    private static readonly Color[] Hover =
    [
        Color.FromRgb(0xEB, 0xCC, 0x71),   // Input     10.55 -> 11.34
        Color.FromRgb(0xC0, 0xCC, 0x6D),   // Logic      9.31 -> 10.23
        Color.FromRgb(0x85, 0xD0, 0x78),   // Display    8.57 ->  9.54
        Color.FromRgb(0x50, 0xC3, 0xA1),   // Solid      7.18 ->  8.17
        Color.FromRgb(0x65, 0xC5, 0xDA),   // Curve      7.99 ->  8.92
        Color.FromRgb(0x71, 0xAF, 0xED),   // Point      6.58 ->  7.66
        Color.FromRgb(0x8A, 0x9A, 0xED),   // Script     5.54 ->  6.70
        Color.FromRgb(0xE8, 0x9A, 0xCC),   // List       7.33 ->  8.39
        Color.FromRgb(0xE3, 0x8D, 0x68),   // Math       5.96 ->  6.99
        Color.FromRgb(0xA8, 0xB0, 0xBD),   // Custom     6.98 ->  8.12
    ];

    private static readonly IBrush[] RestBrushes = BuildBrushes(Rest);
    private static readonly IBrush[] HoverBrushes = BuildBrushes(Hover);

    /// <summary>The full-strength header fill for a category.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The colour.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The category is not one of the ten.</exception>
    public static Color ColourOf(NodeCategory category) => Rest[Index(category)];

    /// <summary>
    /// The hovered header fill: the rest colour mixed 14% towards white. The header is the one
    /// part of a node whose hover <i>brightens</i>, because it carries dark text and brightening
    /// therefore raises contrast (§5.1).
    /// </summary>
    /// <param name="category">The category.</param>
    /// <returns>The colour.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The category is not one of the ten.</exception>
    public static Color HoverColourOf(NodeCategory category) => Hover[Index(category)];

    /// <summary>A frozen brush for <see cref="ColourOf(NodeCategory)"/>.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The brush.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The category is not one of the ten.</exception>
    public static IBrush BrushOf(NodeCategory category) => RestBrushes[Index(category)];

    /// <summary>A frozen brush for <see cref="HoverColourOf(NodeCategory)"/>.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The brush.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The category is not one of the ten.</exception>
    public static IBrush HoverBrushOf(NodeCategory category) => HoverBrushes[Index(category)];

    private static int Index(NodeCategory category)
    {
        int index = (int)category;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(category));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Rest.Length, nameof(category));
        return index;
    }

    private static IBrush[] BuildBrushes(Color[] colours)
    {
        IBrush[] brushes = new IBrush[colours.Length];
        for (int i = 0; i < colours.Length; i++)
        {
            brushes[i] = new ImmutableSolidColorBrush(colours[i]);
        }

        return brushes;
    }
}
