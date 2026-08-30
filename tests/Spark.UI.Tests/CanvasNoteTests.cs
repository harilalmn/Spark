using System;
using System.IO;
using System.Linq;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// Notes as the canvas holds them, and as they survive a file.
/// </summary>
/// <remarks>
/// <c>GraphNoteTests</c> in the engine suite owns the format and the version rule. What is left
/// here is the shell's half: that a note is kept beside the nodes rather than among them, and that
/// the identity a file was saved with is the identity it opens with — without which a graph that
/// was only opened and saved again would produce a diff of every note in it.
/// </remarks>
public sealed class CanvasNoteTests
{
    [Fact]
    public void ANoteRoundTripsThroughAFileWithItsIdentityIntact()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        CanvasNote note = graph.AddNote(140, -60, "Radii are in millimetres.");
        note.Width = 320;
        note.Height = 140;

        CanvasGraph reopened = CanvasDocument.Open(CanvasDocument.Save(graph), TestGraphs.Library);

        CanvasNote restored = Assert.Single(reopened.Notes);
        Assert.Equal(note.Id, restored.Id);
        Assert.Equal(140, restored.X);
        Assert.Equal(-60, restored.Y);
        Assert.Equal(320, restored.Width);
        Assert.Equal(140, restored.Height);
        Assert.Equal("Radii are in millimetres.", restored.Text);
    }

    /// <summary>
    /// Opening a graph and saving it again produces the same bytes. A fresh identity on load would
    /// break this, and it would break it silently: the graph would look right and every save would
    /// rewrite every note.
    /// </summary>
    [Fact]
    public void AGraphWithNotesReSavesByteForByte()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        graph.AddNote(0, 0, "one");
        graph.AddNote(400, 200, "two");

        string first = CanvasDocument.Save(graph);
        string second = CanvasDocument.Save(CanvasDocument.Open(first, TestGraphs.Library));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// <b>The ADR-0016 guard, from the shell's side.</b> The checked-in example is a version-1
    /// graph. Opening it and saving it must not bump its version or otherwise touch a byte, or
    /// every graph anybody has on disk gets a spurious diff the first time they open it.
    /// </summary>
    [Fact]
    public void TheCheckedInVersionOneExampleReSavesUnchanged()
    {
        string path = ExamplePath("curves.spark");
        string original = File.ReadAllText(path).ReplaceLineEndings("\n");

        Assert.Contains("\"formatVersion\": 1", original, StringComparison.Ordinal);

        string resaved = CanvasDocument
            .Save(CanvasDocument.Open(original, TestGraphs.Library))
            .ReplaceLineEndings("\n");

        Assert.Equal(original, resaved);
    }

    [Fact]
    public void ANoteIsNotANode()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        int nodes = graph.Nodes.Count;

        graph.AddNote(10, 10, "not a node");

        Assert.Equal(nodes, graph.Nodes.Count);
        Assert.Single(graph.Notes);
    }

    [Fact]
    public void ANoteCanBeRemovedAndRemovingItTwiceIsHonestAboutIt()
    {
        CanvasGraph graph = new();
        CanvasNote note = graph.AddNote(0, 0, "temporary");

        Assert.True(graph.RemoveNote(note));
        Assert.False(graph.RemoveNote(note));
        Assert.Empty(graph.Notes);
    }

    /// <summary>
    /// A note cannot be shrunk into something impossible to find and click again. The clamp is on
    /// the property rather than in the resize gesture, so it holds for a hand-edited file too.
    /// </summary>
    [Fact]
    public void ANoteCannotBeShrunkBelowItsMinimumSize()
    {
        CanvasNote note = new() { Width = 1, Height = -400 };

        Assert.Equal(CanvasNote.MinimumSize, note.Width);
        Assert.Equal(CanvasNote.MinimumSize, note.Height);
    }

    /// <summary>Text is never null, because an untyped note is an ordinary state.</summary>
    [Fact]
    public void ANotesTextIsNeverNull()
    {
        CanvasNote note = new() { Text = null! };

        Assert.Equal(string.Empty, note.Text);
    }

    /// <summary>
    /// <i>Zoom to fit</i> has to fit the document, not the part of it that evaluates. A note placed
    /// beside a graph would otherwise sit just off the edge of the one gesture whose whole promise
    /// is that nothing is off the edge any more.
    /// </summary>
    [Fact]
    public void ComputingBoundsIncludesTheNotes()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        Spark.UI.Canvas.CanvasBounds withoutNote = graph.ComputeBounds();

        graph.AddNote(withoutNote.MaxX + 400, withoutNote.MaxY + 400, "over here");

        Spark.UI.Canvas.CanvasBounds withNote = graph.ComputeBounds();

        Assert.True(withNote.MaxX > withoutNote.MaxX);
        Assert.True(withNote.MaxY > withoutNote.MaxY);
    }

    /// <summary>A canvas holding only notes still has bounds, so an empty graph is not the answer.</summary>
    [Fact]
    public void AGraphOfNothingButNotesStillHasBounds()
    {
        CanvasGraph graph = new();
        graph.AddNote(100, 200, "alone");

        Spark.UI.Canvas.CanvasBounds bounds = graph.ComputeBounds();

        Assert.Equal(100, bounds.MinX);
        Assert.Equal(200, bounds.MinY);
    }

    private static string ExamplePath(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "docs", "examples", name);
    }
}
