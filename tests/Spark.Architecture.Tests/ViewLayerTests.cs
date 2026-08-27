using System;
using System.Collections.Generic;
using System.IO;

namespace Spark.Architecture.Tests;

/// <summary>
/// The half of ADR-0005's UI rule that the project graph cannot express: <b>views never touch
/// <c>Spark.Engine</c>; view models do.</b>
/// </summary>
/// <remarks>
/// <para>
/// <c>Spark.UI</c> legitimately references <c>Spark.Host</c>, and therefore transitively the
/// engine, so <see cref="ReferenceGraphTests"/> can say nothing about this. The rule is real
/// anyway: a control that constructs a node instance or starts an evaluation is doing engine work
/// on the thread it draws on, and the canvas is the one place in the product where that is both
/// easy to do and expensive to undo.
/// </para>
/// <para>
/// So this is a source scan, in the same spirit as the rest of this project: it reads the files as
/// text rather than referencing them, because a test that referenced them could not observe the
/// thing it is checking.
/// </para>
/// </remarks>
public sealed class ViewLayerTests
{
    private static readonly string[] ViewDirectories = ["Controls", "Views"];

    /// <summary>
    /// No file under <c>Spark.UI/Controls</c> or <c>Spark.UI/Views</c> names <c>Spark.Engine</c>.
    /// The seam is <c>Spark.UI/Graph/CanvasGraph.cs</c> and the view models; everything a control
    /// needs comes through one of those.
    /// </summary>
    [Fact]
    public void NoViewFileReferencesTheEngine()
    {
        List<string> offenders = [];

        foreach (string file in ViewFiles())
        {
            if (WithoutComments(file).Contains("Spark.Engine", StringComparison.Ordinal))
            {
                offenders.Add(Relative(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Views that reach into Spark.Engine: {string.Join(", ", offenders)}. "
            + "Add what you need to CanvasGraph or to a view model instead.");
    }

    /// <summary>
    /// The temporary placeholder node model is gone. It existed so the canvas could be built before
    /// the engine's model landed, and a canvas that still drew it would be a canvas that is not
    /// connected to anything.
    /// </summary>
    [Fact]
    public void ThePlaceholderNodeModelIsGone()
    {
        List<string> survivors = [];

        string ui = Path.Combine(RepositoryRoot(), "src", "Spark.UI");
        foreach (string file in Directory.EnumerateFiles(ui, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            if (File.ReadAllText(file).Contains("PlaceholderGraph", StringComparison.Ordinal))
            {
                survivors.Add(Relative(file));
            }
        }

        Assert.True(survivors.Count == 0, $"Placeholder model still referenced by: {string.Join(", ", survivors)}.");
    }

    /// <summary>
    /// A file's text with comment lines removed, so that a view <i>explaining</i> why it does not
    /// call the engine is not mistaken for one that does.
    /// </summary>
    /// <remarks>
    /// Line-based and deliberately crude. A precise answer needs a parser, and the only thing this
    /// has to survive is a doc comment naming the rule — which the very file the rule is about is
    /// the most likely place to find.
    /// </remarks>
    private static string WithoutComments(string file)
    {
        List<string> kept = [];

        foreach (string line in File.ReadAllLines(file))
        {
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("*", StringComparison.Ordinal)
                || trimmed.StartsWith("<!--", StringComparison.Ordinal))
            {
                continue;
            }

            kept.Add(line);
        }

        return string.Join('\n', kept);
    }

    private static IEnumerable<string> ViewFiles()
    {
        string ui = Path.Combine(RepositoryRoot(), "src", "Spark.UI");

        foreach (string directory in ViewDirectories)
        {
            string path = Path.Combine(ui, directory);
            Assert.True(Directory.Exists(path), $"Expected a view directory at {path}.");

            foreach (string file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                yield return file;
            }

            foreach (string file in Directory.EnumerateFiles(path, "*.axaml", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');

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
