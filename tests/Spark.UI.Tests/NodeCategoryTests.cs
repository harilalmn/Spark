using System;
using System.Collections.Generic;
using Avalonia.Media;
using Spark.UI.Theming;

namespace Spark.UI.Tests;

/// <summary>
/// The ten category colours from §7.2. Below 40% zoom a node is a plain coloured rectangle, so
/// <b>colour is the only thing left carrying identity at that scale</b> — which makes every number
/// in this file a functional requirement rather than a taste one.
/// </summary>
public sealed class NodeCategoryTests
{
    [Theory]
    [InlineData(NodeCategory.Input, 10.26, 10.55, 11.34)]
    [InlineData(NodeCategory.Logic, 9.06, 9.31, 10.23)]
    [InlineData(NodeCategory.Display, 8.34, 8.57, 9.54)]
    [InlineData(NodeCategory.Solid, 6.99, 7.18, 8.17)]
    [InlineData(NodeCategory.Curve, 7.77, 7.99, 8.92)]
    [InlineData(NodeCategory.Point, 6.41, 6.58, 7.66)]
    [InlineData(NodeCategory.Script, 5.39, 5.54, 6.70)]
    [InlineData(NodeCategory.List, 7.13, 7.33, 8.39)]
    [InlineData(NodeCategory.Math, 5.80, 5.96, 6.99)]
    [InlineData(NodeCategory.Custom, 6.79, 6.98, 8.12)]
    public void EachCategoryMatchesItsThreePrintedRatios(
        NodeCategory category, double againstCanvas, double headerText, double hoveredHeaderText)
    {
        Color rest = NodeCategoryColours.ColourOf(category);
        Color hover = NodeCategoryColours.HoverColourOf(category);

        Assert.Equal(againstCanvas, PaletteContrastTests.Truncate(
            PaletteContrastTests.Ratio(rest, SparkPalette.CanvasBackground)));
        Assert.Equal(headerText, PaletteContrastTests.Truncate(
            PaletteContrastTests.Ratio(rest, SparkPalette.TextInverse)));
        Assert.Equal(hoveredHeaderText, PaletteContrastTests.Truncate(
            PaletteContrastTests.Ratio(hover, SparkPalette.TextInverse)));

        // A node is a control, so its fill at level of detail is held to the 3:1 boundary floor,
        // and its header text to the 4.5:1 body floor.
        Assert.True(againstCanvas >= 3.0);
        Assert.True(headerText >= 4.5);
    }

    [Fact]
    public void HoveringAHeaderRaisesItsTextContrast()
    {
        // The header is the one part of a node whose hover BRIGHTENS, because it carries dark text
        // — and §5.1's rule is direction-independent: hover moves a surface away from the colour
        // of the text on it.
        foreach (NodeCategory category in Enum.GetValues<NodeCategory>())
        {
            double rest = PaletteContrastTests.Ratio(
                NodeCategoryColours.ColourOf(category), SparkPalette.TextInverse);
            double hover = PaletteContrastTests.Ratio(
                NodeCategoryColours.HoverColourOf(category), SparkPalette.TextInverse);

            Assert.True(hover > rest, $"{category} hover lowered contrast: {rest:F2} to {hover:F2}.");
        }
    }

    [Fact]
    public void AdjacentHuesAreSeparatedInLightnessAsWellAsInHue()
    {
        // §7.2: "Adjacent hues differ by at least 2.77 L*". The categories are declared in hue
        // order, so adjacency here is adjacency in the enum. The separation is what stops the set
        // collapsing into one band in greyscale, in a screenshot posted to a forum, or under
        // protanopia — and it is a real constraint: ten mutually distinguishable hues inside a
        // 60-81 L* band is close to the limit of what is possible, which is why there are ten
        // categories and not fifteen.
        //
        // The floor is asserted at 2.76 rather than 2.77 because the tightest pair, logic to
        // display, is 2.7692 L* apart and the document rounds it up for printing.
        NodeCategory[] categories = Enum.GetValues<NodeCategory>();

        for (int i = 1; i < categories.Length; i++)
        {
            double previous = Lightness(NodeCategoryColours.ColourOf(categories[i - 1]));
            double current = Lightness(NodeCategoryColours.ColourOf(categories[i]));

            Assert.True(
                Math.Abs(current - previous) >= 2.76,
                $"{categories[i - 1]} and {categories[i]} are only {Math.Abs(current - previous):F2} L* apart.");
        }
    }

    [Theory]
    [InlineData(NodeCategory.Input, 80.4)]
    [InlineData(NodeCategory.Logic, 76.1)]
    [InlineData(NodeCategory.Display, 73.4)]
    [InlineData(NodeCategory.Solid, 67.6)]
    [InlineData(NodeCategory.Curve, 71.0)]
    [InlineData(NodeCategory.Point, 64.9)]
    [InlineData(NodeCategory.Script, 59.7)]
    [InlineData(NodeCategory.List, 68.3)]
    [InlineData(NodeCategory.Math, 61.9)]
    [InlineData(NodeCategory.Custom, 66.7)]
    public void EachCategorySitsAtTheLightnessTheDesignLanguagePrints(NodeCategory category, double expected)
    {
        Assert.Equal(expected, Lightness(NodeCategoryColours.ColourOf(category)), 1);

        // The whole set lives inside a narrow band by necessity: too dark and it fails the 3:1
        // floor against the canvas, too light and ten hues stop being distinguishable.
        Assert.InRange(expected, 59, 81);
    }

    [Fact]
    public void EveryCategoryHasAFrozenBrushAndAnUnknownOneThrows()
    {
        foreach (NodeCategory category in Enum.GetValues<NodeCategory>())
        {
            Assert.NotNull(NodeCategoryColours.BrushOf(category));
            Assert.NotNull(NodeCategoryColours.HoverBrushOf(category));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => NodeCategoryColours.ColourOf((NodeCategory)99));
    }

    /// <summary>CIE L* from an sRGB colour, which is the axis the design language separates on.</summary>
    /// <param name="colour">The colour.</param>
    /// <returns>Lightness in the range 0..100.</returns>
    private static double Lightness(Color colour)
    {
        double y = (0.2126 * Linear(colour.R)) + (0.7152 * Linear(colour.G)) + (0.0722 * Linear(colour.B));
        return y > 0.008856 ? (116 * Math.Cbrt(y)) - 16 : 903.3 * y;
    }

    private static double Linear(byte value)
    {
        double c = value / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
