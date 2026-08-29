using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Spark.UI.Views.Panes;

/// <summary>
/// The node library: a search box, the matching entries, and the gesture that places one.
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

    private void OnLibraryDoubleTapped(object? sender, TappedEventArgs e) =>
        PlaceRequested?.Invoke(this, EventArgs.Empty);
}
