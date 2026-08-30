using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Spark.Api;
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

    /// <summary>An unreadable path is skipped rather than taking the whole catalogue down.</summary>
    [Fact]
    public void AnUnreadablePathIsSkipped()
    {
        ReferenceCatalog catalogue = new();

        catalogue.Add(["Z:\\does\\not\\exist.dll", string.Empty]);

        Assert.NotEmpty(catalogue.References);
    }
}
