using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Spark.UI;
using Spark.UI.Views;

namespace Spark.UI.Tests;

/// <summary>
/// The application mark and the splash window.
/// </summary>
/// <remarks>
/// The parity test is the point of this file. The mark exists twice — as `assets/spark-icon.svg`
/// for anyone who needs the artwork, and as a drawing in `Theming/SparkLogo.axaml` for the
/// application to draw — and the only thing keeping them the same shape is that Avalonia's
/// geometry syntax is SVG path syntax, so both carry the same strings. Nothing enforces that but
/// this.
/// </remarks>
public sealed class SparkLogoTests
{
    /// <summary>
    /// Every path in the SVG appears verbatim in the drawing the application actually renders.
    /// </summary>
    /// <remarks>
    /// Without this the two drift the first time somebody nudges the artwork, and the failure is
    /// invisible: the SVG is what a designer opens, the AXAML is what ships, and nothing renders
    /// them side by side.
    /// </remarks>
    [Fact]
    public void TheDrawnMarkCarriesTheSvgsPathsVerbatim()
    {
        string root = RepositoryRoot();
        string svg = File.ReadAllText(Path.Combine(root, "assets", "spark-icon.svg"));
        string axaml = File.ReadAllText(Path.Combine(root, "src", "Spark.UI", "Theming", "SparkLogo.axaml"));

        string[] paths = ExtractPathData(svg);

        // Three: the spark and the two control points. A change that adds a fourth without
        // updating this number is a change that has not been checked.
        Assert.Equal(3, paths.Length);

        foreach (string path in paths)
        {
            Assert.Contains(path, axaml, StringComparison.Ordinal);
        }
    }

    /// <summary>The accent is the design language's, not a value invented for the mark.</summary>
    [Fact]
    public void TheMarkIsBuiltFromDesignLanguageTokens()
    {
        string root = RepositoryRoot();
        string svg = File.ReadAllText(Path.Combine(root, "assets", "spark-icon.svg"));

        // §2.5 accent, accent.hover, and §2.2 bg.void. If the palette moves, these move with it.
        Assert.Contains("#A98BFF", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#C0A8FF", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#12151A", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMetadataIsStrippedFromTheVersionOnTheSplash()
    {
        // What MinVer actually produces for an untagged build.
        Assert.Equal(
            "0.0.0-alpha.0.23",
            SplashWindow.DisplayVersion("0.0.0-alpha.0.23+ed6af0787fde3d3ab60862abb48c6e547c101668"));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void AVersionWithoutBuildMetadataIsLeftAlone(string? given, string expected) =>
        Assert.Equal(expected, SplashWindow.DisplayVersion(given));

    /// <summary>
    /// The splash is suppressed for both measurement modes.
    /// </summary>
    /// <remarks>
    /// Not a nicety. A splash in front of `--screenshot` puts a second window in the capture, and
    /// in front of `--canvas-benchmark` it composites against the thing being measured — so it
    /// would bias the number rather than merely decorate it.
    /// </remarks>
    [Fact]
    public void NoSplashDuringAMeasurementRun()
    {
        Assert.False(StartupOptions.Parse(["--canvas-benchmark"]).ShowSplash);
        Assert.False(StartupOptions.Parse(["--screenshot", "out"]).ShowSplash);
    }

    [Fact]
    public void AnOrdinaryStartShowsTheSplash()
    {
        Assert.True(StartupOptions.Default.ShowSplash);
        Assert.True(StartupOptions.Parse(["--graph", "curves"]).ShowSplash);
    }

    /// <summary>The <c>d</c> attribute of every <c>path</c> in the document.</summary>
    /// <param name="svg">The SVG source.</param>
    /// <returns>The path data strings, in document order.</returns>
    /// <remarks>
    /// The lookbehind is load-bearing: without it the pattern also matches the <c>d="</c> at the
    /// end of every <c>id="</c>, which is how this returned ten paths for a file containing three.
    /// </remarks>
    private static string[] ExtractPathData(string svg) =>
        Regex.Matches(svg, @"(?<=\s)d=""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToArray();

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
