using System.Linq;
using System.Reflection;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// `Curve.IntersectWith` and `Curve.OverlapWith` as nodes - `E2-T11`, step C.
/// </summary>
/// <remarks>
/// Run through the engine rather than by calling the methods, because what a graph user gets is
/// what the importer and the marshaller make of them: a list port out, and a curve port in that
/// replicates over a list like any other.
/// </remarks>
public sealed class CurveIntersectionNodeTests
{
    private static readonly NodeLibrary Library = BuildLibrary();

    /// <summary>A line through a circle gives the two points where it crosses.</summary>
    [Fact]
    public void TheIntersectNodeGivesThePointsWhereTwoCurvesCross()
    {
        Line line = new(new Point3d(-2, 0, 0), new Point3d(2, 0, 0));
        Circle circle = Circle.FromCenterRadius(Point3d.Origin, 1.0);

        Point3d[] points = [.. Assert.IsType<SparkList>(Run("Curve.IntersectWith", line, circle)).Cast<Point3d>()];

        Assert.Equal(2, points.Length);
        Assert.Contains(points, p => p.DistanceTo(new Point3d(-1, 0, 0)) < 1e-9);
        Assert.Contains(points, p => p.DistanceTo(new Point3d(1, 0, 0)) < 1e-9);
    }

    /// <summary>Two lines on one line give their shared stretch, cut from the first.</summary>
    [Fact]
    public void TheOverlapNodeGivesTheSharedStretchAsACurve()
    {
        Line first = new(new Point3d(0, 0, 0), new Point3d(2, 0, 0));
        Line second = new(new Point3d(1, 0, 0), new Point3d(3, 0, 0));

        SparkList curves = Assert.IsType<SparkList>(Run("Curve.OverlapWith", first, second));
        Curve piece = Assert.IsAssignableFrom<Curve>(Assert.Single(curves));

        Assert.True(piece.StartPoint.DistanceTo(new Point3d(1, 0, 0)) < 1e-9);
        Assert.True(piece.EndPoint.DistanceTo(new Point3d(2, 0, 0)) < 1e-9);
    }

    /// <summary>Curves with nothing in common give an empty list, not an error.</summary>
    [Fact]
    public void CurvesApartGiveAnEmptyList()
    {
        Line line = new(new Point3d(-2, 0, 0), new Point3d(2, 0, 0));
        Circle far = Circle.FromCenterRadius(new Point3d(0, 10, 0), 1.0);

        Assert.Empty(Assert.IsType<SparkList>(Run("Curve.IntersectWith", line, far)));
    }

    private static object? Run(string name, params object?[] arguments)
    {
        Graph graph = new();
        NodeInstance node = graph.AddNode(Library.ByName(name));

        for (int i = 0; i < arguments.Length; i++)
        {
            graph.SetLiteral(node.Id, i, arguments[i]);
        }

        EvaluationResult result = GraphEvaluator.Evaluate(graph, new EvaluationContext(), TestContext.Current.CancellationToken);

        Assert.Empty(result.Diagnostics);
        return result.Value(node.Id);
    }

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(Assembly.Load("Spark.Nodes.Core")));
        return library;
    }
}
