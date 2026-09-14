using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spark.Docs.Verify;

/// <summary>
/// No text file in this repository contains a control character (<c>E11-T17</c>,
/// [N132](../../docs/NOTES.md)).
/// </summary>
/// <remarks>
/// <para>
/// <b>N132 ends by saying this scan costs one command, and nobody ran it for a month.</b> The note
/// is called <i>A NUL byte in a source file compiles, and greps as binary</i>, and it was written
/// after a shell heredoc mangled a space into a <c>U+0000</c> inside a <c>char</c> literal — code
/// that compiled, passed every gate, and turned its own file into something <c>grep</c> would not
/// search.
/// </para>
/// <para>
/// <b>The note contained two of them.</b> One in the prose illustration, where the point was to
/// show the character. One — and this is the expensive one — <b>inside the code fence giving the
/// scan</b>, so the remedy the note handed the next reader was itself corrupted. The cost was
/// real and was paid on 2026-09-14: a census of <c>docs/NOTES.md</c> came back with 111 entries
/// against an actual 172, because <c>grep</c> had classified the file as binary and stopped
/// reporting. **A wrong count that looks like a count is worse than no count**, and it very nearly
/// went into the documents as a correction of a figure that had been right.
/// </para>
/// <para>
/// <b>Why a check rather than a habit.</b> A control character is invisible in every editor, is
/// legal in C# source, and breaks the tools quietly — <c>grep</c>, <c>git diff</c>, a code review
/// in a browser. Nothing else here would notice it, which is the same argument as every other
/// check in this harness, and the reason N132's *one command* needed to stop being a command
/// somebody remembers.
/// </para>
/// </remarks>
public sealed class ControlCharacterChecks
{
    /// <summary>
    /// Directories holding build output or tooling state, which are not the repository's text.
    /// </summary>
    /// <remarks>
    /// <b><c>licences/</c> is the interesting entry and is not an exception to the rule so much as
    /// a limit on our authority.</b> <c>LGPL-2.1.txt</c> contains fifteen form feeds, and they are
    /// correct: the FSF's own text uses them as page separators. It is a verbatim third-party
    /// licence, reproduced because <a href="https://www.gnu.org/licenses/">the licence requires
    /// it</a>, and editing one byte of a licence to satisfy a checker of ours would be the wrong
    /// way round. So the directory is skipped and the reason is written here rather than left for
    /// somebody to rediscover when the check first goes red on it.
    /// </remarks>
    private static readonly string[] SkippedDirectories =
        [".git", ".vs", "bin", "obj", "artifacts", "node_modules", "TestResults", "packages", "licences"];

    /// <summary>Extensions whose contents are bytes rather than text.</summary>
    /// <remarks>
    /// A short list on purpose. Every extension NOT here is scanned, so a new kind of text file
    /// is covered the day it arrives rather than the day somebody adds it to a list — which is the
    /// failure mode of every allow-list, and the opposite of what this check is for.
    /// </remarks>
    private static readonly string[] BinaryExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".ico", ".pdf", ".zip", ".dll", ".exe", ".ttf", ".woff", ".woff2"];

    /// <summary>Every text file in the repository is free of control characters.</summary>
    [Fact]
    public void NoTextFileContainsAControlCharacter()
    {
        List<string> found = [.. Offences(RepositoryRoot())];

        Assert.True(found.Count == 0, "control characters in text files:\n" + string.Join("\n", found));
    }

    /// <summary>
    /// <b>The scan looked at the repository and not at an empty directory.</b> A walk that matched
    /// nothing would pass this suite while proving nothing, which is the failure
    /// [N167](../../docs/NOTES.md) is about.
    /// </summary>
    [Fact]
    public void TheScanReachesTheRepository()
    {
        List<string> scanned = [.. TextFiles(RepositoryRoot())];

        Assert.True(scanned.Count > 500, $"only {scanned.Count} text files were scanned.");
        Assert.Contains(scanned, f => f.EndsWith("NOTES.md", StringComparison.Ordinal));
        Assert.Contains(scanned, f => f.EndsWith(".cs", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>The check is not vacuous.</b> Given a file with a NUL in it, it names the file and the
    /// offset; given the same file with the NUL written as an escape, it says nothing.
    /// </summary>
    [Fact]
    public void TheCheckCatchesAControlCharacter()
    {
        string directory = Path.Combine(Path.GetTempPath(), "spark-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string path = Path.Combine(directory, "sample.cs");

            File.WriteAllBytes(path, "var x = '\0';\n"u8.ToArray());
            string offence = Assert.Single(Offences(directory));
            Assert.Contains("sample.cs", offence, StringComparison.Ordinal);
            Assert.Contains("0x00", offence, StringComparison.Ordinal);

            // Tabs, newlines and carriage returns are text and must not be reported, or the check
            // would fail on every file in the repository and be switched off within a day.
            File.WriteAllBytes(path, "var x = '\\0';\r\n\tvar y = 1;\n"u8.ToArray());
            Assert.Empty(Offences(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Every control character in every text file below a root, as readable lines.</summary>
    /// <param name="root">The directory to walk.</param>
    /// <returns>One line per offending byte, naming the file, the offset and the byte.</returns>
    private static IEnumerable<string> Offences(string root)
    {
        foreach (string file in TextFiles(root))
        {
            byte[] bytes = File.ReadAllBytes(file);

            for (int i = 0; i < bytes.Length; i++)
            {
                if (IsControl(bytes[i]))
                {
                    yield return $"{Path.GetRelativePath(root, file).Replace('\\', '/')} at byte {i}: 0x{bytes[i]:X2}";
                }
            }
        }
    }

    /// <summary>Tab, newline and carriage return are text; everything below 0x20 else is not.</summary>
    private static bool IsControl(byte value) =>
        value is not ((byte)'\t' or (byte)'\n' or (byte)'\r') && value < 0x20;

    private static IEnumerable<string> TextFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !Skipped(root, f))
            .OrderBy(f => f, StringComparer.Ordinal);

    private static bool Skipped(string root, string file)
    {
        if (BinaryExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        string relative = Path.GetRelativePath(root, file).Replace('\\', '/');

        return relative.Split('/').Any(segment => SkippedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
