using System;
using System.IO;
using Spark.Cli;
using Spark.Engine;

namespace Spark.Cli.Tests;

/// <summary>
/// <c>spark check</c> (<c>E12-T5</c>). It is <c>spark run</c> with the printing taken away: a
/// build gate whose answer is an exit code.
/// </summary>
/// <remarks>
/// <para>
/// <b>The contract under test is the return value, which is exactly what a person reading console
/// output is worst at checking.</b> <c>spark run</c> and <c>spark export</c> shipped with no tests
/// at all and were verified by eye; this is the first test project over the command line, and the
/// reason it exists is that a verb nobody can see failing is a verb that will one day stop failing.
/// </para>
/// <para>
/// <c>Check</c> takes a <see cref="TextWriter"/> rather than reaching for <c>Console.Error</c>, so
/// the message and the exit code can both be asserted. The four verbs still to be written will
/// need the same shape.
/// </para>
/// </remarks>
public sealed class CheckVerbTests
{
    /// <summary>A graph that evaluates cleanly exits zero and says nothing whatsoever.</summary>
    /// <remarks>
    /// <b>Silence is the assertion, not a side effect of it.</b> A gate that writes a line on the
    /// happy path is a gate whose output stops being read, and then the one run that had something
    /// to say scrolls past with the rest.
    /// </remarks>
    [Fact]
    public void ACleanGraphExitsZeroAndSaysNothing()
    {
        string path = WriteGraph(graph =>
        {
            NodeId number = Add(graph, "Number.Value");
            graph.SetLiteral(number, 0, 3.0);
        });

        StringWriter error = new();

        Assert.Equal(0, Program.Check([path], error));
        Assert.Equal(string.Empty, error.ToString());
    }

    /// <summary>A graph with an erroring node exits one and names the node.</summary>
    /// <remarks>
    /// <b>The node name is the half <c>spark run</c> does not print and a build log needs.</b> A
    /// log is read by somebody who was not watching, and a diagnostic code with no node attached
    /// is a message that costs an hour. A circle of radius zero is refused by the kernel, which is
    /// what makes this a real evaluation failure rather than a synthesised one.
    /// </remarks>
    [Fact]
    public void AnErroringGraphExitsOneAndNamesTheNode()
    {
        string path = WriteGraph(graph =>
        {
            NodeId circle = Add(graph, "Circle.FromCenterRadius");
            graph.SetLiteral(circle, 1, 0.0);
        });

        StringWriter error = new();

        Assert.Equal(1, Program.Check([path], error));

        string message = error.ToString();

        Assert.Contains("error", message, StringComparison.Ordinal);
        Assert.Contains("Circle", message, StringComparison.Ordinal);
        Assert.Contains(path, message, StringComparison.Ordinal);
    }

    /// <summary>Naming no graph is an error, and the message says what to type instead.</summary>
    [Fact]
    public void CheckWithNoGraphSaysWhatToType()
    {
        StringWriter error = new();

        Assert.Equal(1, Program.Check([], error));
        Assert.Contains("spark check graph.spark", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>An option the verb does not have is refused rather than ignored.</summary>
    /// <remarks>
    /// Ignoring an unrecognised option in a build gate is the worst of the three choices: the
    /// script author believes they asked for something and the gate believes it was asked for
    /// nothing.
    /// </remarks>
    [Fact]
    public void AnUnrecognisedOptionIsRefused()
    {
        StringWriter error = new();

        Assert.Equal(1, Program.Check(["--all"], error));
        Assert.Contains("--all", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary><c>--open PATH</c> is accepted as well as a bare path.</summary>
    /// <remarks>
    /// <c>export</c> requires <c>--open</c> and <c>run</c> accepts both; <c>check</c> follows
    /// <c>run</c>, because a bare path is the ordinary way to name a file to a command line and a
    /// gate is something people type into a script quickly.
    /// </remarks>
    [Fact]
    public void TheGraphMayBeNamedWithOpenOrBare()
    {
        string path = WriteGraph(graph => graph.SetLiteral(Add(graph, "Number.Value"), 0, 1.0));

        StringWriter bare = new();
        StringWriter named = new();

        Assert.Equal(0, Program.Check([path], bare));
        Assert.Equal(0, Program.Check(["--open", path], named));
        Assert.Equal(bare.ToString(), named.ToString());
    }

    /// <summary>A file that is not a graph is reported rather than throwing out of the verb.</summary>
    /// <remarks>
    /// The exception is caught by <c>Main</c>, not by <c>Check</c> — this asserts that it is the
    /// documented kind, so that the catch in <c>Main</c> keeps covering it.
    /// </remarks>
    [Fact]
    public void AFileThatIsNotAGraphThrowsTheKindMainCatches()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".spark");
        File.WriteAllText(path, "this is not a graph");

        try
        {
            Assert.ThrowsAny<Exception>(() => Program.Check([path], new StringWriter()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// <b>A replication where every element failed is a warning, and the default gate passes it.</b>
    /// </summary>
    /// <remarks>
    /// This is the surprising case and the reason <c>--strict</c> exists, so it is asserted rather
    /// than described. The same radius of zero on an unreplicated node is an <i>error</i> and fails
    /// — see <see cref="AnErroringGraphExitsOneAndNamesTheNode"/> — but fan the node out over eight
    /// points and eight failures out of eight is <c>SPK1042</c>, a warning, because the node did
    /// produce a list and everything downstream did evaluate. A build that wants the stricter
    /// reading has to ask for it, and now it can.
    /// </remarks>
    [Fact]
    public void AWhollyFailedReplicationIsAWarningAndOnlyStrictFailsIt()
    {
        string path = WriteGraph(graph =>
        {
            // A range rather than an array literal: a `.spark` file cannot represent a list typed
            // into a port, so the list has to be produced by a node. That is a property of the
            // file format rather than of this test, and it is the reason the graph has three
            // nodes in it instead of two.
            NodeId range = Add(graph, "Number.Range");
            NodeId points = Add(graph, "Point.FromCoordinates");
            NodeId circle = Add(graph, "Circle.FromCenterRadius");

            graph.SetLiteral(range, 0, 0.0);
            graph.SetLiteral(range, 1, 2.0);
            graph.SetLiteral(range, 2, 1.0);
            graph.SetLiteral(circle, 1, 0.0);

            Assert.True(graph.TryConnect(range, 0, points, 0).Accepted);
            Assert.True(graph.TryConnect(points, 0, circle, 0).Accepted);
        });

        StringWriter lenient = new();
        StringWriter strict = new();

        Assert.Equal(0, Program.Check([path], lenient));
        Assert.Equal(1, Program.Check([path, "--strict"], strict));

        Assert.Contains("warning", lenient.ToString(), StringComparison.Ordinal);
        Assert.Equal(lenient.ToString(), strict.ToString());
    }

    /// <summary>
    /// <b>Every diagnostic is one line, whatever the exception put in the message.</b>
    /// </summary>
    /// <remarks>
    /// <see cref="ArgumentOutOfRangeException"/> appends <i>Actual value was 0.</i> on a line of
    /// its own, and a build log read by <c>grep</c> would otherwise get a second line carrying no
    /// file name, no node and no severity. The canvas keeps the break; the command line does not.
    /// </remarks>
    [Fact]
    public void EveryDiagnosticIsOneLine()
    {
        string path = WriteGraph(graph =>
        {
            NodeId circle = Add(graph, "Circle.FromCenterRadius");
            graph.SetLiteral(circle, 1, 0.0);
        });

        StringWriter error = new();

        Assert.Equal(1, Program.Check([path], error));

        string[] lines = error.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.All(lines, line => Assert.StartsWith("spark: ", line, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Actual value was 0", StringComparison.Ordinal));
    }

    private static NodeId Add(Graph graph, string name) =>
        graph.AddNode(Library.Get(new NodeKey("Spark.Nodes.Core", name))).Id;

    private static NodeLibrary Library { get; } = BuildLibrary();

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));
        return library;
    }

    /// <summary>
    /// Writes a graph to a temporary <c>.spark</c> file and returns its path.
    /// </summary>
    /// <param name="build">Fills the graph in.</param>
    /// <returns>The path, which the test process leaves behind for the operating system.</returns>
    /// <remarks>
    /// A real file on disk rather than a stream, because the verb's job starts at a path and a
    /// test that handed it something else would be testing a different function.
    /// </remarks>
    private static string WriteGraph(Action<Graph> build)
    {
        Graph graph = new();
        build(graph);

        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".spark");
        File.WriteAllText(path, SparkFile.Write(GraphDocument.Capture(graph)));

        return path;
    }
}
