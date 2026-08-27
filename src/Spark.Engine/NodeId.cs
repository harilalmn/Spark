using System;

namespace Spark.Engine;

/// <summary>
/// The identity of one node <i>instance</i> on a canvas. A <see cref="Guid"/>, stable for the life
/// of the graph and never reused.
/// </summary>
/// <remarks>
/// <para>
/// Instance identity and definition identity are different things and must not be confused: a
/// graph can hold fifty instances of the same <see cref="NodeKey"/>, and each of them has its own
/// <c>NodeId</c>, its own lacing and its own cache entry.
/// </para>
/// <para>
/// This is deliberately a wrapper rather than a bare <see cref="Guid"/>. Node ids, wire ids and
/// any future document id are all Guids, and a bare Guid parameter accepts all of them
/// interchangeably.
/// </para>
/// </remarks>
public readonly struct NodeId : IEquatable<NodeId>
{
    /// <summary>Wraps an existing Guid, which is what deserialisation does.</summary>
    /// <param name="value">The identity.</param>
    public NodeId(Guid value) => Value = value;

    /// <summary>The underlying Guid, as carried on a <see cref="Spark.Api.SparkDiagnostic"/>.</summary>
    public Guid Value { get; }

    /// <summary>The identity no node has.</summary>
    public static NodeId None => default;

    /// <summary>Creates a fresh identity that has never been used.</summary>
    /// <returns>The new identity.</returns>
    public static NodeId New() => new(Guid.NewGuid());

    /// <summary>Whether two identities are the same.</summary>
    /// <param name="left">The first identity.</param>
    /// <param name="right">The second identity.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    public static bool operator ==(NodeId left, NodeId right) => left.Equals(right);

    /// <summary>Whether two identities differ.</summary>
    /// <param name="left">The first identity.</param>
    /// <param name="right">The second identity.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(NodeId left, NodeId right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(NodeId other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NodeId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
}
