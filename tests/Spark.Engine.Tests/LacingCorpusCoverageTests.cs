using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spark.Engine.Tests;

/// <summary>
/// Checks the corpus against the document it was transcribed from, in both directions.
/// </summary>
/// <remarks>
/// <para>
/// The lesson being applied prophylactically is <c>DoodleSharp</c>'s: a hand-maintained mapping
/// between a document and code drifts in <b>both</b> directions at once, and neither direction is
/// visible until somebody writes the diff. There, 101 of 108 public constructors rendered blank
/// while seven carefully written entries pointed at members that no longer existed.
/// </para>
/// <para>
/// So: every case number in <c>lacing.md</c> has a row in the corpus, and every row in the corpus
/// corresponds to a case number in <c>lacing.md</c>. Adding a case to the document and forgetting
/// to test it is a red build, and so is a test asserting behaviour the specification no longer
/// describes.
/// </para>
/// </remarks>
public sealed class LacingCorpusCoverageTests
{
    private static readonly Regex CaseRow = new(@"^\| *(\d+) *\|", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Every case the specification names is in the corpus, and nothing else is.</summary>
    [Fact]
    public void TheCorpusAndTheSpecificationNameTheSameCases()
    {
        HashSet<int> documented = [.. CaseRow.Matches(File.ReadAllText(SpecificationPath()))
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))];

        HashSet<int> tested = [.. LacingCaseTable.AllNumbers];

        int[] untested = [.. documented.Except(tested).Order()];
        int[] undocumented = [.. tested.Except(documented).Order()];

        Assert.True(
            untested.Length == 0,
            $"lacing.md names cases the corpus does not run: {string.Join(", ", untested)}.");

        Assert.True(
            undocumented.Length == 0,
            $"The corpus runs cases lacing.md does not name: {string.Join(", ", undocumented)}.");
    }

    private static string SpecificationPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string path = Path.Combine(directory!.FullName, "docs", "help", "concepts", "lacing.md");

        Assert.True(File.Exists(path), $"Expected the lacing specification at {path}.");
        return path;
    }
}
