using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Spark.UI.ViewModels;

namespace Spark.UI.Views.Panes;

/// <summary>
/// The properties inspector and the diagnostics it sits above: the literals of the selected node,
/// and what the last run had to say.
/// </summary>
/// <remarks>
/// Both handlers here commit the row they are on and nothing else. The commit itself belongs to
/// <see cref="PortLiteralViewModel"/>, which knows how to parse the text and what to do when it
/// does not parse; this pane only decides <i>when</i> — losing focus, or pressing Enter.
/// </remarks>
public sealed partial class InspectorPane : UserControl
{
    /// <summary>Creates the pane.</summary>
    public InspectorPane() => InitializeComponent();

    /// <summary>Raised when the selected note's text has been changed and committed.</summary>
    /// <remarks>
    /// An event rather than a call, for the reason <c>LibraryPane.PlaceRequested</c> is one: the
    /// canvas has to redraw and the shell has to record an undo step, and this pane should know
    /// about neither.
    /// </remarks>
    public event EventHandler? NoteEdited;

    /// <summary>Raised when the selected group's title has been changed and committed.</summary>
    public event EventHandler? GroupRenamed;

    /// <summary>Raised when a code block's source has been changed and committed.</summary>
    public event EventHandler? ScriptEdited;

    private void OnScriptCommitted(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel model && model.CommitScriptText())
        {
            ScriptEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnGroupTitleCommitted(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel model && model.CommitGroupTitle())
        {
            GroupRenamed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnNoteCommitted(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel model && model.CommitNoteText())
        {
            NoteEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnLiteralCommitted(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PortLiteralViewModel literal })
        {
            literal.Commit();
        }
    }

    private void OnLiteralKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter and not Key.Return)
        {
            return;
        }

        if (sender is Control { DataContext: PortLiteralViewModel literal })
        {
            literal.Commit();
            e.Handled = true;
        }
    }
}
