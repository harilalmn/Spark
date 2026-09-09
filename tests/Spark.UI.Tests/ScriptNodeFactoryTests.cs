using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Spark.Api;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// The Roslyn pipeline behind a code block — `E6-T2`, `E6-T5`, `E6-T8`, `E6-T9`.
/// </summary>
/// <remarks>
/// <para>
/// These live in the UI test assembly because it is the one that already references
/// <c>Spark.Scripting</c> — the M1.5 completion spike put them together, and a graph assembly that
/// referenced Roslyn would defeat the point of <c>E6-T14</c>.
/// </para>
/// <para>
/// <b>Port inference is the part worth testing hardest.</b> It is the difference between a code
/// block that reads like C# and one that needs a declaration ceremony, and the semantic approach
/// is only better than a syntax walk if it actually distinguishes the cases a syntax walk gets
/// wrong — a local, a lambda parameter, a type name, a method call. Each of those has a test.
/// </para>
/// </remarks>
public sealed class ScriptNodeFactoryTests
{
    /// <summary>
    /// <b>The simplest thing that must work.</b> A free identifier is an input port; the value
    /// comes back through it.
    /// </summary>
    [Fact]
    public void AFreeIdentifierBecomesAnInputPort()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create("return a * 2;");

        Assert.Equal("a", Assert.Single(block.Inputs).Name);
        Assert.Equal(84.0, Assert.Single(block.Invoke([42.0], CancellationToken.None)));
    }

    /// <summary>Several free identifiers become several ports, in source order.</summary>
    [Fact]
    public void FreeIdentifiersBecomePortsInSourceOrder()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create("return width * height + depth;");

        Assert.Equal(["width", "height", "depth"], block.Inputs.Select(p => p.Name));
        Assert.Equal(23.0, Assert.Single(block.Invoke([4.0, 5.0, 3.0], CancellationToken.None)));
    }

    /// <summary>
    /// <b>A local is not a port</b>, and this is the first case a syntax walk gets wrong — it sees
    /// an identifier and has to re-implement scoping to know better. The compiler already knows.
    /// </summary>
    [Fact]
    public void ALocalVariableIsNotAnInputPort()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "var doubled = a * 2; var total = doubled + 1; return total;");

        Assert.Equal("a", Assert.Single(block.Inputs).Name);
    }

    /// <summary>A lambda's parameter is not a port either, for the same reason.</summary>
    [Fact]
    public void ALambdaParameterIsNotAnInputPort()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "var f = (double x) => x * scale; return f(2.0);");

        Assert.Equal("scale", Assert.Single(block.Inputs).Name);
    }

    /// <summary>
    /// A type reached through the prelude is not a port. <c>Point3d</c> resolves because
    /// <c>Spark.Geometry</c> is imported, and an identifier that resolves to anything at all is not
    /// an input — which is exactly the rule a syntax walk cannot express.
    /// </summary>
    [Fact]
    public void ATypeFromThePreludeIsNotAnInputPort()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "var p = new Point3d(x, 0, 0); return p;");

        Assert.Equal("x", Assert.Single(block.Inputs).Name);
    }

    /// <summary>And the geometry it built is real geometry, not a string that looks like it.</summary>
    [Fact]
    public void AScriptCanBuildGeometryFromThePrelude()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "return new Point3d(x, 2, 3);");

        object? result = Assert.Single(block.Invoke([1.0], CancellationToken.None));

        Spark.Geometry.Point3d point = Assert.IsType<Spark.Geometry.Point3d>(result);
        Assert.Equal(1.0, point.X);
        Assert.Equal(2.0, point.Y);
    }

    /// <summary>
    /// <b>`E6-T8`: a named tuple gives named output ports.</b> Idiomatic C#, statically analysable,
    /// no invented syntax.
    /// </summary>
    [Fact]
    public void ANamedTupleReturnGivesNamedOutputPorts()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "return (area: w * h, perimeter: 2 * (w + h));");

        Assert.Equal(["area", "perimeter"], block.Outputs.Select(p => p.Name));

        object?[] values = block.Invoke([3.0, 4.0], CancellationToken.None);
        Assert.Equal(12.0, values[0]);
        Assert.Equal(14.0, values[1]);
    }

    /// <summary>Anything else returns one port called <c>result</c>.</summary>
    [Fact]
    public void APlainReturnGivesOneResultPort()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create("return a + 1;");

        Assert.Equal("result", Assert.Single(block.Outputs).Name);
    }

    /// <summary>
    /// <b>A script that does not compile still produces a definition.</b> A node that vanished
    /// because of a typo would take its wires with it, and the user would rebuild them after fixing
    /// a semicolon. The failure surfaces when it runs, which is where they are looking.
    /// </summary>
    [Fact]
    public void ABrokenScriptStillYieldsANodeAndFailsWhenRun()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create("return a * ;");

        Assert.NotNull(block.Invoke);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => block.Invoke([1.0], CancellationToken.None));

        Assert.Contains("did not compile", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <b>`E6-T17`: a script does not start once evaluation has been cancelled.</b>
    /// </summary>
    /// <remarks>
    /// The generated entry point takes the token and tests it before a line of the user's source
    /// runs. On its own that stops nothing already looping — bounding a loop is `E6-T4`'s job — but
    /// it is what keeps every code block downstream of a cancelled node from running to completion
    /// before anyone notices, which is the common case rather than the dramatic one.
    /// </remarks>
    [Fact]
    public void ACancelledTokenStopsAScriptBeforeItRuns()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create("return a * 2;");

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => block.Invoke([42.0], cancelled.Token));
    }

    /// <summary>
    /// <b>Cancellation arrives bare, not wrapped in a <see cref="TargetInvocationException"/>.</b>
    /// </summary>
    /// <remarks>
    /// The entry point is bound with <c>CreateDelegate</c> rather than called through
    /// <c>MethodInfo.Invoke</c>, and this test is the reason rather than speed. The replicator
    /// recognises cancellation by catching <see cref="OperationCanceledException"/> and letting it
    /// through; a wrapped one does not match that filter, so it would be reported as
    /// <c>'CodeBlock' failed</c> and the evaluation would continue — a stop button that logs an
    /// error and does not stop. <see cref="Assert.Throws{T}(System.Func{object})"/> is exact rather
    /// than assignable, so a wrapper fails this outright.
    /// </remarks>
    [Fact]
    public void AScriptsExceptionIsNotWrappedByReflection()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            "if (a > 0) throw new InvalidOperationException(\"from the script\"); return a;");

        using CancellationTokenSource live = new();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => block.Invoke([1.0], live.Token));

        Assert.Equal("from the script", thrown.Message);
    }

    /// <summary>An uncancelled token is simply the ordinary path, and costs the script nothing.</summary>
    [Fact]
    public void ALiveTokenLetsTheScriptRun()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create("return a * 2;");

        using CancellationTokenSource live = new();

        Assert.Equal(84.0, Assert.Single(block.Invoke([42.0], live.Token)));
    }

    /// <summary>
    /// <b>`E6-T9`: the same script compiles once.</b> This is what makes a slider feeding a code
    /// block feel live — every drag is an invocation, not a compilation.
    /// </summary>
    [Fact]
    public void TheSameScriptIsCompiledOnce()
    {
        ScriptNodeFactory factory = new();

        factory.Create("return a + 1;");
        factory.Create("return a + 1;");
        factory.Create("return a + 1;");

        Assert.Equal(1, factory.CachedScripts);

        factory.Create("return a + 2;");
        Assert.Equal(2, factory.CachedScripts);
    }

    /// <summary>
    /// Two scripts differing only in line endings are the same script. Whitespace inside a line is
    /// left alone, because it is meaningful in a verbatim string and normalising it would make two
    /// scripts that behave differently hash the same.
    /// </summary>
    [Fact]
    public void LineEndingsDoNotChangeAScriptsIdentity()
    {
        ScriptNodeFactory factory = new();

        NodeDefinitionSource unix = factory.Create("var b = a;\nreturn b;");
        NodeDefinitionSource windows = factory.Create("var b = a;\r\nreturn b;");

        Assert.Equal(unix.ContentHash, windows.ContentHash);
    }

    /// <summary>
    /// The reference catalogue carries the prelude, and the prelude is what makes
    /// <c>Point3d</c> resolve without the user typing a <c>using</c>.
    /// </summary>
    [Fact]
    public void TheCatalogueImportsTheGeometryNamespaces()
    {
        ReferenceCatalog catalogue = new();

        Assert.Contains("Spark.Geometry", catalogue.Imports);
        Assert.Contains("Spark.Api", catalogue.Imports);
        Assert.NotEmpty(catalogue.References);
        Assert.Contains("using Spark.Geometry;", catalogue.Prelude(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The catalogue's version changes when it does, because it is part of every compile-cache key
    /// — a script whose text has not changed still has to recompile when the assemblies underneath
    /// it have.
    /// </summary>
    [Fact]
    public void AddingReferencesMovesTheCatalogueVersion()
    {
        ReferenceCatalog catalogue = new();
        int before = catalogue.Version;

        catalogue.Add([typeof(ScriptNodeFactoryTests).Assembly.Location]);

        Assert.NotEqual(before, catalogue.Version);
    }

    /// <summary>
    /// <b>The two assemblies <c>dynamic</c> needs are named rather than hoped for.</b> The catalogue
    /// is otherwise built from what the process has already loaded, and neither of these is loaded
    /// by anything a user does — so a host that had touched neither compiled <c>return count * 2;</c>
    /// and was told <c>Missing compiler required member 'Binder.BinaryOperation'</c>, a message that
    /// names the binder while the assembly actually absent is the one underneath it.
    /// </summary>
    [Fact]
    public void TheCatalogueNamesTheAssembliesDynamicNeeds()
    {
        string[] paths =
        [
            .. new ReferenceCatalog().References
                .OfType<PortableExecutableReference>()
                .Select(reference => Path.GetFileName(reference.FilePath) ?? string.Empty),
        ];

        Assert.Contains("Microsoft.CSharp.dll", paths, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("System.Linq.Expressions.dll", paths, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>An unreadable path is skipped rather than taking the whole catalogue down.</summary>
    [Fact]
    public void AnUnreadablePathIsSkipped()
    {
        ReferenceCatalog catalogue = new();

        catalogue.Add(["Z:\\does\\not\\exist.dll", string.Empty]);

        Assert.NotEmpty(catalogue.References);
    }

    /// <summary>
    /// <b>The client's own block, character for character, and it did not compile at all</b>
    /// (`E6-T34`). A code block's text is the body of a generated method and C# has no local class,
    /// so a block that declared one was answered with <c>CS1022</c> and <c>CS0161</c> — neither of
    /// which names what is wrong, and both of which are about the frame rather than the script.
    /// </summary>
    [Fact]
    public void ABlockCanDeclareAClassAndUseIt()
    {
        const string script = """
            public class TestClass
            {
                public static Circle Test()
                {
                    Point3d p = new Point3d(0,0,0);
                    return new Circle(p,1.0);
                }
            }
            var circle = TestClass.Test();
            """;

        ScriptNodeFactory factory = new();

        Assert.Empty(factory.Diagnose(script));

        NodeDefinitionSource block = factory.Create(script);

        Assert.Empty(block.Inputs);
        Assert.Equal(["circle"], block.Outputs.Select(port => port.Name));
        Assert.Equal(1.0, Assert.IsType<Circle>(Assert.Single(block.Invoke([], CancellationToken.None))).Radius);
    }

    /// <summary>
    /// <b>The declaration may come before the statements that use it</b>, which is the case a
    /// scheme that kept the user's text in one contiguous run could not have handled — it would
    /// have had to close the generated method before the class and reopen it after, and the
    /// block's locals do not survive that. Blanking the declaration to spaces where it stood and
    /// re-emitting it below is what makes the position of the class irrelevant.
    /// </summary>
    [Fact]
    public void ADeclarationMayComeBeforeTheStatementsThatUseIt()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            """
            public class Helper
            {
                public static double Twice(double x) => x * 2;
            }
            var doubled = Helper.Twice(seed);
            """);

        Assert.Equal(["seed"], block.Inputs.Select(port => port.Name));
        Assert.Equal(["doubled"], block.Outputs.Select(port => port.Name));
        Assert.Equal(84.0, Assert.Single(block.Invoke([42.0], CancellationToken.None)));
    }

    /// <summary>Records, structs and enums move the same way a class does.</summary>
    [Theory]
    [InlineData("public record Pair(double A, double B);\nvar sum = new Pair(1, 2).A;")]
    [InlineData("public struct Pair { public double A; }\nvar sum = new Pair { A = 1 }.A;")]
    [InlineData("public enum Side { Left = 1 }\nvar sum = (double)Side.Left;")]
    public void EveryKindOfTypeDeclarationIsHoisted(string script)
    {
        Assert.Equal(1.0, Assert.Single(new ScriptNodeFactory().Create(script).Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// <b>A diagnostic inside a declared type lands on the user's own line.</b> The declaration is
    /// re-emitted a long way below where it was written, so this is the one place the source map is
    /// no longer a subtraction — and getting it wrong would put the squiggle on whatever statement
    /// happened to share the arithmetic.
    /// </summary>
    [Fact]
    public void ADiagnosticInsideADeclaredTypeIsPlacedOnTheUsersLine()
    {
        ScriptDiagnostic error = Assert.Single(
            new ScriptNodeFactory().Diagnose(
                """
                var a = 1;
                public class Helper
                {
                    public static double Twice(double x) => nope;
                }
                """),
            diagnostic => diagnostic.IsError);

        Assert.Equal("CS0103", error.Id);
        Assert.Equal(4, error.Line);
    }

    /// <summary>
    /// <b>An unresolved name in a class body is a typo, not an input port.</b> Ports are found by
    /// compiling with nothing declared and reading the <c>CS0103</c>s, and a declared type cannot
    /// see the entry point's locals — so without this the block above would grow a socket called
    /// <c>nope</c> and hide the misspelling behind it.
    /// </summary>
    [Fact]
    public void AnUnresolvedNameInsideADeclaredTypeIsNotAnInputPort()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            """
            public class Helper
            {
                public static double Twice(double x) => nope;
            }
            var doubled = seed * 2;
            """);

        Assert.Equal(["seed"], block.Inputs.Select(port => port.Name));
    }

    /// <summary>
    /// <b>A block may declare a type called <c>Block</c></b>, which is not a contrived name in a
    /// CAD application. The generated class was called that until `E6-T34` let a user's type reach
    /// namespace scope, at which point the collision would have been reported against a namespace
    /// they have never heard of.
    /// </summary>
    [Fact]
    public void ADeclaredTypeMayBeCalledBlock()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            """
            public class Block
            {
                public static double Height => 3;
            }
            var height = Block.Height;
            """);

        Assert.Equal(3.0, Assert.Single(block.Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// <b>`E10-T15`'s range markers are unmoved by a declaration leaving.</b> They are offsets into
    /// the script, and the declaration is blanked to spaces rather than cut out precisely so that
    /// every offset after it stays where it was. Cutting instead would lower <c>0..8..#5</c> as a
    /// step range — a wrong list rather than an error.
    /// </summary>
    [Fact]
    public void ARangeAfterADeclarationIsStillLoweredCorrectly()
    {
        NodeDefinitionSource block = new ScriptNodeFactory().Create(
            """
            public class Helper
            {
                public static double Twice(double x) => x * 2;
            }
            var counted = 0..8..#5;
            """);

        Assert.Equal(
            [0.0, 2.0, 4.0, 6.0, 8.0],
            Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                Assert.Single(block.Invoke([], CancellationToken.None))).Cast<object>());
    }
}
