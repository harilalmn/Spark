using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The graphs in <c>tests/corpus/graphs/</c> — real files written by older builds — still open
/// against the real node library (<c>E11-T17</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>`AGENTS.md` has promised this fixture for a month.</b> Its migration rule says every
/// migration ships <i>with a golden-file test against a real old-version graph in
/// <c>tests/corpus/</c></i> — and there was no such graph, so the rule had nothing to stand on.
/// </para>
/// <para>
/// <b>What was missing is not a file but a provenance.</b>
/// <see cref="NodeAliasTests.AFileNamingTheOldKeyStillOpens"/> already proves the mechanism, and
/// proves it well — but against a synthetic library holding one synthetic renamed definition and a
/// JSON string typed inside the test. That shows the alias path works. It cannot show that
/// <b>the real library's aliases cover the renames this project actually made</b>, because the
/// renames are not in it.
/// </para>
/// <para>
/// <b>So the fixture is taken from git rather than written.</b>
/// <c>curves-2026-08-28.spark</c> is <c>docs/examples/curves.spark</c> exactly as it stood at
/// <c>a30e98c</c>, before <c>E2-T58</c> turned every <c>By</c> factory into <c>From</c> and
/// <c>E2-T60</c> turned <c>Centre</c> into <c>Center</c>. <b>Eight of its eleven node keys no
/// longer exist.</b> Opening it resolves eight aliases at once, from a file an older build really
/// wrote — which is the difference between testing a mechanism and testing a claim.
/// </para>
/// </remarks>
public sealed class CorpusGraphTests
{
    /// <summary>
    /// The keys in <c>curves-2026-08-28.spark</c> that no longer exist, and what they became.
    /// </summary>
    /// <remarks>
    /// <b>Written out rather than counted.</b> A test that asserted <i>eight aliases resolved</i>
    /// would pass on the day somebody quietly rewrote the corpus file's keys to the new spellings,
    /// which is the one edit that would destroy the fixture's whole value. Naming both ends makes
    /// that edit fail.
    /// </remarks>
    private static readonly (string Old, string New)[] Renames =
    [
        ("Circle.ByCentreRadius", "Circle.FromCenterRadius"),
        ("Colour.ByRgb", "Colour.FromRgb"),
        ("Display.ByGeometryColour", "Display.FromGeometryColour"),
        ("Ellipse.ByPlaneRadii", "Ellipse.FromPlaneRadii"),
        ("Plane.ByOriginNormal", "Plane.FromOriginNormal"),
        ("Point.ByCoordinates", "Point.FromCoordinates"),
        ("PolyLine.ByRegularPolygon", "PolyLine.FromRegularPolygon"),
    ];

    /// <summary>Every corpus graph opens against the real library, with every node resolved.</summary>
    [Fact]
    public void EveryCorpusGraphOpens()
    {
        NodeLibrary library = RealLibrary();
        List<string> files = [.. CorpusGraphs()];

        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            Graph graph = SparkFile.Read(File.ReadAllText(file)).Restore(library);

            Assert.True(
                graph.Nodes().Any(),
                $"{Path.GetFileName(file)} opened with no nodes at all.");
        }
    }

    /// <summary>
    /// <b>The August graph still names the old keys, and they still resolve.</b> Both halves
    /// matter: the first is what makes the fixture a fixture, the second is what it proves.
    /// </summary>
    [Fact]
    public void TheAugustGraphStillNamesTheOldKeysAndTheyStillResolve()
    {
        string path = Path.Combine(CorpusDirectory(), "curves-2026-08-28.spark");
        string text = File.ReadAllText(path);

        foreach ((string old, string _) in Renames)
        {
            Assert.Contains(
                old,
                text,
                StringComparison.Ordinal);
        }

        Graph graph = SparkFile.Read(text).Restore(RealLibrary());
        List<string> names = [.. graph.Nodes().Select(n => n.Definition.DisplayName)];

        foreach ((string old, string current) in Renames)
        {
            Assert.True(
                names.Contains(current, StringComparer.Ordinal),
                $"'{old}' did not resolve to '{current}'. The graph opened with: {string.Join(", ", names)}");
        }
    }

    /// <summary>
    /// <b>Saving heals the file, and the corpus copy is never the one that heals.</b> An opened
    /// graph written back names the current keys — that is the alias's purpose, a bridge rather
    /// than a second permanent name — and the fixture on disk keeps the old ones because nothing
    /// writes over it.
    /// </summary>
    [Fact]
    public void SavingTheAugustGraphWritesTodaysKeys()
    {
        string path = Path.Combine(CorpusDirectory(), "curves-2026-08-28.spark");
        Graph graph = SparkFile.Read(File.ReadAllText(path)).Restore(RealLibrary());

        string written = SparkFile.Write(GraphDocument.Capture(graph, _ => (10, 20)));

        foreach ((string old, string current) in Renames)
        {
            Assert.DoesNotContain(old, written, StringComparison.Ordinal);
            Assert.Contains(current, written, StringComparison.Ordinal);
        }

        // The file on disk is untouched. Said as an assertion because a test that rewrote its own
        // fixture would pass forever and prove nothing after the first run.
        Assert.Contains("Circle.ByCentreRadius", File.ReadAllText(path), StringComparison.Ordinal);
    }

    /// <summary>The node library the application actually builds, not a fixture.</summary>
    private static NodeLibrary RealLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(Assembly.Load("Spark.Nodes.Core")));
        return library;
    }

    private static IEnumerable<string> CorpusGraphs() =>
        Directory
            .EnumerateFiles(CorpusDirectory(), "*.spark")
            .OrderBy(f => f, StringComparer.Ordinal);

    private static string CorpusDirectory() =>
        Path.Combine(RepositoryRoot(), "tests", "corpus", "graphs");

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
