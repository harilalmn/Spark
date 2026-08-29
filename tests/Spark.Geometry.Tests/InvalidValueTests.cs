using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// A <c>readonly struct</c> cannot stop <c>default</c> from existing, so <see cref="Plane"/>
/// and <see cref="CoordinateSystem"/> both have a value with a zero frame that is not a plane
/// and not a frame. Every geometric member must refuse it rather than compute with zero axes
/// and return an answer that looks ordinary.
/// </summary>
/// <remarks>
/// These are not hypothetical failures. Unguarded, <c>default(Plane).Contains(p)</c> returned
/// <see langword="true"/> for <b>every point in space</b> — a zero normal makes every
/// perpendicular distance zero — while <c>To2d</c> collapsed all of space onto the origin and
/// <c>Project</c> was the identity. <see cref="CoordinateSystem"/> was worse for being
/// half-guarded: <c>ToPlane</c> and <c>ToTransform</c> threw while all four
/// <c>ToLocal</c>/<c>ToWorld</c> overloads silently answered <c>(0, 0, 0)</c>.
/// </remarks>
public sealed class InvalidValueTests
{
    private static readonly Point3d SomePoint = new(1.0, 2.0, 3.0);
    private static readonly Vector3d SomeVector = new(4.0, 5.0, 6.0);

    [Fact]
    public void ADefaultPlaneRefusesEveryDistanceAndProjectionQuery()
    {
        Plane invalid = default;

        Assert.Throws<InvalidOperationException>(() => invalid.DistanceTo(SomePoint));
        Assert.Throws<InvalidOperationException>(() => invalid.ClosestPoint(SomePoint));
        Assert.Throws<InvalidOperationException>(() => invalid.Project(SomeVector));
    }

    [Fact]
    public void ADefaultPlaneRefusesEveryContainmentQuery()
    {
        Plane invalid = default;

        // Unguarded this returned true for every point that has ever existed.
        Assert.Throws<InvalidOperationException>(() => invalid.Contains(SomePoint));
        Assert.Throws<InvalidOperationException>(() => invalid.IsCoplanar(Plane.WorldXY));
    }

    [Fact]
    public void ADefaultPlaneRefusesEveryCoordinateConversion()
    {
        Plane invalid = default;

        Assert.Throws<InvalidOperationException>(() => invalid.To2d(SomePoint));
        Assert.Throws<InvalidOperationException>(() => invalid.To3d(new Point2d(1.0, 2.0)));
        Assert.Throws<InvalidOperationException>(() => invalid.Flipped());
        Assert.Throws<InvalidOperationException>(() => invalid.Offset(1.0));
    }

    [Fact]
    public void AValidPlaneStillRefusesToBeComparedAgainstAnInvalidOne()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => Plane.WorldXY.IsCoplanar(default));

        Assert.Equal("other", failure.ParamName);
    }

    [Fact]
    public void ADefaultPlaneStillComparesAndReportsItsOwnInvalidity()
    {
        Plane invalid = default;

        Assert.False(invalid.IsValid);
        Assert.True(invalid.Equals(default(Plane)));
        Assert.Equal(invalid.GetHashCode(), default(Plane).GetHashCode());
        Assert.NotNull(invalid.ToString());
    }

    [Fact]
    public void ADefaultCoordinateSystemRefusesEveryConversionInBothDirections()
    {
        CoordinateSystem invalid = default;

        Assert.Throws<InvalidOperationException>(() => invalid.ToLocal(SomePoint));
        Assert.Throws<InvalidOperationException>(() => invalid.ToLocal(SomeVector));
        Assert.Throws<InvalidOperationException>(() => invalid.ToWorld(SomePoint));
        Assert.Throws<InvalidOperationException>(() => invalid.ToWorld(SomeVector));
        Assert.Throws<InvalidOperationException>(() => invalid.ToPlane());
        Assert.Throws<InvalidOperationException>(() => invalid.ToTransform());
    }

    [Fact]
    public void ADefaultCoordinateSystemStillComparesAndReportsItsOwnInvalidity()
    {
        CoordinateSystem invalid = default;

        Assert.False(invalid.IsValid);
        Assert.True(invalid.Equals(default(CoordinateSystem)));
        Assert.NotNull(invalid.ToString());
    }

    [Fact]
    public void AValidPlaneAndFrameAnswerEveryOneOfThoseQueriesWithoutThrowing()
    {
        // The guards must not have been bolted onto the working path by accident.
        Plane plane = Plane.WorldXY;
        CoordinateSystem frame = CoordinateSystem.Identity;

        Assert.Equal(3.0, plane.DistanceTo(SomePoint), 12);
        Assert.True(plane.ClosestPoint(SomePoint).EqualsWithin(new Point3d(1.0, 2.0, 0.0)));
        Assert.True(plane.Project(SomeVector).EqualsWithin(new Vector3d(4.0, 5.0, 0.0)));
        Assert.False(plane.Contains(SomePoint));
        Assert.True(plane.IsCoplanar(plane.Flipped()));
        Assert.True(plane.To2d(SomePoint).EqualsWithin(new Point2d(1.0, 2.0)));
        Assert.True(frame.ToWorld(frame.ToLocal(SomePoint)).EqualsWithin(SomePoint));
        Assert.True(frame.ToTransform().IsIdentity());
    }
}
