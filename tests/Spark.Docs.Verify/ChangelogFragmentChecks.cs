using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Spark.Docs.Verify;

/// <summary>
/// The changelog fragments in <c>changelog.d/</c>, and the assembler that turns them into the
/// <i>What changed</i> section of a release (<c>E10-T12</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The assembler is the deliverable, so it is tested rather than reimplemented.</b> These
/// checks run <c>scripts/assemble-changelog.py</c> as a subprocess and read what it prints. The
/// alternative — a second implementation here, compared against the first, which is what
/// <see cref="ProgressDashboardChecks"/> does — is right when the generated file is committed and
/// the two can be reconciled on every run. Nothing here is committed: the section exists for the
/// minutes between cutting a release and publishing it, so there is nothing to reconcile against
/// and a second implementation would only be a second thing to get wrong.
/// </para>
/// <para>
/// <b>This is the first test in the repository that starts a process</b>, and the cost is that a
/// machine with no Python 3 fails it. That is a true statement about this repository rather than a
/// false failure: <c>scripts/build-progress.py</c> has to be run on every step under the client's
/// standing instruction of 2026-09-12, so Python is already a tool you cannot work here without.
/// <see cref="ThePythonInterpreterIsFound"/> says so in one sentence instead of letting four other
/// tests fail obscurely, and <c>ci.yml</c> asks for Python explicitly rather than relying on the
/// runner image happening to carry it.
/// </para>
/// </remarks>
public sealed class ChangelogFragmentChecks
{
    /// <summary>The interpreters to try, in order, and the first one that answers wins.</summary>
    /// <remarks>
    /// <c>python3</c> first because that is the one Linux has and the one that is never the
    /// Microsoft Store stub — on Windows a bare <c>python</c> can be an alias that prints nothing,
    /// opens the Store and exits non-zero, which is why the probe below runs <c>--version</c> and
    /// reads the answer rather than trusting that the executable resolved.
    /// </remarks>
    private static readonly string[] Interpreters = ["python3", "python", "py"];

    /// <summary>
    /// <b>A Python 3 interpreter is on the path.</b> Stated as its own check so that a machine
    /// without one gets a sentence naming what to install, rather than four assembler tests
    /// failing with a file-not-found from somewhere inside a process start.
    /// </summary>
    [Fact]
    public void ThePythonInterpreterIsFound()
    {
        Assert.True(
            Interpreter() is not null,
            "no Python 3 interpreter was found on PATH (tried " + string.Join(", ", Interpreters)
            + "). Python is needed by scripts/build-progress.py on every step and by "
            + "scripts/assemble-changelog.py at release; install Python 3 and re-run.");
    }

    /// <summary>
    /// <b>Every fragment in the repository is well-formed</b>, checked by the checker that will be
    /// run at release rather than by a rule written here that could differ from it.
    /// </summary>
    [Fact]
    public void EveryFragmentInTheRepositoryIsWellFormed()
    {
        (int exit, _, string error) = Assemble("--check", Path.Combine(RepositoryRoot(), "changelog.d"));

        Assert.True(exit == 0, "changelog.d/ has a malformed fragment:\n" + error);
    }

    /// <summary>
    /// <b>The fragments are grouped under their kind's heading, in a fixed order</b> — the kinds in
    /// the order the format declares, and within a kind by register row, numerically. Numerically
    /// matters: sorted as text, <c>E10</c> comes before <c>E2</c>, which is the order nobody means.
    /// </summary>
    [Fact]
    public void TheAssemblerGroupsFragmentsUnderHeadingsInAFixedOrder()
    {
        using TemporaryDirectory directory = new();

        // Written in an order no rule would produce, so that a passing assertion is about the
        // assembler's ordering rather than about the order they happened to arrive in.
        directory.Write("fixed-E5-T11-c.md", "A crash no longer happens.");
        directory.Write("removed-E1-T1-d.md", "An option is gone.\n\nUse the other one instead.");
        directory.Write("added-E10-T12-a.md", "A thing was added.");
        directory.Write("added-E2-T3-b.md", "An earlier thing was added.");

        (int exit, string output, string error) = Assemble(directory.Path);

        Assert.True(exit == 0, error);
        Assert.Equal(
            """
            ## What changed

            ### Added

            - An earlier thing was added.
            - A thing was added.

            ### Fixed

            - A crash no longer happens.

            ### Removed

            - An option is gone.

              Use the other one instead.

            """.ReplaceLineEndings("\n"),
            output.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// <b>An empty directory produces nothing at all, not an empty heading.</b> A release with no
    /// user-visible change is a real thing — a rebuild, a signing fix, a packaging correction — and
    /// a <i>What changed</i> heading with nothing under it reads as a bug in the release process.
    /// </summary>
    [Fact]
    public void AnEmptyDirectoryProducesNothingRatherThanAnEmptyHeading()
    {
        using TemporaryDirectory directory = new();

        // The README is always there and is the specification, never a fragment. A directory
        // holding only it is the state the repository is in for most of a release cycle.
        directory.Write("README.md", "# changelog.d\n\nThe format lives here.\n");

        (int exit, string output, string error) = Assemble(directory.Path);

        Assert.True(exit == 0, error);
        Assert.Equal(string.Empty, output);
    }

    /// <summary>
    /// <b>The checker is proved to fire.</b> A validator that has never rejected anything is a
    /// validator nobody has tested, and this one guards the only moment — release — when a
    /// malformed fragment costs something: it is either dropped silently from the notes or it
    /// stops the release.
    /// </summary>
    /// <param name="name">The fragment's file name.</param>
    /// <param name="body">Its content.</param>
    /// <param name="expected">A phrase the complaint has to contain, so the message is checked too.</param>
    [Theory]
    [InlineData("improved-E10-T12-a.md", "A thing got better.", "'improved' is not a kind")]
    [InlineData("added-a-thing.md", "A thing was added.", "not a fragment name")]
    [InlineData("added-E10-T12-a.md", "   \n\n  \n", "empty")]
    [InlineData("added-E10-T12-a.md", "Add support for widgets", "does not end in a full stop")]
    public void TheCheckerRefusesAMalformedFragment(string name, string body, string expected)
    {
        using TemporaryDirectory directory = new();

        directory.Write(name, body);

        (int exit, string output, string error) = Assemble("--check", directory.Path);

        Assert.True(exit != 0, "the checker accepted " + name + " and should not have.");
        Assert.Contains(expected, error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output);
    }

    /// <summary>
    /// <b>A malformed fragment stops the assembler too, not only the checker.</b> If assembling
    /// skipped what checking refuses, a release could be cut with a change quietly missing from its
    /// notes — the one failure mode that would make this whole directory worse than nothing.
    /// </summary>
    [Fact]
    public void TheAssemblerRefusesWhatTheCheckerRefuses()
    {
        using TemporaryDirectory directory = new();

        directory.Write("added-E10-T12-a.md", "A thing was added.");
        directory.Write("improved-E10-T13-b.md", "A thing got better.");

        (int exit, string output, _) = Assemble(directory.Path);

        Assert.True(exit != 0, "the assembler rendered a directory the checker refuses.");
        Assert.Equal(string.Empty, output);
    }

    /// <summary>
    /// <b>The README lists exactly the kinds the assembler accepts.</b> The specification a
    /// contributor reads and the list the script enforces are in different files and different
    /// languages; this is what stops them drifting apart, which is the ordinary fate of a format
    /// documented in one place and implemented in another.
    /// </summary>
    [Fact]
    public void TheReadmeDocumentsExactlyTheKindsTheAssemblerAccepts()
    {
        string script = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "assemble-changelog.py"));
        string readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "changelog.d", "README.md"));

        int start = script.IndexOf("KINDS = [", StringComparison.Ordinal);
        Assert.True(start >= 0, "scripts/assemble-changelog.py no longer declares KINDS.");

        string declaration = script[start..script.IndexOf(']', start)];
        List<string> kinds =
        [
            .. declaration
                .Split('"', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => part.All(char.IsLower) && part.Length > 2),
        ];

        Assert.Equal("added, changed, fixed, removed, deprecated", string.Join(", ", kinds));

        foreach (string kind in kinds)
        {
            Assert.Contains("`" + kind + "`", readme, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <b>The release path actually uses what this directory produces.</b> The fragments, the
    /// format and the assembler are all worth nothing if the notes never carry the section —
    /// which is exactly the state <c>E10-T12</c> found the repository in, with
    /// <c>CONTRIBUTING.md</c> requiring a fragment that nothing read.
    /// </summary>
    [Fact]
    public void TheReleaseNotesAndTheWorkflowCarryTheAssembledSection()
    {
        string notes = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "release-notes.md"));
        string workflow = File.ReadAllText(
            Path.Combine(RepositoryRoot(), ".github", "workflows", "release.yml"));

        Assert.Contains("{CHANGELOG}", notes, StringComparison.Ordinal);
        Assert.Contains("assemble-changelog.py", workflow, StringComparison.Ordinal);
        Assert.Contains("{CHANGELOG}", workflow, StringComparison.Ordinal);
    }

    /// <summary>Runs the assembler and returns what it did.</summary>
    /// <param name="arguments">The script's arguments; the last one is the fragment directory.</param>
    /// <returns>The exit code, standard output and standard error.</returns>
    private static (int Exit, string Output, string Error) Assemble(params string[] arguments)
    {
        string? interpreter = Interpreter();
        Assert.NotNull(interpreter);

        ProcessStartInfo start = new()
        {
            FileName = interpreter,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = RepositoryRoot(),
        };

        start.ArgumentList.Add(Path.Combine(RepositoryRoot(), "scripts", "assemble-changelog.py"));

        // The directory always arrives as --directory, so a caller writes the path and nothing
        // else. The script's default is the repository's own changelog.d, and a test that used it
        // by accident would be asserting about whatever is unreleased today.
        for (int i = 0; i < arguments.Length - 1; i++)
        {
            start.ArgumentList.Add(arguments[i]);
        }

        start.ArgumentList.Add("--directory");
        start.ArgumentList.Add(arguments[^1]);

        using Process process = Assert.IsType<Process>(Process.Start(start));

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(60_000), "the assembler did not finish within a minute.");

        return (process.ExitCode, output, error);
    }

    /// <summary>The first interpreter that answers <c>--version</c> with a Python 3, or null.</summary>
    private static string? Interpreter()
    {
        foreach (string candidate in Interpreters)
        {
            try
            {
                ProcessStartInfo start = new()
                {
                    FileName = candidate,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };

                start.ArgumentList.Add("--version");

                using Process? probe = Process.Start(start);

                if (probe is null)
                {
                    continue;
                }

                string answer = probe.StandardOutput.ReadToEnd() + probe.StandardError.ReadToEnd();

                if (!probe.WaitForExit(30_000))
                {
                    continue;
                }

                if (probe.ExitCode == 0 && answer.Contains("Python 3", StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not on PATH. Try the next one; this is the ordinary case on any given machine,
                // since no machine has all three.
            }
        }

        return null;
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

    /// <summary>A directory of fragments that exists for one test.</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "spark-changelog-" + Guid.NewGuid().ToString("n"));

            Directory.CreateDirectory(Path);
        }

        /// <summary>Where it is.</summary>
        public string Path { get; }

        /// <summary>Writes one file into it, with LF endings whatever the platform prefers.</summary>
        /// <param name="name">The file name.</param>
        /// <param name="content">Its content.</param>
        public void Write(string name, string content) =>
            File.WriteAllText(
                System.IO.Path.Combine(Path, name),
                content.ReplaceLineEndings("\n"),
                // No byte-order mark. `Encoding.UTF8` writes one, Python's `open` does not strip
                // it, and it would arrive at the front of the first bullet of the assembled
                // section as an invisible character that only the equality assertion can see.
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A temporary directory that outlives the run is untidy, not wrong, and failing a
                // green test because the file system was busy would be the worse trade.
            }
        }
    }
}
