using System;
using System.Linq;
using System.Threading.Tasks;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// When two runs overlap, the newest one's results are the ones left applied - not whichever
/// finished last.
/// </summary>
/// <remarks>
/// <para>
/// <b>Found as a flaky test, and it was a real ordering hole.</b> Every constructor that opens a graph
/// starts a run, so a caller's own run racing that one is the ordinary case. Results were applied in
/// the order they reached the apply gate, and a run that had already finished when a later one
/// superseded it could reach the gate second - putting an older graph's results over a newer one's.
/// </para>
/// <para>
/// <b>Deterministic, through a hook rather than a race.</b> The older run is held after it has
/// evaluated and before it is applied; the newer run is evaluated and applied past it; then the
/// older one is let go. Without the rule it is applied last, and the view reports what it computed
/// instead of what the newer run did.
/// </para>
/// </remarks>
public sealed class NewestRunWinsTests
{
    private static int SlotOf(MainWindowViewModel model, string title)
    {
        for (int slot = 0; slot < model.Graph.Nodes.Count; slot++)
        {
            if (string.Equals(model.Graph.Nodes[slot].Title, title, StringComparison.Ordinal))
            {
                return slot;
            }
        }

        throw new InvalidOperationException("No node titled '" + title + "' in the demo graph.");
    }

    /// <summary>
    /// <b>The row.</b> An older run let go after a newer one has been applied leaves the newer one's
    /// results in place.
    /// </summary>
    [Fact]
    public async Task AnOlderRunFinishingLastDoesNotReplaceANewerOne()
    {
        using MainWindowViewModel model = new("demo");
        await model.EvaluateAsync();

        // Manual, so the edit below does not start a third run of its own.
        model.SelectedRunMode = "Manual";

        TaskCompletionSource olderHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource letGo = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int held = 0;

        model.BeforeApplyingForTesting = async generation =>
        {
            if (held == 0)
            {
                held = generation;
                olderHeld.SetResult();
                await letGo.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
        };

        // The older run: nothing has changed, so it computes nothing and is served from the cache.
        Task older = model.EvaluateAsync();
        await olderHeld.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // The newer run, over an edited graph, so it computes something.
        model.ShowSelection([SlotOf(model, "Number.Range")]);
        PortLiteralViewModel end = model.Inspector.Single(port => port.Name == "end");
        end.Text = "2";
        end.Commit();

        await model.EvaluateAsync();
        int newerComputed = model.LastRunNodesEvaluated;
        Assert.True(newerComputed > 0, "the newer run should have computed the edited nodes");

        letGo.SetResult();
        await older;

        Assert.Equal(newerComputed, model.LastRunNodesEvaluated);
    }
}
