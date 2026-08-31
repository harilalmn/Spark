using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spark.Packages;

/// <summary>
/// Where installed packages live on disk, and what is in them.
/// </summary>
/// <remarks>
/// <para>
/// <b>One folder per package version</b>, named the way NuGet names them, because that is exactly
/// the unit <see cref="PackageLoadContext"/> isolates. Two versions of one package are two folders
/// and two contexts, and nothing has to reconcile them.
/// </para>
/// <para>
/// <b>The store is a cache, not a database.</b> Everything it knows is derivable from the
/// filesystem: what is installed is what is on disk, and an index would be a second copy that can
/// disagree with it. Deleting a folder uninstalls a package, which is the behaviour a user
/// expects from a folder and the one a support conversation can rely on.
/// </para>
/// </remarks>
public sealed class PackageStore
{
    /// <summary>Creates a store over a directory.</summary>
    /// <param name="root">Where packages are installed. Created on demand.</param>
    /// <exception cref="ArgumentException"><paramref name="root"/> is null or blank.</exception>
    public PackageStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        Root = Path.GetFullPath(root);
    }

    /// <summary>The directory packages are installed under.</summary>
    public string Root { get; }

    /// <summary>The default store, beside the user's other Spark settings.</summary>
    /// <returns>A store under the local application data folder.</returns>
    public static PackageStore Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Spark",
        "packages"));

    /// <summary>Where one package version's files live, whether or not it is installed.</summary>
    /// <param name="identity">The package and version.</param>
    /// <returns>The folder path.</returns>
    public string FolderFor(PackageIdentity identity) => Path.Combine(Root, identity.FolderName);

    /// <summary>Whether a package version is installed.</summary>
    /// <param name="identity">The package and version.</param>
    /// <returns>True when its folder exists and holds a manifest.</returns>
    /// <remarks>
    /// A folder without a manifest is a half-finished install, and is treated as absent so the next
    /// attempt replaces it rather than loading whatever survived an interrupted extract.
    /// </remarks>
    public bool IsInstalled(PackageIdentity identity) =>
        File.Exists(Path.Combine(FolderFor(identity), SparkPackageManifest.PathInPackage));

    /// <summary>Every package version installed, in a stable order.</summary>
    /// <returns>The identities, ordered by id then version.</returns>
    public IReadOnlyList<PackageIdentity> Installed()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        List<PackageIdentity> found = [];

        foreach (string folder in Directory.EnumerateDirectories(Root))
        {
            // The folder name is id.version, and an id contains dots too, so the split is before
            // the first segment that starts with a digit.
            string name = Path.GetFileName(folder);
            int split = FirstVersionDot(name);
            if (split <= 0)
            {
                continue;
            }

            PackageIdentity identity = new(name[..split], name[(split + 1)..]);
            if (IsInstalled(identity))
            {
                // The folder name is lower case by convention, but the id is a name somebody
                // chose, and a manager listing 'acme.nodes' beside a feed offering 'Acme.Nodes'
                // reads as two different packages. Only the display changes: every comparison
                // here ignores case, and FolderFor lower-cases again on the way back to disk.
                found.Add(identity with { Id = PackageInspector.DeclaredIdIn(folder) ?? identity.Id });
            }
        }

        return
        [
            .. found
                .OrderBy(identity => identity.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(identity => identity.Version, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>Reads an installed package's manifest.</summary>
    /// <param name="identity">The package and version.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="SparkPackageException">
    /// It is not installed, or its manifest cannot be read.
    /// </exception>
    public SparkPackageManifest ManifestOf(PackageIdentity identity)
    {
        string path = Path.Combine(FolderFor(identity), SparkPackageManifest.PathInPackage);

        if (!File.Exists(path))
        {
            throw new SparkPackageException($"'{identity}' is not installed.");
        }

        try
        {
            return SparkPackageManifest.Parse(File.ReadAllText(path));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new SparkPackageException($"'{identity}' has an unreadable manifest: {failure.Message}", failure);
        }
    }

    /// <summary>
    /// Removes an installed package version.
    /// </summary>
    /// <param name="identity">The package and version.</param>
    /// <returns>True when something was removed.</returns>
    /// <remarks>
    /// <b>This will fail while the package is loaded</b>, because an assembly loaded by path is
    /// locked on Windows until its context has genuinely unloaded, and unloading is best-effort.
    /// That is the same fact as <c>E7-T5</c>'s and the same reason restart is the documented
    /// default; the exception carries it rather than the caller having to know.
    /// </remarks>
    /// <exception cref="SparkPackageException">The folder could not be removed.</exception>
    public bool Uninstall(PackageIdentity identity)
    {
        string folder = FolderFor(identity);
        if (!Directory.Exists(folder))
        {
            return false;
        }

        // Retried briefly, because unmapping lags the collection that freed it. A caller that has
        // just unloaded the package's context and collected until its weak reference died can
        // still find the .dll mapped for a few milliseconds afterwards, and a single attempt then
        // fails - or worse, half-succeeds and leaves a folder with some files gone. This was a
        // one-in-three failure under a parallel test run before the retry.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
                return true;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 9)
                {
                    throw new SparkPackageException(
                        $"'{identity}' could not be removed, most likely because it is loaded. "
                        + "Restart Spark and try again.",
                        failure);
                }

                System.Threading.Thread.Sleep(20);
            }
        }
    }

    /// <summary>
    /// Where a folder name's version begins: the first dot followed by a digit.
    /// </summary>
    /// <remarks>
    /// A package id contains dots — <c>Acme.Nodes.Geometry</c> — so splitting on the first one
    /// would call the package <c>Acme</c>. Splitting before the first digit-led segment is what
    /// NuGet's own folder convention means, and it is why this is a method rather than an
    /// <c>IndexOf</c>.
    /// </remarks>
    private static int FirstVersionDot(string folderName)
    {
        for (int i = 0; i < folderName.Length - 1; i++)
        {
            if (folderName[i] == '.' && char.IsDigit(folderName[i + 1]))
            {
                return i;
            }
        }

        return -1;
    }
}
