using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using Spark.UI.Views.Controls;

namespace Spark.UI.Tests;

/// <summary>
/// The Selection commands of the code block's editor, and the multiple carets most of them need —
/// `E6-T24`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The client asked for VS Code's Selection menu by name, all fourteen of it.</b> Eight of those
/// commands need more than one caret and AvaloniaEdit has exactly one, so most of what is asserted
/// here is a caret layer that did not exist: where the carets are, that typing goes to all of them,
/// and that Ctrl+Z takes the whole multi-caret edit back in one step.
/// </para>
/// <para>
/// <b>What the headless harness cannot answer</b> is what the extra carets look like: drawing is
/// a background renderer and there are no font metrics to measure it with ([N90](NOTES.md) is the
/// same limit from the other side). Every test here is therefore about the model — offsets and
/// text — which is also where the defects would be.
/// </para>
/// </remarks>
public sealed class EditorSelectionTests
{
    /// <summary>Alt+Up moves the caret's line above the one before it.</summary>
    [Fact]
    public void MoveLineUpSwapsWithTheLineAbove() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("var a = 1;\nvar b = 2;\nvar c = 3;");

        Caret(editor, 1, 0);
        editor.MoveLinesUp();

        Assert.Equal("var b = 2;\nvar a = 1;\nvar c = 3;", Text(editor));
    });

    /// <summary>The moved line keeps the caret, or the command is unusable twice in a row.</summary>
    [Fact]
    public void MovingALineTakesTheCaretWithIt() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("var a = 1;\nvar b = 2;\nvar c = 3;");

        Caret(editor, 2, 3);
        editor.MoveLinesUp();
        editor.MoveLinesUp();

        Assert.Equal("var c = 3;\nvar a = 1;\nvar b = 2;", Text(editor));
        Assert.Equal(0, Line(editor));
    });

    /// <summary>Alt+Down is the same in the other direction, and the last line cannot go further.</summary>
    [Fact]
    public void MoveLineDownStopsAtTheEnd() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("var a = 1;\nvar b = 2;");

        Caret(editor, 1, 0);
        editor.MoveLinesDown();

        Assert.Equal("var a = 1;\nvar b = 2;", Text(editor));
    });

    /// <summary>A multi-line selection moves as one block.</summary>
    [Fact]
    public void MovingMovesEveryLineTheSelectionTouches() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("one\ntwo\nthree\nfour");

        Select(editor, 1, 0, 2, 5);
        editor.MoveLinesDown();

        Assert.Equal("one\nfour\ntwo\nthree", Text(editor));
    });

    /// <summary>
    /// <b>Copy Line Up leaves the caret in the upper copy.</b> That is VS Code's behaviour and it
    /// is the reason the command is useful: the copy you are about to change is the one you are on.
    /// </summary>
    [Fact]
    public void CopyLineUpLeavesTheCaretInTheCopy() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("var a = 1;\nvar b = 2;");

        Caret(editor, 1, 4);
        editor.CopyLineUp();

        Assert.Equal("var a = 1;\nvar b = 2;\nvar b = 2;", Text(editor));
        Assert.Equal(1, Line(editor));
    });

    /// <summary>Copy Line Down leaves the caret in the lower copy, for the same reason.</summary>
    [Fact]
    public void CopyLineDownLeavesTheCaretInTheCopy() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("var a = 1;\nvar b = 2;");

        Caret(editor, 0, 4);
        editor.CopyLineDown();

        Assert.Equal("var a = 1;\nvar a = 1;\nvar b = 2;", Text(editor));
        Assert.Equal(1, Line(editor));
    });

    /// <summary>Duplicate Selection copies what is selected and selects the copy.</summary>
    [Fact]
    public void DuplicateSelectionCopiesTheSelection() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("radius");

        Select(editor, 0, 0, 0, 6);
        editor.DuplicateSelection();

        Assert.Equal("radiusradius", Text(editor));
        Assert.Equal((6, 12), editor.SelectionRange);
    });

    /// <summary>With nothing selected it duplicates the line, which is what people expect of it.</summary>
    [Fact]
    public void DuplicateSelectionWithNoSelectionDuplicatesTheLine() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("var a = 1;\nvar b = 2;");

        Caret(editor, 0, 2);
        editor.DuplicateSelection();

        Assert.Equal("var a = 1;\nvar a = 1;\nvar b = 2;", Text(editor));
    });

    /// <summary>
    /// <b>Expand Selection grows word, then brackets, then the line.</b> Structural rather than
    /// semantic — see the control's own remarks — and the sequence is what makes it feel like an
    /// editor rather than a set of unrelated selections.
    /// </summary>
    [Fact]
    public void ExpandSelectionGrowsOutwards() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("var c = Circle.ByRadius(radius, height);");

        Caret(editor, 0, 26);

        editor.ExpandSelection();
        Assert.Equal("radius", Selected(editor));

        editor.ExpandSelection();
        Assert.Equal("radius, height", Selected(editor));

        editor.ExpandSelection();
        Assert.Equal("(radius, height)", Selected(editor));

        editor.ExpandSelection();
        Assert.Equal("var c = Circle.ByRadius(radius, height);", Selected(editor));
    });

    /// <summary>Shrink Selection walks back down exactly the way Expand came up.</summary>
    [Fact]
    public void ShrinkSelectionRetracesTheExpansion() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("var c = Circle.ByRadius(radius, height);");

        Caret(editor, 0, 26);

        editor.ExpandSelection();
        editor.ExpandSelection();
        editor.ExpandSelection();

        editor.ShrinkSelection();
        Assert.Equal("radius, height", Selected(editor));

        editor.ShrinkSelection();
        Assert.Equal("radius", Selected(editor));
    });

    /// <summary>Add Cursor Below puts a second caret one line down, in the same column.</summary>
    [Fact]
    public void AddCaretBelowPutsASecondCaretInTheSameColumn() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("var a = 1;\nvar b = 2;");

        Caret(editor, 0, 4);
        editor.AddCaretBelow();

        Assert.Equal([4, 15], editor.CaretOffsets);
    });

    /// <summary>Add Cursor Above does the same upwards, and stops at the first line.</summary>
    [Fact]
    public void AddCaretAboveStopsAtTheTop() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("var a = 1;\nvar b = 2;");

        Caret(editor, 0, 4);
        editor.AddCaretAbove();

        Assert.Equal([4], editor.CaretOffsets);
    });

    /// <summary>
    /// <b>Typing goes to every caret.</b> This is the whole point of the layer, and it is also
    /// where the arithmetic is: every edit shifts every offset after it.
    /// </summary>
    [Fact]
    public void TypingReachesEveryCaret() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("a = 1;\nb = 2;\nc = 3;");

        Caret(editor, 0, 0);
        editor.AddCaretBelow();
        editor.AddCaretBelow();

        Assert.Equal(3, editor.CaretOffsets.Count);

        TypeText(editor, "var ");

        Assert.Equal("var a = 1;\nvar b = 2;\nvar c = 3;", Text(editor));
        Assert.Equal([4, 15, 26], editor.CaretOffsets);
    });

    /// <summary>Backspace deletes behind every caret at once.</summary>
    [Fact]
    public void BackspaceReachesEveryCaret() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("xa = 1;\nxb = 2;");

        Caret(editor, 0, 1);
        editor.AddCaretBelow();

        Press(editor, Key.Back);

        Assert.Equal("a = 1;\nb = 2;", Text(editor));
    });

    /// <summary>
    /// <b>A multi-caret edit is one undo step.</b> Five carets that took five presses of Ctrl+Z to
    /// undo would make the whole feature a trap, which is why the edits are wrapped in one document
    /// update rather than applied one at a time.
    /// </summary>
    [Fact]
    public void AMultiCaretEditIsOneUndoStep() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("a = 1;\nb = 2;\nc = 3;");

        Caret(editor, 0, 0);
        editor.AddCaretBelow();
        editor.AddCaretBelow();

        TypeText(editor, "var ");
        Assert.Equal("var a = 1;\nvar b = 2;\nvar c = 3;", Text(editor));

        Inner(editor).Undo();

        Assert.Equal("a = 1;\nb = 2;\nc = 3;", Text(editor));
    });

    /// <summary>Escape drops the extra carets and leaves the text alone.</summary>
    [Fact]
    public void EscapeDropsTheExtraCarets() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("a = 1;\nb = 2;");

        Caret(editor, 0, 0);
        editor.AddCaretBelow();

        Press(editor, Key.Escape);

        Assert.Single(editor.CaretOffsets);
        Assert.Equal("a = 1;\nb = 2;", Text(editor));
    });

    /// <summary>Add Cursors to Line Ends puts one caret at the end of every selected line.</summary>
    [Fact]
    public void CursorsToLineEndsCoversEveryLineOfTheSelection() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("one\ntwo\nthree");

        Select(editor, 0, 0, 2, 1);
        editor.AddCaretsToLineEnds();

        Assert.Equal([3, 7, 13], editor.CaretOffsets);
    });

    /// <summary>The first Ctrl+D selects the word under the caret, and adds nothing.</summary>
    [Fact]
    public void TheFirstAddNextOccurrenceSelectsTheWord() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("radius + radius");

        Caret(editor, 0, 2);
        editor.AddNextOccurrence();

        Assert.Equal("radius", Selected(editor));
        Assert.Single(editor.CaretOffsets);
    });

    /// <summary>
    /// <b>The second adds a caret on the next occurrence, and typing then renames both.</b> Each
    /// caret carries its own selection, so what is typed replaces the word rather than being
    /// appended to it — which is the whole reason Ctrl+D is the rename people actually use.
    /// </summary>
    [Fact]
    public void TheSecondAddNextOccurrenceAddsACaret() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("radius + radius");

        Caret(editor, 0, 2);
        editor.AddNextOccurrence();
        editor.AddNextOccurrence();

        Assert.Equal(2, editor.CaretOffsets.Count);

        TypeText(editor, "r");

        Assert.Equal("r + r", Text(editor));
    });

    /// <summary>
    /// <b>An identifier matches whole words only.</b> Ctrl+D on <c>radius</c> handing back
    /// <c>radiusFactor</c> is the behaviour that makes people stop trusting the key.
    /// </summary>
    [Fact]
    public void AnIdentifierMatchesWholeWordsOnly() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("radius + radiusFactor + radius");

        Caret(editor, 0, 2);
        editor.AddNextOccurrence();
        editor.AddNextOccurrence();

        TypeText(editor, "size");

        Assert.Equal("size + radiusFactor + size", Text(editor));
    });

    /// <summary>Select All Occurrences takes every one of them at once.</summary>
    [Fact]
    public void SelectAllOccurrencesTakesThemAll() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("a + a + a");

        Caret(editor, 0, 0);
        editor.SelectAllOccurrences();

        Assert.Equal(3, editor.CaretOffsets.Count);

        TypeText(editor, "b");

        Assert.Equal("b + b + b", Text(editor));
    });

    /// <summary>Add Previous Occurrence is the same command looking the other way.</summary>
    [Fact]
    public void AddPreviousOccurrenceLooksBackwards() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("count + count");

        Caret(editor, 0, 9);
        editor.AddNextOccurrence();
        editor.AddPreviousOccurrence();

        Assert.Equal([5, 13], editor.CaretOffsets);
    });

    /// <summary>
    /// <b>Column-selection mode makes the selection a rectangle</b>, and AvaloniaEdit keeps it one
    /// through every ordinary selection key from then on — which is the whole implementation.
    /// </summary>
    [Fact]
    public void ColumnSelectionModeMakesTheSelectionARectangle() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("one\ntwo\nthree");

        Select(editor, 0, 0, 0, 2);

        editor.ColumnSelectionMode = true;

        Assert.IsType<RectangleSelection>(Inner(editor).TextArea.Selection);

        editor.ColumnSelectionMode = false;

        Assert.IsNotType<RectangleSelection>(Inner(editor).TextArea.Selection);
    });

    /// <summary>
    /// A rectangle extended down covers the same columns on both lines, which is the property that
    /// makes column selection worth having.
    /// </summary>
    [Fact]
    public void ARectangleCoversTheSameColumnsOnEveryLine() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("abcd\nefgh");

        Select(editor, 0, 1, 0, 3);
        editor.ColumnSelectionMode = true;

        TextArea area = Inner(editor).TextArea;

        area.Selection = area.Selection.SetEndpoint(new TextViewPosition(2, 4));

        Assert.Equal("bc\nfg", area.Selection.GetText().Replace("\r\n", "\n", StringComparison.Ordinal));
    });

    /// <summary>The Ctrl+Click switch is a mode, and the menu ticks whichever it is.</summary>
    [Fact]
    public void TheMultiCursorModifierCanBeSwitched() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("a");

        Assert.False(editor.ControlClickAddsCaret);

        Assert.True(editor.Invoke("ControlClick"));
        Assert.True(editor.ControlClickAddsCaret);
    });

    /// <summary>
    /// <b>Every command the context menu names is a command the editor has.</b> A menu item whose
    /// tag nothing answers is a dead entry, and it fails silently — which is exactly the failure a
    /// menu of fourteen items invites.
    /// </summary>
    [Fact]
    public void EveryMenuItemRunsSomething() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("var a = 1;\nvar b = 2;");

        ContextMenu menu = Inner(editor).ContextMenu!;

        string[] tags =
        [
            .. menu.Items.OfType<MenuItem>().Select(item => item.Tag).OfType<string>(),
        ];

        Assert.Equal(16, tags.Length);
        Assert.All(tags, tag => Assert.True(editor.Invoke(tag), $"nothing answers to '{tag}'"));
    });

    /// <summary>Ctrl+D reaches the editor as a key, not only as a method a test can call.</summary>
    [Fact]
    public void TheBindingsAreReachableFromTheKeyboard() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("radius + radius");

        Caret(editor, 0, 2);

        Press(editor, Key.D, KeyModifiers.Control);
        Assert.Equal("radius", Selected(editor));

        Press(editor, Key.D, KeyModifiers.Control);
        Assert.Equal(2, editor.CaretOffsets.Count);

        Press(editor, Key.Down, KeyModifiers.Alt);
        Assert.Equal("radius + radius", Text(editor));
    });

    /// <summary>Alt+Up moves a line when there is no signature popup to cycle.</summary>
    [Fact]
    public void AltUpMovesTheLineWhenNoPopupIsOpen() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open("var a = 1;\nvar b = 2;");

        Caret(editor, 1, 0);
        Press(editor, Key.Up, KeyModifiers.Alt);

        Assert.Equal("var b = 2;\nvar a = 1;", Text(editor));
    });

    /// <summary>
    /// <b>The screenshot poses are parsed</b>, because a flag that silently does nothing
    /// photographs nothing — and a popup or a set of carets cannot be photographed any other way.
    /// </summary>
    [Fact]
    public void ThePosesAreParsed()
    {
        Spark.UI.StartupOptions options = Spark.UI.StartupOptions.Parse(
            ["--code-block", "var a = 1;", "--code-block-command", "SelectAllOccurrences"]);

        Assert.Equal("var a = 1;", options.CodeBlock);
        Assert.Equal("SelectAllOccurrences", options.CodeBlockCommand);
        Assert.Null(Spark.UI.StartupOptions.Parse([]).CodeBlock);
    }

    /// <summary>
    /// <b>`E11-T22`: the two switches that make a screenshot show a behaviour rather than a
    /// mechanism.</b> <c>--frame-node</c> centres on the block being photographed instead of the
    /// graph, which is how `E8-T43` came to be captured with its own subject off screen; and
    /// <c>--code-block-type</c> enters text through the input path, which is the only way a
    /// screenshot can exercise bracket completion or a completion trigger at all ([N112]).
    /// </summary>
    [Fact]
    public void TheHarnessSwitchesAreParsed()
    {
        Spark.UI.StartupOptions options = Spark.UI.StartupOptions.Parse(
            ["--code-block", "var a = 1;", "--code-block-in-node", "--frame-node", "--code-block-type", "b."]);

        Assert.True(options.CodeBlockInNode);
        Assert.True(options.FrameNode);
        Assert.Equal("b.", options.CodeBlockTyped);

        Spark.UI.StartupOptions bare = Spark.UI.StartupOptions.Parse([]);

        Assert.False(bare.FrameNode);
        Assert.Null(bare.CodeBlockTyped);
    }

    /// <summary>
    /// <b>Typing is not writing, and the editor's own handlers are the difference.</b>
    /// <see cref="CodeBlockEditor.TypeText"/> raises text input one character at a time, so bracket
    /// completion runs — a document write raises <c>TextChanged</c> and never <c>TextEntered</c>,
    /// which is exactly how `E8-T42` stayed invisible to three verification paths.
    /// </summary>
    [Fact]
    public void TypedTextRunsTheEditorsOwnHandlers() => HeadlessSession.Run(() =>
    {
        CodeBlockEditor editor = new();
        Window window = new() { Width = 600, Height = 400, Content = editor };

        window.Show();

        editor.Text = "var c = Circle.ByCentreNormalRadius";
        editor.FocusEditor();
        editor.CaretOffset = editor.Text.Length;

        editor.TypeText("(");

        // The closing bracket is the editor's, not the caller's: typing one character produced two.
        Assert.Equal("var c = Circle.ByCentreNormalRadius()", Text(editor));

        window.Close();
    });

    private static string Text(CodeBlockEditor editor) =>
        editor.Text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Selected(CodeBlockEditor editor)
    {
        (int start, int end) = editor.SelectionRange;

        return editor.Text[start..end].Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static int Line(CodeBlockEditor editor) =>
        Inner(editor).TextArea.Caret.Line - 1;

    private static void Caret(CodeBlockEditor editor, int line, int column)
    {
        TextEditor inner = Inner(editor);

        inner.TextArea.ClearSelection();
        inner.CaretOffset = inner.Document.GetOffset(line + 1, column + 1);
    }

    private static void Select(CodeBlockEditor editor, int fromLine, int fromColumn, int toLine, int toColumn)
    {
        TextEditor inner = Inner(editor);

        int start = inner.Document.GetOffset(fromLine + 1, fromColumn + 1);
        int end = inner.Document.GetOffset(toLine + 1, toColumn + 1);

        inner.CaretOffset = end;
        inner.TextArea.Selection = Selection.Create(inner.TextArea, start, end);
    }

    /// <summary>Types text the way the keyboard does, so the multi-caret path runs.</summary>
    private static void TypeText(CodeBlockEditor editor, string text)
    {
        TextEditor inner = Inner(editor);

        inner.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = text,
            Source = inner,
        });

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Press(CodeBlockEditor editor, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        TextEditor inner = Inner(editor);

        inner.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = inner,
        });

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static TextEditor Inner(CodeBlockEditor editor) =>
        editor.GetVisualDescendants().OfType<TextEditor>().First();

    private static (Window Window, CodeBlockEditor Editor) Open(string text)
    {
        CodeBlockEditor editor = new();
        Window window = new() { Width = 600, Height = 400, Content = editor };

        window.Show();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        editor.Text = text;

        return (window, editor);
    }

    private static void OnUiThread(Action body) => HeadlessSession.Run(body);
}
