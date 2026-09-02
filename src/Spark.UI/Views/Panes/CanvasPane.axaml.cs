using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Spark.UI.Controls;
using Spark.UI.ViewModels;

namespace Spark.UI.Views.Panes;

/// <summary>
/// The node canvas and the overlay layer above it: the creation box, the in-place value field,
/// and the code block's own editor.
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
    private int _editingSlot = -1;
    private int _editingScript = -1;

    /// <summary>What the open editor asked for, in screen pixels, so a moved view can ask again.</summary>
    private Size _editorWanted;

    /// <summary>
    /// Roughly how tall one line is in the editor, so the editor opens tall enough to show the
    /// source it replaced rather than scrolling it.
    /// </summary>
    /// <remarks>
    /// <b>Not <see cref="Spark.UI.Graph.CanvasNode.ScriptLineHeight"/>, and the difference is the point.</b>
    /// The drawing is scaled by the zoom and the editor is not - it is drawn at a legible size
    /// whatever the canvas is at, for the same reason the value field is floored at 22 px. So a
    /// block zoomed out to 50% needs an editor twice the height of the box it came out of, and
    /// the block grows to hold it (<c>E8-T40</c>) rather than the editor spilling over the tabs.
    /// </remarks>
    private const double EditorLineHeight = 17;

    /// <summary>Roughly one character's width at the editor's 12 px monospaced face.</summary>
    private const double EditorCharWidth = 7.3;

    /// <summary>The line-number gutter and the editor's own insets, in screen pixels.</summary>
    private const double EditorChrome = 52;

    /// <summary>The narrowest an editor is opened, so a one-word block still gets a usable one.</summary>
    private const double EditorMinimumWidth = 260;

    /// <summary>
    /// The widest a block is grown to hold its own longest line.
    /// </summary>
    /// <remarks>
    /// A block with one 300-character line would otherwise become a node wider than the canvas.
    /// Past this the editor scrolls sideways, which is what every editor does and what the person
    /// who wrote that line already expects.
    /// </remarks>
    private const double EditorMaximumWidth = 760;

    /// <summary>The editor's own vertical insets, so the last line is not against the frame.</summary>
    private const double EditorChromeHeight = 14;

    /// <summary>Creates the pane.</summary>
    public CanvasPane()
    {
        InitializeComponent();

        CanvasControl.CreateRequested += OnCanvasCreateRequested;
        CanvasControl.CodeBlockRequested += OnCanvasCodeBlockRequested;
        CanvasControl.FieldEditRequested += OnCanvasFieldEditRequested;
        CanvasControl.ScriptEditRequested += OnCanvasScriptEditRequested;
        CanvasControl.ViewChanged += OnCanvasViewChanged;

        // `E8-T39`. The same four sources the properties pane gives its editor, because it is
        // the same editor over the same block - a list that answered differently depending on
        // where you were typing would be worse than no list at all (`E6-T13`).
        ScriptEditor.Committed += OnScriptCommitted;

        ScriptEditor.CompletionSource = (code, caret, token) =>
            Model is { } model
                ? model.CompleteScriptAsync(code, caret, token)
                : Task.FromResult<IReadOnlyList<Controls.CodeCompletionCandidate>>([]);

        ScriptEditor.SignatureSource = (code, caret, token) =>
            Model is { } model
                ? model.SignatureScriptAsync(code, caret, token)
                : Task.FromResult<Controls.CodeSignatureInfo?>(null);

        ScriptEditor.DiagnosticsSource = (code, token) =>
            Model is { } model
                ? model.DiagnoseScriptAsync(code, token)
                : Task.FromResult<IReadOnlyList<Controls.CodeDiagnostic>>([]);

        ScriptEditor.QuickInfoSource = (code, offset, token) =>
            Model is { } model
                ? model.DescribeScriptAsync(code, offset, token)
                : Task.FromResult<Controls.CodeQuickInfo?>(null);
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

    /// <summary>
    /// Drops a code block where empty canvas was double-clicked (<c>E8-T27</c>).
    /// </summary>
    /// <remarks>
    /// <b>At the point that was double-clicked, not at the next free slot.</b> The toolbar's Code
    /// block button asks the canvas to suggest a spot because the user pointed at nothing; this
    /// gesture is the user pointing, and putting the block anywhere else would be answering a
    /// different question.
    /// </remarks>
    /// <param name="sender">The canvas.</param>
    /// <param name="e">Where the block should go.</param>
    private void OnCanvasCodeBlockRequested(object? sender, CanvasCreateRequestedEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        int slot = model.PlaceCodeBlock(e.WorldX, e.WorldY);
        if (slot < 0)
        {
            return;
        }

        CanvasControl.RefreshStructure();
        CanvasControl.SelectOnly(slot);
        CanvasControl.Focus();
        model.RequestRun();
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
        model.RequestRun();
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
    /// <summary>
    /// Puts a real text box over the value field the canvas drew (<c>E8-T5</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the hybrid overlay <c>GraphCanvas</c>'s own remarks describe.</b> Every node is
    /// drawn immediate-mode, which is what makes a large graph cheap; the one node being
    /// interacted with gets a real Avalonia control positioned over the drawing, which is what
    /// preserves input fidelity. One at a time, by construction — there is one editor.
    /// </para>
    /// <para>
    /// <b>Sized to the field but floored at a usable height.</b> Zoomed a long way out the field is
    /// a few pixels tall, and a text box that small is one nobody can type into; the editor stays
    /// legible and simply covers more of the node than the field did.
    /// </para>
    /// </remarks>
    /// <param name="sender">The canvas.</param>
    /// <param name="e">Which node, what it holds, and where the field is on screen.</param>
    private void OnCanvasFieldEditRequested(object? sender, CanvasFieldEditEventArgs e)
    {
        _editingSlot = e.Slot;

        FieldEditor.Width = Math.Max(e.ScreenWidth, 60);
        FieldEditor.Height = Math.Max(e.ScreenHeight, 22);

        Avalonia.Controls.Canvas.SetLeft(FieldEditor, e.ScreenX);
        Avalonia.Controls.Canvas.SetTop(FieldEditor, e.ScreenY);

        FieldEditor.Text = e.Text;
        FieldEditor.IsVisible = true;
        FieldEditor.Focus();
        FieldEditor.SelectAll();
    }

    /// <summary>
    /// Enter commits, Escape abandons.
    /// </summary>
    /// <remarks>
    /// <b>Escape has to close the editor without committing</b>, and closing it moves focus, which
    /// raises <c>LostFocus</c> — which commits. So the slot is cleared <i>before</i> the editor is
    /// hidden, and the commit path checks it. Without that, Escape would save the thing it was
    /// asked to discard.
    /// </remarks>
    /// <param name="sender">The editor.</param>
    /// <param name="e">The key.</param>
    private void OnFieldEditorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitFieldEditor();
                e.Handled = true;
                break;

            case Key.Escape:
                _editingSlot = -1;
                CloseFieldEditor();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void OnFieldEditorLostFocus(object? sender, RoutedEventArgs e) => CommitFieldEditor();

    private void CommitFieldEditor()
    {
        if (_editingSlot < 0)
        {
            return;
        }

        int slot = _editingSlot;
        _editingSlot = -1;

        CanvasControl.CommitFieldText(slot, FieldEditor.Text);
        CloseFieldEditor();
    }

    private void CloseFieldEditor()
    {
        if (!FieldEditor.IsVisible)
        {
            return;
        }

        FieldEditor.IsVisible = false;
        CanvasControl.Focus();
    }

    /// <summary>
    /// Puts the real code editor over the source the canvas drew (<c>E8-T39</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the hybrid overlay carrying the control it was really for.</b> `E8-T5` proved the
    /// mechanism on a value field; a code block is the case that justifies it — a caret, a
    /// selection, an undo stack, a completion list and a clipboard are not things to re-implement
    /// in a draw loop, and every other block on the canvas stays a picture.
    /// </para>
    /// <para>
    /// <b>Floored at a usable size, like the field is.</b> A one-line block's source rectangle is a
    /// few hundred pixels by twenty-five, and zoomed out it is smaller still; an editor that size
    /// is one nobody can work in. It covers more of the node than the drawing did, which is the
    /// same trade the value field makes and for the same reason.
    /// </para>
    /// </remarks>
    /// <param name="sender">The canvas.</param>
    /// <param name="e">Which node, its source, and where that source is on screen.</param>
    private void OnCanvasScriptEditRequested(object? sender, CanvasFieldEditEventArgs e)
    {
        if (DataContext is not MainWindowViewModel model || e.Slot >= CanvasControl.Graph.Nodes.Count)
        {
            return;
        }

        // The view model is the one that knows how to compile, complete and commit a block, and it
        // works on the *selected* block - so the block being typed into becomes the selected one.
        model.ShowCodeBlock(CanvasControl.Graph.Nodes[e.Slot]);

        _editingScript = e.Slot;

        // `E8-T40`: the block is grown to hold the editor rather than the editor being allowed to
        // spill over the port tabs either side of it. The pane says what the editor needs, in
        // screen pixels, because the editor's metrics are the pane's; the canvas decides where
        // that goes, because the node's geometry is the canvas's.
        Measure(e.Text, out int lines, out int columns);

        _editorWanted = new Size(
            Math.Clamp((columns * EditorCharWidth) + EditorChrome, EditorMinimumWidth, EditorMaximumWidth),
            (lines * EditorLineHeight) + EditorChromeHeight);

        if (!Place(e.Slot))
        {
            return;
        }

        ScriptEditor.Text = e.Text;
        ScriptEditor.IsVisible = true;
        ScriptEditor.FocusEditor();
    }

    /// <summary>
    /// Puts the editor over a block's source at the current pan and zoom (<c>E8-T43</c>).
    /// </summary>
    /// <param name="slot">The node's slot.</param>
    /// <returns>False when the slot is no longer a code block, in which case nothing moved.</returns>
    /// <remarks>
    /// <b>The size asked for is in screen pixels and does not change with the zoom</b>, which is
    /// the whole of what makes this work: the reservation the canvas makes is that size divided by
    /// the zoom, so re-asking on every view change keeps the editor a constant, legible size while
    /// the block grows and shrinks around it. It is <c>E8-T40</c>'s rule applied at every zoom
    /// rather than only at the one the editor happened to open at.
    /// </remarks>
    private bool Place(int slot)
    {
        if (!CanvasControl.ScriptEditorSpace(
            slot,
            _editorWanted.Width,
            _editorWanted.Height,
            out double x,
            out double y,
            out double width,
            out double height))
        {
            return false;
        }

        // Placed in the rectangle the canvas answered with and not one pixel outside it. Every
        // floor that used to be applied here was a port tab covered up.
        ScriptEditor.Width = width;
        ScriptEditor.Height = height;

        Avalonia.Controls.Canvas.SetLeft(ScriptEditor, x);
        Avalonia.Controls.Canvas.SetTop(ScriptEditor, y);

        // `E8-T41`: the completion list and the signature are drawn on an overlay inside the
        // editor, so on a block two lines tall they came out as a sliver inside the block. They
        // are allowed the whole pane instead - given relative to the editor, which is why the
        // origin is negative, and why it is recomputed on every move.
        ScriptEditor.PopupArea = new Rect(
            -x, -y, OverlayLayer.Bounds.Width, OverlayLayer.Bounds.Height);

        return true;
    }

    /// <summary>Keeps the open editor over its block when the view moves (<c>E8-T43</c>).</summary>
    /// <remarks>
    /// <para>
    /// <b>This replaces closing the editor, which is what the wheel used to do.</b> A control
    /// positioned in screen coordinates over a surface that pans and zooms is correct until the
    /// surface moves, and there are only two honest answers: move with it, or get out of the way.
    /// The second was the cheaper one and it reads as the application snapping shut on a user who
    /// was typing.
    /// </para>
    /// <para>
    /// <b>A block whose slot has gone takes the editor with it</b> rather than leaving it floating
    /// over a node that is not there — which is what a deletion from elsewhere would otherwise
    /// produce.
    /// </para>
    /// </remarks>
    private void OnCanvasViewChanged(object? sender, EventArgs e)
    {
        if (_editingScript < 0)
        {
            return;
        }

        if (!Place(_editingScript))
        {
            _editingScript = -1;
            ScriptEditor.IsVisible = false;
        }
    }

    /// <summary>
    /// Escape closes the editor, and closing it is what commits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Escape commits here and abandons in the value field, and that asymmetry is deliberate.</b>
    /// A field holds a number somebody can retype; an editor holds a screenful of code, and
    /// discarding it on a keystroke that every editor in the world uses to close a popup would be
    /// the most expensive keypress in the application. Undo takes the edit back
    /// (<c>E8-T25</c>) — nothing else can bring the typing back.
    /// </para>
    /// <para>
    /// <b>Enter is not a commit</b>, for the reason it is one in the field: in an editor it is a
    /// newline, and there is nothing else it can be.
    /// </para>
    /// <para>
    /// The editor handles Escape itself while a completion list or a signature is open, and marks
    /// it handled — so the first Escape closes the popup and the second closes the editor, which is
    /// what every code editor does.
    /// </para>
    /// </remarks>
    /// <param name="sender">The editor.</param>
    /// <param name="e">The key.</param>
    private void OnScriptEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            // Focusing the canvas raises LostFocus on the editor, which is the commit path. Doing
            // it that way rather than committing here means there is one commit, not two that have
            // to be kept saying the same thing.
            e.Handled = true;
            CanvasControl.Focus();
        }
    }

    /// <summary>Commits the edit and takes the editor away, on the editor losing focus.</summary>
    private void OnScriptCommitted(object? sender, EventArgs e)
    {
        if (_editingScript < 0 || DataContext is not MainWindowViewModel model)
        {
            return;
        }

        int slot = _editingScript;
        _editingScript = -1;

        // `E8-T40`: the room the editor reserved goes back *before* the commit. Committing
        // replaces the node, so a release afterwards is aimed at something that is not there.
        CanvasControl.EndScriptEdit(slot);

        // The editor owns the text while it is being typed, so the view model is told what it says
        // before being asked to commit it - the same order the properties pane uses.
        model.ScriptText = ScriptEditor.Text;

        bool changed = model.CommitScriptText();

        ScriptEditor.IsVisible = false;

        if (changed)
        {
            // Committing replaces the node's definition, so its ports - and its size - are not what
            // the index was built from.
            CanvasControl.RefreshStructure();
            CanvasControl.InvalidateVisual();
            model.RequestRun();
        }
    }

    /// <summary>
    /// Opens the in-node editor's popups over the block being edited, for a screenshot
    /// (<c>E8-T41</c>).
    /// </summary>
    /// <remarks>
    /// <b>A pose, in the sense <c>InspectorPane.PoseCodeEditor</c> is one.</b> A completion list
    /// exists only while somebody is typing, so no screenshot can contain one unless the
    /// application is asked to put it there — and this is the popup whose placement the pane, not
    /// the editor, is now responsible for.
    /// <para>
    /// <b>It is awaitable, and that is the difference between a verification step and a picture
    /// of nothing.</b> The first call into Roslyn composes MEF and is slow; a caller that fired
    /// the request and photographed the window a moment later photographed a closed list every
    /// time, and reported it as a pass.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes once both popups have been asked for and answered.</returns>
    public async Task PoseScriptPopupsAsync()
    {
        if (!ScriptEditor.IsVisible)
        {
            return;
        }

        ScriptEditor.FocusEditor();

        await ScriptEditor.RequestSignatureAsync().ConfigureAwait(true);
        await ScriptEditor.RequestCompletionAsync().ConfigureAwait(true);
    }

    /// <summary>The line count and the longest line of a block's source.</summary>
    /// <param name="source">The source, as the editor holds it.</param>
    /// <param name="lines">How many lines it has.</param>
    /// <param name="columns">How many characters its longest line has.</param>
    private static void Measure(string source, out int lines, out int columns)
    {
        lines = 1;
        columns = 0;

        int run = 0;

        foreach (char c in source)
        {
            if (c == '\n')
            {
                lines++;
                run = 0;
                continue;
            }

            if (c != '\r')
            {
                run++;
                columns = Math.Max(columns, run);
            }
        }

        // A trailing newline is not a line anybody typed on, and every starter script ends in one.
        if (lines > 1 && run == 0)
        {
            lines--;
        }
    }

}
