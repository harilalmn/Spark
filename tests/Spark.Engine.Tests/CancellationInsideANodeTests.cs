using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// Cancellation reaching <i>inside</i> a node, which is the half `E3-T12` was missing.
/// </summary>
/// <remarks>
/// Cancelling between nodes and between replication elements was never enough for one expensive
/// element. A node given a hundred thousand points replicates into a hundred thousand cheap calls
/// and stops promptly; a node given one enormous mesh makes a single call that nothing can
/// interrupt — and the whole point of cancelling is that the user has already changed their mind.
/// </remarks>
public sealed class CancellationInsideANodeTests
{
    [Fact]
    public void ATrailingTokenIsNotAPort()
    {
        ImportReport report = NodeImporter.Import([typeof(Interruptible)], "Test");

        NodeDefinition definition = Definition(report, "Interruptible.Sum");

        // Two parameters and a token; two ports. A port for the token would be a port no canvas
        // could ever supply a value for.
        Assert.Equal(2, definition.Inputs.Count);
        Assert.Equal(["count", "seed"], definition.Inputs.Select(port => port.Name));
        Assert.True(definition.WantsCancellation);
    }

    [Fact]
    public void ANodeWithoutATokenDoesNotAskForOne()
    {
        ImportReport report = NodeImporter.Import([typeof(Interruptible)], "Test");

        Assert.False(Definition(report, "Interruptible.Plain").WantsCancellation);
    }

    [Fact]
    public void ATokenThatIsNotLastIsRefusedWithAReason()
    {
        ImportReport report = NodeImporter.Import([typeof(BadlyPlacedToken)], "Test");

        ExcludedMember excluded = Assert.Single(
            report.Exclusions,
            exclusion => exclusion.Member.Name == nameof(BadlyPlacedToken.Wrong));

        // Silently making it a port would shift every port after it by one, so the node would
        // read its arguments from the wrong slots and produce plausible nonsense.
        Assert.Contains("last parameter", excluded.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRunsTokenReachesTheNode()
    {
        ImportReport report = NodeImporter.Import([typeof(Interruptible)], "Test");
        NodeDefinition definition = Definition(report, "Interruptible.Sum");

        using CancellationTokenSource source = new();
        source.Cancel();

        OperationCanceledException cancelled = Assert.Throws<OperationCanceledException>(
            () => Replicator.Replicate(definition, LacingMode.Longest, [1000.0, 0.0], source.Token));

        // The stack is the assertion, and nothing weaker would do. There is exactly ONE element
        // here, so the replicator's own between-elements check cannot be what stopped it: the
        // throw has to come from inside the node's own loop, and the frame says so by name.
        Assert.Contains(nameof(Interruptible.Sum), cancelled.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUncancelledRunPassesAWorkingTokenRatherThanNothing()
    {
        ImportReport report = NodeImporter.Import([typeof(Interruptible)], "Test");
        NodeDefinition definition = Definition(report, "Interruptible.Sum");

        ReplicationResult result = Replicator.Replicate(
            definition,
            LacingMode.Longest,
            [10.0, 5.0],
            CancellationToken.None);

        Assert.True(result.HasOutput);
        Assert.Equal(50.0, result.Outputs[0]);
    }

    [Fact]
    public void TheTokenSlotDoesNotDisturbTheArgumentsBeforeIt()
    {
        // The compiled invoker binds parameters to argument slots by position, so this is the
        // test that would fail if the extra slot were inserted anywhere but at the end.
        ImportReport report = NodeImporter.Import([typeof(Interruptible)], "Test");

        ReplicationResult result = Replicator.Replicate(
            Definition(report, "Interruptible.Sum"),
            LacingMode.Longest,
            [3.0, 100.0],
            CancellationToken.None);

        Assert.Equal(300.0, result.Outputs[0]);
    }

    [Fact]
    public void ReplicationStillStopsBetweenElementsForANodeWithNoToken()
    {
        // The behaviour that already existed, asserted beside the new one so that adding the
        // token slot cannot have quietly removed it.
        ImportReport report = NodeImporter.Import([typeof(Interruptible)], "Test");
        NodeDefinition plain = Definition(report, "Interruptible.Plain");

        using CancellationTokenSource source = new();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => Replicator.Replicate(
            plain,
            LacingMode.Longest,
            [SparkList.Of(1.0, 2.0, 3.0)],
            source.Token));
    }

    private static NodeDefinition Definition(ImportReport report, string name) =>
        report.Nodes.Single(node => node.Definition.DisplayName == name).Definition;
}

/// <summary>A node whose work can be interrupted part way through.</summary>
public static class Interruptible
{
    /// <summary>Adds a seed to itself a number of times, checking the token as it goes.</summary>
    /// <param name="count">How many times to add.</param>
    /// <param name="seed">The value to add.</param>
    /// <param name="cancellationToken">The run's token.</param>
    /// <returns>The sum.</returns>
    public static double Sum(double count, double seed, CancellationToken cancellationToken)
    {
        double total = 0.0;

        for (int index = 0; index < (int)count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += seed;
        }

        return total;
    }

    /// <summary>Doubles a number, and takes no token.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Twice it.</returns>
    public static double Plain(double value) => value * 2.0;
}

/// <summary>A node that puts its token in the wrong place.</summary>
public static class BadlyPlacedToken
{
    /// <summary>A token that is not the last parameter.</summary>
    /// <param name="cancellationToken">The token, in the wrong position.</param>
    /// <param name="value">The value.</param>
    /// <returns>The value.</returns>
    public static double Wrong(CancellationToken cancellationToken, double value) => value;
}
