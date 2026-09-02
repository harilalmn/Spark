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

    /// <summary>
    /// <b>And it starts with no input ports at all.</b>
    /// </summary>
    /// <remarks>
    /// The starter used to be <c>return a;</c>, which compiles to a block with one input called
    /// <c>a</c> that the user never asked for and has no obvious way to remove. Asked for directly:
    /// "let the default be zero inputs". The starter is now a comment, and a comment has no free
    /// identifiers.
    /// </remarks>
    [Fact]
    public void AFreshCodeBlockHasNoInputs()
    {
        MainWindowViewModel model = new();

        int slot = model.PlaceCodeBlock(0, 0);

        Assert.Empty(model.Graph.Nodes[slot].Inputs);
    }

    /// <summary>
    /// <b>Typing an undeclared name into it is what adds an input</b>, which is the rule the
    /// starter comment states and the only way a user gets a port.
    /// </summary>
    [Fact]
    public void TypingAnUndeclaredNameAddsAnInput()
    {
        MainWindowViewModel model = new();

        int slot = model.PlaceCodeBlock(0, 0);

        Assert.Empty(model.Graph.Nodes[slot].Inputs);

        Spark.UI.Graph.CanvasNode node = model.Graph.Nodes[slot];

        model.ShowCodeBlock(node);
        model.ScriptText = "return radius * 2;";

        Assert.True(model.CommitScriptText(), "the edit was not committed");

        // Rebuilding replaces the node, so its slot moves. Its identity does not.
        int rebuilt = model.Graph.SlotOf(node.Id);

        Assert.Equal("radius", Assert.Single(model.Graph.Nodes[rebuilt].Inputs).Name);
    }

    /// <summary>
    /// <b>`E6-T26` on the canvas, not just in the factory.</b> Two lines, two output ports on
    /// the node the user is looking at — which is the path through <c>ReplaceDefinition</c>, so it
    /// is also the proof that a block growing ports keeps its identity and its place.
    /// </summary>
    [Fact]
    public void EachLineOfACommittedScriptBecomesAnOutputPortOnTheNode()
    {
        MainWindowViewModel model = new();

        int slot = model.PlaceCodeBlock(0, 0);
        Spark.UI.Graph.CanvasNode node = model.Graph.Nodes[slot];

        model.ShowCodeBlock(node);
        model.ScriptText = "var doubled = radius * 2;\nvar tripled = radius * 3;\n";

        Assert.True(model.CommitScriptText(), "the edit was not committed");

        int rebuilt = model.Graph.SlotOf(node.Id);

        Assert.Equal("radius", Assert.Single(model.Graph.Nodes[rebuilt].Inputs).Name);
        Assert.Equal(
            ["doubled", "tripled"],
            model.Graph.Nodes[rebuilt].Outputs.Select(port => port.Name));
    }
}
