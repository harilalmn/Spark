using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// A code block gives one output port per value statement, not just for the last one — `E6-T28`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client with a screenshot</b> of a block reading <c>5+3;</c> then
/// <c>"Test";</c>, one <c>result</c> port and the <c>CS0201</c> that line 1 still was: <i>"let the
/// code block give output ports corresponding to each statement, in this case 8 and Test"</i>.
/// </para>
/// <para>
/// <b>This is `E6-T27` read once per statement rather than once per block, and it inherits that
/// row's safety unchanged</b> — only an expression C# would refuse as a statement is claimed. The
/// test that holds the line is still <c>ScriptTrailingValueTests.ACallIsStillAStatement</c>, and
/// <b>that whole file must stay green without being edited</b>: it is what says the single-value
/// case did not move underneath this one.
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

    private static object?[] Run(string script) =>
        [.. Compile(script).Invoke([], CancellationToken.None)];

    /// <summary>The client's block, exactly as their screenshot shows it.</summary>
    [Fact]
    public void EachValueStatementIsAPort()
    {
        const string Script = "5+3;\n\"Test\";";

        NodeDefinitionSource definition = Compile(Script);

        Assert.Equal(["result", "result2"], definition.Outputs.Select(port => port.Name));
        Assert.Equal([8, "Test"], Run(Script));
    }

    /// <summary>
    /// <b>It was a compile error before this row</b>, which is the whole reason the change is safe
    /// to make: nothing that worked can have changed meaning.
    /// </summary>
    [Fact]
    public void ItUsedToBeACompileError()
    {
        Assert.DoesNotContain(Factory().Diagnose("5+3;\n\"Test\";"), diagnostic => diagnostic.IsError);
    }

    /// <summary>More than two, and the ports keep the source order.</summary>
    [Fact]
    public void ThePortsAreInSourceOrder()
    {
        const string Script = "1;\n2;\n3;\n4;";

        Assert.Equal(
            ["result", "result2", "result3", "result4"],
            Compile(Script).Outputs.Select(port => port.Name));

        Assert.Equal([1, 2, 3, 4], Run(Script));
    }

    /// <summary>
    /// <b>The first port keeps the name it had</b>, so typing a second line into an existing block
    /// does not disconnect the wire already on it — wires are re-made by port name.
    /// </summary>
    [Fact]
    public void TheFirstPortIsStillCalledResult()
    {
        Assert.Equal(["result"], Compile("5+3;").Outputs.Select(port => port.Name));
        Assert.Equal("result", Compile("5+3;\n\"Test\";").Outputs[0].Name);
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
    /// <b>Values replace the declared-variable ports rather than joining them</b> — `E6-T27`'s
    /// decision, and one this row does not reopen. The block has said what it produces.
    /// </summary>
    [Fact]
    public void ValuesStillReplaceTheDeclaredPorts()
    {
        NodeDefinitionSource definition = Compile("var a = 1; var b = 2; a; b; a + b;");

        Assert.Equal(["result", "result2", "result3"], definition.Outputs.Select(port => port.Name));
        Assert.Equal([1, 2, 3], definition.Invoke([], CancellationToken.None).ToArray());
    }

    /// <summary>
    /// A statement that C# accepts is left alone even in the middle, so a block that computes with
    /// calls between its values still means what it says.
    /// </summary>
    [Fact]
    public void CallsBetweenValuesAreStillStatements()
    {
        const string Script =
            "var list = new List<int>();\nlist.Add(3);\nlist.Count;\nlist.Add(4);\nlist.Count;";

        Assert.Equal(["result", "result2"], Compile(Script).Outputs.Select(port => port.Name));
        Assert.Equal([1, 2], Run(Script));
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
    /// <b>An explicit <c>return</c> still decides the ports</b>, and a value beside one is
    /// unreachable rather than a second answer — the gate `E6-T26` put on all of this.
    /// </summary>
    [Fact]
    public void AnExplicitReturnStillWins()
    {
        Assert.Equal(["result"], Compile("var a = 1; var b = 2; return b;").Outputs.Select(p => p.Name));
        Assert.Equal(
            ["area", "count"],
            Compile("var a = 1; return (area: a, count: 2);").Outputs.Select(p => p.Name));
    }

    /// <summary>
    /// <b>The range markers move by what was inserted <i>ahead of them</i>, not by a constant.</b>
    /// Each value gets its own <c>var __resultN = </c>, so a marker on the third value has three
    /// insertions in front of it — and a marker looked for in the wrong place is lowered as the
    /// <i>other</i> range form, which is a wrong answer and not an error.
    /// </summary>
    /// <remarks>
    /// This is the interaction between `E6-T28` and `E10-T15`, and it is a defect the single-value
    /// version of this code could not have had: with one insertion the shift is a constant.
    /// </remarks>
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
