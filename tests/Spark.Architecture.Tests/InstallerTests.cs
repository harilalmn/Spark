using System;
using System.IO;

namespace Spark.Architecture.Tests;

/// <summary>
/// The two facts about the installer that a future edit can silently destroy (<c>E13-T17</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Neither of these fails at build time, at install time, or on the machine that makes the
/// mistake.</b> They fail on a user's machine, one release later, and they fail quietly — which is
/// the exact shape of defect a test is worth the most against.
/// </para>
/// <para>
/// This project reads files rather than referencing anything, which is what
/// <see cref="ReferenceGraphTests"/> does and for the same reason: a test that referenced what it
/// inspects would be part of what it is inspecting.
/// </para>
/// </remarks>
public sealed class InstallerTests
{
    /// <summary>
    /// The GUID Windows knows Spark by. Written out here, a second time, on purpose.
    /// </summary>
    /// <remarks>
    /// <b>This is a deliberate second copy, and it is the only place in this repository where that
    /// is the right answer.</b> The point of the constant is to make the value *hard to change* —
    /// so it has to be recorded somewhere that a change to `spark.iss` does not also change.
    /// Sharing one source between the script and the check would let a single careless edit move
    /// both and leave the test green, which is precisely the failure being guarded against.
    /// </remarks>
    private const string AppId = "{64C33818-D99E-4FA6-81EB-26615288D8CB}";

    /// <summary>
    /// <b>The <c>AppId</c> is Spark's identity to Windows and may never change.</b>
    /// </summary>
    /// <remarks>
    /// Change it and every installed copy becomes a different product: the new installer finds no
    /// previous version, uninstalls nothing, and leaves two Sparks in Add/Remove Programs and two
    /// Start-menu entries. The build is green, the installer runs, and the damage lands on users
    /// who upgrade — the only people who would notice are the ones already affected.
    /// </remarks>
    [Fact]
    public void TheInstallerAppIdIsUnchanged()
    {
        string script = Script();

        Assert.Contains(
            "AppId={" + AppId,
            script,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The installer stays per-user and unelevated</b> while nothing is signed.
    /// </summary>
    /// <remarks>
    /// An unsigned installer that also demands administrator is the worst combination to hand
    /// SmartScreen, and Spark needs nothing outside its own folder, so machine-wide would buy the
    /// user nothing for that cost. This is a decision worth revisiting the day there is a
    /// certificate — and worth failing a build over until then, because "just make it install for
    /// everyone" is a one-line change that nobody would think to question.
    /// </remarks>
    [Fact]
    public void TheInstallerAsksForNoPrivilegesItDoesNotNeed()
    {
        string script = Script();

        Assert.Contains("PrivilegesRequired=lowest", script, StringComparison.Ordinal);
        Assert.Contains("DefaultDirName={localappdata}", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The installer chains the runtime the application actually declares.
    /// </summary>
    /// <remarks>
    /// Spark is Avalonia rather than WPF, so it needs <c>Microsoft.NETCore.App</c> and not
    /// <c>Microsoft.WindowsDesktop.App</c> — checked against
    /// <c>Spark.Desktop.runtimeconfig.json</c>, which names it outright, rather than inferred from
    /// the fact that this is a desktop application. Chaining the desktop runtime would work and
    /// would be a needlessly larger download; chaining the wrong major version would produce an
    /// installer that succeeds and an application that will not start.
    /// </remarks>
    [Fact]
    public void TheInstallerChainsTheRuntimeTheApplicationDeclares()
    {
        string script = Script();

        // The directory the probe looks in, and the installer it downloads. Asserted on the code
        // rather than on the whole file, which also talks about the desktop runtime in order to
        // explain why it is not the one wanted.
        Assert.Contains(@"shared\Microsoft.NETCore.App", script, StringComparison.Ordinal);
        Assert.Contains("dotnet-runtime-win-x64.exe", script, StringComparison.Ordinal);
        Assert.DoesNotContain("windowsdesktop-runtime", script, StringComparison.Ordinal);

        // The framework the project targets, read from the project rather than assumed. The two
        // move together or the installer is wrong.
        string project = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Directory.Build.props"));

        Assert.Contains("net10.0", project, StringComparison.Ordinal);
        Assert.Contains("#define DotNetMajor \"10\"", script, StringComparison.Ordinal);
    }

    private static string Script()
    {
        string path = Path.Combine(RepositoryRoot(), "installer", "spark.iss");

        Assert.True(File.Exists(path), $"Expected the installer script at {path}.");

        return File.ReadAllText(path);
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
