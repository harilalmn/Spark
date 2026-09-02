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
/// Find, replace and the text size — slice 6 of the RCS port.
/// </summary>
/// <remarks>
/// <b>The panel is AvaloniaEdit's, and that is the port's one deliberate substitution.</b> RCS
/// writes its own find bar in 593 lines of WPF because AvalonEdit's <c>SearchPanel</c> finds but
/// cannot replace; AvaloniaEdit's can, and Avalonia has no adorner layer to float a hand-built one
/// over. So the gestures are RCS's — Ctrl+F, Ctrl+H, seeded from the selection — and the control
/// underneath them is not.
/// </remarks>
public sealed class SearchAndZoomTests
{
    /// <summary>Ctrl+F opens the bar; Ctrl+H opens it ready to replace.</summary>
    [Theory]
    [InlineData(Key.F, false)]
    [InlineData(Key.H, true)]
    public void TheShortcutsOpenTheFindBar(Key key, bool replacing) => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor, TextEditor inner) = Open("var radius = 3.0;");

        Assert.False(editor.IsSearchOpen);

        Press(inner, key, KeyModifiers.Control);

        Assert.True(editor.IsSearchOpen, "the find bar should be open");
        Assert.Equal(replacing, IsReplacing(editor));

        window.Close();
    });

    /// <summary>
    /// <b>The selection seeds the search box</b>, which is the whole of "select a word, press
    /// Ctrl+F". Opening empty means typing the word again, which is a dialog rather than a
    /// shortcut.
    /// </summary>
    [Fact]
    public void TheSelectionSeedsTheSearch() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor, TextEditor inner) = Open("var radius = 3.0;");

        inner.Select(4, 6);
        editor.OpenSearch(replacing: false);

        Assert.Equal("radius", Panel(editor).SearchPattern);

        window.Close();
    });

    /// <summary>A multi-line selection does not seed it, because a find box is one line.</summary>
    [Fact]
    public void AMultiLineSelectionDoesNotSeedTheSearch() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor, TextEditor inner) = Open("one\ntwo");

        inner.Select(0, 7);
        editor.OpenSearch(replacing: false);

        Assert.True(
            string.IsNullOrEmpty(Panel(editor).SearchPattern),
            $"the box should be empty, not '{Panel(editor).SearchPattern}'");

        window.Close();
    });

    /// <summary>Ctrl+wheel resizes the text, and stops at both ends.</summary>
    [Fact]
    public void CtrlWheelResizesTheTextWithinBounds() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor, TextEditor inner) = Open("var a = 1;");

        double before = inner.FontSize;

        editor.Zoom(3);
        Assert.Equal(before + 3, inner.FontSize);

        editor.Zoom(-6);
        Assert.Equal(before - 3, inner.FontSize);

        // A step rather than a factor, so the two ends move at the same rate — and neither runs off.
        editor.Zoom(1000);
        Assert.Equal(CodeBlockEditor.MaximumFontSize, inner.FontSize);

        editor.Zoom(-1000);
        Assert.Equal(CodeBlockEditor.MinimumFontSize, inner.FontSize);

        window.Close();
    });

    /// <summary>Ctrl+0 puts it back, which is the only way out of an accidental zoom.</summary>
    [Fact]
    public void CtrlZeroResetsTheTextSize() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor, TextEditor inner) = Open("var a = 1;");

        editor.Zoom(9);
        Assert.NotEqual(CodeBlockEditor.DefaultFontSize, inner.FontSize);

        Press(inner, Key.D0, KeyModifiers.Control);

        Assert.Equal(CodeBlockEditor.DefaultFontSize, inner.FontSize);

        window.Close();
    });

    private static AvaloniaEdit.Search.SearchPanel Panel(CodeBlockEditor editor) =>
        editor.GetVisualDescendants().OfType<AvaloniaEdit.Search.SearchPanel>().FirstOrDefault()
        ?? Reflected(editor);

    /// <summary>The panel is installed on the text area, which does not always host it visually.</summary>
    private static AvaloniaEdit.Search.SearchPanel Reflected(CodeBlockEditor editor) =>
        (AvaloniaEdit.Search.SearchPanel)typeof(CodeBlockEditor)
            .GetField("_search", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(editor)!;

    private static bool IsReplacing(CodeBlockEditor editor) => Panel(editor).IsReplaceMode;

    private static void Press(TextEditor inner, Key key, KeyModifiers modifiers) =>
        inner.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = inner,
        });

    private static (Window Window, CodeBlockEditor Editor, TextEditor Inner) Open(string text)
    {
        CodeBlockEditor editor = new();
        Window window = new() { Width = 600, Height = 400, Content = editor };

        window.Show();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        TextEditor inner = editor.GetVisualDescendants().OfType<TextEditor>().First();
        inner.Document.Text = text;

        return (window, editor, inner);
    }
}
