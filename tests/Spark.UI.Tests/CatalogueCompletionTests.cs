using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// Completion and signature help over the catalogue the application actually uses — `E6-T32`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reported by the client: no completion and no signature help inside a code block.</b> The
/// cause was a seam rather than a fault in either half. <c>ScriptNodeFactory</c> writes the
/// catalogue's imports into the script as <c>using</c> <i>source lines</i>, where
/// <c>using Circle = Spark.Geometry.Circle;</c> is ordinary C#. <c>ScriptCompletion</c> passed the
/// same strings to <c>CSharpCompilationOptions.Usings</c>, which is the global-usings list and
/// takes <b>namespace names</b> — so every alias was discarded without a diagnostic while
/// <c>using Spark.Nodes.Core;</c> survived, and all ten aliased names became ambiguous
/// (<c>CS0104</c>) in the workspace that answers the editor.
/// </para>
/// <para>
/// <b>Every completion test in this repository built its service from an assembly list</b>, which
/// carries no aliases at all — so the defect lived precisely in the gap between the two
/// constructors, and could not have been caught by adding more cases to the tests that existed.
/// These use <see cref="ReferenceCatalog"/> because that is the constructor the shell calls.
/// </para>
/// </remarks>
public sealed class CatalogueCompletionTests
{
    private static ScriptCompletion Real()
    {
        // The catalogue sweeps loaded assemblies, and a referenced assembly does not load until
        // something touches a type in it — so both are touched before it is built.
        _ = typeof(Point3d).Assembly.Location;
        _ = typeof(Spark.Nodes.Core.Point).Assembly.Location;

        return new ScriptCompletion(new ReferenceCatalog());
    }

    /// <summary>
    /// <b>The aliased names bind, and this is the whole row.</b> Ten names collide between
    /// <c>Spark.Geometry</c> and <c>Spark.Nodes.Core</c>; the alias is what settles each one, and
    /// before this row none of them reached the editor.
    /// </summary>
    [Theory]
    [InlineData("new Circle(")]
    [InlineData("new Line(")]
    [InlineData("new Arc(")]
    [InlineData("Circle.FromCenterRadius(")]
    [InlineData("var m = Math.Max(")]
    public async Task AnAliasedNameHasSignatureHelp(string snippet)
    {
        using ScriptCompletion completion = Real();

        ScriptSignatureHelp? help = await completion.SignatureAsync(
            snippet, snippet.Length, null, TestContext.Current.CancellationToken);

        Assert.NotNull(help);
        Assert.NotEmpty(help.Value.Signatures);
    }

    /// <summary>
    /// <b>The name resolves to the geometry type rather than to the façade, and
    /// <c>new Circle(</c> is what proves it.</b>
    /// </summary>
    /// <remarks>
    /// <b>Nothing about the member list can tell those two apart, and two drafts of this test
    /// tried.</b> <c>Spark.Nodes.Core.Circle</c> is a façade over <c>Spark.Geometry.Circle</c>
    /// carrying factories of the same four names, so a list that had bound to the wrong one looked
    /// right; and quick info prints <c>MinimallyQualifiedFormat</c>, which drops the namespace that
    /// would have settled it. Both drafts went on passing with the defect reinstated. The façade is
    /// a <b>static class</b>, so it has no constructors at all — which makes a constructor
    /// signature something only the geometry type can produce.
    /// </remarks>
    [Fact]
    public async Task ConstructorsProveTheAliasReachedTheGeometryType()
    {
        using ScriptCompletion completion = Real();

        const string Snippet = "new Circle(";

        ScriptSignatureHelp? help = await completion.SignatureAsync(
            Snippet, Snippet.Length, null, TestContext.Current.CancellationToken);

        Assert.NotNull(help);
        Assert.All(help.Value.Signatures, signature => Assert.Equal("Circle", signature.Name));
        Assert.Contains(
            help.Value.Signatures,
            signature => signature.Parameters.Any(
                parameter => parameter.Contains("Point3d", StringComparison.Ordinal)));
    }

    /// <summary>
    /// <b><c>Math</c> is <c>System.Math</c> here too.</b> The alias exists to settle that collision,
    /// and an editor that offered the library's <c>Math</c> while the compiler used
    /// <c>System</c>'s would be worse than one that offered nothing.
    /// </summary>
    [Fact]
    public async Task MathIsSystemMathInTheEditorAsWell()
    {
        using ScriptCompletion completion = Real();

        IReadOnlyList<ScriptCompletionItem> members = await completion.CompleteAsync(
            "Math.", "Math.".Length, null, TestContext.Current.CancellationToken);

        Assert.Contains(members, item => item.DisplayText == "PI");
        Assert.Contains(members, item => item.DisplayText == "Sqrt");
    }

    /// <summary>
    /// <b>A name that was never aliased still works</b> — the prelude is prepended to every
    /// request, so an off-by-one in the caret shift would break everything rather than only the
    /// aliases, and <c>Point3d</c> is the case that would go on passing if it did.
    /// </summary>
    [Fact]
    public async Task AnUnaliasedNameIsUnaffected()
    {
        using ScriptCompletion completion = Real();

        const string Snippet = "var p = new Point3d(";

        ScriptSignatureHelp? help = await completion.SignatureAsync(
            Snippet, Snippet.Length, null, TestContext.Current.CancellationToken);

        Assert.NotNull(help);
        Assert.NotEmpty(help.Value.Signatures);
    }

    /// <summary>
    /// <b>The caret shift is exact, not approximately right.</b> The prelude sits in front of the
    /// user's text, so every offset the editor sends has to be moved past it — and a signature that
    /// names the wrong parameter is a confident wrong answer, which is worse than silence.
    /// </summary>
    [Fact]
    public async Task TheActiveParameterIsTheOneTheCaretIsIn()
    {
        using ScriptCompletion completion = Real();

        const string Snippet = "Circle.FromCenterNormalRadius(Point3d.Origin, Vector3d.ZAxis, ";

        ScriptSignatureHelp? help = await completion.SignatureAsync(
            Snippet, Snippet.Length, null, TestContext.Current.CancellationToken);

        Assert.NotNull(help);
        Assert.Equal(2, help.Value.ActiveParameter);
    }

    /// <summary>
    /// <b>Input ports still declare themselves, with the prelude in front of them.</b> Two prefixes
    /// now share one offset, and this is the test that says they compose rather than collide.
    /// </summary>
    [Fact]
    public async Task AnInputPortStillCompletesAgainstItsWiredType()
    {
        using ScriptCompletion completion = Real();

        Dictionary<string, Type?> inputs = new(StringComparer.Ordinal) { ["center"] = typeof(Point3d) };

        IReadOnlyList<ScriptCompletionItem> members = await completion.CompleteAsync(
            "center.", "center.".Length, inputs, TestContext.Current.CancellationToken);

        Assert.Contains(members, item => item.DisplayText == "DistanceTo");
    }
}
