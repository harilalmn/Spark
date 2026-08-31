using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;

namespace Spark.Engine.Tests;

/// <summary>
/// The About text (<c>E12-T18</c>), and the licence obligation inside it (<c>E13-T16</c>,
/// <c>R21</c>).
/// </summary>
/// <remarks>
/// <b>These assert an obligation, not a preference.</b> The Open CASCADE exception requires
/// prominent notice that the work uses facilities provided by the Open CASCADE Technology
/// software. A notice that quietly stops appearing is not a cosmetic regression, and the only way
/// it stays true is if something fails when it is not. <b>Nothing here is legal advice</b> — the
/// six questions with counsel are <c>Q13</c>.
/// </remarks>
public sealed class ProductNoticeTests
{
    /// <summary>
    /// <b>When a kernel is loaded, the notice names Open CASCADE.</b> This is the obligation
    /// stated as a check.
    /// </summary>
    [Fact]
    public void AKernelBearingBuildNamesOpenCascade()
    {
        string text = ProductNotice.ToText("1.2.3", "OpenCascade 8.0.1");

        Assert.Contains("Open CASCADE Technology", text, StringComparison.Ordinal);
        Assert.Contains("LGPL-2.1", text, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES.md", text, StringComparison.Ordinal);
        Assert.Contains("OpenCascade 8.0.1", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// It also states that the libraries are dynamically linked and replaceable, which is the
    /// substantive half of the exception rather than the acknowledgement half.
    /// </summary>
    [Fact]
    public void TheNoticeStatesDynamicLinkingAndReplaceability()
    {
        string text = ProductNotice.ToText("1.2.3", "OpenCascade 8.0.1");

        Assert.Contains("linked dynamically", text, StringComparison.Ordinal);
        Assert.Contains("replaceable", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A build with no kernel says so plainly, and does <b>not</b> carry the Open CASCADE notice —
    /// claiming to link something absent would be its own kind of wrong.
    /// </summary>
    [Fact]
    public void AKernellessBuildSaysSoAndDoesNotClaimOpenCascade()
    {
        string text = ProductNotice.ToText("1.2.3", null);

        Assert.Contains("No solid-modelling kernel is installed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Open CASCADE Technology", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The kernel-less notice leads with what still works. A build without the provider is not a
    /// broken Spark, and a notice that opened with the absence would read as an error message.
    /// </summary>
    [Fact]
    public void TheKernellessNoticeSaysWhatStillWorks()
    {
        Assert.Contains("curves, surfaces, meshes", ProductNotice.NoKernelNotice, StringComparison.Ordinal);
        Assert.Contains("every file format work", ProductNotice.NoKernelNotice, StringComparison.Ordinal);
    }

    /// <summary>Spark's own licence is stated whichever build this is.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("OpenCascade 8.0.1")]
    public void SparksOwnLicenceIsAlwaysStated(string? kernel)
    {
        Assert.Contains("MIT", ProductNotice.ToText("1.2.3", kernel), StringComparison.Ordinal);
    }

    /// <summary>Every line carries both a label and text, so nothing renders as a blank row.</summary>
    [Fact]
    public void EveryLineHasALabelAndText()
    {
        IReadOnlyList<NoticeLine> lines = ProductNotice.Build("1.2.3", "OpenCascade 8.0.1");

        Assert.NotEmpty(lines);
        Assert.All(lines, line =>
        {
            Assert.False(string.IsNullOrWhiteSpace(line.Label));
            Assert.False(string.IsNullOrWhiteSpace(line.Text));
        });
    }

    /// <summary>A missing version omits the line rather than printing an empty one.</summary>
    [Fact]
    public void AMissingVersionOmitsItsLine()
    {
        Assert.DoesNotContain(
            ProductNotice.Build(null, null),
            line => string.Equals(line.Label, "Version", StringComparison.Ordinal));
    }

    /// <summary>
    /// The default <see cref="IBrepKernel.Description"/> falls back to the name, so a provider
    /// compiled against the older contract still describes itself rather than throwing.
    /// </summary>
    [Fact]
    public void AProviderThatDoesNotOverrideDescriptionFallsBackToItsName()
    {
        // Reached through the interface deliberately: a default interface member is not visible
        // on the concrete type, which is exactly what makes it additive for a provider compiled
        // against the older contract.
        IBrepKernel kernel = UnavailableBrepKernel.Instance;

        Assert.Equal("none", kernel.Description);
    }
}
