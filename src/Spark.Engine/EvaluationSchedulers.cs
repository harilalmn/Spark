using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// Runs a level's nodes one after another, on the calling thread, in the order given.
/// </summary>
/// <remarks>
/// This is what the CLI and the documentation harness use. Determinism is the point: a run that
/// produces the same diagnostics in the same order every time is a run whose output can be a golden
/// file. It is also the implementation to reach for when debugging, because a stack trace means
/// something.
/// </remarks>
public sealed class SequentialEvaluationScheduler : IEvaluationScheduler
{
    /// <inheritdoc/>
    public void Run(IReadOnlyList<Action> operations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operations);

        foreach (Action operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation();
        }
    }
}

/// <summary>
/// Runs a level's nodes in parallel on the thread pool.
/// </summary>
/// <remarks>
/// This is the desktop default. It is safe because a level contains only nodes that are independent
/// of each other by construction — that is what the topological sort produces — and because node
/// results and cache entries are the only shared state, both of which are written under a lock.
/// </remarks>
public sealed class ParallelEvaluationScheduler : IEvaluationScheduler
{
    /// <inheritdoc/>
    public void Run(IReadOnlyList<Action> operations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operations);

        if (operations.Count <= 1)
        {
            foreach (Action operation in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation();
            }

            return;
        }

        ParallelOptions options = new() { CancellationToken = cancellationToken };
        Parallel.ForEach(operations, options, operation => operation());
    }
}

/// <summary>
/// Runs a level's nodes on a thread the host owns, one after another, and does not return until
/// they have finished.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the scheduler a CAD add-in needs, and most of why the seam exists.</b> The Revit and
/// AutoCAD APIs are callable from one thread and one thread only; a node that touches the host
/// model from anywhere else does not fail cleanly, it corrupts or crashes. Spark evaluates on a
/// worker thread by design, so something has to carry each level across, and this is it.
/// </para>
/// <para>
/// <b>The whole batch crosses in one hop, not one hop per node.</b> A round trip to a host thread
/// is a queued message and a wait; a node is often a few microseconds of arithmetic. Marshalling
/// per operation would turn a two-thousand-node level into two thousand round trips and make the
/// scheduler cost more than the work it schedules.
/// </para>
/// <para>
/// <b>Operations run sequentially, and that is not a limitation to be worked around.</b> The host
/// thread is one thread. A scheduler that started tasks inside the marshalled callback would be
/// running host API calls off the host thread again, which is the exact failure this class exists
/// to prevent.
/// </para>
/// <para>
/// <b>Re-entrancy is the deadlock this design is most likely to hit, so it is handled rather than
/// documented as a caveat.</b> When evaluation is already running on the host thread — a host that
/// calls Spark from a command handler, say — marshalling again would block that thread waiting for
/// itself on any marshaller that is not re-entrant, and most are not. Supply
/// <c>isOnHostThread</c> and the batch runs inline instead. Without it, the scheduler cannot tell
/// the two cases apart and takes the marshalling path every time.
/// </para>
/// <para>
/// <b>Cancellation is checked before each operation and cannot interrupt the hop.</b> Once the
/// batch has been handed to the host there is no general way to take it back — the marshaller is
/// the host's and its contract is not ours. The token stops the batch between operations and
/// stops the next level from starting, which is the same granularity
/// <see cref="SequentialEvaluationScheduler"/> offers and is stated here so that nobody expects
/// more from it.
/// </para>
/// </remarks>
public sealed class HostThreadEvaluationScheduler : IEvaluationScheduler
{
    private readonly Action<Action> _marshal;
    private readonly Func<bool>? _isOnHostThread;

    /// <summary>
    /// Creates a scheduler over a host's marshalling primitive.
    /// </summary>
    /// <param name="marshal">
    /// Runs a delegate on the host's thread and <b>does not return until it has finished</b>. A
    /// fire-and-forget post is not sufficient: the evaluator's contract is that a level is
    /// complete when <see cref="Run"/> returns, and the level after it reads what this one wrote.
    /// A WPF or Avalonia dispatcher's blocking invoke satisfies this; so does a Revit external
    /// event paired with a wait handle.
    /// </param>
    /// <param name="isOnHostThread">
    /// Answers whether the calling thread is already the host's. Optional, and strongly
    /// recommended: without it a call made from the host thread marshals to the host thread and
    /// deadlocks on any marshaller that is not re-entrant.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="marshal"/> is <see langword="null"/>.
    /// </exception>
    public HostThreadEvaluationScheduler(Action<Action> marshal, Func<bool>? isOnHostThread = null)
    {
        ArgumentNullException.ThrowIfNull(marshal);

        _marshal = marshal;
        _isOnHostThread = isOnHostThread;
    }

    /// <inheritdoc/>
    public void Run(IReadOnlyList<Action> operations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operations);

        if (operations.Count == 0)
        {
            // Nothing to do, and no reason to pay for a round trip to say so.
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (_isOnHostThread?.Invoke() == true)
        {
            RunHere(operations, cancellationToken);
            return;
        }

        ExceptionDispatchInfo? failure = null;

        _marshal(() =>
        {
            try
            {
                RunHere(operations, cancellationToken);
            }
            catch (Exception exception)
            {
                // Captured rather than rethrown here. An exception escaping into a host's
                // dispatcher is the host's problem to handle and it will handle it badly - a
                // Revit add-in that throws inside an external event takes the message box, not
                // the caller. ExceptionDispatchInfo carries the original stack across.
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });

        failure?.Throw();
    }

    private static void RunHere(IReadOnlyList<Action> operations, CancellationToken cancellationToken)
    {
        foreach (Action operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation();
        }
    }
}
