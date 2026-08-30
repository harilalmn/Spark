using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;
using Spark.Nodes.Core;
using Spark.Scripting;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// Typed input injection — `E6-T6`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The behaviour worth testing is not "it compiles".</b> It is that a script can do something
/// with a wired input that is impossible with <c>dynamic</c> alone: call a member the compiler had
/// to know about, and be told which *port* is wrong when the wrong thing arrives. Both are
/// asserted below, because both are what the row is for.
/// </para>
/// <para>
/// The second half is the canvas: a code block re-types itself when a wire lands on it, and does
/// not churn when one lands somewhere else.
/// </para>
/// </remarks>
public sealed class TypedScriptInputTests
{
    private static ScriptNodeFactory Factory()
    {
        _ = typeof(Point3d).Assembly.Location;

        return new ScriptNodeFactory();
    }

    /// <summary>
    /// <b>The port carries the wire's type rather than <c>object</c>.</b> That is what puts a real
    /// type on the port label, gives the port a rank, and is the same fact the declaration is
    /// generated from.
    /// </summary>
    [Fact]
    public void AKnownInputTypeReachesThePort()
    {
        NodeDefinitionSource block = Factory().Create(
            "return centre;",
            new Dictionary<string, Type> { ["centre"] = typeof(Point3d) });

        Assert.Equal(typeof(Point3d), Assert.Single(block.Inputs).ValueType);
    }

    /// <summary>An input nobody has wired is still <c>dynamic</c>, and its port is still open.</summary>
    [Fact]
    public void AnUnknownInputStaysDynamic()
    {
        NodeDefinitionSource block = Factory().Create("return centre;");

        Assert.Equal(typeof(object), Assert.Single(block.Inputs).ValueType);
        Assert.Equal(3.0, Assert.Single(block.Invoke([3.0], CancellationToken.None)));
    }

    /// <summary>
    /// <b>The typed declaration is real, not decorative.</b> <c>centre.X</c> resolves at compile
    /// time against <see cref="Point3d"/>; with the input declared <c>object</c> the same script
    /// would not compile at all.
    /// </summary>
    [Fact]
    public void ATypedInputResolvesItsMembersAtCompileTime()
    {
        NodeDefinitionSource block = Factory().Create(
            "return centre.X + centre.Y;",
            new Dictionary<string, Type> { ["centre"] = typeof(Point3d) });

        Assert.Equal(7.0, Assert.Single(block.Invoke([new Point3d(3.0, 4.0, 0.0)], CancellationToken.None)));
    }

    /// <summary>
    /// A number that arrives as an <see cref="int"/> where the script wants a <see cref="double"/>
    /// is converted rather than refused. This is the commonest thing a graph delivers, and a typed
    /// input that rejected it would be worse than <c>dynamic</c> rather than better.
    /// </summary>
    [Fact]
    public void AWidenedNumberIsAccepted()
    {
        NodeDefinitionSource block = Factory().Create(
            "return radius * 2.0;",
            new Dictionary<string, Type> { ["radius"] = typeof(double) });

        Assert.Equal(8.0, Assert.Single(block.Invoke([4], CancellationToken.None)));
    }

    /// <summary>
    /// <b>When the wrong thing does arrive, the message names the port.</b> A plain cast would say
    /// <c>Unable to cast object of type 'System.String' to type 'Spark.Geometry.Point3d'</c>, which
    /// names two CLR types, no port and no node — nothing a user could act on.
    /// </summary>
    [Fact]
    public void AMismatchedValueNamesThePort()
    {
        NodeDefinitionSource block = Factory().Create(
            "return centre.X;",
            new Dictionary<string, Type> { ["centre"] = typeof(Point3d) });

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => block.Invoke(["not a point"], CancellationToken.None));

        Assert.Contains("centre", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Point3d", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing arriving on a struct-typed port is a sentence rather than a
    /// <see cref="NullReferenceException"/>.
    /// </summary>
    [Fact]
    public void NothingOnAStructPortIsExplained()
    {
        NodeDefinitionSource block = Factory().Create(
            "return centre.X;",
            new Dictionary<string, Type> { ["centre"] = typeof(Point3d) });

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => block.Invoke([null], CancellationToken.None));

        Assert.Contains("centre", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The same source with different input types is a different node.</b> The evaluation cache
    /// keys on the definition's key, so two blocks that hashed the same would serve each other's
    /// results — and <c>a + a</c> over two doubles is not <c>a + a</c> over two strings.
    /// </summary>
    [Fact]
    public void InputTypesChangeTheContentHash()
    {
        ScriptNodeFactory factory = Factory();

        NodeDefinitionSource untyped = factory.Create("return a;");
        NodeDefinitionSource asDouble = factory.Create(
            "return a;", new Dictionary<string, Type> { ["a"] = typeof(double) });
        NodeDefinitionSource asString = factory.Create(
            "return a;", new Dictionary<string, Type> { ["a"] = typeof(string) });

        Assert.NotEqual(untyped.ContentHash, asDouble.ContentHash);
        Assert.NotEqual(asDouble.ContentHash, asString.ContentHash);
    }

    /// <summary>
    /// A type that source cannot name — an internal one — falls back to <c>dynamic</c> rather than
    /// producing a generated line that does not compile.
    /// </summary>
    [Fact]
    public void ATypeSourceCannotNameFallsBackToDynamic()
    {
        NodeDefinitionSource block = Factory().Create(
            "return a;",
            new Dictionary<string, Type> { ["a"] = typeof(NotNameableFromGeneratedCode) });

        Assert.Equal(typeof(object), Assert.Single(block.Inputs).ValueType);
    }

    /// <summary>The type speller writes what C# would write, including the awkward shapes.</summary>
    [Theory]
    [InlineData(typeof(double), "double")]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(Point3d), "global::Spark.Geometry.Point3d")]
    [InlineData(typeof(Point3d[]), "global::Spark.Geometry.Point3d[]")]
    [InlineData(typeof(List<Point3d>), "global::System.Collections.Generic.List<global::Spark.Geometry.Point3d>")]
    [InlineData(typeof(Dictionary<string, int>), "global::System.Collections.Generic.Dictionary<string, int>")]
    [InlineData(typeof(int?), "int?")]
    public void TheTypeSpellerWritesCSharp(Type type, string expected) =>
        Assert.Equal(expected, ScriptTypeName.Of(type));

    /// <summary>A type that cannot be named is refused rather than mis-spelt.</summary>
    [Fact]
    public void TheTypeSpellerRefusesWhatItCannotName()
    {
        Assert.Null(ScriptTypeName.Of(typeof(NotNameableFromGeneratedCode)));
        Assert.Null(ScriptTypeName.Of(typeof(List<>)));
    }

    /// <summary>
    /// <b>The canvas re-types a code block when a wire lands on it.</b> This is where `E6-T6`
    /// becomes visible: before the wire the port is <c>object</c>, after it the port is whatever
    /// the upstream node produces, and the script can use its members.
    /// </summary>
    [Fact]
    public void WiringACodeBlockRetypesIt()
    {
        ScriptNodeFactory scripts = Factory();
        CanvasGraph graph = new() { Scripts = scripts };

        NodeId block = AddBlock(graph, scripts, "return centre.X;");
        NodeId point = AddPoint(graph);

        Assert.Equal(typeof(object), PortType(graph, block));
        Assert.True(Connect(graph, point, 0, block, 0));

        Assert.Equal(typeof(Point3d), PortType(graph, block));
    }

    /// <summary>
    /// Disconnecting takes the type away again, because an unwired port has no type and pretending
    /// otherwise would leave the block failing on a value nothing is sending it.
    /// </summary>
    [Fact]
    public void UnwiringACodeBlockPutsItBackToDynamic()
    {
        ScriptNodeFactory scripts = Factory();
        CanvasGraph graph = new() { Scripts = scripts };

        NodeId block = AddBlock(graph, scripts, "return centre.X;");
        NodeId point = AddPoint(graph);

        Assert.True(Connect(graph, point, 0, block, 0));
        Assert.True(graph.Disconnect(Assert.Single(graph.Wires)));

        Assert.Equal(typeof(object), PortType(graph, block));
    }

    /// <summary>
    /// <b>A rebuild keeps the wires on both sides of the block.</b> Re-typing runs on every connect
    /// now, and a rebuild that dropped what was downstream would quietly disconnect the graph every
    /// time a wire was drawn into a code block. Before this, editing a script did exactly that.
    /// </summary>
    [Fact]
    public void RetypingKeepsTheWiresLeavingTheBlock()
    {
        ScriptNodeFactory scripts = Factory();
        CanvasGraph graph = new() { Scripts = scripts };

        NodeId block = AddBlock(graph, scripts, "return centre.X;");
        NodeId point = AddPoint(graph);

        // A second code block downstream, because its input port is `dynamic` and will therefore
        // accept the first block's `object` output - which is what this test is about, not typing.
        NodeId downstream = AddBlock(graph, scripts, "return upstream;");

        Assert.True(Connect(graph, block, 0, downstream, 0));
        Assert.True(Connect(graph, point, 0, block, 0));

        // Both survive: the wire out of the block was made before the rebuild, the wire into it
        // caused the rebuild.
        Assert.Equal(2, graph.Wires.Count);
    }

    /// <summary>
    /// A wire that lands somewhere else does not disturb a code block. The rebuild moves a node's
    /// slot, so doing it when nothing changed would renumber the canvas for no reason.
    /// </summary>
    [Fact]
    public void AWireElsewhereDoesNotRebuildTheBlock()
    {
        ScriptNodeFactory scripts = Factory();
        CanvasGraph graph = new() { Scripts = scripts };

        NodeId block = AddBlock(graph, scripts, "return centre.X;");
        int number = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 200);
        int sine = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 0, 400);

        int before = graph.SlotOf(block);
        Assert.True(Connect(graph, graph.Nodes[number].Id, 0, graph.Nodes[sine].Id, 0));

        Assert.Equal(before, graph.SlotOf(block));
    }

    private static NodeId AddBlock(CanvasGraph graph, ScriptNodeFactory scripts, string script)
    {
        int slot = graph.Add(NodeDefinition.FromScript(scripts.Create(script), script), 0, 0);

        return graph.Nodes[slot].Id;
    }

    private static NodeId AddPoint(CanvasGraph graph)
    {
        int slot = graph.Add(TestGraphs.Library.ByName("Point.ByCoordinates"), 0, 200);

        return graph.Nodes[slot].Id;
    }

    /// <summary>Connects by identity, because re-typing moves slots underneath a test.</summary>
    private static bool Connect(CanvasGraph graph, NodeId source, int sourcePort, NodeId target, int targetPort) =>
        graph.TryConnect(
            new CanvasPort(graph.SlotOf(source), sourcePort, IsOutput: true),
            new CanvasPort(graph.SlotOf(target), targetPort, IsOutput: false));

    /// <summary>The type the engine says a node's first input port wants.</summary>
    private static Type PortType(CanvasGraph graph, NodeId id) =>
        graph.Engine.Node(id).Definition.Inputs[0].ValueType;

    /// <summary>A type no generated file could name, because it is internal to this assembly.</summary>
    internal sealed class NotNameableFromGeneratedCode;
}
