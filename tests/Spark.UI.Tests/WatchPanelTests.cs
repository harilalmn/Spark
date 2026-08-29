using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spark.Api;
using Spark.UI.Graph;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// The watch panel: the pinned pane that shows one node's whole output.
/// </summary>
/// <remarks>
/// The panel exists to do the three things the strip under a node documents itself as not doing —
/// show every output port, show every element, and show a long value in full. Each of those has a
/// test here named after it, because "the panel exists" is not the claim being made.
/// </remarks>
public sealed class WatchPanelTests
{
    [Fact]
    public void NothingIsPinnedToBeginWith()
    {
        using MainWindowViewModel model = new();

        Assert.False(model.IsWatchPinned);
        Assert.Empty(model.Watch);
        Assert.Equal("Nothing pinned", model.WatchTitle);
    }

    [Fact]
    public async Task APinnedNodeReportsItsWholeOutput()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();

        model.PinWatch(0);

        Assert.True(model.IsWatchPinned);
        Assert.NotEmpty(model.Watch);
        Assert.NotEqual("Nothing pinned", model.WatchTitle);
    }

    [Fact]
    public async Task ThePinSurvivesTheSelectionMovingElsewhere()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();

        model.PinWatch(0);
        string pinned = model.WatchTitle;
        List<string> lines = [.. model.Watch.Select(line => line.Text)];

        // This is the whole reason the panel is a different tool from the strip: the strip
        // answers for whatever is under the pointer, and a panel that followed the selection
        // would be a wider strip.
        model.ShowSelection([1]);
        model.ShowSelection([]);

        Assert.Equal(pinned, model.WatchTitle);
        Assert.Equal(lines, model.Watch.Select(line => line.Text));
    }

    [Fact]
    public async Task ThePanelFollowsTheNodeAcrossARunRatherThanFreezing()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();
        model.PinWatch(0);

        List<string> before = [.. model.Watch.Select(line => line.Text)];

        await model.EvaluateAsync();

        Assert.NotEmpty(model.Watch);
        Assert.Equal(before, model.Watch.Select(line => line.Text));
    }

    [Fact]
    public void PinningANodeThatIsNotThereClearsRatherThanThrows()
    {
        using MainWindowViewModel model = new();

        model.PinWatch(9999);

        Assert.False(model.IsWatchPinned);
        Assert.Empty(model.Watch);
    }

    [Fact]
    public async Task PinningWithNothingSelectedClears()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();

        model.PinWatch(0);
        Assert.True(model.IsWatchPinned);

        model.ShowSelection([]);
        model.PinSelectionCommand.Execute(null);

        Assert.False(model.IsWatchPinned);
    }

    [Fact]
    public async Task ClearingUnpins()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();

        model.PinWatch(0);
        model.ClearWatchCommand.Execute(null);

        Assert.False(model.IsWatchPinned);
        Assert.Empty(model.Watch);
        Assert.Equal("Nothing pinned", model.WatchTitle);
    }

    [Fact]
    public async Task SelectingANodeAndPressingPinIsThePathTheButtonTakes()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();

        // The gesture, rather than the method behind it: select a node the way the canvas does,
        // then run the command the toolbar button is bound to.
        model.ShowSelection([2]);
        model.PinSelectionCommand.Execute(null);

        Assert.True(model.IsWatchPinned);
        Assert.Equal(model.SelectionTitle, model.WatchTitle);
        Assert.NotEmpty(model.Watch);
    }

    [Fact]
    public void EveryOutputPortIsReportedAndNamed()
    {
        // The strip shows the first output port only, and says so. The panel shows all of them.
        IReadOnlyList<CanvasPortInfo> ports =
        [
            new("first", 0, null, "Double"),
            new("second", 0, null, "Double"),
        ];

        IReadOnlyList<WatchLine> lines = WatchReport.Describe(ports, [1.5, 2.5]);

        Assert.Equal(2, lines.Count);
        Assert.Equal("first — 1.5", lines[0].Text);
        Assert.Equal("second — 2.5", lines[1].Text);
    }

    [Fact]
    public void APortWithNoValueIsReportedRatherThanOmitted()
    {
        // A missing row reads as a node with fewer outputs than it has.
        IReadOnlyList<CanvasPortInfo> ports = [new("out", 0, null, "Double")];

        WatchLine line = Assert.Single(WatchReport.Describe(ports, []));

        Assert.Equal("out — nothing yet", line.Text);
    }

    [Fact]
    public void EveryElementOfAListIsShownRatherThanTheFirstSix()
    {
        // The strip stops at six and says how many it left out. The panel is where the rest is.
        IReadOnlyList<CanvasPortInfo> ports = [new("out", 1, null, "Double")];
        SparkList list = new([.. Enumerable.Range(0, 40).Select(index => (object?)(double)index)], 1);

        IReadOnlyList<WatchLine> lines = WatchReport.Describe(ports, [list]);

        Assert.Equal(41, lines.Count);
        Assert.Equal("out — 40 items · rank 1", lines[0].Text);
        Assert.Equal("[39] 39", lines[^1].Text);
    }

    [Fact]
    public void RankIsOnEveryListLineAtEveryDepth()
    {
        IReadOnlyList<CanvasPortInfo> ports = [new("out", 2, null, "Double")];
        SparkList inner = new([1.0, 2.0], 1);
        SparkList outer = new([inner, inner], 2);

        IReadOnlyList<WatchLine> lines = WatchReport.Describe(ports, [outer]);

        // A hundred points at rank 1 and a hundred at rank 2 draw identically and lace completely
        // differently, so the shape is on every line rather than only at the top.
        Assert.Equal("out — 2 items · rank 2", lines[0].Text);
        Assert.Equal("[0] 2 items · rank 1", lines[1].Text);
        Assert.Equal("[0] 1", lines[2].Text);
        Assert.Equal(1, lines[1].Depth);
        Assert.Equal(2, lines[2].Depth);
    }

    [Fact]
    public void ALongValueIsNotClipped()
    {
        // The strip clips at 96 characters so that one enormous string cannot lay a band across
        // the graph. "If you need the whole value, that is what the watch panel is for" is a
        // promise this test makes the panel keep.
        string enormous = new('x', 500);
        IReadOnlyList<CanvasPortInfo> ports = [new("out", 0, null, "String")];

        WatchLine line = Assert.Single(WatchReport.Describe(ports, [enormous]));

        Assert.Contains(enormous, line.Text, StringComparison.Ordinal);
        Assert.DoesNotContain('…', line.Text);
    }

    [Fact]
    public void AnEnormousListStopsAndSaysHowManyLinesItLeftOut()
    {
        // A list of a million expanded in full is a hang, and a hang is a worse answer than a
        // truncated one. Silence about the truncation would make it read as a short list.
        IReadOnlyList<CanvasPortInfo> ports = [new("out", 1, null, "Double")];
        SparkList huge = new([.. Enumerable.Range(0, 5000).Select(index => (object?)(double)index)], 1);

        IReadOnlyList<WatchLine> lines = WatchReport.Describe(ports, [huge]);

        Assert.Equal(WatchReport.MaximumLines + 1, lines.Count);
        Assert.StartsWith("… ", lines[^1].Text, StringComparison.Ordinal);
        Assert.Contains("not shown", lines[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NumbersAreWrittenInTheInvariantCulture()
    {
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            IReadOnlyList<CanvasPortInfo> ports = [new("out", 0, null, "Double")];

            // Two users reading the same graph must not disagree about the value.
            Assert.Equal("out — 1.5", Assert.Single(WatchReport.Describe(ports, [1.5])).Text);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => WatchReport.Describe(null!, []));
        Assert.Throws<ArgumentNullException>(() => WatchReport.Describe([], null!));
    }

    [Fact]
    public void ADeeperLineIsIndentedFurther()
    {
        WatchLineViewModel top = new(0, "out");
        WatchLineViewModel nested = new(2, "[0] 1");

        Assert.Equal(0.0, top.Margin.Left);
        Assert.True(nested.Margin.Left > top.Margin.Left);

        // The indent is a margin rather than spaces in the text, so a line copied out of the
        // panel pastes as the value rather than as the value with a ragged prefix.
        Assert.DoesNotContain("  ", nested.Text, StringComparison.Ordinal);
    }
}
