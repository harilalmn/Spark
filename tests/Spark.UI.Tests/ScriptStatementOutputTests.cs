using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// Dynamo's Code Block rule: one output port per line that makes something, named after the
/// variable when there is one and after the expression's kind when there is not — `E6-T28`,
/// `E6-T29`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Specified by the client with a Dynamo screenshot</b> of six lines and six ports, and the
/// instruction <i>let us follow the Dynamo code block exactly</i>.
/// <see cref="TheClientsDynamoBlockLineForLine"/> is that screenshot, and it is the test to read
/// first.
/// </para>
/// <para>
/// <b>This reversed `E6-T27`'s rule that a trailing value replaces the declared ports</b> — the
/// client's own earlier decision, superseded by their own later instruction. What survives from
/// `E6-T27` is the part that was never about counting: only an expression C# would <i>refuse</i>
/// as a statement is claimed, so a call still discards its value on purpose and no script that
/// compiled ever changed meaning. <c>ACallIsStillAStatement</c> holds that line.
/// </para>
/// </remarks>
public sealed class ScriptStatementOutputTests
{
    private static ScriptNodeFactory Factory()
    {
        _ = typeof(Point3d).Assembly.Location;

        return new ScriptNodeFactory();
    }

    private static NodeDefinitionSource Compile(string script) => Factory().Create(script);

    private static string[] Ports(string script) =>
        [.. Compile(script).Outputs.Select(port => port.Name)];

    private static object?[] Run(string script) =>
        [.. Compile(script).Invoke([], CancellationToken.None)];

    /// <summary>
    /// <b>The client's Dynamo screenshot, line for line, in Spark's syntax.</b> Six lines, six
    /// ports, and the names Dynamo gives them.
    /// </summary>
    /// <remarks>
    /// Dynamo's <c>n = 100;</c> declares; C#'s does not — an assignment to a name nothing declared
    /// is the <c>CS0103</c> that makes it an <i>input</i> port. So the Spark spelling of that line
    /// is <c>var n = 100;</c>, and it is the one place the translation is not literal.
    /// </remarks>
    [Fact]
    public void TheClientsDynamoBlockLineForLine()
    {
        const string Script = """
            5;
            5.0 + 6;
            "hello";
            var n = 100;
            var t = 0..1..#10;
            0..#6..10;
            """;

        Assert.Equal(["integer", "function", "string", "n", "t", "list"], Ports(Script));
    }

    /// <summary>And the values really arrive on those ports, in that order.</summary>
    [Fact]
    public void TheValuesArriveOnThosePorts()
    {
        const string Script = """
            5;
            5.0 + 6;
            "hello";
            var n = 100;
            """;

        Assert.Equal([5, 11.0, "hello", 100], Run(Script));
    }

    /// <summary>The client's first block, which started all of this.</summary>
    [Fact]
    public void EachValueStatementIsAPort()
    {
        const string Script = "5+3;\n\"Test\";";

        Assert.Equal(["function", "string"], Ports(Script));
        Assert.Equal([8, "Test"], Run(Script));
    }

    /// <summary>
    /// <b>It was a compile error before `E6-T28`</b>, which is what makes claiming these lines
    /// safe: nothing that worked can have changed meaning.
    /// </summary>
    [Fact]
    public void ItUsedToBeACompileError()
    {
        Assert.DoesNotContain(Factory().Diagnose("5+3;\n\"Test\";"), diagnostic => diagnostic.IsError);
    }

    /// <summary>
    /// <b>`E6-T29` reversed `E6-T27`: declarations and values join.</b> The block that was one port
    /// carrying 4 is now three ports, which is Dynamo's answer and the client's later instruction.
    /// </summary>
    /// <remarks>
    /// This test exists to be <i>read</i> as much as run. If it is ever changed back, the reason
    /// has to be a third instruction from the client, not a tidy-up.
    /// </remarks>
    [Fact]
    public void DeclarationsAndValuesJoinRatherThanCompete()
    {
        const string Script = "var n = 10 / 5;\nvar p = 8 / 4;\nn + p;";

        Assert.Equal(["n", "p", "function"], Ports(Script));
        Assert.Equal([2, 2, 4], Run(Script));
    }

    /// <summary>The ports keep source order however the two kinds of line interleave.</summary>
    [Fact]
    public void ThePortsAreInSourceOrder()
    {
        Assert.Equal(
            ["integer", "a", "string", "b"],
            Ports("1;\nvar a = 2;\n\"three\";\nvar b = 4;"));

        Assert.Equal([1, 2, "three", 4], Run("1;\nvar a = 2;\n\"three\";\nvar b = 4;"));
    }

    /// <summary>
    /// <b>Repeats are separated by a number, and the first keeps the bare name.</b> Wires are
    /// re-made by port name, so two ports called <c>integer</c> would be indistinguishable — and
    /// numbering from <c>integer1</c> would rename the port that already had a wire on it the
    /// moment a second integer line was typed.
    /// </summary>
    [Fact]
    public void RepeatedKindsAreNumberedAndTheFirstKeepsItsName()
    {
        Assert.Equal(["integer", "integer2", "integer3"], Ports("1;\n2;\n3;"));
        Assert.Equal([1, 2, 3], Run("1;\n2;\n3;"));
    }

    /// <summary>Each port carries its own value's type, not one type for the block.</summary>
    [Fact]
    public void EachPortCarriesItsOwnType()
    {
        IReadOnlyList<ScriptPort> ports = Compile("5+3;\n\"Test\";\n1.5 * 2;").Outputs;

        Assert.Equal(typeof(int), ports[0].ValueType);
        Assert.Equal(typeof(string), ports[1].ValueType);
        Assert.Equal(typeof(double), ports[2].ValueType);
    }

    /// <summary>
    /// A statement C# accepts is left alone wherever it sits, so a block that computes with calls
    /// between its values still means what it says.
    /// </summary>
    [Fact]
    public void CallsBetweenValuesAreStillStatements()
    {
        const string Script =
            "var list = new List<int>();\nlist.Add(3);\nlist.Count;\nlist.Add(4);\nlist.Count;";

        Assert.Equal(["list", "function", "function2"], Ports(Script));
        Assert.Equal([new[] { 3, 4 }, 1, 2], [.. Run(Script).Select(v => v is List<int> l ? l.ToArray() : v)]);
    }

    /// <summary>
    /// <b>An input port is still found through the captures.</b> The values are wrapped in
    /// declarations before the compile, and a free identifier inside one has to survive that.
    /// </summary>
    [Fact]
    public void FreeIdentifiersInValuesAreStillInputs()
    {
        NodeDefinitionSource definition = Compile("radius * 2;\nradius * 3;");

        Assert.Equal(["radius"], definition.Inputs.Select(port => port.Name));
        Assert.Equal([8.0, 12.0], definition.Invoke([4.0], CancellationToken.None).ToArray());
    }

    /// <summary>
    /// <b>An explicit <c>return</c> still decides the ports</b>, which is Spark's own escape hatch
    /// and has no Dynamo equivalent — so <i>follow Dynamo exactly</i> says nothing about it, and it
    /// stays.
    /// </summary>
    [Fact]
    public void AnExplicitReturnStillWins()
    {
        Assert.Equal(["result"], Ports("var a = 1; var b = 2; return b;"));
        Assert.Equal(["area", "count"], Ports("var a = 1; return (area: a, count: 2);"));
    }

    /// <summary>
    /// <b>The range markers move by what was inserted <i>ahead of them</i>, not by a constant.</b>
    /// Each value gets its own <c>var __resultN = </c>, so a marker on the third value has three
    /// insertions in front of it — and a marker looked for in the wrong place is lowered as the
    /// <i>other</i> range form, which is a wrong answer and not an error.
    /// </summary>
    [Fact]
    public void RangesInSeveralValuesAreEachLoweredCorrectly()
    {
        object?[] outputs = Run("0..1..#5;\n0..#5..1;\n3..5..#3;");

        Assert.Equal([0, 0.25, 0.5, 0.75, 1], (IReadOnlyList<double>)outputs[0]!);
        Assert.Equal([0, 1, 2, 3, 4], (IReadOnlyList<double>)outputs[1]!);
        Assert.Equal([3, 4, 5], (IReadOnlyList<double>)outputs[2]!);
    }

    /// <summary>
    /// A diagnostic on a line after a claimed value still lands on the user's line: the captures
    /// add characters, never a newline.
    /// </summary>
    [Fact]
    public void TheCapturesAddNoLines()
    {
        const string Script = "1 + 1;\n2 + 2;\nvar b = ;\n3 + 3;";

        ScriptDiagnostic error = Factory().Diagnose(Script).First(diagnostic => diagnostic.IsError);

        Assert.Equal(3, error.Line);
    }

    /// <summary>
    /// <b>More than seven ports.</b> A <c>ValueTuple</c> nests everything past the seventh in
    /// <c>Rest</c>, which is the defect `E6-T26` found the first time eight ports were made — and
    /// this is the second way to make eight.
    /// </summary>
    [Fact]
    public void MoreThanSevenValuesAllArrive()
    {
        string script = string.Join("\n", Enumerable.Range(1, 9).Select(i => i + " + 0;"));

        Assert.Equal(9, Compile(script).Outputs.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], Run(script));
    }
}
