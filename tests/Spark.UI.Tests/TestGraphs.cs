using System.Reflection;
using Spark.Engine;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// Graphs for the UI tests, built from the real imported library rather than from stand-ins.
/// </summary>
/// <remarks>
/// The canvas used to be exercised against a placeholder model whose wires were always accepted and
/// whose ports had no types. Building these from <c>Spark.Nodes.Core</c> through the importer means
/// a gesture test is now also a test that the shell and the engine agree about what a node is —
/// which is the seam the walking skeleton exists to prove.
/// </remarks>
internal static class TestGraphs
{
    private static readonly NodeLibrary Shared = BuildLibrary();

    /// <summary>The imported library the UI tests draw from.</summary>
    internal static NodeLibrary Library => Shared;

    /// <summary>
    /// Two nodes side by side: a source with one output at the origin, and a sink with one input
    /// 300 units to its right. The shape every pointer-gesture test is written against.
    /// </summary>
    internal static CanvasGraph SourceAndSink()
    {
        CanvasGraph graph = new();
        graph.Add(Shared.ByName("Number.Value"), 0, 0);
        graph.Add(Shared.ByName("Math.Sin"), 300, 0);
        return graph;
    }

    /// <summary>The seeded demo graph.</summary>
    internal static CanvasGraph Demo() => DemoGraphs.Demo(Shared);

    /// <summary>A synthetic graph of a given size.</summary>
    internal static CanvasGraph Synthetic(int nodeCount) => DemoGraphs.Synthetic(Shared, nodeCount);

    private static NodeLibrary BuildLibrary()
    {
        // Reached by name rather than by a project reference, because Spark.UI.Tests references
        // Spark.UI and the node library arrives through Spark.Host - the same route the running
        // application uses.
        Assembly nodes = Assembly.Load("Spark.Nodes.Core");
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(nodes));
        return library;
    }
}
