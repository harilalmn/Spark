using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class QuaternionTests
{
    private static readonly Vector3d Probe = new(1.0, -2.0, 3.0);

    [Fact]
    public void IdentityRotatesNothingAndIsNotTheDefaultValue()
    {
        Assert.Equal(Probe, Quaternion.Identity.OfVector(Probe));
        Assert.True(Quaternion.Identity.IsValid);
        Assert.True(Quaternion.Identity.IsUnit());

        // The zero quaternion is the default value and is not a rotation, which is the whole
        // reason Identity has to be asked for by name.
        Assert.NotEqual(Quaternion.Identity, default);
        Assert.False(default(Quaternion).IsValid);
    }

    [Theory]
    [InlineData(1.0, 0.0, 0.0, 30.0)]
    [InlineData(0.0, 1.0, 0.0, 90.0)]
    [InlineData(0.0, 0.0, 1.0, 180.0)]
    [InlineData(1.0, 2.0, 3.0, -47.5)]
    public void ByAxisAngleAgreesWithTransformRotation(double x, double y, double z, double degrees)
    {
        Vector3d axis = new(x, y, z);
        Angle angle = Angle.FromDegrees(degrees);

        // Two independent implementations of the same right-handed convention. If either drifts
        // - a sign, a half-angle, a transposed matrix - this is what says so.
        Vector3d viaQuaternion = Quaternion.ByAxisAngle(axis, angle).OfVector(Probe);
        Vector3d viaMatrix = Transform.Rotation(axis, angle).OfVector(Probe);

        Assert.True(viaQuaternion.EqualsWithin(viaMatrix));
    }

    [Fact]
    public void ToTransformAgreesWithTransformRotation()
    {
        Vector3d axis = new(1.0, 2.0, -0.5);
        Angle angle = Angle.FromDegrees(63.0);

        Transform fromQuaternion = Quaternion.ByAxisAngle(axis, angle).ToTransform();
        Transform direct = Transform.Rotation(axis, angle);

        Assert.True(fromQuaternion.OfVector(Probe).EqualsWithin(direct.OfVector(Probe)));
        Assert.True(fromQuaternion.OfPoint(new Point3d(4.0, 5.0, 6.0))
            .EqualsWithin(direct.OfPoint(new Point3d(4.0, 5.0, 6.0))));
    }

    [Fact]
    public void CompositionAppliesTheRightHandRotationFirst()
    {
        Quaternion yaw = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.QuarterTurn);
        Quaternion pitch = Quaternion.ByAxisAngle(Vector3d.YAxis, Angle.QuarterTurn);

        // Same order as matrix multiplication: (yaw * pitch) means pitch, then yaw.
        Vector3d composed = (yaw * pitch).OfVector(Probe);
        Vector3d sequential = yaw.OfVector(pitch.OfVector(Probe));

        Assert.True(composed.EqualsWithin(sequential));

        // ...and the order matters, so a test that passed under either would prove nothing.
        Assert.False(composed.EqualsWithin((pitch * yaw).OfVector(Probe)));
    }

    [Fact]
    public void MultiplyMatchesTheOperator()
    {
        Quaternion a = Quaternion.ByAxisAngle(Vector3d.XAxis, Angle.FromDegrees(20.0));
        Quaternion b = Quaternion.ByAxisAngle(Vector3d.YAxis, Angle.FromDegrees(35.0));

        Assert.Equal(a * b, Quaternion.Multiply(a, b));
    }

    [Fact]
    public void TheInverseUndoesTheRotation()
    {
        Quaternion q = Quaternion.ByAxisAngle(new Vector3d(1.0, 1.0, 0.0), Angle.FromDegrees(112.0));

        Assert.True(q.TryGetInverse(out Quaternion inverse));
        Assert.True(inverse.OfVector(q.OfVector(Probe)).EqualsWithin(Probe));
        Assert.True((q * inverse).IsSameRotation(Quaternion.Identity));
    }

    [Fact]
    public void ConjugateIsTheInverseForAUnitQuaternionAndIsNotForOthers()
    {
        Quaternion q = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.FromDegrees(40.0));
        Assert.True(q.TryGetInverse(out Quaternion inverse));
        Assert.True(q.Conjugate().EqualsWithin(inverse));

        // Scaled by three: the conjugate scales with it, the inverse does not.
        Quaternion scaled = new(q.X * 3.0, q.Y * 3.0, q.Z * 3.0, q.W * 3.0);
        Assert.False(scaled.Conjugate().EqualsWithin(inverse));
        Assert.True(scaled.TryGetInverse(out Quaternion scaledInverse));
        Assert.True(scaledInverse.EqualsWithin(inverse));
    }

    [Fact]
    public void RotationPreservesLengthAndHandlesANonUnitQuaternion()
    {
        Quaternion q = Quaternion.ByAxisAngle(new Vector3d(0.3, -0.7, 1.0), Angle.FromDegrees(77.0));
        Quaternion scaled = new(q.X * 5.0, q.Y * 5.0, q.Z * 5.0, q.W * 5.0);

        Vector3d rotated = q.OfVector(Probe);

        Assert.Equal(Probe.Length, rotated.Length, 12);

        // OfVector divides by the squared length rather than requiring a unit quaternion, so a
        // chain composed without renormalising still rotates correctly.
        Assert.True(scaled.OfVector(Probe).EqualsWithin(rotated));
        Assert.False(scaled.IsUnit());
    }

    [Fact]
    public void OfPointRotatesAboutTheOrigin()
    {
        Quaternion halfTurn = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.HalfTurn);

        Assert.True(halfTurn.OfPoint(new Point3d(2.0, 0.0, 5.0))
            .EqualsWithin(new Point3d(-2.0, 0.0, 5.0)));
        Assert.True(halfTurn.OfPoint(Point3d.Origin).EqualsWithin(Point3d.Origin));
    }

    [Fact]
    public void AxisAngleRoundTrips()
    {
        Vector3d axis = new Vector3d(2.0, -1.0, 0.5).Normalised();
        Angle angle = Angle.FromDegrees(123.0);

        (Vector3d recoveredAxis, Angle recoveredAngle) = Quaternion.ByAxisAngle(axis, angle).ToAxisAngle();

        Assert.True(recoveredAxis.EqualsWithin(axis));
        Assert.Equal(angle.Radians, recoveredAngle.Radians, 12);
    }

    [Fact]
    public void AxisAngleNormalisesANegativeAngleOntoTheOppositeAxis()
    {
        // The angle always comes back in [0, pi] and the axis carries the sign, so a negative
        // turn about +Z is reported as a positive turn about -Z.
        (Vector3d axis, Angle angle) = Quaternion
            .ByAxisAngle(Vector3d.ZAxis, Angle.FromDegrees(-90.0))
            .ToAxisAngle();

        Assert.True(axis.EqualsWithin(-Vector3d.ZAxis));
        Assert.Equal(Math.PI / 2.0, angle.Radians, 12);
    }

    [Fact]
    public void AxisAngleOfTheIdentityIsZeroAboutAnArbitraryAxis()
    {
        (Vector3d axis, Angle angle) = Quaternion.Identity.ToAxisAngle();

        Assert.Equal(Vector3d.ZAxis, axis);
        Assert.Equal(0.0, angle.Radians);
    }

    [Fact]
    public void SlerpReturnsItsEndsAndHalvesTheAngleInTheMiddle()
    {
        Quaternion start = Quaternion.Identity;
        Quaternion end = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.FromDegrees(90.0));

        Assert.True(Quaternion.Slerp(start, end, 0.0).IsSameRotation(start));
        Assert.True(Quaternion.Slerp(start, end, 1.0).IsSameRotation(end));

        (Vector3d axis, Angle angle) = Quaternion.Slerp(start, end, 0.5).ToAxisAngle();

        Assert.True(axis.EqualsWithin(Vector3d.ZAxis));
        Assert.Equal(45.0, angle.Degrees, 9);
    }

    [Fact]
    public void SlerpTakesTheShortPathWhenAnInputIsNegated()
    {
        Quaternion end = Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.FromDegrees(90.0));
        Quaternion negated = new(-end.X, -end.Y, -end.Z, -end.W);

        // The same rotation, written the other way. Without the sign check inside Slerp this
        // walks 270 degrees the wrong way round - the classic quaternion animation bug.
        (_, Angle angle) = Quaternion.Slerp(Quaternion.Identity, negated, 0.5).ToAxisAngle();

        Assert.Equal(45.0, angle.Degrees, 9);
    }

    [Fact]
    public void SlerpBetweenNearlyEqualRotationsStaysOnTheArc()
    {
        Quaternion a = Quaternion.ByAxisAngle(Vector3d.XAxis, Angle.FromDegrees(10.0));
        Quaternion b = Quaternion.ByAxisAngle(Vector3d.XAxis, Angle.FromDegrees(10.000001));

        // The near-parallel branch: sin(theta) is at the edge of usefulness here, so this is
        // where a naive implementation divides by nearly zero.
        Quaternion middle = Quaternion.Slerp(a, b, 0.5);

        Assert.True(middle.IsUnit());
        Assert.True(middle.IsSameRotation(a, Tolerance.Default.Scaled(1e3)));
    }

    [Fact]
    public void SlerpRejectsAQuaternionThatIsNotARotation()
    {
        Assert.Throws<ArgumentException>(() => Quaternion.Slerp(default, Quaternion.Identity, 0.5));
        Assert.Throws<ArgumentException>(() => Quaternion.Slerp(Quaternion.Identity, default, 0.5));
        Assert.Throws<ArgumentException>(
            () => Quaternion.Slerp(Quaternion.Identity, Quaternion.Identity, double.NaN));
    }

    [Fact]
    public void IsSameRotationAcceptsTheNegatedFormThatEqualityRejects()
    {
        Quaternion q = Quaternion.ByAxisAngle(Vector3d.YAxis, Angle.FromDegrees(64.0));
        Quaternion negated = new(-q.X, -q.Y, -q.Z, -q.W);

        Assert.False(q.EqualsWithin(negated));
        Assert.True(q.IsSameRotation(negated));

        // ...and it really is the same rotation, which is why equality is the wrong question.
        Assert.True(q.OfVector(Probe).EqualsWithin(negated.OfVector(Probe)));

        Assert.False(q.IsSameRotation(default));
        Assert.False(default(Quaternion).IsSameRotation(q));
    }

    [Fact]
    public void ByRotationBetweenTurnsOneDirectionOntoTheOther()
    {
        Vector3d from = new(1.0, 2.0, 3.0);
        Vector3d to = new(-4.0, 0.5, 2.0);

        Vector3d turned = Quaternion.ByRotationBetween(from, to).OfVector(from.Normalised());

        Assert.True(turned.EqualsWithin(to.Normalised()));
    }

    [Fact]
    public void ByRotationBetweenIsTheIdentityForParallelDirections()
    {
        Assert.True(Quaternion.ByRotationBetween(Vector3d.XAxis, Vector3d.XAxis * 7.0)
            .IsSameRotation(Quaternion.Identity));
    }

    [Fact]
    public void ByRotationBetweenHandlesTheAntiparallelCase()
    {
        // No unique answer exists here, so what is asserted is the only thing that should be:
        // the result is a rotation and it lands on the opposite direction.
        Vector3d from = new(0.0, 0.0, 1.0);
        Quaternion q = Quaternion.ByRotationBetween(from, -from);

        Assert.True(q.IsUnit());
        Assert.True(q.OfVector(from).EqualsWithin(-from));
    }

    [Fact]
    public void ByAxisAngleAndByRotationBetweenRejectDegenerateInput()
    {
        Assert.Throws<ArgumentException>(() => Quaternion.ByAxisAngle(Vector3d.Zero, Angle.QuarterTurn));
        Assert.Throws<ArgumentException>(
            () => Quaternion.ByAxisAngle(Vector3d.ZAxis, Angle.FromRadians(double.NaN)));
        Assert.Throws<ArgumentException>(() => Quaternion.ByRotationBetween(Vector3d.Zero, Vector3d.XAxis));
        Assert.Throws<ArgumentException>(() => Quaternion.ByRotationBetween(Vector3d.XAxis, Vector3d.Zero));
    }

    [Fact]
    public void NormalisationFailsOnAValueThatIsNotARotation()
    {
        Assert.False(default(Quaternion).TryNormalise(out Quaternion fallback));
        Assert.Equal(Quaternion.Identity, fallback);
        Assert.Throws<InvalidOperationException>(() => default(Quaternion).Normalised());

        Quaternion infinite = new(double.PositiveInfinity, 0.0, 0.0, 1.0);
        Assert.False(infinite.IsValid);
        Assert.False(infinite.TryNormalise(out _));
    }

    [Fact]
    public void EqualityIsExactAndHashingFollowsIt()
    {
        Quaternion a = new(0.1, 0.2, 0.3, 0.9);
        Quaternion b = new(0.1, 0.2, 0.3, 0.9);

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals("not a quaternion"));

        Quaternion nudged = new(0.1 + 1e-12, 0.2, 0.3, 0.9);
        Assert.False(a == nudged);
        Assert.True(a.EqualsWithin(nudged));
    }

    [Fact]
    public void ToStringNamesTheFourComponents()
    {
        Assert.Equal("Quaternion(0, 0, 0, 1)", Quaternion.Identity.ToString());
    }

    [Fact]
    public void TheVectorPartIsTheAxisScaledByTheHalfAngleSine()
    {
        Quaternion q = Quaternion.ByAxisAngle(Vector3d.YAxis, Angle.HalfTurn);

        Assert.True(q.Vector.EqualsWithin(Vector3d.YAxis));
        Assert.Equal(0.0, q.W, 12);
        Assert.Equal(1.0, q.Length, 12);
    }
}
