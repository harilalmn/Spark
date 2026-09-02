using System.Collections.Generic;
using Avalonia.Media;

namespace Spark.UI.Views.Controls;

/// <summary>
/// The letter badge drawn beside a completion candidate, and the colour it is drawn in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ported from RCS's <c>CompletionGlyphs</c></b> (<c>C:\Zyeta\Projects\RCS</c>), which builds
/// the same badges as WPF <see cref="DrawingImage"/> geometry. Here they are a
/// <c>Border</c> and a <c>TextBlock</c> in the item template instead: Avalonia can draw a rounded
/// rectangle with a letter in it without anybody constructing geometry, and a control tree is a
/// thing the headless tests can assert against where a rasterised drawing is not.
/// </para>
/// <para>
/// <b>The hues are RCS's and they are deliberate</b> — the ones a light IDE uses, lifted in
/// luminance so a small badge still reads on a dark list rather than sinking into it. Keeping them
/// identical is the point of the exercise: somebody moving between the two applications should not
/// have to relearn that purple means a method.
/// </para>
/// <para>
/// <b>The key is Roslyn's tag</b>, which arrives through <c>ScriptCompletionItem.Kind</c>. An
/// unknown tag falls back to the keyword badge rather than drawing nothing, because a row with no
/// badge in a list where every other row has one reads as a broken row.
/// </para>
/// </remarks>
public static class CompletionGlyph
{
    /// <summary>What a snippet is tagged with. Spark's own, never Roslyn's.</summary>
    public const string SnippetKind = "Snippet";

    private static readonly Dictionary<string, (string Letter, uint Fill)> Kinds = new()
    {
        ["Class"] = ("C", 0xFFE0A14E),
        ["Structure"] = ("S", 0xFF64B57B),
        ["Interface"] = ("I", 0xFF5EB4DC),
        ["Enum"] = ("E", 0xFFD29F52),
        ["EnumMember"] = ("e", 0xFFD29F52),
        ["Delegate"] = ("D", 0xFFB486D0),
        ["Method"] = ("M", 0xFFA27BDC),
        ["ExtensionMethod"] = ("M", 0xFFA27BDC),
        ["Property"] = ("P", 0xFF749EDE),
        ["Field"] = ("F", 0xFF749EDE),
        ["Constant"] = ("K", 0xFF749EDE),
        ["Event"] = ("V", 0xFFD6869C),
        ["Namespace"] = ("N", 0xFF969696),
        ["Keyword"] = ("K", 0xFF6394D2),
        ["Local"] = ("L", 0xFF8A8A8A),
        ["Parameter"] = ("p", 0xFF8A8A8A),
        ["TypeParameter"] = ("T", 0xFF86B486),
        ["Operator"] = ("o", 0xFF969696),
        [SnippetKind] = ("{", 0xFF4FC08D),
    };

    // Frozen once. The list is rebuilt on every keystroke and a brush per row per keystroke is
    // allocation for nothing - the same reasoning SparkPalette.Frozen applies on the canvas.
    private static readonly Dictionary<string, IBrush> Brushes = BuildBrushes();

    /// <summary>The letter shown in the badge for a Roslyn tag.</summary>
    /// <param name="kind">The tag, as <c>ScriptCompletionItem.Kind</c> carries it.</param>
    /// <returns>One character, and never an empty string.</returns>
    public static string LetterFor(string? kind) =>
        kind is not null && Kinds.TryGetValue(kind, out (string Letter, uint Fill) known)
            ? known.Letter
            : Kinds["Keyword"].Letter;

    /// <summary>The badge colour for a Roslyn tag.</summary>
    /// <param name="kind">The tag, as <c>ScriptCompletionItem.Kind</c> carries it.</param>
    /// <returns>A frozen brush, and never null.</returns>
    public static IBrush BrushFor(string? kind) =>
        kind is not null && Brushes.TryGetValue(kind, out IBrush? known) ? known : Brushes["Keyword"];

    /// <summary>Whether a tag is one this class draws a badge of its own for.</summary>
    /// <param name="kind">The tag to test.</param>
    /// <returns>True when the tag is known; false when it will fall back to the keyword badge.</returns>
    public static bool Knows(string? kind) => kind is not null && Kinds.ContainsKey(kind);

    private static Dictionary<string, IBrush> BuildBrushes()
    {
        Dictionary<string, IBrush> brushes = new(Kinds.Count);

        foreach (KeyValuePair<string, (string Letter, uint Fill)> kind in Kinds)
        {
            brushes[kind.Key] = new SolidColorBrush(Color.FromUInt32(kind.Value.Fill)).ToImmutable();
        }

        return brushes;
    }
}
