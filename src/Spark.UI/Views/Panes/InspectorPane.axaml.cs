using System;
using System.Collections.Generic;
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
    private readonly Views.Controls.CodeBlockEditor? _script;
    private MainWindowViewModel? _model;

    /// <summary>Creates the pane.</summary>
    public InspectorPane()
    {
        InitializeComponent();

        _script = this.FindControl<Views.Controls.CodeBlockEditor>("ScriptEditor");

        if (_script is not null)
        {
            _script.Committed += OnScriptCommitted;
            _script.CompletionSource = (code, caret, token) =>
                DataContext is MainWindowViewModel model
                    ? model.CompleteScriptAsync(code, caret, token)
                    : System.Threading.Tasks.Task.FromResult<IReadOnlyList<Views.Controls.CodeCompletionCandidate>>([]);

            _script.SignatureSource = (code, caret, token) =>
                DataContext is MainWindowViewModel model
                    ? model.SignatureScriptAsync(code, caret, token)
                    : System.Threading.Tasks.Task.FromResult<Views.Controls.CodeSignatureInfo?>(null);
        }

        // The view model raises `ScriptText` when the selection changes, and that - not a
        // DataContext change - is the moment the editor has to be refilled: the pane's context is
        // the shell's one view model for the whole session.
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
            {
                _model.PropertyChanged -= OnModelChanged;
            }

            _model = DataContext as MainWindowViewModel;

            if (_model is not null)
            {
                _model.PropertyChanged += OnModelChanged;
            }

            ShowScript();
        };
    }

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.ScriptText))
        {
            ShowScript();
        }
    }

    /// <summary>
    /// Puts the selected block's source into the editor when the selection changes.
    /// </summary>
    /// <remarks>
    /// <b>Pushed rather than bound.</b> A two-way binding onto a document the user is typing into
    /// re-enters on every keystroke and has to be defended against; the pane already knows the two
    /// moments that matter — the selection changed, and the edit was committed.
    /// </remarks>
    public void ShowScript()
    {
        if (_script is not null && DataContext is MainWindowViewModel model)
        {
            _script.Text = model.ScriptText;
        }
    }

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

    private void OnScriptCommitted(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel model || _script is null)
        {
            return;
        }

        // The editor owns the text while it is being typed, so the view model is told what it now
        // says before being asked to commit it.
        model.ScriptText = _script.Text;

        if (model.CommitScriptText())
        {
            ScriptEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRunOnce(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel model)
        {
            model.TrustAndRun(remember: false);
        }
    }

    private void OnAlwaysTrust(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel model)
        {
            model.TrustAndRun(remember: true);
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
