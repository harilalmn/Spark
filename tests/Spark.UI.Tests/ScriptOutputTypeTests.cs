using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// A code block's output port carries the type its script returns — `E6-T25`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this closes was found by drawing a wire.</b> Every output port was
/// <see cref="object"/>, whatever the script returned, so a block producing a <c>Circle</c> could
/// not be connected to a port declared <c>Curve</c>: <c>object</c> into <c>Curve</c> is a
/// narrowing, and <see cref="TypeCompatibility"/> refuses those when the wire is drawn rather than
/// when the graph runs. The value was right there in the watch, and the wire was still refused.
/// </para>
/// <para>
/// <b>Half of these tests are about the cache</b>, because that is where this can go wrong
/// invisibly: inferring the type needs the compilation the disk cache exists to skip, so a port
/// typed <c>Circle</c> in the session that compiled it and <c>object</c> in the session that
/// reopened the file would be a wire that works until you close Spark.
/// </para>
/// </remarks>
public sealed class ScriptOutputTypeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "spark-output-types-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked assembly is not this test's problem; the directory is under the temporary
            // path and the operating system will take it.
        }
    }

    /// <summary>
    /// <b>The wire the client could not draw.</b> A block returning a circle has an output typed
    /// <c>Circle</c>, and <c>Circle</c> reaches a <c>Curve</c> port directly.
    /// </summary>
    [Fact]
    public void AReturnedCircleTypesThePortAndReachesACurvePort()
    {
        ScriptPort output = Single("return Circle.ByCentreRadius(new Point3d(10, 10, 0), 10);");

        Assert.Equal(typeof(Circle), output.ValueType);

        CompatibilityResult wire = TypeCompatibility.Default.Check(output.ValueType, typeof(Curve));

        Assert.True(wire.IsAccepted);
        Assert.Equal(PortCompatibility.Direct, wire.Kind);
    }

    /// <summary>
    /// The same script before this row: <c>object</c> is refused by a <c>Curve</c> port, which is
    /// the behaviour being replaced and the reason the row exists.
    /// </summary>
    [Fact]
    public void ObjectWouldStillBeRefused()
    {
        Assert.False(TypeCompatibility.Default.Check(typeof(object), typeof(Curve)).IsAccepted);
    }

    /// <summary>A number is a number, which is what makes a code block usable as a calculator.</summary>
    [Fact]
    public void AReturnedNumberTypesThePort()
    {
        Assert.Equal(typeof(double), Single("return 1.0 + 2.0;").ValueType);
    }

    /// <summary>A list output keeps its element type, so replication and the port's rank are right.</summary>
    [Fact]
    public void AReturnedListKeepsItsElementType()
    {
        ScriptPort output = Single(
            "return new System.Collections.Generic.List<Point3d> { new Point3d(0, 0, 0) };");

        Assert.Equal(typeof(List<Point3d>), output.ValueType);
    }

    /// <summary>Each element of a tuple return types its own port.</summary>
    [Fact]
    public void EachTupleElementTypesItsOwnPort()
    {
        IReadOnlyList<ScriptPort> outputs = Outputs("return (area: 3.0, label: \"disc\");");

        Assert.Equal(2, outputs.Count);
        Assert.Equal(("area", typeof(double)), (outputs[0].Name, outputs[0].ValueType));
        Assert.Equal(("label", typeof(string)), (outputs[1].Name, outputs[1].ValueType));
    }

    /// <summary>
    /// <b>An unwired input is <c>dynamic</c>, so anything computed from it is unknowable</b> — and
    /// the port stays <see cref="object"/> rather than being given a type the compiler has not
    /// promised. A port typed wrongly refuses wires that ought to be legal.
    /// </summary>
    [Fact]
    public void AValueComputedFromAnUnwiredInputStaysObject()
    {
        Assert.Equal(typeof(object), Single("return radius * 2;").ValueType);
    }

    /// <summary>
    /// Two returns of different types agree on nothing, so the port is <see cref="object"/> rather
    /// than a guess at a common base.
    /// </summary>
    [Fact]
    public void TwoReturnsThatDisagreeLeaveThePortAsObject()
    {
        Assert.Equal(
            typeof(object),
            Single("if (System.DateTime.Now.Ticks > 0) { return 1.0; } return \"text\";").ValueType);
    }

    /// <summary>
    /// <b>A <c>return</c> inside a lambda belongs to the lambda.</b> Typing the port from it would
    /// give this script a <see cref="double"/> port where it produces a list.
    /// </summary>
    [Fact]
    public void AReturnInsideALambdaDoesNotTypeThePort()
    {
        ScriptPort output = Single(
            "var points = new System.Collections.Generic.List<Point3d> { new Point3d(1, 2, 3) };"
            + " return points.Select(p => { return p.X; }).ToList();");

        Assert.Equal(typeof(List<double>), output.ValueType);
    }

    /// <summary>
    /// <b>The type survives the disk cache, and that is not a detail.</b> Inferring it needs the
    /// compilation the cache exists to skip, so it is written down beside the assembly; two
    /// factories over one directory is exactly the shape of closing Spark and reopening the graph.
    /// </summary>
    [Fact]
    public void TheTypeSurvivesTheDiskCache()
    {
        const string Script = "return Circle.ByCentreRadius(new Point3d(0, 0, 0), 5.0);";

        ScriptPort compiled = Assert.Single(Factory().Create(Script).Outputs);
        ScriptPort restored = Assert.Single(Factory().Create(Script).Outputs);

        Assert.Equal(typeof(Circle), compiled.ValueType);
        Assert.Equal(compiled.ValueType, restored.ValueType);
    }

    /// <summary>
    /// A cache entry that records no types — one written by an older build — leaves the port as
    /// <see cref="object"/> rather than failing to open the graph.
    /// </summary>
    [Fact]
    public void AnEntryWithNoTypesFallsBackToObject()
    {
        const string Script = "return 42.0;";

        Assert.Equal(typeof(double), Assert.Single(Factory().Create(Script).Outputs).ValueType);

        foreach (string file in Directory.EnumerateFiles(_directory, "*.outputs"))
        {
            File.Delete(file);
        }

        Assert.Equal(typeof(object), Assert.Single(Factory().Create(Script).Outputs).ValueType);
    }

    /// <summary>A script that does not compile keeps a port, and it is the untyped one.</summary>
    [Fact]
    public void AScriptThatDoesNotCompileKeepsAnObjectPort()
    {
        Assert.Equal(typeof(object), Single("return new Nonexistent();").ValueType);
    }

    /// <summary>
    /// <b>The wire, drawn.</b> A code block returning a circle connects to
    /// <c>Curve.PointAtParameter</c> and the graph evaluates through it — which is the whole of
    /// what the client could not do, and the assertion that goes red the moment the inference is
    /// taken out.
    /// </summary>
    [Fact]
    public void ACodeBlockCanBeWiredIntoACurvePortAndRun()
    {
        ScriptNodeFactory scripts = Factory();

        const string Script = "return Circle.ByCentreRadius(new Point3d(0, 0, 0), 5.0);";

        Spark.Engine.Graph graph = new();
        NodeId block = graph.AddNode(NodeDefinition.FromScript(scripts.Create(Script), Script)).Id;
        NodeId reader = graph.AddNode(TestGraphs.Library.ByName("Curve.PointAtParameter")).Id;

        ConnectionResult wire = graph.TryConnect(block, 0, reader, 0);

        Assert.True(wire.Accepted, wire.Diagnostic?.Message);

        EvaluationResult run = GraphEvaluator.Evaluate(
            graph,
            new EvaluationContext(Tolerance.Default, new SequentialEvaluationScheduler()),
            TestContext.Current.CancellationToken);

        Assert.False(run.HasErrors);
        Assert.IsType<Point3d>(run.Value(reader));
    }

    private ScriptNodeFactory Factory()
    {
        // Touched so that the catalogue is built with Spark.Geometry actually loaded; a reference
        // that nothing has touched is not in the process yet, and the prelude would then import a
        // namespace the compilation cannot see.
        _ = typeof(Point3d).Assembly.Location;

        return new ScriptNodeFactory(
            new ReferenceCatalog(), new GuardWeaver(), new ScriptAssemblyCache(_directory));
    }

    private ScriptPort Single(string script) => Assert.Single(Outputs(script));

    private IReadOnlyList<ScriptPort> Outputs(string script) => Factory().Create(script).Outputs;
}
