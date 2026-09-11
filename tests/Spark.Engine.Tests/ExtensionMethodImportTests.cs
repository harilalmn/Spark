using System.Linq;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// Extension methods are shown on the type they extend (`E5-T9`).
/// </summary>
/// <remarks>
/// <b>So a package's extensions look native</b>, which is the row's reason for existing: a user
/// looking for something to do to a widget looks under <c>Widget</c>, not under whatever static
/// class the package author happened to put the method in.
/// </remarks>
public sealed class ExtensionMethodImportTests
{
    private static readonly ImportReport Report = NodeImporter.Import(
        [typeof(ExtensionWidget), typeof(ExtensionWidgetExtensions), typeof(CollidingWidget), typeof(CollidingWidgetExtensions)],
        "Fixtures");

    private static NodeDefinition Node(string name) =>
        Assert.Single(Report.Nodes, node => node.Definition.Key.Name == name).Definition;

    /// <summary>
    /// <b>The row.</b> The extension is a node under its receiver's name and nowhere else, and it is
    /// no longer counted among the exclusions.
    /// </summary>
    [Fact]
    public void AnExtensionIsShownOnTheTypeItExtends()
    {
        Node("ExtensionWidget.Doubled");

        Assert.DoesNotContain(Report.Nodes, node => node.Definition.Key.Name.StartsWith("ExtensionWidgetExtensions.", System.StringComparison.Ordinal));
        Assert.DoesNotContain(Report.Exclusions, exclusion => exclusion.Member.Name == nameof(ExtensionWidgetExtensions.Doubled));
    }

    /// <summary>Its receiver is its first input, under the author's name for it, and it runs.</summary>
    [Fact]
    public void ItsReceiverIsItsFirstInputAndItRuns()
    {
        NodeDefinition doubled = Node("ExtensionWidget.Doubled");

        Assert.Equal("widget", doubled.Inputs[0].Name);
        Assert.Equal(typeof(ExtensionWidget), doubled.Inputs[0].ValueType);

        Graph graph = new();
        NodeInstance node = graph.AddNode(doubled);
        graph.SetLiteral(node.Id, 0, new ExtensionWidget(2.5));

        EvaluationResult result = GraphEvaluator.Evaluate(graph, new EvaluationContext(), TestContext.Current.CancellationToken);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(5.0, result.Value(node.Id));
    }

    /// <summary>
    /// An extension named like a member the receiver declares is kept apart from it by the ordinary
    /// overload rule - its port names - so neither hides the other and neither depends on order.
    /// </summary>
    [Fact]
    public void AnExtensionCollidingWithTheReceiversOwnMemberIsKeptApart()
    {
        string[] keys = [.. Report.Nodes
            .Select(node => node.Definition.Key.Name)
            .Where(name => name.StartsWith("CollidingWidget.Area", System.StringComparison.Ordinal))
            .Order(System.StringComparer.Ordinal)];

        Assert.Equal(2, keys.Length);
        Assert.Equal(["CollidingWidget.Area(collidingWidget)", "CollidingWidget.Area(widget)"], keys);
    }
}

/// <summary>A value a package might extend.</summary>
public sealed class ExtensionWidget
{
    /// <summary>Creates a widget.</summary>
    /// <param name="size">Its size.</param>
    public ExtensionWidget(double size) => Size = size;

    /// <summary>Its size.</summary>
    public double Size { get; }
}

/// <summary>A package's extensions to <see cref="ExtensionWidget"/>.</summary>
public static class ExtensionWidgetExtensions
{
    /// <summary>Twice a widget's size.</summary>
    /// <param name="widget">The widget.</param>
    /// <returns>Twice its size.</returns>
    public static double Doubled(this ExtensionWidget widget) => widget.Size * 2.0;
}

/// <summary>A type whose own member an extension shares a name with.</summary>
public sealed class CollidingWidget
{
    /// <summary>Its side.</summary>
    public double Side { get; } = 1.0;

    /// <summary>Its own area.</summary>
    /// <returns>Its side squared.</returns>
    public double Area() => Side * Side;
}

/// <summary>An extension named like a member of <see cref="CollidingWidget"/>.</summary>
public static class CollidingWidgetExtensions
{
    /// <summary>An area computed elsewhere.</summary>
    /// <param name="widget">The widget.</param>
    /// <returns>Two.</returns>
    public static double Area(this CollidingWidget widget) => 2.0;
}
