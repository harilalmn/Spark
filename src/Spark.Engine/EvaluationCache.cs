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
/// Eviction is by last use against <b>three</b> ceilings: an entry count, a <b>native-memory
/// budget</b>, and since `E3-T9` a <b>managed-memory budget</b>. The count bounds growth without
/// pretending to be a memory budget; the two budgets are the memory budgets.
/// </para>
/// <para>
/// <b>Managed size is estimated from counts, never measured by walking bytes.</b> A mesh weighs its
/// vertex and face counts, a NURBS curve its control points, a list the sum of its elements. That is
/// rough on purpose: hashing or serialising a two-million-triangle mesh to weigh it would cost more
/// than recomputing it, which is the reason this cache keys by provenance in the first place. The
/// estimates err high, which is the safe direction for the same reason given below for native bytes.
/// Before the managed budget, a mesh of that size cost the cache one entry of 4,096 and nothing else.
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

    /// <summary>The default managed-memory ceiling: 1 GB (`E3-T9`).</summary>
    /// <remarks>
    /// <b>Larger than the native budget because meshes live here.</b> A tessellated surface or an
    /// imported model is a managed <see cref="Mesh"/> of tens of megabytes, and the working set of a
    /// graph somebody is editing holds dozens of them; this keeps those and stops a sweep that makes
    /// thousands from filling the machine. A constructor argument for the embedder's reason.
    /// </remarks>
    public const long DefaultManagedBudget = 1024L * 1024 * 1024;

    private readonly Dictionary<CacheKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _byLastUse = new();
    private readonly Lock _gate = new();
    private readonly int _capacity;
    private readonly long _nativeBudget;
    private readonly long _managedBudget;

    private long _nativeBytes;
    private long _managedBytes;

    /// <summary>Creates a cache.</summary>
    /// <param name="capacity">The greatest number of results held. Must be positive.</param>
    /// <param name="nativeBudget">
    /// The greatest amount of kernel-provider memory the held results may reference. Must be
    /// positive.
    /// </param>
    /// <param name="managedBudget">
    /// The greatest estimated managed size the held results may have. Must be positive (`E3-T9`).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A ceiling is not positive.</exception>
    public EvaluationCache(
        int capacity = 4096, long nativeBudget = DefaultNativeBudget, long managedBudget = DefaultManagedBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nativeBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(managedBudget);

        _capacity = capacity;
        _nativeBudget = nativeBudget;
        _managedBudget = managedBudget;
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

    /// <summary>Roughly how much managed memory the held results occupy (`E3-T9`).</summary>
    /// <remarks>An estimate from counts, and an over-estimate by design; see the type's remarks.</remarks>
    public long ManagedBytes
    {
        get
        {
            lock (_gate)
            {
                return _managedBytes;
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
                _managedBytes -= existing.Value.ManagedBytes;
                _byLastUse.Remove(existing);
                _entries.Remove(key);
            }

            long native = NativeSizeOf(result);
            long managed = ManagedSizeOf(result);
            LinkedListNode<Entry> node = _byLastUse.AddFirst(new Entry(key, result, native, managed));
            _entries[key] = node;
            _nativeBytes += native;
            _managedBytes += managed;

            // One entry is always kept: a single result bigger than the whole budget would
            // otherwise be evicted the moment it was stored.
            while ((_entries.Count > _capacity || _nativeBytes > _nativeBudget || _managedBytes > _managedBudget)
                && _entries.Count > 1)
            {
                LinkedListNode<Entry>? oldest = _byLastUse.Last;
                if (oldest is null)
                {
                    break;
                }

                _nativeBytes -= oldest.Value.NativeBytes;
                _managedBytes -= oldest.Value.ManagedBytes;
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
            _managedBytes = 0L;
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

    /// <summary>Roughly how much managed memory one result occupies (`E3-T9`).</summary>
    /// <remarks>
    /// Asked once, when the result is stored, and remembered, for <see cref="NativeSizeOf(CachedResult)"/>'s
    /// reason: the number subtracted on eviction has to be the number that was added.
    /// </remarks>
    private static long ManagedSizeOf(CachedResult result)
    {
        long total = 0L;

        foreach (object? output in result.Outputs)
        {
            total += ManagedSizeOf(output, depth: 0);
        }

        return total;
    }

    /// <summary>An estimate from counts, erring high.</summary>
    /// <remarks>
    /// <b>A shape the provider holds weighs nothing here, and must not be asked its counts.</b>
    /// <see cref="Brep.VertexCount"/> and its siblings read the managed arrays, and for a resident
    /// shape reading them <i>materialises</i> it - the conversion ADR-0021 exists to avoid, and the
    /// estimate would then create the memory it was estimating. Its weight is its native bytes,
    /// counted separately.
    /// </remarks>
    private static long ManagedSizeOf(object? value, int depth)
    {
        const long Header = 32L;
        const long Point = 24L;

        if (depth > 16 || value is null)
        {
            return 0L;
        }

        switch (value)
        {
            case Mesh mesh:
                // Position, normal and texture coordinate per vertex, four indices per face.
                return Header + (mesh.VertexCount * 64L) + (mesh.FaceCount * 16L);

            case NurbsCurve nurbs:
                return Header + (nurbs.Knots.ControlPointCount * (Point + 8L)) + (nurbs.Knots.Count * 8L);

            case NurbsSurface surface:
                return Header
                    + ((long)surface.KnotsU.ControlPointCount * surface.KnotsV.ControlPointCount * (Point + 8L))
                    + ((surface.KnotsU.Count + surface.KnotsV.Count) * 8L);

            case PolyLine polyline:
                return Header + (polyline.PointCount * Point);

            case PolyCurve polycurve:
                {
                    long total = Header;

                    foreach (Curve segment in polycurve.Segments())
                    {
                        total += ManagedSizeOf(segment, depth + 1);
                    }

                    return total;
                }

            case Brep brep:
                return brep.IsResident
                    ? Header
                    : Header + (brep.FaceCount * 256L) + ((brep.VertexCount + brep.EdgeCount) * 64L);

            case Displayable displayable:
                return Header + ManagedSizeOf(displayable.Geometry, depth + 1);

            case string text:
                return Header + (text.Length * 2L);

            case System.Collections.IEnumerable list:
                {
                    long total = Header;

                    foreach (object? item in list)
                    {
                        // A reference per element, and whatever the element itself weighs.
                        total += 8L + ManagedSizeOf(item, depth + 1);
                    }

                    return total;
                }

            default:
                // A point, a number, an analytic curve or surface: a handful of doubles.
                return Header + Point;
        }
    }

    private readonly record struct Entry(CacheKey Key, CachedResult Result, long NativeBytes, long ManagedBytes);
}
