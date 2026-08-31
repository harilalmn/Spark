using System;
using System.Threading;
using System.Threading.Tasks;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Host;

/// <summary>
/// The composition root: the node library, the document graph, and the machinery that runs it.
/// </summary>
/// <remarks>
/// <para>
/// This is the type an embedder constructs. There is no UI in it and no Avalonia anywhere in its
/// reference graph, which is what makes a CAD add-in a matter of supplying a host-thread
/// <c>IEvaluationScheduler</c> rather than a port (ADR-0005).
/// </para>
/// <para>
/// <b>Mutation and evaluation are serialised by one gate.</b> Evaluation reads the whole graph on a
/// worker thread while the user goes on editing it, and a graph mutated mid-traversal produces a
/// crash inside the topological sort that looks nothing like the edit that caused it.
/// <see cref="Mutate{T}"/> cancels any run in flight before it takes the gate, so an edit does not
/// wait for a long evaluation to finish — it stops it.
/// </para>
/// </remarks>
public sealed class SparkSession : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _runs = new(1, 1);
    private CancellationTokenSource? _inFlight;
    private EvaluationContext _context;
    private bool _disposed;
    private Spark.Scripting.ScriptCompletion? _completion;

    /// <summary>Creates a session with the built-in node library imported.</summary>
    /// <param name="tolerance">The document tolerance, hashed into every cache key.</param>
    /// <param name="scheduler">
    /// Where node work runs. Defaults to the parallel scheduler, which is the desktop default; a
    /// CAD host supplies one that marshals onto its own thread.
    /// </param>
    public SparkSession(Tolerance tolerance = default, Spark.Api.IEvaluationScheduler? scheduler = null)
    {
        CoreNodes = NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly);
        Library = new NodeLibrary();
        Library.Add(CoreNodes);

        Graph = new Graph();
        _context = new EvaluationContext(tolerance, scheduler ?? new ParallelEvaluationScheduler());
    }

    /// <summary>
    /// How a code block's source becomes a node definition, or <see langword="null"/> when
    /// scripting is switched off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Created lazily, so a session that never opens a code block never loads Roslyn.</b> That
    /// is <c>E6-T14</c>'s requirement — <i>a graph with no script nodes must never load
    /// Spark.Scripting</i> — and the only place it can be honoured is here, because this is where
    /// the host decides what a document may contain.
    /// </para>
    /// <para>
    /// Setting it to null is what <c>--no-script</c> does. A graph containing a code block then
    /// refuses to open, naming the node, rather than opening with the node missing: a Spark graph
    /// is executable code, and quietly dropping the executable parts would be worse than refusing.
    /// </para>
    /// </remarks>
    public IScriptNodeFactory? Scripts { get; private set; }

    /// <summary>Turns scripting on, building the factory if it has not been built.</summary>
    /// <returns>The factory.</returns>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public IScriptNodeFactory EnableScripting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ScriptingAllowed)
        {
            throw new InvalidOperationException(
                "Scripting has been switched off for this session and cannot be switched back on.");
        }

        // The first touch of ScriptNodeFactory is the first touch of Roslyn, which is why this is
        // a method rather than a field initialiser.
        return Scripts ??= new Spark.Scripting.ScriptNodeFactory();
    }

    /// <summary>
    /// The completion service a code block's editor asks, built once and shared.
    /// </summary>
    /// <returns>The service, or null when scripting is off.</returns>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// <b>Built from the factory's own reference catalogue</b> (`E6-T13`), which is the whole of
    /// that row: a completion list assembled from a different set of references than the compile
    /// offers members of types the script cannot use and hides members of types it can, and the
    /// user believes it.
    /// </para>
    /// <para>
    /// <b>Lazy for the same reason <see cref="Scripts"/> is.</b> Roslyn's completion layer composes
    /// its host services through MEF on first use and is the most expensive thing in the
    /// application to touch; a session that never opens a code block never pays for it.
    /// </para>
    /// </remarks>
    public Spark.Scripting.ScriptCompletion? Completion()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Scripts is not Spark.Scripting.ScriptNodeFactory factory)
        {
            return null;
        }

        return _completion ??= new Spark.Scripting.ScriptCompletion(factory.References);
    }

    /// <summary>Turns scripting off — what <c>--no-script</c> means.</summary>
    /// <remarks>
    /// Once refused it stays refused: <see cref="EnableScripting"/> will not undo it. A switch that
    /// could be reversed by any code path that wanted to would not be a trust boundary.
    /// </remarks>
    public void DisableScripting()
    {
        _completion?.Dispose();
        _completion = null;
        Scripts = null;
        ScriptingAllowed = false;
    }

    /// <summary>
    /// Whether scripting may be turned on at all. False once <see cref="DisableScripting"/> has
    /// been called.
    /// </summary>
    public bool ScriptingAllowed { get; private set; } = true;

    /// <summary>
    /// The assemblies code blocks compile against, or <see langword="null"/> when scripting is
    /// off for this session (<c>E7-T9</c>).
    /// </summary>
    /// <returns>The catalogue, building the script factory if it has not been built.</returns>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <remarks>
    /// <b>Touching this loads Roslyn</b>, which is why nothing calls it to draw a list. Adding a
    /// reference is a deliberate act by a user who is about to write a code block; enumerating
    /// what they have added before is not, and reads the store instead. That is what keeps
    /// <c>E6-T14</c>'s promise — a graph with no script nodes never loads
    /// <c>Spark.Scripting</c> — true in a session that has a reference list.
    /// </remarks>
    public Spark.Scripting.ReferenceCatalog? ScriptReferences()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return ScriptingAllowed && EnableScripting() is Spark.Scripting.ScriptNodeFactory factory
            ? factory.References
            : null;
    }

    /// <summary>The definitions that can be placed.</summary>
    public NodeLibrary Library { get; }

    /// <summary>The import of <c>Spark.Nodes.Core</c>, kept so tests can assert its coverage.</summary>
    public ImportReport CoreNodes { get; }

    /// <summary>The document graph. Mutate it only through <see cref="Mutate{T}"/>.</summary>
    public Graph Graph { get; private set; }

    /// <summary>
    /// Swaps in a different document, cancelling any run in flight and starting a fresh cache
    /// epoch.
    /// </summary>
    /// <remarks>
    /// The cache is keyed by provenance rather than by document, so results computed for the old
    /// graph would still be served to an identically-shaped node in the new one. That is correct —
    /// the same inputs give the same answer — but it makes "open a document and watch it evaluate"
    /// indistinguishable from "open a document and watch nothing happen", so the epoch advances.
    /// </remarks>
    /// <param name="graph">The new document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    public void Replace(Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        CancelInFlight();

        lock (_gate)
        {
            Graph = graph;
            _context = _context.NextRun();
        }
    }

    /// <summary>The context the next run will use.</summary>
    public EvaluationContext Context
    {
        get
        {
            lock (_gate)
            {
                return _context;
            }
        }
    }

    /// <summary>
    /// Applies an edit to the graph, cancelling any run in flight first.
    /// </summary>
    /// <typeparam name="T">What the edit returns.</typeparam>
    /// <param name="edit">The edit.</param>
    /// <returns>Whatever <paramref name="edit"/> returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="edit"/> is <see langword="null"/>.</exception>
    public T Mutate<T>(Func<Graph, T> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        CancelInFlight();

        lock (_gate)
        {
            return edit(Graph);
        }
    }

    /// <summary>Applies an edit that returns nothing.</summary>
    /// <param name="edit">The edit.</param>
    /// <exception cref="ArgumentNullException"><paramref name="edit"/> is <see langword="null"/>.</exception>
    public void Mutate(Action<Graph> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        Mutate<object?>(graph =>
        {
            edit(graph);
            return null;
        });
    }

    /// <summary>Runs the graph on the calling thread.</summary>
    /// <param name="cancellationToken">Checked between nodes and between replication elements.</param>
    /// <returns>The outputs, states and diagnostics.</returns>
    public EvaluationResult Evaluate(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _context = _context.NextRun();
            return GraphEvaluator.Evaluate(Graph, _context, cancellationToken);
        }
    }

    /// <summary>
    /// Runs the graph off the calling thread, cancelling any run already in flight.
    /// </summary>
    /// <remarks>
    /// Evaluation never runs on the UI thread. The caller awaits this and marshals the result back
    /// itself; the session has no idea what a dispatcher is, which is the point.
    /// </remarks>
    /// <param name="cancellationToken">Cancels this run.</param>
    /// <returns>The result, or <see langword="null"/> when the run was superseded or cancelled.</returns>
    public async Task<EvaluationResult?> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        CancelInFlight();

        CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            _inFlight = linked;
        }

        await _runs.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            return await Task.Run(() => Evaluate(linked.Token), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _runs.Release();

            lock (_gate)
            {
                if (ReferenceEquals(_inFlight, linked))
                {
                    _inFlight = null;
                }
            }

            linked.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelInFlight();
        _completion?.Dispose();
        _runs.Dispose();
    }

    private void CancelInFlight()
    {
        CancellationTokenSource? running;
        lock (_gate)
        {
            running = _inFlight;
        }

        if (running is null)
        {
            return;
        }

        try
        {
            running.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run finished between the read and the cancel. Nothing to stop.
        }
    }
}
