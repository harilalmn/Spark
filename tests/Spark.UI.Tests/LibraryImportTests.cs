using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Spark.Api;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// Importing an added library's namespaces without breaking the blocks already written
/// (`E7-T21`, `E6-T39`).
/// </summary>
/// <remarks>
/// <para>
/// <b>The client asked for the import</b>: <i>I hope the user does not have to declare the using
/// statement to use the classes from the NuGet libraries installed.</i> Then they found what it
/// costs: <c>Autodesk.Revit.DB</c> — the library they actually want — spells seven of its types
/// exactly as <c>Spark.Geometry</c> does, so importing it wholesale would answer every geometry
/// block in every graph with <c>CS0104</c>.
/// </para>
/// <para>
/// <b>So the import is conditional, and the refusal is spoken aloud.</b> These tests are the two
/// halves of that and the way out of it: a clean namespace arrives with no <c>using</c> to type; a
/// colliding one does not arrive and says which names stopped it; and the user can still reach it
/// by hand, with an alias for the names that clash.
/// </para>
/// </remarks>
public sealed class LibraryImportTests : IDisposable
{
    private readonly string _folder;

    public LibraryImportTests()
    {
        // Geometry has to be loaded before the catalogue is built, or its prelude line does not
        // resolve and every script here fails for a reason that has nothing to do with imports.
        _ = typeof(Spark.Geometry.Point3d).Assembly.Location;

        _folder = Path.Combine(Path.GetTempPath(), "spark-library-imports", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The assemblies are loaded and Windows will not delete a mapped file. The folder is
            // under the temp directory and the next run makes its own.
        }
    }

    /// <summary>
    /// <b>The thing the client asked for, working.</b> A library whose names clash with nothing is
    /// imported, and a block calls into it with no <c>using</c> at all.
    /// </summary>
    [Fact]
    public void ALibraryThatCollidesWithNothingNeedsNoUsing()
    {
        string library = Compile("Gadgetry", """
            namespace Gadgetry
            {
                public static class Sprockets
                {
                    public static double Twice(double value) => value * 2.0;
                }
            }
            """);

        ReferenceCatalog catalogue = new();
        _ = catalogue.Add([library]);

        Assert.Contains("Gadgetry", catalogue.Imports);
        Assert.Empty(catalogue.SkippedImports);

        NodeDefinitionSource block = new ScriptNodeFactory(catalogue).Create("return Sprockets.Twice(a);");

        Assert.Equal(84.0, Assert.Single(block.Invoke([42.0], CancellationToken.None)));
    }

    /// <summary>
    /// <b>The hazard, refused.</b> One colliding name is enough to keep the whole namespace out of
    /// the prelude — coarse on purpose, because <i>why does <c>Sprocket</c> resolve and <c>Line</c>
    /// not</i> is a worse question than a namespace that plainly was not imported.
    /// </summary>
    [Fact]
    public void ALibraryThatWouldMakeANameAmbiguousIsNotImported()
    {
        ReferenceCatalog catalogue = Colliding();

        Assert.DoesNotContain("Widgetry", catalogue.Imports);

        // And a block written before the library arrived still means what it meant.
        Assert.Equal(
            "Spark.Geometry.Mesh",
            Assert.Single(
                new ScriptNodeFactory(catalogue)
                    .Create("var named = typeof(Mesh).FullName;")
                    .Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// <b>Silence would be the worst outcome</b>: a user who added a library, typed a type name
    /// that plainly exists in it, and was told it does not. The sentence names the types that
    /// stopped the import and the <c>using</c> to write instead.
    /// </summary>
    [Fact]
    public void ARefusedImportSaysWhichNamesStoppedIt()
    {
        string reason = Assert.Single(Colliding().SkippedImports);

        Assert.Contains("Widgetry", reason, StringComparison.Ordinal);
        Assert.Contains("Mesh", reason, StringComparison.Ordinal);
        Assert.Contains("using Widgetry;", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Refusing an import the user could not then ask for by hand would be half an answer.</b>
    /// The namespace is still referenced; `E6-T39` is what lets a block say so. Nothing here uses
    /// the ambiguous name, so nothing is ambiguous — which is ordinary C#.
    /// </summary>
    [Fact]
    public void ASkippedNamespaceIsStillReachableWithAUsing()
    {
        ReferenceCatalog catalogue = Colliding();

        const string script = """
            using Widgetry;
            return Sprockets.Twice(a);
            """;

        NodeDefinitionSource block = new ScriptNodeFactory(catalogue).Create(script);

        Assert.Equal(84.0, Assert.Single(block.Invoke([42.0], CancellationToken.None)));
    }

    /// <summary>
    /// <b>A block's own <c>using</c> beats the prelude, and it does so without any ambiguity at
    /// all</b> — which is better than the row set out to achieve. The hoisted directives sit
    /// <i>inside</i> <c>namespace SparkGenerated</c> and the prelude sits above it, so C#'s own
    /// lookup finds <c>Widgetry.Mesh</c> at the inner scope and never reaches
    /// <c>Spark.Geometry</c>. The rule a user can hold in their head: <b>what you write in the
    /// block wins over what Spark imported for you</b>, and only that block.
    /// </summary>
    [Fact]
    public void ABlocksOwnUsingBeatsThePrelude()
    {
        ScriptNodeFactory factory = new(Colliding());

        Assert.Equal(
            "Spark.Geometry.Mesh",
            Assert.Single(factory.Create("var named = typeof(Mesh).FullName;")
                .Invoke([], CancellationToken.None)));

        const string mine = """
            using Widgetry;
            var named = typeof(Mesh).FullName;
            """;

        Assert.Empty(factory.Diagnose(mine));
        Assert.Equal("Widgetry.Mesh", Assert.Single(factory.Create(mine).Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// <b>And the client's own line, for the case that really is ambiguous.</b> Two namespaces
    /// imported <i>by the same block</i> are at one scope, so <c>Mesh</c> is <c>CS0104</c> — and an
    /// alias is how C# says which one, which is the exact thing they pointed out a block could not
    /// contain.
    /// </summary>
    [Fact]
    public void AnAliasSaysWhichOfTwoMeshesTheBlockMeant()
    {
        ScriptNodeFactory factory = new(Colliding());

        ScriptDiagnostic ambiguous = Assert.Single(
            factory.Diagnose("""
                using Widgetry;
                using Spark.Geometry;
                var named = Mesh.Name;
                """),
            diagnostic => diagnostic.IsError);

        Assert.Equal("CS0104", ambiguous.Id);
        Assert.Equal(3, ambiguous.Line);

        const string resolved = """
            using Widgetry;
            using Spark.Geometry;
            using Mine = Widgetry.Mesh;
            var named = Mine.Name;
            """;

        Assert.Empty(factory.Diagnose(resolved));
        Assert.Equal("widget", Assert.Single(factory.Create(resolved).Invoke([], CancellationToken.None)));
    }

    /// <summary>A catalogue holding a library that collides with <c>Spark.Geometry</c>.</summary>
    private ReferenceCatalog Colliding()
    {
        string library = Compile("Widgetry", """
            namespace Widgetry
            {
                public static class Mesh
                {
                    public static string Name => "widget";
                }

                public static class Sprockets
                {
                    public static double Twice(double value) => value * 2.0;
                }
            }
            """);

        ReferenceCatalog catalogue = new();
        _ = catalogue.Add([library]);

        return catalogue;
    }

    /// <summary>Compiles a tiny assembly into the scratch folder and returns its path.</summary>
    private string Compile(string name, string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            [SyntaxFactory.ParseSyntaxTree(source)],
            new ReferenceCatalog().References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        string path = Path.Combine(_folder, name + ".dll");
        EmitResult emitted = compilation.Emit(path);

        Assert.True(
            emitted.Success,
            "the test's own library did not compile: "
            + string.Join("; ", emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        return path;
    }
}
