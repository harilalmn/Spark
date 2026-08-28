using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Spark.Geometry;

namespace Spark.Geometry.Properties;

/// <summary>
/// Invariants of the value layer that must hold for every input, not just the worked examples.
/// </summary>
/// <remarks>
/// Every property here runs across nine decades of working scale in each direction, per
/// ADR-0018. Assertions use the scale-proportional tolerances on <see cref="Scene"/> rather
/// than one fixed epsilon, because a fixed epsilon at this range either passes everything or
/// fails everything.
/// </remarks>
public sealed class ValueLayerProperties
{
    [Fact]
    public void ATransformComposedWithItsOwnInverseIsTheIdentity()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Transform motion = scene.Motion;

            Assert.True(motion.TryGetInverse(out Transform inverse));
            Assert.True((motion * inverse).IsIdentity(scene.MatrixTolerance));
            Assert.True((inverse * motion).IsIdentity(scene.MatrixTolerance));
        });
    }

    [Fact]
    public void ATransformFollowedByItsInverseReturnsEveryPointToWhereItStarted()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Transform motion = scene.Motion;

            Assert.True(motion.TryGetInverse(out Transform inverse));
            Assert.True(inverse.OfPoint(motion.OfPoint(scene.SecondPoint))
                .EqualsWithin(scene.SecondPoint, scene.PositionTolerance));
        });
    }

    [Fact]
    public void NormalisingAndDenormalisingAnIntervalRoundTrips()
    {
        Gen.Select(GeometryGenerators.Scenes, Gen.Double[-2.0, 3.0])
            .Sample((scene, parameter) =>
            {
                Interval domain = scene.Domain;
                double value = domain.Denormalise(parameter);
                double roundTripped = domain.Denormalise(domain.Normalise(value));

                Assert.True(scene.PositionTolerance.AreEqual(value, roundTripped));
            });
    }

    [Fact]
    public void DenormalisingAndNormalisingAnIntervalRoundTrips()
    {
        Gen.Select(GeometryGenerators.Scenes, Gen.Double[-2.0, 3.0])
            .Sample((scene, parameter) =>
            {
                Interval domain = scene.Domain;
                double roundTripped = domain.Normalise(domain.Denormalise(parameter));

                Assert.True(GeometryGenerators.Dimensionless.AreEqual(parameter, roundTripped));
            });
    }

    [Fact]
    public void NormalisingTheBoundsOfAnIntervalGivesZeroAndOne()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Interval domain = scene.Domain;

            Assert.True(GeometryGenerators.Dimensionless.AreEqual(0.0, domain.Normalise(domain.Min)));
            Assert.True(GeometryGenerators.Dimensionless.AreEqual(1.0, domain.Normalise(domain.Max)));
        });
    }

    [Fact]
    public void AnIntervalIncludesEveryValueBetweenItsBoundsAndNothingBeyondThem()
    {
        Gen.Select(GeometryGenerators.Scenes, Gen.Double[-2.0, 3.0])
            .Sample((scene, parameter) =>
            {
                Interval domain = scene.Domain;
                double value = domain.Denormalise(parameter);

                // The band from -0.02 to 1.02 is left unasserted on purpose: Includes widens
                // both ends by the tolerance, so a parameter a hair outside [0, 1] is
                // legitimately included and pinning that boundary would test the arithmetic of
                // the tolerance rather than the predicate.
                if (parameter is >= 0.02 and <= 0.98)
                {
                    Assert.True(domain.Includes(value, scene.PositionTolerance));
                }
                else if (parameter is < (-0.02) or > 1.02)
                {
                    Assert.False(domain.Includes(value, scene.PositionTolerance));
                }

                Assert.False(domain.Includes(double.NaN, scene.PositionTolerance));
            });
    }

    [Fact]
    public void IntersectAgreesWithIncludesAboutEveryValueItReturns()
    {
        Gen.Select(GeometryGenerators.Scenes, GeometryGenerators.Scenes)
            .Sample((first, second) =>
            {
                Interval left = first.Domain;
                Interval right = second.Domain;
                Interval? overlap = left.Intersect(right);

                if (overlap is not { } shared)
                {
                    return;
                }

                Assert.True(left.Includes(shared.Min) && right.Includes(shared.Min));
                Assert.True(left.Includes(shared.Max) && right.Includes(shared.Max));
            });
    }

    [Fact]
    public void ConvertingAPointOnAPlaneToTwoDimensionsAndBackReturnsTheSamePoint()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Plane plane = scene.Plane;
            Point3d onPlane = plane.To3d(scene.Planar);

            Assert.True(plane.Contains(onPlane, scene.PositionTolerance));
            Assert.True(plane.To3d(plane.To2d(onPlane))
                .EqualsWithin(onPlane, scene.PositionTolerance));
        });
    }

    [Fact]
    public void ConvertingPlanarCoordinatesToThreeDimensionsAndBackReturnsTheSameCoordinates()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Plane plane = scene.Plane;

            Assert.True(plane.To2d(plane.To3d(scene.Planar))
                .EqualsWithin(scene.Planar, scene.PositionTolerance));
        });
    }

    [Fact]
    public void TheUnionOfTwoBoundingBoxesIsCommutativeAndContainsBothInputs()
    {
        Gen.Select(GeometryGenerators.Boxes, GeometryGenerators.Boxes)
            .Sample((first, second) =>
            {
                BoundingBox union = first.Union(second);

                Assert.Equal(union, second.Union(first));
                Assert.True(union.Contains(first));
                Assert.True(union.Contains(second));
                Assert.True(union.Volume >= first.Volume);
                Assert.True(union.Volume >= second.Volume);
            });
    }

    [Fact]
    public void UnioningABoundingBoxWithItselfChangesNothing()
    {
        GeometryGenerators.Boxes.Sample(box => Assert.Equal(box, box.Union(box)));
    }

    [Fact]
    public void EveryCornerOfABoundingBoxIsContainedInIt()
    {
        GeometryGenerators.Boxes.Sample(box =>
        {
            foreach (Point3d corner in box.Corners())
            {
                Assert.True(box.Contains(corner));
            }
        });
    }

    [Fact]
    public void TheClosestPointOnABoundingBoxIsAlwaysInsideIt()
    {
        Gen.Select(GeometryGenerators.Boxes, GeometryGenerators.Points)
            .Sample((box, point) => Assert.True(box.Contains(box.ClosestPoint(point))));
    }

    [Fact]
    public void RotatingByAnAngleAndThenByItsNegativeReturnsTheOriginalPoint()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Transform forward = Transform.Rotation(scene.Axis, scene.Turn, scene.FirstPoint);
            Transform backward = Transform.Rotation(scene.Axis, -scene.Turn, scene.FirstPoint);

            Assert.True(backward.OfPoint(forward.OfPoint(scene.SecondPoint))
                .EqualsWithin(scene.SecondPoint, scene.PositionTolerance));
        });
    }

    [Fact]
    public void RotatingAVectorPreservesItsLength()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Vector3d vector = scene.Second * scene.Scale;
            Vector3d rotated = vector.Rotate(scene.Axis, scene.Turn);

            Assert.True(scene.PositionTolerance.AreEqual(vector.Length, rotated.Length));
        });
    }

    [Fact]
    public void TheCrossProductIsPerpendicularToBothOperands()
    {
        Gen.Select(GeometryGenerators.Vectors, GeometryGenerators.Vectors)
            .Sample((first, second) =>
            {
                Vector3d cross = Vector3d.Cross(first, second);

                if (!cross.TryNormalise(out Vector3d direction))
                {
                    // The operands were parallel, so there is no perpendicular to check.
                    return;
                }

                Assert.True(GeometryGenerators.Dimensionless.IsZero(direction.Dot(first.Normalised())));
                Assert.True(GeometryGenerators.Dimensionless.IsZero(direction.Dot(second.Normalised())));
            });
    }

    [Fact]
    public void TheCrossProductReversesWhenItsOperandsAreExchanged()
    {
        Gen.Select(GeometryGenerators.Vectors, GeometryGenerators.Vectors)
            .Sample((first, second) => Assert.Equal(
                Vector3d.Cross(second, first),
                -Vector3d.Cross(first, second)));
    }

    [Fact]
    public void NormalisingAVectorAlwaysGivesUnitLength()
    {
        GeometryGenerators.Vectors.Sample(vector =>
            Assert.True(vector.Normalised().IsUnit(GeometryGenerators.Dimensionless)));
    }

    [Fact]
    public void TheSignedAngleBetweenTwoVectorsDoesNotDependOnTheirLengths()
    {
        Gen.Select(GeometryGenerators.Scenes, GeometryGenerators.Scales, GeometryGenerators.Scales)
            .Sample((scene, firstScale, secondScale) =>
            {
                Vector3d a = scene.Axis;
                Vector3d b = scene.Axis.Rotate(PerpendicularTo(scene.Axis), scene.Turn);

                if (!a.Cross(b).TryNormalise(out Vector3d reference))
                {
                    return;
                }

                Angle atUnitLength = a.SignedAngleTo(b, reference);
                Angle atOtherLengths = (a * firstScale).SignedAngleTo(b * secondScale, reference);

                Assert.True(atUnitLength.EqualsWithin(atOtherLengths));

                // The sign is only a fact away from the two angles at which it carries no
                // information. At zero and at a half turn the true answer sits on the boundary
                // between +eps and -eps, and scaling either vector moves the result across it
                // by rounding alone - Math.Sign then reports 0 against 1 for two angles that
                // agree to fifty digits. This test asserted through that boundary and failed
                // about once in twenty-five thousand samples, which at a hundred samples a run
                // is a red build every few weeks with a different seed each time. NOTES N35.
                double fromBoundary = Math.Min(
                    Math.Abs(atUnitLength.Radians),
                    Math.Abs(Math.PI - Math.Abs(atUnitLength.Radians)));

                if (fromBoundary > 1e-9)
                {
                    Assert.Equal(Math.Sign(atUnitLength.Radians), Math.Sign(atOtherLengths.Radians));
                }
            });
    }

    [Fact]
    public void AnAngleNormalisesIntoTheHalfOpenRangeOfAFullTurn()
    {
        GeometryGenerators.Angles.Sample(angle =>
        {
            double normalised = angle.Normalised().Radians;

            Assert.True(normalised >= 0.0);
            Assert.True(normalised < 2.0 * Math.PI);
            Assert.True(angle.EqualsWithin(angle.Normalised()));
        });
    }

    [Fact]
    public void SignedNormalisationStaysWithinHalfATurnOfZero()
    {
        GeometryGenerators.Angles.Sample(angle =>
        {
            double signed = angle.NormalisedSigned().Radians;

            Assert.True(signed > -Math.PI);
            Assert.True(signed <= Math.PI);
            Assert.True(angle.EqualsWithin(angle.NormalisedSigned()));
        });
    }

    [Fact]
    public void AQuaternionRotationAlwaysAgreesWithTheEquivalentTransform()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Quaternion rotation = Quaternion.ByAxisAngle(scene.Axis, scene.Turn);
            Vector3d subject = scene.Second * scene.Scale;

            Assert.True(rotation.Rotate(subject)
                .EqualsWithin(Transform.Rotation(scene.Axis, scene.Turn).OfVector(subject), scene.PositionTolerance));
        });
    }

    [Fact]
    public void RotatingByAQuaternionNeverChangesALength()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Vector3d subject = scene.Second * scene.Scale;
            Vector3d rotated = Quaternion.ByAxisAngle(scene.Axis, scene.Turn).Rotate(subject);

            Assert.True(scene.PositionTolerance.AreEqual(subject.Length, rotated.Length));
        });
    }

    [Fact]
    public void AQuaternionComposedWithItsOwnInverseIsTheIdentityRotation()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Quaternion rotation = Quaternion.ByAxisAngle(scene.Axis, scene.Turn);

            Assert.True((rotation * rotation.Inverse())
                .RepresentsSameRotationAs(Quaternion.Identity, GeometryGenerators.Dimensionless));
            Assert.True((rotation.Inverse() * rotation)
                .RepresentsSameRotationAs(Quaternion.Identity, GeometryGenerators.Dimensionless));
        });
    }

    [Fact]
    public void ARotationSurvivesTheRoundTripThroughATransformAsARotation()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Quaternion rotation = Quaternion.ByAxisAngle(scene.Axis, scene.Turn);

            // As a ROTATION, not as a value: ByRotation returns the representative with a
            // non-negative scalar part, so half of all inputs come back negated. Asserting
            // componentwise equality here would be asserting a fact about the double cover
            // that is not true.
            Assert.True(Quaternion.ByRotation(rotation.ToTransform())
                .RepresentsSameRotationAs(rotation, GeometryGenerators.Dimensionless));
        });
    }

    [Fact]
    public void AQuaternionAndItsNegationAlwaysRotateEveryVectorIdentically()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Quaternion rotation = Quaternion.ByAxisAngle(scene.Axis, scene.Turn);
            Quaternion negated = new(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
            Vector3d subject = scene.Second * scene.Scale;

            Assert.True(negated.Rotate(subject).EqualsWithin(rotation.Rotate(subject), scene.PositionTolerance));
            Assert.True(negated.RepresentsSameRotationAs(rotation, GeometryGenerators.Dimensionless));
        });
    }

    [Fact]
    public void AxisAndAngleAlwaysReconstructTheRotationTheyWereReadFrom()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Quaternion rotation = Quaternion.ByAxisAngle(scene.Axis, scene.Turn);
            Vector3d subject = scene.Second * scene.Scale;

            // The angle comes back folded into [0, pi] and the axis flips to match, so this
            // is a statement about the rotation rather than about the numbers.
            Vector3d reconstructed = Transform.Rotation(rotation.Axis, rotation.Angle).OfVector(subject);

            Assert.True(reconstructed.EqualsWithin(rotation.Rotate(subject), scene.PositionTolerance));
        });
    }

    [Fact]
    public void ByTwoVectorsAlwaysTakesTheFirstDirectionToTheSecond()
    {
        Gen.Select(GeometryGenerators.UnitVectors, GeometryGenerators.UnitVectors)
            .Sample((from, to) =>
            {
                Assert.True(Quaternion.ByTwoVectors(from, to)
                    .Rotate(from)
                    .EqualsWithin(to, GeometryGenerators.Dimensionless));
            });
    }

    [Fact]
    public void SlerpStaysOnTheUnitSphereAndEndsWhereItSaysItWill()
    {
        Gen.Select(GeometryGenerators.Scenes, GeometryGenerators.UnitVectors, Gen.Double[0.0, 1.0])
            .Sample((scene, secondAxis, t) =>
            {
                Quaternion from = Quaternion.ByAxisAngle(scene.Axis, scene.Turn);
                Quaternion to = Quaternion.ByAxisAngle(secondAxis, scene.Turn * 0.5);

                Assert.True(Quaternion.Slerp(from, to, t).IsUnit(GeometryGenerators.Dimensionless));
                Assert.True(Quaternion.Slerp(from, to, 0.0)
                    .RepresentsSameRotationAs(from, GeometryGenerators.Dimensionless));
                Assert.True(Quaternion.Slerp(from, to, 1.0)
                    .RepresentsSameRotationAs(to, GeometryGenerators.Dimensionless));
            });
    }

    [Fact]
    public void AnOffsetPlaneIsAlwaysParallelAtTheDistanceAsked()
    {
        Gen.Select(GeometryGenerators.Scenes, Gen.Double[-2.0, 2.0])
            .Sample((scene, factor) =>
            {
                Plane plane = scene.Plane;
                double distance = factor * scene.Scale;
                Plane offset = plane.Offset(distance);

                Assert.True(offset.Normal.EqualsWithin(plane.Normal, GeometryGenerators.Dimensionless));
                Assert.True(scene.PositionTolerance.AreEqual(plane.DistanceTo(offset.Origin), distance));
                Assert.True(offset.Offset(-distance).EqualsWithin(plane, scene.PositionTolerance));
            });
    }

    [Fact]
    public void TheIntersectionOfTwoBoxesIsContainedInBoth()
    {
        Gen.Select(GeometryGenerators.Boxes, GeometryGenerators.Boxes)
            .Sample((first, second) =>
            {
                BoundingBox overlap = first.Intersection(second);

                // Empty is contained in everything, so this holds for disjoint boxes too, and
                // it is the one statement that covers both branches of the member.
                Assert.True(first.Contains(overlap));
                Assert.True(second.Contains(overlap));
                Assert.Equal(overlap, second.Intersection(first));
            });
    }

    [Fact]
    public void ARayAlwaysHitsABoxItsOwnPointsAreInside()
    {
        Gen.Select(GeometryGenerators.Scenes, Gen.Double[0.0, 10.0])
            .Sample((scene, along) =>
            {
                Ray ray = new(scene.FirstPoint, scene.Axis);
                Point3d ahead = ray.PointAt(along * scene.Scale);

                // A box built around a point ON the ray must be hit by it. This is the one
                // statement about the slab test that cannot be satisfied by a version that
                // reports NaN as a miss, because the box is axis-aligned around a point the
                // ray passes exactly through.
                BoundingBox around = new BoundingBox(ahead, ahead).Inflated(0.01 * scene.Scale);

                Assert.True(ray.Intersects(around));
            });
    }

    [Fact]
    public void ARayIntersectionSpanAlwaysStaysInsideTheBox()
    {
        Gen.Select(GeometryGenerators.Scenes, GeometryGenerators.Boxes)
            .Sample((scene, box) =>
            {
                Ray ray = new(scene.FirstPoint, scene.Axis);

                if (!ray.TryIntersect(box, out Interval span))
                {
                    return;
                }

                Assert.True(span.Min >= 0.0);
                Assert.True(span.Min <= span.Max);

                // Scale-aware, because the entry point is computed at the ray's scale and the
                // box at its own; a fixed epsilon here would fail at 1e9 and pass at 1e-9.
                Tolerance tolerance = Tolerance.ForScale(Math.Max(scene.Scale, box.Diagonal.Length));

                Assert.True(box.Contains(ray.PointAt(span.Min), tolerance));
                Assert.True(box.Contains(ray.PointAt(0.5 * (span.Min + span.Max)), tolerance));
            });
    }

    [Fact]
    public void TheNearestPointOnARayIsNeverBehindItsOrigin()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Ray ray = new(scene.FirstPoint, scene.Axis);
            Point3d closest = ray.ClosestPoint(scene.SecondPoint);

            Assert.True((closest - ray.Origin).Dot(ray.Direction) >= -scene.PositionTolerance.Linear);
            Assert.True(ray.DistanceTo(scene.SecondPoint)
                <= ray.Origin.DistanceTo(scene.SecondPoint) + scene.PositionTolerance.Linear);
        });
    }

    [Fact]
    public void ABvhRaySweepAlwaysReturnsExactlyWhatTheLinearScanReturns()
    {
        Gen.Select(GeometryGenerators.Scenes, Gen.Int[1, 60])
            .Sample((scene, count) =>
            {
                BoundingBox[] boxes = ScatteredBoxes(scene, count);
                Bvh<int> tree = Bvh<int>.Build([.. Enumerable.Range(0, boxes.Length)], index => boxes[index]);
                Ray ray = new(scene.FirstPoint, scene.Axis);

                List<int> found = [];
                tree.Hit(ray, found);

                // The linear scan is the reference implementation, and it is the only one that
                // is obviously right. Set equality rather than sequence equality, because the
                // tree returns items in its own order and says so.
                //
                // Checked non-vacuous before being trusted: over a default CsCheck run, 60 of
                // the samples find more than one hit. A property that compared two empty sets
                // every time would pass exactly as loudly as this one, which is the trap this
                // repository has already fallen into twice.
                HashSet<int> expected = [.. Enumerable.Range(0, boxes.Length).Where(index => ray.Intersects(boxes[index]))];

                Assert.True(expected.SetEquals(found));
            });
    }

    [Fact]
    public void ABvhBoxSweepAlwaysReturnsExactlyWhatTheLinearScanReturns()
    {
        Gen.Select(GeometryGenerators.Scenes, Gen.Int[1, 60])
            .Sample((scene, count) =>
            {
                BoundingBox[] boxes = ScatteredBoxes(scene, count);
                Bvh<int> tree = Bvh<int>.Build([.. Enumerable.Range(0, boxes.Length)], index => boxes[index]);
                BoundingBox query = new BoundingBox(scene.SecondPoint, scene.SecondPoint).Inflated(0.3 * scene.Scale);

                List<int> found = [];
                tree.Overlapping(query, found);

                // Also checked non-vacuous: about a quarter of the samples find an overlap.
                HashSet<int> expected =
                    [.. Enumerable.Range(0, boxes.Length).Where(index => boxes[index].Intersects(query))];

                Assert.True(expected.SetEquals(found));
            });
    }

    [Fact]
    public void ABvhNearestSearchAlwaysFindsTheDistanceTheLinearScanFinds()
    {
        Gen.Select(GeometryGenerators.Scenes, Gen.Int[1, 60])
            .Sample((scene, count) =>
            {
                BoundingBox[] boxes = ScatteredBoxes(scene, count);
                Bvh<int> tree = Bvh<int>.Build([.. Enumerable.Range(0, boxes.Length)], index => boxes[index]);
                Point3d from = scene.SecondPoint;

                Assert.True(tree.TryFindNearest(
                    from,
                    index => boxes[index].ClosestPoint(from).DistanceTo(from),
                    out int _,
                    out double distance));

                double best = boxes.Min(box => box.ClosestPoint(from).DistanceTo(from));

                // Exactly, not within a tolerance: the pruning either preserves the minimum or
                // it does not, and a tolerance here would hide a bound that is slightly unsound.
                Assert.Equal(best, distance);
            });
    }

    [Fact]
    public void ASphericalPointIsAlwaysItsRadiusFromThePlanesOrigin()
    {
        Gen.Select(GeometryGenerators.Scenes, GeometryGenerators.Angles, GeometryGenerators.Angles)
            .Sample((scene, azimuth, inclination) =>
            {
                Point3d point = Point3d.BySphericalCoordinates(
                    scene.Plane,
                    scene.Scale,
                    azimuth,
                    inclination);

                Assert.True(scene.PositionTolerance.AreEqual(
                    point.DistanceTo(scene.Plane.Origin),
                    scene.Scale));
            });
    }

    [Fact]
    public void ACylindricalPointIsAlwaysItsRadiusFromThePlanesAxisAndItsHeightAboveThePlane()
    {
        Gen.Select(GeometryGenerators.Scenes, GeometryGenerators.Angles, Gen.Double[-3.0, 3.0])
            .Sample((scene, angle, factor) =>
            {
                double height = factor * scene.Scale;
                Point3d point = Point3d.ByCylindricalCoordinates(scene.Plane, scene.Scale, angle, height);

                // The two coordinates the construction promised, read back independently: the
                // signed distance from the plane is the height, and the in-plane distance from
                // the origin is the radius.
                Assert.True(scene.PositionTolerance.AreEqual(scene.Plane.DistanceTo(point), height));
                Assert.True(scene.PositionTolerance.AreEqual(
                    scene.Plane.To2d(point).DistanceTo(Point2d.Origin),
                    Math.Abs(scene.Scale)));
            });
    }

    [Fact]
    public void PruningNeverMovesAPointFartherThanOneTolerance()
    {
        Gen.Select(GeometryGenerators.Scenes, Gen.Int[1, 80])
            .Sample((scene, count) =>
            {
                Point3d[] points = ClusteredPoints(scene, count);
                Tolerance tolerance = Tolerance.ForScale(scene.Scale);

                Point3d[] pruned = Point3d.PruneDuplicates(points, out int[] map, tolerance);

                // The property the greedy rule exists to guarantee. A rule that followed a
                // dropped point through to its own survivor would satisfy every other assertion
                // here and break this one, on a long enough chain.
                for (int index = 0; index < points.Length; index++)
                {
                    Assert.True(map[index] >= 0);
                    Assert.True(points[index].DistanceTo(pruned[map[index]]) <= tolerance.Linear);
                }
            });
    }

    [Fact]
    public void PruningIsIdempotent()
    {
        Gen.Select(GeometryGenerators.Scenes, Gen.Int[1, 80])
            .Sample((scene, count) =>
            {
                Tolerance tolerance = Tolerance.ForScale(scene.Scale);
                Point3d[] once = Point3d.PruneDuplicates(ClusteredPoints(scene, count), tolerance);
                Point3d[] twice = Point3d.PruneDuplicates(once, tolerance);

                // Nothing left in the result coincides with anything else in it, so a second
                // pass has nothing to do. If it did, the first pass had missed a pair.
                Assert.Equal(once.Length, twice.Length);
            });
    }

    [Fact]
    public void APlaneAndItsFlipAreAlwaysCoplanar()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Plane plane = scene.Plane;

            Assert.True(plane.IsCoplanar(plane.Flipped(), scene.PositionTolerance));
            Assert.True(plane.Flipped().Normal.EqualsWithin(-plane.Normal, GeometryGenerators.Dimensionless));
        });
    }

    [Fact]
    public void TheClosestPointOnAPlaneLiesOnItAndIsNoFartherThanAnyOtherPointOnIt()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Plane plane = scene.Plane;
            Point3d closest = plane.ClosestPoint(scene.SecondPoint);
            Point3d alternative = plane.To3d(scene.Planar);

            Assert.True(plane.Contains(closest, scene.PositionTolerance));
            Assert.True(scene.SecondPoint.DistanceTo(closest)
                <= scene.SecondPoint.DistanceTo(alternative) + scene.PositionTolerance.Linear);
        });
    }

    [Fact]
    public void PlacingGeometryOnAPlaneAndReadingItBackInThatPlanesFrameChangesNothing()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Plane plane = scene.Plane;
            Transform onto = Transform.PlaneToPlane(Plane.WorldXY, plane);
            Transform back = Transform.ChangeBasis(plane);

            Assert.True(back.OfPoint(onto.OfPoint(scene.SecondPoint))
                .EqualsWithin(scene.SecondPoint, scene.PositionTolerance));
        });
    }

    [Fact]
    public void ACoordinateSystemsLocalAndWorldConversionsAreInverses()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            CoordinateSystem frame = scene.Frame;

            Assert.True(frame.ToWorld(frame.ToLocal(scene.SecondPoint))
                .EqualsWithin(scene.SecondPoint, scene.PositionTolerance));
        });
    }

    [Fact]
    public void ARigidTransformPreservesTheDistanceBetweenAnyTwoPoints()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Transform rigid = Transform.PlaneToPlane(Plane.WorldXY, scene.Plane);

            Assert.True(rigid.IsRigid(GeometryGenerators.Dimensionless));
            Assert.True(scene.PositionTolerance.AreEqual(
                scene.FirstPoint.DistanceTo(scene.SecondPoint),
                rigid.OfPoint(scene.FirstPoint).DistanceTo(rigid.OfPoint(scene.SecondPoint))));
        });
    }

    [Fact]
    public void ScalarComparisonIsTrichotomousForEveryPairOfValues()
    {
        Gen.Select(GeometryGenerators.Coordinate, GeometryGenerators.Coordinate)
            .Sample((a, b) => AssertTrichotomy(a, b));
    }

    [Fact]
    public void ScalarComparisonIsTrichotomousForPairsStraddlingTheThreshold()
    {
        // This is the generator that matters. Two independent uniform draws land within a
        // tolerance of each other essentially never, so the previous version of this property
        // could not go red no matter how broken the comparison was. Here the second operand is
        // placed deliberately at a small multiple of the threshold away from the first, which
        // is exactly where the old implementation's two roundings disagreed.
        Gen.Select(GeometryGenerators.Coordinate, Gen.Double[-3.0, 3.0])
            .Sample((a, multiple) =>
            {
                double threshold = Math.Max(1e-6, 1e-12 * Math.Abs(a));
                double b = a + (multiple * threshold);

                AssertTrichotomy(a, b);
                AssertTrichotomy(b, a);
            });
    }

    private static Vector3d PerpendicularTo(in Vector3d direction)
    {
        Vector3d seed = Math.Abs(direction.Z) > 0.9 ? Vector3d.YAxis : Vector3d.ZAxis;

        return seed.Cross(direction).Normalised();
    }

    private static void AssertTrichotomy(double a, double b)
    {
        Tolerance tolerance = Tolerance.Default;
        int trueCount = 0;

        if (tolerance.IsLessThan(a, b))
        {
            trueCount++;
        }

        if (tolerance.AreEqual(a, b))
        {
            trueCount++;
        }

        if (tolerance.IsGreaterThan(a, b))
        {
            trueCount++;
        }

        Assert.Equal(1, trueCount);
    }

    // A handful of unit-ish boxes scattered around the scene's working scale. Built from the
    // scene's own numbers rather than from a fresh Random, so a failing sample reproduces.
    private static BoundingBox[] ScatteredBoxes(in Scene scene, int count)
    {
        BoundingBox[] boxes = new BoundingBox[count];
        double step = 0.37 * scene.Scale;

        // Axis and the plane's X axis, never Second: the generator is entitled to produce the
        // zero vector for Second, and building the scatter out of it made this helper throw on
        // roughly one run in eight. Both of these are unit by construction.
        Vector3d along = scene.Axis;
        Vector3d across = scene.Plane.XAxis;
        Vector3d size = new(0.2 * scene.Scale, 0.2 * scene.Scale, 0.2 * scene.Scale);

        for (int index = 0; index < count; index++)
        {
            Point3d min = scene.FirstPoint
                + (along * (index * step))
                + (across * ((index % 5) * step));

            boxes[index] = new BoundingBox(min, min + size);
        }

        return boxes;
    }

    // Points in tight clusters at the scene's scale, so that pruning has something to prune.
    // Built from the scene's own numbers rather than a fresh Random, so a failing sample
    // reproduces from its seed.
    private static Point3d[] ClusteredPoints(in Scene scene, int count)
    {
        Point3d[] points = new Point3d[count];
        Vector3d along = scene.Axis;
        Vector3d across = scene.Plane.XAxis;

        for (int index = 0; index < count; index++)
        {
            // Every third point is a near-duplicate of the one before it, a thousandth of the
            // default tolerance away, so the coincident case is present rather than hoped for.
            double step = index / 3 * 0.5 * scene.Scale;
            Vector3d jitter = index % 3 == 0
                ? Vector3d.Zero
                : across * (1e-9 * scene.Scale * (index % 3));

            points[index] = scene.FirstPoint + (along * step) + jitter;
        }

        return points;
    }
}
