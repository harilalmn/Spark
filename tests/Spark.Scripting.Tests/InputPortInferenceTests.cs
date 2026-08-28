using System;
using System.Collections.Generic;
using Spark.Engine;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.Scripting.Tests;

/// <summary>
/// Input ports come from asking the compiler what it could not resolve, never from walking the
/// syntax tree. These tests are mostly about the cases a walker gets wrong.
/// </summary>
public sealed class InputPortInferenceTests
{
    [Fact]
    public void EveryUndefinedIdentifierBecomesAnInputPortInSourceOrder()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "radius * height * count",
            CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("radius", "height", "count")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal(["radius", "height", "count"], CodeBlockTestHarness.NamesOf(compilation.Inputs));
    }

    /// <summary>
    /// A local the block declares is not an input. This is the case a syntax walker has to
    /// reimplement scoping to get right, and the reason the inference is semantic.
    /// </summary>
    [Fact]
    public void ALocalTheBlockDeclaresIsNotAnInputPort()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            double scale = 2;
            radius * scale
            """,
            CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("radius")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal(["radius"], CodeBlockTestHarness.NamesOf(compilation.Inputs));
    }

    /// <summary>
    /// A <c>using</c> alias resolves a name that looks exactly like an undefined identifier. The
    /// compiler knows; a walker would have to be told.
    /// </summary>
    [Fact]
    public void AUsingAliasIsNotAnInputPort()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            using Num = System.Math;
            Num.Abs(value)
            """,
            CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("value")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal(["value"], CodeBlockTestHarness.NamesOf(compilation.Inputs));
    }

    /// <summary>
    /// A loop variable and a local function's parameters are in scope where they are used, and the
    /// compiler resolves both without help.
    /// </summary>
    [Fact]
    public void LoopVariablesAndLocalFunctionParametersAreNotInputPorts()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            double Twice(double x) => x * 2;
            double sum = 0;
            for (int i = 0; i < count; i++) { sum += Twice(i); }
            sum
            """,
            CodeBlockTestHarness.Options(CodeBlockTestHarness.Wired(typeof(int), "count")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal(["count"], CodeBlockTestHarness.NamesOf(compilation.Inputs));
    }

    /// <summary>
    /// A port's identity is its name. Using it in more places does not add a port and does not
    /// rewire anything.
    /// </summary>
    [Fact]
    public void UsingAnInputAgainDoesNotAddAPort()
    {
        Dictionary<string, Type> wired = CodeBlockTestHarness.Doubles("a", "b");

        CodeBlockCompilation once = CodeBlockCompiler.Compile(
            "a + b", CodeBlockTestHarness.Options(wired));

        CodeBlockCompilation twice = CodeBlockCompiler.Compile(
            """
            double t = a + b;
            t + a + b + a
            """,
            CodeBlockTestHarness.Options(wired));

        Assert.True(once.Success, CodeBlockTestHarness.Report(once));
        Assert.True(twice.Success, CodeBlockTestHarness.Report(twice));
        Assert.Equal(["a", "b"], CodeBlockTestHarness.NamesOf(once.Inputs));
        Assert.Equal(["a", "b"], CodeBlockTestHarness.NamesOf(twice.Inputs));
    }

    [Fact]
    public void AnUnconnectedPortIsTypedAsObject()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile("centre", CodeBlockTestHarness.Options());

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        PortDefinition port = Assert.Single(compilation.Inputs);
        Assert.Equal("centre", port.Name);
        Assert.Equal(typeof(object), port.ValueType);
        Assert.Contains("object centre = __in[0];", compilation.GeneratedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The differentiator.</b> Once a port is wired, the upstream port's static type is known, so
    /// the generated source declares it as that type rather than as <see cref="object"/> — which is
    /// what lets completion inside the code block know what is on the incoming wire.
    /// </summary>
    [Fact]
    public void AConnectedPortDeclaresTheUpstreamTypeInTheRewrittenSource()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "centre.X + centre.Y",
            CodeBlockTestHarness.Options(CodeBlockTestHarness.Wired(typeof(Point3d), "centre")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        Assert.Contains(
            "global::Spark.Geometry.Point3d centre = ", compilation.GeneratedSource, StringComparison.Ordinal);

        PortDefinition port = Assert.Single(compilation.Inputs);
        Assert.Equal(typeof(Point3d), port.ValueType);
    }

    /// <summary>
    /// And it is not merely cosmetic: the member access that only exists on the typed value has to
    /// compile. Typed as <see cref="object"/> the very same text is an error.
    /// </summary>
    [Fact]
    public void MemberAccessOnAConnectedPortCompilesOnlyBecauseTheTypeIsInjected()
    {
        CodeBlockCompilation typed = CodeBlockCompiler.Compile(
            "centre.X", CodeBlockTestHarness.Options(CodeBlockTestHarness.Wired(typeof(Point3d), "centre")));

        CodeBlockCompilation untyped = CodeBlockCompiler.Compile(
            "centre.X", CodeBlockTestHarness.Options());

        Assert.True(typed.Success, CodeBlockTestHarness.Report(typed));
        Assert.False(untyped.Success);
        Assert.Contains(untyped.Diagnostics, diagnostic => diagnostic.CompilerId == "CS1061");
    }

    [Fact]
    public void AnExplicitDirectiveDeclaresATypedPortWithADefault()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            // in: double radius = 5.0
            radius * 2
            """,
            CodeBlockTestHarness.Options());

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        PortDefinition port = Assert.Single(compilation.Inputs);
        Assert.Equal("radius", port.Name);
        Assert.Equal(typeof(double), port.ValueType);
        Assert.Equal(5.0, port.DefaultValue);
    }

    /// <summary>An explicit directive wins over whatever happens to be wired to that port.</summary>
    [Fact]
    public void AnExplicitDirectiveOverridesTheConnectedType()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            // in: double value = 1.0
            value * 2
            """,
            CodeBlockTestHarness.Options(CodeBlockTestHarness.Wired(typeof(Point3d), "value")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        PortDefinition port = Assert.Single(compilation.Inputs);
        Assert.Equal(typeof(double), port.ValueType);
    }

    [Fact]
    public void AMalformedDirectiveIsRejectedWithItsOwnCode()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            // in: radius
            1
            """,
            CodeBlockTestHarness.Options());

        Assert.False(compilation.Success);
        Assert.Contains(
            compilation.Diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.MalformedInputDirective);
    }

    /// <summary>
    /// A list-typed port keeps its rank, which is what stops the engine from replicating over a list
    /// the block wanted whole.
    /// </summary>
    [Fact]
    public void AListTypedDirectiveProducesARankOnePort()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            // in: System.Collections.Generic.IReadOnlyList<double> xs
            xs.Count
            """,
            CodeBlockTestHarness.Options());

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        PortDefinition port = Assert.Single(compilation.Inputs);
        Assert.Equal(1, port.DeclaredRank);
    }

    /// <summary>
    /// The <c>CS0117</c> half. A name that resolves to a type in scope reports a missing member
    /// rather than a missing name, and a walker following the <c>CS0103</c> rule alone would silently
    /// lose the port.
    /// </summary>
    [Fact]
    public void AnIdentifierThatShadowsATypeNameStillBecomesAPort()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "Math.SomethingOfMine", CodeBlockTestHarness.Options());

        Assert.Contains("Math", CodeBlockTestHarness.NamesOf(compilation.Inputs));
    }

    /// <summary>A block with nothing undefined in it has no input ports at all.</summary>
    [Fact]
    public void ABlockThatNeedsNothingHasNoInputPorts()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile("1 + 1", CodeBlockTestHarness.Options());

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Empty(compilation.Inputs);
    }
}
