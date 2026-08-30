using System;
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
                Interval? overlap = left.Intersection(right);

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

    /// <summary>
    /// The counterexample that made
    /// <see cref="TheSignedAngleBetweenTwoVectorsDoesNotDependOnTheirLengths"/> fail at random,
    /// pinned as an ordinary test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generator drew a turn of <c>-3.844e-15°</c> — vanishingly small, and nothing like the
    /// near-360° case a person would have guessed at. The two vectors are then the same direction
    /// to within about <c>1e-17</c> radians, and scaling them by <c>0.01</c> and <c>4.05e-5</c>
    /// sends the cross product to <b>exactly zero</b>. <c>Math.Sign</c> then answers <c>0</c>,
    /// which is not the opposite sign — it is no sign at all, and the original assertion read that
    /// as a disagreement about direction.
    /// </para>
    /// <para>
    /// <b>The magnitude property held throughout</b>, which is why the failure was rare and looked
    /// like nothing: the thing being tested was fine and the assertion beside it was over-strict.
    /// Found by running the suite forty times and reading CsCheck's counterexample rather than by
    /// guessing — the first two guesses, both about angles near a multiple of 360°, survived four
    /// hundred thousand trials of a hand-rolled search and were wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASignedAngleTooSmallToHaveASignIsNotAsserted()
    {
        Vector3d axis = new(-0.14958244638723028, 0.919029222119718, -0.36470588235294116);
        Angle turn = Angle.FromDegrees(-3.844e-15);

        Vector3d a = axis;
        Vector3d b = axis.Rotate(PerpendicularTo(axis), turn);

        Assert.True(a.Cross(b).TryNormalise(out Vector3d reference));

        Angle atUnitLength = a.SignedAngleTo(b, reference);
        Angle atOtherLengths = (a * 0.01).SignedAngleTo(b * 4.047492879802625E-05, reference);

        // The real property survives the case entirely.
        Assert.True(atUnitLength.EqualsWithin(atOtherLengths));

        // And the angle is below the tolerance at which two directions are the same, which is
        // exactly why its sign carries nothing.
        Assert.True(
            atUnitLength.EqualsWithin(Angle.Zero, GeometryGenerators.Dimensionless),
            $"The angle is {atUnitLength.Radians}, which is above the tolerance — so this is no "
            + "longer the case that produced the flake and the guard it justifies needs revisiting.");
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

                // The property. It holds everywhere, including where the sign below does not.
                Assert.True(atUnitLength.EqualsWithin(atOtherLengths));

                // The sign is only a fact about the geometry when the angle is far enough from
                // zero for one to exist. Below the angular tolerance the two vectors are the same
                // direction as far as this assembly is concerned, and scaling can send the cross
                // product to exactly zero — at which point Math.Sign answers 0, which is neither
                // sign and is not a disagreement about direction. See
                // ASignedAngleTooSmallToHaveASignIsNotAsserted below for the case that found this.
                if (!atUnitLength.EqualsWithin(Angle.Zero, GeometryGenerators.Dimensionless))
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

    [Fact]
    public void AQuaternionRotatesExactlyAsTheEquivalentMatrixDoes()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Vector3d vector = scene.Second * scene.Scale;

            // Two independent implementations of the same right-handed convention, one built
            // from half-angle sines and one from a Rodrigues matrix. Across nine decades of
            // scale, they either agree or one of them has a sign or a half-angle wrong.
            Vector3d viaQuaternion = Quaternion.ByAxisAngle(scene.Axis, scene.Turn).OfVector(vector);
            Vector3d viaMatrix = Transform.Rotation(scene.Axis, scene.Turn).OfVector(vector);

            Assert.True(viaQuaternion.EqualsWithin(viaMatrix, scene.PositionTolerance));
        });
    }

    [Fact]
    public void AQuaternionRotationPreservesLength()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Vector3d vector = scene.Second * scene.Scale;
            Quaternion rotation = Quaternion.ByAxisAngle(scene.Axis, scene.Turn);

            Assert.True(scene.PositionTolerance.AreEqual(vector.Length, rotation.OfVector(vector).Length));
        });
    }

    [Fact]
    public void AQuaternionFollowedByItsInverseReturnsEveryVectorToWhereItStarted()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Vector3d vector = scene.Second * scene.Scale;
            Quaternion rotation = Quaternion.ByAxisAngle(scene.Axis, scene.Turn);

            Assert.True(rotation.TryGetInverse(out Quaternion inverse));
            Assert.True(inverse.OfVector(rotation.OfVector(vector))
                .EqualsWithin(vector, scene.PositionTolerance));
        });
    }

    [Fact]
    public void SlerpingHalfwayLeavesEqualRotationsOnEitherSide()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Quaternion start = Quaternion.Identity;
            Quaternion end = Quaternion.ByAxisAngle(scene.Axis, scene.Turn);
            Quaternion middle = Quaternion.Slerp(start, end, 0.5);

            Assert.True(start.TryGetInverse(out Quaternion startInverse));
            Assert.True(middle.TryGetInverse(out Quaternion middleInverse));

            // Constant angular speed is the entire claim of Slerp over lerp, and this is what
            // it means: the turn from start to middle equals the turn from middle to end.
            Angle first = (middle * startInverse).ToAxisAngle().Angle;
            Angle second = (end * middleInverse).ToAxisAngle().Angle;

            Assert.True(GeometryGenerators.Dimensionless.AreEqual(first.Radians, second.Radians));
        });
    }
}
