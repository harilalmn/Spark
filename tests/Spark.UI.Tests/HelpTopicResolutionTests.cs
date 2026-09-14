using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spark.Api.Help;
using Spark.Engine;
using Spark.Host;

namespace Spark.UI.Tests;

/// <summary>
/// Every help topic id the application can send a reader to resolves to a topic it can serve
/// (<c>E10</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one hole the documentation harness has, and it is there on purpose.</b>
/// <c>DocumentationChecks.IsHelpTopicId</c> <i>skips</i> topic ids rather than checking them,
/// because generated topics — <c>diagnostics.SPK1010</c>, a page per node — have no file on disk,
/// and demanding one would mean committing generated pages, which <c>E10-T5</c> exists to avoid.
/// The skip is right. <b>The absence of any positive check beside it is the hole</b>: a diagnostic
/// pinned to a topic that does not exist sends a stuck user nowhere, and nothing anywhere in this
/// repository would say so.
/// </para>
/// <para>
/// <b>It has already happened once.</b> Five diagnostic codes pointed at <c>concepts.evaluation</c>
/// before that topic was written (<c>E10-T3</c>, 2026-08-31). It was noticed by a person reading
/// the register, not by a gate — and <c>docs/TODO.md</c> then carried the fault as outstanding for
/// a fortnight after it was fixed, which is the other half of having no check.
/// </para>
/// <para>
/// <b>Why it lives here and not in the harness.</b> <c>Spark.Docs.Verify</c> deliberately
/// references no Spark project, so it can see the files and not the catalog — and a topic id is
/// only meaningful against the catalog, where hand-written and generated topics share one index.
/// So this test builds the library <b>the way the application builds it</b>:
/// <c>MainWindowViewModel.Help()</c>'s three steps, in its order. A check that assembled a
/// different library would be checking a different application.
/// </para>
/// <para>
/// <b>What this adds over <see cref="HelpTopicSchemaTests"/>.</b> That suite reads the thirteen
/// concept files as text and checks their <c>related:</c> entries against each other. It cannot
/// see a generated page, and the generated node pages carry <c>related</c> ids of their own —
/// <c>concepts.lacing</c>, <c>concepts.code-blocks</c> — written in C# rather than in front
/// matter. Those are the ids no file-level check can reach.
/// </para>
/// </remarks>
public sealed class HelpTopicResolutionTests
{
    /// <summary>
    /// <b>Every <c>SPK####</c> code lands somewhere.</b> This is the user's journey: a code appears
    /// in the inspector, they follow it, and a topic opens.
    /// </summary>
    [Fact]
    public void EveryDiagnosticCodeResolvesToATopicTheLibraryCanServe()
    {
        HelpLibrary library = AsTheApplicationBuildsIt();
        List<string> dangling = [];

        foreach (string code in DiagnosticCodes.All)
        {
            string? topic = DiagnosticCodes.TopicFor(code);

            if (string.IsNullOrWhiteSpace(topic))
            {
                dangling.Add($"{code} -> (no topic)");
            }
            else if (!library.TryGet(topic, out _))
            {
                dangling.Add($"{code} -> {topic}");
            }
        }

        Assert.True(
            dangling.Count == 0,
            "diagnostic codes whose help topic the library cannot serve: " + string.Join(", ", dangling));
    }

    /// <summary>
    /// <b>Every <c>related</c> id in the assembled library resolves</b> — including the ones the
    /// generators write, which no file-level check can see.
    /// </summary>
    [Fact]
    public void EveryRelatedIdInTheAssembledLibraryResolves()
    {
        HelpLibrary library = AsTheApplicationBuildsIt();

        List<string> dangling = [.. Dangling(library)];

        Assert.True(
            dangling.Count == 0,
            "related ids naming a topic the library cannot serve: " + string.Join(", ", dangling));
    }

    /// <summary>
    /// <b>The library under test is the whole one.</b> A check over an empty or file-only library
    /// would pass every assertion above while proving nothing, which is the failure
    /// [N167](../../docs/NOTES.md) is about.
    /// </summary>
    [Fact]
    public void TheLibraryHasBothKindsOfTopicInIt()
    {
        HelpLibrary library = AsTheApplicationBuildsIt();

        Assert.True(library.TryGet("concepts.lacing", out _), "the hand-written topics did not load.");
        Assert.True(library.TryGet("nodes.index", out _), "the generated node index is not in the library.");
        Assert.True(library.TryGet("diagnostics.index", out _), "the generated diagnostic index is not in the library.");

        Assert.True(
            library.Count > 100,
            $"the library holds {library.Count} topics, which is too few to contain a page per node.");
    }

    /// <summary>
    /// <b>The check is not vacuous.</b> Run against a library with one topic taken out, it names
    /// the ids that pointed at it; run against the real one, it names nothing.
    /// </summary>
    /// <remarks>
    /// Without this, an empty loop, an inverted condition or a <c>TryGet</c> that always succeeded
    /// would look exactly like a clean repository. The topic removed is
    /// <c>concepts.evaluation</c> because it is the one that was genuinely missing once.
    /// </remarks>
    [Fact]
    public void TheCheckCatchesATopicThatIsNotThere()
    {
        HelpLibrary complete = AsTheApplicationBuildsIt();
        HelpLibrary reduced = new();

        foreach (HelpDocument topic in complete.Topics.Where(t => t.Id != "concepts.evaluation"))
        {
            reduced.Add(topic);
        }

        Assert.DoesNotContain("concepts.evaluation", Dangling(complete));
        Assert.Contains("concepts.evaluation", Dangling(reduced).Select(d => d.Split(" -> ")[1]));

        List<string> codes =
        [
            .. DiagnosticCodes.All.Where(code =>
                DiagnosticCodes.TopicFor(code) is string topic && !reduced.TryGet(topic, out _)),
        ];

        Assert.NotEmpty(codes);
    }

    /// <summary>
    /// <b>A node with no code example takes the other branch, and it was unreachable from the core
    /// library.</b> Mutating <c>["concepts.lacing"]</c> in <c>NodeReference.For</c> to a topic that
    /// does not exist left every test here green: the importer gives every imported node a code
    /// example, so the whole core library takes the other arm of that ternary. The branch is live
    /// for custom nodes and code blocks, which have none.
    /// </summary>
    /// <remarks>
    /// <b>A second thing fell out of the same mutation and is worth stating rather than fixing
    /// quietly.</b> The page body asks <c>!string.IsNullOrWhiteSpace(definition.CodeExample)</c>
    /// before rendering the example, and the <c>related</c> ids ask <c>CodeExample is null</c>. A
    /// whitespace-only example therefore renders no example block and still claims
    /// <c>concepts.code-blocks</c> as related. Nothing produces one today, so this is an
    /// observation and not a defect - but the two tests differ, and a reader should know which one
    /// governs what.
    /// </remarks>
    [Fact]
    public void ANodeWithNoCodeExampleNamesRealTopicsToo()
    {
        NodeDefinition bare = new(
            NodeKey.Parse("Test/Bare.Node"),
            "Bare.Node",
            [],
            [new PortDefinition("value", typeof(double), 0)],
            _ => [1.0]);

        Assert.Null(bare.CodeExample);

        HelpLibrary library = AsTheApplicationBuildsIt();
        HelpDocument page = NodeReference.For(bare);
        library.Add(page);

        Assert.NotEmpty(page.Related);

        List<string> dangling = [.. Dangling(library)];

        Assert.True(
            dangling.Count == 0,
            "related ids naming a topic the library cannot serve: " + string.Join(", ", dangling));
    }

    /// <summary>Every related id in the library that names no topic in it, as "topic -> id".</summary>
    private static IEnumerable<string> Dangling(HelpLibrary library) =>
        from topic in library.Topics
        from related in topic.Related
        where !library.TryGet(related, out _)
        select $"{topic.Id} -> {related}";

    /// <summary>
    /// The library <c>MainWindowViewModel.Help()</c> builds, assembled the same way and in the same
    /// order: the hand-written topics from <c>docs/help/</c>, then a page per node, the node index,
    /// and a page per diagnostic code with its index.
    /// </summary>
    private static HelpLibrary AsTheApplicationBuildsIt()
    {
        using SparkSession session = new();
        HelpLibrary library = new();

        int loaded = library.LoadDirectory(Path.Combine(RepositoryRoot(), "docs", "help"));
        Assert.True(loaded > 0, "no hand-written topics loaded, so this suite would prove nothing.");
        Assert.Empty(library.Problems);

        library.AddRange(NodeReference.ForAll(session.Library));
        library.Add(NodeReference.Index(session.Library));
        library.AddRange(DiagnosticReference.ForAll());

        return library;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? here = new(AppContext.BaseDirectory);

        while (here is not null)
        {
            if (File.Exists(Path.Combine(here.FullName, "Spark.slnx")))
            {
                return here.FullName;
            }

            here = here.Parent;
        }

        throw new InvalidOperationException("the repository root was not found above " + AppContext.BaseDirectory);
    }
}
