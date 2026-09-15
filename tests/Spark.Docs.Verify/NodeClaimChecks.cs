using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Spark.Docs.Verify;

/// <summary>
/// Checks that every node the Dynamo parity register claims to have built actually exists
/// (`E5-T14`).
/// </summary>
/// <remarks>
/// <para>
/// <b>The parity manifest ends many of its rows with <i>Exposed as the node X.Y</i>, and until now
/// nothing checked that.</b> It is written by hand, on the day the row is closed, and it is the
/// sentence a reader trusts when asking <i>can a graph reach this?</i> — so a rename anywhere in
/// <c>Spark.Nodes.Core</c> turns it into a confident lie with no symptom.
/// </para>
/// <para>
/// <b>The rows are in <c>tests/corpus/dynamo-parity.tsv</c>, and saying so is not pedantry.</b>
/// This file used to call it <i>the register</i> throughout, which is right about what it is for
/// and wrong about where it lives — <c>docs/TASKS.md</c> is what <i>the register</i> means
/// everywhere else in this repository, and it contains one occurrence of the sentence. Somebody
/// checking this check's reach grepped the wrong file and got an answer that looked like a defect
/// (2026-09-15).
/// </para>
/// <para>
/// <b>That is not hypothetical.</b> <c>Mesh.Repair</c> was renamed to <c>Mesh.Repaired</c> an hour
/// after it was written, to match every other member of its type, and the claim in the register was
/// corrected by hand at the same time. It happened to be correct; nothing would have said so if it
/// had not been.
/// </para>
/// <para>
/// <b>This is what closes <c>E5-T14</c>'s question rather than counting nodes.</b> That row said in
/// as many words that <i>a node count with no target is not a criterion</i>, and waited for the
/// parity assessments to say what the target was. They have. The target is the manifest's claims,
/// and this is the check that they and the node library agree. <b>The row closed on it</b> on
/// 2026-09-15, without a count being the criterion at any point.
/// </para>
/// </remarks>
public sealed class NodeClaimChecks
{
    /// <summary>
    /// The sentence the register uses. The identifier ends where the identifier ends — some rows
    /// punctuate the claim and some do not, so a pattern anchored on a full stop would miss half.
    /// </summary>
    private static readonly Regex Claim =
        new(@"Exposed as the node ([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);

    private static readonly string Root = RepositoryRoot();

    /// <summary>Every node the register names is a public method of a public node type.</summary>
    [Fact]
    public void EveryClaimedNodeExists()
    {
        string manifest = File.ReadAllText(
            Path.Combine(Root, "tests", "corpus", "dynamo-parity.tsv"));

        List<string> missing = Missing(manifest, DynamoParityChecks.PublicMembers("Spark.Nodes.Core"));

        Assert.Empty(missing);
    }

    /// <summary>The manifest makes enough of these claims for the check to be worth having.</summary>
    /// <remarks>
    /// A check over a register that has stopped making claims passes forever. This is the floor
    /// under that: the number only ever goes up, and if it collapses somebody has changed the
    /// sentence the rows are written with and this check has quietly stopped reading them.
    /// </remarks>
    [Fact]
    public void TheRegisterClaimsNodesWorthChecking()
    {
        string manifest = File.ReadAllText(
            Path.Combine(Root, "tests", "corpus", "dynamo-parity.tsv"));

        Assert.True(
            Claim.Matches(manifest).Count >= 25,
            $"the manifest makes {Claim.Matches(manifest).Count} node claims; it made 29 when this "
            + "check was written and 28 a day later, so either rows have lost the sentence or the "
            + "pattern no longer matches it.");
    }

    /// <summary>
    /// <b>The check is not vacuous.</b> A claim naming a node that does not exist is reported; the
    /// same claim naming one that does is not.
    /// </summary>
    /// <remarks>
    /// Without this, an extraction pattern that matches nothing passes the whole register
    /// perfectly and guards nothing at all — which is what [N168](../../docs/NOTES.md) was written
    /// about one step before this check existed.
    /// </remarks>
    [Fact]
    public void TheCheckCatchesAClaimForANodeThatDoesNotExist()
    {
        Dictionary<string, HashSet<string>> library = new(StringComparer.Ordinal)
        {
            ["Spark.Nodes.Core.Widget"] = new(StringComparer.Ordinal) { "Spin" },
        };

        Assert.Single(Missing("… Exposed as the node Widget.Wobble.", library));
        Assert.Empty(Missing("… Exposed as the node Widget.Spin.", library));

        // A claim for a type the library does not have at all is reported too.
        Assert.Single(Missing("… Exposed as the node Gadget.Spin.", library));

        // And the sentence is read whether or not it is punctuated.
        Assert.Empty(Missing("… Exposed as the node Widget.Spin and nothing else", library));
    }

    /// <summary>The claims that name no node in the library.</summary>
    /// <param name="manifest">The register's text.</param>
    /// <param name="library">The node library's public types and their methods.</param>
    /// <returns>One line per claim that does not resolve.</returns>
    private static List<string> Missing(
        string manifest, Dictionary<string, HashSet<string>> library)
    {
        List<string> missing = [];

        foreach (Match match in Claim.Matches(manifest))
        {
            string type = "Spark.Nodes.Core." + match.Groups[1].Value;
            string method = match.Groups[2].Value;

            if (!library.TryGetValue(type, out HashSet<string>? members))
            {
                missing.Add($"{match.Groups[1].Value}.{method} — no such node type.");
                continue;
            }

            if (!members.Contains(method))
            {
                missing.Add($"{match.Groups[1].Value}.{method} — the type has no such method.");
            }
        }

        return missing;
    }

    /// <summary>The repository root, found by walking up to the solution file.</summary>
    /// <returns>The root.</returns>
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
