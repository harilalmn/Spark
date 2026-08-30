using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// The completion engine's invariant, and what half-typed text actually does to it — `E6-T13`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The row asked for two things and measurement settled one of them differently.</b> The
/// invariant — completion and the compiler given the same references and the same imports — is
/// here and is the point. The other half was a port of CADScript's <c>ScriptTextRepair</c>, which
/// balances the delimiters a user has not closed yet so the parser can see past them. It was
/// written, and then measured against Roslyn with it and without it, and it made **no difference
/// to any case**: modern Roslyn recovers from an unclosed brace, bracket, parenthesis and lambda
/// on its own. It was deleted rather than kept as code nothing can falsify ([N46](../../docs/NOTES.md)).
/// </para>
/// <para>
/// <b>What the repair appeared to fix was a different bug entirely</b>, and these tests are what is
/// left after it was found: the completion service used to add a fresh Roslyn document per request
/// and never remove one, so the second request onwards it was looking at several sets of top-level
/// statements at once and answered with nothing. Every spike test made its own instance, so nothing
/// saw it — and an editor sends a request per keystroke.
/// </para>
/// </remarks>
public sealed class CompletionEngineTests
{
    /// <summary>
    /// <b>Completion still answers on the second request, and the tenth.</b> This is the
    /// regression test for the accumulating-document bug, and it is written as a loop because one
    /// call could never have caught it.
    /// </summary>
    [Fact]
    public async Task CompletionKeepsAnsweringAcrossRequests()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        const string Code = "return centre.";
        Dictionary<string, Type?> ports = new() { ["centre"] = typeof(Point3d) };

        for (int request = 0; request < 10; request++)
        {
            IReadOnlyList<ScriptCompletionItem> items = await completion.CompleteAsync(
                Code, Code.Length, ports, TestContext.Current.CancellationToken);

            Assert.Contains(
                "DistanceTo",
                items.Select(item => item.DisplayText),
                StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Completion answers inside text the user has not finished — an unclosed block, an unclosed
    /// call, a lambda body still being written. **How** it answers is Roslyn's business; that it
    /// answers is the guarantee, and it is asserted rather than assumed.
    /// </summary>
    [Theory]
    [InlineData("foreach (var p in points) { var d = centre.")]
    [InlineData("if (a) { while (b) { for (;;) { var d = centre.")]
    [InlineData("var f = new Func<int, int>(x => { var d = centre.")]
    [InlineData("var q = (1 + (2 * (3 - centre.")]
    public async Task CompletionAnswersInsideUnfinishedText(string code)
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        IReadOnlyList<ScriptCompletionItem> items = await completion.CompleteAsync(
            code,
            code.Length,
            new Dictionary<string, Type?> { ["centre"] = typeof(Point3d) },
            TestContext.Current.CancellationToken);

        Assert.Contains("DistanceTo", items.Select(item => item.DisplayText), StringComparer.Ordinal);
    }

    /// <summary>
    /// <b>The list and the compiler are given the same references and imports</b>, from one
    /// catalogue. A list built from a different set offers members of types the script cannot use
    /// and hides members of types it can, and the user believes it.
    /// </summary>
    [Fact]
    public async Task CompletionUsesTheSameCatalogueAsTheCompiler()
    {
        _ = typeof(Point3d).Assembly.Location;

        ReferenceCatalog catalogue = new();
        using ScriptCompletion completion = new(catalogue);

        // `Point3d` resolves with no `using` in the snippet, which is only true if the catalogue's
        // imports reached the completion project — the same imports the generated code gets.
        const string Code = "var p = new Point3d(1, 2, 3); var d = p.";

        IReadOnlyList<ScriptCompletionItem> items = await completion.CompleteAsync(
            Code, Code.Length, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("DistanceTo", items.Select(item => item.DisplayText), StringComparer.Ordinal);
    }
}
