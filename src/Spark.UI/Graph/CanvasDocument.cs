using System;
using System.Collections.Generic;
using Spark.Api;
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

        List<GraphDocumentNote> notes = [];
        foreach (CanvasNote note in graph.Notes)
        {
            notes.Add(new GraphDocumentNote(
                note.Id, note.X, note.Y, note.Width, note.Height, note.Text));
        }

        List<GraphDocumentGroup> groups = [];
        foreach (CanvasGroup group in graph.Groups)
        {
            groups.Add(new GraphDocumentGroup(group.Id, group.Title, [.. group.Members]));
        }

        return SparkFile.Write(GraphDocument.Capture(
            graph.Engine,
            id => positions.TryGetValue(id, out (double X, double Y) at) ? at : (0.0, 0.0),
            notes,
            groups));
    }

    /// <summary>Reads the text of a `.spark` file into a canvas graph.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="library">The library to bind the file's nodes against.</param>
    /// <param name="scripts">
    /// How a code block's source becomes a definition, or <see langword="null"/> when scripting is
    /// off — in which case a document containing one is refused rather than opened without it.
    /// </param>
    /// <returns>The graph, with every node back where it was.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <exception cref="SparkFileException">
    /// The text is not a graph, is from a newer format version, or names a node that is not loaded.
    /// </exception>
    public static CanvasGraph Open(string text, NodeLibrary library, IScriptNodeFactory? scripts = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(library);

        GraphDocument document = SparkFile.Read(text);
        Spark.Engine.Graph engine = document.Restore(library, scripts);
        CanvasGraph graph = new(engine);

        // Adopted in the document's order, which is sorted by identity — so the draw order of a
        // reopened graph is stable rather than an accident of how it was built the first time.
        foreach (GraphDocumentNode node in document.Nodes)
        {
            graph.Adopt(engine.Node(node.Id), node.X, node.Y);
        }

        // Notes keep the identity they were saved with, so that re-saving a file that was only
        // opened produces no diff at all. A fresh Guid here would rewrite every note's id line
        // every time the file was touched.
        foreach (GraphDocumentNote note in document.Notes)
        {
            graph.AdoptNote(new CanvasNote(note.Id)
            {
                X = note.X,
                Y = note.Y,
                Width = note.Width,
                Height = note.Height,
                Text = note.Text,
            });
        }

        // Groups keep their identity for the reason notes do, and their membership is taken as
        // written rather than re-derived: a member the graph no longer has is simply not found
        // when the frame is measured, which is what makes a hand-edited file survive.
        foreach (GraphDocumentGroup group in document.Groups)
        {
            CanvasGroup restored = new(group.Id) { Title = group.Title };
            foreach (NodeId member in group.Members)
            {
                restored.Add(member);
            }

            graph.AdoptGroup(restored);
        }

        return graph;
    }
}
