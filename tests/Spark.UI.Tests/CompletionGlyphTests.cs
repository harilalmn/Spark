using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Indentation.CSharp;
using Spark.UI.Views.Controls;

namespace Spark.UI.Tests;

/// <summary>
/// The completion badges and the editor's own settings, both taken from RCS's <c>CodeEditor</c>.
/// </summary>
/// <remarks>
/// <b>Ported rather than copied.</b> RCS is WPF and AvalonEdit, Spark is Avalonia and
/// AvaloniaEdit; the badge is a control tree here where it is <c>DrawingImage</c> geometry there.
/// What has to survive the port is the vocabulary — the same letter and the same colour for the
/// same kind — because the value of matching them is that somebody moving between the two
/// applications does not have to relearn that purple means a method.
/// </remarks>
public sealed class CompletionGlyphTests
{
    /// <summary>Every kind Roslyn tags a candidate with has a badge, and it is RCS's badge.</summary>
    [Theory]
    [InlineData("Class", "C", 0xFFE0A14E)]
    [InlineData("Structure", "S", 0xFF64B57B)]
    [InlineData("Interface", "I", 0xFF5EB4DC)]
    [InlineData("Delegate", "D", 0xFFB486D0)]
    [InlineData("Method", "M", 0xFFA27BDC)]
    [InlineData("ExtensionMethod", "M", 0xFFA27BDC)]
    [InlineData("Property", "P", 0xFF749EDE)]
    [InlineData("Field", "F", 0xFF749EDE)]
    [InlineData("Event", "V", 0xFFD6869C)]
    [InlineData("Namespace", "N", 0xFF969696)]
    [InlineData("Keyword", "K", 0xFF6394D2)]
    [InlineData("Snippet", "{", 0xFF4FC08D)]
    public void AKindDrawsTheBadgeRcsDrawsForIt(string kind, string letter, uint fill)
    {
        Assert.True(CompletionGlyph.Knows(kind), $"{kind} has no badge");
        Assert.Equal(letter, CompletionGlyph.LetterFor(kind));

        SolidColorBrush brush = Assert.IsAssignableFrom<ISolidColorBrush>(CompletionGlyph.BrushFor(kind))
            is { } solid
            ? new SolidColorBrush(solid.Color)
            : throw new InvalidOperationException("not a solid brush");

        Assert.Equal(Color.FromUInt32(fill), brush.Color);
    }

    /// <summary>
    /// A tag nobody has mapped draws the keyword badge rather than nothing. A row with no badge in
    /// a list where every other row has one reads as a broken row, not as an unknown kind.
    /// </summary>
    [Fact]
    public void AnUnknownKindFallsBackRatherThanDrawingNothing()
    {
        Assert.False(CompletionGlyph.Knows("SomethingRoslynAddedLater"));
        Assert.Equal(CompletionGlyph.LetterFor("Keyword"), CompletionGlyph.LetterFor("SomethingRoslynAddedLater"));
        Assert.NotNull(CompletionGlyph.BrushFor(null));
        Assert.NotEqual(string.Empty, CompletionGlyph.LetterFor(null));
    }

    /// <summary>The candidate exposes the badge, so the item template needs no converter.</summary>
    [Fact]
    public void ACandidateCarriesItsOwnBadge()
    {
        CodeCompletionCandidate candidate = new("DistanceTo", "Method");

        Assert.Equal("M", candidate.Glyph);
        Assert.Equal(CompletionGlyph.BrushFor("Method"), candidate.GlyphBrush);
    }

    /// <summary>
    /// <b>The editor is configured as a C# editor, not as a text box.</b> AvaloniaEdit's defaults
    /// are real tabs, no indentation strategy and no current-line highlight — defensible for plain
    /// text and wrong here, and the difference shows the first time somebody presses Enter inside a
    /// block and the caret lands in column one.
    /// </summary>
    [Fact]
    public void TheEditorIsSetUpTheWayRcsSetsItUp()
    {
        HeadlessSession.Run(() =>
        {
            CodeBlockEditor block = new();
            Window window = new() { Width = 600, Height = 400, Content = block };

            window.Show();
            window.Measure(new Avalonia.Size(window.Width, window.Height));
            window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
            window.UpdateLayout();

            TextEditor editor = block.GetVisualDescendants().OfType<TextEditor>().First();

            Assert.True(editor.Options.ConvertTabsToSpaces, "a tab in a code block should be spaces");
            Assert.Equal(4, editor.Options.IndentationSize);
            Assert.True(editor.Options.HighlightCurrentLine, "the current line should be marked");

            // A code block is not a document with links in it, and a Ctrl+click that navigates is a
            // Ctrl+click that did not add a caret.
            Assert.False(editor.Options.EnableHyperlinks);
            Assert.False(editor.Options.EnableEmailHyperlinks);

            Assert.IsType<CSharpIndentationStrategy>(editor.TextArea.IndentationStrategy);

            window.Close();
        });
    }

    /// <summary>
    /// <b>The badge reaches the screen</b>, not just the candidate. The item template binds
    /// <c>GlyphBrush</c> and <c>Glyph</c> onto a <c>Border</c> and the <c>TextBlock</c> inside it,
    /// and a binding that silently fails leaves a transparent square no assertion on the model
    /// would notice.
    /// </summary>
    /// <remarks>
    /// Built from the template rather than read off a realised row. The completion list lives on a
    /// <c>Canvas</c> and virtualises, so headlessly it has no arranged size and
    /// <c>ContainerFromIndex</c> answers null for every index — which says nothing about the
    /// template. Building it directly asks the question the test is actually about.
    /// </remarks>
    [Theory]
    [InlineData("Method", "M")]
    [InlineData("Property", "P")]
    [InlineData("Class", "C")]
    public void TheTemplateDrawsTheBadgeForTheKind(string kind, string letter)
    {
        HeadlessSession.Run(() =>
        {
            CodeBlockEditor block = new();
            Window window = new() { Width = 600, Height = 400, Content = block };

            window.Show();
            Layout(window);

            ListBox list = block.GetVisualDescendants().OfType<ListBox>().First();
            IDataTemplate template = Assert.IsAssignableFrom<IDataTemplate>(list.ItemTemplate);

            CodeCompletionCandidate candidate = new("Whatever", kind);

            Control row = Assert.IsAssignableFrom<Control>(template.Build(candidate));
            row.DataContext = candidate;

            // A binding is applied on the first measure, not on construction.
            row.Measure(new Avalonia.Size(400, 40));
            row.Arrange(new Avalonia.Rect(0, 0, 400, 40));

            Border badge = row.GetSelfAndVisualDescendants()
                .OfType<Border>()
                .First(border => border.Width == 16 && border.Height == 16);

            Assert.Equal(CompletionGlyph.BrushFor(kind), badge.Background);

            TextBlock glyph = badge.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.Equal(letter, glyph.Text);

            window.Close();
        });
    }

    private static void Layout(Window window)
    {
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();
    }
}
