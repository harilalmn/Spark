using Spark.UI;

namespace Spark.UI.Tests;

/// <summary>
/// What the command line asks the window to do before anybody looks at it.
/// </summary>
/// <remarks>
/// <b>These are facts about the switches, not about the window</b>, which is why they live on
/// <see cref="StartupOptions"/> and can be asserted without building one. A <c>MainWindow</c> claim
/// is testable today only by reading its source as text, and a source scan is a weaker test of a
/// stronger claim: it proves a line was written, not that it decides the right thing.
/// </remarks>
public sealed class StartupOptionsTests
{
    /// <summary>
    /// <b>`E8-T55`: a person's window opens maximised.</b> Asked for by the client — a graph editor
    /// is a whole-screen application, and the declared 1480×900 is a window somebody resizes before
    /// they do anything else on a large display.
    /// </summary>
    [Fact]
    public void APersonsWindowOpensMaximised() =>
        Assert.True(StartupOptions.Parse([]).OpensMaximised);

    /// <summary>
    /// <b>And the two automated paths do not, because both exist to be compared across runs.</b>
    /// A screenshot that opened maximised would be a different size on every machine, so the
    /// documentation images would change size with the display that took them; the canvas
    /// benchmark reports per frame drawn, and the frame would stop being the same frame.
    /// </summary>
    /// <remarks>
    /// The benchmark switch is <c>--canvas-benchmark</c>, and the first draft of this test wrote
    /// <c>--benchmark</c> — which parses to nothing at all and left the option at its default. The
    /// test caught it, which is the argument for asserting against the real parser rather than
    /// constructing an options object by hand.
    /// </remarks>
    [Theory]
    [InlineData("--screenshot", "out")]
    [InlineData("--canvas-benchmark", "600")]
    public void AnAutomatedWindowKeepsItsDeclaredSize(string switchName, string value) =>
        Assert.False(StartupOptions.Parse([switchName, value]).OpensMaximised);

    /// <summary>
    /// The two are independent of everything else on the command line, so opening a graph or the
    /// help window still maximises.
    /// </summary>
    [Fact]
    public void OtherSwitchesDoNotAffectIt() =>
        Assert.True(StartupOptions.Parse(["--graph", "curves", "--no-update-check"]).OpensMaximised);
}
