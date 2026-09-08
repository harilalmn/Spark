using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Spark.Engine;
using Spark.UI.Graph;
using Spark.UI.Views.Panes;

namespace Spark.UI.Tests;

/// <summary>
/// `E8-T70` and `E8-T71` — two overlaps the client found by using the application.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both are the same kind of defect: two things given one space.</b> A port's type label and
/// the body divide shared the middle of a node, and the watch panel and the script-trust banner
/// shared a grid row. Neither is visible in a screenshot of the case that works, which is why both
/// arrived from a person opening the application rather than from a gate.
/// </para>
/// <para>
/// <b>The first is asserted over the whole shipped library rather than over an example.</b>
/// `Solid.Extrude` is where it was reported and it is not where it stops: any node whose input
/// type is long and whose output type is short has the same shape, and there are 138 nodes.
/// </para>
/// </remarks>
public sealed class PortTypeLabelRoomTests
{
    /// <summary>The widths the node was measured with, from <c>CanvasNode</c>'s own constants.</summary>
    private const double TypeCharWidth = 5.6;

    /// <summary>The gap before a type label, and the clearance kept from the divide.</summary>
    private const double TypeGap = 6;
    private const double MinimumRowGap = 8;

    /// <summary>
    /// <b>The claim, over every node in the library.</b> A type label is drawn only if it fits in
    /// its own half; a node too narrow for one drops it instead. Both outcomes are wrong, so what
    /// is asserted is that the node is wide enough for the label to be drawn at all.
    /// </summary>
    [Fact]
    public void NoPortTypeLabelCrossesTheBodyDivide()
    {
        List<string> offenders = [];

        foreach (NodeDefinition definition in TestGraphs.Library.Definitions())
        {
            CanvasGraph graph = new();
            CanvasNode node = graph.Nodes[graph.Add(definition, 0, 0)];

            if (node.Script is not null)
            {
                // A code block draws no type labels at all (`E6-T29`, `E8-T56`); the span between
                // its tabs is its source.
                continue;
            }

            int rows = Math.Max(node.Inputs.Count, node.Outputs.Count);

            for (int row = 0; row < rows; row++)
            {
                node.PortTypeRoom(row, out double input, out double output);

                if (row < node.Inputs.Count && node.Inputs[row].TypeName is { } inputType
                    && input < TypeGap + (inputType.Length * TypeCharWidth) + MinimumRowGap)
                {
                    offenders.Add(
                        $"{node.Title} row {row}: input type '{inputType}' has {input:F1} px to the divide");
                }

                if (row < node.Outputs.Count && node.Outputs[row].TypeName is { } outputType
                    && output < TypeGap + (outputType.Length * TypeCharWidth) + MinimumRowGap)
                {
                    offenders.Add(
                        $"{node.Title} row {row}: output type '{outputType}' has {output:F1} px from the divide");
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// <b>The node the client photographed.</b> `Solid.Extrude` drew <c>Curve</c> and
    /// <c>Vector3d</c> across the middle of itself, and this is the case named rather than left to
    /// a sweep — a sweep that stops finding it is a sweep whose library changed.
    /// </summary>
    [Theory]
    [InlineData("Solid.Extrude")]
    [InlineData("Solid.FilletAll")]
    [InlineData("Display.FromGeometryColour")]
    public void TheReportedNodesHaveRoomOnBothSides(string name)
    {
        CanvasGraph graph = new();
        CanvasNode node = graph.Nodes[graph.Add(TestGraphs.Library.ByName(name), 0, 0)];

        for (int row = 0; row < Math.Max(node.Inputs.Count, node.Outputs.Count); row++)
        {
            node.PortTypeRoom(row, out double input, out double output);

            Assert.True(input > 0, $"{name} row {row} has no room left of the divide");
            Assert.True(output > 0, $"{name} row {row} has no room right of the divide");
        }
    }

    /// <summary>
    /// <b>Twice the wider half is never narrower than the two halves added together</b>, so the
    /// new rule cannot have made any node narrower than the rule it replaced. Asserted rather than
    /// argued, over the library.
    /// </summary>
    [Fact]
    public void NoNodeGotNarrower()
    {
        foreach (NodeDefinition definition in TestGraphs.Library.Definitions())
        {
            CanvasGraph graph = new();
            CanvasNode node = graph.Nodes[graph.Add(definition, 0, 0)];

            node.BodyDivide(out double middle, out _, out _);

            Assert.Equal(node.X + (node.Width / 2), middle, 6);
            Assert.True(node.Width >= CanvasNode.MinimumWidth, $"{node.Title} is below the floor");
        }
    }

    /// <summary>
    /// `E8-T71` — <b>the watch panel and the trust banner are on different rows.</b> They were both
    /// on row 7 and they are not mutually exclusive: a watched node in a graph that has been opened
    /// and not run shows both, on top of each other.
    /// </summary>
    [Fact]
    public void TheWatchPanelAndTheTrustBannerDoNotShareARow() => HeadlessSession.Run(() =>
    {
        InspectorPane pane = new();

        Control watch = Find(pane, "WatchPanel");
        Control banner = Find(pane, "TrustBanner");

        Assert.NotEqual(Grid.GetRow(watch), Grid.GetRow(banner));
    });

    /// <summary>
    /// <b>And no other pair shares one either, apart from the group that is deliberately
    /// exclusive.</b> This is the test that catches the <i>next</i> one: the note box, the code
    /// editor and the group title are three views of "what is selected" and only one can be
    /// visible, which is why they are allowed to share row 6.
    /// </summary>
    [Fact]
    public void NoTwoPropertiesRowsOverlapUnlessTheyAreMutuallyExclusive() => HeadlessSession.Run(() =>
    {
        InspectorPane pane = new();

        Grid root = pane.GetLogicalDescendants().OfType<Grid>().First(grid => grid.RowDefinitions.Count > 6);

        List<int> shared = root.Children
            .GroupBy(Grid.GetRow)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        // Row 6 only: the note box, the code block's editor and the group title box. One selection
        // cannot be a note and a code block and a group at the same time.
        Assert.Equal([6], shared);
    });

    private static Control Find(InspectorPane pane, string name) =>
        pane.GetLogicalDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name)
        ?? throw new InvalidOperationException($"the pane has no control called {name}");
}
