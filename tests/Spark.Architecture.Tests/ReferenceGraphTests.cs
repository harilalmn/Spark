using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Spark.Architecture.Tests;

/// <summary>
/// Guards the layering rules in ADR-0005. These are the rules that make Spark embeddable
/// in a CAD host and keep the geometry kernel publishable on its own; every one of them
/// is trivially easy to break with a single convenient <c>ProjectReference</c>, and
/// impossible to unpick once a release has shipped depending on it.
/// </summary>
/// <remarks>
/// This project deliberately does not reference the projects it inspects. It reads the
/// <c>.csproj</c> files as XML. A test that referenced them could not observe a forbidden
/// reference — it would simply be part of the problem.
/// </remarks>
public sealed class ReferenceGraphTests
{
    private static readonly string SrcDirectory = LocateSourceDirectory();

    /// <summary>
    /// ADR-0005. <c>Spark.Api</c> is the contract every node package compiles against, so
    /// it must stay tiny and dependency-free. If it ever drags in Roslyn, Avalonia or
    /// NuGet, then embedding Spark inside a host that ships its own copy of any of those
    /// becomes an assembly-identity fight — which is the exact failure CADScript hit twice
    /// against AutoCAD, and the reason this layering exists at all.
    /// </summary>
    [Fact]
    public void SparkApiReferencesOnlyTheBclAndGeometry()
    {
        string[] projectReferences = ProjectReferencesOf("Spark.Api");
        Assert.Equal(["Spark.Geometry"], projectReferences);

        string[] packageReferences = PackageReferencesOf("Spark.Api");
        Assert.DoesNotContain(packageReferences, IsNotAmbient);
    }

    /// <summary>
    /// ADR-0005. First-party nodes must be discovered by the same zero-config reflection
    /// importer that third-party assemblies go through. The moment <c>Spark.Nodes.Core</c>
    /// can see <c>Spark.Engine</c>, it becomes possible to register a node by hand — and
    /// then the importer can be subtly broken for everyone else without a single test
    /// failing, because our own library stopped depending on it working.
    /// </summary>
    [Fact]
    public void SparkNodesCoreDoesNotReferenceTheEngine()
    {
        string[] projectReferences = ProjectReferencesOf("Spark.Nodes.Core");

        Assert.DoesNotContain("Spark.Engine", projectReferences);
        Assert.Equal(["Spark.Api", "Spark.Geometry", "Spark.Geometry.Io"], projectReferences.Order());
    }

    /// <summary>
    /// ADR-0014. The viewport renderer stays free of Avalonia so the software backend can
    /// run headlessly. That is what makes <c>spark render</c> deterministic and therefore
    /// what makes viewport output testable in CI at all — GPU output is not comparable
    /// across machines, software output is.
    /// </summary>
    [Fact]
    public void SparkViewportDoesNotReferenceAvalonia()
    {
        string[] packageReferences = PackageReferencesOf("Spark.Viewport");

        Assert.DoesNotContain(packageReferences, package =>
            package.StartsWith("Avalonia", StringComparison.Ordinal));
    }

    /// <summary>
    /// ADR-0002 promises no native dependencies in the default build. Clipper2's C# build
    /// is pure managed, and it is the only third-party dependency the kernel is allowed.
    /// Anything else arriving here needs its transitive native content checked first.
    /// <para>
    /// This asserts a ceiling rather than an exact set, so it holds both before the planar
    /// boolean pipeline brings Clipper2 in and after. An exact-set assertion had to be
    /// edited the moment the unused reference was removed, which is a test tracking an
    /// implementation detail rather than the rule it exists to protect.
    /// </para>
    /// </summary>
    /// <remarks>
    /// This asserts a ceiling, not an exact set. Clipper2 is <b>not</b> referenced at present:
    /// nothing in the kernel uses it until the planar boolean pipeline lands, and a package
    /// reference that no source file consumes still appears in the published nuspec, so
    /// consumers would acquire a dependency the library does not actually have. It comes back
    /// with the code that needs it, and this test keeps holding either way — what it must
    /// never permit is a <i>second</i> third-party package arriving unnoticed.
    /// </remarks>
    [Fact]
    public void SparkGeometryTakesNoThirdPartyDependencyBeyondClipper()
    {
        Assert.Empty(ProjectReferencesOf("Spark.Geometry"));

        string[] packages = PackageReferencesOf("Spark.Geometry")
            .Where(IsNotAmbient)
            .Order()
            .ToArray();

        Assert.All(packages, package => Assert.Equal("Clipper2", package));
    }

    /// <summary>
    /// ADR-0007. A <c>-windows</c> target framework anywhere would quietly end
    /// cross-platform capability, and it tends to arrive by accident through a template
    /// rather than by decision. Windows-only *releases* are a distribution choice (D14);
    /// a Windows-only *build* is not, and would strand the Avalonia investment.
    /// </summary>
    [Fact]
    public void NoProjectTargetsAWindowsSpecificFramework()
    {
        List<string> offenders = [];

        foreach (string project in Directory.EnumerateFiles(RepositoryRoot(), "*.csproj", SearchOption.AllDirectories))
        {
            if (project.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(project);
            if (text.Contains("-windows", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(project));
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Nothing shipped may depend on test code. Obvious, and worth a test precisely
    /// because it is the kind of reference someone adds at midnight to reuse one helper.
    /// </summary>
    [Fact]
    public void NoSourceProjectReferencesATestProject()
    {
        foreach (string project in Directory.EnumerateFiles(SrcDirectory, "*.csproj", SearchOption.AllDirectories))
        {
            string[] references = ProjectReferencesOfPath(project);

            Assert.DoesNotContain(references, reference =>
                reference.Contains("Test", StringComparison.Ordinal)
                || reference.Contains("Verify", StringComparison.Ordinal));
        }
    }

    private static bool IsNotAmbient(string packageName)
    {
        // MinVer is injected into every project by Directory.Build.props, so it is not
        // evidence of a deliberate dependency in any individual project file.
        return packageName != "MinVer";
    }

    private static string[] ProjectReferencesOf(string projectName) =>
        ProjectReferencesOfPath(Path.Combine(SrcDirectory, projectName, projectName + ".csproj"));

    private static string[] ProjectReferencesOfPath(string projectPath) =>
        ItemIncludes(projectPath, "ProjectReference")
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .ToArray();

    private static string[] PackageReferencesOf(string projectName) =>
        ItemIncludes(Path.Combine(SrcDirectory, projectName, projectName + ".csproj"), "PackageReference");

    private static string[] ItemIncludes(string projectPath, string itemName)
    {
        Assert.True(File.Exists(projectPath), $"Expected a project file at {projectPath}.");

        return XDocument.Load(projectPath)
            .Descendants(itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToArray();
    }

    private static string LocateSourceDirectory() => Path.Combine(RepositoryRoot(), "src");

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
