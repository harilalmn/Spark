using System;
using System.Collections.Generic;
using System.Threading;
using Spark.Api;
using Spark.Geometry;

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
/// Eviction is by last use against <b>two</b> ceilings: an entry count, and a <b>native-memory
/// budget</b>. The count is deliberately crude - estimating the managed size of a graph value is
/// its own problem, so it bounds growth without pretending to be a memory budget.
/// </para>
/// <para>
/// <b>The native budget is not crude, and it exists because managed size cannot see the thing that
/// matters.</b> A <see cref="Brep"/> that a kernel provider still holds
/// ([ADR-0021](../../docs/adr/0021-brep-kernel-residency.md)) is a few dozen managed bytes in
/// front of a shape that may be megabytes of somebody else's heap. Two hundred such results would
/// sit inside any count ceiling anybody would set while holding gigabytes. That is <b>NFR-4</b>,
/// and the number the provider reports through <see cref="Brep.NativeBytes"/> is what makes it
/// enforceable rather than merely stated.
/// </para>
/// <para>
/// <b>One entry is always kept.</b> A single result larger than the whole budget would otherwise
/// be evicted the instant it was stored, and every lookup would miss on something that had just
/// been computed - a cache that is worse than no cache.
/// </para>
/// <para>
/// <b>A shape held by two entries is counted twice, and that is the safe direction.</b> Comparing
/// values for identity across entries would make the accounting exact and the eviction slower; the
/// error is an over-estimate, and an over-estimate evicts sooner than it needs to rather than
/// later than it should.
/// </para>
/// </remarks>
public sealed class EvaluationCache
{
    /// <summary>The default native-memory ceiling: 512 MB.</summary>
    /// <remarks>
    /// <b>Chosen against the payload rather than against the machine.</b> A resident BRep of a
    /// realistic building component is on the order of a megabyte, so this holds hundreds of them -
    /// the working set of a graph somebody is actually editing - and stops a graph that produces
    /// thousands from taking the process down. It is a constructor argument because an embedder
    /// inside a CAD host has less to spend than a standalone editor.
    /// </remarks>
    public const long DefaultNativeBudget = 512L * 1024 * 1024;

    private readonly Dictionary<CacheKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _byLastUse = new();
    private readonly Lock _gate = new();
    private readonly int _capacity;
    private readonly long _nativeBudget;

    private long _nativeBytes;

    /// <summary>Creates a cache.</summary>
    /// <param name="capacity">The greatest number of results held. Must be positive.</param>
    /// <param name="nativeBudget">
    /// The greatest amount of kernel-provider memory the held results may reference. Must be
    /// positive.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Either ceiling is not positive.</exception>
    public EvaluationCache(int capacity = 4096, long nativeBudget = DefaultNativeBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nativeBudget);

        _capacity = capacity;
        _nativeBudget = nativeBudget;
    }

    /// <summary>Roughly how much kernel-provider memory the held results reference.</summary>
    /// <remarks>
    /// An over-estimate when one shape is held by two entries. Zero in a build with no provider,
    /// which is every build that has never made a solid.
    /// </remarks>
    public long NativeBytes
    {
        get
        {
            lock (_gate)
            {
                return _nativeBytes;
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
                _nativeBytes -= existing.Value.NativeBytes;
                _byLastUse.Remove(existing);
                _entries.Remove(key);
            }

            long native = NativeSizeOf(result);
            LinkedListNode<Entry> node = _byLastUse.AddFirst(new Entry(key, result, native));
            _entries[key] = node;
            _nativeBytes += native;

            // One entry is always kept: a single result bigger than the whole budget would
            // otherwise be evicted the moment it was stored.
            while ((_entries.Count > _capacity || _nativeBytes > _nativeBudget) && _entries.Count > 1)
            {
                LinkedListNode<Entry>? oldest = _byLastUse.Last;
                if (oldest is null)
                {
                    break;
                }

                _nativeBytes -= oldest.Value.NativeBytes;
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
            _nativeBytes = 0L;
        }
    }

    /// <summary>How much provider memory one result references.</summary>
    /// <remarks>
    /// <b>Asked once, when the result is stored, and remembered.</b> Asking again at eviction time
    /// would be asking a shape that may since have been materialised or disposed, and the number
    /// subtracted has to be the number that was added or the running total drifts.
    /// </remarks>
    private static long NativeSizeOf(CachedResult result)
    {
        long total = 0L;

        foreach (object? output in result.Outputs)
        {
            total += NativeSizeOf(output, depth: 0);
        }

        return total;
    }

    private static long NativeSizeOf(object? value, int depth)
    {
        // A graph value nests - a list of lists of displayables - but not without bound, and a
        // cycle would be a bug elsewhere. Sixteen is far past anything replication produces.
        if (depth > 16)
        {
            return 0L;
        }

        switch (value)
        {
            case Brep brep:
                return brep.NativeBytes;

            case Displayable displayable:
                return NativeSizeOf(displayable.Geometry, depth + 1);

            case string:
                return 0L;

            case System.Collections.IEnumerable list:
                {
                    long total = 0L;

                    foreach (object? item in list)
                    {
                        total += NativeSizeOf(item, depth + 1);
                    }

                    return total;
                }

            default:
                return 0L;
        }
    }

    private readonly record struct Entry(CacheKey Key, CachedResult Result, long NativeBytes);
}
