using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// Generic methods closed over <see cref="object"/>, the rule `E5-T3` wrote down (`E5-T10`).
/// </summary>
/// <remarks>
/// <b>Imported when a canvas can use them, refused with a reason when it cannot.</b> An unconstrained
/// type parameter that appears in an input is closed over object - a graph's lists are object-based -
/// and runs; a constrained one, or one no input mentions, is refused and says why.
/// </remarks>
public sealed class GenericMethodImportTests
{
    private static readonly ImportReport Report = NodeImporter.Import([typeof(GenericFixtures)], "Fixtures");

    private static NodeDefinition Node(string name) =>
        Assert.Single(Report.Nodes, node => node.Definition.Key.Name == name).Definition;

    private static object? Run(NodeDefinition definition, params object?[] arguments)
    {
        Graph graph = new();
        NodeInstance node = graph.AddNode(definition);

        for (int i = 0; i < arguments.Length; i++)
        {
            graph.SetLiteral(node.Id, i, arguments[i]);
        }

        EvaluationResult result = GraphEvaluator.Evaluate(graph, new EvaluationContext(), TestContext.Current.CancellationToken);

        Assert.Empty(result.Diagnostics);
        return result.Value(node.Id);
    }

    /// <summary>
    /// <b>The row.</b> An identity-like method over a list is imported, closed over object, and reverses
    /// a list of numbers as it would a list of anything.
    /// </summary>
    [Fact]
    public void AnIdentityLikeGenericMethodIsImportedAndRuns()
    {
        NodeDefinition reversed = Node("GenericFixtures.Reversed");

        Assert.Equal(typeof(IReadOnlyList<object>), reversed.Inputs[0].ValueType);

        object?[] values = [.. Assert.IsType<SparkList>(Run(reversed, SparkList.Of(1.0, 2.0, 3.0)))];

        Assert.Equal(new object?[] { 3.0, 2.0, 1.0 }, values);
    }

    /// <summary>A method returning its type parameter hands back the value itself.</summary>
    [Fact]
    public void AMethodReturningItsTypeParameterHandsBackTheValue()
    {
        Assert.Equal(7.0, Run(Node("GenericFixtures.First"), SparkList.Of(7.0, 8.0)));
    }

    /// <summary>A constrained type parameter is refused, and the reason names it.</summary>
    [Fact]
    public void AConstrainedTypeParameterIsRefusedByName()
    {
        ExcludedMember larger = Assert.Single(Report.Exclusions, exclusion => exclusion.Member.Name == nameof(GenericFixtures.Larger));

        Assert.Contains("constrained", larger.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(Report.Nodes, node => node.Definition.Key.Name == "GenericFixtures.Larger");
    }

    /// <summary>A type parameter no input mentions is refused: nothing on a canvas could decide it.</summary>
    [Fact]
    public void ATypeParameterNoInputMentionsIsRefused()
    {
        ExcludedMember make = Assert.Single(Report.Exclusions, exclusion => exclusion.Member.Name == nameof(GenericFixtures.Make));

        Assert.Contains("appears in no input", make.Reason, StringComparison.Ordinal);
    }
}

/// <summary>Generic methods of each kind the rule distinguishes.</summary>
public static class GenericFixtures
{
    /// <summary>The items in reverse order.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items.</param>
    /// <returns>A new list.</returns>
    public static IReadOnlyList<T> Reversed<T>(IReadOnlyList<T> items) => [.. items.Reverse()];

    /// <summary>The first item.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items.</param>
    /// <returns>The first.</returns>
    public static T First<T>(IReadOnlyList<T> items) => items[0];

    /// <summary>The larger of two values.</summary>
    /// <typeparam name="T">A comparable type.</typeparam>
    /// <param name="a">One value.</param>
    /// <param name="b">The other.</param>
    /// <returns>The larger.</returns>
    public static T Larger<T>(T a, T b)
        where T : IComparable<T> => a.CompareTo(b) >= 0 ? a : b;

    /// <summary>A default value of a type nothing names.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>Its default.</returns>
    public static T? Make<T>() => default;
}
