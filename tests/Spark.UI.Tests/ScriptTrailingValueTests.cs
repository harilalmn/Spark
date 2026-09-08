using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// A code block's bare expressions are values rather than errors — `E6-T27`, as `E6-T29` left it.
/// </summary>
/// <remarks>
/// <para>
/// <b>`E6-T27` made two claims and only one of them survived, so this file is worth reading
/// carefully.</b> The claim that <i>stands</i> is which lines are claimed at all: only an
/// expression C# would <i>reject</i> as a statement, so every script these rows change was a
/// <c>CS0201</c> compile error a moment before and nothing that already compiles can change
/// meaning. <see cref="ACallIsStillAStatement"/> is the test that holds that line and it is the one
/// to read first.
/// </para>
/// <para>
/// <b>The claim that was reversed is that a trailing value <i>replaces</i> the declared ports.</b>
/// The client asked for that in `E6-T27` and then asked for Dynamo's rule instead in `E6-T29`,
/// where every line that makes something gets a port. Their call both times.
/// <c>ScriptStatementOutputTests</c> owns the new rule; what is left here is the older, narrower
/// claim underneath it.
/// </para>
/// <para>
/// Everything here goes through the real <see cref="ScriptNodeFactory"/> and evaluates the block,
/// because the claim is about what a user gets on the port, not about the shape of a tree.
/// </para>
/// </remarks>
public sealed class ScriptTrailingValueTests
{
    private static ScriptNodeFactory Factory()
    {
        _ = typeof(Point3d).Assembly.Location;

        return new ScriptNodeFactory();
    }

    private static NodeDefinitionSource Compile(string script) => Factory().Create(script);

    private static object? Run(string script)
    {
        object?[] outputs = [.. Compile(script).Invoke([], CancellationToken.None)];

        return outputs.Length > 0 ? outputs[0] : null;
    }

    /// <summary>
    /// The value on the <i>last</i> port, which is where a trailing value now lands.
    /// </summary>
    /// <remarks>
    /// Under `E6-T27` a trailing value was the block's only port and <see cref="Run"/> found it at
    /// index 0. Under `E6-T29` the declarations above it have ports of their own, so the trailing
    /// value is last rather than only — the value is the same, and it moved.
    /// </remarks>
    private static object? RunLast(string script)
    {
        object?[] outputs = [.. Compile(script).Invoke([], CancellationToken.None)];

        return outputs.Length > 0 ? outputs[^1] : null;
    }

    /// <summary>
    /// The client's first script, exactly as they wrote it: the last line is a value and carries 4.
    /// </summary>
    /// <remarks>
    /// <b>It is the last port rather than the only one, and that is `E6-T29`.</b> When this test
    /// was written the answer was one port; <c>n</c> and <c>p</c> now have ports of their own. The
    /// number the client asked about is unchanged.
    /// </remarks>
    [Fact]
    public void TheLastLineIsAValue()
    {
        const string Script = """
            var n = 10 / 5;
            var p = 8 / 4;
            n + p;
            """;

        Assert.Equal(4, RunLast(Script));
    }

    /// <summary>The client's second script: one interpolated string, and nothing else.</summary>
    [Fact]
    public void ABlockThatIsOneExpressionReturnsIt()
    {
        Assert.Equal("test", Run("$\"test\";"));
        Assert.Equal(7, Run("3 + 4;"));
        Assert.Equal("ab", Run("\"a\" + \"b\";"));
    }

    /// <summary>A bare name is a value too, which is the shortest form of all.</summary>
    [Fact]
    public void ABareIdentifierIsAValue()
    {
        Assert.Equal(5, Run("var q = 5; q;"));
    }

    /// <summary>
    /// <b>A trailing value joins the declared ports rather than replacing them</b> — the reversal
    /// `E6-T29` made, kept here beside the rule it replaced so the change is visible in one place.
    /// </summary>
    /// <remarks>
    /// This test previously asserted <c>["result"]</c>, and `E6-T27`'s reasoning for that was
    /// sound: a block that says what it produces has said it. The client asked for Dynamo's rule
    /// instead, where every line that makes something is a port. If this is ever changed back, the
    /// reason must be a third instruction rather than a tidy-up.
    /// </remarks>
    [Fact]
    public void ATrailingValueJoinsTheDeclaredPorts()
    {
        NodeDefinitionSource definition = Compile("var n = 1; var p = 2; n + p;");

        Assert.Equal(["n", "p", "function"], definition.Outputs.Select(port => port.Name));
    }

    /// <summary>
    /// <b>The line this feature must not cross.</b> <c>list.Add(3);</c> is a legal statement that
    /// compiles today and discards its value on purpose; blocks are written that way. It stays a
    /// statement, and the block still reports the port it declared.
    /// </summary>
    /// <remarks>
    /// If this ever goes red because somebody widened the rule to "any trailing expression", the
    /// widening is wrong: `list.Add(3)` returns void, so the block would stop compiling — and every
    /// non-void call at the end of a block would silently start returning something else.
    /// </remarks>
    [Fact]
    public void ACallIsStillAStatement()
    {
        NodeDefinitionSource definition = Compile(
            "var list = new List<int>(); list.Add(3); list.Add(4);");

        Assert.Equal(["list"], definition.Outputs.Select(port => port.Name));
        Assert.Equal([3, 4], (IEnumerable<int>)definition.Invoke([], CancellationToken.None).First()!);
    }

    /// <summary>Assignments and increments are statements too, and keep their declared ports.</summary>
    [Fact]
    public void AssignmentsAndIncrementsAreStillStatements()
    {
        Assert.Equal(["total"], Compile("var total = 1; total = total + 4;").Outputs.Select(p => p.Name));
        Assert.Equal(5, Run("var total = 1; total = total + 4;"));

        Assert.Equal(["count"], Compile("var count = 1; count++;").Outputs.Select(p => p.Name));
        Assert.Equal(2, Run("var count = 1; count++;"));
    }

    /// <summary>
    /// A block that ends in a declaration still reports its declared ports — the behaviour
    /// `E6-T26` built and this must not disturb.
    /// </summary>
    [Fact]
    public void DeclaredPortsStillWorkWhenThereIsNoTrailingValue()
    {
        Assert.Equal(["a", "b"], Compile("var a = 1; var b = 2;").Outputs.Select(port => port.Name));
        Assert.Equal(["result"], Compile("var a = 1; return a;").Outputs.Select(port => port.Name));
        Assert.Equal(["result"], Compile("// nothing at all").Outputs.Select(port => port.Name));
    }

    /// <summary>
    /// <b>An explicit <c>return</c> still wins</b>, and a trailing value after one is unreachable
    /// rather than a second answer.
    /// </summary>
    [Fact]
    public void AnExplicitReturnIsUntouched()
    {
        Assert.Equal(9, Run("var a = 9; return a;"));
        Assert.Equal(1, Run("return 1;"));
    }

    /// <summary>
    /// <b>The range markers move with the inserted <c>return</c>.</b> Seven characters go in ahead
    /// of the last statement, and a marker after that point would otherwise be looked for in the
    /// wrong place — and a range is lowered by <i>where</i> its marker sits, so it would come out
    /// as the wrong form rather than as an error.
    /// </summary>
    /// <remarks>
    /// This is the interaction between `E6-T27` and `E10-T15` and it has no other test: each
    /// feature works perfectly without the other.
    /// </remarks>
    [Fact]
    public void ARangeInTheTrailingValueIsStillLoweredCorrectly()
    {
        Assert.Equal([0, 0.25, 0.5, 0.75, 1], (IReadOnlyList<double>)Run("0..1..#5;")!);
        Assert.Equal([0, 1, 2, 3, 4], (IReadOnlyList<double>)Run("0..#5..1;")!);

        // A marker before the trailing statement and another inside it: the first must not move and
        // the second must. `first` is a port of its own since `E6-T29`, so the trailing value is
        // the last port rather than the only one - and both ranges are checked, because the
        // declaration's marker is exactly the one that must NOT have moved.
        object?[] both = [.. Compile("var first = 0..1..#5;\n3..5..#3;").Invoke([], CancellationToken.None)];

        Assert.Equal([0, 0.25, 0.5, 0.75, 1], (IReadOnlyList<double>)both[0]!);
        Assert.Equal([3, 4, 5], (IReadOnlyList<double>)both[1]!);
    }

    /// <summary>
    /// <b>The output type is inferred through the inserted return</b>, so the port carries a real
    /// type rather than <c>object</c> — which is what puts a type label on it and gives it a rank.
    /// </summary>
    [Fact]
    public void TheTrailingValuesTypeReachesThePort()
    {
        Assert.Equal(typeof(string), Assert.Single(Compile("$\"test\";").Outputs).ValueType);
        Assert.Equal(typeof(int), Assert.Single(Compile("3 + 4;").Outputs).ValueType);
    }

    /// <summary>
    /// A diagnostic on a line after the trailing value still lands on the user's line — the
    /// insertion adds characters, never a newline.
    /// </summary>
    [Fact]
    public void TheInsertedReturnAddsNoLines()
    {
        const string Script = "var a = 1;\nvar b = ;\na + 1;";

        ScriptDiagnostic error = Factory().Diagnose(Script).First(diagnostic => diagnostic.IsError);

        Assert.Equal(2, error.Line);
    }
}
