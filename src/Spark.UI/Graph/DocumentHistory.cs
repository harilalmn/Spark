using System;
using System.Collections.Generic;

namespace Spark.UI.Graph;

/// <summary>
/// Undo and redo, as a stack of whole documents snapshotted to `.spark` text.
/// </summary>
/// <remarks>
/// <para>
/// <b>A snapshot rather than an inverse operation, and the reason is coverage</b>
/// ([ADR-0022](../../../docs/adr/0022-undo-by-document-snapshot.md)). An inverse-command stack is
/// smaller in memory and gives better labels, but it is only as complete as the set of commands
/// somebody remembered to write an inverse for, and an edit that forgets to record is invisible
/// until a user loses work to it. A snapshot of the canonical file cannot miss part of an edit,
/// because the file is already the definition of what a document is.
/// </para>
/// <para>
/// <b>What this costs, stated rather than discovered.</b> One snapshot per edit, capped at
/// <see cref="Depth"/> of them: a few kilobytes each for a graph of ordinary size, and a few
/// hundred for a very large one. The cap is what stops a long editing session growing without
/// bound, and the price of the cap is that the oldest step falls off the end.
/// </para>
/// <para>
/// <b>What it does not cost is time.</b> Restoring a snapshot re-runs the graph, and every node
/// in it hits the provenance cache — the keys of the state being returned to are still resident,
/// because the cache is content-addressed and survives the document swap
/// (<c>E3-T8</c>, <see cref="Spark.Engine.CacheKey"/>). Undo is a re-read of a small JSON
/// document and a cache sweep, not a re-evaluation.
/// </para>
/// <para>
/// The type deals in text and knows nothing about graphs, sessions or the shell, so it is tested
/// on its own and its ordering rules do not need a window to check.
/// </para>
/// </remarks>
public sealed class DocumentHistory
{
    /// <summary>How many steps are kept when no other depth is asked for.</summary>
    public const int DefaultDepth = 64;

    private readonly List<Entry> _past = [];
    private readonly List<Entry> _future = [];
    private string? _present;

    /// <summary>Creates an empty history.</summary>
    /// <param name="depth">The greatest number of undo steps kept. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is not positive.</exception>
    public DocumentHistory(int depth = DefaultDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        Depth = depth;
    }

    /// <summary>The greatest number of undo steps kept.</summary>
    public int Depth { get; }

    /// <summary>Whether there is a step to go back to.</summary>
    public bool CanUndo => _past.Count > 0;

    /// <summary>Whether an undone step can be reapplied.</summary>
    public bool CanRedo => _future.Count > 0;

    /// <summary>What undo would reverse, named as the edit that did it, or null.</summary>
    public string? UndoLabel => _past.Count > 0 ? _past[^1].Label : null;

    /// <summary>What redo would reapply, or null.</summary>
    public string? RedoLabel => _future.Count > 0 ? _future[^1].Label : null;

    /// <summary>How many steps back are available.</summary>
    public int UndoDepth => _past.Count;

    /// <summary>How many steps forward are available.</summary>
    public int RedoDepth => _future.Count;

    /// <summary>
    /// The snapshot of the document as it now stands, or null when none could be taken.
    /// </summary>
    public string? Present => _present;

    /// <summary>
    /// Starts a fresh history at a document, discarding every step in both directions.
    /// </summary>
    /// <remarks>
    /// Opening a file, loading a demo or otherwise replacing the whole document starts again here.
    /// Undoing across a document boundary would restore a graph the user had closed, which is a
    /// different and much more surprising operation than the one the button promises.
    /// </remarks>
    /// <param name="snapshot">
    /// The document's text, or null when it could not be written — in which case the first edit is
    /// not undoable and the ones after it are.
    /// </param>
    public void Reset(string? snapshot)
    {
        _past.Clear();
        _future.Clear();
        _present = snapshot;
    }

    /// <summary>
    /// Records the document as it stands after an edit.
    /// </summary>
    /// <remarks>
    /// <b>An edit that changed nothing is not a step.</b> Committing the same literal twice, or
    /// dropping a node back where it was picked up, would otherwise fill the stack with entries
    /// whose undo does nothing visible — which reads as undo being broken rather than as the
    /// edit having been empty.
    /// </remarks>
    /// <param name="label">What the edit did, in the words the menu shows: <c>Move node</c>.</param>
    /// <param name="snapshot">The document's text after the edit.</param>
    /// <returns><see langword="true"/> when this became a step that can be undone.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public bool Record(string label, string snapshot)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.Equals(_present, snapshot, StringComparison.Ordinal))
        {
            return false;
        }

        _future.Clear();

        if (_present is not { } before)
        {
            // No before-state was ever taken, so there is nothing to go back to. The edit still
            // becomes the present, which makes the next one undoable.
            _present = snapshot;
            return false;
        }

        _past.Add(new Entry(label, before));
        _present = snapshot;

        if (_past.Count > Depth)
        {
            _past.RemoveAt(0);
        }

        return true;
    }

    /// <summary>Discards every step, keeping the current document as the new starting point.</summary>
    public void Clear()
    {
        _past.Clear();
        _future.Clear();
    }

    /// <summary>Steps back one edit.</summary>
    /// <returns>The document to restore, or null when there is nothing to undo.</returns>
    public string? Undo()
    {
        if (_past.Count == 0)
        {
            return null;
        }

        Entry step = _past[^1];
        _past.RemoveAt(_past.Count - 1);

        // The label travels with the state it belongs to: undoing "Move node" makes "Move node"
        // the thing redo would reapply.
        if (_present is { } current)
        {
            _future.Add(new Entry(step.Label, current));
        }

        _present = step.Snapshot;
        return step.Snapshot;
    }

    /// <summary>Reapplies the last undone edit.</summary>
    /// <returns>The document to restore, or null when there is nothing to redo.</returns>
    public string? Redo()
    {
        if (_future.Count == 0)
        {
            return null;
        }

        Entry step = _future[^1];
        _future.RemoveAt(_future.Count - 1);

        if (_present is { } current)
        {
            _past.Add(new Entry(step.Label, current));
        }

        _present = step.Snapshot;
        return step.Snapshot;
    }

    private readonly record struct Entry(string Label, string Snapshot);
}
