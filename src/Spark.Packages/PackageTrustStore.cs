using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Spark.Packages;

/// <summary>
/// What the user has already agreed to install (<c>E7-T8</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Trust is recorded per package and version, not per publisher.</b> Agreeing to
/// <c>Acme.Nodes 1.0.0</c> is not agreeing to <c>Acme.Nodes 2.0.0</c>, because the thing a user
/// weighed — its licence, its dependencies, whether it carried native binaries — can all change
/// between versions. A per-publisher store would let a signed package quietly acquire a native
/// dependency in a patch release, which is precisely the disclosure this exists to protect.
/// </para>
/// <para>
/// <b>It records a decision, it does not make one.</b> Nothing here reads a certificate or decides
/// whether a publisher is reputable; it remembers what a person said. That is the only thing a
/// local file can honestly claim to know.
/// </para>
/// <para>
/// <b>A missing or unreadable file means nothing is trusted</b>, which is the safe direction: the
/// worst outcome is that a user is asked again.
/// </para>
/// </remarks>
public sealed class PackageTrustStore
{
    private readonly string _path;
    private readonly HashSet<string> _trusted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a trust store backed by a file.</summary>
    /// <param name="path">The file. It does not have to exist.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    public PackageTrustStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = Path.GetFullPath(path);
        Load();
    }

    /// <summary>The default store, beside the installed packages.</summary>
    /// <param name="store">The package store whose folder it sits in.</param>
    /// <returns>The trust store.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    public static PackageTrustStore For(PackageStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        return new PackageTrustStore(Path.Combine(store.Root, "trusted.json"));
    }

    /// <summary>How many package versions the user has agreed to.</summary>
    public int Count => _trusted.Count;

    /// <summary>Whether the user has already agreed to this exact package version.</summary>
    /// <param name="identity">The package and version.</param>
    /// <returns>True when it has been trusted.</returns>
    public bool IsTrusted(PackageIdentity identity) => _trusted.Contains(Key(identity));

    /// <summary>Records that the user agreed to install this package version.</summary>
    /// <param name="identity">The package and version.</param>
    /// <exception cref="SparkPackageException">The decision could not be saved.</exception>
    public void Trust(PackageIdentity identity)
    {
        if (_trusted.Add(Key(identity)))
        {
            Save();
        }
    }

    /// <summary>Forgets a decision, so the user is asked again.</summary>
    /// <param name="identity">The package and version.</param>
    /// <returns>True when something was forgotten.</returns>
    /// <exception cref="SparkPackageException">The change could not be saved.</exception>
    public bool Revoke(PackageIdentity identity)
    {
        if (!_trusted.Remove(Key(identity)))
        {
            return false;
        }

        Save();
        return true;
    }

    /// <summary>Everything the user has agreed to, in a stable order.</summary>
    /// <returns>The entries, as <c>id/version</c>.</returns>
    public IReadOnlyList<string> Entries() => [.. _trusted.OrderBy(entry => entry, StringComparer.Ordinal)];

    private static string Key(PackageIdentity identity) =>
        identity.Id.ToLowerInvariant() + "/" + identity.Version.ToLowerInvariant();

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(File.ReadAllText(_path));

            if (parsed.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement entry in parsed.RootElement.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { Length: > 0 } value)
                {
                    _trusted.Add(value);
                }
            }
        }
        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException)
        {
            // Nothing is trusted, which is the safe direction: the user is asked again.
            _trusted.Clear();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(
                _path,
                JsonSerializer.Serialize(Entries(), new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new SparkPackageException(
                $"The list of packages you have agreed to could not be saved to {_path}: {failure.Message}",
                failure);
        }
    }
}
