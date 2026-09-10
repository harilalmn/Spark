using System;
using System.Reflection;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// `Plane.FromBestFit` and `Plane.FromLineAndPoint` as nodes — `E2-T40`.
/// </summary>
/// <remarks>
/// <b>The load-bearing test here is <see cref="TheFitSeesTheWholeListRatherThanOnePointAtATime"/>,
/// and it is the same trap the List category exists for.</b> A port handed a list normally
/// replicates: the node runs once per item. A fit that replicated would be handed one point at a
/// time, and a fit through one point is not an error — it is a refusal, or worse a plane — so the
/// failure would arrive as a list of diagnostics or a list of planes rather than as one plane, and
/// would look like a user mistake. A typed <c>IReadOnlyList&lt;Point3d&gt;</c> port is a list port
/// to the marshaller and so does not replicate; this asserts that through the engine rather than
/// by reading the importer.
/// </remarks>
public sealed class PlaneFitNodeTests
{
    private static readonly NodeLibrary Library = BuildLibrary();

    /// <summary>Nine points on one plane become one plane, not nine.</summary>
    [Fact]
    public void TheFitSeesTheWholeListRatherThanOnePointAtATime()
    {
        SparkList points = SparkList.Of(
            new Point3d(0.0, 0.0, 2.0),
            new Point3d(1.0, 0.0, 2.0),
            new Point3d(1.0, 1.0, 2.0),
            new Point3d(0.0, 1.0, 2.0));

        Plane plane = Assert.IsType<Plane>(Run("Plane.FromBestFit", points));

        Assert.Equal(1.0, plane.Normal.Dot(Vector3d.ZAxis), 9);
        Assert.Equal(2.0, plane.Origin.Z, 9);
    }

    /// <summary>A line and a point off it, through the node.</summary>
    [Fact]
    public void TheLineAndPointNodeGivesThePlaneThroughBoth()
    {
        Line line = new(Point3d.Origin, new Point3d(0.0, 0.0, 3.0));

        Plane plane = Assert.IsType<Plane>(
            Run("Plane.FromLineAndPoint", line, new Point3d(2.0, 0.0, 1.0)));

        Assert.True(plane.Contains(line.EndPoint, Tolerance.Default));
        Assert.Equal(0.0, plane.Normal.Dot(Vector3d.ZAxis), 9);
    }

    /// <summary>
    /// A degenerate input reaches the user as a diagnostic on the node rather than as a plane
    /// nobody asked for, which is the whole reason the kernel refuses instead of guessing.
    /// </summary>
    [Fact]
    public void CollinearPointsFailTheNodeRatherThanProducingAPlane()
    {
        SparkList collinear = SparkList.Of(
            new Point3d(0.0, 0.0, 0.0),
            new Point3d(1.0, 1.0, 1.0),
            new Point3d(2.0, 2.0, 2.0));

        Assert.True(Failed("Plane.FromBestFit", collinear));
    }

    private static object? Run(string name, params object?[] arguments)
    {
        (EvaluationResult result, NodeId id) = Evaluate(name, arguments);

        Assert.Empty(result.Diagnostics);
        return result.Value(id);
    }

    private static bool Failed(string name, params object?[] arguments)
    {
        (EvaluationResult result, _) = Evaluate(name, arguments);

        return result.Diagnostics.Count > 0;
    }

    private static (EvaluationResult Result, NodeId Id) Evaluate(string name, object?[] arguments)
    {
        Graph graph = new();
        NodeInstance node = graph.AddNode(Library.ByName(name));

        for (int i = 0; i < arguments.Length; i++)
        {
            graph.SetLiteral(node.Id, i, arguments[i]);
        }

        return (
            GraphEvaluator.Evaluate(graph, new EvaluationContext(), TestContext.Current.CancellationToken),
            node.Id);
    }

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(Assembly.Load("Spark.Nodes.Core")));
        return library;
    }
}
