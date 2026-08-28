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
/// <b>Eviction is by last use against two bounds, and a result must fit inside both.</b> A byte
/// budget, estimated by <see cref="GraphValueSize"/>, and an entry ceiling. The bytes are the
/// bound that matters — a cache of four thousand meshes and a cache of four thousand numbers are
/// the same cache by count and are not the same cache — and the count survives beside it because
/// a per-entry cost the estimator cannot see (a dictionary node, a linked-list node, the key) is
/// real and is not proportional to the value's size.
/// </para>
/// <para>
/// <b>The estimate's blind spots are the cache's blind spots</b>, and they are listed on
/// <see cref="GraphValueSize"/> rather than repeated here. The one worth naming twice is native
/// memory: ADR-0021 requires a provider to <i>report</i> its own native budget, and until one
/// does, a cache holding shapes resident in another allocator's heap will believe it holds
/// almost nothing.
/// </para>
/// <para>
/// <b>A single result larger than the whole budget is kept anyway.</b> Evicting it would empty
/// the cache and then evict the thing just computed, so the next run recomputes it and the cycle
/// repeats — a cache that costs its budget in work and returns nothing. It is held, the budget is
/// knowingly exceeded, and <see cref="Bytes"/> says so.
/// </para>
/// </remarks>
public sealed class EvaluationCache
{
    /// <summary>
    /// The default byte budget: 256 MiB.
    /// </summary>
    /// <remarks>
    /// Large enough that an ordinary document never reaches it and small enough that a runaway
    /// one is bounded well before the process is. It is a default rather than a recommendation —
    /// an embedder inside a CAD host knows what its process can spare and should say so.
    /// </remarks>
    public const long DefaultByteBudget = 256L * 1024 * 1024;

    private readonly Dictionary<CacheKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _byLastUse = new();
    private readonly Lock _gate = new();
    private readonly int _capacity;
    private readonly long _byteBudget;
    private long _bytes;

    /// <summary>Creates a cache.</summary>
    /// <param name="capacity">The greatest number of results held. Must be positive.</param>
    /// <param name="byteBudget">
    /// The estimated bytes the held results may occupy. Must be positive. See the remarks on
    /// this type for what the estimate can and cannot see.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when either bound is not positive.
    /// </exception>
    public EvaluationCache(int capacity = 4096, long byteBudget = DefaultByteBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteBudget);

        _capacity = capacity;
        _byteBudget = byteBudget;
    }

    /// <summary>The byte budget this cache was created with.</summary>
    public long ByteBudget => _byteBudget;

    /// <summary>
    /// The estimated bytes currently held. May exceed <see cref="ByteBudget"/> when a single
    /// result is larger than the whole budget.
    /// </summary>
    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
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

    /// <summary>
    /// Stores a result, evicting least-recently-used entries until both bounds hold.
    /// </summary>
    /// <remarks>
    /// The loop stops at one entry rather than at zero: the entry that would be evicted last is
    /// the one just stored, and evicting it makes the cache a no-op that still pays the cost of
    /// estimating.
    /// </remarks>
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
                _bytes -= existing.Value.Bytes;
            }

            // Estimated once, on the way in, and remembered. Re-estimating at eviction time
            // would walk a large value a second time in order to decide to drop it, and would
            // corrupt the running total the moment the estimate disagreed with itself.
            long bytes = GraphValueSize.Estimate(result);

            LinkedListNode<Entry> node = _byLastUse.AddFirst(new Entry(key, result, bytes));
            _entries[key] = node;
            _bytes += bytes;

            while ((_entries.Count > _capacity || _bytes > _byteBudget) && _entries.Count > 1)
            {
                LinkedListNode<Entry>? oldest = _byLastUse.Last;
                if (oldest is null)
                {
                    break;
                }

                _byLastUse.RemoveLast();
                _entries.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Bytes;
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
            _bytes = 0;
        }
    }

    private readonly record struct Entry(CacheKey Key, CachedResult Result, long Bytes);
}
