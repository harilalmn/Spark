using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Spark.Api;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// Assembly isolation and type identity across package load contexts — the two properties the
/// whole of <c>E7</c> rests on, and the two whose absence produces error messages that name the
/// same type twice.
/// </summary>
public sealed class PackageLoadContextTests : IDisposable
{
    private readonly string _folder;

    /// <summary>Creates a scratch package folder for one test.</summary>
    public PackageLoadContextTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "spark-package-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_folder);
    }

    /// <summary>Removes the scratch folder.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Expected, and it is the point rather than an annoyance: a loaded assembly is locked
            // on Windows until its context has genuinely unloaded, and unloading is best-effort.
            // That is exactly why restart is the documented default for a package upgrade
            // (E7-T5). Leaving a temp directory behind is not worth failing a test over.
        }
    }

    /// <summary>
    /// A package's own assembly is loaded from the package folder, and is <b>not</b> the copy the
    /// application already has loaded. Without this there is no isolation and no side-by-side.
    /// </summary>
    [Fact]
    public void APackageAssemblyLoadsFromItsOwnFolderAndIsNotTheApplicationsCopy()
    {
        string copied = StageAssembly(typeof(Spark.Viewport.Camera).Assembly);

        PackageLoadContext context = new(PackageIdentity.Create("Acme.Nodes", "1.0.0"), _folder);
        Assembly isolated = context.LoadPackageAssembly("Spark.Viewport");

        Assert.Equal(copied, isolated.Location, ignoreCase: true);
        Assert.NotSame(typeof(Spark.Viewport.Camera).Assembly, isolated);
        Assert.NotEqual(typeof(Spark.Viewport.Camera), isolated.GetType("Spark.Viewport.Camera"));
    }

    /// <summary>
    /// <b>The decisive test for E7-T4.</b> A contract assembly resolves from the default context
    /// even though a file of exactly that name sits in the package folder.
    /// </summary>
    /// <remarks>
    /// The staged <c>Spark.Api.dll</c> is deliberately not a valid assembly. If the resolution
    /// order were file-existence-first, this test would not merely assert the wrong thing — it
    /// would throw <see cref="BadImageFormatException"/>, which is a far better failure than the
    /// one the real bug produces. In production the file <i>would</i> be a valid assembly, and the
    /// symptom would be a <c>Circle</c> that cannot be assigned to a <c>Circle</c>.
    /// </remarks>
    [Fact]
    public void AContractAssemblyResolvesFromTheDefaultContextEvenWhenThePackageShipsItsOwn()
    {
        File.WriteAllBytes(Path.Combine(_folder, "Spark.Api.dll"), "not an assembly"u8.ToArray());

        PackageLoadContext context = new(PackageIdentity.Create("Acme.Nodes", "1.0.0"), _folder);
        Assembly resolved = context.LoadFromAssemblyName(new AssemblyName("Spark.Api"));

        Assert.Same(typeof(SparkList).Assembly, resolved);
    }

    /// <summary>
    /// Every contract assembly, not just the one that was convenient to test. A name added to the
    /// list without being wired through would pass the test above and fail here.
    /// </summary>
    [Theory]
    [InlineData("Spark.Api")]
    [InlineData("Spark.Geometry")]
    [InlineData("Spark.Engine")]
    public void EveryContractAssemblyIsSharedWithTheDefaultContext(string name)
    {
        File.WriteAllBytes(Path.Combine(_folder, name + ".dll"), "not an assembly"u8.ToArray());

        PackageLoadContext context = new(PackageIdentity.Create("Acme.Nodes", "1.0.0"), _folder);
        Assembly resolved = context.LoadFromAssemblyName(new AssemblyName(name));

        Assert.Same(AppDomain.CurrentDomain.Load(new AssemblyName(name)), resolved);
    }

    /// <summary>
    /// A dependency the package ships and Spark does not is loaded from the package folder. This
    /// is the case a hardcoded name list gets wrong the first time a package adds a dependency.
    /// </summary>
    [Fact]
    public void ANonContractDependencyIsLoadedFromThePackageFolder()
    {
        StageAssembly(typeof(Spark.Viewport.Camera).Assembly);

        PackageLoadContext context = new(PackageIdentity.Create("Acme.Nodes", "1.0.0"), _folder);
        Assembly resolved = context.LoadFromAssemblyName(new AssemblyName("Spark.Viewport"));

        Assert.NotSame(typeof(Spark.Viewport.Camera).Assembly, resolved);
        Assert.StartsWith(_folder, resolved.Location, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An assembly the package does not ship and Spark does not either — a framework assembly —
    /// defers to the default context rather than failing.
    /// </summary>
    [Fact]
    public void AnAssemblyTheFolderDoesNotContainDefersToTheDefaultContext()
    {
        PackageLoadContext context = new(PackageIdentity.Create("Acme.Nodes", "1.0.0"), _folder);

        Assembly resolved = context.LoadFromAssemblyName(new AssemblyName("System.Text.Json"));

        Assert.NotNull(resolved);
    }

    /// <summary>
    /// Two versions of the same package coexist. This is the whole reason the identity carries a
    /// version, and it is what a single context per package would make impossible.
    /// </summary>
    [Fact]
    public void TwoVersionsOfOnePackageLoadSideBySide()
    {
        string one = Path.Combine(_folder, "1.0.0");
        string two = Path.Combine(_folder, "2.0.0");
        Directory.CreateDirectory(one);
        Directory.CreateDirectory(two);
        File.Copy(typeof(Spark.Viewport.Camera).Assembly.Location, Path.Combine(one, "Spark.Viewport.dll"));
        File.Copy(typeof(Spark.Viewport.Camera).Assembly.Location, Path.Combine(two, "Spark.Viewport.dll"));

        PackageLoadContext first = new(PackageIdentity.Create("Acme.Nodes", "1.0.0"), one);
        PackageLoadContext second = new(PackageIdentity.Create("Acme.Nodes", "2.0.0"), two);

        Assembly a = first.LoadPackageAssembly("Spark.Viewport");
        Assembly b = second.LoadPackageAssembly("Spark.Viewport");

        Assert.NotSame(a, b);
        Assert.NotEqual(a.GetType("Spark.Viewport.Camera"), b.GetType("Spark.Viewport.Camera"));

        // And the contract stays shared across both, which is what lets their nodes be wired to
        // each other despite everything else about them being separate.
        Assert.Same(
            first.LoadFromAssemblyName(new AssemblyName("Spark.Api")),
            second.LoadFromAssemblyName(new AssemblyName("Spark.Api")));
    }

    /// <summary>
    /// Asking a context for one of its own assemblies that is not there fails loudly rather than
    /// quietly handing back Spark's copy, which would present as a bug much later and elsewhere.
    /// </summary>
    [Fact]
    public void AskingForAMissingPackageAssemblyThrowsRatherThanDeferring()
    {
        PackageLoadContext context = new(PackageIdentity.Create("Acme.Nodes", "1.0.0"), _folder);

        FileNotFoundException thrown = Assert.Throws<FileNotFoundException>(
            () => context.LoadPackageAssembly("Spark.Api"));

        Assert.Contains("Acme.Nodes", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A context over a folder that does not exist resolves nothing and defers everything, rather
    /// than throwing. A package whose files were deleted underneath us is a support case, not a
    /// crash.
    /// </summary>
    [Fact]
    public void AContextOverAMissingFolderDefersInsteadOfThrowing()
    {
        PackageLoadContext context = new(
            PackageIdentity.Create("Acme.Nodes", "1.0.0"), Path.Combine(_folder, "gone"));

        Assert.NotNull(context.LoadFromAssemblyName(new AssemblyName("Spark.Api")));
    }

    /// <summary>
    /// <b>The unload proof, and the only honest one there is (<c>E7-T5</c>).</b> A collectible
    /// context that fails to unload does so silently, so asking it is worthless; a weak reference
    /// going dead is the fact.
    /// </summary>
    [Fact]
    public void AContextUnloadsOnceNothingReferencesIt()
    {
        WeakReference reference = LoadAndAbandon();

        for (int attempt = 0; attempt < 12 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(
            reference.IsAlive,
            "The package context did not unload. Something is still holding a type, a delegate or "
            + "a value from it — which is why E7-T5 purges every registry first and why restart is "
            + "the documented default.");
    }

    /// <summary>
    /// Loading, then keeping a type alive, keeps the context alive. Without this the test above
    /// could pass on a runtime that unloads regardless, and would be proving nothing.
    /// </summary>
    [Fact]
    public void AContextDoesNotUnloadWhileATypeFromItIsHeld()
    {
        (WeakReference reference, Type held) = LoadAndHold();

        for (int attempt = 0; attempt < 4; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.True(reference.IsAlive, "The context unloaded while a type from it was still referenced.");
        Assert.NotNull(held);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference LoadAndAbandon()
    {
        StageAssembly(typeof(Spark.Viewport.Camera).Assembly);

        PackageLoadContext context = new(PackageIdentity.Create("Acme.Nodes", "1.0.0"), _folder);
        context.LoadPackageAssembly("Spark.Viewport");

        WeakReference reference = new(context);
        context.Unload();
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (WeakReference Reference, Type Held) LoadAndHold()
    {
        StageAssembly(typeof(Spark.Viewport.Camera).Assembly);

        PackageLoadContext context = new(PackageIdentity.Create("Acme.Nodes", "1.0.0"), _folder);
        Type held = context.LoadPackageAssembly("Spark.Viewport").GetType("Spark.Viewport.Camera")!;

        WeakReference reference = new(context);
        context.Unload();
        return (reference, held);
    }

    /// <summary>Copies a real assembly into the scratch package folder and returns its new path.</summary>
    private string StageAssembly(Assembly assembly)
    {
        string destination = Path.Combine(_folder, Path.GetFileName(assembly.Location));
        File.Copy(assembly.Location, destination, overwrite: true);
        return destination;
    }
}
