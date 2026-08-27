using System;
using System.Collections.Generic;
using System.Linq;

namespace Spark.Engine;

/// <summary>
/// The definitions a session can place: everything imported from every loaded package, indexed by
/// <see cref="NodeKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// A duplicate key is refused rather than replaced. Two packages may each publish a
/// <c>Curve.Offset</c> and the package half of the key keeps them apart; two definitions with the
/// <i>same</i> key means one of them would be silently unreachable, and a graph saved against the
/// shadowed one would bind to the other and produce geometry rather than an error.
/// </para>
/// </remarks>
public sealed class NodeLibrary
{
    private readonly Dictionary<NodeKey, NodeDefinition> _definitions = [];
    private readonly List<NodeDefinition> _ordered = [];

    /// <summary>Every definition, ordered by display name.</summary>
    /// <returns>A snapshot.</returns>
    public IReadOnlyList<NodeDefinition> Definitions() =>
        [.. _ordered.OrderBy(definition => definition.DisplayName, StringComparer.Ordinal)];

    /// <summary>How many definitions are registered.</summary>
    public int Count => _ordered.Count;

    /// <summary>Adds one definition.</summary>
    /// <param name="definition">The definition.</param>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A definition with that key is already registered.</exception>
    public void Add(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!_definitions.TryAdd(definition.Key, definition))
        {
            throw new ArgumentException(
                $"A definition with key '{definition.Key}' is already registered. Duplicate keys make one of the two unreachable, and a saved graph would bind to whichever won.",
                nameof(definition));
        }

        _ordered.Add(definition);
    }

    /// <summary>Adds every definition in an import.</summary>
    /// <param name="report">The import report.</param>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Any key is already registered.</exception>
    public void Add(ImportReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        foreach (ImportedNode node in report.Nodes)
        {
            Add(node.Definition);
        }
    }

    /// <summary>Looks a definition up by key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="definition">The definition, when it is registered.</param>
    /// <returns><see langword="true"/> when it is registered.</returns>
    public bool TryGet(NodeKey key, out NodeDefinition? definition) =>
        _definitions.TryGetValue(key, out definition);

    /// <summary>Looks a definition up by key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="KeyNotFoundException">Nothing is registered under that key.</exception>
    public NodeDefinition Get(NodeKey key) => _definitions[key];

    /// <summary>
    /// Looks a definition up by display name, ignoring the package. Convenient for tests and for
    /// building a demo graph; a saved document always uses the full key.
    /// </summary>
    /// <param name="displayName">The display name, for example <c>Point.ByCoordinates</c>.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="KeyNotFoundException">No definition has that display name.</exception>
    public NodeDefinition ByName(string displayName)
    {
        foreach (NodeDefinition definition in _ordered)
        {
            if (string.Equals(definition.DisplayName, displayName, StringComparison.Ordinal))
            {
                return definition;
            }
        }

        throw new KeyNotFoundException($"No node named '{displayName}' is registered.");
    }
}
