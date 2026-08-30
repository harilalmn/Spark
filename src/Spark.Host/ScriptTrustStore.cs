using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Spark.Host;

/// <summary>
/// What the user has agreed to run, recorded per file and per exact content (`E6-T16`).
/// </summary>
/// <remarks>
/// <para>
/// <b>A Spark graph is executable code.</b> A code block is arbitrary C# running with the
/// application's own privileges, and .NET has no code-access security to sandbox it with — so
/// pretending a graph is a document would be dishonest. The posture this type implements is the
/// one stated in `E6-T16`: <b>a graph is never run because it was opened</b>. It is run because
/// somebody said so.
/// </para>
/// <para>
/// <b>Trust is keyed on the origin *and* the content, and both halves matter.</b> Keyed on the file
/// alone, a colleague who edits a shared graph gets the same trust the user granted to what it used
/// to say. Keyed on the content alone, trusting one graph would trust every copy of it anywhere,
/// which is precisely how a malicious graph would like to travel. Together, they mean *this file,
/// saying exactly this*.
/// </para>
/// <para>
/// <b>Editing your own graph does not re-prompt, because the store is not the only gate.</b> Trust
/// is asked for when a document is opened, and the user's own edits after that are their own
/// doing. Saving records the new content as trusted, which is what stops the application asking
/// somebody for permission to run the code they have just written.
/// </para>
/// <para>
/// <b>What this is not.</b> It is not a sandbox and does not pretend to be one. It records a
/// decision; it cannot constrain what the decision permits.
/// </para>
/// </remarks>
public sealed class ScriptTrustStore
{
    private readonly string? _path;
    private readonly HashSet<string> _entries = new(StringComparer.Ordinal);

    /// <summary>Creates a store under the user's local application data.</summary>
    public ScriptTrustStore() : this(DefaultPath())
    {
    }

    /// <summary>Creates a store at a chosen path.</summary>
    /// <param name="path">Where the record lives, or null to keep it in memory only.</param>
    /// <remarks>
    /// Null is a session that remembers nothing, which is what a test wants and what a host with
    /// nowhere to write gets. It is the safe direction to fail in: the user is asked again.
    /// </remarks>
    public ScriptTrustStore(string? path)
    {
        _path = path;

        Read();
    }

    /// <summary>How many decisions are recorded.</summary>
    public int Count => _entries.Count;

    /// <summary>Whether this exact content, from this exact origin, has been trusted.</summary>
    /// <param name="origin">The document's path, or null for one that has never been saved.</param>
    /// <param name="scripts">The source of every code block in it.</param>
    /// <returns>True when the user has already agreed to run this.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scripts"/> is null.</exception>
    /// <remarks>
    /// <b>An unsaved document is never trusted, and that is not a gap.</b> It has no origin, so
    /// there is nothing to key a decision to — and a graph the user is building in front of them is
    /// one they place the code blocks into themselves, which is a decision made a different way.
    /// </remarks>
    public bool IsTrusted(string? origin, IReadOnlyList<string> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);

        return origin is not null && _entries.Contains(Entry(origin, scripts));
    }

    /// <summary>Records that the user agreed to run this content from this origin.</summary>
    /// <param name="origin">The document's path.</param>
    /// <param name="scripts">The source of every code block in it.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public void Trust(string origin, IReadOnlyList<string> scripts)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(scripts);

        if (_entries.Add(Entry(origin, scripts)))
        {
            Write();
        }
    }

    /// <summary>Forgets every decision.</summary>
    /// <remarks>
    /// Offered because a trust store with no way to revoke is a trust store nobody should use.
    /// </remarks>
    public void Forget()
    {
        _entries.Clear();
        Write();
    }

    /// <summary>
    /// The line recorded for one document: the origin, and a hash of everything that would run.
    /// </summary>
    /// <remarks>
    /// The origin is a full path, normalised for case on Windows because the file system is; the
    /// scripts are hashed in document order, with line endings normalised, so a graph that has been
    /// through a text editor that changed them is still the same graph.
    /// </remarks>
    private static string Entry(string origin, IReadOnlyList<string> scripts)
    {
        StringBuilder content = new();

        foreach (string script in scripts)
        {
            content.Append(script.ReplaceLineEndings("\n")).Append('\u0000');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString()));

        string path;

        try
        {
            path = Path.GetFullPath(origin);
        }
        catch (Exception failure) when (failure is ArgumentException or IOException or NotSupportedException)
        {
            path = origin;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{path.ToUpperInvariant()} {Convert.ToHexString(hash)}");
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
                if (line.Length > 0)
                {
                    _entries.Add(line);
                }
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
            File.WriteAllLines(_path, _entries);
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

            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, "Spark", "trusted-scripts.txt");
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            return null;
        }
    }

    private static bool IsRecoverable(Exception failure) =>
        failure is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException;
}
