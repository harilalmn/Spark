using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// Dynamo's three range forms inside a code block — <c>E10-T15</c>'s sugar over
/// <c>Spark.Api.NumberRange</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>These run through the real <see cref="ScriptNodeFactory"/> and evaluate the result</b>, not
/// through the rewriter in isolation. A lowering that produced a plausible tree which then failed
/// to bind would pass every syntax-level assertion anybody thought to write, and the feature is
/// "the numbers come out", not "the tree has the right shape".
/// </para>
/// <para>
/// <b>The negative tests carry as much weight as the positive ones.</b> Two-part ranges are legal
/// C# that this must not touch, and a <c>#</c> inside a string or a comment is not a range marker.
/// Each of those is a way for this feature to break code that used to compile.
/// </para>
/// </remarks>
public sealed class ScriptRangeTests
{
    private static ScriptNodeFactory Factory()
    {
        _ = typeof(Point3d).Assembly.Location;

        return new ScriptNodeFactory();
    }

    private static object? Run(string script)
    {
        object?[] outputs = [.. Factory().Create(script).Invoke([], CancellationToken.None)];

        return outputs.Length > 0 ? outputs[0] : null;
    }

    private static double[] Numbers(string script) =>
        [.. ((IReadOnlyList<double>)Run(script)!)];

    /// <summary>
    /// <b>The two forms the client asked for, as they typed them.</b> <c>0..1..#5</c> counts
    /// between bounds; <c>0..#5..1</c> counts from a start by a step.
    /// </summary>
    [Fact]
    public void TheTwoCountFormsProduceDynamosNumbers()
    {
        Assert.Equal([0, 0.25, 0.5, 0.75, 1], Numbers("return 0..1..#5;"));
        Assert.Equal([0, 1, 2, 3, 4], Numbers("return 0..#5..1;"));
    }

    /// <summary>
    /// The step form too, which is legal C# syntax that could never bind — <c>0..1..0.25</c>
    /// reaches the binder and fails there with <i>Cannot implicitly convert 'System.Range' to
    /// 'System.Index'</i>.
    /// </summary>
    [Fact]
    public void TheStepFormIsLoweredAsWell()
    {
        Assert.Equal([0, 0.25, 0.5, 0.75, 1], Numbers("return 0..1..0.25;"));
        Assert.Equal([3, 4, 5], Numbers("return 3..5..1;"));
    }

    /// <summary>
    /// <b>Assigned to a local and returned</b>, which is the shape the request was written in:
    /// <c>var parameters = 0..1..#5;</c>.
    /// </summary>
    [Fact]
    public void ARangeCanBeAssignedToALocal()
    {
        Assert.Equal([0, 0.25, 0.5, 0.75, 1], Numbers("var parameters = 0..1..#5; return parameters;"));
    }

    /// <summary>
    /// <b>The operands are expressions, not literals</b> — which is the whole reason the lowering
    /// is a tree rewrite rather than a regular expression over the text.
    /// </summary>
    [Fact]
    public void TheOperandsCanBeExpressions()
    {
        Assert.Equal(
            [0, 0.5, 1, 1.5, 2],
            Numbers("var n = 5; var top = 1 + 1; return 0..top..#n;"));

        Assert.Equal([2, 3, 4], Numbers("return (1 + 1)..(2 * 2)..#3;"));
    }

    /// <summary>
    /// <b>A two-part range is still a <see cref="Range"/>, and slicing still slices.</b> This is
    /// the code the feature could most easily have broken: <c>arr[1..^1]</c> is ordinary C#.
    /// </summary>
    [Fact]
    public void TwoPartRangesAreLeftAlone()
    {
        Assert.Equal(
            [2, 3],
            ((IEnumerable<int>)Run("var arr = new[] { 1, 2, 3, 4 }; return arr[1..^1];")!).ToArray());

        Assert.Equal(new Range(0, 1), Run("return 0..1;"));
    }

    /// <summary>
    /// <b>A <c>#</c> inside a string or a comment is not a range marker.</b> Roslyn decides which
    /// is which rather than a hand-written scanner, so verbatim, interpolated and raw literals come
    /// free — and this is what proves the classification is wired up at all.
    /// </summary>
    [Fact]
    public void MarkersInsideStringsAndCommentsAreLeftAlone()
    {
        Assert.Equal("a#b", Run(@"return ""a#b"";"));
        Assert.Equal("c#d", Run("return @\"c#d\";"));
        Assert.Equal("e#f", Run("var x = 1; // #5 is not a range\nreturn \"e#f\";"));
        Assert.Equal("g#h", Run("/* #9 */ return \"g#h\";"));

        // The one that would fool a scanner written the obvious way: a marker in a string on the
        // same line as a real one, before it.
        Assert.Equal([0, 0.5, 1], Numbers(@"var label = ""#3""; return 0..1..#3;"));
    }

    /// <summary>
    /// <b>Blanking is length-preserving</b>, and that is what buys the rest of the system its
    /// silence: <see cref="ScriptCompletion"/> resolves a caret by flat character offset and
    /// <see cref="ScriptSourceMap"/> reports a column unmapped. Change a length inside a user's
    /// line and both move without saying so.
    /// </summary>
    [Fact]
    public void BlankingChangesNoOffsets()
    {
        const string Script = "var a = 0..1..#5;\nvar b = \"#x\";\nreturn a;";

        BlankedScript blanked = ScriptRanges.Blank(Script);

        Assert.Equal(Script.Length, blanked.Text.Length);
        Assert.Equal(
            Script.Count(character => character == '\n'),
            blanked.Text.Count(character => character == '\n'));

        // One marker, the one in code; the one inside the string keeps its character.
        Assert.Equal(Script.IndexOf('#', StringComparison.Ordinal), Assert.Single(blanked.Markers));
        Assert.Contains("\"#x\"", blanked.Text, StringComparison.Ordinal);
    }

    /// <summary>A script with no marker is handed straight back, without a parse.</summary>
    [Fact]
    public void AScriptWithoutMarkersIsUntouched()
    {
        BlankedScript blanked = ScriptRanges.Blank("return 1 + 1;");

        Assert.Equal("return 1 + 1;", blanked.Text);
        Assert.False(blanked.HasMarkers);
        Assert.True(blanked.Markers.IsEmpty);
    }

    /// <summary>
    /// <b>A range does not move any other line's diagnostics</b>, which is the property the
    /// trivia-free lowering exists to keep — and it does move the column on <i>its own</i> line,
    /// which is worth pinning because it is not obvious and it is not new.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lines are the contract; columns on a rewritten line are not.</b>
    /// <see cref="ScriptSourceMap"/> maps lines by subtraction and states that it does not map
    /// columns. The lowered call is longer than the range it replaces, so an error further along
    /// the same line is reported at a column that is not the user's.
    /// </para>
    /// <para>
    /// <b><see cref="GuardWeaver"/> already does exactly this</b>, and measuring it was the only
    /// way to be sure this change adds no new class of problem: <c>while (true) { var q = 1; } var
    /// b = ;</c> has its <c>;</c> at column 37 and is reported at column <b>87</b>, because a
    /// <c>ScriptGuard.Tick()</c> went in ahead of it on the same line. So this is one shared wart
    /// with one shared fix — a column map — and not a reason to hold the feature. It is written
    /// down in <c>N122</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADiagnosticAfterARangeStillLandsOnTheUsersLine()
    {
        IReadOnlyList<ScriptDiagnostic> found = Factory().Diagnose(
            "var a = 0..1..#5;\nvar b = 2;\nvar c = ;\n");

        ScriptDiagnostic later = Assert.Single(found, diagnostic => diagnostic.IsError);

        Assert.Equal(3, later.Line);
        Assert.Equal(9, later.Column);

        // The sharper one: an error on the *same line as the range, after it*. This is the column
        // a text rewrite moves. `NumberRange.ByCount(0, 1, 5)` is eighteen characters longer than
        // `0..1..#5`, so a rewritten script would report this at 45 and send the user to look at
        // the middle of nowhere.
        //                        1234567890123456789012345678
        const string SameLine = "var a = 0..1..#5; var b = ;";

        ScriptDiagnostic beside = Assert.Single(
            Factory().Diagnose(SameLine), diagnostic => diagnostic.IsError);

        Assert.Equal(1, beside.Line);

        int typed = SameLine.IndexOf(';', SameLine.IndexOf("var b", StringComparison.Ordinal)) + 1;

        Assert.Equal(27, typed);
        Assert.True(
            beside.Column > typed,
            $"The column on a lowered line is the generated one ({beside.Column}), not the typed one ({typed}).");
    }

    /// <summary>
    /// <b>Output ports are still inferred</b> from a script whose declaration uses a range. The
    /// port names are read by parsing the script on its own, which a stray <c>#</c> would have
    /// stopped — silently, by producing no ports at all.
    /// </summary>
    [Fact]
    public void PortsAreInferredThroughARange()
    {
        NodeDefinitionSource definition = Factory().Create("var parameters = 0..1..#5;");

        Assert.Equal(["parameters"], definition.Outputs.Select(port => port.Name));
    }

    /// <summary>
    /// <b>Nonsense is left for the compiler to report</b> rather than guessed at: two markers in
    /// one range have no reading, so the block fails rather than silently picking one.
    /// </summary>
    [Fact]
    public void TwoMarkersInOneRangeAreNotGuessedAt()
    {
        Assert.Throws<InvalidOperationException>(() => Run("return 0..#5..#5;"));
    }

    /// <summary>Lowering a tree with no markers is the step form, and never throws.</summary>
    [Fact]
    public void NullArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => ScriptRanges.Blank(null!));
        Assert.Throws<ArgumentNullException>(() => ScriptRanges.Lower(null!, ImmutableArray<int>.Empty));
    }
}
