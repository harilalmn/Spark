using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spark.Engine;
using Spark.UI.Graph;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// A node shows as evaluating while a slow run is still working towards it (`E3-T14`, design
/// language §7.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deterministic, through hooks rather than timing.</b> One node's report is held inside the run,
/// so the run is still going when the test looks, and the delay before anything is marked is set to
/// zero or to an hour rather than raced.
/// </para>
/// <para>
/// <b>The test plays the view.</b> The view model never touches a canvas node from the evaluation's
/// threads: it raises <see cref="MainWindowViewModel.EvaluationProgressed"/>, and the view posts
/// <see cref="MainWindowViewModel.ShowProgress"/> to the UI thread. The test calls it where the view
/// would.
/// </para>
/// </remarks>
public sealed class EvaluatingStateTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static int Evaluating(MainWindowViewModel model) =>
        model.Graph.Nodes.Count(node => node.State.HasFlag(CanvasNodeState.Evaluating));

    private static CanvasNode NodeWith(MainWindowViewModel model, NodeId id) =>
        model.Graph.Nodes.Single(node => node.Id == id);

    /// <summary>
    /// <b>The row.</b> A run still going past the delay marks the node it has not finished, and the
    /// node's own report clears the mark before the run is applied; the applied result leaves none.
    /// </summary>
    [Fact]
    public async Task ASlowRunMarksWhatItHasNotFinishedAndAReportClearsIt()
    {
        using MainWindowViewModel model = new("demo");
        await model.EvaluateAsync();

        // Manual, so nothing below starts a run of its own; and no delay, so the run is overdue at once.
        model.SelectedRunMode = "Manual";
        model.EvaluatingDelay = TimeSpan.Zero;

        using ManualResetEventSlim letReportGo = new();
        TaskCompletionSource<NodeId> firstHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource overdue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource applyHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource letApplyGo = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int reports = 0;

        // The first node to finish is held before its report is recorded - on the evaluation's own
        // thread, which is where reports are made.
        model.WhileReportingForTesting = report =>
        {
            if (Interlocked.Increment(ref reports) == 1)
            {
                firstHeld.SetResult(report.Node);
                letReportGo.Wait(Patience);
            }
        };
        model.EvaluationProgressed += (_, _) => overdue.TrySetResult();
        model.BeforeApplyingForTesting = async _ =>
        {
            applyHeld.TrySetResult();
            await letApplyGo.Task.WaitAsync(Patience);
        };

        Task run = model.EvaluateAsync();
        NodeId held = await firstHeld.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        await overdue.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        model.ShowProgress();
        Assert.True(NodeWith(model, held).State.HasFlag(CanvasNodeState.Evaluating), "a node the run has not finished is not marked evaluating");

        letReportGo.Set();
        await applyHeld.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        // Every node has reported and nothing is applied yet: the marks are the reports' doing alone.
        model.ShowProgress();
        Assert.False(NodeWith(model, held).State.HasFlag(CanvasNodeState.Evaluating), "a node that has reported is still marked evaluating");

        letApplyGo.SetResult();
        await run;

        Assert.Equal(0, Evaluating(model));
    }

    /// <summary>
    /// A run that finishes inside the delay marks nothing and asks for no repaint, which is what keeps
    /// a slider drag - a run per pointer move - from flickering every node on the canvas.
    /// </summary>
    [Fact]
    public async Task ARunInsideTheDelayMarksNothing()
    {
        using MainWindowViewModel model = new("demo");
        await model.EvaluateAsync();

        model.SelectedRunMode = "Manual";
        model.EvaluatingDelay = TimeSpan.FromHours(1);

        int raised = 0;
        TaskCompletionSource applyHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource letApplyGo = new(TaskCreationOptions.RunContinuationsAsynchronously);

        model.EvaluationProgressed += (_, _) => Interlocked.Increment(ref raised);
        model.BeforeApplyingForTesting = async _ =>
        {
            applyHeld.TrySetResult();
            await letApplyGo.Task.WaitAsync(Patience);
        };

        Task run = model.EvaluateAsync();
        await applyHeld.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        model.ShowProgress();
        Assert.Equal(0, Evaluating(model));
        Assert.Equal(0, Volatile.Read(ref raised));

        letApplyGo.SetResult();
        await run;
    }
}
