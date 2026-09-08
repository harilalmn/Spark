using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Spark.UI.Theming;
using Spark.UI.Views.Controls;

namespace Spark.UI.Tests;

/// <summary>
/// Changing the code font actually changes what the editor draws in — `E8-T62`.
/// </summary>
/// <remarks>
/// <b>Reported by the client</b>: the Font dropdown said <c>Consolas</c> and the text did not
/// change. `E8-T59` tested the <i>setting</i> — that the list is filtered, that the choice is
/// remembered, that a missing font falls back — and never tested the one thing the setting exists
/// to do.
/// </remarks>
public sealed class CodeFontAppliesTests : IDisposable
{
    /// <summary>The setting is static, so every test puts it back.</summary>
    public void Dispose() => CodeFont.UseDefault();

    /// <summary>
    /// <b>An attached editor follows the setting.</b> This is the claim the client made and the
    /// one nothing asserted.
    /// </summary>
    [Fact]
    public void AnOpenEditorFollowsTheFont() => HeadlessSession.Run(() =>
    {
        CodeFont.UseDefault();

        (Window window, CodeBlockEditor editor) = Open();
        TextEditor inner = editor.GetVisualDescendants().OfType<TextEditor>().Single();

        Assert.Equal(CodeFont.DefaultFamily, inner.FontFamily.ToString());

        CodeFont.Use(Other);

        Assert.Equal(Other, inner.FontFamily.ToString());

        window.Close();
    });

    /// <summary>And an editor opened after the change starts on the chosen face.</summary>
    [Fact]
    public void AnEditorOpenedAfterwardsUsesIt() => HeadlessSession.Run(() =>
    {
        CodeFont.Use(Other);

        (Window window, CodeBlockEditor editor) = Open();
        TextEditor inner = editor.GetVisualDescendants().OfType<TextEditor>().Single();

        Assert.Equal(Other, inner.FontFamily.ToString());

        window.Close();
    });

    /// <summary>Going back to the shipped face takes as well.</summary>
    [Fact]
    public void GoingBackToTheDefaultTakes() => HeadlessSession.Run(() =>
    {
        (Window window, CodeBlockEditor editor) = Open();
        TextEditor inner = editor.GetVisualDescendants().OfType<TextEditor>().Single();

        CodeFont.Use(Other);
        CodeFont.UseDefault();

        Assert.Equal(CodeFont.DefaultFamily, inner.FontFamily.ToString());

        window.Close();
    });

    /// <summary>A face this machine really has, other than the shipped one.</summary>
    /// <remarks>
    /// Chosen from the list rather than named, because headless Avalonia reports almost no system
    /// fonts - the first draft asked for Consolas and failed on the availability filter rather than
    /// on the thing being tested, which is a test measuring its own environment.
    /// </remarks>
    private static string Other =>
        CodeFont.Available().First(name => name != CodeFont.Name);

    private static (Window Window, CodeBlockEditor Editor) Open()
    {
        CodeBlockEditor editor = new();
        Window window = new() { Width = 600, Height = 400, Content = editor };

        window.Show();
        window.Measure(new Size(600, 400));
        window.Arrange(new Rect(0, 0, 600, 400));
        window.UpdateLayout();

        editor.Text = "var a = 1;";

        return (window, editor);
    }
}
