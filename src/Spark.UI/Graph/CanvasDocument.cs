using System;
using System.Collections.Generic;
using Spark.Engine;

namespace Spark.UI.Graph;

/// <summary>
/// Moves a <see cref="CanvasGraph"/> to and from the text of a `.spark` file.
/// </summary>
/// <remarks>
/// <para>
/// The engine's <see cref="GraphDocument"/> already carries canvas coordinates without the engine
/// ever reading them. This type is the other half of that arrangement: it is the only place that
/// knows a node's position is a canvas concern, and it is thirty lines rather than a layer.
/// </para>
/// <para>
/// It deals in text rather than in paths, so that a test can round-trip a graph without touching a
/// disk and so that the file dialogs stay in the view where they belong.
/// </para>
/// </remarks>
public static class CanvasDocument
{
    /// <summary>Writes a canvas graph as the text of a `.spark` file.</summary>
    /// <param name="graph">The graph, including where its nodes sit.</param>
    /// <returns>Canonically formatted JSON.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    /// <exception cref="SparkFileException">A port holds a value the format cannot represent.</exception>
    public static string Save(CanvasGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        Dictionary<NodeId, (double X, double Y)> positions = [];
        foreach (CanvasNode node in graph.Nodes)
        {
            positions[node.Id] = (node.X, node.Y);
        }

        return SparkFile.Write(GraphDocument.Capture(
            graph.Engine,
            id => positions.TryGetValue(id, out (double X, double Y) at) ? at : (0.0, 0.0)));
    }

    /// <summary>Reads the text of a `.spark` file into a canvas graph.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="library">The library to bind the file's nodes against.</param>
    /// <returns>The graph, with every node back where it was.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <exception cref="SparkFileException">
    /// The text is not a graph, is from a newer format version, or names a node that is not loaded.
    /// </exception>
    public static CanvasGraph Open(string text, NodeLibrary library)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(library);

        GraphDocument document = SparkFile.Read(text);
        Spark.Engine.Graph engine = document.Restore(library);
        CanvasGraph graph = new(engine);

        // Adopted in the document's order, which is sorted by identity — so the draw order of a
        // reopened graph is stable rather than an accident of how it was built the first time.
        foreach (GraphDocumentNode node in document.Nodes)
        {
            graph.Adopt(engine.Node(node.Id), node.X, node.Y);
        }

        return graph;
    }
}
