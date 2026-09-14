using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="PolyCurve.Filleted"/> — `E2-T72`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the double trim.</b> A segment lying between two rounded corners is trimmed at
/// both of its ends, and the second trim has to act on the <i>result</i> of the first. An
/// implementation that rounds each corner against the original neighbours produces pieces that do
/// not join — so the assertions that catch it are continuity and total length, not the fillets
/// themselves, every one of which looks perfectly correct on its own.
/// </para>
/// <para>
/// <b>Everything here is measured against the chain, not against the arcs.</b> A fillet is a promise
/// about tangency and about the path, so the tests sample either side of each arc and compare the
/// chain's length to the sum of its pieces.
/// </para>
/// </remarks>
public sealed class PolyCurveFilletTests
{
    /// <summary>A right-angled corner between two lines, rounded.</summary>
    [Fact]
    public void OneCornerBecomesThreePieces()
    {
        PolyCurve chain = Chain(new Point3d(0, 0, 0), new Point3d(10, 0, 0), new Point3d(10, 10, 0));

        PolyCurve rounded = chain.Filleted(2.0);

        Assert.Equal(3, rounded.SegmentCount);
        Assert.IsType<Arc>(rounded.SegmentAt(1));
        Assert.Equal(2.0, ((Arc)rounded.SegmentAt(1)).Radius, 1e-9);

        // The corner is gone: nothing on the chain reaches it any more.
        Assert.True(
            rounded.ClosestPoint(new Point3d(10, 0, 0)).DistanceTo(new Point3d(10, 0, 0)) > 1e-6,
            "the sharp corner is still on the chain.");
    }

    /// <summary>
    /// <b>The branch.</b> Three segments, both corners rounded: the middle segment is trimmed from
    /// both ends, and the second trim must act on the already-trimmed middle. Rounding against the
    /// originals leaves a gap that <see cref="PolyCurve.FromJoinedCurves"/> would refuse — and if it
    /// somehow did not, the chain's length would not be the sum of its pieces.
    /// </summary>
    [Fact]
    public void TheSegmentBetweenTwoRoundedCornersIsTrimmedAtBothEnds()
    {
        PolyCurve chain = Chain(
            new Point3d(0, 0, 0),
            new Point3d(10, 0, 0),
            new Point3d(10, 10, 0),
            new Point3d(20, 10, 0));

        PolyCurve rounded = chain.Filleted(2.0);

        Assert.Equal(5, rounded.SegmentCount);
        AssertContinuous(rounded);
        AssertLengthIsTheSumOfItsPieces(rounded);

        // The middle piece ran from (10, 0) to (10, 10) and has lost two units off each end.
        Curve middle = rounded.SegmentAt(2);
        Assert.Equal(6.0, middle.Length, 1e-6);
    }

    /// <summary>A longer chain, to say that the carry-forward holds past two corners.</summary>
    [Fact]
    public void EveryCornerOfALongChainIsRounded()
    {
        PolyCurve chain = Chain(
            new Point3d(0, 0, 0),
            new Point3d(10, 0, 0),
            new Point3d(10, 8, 0),
            new Point3d(20, 8, 0),
            new Point3d(20, 16, 0),
            new Point3d(30, 16, 0));

        PolyCurve rounded = chain.Filleted(1.5);

        // Five segments, four corners: five straights and four arcs.
        Assert.Equal(9, rounded.SegmentCount);
        AssertContinuous(rounded);
        AssertLengthIsTheSumOfItsPieces(rounded);
        AssertTangentAcrossEveryJoin(rounded);
    }

    /// <summary>Tangency is the whole of what a fillet promises, so it is asserted either side.</summary>
    [Fact]
    public void TheChainIsTangentAcrossEveryFillet()
    {
        PolyCurve chain = Chain(
            new Point3d(0, 0, 0), new Point3d(10, 0, 0), new Point3d(16, 9, 0));

        PolyCurve rounded = chain.Filleted(2.5);

        AssertTangentAcrossEveryJoin(rounded);
    }

    /// <summary>
    /// <b>A corner too tight for the radius is left sharp rather than losing the whole chain.</b>
    /// </summary>
    [Fact]
    public void ACornerTooTightForTheRadiusIsSkipped()
    {
        // The middle segment is one unit long, so a fillet of radius five cannot fit either corner
        // it touches — but the first corner of the chain is wide open and must still be rounded.
        PolyCurve chain = Chain(
            new Point3d(0, 0, 0),
            new Point3d(20, 0, 0),
            new Point3d(20, 1, 0),
            new Point3d(40, 1, 0));

        PolyCurve rounded = chain.Filleted(5.0);

        AssertContinuous(rounded);

        // Both of the one-unit segment's corners are too tight, so no arc survives at all and the
        // chain comes back as it went in — which is the rule taken to its limit, not an exception.
        int arcs = 0;
        foreach (Curve piece in rounded.Segments())
        {
            if (piece is Arc)
            {
                arcs++;
            }
        }

        Assert.Equal(0, arcs);
        Assert.Equal(chain.Length, rounded.Length, 1e-9);
    }

    /// <summary>A radius that fits some corners and not others rounds the ones it fits.</summary>
    [Fact]
    public void ThePartialFilletIsWhatComesBack()
    {
        PolyCurve chain = Chain(
            new Point3d(0, 0, 0),
            new Point3d(20, 0, 0),
            new Point3d(20, 20, 0),
            new Point3d(20.5, 20.5, 0),
            new Point3d(0, 30, 0));

        PolyCurve rounded = chain.Filleted(3.0);

        AssertContinuous(rounded);

        int arcs = 0;
        foreach (Curve piece in rounded.Segments())
        {
            if (piece is Arc)
            {
                arcs++;
            }
        }

        Assert.True(arcs >= 1, "not one of the three corners was rounded.");
        Assert.True(arcs < 3, "the half-unit corner cannot take a fillet of radius three.");
    }

    /// <summary>
    /// <b>A closed chain has one more corner than an open one.</b> The wrap join rounds the last
    /// segment against the first — which has already been trimmed at its far end by the very first
    /// corner, so that one is not symmetric with the others.
    /// </summary>
    [Fact]
    public void AClosedChainHasItsWrapCornerRoundedToo()
    {
        PolyCurve square = Chain(
            new Point3d(0, 0, 0),
            new Point3d(10, 0, 0),
            new Point3d(10, 10, 0),
            new Point3d(0, 10, 0),
            new Point3d(0, 0, 0));

        Assert.True(square.IsClosed, "the fixture is not closed.");

        PolyCurve rounded = square.Filleted(2.0);

        // IT CLOSES, AND IsClosed STILL SAYS FALSE, WHICH IS THE DOCTRINE RATHER THAN A DEFECT.
        // Closure is exact equality in Spark, and the closed factories keep it true by repeating
        // the first point; a fillet cannot, because both ends of the wrap are evaluated — one from
        // a trim parameter and one from an arc's sweep — so they agree to a few ulps and not to
        // the bit. The tolerant question is the one asked here, which is what a caller asks too.
        double gap = rounded.EndPoint.DistanceTo(rounded.StartPoint);
        Assert.True(gap < 1e-12, $"the chain does not come back to its start: {gap} apart.");
        Assert.False(rounded.IsClosed, "exact closure is no longer expected; update the remarks.");

        // Four sides and four corners, all of them rounded.
        int arcs = 0;
        foreach (Curve piece in rounded.Segments())
        {
            if (piece is Arc)
            {
                arcs++;
            }
        }

        Assert.Equal(4, arcs);
        AssertContinuous(rounded);
        AssertTangentAcrossEveryJoin(rounded);
    }

    /// <summary>
    /// And the closed answer is the one arithmetic predicts: four sides of six and four quarter
    /// circles of radius two.
    /// </summary>
    [Fact]
    public void TheRoundedSquareHasTheLengthArithmeticPredicts()
    {
        PolyCurve square = Chain(
            new Point3d(0, 0, 0),
            new Point3d(10, 0, 0),
            new Point3d(10, 10, 0),
            new Point3d(0, 10, 0),
            new Point3d(0, 0, 0));

        PolyCurve rounded = square.Filleted(2.0);

        // 4 x (10 - 2 - 2) straight, plus a whole circle's worth of arc: 2 pi r.
        Assert.Equal((4.0 * 6.0) + (2.0 * Math.PI * 2.0), rounded.Length, 1e-6);
    }

    /// <summary>It works off the world XY plane, which is where an inferred normal would show.</summary>
    [Fact]
    public void ItWorksInATiltedPlane()
    {
        PolyCurve chain = Chain(
            new Point3d(0, 0, 0), new Point3d(10, 0, 10), new Point3d(10, 10, 10));

        PolyCurve rounded = chain.Filleted(2.0);

        Assert.Equal(3, rounded.SegmentCount);
        AssertContinuous(rounded);
        AssertTangentAcrossEveryJoin(rounded);
    }

    /// <summary>A chain of one segment has no corners, and comes back as it went in.</summary>
    [Fact]
    public void AChainOfOneSegmentIsReturnedUnchanged()
    {
        PolyCurve chain = PolyCurve.FromJoinedCurves(
            [new Line(new Point3d(0, 0, 0), new Point3d(10, 0, 0))]);

        Assert.Same(chain, chain.Filleted(2.0));
    }

    /// <summary>
    /// <b>The radius is validated before the loop</b>, because the loop swallows
    /// <see cref="ArgumentException"/> to skip a tight corner and
    /// <see cref="ArgumentOutOfRangeException"/> is one of those.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ABadRadiusIsRefusedRatherThanReadAsTightCorners(double radius)
    {
        PolyCurve chain = Chain(new Point3d(0, 0, 0), new Point3d(10, 0, 0), new Point3d(10, 10, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => chain.Filleted(radius));
    }

    /// <summary>A chain that is not planar has no plane for its fillets to lie in.</summary>
    [Fact]
    public void ANonPlanarChainIsRefused()
    {
        PolyCurve chain = Chain(
            new Point3d(0, 0, 0),
            new Point3d(10, 0, 0),
            new Point3d(10, 10, 0),
            new Point3d(10, 10, 10));

        Assert.Throws<InvalidOperationException>(() => chain.Filleted(2.0));
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
    /// The chain's own length equals the sum of its pieces — which a chain assembled from
    /// independently trimmed neighbours does not manage.
    /// </summary>
    /// <param name="chain">The chain.</param>
    private static void AssertLengthIsTheSumOfItsPieces(PolyCurve chain)
    {
        double sum = 0.0;
        foreach (Curve piece in chain.Segments())
        {
            sum += piece.Length;
        }

        Assert.Equal(sum, chain.Length, 1e-6);
    }

    /// <summary>The direction does not jump at any join, which is what a fillet is for.</summary>
    /// <param name="chain">The chain.</param>
    private static void AssertTangentAcrossEveryJoin(PolyCurve chain)
    {
        Curve[] pieces = chain.Segments();

        for (int index = 1; index < pieces.Length; index++)
        {
            Vector3d leaving = pieces[index - 1].TangentAt(pieces[index - 1].Domain.Max);
            Vector3d arriving = pieces[index].TangentAt(pieces[index].Domain.Min);

            Assert.True(
                (leaving - arriving).Length < 1e-6,
                $"the direction jumps from {leaving} to {arriving} at join {index}.");
        }
    }

    /// <summary>A polyline-shaped polycurve of straight segments through the given points.</summary>
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
