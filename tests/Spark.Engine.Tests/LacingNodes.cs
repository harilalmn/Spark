using System;
using System.Collections.Generic;
using System.Reflection;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// A circle, for the cases in the lacing table that need one. The kernel has no circle yet and the
/// table only ever compares them for equality, so a record struct is enough and keeps the corpus
/// independent of when Curve lands.
/// </summary>
public readonly record struct TestCircle(Point3d Centre, double Radius);

/// <summary>
/// The member bodies behind the case table's nodes. These are ordinary static methods, compiled
/// into invokers by <see cref="NodeInvoker"/> exactly as the importer will compile the real ones —
/// so the whole corpus runs through the expression-tree path rather than around it.
/// </summary>
public static class LacingMembers
{
    public static double Add(double a, double b) => a + b;

    public static double Sum(IReadOnlyList<double> xs)
    {
        double total = 0;
        foreach (double x in xs)
        {
            total += x;
        }

        return total;
    }

    public static double Total2d(IReadOnlyList<IReadOnlyList<double>> rows)
    {
        double total = 0;
        foreach (IReadOnlyList<double> row in rows)
        {
            foreach (double value in row)
            {
                total += value;
            }
        }

        return total;
    }

    public static IReadOnlyList<double> Range(double n)
    {
        List<double> values = [];
        for (int index = 0; index < (int)n; index++)
        {
            values.Add(index);
        }

        return values;
    }

    public static Point3d PointByCoordinates(double x, double y, double z) => new(x, y, z);

    public static TestCircle CircleByCenterRadius(Point3d center, double radius) => new(center, radius);

    public static Point3d GridByXY(double x, double y) => new(x, y, 0);

    public static void Bounds(IReadOnlyList<double> xs, out double min, out double max)
    {
        if (xs.Count == 0)
        {
            throw new InvalidOperationException("Bounds of an empty list is undefined.");
        }

        min = double.MaxValue;
        max = double.MinValue;

        foreach (double x in xs)
        {
            min = Math.Min(min, x);
            max = Math.Max(max, x);
        }
    }

    public static void Split(double a, double b, out double sum, out double difference)
    {
        sum = a + b;
        difference = a - b;
    }

    public static double Invert(double x) =>
        x == 0.0 ? throw new DivideByZeroException("Cannot invert zero.") : 1.0 / x;

    public static object? Echo(object? x) => x;

    public static double Scale(double x, double factor) => x * factor;

    public static int ListCount(object? list) => list is SparkList sparkList ? sparkList.Count : 1;

    public static object? ListReverse(object? list)
    {
        if (list is not SparkList sparkList)
        {
            return list;
        }

        object?[] reversed = new object?[sparkList.Count];
        for (int index = 0; index < sparkList.Count; index++)
        {
            reversed[index] = sparkList[sparkList.Count - 1 - index];
        }

        return new SparkList(reversed, sparkList.Rank);
    }

    public static IReadOnlyList<object?> ListFlatten(IReadOnlyList<object?> list)
    {
        List<object?> flattened = [];
        foreach (object? item in list)
        {
            if (item is SparkList inner)
            {
                foreach (object? element in inner)
                {
                    flattened.Add(element);
                }
            }
            else
            {
                flattened.Add(item);
            }
        }

        return flattened;
    }

    public static int CountNoAttr(IReadOnlyList<object?> list) => list.Count;
}

/// <summary>
/// The node definitions the case table names, with the declared ranks, attributes and
/// <c>DefaultLacing</c> values the table's "Nodes used" block specifies. The default is as
/// load-bearing as the signature: a case whose mode is <c>Auto</c> is asserting what the default
/// resolved to.
/// </summary>
public static class LacingNodes
{
    private const string Package = "Spark.Engine.Tests";

    public static NodeDefinition Add { get; } = Binary("Add", nameof(LacingMembers.Add), LacingMode.Longest);

    /// <summary>Case 51: port a takes dimension 2 and port b dimension 1, so b nests outermost.</summary>
    public static NodeDefinition AddGuided { get; } = new(
        new NodeKey(Package, "Add.Guided"),
        "Add",
        [
            new PortDefinition("a", typeof(double), 0, replicationGuide: 2),
            new PortDefinition("b", typeof(double), 0, replicationGuide: 1),
        ],
        [new PortDefinition("result", typeof(double), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.Add))));

    /// <summary>Case 52: two replicating ports claiming the same dimension.</summary>
    public static NodeDefinition AddDuplicateGuides { get; } = new(
        new NodeKey(Package, "Add.DuplicateGuides"),
        "Add",
        [
            new PortDefinition("a", typeof(double), 0, replicationGuide: 1),
            new PortDefinition("b", typeof(double), 0, replicationGuide: 1),
        ],
        [new PortDefinition("result", typeof(double), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.Add))));

    public static NodeDefinition Sum { get; } = new(
        new NodeKey(Package, "Sum"),
        "Sum",
        [new PortDefinition("xs", typeof(IReadOnlyList<double>), 1)],
        [new PortDefinition("result", typeof(double), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.Sum))));

    public static NodeDefinition Total2d { get; } = new(
        new NodeKey(Package, "Total2d"),
        "Total2d",
        [new PortDefinition("rows", typeof(IReadOnlyList<IReadOnlyList<double>>), 2)],
        [new PortDefinition("result", typeof(double), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.Total2d))));

    public static NodeDefinition Range { get; } = new(
        new NodeKey(Package, "Range"),
        "Range",
        [new PortDefinition("n", typeof(double), 0)],
        [new PortDefinition("values", typeof(IReadOnlyList<double>), 1)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.Range))));

    public static NodeDefinition PointByCoordinates { get; } = new(
        new NodeKey(Package, "Point.ByCoordinates"),
        "Point.ByCoordinates",
        [
            new PortDefinition("x", typeof(double), 0),
            new PortDefinition("y", typeof(double), 0),
            new PortDefinition("z", typeof(double), 0),
        ],
        [new PortDefinition("point", typeof(Point3d), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.PointByCoordinates))));

    public static NodeDefinition CircleByCenterRadius { get; } = new(
        new NodeKey(Package, "Circle.ByCenterRadius"),
        "Circle.ByCenterRadius",
        [
            new PortDefinition("center", typeof(Point3d), 0),
            new PortDefinition("radius", typeof(double), 0),
        ],
        [new PortDefinition("circle", typeof(TestCircle), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.CircleByCenterRadius))));

    /// <summary>The node that makes <c>Auto</c> observable: its author declared Cross Product.</summary>
    public static NodeDefinition GridByXY { get; } = new(
        new NodeKey(Package, "Grid.ByXY"),
        "Grid.ByXY",
        [
            new PortDefinition("x", typeof(double), 0),
            new PortDefinition("y", typeof(double), 0),
        ],
        [new PortDefinition("point", typeof(Point3d), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.GridByXY))),
        LacingMode.CrossProduct);

    public static NodeDefinition Bounds { get; } = new(
        new NodeKey(Package, "Bounds"),
        "Bounds",
        [new PortDefinition("xs", typeof(IReadOnlyList<double>), 1)],
        [
            new PortDefinition("min", typeof(double), 0),
            new PortDefinition("max", typeof(double), 0),
        ],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.Bounds))));

    public static NodeDefinition Split { get; } = new(
        new NodeKey(Package, "Split"),
        "Split",
        [
            new PortDefinition("a", typeof(double), 0),
            new PortDefinition("b", typeof(double), 0),
        ],
        [
            new PortDefinition("sum", typeof(double), 0),
            new PortDefinition("diff", typeof(double), 0),
        ],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.Split))));

    public static NodeDefinition Invert { get; } = new(
        new NodeKey(Package, "Invert"),
        "Invert",
        [new PortDefinition("x", typeof(double), 0)],
        [new PortDefinition("result", typeof(double), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.Invert))));

    public static NodeDefinition Echo { get; } = new(
        new NodeKey(Package, "Echo"),
        "Echo",
        [new PortDefinition("x", typeof(object), 0)],
        [new PortDefinition("result", typeof(object), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.Echo))));

    public static NodeDefinition Scale { get; } = new(
        new NodeKey(Package, "Scale"),
        "Scale",
        [
            new PortDefinition("x", typeof(double), 0),
            new PortDefinition("factor", typeof(double), 0, noReplication: true),
        ],
        [new PortDefinition("result", typeof(double), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.Scale))));

    public static NodeDefinition ListCount { get; } = new(
        new NodeKey(Package, "List.Count"),
        "List.Count",
        [new PortDefinition("list", typeof(object), 0, keepStructure: true)],
        [new PortDefinition("count", typeof(int), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.ListCount))));

    public static NodeDefinition ListReverse { get; } = new(
        new NodeKey(Package, "List.Reverse"),
        "List.Reverse",
        [new PortDefinition("list", typeof(object), 0, keepStructure: true)],
        [new PortDefinition("result", typeof(object), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.ListReverse))));

    /// <summary>A node that consumes list structure, so its author declared <c>Disabled</c>.</summary>
    public static NodeDefinition ListFlatten { get; } = new(
        new NodeKey(Package, "List.Flatten"),
        "List.Flatten",
        [new PortDefinition("list", typeof(IReadOnlyList<object>), 1)],
        [new PortDefinition("result", typeof(IReadOnlyList<object>), 1)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.ListFlatten))),
        LacingMode.Disabled);

    /// <summary>The author forgot both <c>[KeepStructure]</c> and a sensible default. Cases 78 and 79.</summary>
    public static NodeDefinition CountNoAttr { get; } = new(
        new NodeKey(Package, "CountNoAttr"),
        "CountNoAttr",
        [new PortDefinition("list", typeof(IReadOnlyList<object>), 1)],
        [new PortDefinition("count", typeof(int), 0)],
        NodeInvoker.ForMethod(Method(nameof(LacingMembers.CountNoAttr))));

    private static NodeDefinition Binary(string name, string member, LacingMode defaultLacing) => new(
        new NodeKey(Package, name),
        name,
        [
            new PortDefinition("a", typeof(double), 0),
            new PortDefinition("b", typeof(double), 0),
        ],
        [new PortDefinition("result", typeof(double), 0)],
        NodeInvoker.ForMethod(Method(member)),
        defaultLacing);

    private static MethodInfo Method(string name) =>
        typeof(LacingMembers).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException($"No member '{name}' on {nameof(LacingMembers)}.");
}
