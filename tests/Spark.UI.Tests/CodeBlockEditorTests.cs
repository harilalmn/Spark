using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Spark.UI.Views.Controls;

namespace Spark.UI.Tests;

/// <summary>
/// The code block's editing surface — `E6-T11` and `E6-T12`, which together close `E6-T7`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The popup is where the risk is</b>, which is what `E6-T12` says and is why most of this file
/// is about it rather than about the editor. Placement, filtering, what a key does while the list
/// is open, and whether the editor is still the thing receiving keys — those are where AvalonEdit
/// and AvaloniaEdit differ, and none of them is visible from a compile.
/// </para>
/// <para>
/// The completion source here is a stub. What Roslyn answers is
/// <see cref="WireTypedCompletionTests"/>'s subject; what the control does with an answer is this
/// one's, and mixing them would make every popup test pay for a Roslyn composition.
/// </para>
/// </remarks>
public sealed class CodeBlockEditorTests
{
    private static readonly CodeCompletionCandidate[] Members =
    [
        new("DistanceTo", "Method"),
        new("X", "Property"),
        new("Y", "Property"),
        new("Z", "Property"),
    ];

    /// <summary>The editor holds text and reports a caret, which is the floor everything stands on.</summary>
    [Fact]
    public void TheEditorHoldsText() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open();

        editor.Text = "return a;";

        Assert.Equal("return a;", editor.Text);
    });

    /// <summary>
    /// <b>Typing a dot opens the list.</b> A dot and nothing else: opening on every letter is what
    /// an IDE does, and in a pane this narrow it covers the code the moment a name is begun.
    /// </summary>
    [Fact]
    public void TypingADotOpensTheList() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        Type(editor, "return centre.");

        Assert.True(editor.IsCompletionOpen);
        Assert.Equal(4, editor.Candidates.Count);
    });

    /// <summary>Ctrl+Space asks for the list without a dot, which is the explicit request.</summary>
    [Fact]
    public void ControlSpaceAsksForTheList() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        editor.Text = "return cen";
        Pump(editor.RequestCompletionAsync());

        Assert.True(editor.IsCompletionOpen);
    });

    /// <summary>
    /// <b>Typing after the list opens narrows it rather than asking again.</b> A request per
    /// keystroke would be correct and would also make the list flicker on Roslyn's slow first call.
    /// </summary>
    [Fact]
    public void TypingNarrowsTheList() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        Type(editor, "return centre.");
        Type(editor, "Di");

        Assert.True(editor.IsCompletionOpen);
        Assert.Equal("DistanceTo", Selected(editor));
    });

    /// <summary>A prefix that matches nothing closes the list rather than leaving a blank box.</summary>
    [Fact]
    public void APrefixThatMatchesNothingClosesTheList() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        Type(editor, "return centre.");
        Type(editor, "zzz");

        Assert.False(editor.IsCompletionOpen);
    });

    /// <summary>
    /// <b>Enter replaces what was typed rather than inserting after it.</b> Getting this wrong
    /// gives <c>centre.DiDistanceTo</c>, which looks like a completion engine that does not
    /// understand its own list.
    /// </summary>
    [Fact]
    public void EnterCommitsTheSelectionOverWhatWasTyped() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        Type(editor, "return centre.");
        Type(editor, "Di");
        Key(editor, Avalonia.Input.Key.Enter);

        Assert.Equal("return centre.DistanceTo", editor.Text);
        Assert.False(editor.IsCompletionOpen);
    });

    /// <summary>Escape closes the list and leaves the text exactly as it was.</summary>
    [Fact]
    public void EscapeClosesTheListWithoutChangingTheText() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        Type(editor, "return centre.");
        Key(editor, Avalonia.Input.Key.Escape);

        Assert.False(editor.IsCompletionOpen);
        Assert.Equal("return centre.", editor.Text);
    });

    /// <summary>The arrow keys move the selection while the list is open.</summary>
    [Fact]
    public void TheArrowKeysMoveTheSelection() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        Type(editor, "return centre.");
        Key(editor, Avalonia.Input.Key.Down);

        Assert.Equal("X", Selected(editor));

        Key(editor, Avalonia.Input.Key.Up);

        Assert.Equal("DistanceTo", Selected(editor));
    });

    /// <summary>
    /// <b>An empty answer does not open an empty box.</b> A rectangle with nothing in it, sitting
    /// over the code, is worse than no popup — and an empty answer is the normal case after a dot
    /// on something the compiler cannot type.
    /// </summary>
    [Fact]
    public void AnEmptyAnswerDoesNotOpenTheList() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub([]));

        Type(editor, "return centre.");

        Assert.False(editor.IsCompletionOpen);
    });

    /// <summary>
    /// With no completion source at all — an inspector in a session with scripting off — typing a
    /// dot does nothing. The control never reaches for a compiler on its own.
    /// </summary>
    [Fact]
    public void WithNoSourceNothingOpens() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open();

        Type(editor, "return centre.");

        Assert.False(editor.IsCompletionOpen);
    });

    /// <summary>
    /// <b>Setting the text is not a user edit.</b> The inspector pushes a block's source in when
    /// the selection changes, and a source ending in a dot must not open a list nobody asked for.
    /// </summary>
    [Fact]
    public void SettingTheTextDoesNotOpenTheList() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        editor.Text = "return centre.";

        Assert.False(editor.IsCompletionOpen);
    });

    /// <summary>
    /// <b>The popup is placed by the caret's position on screen, not in the document.</b> This is
    /// the M1.5 spike's C3 finding turned into a guard: without subtracting the scroll offset the
    /// popup is right on the first screenful and drifts off the bottom of every one after.
    /// </summary>
    /// <remarks>
    /// Headless drawing has no font metrics, so horizontal placement cannot be asserted — every
    /// glyph measures zero wide. Line height does not need a font, so the vertical is real, and the
    /// vertical is the axis the scroll offset moves.
    /// </remarks>
    [Fact]
    public void ThePopupFollowsTheCaretOnScreenRatherThanInTheDocument() => OnUiThread(() =>
    {
        (Window window, CodeBlockEditor editor) = Open(Stub(Members));

        editor.Text = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"var line{i} = {i};"));
        Layout(window);

        TextEditor inner = Inner(editor);

        inner.CaretOffset = inner.Document.GetOffset(180, 1);
        inner.TextArea.Caret.BringCaretToView();
        Layout(window);

        double scrolled = inner.TextArea.TextView.ScrollOffset.Y;
        Assert.True(scrolled > 100.0, $"the view should have scrolled: {scrolled}");

        Pump(editor.RequestCompletionAsync());
        Layout(window);

        double offset = editor.CompletionOrigin.Y;

        // Inside the visible height, which is the property that matters. Taken from the *document*
        // position it would be thousands of pixels below the control, and the list would be drawn
        // off the bottom of the pane on every screenful but the first.
        Assert.InRange(offset, 0.0, window.Height);
        Assert.True(
            offset < scrolled,
            $"the origin ({offset}) should be the caret minus the scroll offset ({scrolled}), not the document position");
    });

    private static Func<string, int, CancellationToken, Task<IReadOnlyList<CodeCompletionCandidate>>> Stub(
        IReadOnlyList<CodeCompletionCandidate> answer) =>
        (_, _, _) => Task.FromResult(answer);

    /// <summary>Appends text the way a user types it, so the control's own handlers run.</summary>
    private static void Type(CodeBlockEditor editor, string text)
    {
        TextEditor inner = Inner(editor);

        foreach (char c in text)
        {
            inner.Document.Insert(inner.CaretOffset, c.ToString());
            Pump();
        }
    }

    private static void Key(CodeBlockEditor editor, Key key)
    {
        TextEditor inner = Inner(editor);

        inner.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            Source = inner,
        });
    }

    private static string? Selected(CodeBlockEditor editor) =>
        editor.SelectedCandidate?.DisplayText;

    private static TextEditor Inner(CodeBlockEditor editor) =>
        editor.GetVisualDescendants().OfType<TextEditor>().First();

    private static (Window Window, CodeBlockEditor Editor) Open(
        Func<string, int, CancellationToken, Task<IReadOnlyList<CodeCompletionCandidate>>>? source = null)
    {
        CodeBlockEditor editor = new() { CompletionSource = source };
        Window window = new() { Width = 600, Height = 400, Content = editor };

        window.Show();
        Layout(window);

        return (window, editor);
    }

    private static void Layout(Window window)
    {
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();
    }

    /// <summary>Runs the dispatcher until the queued work is done.</summary>
    /// <remarks>
    /// The control starts its completion request without awaiting it, which is what an editor has
    /// to do; a test therefore has to give the loop a turn or it asserts against a list that has
    /// not arrived. Every completion here is already finished, so one drain is enough.
    /// </remarks>
    private static void Pump()
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Pump(Task work)
    {
        while (!work.IsCompleted)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        work.GetAwaiter().GetResult();
    }

    private static void OnUiThread(Action body) => HeadlessSession.Run(body);
}
