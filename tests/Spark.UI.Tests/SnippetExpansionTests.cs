using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Spark.Scripting;
using Spark.UI.Views.Controls;

namespace Spark.UI.Tests;

/// <summary>
/// Expanding a snippet in the editor — the half of the port that needed a text area.
/// </summary>
/// <remarks>
/// <b>Tab has three owners</b>: the completion list while it is open, a snippet session while one
/// is running, and the editor's own indent the rest of the time. Most of this file is about that
/// order, because getting it wrong is not subtle — a Tab that indents instead of stepping to the
/// next field leaves somebody editing a template by hand, and a Tab that expands where it should
/// indent rewrites code the user was only trying to lay out.
/// </remarks>
public sealed class SnippetExpansionTests
{
    /// <summary>A prefix and Tab writes the loop, not the word.</summary>
    [Fact]
    public void APrefixAndTabExpands() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor) = Open();
        TextEditor inner = Inner(editor);

        inner.Document.Text = "for";
        inner.CaretOffset = 3;

        Key(editor, Avalonia.Input.Key.Tab);

        Assert.Equal(
            Indented("for (int i = 0; i < length; i++)\n{\n\t\n}", inner),
            Normalise(inner.Document.Text));

        window.Close();
    });

    /// <summary>
    /// <b>A prefix inside a word does not expand.</b> `myfor` is not the `for` snippet, and Tab
    /// after it indents like any other Tab — which is the difference between a feature and a
    /// keyboard that occasionally rewrites your code.
    /// </summary>
    [Fact]
    public void APrefixInsideAWordIndentsInstead() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor) = Open();
        TextEditor inner = Inner(editor);

        inner.Document.Text = "myfor";
        inner.CaretOffset = 5;

        Key(editor, Avalonia.Input.Key.Tab);

        Assert.StartsWith("myfor", inner.Document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("int i = 0", inner.Document.Text, StringComparison.Ordinal);

        window.Close();
    });

    /// <summary>
    /// Committing a snippet from the completion list expands it. Inserting the word `tryf` and
    /// leaving the user to write the block is the opposite of what the list offered them.
    /// </summary>
    [Fact]
    public void CommittingASnippetFromTheListExpandsIt() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor) = Open(Stub([new("tryf", CompletionGlyph.SnippetKind)]));
        TextEditor inner = Inner(editor);

        inner.Document.Text = "tryf";
        inner.CaretOffset = 4;

        Pump(editor.RequestCompletionAsync());

        Assert.True(editor.IsCompletionOpen, "the list should be open");

        Key(editor, Avalonia.Input.Key.Enter);

        Assert.Equal(Indented("try\n{\n\t\n}\nfinally\n{\n}", inner), Normalise(inner.Document.Text));
        Assert.False(editor.IsCompletionOpen, "the list should have closed");

        window.Close();
    });

    /// <summary>
    /// <b>The whole expansion is one undo step.</b> Two would leave an undo that removes the
    /// template and keeps the prefix gone, which is a state the user never typed.
    /// </summary>
    [Fact]
    public void AnExpansionUndoesInOneStep() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor) = Open();
        TextEditor inner = Inner(editor);

        inner.Document.Text = "while";
        inner.CaretOffset = 5;

        Key(editor, Avalonia.Input.Key.Tab);
        Assert.Contains("while (condition)", Normalise(inner.Document.Text), StringComparison.Ordinal);

        Assert.True(inner.Document.UndoStack.CanUndo);
        inner.Document.UndoStack.Undo();

        Assert.Equal("while", inner.Document.Text);

        window.Close();
    });

    /// <summary>Every catalogue prefix expands to the preview the catalogue promises.</summary>
    [Fact]
    public void EverySnippetExpandsToItsPreview() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor) = Open();
        TextEditor inner = Inner(editor);

        foreach (ScriptSnippet snippet in ScriptSnippets.Snippets)
        {
            inner.Document.Text = string.Empty;

            Assert.True(editor.Expand(snippet, 0, 0), $"`{snippet.Prefix}` did not expand");

            Assert.Equal(
                Indented(ScriptSnippets.Preview(snippet.Body), inner),
                Normalise(inner.Document.Text));
        }

        window.Close();
    });

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>A template's tabs become the editor's indent, whatever that is set to.</summary>
    /// <remarks>
    /// <b>Which is right, and worth asserting rather than working around.</b> The bodies are
    /// written with tabs because that is how RCS writes them and how a template reads; what lands
    /// in the document is whatever <c>ConvertTabsToSpaces</c> and <c>IndentationSize</c> say. A
    /// project that switched back to real tabs would get real tabs here without touching a body.
    /// </remarks>
    private static string Indented(string template, TextEditor editor) =>
        template.Replace("\t", editor.Options.IndentationString, StringComparison.Ordinal);

    private static TextEditor Inner(CodeBlockEditor editor) =>
        editor.GetVisualDescendants().OfType<TextEditor>().First();

    private static Func<string, int, System.Threading.CancellationToken, Task<IReadOnlyList<CodeCompletionCandidate>>>
        Stub(IReadOnlyList<CodeCompletionCandidate> answer) => (_, _, _) => Task.FromResult(answer);

    private static (Window Window, CodeBlockEditor Editor) Open(
        Func<string, int, System.Threading.CancellationToken, Task<IReadOnlyList<CodeCompletionCandidate>>>? source = null)
    {
        CodeBlockEditor editor = new() { CompletionSource = source };
        Window window = new() { Width = 600, Height = 400, Content = editor };

        window.Show();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        return (window, editor);
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

    private static void Pump(Task work)
    {
        while (!work.IsCompleted)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        work.GetAwaiter().GetResult();
    }
}
