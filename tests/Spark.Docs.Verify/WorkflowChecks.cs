using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spark.Docs.Verify;

/// <summary>
/// The properties every GitHub Actions workflow in this repository has to have (<c>E1-T28</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A workflow is the one kind of code here that nothing else checks.</b> It is not compiled,
/// has no tests of its own, and the only way to exercise it is to trigger it — which is exactly
/// how <c>E11-T14</c>'s <c>docs-freshness</c> job slept for seventeen days while the register
/// counted it as coverage. These are the claims the documents make about the workflows, asserted
/// against the files rather than believed.
/// </para>
/// <para>
/// <b>Deliberately a text check and not a YAML parse.</b> This harness has no package references
/// at all — that is its whole design, so that it cannot constrain what it observes — and adding a
/// YAML library to assert three keys would be a poor trade. The parsing here is narrow on purpose:
/// a top-level block is a line beginning at column zero, and everything indented under it belongs
/// to it. <see cref="TheReaderNoticesAMissingConcurrencyBlock"/> is what stops that narrowness
/// from becoming a check that passes by finding nothing.
/// </para>
/// </remarks>
public sealed class WorkflowChecks
{
    /// <summary>The workflows, by file name, with their text.</summary>
    /// <remarks>
    /// Enumerated rather than listed, so that a workflow added tomorrow is checked tomorrow
    /// instead of being exempt by omission — which is the failure mode of every hand-kept list.
    /// </remarks>
    private static IReadOnlyList<(string Name, string Text)> Workflows =>
    [
        .. Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), ".github", "workflows"), "*.yml")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f))),
    ];

    /// <summary>There are workflows to check. A glob that matched nothing would pass silently.</summary>
    [Fact]
    public void TheWorkflowsAreThere()
    {
        List<string> names = [.. Workflows.Select(w => w.Name)];

        Assert.Contains("ci.yml", names);
        Assert.Contains("nightly.yml", names);
        Assert.Contains("release.yml", names);
    }

    /// <summary>
    /// <b>Every workflow declares a concurrency group.</b> Without one, two runs of the same
    /// workflow on the same ref proceed in parallel — which is wasted minutes for a build, two
    /// hours of benchmarks measuring the same tree for the nightly, and a race for a release that
    /// publishes.
    /// </summary>
    [Fact]
    public void EveryWorkflowDeclaresAConcurrencyGroup()
    {
        List<string> missing =
        [
            .. Workflows
                .Where(w => TopLevelBlock(w.Text, "concurrency") is null)
                .Select(w => w.Name),
        ];

        Assert.True(missing.Count == 0, "workflows with no top-level concurrency block: " + string.Join(", ", missing));
    }

    /// <summary>
    /// <b>Only the build workflow cancels a run in progress, and the reason is not efficiency.</b>
    /// A superseded CI run is a record nobody reads. A cancelled <i>release</i> is an installer
    /// that was packed and a release that was never created, or a release created whose assets
    /// never arrived; a cancelled nightly is a measurement thrown away for no gain, since the
    /// overlap it would resolve means the previous run was still going.
    /// </summary>
    [Theory]
    [InlineData("ci.yml", "true")]
    [InlineData("nightly.yml", "false")]
    [InlineData("release.yml", "false")]
    public void TheWorkflowCancelsInProgressOrNotAsItShould(string name, string expected)
    {
        string block = Assert.IsType<string>(TopLevelBlock(Text(name), "concurrency"));

        Assert.Contains($"cancel-in-progress: {expected}", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Every workflow that restores packages caches them.</b> Minutes rather than correctness,
    /// and worth asserting only because it is the kind of line that gets dropped in an edit and
    /// costs nothing visible until somebody reads a bill or waits for a queue.
    /// </summary>
    [Theory]
    [InlineData("ci.yml")]
    [InlineData("nightly.yml")]
    [InlineData("release.yml")]
    public void TheWorkflowCachesNuGet(string name)
    {
        string text = Text(name);

        Assert.Contains("actions/cache", text, StringComparison.Ordinal);
        Assert.Contains("~/.nuget/packages", text, StringComparison.Ordinal);
        Assert.Contains("Directory.Packages.props", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The reader is not vacuous.</b> Given a workflow with no concurrency block it returns
    /// null, and given one that has it indented under a job — where it means something different
    /// and does not apply to the workflow — it does not mistake that for a top-level declaration.
    /// </summary>
    [Fact]
    public void TheReaderNoticesAMissingConcurrencyBlock()
    {
        Assert.Null(TopLevelBlock("name: X\non:\n  push:\njobs:\n  build:\n    runs-on: x\n", "concurrency"));

        Assert.Null(TopLevelBlock(
            "name: X\njobs:\n  build:\n    concurrency:\n      group: g\n      cancel-in-progress: true\n",
            "concurrency"));

        string found = Assert.IsType<string>(TopLevelBlock(
            "name: X\nconcurrency:\n  group: g\n  cancel-in-progress: false\njobs:\n  build:\n    runs-on: x\n",
            "concurrency"));

        Assert.Contains("cancel-in-progress: false", found, StringComparison.Ordinal);
        Assert.DoesNotContain("runs-on", found, StringComparison.Ordinal);
    }

    /// <summary>
    /// The lines of a top-level mapping key, or null when the document has no such key at the top
    /// level.
    /// </summary>
    /// <param name="text">The workflow's text.</param>
    /// <param name="key">The key, unindented.</param>
    /// <returns>The block including its own line, or null.</returns>
    private static string? TopLevelBlock(string text, string key)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        int start = Array.FindIndex(lines, line => line == key + ":");

        if (start < 0)
        {
            return null;
        }

        List<string> block = [lines[start]];

        for (int i = start + 1; i < lines.Length; i++)
        {
            // A blank line or a comment inside a block is part of it; anything at column zero ends
            // it. That is the whole of the rule, and it is why this is a text check rather than a
            // parser: YAML has more ways to write a mapping than this repository uses, and a check
            // that understood all of them would be a check nobody could read.
            if (lines[i].Length > 0 && !char.IsWhiteSpace(lines[i][0]))
            {
                break;
            }

            block.Add(lines[i]);
        }

        return string.Join('\n', block);
    }

    private static string Text(string name) =>
        Workflows.Single(w => w.Name == name).Text;

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
