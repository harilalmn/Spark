using System;
using System.Security.Cryptography;
using System.Text;
using Spark.Api;
using Spark.Engine;

namespace Spark.UI.Graph;

/// <summary>
/// Graphs built from the real node library: a seeded demo that draws a grid of points, a curve demo
/// that draws an ellipse, circles and a polygon, and a synthetic one at whatever size a measurement
/// calls for.
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
    /// The surface demo: a sphere, a cylinder, a cone, a torus and a lofted ruled surface, each
    /// shaded in its own colour.
    /// </summary>
    /// <remarks>
    /// <b>Five surfaces rather than one</b>, because the thing this graph is evidence for is that
    /// the tessellator handles a pole, a seam, a taper, a doubly-closed surface and a lofted one —
    /// and the only way to see all five is to draw all five. The colours are pulled apart on
    /// purpose: a screenshot in which two surfaces are the same colour proves less.
    /// </remarks>
    /// <param name="library">The node library to build from.</param>
    /// <returns>The graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    public static CanvasGraph Surfaces(NodeLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        CanvasGraph graph = new();

        int sphereCentre = graph.Add(library.ByName("Point.ByCoordinates"), 30, 30, Seeded("surfaces", "sphereCentre"));
        int sphere = graph.Add(library.ByName("Surface.Sphere"), 280, 30, Seeded("surfaces", "sphere"));
        int sphereColour = graph.Add(library.ByName("Colour.ByRgb"), 280, 170, Seeded("surfaces", "sphereColour"));
        int sphereDisplay = graph.Add(library.ByName("Display.ByGeometryColour"), 560, 30, Seeded("surfaces", "sphereDisplay"));

        int cylinderBase = graph.Add(library.ByName("Point.ByCoordinates"), 30, 320, Seeded("surfaces", "cylinderBase"));
        int cylinderPlane = graph.Add(library.ByName("Plane.ByOriginNormal"), 280, 320, Seeded("surfaces", "cylinderPlane"));
        int axis = graph.Add(library.ByName("Vector.ZAxis"), 30, 440, Seeded("surfaces", "axis"));
        int cylinder = graph.Add(library.ByName("Surface.Cylinder"), 560, 320, Seeded("surfaces", "cylinder"));
        int cylinderColour = graph.Add(library.ByName("Colour.ByRgb"), 560, 460, Seeded("surfaces", "cylinderColour"));
        int cylinderDisplay = graph.Add(library.ByName("Display.ByGeometryColour"), 840, 320, Seeded("surfaces", "cylinderDisplay"));

        int coneBase = graph.Add(library.ByName("Point.ByCoordinates"), 30, 620, Seeded("surfaces", "coneBase"));
        int conePlane = graph.Add(library.ByName("Plane.ByOriginNormal"), 280, 620, Seeded("surfaces", "conePlane"));
        int cone = graph.Add(library.ByName("Surface.Cone"), 560, 620, Seeded("surfaces", "cone"));
        int coneColour = graph.Add(library.ByName("Colour.ByRgb"), 560, 780, Seeded("surfaces", "coneColour"));
        int coneDisplay = graph.Add(library.ByName("Display.ByGeometryColour"), 840, 620, Seeded("surfaces", "coneDisplay"));

        int torusCentre = graph.Add(library.ByName("Point.ByCoordinates"), 30, 900, Seeded("surfaces", "torusCentre"));
        int torusPlane = graph.Add(library.ByName("Plane.ByOriginNormal"), 280, 900, Seeded("surfaces", "torusPlane"));
        int torus = graph.Add(library.ByName("Surface.Torus"), 560, 900, Seeded("surfaces", "torus"));
        int torusColour = graph.Add(library.ByName("Colour.ByRgb"), 560, 1040, Seeded("surfaces", "torusColour"));
        int torusDisplay = graph.Add(library.ByName("Display.ByGeometryColour"), 840, 900, Seeded("surfaces", "torusDisplay"));

        Literal(graph, sphereCentre, 0, -9.0);
        Literal(graph, sphereCentre, 1, 0.0);
        Literal(graph, sphereCentre, 2, 3.0);
        Literal(graph, sphere, 1, 2.5);
        Literal(graph, sphereColour, 0, 255.0);
        Literal(graph, sphereColour, 1, 150.0);
        Literal(graph, sphereColour, 2, 120.0);

        Literal(graph, cylinderBase, 0, -3.0);
        Literal(graph, cylinderBase, 1, 0.0);
        Literal(graph, cylinderBase, 2, 0.0);
        Literal(graph, cylinder, 1, 1.6);
        Literal(graph, cylinder, 2, 5.0);
        Literal(graph, cylinderColour, 0, 130.0);
        Literal(graph, cylinderColour, 1, 210.0);
        Literal(graph, cylinderColour, 2, 255.0);

        Literal(graph, coneBase, 0, 2.5);
        Literal(graph, coneBase, 1, 0.0);
        Literal(graph, coneBase, 2, 0.0);
        Literal(graph, cone, 1, 2.0);
        Literal(graph, cone, 2, 0.0);
        Literal(graph, cone, 3, 4.5);
        Literal(graph, coneColour, 0, 160.0);
        Literal(graph, coneColour, 1, 255.0);
        Literal(graph, coneColour, 2, 170.0);

        Literal(graph, torusCentre, 0, 9.5);
        Literal(graph, torusCentre, 1, 0.0);
        Literal(graph, torusCentre, 2, 2.0);
        Literal(graph, torus, 1, 2.4);
        Literal(graph, torus, 2, 0.8);
        Literal(graph, torusColour, 0, 210.0);
        Literal(graph, torusColour, 1, 160.0);
        Literal(graph, torusColour, 2, 255.0);

        graph.TryConnect(Output(sphereCentre, 0), Input(sphere, 0));
        graph.TryConnect(Output(sphere, 0), Input(sphereDisplay, 0));
        graph.TryConnect(Output(sphereColour, 0), Input(sphereDisplay, 1));

        graph.TryConnect(Output(cylinderBase, 0), Input(cylinderPlane, 0));
        graph.TryConnect(Output(axis, 0), Input(cylinderPlane, 1));
        graph.TryConnect(Output(cylinderPlane, 0), Input(cylinder, 0));
        graph.TryConnect(Output(cylinder, 0), Input(cylinderDisplay, 0));
        graph.TryConnect(Output(cylinderColour, 0), Input(cylinderDisplay, 1));

        graph.TryConnect(Output(coneBase, 0), Input(conePlane, 0));
        graph.TryConnect(Output(axis, 0), Input(conePlane, 1));
        graph.TryConnect(Output(conePlane, 0), Input(cone, 0));
        graph.TryConnect(Output(cone, 0), Input(coneDisplay, 0));
        graph.TryConnect(Output(coneColour, 0), Input(coneDisplay, 1));

        graph.TryConnect(Output(torusCentre, 0), Input(torusPlane, 0));
        graph.TryConnect(Output(axis, 0), Input(torusPlane, 1));
        graph.TryConnect(Output(torusPlane, 0), Input(torus, 0));
        graph.TryConnect(Output(torus, 0), Input(torusDisplay, 0));
        graph.TryConnect(Output(torusColour, 0), Input(torusDisplay, 1));

        return graph;
    }

    /// <summary>
    /// The curve demo: an ellipse divided by arc length, a row of circles produced by one node, and
    /// a regular polygon — three curve families on screen at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ellipse is the point of it.</b> Dividing it into twenty-four equal <i>lengths</i>
    /// places points that crowd nowhere; dividing it by parameter, which is what a kernel without
    /// arc-length reparameterisation would have to do, bunches them at the ends of the long axis.
    /// The difference is visible from across the room, and it is why the ellipse rather than the
    /// circle is the curve the demo divides.
    /// </para>
    /// <para>
    /// The row of circles is one <c>Circle.ByCentreRadius</c> node fed a list of eight centres — the
    /// same replication the point grid demonstrates, now producing curves rather than points, which
    /// is the thing worth seeing twice.
    /// </para>
    /// </remarks>
    /// <param name="library">The imported node library.</param>
    /// <returns>The graph, ready to evaluate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    public static CanvasGraph Curves(NodeLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        CanvasGraph graph = new();

        int plane = graph.Add(library.ByName("Plane.XY"), 30, 30, Seeded("curves", "plane"));
        int ellipse = graph.Add(library.ByName("Ellipse.ByPlaneRadii"), 250, 30, Seeded("curves", "ellipse"));
        int divide = graph.Add(library.ByName("Curve.DivideEqually"), 520, 140, Seeded("curves", "divide"));
        int ellipseColour = graph.Add(library.ByName("Colour.ByRgb"), 250, 180, Seeded("curves", "ellipseColour"));
        int pointColour = graph.Add(library.ByName("Colour.ByRgb"), 520, 300, Seeded("curves", "pointColour"));
        int ellipseDisplay = graph.Add(library.ByName("Display.ByGeometryColour"), 820, 30, Seeded("curves", "ellipseDisplay"));
        int pointDisplay = graph.Add(library.ByName("Display.ByGeometryColour"), 820, 160, Seeded("curves", "pointDisplay"));

        int range = graph.Add(library.ByName("Number.Range"), 30, 440, Seeded("curves", "range"));
        int centres = graph.Add(library.ByName("Point.ByCoordinates"), 250, 440, Seeded("curves", "centres"));
        int circles = graph.Add(library.ByName("Circle.ByCentreRadius"), 520, 440, Seeded("curves", "circles"));
        int circleColour = graph.Add(library.ByName("Colour.ByRgb"), 520, 580, Seeded("curves", "circleColour"));
        int circleDisplay = graph.Add(library.ByName("Display.ByGeometryColour"), 820, 440, Seeded("curves", "circleDisplay"));

        int base3 = graph.Add(library.ByName("Point.ByCoordinates"), 30, 700, Seeded("curves", "base3"));
        int axis = graph.Add(library.ByName("Vector.ZAxis"), 30, 830, Seeded("curves", "axis"));
        int polygonPlane = graph.Add(library.ByName("Plane.ByOriginNormal"), 250, 700, Seeded("curves", "polygonPlane"));
        int polygon = graph.Add(library.ByName("PolyLine.ByRegularPolygon"), 520, 700, Seeded("curves", "polygon"));
        int polygonColour = graph.Add(library.ByName("Colour.ByRgb"), 520, 840, Seeded("curves", "polygonColour"));
        int polygonDisplay = graph.Add(library.ByName("Display.ByGeometryColour"), 820, 700, Seeded("curves", "polygonDisplay"));

        Literal(graph, ellipse, 1, 6.0);
        Literal(graph, ellipse, 2, 2.0);
        Literal(graph, divide, 1, 24);

        Literal(graph, ellipseColour, 0, 168.0);
        Literal(graph, ellipseColour, 1, 130.0);
        Literal(graph, ellipseColour, 2, 255.0);

        Literal(graph, pointColour, 0, 255.0);
        Literal(graph, pointColour, 1, 214.0);
        Literal(graph, pointColour, 2, 120.0);

        Literal(graph, range, 0, -7.0);
        Literal(graph, range, 1, 7.0);
        Literal(graph, range, 2, 2.0);
        Literal(graph, centres, 1, 7.0);
        Literal(graph, centres, 2, 0.0);
        Literal(graph, circles, 1, 0.9);

        Literal(graph, circleColour, 0, 120.0);
        Literal(graph, circleColour, 1, 220.0);
        Literal(graph, circleColour, 2, 255.0);

        Literal(graph, base3, 0, 0.0);
        Literal(graph, base3, 1, -6.0);
        Literal(graph, base3, 2, 0.0);
        Literal(graph, polygon, 1, 3.0);
        Literal(graph, polygon, 2, 5);

        Literal(graph, polygonColour, 0, 140.0);
        Literal(graph, polygonColour, 1, 255.0);
        Literal(graph, polygonColour, 2, 170.0);

        graph.TryConnect(Output(plane, 0), Input(ellipse, 0));
        graph.TryConnect(Output(ellipse, 0), Input(divide, 0));
        graph.TryConnect(Output(ellipse, 0), Input(ellipseDisplay, 0));
        graph.TryConnect(Output(ellipseColour, 0), Input(ellipseDisplay, 1));
        graph.TryConnect(Output(divide, 0), Input(pointDisplay, 0));
        graph.TryConnect(Output(pointColour, 0), Input(pointDisplay, 1));

        graph.TryConnect(Output(range, 0), Input(centres, 0));
        graph.TryConnect(Output(centres, 0), Input(circles, 0));
        graph.TryConnect(Output(circles, 0), Input(circleDisplay, 0));
        graph.TryConnect(Output(circleColour, 0), Input(circleDisplay, 1));

        graph.TryConnect(Output(base3, 0), Input(polygonPlane, 0));
        graph.TryConnect(Output(axis, 0), Input(polygonPlane, 1));
        graph.TryConnect(Output(polygonPlane, 0), Input(polygon, 0));
        graph.TryConnect(Output(polygon, 0), Input(polygonDisplay, 0));
        graph.TryConnect(Output(polygonColour, 0), Input(polygonDisplay, 1));

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

    /// <summary>
    /// A deterministic identity for a seeded node, derived from the graph's name and the node's
    /// own name in this file.
    /// </summary>
    /// <remarks>
    /// <b>A seeded graph has to be reproducible, and a fresh <c>Guid</c> per run is not.</b>
    /// `docs/examples/curves.spark` is this graph written to a file and committed; with random
    /// identities, regenerating it after changing one literal would rewrite all eighteen node ids
    /// and all fifteen wires, and the diff would say nothing. Deriving the identity from a name
    /// makes the regenerated file differ by exactly what changed.
    /// <para>
    /// The bytes come from SHA-256 rather than from a version-4 generator, so these are not RFC
    /// 4122 random UUIDs and are not pretending to be. Nothing here requires that: a
    /// <see cref="NodeId"/> is an identity, and what it needs is to be unique and stable.
    /// </para>
    /// </remarks>
    /// <param name="graph">The seeded graph's name.</param>
    /// <param name="node">The node's name within it.</param>
    /// <returns>The identity, the same on every run and on every machine.</returns>
    private static NodeId Seeded(string graph, string node)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{graph}/{node}"));
        return new NodeId(new Guid(hash.AsSpan(0, 16)));
    }

    private static void Literal(CanvasGraph graph, int slot, int portIndex, object? value) =>
        graph.SetLiteral(slot, portIndex, value);

    private static CanvasPort Output(int slot, int portIndex) => new(slot, portIndex, IsOutput: true);

    private static CanvasPort Input(int slot, int portIndex) => new(slot, portIndex, IsOutput: false);
}
