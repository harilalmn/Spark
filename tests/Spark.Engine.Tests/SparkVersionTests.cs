using System;
using System.Reflection;
using Spark.Api;

namespace Spark.Engine.Tests;

/// <summary>
/// SemVer parsing and ordering, which is what decides whether a user is told about an update
/// (<c>E12-T21</c>).
/// </summary>
/// <remarks>
/// <b>Every case here is one where string comparison gives the wrong answer</b>, because string
/// comparison is what this type exists instead of. The ordering cases are taken from SemVer §11's
/// own worked example rather than invented, so they are checkable against the specification
/// instead of against my reading of it.
/// </remarks>
public sealed class SparkVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("V0.0.1", 0, 0, 1, null)]
    [InlineData("0.2.0-alpha.1", 0, 2, 0, "alpha.1")]
    [InlineData("v0.2.0-alpha.1+abc1234", 0, 2, 0, "alpha.1")]
    [InlineData("1.2.3+build.7", 1, 2, 3, null)]
    [InlineData("  1.2.3  ", 1, 2, 3, null)]
    public void ParsesTheShapesAReleaseAndAnAssemblyActuallyProduce(
        string text, int major, int minor, int patch, string? prerelease)
    {
        Assert.True(SparkVersion.TryParse(text, out SparkVersion version));

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(prerelease, version.Prerelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    [InlineData("nightly")]
    [InlineData("1.2.-3")]
    [InlineData("1.2.3-")]
    [InlineData("v")]
    public void RefusesWhatIsNotAVersion(string? text)
    {
        Assert.False(SparkVersion.TryParse(text, out _));
    }

    /// <summary>
    /// <b>The case the whole type exists for.</b> As text, <c>"0.10.0" &lt; "0.9.0"</c>, so a check
    /// built on string comparison would stop announcing updates at the tenth minor release and
    /// never start again — and every test written before then would pass.
    /// </summary>
    [Fact]
    public void TenIsNewerThanNine()
    {
        Assert.True(Version("0.10.0").IsNewerThan(Version("0.9.0")));
        Assert.False(Version("0.9.0").IsNewerThan(Version("0.10.0")));

        // And the same trap one component along.
        Assert.True(Version("1.0.10").IsNewerThan(Version("1.0.9")));
        Assert.True(Version("10.0.0").IsNewerThan(Version("9.99.99")));
    }

    /// <summary>SemVer §11's own ordering example, in order.</summary>
    [Fact]
    public void OrdersPrereleasesTheWaySemVerSaysTo()
    {
        string[] ascending =
        [
            "1.0.0-alpha",
            "1.0.0-alpha.1",
            "1.0.0-alpha.beta",
            "1.0.0-beta",
            "1.0.0-beta.2",
            "1.0.0-beta.11",
            "1.0.0-rc.1",
            "1.0.0",
        ];

        for (int i = 1; i < ascending.Length; i++)
        {
            Assert.True(
                Version(ascending[i]).IsNewerThan(Version(ascending[i - 1])),
                $"{ascending[i]} should be newer than {ascending[i - 1]}");
        }
    }

    /// <summary>
    /// <b>A local build between tags must not announce an update to itself.</b>
    /// </summary>
    /// <remarks>
    /// MinVer stamps a build after <c>v0.1.0</c> as <c>0.1.1-alpha.0.5</c>: ahead of the release
    /// that exists, behind the release that does not. Treating the prerelease tail as noise would
    /// make the badge permanent for everybody working on Spark, which is the fastest way to teach
    /// people to ignore it.
    /// </remarks>
    [Fact]
    public void ADevelopmentBuildIsNewerThanTheReleaseItFollows()
    {
        SparkVersion development = Version("0.1.1-alpha.0.5");

        Assert.True(development.IsNewerThan(Version("0.1.0")));
        Assert.False(development.IsNewerThan(Version("0.1.1")));
        Assert.True(development.IsPrerelease);
    }

    [Fact]
    public void EqualVersionsAreNeitherNewerNorOlder()
    {
        Assert.Equal(Version("1.2.3"), Version("v1.2.3+ignored"));
        Assert.False(Version("1.2.3").IsNewerThan(Version("1.2.3")));
        Assert.True(Version("1.2.3") <= Version("1.2.3"));
        Assert.True(Version("1.2.3") >= Version("1.2.3"));
    }

    [Fact]
    public void RoundTripsThroughItsOwnText()
    {
        Assert.Equal("1.2.3", Version("v1.2.3").ToString());
        Assert.Equal("0.2.0-alpha.1", Version("v0.2.0-alpha.1+abc").ToString());
    }

    /// <summary>
    /// <b>The version comes from the informational version, not the assembly version.</b>
    /// </summary>
    /// <remarks>
    /// MinVer truncates <see cref="AssemblyName.Version"/> to <c>major.0.0.0</c> because it
    /// participates in binding, so reading it would compare every build Spark has ever produced as
    /// identical — which is exactly what <c>spark --version</c> printed until this existed.
    /// </remarks>
    [Fact]
    public void ReadsAnAssemblysRealVersion()
    {
        Assembly assembly = typeof(SparkVersion).Assembly;
        SparkVersion? read = SparkVersion.Of(assembly);

        Assert.NotNull(read);

        string informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        Assert.StartsWith(read!.Value.ToString(), informational, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAssemblyIsRequired()
    {
        Assert.Throws<ArgumentNullException>(() => SparkVersion.Of(null!));
    }

    private static SparkVersion Version(string text)
    {
        Assert.True(SparkVersion.TryParse(text, out SparkVersion version), $"'{text}' should parse.");

        return version;
    }
}
