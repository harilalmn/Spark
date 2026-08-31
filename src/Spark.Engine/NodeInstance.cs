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
    private static readonly Dictionary<string, Type> NoDeclaredTypes = [];

    private readonly object?[] _literals;

    private Dictionary<string, Type>? _declaredInputTypes;

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

    /// <summary>
    /// The types the user has declared for this node's input ports, by port name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only a code block has these, and only because a wire is not the only way to learn a
    /// type.</b> A code block's input types normally come from whatever is wired into it, which
    /// costs nothing and is right whenever something *is* wired in. Before that, the port is
    /// <c>dynamic</c> — and a user typing <c>radius.</c> into the editor is offered the members of
    /// <c>object</c>, which is worse than useless because it looks like an answer.
    /// </para>
    /// <para>
    /// <b>Keyed by name, for the reason <see cref="Graph.InputTypes"/> is</b>: a code block's port
    /// indices move when its source gains an identifier, so an index would silently come to mean a
    /// different port. A name survives an edit; that is also what makes a declaration outlive the
    /// rebuild that applies it.
    /// </para>
    /// <para>
    /// <b>A name with no port is kept rather than pruned.</b> Deleting a line and putting it back
    /// is an ordinary thing to do while writing, and a declaration that evaporated in between
    /// would have to be made again for no reason the user could see.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, Type> DeclaredInputTypes =>
        _declaredInputTypes ?? NoDeclaredTypes;

    internal void SetDeclaredInputType(string portName, Type? type)
    {
        ArgumentException.ThrowIfNullOrEmpty(portName);

        if (type is null)
        {
            _declaredInputTypes?.Remove(portName);
            return;
        }

        _declaredInputTypes ??= new Dictionary<string, Type>(StringComparer.Ordinal);
        _declaredInputTypes[portName] = type;
    }

    internal void SetLiteral(int portIndex, object? value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(portIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(portIndex, _literals.Length);
        _literals[portIndex] = value;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Definition.DisplayName} ({Id})";
}
