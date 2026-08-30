using System;
using System.Collections.Generic;
using System.IO;
using Spark.Host;
using Spark.UI;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// The trust posture — `E6-T16`: what runs when a graph is opened, and what does not.
/// </summary>
/// <remarks>
/// <b>The claim being tested is a negative one</b>, which is why it is worth a file. A graph
/// containing a code block must **not** be evaluated because somebody opened it — .NET has no way
/// to sandbox what it would run, so the only honest posture is to open it, draw it, and wait to be
/// told. Every test below is either that negative or the exact conditions under which it is
/// lifted.
/// </remarks>
public sealed class ScriptTrustTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "spark-trust-" + Guid.NewGuid().ToString("N") + ".txt");

    /// <inheritdoc/>
    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    /// <summary>Nothing is trusted to begin with, which is the only safe starting state.</summary>
    [Fact]
    public void NothingIsTrustedToBeginWith() =>
        Assert.False(new ScriptTrustStore(_path).IsTrusted("graph.spark", ["return a;"]));

    /// <summary>A decision, once made, is remembered.</summary>
    [Fact]
    public void ADecisionIsRemembered()
    {
        ScriptTrustStore store = new(_path);

        store.Trust("graph.spark", ["return a;"]);

        Assert.True(store.IsTrusted("graph.spark", ["return a;"]));
    }

    /// <summary>
    /// <b>It is remembered across sessions</b>, which is what makes it a store rather than a flag.
    /// </summary>
    [Fact]
    public void ADecisionSurvivesTheSession()
    {
        new ScriptTrustStore(_path).Trust("graph.spark", ["return a;"]);

        Assert.True(new ScriptTrustStore(_path).IsTrusted("graph.spark", ["return a;"]));
    }

    /// <summary>
    /// <b>Changing what the file says withdraws the trust.</b> Keyed on the file alone, a colleague
    /// who edited a shared graph would inherit the permission the user granted to what it used to
    /// say — which is the whole attack.
    /// </summary>
    [Fact]
    public void ChangingTheContentWithdrawsTrust()
    {
        ScriptTrustStore store = new(_path);

        store.Trust("graph.spark", ["return a;"]);

        Assert.False(store.IsTrusted("graph.spark", ["System.Diagnostics.Process.Start(\"cmd\");"]));
    }

    /// <summary>
    /// <b>Trusting one file does not trust a copy of it elsewhere.</b> Keyed on content alone, a
    /// graph would carry its permission with it wherever it travelled.
    /// </summary>
    [Fact]
    public void TrustDoesNotTravelWithACopy()
    {
        ScriptTrustStore store = new(_path);

        store.Trust(Path.Combine(Path.GetTempPath(), "mine.spark"), ["return a;"]);

        Assert.False(store.IsTrusted(Path.Combine(Path.GetTempPath(), "downloaded.spark"), ["return a;"]));
    }

    /// <summary>Line endings are not content. A graph through a text editor is the same graph.</summary>
    [Fact]
    public void LineEndingsAreNotContent()
    {
        ScriptTrustStore store = new(_path);

        store.Trust("graph.spark", ["var a = 1;\nreturn a;"]);

        Assert.True(store.IsTrusted("graph.spark", ["var a = 1;\r\nreturn a;"]));
    }

    /// <summary>A document with no path is never trusted, because there is nothing to key to.</summary>
    [Fact]
    public void AnUnsavedDocumentIsNeverTrusted() =>
        Assert.False(new ScriptTrustStore(_path).IsTrusted(origin: null, ["return a;"]));

    /// <summary>Revoking works, because a trust store with no revocation is not one.</summary>
    [Fact]
    public void ForgettingRevokesEverything()
    {
        ScriptTrustStore store = new(_path);

        store.Trust("graph.spark", ["return a;"]);
        store.Forget();

        Assert.Equal(0, store.Count);
        Assert.False(store.IsTrusted("graph.spark", ["return a;"]));
    }

    /// <summary>
    /// A store with nowhere to write still answers, and answers *no* — failing towards asking is
    /// the only safe direction for a decision like this.
    /// </summary>
    [Fact]
    public void AStoreWithNoFileRemembersNothing()
    {
        ScriptTrustStore store = new(path: null);

        store.Trust("graph.spark", ["return a;"]);

        Assert.True(store.IsTrusted("graph.spark", ["return a;"]));
        Assert.False(new ScriptTrustStore(path: null).IsTrusted("graph.spark", ["return a;"]));
    }

    /// <summary>
    /// <b>Opening a graph that contains a code block does not run it.</b> This is the behaviour the
    /// row is about, and the negative is the assertion: the graph is there, drawn, with a banner,
    /// and nothing has been evaluated.
    /// </summary>
    [Fact]
    public void OpeningAGraphWithACodeBlockDoesNotRunIt()
    {
        using MainWindowViewModel model = new();

        Assert.True(model.PlaceCodeBlock(0, 0) >= 0);

        string saved = Assert.IsType<string>(model.TrySaveDocument());

        using MainWindowViewModel opened = new();

        Assert.True(opened.TryOpenDocument(saved, Path.Combine(Path.GetTempPath(), "untrusted.spark")));
        Assert.True(opened.IsAwaitingTrust);
        Assert.NotNull(opened.ScriptBanner);
    }

    /// <summary>A graph with no code blocks in it has nothing to decide and runs as it always did.</summary>
    [Fact]
    public void AGraphWithNoScriptsRunsOnOpen()
    {
        using MainWindowViewModel model = new();

        string saved = Assert.IsType<string>(model.TrySaveDocument());

        using MainWindowViewModel opened = new();

        Assert.True(opened.TryOpenDocument(saved, Path.Combine(Path.GetTempPath(), "plain.spark")));
        Assert.False(opened.IsAwaitingTrust);
        Assert.Null(opened.ScriptBanner);
    }

    /// <summary>
    /// Saying yes runs it, and the banner goes. <c>Run once</c> and <c>Always trust</c> are two
    /// decisions, and only the second is recorded — a store that remembered every run would turn a
    /// one-off into a standing permission.
    /// </summary>
    [Fact]
    public void SayingYesRunsIt()
    {
        using MainWindowViewModel model = new();
        model.PlaceCodeBlock(0, 0);

        string saved = Assert.IsType<string>(model.TrySaveDocument());

        using MainWindowViewModel opened = new();
        opened.TryOpenDocument(saved, Path.Combine(Path.GetTempPath(), "untrusted.spark"));

        Assert.True(opened.TrustAndRun(remember: false));
        Assert.False(opened.IsAwaitingTrust);
        Assert.Null(opened.ScriptBanner);
    }

    /// <summary><c>--no-script</c> is parsed, because a switch nobody can type is not a switch.</summary>
    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--no-script" }, true)]
    [InlineData(new[] { "--graph", "curves", "--no-script" }, true)]
    public void TheNoScriptSwitchIsParsed(string[] args, bool expected) =>
        Assert.Equal(expected, StartupOptions.Parse(args).NoScript);
}
