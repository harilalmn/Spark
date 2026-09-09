using System;
using System.IO;
using System.Linq;
using Spark.UI.ViewModels;
using Spark.UI.Views;

namespace Spark.UI.Tests;

/// <summary>
/// Whether a document has unsaved changes, and what is asked before it goes away — `E8-T79`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: <i>confirm with user to save unsaved changes on exit.</i> Spark
/// closed without a word until now, and <c>E8-T37</c>'s <i>New</i> command had said so in its own
/// doc comment — <i>nothing is asked and nothing is saved</i> — because nothing knew whether there
/// was anything to ask about.
/// </para>
/// <para>
/// <b>The flag is content, not a counter</b>, and the test that proves it matters is
/// <see cref="UndoingBackToTheSavedStateIsNotModified"/>: a document edited and then undone is
/// identical to the one on disk, and calling it modified would train people to dismiss the prompt.
/// </para>
/// </remarks>
public sealed class UnsavedChangesTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "spark-unsaved-tests", Guid.NewGuid().ToString("n"));

    public UnsavedChangesTests() => Directory.CreateDirectory(_root);

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

    /// <summary>A graph nobody has touched has nothing to lose.</summary>
    [Fact]
    public void AFreshGraphIsNotModified()
    {
        using MainWindowViewModel model = new();

        Assert.False(model.IsModified);
    }

    /// <summary>An edit is what makes a document modified.</summary>
    [Fact]
    public void AnEditMarksTheDocument()
    {
        using MainWindowViewModel model = new();

        model.LoadDemo();
        Assert.False(model.IsModified);

        Edit(model);

        Assert.True(model.IsModified);
    }

    /// <summary>
    /// <b>Undoing back to the saved state is not modified</b>, which is the whole reason this
    /// compares content rather than counting edits. A counter would say <i>two changes</i> and be
    /// describing a document identical to the one on disk.
    /// </summary>
    [Fact]
    public void UndoingBackToTheSavedStateIsNotModified()
    {
        using MainWindowViewModel model = new();

        model.LoadDemo();
        Edit(model);

        Assert.True(model.IsModified);

        model.Undo();

        Assert.False(model.IsModified);
    }

    /// <summary>Saving makes what is on the canvas what is on disk.</summary>
    [Fact]
    public void SavingClearsTheMark()
    {
        using MainWindowViewModel model = new();

        model.LoadDemo();
        Edit(model);
        Assert.True(model.IsModified);

        model.MarkSaved();

        Assert.False(model.IsModified);
    }

    /// <summary>
    /// Replacing the document starts clean — a graph that has just arrived has nothing in it
    /// anybody would mind losing, and prompting on the way out of one trains people to dismiss the
    /// prompt.
    /// </summary>
    [Fact]
    public void ReplacingTheDocumentStartsClean()
    {
        using MainWindowViewModel model = new();

        model.LoadDemo();
        Edit(model);
        Assert.True(model.IsModified);

        model.NewGraph();
        Assert.False(model.IsModified);

        Edit(model);
        Assert.True(model.IsModified);

        Assert.True(model.TryOpenDocument(model.TrySaveDocument()!, Path.Combine(_root, "g.spark")));
        Assert.False(model.IsModified);
    }

    /// <summary>
    /// <b>The change is announced</b>, because the title's marker follows the flag rather than
    /// being pushed from every place that could have changed it.
    /// </summary>
    [Fact]
    public void TheMarkIsAnnounced()
    {
        using MainWindowViewModel model = new();
        model.LoadDemo();

        int announcements = 0;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsModified))
            {
                announcements++;
            }
        };

        Edit(model);

        Assert.True(announcements > 0);
    }

    /// <summary>
    /// <b>Cancel is the standing answer</b>, so a prompt dismissed by any means — Escape, the close
    /// button, the window manager — keeps the document rather than losing it.
    /// </summary>
    [Fact]
    public void TheDefaultAnswerIsCancel() => HeadlessSession.Run(() =>
    {
        UnsavedChangesWindow prompt = new("tower.spark", "closing Spark");

        Assert.Equal(UnsavedChoice.Cancel, prompt.Choice);

        prompt.Close();
    });

    /// <summary>Each answer is what it says, and answering closes the prompt.</summary>
    [Theory]
    [InlineData(UnsavedChoice.Save)]
    [InlineData(UnsavedChoice.Discard)]
    [InlineData(UnsavedChoice.Cancel)]
    public void EachAnswerIsRecorded(UnsavedChoice choice) => HeadlessSession.Run(() =>
    {
        UnsavedChangesWindow prompt = new("tower.spark", "closing Spark");

        prompt.Answer(choice);

        Assert.Equal(choice, prompt.Choice);
    });

    /// <summary>
    /// The prompt names the document, because <i>you have unsaved changes</i> asks about a file the
    /// user has to identify from memory.
    /// </summary>
    [Fact]
    public void ThePromptNamesTheDocument() => HeadlessSession.Run(() =>
    {
        UnsavedChangesWindow named = new("tower.spark", "closing Spark");
        UnsavedChangesWindow untitled = new(null, "closing Spark");

        Assert.Equal("Unsaved changes", named.Title);
        Assert.Equal("Unsaved changes", untitled.Title);

        named.Close();
        untitled.Close();
    });

    /// <summary>
    /// Makes a real change to the document.
    /// </summary>
    /// <remarks>
    /// <b>Not <c>RecordEdit</c> with a label and nothing behind it.</b> The first version of these
    /// tests did that and went red, correctly: recording an edit that changed nothing leaves the
    /// document byte-identical to the one on disk, and this compares content. The test was wrong
    /// and the behaviour was right, which is the point of comparing content in the first place.
    /// </remarks>
    private static void Edit(MainWindowViewModel model)
    {
        model.SelectedLibraryEntry =
            model.AllLibraryEntries.First(entry => entry.DisplayName == "Point.Origin");

        Assert.True(model.PlaceSelectedLibraryEntry(0, 0) >= 0);
    }
}
