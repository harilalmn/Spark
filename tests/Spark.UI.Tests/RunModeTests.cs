using System;
using System.Linq;
using System.Threading.Tasks;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// When the graph runs: Manual, Automatic and Periodic — <c>E3-T13</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Automatic is what the shell did unconditionally, and the mode is the seam that was missing.</b>
/// Twelve call sites started a run the moment anything changed. They now call
/// <see cref="MainWindowViewModel.RequestRun"/>, which asks the mode; the Run button and F5 still
/// call <see cref="MainWindowViewModel.EvaluateAsync"/> directly, because an explicit run means
/// run.
/// </para>
/// <para>
/// <b>What is asserted is the decision, not the timing.</b> Whether a run has finished is a race
/// this suite has no business winning — <see cref="MainWindowViewModel.EvaluateAsync"/> is
/// asynchronous and supersedes itself — so the observable under test is
/// <see cref="MainWindowViewModel.HasPendingRun"/>, which says whether an edit is waiting for a
/// run that has not happened.
/// </para>
/// </remarks>
public sealed class RunModeTests
{
    /// <summary>Automatic is the default, because it is what the shell has always done.</summary>
    [Fact]
    public void AutomaticIsTheDefault()
    {
        using MainWindowViewModel model = new();

        Assert.Equal(RunMode.Automatic, model.RunMode);
        Assert.Equal("Automatic", model.SelectedRunMode);
        Assert.False(model.HasPendingRun);
    }

    /// <summary>
    /// Under Manual an edit is recorded and does not run, and the status bar says so.
    /// </summary>
    /// <remarks>
    /// <b>The message is not decoration.</b> A graph that quietly stops updating is the most
    /// confusing thing an editor can do, and a user who set the mode ten minutes ago will not
    /// connect the two on their own.
    /// </remarks>
    [Fact]
    public void ManualRecordsTheEditAndWaits()
    {
        using MainWindowViewModel model = new();
        model.SelectedRunMode = "Manual";

        model.SelectedLibraryEntry =
            model.AllLibraryEntries.First(entry => entry.DisplayName == "Point.Origin");
        model.PlaceSelectedLibraryEntry(0, 0);
        model.RequestRun();

        Assert.True(model.HasPendingRun);
        Assert.True(model.CanUndo);
        Assert.Contains("Manual", model.StatusText, StringComparison.Ordinal);
    }

    /// <summary>An explicit run settles the debt, whatever the mode says.</summary>
    [Fact]
    public async Task AnExplicitRunClearsThePendingEdit()
    {
        using MainWindowViewModel model = new();
        model.SelectedRunMode = "Manual";

        model.RequestRun();
        Assert.True(model.HasPendingRun);

        await model.EvaluateAsync();

        Assert.False(model.HasPendingRun);
    }

    /// <summary>
    /// Going back to Automatic runs what Manual was holding.
    /// </summary>
    /// <remarks>
    /// Leaving the graph stale after the user has just asked for it to keep itself fresh would be
    /// the opposite of what they said.
    /// </remarks>
    [Fact]
    public void ReturningToAutomaticSettlesTheDebt()
    {
        using MainWindowViewModel model = new();

        model.SelectedRunMode = "Manual";
        model.RequestRun();
        Assert.True(model.HasPendingRun);

        model.SelectedRunMode = "Automatic";

        Assert.False(model.HasPendingRun);
        Assert.Equal(RunMode.Automatic, model.RunMode);
    }

    /// <summary>Under Automatic an edit never leaves a run owing.</summary>
    [Fact]
    public void AutomaticNeverLeavesARunOwing()
    {
        using MainWindowViewModel model = new();

        model.RequestRun();

        Assert.False(model.HasPendingRun);
    }

    /// <summary>Every mode's word round-trips, so the ribbon and the menu cannot disagree.</summary>
    /// <remarks>
    /// The Graph menu writes <see cref="MainWindowViewModel.SelectedRunMode"/> by name and the
    /// ribbon dropdown is bound to the same property, so a word either control could produce and
    /// the other could not read would leave the two out of step.
    /// </remarks>
    [Fact]
    public void EveryModeRoundTripsThroughItsWord()
    {
        foreach (RunMode mode in Enum.GetValues<RunMode>())
        {
            Assert.True(RunModeNames.TryParse(RunModeNames.Of(mode), out RunMode parsed));
            Assert.Equal(mode, parsed);
        }

        Assert.Equal(Enum.GetValues<RunMode>().Length, RunModeNames.All.Count);
        Assert.False(RunModeNames.TryParse("automatic", out _));
    }

    /// <summary>A word the dropdown could not have produced changes nothing.</summary>
    [Fact]
    public void AnUnrecognisedWordIsIgnored()
    {
        using MainWindowViewModel model = new();

        model.SelectedRunMode = "Whenever";

        Assert.Equal(RunMode.Automatic, model.RunMode);
    }
}
