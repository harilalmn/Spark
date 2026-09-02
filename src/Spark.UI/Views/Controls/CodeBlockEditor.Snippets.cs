using System;
using System.Collections.Generic;
using AvaloniaEdit.Document;
using AvaloniaEdit.Snippets;
using Spark.Scripting;

namespace Spark.UI.Views.Controls;

/// <summary>
/// Snippet expansion: a prefix and <b>Tab</b>, or a pick from the completion list.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ported from RCS's <c>SnippetController</c></b> (<c>C:\Zyeta\Projects\RCS</c>). Once expanded,
/// the editable fields are tab stops: <b>Tab</b> and <b>Shift+Tab</b> step between them, repeated
/// fields follow the one being typed, and <b>Enter</b> or <b>Escape</b> ends the session and drops
/// the caret on the template's <c>$0</c>. That machinery is AvaloniaEdit's; this decides when to
/// start it and gets out of the way while it runs.
/// </para>
/// <para>
/// <b>Which snippets exist is not decided here</b> — <see cref="ScriptSnippets"/> owns the
/// catalogue, the prefix matching and the parsing, none of which needs a text area. This half is
/// only the translation into AvaloniaEdit's elements and the insertion, which is all that could
/// not be written without one.
/// </para>
/// </remarks>
public sealed partial class CodeBlockEditor
{
    private bool _expandingSnippet;

    /// <summary>True while the user is stepping through an expanded snippet's fields.</summary>
    /// <remarks>
    /// <b>Tab has three owners and they queue in this order</b>: the completion list while it is
    /// open, then a snippet session while one is running, then the editor's own indent. Getting
    /// that order wrong is not subtle — a Tab that indents instead of moving to the next field
    /// leaves the user editing a template by hand.
    /// </remarks>
    public bool IsExpandingSnippet => _expandingSnippet;

    /// <summary>
    /// Expands the snippet whose prefix ends at the caret, if there is one.
    /// </summary>
    /// <returns>True when a snippet was expanded and the key should be considered handled.</returns>
    /// <remarks>
    /// Only an exact prefix expands, and only when nothing is selected: Tab has to keep meaning
    /// "indent" everywhere else, which is most places.
    /// </remarks>
    public bool TryExpandSnippetAtCaret()
    {
        if (_editor is null || _expandingSnippet || IsCompletionOpen)
        {
            return false;
        }

        if (!_editor.TextArea.Selection.IsEmpty)
        {
            return false;
        }

        int caret = _editor.CaretOffset;

        if (ScriptSnippets.PrefixBefore(_editor.Text, caret) is not { } snippet)
        {
            return false;
        }

        return Expand(snippet, caret - snippet.Prefix.Length, snippet.Prefix.Length);
    }

    /// <summary>
    /// Replaces a span — the prefix the user typed, or what the completion list filtered on —
    /// with the expanded snippet.
    /// </summary>
    /// <param name="snippet">The snippet to insert.</param>
    /// <param name="start">Where the replaced span begins.</param>
    /// <param name="length">How long it is.</param>
    /// <returns>True when the snippet was inserted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snippet"/> is null.</exception>
    public bool Expand(ScriptSnippet snippet, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(snippet);

        if (_editor?.Document is not { } document)
        {
            return false;
        }

        start = Math.Max(0, Math.Min(start, document.TextLength));
        length = Math.Max(0, Math.Min(length, document.TextLength - start));

        Snippet built = Build(ScriptSnippets.Parse(snippet.Body));

        // The whole expansion is one undo step, the prefix removal included. Two steps would mean
        // an undo that leaves the prefix gone and the template half there.
        _suppressTextChanged = true;

        try
        {
            using (document.RunUpdate())
            {
                if (length > 0)
                {
                    document.Remove(start, length);
                }

                // Built by hand rather than through `Snippet.Insert(TextArea)` so that the
                // insertion context is in reach: it is the only way to be told when the user
                // leaves the fields, and Tab has to become ours again afterwards.
                InsertionContext context = new(_editor.TextArea, start);

                _expandingSnippet = true;
                context.Deactivated += (_, _) => _expandingSnippet = false;

                built.Insert(context);
                context.RaiseInsertionCompleted(EventArgs.Empty);
            }
        }
        catch (InvalidOperationException)
        {
            // A snippet that will not insert is not worth taking the editor down for. The document
            // update has already been rolled back by the time this is reached.
            _expandingSnippet = false;

            return false;
        }
        finally
        {
            _suppressTextChanged = false;
        }

        OnTextChanged(this, EventArgs.Empty);

        return true;
    }

    /// <summary>Turns parsed segments into AvaloniaEdit's elements.</summary>
    /// <param name="segments">The parsed body.</param>
    /// <returns>The snippet AvaloniaEdit will insert.</returns>
    private static Snippet Build(IReadOnlyList<ScriptSnippetSegment> segments)
    {
        Snippet snippet = new();
        Dictionary<int, SnippetReplaceableTextElement> fields = [];

        foreach (ScriptSnippetSegment segment in segments)
        {
            switch (segment.Kind)
            {
                case ScriptSnippetSegmentKind.Literal:
                    snippet.Elements.Add(new SnippetTextElement { Text = segment.Text });
                    break;

                case ScriptSnippetSegmentKind.Caret:
                    snippet.Elements.Add(new SnippetCaretElement());
                    break;

                case ScriptSnippetSegmentKind.Field:
                    SnippetReplaceableTextElement field = new() { Text = segment.Text };
                    fields[segment.Number] = field;
                    snippet.Elements.Add(field);
                    break;

                case ScriptSnippetSegmentKind.Bound:
                    // A repeat of a field already placed follows it as the user types. If the
                    // parser ever hands over a repeat before its first - it does not, but a plain
                    // text element is a readable wrong answer rather than a crash - it is inserted
                    // as the default it carries.
                    if (fields.TryGetValue(segment.Number, out SnippetReplaceableTextElement? first))
                    {
                        snippet.Elements.Add(new SnippetBoundElement { TargetElement = first });
                    }
                    else
                    {
                        snippet.Elements.Add(new SnippetTextElement { Text = segment.Text });
                    }

                    break;

                default:
                    break;
            }
        }

        return snippet;
    }
}
