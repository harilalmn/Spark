using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Cylindrical and spherical construction on <see cref="Point3d"/> and <see cref="Vector3d"/> —
/// `E2-T40`, the last of the value-layer parity gaps that is arithmetic rather than fitting.
/// </summary>
/// <remarks>
/// <para>
/// <b>The convention is the whole risk here, not the trigonometry.</b> Spherical coordinates are
/// written two ways that differ by a quarter turn — the second angle is either the <i>polar</i>
/// angle down from <c>+Z</c> or the <i>elevation</i> up from the XY plane — and a call site cannot
/// tell them apart by looking, because both produce plausible points. Spark takes the polar angle.
/// The axis cases below are what pin that down: they are the three places where the two
/// conventions disagree loudly rather than subtly.
/// </para>
/// <para>
/// <b>Nothing here throws.</b> <see cref="Point3d"/> and <see cref="Vector3d"/> accept any three
/// doubles — <see cref="Point3d.Unset"/> <i>is</i> three NaNs — so a non-finite argument has to
/// come back as a value that answers <see langword="false"/> to <c>IsValid</c>, not as an
/// exception. That is the opposite of <see cref="Plane"/>'s rule, where an invalid value is not
/// representable, and the difference is asserted rather than assumed.
/// </para>
/// </remarks>
public sealed class PolarConstructionTests
{
    private const int Places = 12;

    private static void AssertClose(Point3d expected, Point3d actual)
    {
        Assert.Equal(expected.X, actual.X, Places);
        Assert.Equal(expected.Y, actual.Y, Places);
        Assert.Equal(expected.Z, actual.Z, Places);
    }

    private static void AssertClose(Vector3d expected, Vector3d actual)
    {
        Assert.Equal(expected.X, actual.X, Places);
        Assert.Equal(expected.Y, actual.Y, Places);
        Assert.Equal(expected.Z, actual.Z, Places);
    }

    /// <summary>Zero azimuth is <c>+X</c>, a quarter turn is <c>+Y</c>, and the height passes through.</summary>
    [Fact]
    public void CylindricalAxisCasesLieOnTheAxesTheyName()
    {
        AssertClose(new Point3d(2.0, 0.0, 5.0), Point3d.FromCylindrical(2.0, Angle.Zero, 5.0));
        AssertClose(new Point3d(0.0, 2.0, 5.0), Point3d.FromCylindrical(2.0, Angle.QuarterTurn, 5.0));
        AssertClose(new Point3d(-2.0, 0.0, 5.0), Point3d.FromCylindrical(2.0, Angle.HalfTurn, 5.0));

        AssertClose(new Vector3d(2.0, 0.0, 5.0), Vector3d.FromCylindrical(2.0, Angle.Zero, 5.0));
        AssertClose(new Vector3d(0.0, 2.0, 5.0), Vector3d.FromCylindrical(2.0, Angle.QuarterTurn, 5.0));
    }

    /// <summary>
    /// <b>The polar angle is measured from <c>+Z</c>.</b> A zero second angle is the north pole,
    /// not a point in the XY plane, and this is the assertion that says which convention Spark
    /// uses. Under the elevation convention every line below would be wrong.
    /// </summary>
    [Fact]
    public void ThePolarAngleIsMeasuredFromTheZAxisAndNotFromTheXyPlane()
    {
        AssertClose(new Point3d(0.0, 0.0, 3.0), Point3d.FromSpherical(3.0, Angle.Zero, Angle.Zero));
        AssertClose(new Point3d(0.0, 0.0, -3.0), Point3d.FromSpherical(3.0, Angle.Zero, Angle.HalfTurn));
        AssertClose(new Point3d(3.0, 0.0, 0.0), Point3d.FromSpherical(3.0, Angle.Zero, Angle.QuarterTurn));
        AssertClose(
            new Point3d(0.0, 3.0, 0.0),
            Point3d.FromSpherical(3.0, Angle.QuarterTurn, Angle.QuarterTurn));

        AssertClose(new Vector3d(0.0, 0.0, 3.0), Vector3d.FromSpherical(3.0, Angle.Zero, Angle.Zero));
        AssertClose(
            new Vector3d(0.0, 3.0, 0.0),
            Vector3d.FromSpherical(3.0, Angle.QuarterTurn, Angle.QuarterTurn));
    }

    /// <summary>
    /// The radius is the distance it claims to be, everywhere — which is the one invariant that
    /// holds for every angle pair and so catches a factor the axis cases would not.
    /// </summary>
    [Fact]
    public void TheRadiusIsTheDistanceFromTheAxisAndFromTheOrigin()
    {
        for (int i = 0; i < 24; i++)
        {
            Angle azimuth = Angle.FromDegrees(i * 15.0);

            Point3d cylindrical = Point3d.FromCylindrical(4.0, azimuth, 7.0);
            Assert.Equal(
                4.0,
                Math.Sqrt((cylindrical.X * cylindrical.X) + (cylindrical.Y * cylindrical.Y)),
                Places);
            Assert.Equal(7.0, cylindrical.Z, Places);

            for (int j = 0; j <= 12; j++)
            {
                Angle polar = Angle.FromDegrees(j * 15.0);

                Assert.Equal(
                    4.0,
                    Point3d.Origin.DistanceTo(Point3d.FromSpherical(4.0, azimuth, polar)),
                    Places);
                Assert.Equal(4.0, Vector3d.FromSpherical(4.0, azimuth, polar).Length, Places);
            }
        }
    }

    /// <summary>
    /// Spherical construction agrees with cylindrical construction, which is the relation between
    /// the two systems and would break if either had a sine and a cosine the wrong way round.
    /// </summary>
    [Fact]
    public void SphericalAgreesWithCylindricalOnTheSamePoint()
    {
        for (int i = 0; i < 8; i++)
        {
            Angle azimuth = Angle.FromDegrees(i * 45.0);

            for (int j = 1; j < 12; j++)
            {
                Angle polar = Angle.FromDegrees(j * 15.0);

                double radius = 5.0;
                double inPlane = radius * Math.Sin(polar.Radians);
                double height = radius * Math.Cos(polar.Radians);

                AssertClose(
                    Point3d.FromCylindrical(inPlane, azimuth, height),
                    Point3d.FromSpherical(radius, azimuth, polar));
            }
        }
    }

    /// <summary>
    /// A zero radius is the origin whatever the angles say, because there is nowhere else for it
    /// to be. It is worth pinning because it is the one input where the angles stop mattering.
    /// </summary>
    [Fact]
    public void AZeroRadiusIsTheOriginForEveryAngle()
    {
        AssertClose(
            Point3d.Origin,
            Point3d.FromSpherical(0.0, Angle.FromDegrees(37.0), Angle.FromDegrees(64.0)));
        AssertClose(
            Vector3d.Zero,
            Vector3d.FromSpherical(0.0, Angle.FromDegrees(37.0), Angle.FromDegrees(64.0)));
        AssertClose(
            new Point3d(0.0, 0.0, 2.0),
            Point3d.FromCylindrical(0.0, Angle.FromDegrees(37.0), 2.0));
    }

    /// <summary>
    /// <b>A negative radius reflects rather than throwing.</b> It is the same point as the positive
    /// radius half a turn away, and refusing it would make two spellings of one point disagree.
    /// </summary>
    [Fact]
    public void ANegativeRadiusReflectsThroughTheAxis()
    {
        AssertClose(
            Point3d.FromCylindrical(3.0, Angle.FromDegrees(200.0), 1.0),
            Point3d.FromCylindrical(-3.0, Angle.FromDegrees(20.0), 1.0));

        AssertClose(
            Vector3d.FromSpherical(2.0, Angle.FromDegrees(190.0), Angle.FromDegrees(110.0)),
            Vector3d.FromSpherical(-2.0, Angle.FromDegrees(10.0), Angle.FromDegrees(70.0)));
    }

    /// <summary>
    /// A non-finite argument is reported by <c>IsValid</c>, not thrown. See the class remarks:
    /// this is <see cref="Point3d"/>'s convention and it differs from <see cref="Plane"/>'s.
    /// </summary>
    [Fact]
    public void ANonFiniteArgumentGivesAnInvalidValueRatherThanAnException()
    {
        Assert.False(Point3d.FromCylindrical(double.NaN, Angle.Zero, 0.0).IsValid);
        Assert.False(Point3d.FromCylindrical(1.0, Angle.FromRadians(double.NaN), 0.0).IsValid);
        Assert.False(Point3d.FromCylindrical(1.0, Angle.Zero, double.PositiveInfinity).IsValid);
        Assert.False(Point3d.FromSpherical(1.0, Angle.Zero, Angle.FromRadians(double.NaN)).IsValid);
        Assert.False(Vector3d.FromSpherical(double.PositiveInfinity, Angle.Zero, Angle.Zero).IsValid);
    }

    /// <summary>
    /// The constructors `E2-T59` requires are the factories, not a second implementation of them.
    /// </summary>
    [Fact]
    public void TheConstructorsAgreeWithTheFactoriesTheyForwardTo()
    {
        Angle azimuth = Angle.FromDegrees(31.0);
        Angle polar = Angle.FromDegrees(77.0);

        AssertClose(Point3d.FromCylindrical(2.5, azimuth, 1.5), new Point3d(2.5, azimuth, 1.5));
        AssertClose(Point3d.FromSpherical(2.5, azimuth, polar), new Point3d(2.5, azimuth, polar));
        AssertClose(Vector3d.FromCylindrical(2.5, azimuth, 1.5), new Vector3d(2.5, azimuth, 1.5));
        AssertClose(Vector3d.FromSpherical(2.5, azimuth, polar), new Vector3d(2.5, azimuth, polar));
    }
}
