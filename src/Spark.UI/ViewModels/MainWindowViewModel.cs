using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spark.Api;
using Spark.Api.Help;
using Spark.Engine;
using Spark.Host;
using Spark.Scripting;
using Spark.UI.Graph;
using Spark.UI.Shell;
using Spark.UI.Views.Controls;
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
    private HelpLibrary? _help;
    private CustomNodeLibrary? _customNodes;
    private PackageBrowserViewModel? _packages;
    private LocalReferencesViewModel? _localReferences;

    /// <summary>
    /// The package feed to search instead of nuget.org, or null (<c>--package-source</c>).
    /// </summary>
    /// <remarks>
    /// <b>Static because the browser is built in the constructor</b>, and the constructor runs
    /// before anything can set a property on the instance. An organisation's feed has to be in
    /// place before installed packages are loaded, not after.
    /// </remarks>
    public static string? PackageSource { get; set; }

    /// <summary>
    /// How many of the startup graph's leading nodes to freeze (<c>--freeze N</c>), or zero.
    /// </summary>
    /// <remarks>
    /// <b>Applied before the graph is adopted, and that is why it is here rather than in the
    /// window.</b> Adopting a graph starts an evaluation; freezing afterwards starts a second one,
    /// and the screenshot path photographs whichever landed first — which was the unfrozen
    /// run every time. Freezing before the only evaluation removes the race rather than waiting
    /// it out.
    /// </remarks>
    public static int FreezeFirst { get; set; }

    private readonly HashSet<GeometryKey> _published = [];
    private readonly DocumentHistory _history = new();
    private EvaluationResult? _lastResult;
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

    /// <summary>The note the properties pane is editing, or null when it is not editing one.</summary>
    /// <remarks>
    /// Also the flag the pane's note editor is shown by. A separate <c>IsNoteSelected</c> boolean
    /// would be a second thing that has to agree with this one, and the day they disagree is the
    /// day a note's text is typed into a box that is editing nothing.
    /// </remarks>
    [ObservableProperty]
    private CanvasNote? _selectedNote;

    [ObservableProperty]
    private string _noteText = string.Empty;

    /// <summary>The group the properties pane is editing, or null.</summary>
    [ObservableProperty]
    private CanvasGroup? _selectedGroup;

    [ObservableProperty]
    private string _groupTitle = string.Empty;

    /// <summary>
    /// The selected node's output in full, for the watch panel, or empty when there is nothing to
    /// watch.
    /// </summary>
    /// <remarks>
    /// Separate from the summary the preview bubble shows, and deliberately so: a bubble is a
    /// glance and is cut at sixty characters, and a panel is where somebody goes to actually read
    /// the value. Rendering the same string in both would make one of the two wrong.
    /// </remarks>
    [ObservableProperty]
    private string _watchText = string.Empty;

    /// <summary>The rank line above the watch panel, or empty when nothing is being watched.</summary>
    [ObservableProperty]
    private string _watchRank = string.Empty;

    /// <summary>
    /// The code block being edited, or null when the selection is not one.
    /// </summary>
    /// <remarks>
    /// The source is edited in the properties pane for now, the way a note's text is. The real
    /// editing surface is `E6-T11`'s AvaloniaEdit host with `E6-T7`'s wire-typed completion, and
    /// putting the text box here first gets a working code block on screen without waiting for it
    /// — a code block you cannot type into is not a code block.
    /// </remarks>
    [ObservableProperty]
    private string _selectedRunMode = RunModeNames.Of(RunMode.Automatic);

    [ObservableProperty]
    private bool _hasPendingRun;

    private RunMode _runMode = RunMode.Automatic;
    private Avalonia.Threading.DispatcherTimer? _periodic;

    [ObservableProperty]
    private bool _canSetLacing;

    [ObservableProperty]
    private string _selectedLacing = LacingNames.Auto;

    [ObservableProperty]
    private string _lacingNote = string.Empty;

    /// <summary>
    /// True while <see cref="ShowSelection"/> is pushing the selected node's lacing into
    /// <see cref="SelectedLacing"/>, so the change handler knows the user did not do it.
    /// </summary>
    /// <remarks>
    /// Without this, selecting a node would <i>write</i> that node's own lacing back onto it as an
    /// undo step and a re-run - one per click, on every node, forever.
    /// </remarks>
    private bool _showingLacing;

    private int _lacingSlot = -1;

    [ObservableProperty]
    private CanvasNode? _selectedCodeBlock;

    [ObservableProperty]
    private string _scriptText = string.Empty;

    /// <summary>
    /// Why an opened graph has not been run, or null when there is nothing to say (`E6-T16`).
    /// </summary>
    [ObservableProperty]
    private string? _scriptBanner;

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
        : this(startupGraph, startupDocumentPath, noScript: false)
    {
    }

    /// <summary>
    /// Creates the view model, optionally with scripting refused for the whole session
    /// (`E6-T16`).
    /// </summary>
    /// <param name="startupGraph">The seeded graph to open, or null.</param>
    /// <param name="startupDocumentPath">A file to open instead, or null.</param>
    /// <param name="noScript">
    /// True to refuse scripting permanently. A graph containing a code block then fails to open,
    /// naming the node.
    /// </param>
    /// <remarks>
    /// <b>Refusing happens before the document is read</b>, because the refusal has to hold for the
    /// document being opened at startup as well as for every later one — and because
    /// <see cref="SparkSession.DisableScripting"/> is one-way, which is what makes it a trust
    /// boundary rather than a setting.
    /// </remarks>
    public MainWindowViewModel(string? startupGraph, string? startupDocumentPath, bool noScript)
    {
        if (noScript)
        {
            _session.DisableScripting();
        }

        Layout = WorkspaceLayout.Default;

        // Installed packages contribute their nodes here, before anything reads the library: the
        // library pane is built from it three lines down and a startup document is resolved
        // against it below. Loading them any later would hand a user placeholders for nodes they
        // have already installed, which is the one outcome E7-T6 exists to avoid.
        IReadOnlyList<string> packageProblems = LoadInstalledPackages();

        // Watching starts here too, so an assembly rebuilt during the session announces
        // itself whether or not the user ever opens the list.
        _ = LocalReferences().Apply();

        AllLibraryEntries =
        [
            .. _session.Library.Definitions().Select(definition => new LibraryEntryViewModel(definition)),
        ];

        LibraryEntries = [.. AllLibraryEntries];
        RebuildLibraryGroups(expand: false);
        Inspector = [];

        string? failure = null;
        CanvasGraph? opened = null;
        if (!string.IsNullOrWhiteSpace(startupDocumentPath))
        {
            try
            {
                opened = CanvasDocument.Open(File.ReadAllText(startupDocumentPath), _session.Library, _session.Scripts);
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
            ?? (string.Equals(startupGraph, "surfaces", StringComparison.OrdinalIgnoreCase)
                ? DemoGraphs.Surfaces(_session.Library)
                : string.Equals(startupGraph, "curves", StringComparison.OrdinalIgnoreCase)
                ? DemoGraphs.Curves(_session.Library)
                : string.Equals(startupGraph, "solids", StringComparison.OrdinalIgnoreCase)
                ? DemoGraphs.Solids(_session.Library)
                : DemoGraphs.Demo(_session.Library));
        for (int slot = 0; slot < FreezeFirst && slot < _graph.Nodes.Count; slot++)
        {
            _ = _graph.Engine.SetFrozen(_graph.Nodes[slot].Id, frozen: true);
        }

        AdoptGraph(_graph);

        if (failure is not null)
        {
            DiagnosticsText = failure;
        }

        if (packageProblems.Count > 0)
        {
            DiagnosticsText = string.Join(
                Environment.NewLine,
                new[] { DiagnosticsText }.Concat(packageProblems).Where(line => !string.IsNullOrEmpty(line)));
        }
    }

    /// <summary>
    /// Loads every installed package's nodes into the session library.
    /// </summary>
    /// <returns>The problems worth showing a user, or empty.</returns>
    /// <remarks>
    /// <b>A broken package store must not stop the application starting.</b> The user needs to get
    /// in to remove whatever is broken, and a shell that refuses to open leaves them with no way
    /// to do it.
    /// </remarks>
    private IReadOnlyList<string> LoadInstalledPackages()
    {
        try
        {
            _ = Packages().LoadInstalled();
            return Packages().StartupProblems;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [$"Installed packages could not be read: {error.Message}"];
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

    /// <summary>Raised after <see cref="Layout"/> has been changed by a preset or by a reset.</summary>
    /// <remarks>
    /// <see cref="Layout"/> is a mutable model rather than a stream of values, so changing it
    /// notifies nothing on its own. Until this event existed the preset buttons ran, updated the
    /// model, and left the shell exactly as it was — a command that did its job and appeared to do
    /// nothing.
    /// </remarks>
    public event EventHandler? WorkspaceChanged;

    /// <summary>The shell's pane arrangement.</summary>
    public WorkspaceLayout Layout { get; }

    /// <summary>The named workspace presets, for the workspace selector.</summary>
    public IReadOnlyList<string> Workspaces { get; } = ["Default", "Modelling", "Authoring", "Presenting"];

    /// <summary>Every node in the library, unfiltered.</summary>
    public IReadOnlyList<LibraryEntryViewModel> AllLibraryEntries { get; }

    /// <summary>The library entries matching <see cref="LibrarySearch"/>, best first.</summary>
    public ObservableCollection<LibraryEntryViewModel> LibraryEntries { get; }

    /// <summary>
    /// <see cref="LibraryEntries"/> grouped by category, which is what the panel shows
    /// (<c>E8-T24</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Derived from the filtered list, not the whole library</b>, so searching narrows the tree
    /// rather than leaving it to be searched again by eye. A category with no match does not
    /// appear at all.
    /// </para>
    /// <para>
    /// <b>Collapsed when there is no query and expanded when there is.</b> With no query this is
    /// the whole library and the point of the tree is that ten headings fit on a screen where a
    /// hundred and eight entries do not. With a query the user has already said what they want,
    /// and making them open a group to see the thing they searched for would be a click charged
    /// for nothing.
    /// </para>
    /// </remarks>
    public ObservableCollection<LibraryGroupViewModel> LibraryGroups { get; } = [];

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

    /// <summary>Replaces the document with the surface demo graph.</summary>
    [RelayCommand]
    public void LoadSurfaces()
    {
        AdoptGraph(DemoGraphs.Surfaces(_session.Library));
        GraphReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the document with the curve demo graph.</summary>
    [RelayCommand]
    public void LoadCurves()
    {
        AdoptGraph(DemoGraphs.Curves(_session.Library));
        GraphReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the document with the solid demo graph.</summary>
    [RelayCommand]
    public void LoadSolids()
    {
        AdoptGraph(DemoGraphs.Solids(_session.Library));
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
    public bool TryOpenDocument(string text) => TryOpenDocument(text, origin: null);

    /// <summary>
    /// Replaces the document with one read from a file, applying the trust rule (`E6-T16`).
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <param name="origin">Where it came from, or null when it has no path.</param>
    /// <returns><see langword="true"/> when it opened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>A graph containing a code block is not evaluated because it was opened.</b> That is the
    /// whole of the posture: a Spark graph is executable code, .NET has no way to sandbox it, and
    /// running it on double-click would make opening a file from a colleague equivalent to running
    /// an unknown program. It opens, it draws, its values are empty, and a banner says why.
    /// </para>
    /// <para>
    /// <b>A graph the user has already agreed to runs immediately</b>, keyed on the file *and* its
    /// exact content — see <see cref="ScriptTrustStore"/> for why both halves are needed. Nothing
    /// about a graph with no code blocks in it changes: there is nothing to decide.
    /// </para>
    /// </remarks>
    public bool TryOpenDocument(string text, string? origin)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            IReadOnlyList<string> scripts = SparkFile.Read(text).Scripts();
            bool run = scripts.Count == 0 || _trust.IsTrusted(origin, scripts);

            // **Scripting is turned on here, and only when the document needs it.** A session that
            // has never placed a code block has never loaded Roslyn, and a document with none in it
            // must not make it - that is `E6-T14`. But a saved graph *with* one has to open in a
            // session that has not placed one, which it could not do while this passed whatever
            // `_session.Scripts` happened to be.
            IScriptNodeFactory? factory = scripts.Count > 0 && _session.ScriptingAllowed
                ? _session.EnableScripting()
                : _session.Scripts;

            AdoptGraph(CanvasDocument.Open(text, _session.Library, factory), evaluate: run);

            PendingScripts = run ? 0 : scripts.Count;
            PendingOrigin = run ? null : origin;
            ScriptBanner = run
                ? null
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"This graph contains {scripts.Count} code block{(scripts.Count == 1 ? string.Empty : "s")}, which is a program. It has been opened but not run.");

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
        // An explicit run - the button, F5, or a periodic tick - is what clears the debt a Manual
        // or Periodic session has run up. RequestRun sets it; only an actual run may clear it.
        HasPendingRun = false;

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
            // Kept so the watch panel can render the value in full. The canvas node only carries
            // a sixty-character summary, which is right for a bubble and useless for reading.
            _lastResult = result;

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

    /// <summary>
    /// Shows a selected group in the properties pane, or clears it.
    /// </summary>
    /// <param name="group">The group, or null when the selection is not a group.</param>
    public void ShowGroup(CanvasGroup? group)
    {
        Inspector.Clear();
        SelectedNote = null;
        SelectedGroup = group;
        WatchText = string.Empty;
        WatchRank = string.Empty;

        if (group is null)
        {
            return;
        }

        SelectionTitle = "Group";
        int count = group.Members.Count;
        string nodes = count == 1 ? "1 node" : string.Create(CultureInfo.InvariantCulture, $"{count} nodes");
        SelectionDescription = nodes + ". Deleting the group leaves them where they are.";
        GroupTitle = group.Title;
    }

    /// <summary>Commits the edited title back to the selected group.</summary>
    /// <returns>True when the title actually changed.</returns>
    public bool CommitGroupTitle()
    {
        if (SelectedGroup is not { } group || group.Title == GroupTitle)
        {
            return false;
        }

        group.Title = GroupTitle;
        return true;
    }

    /// <summary>
    /// Shows a selected note in the properties pane, or clears it.
    /// </summary>
    /// <param name="note">The note, or null when the selection is not a note.</param>
    /// <remarks>
    /// The text is edited here rather than on the canvas. The canvas is one immediate-mode surface
    /// that hosts no controls at all — that is <a href="adr/0013">ADR-0013</a>, and it is what lets
    /// two thousand nodes draw in a frame — so putting a caret in it would mean writing a text
    /// editor to avoid writing a binding.
    /// </remarks>
    public void ShowNote(CanvasNote? note)
    {
        Inspector.Clear();
        SelectedGroup = null;
        SelectedNote = note;
        WatchText = string.Empty;
        WatchRank = string.Empty;

        if (note is null)
        {
            return;
        }

        SelectionTitle = "Note";
        SelectionDescription = "Type into the box below. A note is not evaluated and nothing can "
            + "be wired to it.";
        NoteText = note.Text;
    }

    /// <summary>Commits the edited text back to the selected note.</summary>
    /// <returns>True when the note's text actually changed, so the caller can record one step.</returns>
    /// <remarks>
    /// Returns whether anything changed rather than assuming it did. The pane commits on every lost
    /// focus, and a user who clicks into a note and out again without typing must not find a step
    /// on the undo stack whose undo does nothing — the same rule the drag gesture and the alignment
    /// both had to learn.
    /// </remarks>
    public bool CommitNoteText()
    {
        if (SelectedNote is not { } note || note.Text == NoteText)
        {
            return false;
        }

        note.Text = NoteText;
        return true;
    }

    /// <summary>
    /// Places a new code block on the canvas and selects it.
    /// </summary>
    /// <param name="x">Where to put it.</param>
    /// <param name="y">Where to put it.</param>
    /// <returns>The new node's slot, or −1 when scripting is off.</returns>
    /// <remarks>
    /// <b>This is the first thing in the application that touches Roslyn</b>, and it does so by
    /// asking the session to enable scripting rather than by referencing the compiler. A session
    /// that never places a code block never loads it, which is `E6-T14`.
    /// </remarks>
    public int PlaceCodeBlock(double x, double y)
    {
        if (_session.Scripts is null && !_session.ScriptingAllowed)
        {
            StatusText = "Scripting is switched off, so a code block cannot be placed.";
            return -1;
        }

        IScriptNodeFactory scripts = _session.EnableScripting();

        // A NEW BLOCK HAS NO INPUTS, AND THE STARTER IS A COMMENT RATHER THAN CODE.
        //
        // It used to be `return a;`, which compiles to a block with one input port called `a`
        // that nobody asked for. A user placing their first code block got a port they then had
        // to work out how to get rid of - and the answer, "delete the identifier from the
        // source", is exactly the thing the starter was failing to teach.
        //
        // An empty script is legal here: it compiles to zero inputs and one `result` output. So
        // the starter is one line that states the rule which is otherwise invisible, because the
        // question the first user of this asked was "where do I type the code?" and the second
        // was "how do I get more inputs?". Both are answered by the sentence below.
        const string Starter = "// Any name you have not declared becomes an input port.\n";

        // Scripting may have been switched on by this very call, so the canvas learns about the
        // factory here rather than only at `AdoptGraph` — otherwise the first code block placed in
        // a session would never re-type itself when a wire landed on it.
        _graph.Scripts = scripts;

        _placementOrdinal++;
        int slot = _graph.Add(NodeDefinition.FromScript(scripts.Create(Starter), Starter), x, y);
        RecordEdit("Add code block");

        return slot;
    }

    /// <summary>
    /// Shows a selected code block's source in the properties pane, or clears it.
    /// </summary>
    /// <param name="node">The node, or null.</param>
    public void ShowCodeBlock(CanvasNode? node)
    {
        SelectedCodeBlock = node;
        ScriptText = node is null ? string.Empty : ScriptOf(node) ?? string.Empty;
    }

    /// <summary>
    /// Recompiles the selected code block against the edited source.
    /// </summary>
    /// <returns>True when the script changed and the node was rebuilt.</returns>
    /// <remarks>
    /// <b>Rebuilding replaces the node's definition, which changes its ports</b> — that is the
    /// point of a code block and it is also why this is not a literal edit. A wire into a port
    /// that no longer exists cannot survive, so the graph drops it; a wire into a port that still
    /// exists by name does survive, which is what makes editing a script tolerable rather than
    /// destructive.
    /// </remarks>
    public bool CommitScriptText()
    {
        if (SelectedCodeBlock is not { } node || _session.Scripts is not { } scripts)
        {
            return false;
        }

        if (ScriptOf(node) == ScriptText)
        {
            return false;
        }

        // The types already wired in are carried across the edit, by port *name* — so a block that
        // was typed against a `Point3d` stays typed against it when another line is added, rather
        // than falling back to `dynamic` until the wire is redrawn.
        NodeDefinitionSource rebuilt = scripts.Create(ScriptText, _graph.Engine.InputTypes(node.Id));

        if (!_graph.ReplaceDefinition(node, NodeDefinition.FromScript(rebuilt, ScriptText)))
        {
            return false;
        }

        RecordEdit("Edit code block");
        return true;
    }

    /// <summary>
    /// The completions available at a caret inside the selected code block (`E6-T7`).
    /// </summary>
    /// <param name="code">The source as the editor holds it.</param>
    /// <param name="caret">The caret offset.</param>
    /// <param name="cancellationToken">Cancels a request a later keystroke has superseded.</param>
    /// <returns>The candidates, or nothing when there is no code block or no scripting.</returns>
    /// <remarks>
    /// <b>The ports come from the graph, which is the whole point.</b> The completion list is built
    /// against the types the wires carry, so a port called <c>centre</c> with a point wired into it
    /// completes as a <c>Point3d</c> — and one with nothing wired into it completes as
    /// <c>dynamic</c>, because that is what the compiler will make of it.
    /// </remarks>
    public async Task<IReadOnlyList<CodeCompletionCandidate>> CompleteScriptAsync(
        string code, int caret, CancellationToken cancellationToken)
    {
        if (SelectedCodeBlock is not { } node || _session.Completion() is not { } completion)
        {
            return [];
        }

        Dictionary<string, Type?> ports = new(StringComparer.Ordinal);

        foreach (PortDefinition port in _graph.Engine.Node(node.Id).Definition.Inputs)
        {
            ports[port.Name] = port.ValueType == typeof(object) ? null : port.ValueType;
        }

        IReadOnlyList<ScriptCompletionItem> items =
            await completion.CompleteAsync(code, caret, ports, cancellationToken).ConfigureAwait(true);

        return [.. items.Select(item => new CodeCompletionCandidate(item.DisplayText, item.Kind))];
    }

    /// <summary>How many code blocks are waiting on the user's decision.</summary>
    public int PendingScripts { get; private set; }

    /// <summary>Where the untrusted document came from, or null.</summary>
    public string? PendingOrigin { get; private set; }

    /// <summary>Whether an opened graph is waiting to be trusted before it runs.</summary>
    public bool IsAwaitingTrust => PendingScripts > 0;

    /// <summary>
    /// Runs a graph the user has decided to trust, and remembers the decision (`E6-T16`).
    /// </summary>
    /// <param name="remember">
    /// True to record the decision, so this file saying exactly this runs without asking again.
    /// </param>
    /// <returns>True when there was something waiting.</returns>
    /// <remarks>
    /// <b>Running once and remembering are two different decisions and are offered as two.</b> A
    /// user opening a graph from an unknown source may reasonably want to run it now and be asked
    /// again next time; a store that recorded every *run* would quietly turn a one-off into a
    /// standing permission.
    /// </remarks>
    public bool TrustAndRun(bool remember)
    {
        if (!IsAwaitingTrust)
        {
            return false;
        }

        if (remember && PendingOrigin is { } origin && TrySaveDocument() is { } text)
        {
            _trust.Trust(origin, SparkFile.Read(text).Scripts());
        }

        PendingScripts = 0;
        PendingOrigin = null;
        ScriptBanner = null;

        _ = EvaluateGraphAsync();

        return true;
    }

    /// <summary>The source behind a canvas node, or null when it is not a code block.</summary>
    private string? ScriptOf(CanvasNode node) =>
        _graph.Engine.Node(node.Id).Definition.Script;

    /// <summary>
    /// Every help topic available in this session: the hand-written concept topics from
    /// <c>docs/help/</c>, plus a generated page for every node currently loaded.
    /// </summary>
    /// <returns>The library, built once and reused.</returns>
    /// <remarks>
    /// <para>
    /// Built on first use rather than at startup. Generating a page per node is cheap but not
    /// free, and most sessions never open help; paying for it at launch would slow the thing every
    /// session does to speed up the thing few do.
    /// </para>
    /// <para>
    /// <b>The node pages come from the live library</b>, so a package installed this session has
    /// help the moment it is loaded, and a node that does not exist has no page. That is
    /// <c>E10-T5</c>'s whole claim, and it holds only because nothing is generated ahead of time.
    /// </para>
    /// </remarks>
    public HelpLibrary Help()
    {
        if (_help is not null)
        {
            return _help;
        }

        HelpLibrary library = new();

        foreach (string directory in HelpDirectories())
        {
            if (library.LoadDirectory(directory) > 0)
            {
                break;
            }
        }

        library.AddRange(NodeReference.ForAll(_session.Library));
        library.Add(NodeReference.Index(_session.Library));
        library.AddRange(DiagnosticReference.ForAll());

        _help = library;
        return _help;
    }

    /// <summary>
    /// Where the hand-written topics might be: beside the executable in an install, or up the tree
    /// in a source checkout.
    /// </summary>
    /// <remarks>
    /// Two candidates rather than one, because a developer running from <c>bin/Debug</c> and a
    /// user running an install are both ordinary cases, and a help window that works for only one
    /// of them gets tested by only one of them.
    /// </remarks>
    private static IEnumerable<string> HelpDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "help");

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "docs", "help");
            if (Directory.Exists(candidate))
            {
                yield return candidate;
                yield break;
            }

            directory = directory.Parent;
        }
    }

    /// <summary>
    /// The packages this graph needs and this machine does not have (<c>E7-T6</c>).
    /// </summary>
    /// <returns>Each package id once, in the order the nodes appear, or empty.</returns>
    /// <remarks>
    /// <b>Read from the placeholders themselves rather than remembered from the load.</b> A
    /// placeholder keeps the original <c>NodeKey</c> verbatim — that is the guarantee the whole
    /// row is built on — so the package a user has to install is written on the node. Recomputing
    /// also means the banner goes away by itself once a package is installed and the nodes resolve,
    /// with nothing to invalidate.
    /// </remarks>
    public IReadOnlyList<string> MissingPackages()
    {
        List<string> found = [];

        foreach (CanvasNode node in _graph.Nodes)
        {
            if (!_graph.Engine.TryGetNode(node.Id, out NodeInstance? instance)
                || instance is null
                || !PlaceholderNode.IsPlaceholder(instance.Definition))
            {
                continue;
            }

            string package = instance.Definition.Key.Package;

            if (!string.IsNullOrWhiteSpace(package) && !found.Contains(package, StringComparer.Ordinal))
            {
                found.Add(package);
            }
        }

        return found;
    }

    /// <summary>The node key at a canvas slot, or null when the slot is not a node.</summary>
    /// <param name="slot">The canvas slot index.</param>
    /// <returns>The key as <c>Package/Name</c>, or null.</returns>
    public string? NodeKeyAt(int slot)
    {
        if (slot < 0 || slot >= _graph.Nodes.Count)
        {
            return null;
        }

        CanvasNode node = _graph.Nodes[slot];
        return _graph.Engine.TryGetNode(node.Id, out NodeInstance? instance) && instance is not null
            ? instance.Definition.Key.Value
            : null;
    }

    /// <summary>
    /// Highlights the geometry produced by the selected nodes in the viewport (<c>E9-T9</c>).
    /// </summary>
    /// <param name="selection">The selected canvas slots.</param>
    /// <returns>True when the viewport needs a repaint.</returns>
    /// <remarks>
    /// Called from the same place the inspector is rebuilt, because selection is one event with two
    /// consequences and splitting them is how the two drift apart.
    /// </remarks>
    public bool ShowSelectionInViewport(IReadOnlyCollection<int> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        List<string> nodes = [];
        foreach (int slot in selection)
        {
            if (slot >= 0 && slot < _graph.Nodes.Count)
            {
                nodes.Add(_graph.Nodes[slot].Id.ToString());
            }
        }

        return Scene.SetSelectedNodes(nodes);
    }

    /// <summary>
    /// The session's custom node library, over the same node library the canvas resolves against.
    /// </summary>
    /// <returns>The library, built once and reused.</returns>
    /// <remarks>
    /// <b>The same instance every time, deliberately.</b> A custom node built by collapsing a
    /// selection has to be visible to the next collapse, so one can contain another - which is the
    /// whole of what <i>graph-in-graph is the same mechanism</i> buys, and it only works if there
    /// is one library rather than one per gesture.
    /// </remarks>
    public CustomNodeLibrary CustomNodes() => _customNodes ??= new CustomNodeLibrary(_session.Library);

    /// <summary>
    /// The session's local assembly references (<c>E7-T9</c>).
    /// </summary>
    /// <returns>The list, built once and reused.</returns>
    /// <remarks>
    /// <b>The catalogue is reached through a delegate rather than handed over</b>, because
    /// building this must not load Roslyn. <c>E6-T14</c> says a graph with no script nodes never
    /// loads <c>Spark.Scripting</c>, and the delegate is called only when an assembly is actually
    /// referenced — which is a user deliberately preparing to write a code block.
    /// </remarks>
    public LocalReferencesViewModel LocalReferences() =>
        _localReferences ??= new LocalReferencesViewModel(
            store: null,
            catalogue: () => _session.ScriptReferences(),
            toUiThread: work => Avalonia.Threading.Dispatcher.UIThread.Post(work));

    /// <summary>
    /// The session's package browser, over the same node library everything else resolves against
    /// (<c>E7-T10</c>).
    /// </summary>
    /// <returns>The browser, built once and reused.</returns>
    /// <remarks>
    /// <para>
    /// <b>Built on first use, because most sessions never open it</b> and constructing it reads
    /// the install folder off disk.
    /// </para>
    /// <para>
    /// <b>Installing a package drops the cached help library.</b> Help pages are generated from
    /// the live node library, so one built before an install would be missing the pages for the
    /// nodes that install just added - and <c>E10-T5</c>'s claim is precisely that a package
    /// installed this session has help the moment it is loaded. Rebuilding on the next F1 is
    /// cheap; a help window that cannot find a node the canvas offers is not.
    /// </para>
    /// </remarks>
    public PackageBrowserViewModel Packages()
    {
        if (_packages is not null)
        {
            return _packages;
        }

        PackageBrowserViewModel browser = new(_session.Library, source: PackageSource);
        browser.Installed.CollectionChanged += (_, _) => _help = null;

        _packages = browser;
        return _packages;
    }

    /// <summary>The key handed out by the last call to <see cref="NextCustomNodeIdentity"/>.</summary>
    public NodeKey LastCustomNodeKey { get; private set; }

    /// <summary>
    /// A name for the next custom node, unique within the session.
    /// </summary>
    /// <remarks>
    /// Numbered rather than prompted. A dialog asking for a name before the user has seen what
    /// they made is a question asked at the wrong moment; the node can be renamed once it exists.
    /// </remarks>
    public CustomNodeInterface NextCustomNodeIdentity()
    {
        CustomNodeLibrary customs = CustomNodes();

        for (int ordinal = 1; ; ordinal++)
        {
            string name = "Custom." + ordinal.ToString(CultureInfo.InvariantCulture);
            NodeKey key = new("Custom", name);

            if (!_session.Library.TryGet(key, out _))
            {
                LastCustomNodeKey = key;

                return new CustomNodeInterface(
                    key.Package,
                    key.Name,
                    "A node made by collapsing a selection.",
                    NodeCategories.Custom);
            }

            _ = customs;
        }
    }

    /// <summary>
    /// Freezes or unfreezes the selected nodes, and every node in a group any of them is in
    /// (<c>E7-T14</c>).
    /// </summary>
    /// <param name="selection">The selected canvas slots.</param>
    /// <param name="frozen">True to freeze, false to unfreeze.</param>
    /// <returns>How many nodes changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="selection"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>Selecting one node of a group freezes the group</b>, which is what the row pairs freeze
    /// with groups for. A group is the user's own statement that these nodes are one thing; leaving
    /// half of it running would produce a branch that is neither on nor off.
    /// </para>
    /// <para>
    /// <b>Whether it is a freeze or an unfreeze is decided by the caller, not inferred here.</b> A
    /// toggle over a mixed selection has no correct answer, and guessing produces a button whose
    /// effect a user cannot predict before pressing it.
    /// </para>
    /// </remarks>
    public int FreezeSelection(IReadOnlyCollection<int> selection, bool frozen)
    {
        ArgumentNullException.ThrowIfNull(selection);

        HashSet<NodeId> targets = [];

        foreach (int slot in selection)
        {
            if (slot < 0 || slot >= _graph.Nodes.Count)
            {
                continue;
            }

            NodeId id = _graph.Nodes[slot].Id;
            targets.Add(id);

            foreach (CanvasGroup group in _graph.Groups)
            {
                if (group.Members.Contains(id))
                {
                    foreach (NodeId member in group.Members)
                    {
                        targets.Add(member);
                    }
                }
            }
        }

        int changed = 0;

        foreach (NodeId id in targets)
        {
            if (_graph.Engine.TryGetNode(id, out _) && _graph.Engine.SetFrozen(id, frozen))
            {
                changed++;
            }
        }

        if (changed > 0)
        {
            RequestRun();
        }

        return changed;
    }

    /// <summary>
    /// Whether every node the selection would reach is already frozen (<c>E7-T14</c>).
    /// </summary>
    /// <param name="selection">The selected canvas slots.</param>
    /// <returns>True when the gesture should offer to unfreeze rather than to freeze.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="selection"/> is null.</exception>
    /// <remarks>
    /// <b>Every, not any.</b> A mixed selection offers <i>freeze</i>, so pressing the button twice
    /// always ends with everything frozen and then everything thawed — which is predictable,
    /// where a majority rule is not.
    /// </remarks>
    public bool SelectionIsFrozen(IReadOnlyCollection<int> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        bool any = false;

        foreach (int slot in selection)
        {
            if (slot < 0 || slot >= _graph.Nodes.Count)
            {
                continue;
            }

            if (!_graph.Engine.TryGetNode(_graph.Nodes[slot].Id, out NodeInstance? node)
                || node is null
                || !node.IsFrozen)
            {
                return false;
            }

            any = true;
        }

        return any;
    }

    /// <summary>
    /// Replaces a selection with a single custom node that does what it did (<c>E7-T12</c>).
    /// </summary>
    /// <param name="selection">The selected canvas slots.</param>
    /// <param name="reason">Why nothing happened, when the answer is null.</param>
    /// <returns>The new node's slot, or null when the selection cannot be collapsed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="selection"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>Everything that can refuse happens before anything is removed.</b> Planning changes
    /// nothing, and compiling is what discovers recursion, so a selection that cannot become a node
    /// leaves the graph exactly as it was rather than half-collapsed.
    /// </para>
    /// <para>
    /// <b>It lives here rather than on the canvas because the canvas is a view.</b> Inferring an
    /// interface and building a definition are engine work, and
    /// <c>Spark.Architecture.Tests</c> forbids a view file from naming <c>Spark.Engine</c> at all.
    /// The canvas keeps the selection and is told the answer.
    /// </para>
    /// </remarks>
    public int? CollapseSelection(IReadOnlyCollection<int> selection, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(selection);

        reason = null;

        if (selection.Count == 0)
        {
            reason = "Select the nodes to collapse first.";
            return null;
        }

        CustomNodeInterface identity = NextCustomNodeIdentity();

        CollapsePlan? plan = CanvasCollapse.Plan(_graph, selection, identity);
        if (plan is null)
        {
            reason = "Nothing outside the selection reads it, so the new node would have no "
                + "outputs and could never be wired to anything.";
            return null;
        }

        NodeDefinition definition;
        try
        {
            CustomNodeLibrary customs = CustomNodes();
            customs.Register(plan.Definition);
            customs.Build();
            definition = customs.Definition(identity.Key);
        }
        catch (Exception failure) when (failure is CustomNodeRecursionException or SparkFileException)
        {
            reason = failure.Message;
            return null;
        }

        int slot = CanvasCollapse.Apply(_graph, plan, definition);
        PublishCustomNode(definition);
        return slot;
    }

    /// <summary>Adds a freshly built custom node to the library panel.</summary>
    /// <param name="definition">The definition.</param>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
    public void PublishCustomNode(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        LibraryEntryViewModel entry = new(definition);
        LibraryEntries.Add(entry);

        // The tree is derived, so it has to be told. A node the user has just built is one they
        // are about to look for.
        RebuildLibraryGroups(expand: !string.IsNullOrWhiteSpace(LibrarySearch));

        // The help library, if one has been built, gets a page for it too - a node a user just made
        // is exactly the one they are most likely to press F1 on.
        _help?.Add(NodeReference.For(definition));
    }

    /// <summary>Rebuilds the inspector for the current canvas selection.</summary>
    /// <param name="selection">The selected slots.</param>
    public void ShowSelection(IReadOnlyCollection<int> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        Inspector.Clear();
        OnPropertyChanged(nameof(HasInputs));
        SelectedNote = null;
        SelectedGroup = null;
        WatchText = string.Empty;
        WatchRank = string.Empty;
        SelectedCodeBlock = null;
        ScriptText = string.Empty;
        CanSetLacing = false;
        LacingNote = string.Empty;
        _lacingSlot = -1;

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

        // Pushed rather than bound, and guarded, because assigning the property is indistinguishable
        // from a user choosing that value in the dropdown.
        _lacingSlot = slot;
        _showingLacing = true;
        SelectedLacing = LacingNames.Of(instance.Lacing);
        _showingLacing = false;
        CanSetLacing = true;
        LacingNote = DescribeLacing(instance);

        if (ScriptOf(node) is not null)
        {
            ShowCodeBlock(node);
        }

        WatchRank = node.ResultSummary is null ? string.Empty : CanvasGraph.RankLine(node);
        WatchText = _lastResult is null ? string.Empty : CanvasGraph.Expand(_lastResult.Value(node.Id));

        // Only a code block's ports offer a type dropdown (`E6-T11`). Every other node's port type
        // comes from the method it was imported from, and is not the user's to change.
        bool isCodeBlock = instance.Definition.Script is not null;
        IReadOnlyDictionary<string, Type> declared = _graph.Engine.DeclaredInputTypes(node.Id);

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
                CommitLiteral,
                isCodeBlock ? DeclareInputType : null,
                declared.TryGetValue(port.Name, out Type? declaredType) ? declaredType : null));
        }

        OnPropertyChanged(nameof(HasInputs));
    }

    /// <summary>
    /// Whether the selected node has any input ports, which is what shows the INPUTS heading.
    /// </summary>
    /// <remarks>
    /// A property rather than a binding onto <c>Inspector.Count</c>, because a compiled binding
    /// will not turn an <c>int</c> into an <c>IsVisible</c> and a heading over an empty list is a
    /// row that says nothing. Raised by hand wherever <see cref="Inspector"/> is refilled - the
    /// collection's own notifications are about its items, not about whether it has any.
    /// </remarks>
    public bool HasInputs => Inspector.Count > 0;

    /// <summary>How often <see cref="RunMode.Periodic"/> runs the graph.</summary>
    /// <remarks>
    /// One second, and not configurable in this slice. Faster than that is a graph running while
    /// the previous run is still applying — <see cref="EvaluateAsync"/> supersedes rather than
    /// queues, so nothing breaks, but a period shorter than the run is a period that means nothing.
    /// Slower belongs to whoever asks for it with a reason.
    /// </remarks>
    public static TimeSpan PeriodicRunInterval { get; } = TimeSpan.FromSeconds(1);

    /// <summary>The three run modes, in the words the ribbon writes them.</summary>
    public IReadOnlyList<string> RunModeChoices { get; } = RunModeNames.All;

    /// <summary>When the graph runs (<c>E3-T13</c>).</summary>
    public RunMode RunMode => _runMode;

    /// <summary>
    /// Runs the graph if the current mode says an edit is a reason to (<c>E3-T13</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every edit path calls this; only the Run button and F5 call
    /// <see cref="EvaluateAsync"/>.</b> That split is the whole of the feature — an explicit run
    /// means run, and everything else asks the mode. Before this, twelve call sites started a run
    /// unconditionally and there was nowhere to put the decision.
    /// </para>
    /// <para>
    /// Under <see cref="RunMode.Manual"/> the edit is still recorded and the graph is still marked
    /// dirty; <see cref="HasPendingRun"/> is what the status bar reads, because <b>a graph that
    /// quietly stops updating is the most confusing thing an editor can do</b>.
    /// </para>
    /// </remarks>
    public void RequestRun()
    {
        if (_runMode == RunMode.Automatic)
        {
            _ = EvaluateGraphAsync();
            return;
        }

        HasPendingRun = true;

        if (_runMode == RunMode.Manual)
        {
            StatusText = "Edited. The run mode is Manual, so press Run (F5) to see the result.";
        }
    }

    /// <summary>Applies a run mode chosen in the ribbon.</summary>
    /// <param name="value">The chosen mode's name.</param>
    partial void OnSelectedRunModeChanged(string value)
    {
        if (!RunModeNames.TryParse(value, out RunMode mode) || mode == _runMode)
        {
            return;
        }

        _runMode = mode;
        OnPropertyChanged(nameof(RunMode));

        if (mode == RunMode.Periodic)
        {
            StartPeriodic();
        }
        else
        {
            _periodic?.Stop();
        }

        // Switching back to Automatic settles the debt the other two modes ran up. Leaving a
        // graph stale after the user has just asked for it to keep itself fresh would be the
        // opposite of what they said.
        if (mode == RunMode.Automatic && HasPendingRun)
        {
            HasPendingRun = false;
            _ = EvaluateAsync();
        }
    }

    /// <summary>
    /// Starts the periodic timer, building it the first time it is needed.
    /// </summary>
    /// <remarks>
    /// <b>Lazily, because a <c>DispatcherTimer</c> wants a dispatcher and the view model must not.</b>
    /// It is constructed in tests, in <c>spark run</c> and in an embedder that may have no UI loop
    /// at all; building a timer in the constructor would make all three depend on something only
    /// the desktop application has. Nothing touches it until somebody chooses Periodic.
    /// </remarks>
    private void StartPeriodic()
    {
        if (_periodic is null)
        {
            _periodic = new Avalonia.Threading.DispatcherTimer { Interval = PeriodicRunInterval };
            _periodic.Tick += (_, _) => _ = EvaluateAsync();
        }

        _periodic.Start();
    }

    /// <summary>
    /// The five lacing modes, in the words the panel writes them.
    /// </summary>
    /// <remarks>
    /// <b>All five, including <c>Auto</c>.</b> Auto is not a replication algorithm - it means "use
    /// whatever this node's author chose" - and leaving it out would make going back to the
    /// author's answer impossible once a mode had been picked. <see cref="LacingNote"/> is what
    /// says which algorithm Auto lands on for the selected node, because two nodes both on Auto can
    /// lace differently and that is the one thing about it a user has to be told.
    /// </remarks>
    public IReadOnlyList<string> LacingChoices { get; } = LacingNames.All;

    /// <summary>
    /// Applies a lacing chosen in the properties pane, records it, and re-runs (<c>E8-T31</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lacing could not be changed anywhere before this.</b> <c>Graph.SetLacing</c> has existed
    /// since <c>E4-T6</c>, the mode round-trips through the file, the cache keys on it and the
    /// replicator obeys it - and nothing in the shell ever called it. The panel printed
    /// <c>Lacing: Longest</c> as a sentence, which told a user about a setting and then offered no
    /// way to reach it.
    /// </para>
    /// <para>
    /// It re-runs, because lacing is in the cache key: the node and everything downstream of it
    /// produce different answers, and a control whose effect only shows up on the next unrelated
    /// edit is a control nobody trusts.
    /// </para>
    /// </remarks>
    /// <param name="value">The chosen mode's name.</param>
    partial void OnSelectedLacingChanged(string value)
    {
        if (_showingLacing || !CanSetLacing || _lacingSlot < 0 || _lacingSlot >= _graph.Nodes.Count)
        {
            return;
        }

        if (!LacingNames.TryParse(value, out LacingMode mode))
        {
            return;
        }

        NodeId id = _graph.Nodes[_lacingSlot].Id;

        if (_graph.Engine.Node(id).Lacing == mode)
        {
            return;
        }

        _graph.Engine.SetLacing(id, mode);
        RecordEdit("Set lacing");
        LacingNote = DescribeLacing(_graph.Engine.Node(id));
        RequestRun();
    }

    /// <summary>
    /// The line under the lacing selector: what <c>Auto</c> resolves to here, or nothing.
    /// </summary>
    /// <remarks>
    /// Shown only for <c>Auto</c>, and only because Auto is the one mode whose behaviour is not in
    /// its own name. Repeating "Longest means longest" under a selector reading <i>Longest</i>
    /// would be noise.
    /// </remarks>
    /// <param name="instance">The selected node.</param>
    /// <returns>The note, or an empty string.</returns>
    private static string DescribeLacing(NodeInstance instance) =>
        instance.Lacing == LacingMode.Auto
            ? "Auto uses this node's own default, which is " + LacingNames.Of(instance.EffectiveLacing) + "."
            : string.Empty;

    /// <summary>
    /// Declares the type of a code block's input port, and re-runs the graph (<c>E6-T11</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Rebuilding the block moves its slot</b>, exactly as editing its source does, so the panel
    /// is rebuilt from the node's identity rather than from the slot the row was built against.
    /// Without that, choosing a type on the second of two code blocks would repaint the panel for
    /// whichever node had landed in that slot.
    /// </para>
    /// <para>
    /// The re-run is the point of the whole thing arriving on screen: the port's type changes on
    /// the canvas, and the editor starts completing against something real instead of
    /// <c>object</c>.
    /// </para>
    /// </remarks>
    /// <param name="editor">The row the choice was made on.</param>
    /// <param name="type">The chosen type, or null to go back to the wire.</param>
    private void DeclareInputType(PortLiteralViewModel editor, Type? type)
    {
        if (editor.Slot < 0 || editor.Slot >= _graph.Nodes.Count)
        {
            return;
        }

        NodeId id = _graph.Nodes[editor.Slot].Id;

        if (!_graph.SetDeclaredInputType(editor.Slot, editor.Name, type))
        {
            return;
        }

        RecordEdit("Declare input type");

        int rebuilt = _graph.SlotOf(id);
        if (rebuilt >= 0)
        {
            ShowSelection([rebuilt]);
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
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns every pane to its default size and makes them all visible.</summary>
    [RelayCommand]
    public void ResetLayout() => ApplyWorkspace("Default");

    /// <inheritdoc/>
    public void Dispose()
    {
        _periodic?.Stop();

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

    partial void OnLibrarySearchChanged(string value)
    {
        Rank(value, LibraryEntries, limit: int.MaxValue);

        // Open the groups when there is a query and close them when there is not. A user who has
        // typed has already said what they want; one who has cleared the box is back to browsing.
        RebuildLibraryGroups(expand: !string.IsNullOrWhiteSpace(value));
    }

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

    /// <summary>
    /// Rebuilds <see cref="LibraryGroups"/> from <see cref="LibraryEntries"/> (<c>E8-T24</c>).
    /// </summary>
    /// <param name="expand">Whether to open every group, which a search does and a clear does not.</param>
    /// <remarks>
    /// <para>
    /// <b>Categories are sorted, with <c>Custom</c> last.</b> Alphabetical is the order somebody can
    /// predict without being told it — but <c>Custom</c> is the catch-all every unrecognised
    /// category falls into, and sorting it between <c>Curve</c> and <c>Display</c> puts "everything
    /// else" in the middle of the specific things. It goes at the bottom, where a catch-all reads
    /// as one.
    /// </para>
    /// <para>
    /// <b>Entries keep library order inside a group</b> rather than being sorted again. With a
    /// query that order is the search ranking, and re-sorting it alphabetically would throw away
    /// the one thing the search knew.
    /// </para>
    /// </remarks>
    private void RebuildLibraryGroups(bool expand)
    {
        Dictionary<string, List<LibraryEntryViewModel>> byCategory = new(StringComparer.Ordinal);
        List<string> order = [];

        foreach (LibraryEntryViewModel entry in LibraryEntries)
        {
            string category = string.IsNullOrWhiteSpace(entry.Category)
                ? NodeCategories.Custom
                : entry.Category;

            if (!byCategory.TryGetValue(category, out List<LibraryEntryViewModel>? entries))
            {
                entries = [];
                byCategory[category] = entries;
                order.Add(category);
            }

            entries.Add(entry);
        }

        order.Sort(static (left, right) =>
        {
            bool leftCustom = string.Equals(left, NodeCategories.Custom, StringComparison.Ordinal);
            bool rightCustom = string.Equals(right, NodeCategories.Custom, StringComparison.Ordinal);

            return leftCustom == rightCustom
                ? string.CompareOrdinal(left, right)
                : leftCustom ? 1 : -1;
        });

        LibraryGroups.Clear();

        foreach (string category in order)
        {
            LibraryGroups.Add(new LibraryGroupViewModel(category, byCategory[category])
            {
                IsExpanded = expand,
            });
        }
    }

    private int _lastRenderableCount;
    private readonly ScriptTrustStore _trust = new();

    private void AdoptGraph(CanvasGraph graph, bool evaluate = true, bool resetHistory = true)
    {
        _graph = graph;

        // `E6-T6`: the canvas re-types a code block as it is wired up, and it needs the factory to
        // do it. Null when scripting is off, which is exactly when it must not reach for Roslyn.
        graph.Scripts = _session.Scripts;

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
            RequestRun();
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
            AdoptGraph(CanvasDocument.Open(snapshot, _session.Library, _session.Scripts), evaluate: true, resetHistory: false);
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
        RequestRun();
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
            OnPropertyChanged(nameof(HasInputs));
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
        if (node.Message is { } message)
        {
            builder.Append("\n\n").Append(message);
        }

        if (node.ResultSummary is { } summary)
        {
            // The same two lines the preview bubble shows, in the same order and the same words.
            builder.Append("\n\nOutput: ").Append(CanvasGraph.RankLine(node));
            builder.Append('\n').Append(summary);
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
