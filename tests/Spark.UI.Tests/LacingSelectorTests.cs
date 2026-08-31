using System;
using Spark.Api;
using Spark.Engine;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// Changing a node's lacing from the properties pane — `E8-T31`.
/// </summary>
/// <remarks>
/// <b>Lacing could not be changed anywhere at all before this.</b> <c>Graph.SetLacing</c> has
/// existed since `E4-T6`, the mode round-trips through the file, the cache keys on it and the
/// replicator obeys it — and nothing in the shell ever called it. The pane printed
/// <c>Lacing: Longest</c> as a sentence, which told a user about a setting and then offered no way
/// to reach it. Reported directly by the client.
/// </remarks>
public sealed class LacingSelectorTests
{
    /// <summary>Selecting one node offers its own lacing; selecting several offers none.</summary>
    /// <remarks>
    /// Lacing is a property of a node, and a selector over three nodes would have to answer what it
    /// shows when they disagree. Hidden is the honest answer.
    /// </remarks>
    [Fact]
    public void ASingleSelectionOffersLacingAndAMultipleOneDoesNot()
    {
        using MainWindowViewModel model = new();

        model.ShowSelection([SlotOf(model, "Math.Divide")]);
        Assert.True(model.CanSetLacing);
        Assert.Equal(LacingNames.Auto, model.SelectedLacing);

        model.ShowSelection([0, 1]);
        Assert.False(model.CanSetLacing);

        model.ShowSelection([]);
        Assert.False(model.CanSetLacing);
    }

    /// <summary>
    /// The note under the selector says what <c>Auto</c> resolves to, and only for <c>Auto</c>.
    /// </summary>
    /// <remarks>
    /// Auto is the one mode whose behaviour is not in its own name — two nodes both on Auto can
    /// lace differently, because what they share is "not overridden" rather than a behaviour. Every
    /// other mode says what it does, so a line repeating it would be noise.
    /// </remarks>
    [Fact]
    public void TheNoteExplainsAutoAndNothingElse()
    {
        using MainWindowViewModel model = new();
        int slot = SlotOf(model, "Math.Divide");

        model.ShowSelection([slot]);
        Assert.Contains("Longest", model.LacingNote, StringComparison.Ordinal);

        model.SelectedLacing = "Cross product";
        Assert.Equal(string.Empty, model.LacingNote);
    }

    /// <summary>Choosing a mode reaches the engine graph.</summary>
    [Fact]
    public void ChoosingAModeSetsItOnTheNode()
    {
        using MainWindowViewModel model = new();
        int slot = SlotOf(model, "Math.Divide");
        NodeId id = model.Graph.Nodes[slot].Id;

        model.ShowSelection([slot]);
        model.SelectedLacing = "Cross product";

        Assert.Equal(LacingMode.CrossProduct, model.Graph.Engine.Node(id).Lacing);
    }

    /// <summary>It is one undo step, and undoing it puts the mode back.</summary>
    /// <remarks>
    /// The mode is in the document, so the snapshot stack carries it for free — but "for free" is
    /// exactly the kind of claim that turns out to be false, and a lacing change that could not be
    /// undone would be the one edit in the shell that could not.
    /// </remarks>
    [Fact]
    public void ChangingLacingIsOneUndoStep()
    {
        using MainWindowViewModel model = new();

        model.ShowSelection([SlotOf(model, "Math.Divide")]);
        model.SelectedLacing = "Shortest";

        Assert.True(model.CanUndo);
        Assert.Equal("Undo Set lacing", model.UndoDescription);

        model.Undo();

        // The slot renumbers across an undo - the document is reopened (N23) - so the node is found
        // by name again rather than by the index it used to have.
        NodeId id = model.Graph.Nodes[SlotOf(model, "Math.Divide")].Id;
        Assert.Equal(LacingMode.Auto, model.Graph.Engine.Node(id).Lacing);
    }

    /// <summary>
    /// <b>Selecting a node does not record an edit.</b>
    /// </summary>
    /// <remarks>
    /// The pane pushes the selected node's lacing into the bound property, and to the change
    /// handler that assignment is indistinguishable from a user picking that value in the dropdown.
    /// Without the guard, every single click on a node would put "Set lacing" on the undo stack and
    /// start a run.
    /// </remarks>
    [Fact]
    public void SelectingANodeIsNotAnEdit()
    {
        using MainWindowViewModel model = new();

        model.ShowSelection([SlotOf(model, "Math.Divide")]);
        model.ShowSelection([SlotOf(model, "Point.ByCoordinates")]);
        model.ShowSelection([SlotOf(model, "Math.Divide")]);

        Assert.False(model.CanUndo);
    }

    /// <summary>Choosing the mode the node already has is not an edit either.</summary>
    [Fact]
    public void ChoosingTheModeItAlreadyHasIsNotAnEdit()
    {
        using MainWindowViewModel model = new();

        model.ShowSelection([SlotOf(model, "Math.Divide")]);
        model.SelectedLacing = LacingNames.Auto;

        Assert.False(model.CanUndo);
    }

    /// <summary>Every mode's word round-trips, so the dropdown and the graph cannot disagree.</summary>
    [Fact]
    public void EveryModeRoundTripsThroughItsWord()
    {
        foreach (LacingMode mode in Enum.GetValues<LacingMode>())
        {
            Assert.True(LacingNames.TryParse(LacingNames.Of(mode), out LacingMode parsed));
            Assert.Equal(mode, parsed);
        }

        Assert.Equal(Enum.GetValues<LacingMode>().Length, LacingNames.All.Count);
        Assert.False(LacingNames.TryParse("CrossProduct", out _));
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

        throw new InvalidOperationException("No node titled " + title + " in the demo graph.");
    }
}
