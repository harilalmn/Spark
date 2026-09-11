using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Spark.Host;

/// <summary>
/// A graph a session was editing when it stopped without a clean end (`E8-T13`).
/// </summary>
/// <param name="File">The working copy's file.</param>
/// <param name="Text">The graph, exactly as a <c>.spark</c> file would hold it.</param>
/// <param name="Origin">The file the graph belonged to, or null for one never saved.</param>
/// <param name="WrittenUtc">When the working copy was last written.</param>
public sealed record RecoveredGraph(string File, string Text, string? Origin, DateTime WrittenUtc);

/// <summary>
/// Working copies of the graphs being edited, kept so that a process that dies takes no work with
/// it (`E8-T13`, FR-72).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it has to exist.</b> A code block is the user's own C#, and C# can end the process with a
/// <see cref="StackOverflowException"/> that nothing can catch — R11. There is no orderly shutdown
/// to save in, so the only defence is to have saved already: a working copy is written after every
/// change, and a process that ends cleanly deletes its own.
/// </para>
/// <para>
/// <b>One file per session, named by a GUID, holding the process id.</b> Two windows are two
/// sessions and must not overwrite each other, and a second Spark starting while the first is
/// still open must not offer the first one's work back as though it had been lost. A copy whose
/// process is still running is somebody's live work, not a leftover.
/// </para>
/// <para>
/// <b>Written by replacing, never in place.</b> The text goes to a temporary file which then
/// replaces the copy, so a crash in the middle of a write leaves the previous copy rather than half
/// of the next one — and the crash this exists for is exactly the kind that arrives mid-write.
/// </para>
/// <para>
/// <b>Every failure is silent</b>, for <see cref="PreferenceFile"/>'s reason. A copy that cannot be
/// written is a copy the user does not get back after a crash they may never have; refusing to let
/// them edit because of it would be absurd.
/// </para>
/// </remarks>
public sealed class RecoveryStore
{
    private const string Extension = ".spark-recovery";

    private readonly string? _folder;
    private readonly Func<int, bool> _isRunning;

    private RecoveryStore(string? folder, Func<int, bool>? isRunning)
    {
        _folder = folder;
        _isRunning = isRunning ?? IsSparkRunning;
    }

    /// <summary>The store in the standard place, under the user's local application data.</summary>
    /// <returns>The store.</returns>
    public static RecoveryStore Default() => new(DefaultFolder(), isRunning: null);

    /// <summary>A store in an explicit folder, which is what a test wants.</summary>
    /// <param name="folder">The folder, or null to keep nothing at all.</param>
    /// <param name="isRunning">
    /// Whether the process that wrote a copy is still running, or null for the real check. A test
    /// passes its own, because the process that wrote a copy in a test is the test itself.
    /// </param>
    /// <returns>The store.</returns>
    public static RecoveryStore At(string? folder, Func<int, bool>? isRunning = null) => new(folder, isRunning);

    /// <summary>Where copies are kept, or null when they are not kept anywhere.</summary>
    public string? Folder => _folder;

    /// <summary>Writes a session's working copy, replacing the one before.</summary>
    /// <param name="session">The session the copy belongs to.</param>
    /// <param name="text">The graph, as a <c>.spark</c> file would hold it.</param>
    /// <param name="origin">The file the graph belongs to, or null for one never saved.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public void Keep(Guid session, string text, string? origin)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_folder is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_folder);

            string path = PathFor(session);
            string temporary = path + ".tmp";

            using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (Utf8JsonWriter writer = new(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("process", Environment.ProcessId);

                if (origin is null)
                {
                    writer.WriteNull("origin");
                }
                else
                {
                    writer.WriteString("origin", origin);
                }

                writer.WriteString("graph", text);
                writer.WriteEndObject();
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            // Editing goes on; this change is only not protected against a crash.
        }
    }

    /// <summary>Deletes a session's working copy — a clean save, a clean close, or nothing left to lose.</summary>
    /// <param name="session">The session.</param>
    public void Forget(Guid session)
    {
        if (_folder is null)
        {
            return;
        }

        try
        {
            string path = PathFor(session);

            File.Delete(path);
            File.Delete(path + ".tmp");
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            // A copy left behind is offered next time, which is the safe direction to be wrong in.
        }
    }

    /// <summary>
    /// The copies left by sessions that did not end cleanly and are not running now, newest first.
    /// </summary>
    /// <param name="except">This session, whose own copy is never a leftover.</param>
    /// <returns>The leftovers; empty when there are none or nothing is kept.</returns>
    public IReadOnlyList<RecoveredGraph> Leftovers(Guid except)
    {
        if (_folder is null || !Directory.Exists(_folder))
        {
            return [];
        }

        List<string> files;

        try
        {
            files = [.. Directory.EnumerateFiles(_folder, "*" + Extension)];
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            return [];
        }

        string own = PathFor(except);
        List<RecoveredGraph> found = [];

        foreach (string file in files)
        {
            if (!string.Equals(file, own, StringComparison.OrdinalIgnoreCase) && Read(file) is { } graph)
            {
                found.Add(graph);
            }
        }

        return [.. found.OrderByDescending(graph => graph.WrittenUtc)];
    }

    /// <summary>Deletes a leftover — restored, or turned down.</summary>
    /// <param name="graph">The leftover.</param>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is null.</exception>
    public void Discard(RecoveredGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        // A store that keeps nothing discards nothing, even when it is handed a file from elsewhere.
        if (_folder is null)
        {
            return;
        }

        try
        {
            File.Delete(graph.File);
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            // It will be offered again, which is a nuisance rather than a loss.
        }
    }

    private RecoveredGraph? Read(string file)
    {
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement root = parsed.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("graph", out JsonElement graph)
                || graph.ValueKind != JsonValueKind.String
                || graph.GetString() is not { } text)
            {
                return null;
            }

            // Somebody's live work, not a leftover.
            if (root.TryGetProperty("process", out JsonElement process)
                && process.TryGetInt32(out int id)
                && _isRunning(id))
            {
                return null;
            }

            string? origin = root.TryGetProperty("origin", out JsonElement written) && written.ValueKind == JsonValueKind.String
                ? written.GetString()
                : null;

            return new RecoveredGraph(file, text, origin, File.GetLastWriteTimeUtc(file));
        }
        catch (Exception failure) when (failure is JsonException || IsRecoverable(failure))
        {
            return null;
        }
    }

    private string PathFor(Guid session) => Path.Combine(_folder ?? string.Empty, session.ToString("n") + Extension);

    /// <summary>Whether a process id names a Spark that is still running.</summary>
    /// <remarks>
    /// <b>The name is compared as well as the id</b>, because process ids are reused: a crashed
    /// Spark's id can belong to an unrelated program by the next start, and treating that as a
    /// running Spark would hide the user's work from them for as long as the program stayed open.
    /// </remarks>
    private static bool IsSparkRunning(int id)
    {
        try
        {
            using Process process = Process.GetProcessById(id);

            if (process.HasExited)
            {
                return false;
            }

            using Process current = Process.GetCurrentProcess();

            return string.Equals(process.ProcessName, current.ProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception failure) when (failure is ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string? DefaultFolder()
    {
        try
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, "Spark", "recovery");
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
