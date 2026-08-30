using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spark.Api;
using Spark.Engine;
using Spark.UI.Canvas;
using Spark.UI.Graph;
using Spark.UI.Theming;

namespace Spark.UI.Tests;

/// <summary>
/// The canvas's view of a real engine graph: layout, the wire projection, the compatibility
/// preview, and the mapping from a run's node states onto the design language's visual states.
/// </summary>
public sealed class CanvasGraphTests
{
    [Fact]
    public void ANodesHeightFollowsItsPortCount()
    {
        CanvasGraph graph = new();
        int one = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 0, 0);
        int three = graph.Add(TestGraphs.Library.ByName("Point.ByCoordinates"), 0, 0);

        Assert.True(graph.Nodes[three].Height > graph.Nodes[one].Height);
        Assert.Equal(CanvasNode.PortPitch * 2, graph.Nodes[three].Height - graph.Nodes[one].Height);
    }

    [Fact]
    public void InputPortsSitOnTheLeftEdgeAndOutputsOnTheRight()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Math.Add"), 100, 50);
        CanvasNode node = graph.Nodes[slot];

        node.InputPortCentre(0, out double inputX, out double inputY);
        node.OutputPortCentre(0, out double outputX, out double outputY);

        Assert.Equal(100, inputX);
        Assert.Equal(100 + node.Width, outputX);
        Assert.Equal(inputY, outputY);
        Assert.Equal(50 + CanvasNode.HeaderHeight + (CanvasNode.PortPitch * 0.5), inputY);

        node.InputPortCentre(1, out _, out double secondY);
        Assert.Equal(inputY + CanvasNode.PortPitch, secondY);
    }

    /// <summary>
    /// The category on a definition decides the header colour, and an unknown one still resolves to
    /// something legible rather than throwing.
    /// </summary>
    [Fact]
    public void CategoriesFromTheEngineReachTheRenderer()
    {
        CanvasGraph graph = new();
        int point = graph.Add(TestGraphs.Library.ByName("Point.ByCoordinates"), 0, 0);
        int math = graph.Add(TestGraphs.Library.ByName("Math.Add"), 0, 0);
        int input = graph.Add(TestGraphs.Library.ByName("Number.Range"), 0, 0);

        Assert.Equal(NodeCategory.Point, graph.Nodes[point].Category);
        Assert.Equal(NodeCategory.Math, graph.Nodes[math].Category);
        Assert.Equal(NodeCategory.Input, graph.Nodes[input].Category);
        Assert.Equal(NodeCategory.Custom, NodeCategoryNames.Parse("Something.A.Package.Invented"));
    }

    /// <summary>
    /// Declared rank reaches the port shape, which is the only thing on the canvas that says a
    /// lacing is about to happen before the graph runs (§7.6).
    /// </summary>
    [Fact]
    public void DeclaredRankReachesThePortShape()
    {
        CanvasGraph graph = new();
        int range = graph.Add(TestGraphs.Library.ByName("Number.Range"), 0, 0);
        int point = graph.Add(TestGraphs.Library.ByName("Point.ByCoordinates"), 0, 0);

        Assert.Equal(1, graph.Nodes[range].Outputs[0].DeclaredRank);
        Assert.Equal(0, graph.Nodes[point].Inputs[0].DeclaredRank);
    }

    /// <summary>
    /// The wire preview is the engine's own type check, not a UI guess. A number into a number is
    /// accepted; a point into a number is refused.
    /// </summary>
    [Fact]
    public void ThePreviewReportsTheEnginesAnswerRatherThanAlwaysAccepting()
    {
        CanvasGraph graph = new();
        int value = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        int sin = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);
        int point = graph.Add(TestGraphs.Library.ByName("Point.ByCoordinates"), 600, 0);

        Assert.Equal(WireOutcome.Accepted, graph.Preview(Output(value, 0), Input(sin, 0)));

        // Point3d into a double port: no rule in the compatibility order matches.
        Assert.Equal(WireOutcome.Refused, graph.Preview(Output(point, 0), Input(sin, 0)));

        // Output to output, and a node to itself, are refused before types are consulted.
        Assert.Equal(WireOutcome.Refused, graph.Preview(Output(value, 0), Output(sin, 0)));
        Assert.Equal(WireOutcome.Refused, graph.Preview(Output(sin, 0), Input(sin, 0)));
    }

    /// <summary>A wire that would close a cycle is refused, and the preview says so first.</summary>
    [Fact]
    public void AWireThatWouldCloseACycleIsRefused()
    {
        CanvasGraph graph = new();
        int a = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 0, 0);
        int b = graph.Add(TestGraphs.Library.ByName("Math.Cos"), 300, 0);

        Assert.True(graph.TryConnect(Output(a, 0), Input(b, 0)));

        Assert.Equal(WireOutcome.Refused, graph.Preview(Output(b, 0), Input(a, 0)));
        Assert.False(graph.TryConnect(Output(b, 0), Input(a, 0)));
        Assert.Single(graph.Wires);
    }

    [Fact]
    public void ASecondWireIntoOneInputReplacesTheFirst()
    {
        CanvasGraph graph = new();
        int a = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        int b = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 200);
        int c = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);

        Assert.True(graph.TryConnect(Output(a, 0), Input(c, 0)));
        Assert.True(graph.TryConnect(Output(b, 0), Input(c, 0)));

        CanvasWire wire = Assert.Single(graph.Wires);
        Assert.Equal(b, wire.From.NodeIndex);
    }

    [Fact]
    public void DisconnectingAWireRemovesItFromTheEngineToo()
    {
        CanvasGraph graph = new();
        int a = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        int b = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);

        graph.TryConnect(Output(a, 0), Input(b, 0));
        CanvasWire wire = Assert.Single(graph.Wires);

        Assert.True(graph.Disconnect(wire));
        Assert.Empty(graph.Wires);
        Assert.Empty(graph.Engine.Wires());
    }

    /// <summary>
    /// Removing a node renumbers every slot after it, and the wire projection has to follow or the
    /// canvas draws wires between the wrong nodes.
    /// </summary>
    [Fact]
    public void RemovingANodeRenumbersTheSlotsAfterIt()
    {
        CanvasGraph graph = new();
        int a = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        int b = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);
        int c = graph.Add(TestGraphs.Library.ByName("Math.Cos"), 600, 0);

        graph.TryConnect(Output(b, 0), Input(c, 0));
        graph.Remove(a);

        Assert.Equal(2, graph.Nodes.Count);

        CanvasWire wire = Assert.Single(graph.Wires);
        Assert.Equal(0, wire.From.NodeIndex);
        Assert.Equal(1, wire.To.NodeIndex);
    }

    /// <summary>
    /// The non-cascading rule, as a test. One node errors; the node below it is greyed as
    /// <i>not evaluated</i> and carries no diagnostic of its own.
    /// </summary>
    [Fact]
    public void AnErrorDoesNotCascadeIntoDownstreamErrors()
    {
        CanvasGraph graph = DemoGraphs.Demo(TestGraphs.Library);
        EvaluationResult result = GraphEvaluator.Evaluate(graph.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);
        graph.ApplyResult(result);

        CanvasNode divide = Node(graph, "Math.Divide");
        CanvasNode translate = Node(graph, "Point.Translate");

        Assert.True(divide.State.HasFlag(CanvasNodeState.Error));
        Assert.NotNull(divide.Message);

        Assert.True(translate.State.HasFlag(CanvasNodeState.NotEvaluated));
        Assert.False(translate.State.HasFlag(CanvasNodeState.Error));
        Assert.Null(translate.Message);

        // Exactly one node is blamed, however many are downstream of it.
        Assert.Equal(1, graph.Nodes.Count(node => node.State.HasFlag(CanvasNodeState.Error)));
    }

    /// <summary>
    /// The seeded demo is the whole point of the slice: two ranges of ten crossed into one hundred
    /// points, arranged as a grid rather than a diagonal.
    /// </summary>
    [Fact]
    public void TheDemoGraphProducesAHundredPointsInAGrid()
    {
        CanvasGraph graph = DemoGraphs.Demo(TestGraphs.Library);
        EvaluationResult result = GraphEvaluator.Evaluate(graph.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);

        CanvasNode points = Node(graph, "Point.ByCoordinates");
        SparkList grid = Assert.IsType<SparkList>(result.Value(points.Id));

        // Cross Product raises rank by k, not by one: ten crossed with ten is a 10 x 10 nested
        // list, never a flat hundred.
        Assert.Equal(2, grid.Rank);
        Assert.Equal(DemoGraphs.GridSide, grid.Count);

        SparkList row = Assert.IsType<SparkList>(grid[0]);
        Assert.Equal(DemoGraphs.GridSide, row.Count);

        // And the values really are a grid: the first row varies in y at a fixed x.
        Spark.Geometry.Point3d first = Assert.IsType<Spark.Geometry.Point3d>(row[0]);
        Spark.Geometry.Point3d second = Assert.IsType<Spark.Geometry.Point3d>(row[1]);
        Assert.Equal(first.X, second.X);
        Assert.NotEqual(first.Y, second.Y);
    }

    /// <summary>
    /// `docs/examples/curves.spark` is exactly what this build saves, so the example in the
    /// repository cannot drift from the format that reads it.
    /// </summary>
    /// <remarks>
    /// A golden file rather than a description of one. If this fails, either the format changed —
    /// in which case the example is regenerated and the change is visible in the diff, which is the
    /// entire point of ADR-0017 — or the demo changed, which is the same conversation.
    /// </remarks>
    [Fact]
    public void TheCheckedInCurvesExampleMatchesWhatThisBuildWrites()
    {
        string path = ExamplePath("curves.spark");

        // Regenerating is a deliberate act with an environment variable, not something a failing
        // test does on its own. A golden file that rewrites itself when it disagrees is not a
        // golden file; it is a very slow way of asserting true.
        if (Environment.GetEnvironmentVariable("SPARK_UPDATE_EXAMPLES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, CanvasDocument.Save(DemoGraphs.Curves(TestGraphs.Library)));
        }

        Assert.True(File.Exists(path), $"The example graph is missing: {path}");

        // Line endings are normalised on both sides: the file is committed with LF by
        // .gitattributes, and a Windows checkout that converted it would fail this test for a
        // reason that has nothing to do with the format.
        string expected = File.ReadAllText(path).ReplaceLineEndings(LineFeed);
        string actual = CanvasDocument.Save(DemoGraphs.Curves(TestGraphs.Library))
            .ReplaceLineEndings(LineFeed);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// `docs/examples/surfaces.spark` is exactly what this build saves, for the reason the curve
    /// example is: an example that has drifted from the format teaches somebody the wrong thing.
    /// </summary>
    [Fact]
    public void TheCheckedInSurfacesExampleMatchesWhatThisBuildWrites()
    {
        string path = ExamplePath("surfaces.spark");

        if (Environment.GetEnvironmentVariable("SPARK_UPDATE_EXAMPLES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, CanvasDocument.Save(DemoGraphs.Surfaces(TestGraphs.Library)));
        }

        Assert.True(File.Exists(path), $"The example graph is missing: {path}");

        string expected = File.ReadAllText(path).ReplaceLineEndings(LineFeed);
        string actual = CanvasDocument.Save(DemoGraphs.Surfaces(TestGraphs.Library))
            .ReplaceLineEndings(LineFeed);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// The surface example opens, evaluates without error, and produces surfaces — which is the
    /// half a byte comparison cannot check.
    /// </summary>
    [Fact]
    public void TheCheckedInSurfacesExampleOpensAndEvaluates()
    {
        CanvasGraph opened = CanvasDocument.Open(
            File.ReadAllText(ExamplePath("surfaces.spark")), TestGraphs.Library);

        EvaluationResult result = GraphEvaluator.Evaluate(
            opened.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);
        opened.ApplyResult(result);

        Assert.DoesNotContain(opened.Nodes, node => node.State.HasFlag(CanvasNodeState.Error));

        int surfaces = 0;

        foreach (NodeInstance node in opened.Engine.Nodes())
        {
            if (result.Value(node.Id) is Spark.Geometry.Surface)
            {
                surfaces++;
            }
        }

        Assert.Equal(4, surfaces);

        // And one solid, which is the seam reaching the canvas.
        Assert.Contains(opened.Engine.Nodes(), node => result.Value(node.Id) is Spark.Geometry.Brep);
    }

    /// <summary>The checked-in example opens into the graph it was written from.</summary>
    [Fact]
    public void TheCheckedInCurvesExampleOpensAndEvaluates()
    {
        CanvasGraph opened = CanvasDocument.Open(
            File.ReadAllText(ExamplePath("curves.spark")), TestGraphs.Library);

        EvaluationResult result = GraphEvaluator.Evaluate(
            opened.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);
        opened.ApplyResult(result);

        Assert.DoesNotContain(opened.Nodes, node => node.State.HasFlag(CanvasNodeState.Error));
        Assert.Equal(18, opened.Nodes.Count);
    }

    /// <summary>
    /// The line ending the golden comparison normalises to. Named rather than inline because
    /// `.gitattributes` commits `.spark` files as LF, and a Windows checkout that converted them
    /// would otherwise fail the comparison for a reason that has nothing to do with the format.
    /// </summary>
    private const string LineFeed = "\n";

    private static string ExamplePath(string name)
    {
        // Walks up from the test binary to the repository root. A relative path from the working
        // directory would depend on which directory the runner was started in, which is how a test
        // like this passes locally and fails in CI.
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "docs", "examples", name);
    }

    /// <summary>
    /// A graph survives a trip through a `.spark` file with its layout intact — which is what makes
    /// the file a document rather than a description of one.
    /// </summary>
    [Fact]
    public void ACanvasGraphRoundTripsThroughAFileWithItsLayout()
    {
        CanvasGraph original = DemoGraphs.Curves(TestGraphs.Library);
        original.Nodes[0].X = 123.5;
        original.Nodes[0].Y = -45.25;

        string text = CanvasDocument.Save(original);
        CanvasGraph reopened = CanvasDocument.Open(text, TestGraphs.Library);

        Assert.Equal(original.Nodes.Count, reopened.Nodes.Count);
        Assert.Equal(original.Wires.Count, reopened.Wires.Count);

        CanvasNode moved = reopened.Nodes.Single(node => node.Id == original.Nodes[0].Id);
        Assert.Equal(123.5, moved.X);
        Assert.Equal(-45.25, moved.Y);
        Assert.Equal(original.Nodes[0].Title, moved.Title);

        // And it still evaluates to the same thing, which is the claim that matters: the file
        // carried a graph, not a picture of one.
        EvaluationResult result = GraphEvaluator.Evaluate(
            reopened.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);
        reopened.ApplyResult(result);

        Assert.DoesNotContain(reopened.Nodes, node => node.State.HasFlag(CanvasNodeState.Error));
        SparkList circles = Assert.IsType<SparkList>(
            result.Value(Node(reopened, "Circle.ByCentreRadius").Id));
        Assert.Equal(8, circles.Count);
    }

    /// <summary>
    /// Saving a reopened graph reproduces the file byte for byte, so an untouched graph produces no
    /// diff at all. That is the whole reason ADR-0017 chose text over a container.
    /// </summary>
    [Fact]
    public void ReopeningAndResavingAGraphChangesNoBytes()
    {
        string first = CanvasDocument.Save(DemoGraphs.Curves(TestGraphs.Library));
        string second = CanvasDocument.Save(CanvasDocument.Open(first, TestGraphs.Library));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The curve demo evaluates without a diagnostic and produces all three curve families.
    /// </summary>
    [Fact]
    public void TheCurveDemoProducesAnEllipseAndARowOfCirclesAndAPolygon()
    {
        CanvasGraph graph = DemoGraphs.Curves(TestGraphs.Library);
        EvaluationResult result = GraphEvaluator.Evaluate(graph.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);
        graph.ApplyResult(result);

        Assert.DoesNotContain(graph.Nodes, node => node.State.HasFlag(CanvasNodeState.Error));

        Spark.Geometry.EllipseCurve ellipse =
            Assert.IsType<Spark.Geometry.EllipseCurve>(result.Value(Node(graph, "Ellipse.ByPlaneRadii").Id));
        Assert.Equal(6.0, ellipse.XRadius);
        Assert.Equal(2.0, ellipse.YRadius);

        // One node, eight circles: replication over the list of centres, producing curves.
        SparkList circles = Assert.IsType<SparkList>(result.Value(Node(graph, "Circle.ByCentreRadius").Id));
        Assert.Equal(8, circles.Count);
        Assert.IsType<Spark.Geometry.Circle>(circles[0]);

        Spark.Geometry.PolyLine polygon =
            Assert.IsType<Spark.Geometry.PolyLine>(result.Value(Node(graph, "PolyLine.ByRegularPolygon").Id));
        Assert.Equal(5, polygon.SegmentCount);
        Assert.True(polygon.IsClosed);
    }

    /// <summary>
    /// The division in the curve demo is by arc length, which on an ellipse is a different set of
    /// points from a division by parameter — and the difference is what the demo is showing.
    /// </summary>
    [Fact]
    public void TheCurveDemoDividesItsEllipseByLengthRatherThanByParameter()
    {
        CanvasGraph graph = DemoGraphs.Curves(TestGraphs.Library);
        EvaluationResult result = GraphEvaluator.Evaluate(graph.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);

        SparkList points = Assert.IsType<SparkList>(result.Value(Node(graph, "Curve.DivideEqually").Id));
        Assert.Equal(25, points.Count);

        Spark.Geometry.EllipseCurve ellipse =
            Assert.IsType<Spark.Geometry.EllipseCurve>(result.Value(Node(graph, "Ellipse.ByPlaneRadii").Id));

        // Consecutive chords of an equal-length division of an ellipse differ by a few percent at
        // most; an equal-parameter division of these radii differs by a factor of about three.
        double shortest = double.MaxValue;
        double longest = 0.0;
        for (int index = 1; index < points.Count; index++)
        {
            Spark.Geometry.Point3d previous = Assert.IsType<Spark.Geometry.Point3d>(points[index - 1]);
            Spark.Geometry.Point3d current = Assert.IsType<Spark.Geometry.Point3d>(points[index]);
            double chord = previous.DistanceTo(current);
            shortest = Math.Min(shortest, chord);
            longest = Math.Max(longest, chord);
        }

        Assert.True(
            longest / shortest < 1.1,
            $"Chords ran from {shortest} to {longest}, which is not an equal-length division.");
        Assert.Equal(ellipse.Length / 24.0, longest, ellipse.Length / 24.0 * 0.05);
    }

    /// <summary>
    /// The same graph under Longest laces to a ten-point diagonal. This is the comparison the demo
    /// exists to make, and it fails if lacing is ever quietly ignored.
    /// </summary>
    [Fact]
    public void TheSameGraphUnderLongestProducesATenPointDiagonal()
    {
        CanvasGraph graph = DemoGraphs.Demo(TestGraphs.Library);
        CanvasNode points = Node(graph, "Point.ByCoordinates");

        graph.Engine.SetLacing(points.Id, LacingMode.Longest);

        EvaluationResult result = GraphEvaluator.Evaluate(graph.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);
        SparkList line = Assert.IsType<SparkList>(result.Value(points.Id));

        Assert.Equal(1, line.Rank);
        Assert.Equal(DemoGraphs.GridSide, line.Count);
    }

    /// <summary>
    /// Only ports nothing consumes are previewed, or the display node's hundred coloured points
    /// would be drawn coincident with the point node's hundred grey ones.
    /// </summary>
    [Fact]
    public void OnlyTerminalPortsArePreviewed()
    {
        CanvasGraph graph = DemoGraphs.Demo(TestGraphs.Library);

        HashSet<string> previewed =
        [
            .. graph.PreviewPorts().Select(port => graph.Nodes[port.Slot].Title),
        ];

        Assert.Contains("Display.ByGeometryColour", previewed);
        Assert.Contains("Point.Translate", previewed);
        Assert.DoesNotContain("Point.ByCoordinates", previewed);
        Assert.DoesNotContain("Number.Range", previewed);
    }

    /// <summary>
    /// Editing a literal marks the node and everything downstream dirty, which is what makes the
    /// next run recompute rather than serve the cache.
    /// </summary>
    [Fact]
    public void SettingALiteralMarksTheSubgraphDirty()
    {
        CanvasGraph graph = new();
        int value = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        int sin = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);
        graph.TryConnect(Output(value, 0), Input(sin, 0));

        graph.Engine.MarkAllClean();
        graph.SetLiteral(value, 0, 90.0);

        IReadOnlySet<NodeId> dirty = graph.Engine.DirtyNodes();
        Assert.Contains(graph.Nodes[value].Id, dirty);
        Assert.Contains(graph.Nodes[sin].Id, dirty);

        EvaluationResult result = GraphEvaluator.Evaluate(graph.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);
        Assert.Equal(1.0, Assert.IsType<double>(result.Value(graph.Nodes[sin].Id)), 9);
    }

    /// <summary>
    /// Every mutation runs inside the edit scope when one is set, which is what serialises canvas
    /// edits against an evaluation running on a worker thread.
    /// </summary>
    [Fact]
    public void EveryMutationRunsInsideTheEditScope()
    {
        CanvasGraph graph = new();
        int entered = 0;
        graph.EditScope = edit =>
        {
            entered++;
            edit();
        };

        int a = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        int b = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);
        graph.TryConnect(Output(a, 0), Input(b, 0));
        graph.SetLiteral(a, 0, 1.0);
        graph.Disconnect(graph.Wires[0]);
        graph.Remove(b);

        Assert.Equal(6, entered);
    }

    [Fact]
    public void AnEmptyGraphHasNonDegenerateBounds()
    {
        CanvasBounds bounds = new CanvasGraph().ComputeBounds();

        // Zoom-to-fit divides by these, so a zero-area default would produce an infinite zoom.
        Assert.True(bounds.Width > 0);
        Assert.True(bounds.Height > 0);
    }

    [Fact]
    public void GraphBoundsCoverEveryNode()
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Math.Sin"), 0, 0);
        int far = graph.Add(TestGraphs.Library.ByName("Math.Cos"), 900, 700);

        CanvasBounds bounds = graph.ComputeBounds();
        CanvasNode node = graph.Nodes[far];

        Assert.True(bounds.Contains(0, 0));
        Assert.True(bounds.Contains(node.X + node.Width, node.Y + node.Height));
    }

    [Fact]
    public void TheSyntheticGraphIsTheSizeItWasAskedForAndIsWiredThroughout()
    {
        CanvasGraph graph = DemoGraphs.Synthetic(TestGraphs.Library, 2000);

        Assert.Equal(2000, graph.Nodes.Count);
        Assert.True(
            graph.Wires.Count > 1500,
            $"Only {graph.Wires.Count} wires; the wire layer is under-exercised by the benchmark.");
    }

    [Fact]
    public void ASyntheticGraphOfZeroNodesStillProducesOne() =>
        Assert.Single(DemoGraphs.Synthetic(TestGraphs.Library, 0).Nodes);

    /// <summary>
    /// A port carries the type it wants, taken from the real definition and phrased for a reader.
    /// </summary>
    /// <remarks>
    /// This is the seam the canvas draws from. <c>Circle.ByCentreRadius</c> is the node that
    /// prompted it: a port called <c>centre</c> gave a user no way to know a <c>Point3d</c> was
    /// wanted, and the two places that would have said so — the library signature and the
    /// wire-drag preview — are both somewhere other than the node.
    /// </remarks>
    [Fact]
    public void APortCarriesTheTypeItWants()
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Circle.ByCentreRadius"), 0, 0);

        CanvasNode circle = Node(graph, "Circle.ByCentreRadius");

        Assert.Equal("centre", circle.Inputs[0].Name);
        Assert.Equal("Point3d", circle.Inputs[0].TypeName);
        Assert.Equal("radius", circle.Inputs[1].Name);
        Assert.Equal("number", circle.Inputs[1].TypeName);

        // The output is called `circle` and returns a `Circle`, so the type is not said twice.
        Assert.Equal("circle", circle.Outputs[0].Name);
        Assert.Null(circle.Outputs[0].TypeName);
    }

    /// <summary>
    /// A node is wide enough for its widest port row, not only for its title.
    /// </summary>
    /// <remarks>
    /// <c>BoundingBox.ByCorners</c> is the case: its title fits inside the minimum width, and its
    /// first row — <c>corner Point3d</c> against <c>BoundingBox box</c> — does not. Before the row
    /// was measured, the two halves of that row met in the middle.
    /// </remarks>
    [Fact]
    public void ANodeIsWideEnoughForItsWidestPortRow()
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("BoundingBox.ByCorners"), 0, 0);
        graph.Add(TestGraphs.Library.ByName("Point.Origin"), 0, 0);

        // A node with one short row and no inputs stays at the minimum.
        Assert.Equal(CanvasNode.MinimumWidth, Node(graph, "Point.Origin").Width);

        // And one whose rows are wider than its title grows past what the title asks for. The
        // bound is deliberately above the title's own estimate — 34 + 21 characters x 6.8 is
        // 176.8 — because "wider than the minimum" would also be true of a node sized from its
        // title alone, and a test that passes for the wrong reason is the trap this project has
        // fallen into three times ([N18](../../docs/NOTES.md), [N19](../../docs/NOTES.md)).
        Assert.True(
            Node(graph, "BoundingBox.ByCorners").Width > 190,
            "The row 'corner Point3d' against 'BoundingBox box' did not widen the node.");
    }

    private static CanvasNode Node(CanvasGraph graph, string title) =>
        graph.Nodes.First(node => string.Equals(node.Title, title, StringComparison.Ordinal));

    private static CanvasPort Output(int slot, int portIndex) => new(slot, portIndex, IsOutput: true);

    private static CanvasPort Input(int slot, int portIndex) => new(slot, portIndex, IsOutput: false);
}
