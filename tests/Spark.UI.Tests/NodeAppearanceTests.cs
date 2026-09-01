using System.Linq;
using Spark.Engine;
using Spark.UI.Graph;
using Spark.UI.Theming;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// A node's own name and header colour — `E8-T35`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client:</b> after twenty nodes a graph is easier to read when the six that
/// matter say what they are <i>for</i> rather than what they are. The definition's name is kept
/// beside the custom one rather than replaced by it, so <i>what is this really?</i> stays
/// answerable — it is the field's placeholder, and clearing the field puts it back.
/// </para>
/// <para>
/// <b>The colours are the ten category fills and nothing else.</b> Those are the ones whose
/// contrast against the header text is already measured, at rest, hovered and desaturated, so a
/// recoloured node is legible by construction; a picker would need the title to flip between light
/// and dark by luminance, which is a different feature.
/// </para>
/// </remarks>
public sealed class NodeAppearanceTests
{
    /// <summary>A node with no name of its own shows the one its definition gives it.</summary>
    [Fact]
    public void ANodeWithoutACustomNameShowsItsDefinitionName()
    {
        CanvasGraph graph = TestGraphs.Demo();
        CanvasNode node = graph.Nodes[0];

        Assert.Null(node.CustomTitle);
        Assert.Equal(node.Title, node.DisplayTitle);
    }

    /// <summary>
    /// <b>A node nobody has coloured is grey</b> (`E8-T38`), whatever library category it came
    /// from — asked for by the client, and it is what Dynamo does. Colour on this canvas means
    /// <i>somebody marked this</i>, not <i>this came from that folder</i>; the library panel still
    /// colours its rows by category, which is where that fact belongs.
    /// </summary>
    [Fact]
    public void ANodeNobodyHasColouredIsGrey()
    {
        CanvasGraph graph = TestGraphs.Demo();

        Assert.All(graph.Nodes, node => Assert.Equal(NodeCategory.Custom, node.DisplayCategory));

        // And the node still knows what it really is, for the library and for anything else that
        // asks.
        Assert.Contains(graph.Nodes, node => node.LibraryCategory != NodeCategory.Custom);
    }

    /// <summary>
    /// <b>Renaming re-measures the node.</b> The width comes from the title, so a longer name on a
    /// width measured from the old one is a name clipped by its own header — which reads as a bug.
    /// </summary>
    [Fact]
    public void RenamingANodeReMeasuresIt()
    {
        CanvasGraph graph = TestGraphs.Demo();
        CanvasNode node = graph.Nodes[0];

        double before = node.Width;

        node.CustomTitle = "a considerably longer name than the node started with";

        Assert.True(node.Width > before, $"the node should have grown: {before} → {node.Width}");
        Assert.Equal("a considerably longer name than the node started with", node.DisplayTitle);
    }

    /// <summary>Clearing the name puts the definition's own back, whitespace included.</summary>
    [Fact]
    public void ClearingTheNameRestoresTheDefinitionName()
    {
        CanvasGraph graph = TestGraphs.Demo();
        CanvasNode node = graph.Nodes[0];

        node.CustomTitle = "Profile";
        node.CustomTitle = "   ";

        Assert.Null(node.CustomTitle);
        Assert.Equal(node.Title, node.DisplayTitle);
    }

    /// <summary>A colour override changes which category's fill the header draws in.</summary>
    [Fact]
    public void AColourOverrideChangesTheDisplayedCategory()
    {
        CanvasGraph graph = TestGraphs.Demo();
        CanvasNode node = graph.Nodes[0];

        node.ColourOverride = NodeCategory.Script;

        Assert.Equal(NodeCategory.Script, node.DisplayCategory);
        Assert.NotEqual(node.Category, node.DisplayCategory);
    }

    /// <summary>
    /// <b>Both survive a save and an open</b>, which is the difference between a feature and a
    /// session's worth of fiddling.
    /// </summary>
    [Fact]
    public void ANameAndAColourSurviveTheFile()
    {
        CanvasGraph graph = TestGraphs.Demo();
        graph.Nodes[0].CustomTitle = "Profile";
        graph.Nodes[0].ColourOverride = NodeCategory.Math;

        CanvasGraph reopened = CanvasDocument.Open(CanvasDocument.Save(graph), TestGraphs.Library);
        CanvasNode restored = reopened.Nodes.Single(node => node.Id == graph.Nodes[0].Id);

        Assert.Equal("Profile", restored.CustomTitle);
        Assert.Equal(NodeCategory.Math, restored.ColourOverride);
    }

    /// <summary>
    /// <b>A graph nobody has restyled is written exactly as it was before this existed.</b> That is
    /// what keeps `E7-T7`'s byte-for-byte round trip an assertion about every file rather than one
    /// about files this build happened to write.
    /// </summary>
    [Fact]
    public void AGraphNobodyHasRestyledIsUnchanged()
    {
        CanvasGraph graph = TestGraphs.Demo();

        string plain = CanvasDocument.Save(graph);

        Assert.DoesNotContain("\"title\"", plain, System.StringComparison.Ordinal);
        Assert.DoesNotContain("\"colour\"", plain, System.StringComparison.Ordinal);
        Assert.True(SparkFile.Read(plain).FormatVersion < GraphDocument.AppearanceFormatVersion);
    }

    /// <summary>A restyled graph declares the version whose reader would not drop it.</summary>
    [Fact]
    public void ARestyledGraphDeclaresItsFormatVersion()
    {
        CanvasGraph graph = TestGraphs.Demo();
        graph.Nodes[0].CustomTitle = "Profile";

        GraphDocument document = SparkFile.Read(CanvasDocument.Save(graph));

        Assert.Equal(GraphDocument.AppearanceFormatVersion, document.FormatVersion);
    }

    /// <summary>
    /// A colour token this build does not know loses the colour and nothing else. A file from a
    /// later Spark should cost a user a setting, never their graph.
    /// </summary>
    [Fact]
    public void AnUnknownColourTokenLosesOnlyTheColour()
    {
        CanvasGraph graph = TestGraphs.Demo();
        graph.Nodes[0].CustomTitle = "Profile";

        string text = CanvasDocument.Save(graph).Replace(
            "\"title\": \"Profile\"",
            "\"title\": \"Profile\",\n      \"colour\": \"ultraviolet\"",
            System.StringComparison.Ordinal);

        CanvasGraph reopened = CanvasDocument.Open(text, TestGraphs.Library);
        CanvasNode restored = reopened.Nodes.Single(node => node.Id == graph.Nodes[0].Id);

        Assert.Equal("Profile", restored.CustomTitle);
        Assert.Null(restored.ColourOverride);
    }

    /// <summary>Selecting a node fills the pane with its name, its colour and its real name.</summary>
    [Fact]
    public void SelectingANodeShowsItsNameAndColour()
    {
        using MainWindowViewModel model = new();

        model.Graph.Nodes[0].CustomTitle = "Profile";
        model.Graph.Nodes[0].ColourOverride = NodeCategory.Curve;
        model.ShowSelection([0]);

        Assert.True(model.CanStyleNode);
        Assert.Equal("Profile", model.NodeTitle);
        Assert.Equal(nameof(NodeCategory.Curve), model.NodeColour);
        Assert.Equal(model.Graph.Nodes[0].Title, model.NodeTitlePlaceholder);
    }

    /// <summary>
    /// <b>Selecting a node does not rename it.</b> Pushing the node's own name into the pane looks
    /// exactly like a user typing it, and without the guard every click would be an undo step.
    /// </summary>
    [Fact]
    public void SelectingANodeIsNotAnEdit()
    {
        using MainWindowViewModel model = new();

        model.ShowSelection([0]);
        model.ShowSelection([1]);
        model.ShowSelection([0]);

        Assert.False(model.CanUndo);
    }

    /// <summary>Typing a name renames the node as it is typed, and commits once.</summary>
    [Fact]
    public void TypingANameRenamesLiveAndCommitsOnce()
    {
        using MainWindowViewModel model = new();

        model.ShowSelection([0]);

        model.NodeTitle = "Pro";
        model.NodeTitle = "Prof";
        model.NodeTitle = "Profile";

        // Live on the canvas, before anything is committed.
        Assert.Equal("Profile", model.Graph.Nodes[0].DisplayTitle);
        Assert.False(model.CanUndo);

        model.CommitNodeTitle();

        Assert.True(model.CanUndo);
        Assert.Equal("Undo Rename node", model.UndoDescription);

        // And committing again with nothing changed records nothing.
        model.CommitNodeTitle();

        model.Undo();

        Assert.Null(model.Graph.Nodes[0].CustomTitle);
    }

    /// <summary>Choosing a colour is one edit, and undo takes it back.</summary>
    [Fact]
    public void ChoosingAColourIsOneUndoableEdit()
    {
        using MainWindowViewModel model = new();

        model.ShowSelection([0]);
        model.NodeColour = nameof(NodeCategory.Math);

        Assert.Equal(NodeCategory.Math, model.Graph.Nodes[0].ColourOverride);
        Assert.Equal("Undo Colour node", model.UndoDescription);

        model.Undo();

        Assert.Null(model.Graph.Nodes[0].ColourOverride);
    }

    /// <summary>The dropdown offers Default first, then the ten category colours.</summary>
    [Fact]
    public void TheColourChoicesStartWithDefault()
    {
        Assert.Equal(NodeColourChoices.Default, MainWindowViewModel.NodeColourNames[0]);
        Assert.Equal(11, MainWindowViewModel.NodeColourNames.Count);
        Assert.Null(NodeColourChoices.Parse(NodeColourChoices.Default));
        Assert.Equal(NodeCategory.Solid, NodeColourChoices.Parse(nameof(NodeCategory.Solid)));
    }
}
