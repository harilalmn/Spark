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
using NuGet.Versioning;

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
    /// Downloads a package version, extracts it, and reports what it is — without installing it.
    /// </summary>
    /// <param name="identity">The package and version.</param>
    /// <param name="store">Where it will go, if it is committed.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>
    /// The prepared install. <b>Nothing is installed until <see cref="PendingInstall.Commit"/> is
    /// called</b>, and a caller that never commits must <see cref="PendingInstall.Discard"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    /// <exception cref="SparkPackageException">
    /// The package does not exist on the feed, could not be downloaded, or is not a Spark package.
    /// </exception>
    /// <remarks>
    /// <b>Prepare and commit are separate because the disclosure is shown before the user
    /// agrees</b> (<c>E7-T8</c>). A user cannot weigh a package's licence, its dependencies or
    /// whether it carries native binaries until those have been read out of it, and reading them
    /// means downloading it — so the download happens first and the decision second, with the
    /// files parked somewhere they cannot be loaded from.
    /// </remarks>
    public async Task<PendingInstall> PrepareAsync(
        PackageIdentity identity, PackageStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        string staging = store.FolderFor(identity) + ".installing";

        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);

            await DownloadIntoAsync(identity, staging, cancellationToken).ConfigureAwait(false);

            SparkPackageManifest manifest = ReadManifest(staging, identity);

            IReadOnlyList<PackageIdentity> dependencies =
                await StageDependenciesAsync(staging, cancellationToken).ConfigureAwait(false);

            PackageDisclosure disclosure = PackageInspector.Inspect(staging, identity) with
            {
                // What will actually be installed, resolved and transitive, rather than the direct
                // ids the nuspec happens to name. Agreeing to one package should not silently
                // agree to five, and the only way to say how many is to have resolved them.
                Dependencies = [.. dependencies.Select(dependency => dependency.ToString())],
            };

            return new PendingInstall(identity, staging, store.FolderFor(identity), manifest, disclosure);
        }
        catch (Exception failure)
        {
            // Anything that goes wrong leaves nothing behind: a staging folder that survived a
            // failure is a package that looks half-installed to the next attempt.
            Sweep(staging);

            throw failure is SparkPackageException or OperationCanceledException
                ? failure
                : new SparkPackageException($"'{identity}' could not be installed: {failure.Message}", failure);
        }
    }

    /// <summary>
    /// Downloads and installs a package version, without showing anybody a disclosure first.
    /// </summary>
    /// <param name="identity">The package and version.</param>
    /// <param name="store">Where to put it.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The manifest of the installed package.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    /// <exception cref="SparkPackageException">The package could not be installed.</exception>
    /// <remarks>
    /// For callers with nobody to ask — a command line, a test, a scripted setup. Anything with a
    /// user in front of it should use <see cref="PrepareAsync"/> and show them
    /// <see cref="PendingInstall.Disclosure"/> first.
    /// </remarks>
    public async Task<SparkPackageManifest> InstallAsync(
        PackageIdentity identity, PackageStore store, CancellationToken cancellationToken = default)
    {
        PendingInstall pending = await PrepareAsync(identity, store, cancellationToken).ConfigureAwait(false);

        pending.Commit();
        return pending.Manifest;
    }

    /// <summary>The folder inside a package that holds the dependencies installed with it.</summary>
    /// <remarks>
    /// <b>Inside the package's own folder rather than shared</b>, which is the trade-off this
    /// layer already made: <i>a package version's folder is a copy of what it needs</i>. Two
    /// packages depending on the same library each get their own copy, and in exchange removing a
    /// package removes exactly what it brought, and no package can be broken by another's
    /// uninstall. The dotted name keeps it out of the way of anything the package itself ships.
    /// </remarks>
    public const string DependencyFolder = ".deps";

    /// <summary>The most dependencies one install will pull in before it refuses.</summary>
    /// <remarks>
    /// A ceiling rather than a promise about any particular package. A Spark package is a node
    /// library; one that drags in eighty NuGet packages is either a mistake or something the user
    /// should be told about before it lands on their disk, and either way stopping and saying so
    /// is better than a download that appears to hang.
    /// </remarks>
    public const int DependencyCeiling = 64;

    /// <summary>Downloads one package version and extracts it into a folder.</summary>
    private async Task DownloadIntoAsync(
        PackageIdentity identity, string folder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);

        using MemoryStream nupkg = new();

        FindPackageByIdResource? source = await _repository
            .GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);

        if (source is null)
        {
            throw new SparkPackageException($"The feed at {Source} cannot serve package downloads.");
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
            throw new SparkPackageException($"The feed at {Source} has no '{identity}'.");
        }

        nupkg.Position = 0;
        Extract(nupkg, folder);
    }

    /// <summary>
    /// Walks the staged package's dependencies and stages them beside it (<c>E7-T2</c>).
    /// </summary>
    /// <param name="staging">The root package's staging folder.</param>
    /// <param name="cancellationToken">Cancels the downloads.</param>
    /// <returns>Everything staged, transitively, in the order it was resolved.</returns>
    /// <remarks>
    /// <para>
    /// <b>Breadth-first over the nuspec, because that is what a package declares.</b> Each level's
    /// dependencies are read out of the <c>.nuspec</c> that was just extracted, so the walk sees
    /// exactly what will be on disk rather than what a separate metadata query claims.
    /// </para>
    /// <para>
    /// <b>The lowest version satisfying the range, which is NuGet's own rule.</b> Taking the
    /// highest would mean two installs of the same package on different days quietly getting
    /// different code, which is the behaviour that makes *it works on my machine* possible.
    /// </para>
    /// <para>
    /// <b>A dependency that cannot be resolved is reported, not skipped.</b> The alternative is a
    /// <c>TypeLoadException</c> at first use naming an assembly the user has never heard of, at a
    /// moment when nothing on screen connects it to an install they did last week.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<PackageIdentity>> StageDependenciesAsync(
        string staging, CancellationToken cancellationToken)
    {
        List<PackageIdentity> staged = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> folders = new();
        folders.Enqueue(staging);

        string deps = Path.Combine(staging, DependencyFolder);

        while (folders.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach ((string id, VersionRange range) in PackageInspector.DependenciesIn(folders.Dequeue()))
            {
                if (!seen.Add(id))
                {
                    continue;
                }

                if (staged.Count >= DependencyCeiling)
                {
                    throw new SparkPackageException(
                        $"This package needs more than {DependencyCeiling} other packages. That is "
                        + "more than Spark will install in one step; it has not been installed.");
                }

                PackageIdentity resolved = await ResolveAsync(id, range, cancellationToken)
                    .ConfigureAwait(false);

                string folder = Path.Combine(deps, resolved.FolderName);

                await DownloadIntoAsync(resolved, folder, cancellationToken).ConfigureAwait(false);

                staged.Add(resolved);
                folders.Enqueue(folder);
            }
        }

        return staged;
    }

    /// <summary>Picks the lowest version on the feed that satisfies a range.</summary>
    private async Task<PackageIdentity> ResolveAsync(
        string id, VersionRange range, CancellationToken cancellationToken)
    {
        FindPackageByIdResource? source = await _repository
            .GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);

        if (source is null)
        {
            throw new SparkPackageException($"The feed at {Source} cannot serve package downloads.");
        }

        IEnumerable<NuGetVersion> versions = await source
            .GetAllVersionsAsync(id, _cache, NullLogger.Instance, cancellationToken).ConfigureAwait(false);

        NuGetVersion? best = range.FindBestMatch(versions.Where(version => !version.IsPrerelease));

        if (best is null)
        {
            throw new SparkPackageException(
                $"This package depends on '{id}' {range.PrettyPrint()}, and the feed at {Source} has "
                + "no version that satisfies it. Nothing has been installed.");
        }

        return new PackageIdentity(id, best.ToNormalizedString());
    }

    private static void Sweep(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Untidy, never wrong: the next attempt replaces it, and IsInstalled ignores it
            // because it is not the folder a package is loaded from.
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
