using System;
using System.Collections.Generic;
using Spark.Engine;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// `E8-T66` — every port tab on one side of a node is the same length.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client, over a screenshot of <c>Colour.FromRgb</c>:</b> <i>make the input
/// port and output pills' length the same as the longest one.</i> <c>red</c>, <c>green</c> and
/// <c>blue</c> came out three different lengths, so the node's left edge was a staircase.
/// </para>
/// <para>
/// <b>A port tab is a target as much as a label</b>, which is why ports are lozenges and not dots
/// (<see cref="CanvasNode.PortTab"/>) — and a column of targets that all begin at the same edge
/// and stop at three different ones is harder to aim down than a column of equal ones.
/// </para>
/// <para>
/// <b>The second half is what the change could have broken.</b> A node's width is measured from
/// character counts before anything is drawn, so widening the tabs without widening the
/// measurement puts the longest port name under the type label beside it. Both are asserted.
/// </para>
/// </remarks>
public sealed class PortTabWidthTests
{
    /// <summary>The three-input node the client photographed.</summary>
    private static CanvasNode Colour()
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Colour.FromRgb"), 0, 0);

        return graph.Nodes[0];
    }

    /// <summary>The width of one side's tab, taken from the port at the given row.</summary>
    private static double TabWidth(CanvasNode node, int index, bool isOutput)
    {
        node.PortTab(index, isOutput, out double left, out _, out double right, out _);

        return right - left;
    }

    /// <summary>
    /// <b>The claim itself.</b> <c>red</c>, <c>green</c> and <c>blue</c> are three, five and four
    /// characters, and the three tabs are one width.
    /// </summary>
    [Fact]
    public void EveryInputTabOnANodeIsTheSameWidth()
    {
        CanvasNode node = Colour();

        Assert.Equal(3, node.Inputs.Count);

        double first = TabWidth(node, 0, isOutput: false);

        for (int index = 1; index < node.Inputs.Count; index++)
        {
            Assert.Equal(first, TabWidth(node, index, isOutput: false), 6);
        }
    }

    /// <summary>
    /// <b>It is the width of the longest name, not of the first one.</b> Sizing a side from
    /// whichever port happened to be at row 0 would be equally uniform and would clip
    /// <c>green</c>, which is the failure this asserts against.
    /// </summary>
    [Fact]
    public void TheSharedWidthIsTheLongestNamesWidth()
    {
        CanvasNode node = Colour();

        double shared = TabWidth(node, 0, isOutput: false);

        // The estimate the node is measured with: 6.2 px a character at 11 px Inter, plus 8 px of
        // padding either side. Asserted as a floor, because the tab may only ever be wider than
        // the word it holds.
        double longest = 0;

        foreach (CanvasPortInfo port in node.Inputs)
        {
            longest = Math.Max(longest, (port.Name.Length * 6.2) + 16);
        }

        Assert.True(
            shared >= longest,
            $"the shared tab is narrower than the longest name needs: {shared:F1} < {longest:F1}");

        // And no wider than that name needs, so a one-letter port on a node of long names does not
        // drag the whole side across the body.
        Assert.True(
            shared <= longest + 0.001,
            $"the shared tab is wider than the longest name needs: {shared:F1} > {longest:F1}");
    }

    /// <summary>
    /// <b>The two sides are measured on their own.</b> A side takes the longest name it has, not
    /// the longest name on the node — the tabs face each other across the body, and matching them
    /// to each other would be a rule nobody asked for.
    /// </summary>
    [Fact]
    public void TheTwoSidesAreMeasuredSeparately()
    {
        CanvasNode node = Colour();

        Assert.Equal(["red", "green", "blue"], Names(node.Inputs));
        Assert.Equal(["colour"], Names(node.Outputs));

        // "green" is five characters and "colour" is six, so the two sides cannot agree unless
        // one of them was measured from the other.
        Assert.True(
            TabWidth(node, 0, isOutput: true) > TabWidth(node, 0, isOutput: false),
            "the output side was sized from the input side");
    }

    /// <summary>
    /// <b>The node is still wide enough for what is drawn on it.</b> Every port row must leave a
    /// span between the two tabs for the type labels, and a tab widened without the measurement
    /// being widened with it is exactly how that span goes negative.
    /// </summary>
    [Fact]
    public void EveryRowStillLeavesRoomBetweenTheTabs()
    {
        foreach (string name in new[] { "Colour.FromRgb", "Circle.FromCenterRadius", "Math.Divide" })
        {
            CanvasGraph graph = new();
            graph.Add(TestGraphs.Library.ByName(name), 0, 0);

            CanvasNode node = graph.Nodes[0];

            int rows = Math.Max(node.Inputs.Count, node.Outputs.Count);

            for (int row = 0; row < rows; row++)
            {
                node.PortLabelRow(row, out double leftEnd, out double rightStart);

                Assert.True(
                    rightStart > leftEnd,
                    $"{name} row {row} has no space between its tabs: {leftEnd:F1}..{rightStart:F1}");
            }
        }
    }

    /// <summary>
    /// A side of one port is what it always was: there is no longest name to grow to, so nothing
    /// about a single-port node changes.
    /// </summary>
    [Fact]
    public void ASideOfOnePortIsUnchanged()
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Math.Sin"), 0, 0);

        CanvasNode node = graph.Nodes[0];

        CanvasPortInfo only = Assert.Single(node.Inputs);

        Assert.Equal(
            Math.Max(CanvasNode.PortTabMinimumWidth, (only.Name.Length * 6.2) + 16),
            TabWidth(node, 0, isOutput: false),
            6);
    }

    private static IEnumerable<string> Names(IReadOnlyList<CanvasPortInfo> ports)
    {
        foreach (CanvasPortInfo port in ports)
        {
            yield return port.Name;
        }
    }
}
