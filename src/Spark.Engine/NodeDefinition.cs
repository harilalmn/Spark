using System;
using System.Collections.Generic;
using System.Linq;
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
        string? category = null)
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

    /// <inheritdoc/>
    public override string ToString() => Key.Value;
}
