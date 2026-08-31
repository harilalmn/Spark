using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// Runs a level's nodes on a host application's own thread (<c>E12-T3</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the entire embedding mechanism.</b> Revit's and AutoCAD's APIs are callable from one
/// thread and no other, so a node that asks the host for a wall or a layer must run there. Spark's
/// evaluator never assumed it owned its thread — that is the whole reason
/// <see cref="IEvaluationScheduler"/> exists — so making Spark work inside a CAD application is a
/// matter of supplying this rather than of porting anything.
/// </para>
/// <para>
/// <b>The host supplies two delegates and nothing else.</b> One says whether the calling thread is
/// already the host's; the other runs a delegate on the host's thread and does not return until it
/// has finished. Every host that can be embedded in has both, under some name of its own —
/// Revit calls the second an external event, AutoCAD a document-lock invoke, a WPF or Avalonia
/// shell a dispatcher <c>Invoke</c> — and asking for the two of them rather than for a named type
/// keeps this file free of every one of them.
/// </para>
/// <para>
/// <b>Running inline when already on the host thread is not an optimisation.</b> A host thread
/// almost always services its own marshalled work in a message loop, so asking it to run something
/// and then blocking that same thread waiting for the answer is a deadlock — and it is the *first*
/// thing that happens, because a CAD add-in evaluates in response to the host calling it. The check
/// is therefore the first line of <see cref="Run"/>, not a special case at the end.
/// </para>
/// <para>
/// <b>Sequential, because a host thread is one thread.</b> A level's operations are independent of
/// each other by construction, so running them one after another is correct; it is simply slower
/// than the desktop's parallel scheduler, which is the price of the API being where it is.
/// </para>
/// </remarks>
public sealed class HostThreadEvaluationScheduler : IEvaluationScheduler
{
    private readonly Func<bool> _onHostThread;
    private readonly Action<Action> _invoke;

    /// <summary>Creates a scheduler over a host's thread.</summary>
    /// <param name="isOnHostThread">
    /// Whether the calling thread is the host's own. Called on every batch, so it should be cheap;
    /// a comparison of <see cref="Environment.CurrentManagedThreadId"/> is the usual shape.
    /// </param>
    /// <param name="invokeOnHostThread">
    /// Runs a delegate on the host's thread and <b>does not return until it has finished</b>. An
    /// implementation that returns early turns every evaluation into a race.
    /// </param>
    /// <exception cref="ArgumentNullException">Either delegate is null.</exception>
    public HostThreadEvaluationScheduler(Func<bool> isOnHostThread, Action<Action> invokeOnHostThread)
    {
        ArgumentNullException.ThrowIfNull(isOnHostThread);
        ArgumentNullException.ThrowIfNull(invokeOnHostThread);

        _onHostThread = isOnHostThread;
        _invoke = invokeOnHostThread;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>The whole batch is marshalled once, not each operation.</b> A level of two hundred nodes
    /// marshalled one at a time is two hundred round trips through a message loop, and on a host
    /// that pumps between them it is also two hundred opportunities for the user to start something
    /// else in the middle of an evaluation.
    /// </remarks>
    public void Run(IReadOnlyList<Action> operations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operations);

        if (operations.Count == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        if (_onHostThread())
        {
            RunHere(operations, cancellationToken);
            return;
        }

        // The exception has to cross the marshal. Letting it escape the delegate leaves it wherever
        // the host decided to run it - which for an external event or a dispatcher post is a thread
        // the caller cannot see - and the evaluation would appear to have succeeded with a node
        // silently missing its output.
        ExceptionDispatchInfo? failure = null;

        _invoke(() =>
        {
            try
            {
                RunHere(operations, cancellationToken);
            }
            catch (Exception thrown)
            {
                failure = ExceptionDispatchInfo.Capture(thrown);
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
