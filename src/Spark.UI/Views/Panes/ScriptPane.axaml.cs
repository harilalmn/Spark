using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Spark.UI.ViewModels;

namespace Spark.UI.Views.Panes;

/// <summary>
/// The selected code block's source, docked in a pane of its own (<c>E6-T14</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The second of <c>E6</c>'s two node types, and the second presentation of one pipeline.</b>
/// The inline Code Block edits its source in the Properties pane; this edits the same source, on
/// the same view model, through the same <see cref="Views.Controls.CodeBlockEditor"/>. There is no
/// second evaluation path, no second Roslyn session and nothing added to <c>Spark.Scripting</c> —
/// which is what the row means by the docked variant being a presentation rather than machinery.
/// </para>
/// <para>
/// <b>Opening this pane must not load Roslyn</b>, and it does not: every service it offers the
/// editor is a call onto <see cref="MainWindowViewModel"/>, each of which returns empty unless a
/// code block is selected. <c>SparkSession.EnableScripting</c> is still first reached through
/// <c>PlaceCodeBlock</c>, so the epic's standing invariant — a graph with no script nodes never
/// loads <c>Spark.Scripting</c> — survives a pane that is open over an empty document.
/// </para>
/// <para>
/// <b>Text is pushed, not bound</b>, for the reason <see cref="InspectorPane"/> records: a two-way
/// binding onto a document somebody is typing into re-enters on every keystroke and has to be
/// defended against, and the two moments that matter are already known — the selection changed,
/// and the edit was committed.
/// </para>
/// <para>
/// <b>Two editors over one document, and the reason that is safe is that neither holds it.</b>
/// Both panes push from <see cref="MainWindowViewModel.ScriptText"/> when it changes and write back
/// to it on commit, so the view model is the single copy and each editor is a view of it. Both can
/// be open at once; committing in either refills the other on the same notification.
/// </para>
/// </remarks>
public sealed partial class ScriptPane : UserControl
{
    private readonly Views.Controls.CodeBlockEditor? _script;
    private MainWindowViewModel? _model;

    /// <summary>Creates the pane.</summary>
    public ScriptPane()
    {
        InitializeComponent();

        _script = this.FindControl<Views.Controls.CodeBlockEditor>("ScriptEditor");

        if (_script is not null)
        {
            _script.Committed += OnScriptCommitted;

            _script.CompletionSource = (code, caret, token) =>
                DataContext is MainWindowViewModel model
                    ? model.CompleteScriptAsync(code, caret, token)
                    : Task.FromResult<IReadOnlyList<Views.Controls.CodeCompletionCandidate>>([]);

            _script.SignatureSource = (code, caret, token) =>
                DataContext is MainWindowViewModel model
                    ? model.SignatureScriptAsync(code, caret, token)
                    : Task.FromResult<Views.Controls.CodeSignatureInfo?>(null);

            _script.DiagnosticsSource = (code, token) =>
                DataContext is MainWindowViewModel model
                    ? model.DiagnoseScriptAsync(code, token)
                    : Task.FromResult<IReadOnlyList<Views.Controls.CodeDiagnostic>>([]);

            _script.QuickInfoSource = (code, offset, token) =>
                DataContext is MainWindowViewModel model
                    ? model.DescribeScriptAsync(code, offset, token)
                    : Task.FromResult<Views.Controls.CodeQuickInfo?>(null);
        }

        // The pane's context is the shell's one view model for the whole session, so a DataContext
        // change is not what says the selection moved - `ScriptText` changing is.
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

    /// <summary>Raised when a code block's source has been changed and committed.</summary>
    /// <remarks>
    /// An event rather than a call, exactly as <see cref="InspectorPane.ScriptEdited"/> is: the
    /// canvas has to redraw and the shell has to record an undo step, and this pane should know
    /// about neither. The shell forwards both events to one handler, so an edit committed here and
    /// an edit committed in Properties are indistinguishable downstream — which is the point.
    /// </remarks>
    public event EventHandler? ScriptEdited;

    /// <summary>Puts the selected block's source into the editor.</summary>
    public void ShowScript()
    {
        if (_script is not null && DataContext is MainWindowViewModel model)
        {
            _script.Text = model.ScriptText;
        }
    }

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.ScriptText))
        {
            ShowScript();
        }
    }

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
}
