using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="CurveOffset.FilletTangentTo"/> — `E2-T72`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the third tangency, because it is the whole of what this member adds.</b>
/// <see cref="CurveOffset.Fillet"/> is given the radius; this one is not, and works it out from a
/// third curve. Any test asserting only that the arc touches the first two curves would pass at
/// <i>any</i> radius, so the assertions are about the radius and about the third curve.
/// </para>
/// <para>
/// <b>The anchor is a 3-4-5 triangle, whose incircle radius is known by hand.</b> Area over
/// semiperimeter, <c>6 / 6 = 1</c> exactly, centred at <c>(1, 1)</c> with the right angle at the
/// origin — a number derived rather than read off the implementation.
/// </para>
/// </remarks>
public sealed class FilletTangentToThreeTests
{
    /// <summary>
    /// <b>The anchor.</b> The two legs of a 3-4-5 triangle, with the hypotenuse deciding the
    /// radius: the incircle, radius one, centre at one-one.
    /// </summary>
    [Fact]
    public void TheIncircleOfAThreeFourFiveTriangle()
    {
        // Right angle at the origin: legs along x to (4, 0) and along y to (0, 3), hypotenuse
        // between them. Area 6, semiperimeter 6, inradius 6 / 6 = 1.
        Line alongX = new(new Point3d(0, 0, 0), new Point3d(4, 0, 0));
        Line alongY = new(new Point3d(0, 0, 0), new Point3d(0, 3, 0));
        Line hypotenuse = new(new Point3d(4, 0, 0), new Point3d(0, 3, 0));

        Arc fillet = CurveOffset.FilletTangentTo(alongX, alongY, hypotenuse, Vector3d.ZAxis);

        Assert.Equal(1.0, fillet.Radius, 1e-9);
        Assert.True(
            fillet.Center.DistanceTo(new Point3d(1, 1, 0)) < 1e-9,
            $"the centre is at {fillet.Center}, not at the incentre (1, 1, 0).");
    }

    /// <summary>
    /// Tangency to <b>all three</b>, measured from the centre — the first two would be satisfied at
    /// any radius at all.
    /// </summary>
    [Fact]
    public void TheArcIsTangentToAllThreeCurves()
    {
        Line alongX = new(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        Line slanted = new(new Point3d(0, 0, 0), new Point3d(6, 8, 0));
        Line closing = new(new Point3d(10, 0, 0), new Point3d(6, 8, 0));

        Arc fillet = CurveOffset.FilletTangentTo(alongX, slanted, closing, Vector3d.ZAxis);

        Assert.Equal(fillet.Radius, alongX.ClosestPoint(fillet.Center).DistanceTo(fillet.Center), 1e-9);
        Assert.Equal(fillet.Radius, slanted.ClosestPoint(fillet.Center).DistanceTo(fillet.Center), 1e-9);
        Assert.Equal(
            fillet.Radius,
            closing.ClosestPoint(fillet.Center).DistanceTo(fillet.Center),
            1e-9);
    }

    /// <summary>
    /// The arc runs between the first two curves; the third only fixes the size, and contributes no
    /// endpoint.
    /// </summary>
    [Fact]
    public void TheArcRunsBetweenTheFirstTwoCurves()
    {
        Line alongX = new(new Point3d(0, 0, 0), new Point3d(4, 0, 0));
        Line alongY = new(new Point3d(0, 0, 0), new Point3d(0, 3, 0));
        Line hypotenuse = new(new Point3d(4, 0, 0), new Point3d(0, 3, 0));

        Arc fillet = CurveOffset.FilletTangentTo(alongX, alongY, hypotenuse, Vector3d.ZAxis);

        // The incircle touches the x axis at (1, 0) and the y axis at (0, 1).
        Assert.True(
            fillet.StartPoint.DistanceTo(new Point3d(1, 0, 0)) < 1e-9,
            $"the arc starts at {fillet.StartPoint}, not on the first curve.");
        Assert.True(
            fillet.EndPoint.DistanceTo(new Point3d(0, 1, 0)) < 1e-9,
            $"the arc ends at {fillet.EndPoint}, not on the second curve.");
    }

    /// <summary>
    /// Swapping which curve is the constraint changes which pair the arc spans, and not the circle
    /// it belongs to — the same incircle, a different quarter of it.
    /// </summary>
    [Fact]
    public void WhichCurveIsTheConstraintDecidesWhichPairTheArcSpans()
    {
        Line alongX = new(new Point3d(0, 0, 0), new Point3d(4, 0, 0));
        Line alongY = new(new Point3d(0, 0, 0), new Point3d(0, 3, 0));
        Line hypotenuse = new(new Point3d(4, 0, 0), new Point3d(0, 3, 0));

        Arc spanningTheLegs = CurveOffset.FilletTangentTo(alongX, alongY, hypotenuse, Vector3d.ZAxis);
        Arc spanningTheOthers = CurveOffset.FilletTangentTo(alongX, hypotenuse, alongY, Vector3d.ZAxis);

        Assert.Equal(spanningTheLegs.Radius, spanningTheOthers.Radius, 1e-9);
        Assert.True(
            spanningTheLegs.Center.DistanceTo(spanningTheOthers.Center) < 1e-9,
            "the same three curves gave two different circles.");

        // The second arc ends on the hypotenuse, which the first never touches.
        Assert.True(
            hypotenuse.ClosestPoint(spanningTheOthers.EndPoint).DistanceTo(spanningTheOthers.EndPoint)
                < 1e-9,
            $"the arc ends at {spanningTheOthers.EndPoint}, which is not on the hypotenuse.");
    }

    /// <summary>A curved third constraint, so the member is not pinned to straight lines.</summary>
    [Fact]
    public void TheConstraintMayBeCurved()
    {
        Line alongX = new(new Point3d(0, 0, 0), new Point3d(20, 0, 0));
        Line alongY = new(new Point3d(0, 0, 0), new Point3d(0, 20, 0));

        // A circle about (14, 14) big enough to reach past both axes: its centre is 14 from each,
        // so a radius of 16 crosses both and closes a corner with them.
        Circle constraint = new(Plane.FromOriginNormal(new Point3d(14, 14, 0), Vector3d.ZAxis), 16.0);

        Arc fillet = CurveOffset.FilletTangentTo(alongX, alongY, constraint, Vector3d.ZAxis);

        Assert.Equal(
            fillet.Radius, alongX.ClosestPoint(fillet.Center).DistanceTo(fillet.Center), 1e-8);
        Assert.Equal(
            fillet.Radius, alongY.ClosestPoint(fillet.Center).DistanceTo(fillet.Center), 1e-8);
        Assert.Equal(
            fillet.Radius,
            constraint.ClosestPoint(fillet.Center).DistanceTo(fillet.Center),
            1e-8);
    }

    /// <summary>An equilateral triangle, whose inradius is also known by hand.</summary>
    [Fact]
    public void AnEquilateralTriangleGivesItsOwnInradius()
    {
        // Side 6: inradius = side / (2 * sqrt(3)).
        Point3d a = new(0, 0, 0);
        Point3d b = new(6, 0, 0);
        Point3d c = new(3, 3.0 * Math.Sqrt(3.0), 0);

        Arc fillet = CurveOffset.FilletTangentTo(
            new Line(a, b), new Line(a, c), new Line(b, c), Vector3d.ZAxis);

        Assert.Equal(6.0 / (2.0 * Math.Sqrt(3.0)), fillet.Radius, 1e-9);
    }

    [Fact]
    public void ItsDegeneraciesAreRefused()
    {
        Line alongX = new(new Point3d(0, 0, 0), new Point3d(4, 0, 0));
        Line alongY = new(new Point3d(0, 0, 0), new Point3d(0, 3, 0));
        Line hypotenuse = new(new Point3d(4, 0, 0), new Point3d(0, 3, 0));

        Assert.Throws<ArgumentNullException>(
            () => CurveOffset.FilletTangentTo(null!, alongY, hypotenuse, Vector3d.ZAxis));
        Assert.Throws<ArgumentNullException>(
            () => CurveOffset.FilletTangentTo(alongX, null!, hypotenuse, Vector3d.ZAxis));
        Assert.Throws<ArgumentNullException>(
            () => CurveOffset.FilletTangentTo(alongX, alongY, null!, Vector3d.ZAxis));
        Assert.Throws<ArgumentException>(
            () => CurveOffset.FilletTangentTo(alongX, alongY, hypotenuse, Vector3d.Zero));
    }

    /// <summary>Three curves that enclose nothing have no corner to seed the solve from.</summary>
    [Fact]
    public void ThreeCurvesThatEncloseNothingAreRefused()
    {
        Line first = new(new Point3d(0, 0, 0), new Point3d(4, 0, 0));
        Line second = new(new Point3d(0, 10, 0), new Point3d(4, 10, 0));
        Line third = new(new Point3d(0, 20, 0), new Point3d(4, 20, 0));

        Assert.Throws<ArgumentException>(
            () => CurveOffset.FilletTangentTo(first, second, third, Vector3d.ZAxis));
    }
}
