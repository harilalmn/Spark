using System;
using System.Collections.Generic;
using System.IO;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// Which folder of a package this build reads assemblies from — `E7-T23`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written from a real package the client tried to add.</b>
/// <c>Nice3point.Revit.Api.RevitAPI 2027.2.0</c> puts its assembly in
/// <c>ref/net10.0-windows7.0/</c> and has no <c>lib</c> folder at all. Spark refused it and said
/// <i>its 'lib' folder may target a framework Spark does not run on</i> — wrong twice over, since
/// there is no <c>lib</c> folder and <c>net10.0</c> is exactly what Spark runs on.
/// </para>
/// <para>
/// <b>Two defects, and the fixtures had hidden both.</b> Every existing test built a package with
/// <c>lib/</c> and a bare <c>netN.0</c> moniker, which is the shape that already worked — the same
/// lesson as <c>N77</c>, met a second time in the same layer.
/// </para>
/// </remarks>
public sealed class PackageFrameworkChoiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// <b>The client's package, exactly as it extracts.</b> A compile-time-only assembly under
    /// <c>ref</c>, a platform-specific moniker, and no <c>lib</c> anywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This test's answer depends on the operating system running it, and saying so is the
    /// whole of the fix.</b> <c>net10.0-windows7.0</c> is compatible with a Windows host and is
    /// <i>genuinely not</i> compatible with any other — NuGet's own <c>FrameworkReducer</c> says so,
    /// and <c>PackageFrameworks.Current</c> only claims a platform when
    /// <c>OperatingSystem.IsWindows()</c>. Finding the assembly on Linux would be the bug.
    /// </para>
    /// <para>
    /// <b>It was written as an unconditional assertion and passed for months, because nothing but
    /// Windows ever ran it.</b> The first CI run after 2026-09-14 put it on ubuntu and it failed —
    /// the one failure in 3,875 tests, and in the test rather than in the code
    /// ([N169](../../docs/NOTES.md)). Spark is Windows-only by D16; the ubuntu leg exists as a
    /// second implementation of the same arithmetic, not as a supported target. So both arms are
    /// asserted here rather than the awkward one being skipped: a skip records that a platform was
    /// not tested, and this records what each platform is supposed to do.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARefOnlyPackageWithAPlatformMonikerIsFoundOnWindowsAndNowhereElse()
    {
        string graph = Graph("cylinder");
        string package = Path.Combine(
            _root, "cylinder.packages", "nice3point.revit.api.revitapi.2027.2.0");

        Write(Path.Combine(package, "ref", "net10.0-windows7.0", "RevitAPI.dll"), "revit");

        IReadOnlyList<GraphAssembly> found = GraphPackages.Discover(graph).Assemblies;

        if (!OperatingSystem.IsWindows())
        {
            Assert.Empty(found);
            return;
        }

        GraphAssembly only = Assert.Single(found);

        Assert.Equal("RevitAPI.dll", only.Name);
        Assert.Equal("nice3point.revit.api.revitapi.2027.2.0", only.Package);
    }

    /// <summary>
    /// <b><c>lib</c> wins when both are there</b>, because it is the copy that also runs;
    /// <c>ref</c> is NuGet's compile-only folder and is the fallback, not the preference.
    /// </summary>
    [Fact]
    public void LibIsPreferredOverRef()
    {
        string graph = Graph("project1");
        string package = Path.Combine(_root, "project1.packages", "acme.plain.1.0.0");

        Write(Path.Combine(package, "ref", "net10.0", "Acme.Plain.dll"), "reference");
        Write(Path.Combine(package, "lib", "net10.0", "Acme.Plain.dll"), "runnable");

        GraphAssembly only = Assert.Single(GraphPackages.Discover(graph).Assemblies);

        Assert.Contains(Path.Combine("lib", "net10.0"), only.Path, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Windows-specific assembly is accepted on Windows — the process is already running on the
    /// platform, whatever Spark's own platform-neutral target framework says.
    /// </summary>
    [Fact]
    public void AWindowsSpecificFolderIsAcceptedOnWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the platform rule is about Windows.");

        string graph = Graph("project1");
        string package = Path.Combine(_root, "project1.packages", "acme.plain.1.0.0");

        Write(Path.Combine(package, "lib", "net8.0-windows", "Acme.Plain.dll"), "windows");

        Assert.Single(GraphPackages.Discover(graph).Assemblies);
    }

    /// <summary>
    /// <b>And it is a rule rather than a bypass.</b> A framework this build cannot use is still
    /// refused, platform suffix or not.
    /// </summary>
    [Fact]
    public void AFolderForAnotherPlatformIsRefused()
    {
        string graph = Graph("project1");
        string package = Path.Combine(_root, "project1.packages", "acme.plain.1.0.0");

        Write(Path.Combine(package, "lib", "net8.0-android", "Acme.Plain.dll"), "android");
        Write(Path.Combine(package, "lib", "net8.0-ios", "Acme.Plain.dll"), "ios");

        Assert.Empty(GraphPackages.Discover(graph).Assemblies);
    }

    /// <summary>
    /// The choices that already worked still work, which is what taking the hand-written ranking
    /// out has to preserve: newest usable framework, nothing newer than this build, no
    /// .NET Framework.
    /// </summary>
    [Fact]
    public void TheChoicesThatAlreadyWorkedAreUnchanged()
    {
        string graph = Graph("project1");
        string package = Path.Combine(_root, "project1.packages", "acme.plain.1.0.0");

        Write(Path.Combine(package, "lib", "netstandard2.0", "Acme.Plain.dll"), "old");
        Write(Path.Combine(package, "lib", "net8.0", "Acme.Plain.dll"), "newer");
        Write(Path.Combine(package, "lib", "net10.0", "Acme.Plain.dll"), "newest");
        Write(Path.Combine(package, "lib", "net472", "Acme.Plain.dll"), "framework");

        GraphAssembly only = Assert.Single(GraphPackages.Discover(graph).Assemblies);

        Assert.Contains(Path.Combine("lib", "net10.0"), only.Path, StringComparison.Ordinal);
    }

    private string Graph(string name)
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, name + ".spark");
        File.WriteAllText(path, "{}");

        return path;
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
