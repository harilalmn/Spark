using System;
using System.Threading;
using Spark.Engine;
using Spark.Scripting;

namespace Spark.Scripting.Tests;

/// <summary>
/// Guards against a code block that never gives its thread back.
/// </summary>
/// <remarks>
/// Every test here runs its work under a hard timeout. A guard test that hangs would otherwise take
/// the whole run with it and report nothing at all, which is the one failure mode these tests exist
/// to make impossible.
/// </remarks>
public sealed class GuardTests
{
    [Fact]
    public void AnInfiniteLoopIsStoppedByTheTimeBudget()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            double total = 0;
            while (true) { total += 1; }
            """,
            CodeBlockTestHarness.Options(budget: TimeSpan.FromMilliseconds(250)));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        ScriptStoppedException stopped = CodeBlockTestHarness.RunWithHardTimeout(
            () => Assert.Throws<ScriptStoppedException>(() => compilation.Definition!.Invoke([])),
            milliseconds: 15_000);

        Assert.Equal(ScriptStopReason.TimeBudget, stopped.Reason);
    }

    [Fact]
    public void AnInfiniteLoopIsStoppedByCancellation()
    {
        using CancellationTokenSource cancellation = new();

        CodeBlockOptions options = new()
        {
            Cache = new ScriptCompilationCache(string.Empty),
            TimeBudget = TimeSpan.Zero,
            Cancellation = () => cancellation.Token,
        };

        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            double total = 0;
            for (int i = 0; ; i++) { total += i; }
            """,
            options);

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        cancellation.CancelAfter(TimeSpan.FromMilliseconds(250));

        ScriptStoppedException stopped = CodeBlockTestHarness.RunWithHardTimeout(
            () => Assert.Throws<ScriptStoppedException>(() => compilation.Definition!.Invoke([])),
            milliseconds: 15_000);

        Assert.Equal(ScriptStopReason.Cancelled, stopped.Reason);
    }

    /// <summary>
    /// A loop with no braces is guarded too. Without giving it a block of its own there is nowhere to
    /// put the guard, and <c>while (true) x++;</c> would run for ever.
    /// </summary>
    [Fact]
    public void ALoopWithNoBracesIsStillGuarded()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            double total = 0;
            while (true) total += 1;
            """,
            CodeBlockTestHarness.Options(budget: TimeSpan.FromMilliseconds(250)));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        _ = CodeBlockTestHarness.RunWithHardTimeout(
            () => Assert.Throws<ScriptStoppedException>(() => compilation.Definition!.Invoke([])),
            milliseconds: 15_000);
    }

    /// <summary>
    /// Runaway recursion is caught while there is still stack to unwind. A real
    /// <see cref="StackOverflowException"/> cannot be caught in .NET at all — it would end this test
    /// process, and every other test with it — so this one either passes or takes the run down.
    /// </summary>
    [Fact]
    public void RunawayRecursionIsStoppedBeforeTheStackIsGone()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            double Down(double n) { return Down(n + 1); }
            Down(0)
            """,
            CodeBlockTestHarness.Options(budget: TimeSpan.FromSeconds(30)));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        _ = CodeBlockTestHarness.RunWithHardTimeout(
            () => Assert.Throws<ScriptStoppedException>(() => compilation.Definition!.Invoke([])),
            milliseconds: 30_000);
    }

    /// <summary>A loop that finishes on its own is untouched by any of this.</summary>
    [Fact]
    public void AFiniteLoopRunsToCompletion()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            """
            double total = 0;
            for (int i = 0; i < 1000; i++) { total += i; }
            total
            """,
            CodeBlockTestHarness.Options());

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        Assert.Equal(499500.0, compilation.Definition!.Invoke([])[0]);
    }

    /// <summary>
    /// The guard is inert outside an invocation, so a delegate a script handed out and something else
    /// later called does not throw for want of a scope.
    /// </summary>
    [Fact]
    public void TheGuardIsInertOutsideAnInvocation()
    {
        for (int index = 0; index < 1000; index++)
        {
            ScriptGuard.Tick();
        }

        ScriptGuard.Enter();
    }
}
