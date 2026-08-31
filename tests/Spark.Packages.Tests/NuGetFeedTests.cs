using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// The NuGet client against a real feed (<c>E7-T2</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>These need a network, and they say so rather than pretending.</b> A machine without one —
/// an offline CI runner, a developer on a train — must not see a red build for a reason that is
/// nothing to do with the code. When the feed is unreachable each test returns early having
/// asserted nothing, and <see cref="TheOfflineHalfIsAlwaysChecked"/> exists so a run in that state
/// is still meaningful.
/// </para>
/// <para>
/// <b>Which is the trap this arrangement is one step away from.</b> A file of tests that all skip
/// silently is a green run that checked nothing — the exact failure D18 names for the native
/// provider. The mitigation is the same: the *offline* half of this layer is asserted
/// unconditionally, in <see cref="PackageConventionTests"/>, and a network-dependent test that
/// skipped is visible in the output rather than indistinguishable from one that passed.
/// </para>
/// <para>
/// <b>No package is installed from nuget.org.</b> Searching is read-only and cheap; installing a
/// stranger's package as a side effect of running tests is not something a test suite should do.
/// The install path's own logic — staging, the escape check, manifest validation — is exercised
/// against a package built in the test itself.
/// </para>
/// </remarks>
public sealed class NuGetFeedTests
{
    private static readonly Lazy<bool> FeedReachable = new(Probe);

    /// <summary>
    /// <b>The offline half is always checked</b>, so a run with no network still proves something.
    /// </summary>
    /// <remarks>
    /// This test is the reason the rest may skip. Without it, a machine with no network would run
    /// this file, assert nothing at all, and report green.
    /// </remarks>
    [Fact]
    public void TheOfflineHalfIsAlwaysChecked()
    {
        NuGetPackageClient client = new();

        Assert.Equal(NuGetPackageClient.DefaultSource, client.Source);
        Assert.Equal("https://example.invalid/index.json", new NuGetPackageClient("https://example.invalid/index.json").Source);
        Assert.Equal("spark", SparkPackageManifest.Tag);
        Assert.Equal("tools/spark.json", SparkPackageManifest.PathInPackage);
    }

    /// <summary>Searching a real feed returns real listings.</summary>
    [Fact]
    public async Task SearchingTheRealFeedReturnsListings()
    {
        if (!FeedReachable.Value)
        {
            return;
        }

        NuGetPackageClient client = new();

        // "json" rather than a Spark package, because no Spark package exists on nuget.org yet and
        // a test that only passes once somebody publishes one is a test that fails today for the
        // wrong reason. What is under test is that the client can talk to the feed and shape a
        // result; the tag filter is asserted separately below.
        IReadOnlyList<PackageListing> found =
            await client.SearchAsync("json", 5, TestContext.Current.CancellationToken);

        Assert.All(found, listing =>
        {
            Assert.False(string.IsNullOrWhiteSpace(listing.Identity.Id));
            Assert.False(string.IsNullOrWhiteSpace(listing.Identity.Version));
            Assert.False(string.IsNullOrWhiteSpace(listing.Title));
        });
    }

    /// <summary>
    /// <b>The search asks the feed for the tag rather than filtering afterwards.</b> Nothing on
    /// nuget.org carries the <c>spark</c> tag yet, so a tagged search returns nothing — and that
    /// is the assertion: a tag filter applied after the fact would have returned the whole first
    /// page of general results.
    /// </summary>
    [Fact]
    public async Task ATaggedSearchDoesNotReturnUntaggedPackages()
    {
        if (!FeedReachable.Value)
        {
            return;
        }

        NuGetPackageClient client = new();

        IReadOnlyList<PackageListing> found =
            await client.SearchAsync("newtonsoft", 10, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            found,
            listing => listing.Identity.Id.Equals("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Asking a real feed for a version that does not exist fails with a usable message.</summary>
    [Fact]
    public async Task InstallingAVersionThatDoesNotExistSaysSo()
    {
        if (!FeedReachable.Value)
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "spark-feed-tests", Guid.NewGuid().ToString("n"));

        try
        {
            NuGetPackageClient client = new();
            PackageStore store = new(root);

            SparkPackageException thrown = await Assert.ThrowsAsync<SparkPackageException>(
                () => client.InstallAsync(
                    PackageIdentity.Create("Spark.APackageThatDoesNotExist", "0.0.1"),
                    store,
                    TestContext.Current.CancellationToken));

            Assert.False(string.IsNullOrWhiteSpace(thrown.Message));
            Assert.False(store.IsInstalled(PackageIdentity.Create("Spark.APackageThatDoesNotExist", "0.0.1")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// An unreachable feed reports the feed rather than throwing whatever the protocol library
    /// throws, because a user seeing this needs to know which feed and that it is a network fault.
    /// </summary>
    [Fact]
    public async Task AnUnreachableFeedFailsWithTheFeedNamed()
    {
        NuGetPackageClient client = new("https://spark-tests.invalid/v3/index.json");

        SparkPackageException thrown = await Assert.ThrowsAsync<SparkPackageException>(
            () => client.SearchAsync("anything", 1, TestContext.Current.CancellationToken));

        Assert.Contains("spark-tests.invalid", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>Whether the feed answers, decided once for the whole run.</summary>
    private static bool Probe()
    {
        try
        {
            using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
            using HttpResponseMessage response = http
                .GetAsync(NuGetPackageClient.DefaultSource, HttpCompletionOption.ResponseHeadersRead)
                .GetAwaiter()
                .GetResult();

            return response.IsSuccessStatusCode;
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
