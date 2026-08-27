using System;
using System.Globalization;
using Spark.UI.Theming;

namespace Spark.UI.Graph;

/// <summary>
/// <b>Temporary.</b> Builds graphs for the canvas to draw before the engine exists: a small
/// readable one for the demo, and a synthetic one at whatever size a measurement calls for.
/// </summary>
/// <remarks>
/// The two-thousand-node graph is not decoration. ADR-0013 states the immediate-mode canvas as a
/// bet against one-control-per-node, and the bet is only settled by panning and zooming that many
/// nodes and reading the frame time. This builder is what makes the number reproducible.
/// </remarks>
public static class SampleGraphs
{
    private static readonly (string Title, NodeCategory Category, string[] Inputs, string[] Outputs)[] Catalogue =
    [
        ("Number", NodeCategory.Input, [], ["value"]),
        ("Number Slider", NodeCategory.Input, [], ["value"]),
        ("Point.ByCoordinates", NodeCategory.Point, ["x", "y", "z"], ["point"]),
        ("Vector.ByTwoPoints", NodeCategory.Point, ["start", "end"], ["vector"]),
        ("Line.ByStartPointEndPoint", NodeCategory.Curve, ["start", "end"], ["line"]),
        ("Circle.ByCenterRadius", NodeCategory.Curve, ["centre", "radius"], ["circle"]),
        ("Surface.ByLoft", NodeCategory.Solid, ["curves"], ["surface"]),
        ("Solid.ByExtrusion", NodeCategory.Solid, ["profile", "height"], ["solid"]),
        ("Math.Sin", NodeCategory.Math, ["angle"], ["result"]),
        ("Math.RemapRange", NodeCategory.Math, ["values", "min", "max"], ["result"]),
        ("List.Map", NodeCategory.List, ["list", "function"], ["list"]),
        ("List.Transpose", NodeCategory.List, ["lists"], ["lists"]),
        ("If", NodeCategory.Logic, ["test", "true", "false"], ["result"]),
        ("Watch", NodeCategory.Display, ["value"], ["value"]),
        ("Code Block", NodeCategory.Script, ["a", "b"], ["out"]),
        ("Python Script", NodeCategory.Script, ["IN[0]", "IN[1]"], ["OUT"]),
        ("Custom Node", NodeCategory.Custom, ["in"], ["out"]),
    ];

    /// <summary>
    /// A small graph that exercises every visual state the canvas draws: rest, selected, anchor,
    /// warning and error, plus wires that cross node headers so the casing-and-core pair can be
    /// seen doing its job.
    /// </summary>
    /// <returns>The graph.</returns>
    public static PlaceholderGraph Demo()
    {
        PlaceholderGraph graph = new();

        int x = graph.Add(new PlaceholderNode("x", "Number Slider", NodeCategory.Input, 40, 60, [], ["value"]));
        int y = graph.Add(new PlaceholderNode("y", "Number Slider", NodeCategory.Input, 40, 140, [], ["value"]));
        int z = graph.Add(new PlaceholderNode("z", "Number", NodeCategory.Input, 40, 220, [], ["value"]));

        int sin = graph.Add(new PlaceholderNode(
            "sin", "Math.Sin", NodeCategory.Math, 280, 132, ["angle"], ["result"]));

        int point = graph.Add(new PlaceholderNode(
            "point", "Point.ByCoordinates", NodeCategory.Point, 520, 92, ["x", "y", "z"], ["point"]));

        int circle = graph.Add(new PlaceholderNode(
            "circle", "Circle.ByCenterRadius", NodeCategory.Curve, 780, 108, ["centre", "radius"], ["circle"]));

        int loft = graph.Add(new PlaceholderNode(
            "loft", "Surface.ByLoft", NodeCategory.Solid, 1040, 116, ["curves"], ["surface"]));

        int watch = graph.Add(new PlaceholderNode(
            "watch", "Watch", NodeCategory.Display, 1040, 240, ["value"], ["value"]));

        int broken = graph.Add(new PlaceholderNode(
            "broken", "Code Block", NodeCategory.Script, 780, 264, ["a", "b"], ["out"]));

        graph.Nodes[point].State = PlaceholderNodeState.Selected | PlaceholderNodeState.Anchor;
        graph.Nodes[circle].State = PlaceholderNodeState.Selected;
        graph.Nodes[broken].State = PlaceholderNodeState.Error;
        graph.Nodes[watch].State = PlaceholderNodeState.Warning;

        graph.AddWire(Wire(x, 0, sin, 0));
        graph.AddWire(Wire(sin, 0, point, 0));
        graph.AddWire(Wire(y, 0, point, 1));
        graph.AddWire(Wire(z, 0, point, 2));
        graph.AddWire(Wire(point, 0, circle, 0));
        graph.AddWire(Wire(y, 0, circle, 1));
        graph.AddWire(Wire(circle, 0, loft, 0));
        graph.AddWire(Wire(circle, 0, watch, 0));
        graph.AddWire(Wire(z, 0, broken, 0));

        return graph;
    }

    /// <summary>
    /// A synthetic graph of a given size, laid out on a grid with one wire per node so the wire
    /// layer is exercised as heavily as the node layer.
    /// </summary>
    /// <param name="nodeCount">How many nodes to build. Clamped to at least one.</param>
    /// <returns>The graph.</returns>
    /// <remarks>
    /// The layout is a grid rather than a random scatter because a grid is the worst case for a
    /// uniform spatial index — every cell is evenly occupied, so no query gets to skip a sparse
    /// region — and measuring the worst case is the only measurement worth quoting.
    /// </remarks>
    public static PlaceholderGraph Synthetic(int nodeCount)
    {
        nodeCount = Math.Max(1, nodeCount);

        PlaceholderGraph graph = new();
        int columns = (int)Math.Ceiling(Math.Sqrt(nodeCount * 1.6));

        for (int i = 0; i < nodeCount; i++)
        {
            (string title, NodeCategory category, string[] inputs, string[] outputs) =
                Catalogue[i % Catalogue.Length];

            int column = i % columns;
            int row = i / columns;

            graph.Add(new PlaceholderNode(
                string.Create(CultureInfo.InvariantCulture, $"n{i}"),
                title,
                category,
                column * 260.0,
                row * 150.0,
                inputs,
                outputs));
        }

        // One wire per node that has both an output and a downstream neighbour with an input.
        // Wires connect across rows as well as along them, so a cull cannot succeed merely by
        // treating the graph as a set of independent strips.
        for (int i = 0; i < nodeCount; i++)
        {
            int target = i + (i % 5 == 0 ? columns : 1);
            if (target >= nodeCount)
            {
                continue;
            }

            if (graph.Nodes[i].Outputs.Count == 0 || graph.Nodes[target].Inputs.Count == 0)
            {
                continue;
            }

            graph.AddWire(Wire(i, 0, target, 0));
        }

        return graph;
    }

    private static PlaceholderWire Wire(int fromNode, int fromPort, int toNode, int toPort) => new(
        new PlaceholderPort(fromNode, fromPort, IsOutput: true),
        new PlaceholderPort(toNode, toPort, IsOutput: false));
}
