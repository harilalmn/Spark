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

    /// <summary>Runs a body on the session's UI thread and waits for it.</summary>
    /// <param name="body">What to run.</param>
    public static void Run(Action body) =>
        Instance.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();
}
