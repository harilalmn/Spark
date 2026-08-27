using System;

namespace Spark.Engine;

/// <summary>
/// A connection from one node's output port to another node's input port.
/// </summary>
/// <remarks>
/// A wire is a value, not an object with identity: two wires between the same four coordinates are
/// the same wire. An input port takes at most one wire — fan-in is what a list node is for — while
/// an output port may feed any number.
/// </remarks>
public readonly struct Wire : IEquatable<Wire>
{
    /// <summary>Creates a wire.</summary>
    /// <param name="source">The node the value comes from.</param>
    /// <param name="sourcePort">Its output port index.</param>
    /// <param name="target">The node the value goes to.</param>
    /// <param name="targetPort">Its input port index.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either port index is negative.</exception>
    public Wire(NodeId source, int sourcePort, NodeId target, int targetPort)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourcePort);
        ArgumentOutOfRangeException.ThrowIfNegative(targetPort);

        Source = source;
        SourcePort = sourcePort;
        Target = target;
        TargetPort = targetPort;
    }

    /// <summary>The node the value comes from.</summary>
    public NodeId Source { get; }

    /// <summary>The source node's output port index.</summary>
    public int SourcePort { get; }

    /// <summary>The node the value goes to.</summary>
    public NodeId Target { get; }

    /// <summary>The target node's input port index.</summary>
    public int TargetPort { get; }

    /// <summary>Whether two wires connect the same four coordinates.</summary>
    /// <param name="left">The first wire.</param>
    /// <param name="right">The second wire.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    public static bool operator ==(Wire left, Wire right) => left.Equals(right);

    /// <summary>Whether two wires differ.</summary>
    /// <param name="left">The first wire.</param>
    /// <param name="right">The second wire.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(Wire left, Wire right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(Wire other) =>
        Source == other.Source
        && SourcePort == other.SourcePort
        && Target == other.Target
        && TargetPort == other.TargetPort;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Wire other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Source, SourcePort, Target, TargetPort);

    /// <inheritdoc/>
    public override string ToString() => $"{Source}[{SourcePort}] -> {Target}[{TargetPort}]";
}
