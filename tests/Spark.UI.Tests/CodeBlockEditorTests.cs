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
    private static readonly CodeSignatureCandidate[] Overloads =
    [
        new("ByCentreNormalRadius", ["Point3d centre", "Vector3d normal", "double radius"], "Circle"),
        new("ByThreePoints", ["Point3d first", "Point3d second", "Point3d third"], "Circle"),
    ];

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

    /// <summary>
    /// <b>An assignment opens the list.</b> `E6-T23`: the client typed <c>var circle = </c> in the
    /// running application and nothing happened, which is the moment a graph tool is supposed to
    /// be more helpful than a text editor rather than less.
    /// </summary>
    [Fact]
    public void AnAssignmentOpensTheList() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        Type(editor, "var circle =");

        Assert.True(editor.IsCompletionOpen);
    });

    /// <summary>
    /// The space after the assignment keeps it open rather than closing it, which is the whole
    /// difference between a list that helps and a list that flickers.
    /// </summary>
    [Fact]
    public void TheSpaceAfterAnAssignmentKeepsItOpen() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        Type(editor, "var circle = ");

        Assert.True(editor.IsCompletionOpen);
    });

    /// <summary><c>new </c> opens it, which is the other place the client named.</summary>
    [Fact]
    public void NewOpensTheList() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        Type(editor, "var circle = new ");

        Assert.True(editor.IsCompletionOpen);
    });

    /// <summary>
    /// <b>A comparison is not an assignment.</b> <c>==</c> opens nothing: somebody writing a
    /// condition is not asking what exists in scope, and a list over the code they are reading is
    /// exactly the complaint that kept this trigger to a dot for so long.
    /// </summary>
    [Fact]
    public void AComparisonDoesNotOpenTheList() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        Type(editor, "if (a ==");

        Assert.False(editor.IsCompletionOpen);
    });

    /// <summary>
    /// The first letter of a name opens it and the rest narrow it, which is one request per word
    /// rather than one per keystroke — and is the ordinary IDE behaviour the client expected.
    /// </summary>
    [Fact]
    public void TheFirstLetterOfANameOpensTheList() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        Type(editor, "D");

        Assert.True(editor.IsCompletionOpen);

        Type(editor, "ist");

        Assert.True(editor.IsCompletionOpen);
        Assert.Equal("DistanceTo", Selected(editor));
    });

    /// <summary>
    /// <b>Nothing opens inside a comment.</b> The starter script of every new code block is a
    /// comment line, so getting this wrong would put a list over the first thing a user sees.
    /// </summary>
    [Fact]
    public void ACommentOpensNothing() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub(Members));

        Type(editor, "// any name you have not declared");

        Assert.False(editor.IsCompletionOpen);
    });

    /// <summary>
    /// <b>An open parenthesis says what the call wants</b> — `E6-T22`, and the defect it fixes is
    /// that the only way to learn the parameters of <c>Circle.ByCentreNormalRadius</c> was to run
    /// the graph and read the compiler error.
    /// </summary>
    [Fact]
    public void AnOpenParenthesisShowsTheSignature() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub([]), Signatures());

        Type(editor, "var c = Circle.ByCentreNormalRadius(");

        Assert.True(editor.IsSignatureOpen);
        Assert.Equal("ByCentreNormalRadius", editor.ActiveSignature?.Name);
        Assert.Equal(0, editor.ActiveParameter);
    });

    /// <summary>The parameter being typed is the one the popup emphasises.</summary>
    [Fact]
    public void TheActiveParameterFollowsTheCommas() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub([]), Signatures(activeParameter: 2));

        Type(editor, "var c = Circle.ByCentreNormalRadius(a, b,");

        Assert.True(editor.IsSignatureOpen);
        Assert.Equal(2, editor.ActiveParameter);
    });

    /// <summary>
    /// <b>Alt+Down cycles the overloads while the popup is up</b>, and wraps — VS Code's binding,
    /// and the reason Move Line Down has to yield to it for exactly as long as it is visible.
    /// </summary>
    [Fact]
    public void AltDownCyclesTheOverloads() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub([]), Signatures());

        Type(editor, "var c = Circle.ByCentreNormalRadius(");

        Assert.Equal(2, editor.SignatureCount);

        Key(editor, Avalonia.Input.Key.Down, KeyModifiers.Alt);

        Assert.Equal("ByThreePoints", editor.ActiveSignature?.Name);

        Key(editor, Avalonia.Input.Key.Down, KeyModifiers.Alt);

        Assert.Equal("ByCentreNormalRadius", editor.ActiveSignature?.Name);
    });

    /// <summary>Escape closes the popup, and the text is untouched.</summary>
    [Fact]
    public void EscapeClosesTheSignature() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub([]), Signatures());

        Type(editor, "var c = Circle.ByCentreNormalRadius(");
        Key(editor, Avalonia.Input.Key.Escape);

        Assert.False(editor.IsSignatureOpen);
        Assert.Equal("var c = Circle.ByCentreNormalRadius(", editor.Text);
    });

    /// <summary>
    /// <b>A caret that leaves the call closes the popup.</b> The source answers null once the
    /// arguments are finished, and a popup that stays up describes a call the user has moved on
    /// from while covering the one they are writing.
    /// </summary>
    [Fact]
    public void LeavingTheCallClosesTheSignature() => OnUiThread(() =>
    {
        bool inside = true;

        (Window _, CodeBlockEditor editor) = Open(
            Stub([]),
            (_, _, _) => Task.FromResult<CodeSignatureInfo?>(
                inside ? new CodeSignatureInfo(Overloads, 0, 0) : null));

        Type(editor, "var c = Circle.ByCentreNormalRadius(");

        Assert.True(editor.IsSignatureOpen);

        inside = false;
        Type(editor, ")");

        Assert.False(editor.IsSignatureOpen);
    });

    /// <summary>With no signature source — scripting off — a parenthesis does nothing.</summary>
    [Fact]
    public void WithNoSignatureSourceNothingOpens() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = Open(Stub([]));

        Type(editor, "var c = Circle.ByCentreNormalRadius(");

        Assert.False(editor.IsSignatureOpen);
    });

    /// <summary>
    /// <b>The signature is placed by the caret's position on screen, not in the document</b> — the
    /// same subtraction the completion list makes, and this is the second popup that the M1.5
    /// spike's C3 finding now guards.
    /// </summary>
    [Fact]
    public void TheSignatureFollowsTheCaretOnScreenRatherThanInTheDocument() => OnUiThread(() =>
    {
        (Window window, CodeBlockEditor editor) = Open(Stub(Members), Signatures());

        editor.Text = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"var line{i} = {i};"));
        Layout(window);

        TextEditor inner = Inner(editor);

        inner.CaretOffset = inner.Document.GetOffset(180, 1);
        inner.TextArea.Caret.BringCaretToView();
        Layout(window);

        double scrolled = inner.TextArea.TextView.ScrollOffset.Y;
        Assert.True(scrolled > 100.0, $"the view should have scrolled: {scrolled}");

        Pump(editor.RequestSignatureAsync());
        Layout(window);

        // Taken from the document position it would be two and a half thousand pixels below the
        // control, which is to say invisible on every screenful but the first.
        Assert.InRange(editor.SignatureOrigin.Y, 0.0, window.Height);
    });

    /// <summary>
    /// <b>The signature hangs above the caret's line and the list below it</b>, so that a call
    /// being written is described and completed at once with neither popup covering the other or
    /// the code.
    /// </summary>
    /// <remarks>
    /// Unscrolled on purpose. <c>BringCaretToView</c> in the headless session leaves the caret's
    /// line exactly at the top edge, where both popups clamp to the pane's top and the ordering
    /// cannot be seen — which is a property of the test harness rather than of the placement.
    /// </remarks>
    [Fact]
    public void TheSignatureSitsAboveTheList() => OnUiThread(() =>
    {
        (Window window, CodeBlockEditor editor) = Open(Stub(Members), Signatures());

        editor.Text = "var a = 1;\nvar b = 2;\nvar c = Circle.ByCentreNormalRadius(";
        Layout(window);

        Pump(editor.RequestSignatureAsync());
        Pump(editor.RequestCompletionAsync());
        Layout(window);

        Assert.True(editor.IsSignatureOpen);
        Assert.True(
            editor.SignatureOrigin.Y < editor.CompletionOrigin.Y,
            $"the signature ({editor.SignatureOrigin.Y}) sits above the list ({editor.CompletionOrigin.Y})");
    });

    private static Func<string, int, CancellationToken, Task<IReadOnlyList<CodeCompletionCandidate>>> Stub(
        IReadOnlyList<CodeCompletionCandidate> answer) =>
        (_, _, _) => Task.FromResult(answer);

    /// <summary>A signature source that always answers the same overloads.</summary>
    private static Func<string, int, CancellationToken, Task<CodeSignatureInfo?>> Signatures(
        int activeParameter = 0,
        params CodeSignatureCandidate[] overloads) =>
        (_, _, _) => Task.FromResult<CodeSignatureInfo?>(
            new CodeSignatureInfo(overloads.Length > 0 ? overloads : Overloads, 0, activeParameter));

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

    private static void Key(CodeBlockEditor editor, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        TextEditor inner = Inner(editor);

        inner.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = inner,
        });
    }

    private static string? Selected(CodeBlockEditor editor) =>
        editor.SelectedCandidate?.DisplayText;

    private static TextEditor Inner(CodeBlockEditor editor) =>
        editor.GetVisualDescendants().OfType<TextEditor>().First();

    private static (Window Window, CodeBlockEditor Editor) Open(
        Func<string, int, CancellationToken, Task<IReadOnlyList<CodeCompletionCandidate>>>? source = null,
        Func<string, int, CancellationToken, Task<CodeSignatureInfo?>>? signatures = null)
    {
        CodeBlockEditor editor = new() { CompletionSource = source, SignatureSource = signatures };
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

    /// <summary>
    /// <b>`E8-T41`: an editor that lets its popups leave it cannot also clip to itself.</b> The
    /// completion list is an overlay inside the control rather than a <c>Popup</c> (`E6-T12`), so
    /// on a code block two lines tall it was clipped to the block — which is what the client saw:
    /// a sliver of a list inside the node.
    /// </summary>
    [Fact]
    public void AnAreaTurnsOffTheEditorsOwnClipping() => OnUiThread(() =>
    {
        (Window _, CodeBlockEditor editor) = OpenSmall(Stub(Members), area: null);

        Assert.True(editor.ClipToBounds, "an editor with no area given clips to itself");

        editor.PopupArea = new Rect(-100, -200, 600, 400);
        Assert.False(editor.ClipToBounds, "an editor whose popups may leave it cannot clip to itself");

        editor.PopupArea = null;
        Assert.True(editor.ClipToBounds, "taking the area away puts the clipping back");
    });

    /// <summary>
    /// <b>And the area, not the control, is what a popup is held inside.</b> An area too narrow for
    /// the list pins it to the area's own left edge — which is outside the editor whenever the
    /// editor is not in the corner of its host, and was pinned to the editor's edge before.
    /// </summary>
    /// <remarks>
    /// <b>Asserted on the horizontal, deliberately.</b> The headless session has no font metrics —
    /// every glyph measures zero wide (<c>E11-T21</c>) — so a popup's height there is not the height
    /// it has in the application, and an assertion about vertical room would be measuring the
    /// harness. The pinning rule holds whatever the text measures.
    /// </remarks>
    [Fact]
    public void ThePopupsAreHeldInsideTheAreaRatherThanTheEditor() => OnUiThread(() =>
    {
        (Window window, CodeBlockEditor editor) = OpenSmall(
            Stub(Members), area: new Rect(-100, -200, 1, 1));

        editor.Text = "a.";
        Layout(window);

        Inner(editor).CaretOffset = 2;

        Pump(editor.RequestCompletionAsync());
        Layout(window);

        // Left of the editor's own left edge, which the old clamp - into [0, this control's
        // width] - could not produce however small the area was.
        Assert.True(
            editor.CompletionOrigin.X < 0.0,
            $"the list should be pinned to the area's left edge, not to the editor's: {editor.CompletionOrigin.X}");

        Assert.InRange(editor.CompletionOrigin.X, -100.0, -99.0);
    });

    /// <summary>
    /// And an area big enough keeps the list inside it rather than anywhere it pleases.
    /// </summary>
    [Fact]
    public void TheAreaIsWhatThePopupsAreKeptInside() => OnUiThread(() =>
    {
        (Window window, CodeBlockEditor editor) = OpenSmall(
            Stub(Members), area: new Rect(-100, -200, 600, 400));

        editor.Text = "a.";
        Layout(window);

        Inner(editor).CaretOffset = 2;

        Pump(editor.RequestCompletionAsync());
        Layout(window);

        Assert.InRange(editor.CompletionOrigin.X, -100.0, 500.0);
        Assert.InRange(editor.CompletionOrigin.Y, -200.0, 200.0);
    });

    /// <summary>An editor hosted small on a surface, the way a code block hosts one.</summary>
    private static (Window Window, CodeBlockEditor Editor) OpenSmall(
        Func<string, int, CancellationToken, Task<IReadOnlyList<CodeCompletionCandidate>>>? source,
        Rect? area)
    {
        CodeBlockEditor editor = new() { CompletionSource = source, Width = 300, Height = 40 };

        Avalonia.Controls.Canvas host = new();
        Avalonia.Controls.Canvas.SetLeft(editor, 100);
        Avalonia.Controls.Canvas.SetTop(editor, 200);
        host.Children.Add(editor);

        Window window = new() { Width = 600, Height = 400, Content = host };

        window.Show();
        Layout(window);

        editor.PopupArea = area;

        return (window, editor);
    }
}
