using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Spark.Api;
using Spark.Host;

namespace Spark.UI.Tests;

/// <summary>
/// The update check: what it decides, and what it does when it cannot decide (<c>E12-T21</c>).
/// </summary>
/// <remarks>
/// <b>The failure paths matter more than the happy one here.</b> The happy path is visible the
/// first time anybody runs the application against a real release; being offline, being behind a
/// proxy that returns an HTML login page, and being rate-limited are the paths nobody exercises on
/// purpose and every user eventually hits. All three must produce silence, and silence is
/// indistinguishable from "you are up to date" — which is the intended behaviour, not a
/// limitation.
/// </remarks>
public sealed class UpdateCheckTests
{
    private static readonly SparkVersion Running = Parse("0.1.0");

    [Fact]
    public void ANewerReleaseIsAnUpdate()
    {
        UpdateAvailable? update = UpdateCheck.Read(
            """{"tag_name":"v0.2.0","html_url":"https://example.invalid/releases/v0.2.0"}""",
            Running);

        Assert.NotNull(update);
        Assert.Equal("0.2.0", update!.Version.ToString());
        Assert.Equal("https://example.invalid/releases/v0.2.0", update.ReleaseUrl);
    }

    [Fact]
    public void TheSameVersionIsNotAnUpdate()
    {
        Assert.Null(UpdateCheck.Read("""{"tag_name":"v0.1.0"}""", Running));
    }

    /// <summary>
    /// An older release is not an update, which is not as silly as it sounds: a release can be
    /// deleted, and <c>latest</c> then names the one before it while a user is running the one
    /// that went away.
    /// </summary>
    [Fact]
    public void AnOlderReleaseIsNotAnUpdate()
    {
        Assert.Null(UpdateCheck.Read("""{"tag_name":"v0.0.9"}""", Running));
    }

    /// <summary>
    /// <b>A prerelease never prompts.</b> The endpoint already excludes them; this is the check
    /// that holds when the endpoint's behaviour does not.
    /// </summary>
    [Theory]
    [InlineData("""{"tag_name":"v0.2.0","prerelease":true}""")]
    [InlineData("""{"tag_name":"v0.2.0","draft":true}""")]
    public void ADraftOrPrereleaseIsNotAnUpdate(string json)
    {
        Assert.Null(UpdateCheck.Read(json, Running));
    }

    /// <summary>
    /// <b>Anything that is not a release payload is silence, not an exception.</b> The realistic
    /// source of these is a captive portal or a corporate proxy answering with an HTML page and a
    /// 200, which is a case no amount of endpoint correctness prevents.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("<html><body>Sign in to the network</body></html>")]
    [InlineData("[]")]
    [InlineData("""{"message":"API rate limit exceeded"}""")]
    [InlineData("""{"tag_name":"nightly"}""")]
    [InlineData("""{"tag_name":null}""")]
    public void NonsenseIsSilence(string? json)
    {
        Assert.Null(UpdateCheck.Read(json, Running));
    }

    /// <summary>
    /// A release with no <c>html_url</c> still opens somewhere useful rather than nowhere.
    /// </summary>
    [Fact]
    public void AMissingUrlFallsBackToTheReleasesPage()
    {
        UpdateAvailable? update = UpdateCheck.Read("""{"tag_name":"v0.2.0"}""", Running);

        Assert.NotNull(update);
        Assert.Contains("releases", update!.ReleaseUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreachableEndpointIsSilence()
    {
        using HttpClient client = new(new ThrowingHandler(new HttpRequestException("no route to host")));
        using UpdateCheck check = new(client, "https://example.invalid/latest");

        Assert.Null(await check.CheckAsync(Running, CancellationToken.None));
    }

    [Fact]
    public async Task ATimeoutIsSilence()
    {
        using HttpClient client = new(new ThrowingHandler(new TaskCanceledException("timed out")));
        using UpdateCheck check = new(client, "https://example.invalid/latest");

        Assert.Null(await check.CheckAsync(Running, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ARefusalIsSilence(HttpStatusCode status)
    {
        using HttpClient client = new(new CannedHandler(status, """{"tag_name":"v99.0.0"}"""));
        using UpdateCheck check = new(client, "https://example.invalid/latest");

        Assert.Null(await check.CheckAsync(Running, CancellationToken.None));
    }

    [Fact]
    public async Task AGoodAnswerIsRead()
    {
        using HttpClient client = new(new CannedHandler(
            HttpStatusCode.OK,
            """{"tag_name":"v9.9.9","html_url":"https://example.invalid/nine"}"""));
        using UpdateCheck check = new(client, "https://example.invalid/latest");

        UpdateAvailable? update = await check.CheckAsync(Running, CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("9.9.9", update!.Version.ToString());
    }

    /// <summary>
    /// <b>The endpoint is Spark's own repository and is not configurable.</b> A settable update
    /// endpoint is a mechanism for pointing an installation at a build that is not Spark.
    /// </summary>
    [Fact]
    public void TheDefaultEndpointIsSparksOwnRelease()
    {
        Assert.Equal(
            "https://api.github.com/repos/harilalmn/Spark/releases/latest",
            UpdateCheck.DefaultEndpoint);
    }

    /// <summary>The preference is on unless the user has said otherwise, and off is remembered.</summary>
    [Fact]
    public void ThePreferenceDefaultsToOnAndRemembersOff()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "update-check.txt");

        try
        {
            UpdatePreference first = new(path);
            Assert.True(first.Enabled);

            first.Enabled = false;

            Assert.False(new UpdatePreference(path).Enabled);

            first.Enabled = true;

            Assert.True(new UpdatePreference(path).Enabled);
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);

            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Nowhere to write means on and forgotten, rather than off — the safe direction is the one
    /// where a user still hears about a fix.
    /// </summary>
    [Fact]
    public void NowhereToWriteIsStillOn()
    {
        UpdatePreference preference = new((string?)null);

        Assert.True(preference.Enabled);
        Assert.Null(preference.Path);

        preference.Enabled = false;

        Assert.False(preference.Enabled);
        Assert.True(new UpdatePreference((string?)null).Enabled);
    }

    /// <summary>
    /// <b>The badge is hidden until something says otherwise</b>, which is the state every session
    /// that is current, offline or opted out ends up in.
    /// </summary>
    [Fact]
    public void TheBadgeStartsHidden()
    {
        Spark.UI.ViewModels.MainWindowViewModel model = new();

        try
        {
            Assert.False(model.IsUpdateAvailable);
            Assert.Empty(model.UpdateLabel);
            Assert.Empty(model.UpdateUrl);
        }
        finally
        {
            model.Dispose();
        }
    }

    /// <summary><c>--no-update-check</c> is parsed, because it is the switch NFR-13 is answered with.</summary>
    [Fact]
    public void TheSessionSwitchIsParsed()
    {
        Assert.True(Spark.UI.StartupOptions.Parse(["--no-update-check"]).NoUpdateCheck);
        Assert.False(Spark.UI.StartupOptions.Parse([]).NoUpdateCheck);
    }

    private static SparkVersion Parse(string text)
    {
        Assert.True(SparkVersion.TryParse(text, out SparkVersion version));

        return version;
    }

    private sealed class ThrowingHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(failure);
    }

    private sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
