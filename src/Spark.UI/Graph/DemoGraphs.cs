using System;
using Spark.Api;
using Spark.Engine;

namespace Spark.UI.Graph;

/// <summary>
/// Graphs built from the real node library: a seeded demo that draws a grid of points, and a
/// synthetic one at whatever size a measurement calls for.
/// </summary>
public static class DemoGraphs
{
    /// <summary>How many values each of the demo's two ranges produces.</summary>
    public const int GridSide = 10;

    /// <summary>
    /// The walking skeleton, as a graph: two <c>Number.Range</c> nodes feeding
    /// <c>Point.ByCoordinates</c> under <b>Cross Product</b>, so a 10 × 10 grid of points appears in
    /// the viewport.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lacing is the point of the demo and it is visible without opening anything: two ports
    /// carrying ten values each, one node, a hundred points. Switch that node to Longest and the
    /// same graph draws a ten-point diagonal instead — same wires, same numbers, a different
    /// answer, which is the fact that most distinguishes Spark from a spreadsheet with pictures.
    /// </para>
    /// <para>
    /// It also seeds a deliberate error. <c>Math.Divide</c> by zero throws, so that node wears the
    /// error ring and the <c>Point.Translate</c> below it is greyed as <i>not evaluated</i> rather
    /// than flooded with an error of its own — the non-cascading rule, on screen, in the demo.
    /// </para>
    /// </remarks>
    /// <param name="library">The imported node library.</param>
    /// <returns>The graph, ready to evaluate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    public static CanvasGraph Demo(NodeLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        CanvasGraph graph = new();

        int columns = graph.Add(library.ByName("Number.Range"), 30, 30);
        int rows = graph.Add(library.ByName("Number.Range"), 30, 150);
        int height = graph.Add(library.ByName("Number.Value"), 30, 270);

        int points = graph.Add(library.ByName("Point.ByCoordinates"), 300, 60);
        int colour = graph.Add(library.ByName("Colour.ByRgb"), 300, 200);
        int display = graph.Add(library.ByName("Display.ByGeometryColour"), 580, 90);

        // The error branch. Divide by zero throws, so this node errors and everything downstream of
        // it is greyed rather than blamed.
        int axis = graph.Add(library.ByName("Vector.ZAxis"), 30, 370);
        int divide = graph.Add(library.ByName("Math.Divide"), 300, 340);
        int translate = graph.Add(library.ByName("Point.Translate"), 580, 300);

        Literal(graph, columns, 0, 0.0);
        Literal(graph, columns, 1, (double)(GridSide - 1));
        Literal(graph, columns, 2, 1.0);

        Literal(graph, rows, 0, 0.0);
        Literal(graph, rows, 1, (double)(GridSide - 1));
        Literal(graph, rows, 2, 1.0);

        Literal(graph, height, 0, 1.0);

        Literal(graph, colour, 0, 90.0);
        Literal(graph, colour, 1, 200.0);
        Literal(graph, colour, 2, 255.0);

        Literal(graph, divide, 0, 1.0);
        Literal(graph, divide, 1, 0.0);

        graph.TryConnect(Output(columns, 0), Input(points, 0));
        graph.TryConnect(Output(rows, 0), Input(points, 1));
        graph.TryConnect(Output(height, 0), Input(points, 2));
        graph.TryConnect(Output(points, 0), Input(display, 0));
        graph.TryConnect(Output(colour, 0), Input(display, 1));

        graph.TryConnect(Output(points, 0), Input(translate, 0));
        graph.TryConnect(Output(axis, 0), Input(translate, 1));
        graph.TryConnect(Output(divide, 0), Input(translate, 2));

        // Cross Product is what turns two ten-value ranges into a hundred points rather than ten.
        // Declaring it here rather than leaving it to Auto is the whole demonstration.
        graph.Engine.SetLacing(graph.Nodes[points].Id, LacingMode.CrossProduct);

        return graph;
    }

    /// <summary>
    /// A synthetic graph of a given size, laid out on a grid with one wire per node so the wire
    /// layer is exercised as heavily as the node layer.
    /// </summary>
    /// <remarks>
    /// The layout is a grid rather than a random scatter because a grid is the worst case for a
    /// uniform spatial index — every cell is evenly occupied, so no query gets to skip a sparse
    /// region — and measuring the worst case is the only measurement worth quoting.
    /// </remarks>
    /// <param name="library">The imported node library.</param>
    /// <param name="nodeCount">How many nodes to build. Clamped to at least one.</param>
    /// <returns>The graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    public static CanvasGraph Synthetic(NodeLibrary library, int nodeCount)
    {
        ArgumentNullException.ThrowIfNull(library);

        nodeCount = Math.Max(1, nodeCount);

        // Ordered so that consecutive entries chain: each one's output is something the next one
        // will accept. These are real wires now and the engine refuses a mismatched pair, so an
        // arbitrary order leaves a third of the graph unwired and quietly under-exercises the layer
        // the benchmark exists to measure.
        string[] catalogue =
        [
            "Number.Value",
            "Math.Add",
            "Math.Multiply",
            "Math.Sin",
            "Number.Range",
            "Point.ByCoordinates",
            "BoundingBox.ByCorners",
            "BoundingBox.Centre",
            "Plane.ByOriginNormal",
            "Point.Translate",
            "Point.Distance",
            "Vector.ByCoordinates",
            "Vector.Scale",
            "Vector.Length",
            "Colour.ByRgb",
            "Display.ByGeometryColour",
        ];

        CanvasGraph graph = new();
        int columns = (int)Math.Ceiling(Math.Sqrt(nodeCount * 1.6));

        for (int i = 0; i < nodeCount; i++)
        {
            graph.Add(library.ByName(catalogue[i % catalogue.Length]), (i % columns) * 260.0, (i / columns) * 150.0);
        }

        // Wires connect across rows as well as along them, so a cull cannot succeed merely by
        // treating the graph as a set of independent strips.
        //
        // Every input port of the target is tried rather than only the first, because these are
        // real wires now and the engine refuses one whose types do not match. Trying port 0 alone
        // left a fifth of the graph unwired, which quietly under-exercises the layer the benchmark
        // exists to measure.
        for (int i = 0; i < nodeCount; i++)
        {
            // The cross-row target first, falling back to the neighbour. The cross-row wires are
            // what stop a cull succeeding merely by treating the graph as independent strips; the
            // fallback is what stops the fifth of the graph that lands on an incompatible pair from
            // being unwired altogether.
            if (i % 5 == 0 && TryChain(graph, i, i + columns, nodeCount))
            {
                continue;
            }

            TryChain(graph, i, i + 1, nodeCount);
        }

        return graph;
    }

    private static bool TryChain(CanvasGraph graph, int source, int target, int nodeCount)
    {
        if (target >= nodeCount)
        {
            return false;
        }

        int inputs = graph.Nodes[target].Inputs.Count;
        for (int port = 0; port < inputs; port++)
        {
            if (graph.TryConnect(Output(source, 0), Input(target, port)))
            {
                return true;
            }
        }

        return false;
    }

    private static void Literal(CanvasGraph graph, int slot, int portIndex, object? value) =>
        graph.SetLiteral(slot, portIndex, value);

    private static CanvasPort Output(int slot, int portIndex) => new(slot, portIndex, IsOutput: true);

    private static CanvasPort Input(int slot, int portIndex) => new(slot, portIndex, IsOutput: false);
}
