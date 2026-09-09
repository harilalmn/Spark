using System;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// A library a user added is usable without typing a <c>using</c> — `E7-T21`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: <i>I hope the user does not have to declare the using statement
/// to use the classes from the NuGet libraries installed.</i> Adding a library and then being told
/// its types do not exist is the ceremony the feature was meant to remove.
/// </para>
/// <para>
/// <b>`E6-T30` is why this needed care.</b> Importing <c>Spark.Nodes.Core</c> wholesale broke nine
/// names against <c>Spark.Geometry</c> and every block written with them. The difference here is
/// consent — the user fetched this library — and that a <c>using</c> only errors where an ambiguous
/// name is actually used, on the line that uses it.
/// </para>
/// </remarks>
public sealed class LibraryNamespaceImportTests
{
    /// <summary>
    /// <b>The point of the row.</b> A type from an added assembly resolves with no <c>using</c> in
    /// the block.
    /// </summary>
    [Fact]
    public void ATypeFromAnAddedLibraryNeedsNoUsing()
    {
        ReferenceCatalog catalogue = new();

        // xunit's own assembly stands in for a NuGet library: it is on disk, it is managed, and
        // nothing in Spark's prelude imports it - so a type of its own is unreachable until it is
        // added, which is exactly the state a freshly downloaded package is in.
        catalogue.Add([typeof(FactAttribute).Assembly.Location]);

        Assert.Contains("Xunit", catalogue.Imports, StringComparer.Ordinal);

        ScriptNodeFactory factory = new(catalogue, new GuardWeaver());

        // `FactAttribute` unqualified compiles only if `using Xunit;` reached the prelude.
        Assert.DoesNotContain(factory.Diagnose("var a = nameof(FactAttribute);"), d => d.IsError);
    }

    /// <summary>
    /// <b>Only what the user added is imported, never the sweep.</b> The catalogue also holds every
    /// assembly the process has loaded — the whole framework — and importing those namespaces would
    /// put all of .NET in front of every block and make half its names ambiguous.
    /// </summary>
    [Fact]
    public void TheSweptAssembliesAreNotImported()
    {
        ReferenceCatalog catalogue = new();

        // Loaded by this very test process, so it is in the catalogue by the sweep - and must not
        // be imported, because nobody chose it.
        Assert.DoesNotContain("System.Reflection.Metadata", catalogue.Imports, StringComparer.Ordinal);
        Assert.DoesNotContain("System.Text.Json", catalogue.Imports, StringComparer.Ordinal);
    }

    /// <summary>Nothing is imported twice, however many times it is added.</summary>
    [Fact]
    public void AnImportAppearsOnce()
    {
        ReferenceCatalog catalogue = new();

        catalogue.Add([typeof(FactAttribute).Assembly.Location]);
        catalogue.Add([typeof(FactAttribute).Assembly.Location]);

        Assert.Equal(1, catalogue.Imports.Count(import => string.Equals(import, "Xunit", StringComparison.Ordinal)));
    }

    /// <summary>
    /// <b>The namespaces already in the prelude are not repeated.</b> `Spark.Geometry` is imported
    /// by default, and adding its assembly must not offer it a second time.
    /// </summary>
    [Fact]
    public void ANamespaceAlreadyInThePreludeIsNotRepeated()
    {
        ReferenceCatalog catalogue = new();

        catalogue.Add([typeof(Spark.Geometry.Point3d).Assembly.Location]);

        Assert.Equal(
            1,
            catalogue.Imports.Count(import => string.Equals(import, "Spark.Geometry", StringComparison.Ordinal)));
    }

    /// <summary>
    /// <b>A file that is not a managed assembly contributes nothing rather than failing.</b> Every
    /// package with a runtime component ships native DLLs beside the managed ones.
    /// </summary>
    [Fact]
    public void AFileThatIsNotAManagedAssemblyIsIgnored()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".dll");
        System.IO.File.WriteAllText(path, "this is not a PE file");

        try
        {
            ReferenceCatalog catalogue = new();
            int before = catalogue.Imports.Length;

            catalogue.Add([path]);

            Assert.Equal(before, catalogue.Imports.Length);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
