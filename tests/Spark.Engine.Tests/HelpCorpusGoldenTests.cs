using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spark.Api.Help;

namespace Spark.Engine.Tests;

/// <summary>
/// <c>HelpMarkdown</c> against the thirteen help topics that actually ship, and the golden files
/// that pin what it makes of them (<c>E11-T7</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this adds over <c>HelpTests</c>.</b> Those assert one construct at a time against a
/// snippet written in the test — a table, a heading, a run of inlines — which is the right way to
/// pin a rule and the wrong way to notice that a real topic stopped rendering as it did. Real
/// topics are where constructs meet: a link inside a bold run inside a table cell inside a section
/// nobody wrote a test for. **A golden over the real corpus is the only check that sees those**,
/// and it is the difference between *the parser handles tables* and *`lacing.md` still renders*.
/// </para>
/// <para>
/// <b>The corpus is the shipped documentation, not a fixture directory.</b> That is deliberate:
/// fixtures drift away from what the product contains, and the topics are already committed,
/// already reviewed, and already the thing a reader will see. The cost is that editing a topic
/// changes a golden, which is correct — the golden's job is to make that change visible in the
/// diff instead of invisible in a render.
/// </para>
/// </remarks>
public sealed class HelpCorpusGoldenTests
{
    /// <summary>The topics, by file name.</summary>
    public static TheoryData<string> Topics => [.. HelpCorpusGolden.Names()];

    /// <summary>Every topic renders as its golden says.</summary>
    [Theory]
    [MemberData(nameof(Topics))]
    public void TheTopicRendersAsItsGoldenSays(string name)
    {
        string? report = HelpCorpusGolden.Check(name);

        Assert.True(report is null, report);
    }

    /// <summary>Every topic has a golden committed beside it.</summary>
    /// <remarks>
    /// A missing golden makes <c>Check</c> report rather than throw, which is right for a first run
    /// and wrong for a green suite — and it is how a topic added without one would otherwise slip
    /// past unchecked.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Topics))]
    public void TheGoldenIsInTheRepository(string name)
    {
        string path = HelpCorpusGolden.PathFor(name);

        Assert.True(
            File.Exists(path),
            $"no golden at {path}; run with {HelpCorpusGolden.UpdateVariable}=1 and read it before committing.");
    }

    /// <summary>
    /// <b>The corpus is not empty and not a subset.</b> A theory over an empty collection is a
    /// green test that ran nothing, and a corpus that silently loses a topic reads exactly the
    /// same — [N167](../../docs/NOTES.md) is this project's note about checks that pass by finding
    /// nothing.
    /// </summary>
    [Fact]
    public void EveryShippedTopicIsInTheCorpus()
    {
        IReadOnlyList<string> names = HelpCorpusGolden.Names();

        Assert.Equal(13, names.Count);
        Assert.Contains("lacing", names);
        Assert.Contains("code-blocks", names);
    }

    /// <summary>
    /// <b>The checker is not vacuous, and this is the test that says so.</b> A golden suite that
    /// passes because its comparison never reports is the failure this project has a note about,
    /// so the comparison is exercised against a rendering deliberately changed.
    /// </summary>
    [Fact]
    public void TheComparisonReportsAChangedPartAndNamesBothSides()
    {
        IReadOnlyList<(string Part, string Detail)> golden = Rendering("lacing");
        List<(string Part, string Detail)> actual = [.. golden];

        actual[1] = ("title", "Something else entirely");

        string report = Assert.IsType<string>(HelpCorpusGolden.Compare(golden, actual));

        Assert.Contains("title", report, StringComparison.Ordinal);
        Assert.Contains("Something else entirely", report, StringComparison.Ordinal);
        Assert.Contains("1 of", report, StringComparison.Ordinal);
    }

    /// <summary>An identical pair reports nothing, so the comparison is not simply always red.</summary>
    [Fact]
    public void AnIdenticalPairReportsNoDifference()
    {
        IReadOnlyList<(string Part, string Detail)> rendering = Rendering("lacing");

        Assert.Null(HelpCorpusGolden.Compare(rendering, rendering));
    }

    /// <summary>
    /// <b>The rendering carries link targets, which is the half a plain-text golden would lose.</b>
    /// A topic whose cross-references all resolved somewhere else would render with identical
    /// visible text, and a golden over the text alone would hold.
    /// </summary>
    [Fact]
    public void TheRenderingRecordsLinkTargetsAndNotOnlyLinkText()
    {
        string rendered = string.Join(
            "\n",
            Rendering("lacing").Select(line => line.Part + "\t" + line.Detail));

        Assert.Contains("[link->", rendered, StringComparison.Ordinal);
    }

    /// <summary>A rendering survives being written and read back unchanged.</summary>
    [Fact]
    public void ARenderingSurvivesATextRoundTrip()
    {
        IReadOnlyList<(string Part, string Detail)> rendering = Rendering("lacing");

        Assert.Equal(rendering, HelpCorpusGolden.Parse(HelpCorpusGolden.ToText("lacing", rendering)));
    }

    /// <summary>
    /// <b>A golden whose header has moved will not parse.</b> The other two tabular fixtures in
    /// this repository assert their header literally for the same reason: a column reordered by
    /// hand and read as though it had not moved is a green suite checking the wrong thing.
    /// </summary>
    [Fact]
    public void AGoldenWithTheWrongHeaderRefusesToParse()
    {
        string good = HelpCorpusGolden.ToText("lacing", Rendering("lacing"));

        Assert.NotEmpty(HelpCorpusGolden.Parse(good));

        InvalidDataException reordered = Assert.Throws<InvalidDataException>(
            () => HelpCorpusGolden.Parse(good.Replace("Part\tDetail", "Detail\tPart", StringComparison.Ordinal)));

        Assert.Contains("Part<tab>Detail", reordered.Message, StringComparison.Ordinal);

        InvalidDataException headless = Assert.Throws<InvalidDataException>(
            () => HelpCorpusGolden.Parse("# nothing but a comment\n"));

        Assert.Contains("no header", headless.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A passing run deletes the sidecar a failing one left behind</b> ([N177](../../docs/NOTES.md)).
    /// The geometry goldens shipped this defect and it is not worth shipping twice: the file is
    /// gitignored, so nothing else would ever report it stale.
    /// </summary>
    [Fact]
    public void APassingCheckRemovesTheSidecarAFailingOneLeft()
    {
        string sidecar = HelpCorpusGolden.SidecarFor(HelpCorpusGolden.PathFor("lacing"));

        File.WriteAllText(sidecar, "# left over from a failure that has since been fixed" + Environment.NewLine);
        Assert.True(File.Exists(sidecar));

        Assert.Null(HelpCorpusGolden.Check("lacing"));
        Assert.False(File.Exists(sidecar), $"{sidecar} survived a passing run.");
    }

    private static IReadOnlyList<(string Part, string Detail)> Rendering(string name) =>
        HelpCorpusGolden.Render(HelpMarkdown.Parse(HelpCorpusGolden.Source(name), name));
}
