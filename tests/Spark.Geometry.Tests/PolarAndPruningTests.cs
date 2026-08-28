using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Polar construction and duplicate pruning: the last of the near-term Dynamo parity list for
/// values and frames.
/// </summary>
public sealed class PolarAndPruningTests
{
    [Fact]
    public void CylindricalCoordinatesAreMeasuredInThePlanesFrameRatherThanTheWorlds()
    {
        Plane raised = Plane.ByOriginNormalXAxis(
            new Point3d(10.0, 0.0, 0.0),
            Vector3d.ZAxis,
            Vector3d.YAxis);

        // Radius 2 at zero angle runs along the plane's X axis, which is the world Y.
        Point3d point = Point3d.ByCylindricalCoordinates(raised, 2.0, Angle.Zero, 3.0);

        Assert.True(point.EqualsWithin(new Point3d(10.0, 2.0, 3.0)));
    }

    [Fact]
    public void AQuarterTurnGoesFromTheXAxisTowardsTheYAxis()
    {
        Point3d point = Point3d.ByCylindricalCoordinates(Plane.WorldXY, 5.0, Angle.QuarterTurn, 0.0);

        Assert.True(point.EqualsWithin(new Point3d(0.0, 5.0, 0.0)));
    }

    [Fact]
    public void ANegativeRadiusIsTheSameAsAddingHalfATurn()
    {
        Point3d negative = Point3d.ByCylindricalCoordinates(Plane.WorldXY, -5.0, Angle.Zero, 1.0);
        Point3d turned = Point3d.ByCylindricalCoordinates(Plane.WorldXY, 5.0, Angle.HalfTurn, 1.0);

        Assert.True(negative.EqualsWithin(turned));
    }

    [Fact]
    public void SphericalInclinationIsMeasuredFromTheNormalAndNotFromThePlane()
    {
        // Zero inclination is straight up the normal. An elevation convention would put it in
        // the plane, and the two differ by a sign as well as an offset - which is the whole
        // reason this is asserted rather than assumed.
        Assert.True(Point3d.BySphericalCoordinates(Plane.WorldXY, 4.0, Angle.Zero, Angle.Zero)
            .EqualsWithin(new Point3d(0.0, 0.0, 4.0)));

        Assert.True(Point3d.BySphericalCoordinates(Plane.WorldXY, 4.0, Angle.Zero, Angle.QuarterTurn)
            .EqualsWithin(new Point3d(4.0, 0.0, 0.0)));

        Assert.True(Point3d.BySphericalCoordinates(Plane.WorldXY, 4.0, Angle.Zero, Angle.HalfTurn)
            .EqualsWithin(new Point3d(0.0, 0.0, -4.0)));
    }

    [Fact]
    public void EverySphericalPointIsTheRadiusFromTheOrigin()
    {
        foreach (double azimuth in new[] { 0.0, 37.0, 190.0, 359.0 })
        {
            foreach (double inclination in new[] { 0.0, 45.0, 90.0, 135.0, 180.0 })
            {
                Point3d point = Point3d.BySphericalCoordinates(
                    Plane.WorldXY,
                    7.0,
                    Angle.FromDegrees(azimuth),
                    Angle.FromDegrees(inclination));

                Assert.Equal(7.0, point.DistanceTo(Point3d.Origin), 9);
            }
        }
    }

    [Fact]
    public void TheVectorFormsAgreeWithThePointFormsMeasuredFromThePlanesOrigin()
    {
        Plane plane = Plane.ByOriginNormal(new Point3d(3.0, -2.0, 7.0), new Vector3d(1.0, 1.0, 1.0));

        // Two conventions that disagreed by a sign, one on each type, would be a defect nobody
        // could see in either of them alone.
        Assert.True(Vector3d.ByCylindricalCoordinates(plane, 2.0, Angle.FromDegrees(53.0), 1.5)
            .EqualsWithin(Point3d.ByCylindricalCoordinates(plane, 2.0, Angle.FromDegrees(53.0), 1.5) - plane.Origin));

        Assert.True(Vector3d.BySphericalCoordinates(plane, 2.0, Angle.FromDegrees(53.0), Angle.FromDegrees(20.0))
            .EqualsWithin(
                Point3d.BySphericalCoordinates(plane, 2.0, Angle.FromDegrees(53.0), Angle.FromDegrees(20.0))
                - plane.Origin));
    }

    [Fact]
    public void PolarConstructionRefusesADefaultPlaneAndANonFiniteCoordinate()
    {
        Assert.Throws<InvalidOperationException>(
            () => Point3d.ByCylindricalCoordinates(default, 1.0, Angle.Zero, 0.0));

        Assert.Equal(
            "radius",
            Assert.Throws<ArgumentException>(
                () => Point3d.ByCylindricalCoordinates(Plane.WorldXY, double.NaN, Angle.Zero, 0.0)).ParamName);

        Assert.Equal(
            "inclination",
            Assert.Throws<ArgumentException>(
                () => Point3d.BySphericalCoordinates(
                    Plane.WorldXY,
                    1.0,
                    Angle.Zero,
                    Angle.FromRadians(double.PositiveInfinity))).ParamName);
    }

    [Fact]
    public void PruningKeepsTheFirstOfEachCoincidentGroupAndKeepsInputOrder()
    {
        Point3d[] points =
        [
            new(0.0, 0.0, 0.0),
            new(5.0, 0.0, 0.0),
            new(0.0, 0.0, 1e-9),
            new(5.0, 1e-9, 0.0),
            new(9.0, 0.0, 0.0),
        ];

        Point3d[] pruned = Point3d.PruneDuplicates(points, out int[] map);

        Assert.Equal(3, pruned.Length);
        Assert.Equal(points[0], pruned[0]);
        Assert.Equal(points[1], pruned[1]);
        Assert.Equal(points[4], pruned[2]);
        Assert.Equal([0, 1, 0, 1, 2], map);
    }

    [Fact]
    public void TheMapIsWhatAWelderNeedsAndIsNotRecoverableAfterwards()
    {
        Point3d[] points = [new(0.0, 0.0, 0.0), new(1.0, 0.0, 0.0), new(0.0, 0.0, 0.0)];

        Point3d[] pruned = Point3d.PruneDuplicates(points, out int[] map);

        // Every input index lands on a real output index, and the position it lands on is the
        // position it had. Without this a caller has deduplicated points and no way to renumber
        // whatever referred to them.
        for (int index = 0; index < points.Length; index++)
        {
            Assert.InRange(map[index], 0, pruned.Length - 1);
            Assert.True(pruned[map[index]].EqualsWithin(points[index]));
        }
    }

    [Fact]
    public void NothingIsPrunedWhenNothingCoincides()
    {
        Point3d[] points = [.. Enumerable.Range(0, 50).Select(index => new Point3d(index, 0.0, 0.0))];

        Assert.Equal(50, Point3d.PruneDuplicates(points).Length);
    }

    [Fact]
    public void PruningIsGreedyOnAChainRatherThanPretendingCoincidenceIsTransitive()
    {
        // Three points, each 0.6 of a tolerance from the next and 1.2 from the far one. Every
        // partition of such a chain is arbitrary; what is defined is the greedy answer, and this
        // test exists so that the answer is a decision rather than an accident.
        Tolerance tolerance = new(1.0, Angle.FromDegrees(0.001), 1e-12);

        Point3d[] chain = [new(0.0, 0.0, 0.0), new(0.6, 0.0, 0.0), new(1.2, 0.0, 0.0)];

        Point3d[] pruned = Point3d.PruneDuplicates(chain, out int[] map, tolerance);

        Assert.Equal(2, pruned.Length);
        Assert.Equal([0, 0, 1], map);
    }

    [Fact]
    public void PointsThatAreNotFiniteAreDroppedAndSayWhereTheyWent()
    {
        Point3d[] points = [new(0.0, 0.0, 0.0), Point3d.Unset, new(1.0, 0.0, 0.0), Point3d.Unset];

        Point3d[] pruned = Point3d.PruneDuplicates(points, out int[] map);

        Assert.Equal(2, pruned.Length);
        Assert.Equal([0, -1, 1, -1], map);
    }

    [Fact]
    public void PruningAThousandCoincidentPointsLeavesOne()
    {
        Point3d[] points = [.. Enumerable.Repeat(new Point3d(1.0, 2.0, 3.0), 1000)];

        Assert.Single(Point3d.PruneDuplicates(points));
    }

    [Fact]
    public void PruningAgreesWithTheQuadraticSweepItReplaces()
    {
        // The reference implementation is the obvious O(n squared) one. The hierarchy is only
        // worth having if it gives the same answer, and "the same answer" for a greedy rule
        // means the same survivors AND the same map.
        Random random = new(20260828);
        List<Point3d> points = [];

        for (int index = 0; index < 400; index++)
        {
            Point3d point = new(
                Math.Round(random.NextDouble() * 10.0, 2),
                Math.Round(random.NextDouble() * 10.0, 2),
                Math.Round(random.NextDouble() * 10.0, 2));

            points.Add(point);

            // Half the points get a near-coincident twin, so the case being tested actually
            // occurs rather than being hoped for.
            if (index % 2 == 0)
            {
                points.Add(point + new Vector3d(1e-9, 0.0, 0.0));
            }
        }

        Point3d[] pruned = Point3d.PruneDuplicates(points, out int[] map);
        (List<Point3d> expected, int[] expectedMap) = Quadratic(points, Tolerance.Default.Linear);

        Assert.Equal(expected.Count, pruned.Length);
        Assert.Equal(expectedMap, map);

        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index], pruned[index]);
        }
    }

    [Fact]
    public void PruningNullIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => Point3d.PruneDuplicates(null!));
        Assert.Throws<ArgumentNullException>(() => Point3d.PruneDuplicates(null!, out _));
    }

    [Fact]
    public void PruningAnEmptyListIsAnEmptyResultRatherThanAFailure()
    {
        Assert.Empty(Point3d.PruneDuplicates([], out int[] map));
        Assert.Empty(map);
    }

    private static (List<Point3d> Kept, int[] Map) Quadratic(IReadOnlyList<Point3d> points, double linear)
    {
        List<Point3d> kept = [];
        int[] map = new int[points.Count];
        bool[] isRepresentative = new bool[points.Count];

        for (int index = 0; index < points.Count; index++)
        {
            if (!points[index].IsValid)
            {
                map[index] = -1;
                continue;
            }

            int survivor = -1;

            for (int earlier = 0; earlier < index; earlier++)
            {
                // Representatives only, matching the rule under test: following a dropped point
                // to its own survivor would make coincidence transitive along a chain.
                if (isRepresentative[earlier] && points[index].DistanceTo(points[earlier]) <= linear)
                {
                    survivor = map[earlier];
                    break;
                }
            }

            if (survivor >= 0)
            {
                map[index] = survivor;
                continue;
            }

            map[index] = kept.Count;
            isRepresentative[index] = true;
            kept.Add(points[index]);
        }

        return (kept, map);
    }
}
