using System;
using System.Linq;
using System.Reflection;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The curated List, Logic and String categories — `E5-T13`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The load-bearing test here is <see cref="EveryListNodeSeesTheWholeList"/>.</b> A list port
/// that replicates is handed one item at a time, so <c>List.Count</c> would answer 1 for every
/// element instead of the length once — and it would do so silently, producing a list of ones
/// where a number was expected. Every input in the List category is <c>[KeepStructure]</c> for
/// that reason, and this asserts it through the engine rather than by reading the source.
/// </para>
/// <para>
/// Everything else is behaviour: the nodes do what their names say, and the ones that can be asked
/// something impossible say so rather than returning a plausible wrong answer.
/// </para>
/// </remarks>
public sealed class CuratedNodeTests
{
    private static readonly NodeLibrary Library = BuildLibrary();

    /// <summary>
    /// <b>The reason the category exists.</b> A list node handed a list must see the list, not its
    /// items one at a time.
    /// </summary>
    [Fact]
    public void EveryListNodeSeesTheWholeList()
    {
        SparkList five = SparkList.Of(10.0, 20.0, 30.0, 40.0, 50.0);

        Assert.Equal(5, Run("List.Count", five));
        Assert.Equal(10.0, Run("List.FirstItem", five));
        Assert.Equal(50.0, Run("List.LastItem", five));
    }

    [Fact]
    public void ItemsAreFetchedByIndexAndNegativeIndicesCountBack()
    {
        SparkList five = SparkList.Of(10.0, 20.0, 30.0, 40.0, 50.0);

        Assert.Equal(30.0, Run("List.GetItemAtIndex", five, 2));
        Assert.Equal(50.0, Run("List.GetItemAtIndex", five, -1));
        Assert.Equal(10.0, Run("List.GetItemAtIndex", five, -5));
    }

    [Fact]
    public void AnIndexOutsideTheListIsReportedRatherThanGuessed()
    {
        SparkList three = SparkList.Of(1.0, 2.0, 3.0);

        Assert.True(Failed("List.GetItemAtIndex", three, 3));
        Assert.True(Failed("List.GetItemAtIndex", three, -4));
    }

    [Fact]
    public void ReverseJoinAndTakeDoWhatTheySay()
    {
        SparkList a = SparkList.Of(1.0, 2.0, 3.0);
        SparkList b = SparkList.Of(4.0, 5.0);

        Assert.Equal([3.0, 2.0, 1.0], Items(Run("List.Reverse", a)));
        Assert.Equal([1.0, 2.0, 3.0, 4.0, 5.0], Items(Run("List.Join", a, b)));
        Assert.Equal([1.0, 2.0], Items(Run("List.TakeItems", a, 2)));
        Assert.Equal([2.0, 3.0], Items(Run("List.TakeItems", a, -2)));
    }

    [Fact]
    public void FlattenTakesEveryLeafInOrder()
    {
        SparkList nested = SparkList.Of(
            SparkList.Of(1.0, 2.0), SparkList.Of(SparkList.Of(3.0), 4.0), 5.0);

        Assert.Equal([1.0, 2.0, 3.0, 4.0, 5.0], Items(Run("List.Flatten", nested)));
    }

    [Fact]
    public void UniqueItemsComparesByValue()
    {
        SparkList repeated = SparkList.Of(1.0, 2.0, 1.0, 3.0, 2.0);

        Assert.Equal([1.0, 2.0, 3.0], Items(Run("List.UniqueItems", repeated)));
    }

    /// <summary>
    /// A single value is promoted to a list of one rather than refused — a graph that produces one
    /// item where it usually produces several is an ordinary Tuesday, and failing there would make
    /// every list node a source of intermittent errors.
    /// </summary>
    [Fact]
    public void ASingleValueIsTreatedAsAListOfOne()
    {
        Assert.Equal(1, Run("List.Count", 42.0));
        Assert.Equal(42.0, Run("List.FirstItem", 42.0));
    }

    [Fact]
    public void LogicNodesDoWhatTheySay()
    {
        Assert.Equal(true, Run("Logic.And", true, true));
        Assert.Equal(false, Run("Logic.And", true, false));
        Assert.Equal(true, Run("Logic.Or", false, true));
        Assert.Equal(false, Run("Logic.Not", true));
        Assert.Equal(true, Run("Logic.LessThan", 1.0, 2.0));
        Assert.Equal(true, Run("Logic.GreaterThan", 2.0, 1.0));
    }

    /// <summary>
    /// <b>Equality takes a tolerance and the default is not zero.</b> Two doubles from different
    /// arithmetic are almost never bitwise equal, and a node answering false to
    /// <c>0.1 + 0.2 == 0.3</c> would be technically correct and useless.
    /// </summary>
    [Fact]
    public void EqualityIsToleranced()
    {
        Assert.Equal(true, Run("Logic.Equal", 0.1 + 0.2, 0.3));
        Assert.Equal(false, Run("Logic.Equal", 1.0, 1.1));
    }

    [Fact]
    public void IfChoosesBetweenTwoValues()
    {
        Assert.Equal(7.0, Run("Logic.If", true, 7.0, 9.0));
        Assert.Equal(9.0, Run("Logic.If", false, 7.0, 9.0));
    }

    /// <summary>
    /// Text is rendered and read in the invariant culture, so a graph produces the same file
    /// wherever it runs — which is what ADR-0017 bought by choosing text.
    /// </summary>
    [Fact]
    public void NumbersRenderAndParseInTheInvariantCulture()
    {
        Assert.Equal("3.142", Run("String.FromNumber", System.Math.PI, 3));
        Assert.Equal("3", Run("String.FromNumber", System.Math.PI, 0));
        Assert.Equal(2.5, Run("String.ToNumber", "2.5"));
    }

    /// <summary>Unparseable text is reported rather than turned into zero.</summary>
    [Fact]
    public void TextThatIsNotANumberIsReported()
    {
        Assert.True(Failed("String.ToNumber", "not a number"));
    }

    [Fact]
    public void TextNodesDoWhatTheySay()
    {
        Assert.Equal("abcdef", Run("String.Concat", "abc", "def"));
        Assert.Equal(3, Run("String.Length", "abc"));
        Assert.Equal("a, b, c", Run("String.JoinList", SparkList.Of("a", "b", "c"), ", "));
    }

    /// <summary>
    /// The category names are the curated ones, so the library panel groups these where a user
    /// expects to find them rather than under <c>Custom</c>.
    /// </summary>
    [Fact]
    public void TheCuratedNodesAreInTheCuratedCategories()
    {
        Assert.Equal(NodeCategories.List, Library.ByName("List.Count").Category);
        Assert.Equal(NodeCategories.Logic, Library.ByName("Logic.And").Category);
        Assert.Equal(NodeCategories.Math, Library.ByName("Math.Add").Category);
        Assert.Equal(NodeCategories.Input, Library.ByName("String.Value").Category);
    }

    private static object? Run(string name, params object?[] arguments)
    {
        Graph graph = new();
        NodeInstance node = graph.AddNode(Library.ByName(name));

        for (int i = 0; i < arguments.Length; i++)
        {
            graph.SetLiteral(node.Id, i, arguments[i]);
        }

        EvaluationResult result = GraphEvaluator.Evaluate(
            graph, new EvaluationContext(), TestContext.Current.CancellationToken);

        Assert.Empty(result.Diagnostics);
        return result.Value(node.Id);
    }

    private static bool Failed(string name, params object?[] arguments)
    {
        Graph graph = new();
        NodeInstance node = graph.AddNode(Library.ByName(name));

        for (int i = 0; i < arguments.Length; i++)
        {
            graph.SetLiteral(node.Id, i, arguments[i]);
        }

        EvaluationResult result = GraphEvaluator.Evaluate(
            graph, new EvaluationContext(), TestContext.Current.CancellationToken);

        return result.HasErrors;
    }

    private static object?[] Items(object? value)
    {
        SparkList list = Assert.IsType<SparkList>(value);

        return [.. Enumerable.Range(0, list.Count).Select(i => list[i])];
    }

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(Assembly.Load("Spark.Nodes.Core")));
        return library;
    }
}
