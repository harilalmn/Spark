using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Spark.UI.Theming;

namespace Spark.UI.Views.Controls;

/// <summary>
/// The Selection commands of the code block's editor, and the multiple carets most of them need
/// (`E6-T24`).
/// </summary>
/// <remarks>
/// <para>
/// <b>AvaloniaEdit has one caret, and eight of these fourteen commands are about having several.</b>
/// So there is a caret layer here: secondary carets are pairs of <see cref="TextAnchor"/>, which
/// the document moves for us through every edit — the single piece of AvaloniaEdit that makes this
/// tractable — drawn by a background renderer, and edited by intercepting text input and applying
/// the same change at every caret inside one document update, so that the whole multi-caret edit is
/// one step on the undo stack.
/// </para>
/// <para>
/// <b>The editing path only diverges when there is more than one caret.</b> With a single caret
/// every keystroke goes to AvaloniaEdit exactly as before; a control that reimplemented typing for
/// everybody in order to serve Ctrl+D would be trading a rare feature against the common one.
/// </para>
/// <para>
/// <b>Where the bindings came from.</b> They are VS Code's, because that is what the client asked
/// for by name and what anybody opening this editor will try. Two commands have no VS Code
/// binding — Duplicate Selection and Add Previous Occurrence — and are reachable from the editor's
/// context menu, which is also where the whole list is discoverable: Spark's own <i>Edit</i> menu
/// is about nodes, and a <i>Selection</i> menu in the main bar that did nothing unless a code
/// block had focus would be worse than no menu at all.
/// </para>
/// </remarks>
public sealed partial class CodeBlockEditor
{
    /// <summary>Every secondary caret: its selection anchor, and where it is.</summary>
    private readonly List<(TextAnchor Anchor, TextAnchor Position)> _extra = [];

    /// <summary>The selections that Expand Selection grew out of, newest last.</summary>
    private readonly List<(int Start, int End)> _shrink = [];

    private bool _columnSelection;
    private bool _columnSelecting;
    private TextViewPosition? _columnAnchor;

    /// <summary>Whether the editor is in column-selection mode.</summary>
    /// <remarks>
    /// <b>Turning it on converts the selection into a rectangle</b>, and AvaloniaEdit does the rest:
    /// a <see cref="RectangleSelection"/> answers <c>SetEndpoint</c> with another rectangle, so
    /// every Shift+arrow, Shift+Home and Shift+End keeps the block shape without a single extra key
    /// handler. The mouse is ours, because AvaloniaEdit only makes a rectangle when Alt is held and
    /// the entire point of a mode is that the modifier is no longer needed.
    /// </remarks>
    public bool ColumnSelectionMode
    {
        get => _columnSelection;

        set
        {
            if (_columnSelection == value)
            {
                return;
            }

            _columnSelection = value;

            if (_editor?.TextArea is not { } area)
            {
                return;
            }

            // Virtual space is what lets a box extend past the end of a short line, which is the
            // case column selection exists for — a ragged block of code with one long line in it.
            area.Options.EnableVirtualSpace = value;

            // An empty selection has no start position — its `StartPosition` is line 0, which the
            // document rejects — so the caret is the rectangle's corner when there is nothing
            // selected. Switching a mode on must never throw at the person switching it.
            TextViewPosition start = area.Selection.IsEmpty ? area.Caret.Position : area.Selection.StartPosition;
            TextViewPosition end = area.Selection.IsEmpty ? area.Caret.Position : area.Selection.EndPosition;

            area.Selection = value
                ? new RectangleSelection(area, start, end)
                : Selection.Create(area, area.Selection.SurroundingSegment ?? new SimpleSegment(area.Caret.Offset, 0));
        }
    }

    /// <summary>Whether Ctrl+Click adds a caret instead of Alt+Click.</summary>
    /// <remarks>
    /// VS Code's <i>Switch to Ctrl+Click for Multi-Cursor</i>, and it exists for the same reason
    /// there: on some window managers Alt+drag belongs to the desktop and never reaches the
    /// application, which makes the default unusable through no fault of the editor.
    /// </remarks>
    public bool ControlClickAddsCaret { get; set; }

    /// <summary>Every caret in the editor, ascending, the primary one included.</summary>
    public IReadOnlyList<int> CaretOffsets =>
        [.. Carets().Select(caret => caret.Position).Order()];

    /// <summary>The primary selection, as a pair of offsets.</summary>
    public (int Start, int End) SelectionRange
    {
        get
        {
            if (_editor?.TextArea is not { } area)
            {
                return (0, 0);
            }

            ISegment? segment = area.Selection.SurroundingSegment;

            return segment is null
                ? (area.Caret.Offset, area.Caret.Offset)
                : (segment.Offset, segment.EndOffset);
        }
    }

    /// <summary>Selects the whole document, and drops every secondary caret.</summary>
    public void SelectAllText()
    {
        ClearExtraCarets();
        _editor?.SelectAll();
    }

    /// <summary>
    /// Grows the selection to the next structure out: word, then brackets, then line, then all.
    /// </summary>
    /// <remarks>
    /// <b>Structural rather than semantic, and the difference is worth stating.</b> VS Code expands
    /// by syntax tree; this expands by brackets and lines, which agrees with the tree for the
    /// shapes people actually select in a code block — an argument, a call, a statement — and is
    /// four dozen lines rather than a round trip through Roslyn on a keystroke. Each step is
    /// pushed, so Shrink Selection walks back down exactly the way it came.
    /// </remarks>
    public void ExpandSelection()
    {
        if (_editor?.Document is not { } document)
        {
            return;
        }

        (int start, int end) = SelectionRange;

        foreach ((int candidateStart, int candidateEnd) in Expansions(document, start, end))
        {
            if (candidateStart <= start && candidateEnd >= end && (candidateStart < start || candidateEnd > end))
            {
                _shrink.Add((start, end));
                Select(candidateStart, candidateEnd);

                return;
            }
        }
    }

    /// <summary>Steps back to the selection Expand Selection grew from.</summary>
    public void ShrinkSelection()
    {
        if (_shrink.Count == 0)
        {
            return;
        }

        (int start, int end) = _shrink[^1];
        _shrink.RemoveAt(_shrink.Count - 1);

        Select(start, end);
    }

    /// <summary>Copies the selected lines above themselves, leaving the caret in the upper copy.</summary>
    public void CopyLineUp() => CopyLines(above: true);

    /// <summary>Copies the selected lines below themselves, leaving the caret in the lower copy.</summary>
    public void CopyLineDown() => CopyLines(above: false);

    /// <summary>Swaps the selected lines with the line above them.</summary>
    public void MoveLinesUp() => MoveLines(up: true);

    /// <summary>Swaps the selected lines with the line below them.</summary>
    public void MoveLinesDown() => MoveLines(up: false);

    /// <summary>
    /// Duplicates the selection, or the whole line when there is no selection.
    /// </summary>
    public void DuplicateSelection()
    {
        if (_editor?.Document is not { } document)
        {
            return;
        }

        (int start, int end) = SelectionRange;

        if (start == end)
        {
            CopyLineDown();
            return;
        }

        string text = document.GetText(start, end - start);

        Edit(() => document.Insert(end, text));
        Select(end, end + text.Length);
    }

    /// <summary>Adds a caret on the line above the topmost one, in the same column.</summary>
    public void AddCaretAbove() => AddCaretOnAdjacentLine(-1);

    /// <summary>Adds a caret on the line below the bottommost one, in the same column.</summary>
    public void AddCaretBelow() => AddCaretOnAdjacentLine(1);

    /// <summary>Puts a caret at the end of every line the selection touches.</summary>
    public void AddCaretsToLineEnds()
    {
        if (_editor?.Document is not { } document)
        {
            return;
        }

        (int start, int end) = SelectionRange;

        DocumentLine first = document.GetLineByOffset(start);
        DocumentLine last = document.GetLineByOffset(end);

        List<int> ends = [];

        for (int line = first.LineNumber; line <= last.LineNumber; line++)
        {
            ends.Add(document.GetLineByNumber(line).EndOffset);
        }

        SetCarets([.. ends.Select(offset => (offset, offset))], ends.Count - 1);
    }

    /// <summary>
    /// Selects the word under the caret, or adds the next occurrence of what is selected.
    /// </summary>
    /// <remarks>
    /// <b>The first press selects, the rest add</b> — Ctrl+D, and it is the multi-caret command
    /// people reach for without thinking. A selection that is an identifier matches whole words
    /// only, because the press after the first is almost always a rename; a selection that is not
    /// matches as plain text, because then it is a search.
    /// </remarks>
    public void AddNextOccurrence() => AddOccurrence(forwards: true);

    /// <summary>Adds the previous occurrence of what is selected.</summary>
    public void AddPreviousOccurrence() => AddOccurrence(forwards: false);

    /// <summary>Puts a caret on every occurrence of the selection, or of the word under the caret.</summary>
    public void SelectAllOccurrences()
    {
        if (_editor?.Document is not { } document || !SelectWordIfEmpty())
        {
            return;
        }

        (int start, int end) = SelectionRange;
        string needle = document.GetText(start, end - start);

        if (needle.Length == 0)
        {
            return;
        }

        List<(int Start, int End)> found = [.. Occurrences(document, needle)];

        if (found.Count == 0)
        {
            return;
        }

        int primary = Math.Max(0, found.FindIndex(match => match.Start == start));

        SetCarets(found, primary);
    }

    /// <summary>Drops every secondary caret, leaving the primary where it is.</summary>
    public void ClearExtraCarets()
    {
        if (_extra.Count == 0)
        {
            return;
        }

        _extra.Clear();
        _editor?.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
    }

    /// <summary>Runs a Selection command by name, which is what the context menu sends.</summary>
    /// <param name="command">The command's name, as the menu item's tag spells it.</param>
    /// <returns>True when the name was one this editor knows.</returns>
    public bool Invoke(string command)
    {
        switch (command)
        {
            case "SelectAll": SelectAllText(); return true;
            case "ExpandSelection": ExpandSelection(); return true;
            case "ShrinkSelection": ShrinkSelection(); return true;
            case "CopyLineUp": CopyLineUp(); return true;
            case "CopyLineDown": CopyLineDown(); return true;
            case "MoveLinesUp": MoveLinesUp(); return true;
            case "MoveLinesDown": MoveLinesDown(); return true;
            case "DuplicateSelection": DuplicateSelection(); return true;
            case "AddCaretAbove": AddCaretAbove(); return true;
            case "AddCaretBelow": AddCaretBelow(); return true;
            case "AddCaretsToLineEnds": AddCaretsToLineEnds(); return true;
            case "AddNextOccurrence": AddNextOccurrence(); return true;
            case "AddPreviousOccurrence": AddPreviousOccurrence(); return true;
            case "SelectAllOccurrences": SelectAllOccurrences(); return true;
            case "ControlClick": ControlClickAddsCaret = !ControlClickAddsCaret; return true;
            case "ColumnSelection": ColumnSelectionMode = !ColumnSelectionMode; return true;
            default: return false;
        }
    }

    /// <summary>Runs the command a context-menu item names, and re-ticks the two modes.</summary>
    private void OnSelectionMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
        {
            return;
        }

        if (item.Tag is string command)
        {
            Invoke(command);
        }

        if (item.Parent is ContextMenu menu)
        {
            ShowModes(menu);
        }

        _editor?.Focus();
    }

    /// <summary>Ticks the two mode items to match the editor, whoever changed them.</summary>
    /// <remarks>
    /// Read from the properties rather than toggled by the click. A tick maintained by the click
    /// drifts the moment a mode is changed any other way — and both of these are ordinary
    /// properties that a test, a binding or a future settings pane can set.
    /// </remarks>
    private void ShowModes(ContextMenu menu)
    {
        foreach (object? child in menu.Items)
        {
            if (child is not MenuItem { Tag: string tag } entry)
            {
                continue;
            }

            if (tag == "ControlClick")
            {
                entry.IsChecked = ControlClickAddsCaret;
            }
            else if (tag == "ColumnSelection")
            {
                entry.IsChecked = ColumnSelectionMode;
            }
        }
    }

    /// <summary>Handles the Selection bindings, and the keys multiple carets have to answer.</summary>
    /// <returns>True when the key was consumed.</returns>
    private bool HandleSelectionKey(KeyEventArgs e)
    {
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        switch (e.Key)
        {
            case Key.A when control && !shift && !alt:
                SelectAllText();
                return true;

            case Key.Right when shift && alt:
                ExpandSelection();
                return true;

            case Key.Left when shift && alt:
                ShrinkSelection();
                return true;

            case Key.Up when shift && alt && !control:
                CopyLineUp();
                return true;

            case Key.Down when shift && alt && !control:
                CopyLineDown();
                return true;

            case Key.Up when alt && !shift && !control:
                MoveLinesUp();
                return true;

            case Key.Down when alt && !shift && !control:
                MoveLinesDown();
                return true;

            case Key.Up when control && alt:
                AddCaretAbove();
                return true;

            case Key.Down when control && alt:
                AddCaretBelow();
                return true;

            case Key.I when shift && alt:
                AddCaretsToLineEnds();
                return true;

            case Key.D when control && !shift:
                AddNextOccurrence();
                return true;

            case Key.D when control && shift:
                DuplicateSelection();
                return true;

            case Key.L when control && shift:
                SelectAllOccurrences();
                return true;

            default:
                break;
        }

        return _extra.Count > 0 && MultiCaretKey(e);
    }

    /// <summary>The keys that have to be answered for every caret rather than for one.</summary>
    /// <remarks>
    /// Deliberately short. Typing, deleting in both directions, a new line and an indent are what a
    /// multi-caret edit is made of; anything else — a word jump, a page, a selection by keyboard —
    /// drops the extra carets rather than pretending to do something clever with them, because a
    /// wrong guess at several carets is several wrong edits at once.
    /// </remarks>
    private bool MultiCaretKey(KeyEventArgs e)
    {
        if (_editor is null)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.Escape:
                ClearExtraCarets();
                return true;

            case Key.Back:
                EditAtEveryCaret(string.Empty, before: 1, after: 0);
                return true;

            case Key.Delete:
                EditAtEveryCaret(string.Empty, before: 0, after: 1);
                return true;

            case Key.Enter:
                EditAtEveryCaret(Environment.NewLine, before: 0, after: 0);
                return true;

            case Key.Tab:
                EditAtEveryCaret(_editor.Options.IndentationString, before: 0, after: 0);
                return true;

            case Key.Left:
            case Key.Right:
                MoveEveryCaret(e.Key == Key.Left ? -1 : 1);
                return true;

            default:
                ClearExtraCarets();
                return false;
        }
    }

    /// <summary>Types the same text at every caret, as one undo step.</summary>
    private void OnEditorTextInput(object? sender, TextInputEventArgs e)
    {
        if (_extra.Count == 0 || e.Text is not { Length: > 0 } text)
        {
            return;
        }

        EditAtEveryCaret(text, before: 0, after: 0);
        e.Handled = true;
    }

    /// <summary>Adds a caret where the mouse is, or drags a box, or clears the extras.</summary>
    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor is null || !e.GetCurrentPoint(_editor).Properties.IsLeftButtonPressed)
        {
            return;
        }

        KeyModifiers wanted = ControlClickAddsCaret ? KeyModifiers.Control : KeyModifiers.Alt;

        if (e.KeyModifiers.HasFlag(wanted))
        {
            if (Offset(e) is { } offset)
            {
                AddCaretAt(offset);
                e.Handled = true;
            }

            return;
        }

        if (ColumnSelectionMode)
        {
            _columnAnchor = Position(e);
            _columnSelecting = _columnAnchor is not null;

            return;
        }

        ClearExtraCarets();
    }

    /// <summary>Drags a rectangle while column-selection mode is on.</summary>
    private void OnEditorPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_columnSelecting
            || _editor?.TextArea is not { } area
            || _columnAnchor is not { } anchor
            || Position(e) is not { } current)
        {
            return;
        }

        area.Selection = new RectangleSelection(area, anchor, current);
        area.Caret.Position = current;

        e.Handled = true;
    }

    private void OnEditorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _columnSelecting = false;
    }

    /// <summary>Every caret, primary first, as offsets rather than anchors.</summary>
    private List<(int Anchor, int Position)> Carets()
    {
        if (_editor?.TextArea is not { } area)
        {
            return [];
        }

        ISegment? segment = area.Selection.SurroundingSegment;

        List<(int Anchor, int Position)> carets =
        [
            segment is null
                ? (area.Caret.Offset, area.Caret.Offset)
                : (segment.Offset, segment.EndOffset),
        ];

        carets.AddRange(_extra
            .Where(caret => !caret.Anchor.IsDeleted && !caret.Position.IsDeleted)
            .Select(caret => (caret.Anchor.Offset, caret.Position.Offset)));

        return carets;
    }

    /// <summary>
    /// Applies the same change at every caret, inside one document update.
    /// </summary>
    /// <remarks>
    /// <b>Ascending with a running delta, and one <c>BeginUpdate</c> around the lot.</b> Editing in
    /// document order means every later offset is out of date by exactly the length the earlier
    /// edits changed, which is one number to carry; and the single update is what makes Ctrl+Z undo
    /// a five-caret edit once rather than five times, which is the difference between multiple
    /// carets being usable and being a trap.
    /// </remarks>
    /// <param name="insert">The text to put at each caret.</param>
    /// <param name="before">Characters to remove behind a caret that has no selection.</param>
    /// <param name="after">Characters to remove in front of a caret that has no selection.</param>
    private void EditAtEveryCaret(string insert, int before, int after)
    {
        if (_editor?.Document is not { } document)
        {
            return;
        }

        List<(int Anchor, int Position)> carets = Carets();
        List<(int Start, int End, bool Primary)> spans =
        [
            .. carets.Select((caret, index) => (
                Math.Min(caret.Anchor, caret.Position),
                Math.Max(caret.Anchor, caret.Position),
                index == 0)),
        ];

        spans.Sort((left, right) => left.Start.CompareTo(right.Start));

        List<(int Start, int End)> resulting = [];
        int primary = 0;
        int delta = 0;

        _suppressTextChanged = true;
        document.BeginUpdate();

        try
        {
            for (int i = 0; i < spans.Count; i++)
            {
                (int start, int end, bool isPrimary) = spans[i];

                start += delta;
                end += delta;

                if (start == end)
                {
                    start = Math.Max(0, start - before);
                    end = Math.Min(document.TextLength, end + after);
                }

                document.Replace(start, end - start, insert);

                int caret = start + insert.Length;

                resulting.Add((caret, caret));
                delta += insert.Length - (end - start);

                if (isPrimary)
                {
                    primary = i;
                }
            }
        }
        finally
        {
            document.EndUpdate();
            _suppressTextChanged = false;
        }

        SetCarets(resulting, primary);
    }

    /// <summary>Moves every caret by one character, dropping any selections.</summary>
    private void MoveEveryCaret(int delta)
    {
        if (_editor?.Document is not { } document)
        {
            return;
        }

        List<(int Anchor, int Position)> carets = Carets();

        List<(int Start, int End)> moved =
        [
            .. carets.Select(caret =>
            {
                int offset = Math.Clamp(caret.Position + delta, 0, document.TextLength);

                return (offset, offset);
            }),
        ];

        SetCarets(moved, 0);
    }

    /// <summary>Puts the carets where a command decided they go, the first named one primary.</summary>
    private void SetCarets(IReadOnlyList<(int Start, int End)> carets, int primary)
    {
        if (_editor?.TextArea is not { } area || _editor.Document is not { } document || carets.Count == 0)
        {
            return;
        }

        primary = Math.Clamp(primary, 0, carets.Count - 1);

        _extra.Clear();

        for (int i = 0; i < carets.Count; i++)
        {
            (int start, int end) = carets[i];

            start = Math.Clamp(start, 0, document.TextLength);
            end = Math.Clamp(end, 0, document.TextLength);

            if (i == primary)
            {
                area.Caret.Offset = end;
                area.Selection = start == end
                    ? Selection.Create(area, end, end)
                    : Selection.Create(area, start, end);

                continue;
            }

            _extra.Add((Anchor(document, start), Anchor(document, end)));
        }

        area.Caret.BringCaretToView();
        area.TextView.InvalidateLayer(KnownLayer.Caret);
        area.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    /// <summary>An anchor that survives having its text deleted and stays after an insertion.</summary>
    private static TextAnchor Anchor(TextDocument document, int offset)
    {
        TextAnchor anchor = document.CreateAnchor(offset);

        anchor.SurviveDeletion = true;
        anchor.MovementType = AnchorMovementType.AfterInsertion;

        return anchor;
    }

    /// <summary>Adds one caret at an offset, keeping everything already there.</summary>
    private void AddCaretAt(int offset)
    {
        if (_editor?.Document is not { } document)
        {
            return;
        }

        offset = Math.Clamp(offset, 0, document.TextLength);

        if (Carets().Any(caret => caret.Position == offset))
        {
            return;
        }

        _extra.Add((Anchor(document, offset), Anchor(document, offset)));
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
    }

    /// <summary>Adds a caret one line above the topmost, or below the bottommost.</summary>
    private void AddCaretOnAdjacentLine(int direction)
    {
        if (_editor?.Document is not { } document)
        {
            return;
        }

        IReadOnlyList<int> offsets = CaretOffsets;
        int from = direction < 0 ? offsets[0] : offsets[^1];

        DocumentLine line = document.GetLineByOffset(from);
        int column = from - line.Offset;
        int number = line.LineNumber + direction;

        if (number < 1 || number > document.LineCount)
        {
            return;
        }

        DocumentLine target = document.GetLineByNumber(number);

        AddCaretAt(target.Offset + Math.Min(column, target.Length));
    }

    /// <summary>Selects the word under the caret when nothing is selected.</summary>
    /// <returns>True when there is now something selected.</returns>
    private bool SelectWordIfEmpty()
    {
        if (_editor?.Document is not { } document)
        {
            return false;
        }

        (int start, int end) = SelectionRange;

        if (start != end)
        {
            return true;
        }

        string text = document.Text;
        int wordStart = WordStart(text, start);
        int wordEnd = start;

        while (wordEnd < text.Length && Identifier(text[wordEnd]))
        {
            wordEnd++;
        }

        if (wordEnd <= wordStart)
        {
            return false;
        }

        Select(wordStart, wordEnd);

        return true;
    }

    /// <summary>Adds the next or previous occurrence of the selection as another caret.</summary>
    private void AddOccurrence(bool forwards)
    {
        if (_editor?.Document is not { } document)
        {
            return;
        }

        bool wasEmpty = SelectionRange.Start == SelectionRange.End;

        if (!SelectWordIfEmpty() || wasEmpty)
        {
            return;
        }

        (int start, int end) = SelectionRange;
        string needle = document.GetText(start, end - start);

        List<(int Start, int End)> all = [.. Occurrences(document, needle)];

        if (all.Count == 0)
        {
            return;
        }

        List<(int Anchor, int Position)> carets = Carets();
        HashSet<int> taken = [.. carets.Select(caret => Math.Min(caret.Anchor, caret.Position))];

        int from = forwards
            ? carets.Max(caret => Math.Max(caret.Anchor, caret.Position))
            : carets.Min(caret => Math.Min(caret.Anchor, caret.Position));

        // Wrapping, in the order the search runs, so that the last occurrence in a file leads back
        // to the first rather than to nothing happening — which reads as a broken key.
        IEnumerable<(int Start, int End)> ordered = forwards
            ? all.Where(match => match.Start >= from).Concat(all)
            : Enumerable.Reverse(all.Where(match => match.End <= from).ToList())
                .Concat(Enumerable.Reverse(all));

        foreach ((int matchStart, int matchEnd) in ordered)
        {
            if (taken.Contains(matchStart))
            {
                continue;
            }

            // The new one becomes primary, so the view scrolls to what was just found.
            List<(int Start, int End)> updated =
            [
                (matchStart, matchEnd),
                .. carets.Select(caret => (Math.Min(caret.Anchor, caret.Position), Math.Max(caret.Anchor, caret.Position))),
            ];

            SetCarets(updated, 0);

            return;
        }
    }

    /// <summary>Every occurrence of a string in the document, ascending.</summary>
    /// <remarks>
    /// An identifier matches whole words only. Pressing Ctrl+D on <c>radius</c> and being given
    /// <c>radiusFactor</c> is the behaviour that makes people stop trusting the key.
    /// </remarks>
    private static IEnumerable<(int Start, int End)> Occurrences(TextDocument document, string needle)
    {
        if (needle.Length == 0)
        {
            yield break;
        }

        string text = document.Text;
        bool word = needle.All(Identifier);

        for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;)
        {
            bool bounded = !word
                || ((i == 0 || !Identifier(text[i - 1]))
                    && (i + needle.Length >= text.Length || !Identifier(text[i + needle.Length])));

            if (bounded)
            {
                yield return (i, i + needle.Length);
            }

            i = text.IndexOf(needle, i + 1, StringComparison.Ordinal);
        }
    }

    /// <summary>The selections Expand Selection will consider, innermost first.</summary>
    private static IEnumerable<(int Start, int End)> Expansions(TextDocument document, int start, int end)
    {
        string text = document.Text;

        // Only when the selection is inside a single word. Growing `(radius, height)` by "the word
        // around it" would swallow the `ByRadius` in front of the bracket, which is not a step
        // outwards — it is a different selection altogether.
        if (start == end || text[start..end].All(Identifier))
        {
            int wordStart = WordStart(text, start);
            int wordEnd = end;

            while (wordEnd < text.Length && Identifier(text[wordEnd]))
            {
                wordEnd++;
            }

            yield return (wordStart, wordEnd);
        }

        if (Brackets(text, start, end) is { } pair)
        {
            yield return (pair.Open + 1, pair.Close);
            yield return (pair.Open, pair.Close + 1);
        }

        DocumentLine first = document.GetLineByOffset(start);
        DocumentLine last = document.GetLineByOffset(end);

        int trimmed = first.Offset;

        while (trimmed < first.EndOffset && char.IsWhiteSpace(text[trimmed]))
        {
            trimmed++;
        }

        yield return (trimmed, last.EndOffset);
        yield return (first.Offset, last.EndOffset);
        yield return (0, document.TextLength);
    }

    /// <summary>The innermost bracket pair enclosing a span, or null when there is none.</summary>
    private static (int Open, int Close)? Brackets(string text, int start, int end)
    {
        Stack<int> open = new();

        for (int i = 0; i < start; i++)
        {
            if (text[i] is '(' or '[' or '{')
            {
                open.Push(i);
            }
            else if (text[i] is ')' or ']' or '}' && open.Count > 0)
            {
                open.Pop();
            }
        }

        while (open.Count > 0)
        {
            int candidate = open.Pop();
            char closing = text[candidate] switch { '(' => ')', '[' => ']', _ => '}' };

            int depth = 0;

            for (int i = candidate + 1; i < text.Length; i++)
            {
                if (text[i] == text[candidate])
                {
                    depth++;
                }
                else if (text[i] == closing)
                {
                    if (depth == 0)
                    {
                        if (i >= end)
                        {
                            return (candidate, i);
                        }

                        break;
                    }

                    depth--;
                }
            }
        }

        return null;
    }

    /// <summary>Copies the selected lines above or below themselves.</summary>
    private void CopyLines(bool above)
    {
        if (_editor?.Document is not { } document)
        {
            return;
        }

        (int start, int end) = SelectionRange;

        DocumentLine first = document.GetLineByOffset(start);
        DocumentLine last = document.GetLineByOffset(end);

        string block = document.GetText(first.Offset, last.EndOffset - first.Offset);
        string newline = Newline(document, last);

        if (above)
        {
            // The copy lands on the original offsets and the original moves down, which leaves the
            // caret in the upper copy — what VS Code does, and the reason Copy Line Up is useful
            // for writing a variation of the line you are on.
            Edit(() => document.Insert(first.Offset, block + newline));
            Select(start, end);

            return;
        }

        Edit(() => document.Insert(last.EndOffset, newline + block));

        int shift = block.Length + newline.Length;

        Select(start + shift, end + shift);
    }

    /// <summary>Swaps the selected lines with their neighbour.</summary>
    private void MoveLines(bool up)
    {
        if (_editor?.Document is not { } document)
        {
            return;
        }

        (int start, int end) = SelectionRange;

        DocumentLine first = document.GetLineByOffset(start);
        DocumentLine last = document.GetLineByOffset(end);

        DocumentLine? neighbour = up ? first.PreviousLine : last.NextLine;

        if (neighbour is null)
        {
            return;
        }

        string block = document.GetText(first.Offset, last.EndOffset - first.Offset);
        string other = document.GetText(neighbour.Offset, neighbour.Length);

        if (up)
        {
            string between = document.GetText(neighbour.EndOffset, first.Offset - neighbour.EndOffset);
            int from = neighbour.Offset;

            Edit(() => document.Replace(from, last.EndOffset - from, block + between + other));

            int shift = other.Length + between.Length;

            Select(start - shift, end - shift);

            return;
        }

        string gap = document.GetText(last.EndOffset, neighbour.Offset - last.EndOffset);
        int origin = first.Offset;

        Edit(() => document.Replace(origin, neighbour.EndOffset - origin, other + gap + block));

        int distance = other.Length + gap.Length;

        Select(start + distance, end + distance);
    }

    /// <summary>The line delimiter to copy with a block, defaulting to the platform's.</summary>
    private static string Newline(TextDocument document, DocumentLine line) =>
        line.DelimiterLength > 0
            ? document.GetText(line.EndOffset, line.DelimiterLength)
            : Environment.NewLine;

    /// <summary>Runs a document change as one undo step, without opening a completion list.</summary>
    private void Edit(Action change)
    {
        if (_editor?.Document is not { } document)
        {
            return;
        }

        _suppressTextChanged = true;
        document.BeginUpdate();

        try
        {
            change();
        }
        finally
        {
            document.EndUpdate();
            _suppressTextChanged = false;
        }
    }

    /// <summary>Selects a range with the single primary caret.</summary>
    private void Select(int start, int end)
    {
        if (_editor?.TextArea is not { } area || _editor.Document is not { } document)
        {
            return;
        }

        start = Math.Clamp(start, 0, document.TextLength);
        end = Math.Clamp(end, 0, document.TextLength);

        area.Caret.Offset = end;
        area.Selection = Selection.Create(area, start, end);
        area.Caret.BringCaretToView();
    }

    /// <summary>Where in the document a pointer event happened, or null when it is past the text.</summary>
    private int? Offset(PointerEventArgs e) =>
        _editor?.Document is { } document && Position(e) is { } position
            ? document.GetOffset(position.Location)
            : null;

    private TextViewPosition? Position(PointerEventArgs e)
    {
        if (_editor?.TextArea.TextView is not { } view)
        {
            return null;
        }

        return view.GetPosition(e.GetPosition(view) + view.ScrollOffset);
    }

    /// <summary>Draws the secondary carets and their selections, which nothing else knows about.</summary>
    private sealed class ExtraCaretRenderer(CodeBlockEditor owner, KnownLayer layer) : IBackgroundRenderer
    {
        /// <inheritdoc/>
        public KnownLayer Layer => layer;

        /// <inheritdoc/>
        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (owner._extra.Count == 0)
            {
                return;
            }

            textView.EnsureVisualLines();

            foreach ((TextAnchor anchor, TextAnchor position) in owner._extra)
            {
                if (anchor.IsDeleted || position.IsDeleted)
                {
                    continue;
                }

                if (layer == KnownLayer.Selection)
                {
                    DrawSelection(
                        textView,
                        drawingContext,
                        owner._editor?.TextArea.SelectionBrush,
                        anchor.Offset,
                        position.Offset);
                }
                else
                {
                    DrawCaret(textView, drawingContext, position.Offset);
                }
            }
        }

        private static void DrawSelection(
            TextView view,
            DrawingContext context,
            IBrush? brush,
            int anchor,
            int position)
        {
            int start = Math.Min(anchor, position);
            int end = Math.Max(anchor, position);

            if (start == end)
            {
                return;
            }

            BackgroundGeometryBuilder builder = new() { AlignToWholePixels = true };

            builder.AddSegment(view, new SimpleSegment(start, end - start));

            if (builder.CreateGeometry() is { } geometry)
            {
                context.DrawGeometry(brush ?? SparkPalette.AccentBrush, null, geometry);
            }
        }

        private static void DrawCaret(TextView view, DrawingContext context, int offset)
        {
            TextViewPosition position = new(view.Document.GetLocation(offset));
            Point top = view.GetVisualPosition(position, VisualYPosition.LineTop) - view.ScrollOffset;

            context.FillRectangle(
                SparkPalette.TextPrimaryBrush,
                new Rect(top.X, top.Y, 1.0, view.DefaultLineHeight));
        }
    }
}
