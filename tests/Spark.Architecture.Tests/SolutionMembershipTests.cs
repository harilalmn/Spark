using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Spark.Architecture.Tests;

/// <summary>
/// Guards the one thing every other gate is built on: that the solution and the working tree
/// agree about which projects exist.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because both halves failed at once, and neither was visible.</b> On
/// 2026-09-09 <c>tests/Spark.Geometry.Io.Tests/</c> held a <c>bin/</c> and an <c>obj/</c> and
/// no project: the tests in it had been folded into <c>Spark.Geometry.Tests</c> and the
/// directory deleted from git, but the build output is gitignored and stayed on disk. The
/// standing verification loop in <c>AGENTS.md</c> runs
/// <c>tests/&lt;name&gt;/bin/Debug/net10.0/&lt;name&gt;.exe</c> for every directory under
/// <c>tests/</c>, so it kept running an eleven-day-old executable and adding its twelve
/// passing tests to the total. <b>A green run that includes a deleted project is not a green
/// run.</b>
/// </para>
/// <para>
/// The mirror failure is the more dangerous one and the same assertion catches it: a test
/// project added to the tree and not to <c>Spark.slnx</c> is built by nothing, checked by
/// <c>dotnet format</c> never, and reports no failures because it reports nothing at all.
/// That is the shape N30 describes — a suite whose silence reads as success.
/// </para>
/// </remarks>
public sealed class SolutionMembershipTests
{
    /// <summary>
    /// Every test project on disk is in <c>Spark.slnx</c>, and every test project in
    /// <c>Spark.slnx</c> is on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted in both directions rather than as one containment, because the two failures are
    /// opposite and the message has to say which happened: a project with no solution entry is
    /// one to register, a solution entry with no project is one to remove.
    /// </para>
    /// <para>
    /// <b>Membership is decided by the presence of a <c>.csproj</c>, not by the presence of a
    /// directory.</b> <c>tests/corpus/</c> is data — graphs the suites read — and has no project
    /// in it by design. Asserting over directories flagged it, which would have made the guard
    /// something to suppress within a day of writing it.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryTestProjectIsInTheSolution()
    {
        string tests = Path.Combine(RepositoryRoot(), "tests");

        string[] onDisk = Directory.EnumerateDirectories(tests)
            .Where(directory => Directory.EnumerateFiles(directory, "*.csproj").Any())
            .Select(directory => Path.GetFileName(directory)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] inSolution = TestProjectsInSolution()
            .Order(StringComparer.Ordinal)
            .ToArray();

        List<string> problems = [];

        foreach (string directory in onDisk.Except(inSolution, StringComparer.Ordinal))
        {
            problems.Add(
                $"tests/{directory}/ holds a project that Spark.slnx does not name, so no gate "
                + "builds it, dotnet format never sees it, and it reports no failures because it "
                + "reports nothing at all. Register it.");
        }

        foreach (string project in inSolution.Except(onDisk, StringComparer.Ordinal))
        {
            problems.Add($"Spark.slnx names tests/{project}/, which holds no project file.");
        }

        Assert.Empty(problems);
    }

    /// <summary>
    /// No directory under <c>tests/</c> holds build output without a project beside it.
    /// </summary>
    /// <remarks>
    /// A narrower restatement of the first assertion, kept separate because it names the
    /// specific artefact — a <c>bin</c> with no <c>.csproj</c> — that the verification loop
    /// will happily execute. The first test tells you the solution is wrong; this one tells
    /// you there is an executable on disk that nothing in the repository produces.
    /// </remarks>
    [Fact]
    public void NoTestDirectoryHoldsBuildOutputWithoutAProject()
    {
        string tests = Path.Combine(RepositoryRoot(), "tests");
        List<string> orphans = [];

        foreach (string directory in Directory.EnumerateDirectories(tests))
        {
            bool hasProject = Directory.EnumerateFiles(directory, "*.csproj").Any();
            bool hasOutput = Directory.Exists(Path.Combine(directory, "bin"));

            if (hasOutput && !hasProject)
            {
                orphans.Add(Path.GetFileName(directory)!);
            }
        }

        Assert.Empty(orphans);
    }

    /// <summary>
    /// The test project names <c>Spark.slnx</c> carries under <c>tests/</c>.
    /// </summary>
    /// <returns>The directory name of each, which is also the project name.</returns>
    private static string[] TestProjectsInSolution()
    {
        string solution = Path.Combine(RepositoryRoot(), "Spark.slnx");

        return XDocument.Load(solution)
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Replace('\\', '/'))
            .Where(path => path.StartsWith("tests/", StringComparison.Ordinal))
            .Select(path => path.Split('/')[1])
            .ToArray();
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
