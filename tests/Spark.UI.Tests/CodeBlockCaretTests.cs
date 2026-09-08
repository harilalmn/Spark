using System;
using Avalonia;
using Avalonia.Controls;
using Spark.UI.Views.Controls;

namespace Spark.UI.Tests;

/// <summary>
/// A code block opens ready to type in — `E8-T60`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: inserting a block should leave it in edit mode with the caret at
/// line 1 column 1, and opening an existing one should put the caret at the end of the last line.
/// </para>
/// <para>
/// <b>It is one rule, not two.</b> A new block's text has been empty since `E6-T18`, so *the end of
/// the text* and *line 1 column 1* are the same position — the caret always goes to the end, and
/// the two behaviours the client described fall out of that single rule. Asserted here in both
/// shapes anyway, because the equivalence is the sort of thing that stops being true quietly if the
/// starter ever gains a character.
/// </para>
/// </remarks>
public sealed class CodeBlockCaretTests
{
    /// <summary>
    /// <b>An empty block puts the caret at the start, which is also the end.</b> This is the
    /// insert gesture's case: place a block, and the user types.
    /// </summary>
    [Fact]
    public void AnEmptyBlockOpensWithTheCaretAtTheStart() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor) = Open(string.Empty);

        editor.PlaceCaretAtEnd();

        Assert.Equal(0, editor.CaretOffset);

        window.Close();
    });

    /// <summary>An existing block puts it after the last character.</summary>
    [Fact]
    public void AnExistingBlockOpensWithTheCaretAtTheEnd() => HeadlessSession.Run(() =>
    {
        const string Source = "var a = 1;\nvar b = 2;";

        (Window window, CodeBlockEditor editor) = Open(Source);

        editor.PlaceCaretAtEnd();

        Assert.Equal(Source.Length, editor.CaretOffset);

        window.Close();
    });

    /// <summary>
    /// <b>And it is the end of the <i>last</i> line, not the end of the first.</b> Worth its own
    /// assertion because an off-by-one-line caret looks right in a one-line block and is wrong in
    /// every other.
    /// </summary>
    [Fact]
    public void TheCaretIsOnTheLastLine() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor) = Open("var a = 1;\nvar b = 2;\nvar c = 3;");

        editor.PlaceCaretAtEnd();

        // Three lines of ten characters and two newlines: the caret sits past the last `;`.
        Assert.Equal(32, editor.CaretOffset);

        window.Close();
    });

    /// <summary>
    /// Moving the caret does not disturb the text, which is the one thing this must not do to a
    /// block somebody is about to edit.
    /// </summary>
    [Fact]
    public void TheTextIsUnchanged() => HeadlessSession.Run(() =>
    {
        const string Source = "var a = 1;\nvar b = 2;";

        (Window window, CodeBlockEditor editor) = Open(Source);

        editor.PlaceCaretAtEnd();

        Assert.Equal(Source, editor.Text);

        window.Close();
    });

    /// <summary>An editor that was never shown does not throw, it just sits at zero.</summary>
    /// <remarks>
    /// Inside the session because constructing the control at all needs a font manager - the first
    /// draft built it on the test thread and failed in <c>TextFormatterImpl</c>, naming nothing in
    /// this repository.
    /// </remarks>
    [Fact]
    public void AnEditorThatWasNeverShownIsSafe() => HeadlessSession.Run(() =>
    {
        CodeBlockEditor editor = new();

        editor.PlaceCaretAtEnd();

        Assert.Equal(0, editor.CaretOffset);
    });

    /// <summary>
    /// <b>The control the editor focuses can actually take focus</b> - which is the whole of
    /// `E8-T61`.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>FocusEditor</c> called <c>TextEditor.Focus()</c>, and AvaloniaEdit's <c>TextEditor</c>
    /// declares <c>Focusable = false</c> - so it returned false, put the caret nowhere, and said
    /// nothing. It had been that way since `E8-T39`; nobody saw it because the click that opened
    /// the editor was followed by a second click to start typing, and that second click focused it
    /// the ordinary way. `E8-T60` removed the reason for the second click, and the missing caret
    /// became the whole experience.
    /// </para>
    /// <para>
    /// <b>This asserts the half that can be asserted.</b> Headless Avalonia does not grant focus at
    /// all - <c>Focus()</c> returns false for every control, activated window or not, which was
    /// established by probing rather than assumed - so <c>IsFocused</c> is not a usable oracle
    /// here. <i>The thing we focus is able to take focus</i> is, and it is precisely the half that
    /// was wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEditorFocusesSomethingThatCanTakeFocus() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor) = Open("var a = 1;");

        Assert.NotNull(editor.FocusTarget);
        Assert.True(
            editor.FocusTarget!.Focusable,
            "the editor focuses a control whose Focusable is false, which can never work");

        window.Close();
    });

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
}
