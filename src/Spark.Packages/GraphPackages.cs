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
/// A package a graph's file names that is not where the file says it is (`E7-T17`).
/// </summary>
/// <param name="Recorded">The path exactly as the file records it.</param>
/// <param name="Name">What a message names: the entry's last segment.</param>
/// <param name="LookedIn">The folder Spark looked in, which is the one named after the file.</param>
public sealed record AbsentGraphPackage(string Recorded, string Name, string LookedIn);

/// <summary>What carrying a graph's package folder to a new file did (`E7-T19`).</summary>
/// <param name="From">The folder copied from.</param>
/// <param name="To">The folder copied into.</param>
/// <param name="Copied">How many files were copied.</param>
/// <param name="Kept">
/// How many were already at the destination and left alone, because they belong to whatever graph
/// was there before.
/// </param>
public sealed record GraphFolderCopy(string From, string To, int Copied, int Kept);

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

    /// <summary>
    /// The top-level entries of a graph's package folder, in the form a <c>.spark</c> file records
    /// them (`E7-T17`).
    /// </summary>
    /// <param name="graphPath">The <c>.spark</c> file's path.</param>
    /// <returns>
    /// One path per package folder and per loose <c>.dll</c> — <c>tower.packages/Helpers.dll</c> —
    /// relative to the graph file and ordered; empty when there is no folder.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="graphPath"/> is null or blank.</exception>
    /// <remarks>
    /// <b>Listed, not hashed.</b> This runs on every save, and <see cref="Discover"/> reads every byte
    /// of every assembly to hash it — a thirty-megabyte CAD API included. A file records which
    /// packages it expects, not what their bytes are; whether those bytes may run is the trust
    /// gate's question, at load.
    /// </remarks>
    public static IReadOnlyList<string> Entries(string graphPath)
    {
        string folder = FolderFor(graphPath);

        if (!Directory.Exists(folder))
        {
            return [];
        }

        string prefix = Path.GetFileName(folder) + "/";
        List<string> found = [];

        foreach (string file in Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly))
        {
            found.Add(prefix + Path.GetFileName(file));
        }

        foreach (string directory in Directory.EnumerateDirectories(folder))
        {
            string name = Path.GetFileName(directory);

            // A staged download is not a package, for the reason `Discover` skips it (`E7-T21`):
            // recording it would make a graph expect half an extract.
            if (!name.EndsWith(NuGetPackageClient.StagingSuffix, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(prefix + name);
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>
    /// Which of the packages a graph's file names are not where it says (`E7-T17`).
    /// </summary>
    /// <param name="graphPath">The <c>.spark</c> file's path.</param>
    /// <param name="recorded">The paths the file records, as written.</param>
    /// <returns>The absent ones, in the order given; empty when every one is there.</returns>
    /// <exception cref="ArgumentException"><paramref name="graphPath"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="recorded"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>Looked up in the folder named after the file as it is now, whatever folder the path was
    /// recorded under.</b> A file renamed outside Spark therefore reports its packages absent and
    /// names the folder it looked in — the client's call: a rename Spark did not perform is answered
    /// by a loud failure naming <c>&lt;filename&gt;.packages</c>, not by guessing which nearby
    /// folder was meant. Rename the folder to match and they are found.
    /// </para>
    /// <para>
    /// <b>Confined, and never resolved when it is not.</b> A path that is rooted, or climbs out with
    /// <c>..</c>, is reported absent without being looked up: a file from somebody else must not be
    /// able to ask this machine whether something outside the graph's folder exists.
    /// </para>
    /// </remarks>
    public static ImmutableArray<AbsentGraphPackage> Absent(string graphPath, IEnumerable<string> recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        string folder = FolderFor(graphPath);
        List<AbsentGraphPackage> absent = [];

        foreach (string path in recorded)
        {
            string? inside = Inside(path);
            bool there = false;

            if (inside is not null)
            {
                string full = Path.Combine(folder, inside.Replace('/', Path.DirectorySeparatorChar));
                there = File.Exists(full) || Directory.Exists(full);
            }

            if (!there)
            {
                absent.Add(new AbsentGraphPackage(path, LastSegment(inside ?? path), folder));
            }
        }

        return [.. absent];
    }

    /// <summary>
    /// What a graph's file should record when it is written to a path: what it already recorded,
    /// plus what is in the folder now (`E7-T17`).
    /// </summary>
    /// <param name="graphPath">Where the file is being written, or null for a graph with no file.</param>
    /// <param name="carried">The paths the file recorded when it was opened.</param>
    /// <returns>The paths to write. The document sorts them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="carried"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>A recorded package that is missing is never dropped.</b> That is `E7-T7`'s promise: a graph
    /// naming a package you do not have re-saves byte for byte, having been through a session that
    /// could not find it. Forgetting it would turn opening a graph on the wrong machine into
    /// silently editing it.
    /// </para>
    /// <para>
    /// <b>An entry is written under the folder named after the file being written</b>, so Save As
    /// records the new name — and one already recorded under that name is kept exactly as written,
    /// so an untouched graph produces no diff. A path that was not confined to begin with is kept as
    /// written rather than rewritten into something that would then resolve.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> ToRecord(string? graphPath, IEnumerable<string> carried)
    {
        ArgumentNullException.ThrowIfNull(carried);

        if (string.IsNullOrWhiteSpace(graphPath))
        {
            return [.. carried];
        }

        string folderName = Path.GetFileName(FolderFor(graphPath));
        List<string> result = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in carried)
        {
            string written = Inside(path) is { } inside && !RecordedUnder(path, folderName)
                ? folderName + "/" + inside
                : path;

            if (seen.Add(Inside(written) ?? written))
            {
                result.Add(written);
            }
        }

        foreach (string entry in Entries(graphPath))
        {
            if (seen.Add(Inside(entry) ?? entry))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>
    /// Copies a graph's package folder to sit beside a file it is being saved as (`E7-T19`).
    /// </summary>
    /// <param name="fromGraph">The <c>.spark</c> file the graph was saved as until now.</param>
    /// <param name="toGraph">The <c>.spark</c> file it is being saved as.</param>
    /// <returns>What was copied and what was kept; zero and zero when there was nothing to carry.</returns>
    /// <exception cref="ArgumentException">Either path is null or blank.</exception>
    /// <exception cref="IOException">A file could not be copied.</exception>
    /// <exception cref="UnauthorizedAccessException">The destination could not be written.</exception>
    /// <remarks>
    /// <para>
    /// <b>The client's call, and the reasoning is theirs</b>: renaming the package folder would
    /// otherwise become the user's problem. Save As is the one rename Spark performs itself, so it is
    /// the one Spark can carry the folder through — and since `E7-T17` the new file records its
    /// packages under the new name, so a copy without its folder reopens with every one of them
    /// named absent.
    /// </para>
    /// <para>
    /// <b>Copied, not moved</b>, because Save As leaves the original file where it was and must leave
    /// it working. <b>Merged, not overwritten</b>, when the destination already has a folder: it
    /// belongs to whatever graph was saved under that name before, and a save that silently replaced
    /// somebody's library with a different build of it would change what their graph computes. A
    /// file already there is counted as kept and left alone.
    /// </para>
    /// <para>
    /// <b>A staged download is not carried</b>, for the reason <see cref="Discover"/> skips it: it is
    /// half an extract whose disclosure may not have been answered. The listing is taken before
    /// anything is written, so a destination inside the source cannot copy into itself forever.
    /// </para>
    /// </remarks>
    public static GraphFolderCopy CopyFolder(string fromGraph, string toGraph)
    {
        string from = FolderFor(fromGraph);
        string to = FolderFor(toGraph);

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(from))
        {
            return new GraphFolderCopy(from, to, 0, 0);
        }

        int copied = 0;
        int kept = 0;

        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).ToList())
        {
            string relative = Path.GetRelativePath(from, file);
            string first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

            if (first.EndsWith(NuGetPackageClient.StagingSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destination = Path.Combine(to, relative);

            if (File.Exists(destination))
            {
                kept++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
            copied++;
        }

        return new GraphFolderCopy(from, to, copied, kept);
    }

    /// <summary>
    /// The part of a recorded path inside the package folder — <c>Helpers.dll</c> for
    /// <c>tower.packages/Helpers.dll</c> — or null when the path is not confined to it.
    /// </summary>
    private static string? Inside(string recorded)
    {
        string normal = recorded.Replace('\\', '/');

        if (normal.StartsWith('/') || Path.IsPathRooted(normal) || normal.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        string[] segments = normal.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            return null;
        }

        // The first segment is the folder the file was written beside, and it is not what the
        // lookup uses: that always goes to the folder named after the file as it is now.
        return segments.Length > 1 && segments[0].EndsWith(FolderSuffix, StringComparison.OrdinalIgnoreCase)
            ? string.Join('/', segments[1..])
            : string.Join('/', segments);
    }

    /// <summary>Whether a recorded path already sits under the folder named <paramref name="folderName"/>.</summary>
    private static bool RecordedUnder(string recorded, string folderName)
    {
        string normal = recorded.Replace('\\', '/');
        int slash = normal.IndexOf('/', StringComparison.Ordinal);

        return slash > 0 && string.Equals(normal[..slash], folderName, StringComparison.OrdinalIgnoreCase);
    }

    private static string LastSegment(string path)
    {
        string trimmed = path.Replace('\\', '/').TrimEnd('/');
        return trimmed[(trimmed.LastIndexOf('/') + 1)..];
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
