using System;
using Avalonia.Media;
using Spark.UI.Canvas;
using Spark.UI.Graph;
using Spark.UI.Theming;

namespace Spark.UI.Tests;

/// <summary>
/// `E8-T67` — the line and the tint step that divide a node's inputs from its outputs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: a faint grey line down the middle of a node, and a very small
/// difference in tint either side of it. What it says is that the two columns mean different
/// things — the left edge is where wires arrive and the right edge is where they leave — which is
/// the first thing anybody has to learn about a node and the one thing the drawing never said.
/// </para>
/// <para>
/// <b>Two claims are testable and one is not.</b> The geometry is, because it is a method on
/// <see cref="CanvasNode"/> rather than arithmetic inside a draw loop; and the contrast is,
/// because the wash is a palette token. That the line <i>looks</i> faint rather than heavy is
/// neither, and is checked by a screenshot.
/// </para>
/// </remarks>
public sealed class NodeBodyDivideTests
{
    private static CanvasNode Node(string name)
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName(name), 40, 60);

        return graph.Nodes[0];
    }

    /// <summary>The line is down the middle of the node, wherever the node happens to be.</summary>
    [Fact]
    public void TheDivideIsAtTheMiddleOfTheNode()
    {
        CanvasNode node = Node("Colour.FromRgb");

        node.BodyDivide(out double x, out _, out _);

        Assert.Equal(node.X + (node.Width / 2), x, 6);
    }

    /// <summary>
    /// <b>It starts below the header and not at the top of the node.</b> The header carries one
    /// title across the whole width and a state glyph at its right end, so a line through it would
    /// divide something that is not divided.
    /// </summary>
    [Fact]
    public void TheDivideStartsBelowTheHeaderAndEndsAtTheFoot()
    {
        CanvasNode node = Node("Colour.FromRgb");

        node.BodyDivide(out _, out double top, out double bottom);

        Assert.Equal(node.Y + CanvasNode.HeaderHeight, top, 6);
        Assert.Equal(node.Y + node.Height, bottom, 6);
        Assert.True(bottom > top, "the divide has no length");
    }

    /// <summary>
    /// It clears the port tabs on both sides, so the line never crosses a lozenge. A node whose
    /// tabs met in the middle would have no two columns to separate in the first place.
    /// </summary>
    [Theory]
    [InlineData("Colour.FromRgb")]
    [InlineData("Circle.FromCenterRadius")]
    [InlineData("Math.Divide")]
    [InlineData("Point.Translate")]
    public void TheDivideFallsBetweenTheTwoColumnsOfTabs(string name)
    {
        CanvasNode node = Node(name);

        node.BodyDivide(out double x, out _, out _);

        for (int index = 0; index < node.Inputs.Count; index++)
        {
            node.PortTab(index, isOutput: false, out _, out _, out double right, out _);

            Assert.True(right < x, $"{name} input {index} crosses the divide: {right:F1} >= {x:F1}");
        }

        for (int index = 0; index < node.Outputs.Count; index++)
        {
            node.PortTab(index, isOutput: true, out double left, out _, out _, out _);

            Assert.True(left > x, $"{name} output {index} crosses the divide: {left:F1} <= {x:F1}");
        }
    }

    /// <summary>
    /// <b>The wash darkens, and Principle 2 is why.</b> Body text on a node is light, so a
    /// surface that gets darker under it gets <i>more</i> legible — the same direction hover and
    /// selection step the fill. A wash that brightened the half would have to be argued for
    /// against every text token drawn on it; this one cannot lower a ratio at all.
    /// </summary>
    [Theory]
    [InlineData(0x26, 0x2B, 0x33)]
    [InlineData(0x20, 0x24, 0x2B)]
    [InlineData(0x1B, 0x1F, 0x26)]
    public void TheOutputHalfIsNeverLessLegibleThanTheInputHalf(byte red, byte green, byte blue)
    {
        Color body = Color.FromRgb(red, green, blue);
        Color washed = Washed(body);

        foreach (Color text in new[]
        {
            SparkPalette.TextPrimary,
            SparkPalette.TextSecondary,
            SparkPalette.TextMuted,
            SparkPalette.TextDisabled,
        })
        {
            Assert.True(
                Ratio(washed, text) >= Ratio(body, text),
                $"the wash lowered contrast on {body} from {Ratio(body, text):F2} to {Ratio(washed, text):F2}");
        }
    }

    /// <summary>
    /// <b>And it is a step, not a repaint.</b> The client asked for *a very small difference*, so
    /// the washed half must differ from the body by less than one rung of the body ladder —
    /// <c>node.body</c> to <c>node.body.hover</c> is six code values, and this is under it.
    /// </summary>
    [Fact]
    public void TheTintStepIsSmallerThanOneRungOfTheBodyLadder()
    {
        Color washed = Washed(SparkPalette.NodeBody);

        int step = Math.Abs(SparkPalette.NodeBody.R - washed.R);
        int rung = Math.Abs(SparkPalette.NodeBody.R - SparkPalette.NodeBodyHover.R);

        Assert.True(step > 0, "the wash is invisible");
        Assert.True(step < rung, $"the wash is a whole ladder rung: {step} vs {rung}");
    }

    /// <summary>
    /// The hairline is the palette's quietest line. A divider inside a surface must not read as an
    /// edge between two things, which is what <c>border.control</c> would have made it.
    /// </summary>
    [Fact]
    public void TheLineIsTheQuietestBorderInThePalette()
    {
        Assert.True(
            Ratio(SparkPalette.NodeBody, SparkPalette.BorderHairline)
                < Ratio(SparkPalette.NodeBody, SparkPalette.BorderControl),
            "the divider is louder than the node's own outline");
    }

    /// <summary>
    /// <b>Nothing is drawn below 67% zoom</b>, which is the level the header title appears at and
    /// the level at and below which the body lerps towards the category colour. Below it there
    /// are no port labels to separate, and the wash would land on a brightened fill.
    /// </summary>
    [Fact]
    public void TheDivideIsGatedOnTheLevelThatDrawsTheTitle()
    {
        Assert.False(CanvasLevelOfDetail.DrawsTitle(CanvasLevelOfDetail.For(0.5)));
        Assert.True(CanvasLevelOfDetail.DrawsTitle(CanvasLevelOfDetail.For(0.7)));

        // And the gate is exactly where the category lerp has finished, so the two never overlap.
        Assert.Equal(0, CanvasLevelOfDetail.CategoryFillBlend(CanvasLevelOfDetail.TextThreshold));
    }

    /// <summary>Composites the output wash over a body colour, the way the renderer does.</summary>
    private static Color Washed(Color body) =>
        SparkPalette.Mix(body, Color.FromRgb(SparkPalette.NodeOutputTint.R, SparkPalette.NodeOutputTint.G, SparkPalette.NodeOutputTint.B), SparkPalette.NodeOutputTint.A / 255.0);

    private static double Ratio(Color a, Color b)
    {
        double first = Luminance(a);
        double second = Luminance(b);

        return first > second
            ? (first + 0.05) / (second + 0.05)
            : (second + 0.05) / (first + 0.05);
    }

    private static double Luminance(Color colour) =>
        (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));

    private static double Channel(byte value)
    {
        double v = value / 255.0;

        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
