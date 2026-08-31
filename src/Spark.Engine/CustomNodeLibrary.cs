using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// Thrown when a custom node contains itself, directly or through others (<c>E7-T13</c>).
/// </summary>
/// <remarks>
/// The containment path is carried rather than just the fact, because <i>A contains B contains C
/// contains A</i> is the difference between a bug report somebody can act on and one that says
/// "recursion detected".
/// </remarks>
public sealed class CustomNodeRecursionException : InvalidOperationException
{
    /// <summary>Creates the exception from a containment path.</summary>
    /// <param name="path">The chain of keys, starting and ending with the same one.</param>
    public CustomNodeRecursionException(IReadOnlyList<NodeKey> path)
        : base(Describe(path))
    {
        Path = path ?? [];
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public CustomNodeRecursionException(string message) : base(message) => Path = [];

    /// <summary>Creates the exception with a message and a cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public CustomNodeRecursionException(string message, Exception innerException)
        : base(message, innerException) => Path = [];

    /// <summary>Creates the exception with no message.</summary>
    public CustomNodeRecursionException()
        : base("A custom node contains itself.") => Path = [];

    /// <summary>The containment path, first key to repeated key.</summary>
    public IReadOnlyList<NodeKey> Path { get; }

    private static string Describe(IReadOnlyList<NodeKey> path)
    {
        if (path is null || path.Count == 0)
        {
            return "A custom node contains itself.";
        }

        StringBuilder text = new("A custom node contains itself: ");
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0)
            {
                text.Append(" contains ");
            }

            text.Append('\'').Append(path[i].Value).Append('\'');
        }

        text.Append(". A graph cannot be its own definition, because evaluating it would never finish.");
        return text.ToString();
    }
}

/// <summary>
/// Turns <c>.sparkcustom</c> documents into ordinary <see cref="NodeDefinition"/>s that a graph
/// can hold and an evaluator can run (<c>E7-T11</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A custom node is a node, not a new kind of thing.</b> What comes out of here is a plain
/// <see cref="NodeDefinition"/> whose invocation happens to evaluate a graph. Nothing downstream —
/// the replicator, the cache, the canvas, the file writer — needs to know, which is what makes
/// <i>graph-in-graph the same mechanism rather than a separate feature</i>.
/// </para>
/// <para>
/// <b>Recursion is refused when the definition is built, and the containment path is reported.</b>
/// That is earlier than it strictly has to be, and deliberately so: refusing at evaluation time
/// would mean a graph that opens, looks fine, and hangs the first time somebody presses run.
/// </para>
/// <para>
/// <b>The inner graph is built once and reused under a lock.</b> Rebuilding it per invocation
/// would be simpler to reason about and unusably slow — a custom node replicated over a thousand
/// items would restore a thousand graphs. The lock is the cost: one custom node's body evaluates
/// one call at a time, even when the outer graph is running in parallel. That is a real ceiling
/// and it is written down here rather than discovered in a profile.
/// </para>
/// </remarks>
public sealed class CustomNodeLibrary
{
    private readonly Dictionary<NodeKey, CustomNodeDocument> _documents = [];
    private readonly NodeLibrary _host;

    /// <summary>Creates a custom node library over the library its bodies resolve against.</summary>
    /// <param name="host">
    /// The library holding every built-in node, and into which finished custom node definitions
    /// are added so that one custom node can use another.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is null.</exception>
    public CustomNodeLibrary(NodeLibrary host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        CustomNodePorts.AddTo(_host);
    }

    /// <summary>The definition built for a key.</summary>
    /// <param name="key">The custom node's key.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="KeyNotFoundException">
    /// Nothing has been built under that key. Register the document and call
    /// <see cref="Build"/> first.
    /// </exception>
    public NodeDefinition Definition(NodeKey key) => _host.Get(key);

    /// <summary>The keys of every custom node registered here.</summary>
    public IReadOnlyCollection<NodeKey> Keys => _documents.Keys;

    /// <summary>
    /// Registers a custom node's document without building it. Register everything, then
    /// <see cref="Build"/>, so that nodes which use each other resolve regardless of order.
    /// </summary>
    /// <param name="document">The custom node.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public void Register(CustomNodeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _documents[document.Interface.Key] = document;
    }

    /// <summary>
    /// Builds every registered custom node into the host library, innermost first.
    /// </summary>
    /// <exception cref="CustomNodeRecursionException">
    /// A custom node contains itself. The message names the containment path.
    /// </exception>
    /// <remarks>
    /// Order matters and is worked out rather than required of the caller: a node whose body uses
    /// another custom node needs that one's definition to exist first, and asking a user to
    /// register in dependency order would be asking them to do a topological sort by hand.
    /// </remarks>
    public void Build()
    {
        foreach (NodeKey key in _documents.Keys.OrderBy(k => k.Value, StringComparer.Ordinal))
        {
            BuildOne(key, []);
        }
    }

    private NodeDefinition BuildOne(NodeKey key, List<NodeKey> path)
    {
        if (path.Contains(key))
        {
            List<NodeKey> cycle = [.. path[path.IndexOf(key)..], key];
            throw new CustomNodeRecursionException(cycle);
        }

        if (_host.TryGet(key, out NodeDefinition? existing) && existing is not null && _built.Contains(key))
        {
            return existing;
        }

        CustomNodeDocument document = _documents[key];
        path.Add(key);

        // Depth first: every custom node this body uses has to exist before the body can be
        // restored, and walking them here is also what discovers indirect recursion.
        foreach (GraphDocumentNode node in document.Body.Nodes)
        {
            if (_documents.ContainsKey(node.Key) && !_built.Contains(node.Key))
            {
                BuildOne(node.Key, path);
            }
            else if (_documents.ContainsKey(node.Key) && path.Contains(node.Key))
            {
                throw new CustomNodeRecursionException([.. path[path.IndexOf(node.Key)..], node.Key]);
            }
        }

        path.RemoveAt(path.Count - 1);

        NodeDefinition definition = Compile(document);
        _host.Add(definition);
        _built.Add(key);
        return definition;
    }

    private readonly HashSet<NodeKey> _built = [];

    private NodeDefinition Compile(CustomNodeDocument document)
    {
        IReadOnlyList<(NodeId Id, string Name)> inputs =
            CustomNodePorts.Collect(document.Body, CustomNodePorts.InputKey);
        IReadOnlyList<(NodeId Id, string Name)> outputs =
            CustomNodePorts.Collect(document.Body, CustomNodePorts.OutputKey);

        if (outputs.Count == 0)
        {
            throw new SparkFileException(new SparkDiagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCodes.MalformedGraphFile,
                $"The custom node '{document.Interface.Key}' has no Output node, so it produces nothing.",
                detail: "Place an Output node inside the definition and wire the result into it. "
                    + "A node with no outputs cannot be wired to anything downstream, which means "
                    + "nothing could ever use it.",
                helpTopicId: DiagnosticCodes.FileTopic));
        }

        List<PortDefinition> inputPorts =
        [
            .. inputs.Select(port => new PortDefinition(
                port.Name, typeof(object), 0, keepStructure: true)),
        ];
        List<PortDefinition> outputPorts =
        [
            .. outputs.Select(port => new PortDefinition(
                port.Name, typeof(object), 0, keepStructure: true)),
        ];

        Graph body = document.Body.Restore(_host);
        object gate = new();

        object?[] Run(object?[] arguments, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                for (int index = 0; index < inputs.Count; index++)
                {
                    body.SetLiteral(
                        inputs[index].Id,
                        CustomNodePorts.ValuePort,
                        index < arguments.Length ? arguments[index] : null);
                }

                // A fresh cache each call. The body's nodes are keyed by their own identity, and
                // two invocations with different arguments must not see each other's results --
                // which is exactly what a shared cache across calls would arrange.
                EvaluationContext context = new(
                    Geometry.Tolerance.Default, new SequentialEvaluationScheduler(), new EvaluationCache(), 0);

                EvaluationResult result = GraphEvaluator.Evaluate(body, context, cancellationToken);

                if (result.HasErrors)
                {
                    SparkDiagnostic first = result.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
                    throw new InvalidOperationException(
                        $"Inside '{document.Interface.Key}': {first.Message}");
                }

                object?[] produced = new object?[outputs.Count];
                for (int index = 0; index < outputs.Count; index++)
                {
                    produced[index] = result.Value(outputs[index].Id);
                }

                return produced;
            }
        }

        return NodeDefinition.FromNestedGraph(
            document.Interface.Key,
            document.Interface.Name,
            inputPorts,
            outputPorts,
            Run,
            document.Interface.Description,
            document.Interface.Category);
    }
}
