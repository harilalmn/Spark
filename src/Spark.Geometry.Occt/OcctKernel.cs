using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Spark.Api;

namespace Spark.Geometry.Occt;

/// <summary>
/// Finds the native provider and installs it, or explains why it could not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Installing is a decision an application makes, not something a library does on load.</b>
/// <see cref="BrepKernel.Current"/> is per-process ambient state, so a host that has its own
/// kernel must be able to keep it; a static constructor that seized the seam the moment this
/// assembly was touched would take that choice away from an embedder, which is exactly what
/// ADR-0005's layering exists to prevent.
/// </para>
/// <para>
/// <b>The failure path is the interesting one.</b> A build with no native component present is a
/// supported configuration — that is what <c>UnavailableBrepKernel</c> is for — so a missing DLL
/// produces a sentence a user can act on and a <see langword="false"/>, not an exception on
/// startup.
/// </para>
/// </remarks>
public static class OcctKernel
{
    private static readonly object Gate = new();

    private static bool _resolverInstalled;

    /// <summary>The environment variable that names the directory holding the provider.</summary>
    /// <remarks>
    /// For a developer running from the repository, where the native build lands in
    /// <c>artifacts/native/win-x64</c> rather than beside the executable. A shipped install puts
    /// the DLLs next to the application and needs nothing set.
    /// </remarks>
    public const string PathVariable = "SPARK_OCCT_PATH";

    /// <summary>Whether the provider could be loaded, without installing it.</summary>
    /// <remarks>Loads the library on the first call and remembers the answer.</remarks>
    public static bool IsAvailable => TryLoad(out _);

    /// <summary>
    /// Installs the OpenCascade provider as the process's kernel.
    /// </summary>
    /// <param name="reason">Why it could not be installed, when the answer is false.</param>
    /// <returns>True when <see cref="BrepKernel.Current"/> is now the OpenCascade provider.</returns>
    public static bool TryInstall(out string? reason)
    {
        lock (Gate)
        {
            if (!TryLoad(out reason))
            {
                return false;
            }

            int abi = NativeMethods.spark_occt_abi_version();

            if (abi != NativeMethods.AbiVersion)
            {
                reason = string.Create(
                    CultureInfo.InvariantCulture,
                    $"The installed spark_occt speaks ABI {abi} and this build speaks "
                    + $"{NativeMethods.AbiVersion}. Rebuild it with scripts/build-native.ps1.");

                return false;
            }

            BrepKernel.Install(new OcctBrepKernel());
            reason = null;

            return true;
        }
    }

    /// <summary>The directories the provider is looked for in, in order.</summary>
    /// <returns>The candidate directories, whether or not they exist.</returns>
    public static IReadOnlyList<string> SearchPath()
    {
        List<string> places = [];

        string? configured = Environment.GetEnvironmentVariable(PathVariable);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            places.Add(configured);
        }

        string beside = AppContext.BaseDirectory;
        places.Add(beside);
        places.Add(Path.Combine(beside, "runtimes", "win-x64", "native"));

        // Walking up for artifacts/native/win-x64 is what makes `dotnet run` work from a clone
        // without anybody exporting a variable first. It stops at the repository root, which is
        // the directory that has a Spark.slnx in it.
        DirectoryInfo? here = new(beside);

        while (here is not null)
        {
            if (File.Exists(Path.Combine(here.FullName, "Spark.slnx")))
            {
                places.Add(Path.Combine(here.FullName, "artifacts", "native", "win-x64"));
                break;
            }

            here = here.Parent;
        }

        return places;
    }

    private static bool TryLoad(out string? reason)
    {
        EnsureResolver();

        try
        {
            _ = NativeMethods.spark_occt_abi_version();
            reason = null;

            return true;
        }
        catch (DllNotFoundException)
        {
            reason = string.Create(
                CultureInfo.InvariantCulture,
                $"spark_occt was not found. Build it with scripts/build-native.ps1, or set "
                + $"{PathVariable} to the directory holding it. Looked in: "
                + $"{string.Join("; ", SearchPath())}.");

            return false;
        }
        catch (BadImageFormatException error)
        {
            reason = "spark_occt was found but could not be loaded: " + error.Message;

            return false;
        }
        catch (EntryPointNotFoundException)
        {
            reason =
                "spark_occt was found but does not export spark_occt_abi_version, so it is not "
                + "the library this build expects.";

            return false;
        }
    }

    /// <summary>
    /// Teaches the runtime where to look, once.
    /// </summary>
    /// <remarks>
    /// <b>A resolver rather than a copy step, because the OpenCascade DLLs travel with it.</b>
    /// Loading <c>spark_occt.dll</c> from a directory makes Windows resolve its own dependencies
    /// from that directory too, which is why the staging step puts all of them together and this
    /// only has to name the one.
    /// </remarks>
    private static void EnsureResolver()
    {
        lock (Gate)
        {
            if (_resolverInstalled)
            {
                return;
            }

            _resolverInstalled = true;

            NativeLibrary.SetDllImportResolver(
                typeof(OcctKernel).Assembly,
                static (name, assembly, search) =>
                {
                    if (!string.Equals(name, NativeMethods.Library, StringComparison.Ordinal))
                    {
                        return IntPtr.Zero;
                    }

                    foreach (string directory in SearchPath())
                    {
                        string candidate = Path.Combine(directory, name + ".dll");

                        if (File.Exists(candidate)
                            && NativeLibrary.TryLoad(candidate, out IntPtr handle))
                        {
                            return handle;
                        }
                    }

                    return NativeLibrary.TryLoad(name, assembly, search, out IntPtr fallback)
                        ? fallback
                        : IntPtr.Zero;
                });
        }
    }
}
