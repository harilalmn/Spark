using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace Spark.Packages;

/// <summary>
/// One package as a feed describes it, before anything is downloaded.
/// </summary>
/// <param name="Identity">The package and version.</param>
/// <param name="Title">What the feed calls it.</param>
/// <param name="Authors">Who published it.</param>
/// <param name="Description">The feed's description.</param>
/// <param name="Downloads">How many times it has been downloaded, or null when the feed does not say.</param>
/// <param name="ProjectUrl">Where to read more, or null.</param>
public sealed record PackageListing(
    PackageIdentity Identity,
    string Title,
    string Authors,
    string Description,
    long? Downloads,
    string? ProjectUrl);

/// <summary>
/// Searching a NuGet feed for Spark packages and installing them (<c>E7-T2</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>NuGet is the registry and this is a client, not a replacement.</b> Search, hosting, auth,
/// SemVer, dependency resolution and private feeds are all somebody else's solved problem; what is
/// built here is the part that is ours — recognising a Spark package, and putting its files where
/// <see cref="PackageLoadContext"/> expects them.
/// </para>
/// <para>
/// <b>Install is download-and-extract, not a restore.</b> A package version's folder is a copy of
/// the <c>.nupkg</c>'s contents, because the load context resolves by file existence in that
/// folder and nothing else. That keeps the layer that decides *what is loaded* independent of the
/// layer that decides *where files came from*, which is what lets a package be installed from a
/// private feed, a local file or a shared folder without the loader knowing.
/// </para>
/// <para>
/// <b>Every network operation takes a cancellation token and none of them is optional.</b> A feed
/// that has gone quiet is the normal failure of this layer, and a user who pressed Install has to
/// be able to change their mind.
/// </para>
/// </remarks>
public sealed class NuGetPackageClient
{
    private readonly SourceRepository _repository;
    private readonly SourceCacheContext _cache = new();

    /// <summary>Creates a client over a feed.</summary>
    /// <param name="source">The feed URL. Defaults to nuget.org.</param>
    public NuGetPackageClient(string? source = null)
    {
        Source = string.IsNullOrWhiteSpace(source) ? DefaultSource : source;
        _repository = Repository.Factory.GetCoreV3(Source);
    }

    /// <summary>The public feed, used when no other is named.</summary>
    public static string DefaultSource => "https://api.nuget.org/v3/index.json";

    /// <summary>The feed this client reads.</summary>
    public string Source { get; }

    /// <summary>
    /// Searches the feed for Spark packages.
    /// </summary>
    /// <param name="query">What the user typed. Empty lists whatever the feed offers first.</param>
    /// <param name="limit">The most results to return.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The listings, most relevant first.</returns>
    /// <remarks>
    /// <b>The <c>spark</c> tag is added to the query rather than filtered afterwards.</b> Filtering
    /// a page of general results would return almost nothing and would get worse as nuget.org
    /// grows; asking the feed to do it is what the tag is for.
    /// </remarks>
    /// <exception cref="SparkPackageException">The feed could not be searched.</exception>
    public async Task<IReadOnlyList<PackageListing>> SearchAsync(
        string? query, int limit = 25, CancellationToken cancellationToken = default)
    {
        string tagged = string.IsNullOrWhiteSpace(query)
            ? "tags:" + SparkPackageManifest.Tag
            : query.Trim() + " tags:" + SparkPackageManifest.Tag;

        try
        {
            PackageSearchResource? search =
                await _repository.GetResourceAsync<PackageSearchResource>(cancellationToken).ConfigureAwait(false);

            if (search is null)
            {
                // A feed that offers no search resource is a real configuration, not a fault: a
                // bare folder source is one. Saying which capability is missing is more use than
                // a null reference from inside the protocol library.
                throw new SparkPackageException(
                    $"The feed at {Source} does not support search. Install by name and version instead.");
            }

            IEnumerable<IPackageSearchMetadata> found = await search.SearchAsync(
                tagged,
                new SearchFilter(includePrerelease: false),
                skip: 0,
                take: Math.Max(1, limit),
                NullLogger.Instance,
                cancellationToken).ConfigureAwait(false);

            return
            [
                .. found.Select(package => new PackageListing(
                    new PackageIdentity(package.Identity.Id, package.Identity.Version.ToNormalizedString()),
                    package.Title ?? package.Identity.Id,
                    package.Authors ?? "unknown",
                    package.Description ?? string.Empty,
                    package.DownloadCount,
                    package.ProjectUrl?.ToString())),
            ];
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new SparkPackageException($"The feed at {Source} could not be searched: {failure.Message}", failure);
        }
    }

    /// <summary>
    /// Downloads a package version and extracts it into the store.
    /// </summary>
    /// <param name="identity">The package and version.</param>
    /// <param name="store">Where to put it.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The manifest of the installed package.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    /// <exception cref="SparkPackageException">
    /// The package does not exist on the feed, could not be downloaded, or is not a Spark package.
    /// </exception>
    /// <remarks>
    /// <b>Extracted to a temporary folder and then moved into place.</b> An interrupted download
    /// that had been writing straight into the final folder would leave a package that looks
    /// installed and is not, and the next run would load half of it.
    /// </remarks>
    public async Task<SparkPackageManifest> InstallAsync(
        PackageIdentity identity, PackageStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        string staging = store.FolderFor(identity) + ".installing";
        string destination = store.FolderFor(identity);

        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);

            using (MemoryStream nupkg = new())
            {
                FindPackageByIdResource? source = await _repository
                    .GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);

                if (source is null)
                {
                    throw new SparkPackageException(
                        $"The feed at {Source} cannot serve package downloads.");
                }

                bool copied = await source.CopyNupkgToStreamAsync(
                    identity.Id,
                    NuGet.Versioning.NuGetVersion.Parse(identity.Version),
                    nupkg,
                    _cache,
                    NullLogger.Instance,
                    cancellationToken).ConfigureAwait(false);

                if (!copied || nupkg.Length == 0)
                {
                    throw new SparkPackageException(
                        $"The feed at {Source} has no '{identity}'.");
                }

                nupkg.Position = 0;
                Extract(nupkg, staging);
            }

            SparkPackageManifest manifest = ReadManifest(staging, identity);

            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);

            return manifest;
        }
        catch (Exception failure) when (failure is not SparkPackageException and not OperationCanceledException)
        {
            throw new SparkPackageException($"'{identity}' could not be installed: {failure.Message}", failure);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch (Exception sweep) when (sweep is IOException or UnauthorizedAccessException)
                {
                    // A staging folder left behind is untidy, never wrong: the next install
                    // replaces it, and IsInstalled ignores it because it is not the real folder.
                }
            }
        }
    }

    /// <summary>
    /// Extracts a <c>.nupkg</c>, which is a zip.
    /// </summary>
    /// <remarks>
    /// <b>Entries that escape the destination are refused.</b> A zip may name
    /// <c>../../something</c>, and an extractor that obeys it writes wherever the archive says —
    /// which is a package install turned into arbitrary file write. The check is on the resolved
    /// full path rather than on the entry name, because <c>a/../../b</c> is the same attack
    /// spelled differently.
    /// </remarks>
    private static void Extract(Stream nupkg, string destination)
    {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        using ZipArchive archive = new(nupkg, ZipArchiveMode.Read, leaveOpen: true);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.Length == 0 && entry.Name.Length == 0)
            {
                continue;
            }

            string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));

            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new SparkPackageException(
                    $"The package contains an entry that would be written outside its own folder: "
                    + $"'{entry.FullName}'. It has not been installed.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static SparkPackageManifest ReadManifest(string folder, PackageIdentity identity)
    {
        string path = Path.Combine(folder, SparkPackageManifest.PathInPackage);

        if (!File.Exists(path))
        {
            throw new SparkPackageException(
                $"'{identity}' is a NuGet package but not a Spark package: it has no "
                + $"{SparkPackageManifest.PathInPackage}. Spark packages carry one, and are tagged "
                + $"'{SparkPackageManifest.Tag}' so they can be found.");
        }

        SparkPackageManifest manifest = SparkPackageManifest.Parse(File.ReadAllText(path));

        if (!manifest.IsReadable)
        {
            throw new SparkPackageException(
                $"'{identity}' was built for a newer Spark: its manifest is schema {manifest.Schema} "
                + $"and this build reads {SparkPackageManifest.CurrentSchema}.");
        }

        return manifest;
    }
}
