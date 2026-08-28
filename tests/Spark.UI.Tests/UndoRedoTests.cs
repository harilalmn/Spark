using System;
using System.Linq;
using System.Threading.Tasks;
using Spark.UI.Graph;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// Undo and redo through the shell: a real document, a real session and the real `.spark` writer.
/// </summary>
/// <remarks>
/// <para>
/// These are the tests that say undo <i>restores a graph</i>, as opposed to
/// <see cref="DocumentHistoryTests"/>, which say the stack counts correctly. Every one of them
/// asserts on the document after the step rather than on the stack's depth, because "there was
/// something to undo" and "the graph went back" are different claims.
/// </para>
/// <para>
/// The last of them is the one that has been owed since <c>E3-T8</c>: the provenance cache's whole
/// justification is that undo is free, and until there was an undo stack nothing in the repository
/// exercised it.
/// </para>
/// </remarks>
public sealed class UndoRedoTests
{
    /// <summary>A graph as it opens offers nothing to undo.</summary>
    [Fact]
    public void ANewlyOpenedDocumentHasNothingToUndo()
    {
        using MainWindowViewModel model = new();

        Assert.False(model.CanUndo);
        Assert.False(model.CanRedo);
        Assert.Equal("Nothing to undo", model.UndoDescription);
    }

    /// <summary>Placing a node from the library is undone, and redone.</summary>
    [Fact]
    public async Task PlacingANodeIsUndoneAndRedone()
    {
        using MainWindowViewModel model = new();
        int before = model.Graph.Nodes.Count;

        model.SelectedLibraryEntry =
            model.AllLibraryEntries.First(entry => entry.DisplayName == "Point.Origin");
        model.PlaceSelectedLibraryEntry(0, 0);

        Assert.Equal(before + 1, model.Graph.Nodes.Count);
        Assert.True(model.CanUndo);
        Assert.Equal("Undo Add Point.Origin", model.UndoDescription);

        model.Undo();

        Assert.Equal(before, model.Graph.Nodes.Count);
        Assert.DoesNotContain(model.Graph.Nodes, node => node.Title == "Point.Origin");
        Assert.True(model.CanRedo);
        Assert.Equal("Redo Add Point.Origin", model.RedoDescription);

        model.Redo();

        Assert.Equal(before + 1, model.Graph.Nodes.Count);
        Assert.Contains(model.Graph.Nodes, node => node.Title == "Point.Origin");

        await model.EvaluateAsync();
    }

    /// <summary>
    /// Undoing a literal puts back both the value and the geometry it produced.
    /// </summary>
    /// <remarks>
    /// The viewport assertion is the point. A document that reads correctly but draws the previous
    /// run's geometry is the failure this slice is most exposed to, because undo swaps the whole
    /// document and the scene is published separately from it.
    /// </remarks>
    [Fact]
    public async Task UndoRestoresALiteralAndTheGeometryItProduced()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();

        Assert.Equal(100 * 8, model.Scene.Snapshot().Single().TriangleCount);

        model.ShowSelection([SlotOf(model, "Number.Range")]);
        PortLiteralViewModel end = model.Inspector.Single(port => port.Name == "end");
        end.Text = "2";
        end.Commit();

        await model.EvaluateAsync();
        Assert.Equal(30 * 8, model.Scene.Snapshot().Single().TriangleCount);
        Assert.Equal("Undo Change end", model.UndoDescription);

        model.Undo();
        await model.EvaluateAsync();

        Assert.Equal(100 * 8, model.Scene.Snapshot().Single().TriangleCount);
    }

    /// <summary>
    /// Moving a node is undone, which is what makes the snapshot's reach worth its cost.
    /// </summary>
    /// <remarks>
    /// A position is not part of the engine graph at all - it lives on the canvas node and reaches
    /// the file through <see cref="CanvasDocument"/>. An inverse-command stack built over the
    /// engine's own mutations would not have covered this without a second mechanism.
    /// </remarks>
    [Fact]
    public async Task UndoRestoresWhereANodeWas()
    {
        using MainWindowViewModel model = new();
        CanvasNode node = model.Graph.Nodes[0];
        Spark.Engine.NodeId id = node.Id;
        double x = node.X;
        double y = node.Y;

        node.X += 120;
        node.Y += 45;
        model.RecordEdit("Move node");

        Assert.True(model.CanUndo);
        model.Undo();

        // Found by identity rather than by slot: a restored document is adopted in the file's
        // canonical order, so slot 0 afterwards is not necessarily the node that was moved (N21).
        CanvasNode restored = model.Graph.Nodes[model.Graph.SlotOf(id)];
        Assert.Equal(x, restored.X, 6);
        Assert.Equal(y, restored.Y, 6);

        await model.EvaluateAsync();
    }

    /// <summary>An edit that changed nothing does not become a step.</summary>
    [Fact]
    public void CommittingTheSameLiteralTwiceIsOneStep()
    {
        using MainWindowViewModel model = new();

        model.ShowSelection([SlotOf(model, "Number.Range")]);
        PortLiteralViewModel end = model.Inspector.Single(port => port.Name == "end");

        end.Text = "2";
        end.Commit();
        end.Commit();

        model.Undo();

        Assert.False(model.CanUndo);
    }

    /// <summary>
    /// The commands the toolbar buttons and Ctrl+Z bind to gate themselves, and take the same steps.
    /// </summary>
    /// <remarks>
    /// The other tests here call <c>Undo()</c> directly, which is not what a button does. A button
    /// asks <c>CanExecute</c> first and is drawn disabled when the answer is no, so this drives the
    /// commands themselves — otherwise the gating could be wrong in exactly the way that leaves a
    /// live-looking button doing nothing.
    /// </remarks>
    [Fact]
    public void TheUndoAndRedoCommandsGateThemselves()
    {
        using MainWindowViewModel model = new();

        Assert.False(model.UndoCommand.CanExecute(null));
        Assert.False(model.RedoCommand.CanExecute(null));

        model.SelectedLibraryEntry =
            model.AllLibraryEntries.First(entry => entry.DisplayName == "Point.Origin");
        model.PlaceSelectedLibraryEntry(0, 0);

        Assert.True(model.UndoCommand.CanExecute(null));
        model.UndoCommand.Execute(null);

        Assert.False(model.UndoCommand.CanExecute(null));
        Assert.True(model.RedoCommand.CanExecute(null));
        Assert.DoesNotContain(model.Graph.Nodes, node => node.Title == "Point.Origin");

        model.RedoCommand.Execute(null);

        Assert.Contains(model.Graph.Nodes, node => node.Title == "Point.Origin");
    }

    /// <summary>Opening a document starts a new history rather than extending the old one.</summary>
    [Fact]
    public void OpeningADocumentStartsANewHistory()
    {
        using MainWindowViewModel model = new();

        model.SelectedLibraryEntry =
            model.AllLibraryEntries.First(entry => entry.DisplayName == "Point.Origin");
        model.PlaceSelectedLibraryEntry(0, 0);
        Assert.True(model.CanUndo);

        string document = Assert.IsType<string>(model.TrySaveDocument());
        Assert.True(model.TryOpenDocument(document));

        // Undoing across a document boundary would bring back a graph the user had closed.
        Assert.False(model.CanUndo);
        Assert.False(model.CanRedo);
    }

    /// <summary>
    /// The run an undo starts recomputes nothing: every result it needs is still in the cache.
    /// </summary>
    /// <remarks>
    /// This is <c>E3-T8</c>'s acceptance criterion, and it could not be checked until there was an
    /// undo stack to check it with. The cache is keyed by provenance rather than by document
    /// (<see cref="Spark.Engine.CacheKey"/>), so restoring a former state re-derives the same keys
    /// and every one of them is still resident - which is why undo is instant rather than a second
    /// evaluation of the whole graph.
    /// </remarks>
    [Fact]
    public async Task TheRunAfterAnUndoRecomputesNothing()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();

        // How many nodes a run of this graph reaches at all, which is what "recomputed nothing"
        // has to be measured against. It is not the node count: a node whose upstream produced
        // nothing is never visited.
        int visited = model.LastRunNodesEvaluated + model.LastRunCacheHits;
        Assert.True(visited > 0);

        model.ShowSelection([SlotOf(model, "Number.Range")]);
        PortLiteralViewModel end = model.Inspector.Single(port => port.Name == "end");
        end.Text = "2";
        end.Commit();

        await model.EvaluateAsync();
        Assert.True(model.LastRunNodesEvaluated > 0, "The edited graph should have computed something.");

        // The run undo starts is the one that matters. Awaiting a fresh evaluation instead would
        // measure a second run over an already-warmed cache and prove nothing.
        Task restored = NextEvaluation(model);
        model.Undo();
        await restored;

        Assert.Equal(0, model.LastRunNodesEvaluated);
        Assert.Equal(visited, model.LastRunCacheHits);
    }

    private static Task NextEvaluation(MainWindowViewModel model)
    {
        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, EventArgs e)
        {
            model.EvaluationCompleted -= Handler;
            completed.TrySetResult();
        }

        model.EvaluationCompleted += Handler;
        return completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static int SlotOf(MainWindowViewModel model, string title)
    {
        for (int slot = 0; slot < model.Graph.Nodes.Count; slot++)
        {
            if (string.Equals(model.Graph.Nodes[slot].Title, title, StringComparison.Ordinal))
            {
                return slot;
            }
        }

        Assert.Fail("No node titled '" + title + "' in the demo graph.");
        return -1;
    }
}
