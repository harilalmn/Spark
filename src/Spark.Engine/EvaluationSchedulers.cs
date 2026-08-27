using System;
using System.Collections.Generic;
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
