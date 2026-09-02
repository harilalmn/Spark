using System.Globalization;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// `E6-T26` — a code block with no <c>return</c> of its own gives one output port per variable it
/// declares, which is how Dynamo's Code Block reads eleven lines as eleven ports.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule is gated on the absence of a return, and that gate is the point of half of these
/// tests.</b> `E6-T8` rejected inferring ports from locals precisely because a debug line would
/// silently change the port set; a script that writes its own <c>return</c> still says exactly what
/// its ports are, and only a script that says nothing gets the per-variable reading.
/// </para>
/// <para>
/// These live beside <see cref="ScriptNodeFactoryTests"/> for the reason that file records: the UI
/// test assembly is the one that already references <c>Spark.Scripting</c>.
/// </para>
/// </remarks>
public sealed class ScriptDeclaredOutputTests
{
    /// <summary>
    /// <b>The thing the client asked for.</b> Three lines, three ports, each carrying its own line's
    /// value.
    /// </summary>
    [Fact]
    public void EveryDeclaredVariableBecomesAnOutputPort()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "var doubled = a * 2;\nvar tripled = a * 3;\nvar total = doubled + tripled;\n");

        Assert.Equal(["doubled", "tripled", "total"], block.Outputs.Select(port => port.Name));

        object?[] values = block.Invoke([2.0], CancellationToken.None);

        Assert.Equal(4.0, values[0]);
        Assert.Equal(6.0, values[1]);
        Assert.Equal(10.0, values[2]);
    }

    /// <summary>
    /// <b>Eleven ports, which is the case in the screenshot and the case that was broken.</b> A
    /// <c>ValueTuple</c> holds seven fields and nests the rest, so reading <c>Item8</c> off the outer
    /// tuple found no such field and filled ports 8 to 11 with null — silently, because nothing
    /// before this row could produce eight ports at all.
    /// </summary>
    [Fact]
    public void ElevenDeclarationsGiveElevenPortsThatEachCarryTheirValue()
    {
        string script = string.Join(
            "\n",
            Enumerable.Range(1, 11).Select(i => string.Format(
                CultureInfo.InvariantCulture, "var v{0} = {0}.0;", i)));

        NodeDefinitionSource block = new ScriptNodeFactory().Create(script);

        Assert.Equal(
            Enumerable.Range(1, 11).Select(i => "v" + i.ToString(CultureInfo.InvariantCulture)),
            block.Outputs.Select(port => port.Name));

        object?[] values = block.Invoke([], CancellationToken.None);

        Assert.Equal(
            Enumerable.Range(1, 11).Select(i => (object?)(double)i),
            values);
    }

    /// <summary>
    /// <b>A script that writes its own <c>return</c> is untouched</b>, which is `E6-T8`'s rejection
    /// kept rather than reversed: the locals are still there, and they are still not ports.
    /// </summary>
    [Fact]
    public void AnExplicitReturnStillDecidesThePorts()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "var doubled = a * 2;\nvar tripled = a * 3;\nreturn doubled + tripled;\n");

        Assert.Equal("result", Assert.Single(block.Outputs).Name);
        Assert.Equal(10.0, Assert.Single(block.Invoke([2.0], CancellationToken.None)));
    }

    /// <summary>
    /// And a tuple return still names them, so a block with eleven locals can put three on the
    /// canvas.
    /// </summary>
    [Fact]
    public void AnExplicitTupleReturnStillNamesThePorts()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "var doubled = a * 2;\nvar tripled = a * 3;\nreturn (small: doubled, big: tripled);\n");

        Assert.Equal(["small", "big"], block.Outputs.Select(port => port.Name));
    }

    /// <summary>
    /// <b>Each port carries the type its line produced</b>, which `E6-T25` needs in order to let a
    /// wire leave the block at all — and which comes free, because the generated return is a tuple
    /// the semantic model can read.
    /// </summary>
    [Fact]
    public void ADeclaredPortCarriesTheTypeItsLineProduced()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "var count = 3;\nvar name = \"circle\";\nvar centre = new Point3d(1, 2, 3);\n");

        Assert.Equal(typeof(int), block.Outputs[0].ValueType);
        Assert.Equal(typeof(string), block.Outputs[1].ValueType);
        Assert.Equal(typeof(Spark.Geometry.Point3d), block.Outputs[2].ValueType);
    }

    /// <summary>
    /// <b>An input port and an output port cannot collide.</b> An input is a name the compiler could
    /// not resolve; a declaration is one it can. The two sets are disjoint by construction, which is
    /// why the rule is declarations rather than assignments — <c>x = 5;</c> on an undeclared name is
    /// the <c>CS0103</c> that makes <c>x</c> an input.
    /// </summary>
    [Fact]
    public void AnInputIsNotAlsoAnOutput()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create("var doubled = a * 2;\n");

        Assert.Equal("a", Assert.Single(block.Inputs).Name);
        Assert.Equal("doubled", Assert.Single(block.Outputs).Name);
    }

    /// <summary>
    /// <b>A local inside a loop is not a port.</b> It is out of scope where the generated return
    /// runs, and a rule that reached into blocks would make wrapping two lines in an <c>if</c>
    /// delete two ports.
    /// </summary>
    [Fact]
    public void ALocalInsideABlockIsNotAnOutputPort()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "var total = 0.0;\nfor (int i = 0; i < 3; i++) { var step = i * 2.0; total += step; }\n");

        Assert.Equal("total", Assert.Single(block.Outputs).Name);
        Assert.Equal(6.0, Assert.Single(block.Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// A <c>return</c> inside a lambda returns from the lambda, so it does not stop the block being
    /// read per variable — and the lambda itself is a port, which is what Dynamo shows as
    /// <c>function</c>.
    /// </summary>
    [Fact]
    public void AReturnInsideALambdaIsNotTheBlocksReturn()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "var twice = (double x) => { return x * 2; };\nvar answer = twice(21.0);\n");

        Assert.Equal(["twice", "answer"], block.Outputs.Select(port => port.Name));
        Assert.Equal(42.0, block.Invoke([], CancellationToken.None)[1]);
    }

    /// <summary>A constant is a value the script was given, not one it computed.</summary>
    [Fact]
    public void AConstantIsNotAnOutputPort()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "const double Factor = 2.0;\nvar scaled = a * Factor;\n");

        Assert.Equal("scaled", Assert.Single(block.Outputs).Name);
    }

    /// <summary>
    /// <b>The starter script now compiles, and it did not.</b> `E6-T18` made a fresh code block one
    /// comment line and recorded that an empty script is legal — zero inputs, one <c>result</c>
    /// output. The ports were right and the block was not: a generated method returning
    /// <c>object</c> with no <c>return</c> in it is <c>CS0161</c>, so every fresh block failed to
    /// compile and said so the moment anything asked it for a value.
    /// </summary>
    [Fact]
    public void AScriptThatDeclaresNothingCompilesAndReturnsNull()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "// Any name you have not declared becomes an input port.\n");

        Assert.Equal("result", Assert.Single(block.Outputs).Name);
        Assert.Null(Assert.Single(block.Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// A declaration with no initialiser is skipped rather than becoming a port that reports
    /// <c>CS0165</c> from a line the user cannot see.
    /// </summary>
    [Fact]
    public void AnUninitialisedDeclarationIsNotAnOutputPort()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "double later;\nvar ready = 1.0;\nlater = ready * 2;\n");

        Assert.Equal("ready", Assert.Single(block.Outputs).Name);
    }

    /// <summary>
    /// The diagnostics path shares the generated frame, so a block being typed into does not report
    /// the <c>CS0161</c> that the frame itself would otherwise cause.
    /// </summary>
    [Fact]
    public void ABlockWithNoReturnHasNoDiagnostics()
    {
        Assert.Empty(new ScriptNodeFactory().Diagnose("var doubled = a * 2;\n"));
    }
}
