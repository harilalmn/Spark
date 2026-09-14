using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Spark.Engine;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// The importer against a library nobody here wrote (<c>E5-T11</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in this repository had ever imported an assembly built outside it.</b> All
/// thirty-five <c>NodeImporter.Import</c> call sites pointed at <c>Spark.Nodes.Core</c>,
/// <c>Spark.Viewport</c>, <c>Spark.Geometry</c>, or a list of fixture types declared in the test
/// beside them. The package suite's own fixtures are <c>.nupkg</c> files built in a temp folder
/// around <b>our</b> assemblies. Every one of those was written by somebody who knew what the
/// importer wanted.
/// </para>
/// <para>
/// <b>Which makes <c>NodeImporter</c>'s central claim untested.</b> Its header says
/// <i>zero configuration is the whole design: a public static method is a node with no attribute
/// on it at all</i> — and the row this closes calls a real third-party package
/// <i>the only honest way to know zero-config actually works</i>. MathNet.Numerics has never heard
/// of Spark.
/// </para>
/// <para>
/// <b>The package is restored, not downloaded.</b> `Spark.Packages.Tests` references it with
/// <c>ExcludeAssets="all"</c>, so nothing compiles against it and nothing copies it anywhere; it
/// lands on disk during restore and this test reads it from there. A test that fetched from
/// nuget.org would go red when nuget.org was slow, which is a result about the internet.
/// <c>NuGetFeedTests</c> handles that by asserting nothing when the feed is unreachable — right
/// for a feed probe, useless for an acceptance test.
/// </para>
/// <para>
/// <b>What this deliberately does not test: <see cref="PackageManager"/>.</b> Its
/// <c>Load</c> imports only the assemblies named in <c>tools/spark.json</c>, so an arbitrary
/// package is refused <i>by design</i> — importing nodes from libraries whose authors never
/// intended them is exactly what that gate prevents, and going around it in a test would be
/// testing the opposite of the rule. The claim under test here is the <b>importer's</b>.
/// </para>
/// </remarks>
public sealed class ThirdPartyImportTests
{
    /// <summary>The package root, put there by restore and named by an assembly attribute.</summary>
    private static string PackageRoot =>
        typeof(ThirdPartyImportTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "MathNetPackageRoot")
            .Value!;

    /// <summary>The fixture is on disk. Everything below is meaningless without this.</summary>
    [Fact]
    public void TheThirdPartyPackageWasRestored()
    {
        Assert.True(
            Directory.Exists(PackageRoot),
            $"MathNet.Numerics is not at {PackageRoot}. It is a PackageReference of this project, so "
            + "a restore should have put it there.");

        Assert.True(File.Exists(Path.Combine(PackageRoot, "lib", "net6.0", "MathNet.Numerics.dll")));
    }

    /// <summary>
    /// <b>The framework picker reduces to a real package's real folders.</b> MathNet offers
    /// <c>net461</c>, <c>net48</c>, <c>net5.0</c>, <c>net6.0</c> and <c>netstandard2.0</c>; this
    /// application is <c>net10.0</c> and none of those is it.
    /// </summary>
    /// <remarks>
    /// <b>Every other test of this picker builds a package with one lib folder in it.</b> That
    /// proves the plumbing and cannot prove the choice, because there is nothing to choose between.
    /// The nearest framework to net10.0 here is net6.0, and picking <c>netstandard2.0</c> — which
    /// also loads, and would pass a test that only asked for *an* assembly — would silently give a
    /// reader an older surface.
    /// </remarks>
    [Fact]
    public void TheFrameworkPickerChoosesNet60ForNet10()
    {
        string folder = Assert.Single(PackageFrameworks.AssemblyFoldersIn(PackageRoot));

        Assert.Equal("net6.0", Path.GetFileName(folder));

        // The offered list is what a refusal message would name, so it is worth pinning too.
        List<string> offered = [.. PackageFrameworks.FrameworksOffered(PackageRoot)];

        Assert.Contains(offered, f => f.EndsWith("netstandard2.0", StringComparison.Ordinal));
        Assert.Contains(offered, f => f.EndsWith("net6.0", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>Zero configuration, against a library that has never heard of Spark.</b> No attribute,
    /// no manifest, no cooperation of any kind — and a sane number of nodes comes out.
    /// </summary>
    /// <remarks>
    /// <b>The count is a band and not a number.</b> An exact figure would fail on every MathNet
    /// release for no reason a reader could act on; a bare lower bound would pass on the day the
    /// importer started importing something it should not. The named members below are what
    /// actually pins the behaviour — a count says *something* arrived, and a name says *the right
    /// thing* did.
    /// </remarks>
    [Fact]
    public void AThirdPartyLibraryImportsWithNoConfigurationAtAll()
    {
        ImportReport report = Import();

        Assert.InRange(report.Nodes.Count, 500, 20_000);

        List<string> names = [.. report.Definitions().Select(d => d.DisplayName)];

        // Static maths, which is the shape the importer's header is about.
        Assert.Contains("SpecialFunctions.Erf", names);
        Assert.Contains("Trig.Sin", names);
        Assert.Contains("Combinatorics.Combinations", names);

        // And a type whose members are instance methods on a concrete class, so the receiver-port
        // path is exercised by somebody else's code rather than by ours.
        Assert.Contains(names, n => n.StartsWith("DenseVector.", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>Nothing threw, and nothing was dropped in silence.</b> Every public member is either a
    /// node or an exclusion carrying a reason — which is <see cref="ImportReport"/>'s own
    /// invariant, and the one worth checking against an assembly nobody tailored.
    /// </summary>
    /// <remarks>
    /// <b>This is the half of the row that says <i>with no crashes</i>.</b> An importer that threw
    /// on a generic constraint it had not seen, or quietly skipped a member shape it did not
    /// recognise, would be invisible against our own libraries and obvious against a real one.
    /// </remarks>
    [Fact]
    public void EveryPublicMemberIsEitherANodeOrARefusalWithAReason()
    {
        ImportReport report = Import();

        Assert.NotEmpty(report.Exclusions);

        List<string> unexplained =
        [
            .. report.Exclusions
                .Where(e => string.IsNullOrWhiteSpace(e.Reason))
                .Select(e => e.Member.DeclaringType?.Name + "." + e.Member.Name),
        ];

        Assert.True(
            unexplained.Count == 0,
            "public members refused with no reason given: " + string.Join(", ", unexplained));
    }

    /// <summary>
    /// <b>The nodes are usable, not merely counted.</b> A definition with no output port, a blank
    /// name or a duplicate key would satisfy every count above and be useless in a graph.
    /// </summary>
    [Fact]
    public void TheImportedNodesAreWellFormed()
    {
        List<NodeDefinition> definitions = [.. Import().Definitions()];

        Assert.All(definitions, definition =>
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
            Assert.NotEmpty(definition.Outputs);
            Assert.Equal("MathNet.Numerics", definition.Key.Package);
        });

        List<string> duplicates =
        [
            .. definitions.GroupBy(d => d.Key.Value, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key),
        ];

        Assert.True(duplicates.Count == 0, "duplicate node keys: " + string.Join(", ", duplicates.Take(10)));
    }

    /// <summary>Imports the package's assembly, from the folder the framework picker chose.</summary>
    private static ImportReport Import()
    {
        string folder = PackageFrameworks.AssemblyFoldersIn(PackageRoot).Single();
        string dll = Path.Combine(folder, "MathNet.Numerics.dll");

        return NodeImporter.Import(Assembly.LoadFrom(dll), "MathNet.Numerics");
    }
}
