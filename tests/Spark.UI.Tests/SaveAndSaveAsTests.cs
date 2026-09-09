using System;
using System.IO;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// Which file a save writes to, and when it is allowed to be silent — `E8-T78`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reported by the client</b>: <i>there is no Save option in Spark, we have Save As only; a file
/// opened, on pressing Ctrl+S, shows the dialog for Save As instead of saving.</i> The menu had one
/// item, <c>Save as…</c>, bound to <c>Ctrl+S</c> and wired to a handler that always opened the file
/// picker — so saving a file you opened five seconds ago meant a dialog and a chance to put it
/// somewhere else by accident.
/// </para>
/// <para>
/// <b>These tests are about the path, not about the dialog.</b> The picker lives in the view and
/// needs a real storage provider, but the question that makes a silent save safe or dangerous is
/// <i>which file does the document think it belongs to</i> — and that is
/// <see cref="MainWindowViewModel.GraphPath"/>, which is testable without a window. <b>The
/// dangerous case is the second one below</b>: a silent <c>Ctrl+S</c> after <i>New</i> or after a
/// demo graph, with a stale path, would overwrite the user's file with something they never asked
/// to save there.
/// </para>
/// </remarks>
public sealed class SaveAndSaveAsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "spark-save-tests", Guid.NewGuid().ToString("n"));

    public SaveAndSaveAsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// <b>A document opened from a file knows where it came from</b>, which is what lets Save be
    /// silent: there is no question left to ask.
    /// </summary>
    [Fact]
    public void AnOpenedDocumentKnowsItsFile()
    {
        using MainWindowViewModel model = new();
        string path = Path.Combine(_root, "tower.spark");

        Assert.Null(model.GraphPath);

        Assert.True(model.TryOpenDocument(Saved(model), path));
        Assert.Equal(path, model.GraphPath);
    }

    /// <summary>
    /// <b>The dangerous case, and the reason the path is cleared where the document is replaced.</b>
    /// New, and each of the four demo graphs, are different documents — so a silent Save after one
    /// of them must ask where to go rather than write over whatever was open before.
    /// </summary>
    [Fact]
    public void ReplacingTheDocumentForgetsWhichFileItCameFrom()
    {
        using MainWindowViewModel model = new();
        string path = Path.Combine(_root, "tower.spark");

        Assert.True(model.TryOpenDocument(Saved(model), path));
        Assert.Equal(path, model.GraphPath);

        model.NewGraph();
        Assert.Null(model.GraphPath);

        Assert.True(model.TryOpenDocument(Saved(model), path));
        model.LoadDemo();
        Assert.Null(model.GraphPath);

        Assert.True(model.TryOpenDocument(Saved(model), path));
        model.LoadSolids();
        Assert.Null(model.GraphPath);
    }

    /// <summary>
    /// <b>Undo does not change which file you are editing.</b> It replaces the graph, as New does,
    /// and it is the one replacement that must keep the path — or a save after an undo would ask
    /// where to go for a file the user has had open all along.
    /// </summary>
    [Fact]
    public void UndoKeepsTheFile()
    {
        using MainWindowViewModel model = new();
        string path = Path.Combine(_root, "tower.spark");

        model.LoadDemo();
        Assert.True(model.TryOpenDocument(Saved(model), path));

        model.RecordEdit("Move a node");
        model.Undo();

        Assert.Equal(path, model.GraphPath);
    }

    /// <summary>
    /// A document with no file has nothing to be silent about, so Save has a question to ask and
    /// falls through to the picker.
    /// </summary>
    [Fact]
    public void AScratchDocumentHasNoFile()
    {
        using MainWindowViewModel model = new();

        Assert.Null(model.GraphPath);

        // And loading a demo does not invent one.
        model.LoadCurves();
        Assert.Null(model.GraphPath);
    }

    /// <summary>
    /// Saving re-points the document, which is what makes the Packages tab stop refusing on the
    /// window that is already open (`E7-T18`).
    /// </summary>
    [Fact]
    public void NotingASavePointsTheDocumentAtItsNewFile()
    {
        using MainWindowViewModel model = new();
        PackageBrowserViewModel packages = model.Packages();

        Assert.False(packages.IsGraphSaved);

        model.NoteGraphPath(Path.Combine(_root, "tower.spark"));

        Assert.True(packages.IsGraphSaved);
        Assert.Equal(Path.Combine(_root, "tower.spark"), model.GraphPath);
    }

    /// <summary>The document as text, which is what a save writes.</summary>
    private static string Saved(MainWindowViewModel model)
    {
        string? text = model.TrySaveDocument();
        Assert.NotNull(text);
        return text;
    }
}
