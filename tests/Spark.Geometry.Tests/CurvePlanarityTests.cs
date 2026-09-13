using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Curve.IsPlanar(in Tolerance)"/> and <see cref="Curve.PlaneOf(in Tolerance)"/> —
/// `E2-T71` family (2).
/// </summary>
/// <remarks>
/// <para>
/// <b>The fit is not the test, and that is what these tests are mostly about.</b> A best-fit plane
/// exists for <i>any</i> set of points whatever, so an implementation that fitted one and returned
/// it would report every curve as planar — a helix included. What decides the answer is how far the
/// curve strays from the plane that was fitted to it, and the case that separates a working
/// implementation from a broken one is a curve that is <b>nearly</b> planar and is not: a wildly
/// non-planar curve fails even a broken check, by accident.
/// </para>
/// <para>
/// <b>And the tolerance is the caller's.</b> What counts as planar at the scale of a building is
/// not what counts at the scale of a bolt (ADR-0010), so the same curve has to answer differently
/// under two tolerances.
/// </para>
/// </remarks>
public sealed class CurvePlanarityTests
{
    private static Tolerance At(double linear) => new(linear, Angle.FromDegrees(0.001), 1e-12);

    [Fact]
    public void ACircleKnowsItsOwnPlaneWithoutFittingAnything()
    {
        Plane tilted = Plane.FromOriginNormal(new Point3d(1.0, 2.0, 3.0), new Vector3d(1.0, 2.0, 3.0));
        Circle circle = Circle.FromPlaneRadius(tilted, 2.5);

        Assert.True(circle.IsPlanar());

        Plane? found = circle.PlaneOf();
        Assert.NotNull(found);
        Assert.Equal(1.0, Math.Abs(found!.Value.Normal.Dot(tilted.Normal)), 1e-12);
        Assert.True(found.Value.Contains(circle.PointAt(1.0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheAnalyticCurvesAnswerFromTheFrameTheyWereBuiltWith(int which)
    {
        Plane frame = Plane.FromOriginNormal(new Point3d(-1.0, 0.5, 2.0), new Vector3d(0.0, 1.0, 1.0));

        Curve curve = which switch
        {
            0 => Arc.FromPlaneRadiusAngles(frame, 3.0, Angle.FromDegrees(20.0), Angle.FromDegrees(140.0)),
            1 => Circle.FromPlaneRadius(frame, 3.0),
            _ => EllipseCurve.FromPlaneRadiiAngles(frame, 4.0, 2.0, Angle.Zero, Angle.FromDegrees(200.0)),
        };

        Assert.True(curve.IsPlanar());
        Assert.Equal(1.0, Math.Abs(curve.PlaneOf()!.Value.Normal.Dot(frame.Normal)), 1e-12);
    }

    [Fact]
    public void AStraightLineIsPlanarAndHasNoPlane()
    {
        // The decision this pair of members exists to make, pinned rather than left to be
        // discovered: `is there a plane containing this curve` and `which plane is it` are
        // different questions, and a line is where they give different answers. Every plane
        // through the line contains it, so choosing one would invent a rotation nobody asked for.
        Line line = new(new Point3d(1.0, 1.0, 1.0), new Point3d(4.0, 5.0, 6.0));

        Assert.True(line.IsPlanar());
        Assert.Null(line.PlaneOf());
    }

    [Fact]
    public void APolyLineWithCollinearVerticesIsTheSameCase()
    {
        // Not a special case in the code — it falls out of the same fit refusing to name a normal
        // for collinear points — but it is a second way in, so it is pinned separately.
        PolyLine straight = PolyLine.FromPoints(
        [
            Point3d.Origin,
            new Point3d(1.0, 0.0, 0.0),
            new Point3d(3.0, 0.0, 0.0),
        ]);

        Assert.True(straight.IsPlanar());
        Assert.Null(straight.PlaneOf());
    }

    [Fact]
    public void AFlatPolyLineFindsItsPlane()
    {
        PolyLine hexagon = PolyLine.FromRegularPolygon(
            Plane.FromOriginNormal(Point3d.Origin, new Vector3d(1.0, 1.0, 1.0)), 2.0, 6);

        Assert.True(hexagon.IsPlanar());

        Plane? found = hexagon.PlaneOf();
        Assert.NotNull(found);
        Assert.Equal(
            1.0,
            Math.Abs(found!.Value.Normal.Dot(new Vector3d(1.0, 1.0, 1.0).Normalised())),
            1e-12);
    }

    [Fact]
    public void AHelixIsNotPlanarAndHasNoPlane()
    {
        Helix helix = Helix.FromAxis(
            Point3d.Origin, Vector3d.ZAxis, new Point3d(2.0, 0.0, 0.0), 3.0, Angle.FullTurn);

        Assert.False(helix.IsPlanar());
        Assert.Null(helix.PlaneOf());
    }

    [Fact]
    public void ACurveThatIsNearlyPlanarAndIsNotIsTheCaseThatMatters()
    {
        // `E2-T71`: the branch this proves is the residual check. DELETE IT AND THE FIT ALONE
        // DECIDES, which makes everything planar — and a wildly non-planar curve would still fail,
        // by luck, so it is this curve that has to be here. One control point lifted by a
        // hundredth, on a curve about ten across.
        NurbsCurve almost = new(
            3,
            [
                new Point3d(0.0, 0.0, 0.0),
                new Point3d(3.0, 4.0, 0.0),
                new Point3d(6.0, 4.0, 0.01),
                new Point3d(9.0, 0.0, 0.0),
            ],
            [0, 0, 0, 0, 1, 1, 1, 1]);

        Assert.False(almost.IsPlanar(At(1e-6)));
        Assert.Null(almost.PlaneOf(At(1e-6)));

        // And it IS planar at a tolerance that does not care about a hundredth, which is the other
        // half of the same claim.
        Assert.True(almost.IsPlanar(At(0.1)));
        Assert.NotNull(almost.PlaneOf(At(0.1)));
    }

    [Fact]
    public void TheSameCurveAnswersDifferentlyUnderDifferentTolerances()
    {
        // ADR-0010, stated as a test: an ambient tolerance would make this one question with one
        // answer, and it is not.
        NurbsCurve bowed = new(
            3,
            [
                new Point3d(0.0, 0.0, 0.0),
                new Point3d(1.0, 1.0, 0.0),
                new Point3d(2.0, 1.0, 0.05),
                new Point3d(3.0, 0.0, 0.0),
            ],
            [0, 0, 0, 0, 1, 1, 1, 1]);

        Assert.False(bowed.IsPlanar(At(1e-4)));
        Assert.True(bowed.IsPlanar(At(1.0)));
    }

    [Fact]
    public void AClosedPlanarPolyLinesNormalFollowsItsWinding()
    {
        // `Plane.FromBestFit` signs its normal from the winding of the points, and a curve's plane
        // inherits that — so a loop and its reverse face opposite ways. Anything extruding along
        // this normal depends on it.
        PolyLine loop = PolyLine.FromRegularPolygon(Plane.WorldXY, 2.0, 5);

        Vector3d forwards = loop.PlaneOf()!.Value.Normal;
        Vector3d backwards = loop.Reversed().PlaneOf()!.Value.Normal;

        Assert.Equal(1.0, Math.Abs(forwards.Dot(Vector3d.ZAxis)), 1e-12);
        Assert.Equal(-1.0, forwards.Dot(backwards), 1e-12);
    }

    [Fact]
    public void APlanarPolyCurveFindsThePlaneItsSegmentsShare()
    {
        // The row this most directly answers beyond `Curve.IsPlanar` itself: `PolyCurve.BasePlane`
        // (`E2-T72`) is this member on a chain.
        PolyCurve chain = PolyCurve.FromJoinedCurves(
        [
            new Line(Point3d.Origin, new Point3d(3.0, 0.0, 0.0)),
            Arc.FromThreePoints(
                new Point3d(3.0, 0.0, 0.0), new Point3d(4.0, 1.0, 0.0), new Point3d(3.0, 2.0, 0.0)),
        ]);

        Assert.True(chain.IsPlanar());
        Assert.Equal(1.0, Math.Abs(chain.PlaneOf()!.Value.Normal.Dot(Vector3d.ZAxis)), 1e-9);
    }

    [Fact]
    public void ACurveOutOfPlaneByExactlyTheToleranceIsStillPlanar()
    {
        // The boundary, because `<=` versus `<` is the sort of thing that is decided by accident.
        // A triangle with one vertex lifted by a tenth: the best-fit plane through three points
        // passes through all three, so the strayed distance is zero and the answer is yes whatever
        // the tolerance — which is itself worth knowing, and is why the case above uses four
        // control points rather than three.
        PolyLine triangle = PolyLine.FromPoints(
        [
            Point3d.Origin,
            new Point3d(4.0, 0.0, 0.0),
            new Point3d(2.0, 3.0, 0.1),
            Point3d.Origin,
        ]);

        Assert.True(triangle.IsPlanar(At(1e-12)));
        Assert.NotNull(triangle.PlaneOf(At(1e-12)));
    }
}
