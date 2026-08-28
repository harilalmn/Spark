using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// The host-thread scheduler, driven against a stand-in for a CAD host's thread.
/// </summary>
/// <remarks>
/// The fixture below is a single worker thread with a queue and a wait handle, which is what a
/// Revit external event or a dispatcher's blocking invoke amounts to. Testing against a real one
/// is not possible here and would not be more convincing: what has to hold is that every node
/// runs on that one thread, that the batch crosses once, and that the re-entrant call does not
/// deadlock — and those are properties of this class rather than of anybody's dispatcher.
/// </remarks>
public sealed class HostThreadSchedulerTests
{
    [Fact]
    public void EveryOperationRunsOnTheHostThread()
    {
        using HostThread host = new();
        HostThreadEvaluationScheduler scheduler = new(host.Invoke, host.IsCurrent);

        ConcurrentBag<int> threads = [];
        List<Action> operations = [.. Enumerable.Range(0, 20).Select<int, Action>(
            _ => () => threads.Add(Environment.CurrentManagedThreadId))];

        scheduler.Run(operations, CancellationToken.None);

        Assert.Equal(20, threads.Count);
        Assert.Equal(host.ThreadId, Assert.Single(threads.Distinct()));
        Assert.NotEqual(Environment.CurrentManagedThreadId, host.ThreadId);
    }

    [Fact]
    public void TheWholeBatchCrossesInOneHop()
    {
        using HostThread host = new();
        HostThreadEvaluationScheduler scheduler = new(host.Invoke, host.IsCurrent);

        scheduler.Run([.. Enumerable.Range(0, 50).Select<int, Action>(_ => () => { })], CancellationToken.None);

        // Fifty operations, one round trip. A round trip is a queued message and a wait; a node
        // is a few microseconds of arithmetic, so a hop per operation would cost more than the
        // work it schedules.
        Assert.Equal(1, host.Hops);
    }

    [Fact]
    public void OperationsRunInOrderAndOneAtATime()
    {
        using HostThread host = new();
        HostThreadEvaluationScheduler scheduler = new(host.Invoke, host.IsCurrent);

        List<int> order = [];
        List<Action> operations = [.. Enumerable.Range(0, 10).Select<int, Action>(index => () => order.Add(index))];

        scheduler.Run(operations, CancellationToken.None);

        // The host thread is one thread. A scheduler that fanned out inside the callback would
        // be running host API calls off the host thread again, which is what this class exists
        // to prevent.
        Assert.Equal(Enumerable.Range(0, 10), order);
    }

    [Fact]
    public void RunDoesNotReturnUntilTheBatchHasFinished()
    {
        using HostThread host = new();
        HostThreadEvaluationScheduler scheduler = new(host.Invoke, host.IsCurrent);

        int completed = 0;
        List<Action> operations = [.. Enumerable.Range(0, 10).Select<int, Action>(
            _ => () =>
            {
                Thread.Sleep(1);
                Interlocked.Increment(ref completed);
            })];

        scheduler.Run(operations, CancellationToken.None);

        // The level after this one reads what this one wrote. A fire-and-forget post would
        // satisfy every other test here and break the evaluator.
        Assert.Equal(10, completed);
    }

    [Fact]
    public void ACallAlreadyOnTheHostThreadRunsInlineRatherThanDeadlocking()
    {
        using HostThread host = new();
        HostThreadEvaluationScheduler scheduler = new(host.Invoke, host.IsCurrent);

        int ran = 0;

        // A host calling Spark from its own command handler. Marshalling again would block the
        // host thread waiting for itself, because this marshaller - like most real ones - is
        // not re-entrant.
        host.Invoke(() => scheduler.Run([() => ran++], CancellationToken.None));

        Assert.Equal(1, ran);
        Assert.Equal(1, host.Hops);
    }

    [Fact]
    public void AnExceptionOnTheHostThreadReachesTheCallerWithItsStack()
    {
        using HostThread host = new();
        HostThreadEvaluationScheduler scheduler = new(host.Invoke, host.IsCurrent);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => scheduler.Run([Throwing], CancellationToken.None));

        Assert.Equal("from the host thread", thrown.Message);

        // The original stack, not one that starts at the rethrow. An add-in author reading this
        // needs to see which node failed, not which line of the scheduler re-raised it.
        Assert.Contains(nameof(Throwing), thrown.StackTrace, StringComparison.Ordinal);

        // And the host thread survived it: an exception escaping into a real dispatcher takes a
        // message box, or the process.
        Assert.False(host.Faulted);
    }

    [Fact]
    public void CancellationBeforeTheHopStopsTheBatchWithoutCrossing()
    {
        using HostThread host = new();
        HostThreadEvaluationScheduler scheduler = new(host.Invoke, host.IsCurrent);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => scheduler.Run([() => { }], cancelled.Token));

        Assert.Equal(0, host.Hops);
    }

    [Fact]
    public void CancellationPartWayThroughStopsBetweenOperations()
    {
        using HostThread host = new();
        HostThreadEvaluationScheduler scheduler = new(host.Invoke, host.IsCurrent);
        using CancellationTokenSource source = new();

        int ran = 0;
        List<Action> operations =
        [
            () => ran++,
            () =>
            {
                ran++;
                source.Cancel();
            },
            () => ran++,
        ];

        Assert.Throws<OperationCanceledException>(() => scheduler.Run(operations, source.Token));

        // Between operations, not inside one. The hop itself cannot be interrupted, because the
        // marshaller belongs to the host and its contract is not ours.
        Assert.Equal(2, ran);
    }

    [Fact]
    public void AnEmptyBatchDoesNotCrossAtAll()
    {
        using HostThread host = new();
        HostThreadEvaluationScheduler scheduler = new(host.Invoke, host.IsCurrent);

        scheduler.Run([], CancellationToken.None);

        Assert.Equal(0, host.Hops);
    }

    [Fact]
    public void WithoutAThreadCheckEveryCallMarshals()
    {
        using HostThread host = new();

        // The documented consequence of omitting isOnHostThread, asserted so that it is a known
        // cost rather than a surprise: a re-entrant call would take this path and deadlock on a
        // marshaller that is not re-entrant.
        HostThreadEvaluationScheduler scheduler = new(host.Invoke);

        scheduler.Run([() => { }], CancellationToken.None);
        scheduler.Run([() => { }], CancellationToken.None);

        Assert.Equal(2, host.Hops);
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        using HostThread host = new();

        Assert.Throws<ArgumentNullException>(() => new HostThreadEvaluationScheduler(null!));
        Assert.Throws<ArgumentNullException>(
            () => new HostThreadEvaluationScheduler(host.Invoke).Run(null!, CancellationToken.None));
    }


    /// <summary>
    /// A whole graph evaluates through the scheduler, and every node runs on the host thread.
    /// </summary>
    /// <remarks>
    /// The unit tests above check the scheduler against a batch of delegates. This one checks
    /// the thing an add-in author actually does: hand the scheduler to an evaluation context and
    /// run a graph. It is the only test here that would fail if the evaluator ever ran a node
    /// outside its scheduler.
    /// </remarks>
    [Fact]
    public void AGraphEvaluatedThroughTheSchedulerRunsEveryNodeOnTheHostThread()
    {
        using HostThread host = new();
        ConcurrentBag<int> threads = [];

        NodeDefinition watching = new(
            new NodeKey("Test", "Watching"),
            "Watching",
            [new PortDefinition("in", typeof(double), 0)],
            [new PortDefinition("out", typeof(double), 0)],
            arguments =>
            {
                threads.Add(Environment.CurrentManagedThreadId);
                return [(double)arguments[0]! + 1.0];
            },
            LacingMode.Longest,
            version: 1);

        Graph graph = new();
        NodeInstance first = graph.AddNode(watching);
        NodeInstance second = graph.AddNode(watching);
        graph.SetLiteral(first.Id, 0, 1.0);
        graph.TryConnect(first.Id, 0, second.Id, 0);

        EvaluationContext context = new(
            Tolerance.Default,
            new HostThreadEvaluationScheduler(host.Invoke, host.IsCurrent));

        EvaluationResult result = GraphEvaluator.Evaluate(graph, context, TestContext.Current.CancellationToken);

        Assert.Equal(3.0, result.Value(second.Id));
        Assert.Equal(host.ThreadId, Assert.Single(threads.Distinct()));

        // Two levels, two hops. One per node would be four, and one per graph would be one -
        // the level is the unit because a level is what the evaluator can hand over as a whole.
        Assert.Equal(2, host.Hops);
    }

    private static void Throwing() => throw new InvalidOperationException("from the host thread");

    /// <summary>
    /// A stand-in for a host's thread: one worker, a queue of one, and a blocking invoke.
    /// </summary>
    private sealed class HostThread : IDisposable
    {
        private readonly BlockingCollection<(Action Work, ManualResetEventSlim Done)> _queue = [];
        private readonly Thread _thread;
        private int _hops;

        public HostThread()
        {
            _thread = new Thread(Pump) { IsBackground = true, Name = "fake host thread" };
            _thread.Start();
            Started.Wait();
        }

        public int ThreadId => _thread.ManagedThreadId;

        public int Hops => Volatile.Read(ref _hops);

        public bool Faulted { get; private set; }

        private ManualResetEventSlim Started { get; } = new(false);

        public bool IsCurrent() => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

        public void Invoke(Action work)
        {
            Interlocked.Increment(ref _hops);

            if (IsCurrent())
            {
                // Deliberately NOT re-entrant beyond this: a real dispatcher that is pumped from
                // inside itself is the exception rather than the rule, and the scheduler must not
                // depend on it.
                work();
                return;
            }

            using ManualResetEventSlim done = new(false);
            _queue.Add((work, done));
            done.Wait();
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
            Started.Dispose();
        }

        private void Pump()
        {
            Started.Set();

            foreach ((Action work, ManualResetEventSlim done) in _queue.GetConsumingEnumerable())
            {
                try
                {
                    work();
                }
                catch (Exception)
                {
                    // A real host would show a message box or die here, which is exactly why the
                    // scheduler must not let an exception reach this point.
                    Faulted = true;
                }
                finally
                {
                    done.Set();
                }
            }
        }
    }
}
