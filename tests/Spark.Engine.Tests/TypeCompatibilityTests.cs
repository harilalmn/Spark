using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>A type whose full name is deliberately duplicated in a second assembly at run time.</summary>
public sealed class Widget
{
}

/// <summary>A type with a user-defined implicit conversion, for rule 4.</summary>
public readonly struct Metres
{
    public Metres(double value) => Value = value;

    public double Value { get; }

    public static implicit operator Millimetres(Metres metres) => new(metres.Value * 1000.0);
}

/// <summary>The target of <see cref="Metres"/>'s implicit conversion.</summary>
public readonly struct Millimetres
{
    public Millimetres(double value) => Value = value;

    public double Value { get; }
}

/// <summary>
/// The wire compatibility rules, in the order they are tried. The order is the contract, so each
/// test names the rule it expects rather than only asserting that the connection was allowed.
/// </summary>
public sealed class TypeCompatibilityTests
{
    /// <summary>Rule 1: identical or assignable.</summary>
    [Fact]
    public void IdenticalAndAssignableTypesConnectDirectly()
    {
        Assert.Equal(PortCompatibility.Direct, TypeCompatibility.Default.Check(typeof(double), typeof(double)).Kind);
        Assert.Equal(PortCompatibility.Direct, TypeCompatibility.Default.Check(typeof(Widget), typeof(object)).Kind);
    }

    /// <summary>
    /// Rule 2: numeric widening only. Narrowing is never automatic — it is a decision the user makes
    /// with a node, so that the loss is visible on the canvas rather than buried inside a wire.
    /// </summary>
    [Fact]
    public void NumericWideningIsAutomaticAndNarrowingIsRefused()
    {
        Assert.Equal(PortCompatibility.NumericWidening, TypeCompatibility.Default.Check(typeof(int), typeof(double)).Kind);
        Assert.Equal(PortCompatibility.NumericWidening, TypeCompatibility.Default.Check(typeof(float), typeof(double)).Kind);
        Assert.Equal(PortCompatibility.Incompatible, TypeCompatibility.Default.Check(typeof(double), typeof(int)).Kind);
    }

    /// <summary>Rule 3: a registered converter, with its lossiness reported so the wire can be drawn yellow.</summary>
    [Fact]
    public void ARegisteredConverterIsUsedAndReportsWhetherItIsLossy()
    {
        ConversionRegistry registry = new();
        registry.Register<Widget, string>(_ => "widget", isLossy: true);
        TypeCompatibility compatibility = new(registry);

        CompatibilityResult result = compatibility.Check(typeof(Widget), typeof(string));

        Assert.Equal(PortCompatibility.RegisteredConverter, result.Kind);
        Assert.True(result.IsLossy);
    }

    /// <summary>
    /// The order is a contract, not an implementation detail: a registered converter must not be able
    /// to shadow the widening rule above it, or a package could change what an existing wire does.
    /// </summary>
    [Fact]
    public void AnEarlierRuleWinsOverALaterOne()
    {
        ConversionRegistry registry = new();
        registry.Register<int, double>(value => value * 1000.0, isLossy: true);

        CompatibilityResult result = new TypeCompatibility(registry).Check(typeof(int), typeof(double));

        Assert.Equal(PortCompatibility.NumericWidening, result.Kind);
        Assert.False(result.IsLossy);
    }

    /// <summary>Rule 4: a user-defined implicit conversion, reported as a conversion because we cannot know whether it loses anything.</summary>
    [Fact]
    public void AUserDefinedImplicitOperatorIsFoundByReflection()
    {
        CompatibilityResult result = TypeCompatibility.Default.Check(typeof(Metres), typeof(Millimetres));

        Assert.Equal(PortCompatibility.ImplicitOperator, result.Kind);
        Assert.True(result.IsLossy);
    }

    /// <summary>
    /// Rule 5: rank lifting. A list of doubles into a double port is not an error — the node
    /// replicates over it, which is how anything gets built.
    /// </summary>
    [Fact]
    public void RankLiftingConnectsAListToAScalarPortAndBack()
    {
        Assert.Equal(
            PortCompatibility.RankLifting,
            TypeCompatibility.Default.Check(typeof(IReadOnlyList<double>), typeof(double)).Kind);

        Assert.Equal(
            PortCompatibility.RankLifting,
            TypeCompatibility.Default.Check(typeof(double), typeof(IReadOnlyList<double>)).Kind);
    }

    /// <summary>Rule 6: a port that keeps structure accepts anything, whatever it is declared as.</summary>
    [Fact]
    public void AKeepStructurePortAcceptsAnything()
    {
        Assert.Equal(
            PortCompatibility.ObjectTarget,
            TypeCompatibility.Default.Check(typeof(Widget), typeof(double), targetKeepsStructure: true).Kind);
    }

    /// <summary>Nothing matched: refused at design time, with a message that says what to do.</summary>
    [Fact]
    public void AnUnmatchedPairIsRefusedWithAnActionableMessage()
    {
        CompatibilityResult result = TypeCompatibility.Default.Check(typeof(string), typeof(double));

        Assert.Equal(PortCompatibility.Incompatible, result.Kind);
        Assert.Contains("conversion node", result.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two types with the same full name from different assemblies are refused with <b>both</b>
    /// assemblies named.
    /// </summary>
    /// <remarks>
    /// Without this rule the wire is allowed and fails at run time as <i>cannot cast Widget to
    /// Widget</i>, which is unactionable: nothing in the message says there are two Widgets. The test
    /// builds a genuine second assembly at run time rather than mocking one, because the property
    /// being asserted is about assembly identity.
    /// </remarks>
    [Fact]
    public void TwoTypesWithOneNameFromDifferentAssembliesAreRefusedNamingBoth()
    {
        Type ghost = BuildGhostWidget();

        Assert.Equal(typeof(Widget).FullName, ghost.FullName);
        Assert.NotEqual(typeof(Widget).Assembly, ghost.Assembly);

        CompatibilityResult result = TypeCompatibility.Default.Check(ghost, typeof(Widget));

        Assert.Equal(PortCompatibility.Incompatible, result.Kind);
        Assert.Contains("GhostAssembly", result.Explanation, StringComparison.Ordinal);
        Assert.Contains("Spark.Engine.Tests", result.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same-name rule is tried before everything else, including the object-target catch-all, so
    /// that the specific message wins over the general one.
    /// </summary>
    [Fact]
    public void TheSameNameRuleIsTriedBeforeTheObjectTargetCatchAll()
    {
        Type ghost = BuildGhostWidget();

        CompatibilityResult result = TypeCompatibility.Default.Check(ghost, typeof(Widget), targetKeepsStructure: true);

        Assert.Equal(PortCompatibility.Incompatible, result.Kind);
    }

    private static Type BuildGhostWidget()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("GhostAssembly"), AssemblyBuilderAccess.Run);

        ModuleBuilder module = assembly.DefineDynamicModule("GhostModule");
        TypeBuilder type = module.DefineType(typeof(Widget).FullName!, TypeAttributes.Public | TypeAttributes.Class);

        return type.CreateType();
    }
}
