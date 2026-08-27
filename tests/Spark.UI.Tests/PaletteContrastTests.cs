using System;
using Avalonia.Media;
using Spark.UI.Theming;

namespace Spark.UI.Tests;

/// <summary>
/// The two checks <c>docs/help/concepts/design-language.md</c> §14 asks CI to run, implemented
/// against the palette the application actually draws with.
/// </summary>
/// <remarks>
/// <para>
/// <b>The palette check</b> recomputes every ratio the document prints and asserts the
/// implementation reproduces it exactly. The document's figures are truncated downward, so a
/// printed figure is never a rounded-up pass, and these assertions truncate the same way.
/// </para>
/// <para>
/// <b>The monotonicity check</b> asserts Principle 2 as an invariant: every state transition
/// raises contrast or leaves it alone, and none lowers it. That is the failure this style is prone
/// to, and it is the one a reviewer cannot reliably catch by eye.
/// </para>
/// <para>
/// If a future change makes one of these numbers wrong, the correct response is to change the
/// palette until the number is right again — not to edit the number.
/// </para>
/// </remarks>
public sealed class PaletteContrastTests
{
    private const double BodyTextFloor = 4.5;
    private const double NonTextFloor = 3.0;

    [Theory]
    [InlineData("bg.void", 16.74, 11.44, 8.07, 6.68)]
    [InlineData("canvas.bg", 15.81, 10.80, 7.63, 6.31)]
    [InlineData("canvas.group", 14.58, 9.97, 7.03, 5.82)]
    [InlineData("surface.sunken", 15.31, 10.46, 7.38, 6.11)]
    [InlineData("surface.base", 13.69, 9.36, 6.61, 5.47)]
    [InlineData("node.body", 13.02, 8.90, 6.28, 5.20)]
    [InlineData("surface.raised", 12.30, 8.40, 5.93, 4.91)]
    [InlineData("surface.float", 11.42, 7.81, 5.51, 4.56)]
    public void EveryTextTokenOnEverySurfaceMatchesTheTable(
        string surfaceName, double primary, double secondary, double muted, double disabled)
    {
        Color surface = SurfaceNamed(surfaceName);

        AssertRatio(primary, surface, SparkPalette.TextPrimary, $"text.primary on {surfaceName}");
        AssertRatio(secondary, surface, SparkPalette.TextSecondary, $"text.secondary on {surfaceName}");
        AssertRatio(muted, surface, SparkPalette.TextMuted, $"text.muted on {surfaceName}");
        AssertRatio(disabled, surface, SparkPalette.TextDisabled, $"text.disabled on {surfaceName}");

        // Spark does not take WCAG's disabled-text exemption: "you cannot use this" and "you
        // cannot read this" are different statements.
        Assert.True(disabled >= BodyTextFloor, $"text.disabled on {surfaceName} is below the 4.5:1 floor.");
    }

    [Fact]
    public void TheLowestTextRatioAnywhereIsDisabledTextInAnOpenMenu()
    {
        // 4.56:1, clearing the floor by 0.06. That pairing is the binding constraint on the whole
        // palette: it is why surface.float is #2E3440 and not lighter, and why text.disabled is
        // #949DAC and not dimmer.
        double lowest = Ratio(SparkPalette.SurfaceFloat, SparkPalette.TextDisabled);

        Assert.Equal(4.56, Truncate(lowest));
        Assert.True(lowest > BodyTextFloor);
    }

    [Theory]
    [InlineData("canvas.bg", 6.43)]
    [InlineData("surface.sunken", 6.23)]
    [InlineData("surface.base", 5.57)]
    [InlineData("surface.raised", 5.00)]
    [InlineData("surface.float", 4.65)]
    [InlineData("node.body", 5.30)]
    public void AccentUsedAsTextOrAGlyphMatchesTheTable(string surfaceName, double expected) =>
        AssertRatio(expected, SurfaceNamed(surfaceName), SparkPalette.Accent, $"accent on {surfaceName}");

    [Theory]
    [InlineData("canvas.bg", 4.64)]
    [InlineData("surface.sunken", 4.49)]
    [InlineData("surface.base", 4.02)]
    [InlineData("surface.raised", 3.61)]
    [InlineData("surface.float", 3.35)]
    [InlineData("node.body", 3.82)]
    public void TheBoundaryTokenClearsThreeToOneOnEverySurface(string surfaceName, double expected)
    {
        AssertRatio(expected, SurfaceNamed(surfaceName), SparkPalette.BorderControl, $"border.control on {surfaceName}");
        Assert.True(expected >= NonTextFloor);
    }

    [Fact]
    public void DarkTextOnEveryBrightFillClearsTheBodyFloor()
    {
        (string Name, Color Fill, double Expected)[] fills =
        [
            ("accent", SparkPalette.Accent, 6.61),
            ("accent.hover", SparkPalette.AccentHover, 8.69),
            ("accent.press", SparkPalette.AccentPress, 11.11),
            ("state.error", SparkPalette.StateError, 7.11),
            ("state.warning", SparkPalette.StateWarning, 8.64),
            ("state.success", SparkPalette.StateSuccess, 9.53),
            ("state.info", SparkPalette.StateInfo, 8.08),
        ];

        foreach ((string name, Color fill, double expected) in fills)
        {
            AssertRatio(expected, fill, SparkPalette.TextInverse, $"text.inverse on {name}");
            Assert.True(expected >= BodyTextFloor);
        }
    }

    [Fact]
    public void HoverAndPressRaiseTextContrastOnEverySurfaceLadder()
    {
        // §5.1 stated as an invariant. A dark surface with light text gets DARKER on hover, which
        // is unusual, deliberate, and the only direction that satisfies Principle 2 here.
        AssertRising("surface.base", SparkPalette.TextSecondary,
            SparkPalette.SurfaceBase, SparkPalette.SurfaceBaseHover, SparkPalette.SurfaceBasePressed);

        AssertRising("surface.raised", SparkPalette.TextPrimary,
            SparkPalette.SurfaceRaised, SparkPalette.SurfaceRaisedHover, SparkPalette.SurfaceRaisedPressed);

        AssertRising("node.body", SparkPalette.TextPrimary,
            SparkPalette.NodeBody, SparkPalette.NodeBodyHover, SparkPalette.NodeBodySelected);

        AssertRising("node.body", SparkPalette.TextSecondary,
            SparkPalette.NodeBody, SparkPalette.NodeBodyHover, SparkPalette.NodeBodySelected);
    }

    [Fact]
    public void TheWorkedHoverRatiosMatchTheTable()
    {
        Assert.Equal(12.30, Truncate(Ratio(SparkPalette.SurfaceRaised, SparkPalette.TextPrimary)));
        Assert.Equal(13.55, Truncate(Ratio(SparkPalette.SurfaceRaisedHover, SparkPalette.TextPrimary)));
        Assert.Equal(14.95, Truncate(Ratio(SparkPalette.SurfaceRaisedPressed, SparkPalette.TextPrimary)));

        Assert.Equal(13.02, Truncate(Ratio(SparkPalette.NodeBody, SparkPalette.TextPrimary)));
        Assert.Equal(14.24, Truncate(Ratio(SparkPalette.NodeBodyHover, SparkPalette.TextPrimary)));
        Assert.Equal(15.12, Truncate(Ratio(SparkPalette.NodeBodySelected, SparkPalette.TextPrimary)));

        Assert.Equal(8.90, Truncate(Ratio(SparkPalette.NodeBody, SparkPalette.TextSecondary)));
        Assert.Equal(9.73, Truncate(Ratio(SparkPalette.NodeBodyHover, SparkPalette.TextSecondary)));
        Assert.Equal(10.33, Truncate(Ratio(SparkPalette.NodeBodySelected, SparkPalette.TextSecondary)));
    }

    [Fact]
    public void TheWireCasingAndCorePairAlwaysLeavesOneStrokeAboveThreeToOne()
    {
        // Decision V9. There is no single stroke colour that clears 3:1 against both the near-black
        // canvas and a node header at L* 80, so the wire is drawn as a casing and a core and the
        // guarantee is that at least one of the two always has the contrast.
        Color[] backdrops =
        [
            SparkPalette.CanvasBackground,
            SparkPalette.CanvasGroup,
            SparkPalette.NodeBody,
            .. AllCategoryColours(),
        ];

        foreach (Color backdrop in backdrops)
        {
            double core = Ratio(backdrop, SparkPalette.WireCore);
            double casing = Ratio(backdrop, SparkPalette.WireCasing);

            Assert.True(
                Math.Max(core, casing) >= NonTextFloor,
                $"Neither the wire core ({core:F2}) nor its casing ({casing:F2}) clears 3:1 on {backdrop}.");
        }

        Assert.Equal(10.81, Truncate(Ratio(SparkPalette.CanvasBackground, SparkPalette.WireCore)));
        Assert.Equal(11.23, Truncate(Ratio(NodeCategoryColours.ColourOf(NodeCategory.Input), SparkPalette.WireCasing)));
    }

    [Fact]
    public void TheFocusRingReadsAgainstBothSidesOfItsSandwich()
    {
        // §6. The ring is 11.24:1 against its own dark separators, so its 3:1 requirement holds
        // regardless of what the outer separator lands on.
        Assert.Equal(11.24, Truncate(Ratio(SparkPalette.FocusRing, SparkPalette.FocusContour)));

        // And the separator itself reads against the arbitrary backdrops the sandwich exists for:
        // an accent fill, the brightest node header, and body text.
        Assert.Equal(7.19, Truncate(Ratio(SparkPalette.FocusContour, SparkPalette.Accent)));
        Assert.Equal(11.46, Truncate(Ratio(
            SparkPalette.FocusContour, NodeCategoryColours.ColourOf(NodeCategory.Input))));
        Assert.Equal(17.66, Truncate(Ratio(SparkPalette.FocusContour, SparkPalette.TextPrimary)));

        foreach (Color category in AllCategoryColours())
        {
            Assert.True(
                Ratio(SparkPalette.FocusContour, category) >= NonTextFloor,
                $"The focus contour is below 3:1 against a node header at {category}.");
        }
    }

    [Fact]
    public void TheFocusRingAloneAlreadyReadsAgainstEverySurface()
    {
        // The sandwich is for arbitrary backdrops; on an ordinary panel the ring does not need it.
        // §4.6 prints the range as 7.27 to 10.06.
        string[] surfaces = ["canvas.bg", "surface.sunken", "surface.base", "surface.raised", "surface.float", "node.body"];

        foreach (string name in surfaces)
        {
            double ratio = Ratio(SurfaceNamed(name), SparkPalette.FocusRing);
            Assert.InRange(ratio, 7.27, 10.07);
        }

        Assert.Equal(10.06, Truncate(Ratio(SparkPalette.CanvasBackground, SparkPalette.FocusRing)));
        Assert.Equal(7.27, Truncate(Ratio(SparkPalette.SurfaceFloat, SparkPalette.FocusRing)));
    }

    [Fact]
    public void AnUnconnectedPortClearsTheGlyphFloorOnItsNodeAndOnTheCanvas()
    {
        Assert.Equal(4.59, Truncate(Ratio(SparkPalette.NodeBody, SparkPalette.PortRest)));
        Assert.True(Ratio(SparkPalette.CanvasBackground, SparkPalette.PortRest) >= NonTextFloor);

        // port.connected is the same value as wire.core on purpose, so a wire terminates in its
        // port rather than stopping next to it.
        Assert.Equal(SparkPalette.WireCore, SparkPalette.PortConnected);
    }

    [Fact]
    public void MixingIsClampedAndEndpointExact()
    {
        Assert.Equal(SparkPalette.NodeBody, SparkPalette.Mix(SparkPalette.NodeBody, SparkPalette.Accent, -1));
        Assert.Equal(SparkPalette.Accent, SparkPalette.Mix(SparkPalette.NodeBody, SparkPalette.Accent, 2));
    }

    private static void AssertRising(string surfaceName, Color text, Color rest, Color hover, Color pressed)
    {
        double atRest = Ratio(rest, text);
        double atHover = Ratio(hover, text);
        double atPressed = Ratio(pressed, text);

        Assert.True(atHover >= atRest, $"{surfaceName} hover lowered contrast: {atRest:F2} to {atHover:F2}.");
        Assert.True(atPressed >= atHover, $"{surfaceName} pressed lowered contrast: {atHover:F2} to {atPressed:F2}.");
    }

    private static void AssertRatio(double expected, Color a, Color b, string what)
    {
        double actual = Truncate(Ratio(a, b));
        Assert.True(
            expected == actual,
            $"{what}: the design language prints {expected:F2}:1, the palette gives {actual:F2}:1.");
    }

    private static Color SurfaceNamed(string name) => name switch
    {
        "bg.void" => SparkPalette.BackgroundVoid,
        "canvas.bg" => SparkPalette.CanvasBackground,
        "canvas.group" => SparkPalette.CanvasGroup,
        "surface.sunken" => SparkPalette.SurfaceSunken,
        "surface.base" => SparkPalette.SurfaceBase,
        "node.body" => SparkPalette.NodeBody,
        "surface.raised" => SparkPalette.SurfaceRaised,
        "surface.float" => SparkPalette.SurfaceFloat,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown surface token."),
    };

    private static Color[] AllCategoryColours()
    {
        NodeCategory[] categories = Enum.GetValues<NodeCategory>();
        Color[] colours = new Color[categories.Length];

        for (int i = 0; i < categories.Length; i++)
        {
            colours[i] = NodeCategoryColours.ColourOf(categories[i]);
        }

        return colours;
    }

    /// <summary>Truncates downward to two decimals, the way the design language prints its figures.</summary>
    /// <param name="value">The ratio.</param>
    /// <returns>The truncated ratio.</returns>
    internal static double Truncate(double value) => Math.Floor(value * 100) / 100;

    /// <summary>The WCAG 2.2 contrast ratio between two sRGB colours.</summary>
    /// <param name="a">The first colour.</param>
    /// <param name="b">The second colour.</param>
    /// <returns>A ratio between 1 and 21.</returns>
    internal static double Ratio(Color a, Color b)
    {
        double first = RelativeLuminance(a);
        double second = RelativeLuminance(b);
        double lighter = Math.Max(first, second);
        double darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color colour) =>
        (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));

    private static double Channel(byte value)
    {
        double c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
