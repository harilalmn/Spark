using System.Linq;
using Spark.Api;
using Spark.Scripting;

namespace Spark.Scripting.Tests;

/// <summary>
/// The source map is what makes a code block feel like an editor rather than like a form. If it
/// slips by one character, every squiggle, every completion offset and every reported line number
/// slips with it — and the user is sent to look at code that is fine.
/// </summary>
public sealed class SourceMapTests
{
    /// <summary>
    /// The one that matters. A compiler error on the user's line three must be reported on line
    /// three, not on whatever line the generated file happened to put it on.
    /// </summary>
    [Fact]
    public void ACompilerErrorIsReportedOnTheLineTheUserTyped()
    {
        const string Text = """
            double a = 1;
            double b = 2;
            a.ThisMemberDoesNotExist();
            a + b
            """;

        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(Text, CodeBlockTestHarness.Options());

        ScriptDiagnostic error = Assert.Single(
            compilation.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        Assert.Equal("CS1061", error.CompilerId);
        Assert.Equal(3, error.Line);
    }

    /// <summary>
    /// The header sits above a <c>#line 1</c> directive, so a long prelude must not push the user's
    /// lines down. Leading blank lines in the user's own text still count, because they are the
    /// user's.
    /// </summary>
    [Fact]
    public void LeadingBlankLinesShiftTheReportedLineAndTheHeaderDoesNot()
    {
        const string Text = """


            nope.Missing();
            """;

        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(Text, CodeBlockTestHarness.Options());

        ScriptDiagnostic error = Assert.Single(
            compilation.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        Assert.Equal(3, error.Line);
    }

    /// <summary>
    /// The line and the column both have to survive a guard call woven into the middle of the very
    /// line they are on. The guard is inserted immediately after the loop's opening brace, so
    /// everything to its right on that line moves in the generated text and must not move here.
    /// </summary>
    [Fact]
    public void ALineAndColumnSurviveAGuardWovenIntoTheSameLine()
    {
        const string Text = """
            double total = 0;
            for (int i = 0; i < 3; i++) { total += 1.NotAMember(); }
            total
            """;

        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(Text, CodeBlockTestHarness.Options());

        ScriptDiagnostic error = Assert.Single(
            compilation.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        int offset = Text.IndexOf("NotAMember", System.StringComparison.Ordinal);
        int columnOnLine = offset - (Text.IndexOf('\n', System.StringComparison.Ordinal) + 1) + 1;

        Assert.Equal("CS1061", error.CompilerId);
        Assert.Equal(2, error.Line);
        Assert.Equal(columnOnLine, error.Column);
        Assert.Equal(offset, error.Start);
    }

    /// <summary>Every offset in the user's text must survive a round trip through the map.</summary>
    [Fact]
    public void EveryUserOffsetRoundTripsThroughTheMap()
    {
        const string Text = """
            double a = radius * 2;
            for (int i = 0; i < 3; i++) { a += i; }
            (doubled: a, half: a / 2)
            """;

        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(Text, CodeBlockTestHarness.Options());

        for (int offset = 0; offset < Text.Length; offset++)
        {
            int generated = compilation.Map.ToGenerated(offset);
            Assert.Equal(offset, compilation.Map.ToUser(generated));
        }
    }

    /// <summary>An offset in the generated scaffolding maps to nothing, rather than to a nearby guess.</summary>
    [Fact]
    public void AnOffsetInScaffoldingMapsToNothing()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile("1 + 1", CodeBlockTestHarness.Options());

        Assert.Equal(-1, compilation.Map.ToUser(0));
    }
}
