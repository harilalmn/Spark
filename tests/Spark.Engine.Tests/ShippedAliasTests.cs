using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// Every alias the shipped library declares actually opens a file that names it — `E2-T60`.
/// </summary>
/// <remarks>
/// <para>
/// <b>`NodeAliasTests` proves the mechanism; this proves the wiring.</b> Those tests build a
/// two-node library out of fixtures and check that a lookup falls back correctly. Nothing checked
/// that the aliases on the <i>real</i> library resolve — and an alias that resolves to nothing is
/// silent in the worst way. `MissingNodePolicy.Placeholder` keeps the wires, refuses to evaluate
/// and reports `SPK1062`, so the user opens a saved graph, sees something that looks nearly right,
/// and gets nothing out of it.
/// </para>
/// <para>
/// <b>Written because the proof it replaces was disposable.</b> `E2-T58` and `E2-T60` each proved
/// their aliases by opening `docs/examples/*.spark` unedited, which was real evidence and lasted
/// exactly until those files were healed to the new keys. The next rename would have had nothing
/// standing behind it. This runs on every build instead.
/// </para>
/// </remarks>
public sealed class ShippedAliasTests
{
    private static readonly Assembly CoreNodes = typeof(Spark.Nodes.Core.Point).Assembly;

    private static NodeLibrary Library()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(CoreNodes));
        return library;
    }

    /// <summary>Every alias declared anywhere in the library, with the member that declares it.</summary>
    private static IEnumerable<(string Member, string Alias)> Declared() =>
        from type in CoreNodes.GetExportedTypes()
        from member in type.GetMembers(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        from alias in member.GetCustomAttributes<SparkNodeAliasAttribute>()
        select ($"{type.Name}.{member.Name}", alias.Name);

    /// <summary>
    /// <b>Every alias resolves.</b> A graph saved before the rename opens, and opens onto the node
    /// the rename produced rather than onto a placeholder.
    /// </summary>
    [Fact]
    public void EveryDeclaredAliasResolvesToALiveNode()
    {
        NodeLibrary library = Library();
        List<string> dead = [];

        foreach ((string member, string alias) in Declared())
        {
            if (!library.TryGet(new NodeKey("Spark.Nodes.Core", alias), out _))
            {
                dead.Add($"{alias} (on {member})");
            }
        }

        Assert.True(
            dead.Count == 0,
            "Aliases that resolve to nothing — every graph naming one degrades to a placeholder: "
            + string.Join(", ", dead));
    }

    /// <summary>
    /// <b>No alias shadows a live node.</b> Aliases are consulted only after the real index misses,
    /// so an alias sharing a name with a real node is dead text rather than a hijack — but it is
    /// dead text that reads like a promise, and the next reader would believe it.
    /// </summary>
    [Fact]
    public void NoAliasCollidesWithARealNodeName()
    {
        HashSet<string> real = [.. NodeImporter.Import(CoreNodes).Nodes
            .Select(node => node.Definition.Key.Name)];

        List<string> shadowed = [.. Declared()
            .Where(pair => real.Contains(pair.Alias))
            .Select(pair => $"{pair.Alias} (on {pair.Member})")];

        Assert.True(
            shadowed.Count == 0,
            $"Aliases that name a live node and can never be reached: {string.Join(", ", shadowed)}.");
    }

    /// <summary>
    /// <b>The renames the client asked for are named here in full, rather than only counted.</b>
    /// `E2-T58` turned <c>By</c> into <c>From</c> and `E2-T60` turned <c>Centre</c> into
    /// <c>Center</c>, so a circle node has been called three things; every file that ever named it
    /// still opens.
    /// </summary>
    [Theory]
    [InlineData("Circle.ByCentreRadius", "Circle.FromCenterRadius")]
    [InlineData("Circle.FromCentreRadius", "Circle.FromCenterRadius")]
    [InlineData("Circle.ByCentreNormalRadius", "Circle.FromCenterNormalRadius")]
    [InlineData("Circle.FromCentreNormalRadius", "Circle.FromCenterNormalRadius")]
    [InlineData("Arc.ByCentreStartPointSweepAngle", "Arc.FromCenterStartPointSweepAngle")]
    [InlineData("Arc.FromCentreStartPointSweepAngle", "Arc.FromCenterStartPointSweepAngle")]
    [InlineData("BoundingBox.Centre", "BoundingBox.Center")]
    [InlineData("Circle.ByThreePoints", "Circle.FromThreePoints")]
    public void AnOldKeyOpensOntoTheNodeThatReplacedIt(string old, string now)
    {
        Assert.True(
            Library().TryGet(new NodeKey("Spark.Nodes.Core", old), out NodeDefinition? found),
            $"{old} no longer opens anything.");

        Assert.Equal(now, found!.Key.Name);
    }
}
