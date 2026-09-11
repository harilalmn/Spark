using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Spark.UI.Views.Panes;

namespace Spark.UI.Tests;

/// <summary>
/// A warning message carries a warning triangle before it — the client's standing rule, given on
/// 2026-09-11 with a screenshot: <i>"Always add warning triangle before the message, both in banner
/// and in properties window."</i>
/// </summary>
/// <remarks>
/// <para>
/// <b>"Always" is why this is a test and not only a screenshot.</b> The rule is about every warning,
/// including the next one somebody adds, so what is asserted is the structure the rule needs — the
/// glyph, first, in the class every warning shares — rather than a picture of one banner.
/// </para>
/// <para>
/// <b>The headless application does not load Spark's theme</b> (see <c>HeadlessTestApplication</c>),
/// so the glyph's shape and colour are checked by loading the theme dictionary directly: a
/// misspelt key would otherwise draw nothing, silently, on the real window only.
/// </para>
/// </remarks>
public sealed class WarningGlyphTests
{
    /// <summary>Both warnings in the Properties pane put the triangle before their message.</summary>
    [Theory]
    [InlineData("TrustBannerGlyph")]
    [InlineData("PackageTrustBannerGlyph")]
    public void EveryPropertiesWarningHasTheTriangleBeforeItsMessage(string name) => HeadlessSession.Run(() =>
    {
        InspectorPane pane = new();

        PathIcon glyph = Assert.IsType<PathIcon>(
            pane.GetLogicalDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name));

        Assert.Contains("warning", glyph.Classes);
        Assert.Equal(0, Grid.GetColumn(glyph));

        Grid row = Assert.IsType<Grid>(glyph.Parent);
        TextBlock message = Assert.Single(row.Children.OfType<TextBlock>());

        Assert.Equal(1, Grid.GetColumn(message));
    });

    /// <summary>The theme defines the glyph the <c>warning</c> class draws, and its amber.</summary>
    [Fact]
    public void TheThemeDefinesTheGlyphAndItsColour() => HeadlessSession.Run(() =>
    {
        ResourceInclude theme = new(new Uri("avares://Spark.UI/"))
        {
            Source = new Uri("avares://Spark.UI/Theming/SparkTheme.axaml"),
        };

        Assert.True(theme.TryGetResource("SparkWarningGlyph", null, out object? glyph));
        // Spelled out: inside `Spark.UI.Tests`, a bare `Geometry` is the `Spark.Geometry` namespace.
        Assert.IsAssignableFrom<Avalonia.Media.Geometry>(glyph);

        Assert.True(theme.TryGetResource("SparkStateWarningBrush", null, out object? brush));
        Assert.IsAssignableFrom<IBrush>(brush);
    });
}
