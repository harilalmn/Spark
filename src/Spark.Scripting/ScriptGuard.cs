using System;
using System.Globalization;
using System.Threading;

namespace Spark.Scripting;

/// <summary>
/// The counters a woven script checks against: how many loop iterations one invocation has run,
/// and how deep its recursion has gone.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is called only by generated code</b> (`E6-T4`). <see cref="GuardWeaver"/> rewrites
/// a script's syntax tree so that every loop body begins with <see cref="Tick"/> and every local
/// function is bracketed by <see cref="Enter"/> and <see cref="Exit"/>. Nothing a user writes calls
/// it directly, and nothing in Spark outside the weaver should.
/// </para>
/// <para>
/// <b>Why the counters are thread-static rather than passed in.</b> The replicator evaluates a
/// replicated code block on several threads at once, and each of those invocations needs its own
/// budget — a shared counter would let a wide list exhaust a ceiling that no single item came close
/// to, and would report the wrong node. A field per thread costs one static read at each guard and
/// needs no allocation, no parameter and no closure, which matters because <see cref="Tick"/> runs
/// once per iteration of every loop the user wrote.
/// </para>
/// <para>
/// <b>The two ceilings do different jobs, and only one of them is really about safety.</b> The
/// cancellation check is the mechanism a user actually experiences: they press Escape, the
/// evaluation's token is cancelled, and the loop stops. The iteration ceiling is what stops a
/// runaway script where nobody is watching — <c>spark run</c> in a build, a headless host — and it
/// is deliberately generous, because a ceiling low enough to be a safety net for a bad script is
/// also low enough to break a good one. The depth ceiling is the exception: it is the only defence
/// there is, because <see cref="StackOverflowException"/> cannot be caught in .NET and terminates
/// the process (<c>R11</c> in the PRD). Depth must therefore be bounded *before* the stack runs
/// out, never caught after.
/// </para>
/// </remarks>
public static class ScriptGuard
{
    /// <summary>
    /// The default ceiling on loop iterations in one invocation of one code block.
    /// </summary>
    /// <remarks>
    /// A hundred million guarded iterations is a fraction of a second of <c>while (true) { }</c>
    /// and far more than any script that is doing real work per iteration will reach. It is a
    /// runaway detector, not a quota.
    /// </remarks>
    public const long DefaultIterationLimit = 100_000_000L;

    /// <summary>The default ceiling on recursion depth inside one invocation.</summary>
    /// <remarks>
    /// Chosen well below where a one-megabyte stack actually fails, because the frames a code block
    /// produces are unusually large: <c>dynamic</c> call sites carry binder state, and the generated
    /// entry point holds every input. Overshooting the real limit costs the process; undershooting
    /// costs a diagnostic the user can read.
    /// </remarks>
    public const int DefaultDepthLimit = 512;

    [ThreadStatic]
    private static long _iterations;

    [ThreadStatic]
    private static long _iterationLimit;

    [ThreadStatic]
    private static int _depth;

    [ThreadStatic]
    private static int _depthLimit;

    /// <summary>Starts one invocation, resetting both counters.</summary>
    /// <param name="iterationLimit">The ceiling on loop iterations.</param>
    /// <param name="depthLimit">The ceiling on recursion depth.</param>
    /// <remarks>
    /// Woven as the first statement of the generated entry point, so the budget is per invocation
    /// rather than per node or per session. Resetting on entry rather than restoring on exit is
    /// what makes an invocation that threw leave nothing behind for the next one on that thread.
    /// </remarks>
    public static void Begin(long iterationLimit, int depthLimit)
    {
        _iterations = 0;
        _depth = 0;
        _iterationLimit = iterationLimit;
        _depthLimit = depthLimit;
    }

    /// <summary>One turn of a loop the weaver rewrote.</summary>
    /// <param name="cancellationToken">The evaluation's token.</param>
    /// <exception cref="OperationCanceledException">The evaluation was cancelled.</exception>
    /// <exception cref="ScriptGuardException">The iteration ceiling was passed.</exception>
    /// <remarks>
    /// <b>The token is checked every iteration, not every n.</b> Sampling would make the guard
    /// cheaper on a tight arithmetic loop and would make cancellation arbitrarily slow on a loop
    /// whose body blocks — a body containing one network call would take a thousand of them before
    /// noticing. <see cref="CancellationToken.ThrowIfCancellationRequested"/> on an uncancelled
    /// token is a field read, so the cheap case is already cheap.
    /// </remarks>
    public static void Tick(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_iterationLimit == 0)
        {
            // A thread the script started itself never ran `Begin`, so its budget is zero and the
            // very first turn of its loop would be reported as a runaway. It is given the default
            // budget instead: a script that starts a thread is unusual, and failing it with a
            // message about a limit it never approached would be a lie.
            _iterationLimit = DefaultIterationLimit;
            _depthLimit = DefaultDepthLimit;
        }

        if (++_iterations > _iterationLimit)
        {
            throw new ScriptGuardException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The script ran more than {0:N0} loop iterations and was stopped. If that is genuinely the work, do it in a custom node rather than a code block.",
                    _iterationLimit));
        }
    }

    /// <summary>Enters a local function the weaver bracketed.</summary>
    /// <exception cref="ScriptGuardException">The depth ceiling was passed.</exception>
    public static void Enter()
    {
        if (_depthLimit == 0)
        {
            // Same reasoning as `Tick`: a thread the script started has no budget of its own.
            _depthLimit = DefaultDepthLimit;
            _iterationLimit = _iterationLimit == 0 ? DefaultIterationLimit : _iterationLimit;
        }

        if (++_depth > _depthLimit)
        {
            // Undone before throwing, so the `finally` that matches this `Enter` does not take the
            // count below zero — the throw happens *instead of* the call, and the matching Exit
            // still runs on the way out.
            _depth--;

            throw new ScriptGuardException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The script recursed more than {0:N0} levels deep and was stopped before the stack overflowed. A stack overflow cannot be caught in .NET and would end the whole application, so this limit is a hard one.",
                    _depthLimit));
        }
    }

    /// <summary>Leaves a local function the weaver bracketed.</summary>
    public static void Exit() => _depth--;
}

/// <summary>
/// Thrown when a script passes one of the ceilings the guard weaver wove into it.
/// </summary>
/// <remarks>
/// A type of its own so a host can tell <i>the script was stopped by its guards</i> from <i>the
/// script threw</i>, and so it is never mistaken for cancellation: an
/// <see cref="OperationCanceledException"/> means the user asked, and the replicator treats it as
/// such, while this means nobody asked and the script would not have stopped.
/// </remarks>
public sealed class ScriptGuardException : Exception
{
    /// <summary>Creates the exception with a message a user can act on.</summary>
    /// <param name="message">What ceiling was passed, and what to do about it.</param>
    public ScriptGuardException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public ScriptGuardException() : base("The script passed one of its guard limits.")
    {
    }

    /// <summary>Creates the exception wrapping another.</summary>
    /// <param name="message">What ceiling was passed.</param>
    /// <param name="innerException">The cause.</param>
    public ScriptGuardException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
