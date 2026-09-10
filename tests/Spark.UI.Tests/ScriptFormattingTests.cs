using System;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// Tidying a code block's text — `E8-T84`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>, with a screenshot of <c>3;20;</c> sharing one line: <i>add line
/// breaks automatically when one clicks out of a code block, or perform an auto-formatting.</i>
/// </para>
/// <para>
/// <b>The assertion that makes the feature safe is <see cref="ThePortsAreIdenticalAfterFormatting"/>.</b>
/// A block's ports come from its declarations and the identifiers it uses, not from its lines — so
/// moving text can never change a node's interface or break a wire. That is a claim worth a test
/// rather than a comment, because if it were false this feature would silently rewire graphs.
/// </para>
/// </remarks>
public sealed class ScriptFormattingTests
{
    /// <summary>
    /// <b>The client's own block.</b> Two statements on one line become two lines.
    /// </summary>
    [Fact]
    public void TwoStatementsOnOneLineBecomeTwoLines()
    {
        string formatted = ScriptFormatting.Format("3;20;");

        Assert.Equal(2, formatted.Split('\n').Length);
        Assert.Contains("3;", formatted, StringComparison.Ordinal);
        Assert.Contains("20;", formatted, StringComparison.Ordinal);
    }

    /// <summary>And the same for the declarations a block is usually made of.</summary>
    [Fact]
    public void DeclarationsAreGivenALineEach()
    {
        string formatted = ScriptFormatting.Format("var a = 1; var b = a * 2; var c = b + 1;");

        Assert.Equal(3, formatted.Split('\n').Length);
    }

    /// <summary>
    /// <b>Text that does not parse is returned byte for byte.</b> Focus is lost in the middle of
    /// half-finished work, and an editor that rearranges a broken block is fighting the person
    /// trying to fix it.
    /// </summary>
    [Theory]
    [InlineData("var a = ")]
    [InlineData("var a = (1 + 2")]
    [InlineData("if (x) {")]
    [InlineData("}{")]
    public void TextThatDoesNotParseIsUntouched(string broken)
    {
        Assert.Equal(broken, ScriptFormatting.Format(broken));
    }

    /// <summary>Nothing at all is nothing at all, rather than an exception.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyTextIsLeftAlone(string? source)
    {
        Assert.Equal(source ?? string.Empty, ScriptFormatting.Format(source));
    }

    /// <summary>
    /// <b>A <c>using</c> survives and stays at the top</b>, which is why the text is parsed as a
    /// compilation unit rather than as a list of statements: `E6-T39` lets a block open with one,
    /// and a <c>using</c> directive is not a statement.
    /// </summary>
    [Fact]
    public void AUsingDirectiveSurvivesAndStaysAtTheTop()
    {
        string formatted = ScriptFormatting.Format("using System.Text; var a = 1;");

        Assert.StartsWith("using System.Text;", formatted, StringComparison.Ordinal);
        Assert.Contains("var a = 1;", formatted, StringComparison.Ordinal);
    }

    /// <summary>An alias survives too, which is the form `E6-T39` exists for.</summary>
    [Fact]
    public void AUsingAliasSurvives()
    {
        string formatted = ScriptFormatting.Format("using Point = Spark.Geometry.Point3d; var a = 1;");

        Assert.Contains("using Point = Spark.Geometry.Point3d;", formatted, StringComparison.Ordinal);
    }

    /// <summary>A comment is the user's writing and is not thrown away.</summary>
    [Fact]
    public void CommentsSurvive()
    {
        string formatted = ScriptFormatting.Format("// the wall height\nvar h = 3.2;");

        Assert.Contains("// the wall height", formatted, StringComparison.Ordinal);
        Assert.Contains("var h = 3.2;", formatted, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Formatting formatted text changes nothing</b>, so a block does not become modified by
    /// being clicked into and out of again — which would mark a saved document dirty for nothing.
    /// </summary>
    [Fact]
    public void FormattingIsIdempotent()
    {
        string once = ScriptFormatting.Format("var a = 1; var b = 2;");
        string twice = ScriptFormatting.Format(once);

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// <b>The assertion the whole feature rests on.</b> A block's ports come from its declarations
    /// and the identifiers it uses, not from its lines — so tidying the text cannot change a node's
    /// interface, and no wire can break because somebody clicked away from a code block.
    /// </summary>
    [Fact]
    public void ThePortsAreIdenticalAfterFormatting()
    {
        const string Source = "var doubled = width * 2; var area = doubled * height;";

        ReferenceCatalog catalogue = new();
        ScriptNodeFactory factory = new(catalogue);

        NodeDefinitionSource before = factory.Create(Source);
        NodeDefinitionSource after = factory.Create(ScriptFormatting.Format(Source));

        Assert.Equal(
            before.Inputs.Select(port => port.Name),
            after.Inputs.Select(port => port.Name));

        Assert.Equal(
            before.Outputs.Select(port => port.Name),
            after.Outputs.Select(port => port.Name));
    }

    /// <summary>And the block still computes what it computed, which is the other half of that.</summary>
    [Fact]
    public void TheBlockStillComputesTheSameAnswer()
    {
        const string Source = "var doubled = a * 2; var total = doubled + 1;";

        ScriptNodeFactory factory = new(new ReferenceCatalog());

        object?[] before = [.. factory.Create(Source).Invoke([21.0], CancellationToken.None)];
        object?[] after =
        [
            .. factory.Create(ScriptFormatting.Format(Source)).Invoke([21.0], CancellationToken.None),
        ];

        Assert.Equal(before, after);
    }

    /// <summary>
    /// <b>The trap this method exists for, asserted first.</b> Pressing <kbd>Enter</kbd> must
    /// leave the newline that was just typed in place.
    /// </summary>
    /// <remarks>
    /// <see cref="ScriptFormatting.Format"/> trims trailing newlines on purpose, so tidying the
    /// whole document the instant a newline is typed at the end of it takes that newline straight
    /// back out — the feature undoing the keystroke that triggered it, on every line. A test that
    /// only checked *that the text got tidied* would pass against exactly that implementation,
    /// which is why this one is written in terms of the newline rather than the tidying.
    /// </remarks>
    [Fact]
    public void FormattingOnALineBreakKeepsTheLineBreak()
    {
        // What the document holds the instant after Enter, with the caret on the new empty line.
        string typed = "var a=1;\n";
        string result = ScriptFormatting.FormatAbove(typed, typed.Length, out int caret);

        Assert.EndsWith("\n", result, StringComparison.Ordinal);
        Assert.Equal("var a = 1;\n", result);

        // And the caret is still on the empty line at the end, one character further along than it
        // was, because `var a=1;` gained a space.
        Assert.Equal(result.Length, caret);
    }

    /// <summary>The line the caret is on is being typed, so it is never touched.</summary>
    [Fact]
    public void TheCaretsOwnLineIsLeftAlone()
    {
        // The second line is mid-typing and deliberately untidy. Only the first should move.
        string typed = "var a=1;\nvar b=a  *2";
        string result = ScriptFormatting.FormatAbove(typed, typed.Length, out _);

        Assert.Equal("var a = 1;\nvar b=a  *2", result);
    }

    /// <summary>
    /// <b>A block that does not parse yet is left exactly as it is</b>, which is what makes this
    /// safe to run on every line break rather than only on a finished block.
    /// </summary>
    [Fact]
    public void AnUnfinishedBlockAboveTheCaretIsUntouched()
    {
        string typed = "if (a > 1)\n{\n    var b=2;\n";

        Assert.Equal(typed, ScriptFormatting.FormatAbove(typed, typed.Length, out _));
    }

    /// <summary>Nothing is above the first line, so a caret on it changes nothing.</summary>
    [Fact]
    public void ACaretOnTheFirstLineChangesNothing()
    {
        string typed = "var a=1;";
        string result = ScriptFormatting.FormatAbove(typed, typed.Length, out int caret);

        Assert.Equal(typed, result);
        Assert.Equal(typed.Length, caret);
    }

    /// <summary>
    /// <b>The caret moves by the length difference, and the text after it is untouched.</b> This
    /// is the assertion that catches a caret left behind when the tidying shortens the text above
    /// it — the direction that is easy to get wrong, because most tidying makes text longer.
    /// </summary>
    [Fact]
    public void TheCaretFollowsTextThatGetsShorter()
    {
        // Two statements on one line become two lines: longer. Extra blank lines: shorter.
        string typed = "var a = 1;\n\n\n\nvar b = 2;\n";
        string result = ScriptFormatting.FormatAbove(typed, typed.Length, out int caret);

        Assert.True(result.Length < typed.Length, "collapsing the blank lines should shorten it");
        Assert.Equal(result.Length, caret);
        Assert.EndsWith("\n", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pressing <kbd>Enter</kbd> in the middle of a block moves the caret with the text, not to
    /// the end of it.
    /// </summary>
    [Fact]
    public void ACaretInTheMiddleStaysWhereItsTextWent()
    {
        // Caret at the start of the third line, which holds `var c = 3;`.
        string typed = "var a=1;\nvar b=2;\nvar c = 3;";
        int caretAt = typed.IndexOf("var c", StringComparison.Ordinal);

        string result = ScriptFormatting.FormatAbove(typed, caretAt, out int caret);

        Assert.Equal("var a = 1;\nvar b = 2;\nvar c = 3;", result);
        Assert.Equal(result.IndexOf("var c", StringComparison.Ordinal), caret);
    }

    /// <summary>Windows line endings put the boundary in the same place.</summary>
    [Fact]
    public void CarriageReturnsDoNotMoveTheBoundary()
    {
        string typed = "var a=1;\r\n";
        string result = ScriptFormatting.FormatAbove(typed, typed.Length, out _);

        Assert.Equal("var a = 1;\n", result);
    }

    /// <summary>An empty block is not a crash, and neither is a caret past the end of one.</summary>
    [Fact]
    public void EmptyTextAndAStrayCaretAreBothSafe()
    {
        Assert.Equal(string.Empty, ScriptFormatting.FormatAbove(null, 5, out int fromNull));
        Assert.Equal(0, fromNull);

        Assert.Equal("var a = 1;", ScriptFormatting.FormatAbove("var a = 1;", 9999, out int clamped));
        Assert.Equal(10, clamped);
    }


    /// <summary>
    /// <b>Format Document tidies the caret's own line too</b>, which is the difference from
    /// <see cref="ScriptFormatting.FormatAbove"/>: this one was asked for.
    /// </summary>
    [Fact]
    public void FormattingTheDocumentTidiesTheCaretsLine()
    {
        string typed = "var a=1;\nvar b=2;";
        string result = ScriptFormatting.FormatDocument(typed, typed.Length, out _);

        Assert.Equal("var a = 1;\nvar b = 2;", result);
    }

    /// <summary>
    /// <b>The caret lands on the same character of the same token, not at the same offset.</b>
    /// </summary>
    /// <remarks>
    /// The mapping counts non-whitespace characters, which is exact because formatting rewrites
    /// only whitespace. Clamping the old offset instead — the obvious implementation — puts the
    /// caret out by however much the indentation changed above it, and this is the test that tells
    /// the two apart: the text before the caret gains four characters, so a clamped offset would
    /// still be inside `b` rather than after it.
    /// </remarks>
    [Fact]
    public void TheCaretLandsOnTheSameToken()
    {
        // `var b=2;` becomes `var b = 2;`: two spaces added before the caret's line, one within it.
        string typed = "var a=1;\nvar b=2;";
        int caretAt = typed.Length;

        string result = ScriptFormatting.FormatDocument(typed, caretAt, out int caret);

        Assert.Equal(result.Length, caret);
        Assert.NotEqual(caretAt, caret);
    }

    /// <summary>And a caret in the middle follows its own token rather than the text length.</summary>
    [Fact]
    public void ACaretMidwayFollowsItsToken()
    {
        // Caret immediately after the `1` on the first line.
        string typed = "var a=1;\nvar b=2;";
        int caretAt = typed.IndexOf('1', StringComparison.Ordinal) + 1;

        string result = ScriptFormatting.FormatDocument(typed, caretAt, out int caret);

        Assert.Equal("var a = 1;\nvar b = 2;", result);
        Assert.Equal(result.IndexOf('1', StringComparison.Ordinal) + 1, caret);
    }

    /// <summary>A caret at the very top has passed nothing and stays at the top.</summary>
    [Fact]
    public void ACaretAtTheStartStaysAtTheStart()
    {
        ScriptFormatting.FormatDocument("var a=1;\nvar b=2;", 0, out int caret);

        Assert.Equal(0, caret);
    }

    /// <summary>Text that will not parse is returned untouched, and so is the caret.</summary>
    [Fact]
    public void FormattingTheDocumentLeavesBrokenTextAlone()
    {
        string typed = "if (a > 1) {";

        Assert.Equal(typed, ScriptFormatting.FormatDocument(typed, 4, out int caret));
        Assert.Equal(4, caret);
    }

}
