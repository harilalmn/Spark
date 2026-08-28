using System;
using Spark.Engine;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.Scripting.Tests;

/// <summary>
/// Output ports come from a named tuple return, or from one port called <c>result</c>. Nothing is
/// inferred from which locals stop being read, because that would make adding a debug line change
/// the shape of the node.
/// </summary>
public sealed class OutputPortInferenceTests
{
    [Fact]
    public void ANamedTupleReturnProducesOneNamedPortPerElement()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            double a = width * height;
            double p = 2 * (width + height);
            return (area: a, perimeter: p);
            """,
            CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("width", "height")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal(["area", "perimeter"], CodeBlockTestHarness.NamesOf(compilation.Outputs));
    }

    [Fact]
    public void ANamedTupleAsTheTrailingExpressionAlsoProducesNamedPorts()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "(sum: a + b, difference: a - b)",
            CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("a", "b")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal(["sum", "difference"], CodeBlockTestHarness.NamesOf(compilation.Outputs));
    }

    [Fact]
    public void APlainTrailingExpressionProducesOneResultPort()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile("1 + 1", CodeBlockTestHarness.Options());

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal("result", Assert.Single(compilation.Outputs).Name);
    }

    [Fact]
    public void AnExplicitReturnProducesOneResultPort()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "return 2 * radius;", CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("radius")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal("result", Assert.Single(compilation.Outputs).Name);
    }

    /// <summary>A node must have an output port even when the block produces nothing.</summary>
    [Fact]
    public void ABlockThatReturnsNothingStillHasOneOutputPort()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "double unused = 1;", CodeBlockTestHarness.Options());

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        PortDefinition port = Assert.Single(compilation.Outputs);
        Assert.Equal("result", port.Name);
        Assert.Equal(typeof(object), port.ValueType);
    }

    /// <summary>
    /// Output ports carry the type the block actually produces, not <see cref="object"/>. Without
    /// this a code block could not be wired into a typed input at all.
    /// </summary>
    [Fact]
    public void AnOutputPortCarriesTheTypeTheBlockProduces()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "new Point3d(1, 2, 3)", CodeBlockTestHarness.Options());

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal(typeof(Point3d), Assert.Single(compilation.Outputs).ValueType);
    }

    [Fact]
    public void EachTupleElementCarriesItsOwnType()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "(point: new Point3d(0, 0, 0), label: \"origin\")", CodeBlockTestHarness.Options());

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal(typeof(Point3d), compilation.Outputs[0].ValueType);
        Assert.Equal(typeof(string), compilation.Outputs[1].ValueType);
    }

    /// <summary>
    /// The compiler settles what parsing alone cannot: a trailing call that returns a value is the
    /// block's result, semicolon and all.
    /// </summary>
    [Fact]
    public void ATrailingCallThatReturnsAValueIsTheResult()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "Math.Sqrt(x);", CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("x")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal(typeof(double), Assert.Single(compilation.Outputs).ValueType);
        Assert.Equal(4.0, compilation.Definition!.Invoke([16.0])[0]);
    }

    /// <summary>And a trailing call that returns nothing is not, rather than being a compile error.</summary>
    [Fact]
    public void ATrailingVoidCallIsNotTheResult()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            var list = new System.Collections.Generic.List<double>();
            list.Add(1);
            """,
            CodeBlockTestHarness.Options());

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal("result", Assert.Single(compilation.Outputs).Name);
        Assert.Null(compilation.Definition!.Invoke([])[0]);
    }

    /// <summary>
    /// Two returns naming their tuple elements differently is a warning, not a silent choice: the
    /// ports come from the first, and the user is told which branch disagrees.
    /// </summary>
    [Fact]
    public void DisagreeingTupleNamesWarnAndTheFirstOneWins()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            if (flag) { return (area: 1.0, perimeter: 2.0); }
            return (width: 3.0, height: 4.0);
            """,
            CodeBlockTestHarness.Options(CodeBlockTestHarness.Wired(typeof(bool), "flag")));

        Assert.Contains(
            compilation.Diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.InconsistentTupleNames);

        Assert.Equal(["area", "perimeter"], CodeBlockTestHarness.NamesOf(compilation.Outputs));
    }

    /// <summary>
    /// A <c>return</c> inside a local function belongs to that function, not to the block. Getting
    /// this wrong would give the node ports named after somebody's helper.
    /// </summary>
    [Fact]
    public void AReturnInsideALocalFunctionDoesNotBecomeTheBlocksResult()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            (double low, double high) Split(double v) { return (low: v - 1, high: v + 1); }
            Split(value).low
            """,
            CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("value")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal(["result"], CodeBlockTestHarness.NamesOf(compilation.Outputs));
    }
}
