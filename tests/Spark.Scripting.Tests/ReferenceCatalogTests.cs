using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Spark.Scripting;

namespace Spark.Scripting.Tests;

/// <summary>
/// The reference catalog decides what a code block can see. Its two hard requirements are that a
/// user can rebuild their own library while Spark is open, and that a bad file fails once rather
/// than on every subsequent compile.
/// </summary>
public sealed class ReferenceCatalogTests
{
    [Fact]
    public void TheBaselineCarriesOneReferencePerAssemblyName()
    {
        IReadOnlyList<MetadataReference> references = ReferenceCatalog.Default.References;

        List<string> names = [.. references
            .OfType<PortableExecutableReference>()
            .Select(reference => Path.GetFileNameWithoutExtension(reference.FilePath ?? string.Empty))
            .Where(name => name.Length > 0)];

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Spark's own assemblies are reachable whether or not something else has already pulled them
    /// in, which is what lets a code block say <c>Point3d</c> and mean it.
    /// </summary>
    [Fact]
    public void SparkTypesAreReachableFromACodeBlock()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "new Point3d(1, 2, 3).X + new SparkList(new object[] { 1.0 }, 1).Count",
            CodeBlockTestHarness.Options());

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
    }

    /// <summary>
    /// A file that is not a managed assembly is rejected while the catalog is being built. Left to
    /// Roslyn it would be accepted here and fail later as CS0009 on every single compile, pointing at
    /// nothing in particular.
    /// </summary>
    [Fact]
    public void AFileThatIsNotAnAssemblyIsRejectedRatherThanPoisoningEveryCompile()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "spark-scripting-tests", Guid.NewGuid().ToString("N"));

        try
        {
            System.IO.Directory.CreateDirectory(directory);

            string junk = Path.Combine(directory, "NotReallyAnAssembly.dll");
            File.WriteAllBytes(junk, [0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05]);

            ReferenceCatalog catalog = new([junk]);

            Assert.DoesNotContain(
                catalog.References.OfType<PortableExecutableReference>(),
                reference => string.Equals(reference.FilePath, junk, StringComparison.OrdinalIgnoreCase));

            CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
                "1 + 1",
                new CodeBlockOptions
                {
                    References = catalog,
                    Cache = new ScriptCompilationCache(string.Empty),
                });

            Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        }
        finally
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// An extra assembly is read into memory rather than referenced from disk, so the file stays
    /// writable and a user can rebuild their own node library without closing Spark. The test deletes
    /// the file while the catalog is alive; a locked file cannot be deleted on Windows.
    /// </summary>
    [Fact]
    public void AnExtraAssemblyIsNotLockedOnDisk()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "spark-scripting-tests", Guid.NewGuid().ToString("N"));

        try
        {
            System.IO.Directory.CreateDirectory(directory);

            string source = typeof(ReferenceCatalog).Assembly.Location;
            string copy = Path.Combine(directory, Path.GetFileName(source));
            File.Copy(source, copy);

            ReferenceCatalog catalog = new([copy]);
            Assert.NotEmpty(catalog.References);

            File.Delete(copy);
            Assert.False(File.Exists(copy));
        }
        finally
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// The catalog version is what the compile cache keys on, so it has to change when the set of
    /// assemblies changes and stay put when it does not.
    /// </summary>
    [Fact]
    public void TheVersionIsStableForOneSetOfAssembliesAndDiffersForAnother()
    {
        Assert.Equal(ReferenceCatalog.Default.Version, new ReferenceCatalog().Version);

        string directory = Path.Combine(
            Path.GetTempPath(), "spark-scripting-tests", Guid.NewGuid().ToString("N"));

        try
        {
            System.IO.Directory.CreateDirectory(directory);

            string copy = Path.Combine(directory, "SparkScriptingCopy.dll");
            File.Copy(typeof(ReferenceCatalog).Assembly.Location, copy);

            Assert.NotEqual(ReferenceCatalog.Default.Version, new ReferenceCatalog([copy]).Version);
        }
        finally
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }
    }
}
