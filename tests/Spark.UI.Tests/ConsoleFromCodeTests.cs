using System;
using System.Threading;
using Spark.Api;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// <c>Console.Write</c>, <c>WriteLine</c> and <c>Clear</c> from a code block, and as nodes —
/// `E8-T80`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: <i>introduce a console with Console.Write/WriteLine/Clear
/// methods, callable from codeblock. Great if you can have nodes also for these methods.</i> Both
/// halves are the same three methods; only the way in differs.
/// </para>
/// <para>
/// <b>The assertion that costs the most to get wrong is the last one.</b> A console node whose
/// inputs have not changed is served from the evaluation cache, so a graph run twice would print
/// once — correct for a node that computes and useless for one whose whole purpose is what it does
/// on the way.
/// </para>
/// </remarks>
[Collection("SparkConsole")]
public sealed class ConsoleFromCodeTests : IDisposable
{
    public ConsoleFromCodeTests() => SparkConsole.Clear();

    public void Dispose() => SparkConsole.Clear();

    /// <summary>
    /// <b>The thing the client asked for.</b> No <c>using</c>, no qualification: the line the user
    /// types is the line that appears.
    /// </summary>
    [Fact]
    public void ACodeBlockWritesToTheConsoleWithNoUsing()
    {
        Run("""Console.WriteLine("hello from a block");""");

        Assert.Equal(["hello from a block"], SparkConsole.Lines());
    }

    /// <summary><c>Write</c> does not end the line, in a block as anywhere else.</summary>
    [Fact]
    public void WriteAndWriteLineComposeAsTheyDoInCSharp()
    {
        Run("""
            Console.Write("a");
            Console.Write("b");
            Console.WriteLine("c");
            """);

        Assert.Equal(["abc"], SparkConsole.Lines());
    }

    /// <summary><c>Clear</c> empties it from a block too, and says how much it took.</summary>
    [Fact]
    public void ClearEmptiesItFromABlock()
    {
        SparkConsole.WriteLine("before");

        Run("""
            var removed = Console.Clear();
            Console.WriteLine("removed " + removed);
            """);

        Assert.Equal(["removed 1"], SparkConsole.Lines());
    }

    /// <summary>
    /// <b><c>Console</c> means Spark's, and <c>System.Console</c> is still reachable.</b> The
    /// prelude pins the name rather than removing the other one — a block that has a reason to
    /// write to a real terminal can still say so in full.
    /// </summary>
    [Fact]
    public void SystemConsoleIsStillReachableByItsFullName()
    {
        Run("""
            System.Console.Out.Flush();
            Console.WriteLine("mine");
            """);

        Assert.Equal(["mine"], SparkConsole.Lines());
    }

    /// <summary>
    /// <b>The three nodes exist, and they are marked as having a side effect</b> — without which a
    /// graph run twice prints once, because the cache would serve the first run's answer to a node
    /// whose inputs have not changed.
    /// </summary>
    [Fact]
    public void TheThreeNodesExistAndDeclareTheirSideEffect()
    {
        // The session's own library, which is the one the application and the canvas resolve
        // against - a hand-built one would prove the importer works and not that the nodes ship.
        using Spark.Host.SparkSession session = new();

        foreach (string name in (string[])["Console.Write", "Console.WriteLine", "Console.Clear"])
        {
            Spark.Engine.NodeDefinition definition = Assert.Single(
                session.Library.Definitions(),
                candidate => candidate.DisplayName == name);

            Assert.True(
                definition.IsSideEffect,
                name + " must declare a side effect, or a graph run twice prints once.");

            // And each has an output, because a void method is not imported at all.
            Assert.NotEmpty(definition.Outputs);
        }
    }

    /// <summary>Compiles and runs a code block the way the application does.</summary>
    private static void Run(string source)
    {
        ReferenceCatalog catalogue = new();
        NodeDefinitionSource block = new ScriptNodeFactory(catalogue).Create(source);

        _ = block.Invoke([], CancellationToken.None);
    }
}
