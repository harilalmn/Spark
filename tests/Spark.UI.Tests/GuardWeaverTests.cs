using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Spark.Api;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// The guard weaver — `E6-T4`, and with it `E6-T17`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The test this file exists for is the first one</b>: a script containing
/// <c>while (true) { }</c> stops when the evaluation is cancelled. Before the weaver it hung the
/// thread forever, and every other guarantee in the scripting layer was written on top of a
/// process that could be wedged by four words.
/// </para>
/// <para>
/// The ceilings are set absurdly low here on purpose. Proving that a runaway loop is stopped should
/// not cost a hundred million iterations, and the limits are a constructor argument precisely so a
/// test can say what it means — see <see cref="ScriptNodeFactory(ReferenceCatalog, GuardWeaver)"/>.
/// </para>
/// <para>
/// <b>Every test here that asserts a stop also has a timeout on it.</b> A guard test that fails by
/// hanging is worse than no test: the suite stops rather than going red, and on CI that reads as an
/// infrastructure problem rather than as this regression.
/// </para>
/// </remarks>
public sealed class GuardWeaverTests
{
    /// <summary>How long a script that must stop is given before the test itself fails.</summary>
    private static readonly TimeSpan MustStopWithin = TimeSpan.FromSeconds(20);

    private static ScriptNodeFactory Factory(long iterations = 5_000, int depth = 16)
    {
        // The catalogue is built from the assemblies this process has already loaded, and a test
        // class that never mentions geometry can run before anything has loaded Spark.Geometry - at
        // which point the prelude's `using Spark.Geometry;` does not resolve and every script here
        // fails to compile for a reason that has nothing to do with guards. Naming a type loads it.
        _ = typeof(Spark.Geometry.Point3d).Assembly.Location;

        return new ScriptNodeFactory(new ReferenceCatalog(), new GuardWeaver(iterations, depth));
    }

    /// <summary>
    /// <b>The point of the whole row.</b> A deliberately infinite loop is cancelled rather than
    /// hanging the evaluation thread. Revert the weaver and this test never returns — which is why
    /// it runs on a worker with a deadline rather than inline.
    /// </summary>
    [Fact]
    public void AnInfiniteLoopIsCancelled()
    {
        // A generous iteration ceiling, so that what stops this loop can only be the token.
        NodeDefinitionSource block = Factory(iterations: long.MaxValue).Create("while (true) { }\nreturn 1;");

        using CancellationTokenSource cancellation = new();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));

        Assert.True(
            Ran(() => Assert.ThrowsAny<OperationCanceledException>(
                () => block.Invoke([], cancellation.Token))),
            "The script was still running after the deadline: the loop guard is not woven.");
    }

    /// <summary>
    /// With nobody watching — <c>spark run</c> in a build, a headless host — the iteration ceiling
    /// is what stops a runaway loop, and it fails as a guard rather than as cancellation.
    /// </summary>
    [Fact]
    public void AnInfiniteLoopWithNoCancellationHitsTheIterationCeiling()
    {
        NodeDefinitionSource block = Factory(iterations: 5_000).Create("while (true) { }\nreturn 1;");

        ScriptGuardException? failure = null;

        Assert.True(
            Ran(() => failure = Assert.Throws<ScriptGuardException>(
                () => block.Invoke([], CancellationToken.None))),
            "The script was still running after the deadline: the iteration ceiling is not woven.");

        Assert.Contains("loop iterations", failure!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A loop whose body is a single statement rather than a block is still guarded. This is the
    /// case a rewriter that only visited <c>BlockSyntax</c> would miss, and <c>while (c) x++;</c> is
    /// not unusual code.
    /// </summary>
    [Fact]
    public void ALoopWithNoBracesIsGuarded()
    {
        NodeDefinitionSource block = Factory(iterations: 5_000).Create("var n = 0;\nwhile (true) n++;\nreturn n;");

        Assert.True(
            Ran(() => Assert.Throws<ScriptGuardException>(() => block.Invoke([], CancellationToken.None))),
            "A brace-less loop body was left unguarded.");
    }

    /// <summary>
    /// An empty loop body — <c>while (Step()) ;</c> — is guarded too. There is no statement to put a
    /// check in front of, so the weaver has to make a block where there was none.
    /// </summary>
    [Fact]
    public void ALoopWithAnEmptyBodyIsGuarded()
    {
        NodeDefinitionSource block = Factory(iterations: 5_000).Create("while (true) ;\nreturn 1;");

        Assert.True(
            Ran(() => Assert.Throws<ScriptGuardException>(() => block.Invoke([], CancellationToken.None))),
            "An empty loop body was left unguarded.");
    }

    /// <summary>
    /// <b>A label and a jump are a loop.</b> A weaver that only looked for loop keywords would leave
    /// this one running forever, and it is exactly the shape code translated from another language
    /// arrives in.
    /// </summary>
    [Fact]
    public void AGotoLoopIsGuarded()
    {
        NodeDefinitionSource block = Factory(iterations: 5_000)
            .Create("var n = 0;\nagain:\nn++;\ngoto again;\n");

        Assert.True(
            Ran(() => Assert.Throws<ScriptGuardException>(() => block.Invoke([], CancellationToken.None))),
            "A goto loop was left unguarded.");
    }

    /// <summary>
    /// Unbounded recursion is stopped by the depth ceiling, and that is the one guard with no
    /// alternative: a <see cref="StackOverflowException"/> cannot be caught in .NET and would end
    /// the process, taking the user's unsaved graph with it.
    /// </summary>
    [Fact]
    public void UnboundedRecursionIsStoppedBeforeTheStackOverflows()
    {
        NodeDefinitionSource block = Factory(depth: 16)
            .Create("int f(int n) { return f(n + 1); }\nreturn f(0);");

        ScriptGuardException failure = Assert.Throws<ScriptGuardException>(
            () => block.Invoke([], CancellationToken.None));

        Assert.Contains("recursed", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An expression-bodied local function is guarded as well, which takes rewriting
    /// <c>int f(int n) =&gt; …;</c> into a block. Its return type is stated in the syntax, and that
    /// is the whole reason local functions are covered where lambdas are not.
    /// </summary>
    [Fact]
    public void AnExpressionBodiedLocalFunctionIsGuarded()
    {
        NodeDefinitionSource block = Factory(depth: 16).Create("int f(int n) => f(n + 1);\nreturn f(0);");

        Assert.Throws<ScriptGuardException>(() => block.Invoke([], CancellationToken.None));
    }

    /// <summary>
    /// A <c>void</c> expression-bodied local function is rewritten into a statement rather than a
    /// <c>return</c>. Getting this backwards gives <c>CS0127</c> on code the user wrote correctly.
    /// </summary>
    [Fact]
    public void AVoidExpressionBodiedLocalFunctionStillCompiles()
    {
        NodeDefinitionSource block = Factory()
            .Create("var log = new List<int>();\nvoid add(int n) => log.Add(n);\nadd(7);\nreturn log[0];");

        Assert.Equal(7, Assert.Single(block.Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// <b>The depth counter unwinds through a throw.</b> A recursive function that fails partway
    /// must put the count back, or the second call in the same invocation starts from a depth that
    /// never came down and fails for a reason that has nothing to do with it.
    /// </summary>
    [Fact]
    public void TheDepthCountUnwindsAfterAFailure()
    {
        NodeDefinitionSource block = Factory(depth: 16).Create(
            """
            int f(int n) { if (n == 3) throw new InvalidOperationException("deep"); return f(n + 1); }
            var caught = 0;
            for (var attempt = 0; attempt < 200; attempt++)
            {
                try { f(0); } catch (InvalidOperationException) { caught++; }
            }
            return caught;
            """);

        Assert.Equal(200, Assert.Single(block.Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// <b>A <c>static</c> local function still compiles.</b> The woven check reads
    /// <c>__token</c>, and <c>static</c> is precisely the promise not to capture it — so the weaver
    /// drops the modifier. Without that, a perfectly ordinary script fails with <c>CS8421</c>
    /// naming a parameter the user has never seen.
    /// </summary>
    [Fact]
    public void AStaticLocalFunctionContainingALoopStillCompiles()
    {
        NodeDefinitionSource block = Factory().Create(
            "static int total() { var t = 0; for (var i = 0; i < 4; i++) { t += i; } return t; }\nreturn total();");

        Assert.Equal(6, Assert.Single(block.Invoke([], CancellationToken.None)));
    }

    /// <summary>A <c>static</c> lambda containing a loop compiles for the same reason.</summary>
    [Fact]
    public void AStaticLambdaContainingALoopStillCompiles()
    {
        NodeDefinitionSource block = Factory().Create(
            "Func<int, int> f = static n => { var t = 0; for (var i = 0; i < n; i++) { t += i; } return t; };\nreturn f(4);");

        Assert.Equal(6, Assert.Single(block.Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// The budget is per invocation. Two runs of a script that each use most of the ceiling both
    /// succeed — a counter that was not reset would fail the second one, and on a replicated node
    /// that is the difference between a list of ten working and a list of a thousand failing.
    /// </summary>
    [Fact]
    public void TheBudgetIsPerInvocation()
    {
        NodeDefinitionSource block = Factory(iterations: 5_000)
            .Create("var t = 0;\nfor (var i = 0; i < 4000; i++) { t += i; }\nreturn t;");

        for (int run = 0; run < 5; run++)
        {
            Assert.Equal(7_998_000, Assert.Single(block.Invoke([], CancellationToken.None)));
        }
    }

    /// <summary>
    /// A script that does ordinary work gives the same answer it did before the weaver existed.
    /// The guards must be invisible to anything that is not runaway.
    /// </summary>
    [Fact]
    public void GuardsDoNotChangeWhatAScriptComputes()
    {
        NodeDefinitionSource block = Factory().Create(
            """
            var total = 0.0;
            foreach (var i in Enumerable.Range(1, count))
            {
                total += i;
            }
            return total;
            """);

        Assert.Equal(55.0, Assert.Single(block.Invoke([10], CancellationToken.None)));
    }

    /// <summary>
    /// <b>The weaver adds no lines.</b> A diagnostic's line number is the user's line number plus a
    /// constant prelude, and that is the property `E6-T1`'s source map will be built on. It is far
    /// cheaper to hold now than to reconstruct later, so it is asserted rather than intended.
    /// </summary>
    [Fact]
    public void WeavingDoesNotMoveAnyLine()
    {
        const string source = """
            class C
            {
                static int Run(System.Threading.CancellationToken __token)
                {
                    var total = 0;
                    for (var i = 0; i < 3; i++)
                        total += i;

                    int twice(int n) => n * 2;

                    while (total > 100) { total--; }

                    return twice(total);
                }
            }
            """;

        CancellationToken token = TestContext.Current.CancellationToken;
        SyntaxTree parsed = CSharpSyntaxTree.ParseText(source, cancellationToken: token);
        SyntaxNode woven = new GuardWeaver().Weave(parsed.GetRoot(token));

        Assert.Equal(
            source.ReplaceLineEndings("\n").Split('\n').Length,
            woven.ToFullString().ReplaceLineEndings("\n").Split('\n').Length);
    }

    /// <summary>
    /// Runs an assertion on a worker and reports whether it finished, so a guard that is missing
    /// fails the test instead of hanging the suite.
    /// </summary>
    private static bool Ran(Action assertion)
    {
        Task work = Task.Run(assertion);

        return work.Wait(MustStopWithin) && Finished(work);
    }

    private static bool Finished(Task work)
    {
        Debug.Assert(work.IsCompleted, "Only called once the wait returned.");

        // Rethrows on the calling thread, unwrapped, so an assertion failure inside the worker
        // reads as itself rather than as an AggregateException.
        work.GetAwaiter().GetResult();

        return true;
    }
}
