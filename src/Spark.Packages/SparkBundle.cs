using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace Spark.Packages;

/// <summary>What packing a graph put in its bundle (<c>E3-T20</c>).</summary>
/// <param name="BundlePath">The <c>.sparkz</c> file written.</param>
/// <param name="GraphName">The graph's file name, as it is stored in the bundle.</param>
/// <param name="PackageFiles">How many files of the graph's package folder went with it.</param>
public sealed record SparkBundleContents(string BundlePath, string GraphName, int PackageFiles);

/// <summary>A <c>.sparkz</c> file that cannot be opened, and the reason in words.</summary>
/// <remarks>
/// An <see cref="IOException"/>, because a bad bundle is a bad file: everything that already
/// reports a corrupt file in one line - the command line among them - reports this one the same way.
/// </remarks>
public sealed class SparkBundleException : IOException
{
    /// <summary>Creates the exception with a default message.</summary>
    public SparkBundleException()
        : base("The file is not a Spark bundle this build can open.")
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong with the bundle.</param>
    public SparkBundleException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception around the failure that revealed it.</summary>
    /// <param name="message">What was wrong with the bundle.</param>
    /// <param name="innerException">The failure.</param>
    public SparkBundleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The <c>.sparkz</c> bundle: a graph and what it needs, zipped into one file for sharing
/// (<c>E3-T20</c>, ADR-0017).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> A <c>.spark</c> file is plain JSON so that it diffs and merges, and what it
/// needs lives beside it rather than inside it - so a user who emails only the <c>.spark</c> sends a
/// graph without its libraries. ADR-0017 names the bundle as the fix: one file that cannot be
/// half-sent.
/// </para>
/// <para>
/// <b>What is in one.</b> A <c>manifest.json</c> naming the bundle format and the graph; the graph,
/// under its own file name; and the graph's package folder (<see cref="GraphPackages.FolderSuffix"/>)
/// under its own name, so that opening the bundle recreates exactly the pair Save As and the
/// Packages window already understand, and nothing that loads a graph has to learn to read a zip. A
/// half-finished download in the folder is left out, for the reason
/// <see cref="GraphPackages.CopyFolder"/> leaves it out.
/// </para>
/// <para>
/// <b>What is not in one yet, each for a reason.</b> ADR-0017 also lists assets, custom node
/// definitions and a thumbnail. Assets have no folder yet - nothing writes <c>.assets</c> - and
/// custom node definitions have no file home a graph points at; each joins the bundle with the
/// first thing that gives it one, and <see cref="FormatVersion"/> is what lets an older build refuse
/// the bundle that carries them rather than open it without them. A thumbnail needs a renderer, which
/// a command-line pack does not have.
/// </para>
/// <para>
/// <b>Opening checks before it writes.</b> Every entry is checked first, and one that would land
/// outside the folder it is opened into - a <c>../</c> or a rooted name, the zip-slip - refuses the
/// whole bundle rather than extracting the rest. A bundle is exactly the kind of file that arrives
/// from somebody else.
/// </para>
/// </remarks>
public static class SparkBundle
{
    /// <summary>The bundle's file extension.</summary>
    public const string Extension = ".sparkz";

    /// <summary>The newest bundle format this build writes and reads.</summary>
    public const int FormatVersion = 1;

    private const string ManifestEntry = "manifest.json";

    /// <summary>Zips a graph and its package folder into a bundle.</summary>
    /// <param name="graphPath">The <c>.spark</c> file.</param>
    /// <param name="bundlePath">The <c>.sparkz</c> file to write. Replaced if it exists.</param>
    /// <returns>What went into the bundle.</returns>
    /// <exception cref="ArgumentException">Either path is null or blank.</exception>
    /// <exception cref="FileNotFoundException">There is no graph at <paramref name="graphPath"/>.</exception>
    /// <exception cref="IOException">A file could not be read or the bundle could not be written.</exception>
    /// <remarks>
    /// <b>Written beside the destination and moved into place</b>, so a pack that fails part-way never
    /// leaves a truncated bundle under the name somebody is about to send.
    /// </remarks>
    public static SparkBundleContents Pack(string graphPath, string bundlePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);

        string graph = Path.GetFullPath(graphPath);

        if (!File.Exists(graph))
        {
            throw new FileNotFoundException($"There is no graph at '{graph}' to pack.", graph);
        }

        string name = Path.GetFileName(graph);
        string folder = GraphPackages.FolderFor(graph);
        string folderName = Path.GetFileName(folder);

        // Listed before anything is written, so a bundle written inside the folder is not packed into itself.
        List<string> packages = Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(file => !Staged(folder, file))
                .Order(StringComparer.Ordinal)]
            : [];

        string full = Path.GetFullPath(bundlePath);
        string partial = full + ".partial";

        using (FileStream stream = File.Create(partial))
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
        {
            WriteManifest(zip, name);
            zip.CreateEntryFromFile(graph, name, CompressionLevel.Optimal);

            foreach (string file in packages)
            {
                string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                zip.CreateEntryFromFile(file, folderName + "/" + relative, CompressionLevel.Optimal);
            }
        }

        File.Move(partial, full, overwrite: true);

        return new SparkBundleContents(full, name, packages.Count);
    }

    /// <summary>Opens a bundle into a folder, and says where its graph is.</summary>
    /// <param name="bundlePath">The <c>.sparkz</c> file.</param>
    /// <param name="destination">
    /// The folder to open it into, created if need be. Files already there under the bundle's names are
    /// replaced; nothing else in it is touched.
    /// </param>
    /// <returns>The path of the graph, with its package folder beside it.</returns>
    /// <exception cref="ArgumentException">Either path is null or blank.</exception>
    /// <exception cref="SparkBundleException">
    /// The file is not a zip, has no manifest, was packed by a newer build, does not hold the graph its
    /// manifest names, or has an entry that would be written outside <paramref name="destination"/>.
    /// Nothing is written when any of these is found.
    /// </exception>
    public static string Unpack(string bundlePath, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        string root = Path.GetFullPath(destination);
        string label = Path.GetFileName(bundlePath);
        ZipArchive zip;

        try
        {
            zip = ZipFile.OpenRead(bundlePath);
        }
        catch (InvalidDataException failure)
        {
            throw new SparkBundleException($"'{label}' is not a Spark bundle: it is not a zip file.", failure);
        }

        using (zip)
        {
            ZipArchiveEntry manifest = zip.GetEntry(ManifestEntry)
                ?? throw new SparkBundleException($"'{label}' is not a Spark bundle: it has no {ManifestEntry}.");

            (int version, string graph) = ReadManifest(manifest, label);

            if (version > FormatVersion)
            {
                throw new SparkBundleException(
                    $"'{label}' was packed by a newer Spark (bundle format {version}); this build opens bundles up to format {FormatVersion}. Update Spark to open it.");
            }

            if (!IsPlainGraphName(graph) || zip.GetEntry(graph) is null)
            {
                throw new SparkBundleException($"'{label}' names the graph '{graph}' in its manifest, and does not hold it.");
            }

            // Every entry is checked before anything is written.
            List<(ZipArchiveEntry Entry, string Target)> targets = [];

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string target = Path.GetFullPath(Path.Combine(root, entry.FullName));

                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SparkBundleException(
                        $"'{label}' was refused: its entry '{entry.FullName}' would be written outside the folder it is opened into.");
                }

                targets.Add((entry, target));
            }

            foreach ((ZipArchiveEntry entry, string target) in targets)
            {
                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            return Path.Combine(root, graph);
        }
    }

    private static bool Staged(string folder, string file)
    {
        string relative = Path.GetRelativePath(folder, file);
        string first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

        return first.EndsWith(NuGetPackageClient.StagingSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlainGraphName(string name) =>
        name.Length > 0
        && name.IndexOfAny(['/', '\\', ':']) < 0
        && name is not ("." or "..")
        && name.EndsWith(".spark", StringComparison.OrdinalIgnoreCase);

    private static void WriteManifest(ZipArchive zip, string graphName)
    {
        ZipArchiveEntry entry = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);

        using Stream stream = entry.Open();
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", FormatVersion);
        writer.WriteString("graph", graphName);
        writer.WriteEndObject();
    }

    private static (int Version, string Graph) ReadManifest(ZipArchiveEntry manifest, string label)
    {
        try
        {
            using Stream stream = manifest.Open();
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;

            return (root.GetProperty("formatVersion").GetInt32(), root.GetProperty("graph").GetString() ?? string.Empty);
        }
        catch (Exception failure) when (failure is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new SparkBundleException($"'{label}' has a {ManifestEntry} this build cannot read: {failure.Message}", failure);
        }
    }
}
