using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// The completion list can see a type another block declares — `E6-T37`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reported by the client</b>: typing <c>Test.</c> in one block, with <c>class Test</c> declared
/// in the block beside it, offered snippets and nothing else. `E6-T35` compiles every block's type
/// declarations into one shared assembly and every block references it — and
/// <see cref="ScriptCompletion"/> was built from the <see cref="ReferenceCatalog"/> alone, so the
/// type bound perfectly in the compiler that runs the script and did not exist at all in the
/// service that answers the editor.
/// </para>
/// <para>
/// <b>These go through <see cref="ReferenceCatalog"/> rather than the assembly-list
/// constructor</b>, deliberately. `E6-T32` is the record of a completion defect that lived in
/// exactly the gap between those two constructors: every test in the repository used the one that
/// carries no aliases, so no amount of extra cases on them could have found it.
/// </para>
/// </remarks>
public sealed class CompletionSeesOtherBlocksTests
{
    private const string Declares = """
        public class Helper
        {
            public static double Twice(double x) => x * 2;
            public static double Halve(double x) => x / 2;
        }
        var placed = 1;
        """;

    private static ScriptNodeFactory Factory()
    {
        // The catalogue is built from what this process has loaded; naming a geometry type loads
        // the assembly the prelude imports, or every script here fails for an unrelated reason.
        _ = typeof(Spark.Geometry.Point3d).Assembly.Location;

        return new ScriptNodeFactory();
    }

    /// <summary>
    /// <b>The client's own case.</b> The members offered for <c>Helper.</c> are the ones the other
    /// block declared.
    /// </summary>
    [Fact]
    public async Task MembersOfATypeDeclaredInAnotherBlockAreOffered()
    {
        ScriptNodeFactory factory = Factory();
        const string uses = "var x = Helper.";

        Assert.True(factory.Share([Declares, uses]));

        using ScriptCompletion completion = new(factory.References);

        Assert.True(completion.Reference(factory.Declarations));

        string[] offered =
        [
            .. (await completion.CompleteAsync(uses, uses.Length, null, CancellationToken.None)
                .ConfigureAwait(true)).Select(item => item.DisplayText),
        ];

        Assert.Contains("Twice", offered, StringComparer.Ordinal);
        Assert.Contains("Halve", offered, StringComparer.Ordinal);
    }

    /// <summary>
    /// <b>Without the shared reference there is nothing to offer</b>, which is what the client saw
    /// and what makes the test above mean something. Reinstating the defect is one line.
    /// </summary>
    [Fact]
    public async Task WithoutTheSharedReferenceTheMembersAreNotThere()
    {
        ScriptNodeFactory factory = Factory();
        const string uses = "var x = Helper.";

        _ = factory.Share([Declares, uses]);

        using ScriptCompletion completion = new(factory.References);

        string[] offered =
        [
            .. (await completion.CompleteAsync(uses, uses.Length, null, CancellationToken.None)
                .ConfigureAwait(true)).Select(item => item.DisplayText),
        ];

        Assert.DoesNotContain("Twice", offered, StringComparer.Ordinal);
    }

    /// <summary>
    /// <b>Signature help agrees with the list</b>, because they are three answers to one question
    /// and `E6-T13` is that any of them disagreeing with the compiler is worse than silence.
    /// </summary>
    [Fact]
    public async Task SignatureHelpKnowsTheDeclaredTypeToo()
    {
        ScriptNodeFactory factory = Factory();
        const string uses = "var x = Helper.Twice(";

        _ = factory.Share([Declares, uses]);

        using ScriptCompletion completion = new(factory.References);

        _ = completion.Reference(factory.Declarations);

        ScriptSignatureHelp? signature = await completion
            .SignatureAsync(uses, uses.Length, null, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.NotNull(signature);
        Assert.Contains(
            signature!.Value.Signatures,
            item => string.Equals(item.Name, "Twice", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>A set that has not moved costs nothing.</b> This is asked on every completion, every
    /// signature and every hover, so a repeat must not update the solution.
    /// </summary>
    [Fact]
    public void ReferencingTheSameDeclarationsTwiceChangesNothing()
    {
        ScriptNodeFactory factory = Factory();

        _ = factory.Share([Declares, "var x = 1;"]);

        using ScriptCompletion completion = new(factory.References);

        Assert.True(completion.Reference(factory.Declarations));
        Assert.False(completion.Reference(factory.Declarations));
    }

    /// <summary>
    /// <b>Renaming the declared type takes the old members away.</b> The references are replaced
    /// rather than appended to — a list that only grew would offer every version of a class the
    /// user has ever typed, side by side.
    /// </summary>
    [Fact]
    public async Task RenamingTheDeclaredTypeReplacesWhatIsOffered()
    {
        ScriptNodeFactory factory = Factory();
        const string uses = "var x = Helper.";

        _ = factory.Share([Declares, uses]);

        using ScriptCompletion completion = new(factory.References);

        _ = completion.Reference(factory.Declarations);

        Assert.True(factory.Share([Declares.Replace("Helper", "Assistant", StringComparison.Ordinal), uses]));
        Assert.True(completion.Reference(factory.Declarations));

        string[] offered =
        [
            .. (await completion.CompleteAsync(uses, uses.Length, null, CancellationToken.None)
                .ConfigureAwait(true)).Select(item => item.DisplayText),
        ];

        Assert.DoesNotContain("Twice", offered, StringComparer.Ordinal);
    }
}
