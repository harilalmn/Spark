using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Spark.Scripting;

/// <summary>Why a running code block was stopped.</summary>
public enum ScriptStopReason
{
    /// <summary>The node's time budget ran out.</summary>
    TimeBudget = 0,

    /// <summary>The evaluation run was cancelled.</summary>
    Cancelled = 1,

    /// <summary>
    /// The script came close enough to exhausting the stack that the next call would very likely
    /// have overflowed it.
    /// </summary>
    StackDepth = 2,
}

/// <summary>
/// Thrown inside a code block when <see cref="ScriptGuard"/> stops it. Ordinary and catchable, which
/// is the entire point: it unwinds through the evaluator like any other node failure.
/// </summary>
public sealed class ScriptStoppedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="reason">Why the script was stopped.</param>
    /// <param name="message">What happened, phrased for the person who wrote the script.</param>
    public ScriptStoppedException(ScriptStopReason reason, string message) : base(message) => Reason = reason;

    /// <summary>Creates the exception with no reason recorded. Provided for the framework's benefit.</summary>
    public ScriptStoppedException() => Reason = ScriptStopReason.Cancelled;

    /// <summary>Creates the exception. Provided for the framework's benefit.</summary>
    /// <param name="message">The message.</param>
    public ScriptStoppedException(string message) : base(message) => Reason = ScriptStopReason.Cancelled;

    /// <summary>Creates the exception. Provided for the framework's benefit.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ScriptStoppedException(string message, Exception innerException) : base(message, innerException) =>
        Reason = ScriptStopReason.Cancelled;

    /// <summary>Why the script was stopped.</summary>
    public ScriptStopReason Reason { get; }
}

/// <summary>
/// The runtime half of the guards woven into every code block: a cheap check at the top of every
/// loop body and on entry to every method the script declares.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this actually promises, and what it does not.</b> A <c>while (true)</c> in a code block
/// would otherwise occupy an evaluator thread for the life of the process, and .NET has had no
/// thread abort since Framework. So the way out is built in on the way down: the rewriter puts a
/// <see cref="Tick"/> at the top of every loop body, and the loop asks on each turn whether it has
/// outstayed its budget or the run has been cancelled. A script that is genuinely stuck is stopped
/// with an ordinary exception.
/// </para>
/// <para>
/// Two things this cannot catch, and saying so is more useful than implying otherwise. A single call
/// that blocks forever — a socket read, a <c>Thread.Sleep</c>, a library call that never returns —
/// never reaches a loop of ours to ask. And a loop body that swallows every exception keeps
/// spinning, though noisily rather than silently, because the stop repeats on every subsequent turn.
/// </para>
/// <para>
/// <b><see cref="StackOverflowException"/> cannot be caught in .NET.</b> The runtime does not raise
/// it; it fails fast and terminates the process, taking any unsaved graph with it. <see cref="Enter"/>
/// calls <see cref="RuntimeHelpers.EnsureSufficientExecutionStack"/>, which throws a
/// <i>catchable</i> exception while there is still stack left to unwind — so runaway recursion
/// usually becomes a message naming the method instead of the application vanishing. That reduces
/// the frequency. It is not a guarantee, and nothing in-process can make it one; only an
/// out-of-process worker would, and that is deliberately deferred (ADR-0008).
/// </para>
/// <para>
/// State is <see cref="ThreadStaticAttribute"/>, not static: the evaluator runs nodes in parallel
/// within a level, so two code blocks can be inside <see cref="Begin"/> at the same moment on
/// different threads and must not see each other's deadline.
/// </para>
/// </remarks>
public static class ScriptGuard
{
    /// <summary>
    /// The widest stride between two looks at the clock, as a power of two minus one.
    /// </summary>
    /// <remarks>
    /// Sixty-four turns is chosen for the promise it makes rather than the cost it saves. A
    /// timestamp is around 25 ns, so at this stride it adds well under a nanosecond to a turn. What
    /// it buys is a statable bound on the one blind spot in the design: a loop that runs fast enough
    /// to open the stride right up and then turns slow is not looked at again for another
    /// sixty-four turns, whatever a turn then costs.
    /// </remarks>
    private const int MaxMask = 0x3F;

    private static readonly long LookInterval = Stopwatch.Frequency / 50;

    [ThreadStatic] private static Scope? _scope;
    [ThreadStatic] private static int _turns;
    [ThreadStatic] private static int _mask;
    [ThreadStatic] private static long _lastLook;

    /// <summary>
    /// Opens a budget for one code block invocation on the calling thread. Dispose the result to
    /// close it; the guard is inert outside a scope.
    /// </summary>
    /// <param name="budget">
    /// How long the block may run before it is stopped. <see cref="TimeSpan.Zero"/> or less means no
    /// time limit, leaving cancellation as the only way out.
    /// </param>
    /// <param name="cancellationToken">Checked on every look at the clock.</param>
    /// <returns>The scope. Disposing it restores whatever scope was in force before.</returns>
    public static IDisposable Begin(TimeSpan budget, CancellationToken cancellationToken = default) =>
        new Scope(budget, cancellationToken);

    /// <summary>
    /// Called at the top of every loop body in a code block. Keep it cheap: the common turn is an
    /// increment, a mask and a branch.
    /// </summary>
    /// <exception cref="ScriptStoppedException">The budget ran out, or the run was cancelled.</exception>
    public static void Tick()
    {
        if ((++_turns & _mask) != 0)
        {
            return;
        }

        Look();
    }

    /// <summary>
    /// Called on entry to every method, local function and accessor a code block declares.
    /// </summary>
    /// <remarks>
    /// This is the recursion half of the guard. See the type-level remarks for why it reduces the
    /// frequency of a process-killing <see cref="StackOverflowException"/> rather than preventing
    /// one.
    /// </remarks>
    /// <exception cref="ScriptStoppedException">
    /// The stack is nearly exhausted, the budget ran out, or the run was cancelled.
    /// </exception>
    public static void Enter()
    {
        try
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }
        catch (InsufficientExecutionStackException)
        {
            throw new ScriptStoppedException(
                ScriptStopReason.StackDepth,
                "Stopped: the code block ran out of stack. Almost always this is a method calling itself with "
                + "nothing to stop it. It was caught before the stack was gone, because a real overflow would "
                + "have taken the whole application with it.");
        }

        Tick();
    }

    /// <summary>The slow half, kept out of <see cref="Tick"/> so the common turn stays branch-only.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Look()
    {
        Scope? scope = _scope;

        if (scope is null)
        {
            // Nothing is being timed — a code block's own method called from somewhere else, or a
            // stale delegate. Stop paying for the timestamp.
            _mask = MaxMask;
            return;
        }

        long now = Stopwatch.GetTimestamp();
        Adapt(now);

        if (scope.CancellationToken.IsCancellationRequested)
        {
            throw new ScriptStoppedException(
                ScriptStopReason.Cancelled,
                "Stopped: the evaluation run was cancelled while this code block was still looping.");
        }

        if (scope.Deadline != long.MaxValue && now >= scope.Deadline)
        {
            double elapsed = (now - scope.Started) / (double)Stopwatch.Frequency;
            throw new ScriptStoppedException(
                ScriptStopReason.TimeBudget,
                $"Stopped after {elapsed:0.0} s. A loop in this code block was still going, and it holds an "
                + "evaluator thread while it runs. Raise the node's time budget if the work is genuinely that long.");
        }
    }

    /// <summary>
    /// Keeps the interval between two looks near <see cref="LookInterval"/>, whatever the loop is
    /// doing.
    /// </summary>
    /// <remarks>
    /// Without this the choice is between a timestamp every turn, which a tight loop feels, and a
    /// fixed stride, which a slow loop takes far too long to reach the end of. The stride is
    /// recomputed from the rate just measured rather than nudged, because a nudge recovers too
    /// slowly from a bad sample: a thread descheduled for a moment looks exactly like a loop that
    /// got slower.
    /// </remarks>
    private static void Adapt(long now)
    {
        long since = now - _lastLook;
        _lastLook = now;

        long turns = _mask + 1L;

        // Growth is capped at eight times a look so one unrepresentative sample cannot widen the
        // stride out of all proportion. Shrinking is not capped: being wrong in that direction
        // means being slow to notice the deadline, which is the one thing that must not happen.
        long want = since <= 0 ? turns * 8 : Math.Min(turns * LookInterval / since, turns * 8);

        _mask = MaskFor(want);
    }

    private static int MaskFor(long want)
    {
        if (want <= 1)
        {
            return 0;
        }

        if (want > MaxMask)
        {
            return MaxMask;
        }

        int mask = 0;
        while ((((mask << 1) | 1) + 1L) <= want)
        {
            mask = (mask << 1) | 1;
        }

        return mask;
    }

    private sealed class Scope : IDisposable
    {
        private readonly Scope? _previous;

        internal Scope(TimeSpan budget, CancellationToken cancellationToken)
        {
            _previous = _scope;

            Started = Stopwatch.GetTimestamp();
            CancellationToken = cancellationToken;
            Deadline = budget <= TimeSpan.Zero
                ? long.MaxValue
                : Started + (long)(budget.TotalSeconds * Stopwatch.Frequency);

            _scope = this;
            _turns = 0;
            _mask = 0;
            _lastLook = Started;
        }

        internal long Started { get; }

        internal long Deadline { get; }

        internal CancellationToken CancellationToken { get; }

        public void Dispose()
        {
            _scope = _previous;
            _turns = 0;
            _mask = 0;
            _lastLook = Stopwatch.GetTimestamp();
        }
    }
}
