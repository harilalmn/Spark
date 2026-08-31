using System;
using System.Linq;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;
using Spark.UI.Graph;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// The type dropdown on a code block's input port, as the properties panel offers it (`E6-T11`).
/// </summary>
/// <remarks>
/// <para>
/// <b>These go through the panel's own view models rather than through the graph.</b>
/// <see cref="DeclaredInputTypeTests"/> proves the engine honours a declaration; what is left to
/// get wrong is everything between a user opening a dropdown and that call being made — whether
/// the row offers one at all, whether it offers one where it must not, and whether choosing an
/// entry does anything.
/// </para>
/// <para>
/// <b>That gap is not hypothetical.</b> Three defects in this session were wired correctly and
/// covered by tests that never touched the surface a person touches, and all three were found by
/// somebody opening the application (<c>N88</c>, <c>N89</c>).
/// </para>
/// </remarks>
public sealed class InputTypeDropdownTests
{
    /// <summary>A code block's input port offers the dropdown.</summary>
    [Fact]
    public void ACodeBlockPortOffersTheDropdown()
    {
        (MainWindowViewModel model, _) = BlockWithOneInput();

        PortLiteralViewModel row = Assert.Single(model.Inspector);

        Assert.Equal("radius", row.Name);
        Assert.True(row.CanDeclareType, "a code block's port offered no type dropdown");
        Assert.Equal(PortLiteralViewModel.NotDeclared, row.DeclaredTypeName);
    }

    /// <summary>
    /// <b>An ordinary node's port does not.</b> Its type comes from the method it was imported
    /// from and is not the user's to change; a dropdown there would be a control that either does
    /// nothing or breaks the node.
    /// </summary>
    [Fact]
    public void AnOrdinaryNodePortDoesNotOfferTheDropdown()
    {
        MainWindowViewModel model = new();

        int slot = model.Graph.Add(TestGraphs.Library.ByName("Point.ByCoordinates"), 0, 0);
        model.ShowSelection([slot]);

        Assert.NotEmpty(model.Inspector);
        Assert.All(model.Inspector, row => Assert.False(row.CanDeclareType));
    }

    /// <summary>
    /// <b>Choosing an entry declares the type, and the port changes.</b> This is the whole
    /// feature: everything above it is only the offer.
    /// </summary>
    [Fact]
    public void ChoosingATypeDeclaresIt()
    {
        (MainWindowViewModel model, NodeId block) = BlockWithOneInput();

        Assert.Equal(typeof(object), PortType(model, block));

        model.Inspector[0].DeclaredTypeName = NameOf(typeof(Point3d));

        Assert.Equal(typeof(Point3d), PortType(model, block));
    }

    /// <summary>
    /// And the panel comes back showing what was chosen, rather than snapping to
    /// <see cref="PortLiteralViewModel.NotDeclared"/> because the node was rebuilt underneath it.
    /// </summary>
    [Fact]
    public void ThePanelKeepsShowingTheChosenType()
    {
        (MainWindowViewModel model, _) = BlockWithOneInput();

        model.Inspector[0].DeclaredTypeName = NameOf(typeof(Point3d));

        PortLiteralViewModel row = Assert.Single(model.Inspector);

        Assert.Equal(NameOf(typeof(Point3d)), row.DeclaredTypeName);
        Assert.True(row.CanDeclareType);
    }

    /// <summary>Choosing "from the wire" again takes the declaration off.</summary>
    [Fact]
    public void ChoosingFromTheWireClearsTheDeclaration()
    {
        (MainWindowViewModel model, NodeId block) = BlockWithOneInput();

        model.Inspector[0].DeclaredTypeName = NameOf(typeof(Point3d));
        Assert.Equal(typeof(Point3d), PortType(model, block));

        model.Inspector[0].DeclaredTypeName = PortLiteralViewModel.NotDeclared;

        Assert.Equal(typeof(object), PortType(model, block));
    }

    /// <summary>
    /// <b>Every entry in the dropdown is a distinct word</b>, and the first is the way back.
    /// Two entries reading the same thing would be a dropdown where one of the choices cannot be
    /// made.
    /// </summary>
    [Fact]
    public void TheChoicesAreDistinctAndLedByTheWayBack()
    {
        Assert.Equal(PortLiteralViewModel.NotDeclared, PortLiteralViewModel.TypeChoices[0]);
        Assert.Equal(
            PortLiteralViewModel.TypeChoices.Count,
            PortLiteralViewModel.TypeChoices.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Declaring a type is undoable, like every other edit to a graph.</summary>
    [Fact]
    public void DeclaringATypeCanBeUndone()
    {
        (MainWindowViewModel model, NodeId block) = BlockWithOneInput();

        model.Inspector[0].DeclaredTypeName = NameOf(typeof(Point3d));
        Assert.Equal(typeof(Point3d), PortType(model, block));

        model.UndoCommand.Execute(parameter: null);

        Assert.Equal(typeof(object), PortType(model, block));
    }

    /// <summary>
    /// <b>A null written back through the dropdown's binding is not a choice.</b>
    /// </summary>
    /// <remarks>
    /// "The user declared nothing" is spelled <see cref="PortLiteralViewModel.NotDeclared"/>, which
    /// is an entry in the list. A <c>ComboBox</c> writes null back through a two-way
    /// <c>SelectedItem</c> binding whenever it cannot find the bound value among its items, which
    /// happens transiently while the control is being realised — so acting on a null would clear
    /// the declaration every time the panel was rebuilt, which is every time the selection changes.
    /// </remarks>
    [Fact]
    public void ANullFromTheBindingDoesNotClearTheDeclaration()
    {
        (MainWindowViewModel model, NodeId block) = BlockWithOneInput();

        model.Inspector[0].DeclaredTypeName = NameOf(typeof(Point3d));
        Assert.Equal(typeof(Point3d), PortType(model, block));

        model.Inspector[0].DeclaredTypeName = null;

        Assert.Equal(typeof(Point3d), PortType(model, block));
    }

    private static string NameOf(Type type) => PortTypeName.Describe(type);

    private static Type PortType(MainWindowViewModel model, NodeId block) =>
        model.Graph.Engine.Node(block).Definition.Inputs[0].ValueType;

    /// <summary>A selected code block whose source gives it exactly one input port.</summary>
    private static (MainWindowViewModel Model, NodeId Block) BlockWithOneInput()
    {
        MainWindowViewModel model = new();

        int slot = model.PlaceCodeBlock(0, 0);
        CanvasNode node = model.Graph.Nodes[slot];

        model.ShowCodeBlock(node);
        model.ScriptText = "return radius;";
        Assert.True(model.CommitScriptText(), "the script was not committed");

        model.ShowSelection([model.Graph.SlotOf(node.Id)]);

        return (model, node.Id);
    }
}
