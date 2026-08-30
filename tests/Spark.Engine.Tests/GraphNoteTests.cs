using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// Notes in the `.spark` format, and the version rule that carrying them forced.
/// </summary>
/// <remarks>
/// <para>
/// A note is a canvas annotation, not a document object: no <see cref="NodeId"/>, no ports, no
/// provenance. It travels through <see cref="GraphDocument"/> exactly as a node's coordinates do —
/// something the file must remember and the evaluator must never read — and
/// <see cref="ANoteNeverReachesTheGraph"/> is the assertion that keeps it that way.
/// </para>
/// <para>
/// The version rule is the interesting half. The version written is the <b>minimum version that
/// can read the file</b>, not a stamp of the build that wrote it, because
/// [ADR-0016](../../docs/adr/0016-no-dynamo-interoperability.md) requires a graph referencing a missing
/// package to re-save byte-identically — and stamping every save with the current version would
/// rewrite the first line of every version-1 graph in existence the first time it was opened.
/// </para>
/// </remarks>
public sealed class GraphNoteTests
{
    private static readonly NodeLibrary Library = BuildLibrary();

    [Fact]
    public void ANoteSurvivesBeingWrittenAndReadBack()
    {
        Guid id = Guid.NewGuid();
        GraphDocument original = Capture([
            new GraphDocumentNote(id, 12.5, -40.0, 300, 120, "Watch the units here.")]);

        GraphDocument reread = SparkFile.Read(SparkFile.Write(original));

        GraphDocumentNote note = Assert.Single(reread.Notes);
        Assert.Equal(id, note.Id);
        Assert.Equal(12.5, note.X);
        Assert.Equal(-40.0, note.Y);
        Assert.Equal(300, note.Width);
        Assert.Equal(120, note.Height);
        Assert.Equal("Watch the units here.", note.Text);
    }

    /// <summary>
    /// <b>The ADR-0016 guard.</b> A graph with no notes must produce exactly the bytes earlier
    /// builds produced, first line included, or every version-1 graph on disk gets a spurious diff
    /// the first time it is opened and saved.
    /// </summary>
    [Fact]
    public void AGraphWithNoNotesIsStillWrittenAsVersionOne()
    {
        string text = SparkFile.Write(Capture([]));

        Assert.Contains("\"formatVersion\": 1", text, StringComparison.Ordinal);
        Assert.Equal(GraphDocument.BaselineFormatVersion, SparkFile.Read(text).FormatVersion);
    }

    /// <summary>
    /// A version-1 reader does not know the <c>notes</c> array exists. It would open the file, show
    /// the graph, and throw every note away on the next save — so a file that has notes says it
    /// needs a version-2 reader, and an old build refuses it loudly instead.
    /// </summary>
    [Fact]
    public void AGraphWithNotesIsWrittenAsVersionTwo()
    {
        string text = SparkFile.Write(Capture([
            new GraphDocumentNote(Guid.NewGuid(), 0, 0, 200, 80, "hello")]));

        Assert.Contains("\"formatVersion\": 2", text, StringComparison.Ordinal);
        Assert.Equal(GraphDocument.NotesFormatVersion, SparkFile.Read(text).FormatVersion);
    }

    /// <summary>
    /// Not an empty array — the key is absent altogether. <c>"notes": []</c> would add two lines to
    /// the diff of every graph that has never had a note in it.
    /// </summary>
    [Fact]
    public void AGraphWithNoNotesDoesNotMentionNotesAtAll()
    {
        Assert.DoesNotContain("notes", SparkFile.Write(Capture([])), StringComparison.Ordinal);
    }

    /// <summary>
    /// Notes are sorted by identity, like the nodes, so that two files holding the same graph are
    /// the same bytes however they were assembled.
    /// </summary>
    [Fact]
    public void NotesAreOrderedByIdentityAndNotByInsertion()
    {
        GraphDocumentNote first = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 0, 0, 200, 80, "a");
        GraphDocumentNote second = new(Guid.Parse("99999999-9999-9999-9999-999999999999"), 0, 0, 200, 80, "b");

        // One graph, captured twice. Two graphs would carry two different node identities and
        // the comparison would fail for a reason that has nothing to do with note order.
        Graph graph = new();
        graph.AddNode(Library.ByName("Number.Value"));

        string forwards = SparkFile.Write(GraphDocument.Capture(graph, notes: [first, second]));
        string backwards = SparkFile.Write(GraphDocument.Capture(graph, notes: [second, first]));

        Assert.Equal(forwards, backwards);
        Assert.Equal("a", SparkFile.Read(forwards).Notes[0].Text);
    }

    /// <summary>
    /// Reading a file and writing it again reproduces it byte for byte, notes included. This is
    /// ADR-0017's whole premise applied to the new field.
    /// </summary>
    [Fact]
    public void AFileWithNotesRewritesItselfByteForByte()
    {
        string first = SparkFile.Write(Capture([
            new GraphDocumentNote(Guid.NewGuid(), 4, 8, 260, 90, "one"),
            new GraphDocumentNote(Guid.NewGuid(), 9, 1, 200, 48, "two"),
        ]));

        Assert.Equal(first, SparkFile.Write(SparkFile.Read(first)));
    }

    /// <summary>
    /// An empty note is a note the user made and has not typed into yet. Saving is not modal, so
    /// that state has to survive a round trip rather than being rejected or dropped.
    /// </summary>
    [Fact]
    public void AnEmptyNoteIsARealNote()
    {
        string text = SparkFile.Write(Capture([
            new GraphDocumentNote(Guid.NewGuid(), 0, 0, 200, 80, string.Empty)]));

        Assert.Equal(string.Empty, Assert.Single(SparkFile.Read(text).Notes).Text);
    }

    /// <summary>
    /// <b>The separation, asserted.</b> Restoring a document builds the evaluator's graph, and no
    /// note may appear in it in any form: a note cannot evaluate, so a model that could hold one is
    /// a model that has to check.
    /// </summary>
    [Fact]
    public void ANoteNeverReachesTheGraph()
    {
        GraphDocument document = Capture([
            new GraphDocumentNote(Guid.NewGuid(), 0, 0, 200, 80, "not a node")]);

        Graph restored = document.Restore(Library);

        Assert.Equal(document.Nodes.Count, restored.Nodes().Count);
        Assert.DoesNotContain(restored.Nodes(), node => node.Definition.DisplayName.Contains("note", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A note with no identity is a malformed file, not a note with a fresh identity.</summary>
    [Fact]
    public void ANoteWithoutAnIdentityIsRefused()
    {
        const string text = """
            {"formatVersion": 2, "nodes": [], "wires": [],
             "notes": [{"x": 0, "y": 0, "width": 200, "height": 80, "text": "hi"}]}
            """;

        Assert.Throws<SparkFileException>(() => SparkFile.Read(text));
    }

    /// <summary>A note whose geometry is missing is malformed for the same reason.</summary>
    [Fact]
    public void ANoteWithoutAWidthIsRefused()
    {
        string text = $$"""
            {"formatVersion": 2, "nodes": [], "wires": [],
             "notes": [{"id": "{{Guid.NewGuid():D}}", "x": 0, "y": 0, "height": 80, "text": "hi"}]}
            """;

        Assert.Throws<SparkFileException>(() => SparkFile.Read(text));
    }

    /// <summary>
    /// The reader refuses a version it does not know rather than partly reading it.
    /// </summary>
    /// <remarks>
    /// <b>The number is deliberately absurd.</b> This test said <c>3</c> when the current version
    /// was 2, and broke the day scripts made 3 real — for a reason that had nothing to do with
    /// what it checks. That is the second time a placeholder here has been overtaken by the thing
    /// it was standing in for, the first being the type name in
    /// <c>GeometryJsonTests.AnUnknownTypeIsRefused</c>. A stand-in for *something that does not
    /// exist* must be something that cannot come to exist.
    /// </remarks>
    [Fact]
    public void AVersionNewerThanThisBuildIsStillRefused()
    {
        Assert.Throws<SparkFileException>(
            () => SparkFile.Read("""{"formatVersion": 999999, "nodes": [], "wires": []}"""));
    }

    /// <summary>
    /// A group's members are sorted in the file, so the same selection made in a different order
    /// writes the same bytes. A selection is a set; the file must not inherit an order it never
    /// had.
    /// </summary>
    [Fact]
    public void AGroupsMemberOrderDoesNotReachTheFile()
    {
        Graph graph = new();
        NodeId first = graph.AddNode(Library.ByName("Number.Value")).Id;
        NodeId second = graph.AddNode(Library.ByName("Math.Sin")).Id;
        Guid id = Guid.NewGuid();

        string forwards = SparkFile.Write(GraphDocument.Capture(
            graph, groups: [new GraphDocumentGroup(id, "Both", [first, second])]));
        string backwards = SparkFile.Write(GraphDocument.Capture(
            graph, groups: [new GraphDocumentGroup(id, "Both", [second, first])]));

        Assert.Equal(forwards, backwards);
    }

    /// <summary>
    /// Groups arrive at version 2, the same as notes. Inventing a version 3 for the second field
    /// to land in the same week would refuse a file to a reader that can in fact read it.
    /// </summary>
    [Fact]
    public void AGraphWithGroupsIsWrittenAsVersionTwo()
    {
        Graph graph = new();
        NodeId only = graph.AddNode(Library.ByName("Number.Value")).Id;

        string text = SparkFile.Write(GraphDocument.Capture(
            graph, groups: [new GraphDocumentGroup(Guid.NewGuid(), "One", [only])]));

        Assert.Contains("\"formatVersion\": 2", text, StringComparison.Ordinal);
        Assert.Equal(GraphDocument.GroupsFormatVersion, SparkFile.Read(text).FormatVersion);
    }

    /// <summary>
    /// A group with no members is malformed rather than empty. A group's whole content is what it
    /// contains, so one containing nothing could only have arrived by an editing mistake.
    /// </summary>
    [Fact]
    public void AGroupWithNoMembersIsRefused()
    {
        string text = $$"""
            {"formatVersion": 2, "nodes": [], "wires": [],
             "groups": [{"id": "{{Guid.NewGuid():D}}", "title": "Empty", "members": []}]}
            """;

        Assert.Throws<SparkFileException>(() => SparkFile.Read(text));
    }

    /// <summary>A group whose member is not a node identity is malformed.</summary>
    [Fact]
    public void AGroupNamingSomethingThatIsNotANodeIsRefused()
    {
        string text = $$"""
            {"formatVersion": 2, "nodes": [], "wires": [],
             "groups": [{"id": "{{Guid.NewGuid():D}}", "title": "Bad", "members": ["not-a-guid"]}]}
            """;

        Assert.Throws<SparkFileException>(() => SparkFile.Read(text));
    }

    /// <summary>A graph with neither notes nor groups still writes version 1 and mentions neither.</summary>
    [Fact]
    public void AGraphWithNeitherMentionsNeither()
    {
        string text = SparkFile.Write(Capture([]));

        Assert.DoesNotContain("groups", text, StringComparison.Ordinal);
        Assert.Contains("\"formatVersion\": 1", text, StringComparison.Ordinal);
    }

    private static GraphDocument Capture(IReadOnlyList<GraphDocumentNote> notes)
    {
        Graph graph = new();
        graph.AddNode(Library.ByName("Number.Value"));
        return GraphDocument.Capture(graph, notes: notes);
    }

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(Assembly.Load("Spark.Nodes.Core")));
        return library;
    }
}
