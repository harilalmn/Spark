using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Spark.UI.ViewModels;

namespace Spark.UI.Views.Panes;

/// <summary>
/// The node library: a search box, the matching entries grouped by category, and the gesture that
/// places one.
/// </summary>
/// <remarks>
/// The pane reports that a node was asked for and stops there. Where the node lands depends on
/// what the canvas is already showing, which is knowledge this control does not have and should
/// not acquire — so <see cref="PlaceRequested"/> is an event rather than a call.
/// </remarks>
public sealed partial class LibraryPane : UserControl
{
    /// <summary>Creates the pane.</summary>
    public LibraryPane() => InitializeComponent();

    /// <summary>Raised when the user asks for the selected entry to be placed on the canvas.</summary>
    public event EventHandler? PlaceRequested;

    private void OnPlaceNode(object? sender, RoutedEventArgs e) =>
        PlaceRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Double-clicking places the selected entry — but only when the selection is an entry.
    /// </summary>
    /// <remarks>
    /// A double-click on a <i>category</i> is how a tree is normally expanded, and it must not
    /// also place whatever node happened to be selected before. The guard is
    /// <see cref="MainWindowViewModel.SelectedLibraryEntry"/> being null, which
    /// <see cref="OnLibrarySelectionChanged"/> ensures for a group.
    /// </remarks>
    private void OnLibraryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { SelectedLibraryEntry: not null })
        {
            PlaceRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Places one overload chosen from an overloaded member's flyout (<c>E5-T4</c>).
    /// </summary>
    /// <remarks>
    /// <b>It selects and then places, rather than placing directly</b>, because
    /// <see cref="MainWindowViewModel.SelectedLibraryEntry"/> is what every route to the canvas
    /// reads and what F1 looks help up against. Placing behind its back would leave the panel
    /// showing one thing and the application believing another.
    /// <para>
    /// The popup is closed first. One left open over the canvas hides the node that has just
    /// appeared, which reads as nothing having happened. Closing it also unchecks the row, because
    /// the row's checked state is what <c>IsOpen</c> is bound to.
    /// </para>
    /// </remarks>
    private void OnPlaceOverload(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel model
            || sender is not Button { DataContext: LibraryEntryViewModel entry } button)
        {
            return;
        }

        button.FindLogicalAncestorOfType<Popup>()?.Close();

        model.SelectedLibraryEntry = entry;
        PlaceRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Carries a tree selection back to the view model, and only when it is a node.
    /// </summary>
    /// <remarks>
    /// <b>A <c>TreeView</c>'s <c>SelectedItem</c> is an <c>object</c> that may be either level of
    /// the tree</b>, so it cannot be bound straight to a property typed as an entry. Selecting a
    /// category clears the selection rather than leaving the previous entry standing: the Place
    /// button and the F1 help lookup both read that property, and a button that placed a node the
    /// user was no longer pointing at would be worse than one that does nothing.
    /// </remarks>
    private void OnLibrarySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel model)
        {
            return;
        }

        model.SelectedLibraryEntry = (sender as TreeView)?.SelectedItem as LibraryEntryViewModel;
    }
}
