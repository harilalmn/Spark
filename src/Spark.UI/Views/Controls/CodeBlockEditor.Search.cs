using System;
using Avalonia.Input;
using AvaloniaEdit.Search;

namespace Spark.UI.Views.Controls;

/// <summary>
/// Find and replace, and the text size.
/// </summary>
/// <remarks>
/// <para>
/// <b>RCS writes its own find bar; this uses AvaloniaEdit's, and the difference is the point.</b>
/// RCS's <c>FindReplacePanel</c> is 593 lines of WPF: a <c>Border</c> subclass, a hand-built
/// toolbar, and an <c>AdornerHost</c> to float it over the text — because AvalonEdit's own
/// <c>SearchPanel</c> finds but cannot replace. AvaloniaEdit's can (<c>IsReplaceMode</c>), so
/// porting the panel would have been reimplementing a control that ships in the box, in a
/// framework with no adorner layer to host it in. **The gestures are RCS's; the panel is not.**
/// </para>
/// <para>
/// <b>The zoom is RCS's <c>EditorZoom</c>, reduced to one editor.</b> There it is static because
/// several tabs share one preference — Ctrl+wheel over any of them resizes all. A Spark code block
/// is one editor inside a properties panel and there is never a second to keep in step, so the
/// state lives on the control and the event RCS needs is not needed here.
/// </para>
/// </remarks>
public sealed partial class CodeBlockEditor
{
    /// <summary>The smallest the text goes. Below this the syntax colours stop being legible.</summary>
    public const double MinimumFontSize = 7;

    /// <summary>The largest the text goes.</summary>
    public const double MaximumFontSize = 42;

    /// <summary>The size the editor starts at, matching the XAML.</summary>
    public const double DefaultFontSize = 12;

    private SearchPanel? _search;

    /// <summary>Whether the find bar is showing.</summary>
    public bool IsSearchOpen => _search?.IsOpened == true;

    /// <summary>
    /// Opens the find bar, in replace mode or not.
    /// </summary>
    /// <param name="replacing">True for Ctrl+H, false for Ctrl+F.</param>
    /// <remarks>
    /// <b>The selection seeds the search box</b>, which is the whole of the "select a word, press
    /// Ctrl+F" gesture. Without it the bar opens empty and the word has to be typed again, which is
    /// the difference between a shortcut and a dialog.
    /// </remarks>
    public void OpenSearch(bool replacing)
    {
        if (_editor is null)
        {
            return;
        }

        _search ??= SearchPanel.Install(_editor);
        _search.IsReplaceMode = replacing;

        if (_editor.SelectionLength > 0 && !_editor.SelectedText.Contains('\n', StringComparison.Ordinal))
        {
            _search.SearchPattern = _editor.SelectedText;
        }

        _search.Open();
    }

    /// <summary>Closes the find bar and puts the caret back in the document.</summary>
    public void CloseSearch()
    {
        _search?.Close();
        _editor?.TextArea.Focus();
    }

    /// <summary>
    /// Steps the text size by whole points.
    /// </summary>
    /// <param name="notches">Wheel notches: positive is bigger.</param>
    /// <remarks>
    /// <b>A step rather than a factor</b>, RCS's choice and worth keeping: a multiplier moves one
    /// point at the small end and four at the large one, so the same gesture feels different
    /// depending on where you started.
    /// </remarks>
    public void Zoom(int notches)
    {
        if (_editor is null)
        {
            return;
        }

        _editor.FontSize = Math.Clamp(
            Math.Round(_editor.FontSize + notches, 1),
            MinimumFontSize,
            MaximumFontSize);
    }

    /// <summary>Puts the text size back where it started.</summary>
    public void ResetZoom()
    {
        if (_editor is not null)
        {
            _editor.FontSize = DefaultFontSize;
        }
    }

    private void OnEditorPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        // Ctrl+wheel resizes the text, as it does in every other editor. Handled here rather than
        // left to AvaloniaEdit, which scrolls on a Ctrl+wheel like any other wheel.
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        Zoom(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }
}
