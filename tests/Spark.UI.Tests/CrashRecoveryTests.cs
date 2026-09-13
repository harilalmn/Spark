using System;
using System.IO;
using System.Linq;
using Spark.Host;
using Spark.UI.Graph;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// Autosave and crash recovery (`E8-T13`, FR-72).
/// </summary>
/// <remarks>
/// <para>
/// <b>R11 is why.</b> A code block is the user's own C#, and C# can end the process with a
/// <see cref="StackOverflowException"/> nothing can catch — so there is no shutdown to save in, and
/// the only defence is to have saved already. These tests are about that defence: a working copy that
/// is always as new as the last change, that goes away when there is nothing left to lose, and that a
/// later window offers back — and never restores behind the user's back.
/// </para>
/// <para>
/// <b>Every store here is a scratch folder</b>, and a crash is simulated the only way a test can: by a
/// liveness check that says the writing process is gone, because the process that wrote a copy in a
/// test is the test itself.
/// </para>
/// </remarks>
public sealed class CrashRecoveryTests : IDisposable
{
    private readonly string _root;
    private readonly string _folder;

    public CrashRecoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spark-recovery-tests", Guid.NewGuid().ToString("n"));
        _folder = Path.Combine(_root, "recovery");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Under the temp folder, and the next run makes its own.
        }
    }

    /// <summary>A store whose copies all look as though their process is still running.</summary>
    private RecoveryStore Live => RecoveryStore.At(_folder);

    /// <summary>The same folder, read as though every process that wrote into it had died.</summary>
    private RecoveryStore Crashed => RecoveryStore.At(_folder, isRunning: _ => false);

    /// <summary>
    /// <b>The row.</b> An edit leaves a working copy, and the copy is exactly the document on the
    /// canvas — not a summary of it, and not the state before the edit.
    /// </summary>
    [Fact]
    public void AnEditLeavesAWorkingCopyOfTheDocument()
    {
        using MainWindowViewModel model = new("demo");
        model.Recovery = Live;

        Assert.Empty(Copies());

        Move(model);

        Assert.Single(Copies());
        Assert.Equal(
            Assert.IsType<string>(model.TrySaveDocument()),
            Assert.Single(Crashed.Leftovers(Guid.Empty)).Text);
    }

    /// <summary>Saving leaves nothing to lose, so the copy goes.</summary>
    [Fact]
    public void SavingRemovesTheWorkingCopy()
    {
        using MainWindowViewModel model = new("demo");
        model.Recovery = Live;

        Move(model);
        Assert.Single(Copies());

        model.MarkSaved();

        Assert.Empty(Copies());
    }

    /// <summary>
    /// Undoing back to the saved state leaves nothing to lose either, and redoing away from it puts
    /// the copy back — the copy follows <see cref="MainWindowViewModel.IsModified"/>, not the edits.
    /// </summary>
    [Fact]
    public void TheCopyFollowsUndoAndRedo()
    {
        using MainWindowViewModel model = new("demo");
        model.Recovery = Live;

        Move(model);
        model.Undo();

        Assert.Empty(Copies());

        model.Redo();

        Assert.Single(Copies());
    }

    /// <summary>A window that ends cleanly takes its copy with it — by closing, or by being disposed.</summary>
    [Fact]
    public void ACleanEndRemovesTheCopy()
    {
        MainWindowViewModel model = new("demo");
        model.Recovery = Live;

        Move(model);
        model.EndRecovery();
        Assert.Empty(Copies());

        Move(model);
        Assert.Single(Copies());

        model.Dispose();
        Assert.Empty(Copies());
    }

    /// <summary>
    /// <b>The point of all of it.</b> A graph left by a session that died is offered by the next
    /// window, and restoring it brings back exactly that graph — as unsaved work, protected by the new
    /// session's own copy from the moment it is back.
    /// </summary>
    [Fact]
    public void AGraphLeftByACrashIsOfferedAndRestoredAsUnsavedWork()
    {
        string lost = LeaveACrashedCopy();

        using MainWindowViewModel next = new();
        next.Recovery = Crashed;

        Assert.Equal(1, next.OfferRecovery());
        Assert.Contains("an unsaved graph was left behind", next.RecoveryBanner, StringComparison.Ordinal);

        Assert.True(next.RestoreRecovery());

        Assert.Equal(lost, Assert.IsType<string>(next.TrySaveDocument()));
        Assert.True(next.IsModified, "a restored graph opened as though it were saved");
        Assert.Null(next.RecoveryBanner);

        // The leftover is gone and the new session's own copy has taken its place.
        Assert.Equal(lost, Assert.Single(Crashed.Leftovers(Guid.Empty)).Text);
    }

    /// <summary>
    /// <b>Another window's live work is not a leftover.</b> A second Spark starting while the first is
    /// open must not offer the first one's unsaved graph back as though it had been lost.
    /// </summary>
    [Fact]
    public void ARunningSessionsCopyIsNotOffered()
    {
        using MainWindowViewModel running = new("demo");
        running.Recovery = Live;
        Move(running);

        using MainWindowViewModel second = new();
        second.Recovery = Live;

        Assert.Equal(0, second.OfferRecovery());
        Assert.Null(second.RecoveryBanner);
        Assert.Single(Copies());
    }

    /// <summary>Turning it down deletes it, and nothing is offered after.</summary>
    [Fact]
    public void DiscardingDeletesTheCopyAndOffersNothing()
    {
        _ = LeaveACrashedCopy();

        using MainWindowViewModel next = new();
        next.Recovery = Crashed;

        Assert.Equal(1, next.OfferRecovery());

        next.DiscardRecovery();

        Assert.Null(next.RecoveryBanner);
        Assert.Empty(Copies());
    }

    /// <summary>
    /// A copy identical to the file it names has nothing to recover — the session died after a save —
    /// and is removed without a word.
    /// </summary>
    [Fact]
    public void ACopyIdenticalToItsFileIsNotOffered()
    {
        string path = Path.Combine(_root, "tower.spark");
        string saved = Saved();
        File.WriteAllText(path, saved);

        Crashed.Keep(Guid.NewGuid(), saved, path);

        using MainWindowViewModel next = new();
        next.Recovery = Crashed;

        Assert.Equal(0, next.OfferRecovery());
        Assert.Null(next.RecoveryBanner);
        Assert.Empty(Copies());
    }

    /// <summary>A copy that differs from its file is offered, and the banner names the file.</summary>
    /// <remarks>
    /// <b>The store is emptied between taking the text and keeping the copy, and that is not
    /// tidiness.</b> <see cref="LeaveACrashedCopy"/> is used here only for the <i>text</i> it
    /// returns — but it gets that text by crashing a session, which leaves a copy of its own with
    /// <b>no file</b> behind it. Without the clearing there are two leftovers, the banner describes
    /// whichever <c>Leftovers</c> happens to hand back first, and the two were written milliseconds
    /// apart — so this test failed about one run in ten and passed the other nine, which is the
    /// worst possible behaviour for a test in a gate (`E2-T72` found it; it predates that row).
    /// </remarks>
    [Fact]
    public void ACopyThatDiffersFromItsFileIsOfferedByName()
    {
        string path = Path.Combine(_root, "tower.spark");
        File.WriteAllText(path, Saved());

        string text = LeaveACrashedCopy();

        foreach (RecoveredGraph stray in Crashed.Leftovers(Guid.Empty))
        {
            Crashed.Discard(stray);
        }

        Crashed.Keep(Guid.NewGuid(), text, path);

        using MainWindowViewModel next = new();
        next.Recovery = Crashed;

        Assert.Equal(1, next.OfferRecovery());
        Assert.Contains("tower.spark", next.RecoveryBanner, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Off unless the window switches it on</b>, so that the thousand-odd tests that edit a view
    /// model never write a copy into the user's own folder.
    /// </summary>
    [Fact]
    public void RecoveryIsOffUntilSwitchedOn()
    {
        using MainWindowViewModel model = new("demo");

        Assert.Null(model.Recovery.Folder);
    }

    /// <summary>
    /// Leaves the copy a crashed session would: edited, and never ended — the store is taken away
    /// before the model is disposed, so disposing deletes nothing.
    /// </summary>
    /// <returns>The graph that was lost.</returns>
    private string LeaveACrashedCopy()
    {
        MainWindowViewModel crashed = new("demo");
        crashed.Recovery = Live;

        Move(crashed);
        string lost = Assert.IsType<string>(crashed.TrySaveDocument());

        crashed.Recovery = RecoveryStore.At(null);
        crashed.Dispose();

        return lost;
    }

    private static string Saved()
    {
        using MainWindowViewModel model = new("demo");

        return Assert.IsType<string>(model.TrySaveDocument());
    }

    private static void Move(MainWindowViewModel model)
    {
        CanvasNode node = model.Graph.Nodes[0];
        node.X += 120;
        model.RecordEdit("Move node");
    }

    private string[] Copies() =>
        Directory.Exists(_folder) ? Directory.GetFiles(_folder, "*.spark-recovery") : [];
}
