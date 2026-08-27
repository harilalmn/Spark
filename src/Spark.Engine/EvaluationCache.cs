using System;
using System.Collections.Generic;
using System.Threading;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// One node's cached result: its outputs and everything the engine said about it.
/// </summary>
/// <param name="Outputs">One value per output port.</param>
/// <param name="Diagnostics">The warnings and information produced alongside them.</param>
public sealed record CachedResult(IReadOnlyList<object?> Outputs, IReadOnlyList<SparkDiagnostic> Diagnostics);

/// <summary>
/// Results held against their provenance keys, evicted least-recently-used first.
/// </summary>
/// <remarks>
/// <para>
/// The cache is an instance owned by a session, never a static. Two sessions in one process — a CLI
/// run inside a host, a headless docs harness beside an editor — must not share cached geometry,
/// because their document tolerances and their loaded packages can differ.
/// </para>
/// <para>
/// Eviction is by last use against an entry-count ceiling. That is deliberately crude: the honest
/// budget is bytes, and estimating the size of a graph value is its own problem, so the count is a
/// bound that stops the cache growing without pretending to be a memory budget.
/// </para>
/// </remarks>
public sealed class EvaluationCache
{
    private readonly Dictionary<CacheKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _byLastUse = new();
    private readonly Lock _gate = new();
    private readonly int _capacity;

    /// <summary>Creates a cache.</summary>
    /// <param name="capacity">The greatest number of results held. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public EvaluationCache(int capacity = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <summary>How many results are held.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Looks a result up, and marks it most recently used when found.</summary>
    /// <param name="key">The provenance key.</param>
    /// <param name="result">The result, when it is held.</param>
    /// <returns><see langword="true"/> when it is held.</returns>
    public bool TryGet(CacheKey key, out CachedResult? result)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                result = null;
                return false;
            }

            _byLastUse.Remove(node);
            _byLastUse.AddFirst(node);
            result = node.Value.Result;
            return true;
        }
    }

    /// <summary>Stores a result, evicting the least recently used entry if the cache is full.</summary>
    /// <param name="key">The provenance key.</param>
    /// <param name="result">The result.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public void Set(CacheKey key, CachedResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out LinkedListNode<Entry>? existing))
            {
                _byLastUse.Remove(existing);
                _entries.Remove(key);
            }

            LinkedListNode<Entry> node = _byLastUse.AddFirst(new Entry(key, result));
            _entries[key] = node;

            while (_entries.Count > _capacity)
            {
                LinkedListNode<Entry>? oldest = _byLastUse.Last;
                if (oldest is null)
                {
                    break;
                }

                _byLastUse.RemoveLast();
                _entries.Remove(oldest.Value.Key);
            }
        }
    }

    /// <summary>Empties the cache.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _byLastUse.Clear();
        }
    }

    private readonly record struct Entry(CacheKey Key, CachedResult Result);
}
