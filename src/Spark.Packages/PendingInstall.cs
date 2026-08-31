using System;
using System.IO;

namespace Spark.Packages;

/// <summary>
/// A package that has been downloaded and read, and not yet installed (<c>E7-T8</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This type exists so that the disclosure can come before the decision.</b> A user cannot weigh
/// a package's licence, its dependencies, or whether it carries native binaries until those have
/// been read out of it, and reading them means downloading it. So the files are parked in a
/// staging folder — somewhere <see cref="PackageStore"/> deliberately does not consider installed
/// and <see cref="PackageLoadContext"/> will never load from — and the user decides afterwards.
/// </para>
/// <para>
/// <b>A pending install that is neither committed nor discarded leaks a folder.</b> It is
/// <see cref="IDisposable"/> so that the ordinary <c>using</c> shape does the right thing, and
/// disposing after a commit is harmless because the staging folder has already moved.
/// </para>
/// </remarks>
public sealed class PendingInstall : IDisposable
{
    private readonly string _destination;
    private bool _settled;

    internal PendingInstall(
        PackageIdentity identity,
        string staging,
        string destination,
        SparkPackageManifest manifest,
        PackageDisclosure disclosure)
    {
        Identity = identity;
        StagingFolder = staging;
        _destination = destination;
        Manifest = manifest;
        Disclosure = disclosure;
    }

    /// <summary>The package and version.</summary>
    public PackageIdentity Identity { get; }

    /// <summary>Where the files are while the decision is being made.</summary>
    public string StagingFolder { get; }

    /// <summary>Its Spark manifest, already validated.</summary>
    public SparkPackageManifest Manifest { get; }

    /// <summary>What to tell the user before they agree.</summary>
    public PackageDisclosure Disclosure { get; }

    /// <summary>Whether this has been committed or discarded.</summary>
    public bool IsSettled => _settled;

    /// <summary>
    /// Installs the package: moves it out of staging and into the store.
    /// </summary>
    /// <exception cref="SparkPackageException">The move failed.</exception>
    /// <remarks>
    /// A move rather than a copy, and only at the very end. Until this line runs there is nothing
    /// in the store to load, so an interrupted install leaves the previous state exactly as it was.
    /// </remarks>
    public void Commit()
    {
        if (_settled)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_destination))
            {
                Directory.Delete(_destination, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_destination)!);
            Directory.Move(StagingFolder, _destination);
            _settled = true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new SparkPackageException(
                $"'{Identity}' was downloaded but could not be installed: {failure.Message}. "
                + "If an earlier version is loaded, restart Spark and try again.",
                failure);
        }
    }

    /// <summary>Throws the download away without installing it.</summary>
    public void Discard()
    {
        if (_settled)
        {
            return;
        }

        _settled = true;

        try
        {
            if (Directory.Exists(StagingFolder))
            {
                Directory.Delete(StagingFolder, recursive: true);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Untidy, never wrong: nothing loads from a staging folder, and the next attempt
            // replaces it.
        }
    }

    /// <summary>Discards the download if it was never committed.</summary>
    public void Dispose() => Discard();
}
