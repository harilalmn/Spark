using System;
using System.Collections.Generic;
using CsCheck;
using Spark.Geometry;

namespace Spark.Geometry.Properties;

/// <summary>
/// Invariants every curve has to satisfy, across nine decades of working scale in each direction.
/// </summary>
/// <remarks>
/// <para>
/// Each sample builds one of every curve type at the drawn scale and asserts the same invariants
/// on all of them, so a defect in the shared base is found six times and a defect in one override
/// is found once. Assertions are relative to the curve's own length, not absolute: an absolute
/// epsilon is a different test at a scale of 1e-9 than at 1e9, which is exactly the failure
/// [ADR-0018](../../docs/adr/0018-property-based-tests-on-the-kernel.md) exists to prevent.
/// </para>
/// <para>
/// The ellipse is the only curve here whose speed varies, so it is the only one that can tell an
/// arc-length division from a parameter division. Every property below runs over the whole set
/// rather than a chosen one, so that the ellipse is always in the sample.
/// </para>
/// </remarks>
public sealed class CurveProperties
{
    [Fact]
    public void EveryCurveStartsAtZeroLengthAndEndsAtItsFullLength()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Curve curve in CurvesAt(scene))
            {
                double slack = curve.Length * 1e-9;
                Assert.True(
                    curve.PointAtLength(0.0).DistanceTo(curve.StartPoint) <= slack,
                    $"{curve} does not start where its zero length is.");
                Assert.True(
                    curve.PointAtLength(curve.Length).DistanceTo(curve.EndPoint) <= slack,
                    $"{curve} does not end where its full length is.");
                Assert.Equal(0.0, curve.LengthAt(curve.Domain.Min), slack);
                Assert.Equal(curve.Length, curve.LengthAt(curve.Domain.Max), slack);
            }
        });
    }

    [Fact]
    public void LengthAndParameterAreInversesOnEveryCurve()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Curve curve in CurvesAt(scene))
            {
                for (int step = 1; step < 8; step++)
                {
                    double distance = curve.Length * step / 8.0;
                    double round = curve.LengthAt(curve.ParameterAtLength(distance));
                    Assert.Equal(distance, round, curve.Length * 1e-7);
                }
            }
        });
    }

    [Fact]
    public void DividingEquallyGivesSegmentsOfEqualLength()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Curve curve in CurvesAt(scene))
            {
                const int divisions = 7;
                Point3d[] points = curve.DivideEqually(divisions);
                Assert.Equal(divisions + 1, points.Length);

                double expected = curve.Length / divisions;
                for (int index = 0; index < divisions; index++)
                {
                    double from = curve.LengthAt(curve.ParameterAtLength(expected * index));
                    double to = curve.LengthAt(curve.ParameterAtLength(expected * (index + 1)));
                    Assert.Equal(expected, to - from, expected * 1e-6);
                }
            }
        });
    }

    [Fact]
    public void EveryTangentIsAUnitVector()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Curve curve in CurvesAt(scene))
            {
                for (int step = 0; step <= 8; step++)
                {
                    double parameter = curve.Domain.Denormalise(step / 8.0);
                    Assert.Equal(1.0, curve.TangentAt(parameter).Length, 1e-9);
                    Assert.Equal(1.0, curve.NormalAt(parameter).Length, 1e-9);
                    Assert.Equal(0.0, curve.TangentAt(parameter).Dot(curve.NormalAt(parameter)), 1e-9);
                }
            }
        });
    }

    [Fact]
    public void EveryBoundingBoxContainsTheCurveItBounds()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Curve curve in CurvesAt(scene))
            {
                BoundingBox box = curve.BoundingBox;
                Tolerance slack = new(
                    Math.Max(curve.Length * 1e-9, 1e-300), Angle.FromDegrees(0.001), 1e-12);

                foreach (Point3d point in curve.DivideEqually(64))
                {
                    Assert.True(box.Contains(point, slack), $"{box} does not contain {point}.");
                }
            }
        });
    }

    [Fact]
    public void ReversingTwiceTracesTheOriginalCurve()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Curve curve in CurvesAt(scene))
            {
                Curve twice = curve.Reversed().Reversed();
                Assert.Equal(curve.Length, twice.Length, curve.Length * 1e-9);

                for (int step = 0; step <= 8; step++)
                {
                    double distance = curve.Length * step / 8.0;
                    Assert.True(
                        curve.PointAtLength(distance).DistanceTo(twice.PointAtLength(distance))
                            <= curve.Length * 1e-9,
                        $"{curve} does not survive being reversed twice.");
                }
            }
        });
    }

    [Fact]
    public void ReversingSwapsTheEndsAndKeepsThePath()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Curve curve in CurvesAt(scene))
            {
                Curve reversed = curve.Reversed();
                double slack = curve.Length * 1e-9;

                Assert.True(reversed.StartPoint.DistanceTo(curve.EndPoint) <= slack);
                Assert.True(reversed.EndPoint.DistanceTo(curve.StartPoint) <= slack);

                for (int step = 0; step <= 8; step++)
                {
                    double distance = curve.Length * step / 8.0;
                    Assert.True(
                        curve.PointAtLength(distance)
                            .DistanceTo(reversed.PointAtLength(curve.Length - distance)) <= slack,
                        $"{curve} reversed does not retrace its own path.");
                }
            }
        });
    }

    [Fact]
    public void TessellationStartsAndEndsOnTheCurveAndStaysWithinItsTolerance()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Curve curve in CurvesAt(scene))
            {
                double sag = curve.Length * 1e-3;
                Point3d[] points = curve.Tessellate(
                    new Tolerance(sag, Angle.FromDegrees(0.001), 1e-12));

                Assert.True(points.Length >= 2);
                Assert.True(points[0].DistanceTo(curve.StartPoint) <= curve.Length * 1e-9);
                Assert.True(points[^1].DistanceTo(curve.EndPoint) <= curve.Length * 1e-9);

                // A chord walk of the tessellation can never exceed the true arc length, and with
                // this sag it should be within a fraction of a percent of it. Both halves matter:
                // the first catches a tessellation that wanders off the curve, the second catches
                // one that gives up early.
                double walked = 0.0;
                for (int index = 1; index < points.Length; index++)
                {
                    walked += points[index].DistanceTo(points[index - 1]);
                }

                Assert.True(
                    walked <= curve.Length * (1.0 + 1e-9),
                    $"A chord walk of {walked} exceeds the arc length of {curve.Length}.");
                Assert.True(
                    walked >= curve.Length * 0.99,
                    $"A chord walk of {walked} is too far below the arc length of {curve.Length}.");
            }
        });
    }

    [Fact]
    public void ATransformedCurveIsTheTransformOfEveryPointOnIt()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            // A rotation and a uniform scale, which every curve type accepts. A non-uniform one is
            // refused by the circular types by design, and that refusal is tested by example.
            Transform motion = Transform.Rotation(scene.Axis, scene.Turn)
                * Transform.Scale(scene.Factor);

            foreach (Curve curve in CurvesAt(scene))
            {
                Curve moved = curve.TransformedBy(motion);
                double slack = curve.Length * scene.Factor * 1e-8;

                Assert.Equal(curve.Length * scene.Factor, moved.Length, slack);
                for (int step = 0; step <= 8; step++)
                {
                    double fraction = step / 8.0;
                    Point3d expected = motion.OfPoint(curve.PointAtLength(curve.Length * fraction));
                    Point3d actual = moved.PointAtLength(moved.Length * fraction);
                    Assert.True(
                        expected.DistanceTo(actual) <= slack,
                        $"{curve} transformed put {fraction} of the way along at {actual} "
                        + $"rather than {expected}.");
                }
            }
        });
    }

    [Fact]
    public void AnArcThroughThreePointsPassesThroughAllThreeWhereverTheyAre()
    {
        // The one invariant that pins the circumcircle's *orientation* rather than its radius. The
        // arc's direction is carried entirely by the sign of (second - first) × (third - first), so
        // a sign error there still produces an arc through the first and third points and a
        // completely different path between them. Only the middle point can see it.
        Gen.Select(GeometryGenerators.Scenes, Gen.Double[0.05, 0.95], Gen.Double[0.05, 0.95])
            .Sample((scene, firstFraction, secondFraction) =>
            {
                Circle circle = new(scene.Plane, scene.Scale * 0.5);
                double full = circle.Length;

                // Three distinct points in order around the circle, at fractions that keep them
                // apart: the middle one is placed strictly between the other two by construction,
                // so it is genuinely on the arc the method is being asked for.
                Point3d first = circle.PointAtLength(0.0);
                Point3d second = circle.PointAtLength(full * firstFraction * 0.98);
                Point3d third = circle.PointAtLength(full * ((firstFraction * 0.98) + ((1.0 - (firstFraction * 0.98)) * secondFraction)));

                Arc arc = Arc.ByThreePoints(first, second, third);
                double slack = scene.Scale * 1e-9;

                Assert.True(arc.StartPoint.DistanceTo(first) <= slack, "The arc misses its start.");
                Assert.True(arc.EndPoint.DistanceTo(third) <= slack, "The arc misses its end.");

                // The middle point has to be *on* the arc, which is checked by finding the length
                // at which the arc is closest to it and confirming that distance is negligible.
                double best = double.MaxValue;
                foreach (Point3d sample in arc.DivideEqually(2048))
                {
                    best = Math.Min(best, sample.DistanceTo(second));
                }

                Assert.True(
                    best <= scene.Scale * 1e-3,
                    $"The middle point is {best} from the arc at a scale of {scene.Scale}.");
            });
    }

    /// <summary>
    /// One of every curve type, built at the scene's working scale and sharing its frame.
    /// </summary>
    /// <param name="scene">The drawn scale and shapes.</param>
    /// <returns>Six curves: a line, an arc, a circle, an ellipse, a polyline and a polycurve.</returns>
    [Fact]
    public void TheClosestPointIsAlwaysOnTheCurveAndNoFartherThanADenseScanCanFind()
    {
        Gen.Select(GeometryGenerators.Scenes, Gen.Double[-2.0, 2.0], Gen.Double[-2.0, 2.0])
            .Sample((scene, across, along) =>
            {
                Point3d probe = scene.Plane.Origin
                    + (scene.Plane.XAxis * (across * scene.Scale))
                    + (scene.Plane.Normal * (along * scene.Scale));

                foreach (Curve curve in CurvesAt(scene))
                {
                    Tolerance tolerance = Tolerance.ForScale(scene.Scale);
                    double parameter = curve.ParameterAtClosestPoint(probe, tolerance);

                    Assert.InRange(parameter, curve.Domain.Min, curve.Domain.Max);

                    double found = probe.DistanceTo(curve.PointAt(parameter));
                    double bySampling = DenseMinimum(curve, probe);

                    // A 401-sample scan is the reference and it is a WEAK one on purpose: it
                    // cannot beat a real minimiser, so the query must be at least as good. The
                    // slack is relative to the curve's own length, per ADR-0018.
                    Assert.True(
                        found <= bySampling + (1e-6 * curve.Length),
                        $"{curve.GetType().Name}: {found} against {bySampling} at scale {scene.Scale}.");
                }
            });
    }

    [Fact]
    public void ThePointOnACurveNearestToItselfIsItself()
    {
        Gen.Select(GeometryGenerators.Scenes, Gen.Double[0.0, 1.0])
            .Sample((scene, fraction) =>
            {
                foreach (Curve curve in CurvesAt(scene))
                {
                    Point3d on = curve.PointAt(curve.Domain.Min + (curve.Domain.Length * fraction));

                    // The distance, not the parameter: a closed curve reached at its seam has
                    // two parameters for one point, and both are right.
                    double distance = curve.DistanceTo(on, Tolerance.ForScale(scene.Scale));

                    Assert.True(
                        distance <= 1e-6 * curve.Length,
                        $"{curve.GetType().Name}: {distance} against a curve {curve.Length} long.");
                }
            });
    }

    private static double DenseMinimum(Curve curve, in Point3d probe)
    {
        Interval domain = curve.Domain;
        Point3d target = probe;
        double best = double.PositiveInfinity;

        for (int step = 0; step <= 400; step++)
        {
            best = Math.Min(best, curve.PointAt(domain.Min + (domain.Length * step / 400.0)).DistanceTo(target));
        }

        return best;
    }

    private static IEnumerable<Curve> CurvesAt(Scene scene)
    {
        double radius = scene.Scale * 0.5;
        Plane plane = scene.Plane;
        Point3d origin = plane.Origin;

        Line line = new(origin, origin + (plane.XAxis * scene.Scale));
        yield return line;

        yield return Arc.ByPlaneRadiusAngles(
            plane, radius, Angle.FromDegrees(17.0), Angle.FromDegrees(203.0));

        yield return new Circle(plane, radius);

        // Radii of 2:1, which is enough eccentricity that a parameter division and a length
        // division are visibly different rather than merely different in the last digits.
        yield return EllipseCurve.ByPlaneRadii(plane, radius, radius * 0.5);

        PolyLine polyline = PolyLine.ByPoints(
        [
            origin,
            origin + (plane.XAxis * scene.Scale),
            origin + (plane.XAxis * scene.Scale) + (plane.YAxis * (scene.Scale * 0.75)),
        ]);
        yield return polyline;

        Point3d joint = origin + (plane.XAxis * scene.Scale);
        yield return PolyCurve.ByJoinedCurves(
            [
                line,
                new Line(joint, joint + (plane.YAxis * (scene.Scale * 0.6))),
            ],
            new Tolerance(scene.Scale * 1e-12, Angle.FromDegrees(0.001), 1e-12));
    }
}
