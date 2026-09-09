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
}
