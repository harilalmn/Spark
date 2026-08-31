using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Spark.Packages;

/// <summary>Whether a package carries a cryptographic signature.</summary>
/// <remarks>
/// <b>Three states, and the middle one is the honest answer.</b> Spark reads whether a signature is
/// <i>present</i>; it does not build a certificate chain, check revocation, or decide whether the
/// signer is anyone in particular. Reporting <c>Signed</c> would imply all three.
/// </remarks>
public enum PackageSignature
{
    /// <summary>The package carries no signature at all.</summary>
    Unsigned,

    /// <summary>
    /// A signature is present. <b>Spark has not verified it</b> — not the chain, not revocation,
    /// not the signer's identity.
    /// </summary>
    PresentButUnverified,

    /// <summary>The package could not be read well enough to say.</summary>
    Unknown,
}

/// <summary>
/// Everything a user is told before agreeing to install a package (<c>E7-T8</c>).
/// </summary>
/// <param name="Identity">The package and version.</param>
/// <param name="Authors">Who published it, from the package's own metadata.</param>
/// <param name="Licence">The licence expression or file the package declares, or null.</param>
/// <param name="ProjectUrl">Where to read more, or null.</param>
/// <param name="Signature">Whether a signature is present.</param>
/// <param name="Dependencies">The NuGet packages it depends on, by id.</param>
/// <param name="NodeAssemblies">The assemblies its manifest offers nodes from.</param>
/// <param name="NativeBinaries">
/// The native libraries it carries, by path inside the package. <b>Empty is the answer a user
/// expects</b>, and a non-empty list is the disclosure this row exists for.
/// </param>
public sealed record PackageDisclosure(
    PackageIdentity Identity,
    string Authors,
    string? Licence,
    string? ProjectUrl,
    PackageSignature Signature,
    ImmutableArray<string> Dependencies,
    ImmutableArray<string> NodeAssemblies,
    ImmutableArray<string> NativeBinaries)
{
    /// <summary>
    /// Whether the package carries native binaries.
    /// </summary>
    /// <remarks>
    /// <b>Spark's own promise is that it has no native dependencies</b> beyond the solid-modelling
    /// provider a user chose to install. A package is entitled to break that promise on its own
    /// behalf — plenty of useful libraries are native — but not silently, and not on the user's
    /// behalf without telling them. A native binary is also the part of a package that a
    /// collectible load context cannot unload, so it changes what upgrading costs.
    /// </remarks>
    public bool CarriesNativeBinaries => !NativeBinaries.IsEmpty;

    /// <summary>A short sentence naming what a user most needs to weigh, for a prompt.</summary>
    /// <returns>The summary.</returns>
    public string Summary()
    {
        List<string> parts =
        [
            $"by {Authors}",
            Licence is null ? "no licence declared" : $"licence {Licence}",
            Signature switch
            {
                PackageSignature.PresentButUnverified => "signed (signature not verified by Spark)",
                PackageSignature.Unsigned => "unsigned",
                _ => "signature unknown",
            },
        ];

        if (!Dependencies.IsEmpty)
        {
            parts.Add($"{Dependencies.Length} dependenc{(Dependencies.Length == 1 ? "y" : "ies")}");
        }

        if (CarriesNativeBinaries)
        {
            parts.Add($"**carries {NativeBinaries.Length} native binar"
                + (NativeBinaries.Length == 1 ? "y" : "ies") + "**");
        }

        return string.Join(", ", parts) + ".";
    }
}

/// <summary>
/// Reads a package's disclosure from the package itself.
/// </summary>
/// <remarks>
/// <b>Read, never declared.</b> Every field here comes from the files in the package — the
/// <c>.nuspec</c>, the manifest, the presence of a signature entry, the shape of the folder tree.
/// A disclosure a package could assert about itself would be worth nothing to the user it is shown
/// to.
/// </remarks>
public static class PackageInspector
{
    private static readonly ImmutableArray<string> ManagedExtensions =
        [".dll", ".exe", ".pdb", ".xml", ".json", ".txt", ".md", ".nuspec", ".p7s", ".psmdcp", ".rels"];

    /// <summary>
    /// Inspects an extracted package folder.
    /// </summary>
    /// <param name="folder">The folder the package was extracted into.</param>
    /// <param name="identity">The package and version.</param>
    /// <returns>What to tell the user.</returns>
    /// <exception cref="ArgumentException"><paramref name="folder"/> is null or blank.</exception>
    public static PackageDisclosure Inspect(string folder, PackageIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        string authors = "unknown";
        string? licence = null;
        string? projectUrl = null;
        ImmutableArray<string> dependencies = [];

        if (FindNuspec(folder) is { } nuspec)
        {
            (authors, licence, projectUrl, dependencies) = ReadNuspec(nuspec);
        }

        ImmutableArray<string> assemblies = [];
        string manifestPath = Path.Combine(folder, SparkPackageManifest.PathInPackage);
        if (File.Exists(manifestPath))
        {
            try
            {
                assemblies = SparkPackageManifest.Parse(File.ReadAllText(manifestPath)).Assemblies;
            }
            catch (Exception failure) when (failure is SparkPackageException or IOException)
            {
                // A disclosure is shown to help a user decide, so it reports what it could read
                // rather than refusing outright. Install itself validates the manifest properly.
            }
        }

        return new PackageDisclosure(
            identity,
            authors,
            licence,
            projectUrl,
            SignatureOf(folder),
            dependencies,
            assemblies,
            NativeBinariesIn(folder));
    }

    /// <summary>
    /// The native libraries a package carries, by path relative to its folder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two ways a package carries one, and both are checked. NuGet's own convention is
    /// <c>runtimes/{rid}/native</c>, which is where <see cref="PackageLoadContext"/> looks. But a
    /// package may also simply drop a native <c>.dll</c> beside its managed ones, and a check that
    /// only knew the convention would report *no native binaries* for a package that plainly has
    /// one.
    /// </para>
    /// <para>
    /// <b>Extension-based, and deliberately over-reports rather than under-reports.</b> Telling a
    /// user about a file that turns out to be harmless costs them a moment; not telling them about
    /// one that is not costs them the promise this disclosure exists to keep. Distinguishing a
    /// managed assembly from a native one properly means reading the PE header, which is worth
    /// doing when this is wrong often enough to matter and not before.
    /// </para>
    /// </remarks>
    public static ImmutableArray<string> NativeBinariesIn(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        if (!Directory.Exists(folder))
        {
            return [];
        }

        List<string> found = [];
        string root = Path.GetFullPath(folder);

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            bool inNativeFolder = relative.Contains("/native/", StringComparison.OrdinalIgnoreCase);
            string extension = Path.GetExtension(file);

            bool unmanagedByExtension =
                extension.Length > 0
                && !ManagedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                && extension is ".so" or ".dylib" or ".a" or ".lib" or ".node";

            if (inNativeFolder || unmanagedByExtension)
            {
                found.Add(relative);
            }
        }

        found.Sort(StringComparer.Ordinal);
        return [.. found];
    }

    /// <summary>
    /// Whether a signature is present. <b>Never whether it is valid.</b>
    /// </summary>
    private static PackageSignature SignatureOf(string folder)
    {
        try
        {
            return File.Exists(Path.Combine(folder, ".signature.p7s"))
                ? PackageSignature.PresentButUnverified
                : PackageSignature.Unsigned;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return PackageSignature.Unknown;
        }
    }

    private static string? FindNuspec(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.nuspec", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads what a <c>.nuspec</c> says about its publisher, licence and dependencies.
    /// </summary>
    /// <remarks>
    /// Element names are matched without their namespace. A <c>.nuspec</c> declares one of several
    /// schema namespaces depending on when it was written, and matching the full name would make
    /// the disclosure silently empty for older packages — which reads as *no licence declared*
    /// rather than as *not read*.
    /// </remarks>
    private static (string Authors, string? Licence, string? ProjectUrl, ImmutableArray<string> Dependencies)
        ReadNuspec(string path)
    {
        try
        {
            XDocument document = XDocument.Load(path);

            string? Value(string name) => document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == name)?.Value?.Trim();

            string? licence = Value("license") ?? Value("licenseUrl");

            List<string> dependencies =
            [
                .. document
                    .Descendants()
                    .Where(element => element.Name.LocalName == "dependency")
                    .Select(element => element.Attribute("id")?.Value)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase),
            ];

            return (
                Value("authors") is { Length: > 0 } authors ? authors : "unknown",
                string.IsNullOrWhiteSpace(licence) ? null : licence,
                Value("projectUrl"),
                [.. dependencies]);
        }
        catch (Exception failure) when (failure is System.Xml.XmlException or IOException)
        {
            return ("unknown", null, null, []);
        }
    }
}
