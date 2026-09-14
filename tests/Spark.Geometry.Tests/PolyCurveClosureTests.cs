using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="PolyCurve.ClosedWithLineAndTangentArcs"/> — `E2-T72`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is which of the four candidates is taken.</b> Each arc's centre sits on one of two
/// sides of its end, which is four combinations, and each feasible combination is a genuine closure
/// — tangent at every join, arriving where it should, travelling the right way. What separates them
/// is whether the closure cuts through the shape it is closing, so that is what the tests assert,
/// and taking the shortest candidate unconditionally has to break them.
/// </para>
/// <para>
/// <b>Every fixture uses two different radii</b>, because equal radii make the signed radius
/// difference zero and hide a sign error in it.
/// </para>
/// </remarks>
public sealed class PolyCurveClosureTests
{
    /// <summary>An L-shaped chain, closed.</summary>
    [Fact]
    public void TheClosureJoinsTheTwoEnds()
    {
        PolyCurve chain = Chain(new Point3d(0, 10, 0), new Point3d(0, 0, 0), new Point3d(10, 0, 0));

        PolyCurve closed = chain.ClosedWithLineAndTangentArcs(2.0, 3.0);

        // The chain's own two segments survive untouched at the front — nothing here trims.
        Assert.True(closed.SegmentCount > chain.SegmentCount);
        AssertContinuous(closed);
        AssertComesBackToItsStart(closed);
    }

    /// <summary>Tangency at every new join is what "tangent arcs" means.</summary>
    [Theory]
    [InlineData(2.0, 3.0)]
    [InlineData(3.0, 2.0)]
    [InlineData(1.0, 4.0)]
    public void TheClosureIsTangentAtEveryJoin(double startRadius, double endRadius)
    {
        PolyCurve chain = Chain(new Point3d(0, 10, 0), new Point3d(0, 0, 0), new Point3d(10, 0, 0));

        PolyCurve closed = chain.ClosedWithLineAndTangentArcs(startRadius, endRadius);

        Curve[] pieces = closed.Segments();

        // Every join except the chain's own corner, which was a corner before this member ran and
        // is not this member's to smooth.
        for (int index = 2; index < pieces.Length; index++)
        {
            Vector3d leaving = pieces[index - 1].TangentAt(pieces[index - 1].Domain.Max);
            Vector3d arriving = pieces[index].TangentAt(pieces[index].Domain.Min);

            Assert.True(
                (leaving - arriving).Length < 1e-6,
                $"the direction jumps from {leaving} to {arriving} at join {index}.");
        }

        // And the closure arrives at the chain's start travelling the way the chain sets off.
        Vector3d back = pieces[^1].TangentAt(pieces[^1].Domain.Max);
        Vector3d away = pieces[0].TangentAt(pieces[0].Domain.Min);

        Assert.True((back - away).Length < 1e-6, $"it arrives travelling {back}, not {away}.");
    }

    /// <summary>The arcs have the radii they were asked for, and in the right places.</summary>
    [Fact]
    public void EachArcHasTheRadiusItWasAskedFor()
    {
        PolyCurve chain = Chain(new Point3d(0, 10, 0), new Point3d(0, 0, 0), new Point3d(10, 0, 0));

        PolyCurve closed = chain.ClosedWithLineAndTangentArcs(startRadius: 2.0, endRadius: 5.0);

        Curve[] pieces = closed.Segments();

        // The first added piece leaves the chain's end, so it carries the end radius; the last
        // arrives at the chain's start, so it carries the start radius.
        Assert.Equal(5.0, ((Arc)pieces[2]).Radius, 1e-9);
        Assert.Equal(2.0, ((Arc)pieces[^1]).Radius, 1e-9);
    }

    /// <summary>
    /// <b>The branch.</b> The shortest closure is free to cut straight through the shape it is
    /// closing, so the candidates that keep clear of the chain are preferred. Taking the shortest
    /// unconditionally must turn this red.
    /// </summary>
    [Theory]
    [InlineData(2.0, 3.0)]
    [InlineData(1.5, 2.5)]
    public void TheClosureDoesNotCutThroughTheShapeItCloses(double startRadius, double endRadius)
    {
        PolyCurve chain = Chain(new Point3d(0, 10, 0), new Point3d(0, 0, 0), new Point3d(10, 0, 0));

        PolyCurve closed = chain.ClosedWithLineAndTangentArcs(startRadius, endRadius);

        AssertDoesNotCrossItself(closed);
    }

    /// <summary>
    /// <b>The branch, on the fixtures that actually separate the rules.</b> On an L the shortest
    /// candidate happens to be the clear one, so an L proves nothing — these two were found by
    /// enumerating the four candidates over random chains and keeping the ones where the shortest
    /// crosses and another does not. Taking the shortest unconditionally turns both red.
    /// </summary>
    /// <param name="corners">The chain's corners, flattened to x and y pairs.</param>
    /// <param name="startRadius">The radius at the chain's start.</param>
    /// <param name="endRadius">The radius at the chain's end.</param>
    [Theory]
    [InlineData(new double[] { 1, 3, 5, 4, 5, 1 }, 1.0, 3.0)]
    [InlineData(new double[] { 1, 2, 11, 1, 8, 9, 11, 9 }, 1.5, 1.0)]
    public void TheShortestClosureIsNotAlwaysTheOneThatKeepsClear(
        double[] corners, double startRadius, double endRadius)
    {
        Point3d[] points = new Point3d[corners.Length / 2];
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = new Point3d(corners[index * 2], corners[(index * 2) + 1], 0.0);
        }

        PolyCurve closed = Chain(points).ClosedWithLineAndTangentArcs(startRadius, endRadius);

        AssertContinuous(closed);
        AssertComesBackToItsStart(closed);
        AssertDoesNotCrossItself(closed);
    }

    /// <summary>And on a chain of three segments, where the shape has more to cut through.</summary>
    [Fact]
    public void NorOnALongerChain()
    {
        PolyCurve chain = Chain(
            new Point3d(0, 12, 0),
            new Point3d(0, 0, 0),
            new Point3d(14, 0, 0),
            new Point3d(14, 6, 0));

        PolyCurve closed = chain.ClosedWithLineAndTangentArcs(2.0, 3.0);

        AssertContinuous(closed);
        AssertComesBackToItsStart(closed);
        AssertDoesNotCrossItself(closed);
    }

    /// <summary>It works off the world XY plane, which is where an inferred normal would show.</summary>
    [Fact]
    public void ItWorksInATiltedPlane()
    {
        PolyCurve chain = Chain(
            new Point3d(0, 10, 10), new Point3d(0, 0, 0), new Point3d(7, 0, 0));

        PolyCurve closed = chain.ClosedWithLineAndTangentArcs(2.0, 3.0);

        AssertContinuous(closed);
        AssertComesBackToItsStart(closed);
    }

    /// <summary>
    /// <b>A radius far larger than the chain is not an error, and finding that out is worth a
    /// test.</b> Moving a centre further from its end moves it further from the <i>other</i> centre
    /// too, so a bigger radius does not run out of room the way it does in a fillet — it just gives
    /// an enormous loop, which is what was asked for.
    /// </summary>
    [Fact]
    public void AnAbsurdRadiusGivesAnAbsurdClosureRatherThanAnError()
    {
        PolyCurve chain = Chain(new Point3d(0, 1, 0), new Point3d(0, 0, 0), new Point3d(1, 0, 0));

        PolyCurve closed = chain.ClosedWithLineAndTangentArcs(500.0, 0.5);

        AssertContinuous(closed);
        AssertComesBackToItsStart(closed);
        Assert.True(closed.Length > 500.0, $"a radius of 500 gave a chain only {closed.Length} long.");
    }

    /// <summary>A chain that is already closed has no gap to close.</summary>
    [Fact]
    public void AnAlreadyClosedChainIsRefused()
    {
        PolyCurve square = Chain(
            new Point3d(0, 0, 0),
            new Point3d(10, 0, 0),
            new Point3d(10, 10, 0),
            new Point3d(0, 0, 0));

        Assert.Throws<InvalidOperationException>(() => square.ClosedWithLineAndTangentArcs(1.0, 2.0));
    }

    /// <summary>A chain that is not planar has no plane for its closure to lie in.</summary>
    [Fact]
    public void ANonPlanarChainIsRefused()
    {
        PolyCurve chain = Chain(
            new Point3d(0, 10, 0),
            new Point3d(0, 0, 0),
            new Point3d(10, 0, 0),
            new Point3d(10, 0, 10));

        Assert.Throws<InvalidOperationException>(() => chain.ClosedWithLineAndTangentArcs(2.0, 3.0));
    }

    [Theory]
    [InlineData(0.0, 2.0)]
    [InlineData(2.0, 0.0)]
    [InlineData(-1.0, 2.0)]
    [InlineData(2.0, double.NaN)]
    [InlineData(double.PositiveInfinity, 2.0)]
    public void ARadiusThatIsNotARadiusIsRefused(double startRadius, double endRadius)
    {
        PolyCurve chain = Chain(new Point3d(0, 10, 0), new Point3d(0, 0, 0), new Point3d(10, 0, 0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => chain.ClosedWithLineAndTangentArcs(startRadius, endRadius));
    }

    /// <summary>The chain runs unbroken from piece to piece.</summary>
    /// <param name="chain">The chain.</param>
    private static void AssertContinuous(PolyCurve chain)
    {
        Curve[] pieces = chain.Segments();

        for (int index = 1; index < pieces.Length; index++)
        {
            double gap = pieces[index - 1].EndPoint.DistanceTo(pieces[index].StartPoint);

            Assert.True(gap < 1e-6, $"pieces {index - 1} and {index} are {gap} apart.");
        }
    }

    /// <summary>
    /// It comes back to where it started — to within rounding, because every point of the closure is
    /// evaluated and <see cref="Curve.IsClosed"/> is exact equality by doctrine.
    /// </summary>
    /// <param name="chain">The chain.</param>
    private static void AssertComesBackToItsStart(PolyCurve chain)
    {
        double gap = chain.EndPoint.DistanceTo(chain.StartPoint);

        Assert.True(gap < 1e-9, $"the chain does not come back to its start: {gap} apart.");
    }

    /// <summary>
    /// The closed outline does not cross itself anywhere.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <remarks>
    /// <b>The last point is snapped to the first before the question is asked.</b> The chain closes
    /// to a few ulps rather than to the bit, and <see cref="PolyLine.SelfIntersections"/> skips the
    /// wrap pair only on a polyline that is <i>exactly</i> closed — so without the snap it reports a
    /// false crossing at the outline's own first corner.
    /// </remarks>
    private static void AssertDoesNotCrossItself(PolyCurve chain)
    {
        Point3d[] sampled = chain.Tessellate(new Tolerance(1e-4, Angle.FromDegrees(0.5), 1e-12));
        sampled[^1] = sampled[0];

        CurveIntersections crossings = new PolyLine(sampled).SelfIntersections();

        Assert.True(
            crossings.Points.Count == 0,
            $"the closed outline crosses itself {crossings.Points.Count} times, first at "
            + $"{(crossings.Points.Count > 0 ? crossings.Points[0].Point.ToString() : "nowhere")}.");
    }

    /// <summary>A chain of straight segments through the given points.</summary>
    /// <param name="points">The corners, in order.</param>
    /// <returns>The chain.</returns>
    private static PolyCurve Chain(params Point3d[] points)
    {
        List<Curve> lines = [];
        for (int index = 1; index < points.Length; index++)
        {
            lines.Add(new Line(points[index - 1], points[index]));
        }

        return PolyCurve.FromJoinedCurves(lines);
    }
}
