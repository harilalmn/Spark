using System;
using System.Reflection;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// The four polar-construction nodes — `E2-T40`, the graph-editor half of a parity gap the kernel
/// closed in the same change.
/// </summary>
/// <remarks>
/// <para>
/// <b>A member of <c>Spark.Geometry</c> is reachable from a code block and nowhere else.</b>
/// FR-81's promise is that somebody who knows Dynamo never reaches for a capability and finds it
/// absent, and in Spark they reach for it in the library panel — so `Point.ByCylindricalCoordinates`
/// has to *find* something when it is typed there. That is what the aliases on these four are for,
/// and `ShippedAliasTests` already checks that every declared alias resolves to a live node.
/// </para>
/// <para>
/// <b>What is left for this file is the seam the reflection diffs cannot see: the values.</b> An
/// <see cref="Angle"/> port carries degrees, and a node that forwarded to the wrong overload, or
/// bound its parameters in the wrong order, would still import cleanly and still answer with a
/// perfectly ordinary point.
/// </para>
/// </remarks>
public sealed class PolarNodeTests
{
    private static readonly NodeLibrary Library = BuildLibrary();

    /// <summary>Zero azimuth is <c>+X</c> and a quarter turn is <c>+Y</c>, through the node.</summary>
    [Fact]
    public void TheCylindricalNodesAgreeWithTheKernel()
    {
        AssertPoint(
            new Point3d(0.0, 2.0, 5.0),
            Run("Point.FromCylindrical", 2.0, Angle.FromDegrees(90.0), 5.0));

        AssertVector(
            new Vector3d(2.0, 0.0, 5.0),
            Run("Vector.FromCylindrical", 2.0, Angle.Zero, 5.0));
    }

    /// <summary>
    /// <b>The polar angle is from <c>+Z</c> here too.</b> A node that quietly used the elevation
    /// convention would put the north pole in the XY plane, and nothing else would complain.
    /// </summary>
    [Fact]
    public void TheSphericalNodesMeasureThePolarAngleFromTheZAxis()
    {
        AssertPoint(
            new Point3d(0.0, 0.0, 3.0),
            Run("Point.FromSpherical", 3.0, Angle.Zero, Angle.Zero));

        AssertPoint(
            new Point3d(3.0, 0.0, 0.0),
            Run("Point.FromSpherical", 3.0, Angle.Zero, Angle.FromDegrees(90.0)));

        AssertVector(
            new Vector3d(0.0, 0.0, 1.0),
            Run("Vector.FromSpherical", 1.0, Angle.Zero, Angle.Zero));
    }

    /// <summary>
    /// The defaults are a usable node the moment it is placed, rather than a point at the origin
    /// that looks like a bug — a unit radius, no rotation, no height.
    /// </summary>
    [Fact]
    public void APlacedNodeWithNothingWiredIsTheUnitPointOnTheXAxis()
    {
        AssertPoint(new Point3d(1.0, 0.0, 0.0), Run("Point.FromCylindrical"));
        AssertPoint(new Point3d(0.0, 0.0, 1.0), Run("Point.FromSpherical"));
    }

    private static void AssertPoint(Point3d expected, object? actual)
    {
        Point3d point = Assert.IsType<Point3d>(actual);

        Assert.Equal(expected.X, point.X, 12);
        Assert.Equal(expected.Y, point.Y, 12);
        Assert.Equal(expected.Z, point.Z, 12);
    }

    private static void AssertVector(Vector3d expected, object? actual)
    {
        Vector3d vector = Assert.IsType<Vector3d>(actual);

        Assert.Equal(expected.X, vector.X, 12);
        Assert.Equal(expected.Y, vector.Y, 12);
        Assert.Equal(expected.Z, vector.Z, 12);
    }

    private static object? Run(string name, params object?[] arguments)
    {
        Graph graph = new();
        NodeInstance node = graph.AddNode(Library.ByName(name));

        for (int i = 0; i < arguments.Length; i++)
        {
            graph.SetLiteral(node.Id, i, arguments[i]);
        }

        EvaluationResult result = GraphEvaluator.Evaluate(
            graph, new EvaluationContext(), TestContext.Current.CancellationToken);

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
