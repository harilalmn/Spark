using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Engine;
using Spark.UI.Graph;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// Freezing from the canvas, and what a group does about it (<c>E7-T14</c>).
/// </summary>
public sealed class FreezeGestureTests
{
    /// <summary>Freezing one node freezes exactly that node.</summary>
    [Fact]
    public void FreezingOneNodeFreezesThatNode()
    {
        MainWindowViewModel model = new();

        Assert.Equal(1, model.FreezeSelection([0], frozen: true));
        Assert.True(IsFrozen(model, 0));
        Assert.False(IsFrozen(model, 1));
    }

    /// <summary>Nothing selected freezes nothing, rather than freezing everything.</summary>
    [Fact]
    public void AnEmptySelectionFreezesNothing()
    {
        MainWindowViewModel model = new();

        Assert.Equal(0, model.FreezeSelection([], frozen: true));
        Assert.False(model.SelectionIsFrozen([]));
    }

    /// <summary>Unfreezing a node that is not frozen changes nothing.</summary>
    [Fact]
    public void UnfreezingSomethingUnfrozenChangesNothing()
    {
        MainWindowViewModel model = new();

        Assert.Equal(0, model.FreezeSelection([0], frozen: false));
    }

    /// <summary>
    /// <b>A selection is reported frozen only when all of it is.</b> A mixed selection therefore
    /// offers to freeze, so pressing the button twice always ends with everything frozen and then
    /// everything thawed.
    /// </summary>
    [Fact]
    public void ASelectionIsFrozenOnlyWhenAllOfItIs()
    {
        MainWindowViewModel model = new();

        model.FreezeSelection([0], frozen: true);

        Assert.True(model.SelectionIsFrozen([0]));
        Assert.False(model.SelectionIsFrozen([0, 1]));

        model.FreezeSelection([1], frozen: true);

        Assert.True(model.SelectionIsFrozen([0, 1]));
    }

    /// <summary>
    /// <b>Selecting one node of a group freezes the group.</b> A group is the user's own statement
    /// that these nodes are one thing; leaving half of it running would produce a branch that is
    /// neither on nor off.
    /// </summary>
    [Fact]
    public void FreezingOneNodeOfAGroupFreezesTheGroup()
    {
        MainWindowViewModel model = new();

        Assert.NotNull(model.Graph.AddGroup([0, 1, 2]));

        int changed = model.FreezeSelection([0], frozen: true);

        Assert.Equal(3, changed);
        Assert.True(IsFrozen(model, 0));
        Assert.True(IsFrozen(model, 1));
        Assert.True(IsFrozen(model, 2));
    }

    /// <summary>And unfreezing one of them thaws the whole group again.</summary>
    [Fact]
    public void UnfreezingOneNodeOfAGroupThawsTheGroup()
    {
        MainWindowViewModel model = new();

        Assert.NotNull(model.Graph.AddGroup([0, 1, 2]));
        model.FreezeSelection([0], frozen: true);

        Assert.Equal(3, model.FreezeSelection([2], frozen: false));
        Assert.False(IsFrozen(model, 0));
        Assert.False(IsFrozen(model, 1));
        Assert.False(IsFrozen(model, 2));
    }

    /// <summary>A slot that is not a node is ignored rather than throwing.</summary>
    [Fact]
    public void AnOutOfRangeSlotIsIgnored()
    {
        MainWindowViewModel model = new();

        Assert.Equal(0, model.FreezeSelection([-1, 9999], frozen: true));
        Assert.False(model.SelectionIsFrozen([-1, 9999]));
    }

    /// <summary>
    /// <b>The freeze survives a save and a load through the canvas document</b>, which is the path
    /// a user's file actually takes.
    /// </summary>
    [Fact]
    public void TheFreezeSurvivesTheDocumentRoundTrip()
    {
        MainWindowViewModel model = new();

        model.FreezeSelection([0], frozen: true);

        string text = model.TrySaveDocument()
            ?? throw new InvalidOperationException("the graph did not save");

        MainWindowViewModel reopened = new();

        Assert.True(reopened.TryOpenDocument(text));

        // By count rather than by slot: slots are the canvas's own numbering and a reopened
        // document need not lay them out in the order the first one did.
        Assert.Equal(1, FrozenCount(reopened));
    }

    private static int FrozenCount(MainWindowViewModel model) =>
        Enumerable.Range(0, model.Graph.Nodes.Count).Count(slot => IsFrozen(model, slot));

    private static bool IsFrozen(MainWindowViewModel model, int slot) =>
        model.Graph.Engine.TryGetNode(model.Graph.Nodes[slot].Id, out NodeInstance? node)
        && node is not null
        && node.IsFrozen;
}
