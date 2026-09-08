using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;

namespace Spark.UI.Theming;

/// <summary>
/// Syntax colours for source drawn on the canvas, taken from the editor's own definition
/// (`E8-T65`).
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client: show the code in colour, as in edit mode, always.</b> A code block
/// was drawn in one flat colour until it was clicked into, so the node on the canvas and the same
/// node a moment later looked like two different things.
/// </para>
/// <para>
/// <b>It reuses <see cref="HighlightingManager"/>'s C# definition rather than tokenising
/// separately, and that is the whole design.</b> A second tokeniser would be a second opinion
/// about what a keyword is, and the two would drift — the canvas would colour <c>record</c> and
/// the editor would not, or the reverse, and nobody would know which was right. Here there is one
/// definition, recoloured once by <see cref="EditorHighlightPalette"/>, and both readers of it get
/// the same answer by construction.
/// </para>
/// <para>
/// <b>Highlighting is a whole-document concern, not a per-line one</b>, which is why this takes
/// the source rather than a line: a block comment or a verbatim string opened on line two decides
/// the colour of line three, and a line coloured on its own would lose that and light up the
/// middle of a comment as though it were code.
/// </para>
/// </remarks>
public static class ScriptColouring
{
    /// <summary>One run of one colour within a line.</summary>
    /// <param name="Start">Where it begins, in characters from the start of the line.</param>
    /// <param name="Length">How many characters it covers.</param>
    /// <param name="Brush">What to paint them.</param>
    public readonly record struct Section(int Start, int Length, IBrush Brush);

    /// <summary>
    /// Colours a block's source, a list of runs per line.
    /// </summary>
    /// <param name="source">The source, as the node carries it.</param>
    /// <returns>
    /// One list of sections per line, in order. A line with no sections is drawn in the caller's
    /// own colour, which is what an unrecognised or empty line should look like.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <remarks>
    /// <b>It answers an empty list rather than throwing when the definition is unavailable.</b>
    /// AvaloniaEdit resolves its bundled definitions from resources, and a caller drawing a canvas
    /// has no way to recover from that failing — flat text is the right degradation, and it is
    /// exactly what the canvas drew before this existed.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<Section>> Of(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        IHighlightingDefinition? definition;

        try
        {
            definition = HighlightingManager.Instance.GetDefinition("C#");
        }
        catch (Exception failure) when (failure is InvalidOperationException or NotSupportedException)
        {
            return [];
        }

        if (definition is null)
        {
            return [];
        }

        // Idempotent by contract, and the canvas may well draw a block before any editor has been
        // constructed - so the palette cannot be left to whoever happens to come first.
        EditorHighlightPalette.Apply(definition);

        TextDocument document = new(source);
        DocumentHighlighter highlighter = new(document, definition);

        List<IReadOnlyList<Section>> lines = new(document.LineCount);

        for (int number = 1; number <= document.LineCount; number++)
        {
            DocumentLine line = document.GetLineByNumber(number);
            List<Section> sections = [];

            HighlightedLine highlighted;

            try
            {
                highlighted = highlighter.HighlightLine(number);
            }
            catch (Exception failure) when (failure is InvalidOperationException or ArgumentException)
            {
                lines.Add([]);
                continue;
            }

            foreach (HighlightedSection section in highlighted.Sections)
            {
                if (Foreground(section.Color) is not { } brush)
                {
                    continue;
                }

                int start = section.Offset - line.Offset;

                if (start < 0 || section.Length <= 0 || start + section.Length > line.Length)
                {
                    continue;
                }

                sections.Add(new Section(start, section.Length, brush));
            }

            lines.Add(sections);
        }

        return lines;
    }

    /// <summary>The brush a highlighting colour paints with, or null when it names none.</summary>
    /// <remarks>
    /// <c>GetBrush</c> takes a run-construction context that only the editor can supply; every
    /// colour <see cref="EditorHighlightPalette"/> sets is a <see cref="SimpleHighlightingBrush"/>,
    /// which ignores it. Anything else is declined rather than guessed at.
    /// </remarks>
    private static IBrush? Foreground(HighlightingColor? colour)
    {
        if (colour?.Foreground is not SimpleHighlightingBrush simple)
        {
            return null;
        }

        try
        {
            return simple.GetBrush(null!);
        }
        catch (NullReferenceException)
        {
            return null;
        }
    }
}
