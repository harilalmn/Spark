using System;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// The undo stack's ordering rules, checked on plain strings.
/// </summary>
/// <remarks>
/// <see cref="DocumentHistory"/> deals in document text and knows nothing about graphs, so its
/// rules — what a step is, what clears the redo branch, what happens at the depth limit — are
/// testable without a session, a library or a window. The tests that check undo actually restores
/// a graph live in <c>UndoRedoTests</c>; these check the arithmetic those rely on.
/// </remarks>
public sealed class DocumentHistoryTests
{
    /// <summary>A history with nothing in it offers nothing in either direction.</summary>
    [Fact]
    public void AFreshHistoryOffersNothing()
    {
        DocumentHistory history = new();
        history.Reset("a");

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.UndoLabel);
        Assert.Null(history.RedoLabel);
        Assert.Equal("a", history.Present);
    }

    /// <summary>An edit becomes a step, named by what it did.</summary>
    [Fact]
    public void RecordingAnEditMakesItUndoableUnderItsOwnName()
    {
        DocumentHistory history = new();
        history.Reset("a");

        Assert.True(history.Record("Move node", "b"));

        Assert.True(history.CanUndo);
        Assert.Equal("Move node", history.UndoLabel);
        Assert.Equal("b", history.Present);
    }

    /// <summary>Undo and redo walk the same step in both directions, carrying its label.</summary>
    [Fact]
    public void UndoAndRedoWalkTheSameStepBothWays()
    {
        DocumentHistory history = new();
        history.Reset("a");
        history.Record("Connect", "b");

        Assert.Equal("a", history.Undo());
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Equal("Connect", history.RedoLabel);
        Assert.Equal("a", history.Present);

        Assert.Equal("b", history.Redo());
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal("Connect", history.UndoLabel);
        Assert.Equal("b", history.Present);
    }

    /// <summary>Undoing past the beginning, or redoing past the end, answers with nothing.</summary>
    [Fact]
    public void SteppingPastEitherEndAnswersWithNothing()
    {
        DocumentHistory history = new();
        history.Reset("a");

        Assert.Null(history.Undo());
        Assert.Null(history.Redo());
    }

    /// <summary>
    /// An edit that left the document exactly as it was is not a step.
    /// </summary>
    /// <remarks>
    /// Committing the same literal twice is the case that produces this, and recording it would put
    /// an entry on the stack whose undo does nothing visible — which reads as undo being broken.
    /// </remarks>
    [Fact]
    public void AnEditThatChangedNothingIsNotAStep()
    {
        DocumentHistory history = new();
        history.Reset("a");

        Assert.False(history.Record("Change end", "a"));
        Assert.False(history.CanUndo);
        Assert.Equal(0, history.UndoDepth);
    }

    /// <summary>A new edit after an undo abandons the branch that was undone.</summary>
    [Fact]
    public void RecordingAfterAnUndoDiscardsTheRedo()
    {
        DocumentHistory history = new();
        history.Reset("a");
        history.Record("Connect", "b");
        history.Undo();

        Assert.True(history.CanRedo);

        history.Record("Delete node", "c");

        Assert.False(history.CanRedo);
        Assert.Equal("Delete node", history.UndoLabel);
        Assert.Equal("a", history.Undo());
    }

    /// <summary>
    /// At the depth limit the oldest step falls off the end rather than the newest being refused.
    /// </summary>
    /// <remarks>
    /// The cap is what stops a long editing session growing without bound. Which end it drops is
    /// not a detail: dropping the newest would silently stop recording, and the user would find out
    /// by pressing Ctrl+Z and watching nothing happen.
    /// </remarks>
    [Fact]
    public void TheOldestStepFallsOffTheEndAtTheDepthLimit()
    {
        DocumentHistory history = new(depth: 3);
        history.Reset("0");

        for (int edit = 1; edit <= 5; edit++)
        {
            history.Record("Edit " + edit, edit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(3, history.UndoDepth);
        Assert.Equal("Edit 5", history.UndoLabel);

        Assert.Equal("4", history.Undo());
        Assert.Equal("3", history.Undo());
        Assert.Equal("2", history.Undo());

        // "1" and "0" are gone: three steps back is all a depth of three promises.
        Assert.False(history.CanUndo);
    }

    /// <summary>Resetting to a new document abandons both directions.</summary>
    [Fact]
    public void ResetStartsAgainAtANewDocument()
    {
        DocumentHistory history = new();
        history.Reset("a");
        history.Record("Connect", "b");
        history.Undo();

        history.Reset("z");

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal("z", history.Present);
    }

    /// <summary>
    /// When no before-state could be taken, the edit is not undoable — and the one after it is.
    /// </summary>
    /// <remarks>
    /// This is the path a document that cannot be written takes. Offering an undo whose target was
    /// never captured would restore something other than what the user had, so it offers none; the
    /// edit still becomes the present, so the history recovers on the next one.
    /// </remarks>
    [Fact]
    public void AnEditWithNoBeforeStateIsNotUndoableAndTheNextOneIs()
    {
        DocumentHistory history = new();
        history.Reset(null);

        Assert.False(history.Record("Connect", "b"));
        Assert.False(history.CanUndo);

        Assert.True(history.Record("Delete node", "c"));
        Assert.Equal("b", history.Undo());
    }

    /// <summary>Clearing keeps the document but abandons every step.</summary>
    [Fact]
    public void ClearKeepsTheDocumentAndDropsTheSteps()
    {
        DocumentHistory history = new();
        history.Reset("a");
        history.Record("Connect", "b");

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.Equal("b", history.Present);
    }

    /// <summary>A depth of zero or less is refused rather than treated as unlimited.</summary>
    [Fact]
    public void ANonPositiveDepthIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentHistory(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentHistory(-1));
    }
}
