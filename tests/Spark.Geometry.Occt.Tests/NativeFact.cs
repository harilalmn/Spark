using System;
using Spark.Api;
using Spark.Geometry.Occt;

namespace Spark.Geometry.Occt.Tests;

/// <summary>
/// A fact that needs the native provider, and skips rather than fails without it.
/// </summary>
/// <remarks>
/// <b>Skipping is the correct behaviour, not a convenience.</b> A build with no native component
/// present is a configuration Spark supports on purpose (ADR-0021) — the whole of the geometry
/// kernel, every file format and the viewport work without one — so a suite that went red on that
/// configuration would be reporting a supported state as a defect. What must never happen is a
/// test that <i>passes</i> without the provider while claiming to have exercised it, which is why
/// the skip reason names what is missing and how to build it.
/// </remarks>
public sealed class NativeFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute, skipping when the provider cannot be loaded.</summary>
    /// <param name="sourceFilePath">Filled in by the compiler; xUnit uses it for source links.</param>
    /// <param name="sourceLineNumber">Filled in by the compiler.</param>
    public NativeFactAttribute(
        [System.Runtime.CompilerServices.CallerFilePath] string? sourceFilePath = null,
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!NativeProvider.Available)
        {
            Skip = NativeProvider.Reason
                ?? "The spark_occt provider is not available in this build.";
        }
    }
}

/// <summary>Loads the provider once for the whole assembly.</summary>
public static class NativeProvider
{
    private static readonly object Gate = new();

    private static bool _tried;
    private static bool _available;
    private static string? _reason;

    /// <summary>Whether the provider is installed as the process's kernel.</summary>
    public static bool Available
    {
        get
        {
            Ensure();

            return _available;
        }
    }

    /// <summary>Why it is not, when it is not.</summary>
    public static string? Reason
    {
        get
        {
            Ensure();

            return _reason;
        }
    }

    /// <summary>The provider itself, for a test that wants to call it directly.</summary>
    public static IBrepKernel Kernel
    {
        get
        {
            Ensure();

            return BrepKernel.Current;
        }
    }

    private static void Ensure()
    {
        lock (Gate)
        {
            if (_tried)
            {
                return;
            }

            _tried = true;
            _available = OcctKernel.TryInstall(out _reason);
        }
    }
}
