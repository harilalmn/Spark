using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;

namespace Spark.UI.Theming;

/// <summary>
/// The face a code block's source is drawn and edited in (<c>E8-T57</c>, <c>E8-T59</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Source Code Pro by default, and Spark ships the file.</b> The client asked for the font
/// Dynamo draws its Code Block in; Dynamo bundles exactly two monospaced faces —
/// <c>SourceCodePro-Regular.ttf</c> and <c>CourierPrime-Regular.ttf</c> — its own notification UI
/// names Source Code Pro as <i>the</i> monospace, and Courier Prime is a slab serif that the
/// screenshot plainly is not. Adobe, SIL Open Font License 1.1, Reserved Font Name "Source", which
/// is what makes shipping it legal and is why the licence travels beside it.
/// </para>
/// <para>
/// <b>A shipped font rather than a family list, and that is the point.</b> The canvas used to ask
/// for <c>Cascadia Mono, Consolas, Menlo, monospace</c>, which is a wish: it resolves to a
/// different face on every machine, and to Dynamo's face on none of them unless Dynamo happened to
/// be installed. Embedding the file makes the answer the same everywhere, and the failure it
/// removes is the silent one — a fallback face is still <i>a</i> monospaced font, so nothing looks
/// broken, it just is not what was asked for.
/// </para>
/// <para>
/// <b>It is one name in one place because three surfaces have to agree</b> (<c>E8-T59</c>). The
/// canvas draws a block's source, the editor opens over that drawing on the node, and the
/// properties pane holds a second editor over the same text. <c>CanvasNode.ScriptBox</c> says why
/// they must match: the drawn text and the editing text occupy the same rectangle, so a block
/// whose text reflowed the instant it was clicked into would read as the node jumping. That is
/// also why <see cref="Changed"/> exists — a setting only two of the three noticed would be worse
/// than none.
/// </para>
/// </remarks>
public static class CodeFont
{
    /// <summary>The family name the shipped font declares.</summary>
    /// <remarks>
    /// Read from the file rather than chosen: <c>GlyphTypeface.FamilyNames</c> on the shipped TTF
    /// says <c>Source Code Pro</c>. Avalonia matches the embedded font by this name, so a typo
    /// here does not fail — it falls back, silently, to something that looks almost right.
    /// </remarks>
    public const string Name = "Source Code Pro";

    /// <summary>
    /// How wide one character is in the shipped face, as a fraction of the font size.
    /// </summary>
    /// <remarks>
    /// <b>Measured, not assumed.</b> Every glyph in Source Code Pro advances 0.6 em — checked
    /// against <c>0</c>, <c>W</c>, <c>i</c>, <c>m</c> and <c>.</c>. That is the same ratio Cascadia
    /// Mono has, which is why swapping the face moved no layout constant:
    /// <c>CanvasNode.ScriptCharWidth</c> is 6.6 at an 11 px canvas. <c>CodeBlockFontTests</c>
    /// asserts it against the shipped file so a future default cannot quietly invalidate sizing.
    /// </remarks>
    public const double AdvanceRatio = 0.6;

    /// <summary>
    /// The URI form Avalonia resolves the embedded font by, for XAML and for
    /// <see cref="Typeface"/>.
    /// </summary>
    /// <remarks>
    /// The <c>#</c> separates the folder the resource lives in from the family name inside the
    /// file, which is why both halves have to be right and why <see cref="Name"/> is read from the
    /// font rather than typed from memory.
    /// </remarks>
    public const string DefaultFamily = "avares://Spark.UI/Assets/Fonts#" + Name;

    private static FontFamily _family = new(DefaultFamily);

    /// <summary>Raised when <see cref="FontFamily"/> changes, so every surface can redraw.</summary>
    public static event EventHandler? Changed;

    /// <summary>The face code is drawn and edited in, right now.</summary>
    /// <remarks>
    /// <b>Setting this does not persist it.</b> Persistence is
    /// <c>Spark.Host.CodeFontPreference</c>'s job and the view model owns the pairing — this type
    /// stays a property of the *drawing*, so a test can set a face without writing to the user's
    /// application data.
    /// </remarks>
    public static FontFamily FontFamily
    {
        get => _family;

        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_family.ToString() == value.ToString())
            {
                return;
            }

            _family = value;
            _ratio = null;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// How wide one character is in the face currently chosen, as a fraction of the font size.
    /// </summary>
    /// <remarks>
    /// <b>Measured from the chosen face, not assumed from the shipped one.</b> The offered faces
    /// are all monospaced and they are not all the same width: Consolas advances 0.55 em, Cascadia
    /// Mono 0.586, Source Code Pro and Courier New 0.6. A node sized from the shipped ratio while
    /// drawn in a wider face would put its longest line under its own port tabs, which is the
    /// defect `E8-T58` fixed once already and the one a font setting could reintroduce on every
    /// machine at once.
    /// </remarks>
    public static double CurrentAdvanceRatio => _ratio ??= RatioOf(_family);

    private static double? _ratio;

    /// <summary>The advance of one character in a family, as a fraction of the font size.</summary>
    private static double RatioOf(FontFamily family)
    {
        const double Size = 100;

        double measured = Measure("0", family, Size) / Size;

        // A face that cannot be measured - no font manager - keeps the shipped ratio rather than
        // sizing every block from zero, which would collapse them all to the minimum width.
        return measured > 0 ? measured : AdvanceRatio;
    }

    /// <summary>Puts the face back to the one Spark ships.</summary>
    public static void UseDefault() => FontFamily = new FontFamily(DefaultFamily);

    /// <summary>Chooses a face by family name, falling back to the shipped one.</summary>
    /// <param name="family">The family name, or null for the default.</param>
    /// <remarks>
    /// <b>A name that no longer resolves is not an error.</b> A machine can lose a font between
    /// sessions, so a remembered choice that has gone quietly becomes the shipped face rather than
    /// leaving code blocks drawn in Avalonia's fallback.
    /// </remarks>
    public static void Use(string? family)
    {
        if (string.IsNullOrWhiteSpace(family) || family == Name || family == DefaultFamily)
        {
            UseDefault();
            return;
        }

        FontFamily = Available().Contains(family, StringComparer.Ordinal)
            ? new FontFamily(family)
            : new FontFamily(DefaultFamily);
    }

    /// <summary>The family name to show in the dropdown for the current face.</summary>
    public static string Current =>
        _family.ToString() == DefaultFamily ? Name : _family.Name;

    /// <summary>
    /// Every face a code block could reasonably be drawn in: the shipped one, then the monospaced
    /// fonts installed on this machine (<c>E8-T59</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Avalonia has no "is this monospaced" flag, so this measures.</b> A family whose
    /// <c>i</c> and <c>W</c> come out the same width is monospaced, and one whose ten-character
    /// run is ten times its one-character run really is — the second check catches a face that
    /// happens to have two equal glyphs. It is the same <see cref="FormattedText"/> call the
    /// renderer makes, so a font that measures as monospaced here will draw as one there.
    /// </para>
    /// <para>
    /// <b>Filtered rather than listed in full.</b> Offering every installed font would let a user
    /// pick a proportional face, and a code block sized from
    /// <c>CanvasNode.ScriptCharWidth</c> would then draw its text over its own port tabs — a
    /// setting whose wrong values break the layout is a setting that should not offer them.
    /// </para>
    /// <para>
    /// <b>Computed once.</b> Enumerating and measuring every installed family costs real time, and
    /// fonts do not appear while an application is running often enough to matter.
    /// </para>
    /// </remarks>
    /// <returns>The family names, the shipped face first and the rest alphabetical.</returns>
    public static IReadOnlyList<string> Available() => _available ??= Discover();

    private static IReadOnlyList<string>? _available;

    private static IReadOnlyList<string> Discover()
    {
        List<string> monospaced = [];

        try
        {
            foreach (FontFamily family in FontManager.Current.SystemFonts)
            {
                if (family.Name != Name && IsMonospaced(family) && !CarriesIdeographs(family))
                {
                    monospaced.Add(family.Name);
                }
            }
        }
        catch (Exception failure) when (failure is InvalidOperationException or NotSupportedException)
        {
            // No font manager - a headless or half-initialised host. The shipped face still works,
            // and a dropdown with one entry is better than a crash on the way to the window.
        }

        monospaced.Sort(StringComparer.CurrentCultureIgnoreCase);

        return [Name, .. monospaced];
    }

    /// <summary>
    /// Whether a family ships CJK ideographs, which makes it a CJK face rather than a code face
    /// (<c>E8-T64</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The client was offered <c>MingLiU_HKSCS-ExtB</c>, and the measurement that offered it was
    /// not wrong.</b> Its Latin glyphs really are equal width, so
    /// <see cref="IsMonospaced(FontFamily)"/> answered honestly — the <i>question</i> was too
    /// narrow. A code font is a Latin face whose letters are equal width; a CJK face with
    /// half-width Latin satisfies the second half and fails the first.
    /// </para>
    /// <para>
    /// <b>Asked of the font's own glyph map, not of its name.</b> A list of families to exclude, or
    /// a rule about what a name contains, is a list that rots the moment somebody installs a font
    /// nobody thought of. Whether the file has a glyph for <c>一</c> is a fact about the file.
    /// </para>
    /// <para>
    /// <b>Several blocks rather than one codepoint, and the first attempt got this wrong.</b> It
    /// probed U+4E00 and U+4E2D — the base CJK Unified Ideographs — and the very families the
    /// client saw went on being offered: <c>MingLiU-ExtB</c>, <c>SimSun-ExtB</c> and their
    /// siblings are <i>Extension B</i> fonts, which cover the supplementary plane and carry none
    /// of the common characters. So the probe spans the base block, Extension A, Extension B and
    /// Extension C, plus Kana and Hangul, and a face carrying any of them is not one anybody wants
    /// their C# in.
    /// </para>
    /// </remarks>
    /// <summary>One codepoint from each block a CJK family would carry and a code face would not.</summary>
    private static readonly int[] Ideographs =
    [
        0x4E00,   // CJK Unified Ideographs, the base block.
        0x3400,   // Extension A.
        0x20000,  // Extension B - what the -ExtB families the client saw actually carry.
        0x2A700,  // Extension C.
        0x2B740,  // Extension D.
        0x2B820,  // Extension E.
        0x2CEB0,  // Extension F.
        0x30000,  // Extension G - and SimSun-ExtG survived a probe that stopped at C.
        0x31350,  // Extension H.
        0x3042,   // Hiragana.
        0xAC00,   // Hangul syllables.
    ];

    private static bool CarriesIdeographs(FontFamily family)
    {
        try
        {
            if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out GlyphTypeface? face))
            {
                return false;
            }

            foreach (int codepoint in Ideographs)
            {
                if (face.CharacterToGlyphMap.TryGetGlyph(codepoint, out _))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception failure) when (failure is InvalidOperationException or NotSupportedException)
        {
            // A face that cannot be opened is not one to offer, but it is also not a reason to
            // lose the rest of the list.
            return true;
        }
    }

    /// <summary>Whether every character in a family comes out the same width.</summary>
    private static bool IsMonospaced(FontFamily family)
    {
        double narrow = Measure("i", family);
        double wide = Measure("W", family);

        if (narrow <= 0 || Math.Abs(narrow - wide) > 0.01)
        {
            return false;
        }

        // Ten of them, because two glyphs agreeing is not the claim - the claim is that width is
        // proportional to length, which is what node sizing multiplies by.
        return Math.Abs((10 * narrow) - Measure("iiiiiiiiii", family)) < 0.05;
    }

    private static double Measure(string text, FontFamily family) => Measure(text, family, 12);

    private static double Measure(string text, FontFamily family, double size)
    {
        try
        {
            return new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(family),
                size,
                Brushes.Black).WidthIncludingTrailingWhitespace;
        }
        catch (Exception failure) when (failure is InvalidOperationException or NotSupportedException)
        {
            return 0;
        }
    }
}
