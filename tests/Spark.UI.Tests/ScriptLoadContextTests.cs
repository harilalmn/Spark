using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Spark.Api;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// The collectible load context, and the registry that has to be cleared before it — `E6-T3`,
/// `E6-T15`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The only honest assertion about an unload is a weak reference that goes dead.</b>
/// <c>AssemblyLoadContext.Unload</c> returns whether or not the context can actually go; if
/// anything still references anything inside it, the call succeeds, nothing complains, and the
/// assemblies stay for the life of the process. A test that called <c>Unload</c> and asserted no
/// exception would pass in exactly the case this row exists to prevent.
/// </para>
/// <para>
/// <b>The compile happens in a method that is not this one</b>, and it is marked
/// <see cref="MethodImplOptions.NoInlining"/>. A local holding the definition would be rooted by
/// the caller's frame for as long as the method runs — under a debug JIT, past the point where the
/// source says it is dead — and the unload would fail for a reason that has nothing to do with the
/// code under test.
/// </para>
/// </remarks>
public sealed class ScriptLoadContextTests
{
    /// <summary>
    /// <b>Clearing the registry lets the context go.</b> Every cache entry holds a delegate bound
    /// into a script assembly, and a delegate into user code pins the collectible context it lives
    /// in — which is DoodleSharp's warning, and the reason `E6-T15` is a row of its own.
    /// </summary>
    [Fact]
    public void UnloadingReleasesTheScriptAssemblies()
    {
        ScriptNodeFactory factory = Factory();

        Compile(factory);

        WeakReference context = factory.Unload();

        Assert.True(
            Collected(context),
            "the script load context is still alive after Unload, so the assemblies it holds can "
            + "never be released");
    }

    /// <summary>
    /// <b>A definition still in use keeps the context alive, and that is correct.</b> The row's
    /// promise is that clearing the registry is *necessary*, not that it is sufficient: a graph
    /// still holding a compiled node is a reference like any other.
    /// </summary>
    [Fact]
    public void ADefinitionStillHeldKeepsTheContextAlive()
    {
        ScriptNodeFactory factory = Factory();

        NodeDefinitionSource held = factory.Create("return a * 3;");

        WeakReference context = factory.Unload();

        Assert.False(
            Collected(context),
            "the context should still be alive while a definition holds it");

        // And the definition still works, which is the other half: unloading the factory's cache
        // must not break a node that is on somebody's canvas.
        Assert.Equal(9.0, Assert.Single(held.Invoke([3.0], CancellationToken.None)));

        GC.KeepAlive(held);
    }

    /// <summary>
    /// A script's <c>Point3d</c> is the graph's <c>Point3d</c>. A load context that resolved its
    /// own copy would give the script a type with the same name that nothing could be assigned to
    /// — the most confusing failure this layer can produce, and it is avoided by the
    /// <c>Load</c> override returning null.
    /// </summary>
    [Fact]
    public void AScriptsTypesAreTheHostsTypes()
    {
        object? result = Assert.Single(
            Factory().Create("return new Point3d(1, 2, 3);").Invoke([], CancellationToken.None));

        Assert.IsType<Point3d>(result);
        Assert.Equal(new Point3d(1, 2, 3), result);
    }

    /// <summary>Unloading empties the resident cache, which is what makes the unload possible.</summary>
    [Fact]
    public void UnloadingEmptiesTheResidentCache()
    {
        ScriptNodeFactory factory = Factory();

        factory.Create("return a + 1;");

        Assert.Equal(1, factory.CachedScripts);

        factory.Unload();

        Assert.Equal(0, factory.CachedScripts);
    }

    /// <summary>The factory still works after an unload, on a fresh context.</summary>
    [Fact]
    public void TheFactoryStillCompilesAfterAnUnload()
    {
        ScriptNodeFactory factory = Factory();

        factory.Create("return a + 1;");
        factory.Unload();

        Assert.Equal(5.0, Assert.Single(factory.Create("return a + 1;").Invoke([4.0], CancellationToken.None)));
    }

    /// <summary>
    /// Compiles and drops the definition inside a frame of its own, so nothing is rooted by the
    /// caller's locals.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Compile(ScriptNodeFactory factory) => factory.Create("return a * 2;");

    private static ScriptNodeFactory Factory()
    {
        _ = typeof(Point3d).Assembly.Location;

        // No persistent cache: reading an entry back would load the same assembly, and this file is
        // about what holds it rather than about where it came from.
        return new ScriptNodeFactory(
            new ReferenceCatalog(), new GuardWeaver(), new ScriptAssemblyCache(directory: null));
    }

    /// <summary>
    /// Collects until a weak reference goes dead, or until it is fair to say it will not.
    /// </summary>
    /// <returns>True when the reference died.</returns>
    /// <remarks>
    /// <para>
    /// <b>An unload is not synchronous and cannot be made so.</b> It completes over several
    /// collections — finalisers run between them, and the context is only released once every
    /// assembly in it is — and how many it takes depends on what else the process is doing. A fixed
    /// number of <c>GC.Collect</c> calls is enough when this file runs alone and **was not** when
    /// it ran inside the whole suite, which is exactly the flake a GC assertion invites.
    /// </para>
    /// <para>
    /// So the loop is bounded by time and exits as soon as the answer is known: a pass is fast, and
    /// a genuine failure costs the whole budget once. <b>The negative case uses the same helper</b>,
    /// which is what stops it passing merely because the collector had not got round to it yet.
    /// </para>
    /// </remarks>
    private static bool Collected(WeakReference reference)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();

            if (!reference.IsAlive)
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }
}
