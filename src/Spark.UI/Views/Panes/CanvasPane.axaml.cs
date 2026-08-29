using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Spark.UI.Controls;
using Spark.UI.ViewModels;

namespace Spark.UI.Views.Panes;

/// <summary>
/// The node canvas and the overlay layer above it, which today holds the creation box.
/// </summary>
/// <remarks>
/// The creation gesture lives here rather than on the window because every control it touches is
/// in this pane: the box is positioned against <i>this</i> canvas's bounds, and it hands focus
/// back to <i>this</i> canvas when it closes. The window keeps the gestures that are about the
/// document rather than the surface.
/// </remarks>
public sealed partial class CanvasPane : UserControl
{
    private double _createWorldX;
    private double _createWorldY;

    /// <summary>Creates the pane.</summary>
    public CanvasPane()
    {
        InitializeComponent();

        CanvasControl.CreateRequested += OnCanvasCreateRequested;
    }

    /// <summary>
    /// The canvas itself. Exposed because the window binds the graph onto it, frames it, and
    /// drives it through the benchmark.
    /// </summary>
    public GraphCanvas Canvas => CanvasControl;

    private MainWindowViewModel? Model => DataContext as MainWindowViewModel;

    /// <summary>
    /// Opens the creation box over the point that was double-clicked.
    /// </summary>
    /// <remarks>
    /// The world point is kept rather than recomputed on commit, because the canvas can be panned
    /// or zoomed by the scroll wheel while the box is open, and a node that lands where the
    /// pointer *now* is rather than where the user asked for it is the kind of small betrayal that
    /// makes a gesture feel unreliable.
    /// </remarks>
    /// <param name="sender">The canvas.</param>
    /// <param name="e">Where the node should go.</param>
    private void OnCanvasCreateRequested(object? sender, CanvasCreateRequestedEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        _createWorldX = e.WorldX;
        _createWorldY = e.WorldY;

        model.CreateSearch = string.Empty;

        // Kept inside the canvas, so a double-click near the right or bottom edge does not open a
        // box that is half off screen and half unreachable.
        double left = Math.Clamp(e.ScreenX, 0, Math.Max(0, CanvasControl.Bounds.Width - CreateBox.Width));
        double top = Math.Clamp(e.ScreenY, 0, Math.Max(0, CanvasControl.Bounds.Height - 160));

        Avalonia.Controls.Canvas.SetLeft(CreateBox, left);
        Avalonia.Controls.Canvas.SetTop(CreateBox, top);
        CreateBox.IsVisible = true;
        CreateSearchBox.Focus();
    }

    private void CloseCreateBox()
    {
        if (!CreateBox.IsVisible)
        {
            return;
        }

        CreateBox.IsVisible = false;
        CanvasControl.Focus();
    }

    /// <summary>
    /// Commits the highlighted result, if there is one, and closes the box.
    /// </summary>
    /// <remarks>
    /// The new node is selected and the canvas takes focus, so the gesture ends where the next one
    /// begins: a node on the canvas, ready to be wired or dragged, rather than a text box still
    /// holding the keyboard.
    /// </remarks>
    private void CommitCreateBox()
    {
        if (Model is not { } model || model.SelectedCreateResult is not { } entry)
        {
            return;
        }

        int slot = model.PlaceEntryAt(entry, _createWorldX, _createWorldY);
        CloseCreateBox();

        CanvasControl.RefreshStructure();
        CanvasControl.SelectOnly(slot);
        CanvasControl.Focus();
        _ = model.EvaluateAsync();
    }

    private void OnCreateSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            // Key.Return is the same value as Key.Enter in Avalonia, so naming both here is a
            // duplicate label rather than thoroughness.
            case Key.Enter:
                CommitCreateBox();
                e.Handled = true;
                break;

            case Key.Escape:
                CloseCreateBox();
                e.Handled = true;
                break;

            // The arrows move the highlight without leaving the text box, which is what lets a
            // user keep typing after looking down the list.
            case Key.Down:
                MoveCreateSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveCreateSelection(-1);
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void MoveCreateSelection(int delta)
    {
        if (Model is not { } model || model.CreateResults.Count == 0)
        {
            return;
        }

        int current = model.SelectedCreateResult is { } selected
            ? model.CreateResults.IndexOf(selected)
            : -1;

        int next = Math.Clamp(current + delta, 0, model.CreateResults.Count - 1);
        model.SelectedCreateResult = model.CreateResults[next];
    }

    private void OnCreateSearchLostFocus(object? sender, RoutedEventArgs e)
    {
        // Clicking a result moves focus to the list, which must not read as dismissing the box.
        if (CreateResultsList.IsKeyboardFocusWithin)
        {
            return;
        }

        CloseCreateBox();
    }

    private void OnCreateResultChosen(object? sender, TappedEventArgs e) => CommitCreateBox();
}
