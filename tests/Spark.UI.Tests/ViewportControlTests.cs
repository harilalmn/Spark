using System.Collections.Generic;
using Spark.Api.Help;
using Spark.Engine;
using Spark.UI.Controls;
using Spark.UI.ViewModels;
using Spark.UI.Views;

namespace Spark.UI.Tests;

/// <summary>
/// Which backend is presenting the viewport, which is a question with two wrong answers and one
/// of them is silent. See <c>N64</c>.
/// </summary>
public sealed class ViewportControlTests
{
    /// <summary>
    /// <b>The regression test for N64.</b> A control that has not yet heard from OpenGL must not
    /// claim the software renderer is presenting. Avalonia paints before <c>OnOpenGlInit</c>
    /// fires, so this state is reached on every launch on every machine — and while it held the
    /// opposite answer, a <c>--screenshot</c> on a healthy GPU wrote a CPU-rendered image and
    /// printed the GL driver string underneath it.
    /// </summary>
    [Fact]
    public void ANewControlDoesNotClaimSoftwareIsPresentingBeforeGlHasBeenHeardFrom()
    {
        HeadlessSession.Run(() =>
        {
            ViewportControl viewport = new();

            Assert.False(viewport.IsSoftwarePresenting);
        });
    }

    /// <summary>The switch commits immediately; there is nothing to wait for once it is set.</summary>
    [Fact]
    public void ForcingTheSoftwareRendererCommitsToItAtOnce()
    {
        HeadlessSession.Run(() =>
        {
            ViewportControl viewport = new() { ForceSoftwareRenderer = true };

            Assert.True(viewport.IsSoftwarePresenting);
        });
    }

    /// <summary>
    /// The camera is the control's own and survives being read back, which is what the fallback's
    /// frame signature depends on to decide whether to re-rasterise.
    /// </summary>
    [Fact]
    public void TheCameraIsReachableAndFramesAnEmptySceneWithoutThrowing()
    {
        HeadlessSession.Run(() =>
        {
            ViewportControl viewport = new();

            viewport.ZoomToFit();

            Assert.NotNull(viewport.Camera);
        });
    }
}

/// <summary>
/// The help window and the help library the shell builds (<c>E10-T13</c>).
/// </summary>
public sealed class HelpWindowTests
{
    /// <summary>
    /// The library the shell hands the help window contains both sources: the hand-written concept
    /// topics and a generated page for every node currently loaded.
    /// </summary>
    [Fact]
    public void TheSessionHelpLibraryHoldsConceptTopicsAndAPageForEveryNode()
    {
        HeadlessSession.Run(() =>
        {
            MainWindowViewModel model = new();
            HelpLibrary help = model.Help();

            Assert.True(help.Count > 100, $"expected the node pages and the concepts, got {help.Count}");
            Assert.NotNull(help.TryGet("concepts.lacing", out HelpDocument? lacing) ? lacing : null);
            Assert.NotNull(help.TryGet("nodes.index", out HelpDocument? index) ? index : null);
        });
    }

    /// <summary>
    /// <b>Every loaded node has a page.</b> This is what makes F1 answer rather than apologise, and
    /// it holds by construction rather than by anybody remembering to write one.
    /// </summary>
    [Fact]
    public void EveryLoadedNodeResolvesToAHelpTopic()
    {
        HeadlessSession.Run(() =>
        {
            MainWindowViewModel model = new();
            HelpLibrary help = model.Help();

            List<string> missing = [];
            foreach (LibraryEntryViewModel entry in model.AllLibraryEntries)
            {
                if (help.ForNode(entry.Key) is null)
                {
                    missing.Add(entry.Key);
                }
            }

            Assert.True(missing.Count == 0, "Nodes with no help topic: " + string.Join(", ", missing));
        });
    }

    /// <summary>
    /// <b>Ports carry the descriptions their authors wrote.</b> Until 2026-08-31
    /// <c>XmlDocumentation</c> read only <c>&lt;summary&gt;</c>, so every generated reference page
    /// had a full column of port names and types beside an empty Description column - while the
    /// text sat in the source, where CS1591 had made it mandatory. This asserts the text arrives.
    /// </summary>
    [Fact]
    public void PortsCarryTheDescriptionsFromTheirXmlDocComments()
    {
        HeadlessSession.Run(() =>
        {
            MainWindowViewModel model = new();

            int ports = 0;
            int described = 0;
            foreach (LibraryEntryViewModel entry in model.AllLibraryEntries)
            {
                foreach (PortDefinition port in entry.Definition.Inputs)
                {
                    ports++;
                    if (!string.IsNullOrWhiteSpace(port.Description))
                    {
                        described++;
                    }
                }
            }

            Assert.True(ports > 100, $"expected the core node library, saw {ports} input ports");
            Assert.True(
                described > ports * 9 / 10,
                $"only {described} of {ports} input ports carry a description; <param> text is not "
                + "reaching PortDefinition.");
        });
    }

    /// <summary>The window opens, lists topics, and shows the one it is sent to.</summary>
    [Fact]
    public void TheWindowShowsTheTopicItIsNavigatedTo()
    {
        HeadlessSession.Run(() =>
        {
            MainWindowViewModel model = new();
            HelpWindow window = new(model.Help());

            Assert.True(window.VisibleEntryCount > 0);

            window.Navigate("concepts.lacing");

            Assert.Equal("concepts.lacing", window.CurrentTopicId);
        });
    }

    /// <summary>
    /// F1 on a node lands on that node's page. The library is asked by node key, which is exactly
    /// what the shell passes it.
    /// </summary>
    [Fact]
    public void NavigatingByNodeKeyLandsOnThatNodesPage()
    {
        HeadlessSession.Run(() =>
        {
            MainWindowViewModel model = new();
            HelpWindow window = new(model.Help());
            string key = model.AllLibraryEntries[0].Key;

            window.NavigateToNode(key);

            Assert.NotNull(window.CurrentTopicId);
            Assert.NotEqual("nodes.index", window.CurrentTopicId);
        });
    }

    /// <summary>
    /// Both kinds of link work. Hand-written topics link by relative path so they read on GitHub;
    /// generated pages link by topic id because they have no file. A reader clicking either lands
    /// on the page.
    /// </summary>
    [Theory]
    [InlineData("concepts.lacing")]
    [InlineData("lacing.md")]
    [InlineData("concepts/lacing.md")]
    public void BothRelativePathsAndTopicIdsResolve(string target)
    {
        HeadlessSession.Run(() =>
        {
            MainWindowViewModel model = new();
            HelpWindow window = new(model.Help());

            window.Navigate(target);

            Assert.Equal("concepts.lacing", window.CurrentTopicId);
        });
    }

    /// <summary>Every diagnostic code the engine can raise has a page in the session library.</summary>
    [Fact]
    public void EveryDiagnosticCodeHasAPageInTheSessionLibrary()
    {
        HeadlessSession.Run(() =>
        {
            MainWindowViewModel model = new();
            HelpLibrary help = model.Help();

            List<string> missing = [];
            foreach (string code in DiagnosticCodes.All)
            {
                if (!help.TryGet(DiagnosticReference.TopicIdFor(code), out _))
                {
                    missing.Add(code);
                }
            }

            Assert.True(missing.Count == 0, "Codes with no page: " + string.Join(", ", missing));
        });
    }

    /// <summary>An unknown node falls back to the index rather than to an empty window.</summary>
    [Fact]
    public void AnUnknownNodeFallsBackToTheIndex()
    {
        HeadlessSession.Run(() =>
        {
            MainWindowViewModel model = new();
            HelpWindow window = new(model.Help());

            window.NavigateToNode("Nobody/Nothing.AtAll");

            Assert.Equal("nodes.index", window.CurrentTopicId);
        });
    }
}
