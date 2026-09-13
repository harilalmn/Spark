using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Line.FromBestFit"/>, <see cref="Circle.FromBestFit"/> and
/// <see cref="Arc.FromBestFit"/> — `E2-T72`'s first idea, that Spark had no curve fitting of any
/// kind.
/// </summary>
/// <remarks>
/// <para>
/// <b>A least-squares fit always returns something, so every test here is about the two things that
/// can go wrong silently.</b> The first is a <b>degenerate input answered anyway</b> — collinear
/// points given a circle, isotropic points given a line — which is why the refusals are pinned as
/// hard as the successes. The second is a fit that <b>converges to the wrong place</b>, which is
/// why the successes are pinned against a curve the points were sampled from rather than against
/// the fit's own residual.
/// </para>
/// <para>
/// <b>And the case that decides the circle implementation is a short arc with noise on it.</b> A
/// full circle hides the algebraic fit's bias by symmetry, and exact points hide it because the
/// algebraic fit is then exact. Neither can tell a biased implementation from an unbiased one.
/// Removing the geometric refinement makes
/// <see cref="AShortNoisyArcIsFittedAccuratelyWhenTheNoiseIsSmallBesideTheSagitta"/> report a mean
/// radius of <b>9.58 where the truth is 10</b> — the textbook under-estimate, measured here rather
/// than cited — and leaves every other test in this file green.
/// </para>
/// </remarks>
public sealed class CurveFitTests
{
    private const double Tight = 1e-9;

    /// <summary>A repeatable pseudo-random sequence, so a failure can be reproduced.</summary>
    /// <param name="seed">The seed.</param>
    /// <returns>The generator.</returns>
    private static Random Noise(int seed) => new(seed);

    [Fact]
    public void AFittedLineRecoversTheLineItsPointsCameFrom()
    {
        Point3d start = new(1.0, 2.0, 3.0);
        Vector3d direction = new Vector3d(2.0, -1.0, 3.0).Normalised();

        List<Point3d> points = [];
        for (int index = 0; index <= 20; index++)
        {
            points.Add(start + (direction * (index * 0.5)));
        }

        Line fitted = Line.FromBestFit(points);

        Assert.Equal(1.0, Math.Abs(fitted.Direction.Dot(direction)), Tight);
        Assert.Equal(1.0, fitted.Direction.Dot(direction), Tight);

        // Trimmed to the span of the points, not returned as an unbounded direction.
        Assert.Equal(points[0].X, fitted.StartPoint.X, Tight);
        Assert.Equal(points[^1].Z, fitted.EndPoint.Z, Tight);
        Assert.Equal(10.0, fitted.Length, Tight);
    }

    [Fact]
    public void NoiseOnTheLineMovesTheFitLessThanItMovesThePoints()
    {
        Point3d start = Point3d.Origin;
        Vector3d direction = Vector3d.XAxis;
        Random random = Noise(20260913);

        List<Point3d> points = [];
        for (int index = 0; index <= 200; index++)
        {
            points.Add(new Point3d(
                index * 0.1,
                (random.NextDouble() - 0.5) * 0.2,
                (random.NextDouble() - 0.5) * 0.2));
        }

        Line fitted = Line.FromBestFit(points);

        // The individual points stray up to a tenth; the fitted direction is out by far less,
        // because averaging is what a least-squares fit is for. Asserted as a bound rather than as
        // a number, because the number depends on the seed and the bound does not.
        Assert.True(
            Math.Abs(fitted.Direction.Dot(direction)) > 0.9999,
            $"the fitted direction was {fitted.Direction} for points along {direction}.");

        _ = start;
    }

    [Fact]
    public void PointsWithNoPreferredDirectionAreRefusedRatherThanFitted()
    {
        // `E2-T72`: the branch this proves is the ambiguity refusal. A least-squares line exists
        // for these points — every line through the centre of the ring fits equally well — and
        // returning one would be inventing an answer. It is the exact mirror of
        // Plane.FromBestFit refusing collinear points, which is why both live in one file.
        List<Point3d> ring = [];
        for (int index = 0; index < 12; index++)
        {
            double angle = index * Math.PI * 2.0 / 12.0;
            ring.Add(new Point3d(Math.Cos(angle), Math.Sin(angle), 0.0));
        }

        ArgumentException error = Assert.Throws<ArgumentException>(() => Line.FromBestFit(ring));
        Assert.Equal("points", error.ParamName);
        Assert.Contains("no preferred direction", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CoincidentPointsHaveNoLine()
    {
        Point3d point = new(1.0, 1.0, 1.0);

        Assert.Throws<ArgumentException>(() => Line.FromBestFit([point, point, point]));
    }

    [Fact]
    public void AFittedCircleRecoversTheCircleItsPointsCameFrom()
    {
        Plane tilted = Plane.FromOriginNormal(new Point3d(3.0, -1.0, 2.0), new Vector3d(1.0, 2.0, 2.0));
        Circle original = Circle.FromPlaneRadius(tilted, 4.5);

        List<Point3d> points = [];
        for (int index = 0; index < 16; index++)
        {
            points.Add(original.PointAt(index * Math.PI * 2.0 / 16.0));
        }

        Circle fitted = Circle.FromBestFit(points);

        Assert.Equal(4.5, fitted.Radius, 1e-10);
        Assert.Equal(0.0, fitted.Center.DistanceTo(original.Center), 1e-10);
        Assert.Equal(1.0, Math.Abs(fitted.Plane.Normal.Dot(tilted.Normal)), 1e-12);
    }

    [Fact]
    public void AShortArcOfExactPointsStillRecoversTheWholeCircle()
    {
        // Twenty degrees of a circle of radius ten, which is where an algebraic fit's bias lives —
        // and on EXACT points there is no bias to find, because the algebraic system is satisfied
        // exactly whatever arc the points cover. This is the test that would pass on a biased
        // implementation, and it is here to say so.
        Circle original = Circle.FromCenterRadius(new Point3d(5.0, 5.0, 0.0), 10.0);

        List<Point3d> points = [];
        for (int index = 0; index < 9; index++)
        {
            points.Add(original.PointAt(Angle.FromDegrees(index * 2.5).Radians));
        }

        Circle fitted = Circle.FromBestFit(points);

        Assert.Equal(10.0, fitted.Radius, 1e-7);
        Assert.Equal(0.0, fitted.Center.DistanceTo(original.Center), 1e-7);
    }

    [Fact]
    public void AShortNoisyArcIsFittedAccuratelyWhenTheNoiseIsSmallBesideTheSagitta()
    {
        // `E2-T72`: THE TEST THAT DECIDES THE IMPLEMENTATION. Take the Gauss–Newton refinement
        // out and leave the algebraic fit's own radius, and this reports a mean of 9.58 against a
        // truth of 10 — a four-per-cent under-estimate, which is the documented bias of the
        // algebraic fit on a short arc, reproduced rather than cited. Every other test in this
        // file stays green, which is exactly why this one has to exist.
        //
        // Twenty degrees of a circle of radius ten. The sagitta — how far the arc bulges from its
        // own chord — is about 0.15, and THAT is the signal a radius is recovered from, not the
        // radius. A wobble of 0.05 is a third of it, which is already a hard fit; anything
        // approaching the sagitta makes the radius genuinely indeterminate rather than merely
        // hard, and no implementation can rescue that. Worth stating, because a test tuned past
        // that point measures the conditioning of the problem and not the quality of the code.
        const double radius = 10.0;
        Point3d centre = new(5.0, 5.0, 0.0);
        Random random = Noise(20260913);

        double total = 0.0;
        const int draws = 200;

        for (int draw = 0; draw < draws; draw++)
        {
            List<Point3d> points = [];

            for (int index = 0; index < 11; index++)
            {
                double angle = Angle.FromDegrees(index * 2.0).Radians;
                double wobble = (random.NextDouble() - 0.5) * 0.05;

                points.Add(new Point3d(
                    centre.X + ((radius + wobble) * Math.Cos(angle)),
                    centre.Y + ((radius + wobble) * Math.Sin(angle)),
                    0.0));
            }

            total += Circle.FromBestFit(points).Radius;
        }

        double mean = total / draws;

        Assert.True(
            Math.Abs(mean - radius) < radius * 0.01,
            $"the mean fitted radius was {mean} for a true radius of {radius}.");
    }

    [Fact]
    public void TheFittedCircleMinimisesTheDistanceResidualAndNotTheAlgebraicOne()
    {
        // The same claim as the test above, stated as a PROPERTY rather than as a number: the
        // answer is a minimum of the distance residual Σ(|p−c| − r)², which is what anybody fitting
        // a circle means, and not of the algebraic Σ(|p−c|² − r²)², which is a different function
        // with a different minimum. Nudge the centre and the residual must get worse every way.
        //
        // It is the weaker of the two and is kept anyway, because it holds for every input rather
        // than for one noise seed — and because the algebraic fit's error is mostly in the RADIUS,
        // so its centre can sit close enough to the minimum to survive a nudge this size. The
        // number above is what catches the bias; this is what says the refinement converged.
        Random random = Noise(4242);
        List<Point3d> points = [];

        for (int index = 0; index < 11; index++)
        {
            double angle = Angle.FromDegrees(index * 3.0).Radians;
            double wobble = (random.NextDouble() - 0.5) * 0.05;

            points.Add(new Point3d(
                (10.0 + wobble) * Math.Cos(angle), (10.0 + wobble) * Math.Sin(angle), 0.0));
        }

        Circle fitted = Circle.FromBestFit(points);
        double best = Residual(points, fitted.Center);

        foreach (Vector3d nudge in (Vector3d[])[
            new(1.0, 0.0, 0.0), new(-1.0, 0.0, 0.0), new(0.0, 1.0, 0.0), new(0.0, -1.0, 0.0),
            new(0.7, 0.7, 0.0), new(-0.7, 0.7, 0.0), new(0.7, -0.7, 0.0), new(-0.7, -0.7, 0.0)])
        {
            double moved = Residual(points, fitted.Center + (nudge * 0.02));

            Assert.True(
                moved > best,
                $"moving the centre by {nudge * 0.02} improved the distance residual "
                + $"from {best} to {moved}, so the fit was not at its minimum.");
        }
    }

    /// <summary>
    /// The geometric residual of a circle centred at a point: the sum of squared differences
    /// between each point's distance and the mean distance, which is the best radius for that
    /// centre.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <param name="centre">The candidate centre.</param>
    /// <returns>The residual.</returns>
    private static double Residual(IReadOnlyList<Point3d> points, in Point3d centre)
    {
        double mean = 0.0;

        foreach (Point3d point in points)
        {
            mean += point.DistanceTo(centre);
        }

        mean /= points.Count;

        double total = 0.0;

        foreach (Point3d point in points)
        {
            double error = point.DistanceTo(centre) - mean;
            total += error * error;
        }

        return total;
    }

    [Fact]
    public void CollinearPointsHaveNoCircle()
    {
        List<Point3d> line =
        [
            Point3d.Origin,
            new Point3d(1.0, 1.0, 1.0),
            new Point3d(2.0, 2.0, 2.0),
            new Point3d(3.0, 3.0, 3.0),
        ];

        ArgumentException error = Assert.Throws<ArgumentException>(() => Circle.FromBestFit(line));
        Assert.Equal("points", error.ParamName);
    }

    [Fact]
    public void TwoPointsAreNotEnoughForACircle()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Circle.FromBestFit([Point3d.Origin, new Point3d(1.0, 0.0, 0.0)]));

        Assert.Contains("three", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFittedArcSpansThePointsTheWayTheyAreOrdered()
    {
        Arc original = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, 3.0, Angle.FromDegrees(30.0), Angle.FromDegrees(150.0));

        List<Point3d> points = [];
        for (int index = 0; index <= 10; index++)
        {
            points.Add(original.PointAt(original.Domain.Denormalise(index / 10.0)));
        }

        Arc fitted = Arc.FromBestFit(points);

        Assert.Equal(3.0, fitted.Radius, 1e-9);
        Assert.Equal(150.0, fitted.SweepAngle.Degrees, 1e-7);
        Assert.Equal(0.0, fitted.StartPoint.DistanceTo(original.StartPoint), 1e-8);
        Assert.Equal(0.0, fitted.EndPoint.DistanceTo(original.EndPoint), 1e-8);
    }

    [Fact]
    public void AFittedArcCrossingThePlanesXAxisGetsItsSweepRight()
    {
        // The reason the sweep is accumulated step by step rather than taken as the difference
        // between the first and last angles: these points run from 350° to 20°, and a first-to-last
        // difference reads that as -330° instead of +30°.
        Arc original = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, 2.0, Angle.FromDegrees(350.0), Angle.FromDegrees(30.0));

        List<Point3d> points = [];
        for (int index = 0; index <= 6; index++)
        {
            points.Add(original.PointAt(original.Domain.Denormalise(index / 6.0)));
        }

        Arc fitted = Arc.FromBestFit(points);

        Assert.Equal(30.0, fitted.SweepAngle.Degrees, 1e-7);
        Assert.Equal(0.0, fitted.EndPoint.DistanceTo(original.EndPoint), 1e-8);
    }

    [Fact]
    public void PointsThatDoubleBackAreRefusedRatherThanFittedToWhateverTheySumTo()
    {
        // Shuffled points still have a perfectly good CIRCLE through them, so the fit succeeds and
        // only the sweep is nonsense — which is exactly the shape of failure that gets shipped.
        Circle circle = Circle.FromCenterRadius(Point3d.Origin, 2.0);

        List<Point3d> outOfOrder =
        [
            circle.PointAt(0.0),
            circle.PointAt(1.0),
            circle.PointAt(0.5),
            circle.PointAt(1.5),
        ];

        ArgumentException error = Assert.Throws<ArgumentException>(() => Arc.FromBestFit(outOfOrder));
        Assert.Equal("points", error.ParamName);
        Assert.Contains("one direction", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFitIsAlsoAConstructor()
    {
        // `E2-T59`, and ConstructorParityTests would fail the build without these — but a
        // constructor that compiled and did something else would not be caught by it, so the
        // agreement is asserted here.
        List<Point3d> points = [];
        Circle original = Circle.FromCenterRadius(Point3d.Origin, 3.0);

        for (int index = 0; index < 8; index++)
        {
            points.Add(original.PointAt(index * Math.PI / 8.0));
        }

        Assert.Equal(Circle.FromBestFit(points).Radius, new Circle(points).Radius, Tight);
        Assert.Equal(Arc.FromBestFit(points).Radius, new Arc(points).Radius, Tight);

        List<Point3d> straight =
        [
            Point3d.Origin,
            new Point3d(1.0, 0.0, 0.0),
            new Point3d(2.0, 0.1, 0.0),
            new Point3d(3.0, 0.0, 0.0),
        ];

        Assert.Equal(Line.FromBestFit(straight).Length, new Line(straight).Length, Tight);
    }
}
