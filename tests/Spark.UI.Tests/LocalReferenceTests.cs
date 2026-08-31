using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Spark.Host;
using Spark.Scripting;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// Local DLL references: the hash, the prompt, the watch, and the read that does not lock
/// (<c>E7-T9</c>).
/// </summary>
/// <remarks>
/// <b>The assembly these use is a copy of a real one</b>, made in a scratch folder so it can be
/// rewritten and deleted underneath the catalogue. That is the only way to test the claim the row
/// actually makes: users can rebuild their library while Spark is open.
/// </remarks>
public sealed class LocalReferenceTests : IDisposable
{
    private readonly string _folder;
    private readonly string _assembly;
    private readonly string _record;

    /// <summary>Creates a scratch folder with a copied assembly in it.</summary>
    public LocalReferenceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "spark-local-refs", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_folder);

        _assembly = Path.Combine(_folder, "Acme.Helpers.dll");
        _record = Path.Combine(_folder, "trusted-assemblies.txt");

        File.Copy(typeof(Spark.Nodes.Core.Point).Assembly.Location, _assembly);
    }

    /// <summary>Removes it.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// <b>The claim the row is for: a referenced assembly can be rebuilt and deleted while Spark
    /// holds it.</b> Roslyn opens metadata files sharing read, write and delete, so a user's build
    /// is never blocked by Spark having compiled against the output.
    /// </summary>
    /// <remarks>
    /// The metadata is forced open first. <c>CreateFromFile</c> is lazy, so a test that only added
    /// the reference would pass whether or not the property held — it would be asserting that
    /// Roslyn had not got round to opening the file yet.
    /// </remarks>
    [Fact]
    public void AReferencedAssemblyCanBeRebuiltAndDeletedWhileItIsReferenced()
    {
        ReferenceCatalog catalogue = new();
        _ = catalogue.Add([_assembly]);

        // Presence, not Add's return value. Add reports how much the catalogue grew, and it also
        // picks up assemblies the process loaded in between - so asserting on the number passes
        // this test alone and fails it in a full run. It did.
        Assert.Contains(catalogue.References, Named(_assembly));

        foreach (MetadataReference reference in catalogue.References)
        {
            if (reference is PortableExecutableReference portable
                && string.Equals(portable.FilePath, _assembly, StringComparison.OrdinalIgnoreCase))
            {
                _ = portable.GetMetadata();
            }
        }

        // A rebuild writes over the file; some tools delete and replace it instead. Both.
        File.WriteAllBytes(_assembly, File.ReadAllBytes(typeof(Spark.Nodes.Core.Point).Assembly.Location));
        File.Delete(_assembly);

        Assert.False(File.Exists(_assembly));
    }

    /// <summary>
    /// <b>Removing an added reference takes it out and bumps the version</b>, so nothing cached
    /// against the old set is reused. A removal that did not invalidate the cache would leave a
    /// script compiling against an assembly the user had just taken away.
    /// </summary>
    [Fact]
    public void RemovingAnAddedReferenceDropsItAndInvalidatesTheCache()
    {
        ReferenceCatalog catalogue = new();
        catalogue.Add([_assembly]);

        int version = catalogue.Version;

        Assert.True(catalogue.Remove(_assembly));
        Assert.DoesNotContain(catalogue.References, Named(_assembly));
        Assert.True(catalogue.Version > version);

        Assert.False(catalogue.Remove(_assembly));
    }

    /// <summary>
    /// <b>Reloading replaces the metadata rather than keeping the old.</b> The catalogue is
    /// deliberately additive — it keeps an existing reference for any path a rebuild does not
    /// already have — and that is exactly what would make a reload silently do nothing.
    /// </summary>
    [Fact]
    public void ReloadingReplacesTheReferenceRatherThanKeepingTheOldOne()
    {
        ReferenceCatalog catalogue = new();
        catalogue.Add([_assembly]);

        MetadataReference first = catalogue.References.First(reference => Named(_assembly)(reference));

        Assert.True(catalogue.Reload(_assembly));

        MetadataReference second = catalogue.References.First(reference => Named(_assembly)(reference));

        Assert.NotSame(first, second);
        Assert.Single(catalogue.References.Where(reference => Named(_assembly)(reference)));
    }

    /// <summary>An assembly nobody has been asked about is not trusted.</summary>
    [Fact]
    public void ANewAssemblyIsNotTrusted()
    {
        LocalReferenceStore store = new(_record);

        Assert.False(store.IsTrusted(_assembly));
        Assert.False(store.HasChanged(_assembly));
        Assert.Empty(store.All());
    }

    /// <summary>Agreeing records the hash, and the same file stays trusted.</summary>
    [Fact]
    public void AgreeingRecordsTheHashAndTheSameFileStaysTrusted()
    {
        LocalReferenceStore store = new(_record);

        LocalReference agreed = store.Trust(_assembly);

        Assert.True(agreed.Exists);
        Assert.Equal(64, agreed.Hash.Length);
        Assert.True(store.IsTrusted(_assembly));
        Assert.False(store.HasChanged(_assembly));
        Assert.Equal("Acme.Helpers.dll", Assert.Single(store.All()).Name);
    }

    /// <summary>
    /// <b>A rebuild re-prompts</b>, which is the row's own sentence. The file is rewritten with
    /// different contents and the agreement no longer holds.
    /// </summary>
    [Fact]
    public void ARebuiltAssemblyIsNoLongerTrustedAndIsReportedAsChanged()
    {
        LocalReferenceStore store = new(_record);
        store.Trust(_assembly);

        File.WriteAllBytes(_assembly, File.ReadAllBytes(typeof(Spark.Api.SparkNodeAttribute).Assembly.Location));

        Assert.False(store.IsTrusted(_assembly));
        Assert.True(store.HasChanged(_assembly));
    }

    /// <summary>
    /// <b>Changed and unknown are different states</b>, because they need different words in front
    /// of a user: one is <i>you have not been asked</i>, the other is <i>this is not what you
    /// agreed to</i>.
    /// </summary>
    [Fact]
    public void AnUnknownAssemblyIsNotReportedAsChanged()
    {
        LocalReferenceStore store = new(_record);

        Assert.False(store.IsTrusted(_assembly));
        Assert.False(store.HasChanged(_assembly));
    }

    /// <summary>An assembly that has gone is neither trusted nor reported as merely changed.</summary>
    [Fact]
    public void AMissingAssemblyIsNotTrustedAndIsNotCalledChanged()
    {
        LocalReferenceStore store = new(_record);
        store.Trust(_assembly);

        File.Delete(_assembly);

        Assert.False(store.IsTrusted(_assembly));
        Assert.False(store.HasChanged(_assembly));
        Assert.False(Assert.Single(store.All()).Exists);
    }

    /// <summary>Decisions survive into the next session.</summary>
    [Fact]
    public void ADecisionSurvivesIntoTheNextSession()
    {
        new LocalReferenceStore(_record).Trust(_assembly);

        Assert.True(new LocalReferenceStore(_record).IsTrusted(_assembly));
    }

    /// <summary>Forgetting one drops it; forgetting all empties the store.</summary>
    [Fact]
    public void ForgettingDropsTheDecision()
    {
        LocalReferenceStore store = new(_record);
        store.Trust(_assembly);

        Assert.True(store.Forget(_assembly));
        Assert.False(store.IsTrusted(_assembly));
        Assert.Empty(store.All());
        Assert.False(store.Forget(_assembly));
    }

    /// <summary>A store with nowhere to write still works for the session and remembers nothing.</summary>
    [Fact]
    public void AStoreWithNowhereToWriteRemembersNothing()
    {
        LocalReferenceStore store = new(path: null);
        store.Trust(_assembly);

        Assert.True(store.IsTrusted(_assembly));
        Assert.False(new LocalReferenceStore(path: null).IsTrusted(_assembly));
    }

    /// <summary>
    /// <b>Choosing shows what agreeing means and references nothing.</b> The prompt names the file,
    /// the folder and the hash, and says plainly that the code will run.
    /// </summary>
    [Fact]
    public void ChoosingShowsThePromptAndReferencesNothing()
    {
        ReferenceCatalog catalogue = new();
        using LocalReferencesViewModel model = Model(catalogue);

        model.Choose(_assembly);

        Assert.True(model.HasPendingTrust);
        Assert.Contains("Acme.Helpers.dll", model.Prompt, StringComparison.Ordinal);
        Assert.Contains("SHA-256", model.Prompt, StringComparison.Ordinal);
        Assert.Contains("full permissions", model.Prompt, StringComparison.Ordinal);

        Assert.DoesNotContain(catalogue.References, Named(_assembly));
        Assert.Empty(model.References);
    }

    /// <summary>Agreeing references it and the list shows it.</summary>
    [Fact]
    public void AgreeingReferencesItAndTheListShowsIt()
    {
        ReferenceCatalog catalogue = new();
        using LocalReferencesViewModel model = Model(catalogue);

        model.Choose(_assembly);

        Assert.True(model.Confirm());
        Assert.False(model.HasPendingTrust);
        Assert.Contains(catalogue.References, Named(_assembly));

        LocalReferenceRow row = Assert.Single(model.References);

        Assert.Equal("Acme.Helpers.dll", row.Title);
        Assert.False(row.NeedsAttention);
        Assert.Contains("referenced", row.Detail, StringComparison.Ordinal);
    }

    /// <summary>Declining references nothing and records nothing.</summary>
    [Fact]
    public void DecliningReferencesNothing()
    {
        ReferenceCatalog catalogue = new();
        using LocalReferencesViewModel model = Model(catalogue);

        model.Choose(_assembly);
        model.Cancel();

        Assert.False(model.HasPendingTrust);
        Assert.Empty(model.Prompt);
        Assert.Empty(model.References);
        Assert.DoesNotContain(catalogue.References, Named(_assembly));
        Assert.False(new LocalReferenceStore(_record).IsTrusted(_assembly));
    }

    /// <summary>
    /// <b>An assembly rebuilt between sessions is listed, marked, and not compiled against</b>
    /// until somebody looks at it. <see cref="LocalReferencesViewModel.Apply"/> is what runs at
    /// startup, and applying a changed assembly would be agreeing on the user's behalf.
    /// </summary>
    [Fact]
    public void AnAssemblyRebuiltBetweenSessionsIsMarkedAndNotApplied()
    {
        new LocalReferenceStore(_record).Trust(_assembly);
        File.WriteAllBytes(_assembly, File.ReadAllBytes(typeof(Spark.Api.SparkNodeAttribute).Assembly.Location));

        ReferenceCatalog catalogue = new();
        using LocalReferencesViewModel model = Model(catalogue);

        Assert.Equal(0, model.Apply());
        Assert.DoesNotContain(catalogue.References, Named(_assembly));

        LocalReferenceRow row = Assert.Single(model.References);

        Assert.True(row.NeedsAttention);
        Assert.Contains("rebuilt", row.Detail, StringComparison.Ordinal);
    }

    /// <summary>An unchanged assembly is applied at startup without asking again.</summary>
    [Fact]
    public void AnUnchangedAssemblyIsAppliedAtStartupWithoutAsking()
    {
        new LocalReferenceStore(_record).Trust(_assembly);

        ReferenceCatalog catalogue = new();
        using LocalReferencesViewModel model = Model(catalogue);

        Assert.Equal(1, model.Apply());
        Assert.Contains(catalogue.References, Named(_assembly));
        Assert.False(model.HasPendingTrust);
        Assert.False(Assert.Single(model.References).NeedsAttention);
    }

    /// <summary>Reloading a rebuilt assembly asks again rather than applying it.</summary>
    [Fact]
    public void ReloadingARebuiltAssemblyAsksAgain()
    {
        ReferenceCatalog catalogue = new();
        using LocalReferencesViewModel model = Model(catalogue);

        model.Choose(_assembly);
        model.Confirm();

        File.WriteAllBytes(_assembly, File.ReadAllBytes(typeof(Spark.Api.SparkNodeAttribute).Assembly.Location));

        model.Reload(Assert.Single(model.References));

        Assert.True(model.HasPendingTrust);
        Assert.Contains("Acme.Helpers.dll", model.Prompt, StringComparison.Ordinal);
    }

    /// <summary>Forgetting takes it out of the catalogue as well as the store.</summary>
    [Fact]
    public void ForgettingTakesItOutOfTheCatalogue()
    {
        ReferenceCatalog catalogue = new();
        using LocalReferencesViewModel model = Model(catalogue);

        model.Choose(_assembly);
        model.Confirm();

        model.Remove(Assert.Single(model.References));

        Assert.Empty(model.References);
        Assert.DoesNotContain(catalogue.References, Named(_assembly));
        Assert.False(new LocalReferenceStore(_record).IsTrusted(_assembly));
    }

    /// <summary>
    /// <b>The watcher announces a rebuild and reloads nothing.</b> A reference that swapped itself
    /// out underneath a running graph would change what the graph computes without anybody asking.
    /// </summary>
    [Fact]
    public void TheWatcherAnnouncesARebuildAndReloadsNothing()
    {
        using LocalReferenceWatcher watcher = new();
        using ManualResetEventSlim announced = new(false);

        watcher.Changed += (_, _) => announced.Set();

        Assert.True(watcher.Watch(_assembly));
        Assert.Equal(1, watcher.Count);

        File.WriteAllBytes(_assembly, File.ReadAllBytes(typeof(Spark.Api.SparkNodeAttribute).Assembly.Location));

        Assert.True(
            announced.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken),
            "the watcher never reported a rebuild of the assembly it was watching");

        Assert.True(watcher.Unwatch(_assembly));
        Assert.Equal(0, watcher.Count);
    }

    /// <summary>A file in a directory that does not exist cannot be watched, and that is not a throw.</summary>
    [Fact]
    public void AFileInAMissingDirectoryIsNotWatched()
    {
        using LocalReferenceWatcher watcher = new();

        Assert.False(watcher.Watch(Path.Combine(_folder, "gone", "Nothing.dll")));
        Assert.Equal(0, watcher.Count);
        Assert.False(watcher.Unwatch(Path.Combine(_folder, "gone", "Nothing.dll")));
    }

    /// <summary>
    /// <b>The payoff: a code block calls a method that exists only in the referenced assembly.</b>
    /// Everything else here asserts that a path reached a list; this asserts that a user who added
    /// a DLL can actually use it, which is the only claim worth making.
    /// </summary>
    /// <remarks>
    /// The assembly is compiled by the test rather than copied, because it has to contain a type
    /// this process has never loaded. A copy of something already in memory would resolve against
    /// the loaded one and prove nothing.
    /// </remarks>
    [Fact]
    public void ACodeBlockCanCallIntoAReferencedAssembly()
    {
        string library = Compile("Acme.Maths", """
            namespace Acme.Maths
            {
                public static class Helpers
                {
                    public static double Twice(double value) => value * 2.0;
                }
            }
            """);

        ReferenceCatalog catalogue = new();
        using LocalReferencesViewModel model = Model(catalogue);

        model.Choose(library);
        Assert.True(model.Confirm());

        Spark.Api.NodeDefinitionSource block = new ScriptNodeFactory(catalogue)
            .Create("return Acme.Maths.Helpers.Twice(a);");

        Assert.Equal(84.0, Assert.Single(block.Invoke([42.0], CancellationToken.None)));
    }

    /// <summary>
    /// And forgetting it takes the ability away again: the same code block no longer compiles.
    /// A reference that lingered after being removed would be a reference nobody could account for.
    /// </summary>
    [Fact]
    public void ForgettingAnAssemblyTakesItsTypesAwayAgain()
    {
        string library = Compile("Acme.Maths2", """
            namespace Acme.Maths2
            {
                public static class Helpers
                {
                    public static double Twice(double value) => value * 2.0;
                }
            }
            """);

        ReferenceCatalog catalogue = new();
        using LocalReferencesViewModel model = Model(catalogue);

        model.Choose(library);
        model.Confirm();
        model.Remove(Assert.Single(model.References));

        // A script that does not compile still yields a definition, so the node keeps its place
        // and its wires; the failure is reported when it is evaluated. So the assertion is on the
        // invoke, not on Create.
        Spark.Api.NodeDefinitionSource block = new ScriptNodeFactory(catalogue)
            .Create("return Acme.Maths2.Helpers.Twice(a);");

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => block.Invoke([42.0], CancellationToken.None));

        Assert.Contains("Acme", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The claim in its strongest form: the DLL can be rebuilt after a script has run against
    /// it.</b> Compiling against a file is one open handle and loading it is another, and the
    /// second is the one that would ordinarily be held for the life of the context. If this ever
    /// fails, a user's build fails with <c>the process cannot access the file</c> and the reason
    /// is Spark.
    /// </summary>
    [Fact]
    public void AReferencedAssemblyCanBeRebuiltAfterAScriptHasRunAgainstIt()
    {
        string library = Compile("Acme.Maths3", """
            namespace Acme.Maths3
            {
                public static class Helpers
                {
                    public static double Twice(double value) => value * 2.0;
                }
            }
            """);

        ReferenceCatalog catalogue = new();
        using LocalReferencesViewModel model = Model(catalogue);

        model.Choose(library);
        model.Confirm();

        Spark.Api.NodeDefinitionSource block = new ScriptNodeFactory(catalogue)
            .Create("return Acme.Maths3.Helpers.Twice(a);");

        Assert.Equal(84.0, Assert.Single(block.Invoke([42.0], CancellationToken.None)));

        // The assembly is loaded now. A rebuild must still be possible.
        File.WriteAllBytes(library, File.ReadAllBytes(typeof(Spark.Api.SparkNodeAttribute).Assembly.Location));
        File.Delete(library);

        Assert.False(File.Exists(library));
    }

    /// <summary>Compiles a tiny assembly into the scratch folder and returns its path.</summary>
    private string Compile(string name, string source)
    {
        Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation =
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                name,
                [Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseSyntaxTree(source)],
                new ReferenceCatalog().References,
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        string path = Path.Combine(_folder, name + ".dll");
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(path);

        Assert.True(
            emitted.Success,
            "the test's own library did not compile: "
            + string.Join("; ", emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        return path;
    }

    private LocalReferencesViewModel Model(ReferenceCatalog catalogue) =>
        new(new LocalReferenceStore(_record), () => catalogue);

    private static Predicate<MetadataReference> Named(string path) => reference =>
        reference is PortableExecutableReference portable
        && string.Equals(portable.FilePath, path, StringComparison.OrdinalIgnoreCase);
}
