using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// Signature help inside a code block — `E6-T22`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The complaint this answers came from the running application.</b> Typing
/// <c>Circle.ByCentreNormalRadius(</c> said nothing at all, so the only way to learn that it wants
/// a centre, a normal and a radius — in that order — was to finish the line, run the graph and
/// read <c>SPK1046</c>. A compiler is already in the process; not telling the user what it knows
/// is the defect.
/// </para>
/// <para>
/// <b>These tests are about what Roslyn answers</b>, not about the popup that draws it —
/// <see cref="CodeBlockEditorTests"/> owns that, with a stub, so that a popup assertion never pays
/// for a Roslyn composition.
/// </para>
/// </remarks>
public sealed class SignatureHelpTests
{
    /// <summary>
    /// <b>The call being typed does not bind, and that is the normal case.</b> With no arguments
    /// written, overload resolution fails and <c>GetSymbolInfo</c> is empty — so an implementation
    /// built on the resolved symbol answers nothing exactly when it is asked.
    /// </summary>
    [Fact]
    public async Task AnUnfinishedCallStillHasItsParameters()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        const string Snippet = "var c = Circle.ByCentreNormalRadius(";

        ScriptSignatureHelp? help = await completion.SignatureAsync(
            Snippet, Snippet.Length, null, TestContext.Current.CancellationToken);

        Assert.NotNull(help);

        ScriptSignatureItem active = help.Value.Signatures[help.Value.ActiveSignature];

        Assert.Equal("ByCentreNormalRadius", active.Name);
        Assert.Equal(3, active.Parameters.Count);
        Assert.Contains("centre", active.Parameters[0]);
        Assert.Contains("Point3d", active.Parameters[0]);
        Assert.Equal("Circle", active.ReturnType);
        Assert.Equal(0, help.Value.ActiveParameter);
    }

    /// <summary>
    /// A comma moves on to the next parameter, which is the whole reason the popup is worth
    /// keeping open while the call is written.
    /// </summary>
    [Fact]
    public async Task ACommaAdvancesToTheNextParameter()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        const string Snippet = "var c = Circle.ByCentreNormalRadius(a, b, ";

        ScriptSignatureHelp? help = await completion.SignatureAsync(
            Snippet, Snippet.Length, null, TestContext.Current.CancellationToken);

        Assert.NotNull(help);
        Assert.Equal(2, help.Value.ActiveParameter);
        Assert.Contains("radius", help.Value.Signatures[help.Value.ActiveSignature].Parameters[2]);
    }

    /// <summary><c>new</c> asks the type's constructors rather than a method group.</summary>
    [Fact]
    public async Task ConstructorsAnswerForNew()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        const string Snippet = "var p = new Point3d(";

        ScriptSignatureHelp? help = await completion.SignatureAsync(
            Snippet, Snippet.Length, null, TestContext.Current.CancellationToken);

        Assert.NotNull(help);
        Assert.All(help.Value.Signatures, signature => Assert.Equal("Point3d", signature.Name));
        Assert.Contains(
            help.Value.Signatures,
            signature => signature.Parameters.Count == 3 && signature.Parameters[0].Contains("x", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>Signature help follows the wires too.</b> Nothing in the snippet says what
    /// <c>centre</c> is; the graph does, and the parameters of <c>centre.DistanceTo(</c> come from
    /// the wire — the same claim `E6-T7` makes for the completion list.
    /// </summary>
    [Fact]
    public async Task ItFollowsTheWiredTypeOfAPort()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        const string Snippet = "return centre.DistanceTo(";

        ScriptSignatureHelp? help = await completion.SignatureAsync(
            Snippet,
            Snippet.Length,
            new Dictionary<string, Type?> { ["centre"] = typeof(Point3d) },
            TestContext.Current.CancellationToken);

        Assert.NotNull(help);
        Assert.Equal("DistanceTo", help.Value.Signatures[0].Name);
        Assert.Single(help.Value.Signatures[0].Parameters);
        Assert.Contains("Point3d", help.Value.Signatures[0].Parameters[0]);
    }

    /// <summary>
    /// An unwired port is <c>dynamic</c> and a call on it has no signature to show. Answering
    /// <see langword="null"/> is the honest result: the compiler does not know either.
    /// </summary>
    [Fact]
    public async Task AnUnwiredPortHasNoSignatureToShow()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        const string Snippet = "return centre.DistanceTo(";

        ScriptSignatureHelp? help = await completion.SignatureAsync(
            Snippet,
            Snippet.Length,
            new Dictionary<string, Type?> { ["centre"] = null },
            TestContext.Current.CancellationToken);

        Assert.Null(help);
    }

    /// <summary>
    /// <b>A caret outside every argument list answers nothing</b>, rather than the last thing it
    /// saw. A popup that stays up once opened is worse than one that never opens, because it
    /// covers the code while claiming to describe it.
    /// </summary>
    [Fact]
    public async Task ACaretOutsideACallAnswersNothing()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        const string Snippet = "var p = new Point3d(1, 2, 3);";

        ScriptSignatureHelp? help = await completion.SignatureAsync(
            Snippet, Snippet.Length, null, TestContext.Current.CancellationToken);

        Assert.Null(help);
    }

    /// <summary>
    /// The innermost call wins, because that is the one being written. <c>Outer(Inner(</c> with
    /// the caret inside <c>Inner</c> describes <c>Inner</c>.
    /// </summary>
    [Fact]
    public async Task TheInnermostCallWins()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        const string Snippet = "var d = Math.Abs(new Point3d(";

        ScriptSignatureHelp? help = await completion.SignatureAsync(
            Snippet, Snippet.Length, null, TestContext.Current.CancellationToken);

        Assert.NotNull(help);
        Assert.All(help.Value.Signatures, signature => Assert.Equal("Point3d", signature.Name));
    }

    /// <summary>
    /// <b>The overload shown is the shortest one that still has the parameter being typed.</b>
    /// With one argument written, a caller wants the two-parameter overload rather than the
    /// nine-parameter one it also matches.
    /// </summary>
    [Fact]
    public async Task TheActiveOverloadFitsWhatHasBeenTyped()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        const string Snippet = "var s = string.Join(\",\", ";

        ScriptSignatureHelp? help = await completion.SignatureAsync(
            Snippet, Snippet.Length, null, TestContext.Current.CancellationToken);

        Assert.NotNull(help);
        Assert.Equal(1, help.Value.ActiveParameter);

        IReadOnlyList<ScriptSignatureItem> signatures = help.Value.Signatures;

        Assert.True(signatures[help.Value.ActiveSignature].Parameters.Count > 1);
        Assert.True(
            signatures.Select(signature => signature.Parameters.Count).SequenceEqual(
                signatures.Select(signature => signature.Parameters.Count).Order()),
            "Overloads are offered shortest first, so cycling with Alt+Down lengthens the call.");
    }
}
