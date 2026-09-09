using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// A <c>using</c> written at the top of a code block — `E6-T39`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The client found the hole in `E7-T21` an hour after it shipped</b>: <i>take care of the
/// possible ambiguity, as the user cannot specify like</i>
/// <c>using Circle = SomeNameSpace.Circle;</c>. They were right twice over. A using-alias directive
/// is legal at compilation-unit or namespace scope and nowhere else, and a block's text is emitted
/// inside <c>__Block.Run</c>'s method body — so a user facing two libraries that both spell a type
/// <c>Line</c> had no way to say which one they meant.
/// </para>
/// <para>
/// <b>The mechanism is `E6-T34`'s, aimed at a second construct.</b> The directive is blanked to
/// spaces where it stood, so nothing below it moves, and re-emitted where a <c>using</c> is legal.
/// The tests that matter most here are the two negatives — <c>using var</c> and <c>using (…)</c>
/// are statements, they belong exactly where the user put them, and hoisting one would move a
/// disposal out of the method.
/// </para>
/// </remarks>
public sealed class HoistedUsingTests
{
    private static ScriptNodeFactory Factory()
    {
        // The catalogue is built from what this process has loaded, and a test class that never
        // mentions geometry can run before anything has loaded it - at which point the prelude's
        // `using Spark.Geometry;` does not resolve and every script here fails for a reason that
        // has nothing to do with imports. Naming a type loads it.
        _ = typeof(Spark.Geometry.Point3d).Assembly.Location;

        return new ScriptNodeFactory();
    }

    private static object? Run(string script) =>
        Assert.Single(Factory().Create(script).Invoke([], CancellationToken.None));

    /// <summary>
    /// <b>The plain case, which did not compile before this row.</b> A <c>using</c> directive
    /// inside a method body is <c>CS1529</c>, and that is where a block's text goes.
    /// </summary>
    [Fact]
    public void ABlockMayOpenWithAUsingDirective()
    {
        const string script = """
            using System.Text;
            var built = new StringBuilder().Append("ok").ToString();
            """;

        Assert.Empty(Factory().Diagnose(script));
        Assert.Equal("ok", Run(script));
    }

    /// <summary>
    /// <b>The client's own line, in the shape they wrote it.</b> This is the whole point of the
    /// row: an alias is how C# says <i>this one, not that one</i>, and until now a block could not
    /// contain one.
    /// </summary>
    [Fact]
    public void ABlockMayDeclareAUsingAlias()
    {
        const string script = """
            using Builder = System.Text.StringBuilder;
            var built = new Builder().Append("aliased").ToString();
            """;

        Assert.Empty(Factory().Diagnose(script));
        Assert.Equal("aliased", Run(script));
    }

    /// <summary>
    /// <b>An alias beats the prelude's own import of the same name, and that is the placement doing
    /// the work.</b> The directives are emitted <i>inside</i> <c>namespace SparkGenerated</c> while
    /// the prelude sits above it, so a user's alias lands in an inner declaration space and shadows
    /// what was there rather than colliding with it. <c>Convert</c> is <c>System.Convert</c> to
    /// every other block in Spark; here it is whatever the user said it was.
    /// </summary>
    [Fact]
    public void AnAliasOverrulesAPreludeImportOfTheSameName()
    {
        const string script = """
            using Convert = System.Text.StringBuilder;
            var built = new Convert().Append("mine").ToString();
            """;

        Assert.Empty(Factory().Diagnose(script));
        Assert.Equal("mine", Run(script));
    }

    /// <summary>
    /// <b><c>using var</c> is a statement and must not move.</b> It shares a keyword with the
    /// directive and nothing else: hoisted, the disposal would leave the method and the variable
    /// would be undeclared where it is used. Roslyn has already decided which is which, which is
    /// why this asks the parser rather than the text.
    /// </summary>
    [Fact]
    public void AUsingDeclarationStaysWhereItWasWritten()
    {
        const string script = """
            using var writer = new System.IO.StringWriter();
            writer.Write("held");
            var built = writer.ToString();
            """;

        Assert.Empty(Factory().Diagnose(script));

        // Two outputs, because `E6-T28` claims every value the block leaves behind and the writer
        // is one of them. The last is the one this row is about.
        Assert.Equal("held", Factory().Create(script).Invoke([], CancellationToken.None).Last());
    }

    /// <summary>
    /// <b>And neither does <c>using (…)</c>.</b> The other half of the same trap.
    /// </summary>
    [Fact]
    public void AUsingStatementStaysWhereItWasWritten()
    {
        const string script = """
            var built = "";
            using (var writer = new System.IO.StringWriter())
            {
                writer.Write("scoped");
                built = writer.ToString();
            }
            """;

        Assert.Empty(Factory().Diagnose(script));
        Assert.Equal("scoped", Run(script));
    }

    /// <summary>
    /// <b>A directive that moved still reports on the line it was written on</b> — the segment
    /// `E6-T34` added to the source map, earning its keep for a second construct.
    /// </summary>
    [Fact]
    public void AnErrorInAHoistedUsingLandsOnTheUsersOwnLine()
    {
        const string script = """
            using System.Text;
            using Nowhere.At.All;
            var second = 2;
            """;

        ScriptDiagnostic error = Assert.Single(
            Factory().Diagnose(script), diagnostic => diagnostic.IsError);

        Assert.Equal("CS0246", error.Id);
        Assert.Equal(2, error.Line);
    }

    /// <summary>
    /// <b>Blanked rather than cut, which is the invariant the whole approach rests on.</b> An
    /// error three lines below a hoisted directive must still be reported three lines down; a
    /// removal would have shortened the file and moved it.
    /// </summary>
    [Fact]
    public void HoistingAUsingDoesNotMoveTheLinesBelowIt()
    {
        const string script = """
            using System.Text;
            using System.Globalization;
            var fine = 1;
            var broken = "x".NoSuchMethod();
            """;

        // Not an unresolved *identifier*: `E6-T5` turns one of those into an input port, so the
        // block would compile and this would assert on an empty list.
        ScriptDiagnostic error = Assert.Single(
            Factory().Diagnose(script), diagnostic => diagnostic.IsError);

        Assert.Equal("CS1061", error.Id);
        Assert.Equal(4, error.Line);
    }

    /// <summary>
    /// <b>A type declared in a block, written in terms of a namespace that block imported, still
    /// reaches the block next door.</b> `E6-T35` compiles every declaration into one shared
    /// assembly — so half of what the type needs would have been left behind unless the graph's
    /// <c>using</c> lines came with it.
    /// </summary>
    [Fact]
    public void ASharedDeclarationCarriesTheBlocksOwnUsingWithIt()
    {
        const string declares = """
            using System.Text;
            public class Greeter
            {
                public static string Greet() => new StringBuilder().Append("hello").ToString();
            }
            """;

        const string uses = "var said = Greeter.Greet();";

        ScriptNodeFactory factory = Factory();

        Assert.True(factory.Share([declares, uses]));
        Assert.True(factory.Declarations.IsShared);
        Assert.Empty(factory.Diagnose(uses));
        Assert.Equal("hello", Assert.Single(factory.Create(uses).Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// <b>A hoisted directive is not an input port.</b> Input inference compiles with nothing
    /// declared and reads the <c>CS0103</c>s, so a namespace name it could not resolve would
    /// otherwise arrive on the node as a socket nobody asked for.
    /// </summary>
    [Fact]
    public void AUsingDoesNotBecomeAnInputPort()
    {
        const string script = """
            using System.Text;
            var built = new StringBuilder().Append(given).ToString();
            """;

        NodeDefinitionSource block = Factory().Create(script);

        Assert.Equal("given", Assert.Single(block.Inputs).Name);
    }
}
