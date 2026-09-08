using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// A node that has been renamed still opens the graphs that name it the old way — `E3-T23`.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists so that `E2-T58` could happen at all.</b> A node's key is written into every
/// file that uses it, so renaming 57 factory methods from <c>By</c> to <c>From</c> would otherwise
/// have broken every graph anybody had already saved — including the three shipped examples.
/// `E3-T17` promises a graph outlives the process, and a rename without this breaks that promise
/// for every file at once.
/// </para>
/// <para>
/// <b>The failure it prevents is silent, which is what makes the tests worth reading.</b> An
/// unresolved key is not an exception: <c>MissingNodePolicy.Placeholder</c> substitutes a node
/// that keeps the wires, refuses to evaluate, and reports <c>SPK1062</c>. A user would see a graph
/// that opens, looks nearly right, and produces nothing.
/// </para>
/// </remarks>
public sealed class NodeAliasTests
{
    /// <summary>A node under its new name, answering to its old one.</summary>
    private static NodeDefinition Renamed() => new(
        new NodeKey("Test", "Circle.FromCentreRadius"),
        "Circle.FromCentreRadius",
        [new PortDefinition("radius", typeof(double), 0)],
        [new PortDefinition("circle", typeof(double), 0)],
        arguments => [arguments[0]],
        aliases: ["Circle.ByCentreRadius"]);

    /// <summary>
    /// <b>The old key resolves to the node that now carries the new name.</b> This is the whole
    /// mechanism: one lookup, consulted by every loader.
    /// </summary>
    [Fact]
    public void TheOldKeyFindsTheRenamedNode()
    {
        NodeLibrary library = new();
        library.Add(Renamed());

        Assert.True(library.TryGet(new NodeKey("Test", "Circle.ByCentreRadius"), out NodeDefinition? found));
        Assert.Equal("Circle.FromCentreRadius", found!.DisplayName);
    }

    /// <summary>And the new key still works, which is the case nobody would think to check.</summary>
    [Fact]
    public void TheNewKeyStillWorks()
    {
        NodeLibrary library = new();
        library.Add(Renamed());

        Assert.True(library.TryGet(new NodeKey("Test", "Circle.FromCentreRadius"), out NodeDefinition? found));
        Assert.Equal("Circle.FromCentreRadius", found!.DisplayName);
    }

    /// <summary>
    /// <b>A live node beats a dead name.</b> If a library renames <c>A</c> to <c>B</c> while
    /// something else is genuinely called <c>A</c>, the real <c>A</c> must win — otherwise a
    /// rename makes an unrelated node unreachable, which is a worse bug than the one aliases fix.
    /// </summary>
    [Fact]
    public void ARealNameBeatsAnAlias()
    {
        NodeLibrary library = new();

        library.Add(Renamed());
        library.Add(new NodeDefinition(
            new NodeKey("Test", "Circle.ByCentreRadius"),
            "Circle.ByCentreRadius",
            [new PortDefinition("radius", typeof(double), 0)],
            [new PortDefinition("circle", typeof(double), 0)],
            arguments => [arguments[0]]));

        Assert.True(library.TryGet(new NodeKey("Test", "Circle.ByCentreRadius"), out NodeDefinition? found));
        Assert.Equal("Circle.ByCentreRadius", found!.DisplayName);
    }

    /// <summary>
    /// <b>The package still has to match.</b> An alias names the member half of the key only, so a
    /// node in one package cannot claim a name in another — which is what a full-key alias would
    /// have allowed, and would have made a third-party package able to hijack a built-in.
    /// </summary>
    [Fact]
    public void AnAliasDoesNotCrossPackages()
    {
        NodeLibrary library = new();
        library.Add(Renamed());

        Assert.False(
            library.TryGet(new NodeKey("Other", "Circle.ByCentreRadius"), out _),
            "an alias answered for a package it does not belong to");
    }

    /// <summary>
    /// <b>A file naming the old key opens with a real node, wires intact</b> — the end-to-end
    /// claim, through the format rather than through the library directly.
    /// </summary>
    [Fact]
    public void AFileNamingTheOldKeyStillOpens()
    {
        NodeLibrary library = new();
        library.Add(Renamed());

        const string Text = """
            {
              "formatVersion": 1,
              "nodes": [
                {
                  "id": "154973cf-8779-eba9-6c9d-c2581f629ea3",
                  "key": "Test/Circle.ByCentreRadius",
                  "lacing": "Auto",
                  "x": 10,
                  "y": 20
                }
              ],
              "wires": []
            }
            """;

        Graph graph = SparkFile.Read(Text).Restore(library);
        NodeInstance node = graph.Nodes().Single();

        Assert.Equal("Circle.FromCentreRadius", node.Definition.DisplayName);
    }

    /// <summary>
    /// <b>And saving it writes the new key, so the file heals itself.</b> The alias is a bridge
    /// rather than a permanent second name: a graph opened once and saved once no longer needs it.
    /// </summary>
    [Fact]
    public void SavingAnOpenedFileWritesTheNewKey()
    {
        NodeLibrary library = new();
        library.Add(Renamed());

        const string Text = """
            {
              "formatVersion": 1,
              "nodes": [
                {
                  "id": "154973cf-8779-eba9-6c9d-c2581f629ea3",
                  "key": "Test/Circle.ByCentreRadius",
                  "lacing": "Auto",
                  "x": 10,
                  "y": 20
                }
              ],
              "wires": []
            }
            """;

        Graph graph = SparkFile.Read(Text).Restore(library);
        string saved = SparkFile.Write(GraphDocument.Capture(graph, _ => (10, 20)));

        Assert.Contains("Test/Circle.FromCentreRadius", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("Test/Circle.ByCentreRadius", saved, StringComparison.Ordinal);
    }

    /// <summary>A node with no history carries none, which is almost all of them.</summary>
    [Fact]
    public void MostNodesHaveNoAliases() =>
        Assert.Empty(new NodeDefinition(
            new NodeKey("Test", "Thing.Make"),
            "Thing.Make",
            [],
            [new PortDefinition("value", typeof(double), 0)],
            _ => [1.0]).Aliases);
}
