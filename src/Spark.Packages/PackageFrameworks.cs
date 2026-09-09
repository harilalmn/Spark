using System;
using System.Collections.Generic;
using System.IO;
using NuGet.Frameworks;

namespace Spark.Packages;

/// <summary>
/// Which folder of a NuGet package this build should read assemblies from (`E7-T23`).
/// </summary>
/// <remarks>
/// <para>
/// <b>One rule, in one place, because there were two and they disagreed.</b>
/// <see cref="PackageLoadContext"/> used NuGet's own <see cref="FrameworkReducer"/> and
/// <see cref="GraphPackages"/> used a hand-written ranking whose own doc comment admitted the
/// ranking was crude and that real compatibility is NuGet's resolver. The crude one then met a
/// real package — <c>Nice3point.Revit.Api.RevitAPI</c>, whose folder is
/// <c>ref/net10.0-windows7.0</c> — parsed everything after <c>net</c> as a number, got nothing, and
/// refused a package targeting the exact framework Spark runs on.
/// </para>
/// <para>
/// <b><c>ref</c> counts, and for a code block library it is the normal case.</b> NuGet's
/// <c>lib</c> is compile-and-run and <c>ref</c> is compile-only — a package whose implementation is
/// supplied by a host at run time, which is precisely what a CAD API assembly is. Spark references
/// a graph's libraries to <i>compile</i> code blocks against, so a package with only <c>ref</c> is
/// usable rather than empty. <c>lib</c> still wins when both are there, because it is the one that
/// also runs.
/// </para>
/// </remarks>
public static class PackageFrameworks
{
    /// <summary>NuGet's compile-and-run folder.</summary>
    public const string LibFolder = "lib";

    /// <summary>NuGet's compile-time-only folder.</summary>
    public const string RefFolder = "ref";

    /// <summary>
    /// The framework to resolve against: what this build is, plus the platform it is running on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The platform is added deliberately, and without it the Revit package still fails.</b>
    /// Spark's own target framework is <c>net10.0</c> with no platform, and NuGet says a
    /// <c>net10.0-windows</c> asset is <b>not</b> compatible with a platform-neutral project — a
    /// correct rule for a project that might be built for Linux, and the wrong question here. This
    /// is not a compilation targeting an unknown platform; it is a process that is already running
    /// on one, and a Windows-specific assembly loads and works in it.
    /// </para>
    /// <para>
    /// <b>It is still a real constraint rather than a bypass.</b> On Linux the platform is not
    /// added, so a <c>-windows</c> asset is refused there exactly as before, and an
    /// <c>-android</c> or <c>-ios</c> asset is refused everywhere.
    /// </para>
    /// <para>
    /// The base framework is read from <see cref="AppContext.TargetFrameworkName"/> rather than
    /// hardcoded, so a Spark retargeted to a later framework starts preferring that folder without
    /// anybody remembering to come back here.
    /// </para>
    /// </remarks>
    public static NuGetFramework Current { get; } = WithPlatform(Build());

    /// <summary>
    /// The folders of one extracted package that hold assemblies for this build, best first.
    /// </summary>
    /// <param name="package">The package's own folder.</param>
    /// <returns>
    /// The chosen framework folder, or the package folder itself when it has neither
    /// <c>lib</c> nor <c>ref</c> — the hand-assembled case, where somebody dropped a
    /// <c>.dll</c> in and there is no framework to choose between.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="package"/> is null or blank.</exception>
    public static IReadOnlyList<string> AssemblyFoldersIn(string package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(package);

        // `lib` first: it is compile-and-run, so a package offering both is offering `ref` for
        // compilation only and `lib` is the copy that also loads.
        if (Best(Path.Combine(package, LibFolder)) is { } lib)
        {
            return [lib];
        }

        if (Best(Path.Combine(package, RefFolder)) is { } reference)
        {
            return [reference];
        }

        return [package];
    }

    /// <summary>
    /// What a package actually offers, for a message to a user who got nothing (`E7-T23`).
    /// </summary>
    /// <param name="package">The package's own folder.</param>
    /// <returns>The framework folders it carries, such as <c>lib/net472</c>, or an empty list.</returns>
    /// <remarks>
    /// <b>Because the message that named a folder the package did not have sent the client looking
    /// in the wrong place.</b> It said <i>its 'lib' folder may target a framework Spark does not
    /// run on</i>, of a package with no <c>lib</c> folder and a <c>net10.0</c> target. A refusal
    /// that guesses is worse than one that says what it saw.
    /// </remarks>
    public static IReadOnlyList<string> FrameworksOffered(string package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(package);

        List<string> offered = [];

        foreach (string parent in (string[])[LibFolder, RefFolder])
        {
            foreach (string candidate in SafeDirectories(Path.Combine(package, parent)))
            {
                offered.Add(parent + "/" + Path.GetFileName(candidate));
            }
        }

        return offered;
    }

    /// <summary>The best framework folder under one parent, or null when there is none.</summary>
    private static string? Best(string parent)
    {
        if (!Directory.Exists(parent))
        {
            return null;
        }

        Dictionary<NuGetFramework, string> byFramework = [];

        foreach (string candidate in SafeDirectories(parent))
        {
            NuGetFramework parsed = NuGetFramework.ParseFolder(Path.GetFileName(candidate));

            if (!parsed.IsUnsupported)
            {
                byFramework[parsed] = candidate;
            }
        }

        if (byFramework.Count == 0)
        {
            return null;
        }

        NuGetFramework? nearest = new FrameworkReducer().GetNearest(Current, byFramework.Keys);

        return nearest is not null && byFramework.TryGetValue(nearest, out string? chosen)
            ? chosen
            : null;
    }

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static NuGetFramework Build() =>
        AppContext.TargetFrameworkName is { Length: > 0 } name
            ? NuGetFramework.Parse(name)
            : NuGetFramework.Parse("net10.0");

    /// <summary>The same framework, wearing the platform this process is actually running on.</summary>
    private static NuGetFramework WithPlatform(NuGetFramework framework)
    {
        // Only the .NET 5+ identifier carries a platform at all: `netstandard2.0` and the old
        // `.NETFramework` monikers have no such notion, and asking with one would make every
        // comparison against them nonsense.
        bool net5OrLater =
            string.Equals(framework.Framework, FrameworkConstants.FrameworkIdentifiers.NetCoreApp, StringComparison.OrdinalIgnoreCase)
            && framework.Version.Major >= 5;

        if (!OperatingSystem.IsWindows() || framework.HasPlatform || !net5OrLater)
        {
            return framework;
        }

        // `7.0` is the platform version NuGet uses for plain `-windows`, and it is what
        // `net10.0-windows` parses to - so asking with it matches both that spelling and the
        // explicit `net10.0-windows7.0` the Revit package uses.
        return new NuGetFramework(
            framework.Framework, framework.Version, "windows", new Version(7, 0, 0, 0));
    }
}
