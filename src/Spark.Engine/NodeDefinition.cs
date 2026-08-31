using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// Invokes the underlying member once, with arguments already marshalled into their declared CLR
/// types, and returns one value per output port.
/// </summary>
/// <remarks>
/// This is always an expression-tree-compiled delegate, never a wrapper around
/// <c>MethodInfo.Invoke</c>. Under replication over a hundred thousand items the reflection path is
/// fifty to a hundred times slower, which does not make lacing slow — it makes it unusable. See
/// <see cref="NodeInvoker"/>.
/// </remarks>
/// <param name="arguments">One argument per input port, in port order.</param>
/// <returns>One value per output port, in port order.</returns>
public delegate object?[] NodeInvocation(object?[] arguments);

/// <summary>
/// What a node <i>is</i>, as opposed to a node instance on a canvas: its identity, its ports, the
/// lacing its author chose, and the compiled delegate that runs it.
/// </summary>
/// <remarks>
/// One definition backs any number of <see cref="NodeInstance"/> objects. Definitions are immutable
/// and safe to share across graphs and threads.
/// </remarks>
public sealed class NodeDefinition
{
    /// <summary>Creates a definition.</summary>
    /// <param name="key">The definition's stable identity, including package.</param>
    /// <param name="displayName">The name shown on the canvas and in the library.</param>
    /// <param name="inputs">The input ports, in port order.</param>
    /// <param name="outputs">The output ports, in port order. A node must have at least one.</param>
    /// <param name="invoke">The compiled invoker. See <see cref="NodeInvoker"/>.</param>
    /// <param name="defaultLacing">
    /// What <see cref="LacingMode.Auto"/> resolves to on an instance of this node. May not itself
    /// be <see cref="LacingMode.Auto"/>: there is exactly one hop, never a chain.
    /// </param>
    /// <param name="version">
    /// The definition's version. It is mixed into every cache key, so bumping it invalidates every
    /// cached result computed by the old implementation.
    /// </param>
    /// <param name="isSideEffect">
    /// Whether the node depends on or changes something outside the graph. An impure node mixes the
    /// run epoch into its cache key and poisons the keys of everything downstream.
    /// </param>
    /// <param name="description">One paragraph describing the node. Optional.</param>
    /// <param name="showsValue">Whether the canvas shows this node's value permanently.</param>
    /// <param name="hasSlider">
    /// Whether the node is driven by a slider drawn on the node itself. See
    /// <see cref="Spark.Api.NodeSliderAttribute"/> for the shape this promises.
    /// </param>
    /// <param name="category">
    /// The library category the node is filed under, which is what decides its header colour on the
    /// canvas. Defaults to <see cref="NodeCategories.Custom"/>. A plain string rather than an enum,
    /// because a third-party package must be able to file its nodes under a name Spark has never
    /// heard of and still get a legible node.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// There are no output ports, or <paramref name="defaultLacing"/> is <see cref="LacingMode.Auto"/>.
    /// </exception>
    public NodeDefinition(
        NodeKey key,
        string displayName,
        IReadOnlyList<PortDefinition> inputs,
        IReadOnlyList<PortDefinition> outputs,
        NodeInvocation invoke,
        LacingMode defaultLacing = LacingMode.Longest,
        int version = 1,
        bool isSideEffect = false,
        string? description = null,
        string? category = null,
        bool showsValue = false,
        bool hasSlider = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(invoke);

        if (outputs.Count == 0)
        {
            throw new ArgumentException(
                $"Node '{displayName}' has no output ports. A node that produces nothing is a side effect, and side effects still carry an output port so that downstream ordering is expressible.",
                nameof(outputs));
        }

        // Decision D4. Auto is a sentinel meaning "use the definition's default", so a definition
        // whose default is itself Auto would be a resolution loop. The importer refuses it too,
        // rather than following the chain, because one hop is a rule and n hops is a puzzle.
        if (defaultLacing == LacingMode.Auto)
        {
            throw new ArgumentException(
                $"Node '{displayName}' declares Auto as its default lacing. Auto means 'use the definition's default', so it cannot be the default. Declare one of Shortest, Longest, CrossProduct or Disabled.",
                nameof(defaultLacing));
        }

        Key = key;
        DisplayName = displayName;
        Inputs = [.. inputs];
        Outputs = [.. outputs];
        Invoke = invoke;
        DefaultLacing = defaultLacing;
        Version = version;
        IsSideEffect = isSideEffect;
        Description = description;
        Category = string.IsNullOrWhiteSpace(category) ? NodeCategories.Custom : category;
        ShowsValue = showsValue;
        HasSlider = hasSlider;
    }

    /// <summary>The definition's stable identity, including the package that published it.</summary>
    public NodeKey Key { get; }

    /// <summary>The name shown on the canvas and in the library.</summary>
    public string DisplayName { get; }

    /// <summary>One paragraph describing the node, or <see langword="null"/>.</summary>
    public string? Description { get; }

    /// <summary>
    /// The library category, one of <see cref="NodeCategories"/> or a name a package invented.
    /// Never blank.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Whether the canvas should show this node's value permanently rather than only when it is
    /// hovered or selected — a watch node.
    /// </summary>
    /// <remarks>
    /// A fact the engine carries and never reads, exactly like <see cref="Category"/> and like a
    /// node's canvas coordinates. It is here rather than in the shell because the canvas has no
    /// library and must not name an engine type
    /// ([ADR-0005](../../docs/adr/0005-api-engine-host-layering.md)).
    /// </remarks>
    public bool ShowsValue { get; }

    /// <summary>
    /// Whether the node is driven by a slider drawn on the node itself (<c>E8-T25</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Carried for the same reason <see cref="ShowsValue"/> and <see cref="Category"/> are: a fact
    /// the engine holds and never reads, because the canvas has no library and must not name an
    /// engine type ([ADR-0005](../../docs/adr/0005-api-engine-host-layering.md)).
    /// </para>
    /// <para>
    /// The shape it promises — value, minimum, maximum, step, in that order — is
    /// <see cref="Spark.Api.NodeSliderAttribute"/>'s contract, not this flag's. A definition
    /// carrying this with the wrong shape draws no slider rather than a misleading one.
    /// </para>
    /// </remarks>
    public bool HasSlider { get; }

    /// <summary>
    /// The source this definition was built from, or <see langword="null"/> when it came from a
    /// library.
    /// </summary>
    /// <remarks>
    /// Carried so that a code block can be saved: its ports depend on what the user typed, so the
    /// file has to hold the text and rebuild the definition on open. Everything else in this type
    /// is derived from that text, which is why the text and not the derivation is what persists.
    /// </remarks>
    public string? Script { get; private init; }

    /// <summary>The input ports, in port order.</summary>
    public IReadOnlyList<PortDefinition> Inputs { get; }

    /// <summary>The output ports, in port order.</summary>
    public IReadOnlyList<PortDefinition> Outputs { get; }

    /// <summary>What <see cref="LacingMode.Auto"/> resolves to on an instance of this node.</summary>
    public LacingMode DefaultLacing { get; }

    /// <summary>
    /// The definition's version, mixed into every cache key so that changing the implementation
    /// invalidates results computed by the old one.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Whether the node depends on or changes something outside the graph, and therefore cannot be
    /// cached across runs.
    /// </summary>
    public bool IsSideEffect { get; }

    /// <summary>The compiled invoker.</summary>
    public NodeInvocation Invoke { get; }

    /// <summary>
    /// The invoker for a code block, which takes the evaluation's cancellation token — null for
    /// every node that came from a library.
    /// </summary>
    /// <remarks>
    /// Set only by <see cref="FromScript"/>. It exists because a code block is the one node whose
    /// body a user wrote by hand and can therefore fail to terminate; see
    /// <see cref="ScriptInvocation"/> for why the token stops here rather than reaching
    /// <see cref="NodeInvocation"/> as well.
    /// </remarks>
    public ScriptInvocation? InvokeScript { get; private init; }

    /// <summary>Runs the node once, honouring cancellation if the node is able to.</summary>
    /// <param name="arguments">One argument per input port, in port order.</param>
    /// <param name="cancellationToken">The evaluation's token.</param>
    /// <returns>One value per output port, in port order.</returns>
    /// <remarks>
    /// <b>Call this rather than <see cref="Invoke"/>.</b> For a library node the two are the same
    /// call; for a code block <see cref="Invoke"/> silently drops the token, which is exactly the
    /// bug `E6-T17` exists to prevent. The token is passed on rather than checked here, because a
    /// script that has already started is stopped from inside — by the guard weaver's checks — and
    /// not by anything this method could do after the fact.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public object?[] Call(object?[] arguments, CancellationToken cancellationToken) =>
        InvokeScript is { } script ? script(arguments, cancellationToken) : Invoke(arguments);

    /// <summary>
    /// Resolves an instance's lacing to a real replication algorithm. This is the one hop, and it
    /// is the whole of what <see cref="LacingMode.Auto"/> means.
    /// </summary>
    /// <param name="instanceLacing">The lacing stored on the node instance.</param>
    /// <returns>One of Shortest, Longest, CrossProduct or Disabled — never Auto.</returns>
    public LacingMode ResolveLacing(LacingMode instanceLacing) =>
        instanceLacing == LacingMode.Auto ? DefaultLacing : instanceLacing;

    /// <summary>
    /// The ports that take part in Cross Product dimension ordering, ordered by
    /// <see cref="PortDefinition.ReplicationGuide"/> and then by port index.
    /// </summary>
    /// <param name="replicatingPorts">The indices of the ports that are actually replicating.</param>
    /// <returns>The same indices, in dimension order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="replicatingPorts"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<int> OrderDimensions(IReadOnlyList<int> replicatingPorts)
    {
        ArgumentNullException.ThrowIfNull(replicatingPorts);

        return [.. replicatingPorts.OrderBy(index => Inputs[index].ReplicationGuide ?? index).ThenBy(index => index)];
    }

    /// <summary>
    /// Builds a definition whose invocation evaluates a nested graph — a custom node
    /// (<c>E7-T11</c>).
    /// </summary>
    /// <param name="key">The custom node's key.</param>
    /// <param name="displayName">What appears on the canvas.</param>
    /// <param name="inputs">The ports derived from the definition graph's Input nodes.</param>
    /// <param name="outputs">The ports derived from its Output nodes.</param>
    /// <param name="invoke">Runs the inner graph. Receives the cancellation token.</param>
    /// <param name="description">One sentence for the library and the tooltip.</param>
    /// <param name="category">The library category, or null for Custom.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="invoke"/> is null.</exception>
    /// <remarks>
    /// <b>This exists to give a custom node the cancellable path rather than the plain one.</b>
    /// <see cref="Invoke"/> takes no token, and a nested graph is precisely the kind of work a
    /// user cancels — it can contain a thousand nodes. Routing through
    /// <see cref="InvokeScript"/> means <see cref="Call"/> hands the token down, so cancelling the
    /// outer run stops the inner one between its own nodes. A custom node built on
    /// <see cref="Invoke"/> would swallow the token silently, which is the same defect
    /// <c>E6-T17</c> named for code blocks.
    /// </remarks>
    public static NodeDefinition FromNestedGraph(
        NodeKey key,
        string displayName,
        IReadOnlyList<PortDefinition> inputs,
        IReadOnlyList<PortDefinition> outputs,
        ScriptInvocation invoke,
        string? description = null,
        string? category = null)
    {
        ArgumentNullException.ThrowIfNull(invoke);

        return new NodeDefinition(
            key,
            displayName,
            inputs,
            outputs,
            arguments => invoke(arguments, CancellationToken.None),
            description: description,
            category: category)
        {
            InvokeScript = invoke,
        };
    }

    /// <summary>
    /// Builds a definition from what a script node factory worked out about a piece of source.
    /// </summary>
    /// <param name="source">What the factory inferred.</param>
    /// <param name="script">The source itself, kept so the node can be saved.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is null.</exception>
    /// <remarks>
    /// <b>The key carries a hash of the script</b> rather than being a fixed
    /// <c>Spark.Scripting/CodeBlock</c>. The evaluation cache keys on the definition's key, so two
    /// nodes with different code must not collide — and two nodes with the <i>same</i> code should,
    /// which is what makes ten copies of a snippet compile and evaluate once rather than ten times.
    /// </remarks>
    public static NodeDefinition FromScript(NodeDefinitionSource source, string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        return new NodeDefinition(
            new NodeKey(ScriptPackage, source.Name + "#" + source.ContentHash),
            source.Name,
            [.. source.Inputs.Select(port => new PortDefinition(
                port.Name, port.ValueType, PortDefinition.RankOfType(port.ValueType), port.Description))],
            [.. source.Outputs.Select(port => new PortDefinition(
                port.Name, port.ValueType, PortDefinition.RankOfType(port.ValueType), port.Description))],
            arguments => source.Invoke(arguments, CancellationToken.None),
            description: "A C# code block.",
            category: NodeCategories.Script)
        {
            Script = script,
            InvokeScript = source.Invoke,
        };
    }

    /// <summary>The package a code block's key belongs to.</summary>
    public const string ScriptPackage = "Spark.Scripting";

    /// <inheritdoc/>
    public override string ToString() => Key.Value;
}
