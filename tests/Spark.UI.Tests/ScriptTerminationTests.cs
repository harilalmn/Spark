using System.Linq;
using Spark.Scripting;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// `E6-T33` — the semicolon a code block's last statement is missing is put back on commit.
/// </summary>
/// <remarks>
/// <para>
/// <b>What makes this worth a rule rather than a squiggle.</b> A block with an unterminated last
/// line does not parse, and a block that does not parse declares no variables — so `E6-T26`'s
/// one-output-port-per-variable rule yields nothing and the node loses its output ports, taking
/// its wires with it. The character is worth a page of tests because of what it costs.
/// </para>
/// <para>
/// <b>The refusals are the half worth reading.</b> Most of what is asserted below is that the rule
/// does <i>not</i> fire: on a source that is already terminated, on one whose last statement has
/// no semicolon in the grammar at all, and on one that is broken in some other way. A tidy-up that
/// edits code it did not understand is worse than no tidy-up.
/// </para>
/// </remarks>
public sealed class ScriptTerminationTests
{
    /// <summary>The plain case: one expression, no semicolon, and clicking away supplies it.</summary>
    [Fact]
    public void ALastStatementWithNoSemicolonGetsOne()
    {
        Assert.Equal("a + b;", ScriptTermination.Terminate("a + b"));
    }

    /// <summary>A declaration is the shape a Dynamo-style block is actually written in.</summary>
    [Fact]
    public void ADeclarationGetsOneToo()
    {
        Assert.Equal(
            "var doubled = a * 2;\nvar tripled = a * 3;",
            ScriptTermination.Terminate("var doubled = a * 2;\nvar tripled = a * 3"));
    }

    /// <summary>
    /// <b>Only the last statement, and only when it is the one missing it.</b> Earlier lines are
    /// the parser's problem and not this rule's — a block missing a semicolon in the middle is
    /// being typed, not finished.
    /// </summary>
    [Fact]
    public void AnAlreadyTerminatedScriptIsReturnedUnchanged()
    {
        const string source = "var doubled = a * 2;\nvar tripled = a * 3;\n";

        Assert.Same(source, ScriptTermination.Terminate(source));
    }

    /// <summary>
    /// <b>The semicolon goes before the trailing comment, which is the whole reason the parser
    /// decides this and not a string test.</b> Appending to the end of the text would put it
    /// inside the comment, where it does nothing and cannot be seen.
    /// </summary>
    [Fact]
    public void TheSemicolonGoesBeforeATrailingComment()
    {
        Assert.Equal("var a = 1; // twice", ScriptTermination.Terminate("var a = 1 // twice"));
    }

    /// <summary>The trailing newline every starter script ends with is kept where it was.</summary>
    [Fact]
    public void TrailingWhitespaceIsPreserved()
    {
        Assert.Equal("a + b;\n", ScriptTermination.Terminate("a + b\n"));
    }

    /// <summary>
    /// <b>An unclosed brace is not a missing semicolon.</b> The insertion is discarded because it
    /// does not lower the error count, so a half-typed <c>if</c> is left exactly as typed.
    /// </summary>
    [Fact]
    public void AnUnclosedBlockIsLeftAlone()
    {
        const string source = "if (a > 1) {";

        Assert.Same(source, ScriptTermination.Terminate(source));
    }

    /// <summary>
    /// A statement that ends in <c>}</c> has no semicolon in the grammar, and must not acquire one.
    /// </summary>
    [Fact]
    public void ABlockStatementIsLeftAlone()
    {
        const string source = "if (a > 1) { b = 2; }";

        Assert.Same(source, ScriptTermination.Terminate(source));
    }

    /// <summary>Nothing to terminate, and nothing is done.</summary>
    [Fact]
    public void AnEmptyScriptIsLeftAlone()
    {
        Assert.Equal(string.Empty, ScriptTermination.Terminate(string.Empty));
        Assert.Equal("\n   \n", ScriptTermination.Terminate("\n   \n"));
    }

    /// <summary>A block that is only a comment declares nothing, and gains nothing.</summary>
    [Fact]
    public void ACommentOnlyScriptIsLeftAlone()
    {
        const string source = "// type an expression here\n";

        Assert.Same(source, ScriptTermination.Terminate(source));
    }

    /// <summary>
    /// <b>The end of the path, which is the claim the client made.</b> Typing a last line without
    /// a semicolon and clicking out of the block leaves a block that parses — so the variable is a
    /// port, and the wire has somewhere to land.
    /// </summary>
    [Fact]
    public void CommittingAnUnterminatedBlockGivesItItsOutputPortBack()
    {
        MainWindowViewModel model = new();

        int slot = model.PlaceCodeBlock(0, 0);
        Spark.UI.Graph.CanvasNode node = model.Graph.Nodes[slot];

        model.ShowCodeBlock(node);
        model.ScriptText = "var doubled = radius * 2";

        Assert.True(model.CommitScriptText(), "the edit was not committed");

        // The property carries the tidied text back, so the properties pane's editor shows the
        // character that was added rather than disagreeing with the node beside it.
        Assert.Equal("var doubled = radius * 2;", model.ScriptText);

        int rebuilt = model.Graph.SlotOf(node.Id);

        Assert.Equal("radius", Assert.Single(model.Graph.Nodes[rebuilt].Inputs).Name);
        Assert.Equal(["doubled"], model.Graph.Nodes[rebuilt].Outputs.Select(port => port.Name));
    }
}
