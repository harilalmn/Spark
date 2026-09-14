using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Spark.Docs.Verify;

/// <summary>
/// <c>scripts/check-version.ps1</c>, which refuses to release when the built assemblies disagree
/// with the tag being released (<c>E1-T22</c>, <c>E12-T11</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>AGENTS.md calls this "the gate that matters", and until now nothing had ever run it.</b>
/// `release.yml` invokes it before a single byte is uploaded and the release procedure names it as
/// step 6, so it is the last thing between a wrong version and a published installer — and no test
/// in the tree mentioned it. That is the shape found four times in the week this was written: a
/// mechanism everybody relies on and nobody exercises.
/// </para>
/// <para>
/// <b>The failure it guards is specific and is the default.</b> Spark's version comes from MinVer,
/// derived from the nearest git tag ([ADR-0007]), so in principle a tag and its assemblies cannot
/// disagree. In practice a shallow checkout has no tags, MinVer finds none, every assembly is
/// stamped <c>0.0.0-alpha.0</c>, and the workflow publishes them under whatever tag it was given.
/// That release installs, runs, and makes **every bug report from it name a version that does not
/// exist**.
/// </para>
/// <para>
/// <b>The harness's own assembly is the fixture</b>, rather than a staged publish that a developer
/// machine may not have. It is stamped by MinVer like everything else here, so its informational
/// version carries a prerelease suffix *and* the <c>+</c> build metadata — which makes the script's
/// trimming of that metadata a real case rather than a hypothetical one.
/// </para>
/// </remarks>
public sealed class ReleaseVersionGateChecks
{
    /// <summary>The shells to try, in order, and the first one that answers wins.</summary>
    /// <remarks>
    /// Neither is available everywhere and that is the whole reason this is a probe. This machine
    /// has no <c>pwsh</c> — AGENTS.md's release procedure says so in as many words, because the
    /// pack scripts have to be run under Windows PowerShell here — and a Linux runner has no
    /// <c>powershell</c>. Both GitHub images carry <c>pwsh</c>.
    /// </remarks>
    private static readonly string[] Shells = ["pwsh", "powershell"];

    /// <summary>
    /// <b>A PowerShell is on the path.</b> Its own check, so a machine without one gets a sentence
    /// naming what is missing rather than three tests failing inside a process start.
    /// </summary>
    [Fact]
    public void APowerShellIsFound() =>
        Assert.True(
            Shell() is not null,
            "no PowerShell was found on PATH (tried " + string.Join(", ", Shells)
            + "). The release scripts are PowerShell and cannot be verified without one.");

    /// <summary>
    /// <b>The gate refuses a tag the artefact does not carry, and says which is which.</b> This is
    /// the assertion; the passing case below only proves this one is not vacuous.
    /// </summary>
    [Fact]
    public void ATagTheArtefactDoesNotCarryIsRefused()
    {
        (int exit, string output) = CheckVersion("v99.99.99");

        Assert.True(exit != 0, "the gate accepted a tag the assembly does not carry:\n" + output);
        Assert.Contains("Version mismatch", output, StringComparison.Ordinal);
        Assert.Contains("99.99.99", output, StringComparison.Ordinal);
        Assert.Contains(Version(), output, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The shallow-clone version is refused, which is the case the gate exists for.</b> Named
    /// separately from the case above because it is not a hypothetical wrong tag: it is what every
    /// default checkout produces, and it is how `v0.1.1` nearly shipped.
    /// </summary>
    [Fact]
    public void TheVersionAShallowCheckoutProducesIsRefused()
    {
        (int exit, string output) = CheckVersion("v0.0.0-alpha.0");

        Assert.True(exit != 0, "the gate accepted the version a tagless checkout stamps:\n" + output);
        Assert.Contains("fetch-depth: 0", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The matching tag passes, with or without its leading <c>v</c>, and the build metadata is
    /// not part of the comparison.</b> Without this the refusals above would be satisfied by a
    /// script that refused everything.
    /// </summary>
    [Fact]
    public void TheTagTheArtefactCarriesIsAccepted()
    {
        (int exit, string output) = CheckVersion("v" + Version());

        Assert.True(exit == 0, "the gate refused the assembly's own version:\n" + output);
        Assert.Contains("The tag and the artefact agree", output, StringComparison.Ordinal);

        (int bare, string bareOutput) = CheckVersion(Version());

        Assert.True(bare == 0, "the gate refused the assembly's own version without a leading v:\n" + bareOutput);
    }

    /// <summary>
    /// <b>A tag with no version in it is refused rather than treated as empty.</b> <c>v</c> alone
    /// trims to nothing, and an empty expected version compared against an empty actual one would
    /// otherwise be the one string pair that passes by accident.
    /// </summary>
    [Fact]
    public void ATagWithNoVersionInItIsRefused()
    {
        (int exit, string output) = CheckVersion("v");

        Assert.True(exit != 0, "the gate accepted a tag with no version in it:\n" + output);
        Assert.Contains("no version in it", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A missing assembly is refused with the command that produces one.</b> The gate runs after
    /// the staging step, so the way this fails in practice is that staging did not happen — and an
    /// error that names <c>publish.ps1</c> is the difference between a minute and an afternoon.
    /// </summary>
    [Fact]
    public void AMissingAssemblyIsRefusedByName()
    {
        (int exit, string output) = Run("v1.0.0", Path.Combine(Root(), "artifacts", "no-such-assembly.dll"));

        Assert.True(exit != 0, "the gate accepted a path with no assembly at it:\n" + output);
        Assert.Contains("publish.ps1", output, StringComparison.Ordinal);
    }

    /// <summary>Runs the gate against this harness's own assembly.</summary>
    /// <param name="tag">The tag to claim.</param>
    /// <returns>The exit code, and everything the script wrote.</returns>
    private static (int Exit, string Output) CheckVersion(string tag) =>
        Run(tag, Assembly());

    private static (int Exit, string Output) Run(string tag, string assembly)
    {
        string? shell = Shell();
        Assert.NotNull(shell);

        ProcessStartInfo start = new()
        {
            FileName = shell,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = Root(),
        };

        // -NoProfile because a developer's profile is not part of the release path and must not be
        // able to change what this observes; -ExecutionPolicy Bypass because the repository's
        // scripts are not signed and a machine's policy is not what is under test here.
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(Root(), "scripts", "check-version.ps1"));
        start.ArgumentList.Add("-Tag");
        start.ArgumentList.Add(tag);
        start.ArgumentList.Add("-Assembly");
        start.ArgumentList.Add(assembly);

        using Process process = Assert.IsType<Process>(Process.Start(start));

        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(60_000), "the version gate did not finish within a minute.");

        return (process.ExitCode, output);
    }

    /// <summary>This harness's own assembly, which MinVer stamped like every other one here.</summary>
    private static string Assembly()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Spark.Docs.Verify.dll");

        Assert.True(File.Exists(path), $"the harness cannot find its own assembly at {path}.");
        return path;
    }

    /// <summary>Its informational version, with the build metadata trimmed as the gate trims it.</summary>
    private static string Version()
    {
        string? product = FileVersionInfo.GetVersionInfo(Assembly()).ProductVersion;

        Assert.False(
            string.IsNullOrWhiteSpace(product),
            "the harness assembly declares no product version, so MinVer did not stamp it and this "
            + "fixture proves nothing.");

        return product!.Split('+')[0];
    }

    /// <summary>The first shell that answers, or null.</summary>
    private static string? Shell()
    {
        foreach (string candidate in Shells)
        {
            try
            {
                ProcessStartInfo start = new()
                {
                    FileName = candidate,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };

                start.ArgumentList.Add("-NoProfile");
                start.ArgumentList.Add("-Command");
                start.ArgumentList.Add("$PSVersionTable.PSVersion.Major");

                using Process? probe = Process.Start(start);

                if (probe is null)
                {
                    continue;
                }

                string answer = probe.StandardOutput.ReadToEnd();

                if (probe.WaitForExit(30_000) && probe.ExitCode == 0 && answer.Trim().Length > 0)
                {
                    return candidate;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not on PATH. No machine has both, so this is the ordinary case rather than a fault.
            }
        }

        return null;
    }

    private static string Root()
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
