using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Spark.Geometry.Occt;

/// <summary>
/// Pins managed arrays for the duration of one native call, and unpins all of them at once.
/// </summary>
/// <remarks>
/// <b>One object owns every pin in a call, so there is one place that can leak one.</b> A model
/// crossing the ABI is seventeen arrays; pinning them individually with seventeen
/// <c>fixed</c> statements would be seventeen nested blocks, and pinning them with
/// <see cref="GCHandle"/> without a single owner would be seventeen chances to miss a
/// <see cref="GCHandle.Free"/> on an early return. This is neither.
/// </remarks>
internal sealed class NativeBuffers : IDisposable
{
    private readonly List<GCHandle> _handles = [];

    /// <summary>Pins an array and returns its address, or zero for a null or empty one.</summary>
    /// <remarks>
    /// Zero for empty is deliberate and the C side expects it: a table with a zero count may
    /// arrive as a null pointer, so an empty array does not need an allocation to be addressable.
    /// </remarks>
    public IntPtr Pin<T>(T[]? array)
        where T : struct
    {
        if (array is null || array.Length == 0)
        {
            return IntPtr.Zero;
        }

        GCHandle handle = GCHandle.Alloc(array, GCHandleType.Pinned);
        _handles.Add(handle);

        return handle.AddrOfPinnedObject();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (GCHandle handle in _handles)
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        _handles.Clear();
    }
}

/// <summary>
/// Reading the provider's last error, and turning a status into a sentence.
/// </summary>
internal static class NativeErrors
{
    /// <summary>Why the most recent failing call on this thread failed.</summary>
    public static string LastError()
    {
        int needed = NativeMethods.spark_occt_last_error(IntPtr.Zero, 0);

        if (needed <= 1)
        {
            return string.Empty;
        }

        byte[] buffer = new byte[needed];

        using NativeBuffers pins = new();
        IntPtr address = pins.Pin(buffer);
        int written = NativeMethods.spark_occt_last_error(address, needed);

        int length = Math.Min(written, needed) - 1;

        return length <= 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, length);
    }

    /// <summary>The OpenCascade version the provider was built against.</summary>
    public static string EngineVersion()
    {
        byte[] buffer = new byte[256];

        using NativeBuffers pins = new();
        int written = NativeMethods.spark_occt_engine_version(pins.Pin(buffer), buffer.Length);
        int length = Math.Min(written, buffer.Length) - 1;

        return length <= 0 ? "OpenCascade" : Encoding.UTF8.GetString(buffer, 0, length);
    }

    /// <summary>
    /// A sentence for a status, with the provider's own words when it left any.
    /// </summary>
    /// <remarks>
    /// The fallback text is per-status rather than one generic line, because a caller reading
    /// "the kernel refused" learns something a caller reading "the kernel failed" does not.
    /// </remarks>
    public static string Describe(int status, string operation)
    {
        string detail = LastError();

        if (detail.Length > 0)
        {
            return detail;
        }

        return status switch
        {
            NativeMethods.ErrorArgument => $"{operation} was given something it could not use.",
            NativeMethods.ErrorRefused => $"{operation} did not succeed on this geometry.",
            NativeMethods.ErrorUnsupported => $"{operation} is not something this build can do.",
            NativeMethods.ErrorException => $"{operation} raised inside the kernel.",
            _ => $"{operation} returned status {status}.",
        };
    }
}
