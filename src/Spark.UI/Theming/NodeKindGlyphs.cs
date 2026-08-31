using System;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Spark.Api;

namespace Spark.UI.Theming;

/// <summary>
/// The rail colour and the glyph the library panel draws beside each of the three node kinds
/// (<c>E8-T29</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Dynamo's three marks, deliberately.</b> The client asked for the same icons, and the reason
/// to copy them rather than invent better ones is that the audience already reads them: a green
/// plus, an amber bolt and a blue question mark mean <i>make</i>, <i>do</i> and <i>ask</i> to
/// anybody who has used Dynamo for an afternoon.
/// </para>
/// <para>
/// <b>Three hues that are not the ten category hues</b> (<see cref="NodeCategoryColours"/>) and not
/// the four semantic ones. A rail is a two-pixel line beside a list, never a fill behind text and
/// never a state, so §5.4's rule about accent tints under text does not reach it; what it does have
/// to do is stay distinguishable from its two neighbours at two pixels wide, which is why they are
/// a green, an amber and a blue rather than three tints of one hue.
/// </para>
/// </remarks>
public static class NodeKindGlyphs
{
    /// <summary>The rail beside a <see cref="NodeMemberKind.Create"/> group. <c>#7BC86C</c>.</summary>
    public static Color Create { get; } = Color.FromRgb(0x7B, 0xC8, 0x6C);

    /// <summary>The rail beside an <see cref="NodeMemberKind.Action"/> group. <c>#E0A33A</c>.</summary>
    public static Color Action { get; } = Color.FromRgb(0xE0, 0xA3, 0x3A);

    /// <summary>The rail beside a <see cref="NodeMemberKind.Query"/> group. <c>#5AA2EA</c>.</summary>
    public static Color Query { get; } = Color.FromRgb(0x5A, 0xA2, 0xEA);

    private static readonly IBrush CreateBrush = new ImmutableSolidColorBrush(Create);
    private static readonly IBrush ActionBrush = new ImmutableSolidColorBrush(Action);
    private static readonly IBrush QueryBrush = new ImmutableSolidColorBrush(Query);

    // Drawn as geometry rather than set as text, because a glyph taken from a font is a glyph that
    // is missing on the machine that does not have that font - and the bolt is not in any of them.
    // Each path is authored inside a 16x16 box and scaled by the control that draws it.
    // LAZY, AND THAT IS NOT AN OPTIMISATION. `Geometry.Parse` needs Avalonia's render interface,
    // and a static field initialiser here runs the moment ANY member of this class is touched -
    // including `BrushOf`, which a view model calls while building the library. That made
    // constructing MainWindowViewModel throw in every test with no rendering platform. A brush
    // needs no platform; a geometry does; so only the geometry waits for one.
    private static readonly Lazy<Avalonia.Media.Geometry> CreateGeometry =
        new(() => Avalonia.Media.Geometry.Parse("M7,2 H9 V7 H14 V9 H9 V14 H7 V9 H2 V7 H7 Z"));

    private static readonly Lazy<Avalonia.Media.Geometry> ActionGeometry =
        new(() => Avalonia.Media.Geometry.Parse("M9.5,1.5 L3.5,9.5 H7.5 L6.5,14.5 L12.5,6.5 H8.5 Z"));

    private static readonly Lazy<Avalonia.Media.Geometry> QueryGeometry = new(
        () => Avalonia.Media.Geometry.Parse(
            "M5.1,5.6 A3,3 0 1 1 8,9.2 V10.6 H6.6 V8.2 A1,1 0 0 1 7.6,7.2 " +
            "A1.6,1.6 0 1 0 6.5,5.6 Z M6.6,12 H8.1 V13.6 H6.6 Z"));

    /// <summary>The rail colour for a kind.</summary>
    /// <param name="kind">The kind. <see cref="NodeMemberKind.Auto"/> is not one.</param>
    /// <returns>The brush.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one of the three.</exception>
    public static IBrush BrushOf(NodeMemberKind kind) => kind switch
    {
        NodeMemberKind.Create => CreateBrush,
        NodeMemberKind.Action => ActionBrush,
        NodeMemberKind.Query => QueryBrush,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The glyph for a kind, authored in a sixteen-by-sixteen box.</summary>
    /// <param name="kind">The kind. <see cref="NodeMemberKind.Auto"/> is not one.</param>
    /// <returns>The geometry.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one of the three.</exception>
    public static Avalonia.Media.Geometry GeometryOf(NodeMemberKind kind) => kind switch
    {
        NodeMemberKind.Create => CreateGeometry.Value,
        NodeMemberKind.Action => ActionGeometry.Value,
        NodeMemberKind.Query => QueryGeometry.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The word for a kind, as the panel writes it.
    /// </summary>
    /// <remarks>
    /// Dynamo shows the icon alone. Spark shows the word beside it, because an icon with no label
    /// is a thing a user has to be taught and this one has to be learnable from the panel itself —
    /// and because a screen reader cannot read a path.
    /// </remarks>
    /// <param name="kind">The kind. <see cref="NodeMemberKind.Auto"/> is not one.</param>
    /// <returns>The label.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one of the three.</exception>
    public static string LabelOf(NodeMemberKind kind) => kind switch
    {
        NodeMemberKind.Create => "Create",
        NodeMemberKind.Action => "Action",
        NodeMemberKind.Query => "Query",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>One sentence saying what the kind means, shown as the group's tooltip.</summary>
    /// <param name="kind">The kind. <see cref="NodeMemberKind.Auto"/> is not one.</param>
    /// <returns>The description.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one of the three.</exception>
    public static string DescriptionOf(NodeMemberKind kind) => kind switch
    {
        NodeMemberKind.Create => "Makes a new thing out of values that are not one.",
        NodeMemberKind.Action => "Takes one of these and produces another.",
        NodeMemberKind.Query => "Reports something about one without producing another.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
