using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Spark.Api;

/// <summary>
/// A SemVer version, parsed and ordered by SemVer's rules rather than by string comparison
/// (<c>E12-T21</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because "is there a newer version" is a question string comparison answers
/// wrongly.</b> <c>"0.10.0" &lt; "0.9.0"</c> as text, so a naive update check goes quiet exactly
/// when a project reaches its tenth minor release and stays quiet forever after. It is the kind of
/// defect that ships, because every test written before version 10 passes.
/// </para>
/// <para>
/// <b>Prerelease ordering is implemented properly, and it is the half that earns its keep.</b>
/// Spark's own builds between tags are stamped by MinVer as <c>0.1.1-alpha.0.5</c> — a version
/// *ahead* of the released <c>0.1.0</c> and *behind* the unreleased <c>0.1.1</c>. Treating the
/// prerelease tail as noise would tell every developer running a local build that an update is
/// available, forever, which is the fastest way to teach somebody to ignore a notification.
/// </para>
/// <para>
/// <b>Build metadata is parsed and discarded</b>, as SemVer requires: <c>+abc1234</c> identifies
/// which commit produced a build and says nothing about precedence. Source-linked builds carry it,
/// so it has to be handled rather than tripped over.
/// </para>
/// </remarks>
public readonly struct SparkVersion : IEquatable<SparkVersion>, IComparable<SparkVersion>
{
    private SparkVersion(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    /// <summary>The major version.</summary>
    public int Major { get; }

    /// <summary>The minor version.</summary>
    public int Minor { get; }

    /// <summary>The patch version.</summary>
    public int Patch { get; }

    /// <summary>
    /// The prerelease tail without its hyphen — <c>alpha.0.5</c> — or <see langword="null"/> for a
    /// release.
    /// </summary>
    public string? Prerelease { get; }

    /// <summary>Whether this version is a prerelease.</summary>
    public bool IsPrerelease => Prerelease is not null;

    /// <summary>
    /// Parses a SemVer string, with or without a leading <c>v</c>.
    /// </summary>
    /// <param name="text">The version, such as <c>v0.2.0</c> or <c>0.2.1-beta.1+abc1234</c>.</param>
    /// <param name="version">The parsed version, or the default when this returns false.</param>
    /// <returns>True when <paramref name="text"/> is a version.</returns>
    /// <remarks>
    /// <b>A leading <c>v</c> is accepted because that is what a git tag looks like</b> — Spark's
    /// tag prefix is <c>v</c> (<c>MinVerTagPrefix</c>), so the string arriving from a release is
    /// <c>v0.2.0</c> while the string in an assembly is <c>0.2.0</c>. Requiring the caller to
    /// strip it would put that knowledge in every caller.
    /// </remarks>
    public static bool TryParse(string? text, out SparkVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> span = text.AsSpan().Trim();

        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V'))
        {
            span = span[1..];
        }

        // Build metadata never affects precedence, so it is removed before anything else is read.
        int plus = span.IndexOf('+');
        if (plus >= 0)
        {
            span = span[..plus];
        }

        string? prerelease = null;
        int hyphen = span.IndexOf('-');
        if (hyphen >= 0)
        {
            prerelease = span[(hyphen + 1)..].ToString();
            span = span[..hyphen];

            if (prerelease.Length == 0)
            {
                return false;
            }
        }

        Span<Range> parts = stackalloc Range[4];
        int count = SplitOnDots(span, parts);

        if (count != 3)
        {
            return false;
        }

        if (!TryNumber(span[parts[0]], out int major)
            || !TryNumber(span[parts[1]], out int minor)
            || !TryNumber(span[parts[2]], out int patch))
        {
            return false;
        }

        version = new SparkVersion(major, minor, patch, prerelease);
        return true;
    }

    /// <summary>
    /// The version an assembly was built as, read from its informational version.
    /// </summary>
    /// <param name="assembly">The assembly to ask.</param>
    /// <returns>The version, or null when the assembly carries none that parses.</returns>
    /// <remarks>
    /// <b><see cref="AssemblyInformationalVersionAttribute"/> and not
    /// <see cref="AssemblyName.Version"/>, and the difference is not cosmetic.</b> MinVer stamps
    /// the informational version with the full SemVer and truncates the assembly version to
    /// <c>major.0.0.0</c> — deliberately, because the assembly version participates in binding and
    /// changing it on every patch would break every reference. So the assembly version of a
    /// <c>0.2.3</c> build is <c>0.0.0.0</c>, which is why <c>spark --version</c> printed exactly
    /// that until this existed, and why an update check reading it would compare every build in
    /// history as identical.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
    public static SparkVersion? Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return TryParse(informational, out SparkVersion version) ? version : null;
    }

    /// <summary>Whether this version comes after another.</summary>
    /// <param name="other">The version to compare against.</param>
    /// <returns>True when this one is newer.</returns>
    public bool IsNewerThan(SparkVersion other) => CompareTo(other) > 0;

    /// <inheritdoc/>
    public int CompareTo(SparkVersion other)
    {
        int numeric = Major.CompareTo(other.Major);
        if (numeric != 0)
        {
            return numeric;
        }

        numeric = Minor.CompareTo(other.Minor);
        if (numeric != 0)
        {
            return numeric;
        }

        numeric = Patch.CompareTo(other.Patch);
        if (numeric != 0)
        {
            return numeric;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    /// <inheritdoc/>
    public bool Equals(SparkVersion other) => CompareTo(other) == 0;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SparkVersion other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(Major, Minor, Patch, Prerelease is null ? 0 : StringComparer.Ordinal.GetHashCode(Prerelease));

    /// <summary>The canonical SemVer string, without a leading <c>v</c>.</summary>
    /// <returns>For example <c>0.2.1-beta.1</c>.</returns>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor}.{Patch}{(Prerelease is null ? string.Empty : "-" + Prerelease)}");

    /// <summary>Whether two versions are the same version.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when they compare equal.</returns>
    public static bool operator ==(SparkVersion left, SparkVersion right) => left.Equals(right);

    /// <summary>Whether two versions are different versions.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when they do not compare equal.</returns>
    public static bool operator !=(SparkVersion left, SparkVersion right) => !left.Equals(right);

    /// <summary>Whether the first version precedes the second.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when the first is older.</returns>
    public static bool operator <(SparkVersion left, SparkVersion right) => left.CompareTo(right) < 0;

    /// <summary>Whether the first version follows the second.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when the first is newer.</returns>
    public static bool operator >(SparkVersion left, SparkVersion right) => left.CompareTo(right) > 0;

    /// <summary>Whether the first version precedes or equals the second.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when the first is not newer.</returns>
    public static bool operator <=(SparkVersion left, SparkVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Whether the first version follows or equals the second.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when the first is not older.</returns>
    public static bool operator >=(SparkVersion left, SparkVersion right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// SemVer §11: a prerelease precedes the release it belongs to, and two prereleases are
    /// compared identifier by identifier.
    /// </summary>
    /// <remarks>
    /// The rules, in the order they apply, because each one exists for a case the previous does not
    /// cover: a version with no prerelease is greater than one with; numeric identifiers compare
    /// numerically, so <c>alpha.10</c> follows <c>alpha.9</c>; a numeric identifier is lower than an
    /// alphanumeric one; and where everything so far is equal, more identifiers beat fewer, so
    /// <c>alpha.1</c> follows <c>alpha</c>.
    /// </remarks>
    private static int ComparePrerelease(string? left, string? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        string[] mine = left.Split('.');
        string[] theirs = right.Split('.');

        for (int i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            bool mineIsNumber = TryNumber(mine[i], out int mineNumber);
            bool theirsIsNumber = TryNumber(theirs[i], out int theirsNumber);

            if (mineIsNumber && theirsIsNumber)
            {
                int numeric = mineNumber.CompareTo(theirsNumber);
                if (numeric != 0)
                {
                    return numeric;
                }

                continue;
            }

            if (mineIsNumber != theirsIsNumber)
            {
                return mineIsNumber ? -1 : 1;
            }

            int ordinal = string.CompareOrdinal(mine[i], theirs[i]);
            if (ordinal != 0)
            {
                return Math.Sign(ordinal);
            }
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    private static int SplitOnDots(ReadOnlySpan<char> span, Span<Range> parts)
    {
        int count = 0;
        int start = 0;

        for (int i = 0; i <= span.Length; i++)
        {
            if (i != span.Length && span[i] != '.')
            {
                continue;
            }

            if (count == parts.Length)
            {
                return count + 1;
            }

            parts[count++] = new Range(start, i);
            start = i + 1;
        }

        return count;
    }

    private static bool TryNumber(ReadOnlySpan<char> span, out int value)
    {
        value = 0;

        if (span.Length == 0)
        {
            return false;
        }

        // Digits only. int.TryParse would accept a leading sign and whitespace, and `1.-2.3` is
        // not a version.
        foreach (char character in span)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return int.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
