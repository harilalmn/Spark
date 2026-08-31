using System;
using System.IO;
using System.Linq;

namespace Spark.Architecture.Tests;

/// <summary>
/// The licence obligations ADR-0020 took on, checked by a test rather than remembered.
/// </summary>
/// <remarks>
/// <para>
/// <b>R21 is that these are <i>standing</i> obligations rather than a one-off task</b>, and
/// `E13-T16`'s whole framing is that a condition depending on somebody remembering it at release
/// time is a condition that will eventually be missed. So the notice file, the licence texts and
/// the pipeline that ships them are asserted here, where a deletion is a red build.
/// </para>
/// <para>
/// <b>Nothing here is legal advice and this file does not pretend to be a compliance audit.</b>
/// Six questions are with counsel (**Q13**). What these check is that the artefacts the
/// obligations are met *with* exist and say what they are supposed to say.
/// </para>
/// </remarks>
public sealed class LicenceObligationTests
{
    [Fact]
    public void TheThirdPartyNoticesExistAndNameOpenCascade()
    {
        string path = Path.Combine(RepositoryRoot(), "THIRD-PARTY-NOTICES.md");

        Assert.True(File.Exists(path), $"Expected third-party notices at {path}.");

        string text = File.ReadAllText(path);

        Assert.Contains("OpenCascade", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LGPL-2.1", text, StringComparison.Ordinal);
        Assert.Contains("Open CASCADE exception", text, StringComparison.Ordinal);

        // The exception's own wording is what has to appear somewhere a recipient can read.
        Assert.Contains("makes use of", text, StringComparison.OrdinalIgnoreCase);

        // And the honest disclaimer, because this project is not qualified to give the other kind.
        Assert.Contains("legal advice", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The licence texts are in the repository rather than linked. A link is not a text, and the
    /// obligation is to ship the licence.
    /// </summary>
    [Fact]
    public void TheLicenceTextsAreShippedRatherThanLinked()
    {
        string directory = Path.Combine(RepositoryRoot(), "licences");

        Assert.True(Directory.Exists(directory), $"Expected licence texts in {directory}.");

        string lgpl = Path.Combine(directory, "LGPL-2.1.txt");
        Assert.True(File.Exists(lgpl), "The LGPL-2.1 text is missing.");
        Assert.Contains(
            "GNU LESSER GENERAL PUBLIC LICENSE",
            File.ReadAllText(lgpl),
            StringComparison.OrdinalIgnoreCase);

        string exception = Path.Combine(directory, "OpenCascade-exception.txt");
        Assert.True(File.Exists(exception), "The Open CASCADE exception text is missing.");
        Assert.Contains(
            "Open CASCADE exception",
            File.ReadAllText(exception),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>R22: the source offer has to be honourable against a specific artefact.</b> The build
    /// script writes a key beside the binaries recording exactly what they were built from, and
    /// this asserts the script still does it — because the day it stops, nothing else notices.
    /// </summary>
    [Fact]
    public void TheNativeBuildRecordsAKeyTheSourceOfferCanBeHonouredAgainst()
    {
        string script = Path.Combine(RepositoryRoot(), "scripts", "build-native.ps1");

        Assert.True(File.Exists(script), "scripts/build-native.ps1 is missing.");

        string text = File.ReadAllText(script);

        Assert.Contains("spark_occt.buildkey.json", text, StringComparison.Ordinal);

        foreach (string field in (string[])["occtVersion", "vcpkgBaseline", "shimSourceHash", "rid"])
        {
            Assert.Contains(field, text, StringComparison.Ordinal);
        }

        // And the notices travel with the binaries. A notice left behind in a source tree is a
        // notice nobody who received the software can read.
        Assert.Contains("THIRD-PARTY-NOTICES.md", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The LGPL relink obligation forbids sealing the natives into a single file, and forbids
    /// NativeAOT over them. Nothing in the repository may quietly turn either on.
    /// </summary>
    [Fact]
    public void NothingPublishesSingleFileOrNativeAot()
    {
        string[] offenders = Directory
            .EnumerateFiles(RepositoryRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(project => !project.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(project =>
            {
                string text = File.ReadAllText(project);

                return text.Contains("<PublishSingleFile>true", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("<PublishAot>true", StringComparison.OrdinalIgnoreCase);
            })
            .Select(Path.GetFileName)
            .ToArray()!;

        Assert.Empty(offenders);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? here = new(AppContext.BaseDirectory);

        while (here is not null && !File.Exists(Path.Combine(here.FullName, "Spark.slnx")))
        {
            here = here.Parent;
        }

        Assert.NotNull(here);

        return here!.FullName;
    }
}
