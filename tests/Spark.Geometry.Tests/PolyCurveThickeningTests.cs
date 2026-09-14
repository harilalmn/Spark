using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="PolyCurve.FromThickenedCurve"/> and <see cref="PolyCurve.FromThickenedCurveAlong"/> —
/// `E2-T72`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the reversal of the return side, and it does not fail the way you would
/// expect.</b> Both sides come out running the way the centre line ran, so the second has to be
/// turned round — but leaving that out does <i>not</i> produce a chain with a gap in it. The caps
/// are built from whatever ends they find, so the result still joins and still closes: it is a
/// <b>bow-tie</b>, and it passes every test of continuity and closure there is. What catches it is
/// the perimeter, and that a bow-tie crosses itself.
/// </para>
/// <para>
/// <b>The two members differ only in the direction they thicken</b>, and that difference is the
/// reading being taken of Dynamo's pair, so it is pinned rather than assumed: a curve in the world
/// XY plane comes back flat from one and standing up from the other.
/// </para>
/// </remarks>
public sealed class PolyCurveThickeningTests
{
    private static readonly Point3d From = new(0, 0, 0);
    private static readonly Point3d To = new(10, 0, 0);

    /// <summary>A straight centre line gives a rectangle, and its perimeter is arithmetic.</summary>
    [Fact]
    public void AStraightCentreLineGivesARectangle()
    {
        PolyCurve outline = PolyCurve.FromThickenedCurve(new Line(From, To), 4.0, Vector3d.ZAxis);

        Assert.Equal(4, outline.SegmentCount);

        // Two sides of ten and two caps of four.
        Assert.Equal((2.0 * 10.0) + (2.0 * 4.0), outline.Length, 1e-9);
    }

    /// <summary>
    /// The outline closes. <b>This is not the branch</b> — a bow-tie closes perfectly well — and it
    /// is here because closure is worth asserting in its own right.
    /// </summary>
    /// <param name="thickness">The thickness.</param>
    [Theory]
    [InlineData(1.0)]
    [InlineData(4.0)]
    [InlineData(0.25)]
    public void TheOutlineCloses(double thickness)
    {
        PolyCurve outline =
            PolyCurve.FromThickenedCurve(new Line(From, To), thickness, Vector3d.ZAxis);

        AssertContinuous(outline);
        Assert.True(
            outline.EndPoint.DistanceTo(outline.StartPoint) < 1e-9,
            $"the outline does not come back to its start: {outline.EndPoint} against {outline.StartPoint}.");
    }

    /// <summary>
    /// <b>The branch.</b> An unreversed return side gives a bow-tie — closed, continuous, and
    /// crossing itself in the middle. Both members are checked, because both close their own loop.
    /// </summary>
    [Fact]
    public void TheOutlineIsNotABowTie()
    {
        AssertDoesNotCrossItself(
            PolyCurve.FromThickenedCurve(new Line(From, To), 4.0, Vector3d.ZAxis));
        AssertDoesNotCrossItself(
            PolyCurve.FromThickenedCurveAlong(new Line(From, To), 4.0, Vector3d.ZAxis));
        AssertDoesNotCrossItself(
            PolyCurve.FromThickenedCurve(
                Arc.FromCenterStartEnd(Point3d.Origin, new Point3d(10, 0, 0), new Point3d(0, 10, 0)),
                2.0,
                Vector3d.ZAxis));
    }

    /// <summary>
    /// <b>The centre line is the centre.</b> Half the thickness reaches each side, which a member
    /// putting all of it on one side would fail while still closing perfectly.
    /// </summary>
    [Fact]
    public void HalfTheThicknessGoesToEachSide()
    {
        Line centre = new(From, To);

        PolyCurve outline = PolyCurve.FromThickenedCurve(centre, 6.0, Vector3d.ZAxis);

        Curve[] pieces = outline.Segments();

        // The two long sides are pieces 0 and 2; each should sit three units off the centre line,
        // one on either side.
        double first = centre.ClosestPoint(pieces[0].PointAt(pieces[0].Domain.Mid))
            .DistanceTo(pieces[0].PointAt(pieces[0].Domain.Mid));
        double second = centre.ClosestPoint(pieces[2].PointAt(pieces[2].Domain.Mid))
            .DistanceTo(pieces[2].PointAt(pieces[2].Domain.Mid));

        Assert.Equal(3.0, first, 1e-9);
        Assert.Equal(3.0, second, 1e-9);

        Point3d onFirst = pieces[0].PointAt(pieces[0].Domain.Mid);
        Point3d onSecond = pieces[2].PointAt(pieces[2].Domain.Mid);

        Assert.True(
            onFirst.Y * onSecond.Y < 0.0,
            $"both sides landed on the same side of the centre line: {onFirst} and {onSecond}.");
    }

    /// <summary>The caps are straight lines, asserted by type so that changing it is visible.</summary>
    [Fact]
    public void TheEndsAreCappedWithStraightLines()
    {
        PolyCurve outline = PolyCurve.FromThickenedCurve(new Line(From, To), 2.0, Vector3d.ZAxis);

        Assert.IsType<Line>(outline.SegmentAt(1));
        Assert.IsType<Line>(outline.SegmentAt(3));
        Assert.Equal(2.0, outline.SegmentAt(1).Length, 1e-9);
        Assert.Equal(2.0, outline.SegmentAt(3).Length, 1e-9);
    }

    /// <summary>A curved centre line works too, and the outline still closes.</summary>
    [Fact]
    public void ACurvedCentreLineIsThickenedToo()
    {
        Arc centre = Arc.FromCenterStartEnd(
            Point3d.Origin, new Point3d(10, 0, 0), new Point3d(0, 10, 0));

        PolyCurve outline = PolyCurve.FromThickenedCurve(centre, 2.0, Vector3d.ZAxis);

        AssertContinuous(outline);
        Assert.Equal(4, outline.SegmentCount);

        // The two arcs are at radius 11 and radius 9, so the perimeter is a quarter of each
        // circumference plus the two caps.
        double expected = (Math.PI / 2.0 * 11.0) + (Math.PI / 2.0 * 9.0) + 2.0 + 2.0;

        Assert.Equal(expected, outline.Length, 1e-6);
    }

    /// <summary>
    /// <b>The reading being taken.</b> The same centre line in the world XY plane comes back
    /// <i>flat</i> from one member and <i>standing up</i> from the other, which is the whole of the
    /// difference between Dynamo's two names as this build reads them.
    /// </summary>
    [Fact]
    public void OneThickensInThePlaneAndTheOtherAlongTheVector()
    {
        Line centre = new(From, To);

        PolyCurve flat = PolyCurve.FromThickenedCurve(centre, 4.0, Vector3d.ZAxis);
        PolyCurve upright = PolyCurve.FromThickenedCurveAlong(centre, 4.0, Vector3d.ZAxis);

        // Flat: it spreads in y and stays at z = 0.
        BoundingBox flatBox = flat.BoundingBox;
        Assert.Equal(4.0, flatBox.Max.Y - flatBox.Min.Y, 1e-9);
        Assert.Equal(0.0, flatBox.Max.Z - flatBox.Min.Z, 1e-9);

        // Upright: it spreads in z and stays at y = 0.
        BoundingBox uprightBox = upright.BoundingBox;
        Assert.Equal(0.0, uprightBox.Max.Y - uprightBox.Min.Y, 1e-9);
        Assert.Equal(4.0, uprightBox.Max.Z - uprightBox.Min.Z, 1e-9);
    }

    /// <summary>
    /// Thickening along a direction is a translation, so it is exact for a curve whose offset would
    /// only be fitted — the sides are the same type as the centre line.
    /// </summary>
    [Fact]
    public void ThickeningAlongADirectionKeepsTheCurvesType()
    {
        Arc centre = Arc.FromCenterStartEnd(
            Point3d.Origin, new Point3d(5, 0, 0), new Point3d(0, 5, 0));

        PolyCurve outline = PolyCurve.FromThickenedCurveAlong(centre, 2.0, Vector3d.ZAxis);

        Assert.IsType<Arc>(outline.SegmentAt(0));
        Assert.IsType<Arc>(outline.SegmentAt(2));

        // Translated, not offset: both arcs keep the original radius.
        Assert.Equal(5.0, ((Arc)outline.SegmentAt(0)).Radius, 1e-9);
        Assert.Equal(5.0, ((Arc)outline.SegmentAt(2)).Radius, 1e-9);
    }

    /// <summary>Both members close the loop, so both are checked for it.</summary>
    [Fact]
    public void ThickeningAlongADirectionAlsoCloses()
    {
        PolyCurve outline =
            PolyCurve.FromThickenedCurveAlong(new Line(From, To), 3.0, Vector3d.ZAxis);

        AssertContinuous(outline);
        Assert.Equal((2.0 * 10.0) + (2.0 * 3.0), outline.Length, 1e-9);
    }

    /// <summary>A closed centre line is an annulus, which is two loops and not one polycurve.</summary>
    [Fact]
    public void AClosedCurveIsRefused()
    {
        Circle centre = new(Plane.WorldXY, 5.0);

        Assert.Throws<ArgumentException>(
            () => PolyCurve.FromThickenedCurve(centre, 2.0, Vector3d.ZAxis));
        Assert.Throws<ArgumentException>(
            () => PolyCurve.FromThickenedCurveAlong(centre, 2.0, Vector3d.ZAxis));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AThicknessThatIsNotAThicknessIsRefused(double thickness)
    {
        Line centre = new(From, To);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PolyCurve.FromThickenedCurve(centre, thickness, Vector3d.ZAxis));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PolyCurve.FromThickenedCurveAlong(centre, thickness, Vector3d.ZAxis));
    }

    [Fact]
    public void ANullCurveAndADirectionlessVectorAreBothRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => PolyCurve.FromThickenedCurve(null!, 1.0, Vector3d.ZAxis));
        Assert.Throws<ArgumentException>(
            () => PolyCurve.FromThickenedCurveAlong(new Line(From, To), 1.0, Vector3d.Zero));
    }

    /// <summary>The closed outline does not cross itself anywhere.</summary>
    /// <param name="chain">The outline.</param>
    /// <remarks>
    /// The last point is snapped to the first before the question is asked, because the outline
    /// closes to a few ulps rather than to the bit and
    /// <see cref="PolyLine.SelfIntersections"/> skips the wrap pair only on a polyline that is
    /// exactly closed.
    /// </remarks>
    private static void AssertDoesNotCrossItself(PolyCurve chain)
    {
        Point3d[] sampled = chain.Tessellate(new Tolerance(1e-4, Angle.FromDegrees(0.5), 1e-12));
        sampled[^1] = sampled[0];

        CurveIntersections crossings = new PolyLine(sampled).SelfIntersections();

        Assert.True(
            crossings.Points.Count == 0,
            $"the outline crosses itself {crossings.Points.Count} times - it is a bow-tie.");
    }

    /// <summary>The outline runs unbroken from piece to piece.</summary>
    /// <param name="chain">The outline.</param>
    private static void AssertContinuous(PolyCurve chain)
    {
        Curve[] pieces = chain.Segments();

        for (int index = 1; index < pieces.Length; index++)
        {
            double gap = pieces[index - 1].EndPoint.DistanceTo(pieces[index].StartPoint);

            Assert.True(gap < 1e-9, $"pieces {index - 1} and {index} are {gap} apart.");
        }
    }
}
