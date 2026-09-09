using System;
using System.Linq;
using Spark.Api;
using Spark.Engine;
using Spark.Scripting;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// The graph telling the factory about its blocks — `E6-T36`.
/// </summary>
/// <remarks>
/// <para>
/// <b>`E6-T35` built the shared assembly and gave it no callers</b>, so until these paths existed
/// the client's two-block arrangement still failed in the application while passing in the
/// scripting tests. What is covered here is the seam between the two: a graph that knows its blocks
/// telling a factory that knows how to compile them.
/// </para>
/// <para>
/// <b>The tests that matter are the ones about a block nobody edited.</b> Making the declaring
/// block and the consuming block work together is the easy half; the half that a plausible
/// implementation gets wrong is what happens to the consumer when the declaration is renamed or
/// deleted — it has not changed a character, so nothing rebuilds it unless something goes looking.
/// </para>
/// </remarks>
public sealed class SharedDeclarationsOnTheCanvasTests
{
    private const string Declares = """
        public class Helper
        {
            public static double Twice(double x) => x * 2;
        }
        var placed = 1;
        """;

    private const string Uses = """
        var doubled = Helper.Twice(21);
        """;

    private static CanvasGraph Graph()
    {
        // The catalogue is built from what this process has loaded; naming a geometry type loads
        // the assembly the prelude imports, or every script here fails for an unrelated reason.
        _ = typeof(Spark.Geometry.Point3d).Assembly.Location;

        return new CanvasGraph { Scripts = new ScriptNodeFactory() };
    }

    /// <summary>Places a block the way <c>PlaceCodeBlock</c> does: with whatever is known now.</summary>
    private static NodeId Add(CanvasGraph graph, string source)
    {
        int slot = graph.Add(
            NodeDefinition.FromScript(graph.Scripts!.Create(source), source), 0, 0);

        return graph.Nodes[slot].Id;
    }

    /// <summary>
    /// What <c>EvaluateAsync</c> does before a run: share, and rebuild if the set moved.
    /// </summary>
    /// <returns>How many blocks were rebuilt.</returns>
    private static int Settle(CanvasGraph graph) =>
        graph.ShareDeclarations() ? graph.RebuildScripts() : 0;

    private static object? Run(CanvasGraph graph, NodeId id) =>
        graph.Engine.Node(id).Definition.Invoke([]).FirstOrDefault();

    /// <summary>
    /// <b>The client's arrangement, on a canvas.</b> One block declares a type, the block beside it
    /// calls it, and the second one runs.
    /// </summary>
    [Fact]
    public void ATypeDeclaredInOneBlockIsUsableByAnotherOnTheSameCanvas()
    {
        CanvasGraph graph = Graph();

        _ = Add(graph, Declares);
        NodeId consumer = Add(graph, Uses);

        // The consumer was placed before anything had been shared, so at this instant it is a
        // block that does not compile - which is exactly what the client saw.
        Assert.Throws<InvalidOperationException>(() => Run(graph, consumer));

        // Both move: the declarations left the declaring block's own assembly, and the consumer
        // gained one it could not see.
        Assert.Equal(2, Settle(graph));

        Assert.Equal(42.0, Run(graph, graph.Nodes.Single(node => node.Script == Uses).Id));
    }

    /// <summary>
    /// <b>Renaming the declared type breaks the block that used it, and that block was not
    /// edited.</b> This is the assertion an implementation that rebuilds only the edited node
    /// fails: the consumer goes on holding a definition compiled against a class that no longer
    /// exists, still green, until something unrelated happens to touch it.
    /// </summary>
    [Fact]
    public void RenamingADeclaredTypeRebuildsTheBlockThatUsedIt()
    {
        CanvasGraph graph = Graph();

        NodeId declarer = Add(graph, Declares);
        _ = Add(graph, Uses);

        _ = Settle(graph);

        Assert.Equal(42.0, Run(graph, graph.Nodes.Single(node => node.Script == Uses).Id));

        const string renamed = """
            public class Assistant
            {
                public static double Twice(double x) => x * 2;
            }
            var placed = 1;
            """;

        int slot = graph.Nodes.ToList().FindIndex(node => node.Id == declarer);

        // What `CommitScript` does: share the uncommitted text, replace the edited block, then
        // rebuild the rest.
        Assert.True(graph.ShareDeclarations(declarer, renamed));
        Assert.True(graph.ReplaceDefinition(
            graph.Nodes[slot],
            NodeDefinition.FromScript(graph.Scripts!.Create(renamed), renamed)));

        // One: the block nobody edited. The declarer has already been replaced above, so its key
        // matches and it is left where it is.
        Assert.Equal(1, graph.RebuildScripts());

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => Run(graph, graph.Nodes.Single(node => node.Script == Uses).Id));

        Assert.Contains("Helper", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Deleting the declaring block does the same</b>, and it is the case that shows why the
    /// catch-all before a run is not optional: a deletion is not an edit to any script, so nothing
    /// on the editing path would ever have noticed it.
    /// </summary>
    [Fact]
    public void DeletingTheDeclaringBlockRebuildsTheBlockThatUsedIt()
    {
        CanvasGraph graph = Graph();

        NodeId declarer = Add(graph, Declares);
        _ = Add(graph, Uses);

        _ = Settle(graph);

        Assert.Equal(42.0, Run(graph, graph.Nodes.Single(node => node.Script == Uses).Id));

        Assert.NotNull(graph.Remove(graph.Nodes.ToList().FindIndex(node => node.Id == declarer)));

        Assert.Equal(1, Settle(graph));

        Assert.Throws<InvalidOperationException>(
            () => Run(graph, graph.Nodes.Single(node => node.Script == Uses).Id));
    }

    /// <summary>
    /// <b>The uncommitted text is what the editor is answered about.</b> Somebody halfway through
    /// typing a class must not be underlined for using the type they are looking at.
    /// </summary>
    [Fact]
    public void TheUncommittedTextIsWhatIsShared()
    {
        CanvasGraph graph = Graph();

        NodeId block = Add(graph, "var placed = 1;");

        Assert.True(graph.ShareDeclarations(block, Declares));
        Assert.Contains(Declares, graph.BlockSources(block, Declares));
        Assert.True(graph.Scripts is ScriptNodeFactory { Declarations.IsShared: true });
    }

    /// <summary>
    /// <b>A wire landing shares nothing, and that is correct rather than forgotten.</b> Connecting
    /// cannot change what any block declares, and the definition's key already carries the shared
    /// set's fingerprint — so `Retype` asking "did the key move" is already asking the right
    /// question, and calling `Share` on every connect would re-parse every block in the graph to
    /// compute a fingerprint that cannot have changed.
    /// </summary>
    [Fact]
    public void ConnectingAWireDoesNotMoveTheSharedSet()
    {
        CanvasGraph graph = Graph();

        _ = Add(graph, Declares);
        _ = Add(graph, "var trebled = feed * 3;");

        _ = Settle(graph);

        string before = ((ScriptNodeFactory)graph.Scripts!).Declarations.Fingerprint;

        Assert.False(graph.ShareDeclarations());
        Assert.Equal(before, ((ScriptNodeFactory)graph.Scripts).Declarations.Fingerprint);
    }
}
