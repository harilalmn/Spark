using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using Spark.UI.Theming;

namespace Spark.UI.Tests;

/// <summary>
/// Every syntax colour in the code editor is legible on the editor's own ground (`E6-T21`).
/// </summary>
/// <remarks>
/// <para>
/// <b>This test is owed, and it was written after the defect it would have caught.</b> The editor
/// was recoloured onto Spark's palette by naming twenty-four of AvaloniaEdit's highlighting colours
/// and remapping exactly those. Three were missed. One of them, <c>StringInterpolation</c>, is
/// <c>#000000</c> — so the code inside a <c>$"{...}"</c> hole was black on <c>#1A1E24</c>, a
/// contrast of <b>1.26:1</b>, and the first person to type an interpolated string could not see
/// what they had written. They reported it, which is the second time in one session that a person
/// found a rendering defect the tests could not.
/// </para>
/// <para>
/// <b>So this walks the definition rather than a list.</b> A test that checked the same twenty-four
/// names the code already knows about would have passed while the defect was on screen — it would
/// only have restated the map. What makes it worth having is that it enumerates
/// <see cref="IHighlightingDefinition.NamedHighlightingColors"/>, so a name Spark has never heard
/// of fails here rather than in front of a user, including one a future AvaloniaEdit adds.
/// </para>
/// <para>
/// The floor is <see cref="BodyTextFloor"/>, the same 4.5:1 <c>PaletteContrastTests</c> holds every
/// other piece of body text to. Code in an editor is body text: it is read continuously, at small
/// sizes, by someone who has to spot a single wrong character in it.
/// </para>
/// </remarks>
public sealed class EditorHighlightContrastTests
{
    /// <summary>The WCAG AA floor for body text, and what the rest of the palette is held to.</summary>
    private const double BodyTextFloor = 4.5;

    /// <summary><c>surface.sunken</c>, which is what the editor paints itself.</summary>
    private static Color Ground => SparkPalette.SurfaceSunken;

    /// <summary>
    /// <b>Every named colour clears the floor, including the ones Spark does not name.</b>
    /// </summary>
    [Fact]
    public void EverySyntaxColourIsLegibleOnTheEditorGround()
    {
        IHighlightingDefinition highlighting = Recoloured();

        List<string> failures = [];

        foreach (HighlightingColor colour in highlighting.NamedHighlightingColors)
        {
            if (colour.Foreground?.GetColor(null) is not { } foreground)
            {
                // No foreground of its own: it inherits the editor's, which is text.primary, and
                // that pairing is asserted below rather than guessed at here.
                continue;
            }

            double ratio = Ratio(Ground, foreground);

            if (ratio < BodyTextFloor)
            {
                failures.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} is #{1:X2}{2:X2}{3:X2} at {4:F2}:1",
                    colour.Name,
                    foreground.R,
                    foreground.G,
                    foreground.B,
                    ratio));
            }
        }

        Assert.True(
            failures.Count == 0,
            "These syntax colours are below the 4.5:1 floor on surface.sunken:\n  "
            + string.Join("\n  ", failures));
    }

    /// <summary>
    /// <b>The one that was actually invisible</b>, named on its own so a regression says what
    /// broke rather than only that something did.
    /// </summary>
    [Fact]
    public void TheStringInterpolationHoleIsNotBlack()
    {
        HighlightingColor? colour = Recoloured().GetNamedColor("StringInterpolation");

        Assert.NotNull(colour);

        Color foreground = Assert.IsType<Color>(colour!.Foreground?.GetColor(null), exactMatch: false);

        Assert.NotEqual(Colors.Black, foreground);
        Assert.True(
            Ratio(Ground, foreground) >= BodyTextFloor,
            "the code inside a $\"{...}\" hole is not legible on the editor's ground");
    }

    /// <summary>
    /// A colour that inherits sets no foreground, so it takes the editor's — which must itself
    /// clear the floor, or the inheriting colours quietly do not.
    /// </summary>
    [Fact]
    public void TheInheritedForegroundClearsTheFloor() =>
        Assert.True(Ratio(Ground, SparkPalette.TextPrimary) >= BodyTextFloor);

    /// <summary>
    /// <b>Applying twice is the same as applying once.</b> The definition is shared per process, so
    /// every editor Spark opens runs this over the same object; an assignment that compounded
    /// would drift a shade darker on each code block placed.
    /// </summary>
    [Fact]
    public void ApplyingTwiceChangesNothing()
    {
        IHighlightingDefinition highlighting = Recoloured();

        Dictionary<string, Color> before = Snapshot(highlighting);

        EditorHighlightPalette.Apply(highlighting);

        Assert.Equal(before, Snapshot(highlighting));
    }

    /// <summary>The gutter is supporting text, so it is held to the lower non-text floor of 3:1.</summary>
    [Fact]
    public void TheLineNumberGutterIsStillReadable() =>
        Assert.True(Ratio(Ground, SparkPalette.TextMuted) >= 3.0);

    private static IHighlightingDefinition Recoloured()
    {
        IHighlightingDefinition highlighting = HighlightingManager.Instance.GetDefinition("C#");

        Assert.NotNull(highlighting);

        // The definition is shared and other tests may already have recoloured it. Applying again
        // is deliberate and is asserted harmless by ApplyingTwiceChangesNothing.
        EditorHighlightPalette.Apply(highlighting);

        return highlighting;
    }

    private static Dictionary<string, Color> Snapshot(IHighlightingDefinition highlighting)
    {
        Dictionary<string, Color> map = new(StringComparer.Ordinal);

        foreach (HighlightingColor colour in highlighting.NamedHighlightingColors)
        {
            if (colour.Name is { } name && colour.Foreground?.GetColor(null) is { } foreground)
            {
                map[name] = foreground;
            }
        }

        return map;
    }

    private static double Ratio(Color a, Color b)
    {
        double first = Luminance(a);
        double second = Luminance(b);

        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double Luminance(Color colour) =>
        (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));

    private static double Channel(byte value)
    {
        double linear = value / 255.0;

        return linear <= 0.03928 ? linear / 12.92 : Math.Pow((linear + 0.055) / 1.055, 2.4);
    }
}
