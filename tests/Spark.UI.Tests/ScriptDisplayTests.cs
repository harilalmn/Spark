using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Engine;
using Spark.Scripting;
using Spark.UI.Graph;
using Spark.UI.Theming;

namespace Spark.UI.Tests;

/// <summary>
/// What a code block looks like on the canvas when nobody is typing into it — `E8-T65`.
/// </summary>
/// <remarks>
/// <b>Three things the client asked for after using the blocks.</b> A note saying why a line can
/// carry no output port; syntax colour, always, rather than only in edit mode; and line numbers,
/// always, for the same reason. The first two were about a node that changed appearance the moment
/// it was clicked into, which made one node look like two different things.
/// </remarks>
public sealed class ScriptDisplayTests
{
    private static (CanvasGraph Graph, CanvasNode Node) Block(string source)
    {
        ScriptNodeFactory scripts = new();
        CanvasGraph graph = new() { Scripts = scripts };
        int slot = graph.Add(NodeDefinition.FromScript(scripts.Create(source), source), 0, 0);

        return (graph, graph.Nodes[slot]);
    }

    /// <summary>
    /// <b>The note has a band of its own, between the header and the source.</b> It is not drawn
    /// over the code, and — because the pane lays the live editor over exactly
    /// <see cref="CanvasNode.ScriptBox"/> — it is not covered by the editor either, which is the
    /// moment it is most worth reading.
    /// </summary>
    [Fact]
    public void TheNoteSitsBetweenTheHeaderAndTheSource()
    {
        (_, CanvasNode node) = Block("var a = 1;\nvar b = 2;\n");

        node.ScriptHintBox(out double hintX, out double hintY, out _, out double hintHeight);
        node.ScriptBox(out double sourceX, out double sourceY, out _, out _);

        Assert.Equal(hintX, sourceX, 6);
        Assert.True(hintHeight > 0, "the note has no height");
        Assert.Equal(hintY + hintHeight, sourceY, 6);
    }

    /// <summary>
    /// <b>The note costs the node height rather than costing the source room.</b> A block with the
    /// note must be exactly one band taller than the same source would need without it, or every
    /// existing block would have lost a line the day this landed.
    /// </summary>
    [Fact]
    public void TheNoteMakesTheNodeTallerRatherThanTheSourceShorter()
    {
        (_, CanvasNode node) = Block("var a = 1;\nvar b = 2;\nvar c = 3;\n");

        node.ScriptBox(out _, out _, out _, out double height);

        double lines = (3 * CanvasNode.ScriptLineHeight) + (2 * CanvasNode.ScriptPadding);

        // The source keeps room for every line: taking the band off the source rather than adding
        // it to the node is the mistake this catches, and it would be short by exactly one band.
        Assert.True(
            height >= lines,
            $"three lines need {lines} and the source box is {height}");

        // And the node grew by the band, rather than the band coming from somewhere else.
        Assert.True(
            node.Height >= lines + CanvasNode.ScriptHintHeight,
            $"the node is {node.Height}, which does not hold {lines} of source plus the note");
    }

    /// <summary>
    /// <b>An ordinary node has no note</b>, because it has no source for the note to be about.
    /// </summary>
    [Fact]
    public void AnOrdinaryNodeHasNoNote()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);

        graph.Nodes[slot].ScriptHintBox(out _, out _, out _, out double height);

        Assert.Equal(0, height);
    }

    /// <summary>
    /// <b>The gutter widens with the line count and not before.</b> A nine-line block must not
    /// carry the margin a hundred-line one needs — the source area is narrow already, between two
    /// columns of port tabs.
    /// </summary>
    [Fact]
    public void TheGutterWidensOnlyWhenTheNumbersDo()
    {
        (_, CanvasNode nine) = Block(string.Concat(Enumerable.Repeat("var a = 1;\n", 9)));
        (_, CanvasNode ten) = Block(string.Concat(Enumerable.Repeat("var a = 1;\n", 10)));

        Assert.True(nine.ScriptGutterWidth > 0, "a block with no gutter has nowhere to draw numbers");
        Assert.True(
            ten.ScriptGutterWidth > nine.ScriptGutterWidth,
            $"ten lines need two digits: nine gave {nine.ScriptGutterWidth}, ten gave {ten.ScriptGutterWidth}");
    }

    /// <summary>
    /// <b>The gutter is inside the node's measured width.</b> This is `E8-T57` again: a gutter
    /// drawn inside a box measured without it eats the first characters of every line, and it
    /// would show only on the longest line, which reads as a rendering quirk rather than as a
    /// sizing bug.
    /// </summary>
    [Fact]
    public void TheNodeIsWideEnoughForTheGutterAndTheLongestLine()
    {
        const string Long = "var circumference = radius * 6.28318530717958647692;\n";

        (_, CanvasNode node) = Block(Long);

        node.ScriptBox(out _, out _, out double width, out _);

        double text = (Long.TrimEnd('\n').Length * CanvasNode.ScriptCharWidth)
            + CanvasNode.ScriptGap;

        Assert.True(
            width >= node.ScriptGutterWidth + text,
            $"the source box is {width}, and the gutter plus the longest line is "
            + $"{node.ScriptGutterWidth + text}");
    }

    /// <summary>
    /// <b>The colours come from the editor's own definition</b>, so a keyword is coloured on the
    /// canvas and it is the same colour the editor would give it.
    /// </summary>
    /// <remarks>
    /// Asserting that <i>something</i> is coloured rather than naming a colour: the palette is
    /// <see cref="EditorHighlightPalette"/>'s business and is asserted there, and a test that
    /// repeated its table would fail every time the design language moved.
    /// </remarks>
    [Fact]
    public void AKeywordIsColouredOnTheCanvas() => HeadlessSession.Run(() =>
    {
        IReadOnlyList<IReadOnlyList<ScriptColouring.Section>> lines =
            ScriptColouring.Of("var a = 1;\n// a comment\n");

        Assert.NotEmpty(lines);
        Assert.NotEmpty(lines[0]);
    });

    /// <summary>
    /// <b>Colour is a whole-document property, and this is the case that proves it.</b> A line
    /// inside a block comment is a comment however it reads on its own, so a per-line colouriser
    /// would light up <c>var</c> in the middle of one.
    /// </summary>
    [Fact]
    public void ALineInsideABlockCommentIsAComment() => HeadlessSession.Run(() =>
    {
        IReadOnlyList<IReadOnlyList<ScriptColouring.Section>> lines =
            ScriptColouring.Of("/*\nvar a = 1;\n*/\n");

        Assert.True(lines.Count >= 2, "the source has more than two lines");

        // Asserted non-empty first: `Assert.All` over nothing passes, so the interesting claim
        // below would have been vacuous exactly when the colouriser had stopped working.
        Assert.NotEmpty(lines[1]);

        // One run covering the whole line, rather than a keyword and then the rest.
        ScriptColouring.Section only = Assert.Single(lines[1]);
        Assert.Equal(0, only.Start);
        Assert.Equal("var a = 1;".Length, only.Length);
    });

    /// <summary>An empty script colours to nothing rather than throwing.</summary>
    [Fact]
    public void AnEmptyScriptIsAccepted() =>
        HeadlessSession.Run(() => Assert.NotNull(ScriptColouring.Of(string.Empty)));
}
