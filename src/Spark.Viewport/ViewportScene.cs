using System;
using System.Collections.Generic;

namespace Spark.Viewport;

/// <summary>
/// Everything the viewport is currently showing, keyed by <see cref="GeometryKey"/> so that one
/// node's output occupies exactly one slot.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is thread-safe and it has to be.</b> Tessellation runs in parallel and streams
/// during a run, so packages arrive on worker threads while the render thread is reading. The
/// lock is coarse — one gate over a dictionary — because the contended operations are a
/// dictionary write and a snapshot copy, both measured in microseconds, and a finer scheme would
/// buy nothing except a class of bug that only appears on a large graph.
/// </para>
/// <para>
/// <see cref="Version"/> increments on every mutation. A renderer compares it against the version
/// it last uploaded to decide whether to touch the GPU at all, which is what keeps an idle
/// viewport free.
/// </para>
/// </remarks>
public sealed class ViewportScene
{
    private readonly object _gate = new();
    private readonly Dictionary<GeometryKey, RenderPackage> _packages = [];
    private RenderPackage[]? _snapshot;
    private long _version;

    /// <summary>
    /// A counter incremented on every mutation. Renderers use it to skip work; nothing derives
    /// meaning from its absolute value.
    /// </summary>
    public long Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    /// <summary>The number of packages currently in the scene.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _packages.Count;
            }
        }
    }

    /// <summary>
    /// Adds or replaces the package for a key. Replacing is the normal case: a node that
    /// re-evaluates produces a new package under the same key, and exactly one buffer set is
    /// re-uploaded as a result.
    /// </summary>
    /// <param name="package">The package to store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="package"/> is null.</exception>
    public void Set(RenderPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        lock (_gate)
        {
            _packages[package.Key] = package;
            Invalidate();
        }
    }

    /// <summary>Removes one package.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>True when a package was present and has been removed.</returns>
    public bool Remove(GeometryKey key)
    {
        lock (_gate)
        {
            if (!_packages.Remove(key))
            {
                return false;
            }

            Invalidate();
            return true;
        }
    }

    /// <summary>
    /// Removes every package produced by one node, across all of its output ports. This is what a
    /// node deletion calls, and it is why the key is a tuple rather than an opaque handle.
    /// </summary>
    /// <param name="nodeId">The node whose geometry is going away.</param>
    /// <returns>The number of packages removed.</returns>
    public int RemoveNode(string nodeId)
    {
        ArgumentNullException.ThrowIfNull(nodeId);

        lock (_gate)
        {
            List<GeometryKey>? doomed = null;
            foreach (GeometryKey key in _packages.Keys)
            {
                if (string.Equals(key.NodeId, nodeId, StringComparison.Ordinal))
                {
                    (doomed ??= []).Add(key);
                }
            }

            if (doomed is null)
            {
                return 0;
            }

            foreach (GeometryKey key in doomed)
            {
                _packages.Remove(key);
            }

            Invalidate();
            return doomed.Count;
        }
    }

    /// <summary>Empties the scene.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_packages.Count == 0)
            {
                return;
            }

            _packages.Clear();
            Invalidate();
        }
    }

    /// <summary>
    /// The packages, as an array the caller may hold across a frame. Cached until the next
    /// mutation, so a still scene costs one array read per frame rather than a copy.
    /// </summary>
    /// <returns>The current packages, in no defined order.</returns>
    public RenderPackage[] Snapshot()
    {
        lock (_gate)
        {
            if (_snapshot is null)
            {
                _snapshot = new RenderPackage[_packages.Count];
                _packages.Values.CopyTo(_snapshot, 0);
            }

            return _snapshot;
        }
    }

    /// <summary>The bounds of every package in the scene.</summary>
    /// <returns>The union of all package bounds, empty when the scene is empty.</returns>
    public Bounds3 ComputeBounds()
    {
        Bounds3 bounds = Bounds3.Empty;
        foreach (RenderPackage package in Snapshot())
        {
            bounds = bounds.Union(package.ComputeBounds());
        }

        return bounds;
    }

    private void Invalidate()
    {
        _snapshot = null;
        _version++;
    }
}
