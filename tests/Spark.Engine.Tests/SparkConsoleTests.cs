using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spark.Api;

namespace Spark.Engine.Tests;

/// <summary>
/// The channel a graph writes text to — `E8-T80`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: <c>Console.Write</c>, <c>WriteLine</c> and <c>Clear</c> from a
/// code block, nodes for the same three, and a pane to read them in. This is the channel underneath
/// all of that.
/// </para>
/// <para>
/// <b>These tests run one at a time</b>, because the subject is static and shared — which is the
/// right shape for it (a code block has nowhere to get an instance from) and does mean two tests
/// writing at once would read each other's lines.
/// </para>
/// </remarks>
[Collection("SparkConsole")]
public sealed class SparkConsoleTests : IDisposable
{
    public SparkConsoleTests() => SparkConsole.Clear();

    public void Dispose() => SparkConsole.Clear();

    /// <summary>A line written is a line read.</summary>
    [Fact]
    public void WhatIsWrittenIsWhatIsRead()
    {
        SparkConsole.WriteLine("first");
        SparkConsole.WriteLine("second");

        Assert.Equal(["first", "second"], SparkConsole.Lines());
    }

    /// <summary>
    /// <b><c>Write</c> does not end the line</b>, which is <c>System.Console</c>'s contract and the
    /// one anybody typing this expects.
    /// </summary>
    [Fact]
    public void WriteAppendsUntilTheLineIsEnded()
    {
        SparkConsole.Write("a");
        SparkConsole.Write("b");
        SparkConsole.WriteLine("c");

        Assert.Equal(["abc"], SparkConsole.Lines());
    }

    /// <summary>
    /// <b>An unfinished line is still readable</b>, because somebody debugging with a bare
    /// <c>Write</c> is the person most in need of seeing it.
    /// </summary>
    [Fact]
    public void AnUnfinishedLineIsVisible()
    {
        SparkConsole.Write("half");

        Assert.Equal(["half"], SparkConsole.Lines());

        SparkConsole.WriteLine(" a line");

        Assert.Equal(["half a line"], SparkConsole.Lines());
    }

    /// <summary><c>WriteLine</c> with nothing is a blank line, as it is everywhere else.</summary>
    [Fact]
    public void WriteLineWithNothingIsABlankLine()
    {
        SparkConsole.WriteLine();
        SparkConsole.WriteLine((string?)null);

        Assert.Equal(["", ""], SparkConsole.Lines());
    }

    /// <summary>
    /// <b>Clear takes the pending line too.</b> Half a line left behind, to appear in front of
    /// whatever was written next, would be a puzzle rather than a clear.
    /// </summary>
    [Fact]
    public void ClearTakesThePendingLineWithIt()
    {
        SparkConsole.WriteLine("gone");
        SparkConsole.Write("also gone");

        SparkConsole.Clear();

        Assert.Empty(SparkConsole.Lines());

        SparkConsole.WriteLine("after");
        Assert.Equal(["after"], SparkConsole.Lines());
    }

    /// <summary>
    /// <b>The buffer is bounded and says how much it dropped.</b> A loop writing a line per
    /// iteration must not grow until the process dies, and a console that quietly loses the
    /// beginning of the output is worse than one that admits it.
    /// </summary>
    [Fact]
    public void TheOldestLinesGoAndAreCounted()
    {
        for (int line = 0; line < SparkConsole.Capacity + 5; line++)
        {
            SparkConsole.WriteLine(line);
        }

        IReadOnlyList<string> lines = SparkConsole.Lines();

        Assert.Equal(SparkConsole.Capacity, lines.Count);
        Assert.Equal(5, SparkConsole.Dropped);

        // The five that went are the oldest, so the first survivor is line five.
        Assert.Equal("5", lines[0]);
        Assert.Equal((SparkConsole.Capacity + 4).ToString(System.Globalization.CultureInfo.InvariantCulture), lines[^1]);
    }

    /// <summary>A number reads the same on every machine, because a console line is pasted about.</summary>
    [Fact]
    public void ValuesAreFormattedInvariantly()
    {
        SparkConsole.WriteLine(1.5);
        SparkConsole.WriteLine((object?)null);

        Assert.Equal(["1.5", ""], SparkConsole.Lines());
    }

    /// <summary>Each of the three announces itself, so a view can catch up.</summary>
    [Fact]
    public void EveryChangeIsAnnounced()
    {
        int announced = 0;
        void Count(object? sender, EventArgs e) => announced++;

        SparkConsole.Changed += Count;

        try
        {
            SparkConsole.Write("a");
            SparkConsole.WriteLine("b");
            SparkConsole.Clear();

            Assert.Equal(3, announced);
        }
        finally
        {
            SparkConsole.Changed -= Count;
        }
    }

    /// <summary>
    /// <b>The event is raised outside the lock.</b> A handler is somebody else's code and may write
    /// to the console itself; raised inside, this test would deadlock rather than fail, which is
    /// why it is written as a handler that writes.
    /// </summary>
    [Fact]
    public void AHandlerMayWriteToTheConsole()
    {
        int depth = 0;
        void Echo(object? sender, EventArgs e)
        {
            if (depth++ == 0)
            {
                SparkConsole.WriteLine("echo");
            }
        }

        SparkConsole.Changed += Echo;

        try
        {
            SparkConsole.WriteLine("original");

            Assert.Contains("echo", SparkConsole.Lines());
        }
        finally
        {
            SparkConsole.Changed -= Echo;
        }
    }

    /// <summary>
    /// <b>A graph evaluates in parallel, so whole lines must survive several threads at once.</b>
    /// Partial writes may interleave — the real console interleaves too — but a completed line is
    /// never torn in half or lost.
    /// </summary>
    [Fact]
    public async Task WholeLinesSurviveManyThreads()
    {
        const int writers = 32;
        const int each = 50;

        await Task.WhenAll(Enumerable.Range(0, writers).Select(writer => Task.Run(() =>
        {
            for (int line = 0; line < each; line++)
            {
                SparkConsole.WriteLine("writer " + writer.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        })));

        IReadOnlyList<string> lines = SparkConsole.Lines();

        Assert.Equal(writers * each, lines.Count);
        Assert.All(lines, line => Assert.StartsWith("writer ", line, StringComparison.Ordinal));
    }
}
