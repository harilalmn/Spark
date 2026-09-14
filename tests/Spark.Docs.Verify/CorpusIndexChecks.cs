using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spark.Docs.Verify;

/// <summary>
/// Every file in <c>tests/corpus/</c> is named in that directory's <c>README.md</c>
/// (<c>E11-T17</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A corpus does not rot by holding a wrong fixture. It rots by holding one nobody remembers
/// the purpose of.</b> A golden with no provenance cannot be judged when it goes red — is the code
/// wrong, or was the golden wrong when it was captured? — cannot be regenerated, because nobody
/// knows what produced it, and cannot be retired, because nobody knows what would break. It
/// survives every review, because reviewing it requires exactly the knowledge it failed to record.
/// </para>
/// <para>
/// <b>So the index is the artefact and this is what keeps it honest.</b> The README names each
/// file, what reads it, and where it came from; this check fails when a file arrives without a
/// row. It is deliberately one-directional: a row naming a file that has been deleted is caught
/// by <c>DocumentationChecks.EveryRelativeLinkResolves</c> only if the row links to it, so
/// <see cref="EveryIndexedFileExists"/> closes the other half here rather than relying on that.
/// </para>
/// <para>
/// <b>Why the README rather than a manifest.</b> The corpus already has two manifests and they
/// are read by machines. This index is read by a person deciding whether a fixture still earns
/// its place, which is a judgement no format helps with — and a file a person will actually read
/// is worth more than a schema they will not.
/// </para>
/// </remarks>
public sealed class CorpusIndexChecks
{
    /// <summary>
    /// Artefacts a failing run writes beside a golden. Gitignored, so they are not the
    /// repository's content and must not be demanded of the index.
    /// </summary>
    private static readonly string[] TransientSuffixes = [".actual.png", ".diff.png", ".actual.tsv"];

    /// <summary>Every file in the corpus appears in the README.</summary>
    [Fact]
    public void EveryCorpusFileIsIndexed()
    {
        string readme = File.ReadAllText(Path.Combine(CorpusRoot(), "README.md"));

        List<string> unlisted =
        [
            .. CorpusFiles().Where(relative => !readme.Contains(relative, StringComparison.Ordinal)),
        ];

        Assert.True(
            unlisted.Count == 0,
            "files in tests/corpus/ with no row in its README.md:\n  " + string.Join("\n  ", unlisted)
            + "\n\nSay what reads it and where it came from. A fixture with no provenance cannot be "
            + "judged when it goes red, regenerated, or retired.");
    }

    /// <summary>
    /// <b>And every file the README names exists.</b> An index that outlives its files is the same
    /// failure pointing the other way, and it is the more likely one — deleting a fixture is
    /// easier than remembering the table.
    /// </summary>
    [Fact]
    public void EveryIndexedFileExists()
    {
        string root = CorpusRoot();
        List<string> missing = [];

        foreach (string line in File.ReadAllLines(Path.Combine(root, "README.md")))
        {
            // The index rows are the only ones whose first cell is a backticked path, and a path
            // here always carries an extension. Prose mentioning a file in a sentence is not a row
            // and is not required to resolve.
            if (!line.StartsWith("| `", StringComparison.Ordinal))
            {
                continue;
            }

            string candidate = line[3..].Split('`')[0];

            if (candidate.Contains('.', StringComparison.Ordinal) && !File.Exists(Path.Combine(root, candidate)))
            {
                missing.Add(candidate);
            }
        }

        Assert.True(missing.Count == 0, "README.md names files that are not there: " + string.Join(", ", missing));
    }

    /// <summary>
    /// <b>The check is looking at a corpus.</b> An empty walk would pass both assertions above
    /// while proving nothing ([N167](../../docs/NOTES.md)).
    /// </summary>
    [Fact]
    public void TheCorpusIsThere()
    {
        List<string> files = [.. CorpusFiles()];

        Assert.True(files.Count >= 10, $"only {files.Count} corpus files were found.");
        Assert.Contains("dynamo-parity.tsv", files);
        Assert.Contains("graphs/curves-2026-08-28.spark", files);
    }

    /// <summary>
    /// <b>The transient artefacts are excluded, and nothing else is.</b> A failing golden writes
    /// its result beside the golden; those files are gitignored and must not be demanded of the
    /// index — but the exclusion must not be wide enough to swallow a real fixture.
    /// </summary>
    [Fact]
    public void OnlyTheArtefactsOfAFailingRunAreExcluded()
    {
        Assert.True(Transient("geometry/cuboid.actual.tsv"));
        Assert.True(Transient("viewport/reference-scene.actual.png"));
        Assert.True(Transient("viewport/reference-scene.diff.png"));

        Assert.False(Transient("geometry/cuboid.tsv"));
        Assert.False(Transient("viewport/reference-scene.png"));
        Assert.False(Transient("graphs/curves-2026-08-28.spark"));
        Assert.False(Transient("dynamo-parity.tsv"));
    }

    private static bool Transient(string relative) =>
        TransientSuffixes.Any(suffix => relative.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every corpus file, relative to the corpus root, with forward slashes.</summary>
    private static IEnumerable<string> CorpusFiles()
    {
        string root = CorpusRoot();

        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(relative => relative != "README.md" && !Transient(relative))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    private static string CorpusRoot() => Path.Combine(RepositoryRoot(), "tests", "corpus");

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
