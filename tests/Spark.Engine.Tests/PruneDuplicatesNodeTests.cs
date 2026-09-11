using System.Linq;
using System.Reflection;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// `Point.PruneDuplicates` as a node - `E2-T16`, step B.
/// </summary>
/// <remarks>
/// <b>The trap is replication</b>, the same one `PlaneFitNodeTests` guards: a node handed a list
/// normally runs once per item, and a prune that ran once per point would compare each point with
/// nothing and hand back the list unchanged. Its port is a typed list, so the whole list arrives; this
/// asserts that through the engine.
/// </remarks>
public sealed class PruneDuplicatesNodeTests
{
    private static readonly NodeLibrary Library = BuildLibrary();

    /// <summary>Four points with one near copy come back as three, in order.</summary>
    [Fact]
    public void TheNodeSeesTheWholeListAndKeepsTheFirstOfEach()
    {
        SparkList points = SparkList.Of(
            new Point3d(0, 0, 0),
            new Point3d(2, 0, 0),
            new Point3d(0, 0.0004, 0),
            new Point3d(3, 0, 0));

        Point3d[] kept = [.. Assert.IsType<SparkList>(Run("Point.PruneDuplicates", points, 0.001)).Cast<Point3d>()];

        Assert.Equal(new[] { new Point3d(0, 0, 0), new Point3d(2, 0, 0), new Point3d(3, 0, 0) }, kept);
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
