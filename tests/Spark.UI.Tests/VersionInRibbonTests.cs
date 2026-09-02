using Spark.Api;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// `E8-T44` — the running version, shown in the ribbon rather than only in the About window.
/// </summary>
/// <remarks>
/// <b>Asked for because the answer was two clicks and a dialog away.</b> "What version am I
/// running?" is the first thing any bug report has to establish, and it lived behind
/// <i>Help → About</i>. These assert the string the ribbon binds to, which is the part that can be
/// wrong; that it is drawn under the mark is a line of XAML and a screenshot.
/// </remarks>
public sealed class VersionInRibbonTests
{
    /// <summary>The label is the assembly's own version, prefixed the way a tag is written.</summary>
    [Fact]
    public void TheLabelIsTheRunningVersion()
    {
        using MainWindowViewModel model = new();

        SparkVersion? version = SparkVersion.Of(typeof(MainWindowViewModel).Assembly);

        Assert.NotNull(version);
        Assert.Equal("v" + version.Value, model.VersionLabel);
    }

    /// <summary>
    /// <b>And it agrees with what the About window says</b>, because two places showing a version
    /// is two places that can disagree — and the day they do, one of them is lying to somebody
    /// filing a bug.
    /// </summary>
    [Fact]
    public void TheLabelAgreesWithTheVersionAboutReports()
    {
        using MainWindowViewModel model = new();

        Assert.EndsWith(
            SparkVersion.Of(typeof(MainWindowViewModel).Assembly)!.Value.ToString(),
            model.VersionLabel,
            System.StringComparison.Ordinal);
    }

    /// <summary>A version there is something to show for is shown.</summary>
    [Fact]
    public void TheLineIsShownWhenThereIsAVersion()
    {
        using MainWindowViewModel model = new();

        Assert.True(model.HasVersion, $"nothing to show: '{model.VersionLabel}'");
    }
}
