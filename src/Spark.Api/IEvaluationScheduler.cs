using System;
using System.Collections.Generic;
using System.Threading;

namespace Spark.Api;

/// <summary>
/// Runs one level of a graph evaluation. The evaluator hands over a batch of independent
/// operations and the scheduler decides where and how they run.
/// </summary>
/// <remarks>
/// <para>
/// This one interface is the entire embedding mechanism. Spark evaluates a graph by topologically
/// sorting it into levels, where everything in a level is independent of everything else in the
/// same level; the scheduler is what turns that into work. A desktop session runs the batch in
/// parallel, the CLI runs it sequentially so results are deterministic, and a CAD add-in runs it
/// on the host's own thread because Revit and AutoCAD APIs are not callable from anywhere else.
/// </para>
/// <para>
/// It only works because evaluation never assumes it owns its thread. An implementation may run
/// the operations in any order, on any thread, as long as it does not return until all of them
/// have finished.
/// </para>
/// </remarks>
public interface IEvaluationScheduler
{
    /// <summary>
    /// Runs every operation and does not return until all of them have completed, or until one of
    /// them throws.
    /// </summary>
    /// <param name="operations">
    /// Independent operations. They may run in any order and concurrently with each other.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the batch. An implementation should stop starting new operations promptly; the
    /// operations themselves also observe the token.
    /// </param>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    void Run(IReadOnlyList<Action> operations, CancellationToken cancellationToken);
}
