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
