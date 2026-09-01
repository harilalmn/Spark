using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Spark.Api;

namespace Spark.Host;

/// <summary>
/// A newer release than the one running, and where to read about it.
/// </summary>
/// <param name="Version">The released version.</param>
/// <param name="ReleaseUrl">The release's page, which is what the user is sent to.</param>
public sealed record UpdateAvailable(SparkVersion Version, string ReleaseUrl);

/// <summary>
/// Asks GitHub whether there is a newer release than the one running (<c>E12-T21</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only outbound request Spark makes on its own behalf, and NFR-13 — "no telemetry
/// of any kind in v1" — had to be answered rather than stepped around.</b> It is not telemetry:
/// nothing is collected, nothing is sent, and the request body is empty. What it is honestly is an
/// HTTP GET, and an HTTP GET reveals an IP address and a time to whoever serves it. So it is
/// documented ([D22](../../docs/PRD.md#13-decision-log)), it is switchable off and stays off, and
/// <c>--no-update-check</c> refuses it for a session. Pretending a request is nothing because its
/// body is empty would be the dishonest version of complying with NFR-13.
/// </para>
/// <para>
/// <b>Every failure is silence.</b> No dialog, no banner, no entry in the diagnostics pane. A user
/// on a plane, behind a corporate proxy, or on a machine with no route to github.com has not done
/// anything wrong and does not need to be told about a background request they never asked for.
/// The method returns null and the shell shows nothing, which is the same thing it shows when the
/// build is current.
/// </para>
/// <para>
/// <b><c>/releases/latest</c> rather than <c>/releases</c>, and that endpoint choice is the
/// prerelease policy.</b> GitHub excludes drafts and prereleases from it, so a beta published from
/// a hyphenated tag never prompts anybody running a stable build. The fields are re-checked here
/// anyway, because a policy that depends on somebody else's endpoint semantics should still hold
/// when those change.
/// </para>
/// <para>
/// <b>An unauthenticated GitHub request is rate-limited to 60 an hour per IP</b>, which is
/// generous for something asked once per session and is a reason not to poll. There is no token
/// and there must not be one: this endpoint is public, and shipping a credential to read public
/// data is how credentials leak.
/// </para>
/// </remarks>
public sealed class UpdateCheck : IDisposable
{
    /// <summary>Where the check asks, when nothing else is said.</summary>
    /// <remarks>
    /// Hard-coded to Spark's own repository on purpose. A configurable update endpoint is a
    /// mechanism for pointing somebody's installation at a build that is not Spark, and this
    /// application has no need for one.
    /// </remarks>
    public const string DefaultEndpoint = "https://api.github.com/repos/harilalmn/Spark/releases/latest";

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly string _endpoint;

    /// <summary>Creates a check against the default endpoint.</summary>
    public UpdateCheck()
        : this(null, null)
    {
    }

    /// <summary>Creates a check, optionally over a supplied client.</summary>
    /// <param name="client">
    /// The client to use, or null to make one. Supplying one is how a test answers without a
    /// network, and it is the only reason this parameter exists.
    /// </param>
    /// <param name="endpoint">The endpoint to ask, or null for <see cref="DefaultEndpoint"/>.</param>
    public UpdateCheck(HttpClient? client, string? endpoint)
    {
        _endpoint = endpoint ?? DefaultEndpoint;
        _ownsClient = client is null;

        _client = client ?? new HttpClient
        {
            // Short, and deliberately shorter than a user would notice. This runs while the shell
            // is starting; a check that held anything up would be worse than no check.
            Timeout = TimeSpan.FromSeconds(8),
        };

        // GitHub refuses a request with no User-Agent, with a 403 that names nothing useful. The
        // product and version are what its documentation asks for and are already public.
        if (_client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(
                    ProductNotice.ProductName,
                    SparkVersion.Of(typeof(UpdateCheck).Assembly)?.ToString() ?? "0.0.0"));
        }

        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <summary>
    /// Asks whether a released version is newer than <paramref name="current"/>.
    /// </summary>
    /// <param name="current">The running version.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The newer release, or null — which means current, unreachable, unparseable, or refused, and
    /// deliberately does not distinguish them to the caller.
    /// </returns>
    public async Task<UpdateAvailable?> CheckAsync(
        SparkVersion current, CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response =
                await _client.GetAsync(_endpoint, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return Read(json, current);
        }
        catch (Exception failure) when (IsExpected(failure))
        {
            // Offline, blocked, timed out, or handed something that is not JSON. All of these are
            // ordinary and none of them is the user's problem. See the remarks on this type.
            return null;
        }
    }

    /// <summary>
    /// Reads a GitHub release payload and decides whether it is newer.
    /// </summary>
    /// <param name="json">The response body.</param>
    /// <param name="current">The running version.</param>
    /// <returns>The newer release, or null.</returns>
    /// <remarks>
    /// Separated from the request so that the decision — which is where a wrong answer would be
    /// invisible — is testable without a network or a stubbed handler.
    /// </remarks>
    public static UpdateAvailable? Read(string? json, SparkVersion current)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Belt and braces over the endpoint's own promise. See the remarks on this type.
            if (Flag(root, "draft") || Flag(root, "prerelease"))
            {
                return null;
            }

            if (!root.TryGetProperty("tag_name", out JsonElement tag)
                || tag.ValueKind != JsonValueKind.String
                || !SparkVersion.TryParse(tag.GetString(), out SparkVersion released))
            {
                return null;
            }

            if (!released.IsNewerThan(current))
            {
                return null;
            }

            string url = root.TryGetProperty("html_url", out JsonElement page)
                && page.ValueKind == JsonValueKind.String
                && page.GetString() is { Length: > 0 } address
                    ? address
                    : "https://github.com/harilalmn/Spark/releases/latest";

            return new UpdateAvailable(released, url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// The failures that mean "no answer" rather than "something is wrong with Spark".
    /// </summary>
    /// <remarks>
    /// Listed rather than caught as <see cref="Exception"/>, so that a genuine defect in this
    /// class — a null reference, a bad cast — still reaches a crash report instead of being
    /// swallowed by the code whose whole job is to fail quietly.
    /// </remarks>
    private static bool IsExpected(Exception failure) =>
        failure is HttpRequestException
            or TaskCanceledException
            or OperationCanceledException
            or JsonException
            or UriFormatException
            or InvalidOperationException;
}
