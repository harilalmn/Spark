using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class QuaternionTests
{
    [Fact]
    public void ADefaultQuaternionIsNotTheIdentityAndIsNotValid()
    {
        Quaternion unset = default;

        Assert.False(unset.IsValid);
        Assert.NotEqual(Quaternion.Identity, unset);
        Assert.True(Quaternion.Identity.IsValid);
    }

    [Fact]
    public void ADefaultQuaternionRefusesEveryQuestionAboutTheRotationItDoesNotHave()
    {
        Quaternion unset = default;

        Assert.Throws<InvalidOperationException>(() => unset.Normalised());
        Assert.Throws<InvalidOperationException>(() => unset.Rotate(Vector3d.XAxis));
        Assert.Throws<InvalidOperationException>(() => unset.ToTransform());
        Assert.Throws<InvalidOperationException>(() => unset.Axis);
        Assert.Throws<InvalidOperationException>(() => unset.Angle);
        Assert.Throws<InvalidOperationException>(() => unset.Inverse());
    }

    [Fact]
    public void ByAxisAngleFollowsTheSameHandednessAsTransformRotation()
    {
        Quaternion quarterTurn = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.QuarterTurn);

        Vector3d rotated = quarterTurn.Rotate(Vector3d.XAxis);

        Assert.True(rotated.EqualsWithin(Vector3d.YAxis));
        Assert.True(quarterTurn.IsUnit());
    }

    [Fact]
    public void QuaternionRotationAgreesWithTransformRotationOnAnObliqueAxis()
    {
        Vector3d axis = new(1.0, 2.0, -3.0);
        Angle angle = Angle.FromDegrees(37.0);
        Vector3d subject = new(4.0, -1.0, 2.0);

        Vector3d byQuaternion = Quaternion.ByAxisAngle(axis, angle).Rotate(subject);
        Vector3d byTransform = Transform.Rotation(axis, angle).OfVector(subject);

        Assert.True(byQuaternion.EqualsWithin(byTransform));
    }

    [Fact]
    public void RotatingPreservesLength()
    {
        Vector3d subject = new(4.0, -1.0, 2.0);
        Quaternion rotation = Quaternion.ByAxisAngle(new Vector3d(1.0, 1.0, 1.0), Angle.FromDegrees(200.0));

        Assert.Equal(subject.Length, rotation.Rotate(subject).Length, 12);
    }

    [Fact]
    public void ADriftedQuaternionRotatesRatherThanAlsoScaling()
    {
        Quaternion drifted = new(0.0, 0.0, 5.0, 5.0);
        Vector3d subject = new(3.0, 0.0, 0.0);

        // Norm 7.07, not 1. If Rotate used the components as given, the result would be
        // scaled by the square of the norm.
        Assert.Equal(3.0, drifted.Rotate(subject).Length, 12);
        Assert.True(drifted.Rotate(subject).EqualsWithin(new Vector3d(0.0, 3.0, 0.0)));
    }

    [Fact]
    public void CompositionAppliesTheRightOperandFirst()
    {
        Quaternion aboutZ = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.QuarterTurn);
        Quaternion aboutX = Quaternion.ByAxisAngle(Vector3d.XAxis, Angle.QuarterTurn);

        Vector3d subject = Vector3d.XAxis;

        // Z first, then X: X goes to Y, and Y goes to Z.
        Assert.True((aboutX * aboutZ).Rotate(subject).EqualsWithin(Vector3d.ZAxis));

        // And the other order is a different rotation, which is the point.
        Assert.False((aboutZ * aboutX).Rotate(subject).EqualsWithin(Vector3d.ZAxis));
    }

    [Fact]
    public void ComposingQuaternionsAgreesWithComposingTheirTransforms()
    {
        Quaternion a = Quaternion.ByAxisAngle(new Vector3d(1.0, 2.0, 3.0), Angle.FromDegrees(41.0));
        Quaternion b = Quaternion.ByAxisAngle(new Vector3d(-2.0, 1.0, 0.5), Angle.FromDegrees(-77.0));

        Assert.True((a * b).ToTransform().EqualsWithin(a.ToTransform() * b.ToTransform()));
        Assert.Equal(a * b, Quaternion.Multiply(a, b));
    }

    [Fact]
    public void TheInverseUndoesTheRotation()
    {
        Quaternion rotation = Quaternion.ByAxisAngle(new Vector3d(1.0, 2.0, 3.0), Angle.FromDegrees(123.0));

        Assert.True((rotation * rotation.Inverse()).RepresentsSameRotationAs(Quaternion.Identity));
        Assert.True(rotation.Inverse().Rotate(rotation.Rotate(Vector3d.XAxis)).EqualsWithin(Vector3d.XAxis));
    }

    [Fact]
    public void TheConjugateIsTheInverseOnlyOnTheUnitSphere()
    {
        Quaternion unit = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.QuarterTurn);
        Quaternion drifted = new(unit.X * 3.0, unit.Y * 3.0, unit.Z * 3.0, unit.W * 3.0);

        // EqualsWithin rather than Equal, and the reason is worth stating: ByAxisAngle's
        // output is not EXACTLY unit - sin(45 degrees) squared twice sums to
        // 0.9999999999999998 - so dividing by the squared norm moves the last bit. A test
        // asserting bitwise equality here would pass on some angles and fail on others.
        Assert.True(unit.Conjugate().EqualsWithin(unit.Inverse()));
        Assert.NotEqual(drifted.Conjugate(), drifted.Inverse());

        // Norm 3 in, norm one third out. That is what makes the composition land on the
        // identity itself rather than on the identity scaled by nine.
        Assert.Equal(1.0 / 3.0, drifted.Inverse().Length, 12);
        Assert.True((drifted * drifted.Inverse()).EqualsWithin(Quaternion.Identity));
    }

    [Fact]
    public void AxisAndAngleComeBackOutOfByAxisAngle()
    {
        Vector3d axis = new Vector3d(1.0, -2.0, 4.0).Normalised();
        Angle angle = Angle.FromDegrees(64.0);

        Quaternion rotation = Quaternion.ByAxisAngle(axis, angle);

        Assert.True(rotation.Axis.EqualsWithin(axis));
        Assert.True(rotation.Angle.EqualsWithin(angle));
    }

    [Fact]
    public void TheNegatedQuaternionReportsTheSameAxisAndAngleRatherThanTheReverseRotation()
    {
        Vector3d axis = new Vector3d(1.0, -2.0, 4.0).Normalised();
        Angle angle = Angle.FromDegrees(64.0);

        Quaternion rotation = Quaternion.ByAxisAngle(axis, angle);
        Quaternion negated = new(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);

        // The two are unequal values denoting one rotation, so they must agree about what
        // that rotation is. Reading the axis straight off a negative-W quaternion gives the
        // opposite axis with a positive angle, which describes the rotation backwards.
        Assert.True(negated.Axis.EqualsWithin(axis));
        Assert.True(negated.Angle.EqualsWithin(angle));
        Assert.True(negated.Rotate(Vector3d.XAxis).EqualsWithin(rotation.Rotate(Vector3d.XAxis)));
    }

    [Fact]
    public void AnAngleBeyondHalfATurnComesBackAsTheShorterRotationTheOtherWay()
    {
        Quaternion rotation = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.FromDegrees(350.0));

        Assert.Equal(10.0, rotation.Angle.Degrees, 9);
        Assert.True(rotation.Axis.EqualsWithin(-Vector3d.ZAxis));
    }

    [Fact]
    public void TheIdentityReportsAZeroAngleAndAUsableAxis()
    {
        Assert.Equal(0.0, Quaternion.Identity.Angle.Radians, 12);

        // A zero vector would be unusable as a rotation axis; ZAxis with a zero angle is the
        // identity, so the arbitrary choice costs nothing.
        Assert.True(Quaternion.Identity.Axis.IsUnit());
        Assert.True(Transform.Rotation(Quaternion.Identity.Axis, Quaternion.Identity.Angle).IsIdentity());
    }

    [Fact]
    public void ByTwoVectorsTakesTheFirstDirectionToTheSecond()
    {
        Vector3d from = new(1.0, 2.0, 3.0);
        Vector3d to = new(-4.0, 0.5, 2.0);

        Quaternion rotation = Quaternion.ByTwoVectors(from, to);

        Assert.True(rotation.Rotate(from.Normalised()).EqualsWithin(to.Normalised()));
    }

    [Fact]
    public void ByTwoVectorsOnParallelDirectionsIsTheIdentity()
    {
        Assert.Equal(
            Quaternion.Identity,
            Quaternion.ByTwoVectors(new Vector3d(1.0, 2.0, 3.0), new Vector3d(2.0, 4.0, 6.0)));
    }

    [Fact]
    public void ByTwoVectorsOnOppositeDirectionsPicksAPerpendicularRatherThanThrowing()
    {
        Vector3d from = new(1.0, 2.0, 3.0);
        Vector3d to = -from;

        Quaternion rotation = Quaternion.ByTwoVectors(from, to);

        Assert.True(rotation.Rotate(from.Normalised()).EqualsWithin(to.Normalised()));
        Assert.Equal(180.0, rotation.Angle.Degrees, 9);

        // Deterministic: the same pair gives the same answer, which is what makes a graph
        // that uses it reproducible.
        Assert.Equal(rotation, Quaternion.ByTwoVectors(from, to));
    }

    [Theory]
    [InlineData(1.0, 0.0, 0.0)]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(0.0, 0.0, 1.0)]
    public void ByTwoVectorsHandlesAntiparallelWorldAxesWhereTheChosenPerpendicularIsMostAtRisk(
        double x,
        double y,
        double z)
    {
        Vector3d from = new(x, y, z);

        Quaternion rotation = Quaternion.ByTwoVectors(from, -from);

        Assert.True(rotation.Rotate(from).EqualsWithin(-from));
    }

    [Fact]
    public void ByTwoVectorsRejectsADirectionWithNoDirection()
    {
        Assert.Equal(
            "from",
            Assert.Throws<ArgumentException>(() => Quaternion.ByTwoVectors(Vector3d.Zero, Vector3d.XAxis)).ParamName);
        Assert.Equal(
            "to",
            Assert.Throws<ArgumentException>(() => Quaternion.ByTwoVectors(Vector3d.XAxis, Vector3d.Zero)).ParamName);
    }

    [Fact]
    public void ByAxisAngleRejectsADegenerateAxisAndANonFiniteAngle()
    {
        Assert.Equal(
            "axis",
            Assert.Throws<ArgumentException>(() => Quaternion.ByAxisAngle(Vector3d.Zero, Angle.QuarterTurn)).ParamName);
        Assert.Equal(
            "angle",
            Assert.Throws<ArgumentException>(
                () => Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.FromRadians(double.NaN))).ParamName);
    }

    [Fact]
    public void ATransformRoundTripsThroughAQuaternion()
    {
        Transform rotation = Transform.Rotation(new Vector3d(1.0, -2.0, 0.5), Angle.FromDegrees(97.0));

        Assert.True(Quaternion.ByRotation(rotation).ToTransform().EqualsWithin(rotation));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(179.5)]
    [InlineData(180.0)]
    [InlineData(180.5)]
    [InlineData(270.0)]
    public void ATransformRoundTripsNearAHalfTurnWhereTheTraceFormulaAloneLosesEverything(double degrees)
    {
        Transform rotation = Transform.Rotation(new Vector3d(1.0, 1.0, 1.0), Angle.FromDegrees(degrees));

        Assert.True(Quaternion.ByRotation(rotation).ToTransform().EqualsWithin(rotation));
    }

    [Fact]
    public void AQuaternionRoundTripsThroughATransformUpToTheDoubleCoverAndNotFurther()
    {
        Quaternion rotation = Quaternion.ByAxisAngle(new Vector3d(1.0, -2.0, 0.5), Angle.FromDegrees(97.0));
        Quaternion negated = new(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);

        Quaternion returned = Quaternion.ByRotation(negated.ToTransform());

        Assert.True(returned.RepresentsSameRotationAs(negated));
        Assert.False(returned.EqualsWithin(negated));
    }

    [Fact]
    public void ByRotationRefusesATransformThatIsNotARotation()
    {
        Assert.Equal(
            "transform",
            Assert.Throws<ArgumentException>(() => Quaternion.ByRotation(Transform.Scale(2.0))).ParamName);

        ArgumentException mirrored = Assert.Throws<ArgumentException>(
            () => Quaternion.ByRotation(Transform.Mirror(Plane.WorldXY)));

        Assert.Contains("handedness", mirrored.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ByRotationIgnoresTheTranslation()
    {
        Transform rotation = Transform.Rotation(Vector3d.ZAxis, Angle.QuarterTurn);
        Transform moved = Transform.Translation(new Vector3d(10.0, -5.0, 3.0)) * rotation;

        Assert.True(Quaternion.ByRotation(moved).RepresentsSameRotationAs(Quaternion.ByRotation(rotation)));
    }

    [Fact]
    public void SlerpEndsWhereItSaysItWill()
    {
        Quaternion from = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.Zero);
        Quaternion to = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.QuarterTurn);

        Assert.True(Quaternion.Slerp(from, to, 0.0).RepresentsSameRotationAs(from));
        Assert.True(Quaternion.Slerp(from, to, 1.0).RepresentsSameRotationAs(to));
    }

    [Fact]
    public void SlerpTurnsAtAConstantAngularSpeed()
    {
        Quaternion from = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.Zero);
        Quaternion to = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.FromDegrees(90.0));

        // A component-wise blend would be at 45.0 - 4.05 degrees here. Constant speed is the
        // entire reason to prefer slerp, so it is asserted rather than assumed.
        Assert.Equal(22.5, Quaternion.Slerp(from, to, 0.25).Angle.Degrees, 9);
        Assert.Equal(45.0, Quaternion.Slerp(from, to, 0.5).Angle.Degrees, 9);
        Assert.Equal(67.5, Quaternion.Slerp(from, to, 0.75).Angle.Degrees, 9);
    }

    [Fact]
    public void SlerpTakesTheShortWayRoundEvenWhenTheFarEndIsTheOtherRepresentative()
    {
        Quaternion from = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.Zero);
        Quaternion to = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.FromDegrees(90.0));
        Quaternion negatedTo = new(-to.X, -to.Y, -to.Z, -to.W);

        // Half of all endpoint pairs arrive with the far end negated, and without the sign
        // fix half of all interpolations would take a 270-degree route to a 90-degree target.
        Assert.True(
            Quaternion.Slerp(from, to, 0.5)
                .RepresentsSameRotationAs(Quaternion.Slerp(from, negatedTo, 0.5)));
        Assert.Equal(45.0, Quaternion.Slerp(from, negatedTo, 0.5).Angle.Degrees, 9);
    }

    [Fact]
    public void SlerpBetweenCoincidentRotationsIsThatRotationRatherThanADivisionByZero()
    {
        Quaternion rotation = Quaternion.ByAxisAngle(new Vector3d(1.0, 2.0, 3.0), Angle.FromDegrees(30.0));

        Quaternion halfway = Quaternion.Slerp(rotation, rotation, 0.5);

        Assert.True(halfway.IsUnit());
        Assert.True(halfway.RepresentsSameRotationAs(rotation));
    }

    [Fact]
    public void SlerpExtrapolatesRatherThanClamping()
    {
        Quaternion from = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.Zero);
        Quaternion to = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.FromDegrees(40.0));

        Assert.Equal(60.0, Quaternion.Slerp(from, to, 1.5).Angle.Degrees, 9);
    }

    [Fact]
    public void SlerpRejectsANonFiniteParameter()
    {
        Assert.Equal(
            "t",
            Assert.Throws<ArgumentException>(
                () => Quaternion.Slerp(Quaternion.Identity, Quaternion.Identity, double.NaN)).ParamName);
    }

    [Fact]
    public void EqualityIsComponentwiseAndRotationEquivalenceIsASeparateQuestion()
    {
        Quaternion rotation = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.QuarterTurn);
        Quaternion negated = new(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);

        Assert.NotEqual(rotation, negated);
        Assert.True(rotation != negated);
        Assert.False(rotation.EqualsWithin(negated));
        Assert.True(rotation.RepresentsSameRotationAs(negated));
    }

    [Fact]
    public void EqualQuaternionsShareAHashCodeAndNaNFollowsIeeeInTheOperator()
    {
        Quaternion one = new(1.0, 2.0, 3.0, 4.0);
        Quaternion same = new(1.0, 2.0, 3.0, 4.0);
        Quaternion broken = new(double.NaN, 0.0, 0.0, 1.0);
        Quaternion identicallyBroken = new(double.NaN, 0.0, 0.0, 1.0);

        Assert.Equal(one, same);
        Assert.Equal(one.GetHashCode(), same.GetHashCode());

        // Two values with identical bits, one operator that says no and one method that says
        // yes. The operator follows IEEE 754 so that arithmetic behaves; Equals treats NaN as
        // equal to itself so that a quaternion stays usable as a dictionary key.
        Assert.False(broken == identicallyBroken);
        Assert.True(broken.Equals(identicallyBroken));
    }

    [Fact]
    public void NormalisingAnEnormousQuaternionDoesNotOverflowToZero()
    {
        Quaternion enormous = new(1e200, 1e200, 1e200, 1e200);

        // LengthSquared overflows to infinity here, so a naive normalisation divides by
        // infinity and returns zeros. Scaling by the largest component first is what avoids it.
        Assert.True(enormous.Normalised().IsUnit());
        Assert.Equal(0.5, enormous.Normalised().W, 12);
    }

    [Fact]
    public void TryNormaliseReportsFailureAndYieldsTheIdentity()
    {
        Assert.False(default(Quaternion).TryNormalise(out Quaternion fromDefault));
        Assert.Equal(Quaternion.Identity, fromDefault);

        Assert.False(new Quaternion(double.PositiveInfinity, 0.0, 0.0, 1.0).TryNormalise(out Quaternion fromInfinite));
        Assert.Equal(Quaternion.Identity, fromInfinite);
    }

    [Fact]
    public void TheOperatorFormRotatesAVector()
    {
        Quaternion rotation = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.QuarterTurn);

        Assert.Equal(rotation.Rotate(Vector3d.XAxis), rotation * Vector3d.XAxis);
    }

    [Fact]
    public void ToStringNamesEveryComponentBecauseTheOrderingIsNotConventional()
    {
        Assert.Equal("(w: 1, x: 0, y: 0, z: 0)", Quaternion.Identity.ToString());
    }
}
