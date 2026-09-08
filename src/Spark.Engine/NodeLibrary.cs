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

    /// <summary>
    /// Removes one definition.
    /// </summary>
    /// <param name="key">The definition's key.</param>
    /// <returns>True when a definition with that key was present and has been removed.</returns>
    /// <remarks>
    /// <b>Exists for unloading a package (<c>E7-T5</c>), and for very little else.</b> A library
    /// that nodes are removed from during ordinary use would make a graph's meaning depend on when
    /// it was opened. Removing is what happens when the package that contributed a definition is
    /// going away, and a graph still using it gets a placeholder that keeps everything
    /// (<c>E7-T6</c>) rather than an error.
    /// <para>
    /// Purging the library is <b>necessary and not sufficient</b> for a load context to unload:
    /// cached values, compiled invokers and viewport buffers hold references too. See
    /// <c>PackageManager.Unload</c>, which returns a weak reference rather than a boolean for
    /// exactly that reason.
    /// </para>
    /// </remarks>
    public bool Remove(NodeKey key)
    {
        if (!_definitions.Remove(key))
        {
            return false;
        }

        _ordered.RemoveAll(definition => definition.Key == key);
        return true;
    }

    /// <summary>The names nodes used to have, mapped to what they are called now (`E3-T23`).</summary>
    private readonly Dictionary<NodeKey, NodeDefinition> _aliases = [];

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

        // `E3-T23`: THE OLD NAMES ARE INDEXED SEPARATELY, AND NOT WITH `Add`'S DUPLICATE CHECK.
        //
        // An alias losing a race with a real key would be the wrong way round - a live node must
        // always beat a dead name, or renaming A to B in a library that still has its own B would
        // make B unreachable. So `TryGet` looks here only after the real index has missed, and a
        // clash between two aliases is resolved by first-registered rather than by throwing: an
        // assembly that renamed two members into one history is confused, not broken, and refusing
        // to load its whole library over it would be a worse answer than picking one.
        foreach (string alias in definition.Aliases)
        {
            _aliases.TryAdd(new NodeKey(definition.Key.Package, alias), definition);
        }
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
    /// <remarks>
    /// <b>A key that misses is tried against the renamed names before it fails</b> (<c>E3-T23</c>).
    /// This is the one place a saved graph turns a key into a node, so it is the one place a
    /// rename has to be forgiven — and forgiving it here means every loader gets it, including the
    /// command line, without any of them knowing aliases exist.
    /// </remarks>
    public bool TryGet(NodeKey key, out NodeDefinition? definition) =>
        _definitions.TryGetValue(key, out definition) || _aliases.TryGetValue(key, out definition);

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
