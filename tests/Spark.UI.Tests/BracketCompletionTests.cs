using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Spark.UI.Views.Controls;

namespace Spark.UI.Tests;

/// <summary>
/// Closing brackets and quotes as they are typed, ported from RCS's <c>BracketCompletion</c>.
/// </summary>
/// <remarks>
/// <b>Most of this file is about when it should NOT fire.</b> Auto-closing is a small convenience
/// and a large annoyance when it guesses wrong: a closer landing in the middle of a word, a
/// <c>&gt;</c> after <c>a &lt; b</c>, a quote closed inside a string that was already open. Each of
/// those is a case below, because each is a way this feature makes an editor worse than not having
/// it at all.
/// </remarks>
public sealed class BracketCompletionTests
{
    /// <summary>An opener on empty space closes itself and leaves the caret inside.</summary>
    [Theory]
    [InlineData('(', "()")]
    [InlineData('[', "[]")]
    [InlineData('{', "{}")]
    public void AnOpenerClosesItself(char opener, string expected) => HeadlessSession.Run(() =>
    {
        (Window window, TextEditor inner) = Open();

        Type(inner, opener);

        Assert.Equal(expected, inner.Document.Text);
        Assert.Equal(1, inner.CaretOffset);

        window.Close();
    });

    /// <summary>
    /// <b>An opener typed before a word does not close</b>, or the closer lands in the middle of
    /// it: wrapping `value` by typing `(` in front of it would give `()value`.
    /// </summary>
    [Fact]
    public void AnOpenerBeforeAWordDoesNotClose() => HeadlessSession.Run(() =>
    {
        (Window window, TextEditor inner) = Open("value");

        inner.CaretOffset = 0;
        Type(inner, '(');

        Assert.Equal("(value", inner.Document.Text);

        window.Close();
    });

    /// <summary>
    /// <b><c>&lt;</c> closes after an identifier and not otherwise.</b> In C# it is a comparison
    /// far more often than a generic, and `a &lt;&gt; b` is not what anybody typing `a &lt; b`
    /// wanted.
    /// </summary>
    [Theory]
    [InlineData("List", "List<>")]
    [InlineData("a ", "a <")]
    public void TheLessThanSignClosesOnlyAfterAnIdentifier(string before, string expected) =>
        HeadlessSession.Run(() =>
        {
            (Window window, TextEditor inner) = Open(before);

            inner.CaretOffset = before.Length;
            Type(inner, '<');

            Assert.Equal(expected, inner.Document.Text);

            window.Close();
        });

    /// <summary>Typing the closer that is already there steps over it rather than doubling it.</summary>
    [Fact]
    public void TypingTheClosingBracketStepsOverIt() => HeadlessSession.Run(() =>
    {
        (Window window, TextEditor inner) = Open();

        Type(inner, '(');
        Type(inner, ')');

        Assert.Equal("()", inner.Document.Text);
        Assert.Equal(2, inner.CaretOffset);

        window.Close();
    });

    /// <summary>An opener typed over a selection wraps it. Replacing it is never what was meant.</summary>
    [Fact]
    public void AnOpenerWrapsTheSelection() => HeadlessSession.Run(() =>
    {
        (Window window, TextEditor inner) = Open("radius");

        inner.Select(0, 6);
        Type(inner, '(');

        Assert.Equal("(radius)", inner.Document.Text);
        Assert.Equal("radius", inner.SelectedText);

        window.Close();
    });

    /// <summary>A quote opens a string; the next one closes it rather than opening another.</summary>
    [Fact]
    public void AQuoteClosesOnceAndThenSteps() => HeadlessSession.Run(() =>
    {
        (Window window, TextEditor inner) = Open();

        Type(inner, '"');
        Assert.Equal("\"\"", inner.Document.Text);

        Type(inner, '"');
        Assert.Equal("\"\"", inner.Document.Text);
        Assert.Equal(2, inner.CaretOffset);

        window.Close();
    });

    /// <summary>Enter between braces opens an indented block with the closer below it.</summary>
    [Fact]
    public void EnterBetweenBracesOpensABlock() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor, TextEditor inner) = OpenBoth("if (x)\n{}");

        inner.CaretOffset = inner.Document.Text.IndexOf('}', StringComparison.Ordinal);
        Key(editor, Avalonia.Input.Key.Enter);

        string text = inner.Document.Text.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal("if (x)\n{\n    \n}", text);
        Assert.Equal(text.IndexOf("\n    ", StringComparison.Ordinal) + 5, inner.CaretOffset);

        window.Close();
    });

    /// <summary>Backspace between an empty pair removes both halves, not just the one behind.</summary>
    [Fact]
    public void BackspaceBetweenAnEmptyPairRemovesBoth() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor, TextEditor inner) = OpenBoth();

        Type(inner, '(');
        Assert.Equal("()", inner.Document.Text);

        Key(editor, Avalonia.Input.Key.Back);

        Assert.Equal(string.Empty, inner.Document.Text);

        window.Close();
    });

    /// <summary>
    /// <b>It stands down while extra carets are up.</b> A multi-caret edit applies one text input
    /// at every caret as a single update; a bracket completion in the middle of that would close
    /// at one caret and not the others.
    /// </summary>
    [Fact]
    public void ItStandsDownForMultipleCarets() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor, TextEditor inner) = OpenBoth("a\nb");

        inner.CaretOffset = 1;
        editor.Invoke("AddCaretBelow");

        Assert.True(editor.CaretOffsets.Count > 1, "the second caret should be up");

        Type(inner, '(');

        Assert.DoesNotContain(")", inner.Document.Text, StringComparison.Ordinal);

        window.Close();
    });

    private static void Type(TextEditor inner, char c) =>
        inner.TextArea.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = c.ToString(),
            Source = inner.TextArea,
        });

    private static void Key(CodeBlockEditor editor, Key key)
    {
        TextEditor inner = editor.GetVisualDescendants().OfType<TextEditor>().First();

        inner.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = KeyModifiers.None,
            Source = inner,
        });
    }

    private static (Window Window, TextEditor Inner) Open(string text = "")
    {
        (Window window, CodeBlockEditor _, TextEditor inner) = OpenBoth(text);

        return (window, inner);
    }

    private static (Window Window, CodeBlockEditor Editor, TextEditor Inner) OpenBoth(string text = "")
    {
        CodeBlockEditor editor = new();
        Window window = new() { Width = 600, Height = 400, Content = editor };

        window.Show();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        TextEditor inner = editor.GetVisualDescendants().OfType<TextEditor>().First();

        inner.Document.Text = text;
        inner.CaretOffset = text.Length;

        return (window, editor, inner);
    }
}
