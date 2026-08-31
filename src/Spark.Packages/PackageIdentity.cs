using System;

namespace Spark.Packages;

/// <summary>
/// A package at a particular version — the unit of isolation in this layer.
/// </summary>
/// <param name="Id">The NuGet package identifier, compared case-insensitively as NuGet does.</param>
/// <param name="Version">
/// The version string exactly as NuGet reported it, including any prerelease suffix.
/// </param>
/// <remarks>
/// <b>Version is part of the identity, and that is a design decision rather than bookkeeping.</b>
/// One load context per package would make two versions of the same package impossible to have
/// loaded at once, which is precisely the case a graph built last year and a graph built today
/// put in front of us. See <see cref="PackageLoadContext"/>.
/// </remarks>
public readonly record struct PackageIdentity(string Id, string Version)
{
    /// <summary>Creates an identity, refusing blank parts.</summary>
    /// <param name="id">The package identifier.</param>
    /// <param name="version">The version string.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="ArgumentException">Either part is null, empty or whitespace.</exception>
    public static PackageIdentity Create(string id, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return new PackageIdentity(id, version);
    }

    /// <summary>
    /// A stable, filesystem-safe folder name for this identity, matching NuGet's own
    /// <c>id/version</c> layout in lower case.
    /// </summary>
    public string FolderName =>
        string.Concat(Id.ToLowerInvariant(), ".", Version.ToLowerInvariant());

    /// <summary>Whether two identities name the same package and version.</summary>
    /// <param name="other">The identity to compare with.</param>
    /// <returns>True when both parts match, ignoring case.</returns>
    public bool Equals(PackageIdentity other) =>
        string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Version, other.Version, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(
        Id is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Id),
        Version is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Version));

    /// <inheritdoc/>
    public override string ToString() => $"{Id} {Version}";
}
