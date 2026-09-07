using System;
using System.Threading;
using Avalonia.Headless;

namespace Spark.UI.Tests;

/// <summary>
/// The one headless Avalonia session this assembly runs on.
/// </summary>
/// <remarks>
/// <b>There can be exactly one.</b> `HeadlessUnitTestSession.StartNew` builds an
/// <c>Application</c> and takes over the dispatcher, and a second call in the same process leaves
/// both sessions broken — every test in both classes fails, with no message that points at the
/// cause. That is what happened the moment a second test class wanted a window
/// (`E11-T21`), so the session moved here and both classes share it.
/// </remarks>
internal static class HeadlessSession
{
    private static readonly HeadlessUnitTestSession Instance =
        HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));

    /// <summary>
    /// <b>One dispatch at a time</b>, because there is one session and xunit has sixteen threads.
    /// </summary>
    /// <remarks>
    /// The session owns a single UI thread. Twenty-six test classes reach it through
    /// <see cref="Run"/>, xunit runs their collections in parallel, and two threads calling
    /// <c>Dispatch</c> at once raced the lazy construction of the compositor: the loser got
    /// <i>The calling thread cannot access this object because a different thread owns it</i>,
    /// thrown from inside <c>ServerCompositor</c>'s constructor with a stack naming nothing in this
    /// repository. It failed roughly one full run in eight and named a different victim each time —
    /// <c>TheWheelDolliesTheCamera</c>, <c>TheDeferredFitDoesNotRepeatOnEveryLayout</c> — which is
    /// what a shared-state race looks like from the outside and is why it read as three unrelated
    /// flakes rather than as one defect.
    /// </remarks>
    private static readonly SemaphoreSlim Turnstile = new(1, 1);

    /// <summary>Runs a body on the session's UI thread and waits for it.</summary>
    /// <param name="body">What to run.</param>
    /// <remarks>
    /// <b>Serialised, and only this is.</b> Tests that never touch a window keep running in
    /// parallel — the arithmetic ones are most of the assembly — so the cost is paid by the tests
    /// that genuinely need the one UI thread, which could never have run at the same time anyway.
    /// Disabling parallelism for the whole assembly was the alternative and is a much larger
    /// hammer: it would serialise 940 tests to fix a race between the few dozen that show a window.
    /// </remarks>
    public static void Run(Action body)
    {
        Turnstile.Wait();

        try
        {
            Instance.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            Turnstile.Release();
        }
    }
}
