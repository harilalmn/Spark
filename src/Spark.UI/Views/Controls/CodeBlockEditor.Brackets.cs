using System;
using System.Collections.Generic;
using Avalonia.Input;
using AvaloniaEdit.Document;

namespace Spark.UI.Views.Controls;

/// <summary>
/// Closing brackets and quotes as they are typed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ported from RCS's <c>BracketCompletion</c></b> (<c>C:\Zyeta\Projects\RCS</c>). Four
/// behaviours, and each earns its place: a pair closes as the opener is typed, typing the closer
/// when the caret is already on one steps over it instead of doubling it, an opener typed over a
/// selection wraps it rather than replacing it, and Enter between braces opens an indented block.
/// </para>
/// <para>
/// <b>It stands down whenever something else owns the keystroke</b> — the completion list, a
/// snippet's fields, or extra carets. Multi-caret editing applies one text input at every caret as
/// a single document update (`E6-T24`); a bracket completion firing in the middle of that would
/// insert closers at one caret and not the others, which is a worse outcome than no feature.
/// </para>
/// </remarks>
public sealed partial class CodeBlockEditor
{
    private static readonly Dictionary<char, char> Pairs = new()
    {
        ['('] = ')',
        ['['] = ']',
        ['{'] = '}',
        ['<'] = '>',
        ['"'] = '"',
        ['\''] = '\'',
    };

    /// <summary>Whether bracket completion should keep out of the way right now.</summary>
    private bool BracketsStandDown =>
        _editor is null || IsCompletionOpen || _expandingSnippet || _extra.Count > 0;

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (_editor is null || e.Handled || e.Text is not { Length: 1 } text || BracketsStandDown)
        {
            return;
        }

        char typed = text[0];

        // Wrapping a selection is more useful than replacing it, and it is the only one of these
        // four that a user cannot get by typing one more character.
        if (!_editor.TextArea.Selection.IsEmpty && Pairs.TryGetValue(typed, out char around))
        {
            Wrap(typed, around);
            e.Handled = true;

            return;
        }

        // Typing the closer that is already there steps over it. Without this, closing a pair the
        // editor opened for you leaves two.
        if (IsCloser(typed) && CharAt(_editor.CaretOffset) == typed)
        {
            _editor.CaretOffset++;
            e.Handled = true;
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_editor?.Document is not { } document
            || e.Handled
            || e.Text is not { Length: 1 } text
            || BracketsStandDown)
        {
            return;
        }

        if (!Pairs.TryGetValue(text[0], out char closer) || !ShouldClose(text[0]))
        {
            return;
        }

        int caret = _editor.CaretOffset;

        document.Insert(caret, closer.ToString());
        _editor.CaretOffset = caret;

        // `E8-T42`: ask again, now that the caret is back between the pair.
        //
        // The insert above raises `TextChanged` while the caret is still *after* the closer it
        // just added, so the handler there starts a signature request from a position outside the
        // call - and that request cancels the one the opening bracket started and answers null,
        // which closes the popup. Typing `(` therefore produced no signature help at all from the
        // day bracket completion landed, in the properties pane as much as on a node.
        //
        // **Nothing caught it because nothing typed a bracket.** `E6-T22` was verified through
        // `PoseCodeEditor`, which calls `RequestSignatureAsync` directly, and the editor tests
        // insert into the document rather than raising text input - so neither path ran
        // `OnTextEntered` at all.
        if (text[0] == '(')
        {
            _ = RequestSignatureAsync();
        }
    }

    private void Wrap(char opener, char closer)
    {
        if (_editor?.Document is not { } document)
        {
            return;
        }

        int start = _editor.SelectionStart;
        int length = _editor.SelectionLength;

        // One update, so an undo takes both halves off together.
        using (document.RunUpdate())
        {
            document.Insert(start + length, closer.ToString());
            document.Insert(start, opener.ToString());
        }

        _editor.Select(start + 1, length);
    }

    /// <summary>
    /// Whether a closer would help here.
    /// </summary>
    /// <param name="opener">The character just typed.</param>
    /// <returns>True when a closer should be inserted after the caret.</returns>
    /// <remarks>
    /// <b>The awkward cases are the point of this method.</b> An opener typed directly before
    /// existing text would put its closer in the middle of a word; and <c>&lt;</c> in C# is far
    /// more often a comparison than a generic, so it only closes after an identifier —
    /// <c>List&lt;</c> closes, <c>a &lt; b</c> does not.
    /// </remarks>
    private bool ShouldClose(char opener)
    {
        if (_editor is null)
        {
            return false;
        }

        int caret = _editor.CaretOffset;
        char next = CharAt(caret);

        if (char.IsLetterOrDigit(next) || next == '_')
        {
            return false;
        }

        return opener switch
        {
            '<' => CharAt(caret - 2) is var before && (char.IsLetterOrDigit(before) || before == '_'),
            '"' or '\'' => ShouldCloseQuote(opener, caret),
            _ => true,
        };
    }

    /// <summary>Whether the quote just typed opened a string rather than closing one.</summary>
    private bool ShouldCloseQuote(char quote, int caret)
    {
        if (_editor?.Document is not { } document)
        {
            return false;
        }

        // An escaped quote is part of the string, not the start of one.
        if (CharAt(caret - 2) == '\\')
        {
            return false;
        }

        DocumentLine line = document.GetLineByOffset(caret);

        int count = 0;
        for (int offset = line.Offset; offset < caret; offset++)
        {
            if (CharAt(offset) != quote)
            {
                continue;
            }

            if (offset > line.Offset && CharAt(offset - 1) == '\\')
            {
                continue;
            }

            count++;
        }

        // Odd means the one just typed opened a string; even means it closed one.
        return count % 2 == 1;
    }

    /// <summary>Enter between a pair of braces opens an indented block.</summary>
    /// <returns>True when a block was opened.</returns>
    private bool OpenBlock()
    {
        if (_editor?.Document is not { } document
            || BracketsStandDown
            || !_editor.TextArea.Selection.IsEmpty)
        {
            return false;
        }

        int caret = _editor.CaretOffset;

        if (CharAt(caret - 1) != '{' || CharAt(caret) != '}')
        {
            return false;
        }

        DocumentLine line = document.GetLineByOffset(caret);
        string newLine = TextUtilities.GetNewLineFromDocument(document, line.LineNumber);
        string indent = LeadingWhitespace(line);
        string inner = indent + _editor.Options.IndentationString;

        document.Replace(caret, 0, newLine + inner + newLine + indent);
        _editor.CaretOffset = caret + newLine.Length + inner.Length;

        return true;
    }

    /// <summary>Backspace between an empty pair removes both halves.</summary>
    /// <returns>True when a pair was removed.</returns>
    private bool DeleteEmptyPair()
    {
        if (_editor?.Document is not { } document
            || BracketsStandDown
            || !_editor.TextArea.Selection.IsEmpty)
        {
            return false;
        }

        int caret = _editor.CaretOffset;

        if (!Pairs.TryGetValue(CharAt(caret - 1), out char closer) || CharAt(caret) != closer)
        {
            return false;
        }

        document.Remove(caret - 1, 2);

        return true;
    }

    private string LeadingWhitespace(DocumentLine line)
    {
        if (_editor?.Document is not { } document)
        {
            return string.Empty;
        }

        string text = document.GetText(line.Offset, line.Length);

        int i = 0;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
        {
            i++;
        }

        return text[..i];
    }

    private char CharAt(int offset) =>
        _editor?.Document is not { } document || offset < 0 || offset >= document.TextLength
            ? '\0'
            : document.GetCharAt(offset);

    private static bool IsCloser(char candidate)
    {
        foreach (KeyValuePair<char, char> pair in Pairs)
        {
            if (pair.Value == candidate)
            {
                return true;
            }
        }

        return false;
    }
}
