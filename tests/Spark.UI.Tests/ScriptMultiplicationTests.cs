using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// <c>a * b;</c> on its own is a value, not a pointer declaration — `E6-T31`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Found by running the samples in `E10-T16`'s getting-started guide</b>, on the most natural
/// line a user could type. <c>width * height;</c> did not compile, and the message was
/// <i>"Pointers and fixed size buffers may only be used in an unsafe context"</i> — about a
/// language feature the user did not use.
/// </para>
/// <para>
/// <b>In statement position <c>a * b;</c> is genuinely ambiguous.</b> C# can read it as <i>declare
/// a pointer-to-<c>a</c> called <c>b</c></i>, and the language resolves the ambiguity in favour of
/// the declaration — so the line never reached `E6-T27`'s rule as an expression statement at all.
/// </para>
/// <para>
/// <b>Claiming it is safe because the other reading cannot exist here.</b> A pointer declaration
/// needs <c>unsafe</c>, which a code block never has, so a line of this shape has exactly one
/// meaning it could ever have had. Nothing that compiles today changes, because nothing of this
/// shape compiles today — which is the same argument `E6-T27` made for <c>CS0201</c>.
/// </para>
/// </remarks>
public sealed class ScriptMultiplicationTests
{
    private static ScriptNodeFactory Factory()
    {
        _ = typeof(Point3d).Assembly.Location;

        return new ScriptNodeFactory();
    }

    private static NodeDefinitionSource Compile(string script) => Factory().Create(script);

    private static string[] Ports(string script) =>
        [.. Compile(script).Outputs.Select(port => port.Name)];

    private static object? Last(string script) =>
        Compile(script).Invoke([], CancellationToken.None).LastOrDefault();

    /// <summary>The line from the guide that started it.</summary>
    [Fact]
    public void TwoNamesMultipliedIsAValue()
    {
        Assert.Equal(["function"], Ports("width * height;"));
        Assert.Equal(["width", "height"], Compile("width * height;").Inputs.Select(port => port.Name));
    }

    /// <summary>And it holds when the names are declared, which is where it is most surprising.</summary>
    [Fact]
    public void ItHoldsForDeclaredNamesToo()
    {
        Assert.Equal(["a", "b", "function"], Ports("var a = 3.0; var b = 4.0; a * b;"));
        Assert.Equal(12.0, Last("var a = 3.0; var b = 4.0; a * b;"));
    }

    /// <summary>
    /// <b>The shapes the parser mangles differently all work.</b> <c>a * b * c;</c> arrives as a
    /// declaration with a <i>missing <c>=</c></i> rather than a tidy one, and <c>p.X * b;</c> as a
    /// pointer to a qualified name — which is why the fix re-parses the line's text as an
    /// expression instead of rebuilding it from the pointer type and the declarator.
    /// </summary>
    [Theory]
    [InlineData("var a = 3.0; var b = 4.0; var c = 2.0; a * b * c;", 24.0)]
    [InlineData("var a = 3.0; var b = 4.0; a*b;", 12.0)]
    [InlineData("var p = Point3d.Origin; var b = 4.0; p.X * b;", 0.0)]
    public void TheAwkwardShapesWorkToo(string script, double expected) =>
        Assert.Equal(expected, Last(script));

    /// <summary>
    /// <b>A real declaration is still a declaration.</b> The rule only claims a line whose declared
    /// <i>type</i> is a pointer, so nothing ordinary is reinterpreted.
    /// </summary>
    [Fact]
    public void OrdinaryDeclarationsAreUntouched()
    {
        Assert.Equal(["x"], Ports("var x = 1;"));
        Assert.Equal(["a", "b"], Ports("var a = 1; var b = 2;"));
        Assert.Equal(["total"], Ports("var total = 1; total = total + 4;"));
    }

    /// <summary>
    /// <b>The line `E6-T27` drew is still where it was.</b> A call is a statement, and this row
    /// must not have widened that — a multiplication is not a statement-expression, and the
    /// re-parse checks the same closed list before claiming anything.
    /// </summary>
    [Fact]
    public void ACallIsStillAStatement()
    {
        NodeDefinitionSource definition = Compile(
            "var list = new List<int>(); list.Add(3); list.Add(4);");

        Assert.Equal(["list"], definition.Outputs.Select(port => port.Name));
        Assert.Equal([3, 4], (IEnumerable<int>)definition.Invoke([], CancellationToken.None).First()!);
    }

    /// <summary>
    /// The multiplication is a value like any other, so it takes the <c>function</c> name and joins
    /// the declared ports rather than replacing them — `E6-T29`'s rule, unchanged.
    /// </summary>
    [Fact]
    public void ItIsAnOrdinaryValueLine()
    {
        Assert.Equal(["a", "b", "function", "function2"], Ports("var a = 2.0; var b = 3.0; a * b; a + b;"));
        Assert.Equal(5.0, Last("var a = 2.0; var b = 3.0; a * b; a + b;"));
    }

    /// <summary>
    /// <b>A trailing multiplication is the block's single value when nothing else produces one</b>,
    /// which takes `E6-T27`'s bare-return path rather than the capture path.
    /// </summary>
    [Fact]
    public void ABlockThatIsOneMultiplicationReturnsIt()
    {
        Assert.Equal(["function"], Ports("width * height;"));
        Assert.Equal(6.0, Compile("width * height;").Invoke([2.0, 3.0], CancellationToken.None).Single());
    }

    /// <summary>
    /// A diagnostic after the claimed line still lands on the user's line: the rewrite inserts
    /// characters, never a newline.
    /// </summary>
    [Fact]
    public void TheRewriteAddsNoLines()
    {
        const string Script = "var a = 2.0;\nvar b = 3.0;\na * b;\nvar c = ;";

        ScriptDiagnostic error = Factory().Diagnose(Script).First(diagnostic => diagnostic.IsError);

        Assert.Equal(4, error.Line);
    }
}
