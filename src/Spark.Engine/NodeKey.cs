using System;

namespace Spark.Engine;

/// <summary>
/// The identity of a node <i>definition</i>: a stable string that includes the package it came
/// from. This is what a saved graph stores, and what a reloaded graph binds against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Package identity is part of the key, not decoration.</b> Two packages can each publish a
/// <c>Curve.Offset</c>, and without the package in the key a graph saved against one binds
/// silently against the other on the next machine — a misbinding that produces geometry rather than
/// an error, which is the worst shape a version conflict can take. With it, the graph either binds
/// to the definition it was authored against or reports an unresolved node.
/// </para>
/// <para>
/// <b>It is not a display name.</b> Display names change; keys do not. The canonical form is
/// <c>package/name</c>, both parts compared with ordinal case sensitivity so that a key means the
/// same thing on a case-insensitive file system as on a case-sensitive one.
/// </para>
/// </remarks>
public readonly struct NodeKey : IEquatable<NodeKey>
{
    /// <summary>Creates a key from a package identity and a definition name.</summary>
    /// <param name="package">
    /// The publishing package's identity, for example <c>Spark.Nodes.Core</c>. Must not be blank
    /// and must not contain <c>/</c>, which separates the two halves of the canonical form.
    /// </param>
    /// <param name="name">
    /// The definition name within that package, for example <c>Circle.ByCenterRadius</c>. Must not
    /// be blank.
    /// </param>
    /// <exception cref="ArgumentException">Either part is blank, or the package contains a slash.</exception>
    public NodeKey(string package, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (package.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A package identity may not contain '/'; '{package}' does.", nameof(package));
        }

        Package = package;
        Name = name;
    }

    /// <summary>The publishing package's identity.</summary>
    public string Package { get; }

    /// <summary>The definition name within that package.</summary>
    public string Name { get; }

    /// <summary>
    /// The canonical <c>package/name</c> string. This is the form written to a <c>.spark</c> file
    /// and mixed into a cache key.
    /// </summary>
    public string Value => Package is null ? string.Empty : string.Concat(Package, "/", Name);

    /// <summary>
    /// Parses the canonical <c>package/name</c> form produced by <see cref="Value"/>.
    /// </summary>
    /// <param name="value">The canonical form.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not of the form <c>package/name</c>.</exception>
    public static NodeKey Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        int separator = value.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new ArgumentException($"'{value}' is not a node key of the form package/name.", nameof(value));
        }

        return new NodeKey(value[..separator], value[(separator + 1)..]);
    }

    /// <summary>Whether two keys identify the same definition.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><see langword="true"/> when both package and name match.</returns>
    public static bool operator ==(NodeKey left, NodeKey right) => left.Equals(right);

    /// <summary>Whether two keys identify different definitions.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(NodeKey left, NodeKey right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(NodeKey other) =>
        string.Equals(Package, other.Package, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NodeKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(
            Package is null ? 0 : StringComparer.Ordinal.GetHashCode(Package),
            Name is null ? 0 : StringComparer.Ordinal.GetHashCode(Name));

    /// <inheritdoc/>
    public override string ToString() => Value;
}
