using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class PlanarValueTests
{
    [Fact]
    public void ADefaultConstructedPlanarPointIsTheOrigin()
    {
        Assert.Equal(Point2d.Origin, default(Point2d));
        Assert.Equal(Vector2d.Zero, default(Vector2d));
    }

    [Fact]
    public void AnUnsetPlanarPointIsNotValid()
    {
        Assert.False(Point2d.Unset.IsValid);
        Assert.True(Point2d.Origin.IsValid);
    }

    [Fact]
    public void PlanarDistanceMatchesPythagoras()
    {
        Assert.Equal(5.0, new Point2d(0.0, 0.0).DistanceTo(new Point2d(3.0, 4.0)), 12);
        Assert.Equal(25.0, new Point2d(0.0, 0.0).DistanceSquaredTo(new Point2d(3.0, 4.0)), 12);
    }

    [Fact]
    public void SubtractingTwoPlanarPointsGivesTheVectorBetweenThem()
    {
        Point2d from = new(1.0, 1.0);
        Point2d to = new(4.0, 5.0);

        Vector2d displacement = to - from;

        Assert.Equal(new Vector2d(3.0, 4.0), displacement);
        Assert.Equal(to, from + displacement);
        Assert.Equal(from, to - displacement);
    }

    [Fact]
    public void ConvertingBetweenAPlanarPointAndVectorRequiresAnExplicitCast()
    {
        Point2d point = new(1.0, 2.0);

        Assert.Equal(new Vector2d(1.0, 2.0), (Vector2d)point);
        Assert.Equal(point, (Point2d)new Vector2d(1.0, 2.0));
        Assert.Equal((Vector2d)point, point.ToVector2d());
    }

    [Fact]
    public void PlanarLerpReachesEachEndpoint()
    {
        Point2d start = new(0.0, 0.0);
        Point2d end = new(10.0, 20.0);

        Assert.Equal(start, Point2d.Lerp(start, end, 0.0));
        Assert.Equal(end, Point2d.Lerp(start, end, 1.0));
        Assert.Equal(new Point2d(5.0, 10.0), Point2d.Lerp(start, end, 0.5));
        Assert.Equal(new Point2d(5.0, 10.0), start.Midpoint(end));
    }

    [Fact]
    public void ThePlanarCrossProductIsPositiveForACounterClockwisePair()
    {
        Assert.Equal(1.0, Vector2d.XAxis.Cross(Vector2d.YAxis), 12);
        Assert.Equal(-1.0, Vector2d.YAxis.Cross(Vector2d.XAxis), 12);
        Assert.Equal(0.0, Vector2d.Cross(Vector2d.XAxis, Vector2d.XAxis), 12);
    }

    [Fact]
    public void ThePerpendicularOfAPlanarVectorIsAQuarterTurnCounterClockwise()
    {
        Assert.Equal(Vector2d.YAxis, Vector2d.XAxis.Perpendicular());
        Assert.Equal(-Vector2d.XAxis, Vector2d.YAxis.Perpendicular());
    }

    [Fact]
    public void ThePlanarSignedAngleIsPositiveCounterClockwise()
    {
        Assert.Equal(90.0, Vector2d.XAxis.SignedAngleTo(Vector2d.YAxis).Degrees, 9);
        Assert.Equal(-90.0, Vector2d.YAxis.SignedAngleTo(Vector2d.XAxis).Degrees, 9);
        Assert.Equal(90.0, Vector2d.YAxis.AngleTo(Vector2d.XAxis).Degrees, 9);
    }

    [Fact]
    public void ThePlanarAngleToAZeroVectorThrows()
    {
        Assert.Throws<InvalidOperationException>(() => Vector2d.XAxis.AngleTo(Vector2d.Zero));
    }

    [Fact]
    public void RotatingAPlanarVectorByAQuarterTurnCarriesXToY()
    {
        Assert.True(Vector2d.XAxis.Rotate(Angle.FromDegrees(90)).EqualsWithin(Vector2d.YAxis));
    }

    [Fact]
    public void NormalisingAZeroPlanarVectorThrows()
    {
        Assert.Throws<InvalidOperationException>(() => Vector2d.Zero.Normalised());
        Assert.False(Vector2d.Zero.TryNormalise(out Vector2d unit));
        Assert.Equal(Vector2d.Zero, unit);
    }

    [Fact]
    public void NormalisingAPlanarVectorGivesUnitLength()
    {
        Assert.True(new Vector2d(3.0, 4.0).Normalised().IsUnit());
        Assert.Equal(5.0, new Vector2d(3.0, 4.0).Length, 12);
    }

    [Fact]
    public void ASurfaceParameterPairIsADistinctTypeFromAPlanarPoint()
    {
        UV parameter = new(0.25, 0.75);

        Assert.Equal(0.25, parameter.U);
        Assert.Equal(0.75, parameter.V);
        Assert.Equal(UV.Zero, default(UV));
        Assert.False(UV.Unset.IsValid);
    }

    [Fact]
    public void SurfaceParameterArithmeticIsComponentWise()
    {
        UV a = new(1.0, 2.0);
        UV b = new(0.5, 0.5);

        Assert.Equal(new UV(1.5, 2.5), a + b);
        Assert.Equal(new UV(0.5, 1.5), a - b);
        Assert.Equal(new UV(2.0, 4.0), a * 2.0);
        Assert.Equal(new UV(2.0, 4.0), 2.0 * a);
    }

    [Fact]
    public void SurfaceParameterEqualityWithinToleranceIsComponentWise()
    {
        Assert.True(new UV(1.0, 1.0).EqualsWithin(new UV(1.0 + 1e-9, 1.0 - 1e-9)));
        Assert.False(new UV(1.0, 1.0).EqualsWithin(new UV(1.0 + 1e-3, 1.0)));
    }

    [Fact]
    public void PlanarValuesTreatNaNAsEqualToItselfUnderEquals()
    {
        Assert.True(Point2d.Unset.Equals(Point2d.Unset));
        Assert.False(Point2d.Unset == Point2d.Unset);
        Assert.True(UV.Unset.Equals(UV.Unset));
        Assert.False(UV.Unset == UV.Unset);
    }

    [Fact]
    public void PlanarValuesFormatUsingTheInvariantCulture()
    {
        Assert.Equal("(1, 2)", new Point2d(1.0, 2.0).ToString());
        Assert.Equal("(1, 0)", Vector2d.XAxis.ToString());
        Assert.Equal("(0.5, 0.25)", new UV(0.5, 0.25).ToString());
    }
}
