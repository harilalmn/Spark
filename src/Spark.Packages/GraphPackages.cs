using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Spark.Packages;

/// <summary>
/// One assembly found in a graph's own package folder (`E7-T16`).
/// </summary>
/// <param name="Path">Its full path on disk.</param>
/// <param name="Hash">SHA-256 of its bytes, uppercase hex, or empty when it could not be read.</param>
/// <param name="Package">
/// The package folder it came from, or empty for a <c>.dll</c> dropped loose at the top level.
/// </param>
public sealed record GraphAssembly(string Path, string Hash, string Package)
{
    /// <summary>The file name alone, for a list a person reads.</summary>
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>The first eight characters of the hash, which is what a user compares by eye.</summary>
    public string ShortHash => Hash.Length >= 8 ? Hash[..8] : Hash;

    /// <summary>How this reads in a disclosure: the package if it has one, else the file.</summary>
    public string Describe() => Package.Length == 0 ? Name : Package + " / " + Name;
}

/// <summary>
/// The packages that live beside a graph, in <c>&lt;name&gt;.packages</c> (`E7-T16`,
/// [ADR-0024](../../docs/adr/0024-graph-local-package-folder.md)).
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client, who drew the layout.</b> A graph and the libraries it needs travel
/// together: zip the folder, send it, it opens. Under the global store alone a <c>.spark</c> is a
/// graph plus verbal instructions about what to install first, and the instructions are not in the
/// file. It also lets two graphs disagree about a library version without either breaking, which
/// `E7-T3`'s one-collectible-context-per-package-version was already built for.
/// </para>
/// <para>
/// <b>Both layouts are accepted, because both will happen.</b> A NuGet extraction has
/// <c>lib/&lt;tfm&gt;/</c> and the framework folder has to be chosen; a user who dropped a
/// <c>.dll</c> in by hand has neither. Refusing the second would make the simple case — the one
/// the client actually asked about — the hard one.
/// </para>
/// <para>
/// <b>Nothing here loads anything.</b> This type finds and hashes; whether an assembly may be
/// loaded is <see cref="PackageTrustStore"/>'s answer and the caller's decision. That separation is
/// the whole safety property: a folder of DLLs beside a downloaded graph is remote code execution,
/// and loading is not passive — module initialisers and static constructors run, and the node
/// importer reflects over types, which triggers them.
/// </para>
/// </remarks>
public sealed class GraphPackages
{
    /// <summary>The suffix a graph's package folder carries.</summary>
    public const string FolderSuffix = ".packages";

    private GraphPackages(string folder, ImmutableArray<GraphAssembly> assemblies)
    {
        Folder = folder;
        Assemblies = assemblies;
    }

    /// <summary>The folder this looked in, whether or not it exists.</summary>
    public string Folder { get; }

    /// <summary>Whether the folder is there.</summary>
    public bool Exists => Directory.Exists(Folder);

    /// <summary>Every assembly found, ordered so two runs agree.</summary>
    public ImmutableArray<GraphAssembly> Assemblies { get; }

    /// <summary>Where a graph's packages live.</summary>
    /// <param name="graphPath">The <c>.spark</c> file's path.</param>
    /// <returns>The folder path, which may not exist.</returns>
    /// <exception cref="ArgumentException"><paramref name="graphPath"/> is null or blank.</exception>
    /// <remarks>
    /// <b>Beside the file and named after it</b>, so that copying the pair copies the dependency,
    /// and so that a person looking at a folder of graphs can see which packages belong to which.
    /// </remarks>
    public static string FolderFor(string graphPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphPath);

        string full = Path.GetFullPath(graphPath);

        return Path.Combine(
            Path.GetDirectoryName(full) ?? string.Empty,
            Path.GetFileNameWithoutExtension(full) + FolderSuffix);
    }

    /// <summary>Finds and hashes everything in a graph's package folder.</summary>
    /// <param name="graphPath">The <c>.spark</c> file's path, or null for a graph never saved.</param>
    /// <returns>The set, which is empty when there is no file or no folder.</returns>
    /// <remarks>
    /// <b>A graph that has never been saved has no folder, and that is not an error.</b> It has no
    /// filename to name one after — which is also why `E7-T18` refuses to open the Packages window
    /// until the file is saved rather than inventing a location and moving it later.
    /// </remarks>
    public static GraphPackages Discover(string? graphPath)
    {
        if (string.IsNullOrWhiteSpace(graphPath))
        {
            return new GraphPackages(string.Empty, []);
        }

        string folder = FolderFor(graphPath);

        if (!Directory.Exists(folder))
        {
            return new GraphPackages(folder, []);
        }

        List<GraphAssembly> found = [];

        // A `.dll` dropped straight into the folder, which is the shape the client asked about and
        // the one a user reaches for first.
        foreach (string file in Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly))
        {
            found.Add(Read(file, package: string.Empty));
        }

        foreach (string directory in Directory.EnumerateDirectories(folder))
        {
            string package = Path.GetFileName(directory);

            // A download that was interrupted, or one nobody has agreed to yet, is staged here
            // under its own name plus a suffix - and it is not a package. Offering its contents
            // would hand a user half an extract, and would do it *before* the disclosure it is
            // waiting on had been answered (`E7-T21`).
            if (package.EndsWith(NuGetPackageClient.StagingSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string file in AssembliesIn(directory))
            {
                found.Add(Read(file, package));
            }

            // A package installed from a feed brings its dependencies with it, in the `.deps`
            // folder `NuGetPackageClient` stages them into (`E7-T21`). They are *this* package's
            // copies - removing it removes them - so they are reported under its name rather than
            // their own. Leaving them out would download a library's dependencies and then refuse
            // to compile against them, which fails on the first type that appears in one of the
            // library's own signatures.
            string dependencies = Path.Combine(directory, NuGetPackageClient.DependencyFolder);

            if (!Directory.Exists(dependencies))
            {
                continue;
            }

            foreach (string dependency in Directory.EnumerateDirectories(dependencies))
            {
                foreach (string file in AssembliesIn(dependency))
                {
                    found.Add(Read(file, package));
                }
            }
        }

        // Ordered so that two discoveries of the same folder produce the same list: the disclosure
        // a user reads, and the hash set a decision is recorded against, must not depend on the
        // order a file system happened to enumerate in.
        return new GraphPackages(
            folder,
            [.. found.OrderBy(a => a.Package, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// The framework folders one installed package carries, for a message to a user who got
    /// nothing usable out of it (`E7-T23`).
    /// </summary>
    /// <param name="graphPath">The <c>.spark</c> file's path.</param>
    /// <param name="identity">The package that was installed beside it.</param>
    /// <returns>Folders such as <c>lib/net472</c>, or an empty list when it carries none.</returns>
    /// <exception cref="ArgumentException"><paramref name="graphPath"/> is null or blank.</exception>
    public static IReadOnlyList<string> FrameworksOffered(string graphPath, PackageIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphPath);

        return PackageFrameworks.FrameworksOffered(
            Path.Combine(FolderFor(graphPath), identity.FolderName));
    }

    /// <summary>The assemblies one package folder offers, for the framework this build is.</summary>
    /// <remarks>
    /// <para>
    /// <b>A NuGet package carries one copy per target framework and only one of them may be
    /// used.</b> Taking every <c>.dll</c> under <c>lib/</c> would offer the same type several times
    /// over and pick whichever the loader saw first.
    /// </para>
    /// <para>
    /// <b>The choice is <see cref="PackageFrameworks"/>'s, which is NuGet's own resolver</b>
    /// (`E7-T23`). This method used to rank framework folders by hand, with a comment admitting the
    /// ranking was crude and that real compatibility is NuGet's job. It then met
    /// <c>ref/net10.0-windows7.0</c> — a package targeting the exact framework Spark runs on — and
    /// refused it, because the hand-written parse read everything after <c>net</c> as a number.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> AssembliesIn(string package)
    {
        foreach (string folder in PackageFrameworks.AssemblyFoldersIn(package))
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }
        }
    }

    private static GraphAssembly Read(string path, string package)
    {
        try
        {
            // Opened shared, because a user rebuilding the DLL they just dropped in is the normal
            // case rather than the exception.
            using FileStream file = new(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return new GraphAssembly(path, Convert.ToHexString(SHA256.HashData(file)), package);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be read is reported with no hash rather than thrown about. It is
            // usually mid-build, and an unhashed assembly can never match a recorded decision - so
            // it fails closed, which is the direction this has to fail in.
            return new GraphAssembly(path, string.Empty, package);
        }
    }
}
