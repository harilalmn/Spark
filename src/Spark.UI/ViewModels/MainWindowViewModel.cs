using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spark.Api;
using Spark.Engine;
using Spark.Host;
using Spark.UI.Graph;
using Spark.UI.Shell;
using Spark.Viewport;

namespace Spark.UI.ViewModels;

/// <summary>
/// The main window's view model: the session, the document, evaluation, and the projection of a
/// run's results onto the canvas and the viewport.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only place the shell talks to the engine.</b> Views bind to the properties here
/// and call its methods; no view file references <c>Spark.Engine</c>, and
/// <c>Spark.Architecture.Tests</c> scans them to keep that true (ADR-0005).
/// </para>
/// <para>
/// <b>Nothing here blocks the UI thread on an evaluation.</b> <see cref="EvaluateAsync"/> hands the
/// run to <see cref="SparkSession"/>, which puts it on the thread pool and cancels whatever was
/// already running; the continuation is back on the caller's context, so the canvas and the scene
/// are only ever touched from the UI thread.
/// </para>
/// <para>
/// <b>The canvas is still not bound.</b> It owns its own node list and redraws itself, because
/// pushing two thousand nodes through <c>INotifyPropertyChanged</c> is the exact cost ADR-0013
/// exists to avoid. What is bound here is the shell around it: the library, the inspector, the
/// status line.
/// </para>
/// </remarks>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SparkSession _session = new();
    private readonly HashSet<GeometryKey> _published = [];
    private readonly DocumentHistory _history = new();
    private readonly System.Threading.SemaphoreSlim _applying = new(1, 1);

    [ObservableProperty]
    private string _statusText = "Ready.";

    [ObservableProperty]
    private string _viewportStatusText = "Waiting for the OpenGL context.";

    [ObservableProperty]
    private string _selectedWorkspace = "Default";

    [ObservableProperty]
    private string _selectionTitle = "Nothing selected";

    [ObservableProperty]
    private string _selectionDescription =
        "Select a node to edit the values typed into its unwired inputs.";

    [ObservableProperty]
    private string _diagnosticsText = "No run yet.";

    [ObservableProperty]
    private LibraryEntryViewModel? _selectedLibraryEntry;

    [ObservableProperty]
    private string _librarySearch = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _canUndo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    private bool _canRedo;

    [ObservableProperty]
    private string _undoDescription = "Nothing to undo";

    [ObservableProperty]
    private string _redoDescription = "Nothing to redo";

    [ObservableProperty]
    private string _createSearch = string.Empty;

    [ObservableProperty]
    private LibraryEntryViewModel? _selectedCreateResult;

    private CanvasGraph _graph;
    private int _placementOrdinal;
    private bool _disposed;

    /// <summary>Creates the view model with the built-in library imported and the demo loaded.</summary>
    public MainWindowViewModel()
        : this(null, null)
    {
    }

    /// <summary>Creates the view model with a named seeded graph loaded.</summary>
    /// <param name="startupGraph">
    /// <c>curves</c> for the curve demo, anything else — including null — for the point grid.
    /// </param>
    public MainWindowViewModel(string? startupGraph)
        : this(startupGraph, null)
    {
    }

    /// <summary>
    /// Creates the view model with either a named seeded graph or a file loaded.
    /// </summary>
    /// <param name="startupGraph">
    /// <c>curves</c> for the curve demo, anything else — including null — for the point grid.
    /// </param>
    /// <param name="startupDocumentPath">
    /// A `.spark` file to open instead, or null. A file that cannot be read falls back to the
    /// seeded graph with the reason in the diagnostics pane, because a shell that refuses to open
    /// is worse than one that opens and says why.
    /// </param>
    /// <remarks>
    /// <b>Exactly one graph is adopted here, and that is the whole point of this constructor.</b>
    /// Adopting a graph starts an evaluation; adopting a second one afterwards — from a load
    /// command fired once the window is open — leaves two runs in flight against one session, and
    /// whichever finishes last wins. That is not a theory. It has now produced a window showing the
    /// right graph, the previous graph's diagnostics and an empty viewport **twice**: once for
    /// <c>--graph curves</c>, and again for <c>--open</c> after a comment in the window claimed
    /// that doing it synchronously was enough. It was not: synchronous or not, it is still a second
    /// adoption.
    /// </remarks>
    public MainWindowViewModel(string? startupGraph, string? startupDocumentPath)
    {
        Layout = WorkspaceLayout.Default;

        AllLibraryEntries =
        [
            .. _session.Library.Definitions().Select(definition => new LibraryEntryViewModel(definition)),
        ];

        LibraryEntries = [.. AllLibraryEntries];
        Inspector = [];

        string? failure = null;
        CanvasGraph? opened = null;
        if (!string.IsNullOrWhiteSpace(startupDocumentPath))
        {
            try
            {
                opened = CanvasDocument.Open(File.ReadAllText(startupDocumentPath), _session.Library);
            }
            catch (SparkFileException error)
            {
                failure = Describe(error.Diagnostic);
            }
            catch (IOException error)
            {
                failure = $"That file could not be read: {error.Message}";
            }
            catch (UnauthorizedAccessException error)
            {
                failure = $"That file could not be read: {error.Message}";
            }
        }

        _graph = opened
            ?? (string.Equals(startupGraph, "curves", StringComparison.OrdinalIgnoreCase)
                ? DemoGraphs.Curves(_session.Library)
                : DemoGraphs.Demo(_session.Library));
        AdoptGraph(_graph);

        if (failure is not null)
        {
            DiagnosticsText = failure;
        }
    }

    /// <summary>Raised when the whole document was replaced, so the canvas can rebind.</summary>
    public event EventHandler? GraphReplaced;

    /// <summary>
    /// Raised after undo or redo put a different document in place.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="GraphReplaced"/> because the two want different things from the
    /// view. Opening a document frames it; undoing an edit must leave the view exactly where it
    /// was, since a canvas that jumps and re-zooms every time a user presses Ctrl+Z makes undo
    /// feel like a document load rather than a step backwards.
    /// </remarks>
    public event EventHandler? DocumentRestored;

    /// <summary>Raised on the UI thread after a run's results have been applied.</summary>
    public event EventHandler? EvaluationCompleted;

    /// <summary>The shell's pane arrangement.</summary>
    public WorkspaceLayout Layout { get; }

    /// <summary>The named workspace presets, for the workspace selector.</summary>
    public IReadOnlyList<string> Workspaces { get; } = ["Default", "Modelling", "Authoring", "Presenting"];

    /// <summary>Every node in the library, unfiltered.</summary>
    public IReadOnlyList<LibraryEntryViewModel> AllLibraryEntries { get; }

    /// <summary>The library entries matching <see cref="LibrarySearch"/>, best first.</summary>
    public ObservableCollection<LibraryEntryViewModel> LibraryEntries { get; }

    /// <summary>
    /// The handful of entries the canvas creation box offers for <see cref="CreateSearch"/>.
    /// </summary>
    /// <remarks>
    /// Short on purpose. The box exists to turn three keystrokes into a node, and a list long
    /// enough to browse is a list somebody will browse.
    /// </remarks>
    public ObservableCollection<LibraryEntryViewModel> CreateResults { get; } = [];

    /// <summary>The literals of the selected node.</summary>
    public ObservableCollection<PortLiteralViewModel> Inspector { get; }

    /// <summary>The geometry the viewport is showing.</summary>
    public ViewportScene Scene { get; } = new();

    /// <summary>The document the canvas draws.</summary>
    public CanvasGraph Graph => _graph;

    /// <summary>How many nodes the library imported, for the status line.</summary>
    public int LibraryCount => AllLibraryEntries.Count;

    /// <summary>
    /// How many nodes the last run computed rather than took from the cache.
    /// </summary>
    /// <remarks>
    /// Reported in the status line, and the number that makes the provenance cache's central claim
    /// checkable rather than asserted: after an undo this is zero, because the keys of the state
    /// being returned to are still resident (<c>E3-T8</c>).
    /// </remarks>
    public int LastRunNodesEvaluated { get; private set; }

    /// <summary>How many nodes the last run served from the cache.</summary>
    public int LastRunCacheHits { get; private set; }

    /// <summary>Replaces the document with the seeded demo graph.</summary>
    [RelayCommand]
    public void LoadDemo()
    {
        AdoptGraph(DemoGraphs.Demo(_session.Library));
        GraphReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the document with the curve demo graph.</summary>
    [RelayCommand]
    public void LoadCurves()
    {
        AdoptGraph(DemoGraphs.Curves(_session.Library));
        GraphReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records an edit on the undo stack, as a snapshot of the document it produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the shell after every gesture that changed the document — the canvas reports what
    /// it did, the inspector reports a literal, the library reports a placement. An edit that
    /// changed nothing is not recorded, so pressing Enter twice in a value box does not put a step
    /// on the stack whose undo does nothing visible.
    /// </para>
    /// <para>
    /// <b>An edit whose document cannot be written clears the history rather than skipping a
    /// step.</b> Keeping the stack across an unrecordable edit would make undo jump over that edit
    /// to a state before it, silently discarding work the user could see on screen — a far worse
    /// outcome than an undo button that has gone grey and a diagnostic saying why.
    /// </para>
    /// </remarks>
    /// <param name="label">What the edit did, in the words the menu shows: <c>Move node</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
    public void RecordEdit(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (TrySaveDocument() is not { } snapshot)
        {
            _history.Reset(null);
        }
        else
        {
            _history.Record(label, snapshot);
        }

        RefreshHistory();
    }

    /// <summary>Steps the document back one edit.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo() => Restore(_history.Undo());

    /// <summary>Reapplies the last undone edit.</summary>
    [RelayCommand(CanExecute = nameof(CanRedo))]
    public void Redo() => Restore(_history.Redo());

    /// <summary>
    /// The current document as the text of a `.spark` file, or <see langword="null"/> when it
    /// cannot be written.
    /// </summary>
    /// <remarks>
    /// A refusal is reported into the diagnostics pane rather than thrown at the view, because the
    /// view layer is not allowed to name an engine type (`E8-T11`) — and because a file Spark
    /// declines to write is a diagnostic like any other, carrying an `SPK` code the help panel can
    /// look up.
    /// </remarks>
    /// <returns>Canonically formatted JSON, or <see langword="null"/> after reporting why not.</returns>
    public string? TrySaveDocument()
    {
        try
        {
            return CanvasDocument.Save(_graph);
        }
        catch (SparkFileException error)
        {
            DiagnosticsText = Describe(error.Diagnostic);
            return null;
        }
    }

    /// <summary>
    /// Replaces the document with one read from the text of a `.spark` file.
    /// </summary>
    /// <remarks>
    /// The graph is adopted and evaluated exactly as a seeded graph is, so opening a file and
    /// opening a demo take the same path — which is what stops one of them acquiring a startup
    /// race the other does not have.
    /// </remarks>
    /// <param name="text">The file's text.</param>
    /// <returns><see langword="true"/> when it opened; otherwise the reason is in the diagnostics pane.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public bool TryOpenDocument(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            AdoptGraph(CanvasDocument.Open(text, _session.Library));
            GraphReplaced?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (SparkFileException error)
        {
            DiagnosticsText = Describe(error.Diagnostic);
            return false;
        }
    }

    /// <summary>Puts a message in the diagnostics pane, for a failure the view detected.</summary>
    /// <param name="message">The message.</param>
    public void ReportFailure(string message) => DiagnosticsText = message;

    private static string Describe(SparkDiagnostic diagnostic)
    {
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"{diagnostic.Severity} {diagnostic.Code}  ");
        text.Append(diagnostic.Message);

        if (diagnostic.Detail is { } detail)
        {
            text.AppendLine();
            text.Append(detail);
        }

        return text.ToString();
    }

    /// <summary>
    /// Replaces the document with a synthetic graph of a given size, for the canvas benchmark.
    /// </summary>
    /// <remarks>
    /// Deliberately not evaluated. The synthetic graph exists to measure the <i>renderer</i> at two
    /// thousand nodes (ADR-0013), and running two thousand nodes first would measure the engine
    /// instead and take the frame budget with it.
    /// </remarks>
    /// <param name="nodeCount">How many nodes.</param>
    public void LoadSynthetic(int nodeCount)
    {
        AdoptGraph(DemoGraphs.Synthetic(_session.Library, nodeCount), evaluate: false);
        GraphReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs the graph off the UI thread and applies the result to the canvas and the viewport.
    /// </summary>
    /// <returns>A task that completes once the results are on screen.</returns>
    public async Task EvaluateAsync()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        EvaluationResult? result = await _session.EvaluateAsync().ConfigureAwait(true);

        if (result is null)
        {
            // Superseded by a later edit. The later run's results are the ones that matter.
            return;
        }

        // Applying a result is serialised; running is not. The gate goes here and no earlier
        // because a run must still be able to supersede the one before it — taking the gate first
        // would make a new edit queue behind a long evaluation instead of cancelling it, which is
        // the property SparkSession exists to provide.
        //
        // It is needed at all because the view model must not depend on there being a UI
        // dispatcher. In the application every continuation lands back on the UI thread and this
        // costs nothing; in a headless host — a test, `spark run`, an embedder without one — two
        // results applied concurrently would tear `Inspector` and `_published`, which are ordinary
        // collections. See N24.
        await _applying.WaitAsync().ConfigureAwait(true);

        try
        {
            _graph.ApplyResult(result);
            PublishGeometry(result);
            RefreshInspector();

            LastRunNodesEvaluated = result.NodesEvaluated;
            LastRunCacheHits = result.CacheHits;

            double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            DiagnosticsText = Summarise(result);
            StatusText = string.Create(
                CultureInfo.InvariantCulture,
                $"{_graph.Nodes.Count} nodes, {_graph.Wires.Count} wires. " +
                $"Ran {result.NodesEvaluated} ({result.CacheHits} cached) in {elapsed:F0} ms; " +
                $"{Scene.Count} buffer sets, {_lastRenderableCount} objects. " +
                $"Library: {LibraryCount} nodes.");

            EvaluationCompleted?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _applying.Release();
        }
    }

    /// <summary>
    /// Places the selected library entry on the canvas at a world position.
    /// </summary>
    /// <param name="x">The left edge in world coordinates.</param>
    /// <param name="y">The top edge in world coordinates.</param>
    /// <returns>The new node's slot, or −1 when nothing was selected.</returns>
    public int PlaceSelectedLibraryEntry(double x, double y) =>
        SelectedLibraryEntry is { } entry ? PlaceEntryAt(entry, x, y) : -1;

    /// <summary>How many nodes have been placed from the library this session.</summary>
    public int PlacementOrdinal => _placementOrdinal;

    /// <summary>
    /// Places a specific library entry at a world position, and records it as an undo step.
    /// </summary>
    /// <remarks>
    /// This is what the canvas creation box commits through. It takes the entry rather than
    /// reading a selection, because the box's answer is whatever the user typed and pressed Enter
    /// on, which is not the same thing as whatever the library panel happens to have highlighted.
    /// </remarks>
    /// <param name="entry">The definition to place.</param>
    /// <param name="x">The left edge in world coordinates.</param>
    /// <param name="y">The top edge in world coordinates.</param>
    /// <returns>The new node's slot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    public int PlaceEntryAt(LibraryEntryViewModel entry, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _placementOrdinal++;
        int slot = _graph.Add(entry.Definition, x, y);
        RecordEdit("Add " + entry.DisplayName);
        return slot;
    }

    /// <summary>Rebuilds the inspector for the current canvas selection.</summary>
    /// <param name="selection">The selected slots.</param>
    public void ShowSelection(IReadOnlyCollection<int> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        Inspector.Clear();

        if (selection.Count != 1)
        {
            SelectionTitle = selection.Count == 0 ? "Nothing selected" : $"{selection.Count} nodes selected";
            SelectionDescription = selection.Count == 0
                ? "Select a node to edit the values typed into its unwired inputs."
                : "Select a single node to edit its inputs.";
            return;
        }

        int slot = selection.First();
        if (slot < 0 || slot >= _graph.Nodes.Count)
        {
            return;
        }

        CanvasNode node = _graph.Nodes[slot];
        NodeInstance instance = _graph.Engine.Node(node.Id);

        SelectionTitle = node.Title;
        SelectionDescription = BuildSelectionDescription(node, instance);

        for (int index = 0; index < instance.Definition.Inputs.Count; index++)
        {
            PortDefinition port = instance.Definition.Inputs[index];

            Inspector.Add(new PortLiteralViewModel(
                slot,
                index,
                port.Name,
                port.ValueType,
                instance.Literal(index),
                _graph.IsInputWired(slot, index),
                port.Description,
                CommitLiteral));
        }
    }

    /// <summary>Applies a named preset, or the default when the name is not one of them.</summary>
    /// <param name="name">The preset name.</param>
    [RelayCommand]
    public void ApplyWorkspace(string? name)
    {
        IReadOnlyDictionary<string, WorkspaceLayout> presets = WorkspaceLayout.Presets();

        if (name is null || !presets.TryGetValue(name, out WorkspaceLayout? preset))
        {
            preset = WorkspaceLayout.Default;
            name = "Default";
        }

        Layout.CopyFrom(preset);
        SelectedWorkspace = name;
        OnPropertyChanged(nameof(Layout));
    }

    /// <summary>Returns every pane to its default size and makes them all visible.</summary>
    [RelayCommand]
    public void ResetLayout() => ApplyWorkspace("Default");

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();

        // _applying is deliberately not disposed. A run may still be waiting on it or about to
        // release it when the window closes, and disposing a SemaphoreSlim out from under that
        // turns an orderly shutdown into an ObjectDisposedException on a thread pool thread.
        // SemaphoreSlim only needs disposal when its AvailableWaitHandle has been taken, and it
        // never is here.
    }

    partial void OnLibrarySearchChanged(string value) => Rank(value, LibraryEntries, limit: int.MaxValue);

    partial void OnCreateSearchChanged(string value)
    {
        // Eight is what fits above the canvas without covering the graph the node is going into.
        Rank(value, CreateResults, limit: 8);
        SelectedCreateResult = CreateResults.Count > 0 ? CreateResults[0] : null;
    }

    /// <summary>
    /// Fills a collection with the library, ranked against a query.
    /// </summary>
    /// <remarks>
    /// Both the library panel and the canvas creation box come through here, which is the point:
    /// two search boxes ranking by different rules is how a user learns that one of them is the
    /// one that works.
    /// </remarks>
    /// <param name="query">What the user typed.</param>
    /// <param name="into">The collection to fill.</param>
    /// <param name="limit">The greatest number of results to keep.</param>
    private void Rank(string query, ObservableCollection<LibraryEntryViewModel> into, int limit)
    {
        into.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            // No query is not a failed search. The panel shows everything in library order; the
            // creation box shows nothing, because a list of the whole library under the pointer is
            // a menu rather than an answer.
            if (limit == int.MaxValue)
            {
                foreach (LibraryEntryViewModel entry in AllLibraryEntries)
                {
                    into.Add(entry);
                }
            }

            return;
        }

        List<(LibraryEntryViewModel Entry, NodeSearchResult Result)> matches = [];

        foreach (LibraryEntryViewModel entry in AllLibraryEntries)
        {
            NodeSearchResult result = NodeSearch.Score(
                entry.DisplayName, entry.Category, entry.Description, query);

            if (result.IsMatch)
            {
                matches.Add((entry, result));
            }
        }

        matches.Sort((left, right) =>
            NodeSearch.Compare(left.Result, left.Entry.DisplayName, right.Result, right.Entry.DisplayName));

        foreach ((LibraryEntryViewModel entry, _) in matches)
        {
            if (into.Count >= limit)
            {
                break;
            }

            into.Add(entry);
        }
    }

    private int _lastRenderableCount;

    private void AdoptGraph(CanvasGraph graph, bool evaluate = true, bool resetHistory = true)
    {
        _graph = graph;

        // Every edit the canvas or the inspector makes runs inside the session's mutation gate,
        // which cancels the run in flight first. Without it an edit lands in the middle of a
        // traversal and surfaces as an exception nowhere near the gesture that caused it.
        graph.EditScope = edit => _session.Mutate(_ => edit());
        _session.Replace(graph.Engine);

        // Clearing the scene is what stops the previous document's geometry outliving it.
        Scene.Clear();
        _published.Clear();
        Inspector.Clear();

        // A new document starts a new history: undoing across the boundary would bring back a
        // graph the user had closed, which is a different operation from the one Ctrl+Z promises.
        // A restore keeps the history it is stepping through, which is why this is conditional.
        if (resetHistory)
        {
            _history.Reset(TrySaveDocument());
        }

        RefreshHistory();

        if (evaluate)
        {
            _ = EvaluateGraphAsync();
        }
        else
        {
            DiagnosticsText = "Not evaluated: synthetic graphs are a renderer measurement.";
            StatusText = string.Create(
                CultureInfo.InvariantCulture,
                $"{graph.Nodes.Count} nodes, {graph.Wires.Count} wires (not evaluated).");
        }
    }

    /// <summary>
    /// Puts a snapshot back in place as the document, and tells the view it happened.
    /// </summary>
    /// <remarks>
    /// The snapshot is reopened through <see cref="CanvasDocument"/> — the same path a file takes —
    /// so undo restores exactly what saving would have written, including node positions, and there
    /// is no second definition of what a document is for it to drift from.
    /// </remarks>
    /// <param name="snapshot">The document to restore, or null when there was no step to take.</param>
    private void Restore(string? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        try
        {
            AdoptGraph(CanvasDocument.Open(snapshot, _session.Library), evaluate: true, resetHistory: false);
        }
        catch (SparkFileException error)
        {
            // A snapshot this session wrote and cannot read back is a defect in the reader or the
            // writer, not in the user's graph. Say so, and stop offering a history that is lying.
            DiagnosticsText = Describe(error.Diagnostic);
            _history.Reset(null);
            RefreshHistory();
            return;
        }

        DocumentRestored?.Invoke(this, EventArgs.Empty);
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        CanUndo = _history.CanUndo;
        CanRedo = _history.CanRedo;
        UndoDescription = _history.UndoLabel is { } undo ? "Undo " + undo : "Nothing to undo";
        RedoDescription = _history.RedoLabel is { } redo ? "Redo " + redo : "Nothing to redo";
    }

    private async Task EvaluateGraphAsync()
    {
        try
        {
            await EvaluateAsync().ConfigureAwait(true);
        }
        catch (InvalidOperationException error)
        {
            DiagnosticsText = error.Message;
        }
    }

    private void CommitLiteral(PortLiteralViewModel editor, object? value)
    {
        _graph.SetLiteral(editor.Slot, editor.PortIndex, value);
        RecordEdit("Change " + editor.Name);
        _ = EvaluateGraphAsync();
    }

    private void RefreshInspector()
    {
        foreach (PortLiteralViewModel editor in Inspector)
        {
            if (editor.Slot >= 0 && editor.Slot < _graph.Nodes.Count)
            {
                continue;
            }

            Inspector.Clear();
            return;
        }
    }

    private void PublishGeometry(EvaluationResult result)
    {
        SceneBuilder builder = new();

        foreach ((int slot, int portIndex) in _graph.PreviewPorts())
        {
            CanvasNode node = _graph.Nodes[slot];
            builder.Add(
                new GeometryKey(node.Id.ToString(), portIndex),
                result.Value(node.Id, portIndex));
        }

        // Retiring the previously published keys is the other half of the update. Without it a node
        // that used to produce points and now produces none leaves them on screen, which reads as
        // the graph not having run.
        builder.PublishTo(Scene, _published);

        _published.Clear();
        foreach (GeometryKey key in builder.Keys())
        {
            _published.Add(key);
        }

        _lastRenderableCount = builder.RenderableCount;
    }

    private static string BuildSelectionDescription(CanvasNode node, NodeInstance instance)
    {
        StringBuilder builder = new();
        builder.Append(node.Description ?? "No description.");
        builder.Append("\n\nLacing: ").Append(instance.EffectiveLacing);

        if (node.Message is { } message)
        {
            builder.Append("\n\n").Append(message);
        }

        if (node.ResultSummary is { } summary)
        {
            builder.Append("\n\nOutput: ").Append(summary);
        }

        return builder.ToString();
    }

    private string Summarise(EvaluationResult result)
    {
        if (result.Diagnostics.Count == 0)
        {
            return $"{result.NodesEvaluated} nodes evaluated, {result.CacheHits} served from cache. No diagnostics.";
        }

        StringBuilder builder = new();
        foreach (SparkDiagnostic diagnostic in result.Diagnostics.Take(6))
        {
            string title = diagnostic.NodeId is { } id && _graph.SlotOf(new NodeId(id)) is int slot && slot >= 0
                ? _graph.Nodes[slot].Title
                : "graph";

            builder.Append(diagnostic.Severity).Append(' ').Append(diagnostic.Code)
                .Append("  ").Append(title).Append('\n')
                .Append(diagnostic.Message).Append("\n\n");
        }

        if (result.Diagnostics.Count > 6)
        {
            builder.Append(result.Diagnostics.Count - 6).Append(" more.");
        }

        return builder.ToString().TrimEnd();
    }
}
