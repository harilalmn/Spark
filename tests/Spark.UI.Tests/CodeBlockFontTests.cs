using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Platform;
using Spark.UI.Theming;

namespace Spark.UI.Tests;

/// <summary>
/// Spark ships the face Dynamo draws its Code Block in, and node sizing depends on its metric —
/// `E8-T57`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: <i>follow the same font as in Dynamo Codeblock</i>. Dynamo
/// bundles two monospaced faces, Source Code Pro and Courier Prime; Courier Prime is a slab serif
/// and the screenshot plainly is not one, and Dynamo's own notification UI names Source Code Pro
/// as its monospace.
/// </para>
/// <para>
/// <b>These tests exist because every failure this row can produce is silent.</b> A font that is
/// not shipped, a family name that does not match, a metric that does not hold — none of them
/// throw. Avalonia falls back to some other monospaced face and the block looks fine while being
/// drawn in the wrong font, or the node is sized from a character width that no longer applies and
/// the text creeps under a port tab.
/// </para>
/// </remarks>
public sealed class CodeBlockFontTests
{
    /// <summary>Where the font sits in the assembly, as Avalonia will look for it.</summary>
    private static readonly Uri Asset = new("avares://Spark.UI/Assets/Fonts/SourceCodePro-Regular.ttf");

    /// <summary>
    /// <b>The font is really in the assembly.</b> A missing embedded font does not throw — the
    /// family fails to resolve and Avalonia quietly draws in something else, so nothing but this
    /// asks the question.
    /// </summary>
    [Fact]
    public void TheFontIsShippedInTheAssembly() => HeadlessSession.Run(() =>
    {
        using Stream font = AssetLoader.Open(Asset);
        using MemoryStream copy = new();

        font.CopyTo(copy);

        // The real file is 188 KB. A stream that opens and is empty would pass a CanRead check and
        // fail to be a font, which is the shape of every failure in this row.
        Assert.True(copy.Length > 100_000, $"the font is {copy.Length} bytes, which is too small to be one");

        // `sfnt` version 1.0 - the four bytes every TrueType file starts with. This says the
        // resource is a font rather than something that happens to be the right size.
        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x00 }, copy.ToArray().Take(4));
    });

    /// <summary>
    /// <b>The family name matches what the font declares.</b> It is half of the
    /// <c>avares://…#Family</c> URI, and a typo in it is the silent fallback again.
    /// </summary>
    [Fact]
    public void TheFamilyNameIsTheOneTheFontDeclares() =>
        Assert.Equal("Source Code Pro", CodeFont.Name);

    /// <summary>And the URI is built from it, so the two cannot drift apart.</summary>
    [Fact]
    public void TheUriNamesTheFolderAndTheFamily()
    {
        Assert.Equal("avares://Spark.UI/Assets/Fonts#Source Code Pro", CodeFont.Family);
        Assert.EndsWith(CodeFont.Name, CodeFont.Family, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Every glyph advances 0.6 em, which is why swapping the face moved no layout constant.</b>
    /// </summary>
    /// <remarks>
    /// <c>CanvasNode.ScriptCharWidth</c> is 6.6 px at the canvas's 11 px and
    /// <c>CanvasPane.EditorCharWidth</c> is 7.3 at the editor's 12 px; both were estimated against
    /// Cascadia Mono, which has the same ratio. <b>This is what makes that a checked fact rather
    /// than a coincidence somebody noticed once</b> — a future face with a different advance would
    /// size every code block wrongly, and the only symptom would be text creeping under a port tab
    /// on the longest line of the widest block.
    /// </remarks>
    /// <remarks>
    /// <b>This cannot tell the shipped face from a fallback, and nothing here pretends it can.</b>
    /// Cascadia Mono advances 0.6 em too — that is the point of the swap being free — so a build
    /// that lost the font would pass every measurement in this file. What says the right face is
    /// being used is <see cref="TheFontIsShippedInTheAssembly"/> together with
    /// <see cref="TheFamilyNameIsTheOneTheFontDeclares"/>: the file is in the assembly, and it is
    /// asked for by the name it declares.
    /// </remarks>
    [Theory]
    [InlineData("0")]
    [InlineData("W")]
    [InlineData("i")]
    [InlineData("m")]
    [InlineData(".")]
    public void EveryGlyphAdvancesSixTenthsOfTheFontSize(string glyph) => HeadlessSession.Run(() =>
    {
        // MEASURED THROUGH FormattedText, WHICH IS WHAT THE CANVAS ITSELF DRAWS WITH.
        //
        // Reading the advance off the glyph table would be a more direct question and a less
        // useful one: the layout constants are about the width text comes out at, and this is the
        // same call, the same typeface and the same font size the renderer uses. It also survives
        // Avalonia renaming its glyph-metrics API, which it has.
        Assert.Equal(11.0 * CodeFont.AdvanceRatio, Measure(glyph, 11.0), 2);
        Assert.Equal(12.0 * CodeFont.AdvanceRatio, Measure(glyph, 12.0), 2);
    });

    /// <summary>
    /// <b>Ten characters really are ten times one</b>, which is the property node sizing rests on
    /// and the one a proportional font would break — a fallback face would still measure
    /// <i>something</i> for a single glyph, so measuring one proves less than it looks.
    /// </summary>
    [Fact]
    public void TheFaceIsMonospaced() => HeadlessSession.Run(() =>
    {
        Assert.Equal(Measure("iiiiiiiiii", 11.0), Measure("WWWWWWWWWW", 11.0), 2);
        Assert.Equal(10 * Measure("m", 11.0), Measure("mmmmmmmmmm", 11.0), 2);
    });

    /// <summary>What one run of text comes out as, drawn the way the canvas draws it.</summary>
    private static double Measure(string text, double size) =>
        new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(CodeFont.FontFamily),
            size,
            Brushes.Black).WidthIncludingTrailingWhitespace;

    /// <summary>
    /// The canvas's character width really is the ratio times its font size, rather than a number
    /// that once matched.
    /// </summary>
    /// <remarks>
    /// Both constants are private to their own class, so this restates them — which is exactly
    /// what makes it useful: it goes red when one of them is edited without the other, and the
    /// numbers are the ones a reader would otherwise have to go and find.
    /// </remarks>
    [Theory]
    [InlineData(11.0, 6.6)]
    [InlineData(12.0, 7.2)]
    public void TheSizingConstantsFollowFromTheRatio(double fontSize, double charWidth) =>
        Assert.Equal(charWidth, fontSize * CodeFont.AdvanceRatio, 3);

    /// <summary>
    /// <b>The licence ships beside the font.</b> Source Code Pro is under the SIL Open Font
    /// License, which permits redistribution and requires the licence to travel with it — so a
    /// build that dropped the text would be a licence breach nobody would notice.
    /// </summary>
    [Fact]
    public void TheLicenceShipsWithIt() => HeadlessSession.Run(() =>
    {
        using Stream licence = AssetLoader.Open(new Uri("avares://Spark.UI/Assets/Fonts/OFL.txt"));
        using StreamReader reader = new(licence);

        Assert.Contains("SIL OPEN FONT LICENSE", reader.ReadToEnd(), StringComparison.Ordinal);
    });
}
