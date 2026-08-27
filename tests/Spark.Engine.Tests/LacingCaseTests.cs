using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The case table in <c>docs/help/concepts/lacing.md</c> §6, run against the replicator.
/// </summary>
/// <remarks>
/// The specification was written before the engine existed, and this is the test that makes that
/// worth having: a disagreement between the two is informative, because the table did not come from
/// the implementation.
/// </remarks>
public sealed class LacingCaseTests
{
    /// <summary>
    /// Every row of the case table: the produced value, and — <b>separately</b> — the produced rank.
    /// </summary>
    /// <remarks>
    /// The rank assertion runs before the value assertion on purpose. A rank bug that a value-only
    /// test survives is the whole reason the Rank column exists, and putting rank first means a
    /// failure names the rank rather than the first leaf that happened to move.
    /// </remarks>
    /// <param name="caseNumber">The case number from the table.</param>
    [Theory]
    [MemberData(nameof(CaseNumbers))]
    public void LacingCaseMatchesTheSpecification(int caseNumber)
    {
        LacingCase expected = LacingCaseTable.Case(caseNumber);

        ReplicationResult result = Replicator.Replicate(expected.Node, expected.Mode, expected.Inputs, TestContext.Current.CancellationToken);

        if (expected.Expected is null)
        {
            Assert.False(result.HasOutput, $"Case {caseNumber} ({expected.Description}) should have produced no output.");
        }
        else
        {
            Assert.True(result.HasOutput, $"Case {caseNumber} ({expected.Description}) produced no output. Diagnostics: {Render(result.Diagnostics)}");
            Assert.Equal(expected.Expected.Length, result.Outputs.Count);

            for (int port = 0; port < expected.Expected.Length; port++)
            {
                int actualRank = SparkList.RankOf(result.Outputs[port]);
                Assert.True(
                    expected.ExpectedRanks[port] == actualRank,
                    $"Case {caseNumber} ({expected.Description}), output port {port}: expected rank {expected.ExpectedRanks[port]}, produced rank {actualRank}. Value was {GraphValues.Describe(result.Outputs[port])}.");

                GraphValues.AssertEqual(expected.Expected[port], result.Outputs[port]);
            }
        }

        if (expected.Code is null)
        {
            Assert.True(
                result.Diagnostics.Count == 0,
                $"Case {caseNumber} ({expected.Description}) should have raised nothing, and raised {Render(result.Diagnostics)}.");
            return;
        }

        SparkDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            item => string.Equals(item.Code, expected.Code, StringComparison.Ordinal));

        Assert.Equal(expected.Severity, diagnostic.Severity);

        if (expected.MessagePrefix is not null)
        {
            Assert.StartsWith(expected.MessagePrefix, diagnostic.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every diagnostic the corpus produces resolves to a help topic. A code a user cannot look up
    /// gets screenshotted into an issue rather than fixed by the person who hit it.
    /// </summary>
    /// <param name="caseNumber">The case number from the table.</param>
    [Theory]
    [MemberData(nameof(CaseNumbers))]
    public void EveryDiagnosticRaisedCarriesAHelpTopic(int caseNumber)
    {
        LacingCase expected = LacingCaseTable.Case(caseNumber);

        ReplicationResult result = Replicator.Replicate(expected.Node, expected.Mode, expected.Inputs, TestContext.Current.CancellationToken);

        foreach (SparkDiagnostic diagnostic in result.Diagnostics)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(diagnostic.HelpTopicId),
                $"Case {caseNumber} raised {diagnostic.Code} with no help topic.");
        }
    }

    /// <summary>
    /// The table's case numbers are stable and never reused, so the corpus must not contain a
    /// duplicate — which a copy-paste while adding a row produces silently, hiding one of the two.
    /// </summary>
    [Fact]
    public void CaseNumbersAreUniqueAndTheCorpusIsNotEmpty()
    {
        Assert.True(LacingCaseTable.Count > 0);
        Assert.Equal(LacingCaseTable.Count, LacingCaseTable.Numbers.Count);
    }

    public static TheoryData<int> CaseNumbers => LacingCaseTable.Numbers;

    private static string Render(IReadOnlyList<SparkDiagnostic> diagnostics) =>
        diagnostics.Count == 0 ? "none" : string.Join("; ", diagnostics.Select(item => item.ToString()));
}
