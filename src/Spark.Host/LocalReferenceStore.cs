using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Spark.Host;

/// <summary>
/// What a local assembly reference is at the moment it is looked at.
/// </summary>
/// <param name="Path">The full path to the assembly.</param>
/// <param name="Hash">
/// SHA-256 of its contents, or <see cref="string.Empty"/> when the file could not be read.
/// </param>
/// <param name="Exists">Whether the file was there when this was taken.</param>
public sealed record LocalReference(string Path, string Hash, bool Exists)
{
    /// <summary>The file name alone, for a list a person reads.</summary>
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>The first eight characters of the hash, which is what a user can compare by eye.</summary>
    public string ShortHash => Hash.Length >= 8 ? Hash[..8] : Hash;
}

/// <summary>
/// The local assemblies a user has added as code-block references, and what they agreed to
/// (<c>E7-T9</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A referenced DLL is code from outside Spark, so it is asked about once and remembered by
/// content.</b> This is <see cref="ScriptTrustStore"/>'s posture applied to assemblies rather than
/// to graphs, and for the same reason: a reference is not a document, it is something that will be
/// compiled against and whose types will run.
/// </para>
/// <para>
/// <b>Keyed on the path and the hash together, and both halves matter.</b> Keyed on the path alone,
/// a user who agreed to <c>MyNodes.dll</c> in March would still be trusting whatever that file says
/// today, which is exactly what a rebuild changes. Keyed on the hash alone, agreeing to one copy
/// would agree to every copy of it anywhere. Together they mean <i>this file, saying exactly
/// this</i>.
/// </para>
/// <para>
/// <b>So a rebuild re-prompts, and that is the feature rather than a nuisance.</b> The row asks for
/// exactly this: prompt once, record a content hash, re-prompt when the hash changes. A user
/// rebuilding their own library will answer yes each time, and the one time the file changed
/// because something else changed it, they will be asked.
/// </para>
/// <para>
/// <b>What this is not.</b> It is not a sandbox. It records a decision; it cannot constrain what
/// the decision permits.
/// </para>
/// </remarks>
public sealed class LocalReferenceStore
{
    private readonly string? _path;

    // Path (upper-cased) -> the hash agreed to. One entry per assembly: agreeing to a new build
    // replaces the old agreement rather than accumulating, because a list of every version a user
    // ever trusted would grow without bound and answer no question anybody has.
    private readonly Dictionary<string, string> _agreed = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    /// <summary>Creates a store under the user's local application data.</summary>
    public LocalReferenceStore() : this(DefaultPath())
    {
    }

    /// <summary>Creates a store at a chosen path.</summary>
    /// <param name="path">Where the record lives, or null to keep it in memory only.</param>
    /// <remarks>
    /// Null is a session that remembers nothing, which is what a test wants and what a host with
    /// nowhere to write gets. It is the safe direction to fail in: the user is asked again.
    /// </remarks>
    public LocalReferenceStore(string? path)
    {
        _path = path;

        Read();
    }

    /// <summary>How many assemblies are recorded.</summary>
    public int Count => _agreed.Count;

    /// <summary>Every recorded assembly, in the order they were added.</summary>
    /// <returns>One entry each, read fresh from disk so a changed file shows as changed.</returns>
    public IReadOnlyList<LocalReference> All() => [.. _order.Select(Look)];

    /// <summary>Reads an assembly's current identity: its path, its hash, and whether it is there.</summary>
    /// <param name="path">The assembly's path.</param>
    /// <returns>What the file is right now.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    /// <remarks>
    /// <b>Hashing the whole file, not its length and timestamp.</b> A fingerprint good enough for a
    /// compile cache is not good enough for a trust decision: this is the question <i>is this the
    /// same code I agreed to</i>, and a build that produced the same size at the same second would
    /// answer it wrongly. The files are megabytes, and this runs when a user adds a reference or a
    /// watcher notices a change, not on any hot path.
    /// </remarks>
    public static LocalReference Look(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Full(path);

        try
        {
            using FileStream file = new(
                full,
                FileMode.Open,
                FileAccess.Read,
                // Share everything, including delete. Reading a reference must never be the reason
                // a user's build fails, which is this row's whole point.
                FileShare.ReadWrite | FileShare.Delete);

            return new LocalReference(full, Convert.ToHexString(SHA256.HashData(file)), Exists: true);
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            // A file that cannot be read is reported as absent rather than thrown about: it is
            // usually mid-build, and the answer a caller needs is "not the thing you agreed to".
            return new LocalReference(full, string.Empty, Exists: false);
        }
    }

    /// <summary>Whether this assembly, saying exactly what it says now, has been agreed to.</summary>
    /// <param name="path">The assembly's path.</param>
    /// <returns>False when it is new, when it has changed, or when it cannot be read.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    public bool IsTrusted(string path)
    {
        LocalReference current = Look(path);

        return current.Exists
            && _agreed.TryGetValue(Key(current.Path), out string? hash)
            && string.Equals(hash, current.Hash, StringComparison.Ordinal);
    }

    /// <summary>Whether this assembly is recorded but no longer says what it did.</summary>
    /// <param name="path">The assembly's path.</param>
    /// <returns>True only when it is known, readable, and its hash has moved.</returns>
    /// <remarks>
    /// <b>Distinct from <see cref="IsTrusted"/> returning false</b>, because the two need different
    /// words in front of a user. One is <i>you have not been asked about this</i>; the other is
    /// <i>this is not the file you agreed to</i>, and only the second is worth interrupting for.
    /// </remarks>
    public bool HasChanged(string path)
    {
        LocalReference current = Look(path);

        return current.Exists
            && _agreed.TryGetValue(Key(current.Path), out string? hash)
            && !string.Equals(hash, current.Hash, StringComparison.Ordinal);
    }

    /// <summary>Records that the user agreed to this assembly as it stands.</summary>
    /// <param name="path">The assembly's path.</param>
    /// <returns>What was agreed to, so a caller can show the hash it recorded.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    /// <exception cref="InvalidOperationException">The file cannot be read.</exception>
    public LocalReference Trust(string path)
    {
        LocalReference current = Look(path);

        if (!current.Exists)
        {
            throw new InvalidOperationException(
                $"'{current.Path}' could not be read, so there is nothing to agree to.");
        }

        string key = Key(current.Path);

        if (!_agreed.ContainsKey(key))
        {
            _order.Add(current.Path);
        }

        _agreed[key] = current.Hash;
        Write();

        return current;
    }

    /// <summary>Forgets one assembly.</summary>
    /// <param name="path">The assembly's path.</param>
    /// <returns>True when it was recorded.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    public bool Forget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Full(path);

        if (!_agreed.Remove(Key(full)))
        {
            return false;
        }

        _ = _order.RemoveAll(recorded => string.Equals(Key(recorded), Key(full), StringComparison.Ordinal));
        Write();
        return true;
    }

    /// <summary>Forgets every decision.</summary>
    /// <remarks>
    /// Offered because a trust store with no way to revoke is a trust store nobody should use.
    /// </remarks>
    public void Forget()
    {
        _agreed.Clear();
        _order.Clear();
        Write();
    }

    private static string Key(string full) => full.ToUpperInvariant();

    private static string Full(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception failure) when (failure is ArgumentException or IOException or NotSupportedException)
        {
            return path;
        }
    }

    private void Read()
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        try
        {
            foreach (string line in File.ReadAllLines(_path))
            {
                // "<hash> <path>", the path last because it is the part that contains spaces.
                int split = line.IndexOf(' ', StringComparison.Ordinal);

                if (split <= 0 || split + 1 >= line.Length)
                {
                    continue;
                }

                string recorded = Full(line[(split + 1)..]);

                if (!_agreed.ContainsKey(Key(recorded)))
                {
                    _order.Add(recorded);
                }

                _agreed[Key(recorded)] = line[..split];
            }
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            // An unreadable store is an empty one, and an empty one asks the user. Failing towards
            // asking is the only safe direction for a trust decision.
        }
    }

    private void Write()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            File.WriteAllLines(
                _path,
                _order.Select(path => string.Create(
                    CultureInfo.InvariantCulture, $"{_agreed[Key(path)]} {path}")));
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            // The decision still holds for this session; it is only not remembered for the next.
        }
    }

    private static string? DefaultPath()
    {
        try
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            return string.IsNullOrEmpty(root)
                ? null
                : Path.Combine(root, "Spark", "trusted-assemblies.txt");
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            return null;
        }
    }

    private static bool IsRecoverable(Exception failure) => failure is IOException
        or UnauthorizedAccessException
        or NotSupportedException
        or ArgumentException;
}
