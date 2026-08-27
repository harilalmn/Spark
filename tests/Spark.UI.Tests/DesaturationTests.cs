using Avalonia.Media;
using Spark.UI.Theming;

namespace Spark.UI.Tests;

/// <summary>
/// The luminance-preserving desaturation §7.7 uses for a node that did not run.
/// </summary>
/// <remarks>
/// Three states — frozen, preview off and not evaluated — all mean "this node is not currently
/// contributing", and all three are easy to implement badly by fading the node until it cannot be
/// read. A user must still be able to read a node that did not run, because reading it is how they
/// work out what should have run. Substituting the grey of <i>identical</i> relative luminance
/// carries the state on hue alone, which costs no contrast at all.
/// </remarks>
public sealed class DesaturationTests
{
    /// <summary>The two substitutions the design language prints by name.</summary>
    [Theory]
    [InlineData(0x5A, 0xA2, 0xEA, 0x9E)]   // cat.point  #5AA2EA -> #9E9E9E
    [InlineData(0xDE, 0x7B, 0x50, 0x96)]   // cat.math   #DE7B50 -> #969696
    public void DesaturationProducesTheGreyTheDocumentNames(int r, int g, int b, int expected)
    {
        Color grey = SparkPalette.Desaturate(Color.FromRgb((byte)r, (byte)g, (byte)b));

        Assert.Equal(grey.R, grey.G);
        Assert.Equal(grey.G, grey.B);

        // Within one 8-bit code value of the figure in §7.7.
        Assert.InRange(grey.R, expected - 1, expected + 1);
    }

    /// <summary>
    /// Header text contrast survives the substitution to within a hundredth, which is the whole
    /// claim: the state costs hue, not legibility.
    /// </summary>
    [Fact]
    public void HeaderTextContrastIsUnchangedByDesaturation()
    {
        foreach (NodeCategory category in System.Enum.GetValues<NodeCategory>())
        {
            Color colour = NodeCategoryColours.ColourOf(category);
            Color grey = SparkPalette.Desaturate(colour);

            double before = PaletteContrastTests.Ratio(colour, SparkPalette.TextInverse);
            double after = PaletteContrastTests.Ratio(grey, SparkPalette.TextInverse);

            Assert.True(
                System.Math.Abs(after - before) < 0.05,
                $"{category}: header text contrast moved from {before:F2} to {after:F2} under desaturation.");

            // And it never drops below the non-text floor, in either form.
            Assert.True(after >= 3.0, $"{category}: desaturated header reads {after:F2} against its text.");
        }
    }

    /// <summary>
    /// A desaturated header still clears 3:1 against the canvas, so a not-evaluated node is still
    /// findable at the level-of-detail zoom where the fill is all that is left.
    /// </summary>
    [Fact]
    public void ADesaturatedHeaderIsStillVisibleAgainstTheCanvas()
    {
        foreach (NodeCategory category in System.Enum.GetValues<NodeCategory>())
        {
            Color grey = SparkPalette.Desaturate(NodeCategoryColours.ColourOf(category));
            double ratio = PaletteContrastTests.Ratio(grey, SparkPalette.CanvasBackground);

            Assert.True(ratio >= 3.0, $"{category}: desaturated to {ratio:F2} against canvas.bg.");
        }
    }
}
