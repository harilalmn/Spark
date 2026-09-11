using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Spark.Scripting;
using Spark.UI.Graph;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// A library that arrives after the code block that needs it (`E7-T24`).
/// </summary>
/// <remarks>
/// <para>
/// <b>The ordinary order of events, not an edge case.</b> A user types a block naming a type from a
/// library they have not added yet, sees it fail, and adds the library — through <i>Add as a
/// library…</i> (`E7-T21`) or the Local assemblies tab (`E7-T9`). Both change the reference catalogue
/// and nothing else, and a block's key is deliberately blind to the catalogue
/// ([N146](../../docs/NOTES.md)), so nothing tells the block to compile again.
/// </para>
/// <para>
/// <b>The library is added to the catalogue directly</b>, because that is the whole of what both
/// paths do once their own disclosure is answered — <c>Add</c> for the one, <c>Reload</c> (which is
/// remove-then-add) for the other — and going through either would write to the user's own record of
/// agreed assemblies.
/// </para>
/// </remarks>
public sealed class LibraryArrivalTests : IDisposable
{
    private readonly string _folder;

    public LibraryArrivalTests()
    {
        _ = typeof(Spark.Geometry.Point3d).Assembly.Location;

        _folder = Path.Combine(Path.GetTempPath(), "spark-library-arrival", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A block ran against the library, so Windows has it mapped. The next run makes its own.
        }
    }

    /// <summary>
    /// <b>The row, as a test.</b> The block fails while the library is absent, and runs once it has
    /// been added and the graph evaluates again — with no edit to the block in between.
    /// </summary>
    [Fact]
    public async Task ABlockThatFailedForAMissingLibraryRunsOnceTheLibraryArrives()
    {
        string name = "Lib" + Guid.NewGuid().ToString("n");
        string library = Compile(name, $$"""
            namespace {{name}}
            {
                public static class Payload
                {
                    public static int Run() => 42;
                }
            }
            """);

        string saved;

        using (MainWindowViewModel author = new())
        {
            int slot = author.PlaceCodeBlock(0, 0);
            author.ShowCodeBlock(author.Graph.Nodes[slot]);
            author.ScriptText = $"return {name}.Payload.Run();";

            Assert.True(author.CommitScriptText(), "the block's text did not reach the node");

            saved = Assert.IsType<string>(author.TrySaveDocument());
        }

        using MainWindowViewModel model = new();
        Assert.True(model.TryOpenDocument(saved, origin: null));

        await model.EvaluateAsync();
        Assert.Contains(model.Graph.Nodes, node => node.State.HasFlag(CanvasNodeState.Error));

        ReferenceCatalog catalogue = Assert.IsType<ReferenceCatalog>(model.ScriptReferences());
        _ = catalogue.Add([library]);

        await model.EvaluateAsync();
        Assert.DoesNotContain(model.Graph.Nodes, node => node.State.HasFlag(CanvasNodeState.Error));
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
