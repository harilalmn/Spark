using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The host-thread scheduler: the whole of what embedding Spark in a CAD application takes
/// (<c>E12-T3</c>).
/// </summary>
/// <remarks>
/// <b>The tests run a real second thread with a real work queue</b>, because every interesting
/// property of this type is about which thread something happened on, and a fake that ran
/// everything inline would assert nothing at all.
/// </remarks>
public sealed class HostThreadSchedulerTests
{
    /// <summary>
    /// <b>Every operation runs on the host's thread.</b> The one thing a CAD add-in needs, because
    /// its API is callable from there and nowhere else.
    /// </summary>
    [Fact]
    public void EveryOperationRunsOnTheHostThread()
    {
        using HostThread host = new();

        ConcurrentBag<int> threads = [];
        List<Action> work = [.. Enumerable.Range(0, 8).Select<int, Action>(
            _ => () => threads.Add(Environment.CurrentManagedThreadId))];

        host.Scheduler.Run(work, CancellationToken.None);

        Assert.Equal(8, threads.Count);
        Assert.All(threads, id => Assert.Equal(host.ThreadId, id));
    }

    /// <summary>
    /// <b>Called from the host thread itself, it runs inline and does not marshal.</b> A host
    /// thread services its own marshalled work, so asking it to run something and then blocking it
    /// waiting for the answer is a deadlock — and it is the first thing that happens, because an
    /// add-in evaluates in response to the host calling it.
    /// </summary>
    [Fact]
    public void CalledFromTheHostThreadItRunsInlineRatherThanDeadlocking()
    {
        using HostThread host = new();

        bool ran = false;
        int marshalsInside = -1;

        // The host's invoke would deadlock if it were used from its own thread: it posts to the
        // queue that the calling thread is the one draining, and then waits. So this test hanging
        // *is* the failure, and the count below is what tells a reader why it did not.
        int before = host.MarshalCount;

        host.Run(() =>
        {
            host.Scheduler.Run([() => ran = true], CancellationToken.None);
            marshalsInside = host.MarshalCount;
        });

        Assert.True(ran);
        Assert.Equal(before + 1, marshalsInside);
    }

    /// <summary>The batch is marshalled once, not once per operation.</summary>
    /// <remarks>
    /// A level of two hundred nodes marshalled one at a time is two hundred round trips through a
    /// message loop, and on a host that pumps between them it is two hundred chances for the user
    /// to start something else in the middle of an evaluation.
    /// </remarks>
    [Fact]
    public void TheBatchIsMarshalledOnce()
    {
        using HostThread host = new();

        host.Scheduler.Run([.. Enumerable.Range(0, 20).Select<int, Action>(_ => () => { })], CancellationToken.None);

        Assert.Equal(1, host.MarshalCount);
    }

    /// <summary>
    /// <b>An exception comes back to the caller.</b> Left on the far side of a marshal it would
    /// vanish onto a thread nobody is watching, and the evaluation would look successful with a
    /// node silently missing its output.
    /// </summary>
    [Fact]
    public void AnExceptionCrossesTheMarshalBackToTheCaller()
    {
        using HostThread host = new();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => host.Scheduler.Run(
                [() => throw new InvalidOperationException("from the host thread")],
                CancellationToken.None));

        Assert.Equal("from the host thread", thrown.Message);
    }

    /// <summary>And its original stack survives, so it names the node that threw.</summary>
    [Fact]
    public void TheOriginalStackSurvives()
    {
        using HostThread host = new();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => host.Scheduler.Run([Boom], CancellationToken.None));

        Assert.Contains(nameof(Boom), thrown.StackTrace, StringComparison.Ordinal);
    }

    /// <summary>Cancellation stops the batch, and is observed before the first operation.</summary>
    [Fact]
    public void CancellationStopsTheBatch()
    {
        using HostThread host = new();
        using CancellationTokenSource cancelled = new();

        cancelled.Cancel();

        int ran = 0;

        _ = Assert.ThrowsAny<OperationCanceledException>(
            () => host.Scheduler.Run([() => ran++, () => ran++], cancelled.Token));

        Assert.Equal(0, ran);
    }

    /// <summary>An empty batch marshals nothing and still observes cancellation.</summary>
    [Fact]
    public void AnEmptyBatchMarshalsNothing()
    {
        using HostThread host = new();

        host.Scheduler.Run([], CancellationToken.None);

        Assert.Equal(0, host.MarshalCount);
    }

    /// <summary>Both delegates are required, because neither has a safe default.</summary>
    [Fact]
    public void BothDelegatesAreRequired()
    {
        _ = Assert.Throws<ArgumentNullException>(
            () => new HostThreadEvaluationScheduler(null!, _ => { }));

        _ = Assert.Throws<ArgumentNullException>(
            () => new HostThreadEvaluationScheduler(() => true, null!));
    }

    /// <summary>
    /// <b>A whole graph evaluates through it, on the host thread.</b> The unit tests above check
    /// the scheduler; this checks that the evaluator is genuinely indifferent to where it runs,
    /// which is the claim embedding rests on.
    /// </summary>
    [Fact]
    public void AGraphEvaluatesEntirelyOnTheHostThread()
    {
        using HostThread host = new();

        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));

        Graph graph = new();
        NodeId value = graph.AddNode(library.Get(new NodeKey("Spark.Nodes.Core", "Number.Value"))).Id;
        NodeId sum = graph.AddNode(library.Get(new NodeKey("Spark.Nodes.Core", "Math.Add"))).Id;

        graph.SetLiteral(value, 0, 21.0);
        graph.SetLiteral(sum, 1, 21.0);
        graph.LoadWire(value, 0, sum, 0);

        EvaluationResult result = GraphEvaluator.Evaluate(
            graph, new EvaluationContext(scheduler: host.Scheduler), TestContext.Current.CancellationToken);

        Assert.Equal(42.0, result.Value(sum));
        Assert.True(host.MarshalCount > 0, "nothing was marshalled, so nothing ran on the host thread");
    }

    private static void Boom() => throw new InvalidOperationException("boom");

    /// <summary>
    /// A stand-in for Revit's or AutoCAD's single API thread: one thread draining a queue, and an
    /// invoke that posts to that queue and waits.
    /// </summary>
    private sealed class HostThread : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = [];
        private readonly Thread _thread;
        private int _marshals;

        internal HostThread()
        {
            _thread = new Thread(Pump) { IsBackground = true, Name = "FakeHostThread" };
            _thread.Start();

            Scheduler = new HostThreadEvaluationScheduler(
                () => Environment.CurrentManagedThreadId == ThreadId,
                Run);
        }

        internal HostThreadEvaluationScheduler Scheduler { get; }

        internal int ThreadId => _thread.ManagedThreadId;

        internal int MarshalCount => Volatile.Read(ref _marshals);

        /// <summary>Posts to the host thread and waits, the way a dispatcher invoke does.</summary>
        internal void Run(Action work)
        {
            // A real dispatcher would deadlock here: it would post to the queue that this very
            // thread is draining and then wait for it. Throwing instead means a scheduler that
            // marshalled when it should not fails the test loudly in a second, rather than hanging
            // it for the length of the run and telling nobody why.
            if (Environment.CurrentManagedThreadId == ThreadId)
            {
                throw new InvalidOperationException(
                    "the scheduler marshalled onto the host thread from the host thread, which "
                    + "deadlocks a real host");
            }

            _ = Interlocked.Increment(ref _marshals);

            using ManualResetEventSlim done = new(false);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;

            _queue.Add(() =>
            {
                try
                {
                    work();
                }
                catch (Exception thrown)
                {
                    failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(thrown);
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait();
            failure?.Throw();
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _ = _thread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }

        private void Pump()
        {
            foreach (Action work in _queue.GetConsumingEnumerable())
            {
                work();
            }
        }
    }
}
