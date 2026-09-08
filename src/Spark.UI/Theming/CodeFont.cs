using Avalonia.Media;

namespace Spark.UI.Theming;

/// <summary>
/// The face a code block's source is drawn and edited in (<c>E8-T57</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Source Code Pro, and Spark ships the file.</b> The client asked for the font Dynamo draws
/// its Code Block in; Dynamo bundles exactly two monospaced faces —
/// <c>SourceCodePro-Regular.ttf</c> and <c>CourierPrime-Regular.ttf</c> — its own notification UI
/// names Source Code Pro as <i>the</i> monospace, and Courier Prime is a slab serif that the
/// screenshot plainly is not. Adobe, SIL Open Font License 1.1, Reserved Font Name "Source", which
/// is what makes shipping it legal and is why the licence travels beside it.
/// </para>
/// <para>
/// <b>It is one name in one place because three surfaces have to agree.</b> The canvas draws a
/// block's source, the editor opens over that drawing on the node, and the properties pane holds a
/// second editor over the same text. <c>CanvasNode.ScriptBox</c> says why they must match: the
/// drawn text and the editing text occupy the same rectangle, so a block whose text reflowed the
/// instant it was clicked into would read as the node jumping.
/// </para>
/// <para>
/// <b>A shipped font rather than a family list, and that is the whole point of the row.</b> The
/// canvas asked for <c>Cascadia Mono, Consolas, Menlo, monospace</c>, which is a wish: it resolves
/// to a different face on every machine, and to Dynamo's face on none of them unless Dynamo
/// happened to be installed. Embedding the file makes the answer the same everywhere, and the
/// failure mode it removes is the silent one — a fallback face is still <i>a</i> monospaced font,
/// so nothing looks broken, it just is not what was asked for.
/// </para>
/// </remarks>
public static class CodeFont
{
    /// <summary>The family name as the font itself declares it.</summary>
    /// <remarks>
    /// Read from the file rather than chosen: <c>GlyphTypeface.FamilyNames</c> on the shipped TTF
    /// says <c>Source Code Pro</c>. Avalonia matches the embedded font by this name, so a typo
    /// here does not fail — it falls back, silently, to something that looks almost right.
    /// </remarks>
    public const string Name = "Source Code Pro";

    /// <summary>
    /// How wide one character is, as a fraction of the font size.
    /// </summary>
    /// <remarks>
    /// <b>Measured, not assumed.</b> Every glyph in Source Code Pro advances 0.6 em — checked
    /// against <c>0</c>, <c>W</c>, <c>i</c>, <c>m</c> and <c>.</c>. That is the same ratio Cascadia
    /// Mono has, which is why swapping the face moved no layout constant:
    /// <c>CanvasNode.ScriptCharWidth</c> is 6.6 at an 11 px canvas and
    /// <c>CanvasPane.EditorCharWidth</c> is 7.3 at a 12 px editor, and both were estimated against
    /// the old face. <c>CodeBlockFontTests</c> asserts this against the shipped file so that a
    /// future face change cannot quietly invalidate node sizing.
    /// </remarks>
    public const double AdvanceRatio = 0.6;

    /// <summary>
    /// The URI form Avalonia resolves an embedded font by, for XAML and for
    /// <see cref="Typeface"/>.
    /// </summary>
    /// <remarks>
    /// The <c>#</c> separates the folder the resource lives in from the family name inside the
    /// file, which is why both halves have to be right and why <see cref="Name"/> is read from the
    /// font rather than typed from memory.
    /// </remarks>
    public const string Family = "avares://Spark.UI/Assets/Fonts#" + Name;

    /// <summary>The family, ready to hand to a control.</summary>
    public static FontFamily FontFamily { get; } = new(Family);
}
