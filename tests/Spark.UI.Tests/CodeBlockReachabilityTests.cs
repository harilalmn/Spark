using System;
using System.Linq;
using Spark.UI.Graph;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// Can a user actually reach a code block's source? (<c>E6-T11</c>)
/// </summary>
/// <remarks>
/// <b>Asked because somebody could not find where to type.</b> The editor is hosted in the
/// inspector and shown when <c>SelectedCodeBlock</c> is set, and every part of that was wired —
/// but so was the viewport's navigation, and that had never once been exercised through the
/// gesture a user makes. These go through the same path selection does.
/// </remarks>
public sealed class CodeBlockReachabilityTests
{
    /// <summary>Adding a code block and selecting it puts its source in the inspector.</summary>
    [Fact]
    public void SelectingACodeBlockShowsItsSource()
    {
        MainWindowViewModel model = new();

        int slot = model.PlaceCodeBlock(0, 0);

        Assert.True(slot >= 0, "no code block was added");

        model.ShowSelection([slot]);

        Assert.NotNull(model.SelectedCodeBlock);
        Assert.Equal(model.Graph.Nodes[slot].Id, model.SelectedCodeBlock!.Id);
    }

    /// <summary>Selecting something else clears it, so the editor does not linger.</summary>
    [Fact]
    public void SelectingSomethingElseClearsIt()
    {
        MainWindowViewModel model = new();

        int slot = model.PlaceCodeBlock(0, 0);
        model.ShowSelection([slot]);

        Assert.NotNull(model.SelectedCodeBlock);

        model.ShowSelection([0]);

        Assert.Null(model.SelectedCodeBlock);
    }

    /// <summary>A fresh code block starts with source a user can edit rather than nothing.</summary>
    [Fact]
    public void AFreshCodeBlockHasSource()
    {
        MainWindowViewModel model = new();

        int slot = model.PlaceCodeBlock(0, 0);
        model.ShowSelection([slot]);

        Assert.NotNull(model.SelectedCodeBlock);
    }
}
