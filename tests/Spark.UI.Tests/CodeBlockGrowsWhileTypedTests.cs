using System;
using Avalonia.Controls;
using Spark.Engine;
using Spark.UI.Graph;
using Spark.UI.ViewModels;
using Spark.UI.Views.Panes;

namespace Spark.UI.Tests;

/// <summary>
/// A code block grows around its editor while somebody types into it — `E8-T75`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reported by the client with a screenshot</b>: the block being typed into stayed one line
/// tall while the block beside it was seven. `E8-T40` already built the reservation that grows a
/// node around its editor — <c>ReserveScriptSpace</c>, reached through
/// <c>GraphCanvas.ScriptEditorSpace</c> — and the pane asked for it exactly once, when the editor
/// opened, from the text as it stood at that moment. <c>Place</c> was re-called on every pan and
/// zoom and always with that same stale measurement.
/// </para>
/// <para>
/// <b>These drive the pane rather than the pieces</b>, because the defect was in neither piece: the
/// editor raised its changes and the canvas honoured its reservations, and nothing joined them up.
/// A test of either half alone passes with the defect in place.
/// </para>
/// </remarks>
public sealed class CodeBlockGrowsWhileTypedTests
{
    /// <summary>
    /// <b>Typing a newline makes the block taller.</b> The assertion that was false before this
    /// row, and the one the client would make by looking at it.
    /// </summary>
    [Fact]
    public void TypingANewLineGrowsTheBlock() => HeadlessSession.Run(() =>
    {
        (Window window, CanvasPane pane, CanvasGraph graph, int slot) = Editing("var a = 1;");

        double before = graph.Nodes[slot].Height;

        pane.TypeIntoScriptEditor("\nvar b = 2;\nvar c = 3;");

        Assert.True(
            graph.Nodes[slot].Height > before,
            $"The block did not grow: {before} then {graph.Nodes[slot].Height}.");

        window.Close();
    });

    /// <summary>
    /// <b>A longer line makes the block wider</b>, for the same reason and by the same path — the
    /// reservation carries a width as well as a height.
    /// </summary>
    [Fact]
    public void TypingALongLineWidensTheBlock() => HeadlessSession.Run(() =>
    {
        (Window window, CanvasPane pane, CanvasGraph graph, int slot) = Editing("var a = 1;");

        double before = graph.Nodes[slot].Width;

        pane.TypeIntoScriptEditor(new string('x', 120));

        Assert.True(
            graph.Nodes[slot].Width > before,
            $"The block did not widen: {before} then {graph.Nodes[slot].Width}.");

        window.Close();
    });

    /// <summary>
    /// <b>A keystroke that changes neither dimension moves nothing</b>, and this is the assertion
    /// that fails if the pane is wired straight to every change. Re-placing goes through
    /// <c>ScriptEditorSpace</c>, which re-measures the node and rebuilds the canvas's spatial
    /// index; doing that for a character typed inside a line that was not the longest is pure
    /// waste on the hottest path there is.
    /// </summary>
    [Fact]
    public void TypingInsideAShortLineDoesNotMoveTheBlock() => HeadlessSession.Run(() =>
    {
        (Window window, CanvasPane pane, CanvasGraph graph, int slot) = Editing(
            "var aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa = 1;\nvar b = 2;");

        // The caret opens after the last character, which is on the *short* second line - so this
        // lengthens a line that is nowhere near the longest one.
        double width = graph.Nodes[slot].Width;
        double height = graph.Nodes[slot].Height;

        pane.TypeIntoScriptEditor("3");

        Assert.Equal(width, graph.Nodes[slot].Width);
        Assert.Equal(height, graph.Nodes[slot].Height);

        window.Close();
    });

    /// <summary>
    /// <b>The editor opens every time, not only the first</b> (`E8-T77`). Reported by the client as
    /// <i>cannot type anything in CodeBlock</i>, and it was a regression from `E8-T75`: the open
    /// path was made to call the same helper the keystroke path uses, and that helper answers
    /// <i>did the size move</i>. For the second open of a block the size has not moved — most
    /// blocks are one line, so most opens want exactly what the last one wanted — so the guard said
    /// no, the editor was never placed, never shown and never focused, and every keystroke went to
    /// the canvas.
    /// </summary>
    [Fact]
    public void ClosingAndReopeningAnEditorOpensItAgain() => HeadlessSession.Run(() =>
    {
        (Window window, CanvasPane pane, CanvasGraph graph, int slot) = Editing("var a = 1;");

        Assert.True(pane.ScriptEditor.IsVisible);

        pane.CanvasControl.EndScriptEdit(slot);
        pane.ScriptEditor.IsVisible = false;

        // Exactly the same block, so exactly the same wanted size - which is the case that failed.
        pane.CanvasControl.RequestScriptEdit(slot);

        Assert.True(pane.ScriptEditor.IsVisible, "The editor did not reopen on a block of unchanged size.");

        pane.TypeIntoScriptEditor("2");

        Assert.Contains("2", pane.ScriptEditor.Text, StringComparison.Ordinal);
        _ = graph;

        window.Close();
    });

    /// <summary>
    /// <b>A second block of the same shape opens too</b>, which is the same defect reached the way
    /// a user would actually reach it: place two one-line blocks and edit them in turn.
    /// </summary>
    [Fact]
    public void ASecondBlockOfTheSameShapeAlsoOpens() => HeadlessSession.Run(() =>
    {
        (Window window, CanvasPane pane, CanvasGraph graph, int first) = Editing("var a = 1;");

        int second = graph.Add(
            NodeDefinition.FromScript(graph.Scripts!.Create("var b = 2;"), "var b = 2;"), 300, 0);

        pane.CanvasControl.EndScriptEdit(first);
        pane.ScriptEditor.IsVisible = false;

        pane.CanvasControl.RequestScriptEdit(second);

        Assert.True(pane.ScriptEditor.IsVisible, "The second block's editor did not open.");

        window.Close();
    });

    /// <summary>A pane showing one code block, with its in-node editor open on it.</summary>
    private static (Window Window, CanvasPane Pane, CanvasGraph Graph, int Slot) Editing(string source)
    {
        MainWindowViewModel model = new();
        CanvasPane pane = new() { DataContext = model };
        Window window = new() { Width = 1000, Height = 800, Content = pane };

        window.Show();

        int slot = model.PlaceCodeBlock(0, 0);

        Assert.True(slot >= 0, "Scripting is off, so there is no code block to type into.");

        pane.CanvasControl.Graph = model.Graph;

        // COMMITTED ONTO THE NODE BEFORE THE EDITOR IS OPENED, WHICH IS THE ONLY ORDER THAT WORKS.
        // Opening the editor sets its text from the *node*, so a test that wrote the source into
        // the editor first would have it overwritten by the starter comment a moment later - and
        // would then be measuring the starter while claiming to measure the source.
        model.ShowCodeBlock(model.Graph.Nodes[slot]);
        model.ScriptText = source;

        Assert.True(model.CommitScriptText(), "The source did not reach the node.");

        slot = model.Graph.SlotOf(model.Graph.Nodes[slot].Id) is var moved && moved >= 0
            ? moved
            : slot;

        pane.CanvasControl.RequestScriptEdit(slot);

        Assert.True(pane.ScriptEditor.IsVisible, "The in-node editor did not open.");

        return (window, pane, model.Graph, slot);
    }
}
