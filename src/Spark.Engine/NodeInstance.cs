using System;
using System.Collections.Generic;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// One node on a canvas: an identity, the definition it is an instance of, the lacing the user
/// chose for it, and the literal values typed into its unwired input ports.
/// </summary>
/// <remarks>
/// Everything mutable about a node instance is mutated through the owning <see cref="Graph"/>, not
/// through the instance. That is not ceremony: changing lacing or a literal has to mark the node
/// and everything downstream of it dirty, and a setter on this type would let that be skipped.
/// </remarks>
public sealed class NodeInstance
{
    private readonly object?[] _literals;

    internal NodeInstance(NodeId id, NodeDefinition definition)
    {
        Id = id;
        Definition = definition;
        Lacing = LacingMode.Auto;

        _literals = new object?[definition.Inputs.Count];
        for (int index = 0; index < _literals.Length; index++)
        {
            _literals[index] = definition.Inputs[index].DefaultValue;
        }
    }

    /// <summary>This instance's identity. Stable for the life of the graph, and never reused.</summary>
    public NodeId Id { get; }

    /// <summary>The definition this is an instance of.</summary>
    public NodeDefinition Definition { get; }

    /// <summary>
    /// The lacing chosen for this instance. A freshly placed node carries
    /// <see cref="LacingMode.Auto"/>, which is how the graph records that the user has not
    /// expressed an opinion.
    /// </summary>
    public LacingMode Lacing { get; internal set; }

    /// <summary>
    /// Whether this node is frozen: deliberately skipped during evaluation (<c>E7-T14</c>).
    /// </summary>
    /// <remarks>
    /// <b>Freezing is a property of the instance, not of the definition.</b> Two nodes of the same
    /// kind in one graph are frozen independently, because what a user is switching off is a
    /// branch of their own document rather than a kind of operation.
    /// </remarks>
    public bool IsFrozen { get; internal set; }

    /// <summary>
    /// The lacing this instance actually replicates with: <see cref="Lacing"/> unless it is
    /// <see cref="LacingMode.Auto"/>, in which case the definition's default. This is the value the
    /// node's tooltip shows, so that two nodes both reading "Auto" and behaving differently is
    /// explicable rather than mysterious.
    /// </summary>
    public LacingMode EffectiveLacing => Definition.ResolveLacing(Lacing);

    /// <summary>
    /// The literal value typed into an unwired input port. A wired port ignores its literal.
    /// </summary>
    /// <param name="portIndex">The input port index.</param>
    /// <returns>The literal.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="portIndex"/> is not an input port.</exception>
    public object? Literal(int portIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(portIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(portIndex, _literals.Length);
        return _literals[portIndex];
    }

    /// <summary>A snapshot of every literal, in port order.</summary>
    /// <returns>A copy; mutating it does not affect the node.</returns>
    public IReadOnlyList<object?> Literals() => (object?[])_literals.Clone();

    internal void SetLiteral(int portIndex, object? value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(portIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(portIndex, _literals.Length);
        _literals[portIndex] = value;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Definition.DisplayName} ({Id})";
}
