using System;
using System.Collections.Generic;
using System.IO;

namespace Spark.Host;

/// <summary>
/// Watches the assemblies a user has referenced and says when one has been rebuilt
/// (<c>E7-T9</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It offers; it never reloads.</b> The row says auto-reload is <i>offered</i>, and the
/// distinction is the whole design. A reference that swapped itself out underneath a running graph
/// would change what the graph computes without anybody asking, and the user would have no way to
/// tell that the answer they are looking at came from different code than the one they were
/// looking at a second ago. So this raises an event and stops.
/// </para>
/// <para>
/// <b>Changes are coalesced, because a build is not one write.</b> A compiler writes a DLL,
/// rewrites it, writes the PDB beside it and touches the directory; <see cref="FileSystemWatcher"/>
/// reports every one of those. Raising the offer four times would produce four prompts for one
/// rebuild. Nothing is reported until the file has been still for
/// <see cref="Quiet"/>, which also means the file is no longer half-written when it is read.
/// </para>
/// <para>
/// <b>A directory that cannot be watched is not an error.</b> Watching fails on some network
/// shares and inside some containers, and a reference on such a share is still a perfectly good
/// reference — it simply will not announce itself. The user keeps the manual reload.
/// </para>
/// </remarks>
public sealed class LocalReferenceWatcher : IDisposable
{
    /// <summary>How long a file must be still before a change is reported.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(400);

    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, System.Threading.Timer> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>Raised, once per settled change, with the assembly's full path.</summary>
    /// <remarks>
    /// <b>Raised on a thread-pool thread</b>, because that is where the watcher's callback and the
    /// coalescing timer live. A UI subscriber has to marshal, and the one in this repository does.
    /// </remarks>
    public event EventHandler<string>? Changed;

    /// <summary>How many assemblies are being watched.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _watchers.Count;
            }
        }
    }

    /// <summary>Starts watching one assembly.</summary>
    /// <param name="path">The assembly's path.</param>
    /// <returns>True when a watcher was established.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    public bool Watch(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        string full;
        string? folder;
        string name;

        try
        {
            full = Path.GetFullPath(path);
            folder = Path.GetDirectoryName(full);
            name = Path.GetFileName(full);
        }
        catch (Exception failure) when (failure is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }

        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name) || !Directory.Exists(folder))
        {
            return false;
        }

        lock (_gate)
        {
            if (_watchers.ContainsKey(full))
            {
                return true;
            }

            try
            {
                FileSystemWatcher watcher = new(folder, name)
                {
                    // Size and write time cover a rebuild; CreationTime covers the
                    // delete-and-replace some build tools do instead of writing in place.
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                };

                watcher.Changed += (_, _) => Settle(full);
                watcher.Created += (_, _) => Settle(full);
                watcher.Renamed += (_, _) => Settle(full);
                watcher.EnableRaisingEvents = true;

                _watchers[full] = watcher;
                return true;
            }
            catch (Exception failure) when (failure is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException
                or ArgumentException)
            {
                // Some shares and some containers refuse to be watched. The reference still works;
                // it just will not announce itself.
                return false;
            }
        }
    }

    /// <summary>Stops watching one assembly.</summary>
    /// <param name="path">The assembly's path.</param>
    /// <returns>True when it was being watched.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    public bool Unwatch(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full;

        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception failure) when (failure is ArgumentException or IOException or NotSupportedException)
        {
            full = path;
        }

        lock (_gate)
        {
            if (!_watchers.Remove(full, out FileSystemWatcher? watcher))
            {
                return false;
            }

            watcher.EnableRaisingEvents = false;
            watcher.Dispose();

            if (_pending.Remove(full, out System.Threading.Timer? timer))
            {
                timer.Dispose();
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (FileSystemWatcher watcher in _watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            foreach (System.Threading.Timer timer in _pending.Values)
            {
                timer.Dispose();
            }

            _watchers.Clear();
            _pending.Clear();
        }
    }

    /// <summary>
    /// Restarts the quiet period for one file, so a burst of writes produces one report.
    /// </summary>
    private void Settle(string full)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_pending.TryGetValue(full, out System.Threading.Timer? existing))
            {
                _ = existing.Change(Quiet, System.Threading.Timeout.InfiniteTimeSpan);
                return;
            }

            _pending[full] = new System.Threading.Timer(
                _ => Announce(full), null, Quiet, System.Threading.Timeout.InfiniteTimeSpan);
        }
    }

    private void Announce(string full)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_pending.Remove(full, out System.Threading.Timer? timer))
            {
                timer.Dispose();
            }
        }

        Changed?.Invoke(this, full);
    }
}
