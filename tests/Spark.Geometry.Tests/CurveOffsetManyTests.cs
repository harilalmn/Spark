using System;
using System.Collections.Generic;
using System.Linq;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="CurveOffset.OffsetMany"/>: the offset of a curve as the several pieces it is actually
/// made of (<c>E2-T71</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect it exists for.</b> <see cref="CurveOffset.Offset"/> samples a
/// <see cref="PolyCurve"/> end to end and fits <b>one</b> curve through the samples. A fitted curve
/// is always connected, whatever it was fitted to — so where the offset does not exist, the fit
/// draws through the hole anyway and the caller cannot tell.
/// </para>
/// <para>
/// <b>The decision these tests pin is where a run breaks</b>, and it is decided on the geometry
/// rather than on the input's topology: consecutive offsets whose ends coincide continue one run,
/// and anything else starts a new one. **A corner therefore breaks the run**, because the offsets
/// of two segments meeting at an angle end at different points — filling that gap needs the arc or
/// extend decision, which is not built and is not this member's to make.
/// </para>
/// </remarks>
public sealed class CurveOffsetManyTests
{
    private static Vector3d Up => Vector3d.ZAxis;

    private static Tolerance Fine => new(1e-9, Angle.FromDegrees(1), 1e-12);

    /// <summary>
    /// <b>Anything that is not a polycurve has one piece, and it is the one <c>Offset</c> gives.</b>
    /// The two members must never disagree about the simple case, which is nearly every case.
    /// </summary>
    [Fact]
    public void ASingleCurveGivesOnePieceEqualToTheOrdinaryOffset()
    {
        Line line = new(new Point3d(0, 0, 0), new Point3d(4, 0, 0));

        IReadOnlyList<(Curve Curve, bool Exact)> many = CurveOffset.OffsetMany(line, 1.0, Up, Fine);
        (Curve Curve, bool Exact) one = CurveOffset.Offset(line, 1.0, Up, Fine);

        (Curve Curve, bool Exact) only = Assert.Single(many);

        Assert.Equal(one.Exact, only.Exact);
        Assert.True(one.Curve.StartPoint.EqualsWithin(only.Curve.StartPoint, Fine));
        Assert.True(one.Curve.EndPoint.EqualsWithin(only.Curve.EndPoint, Fine));
    }

    /// <summary>
    /// <b>Two collinear segments offset into one run</b>, because their offsets meet. This is the
    /// test that stops the member degenerating into *one piece per segment* — which would make it
    /// a report on how the caller built the curve rather than on the offset.
    /// </summary>
    [Fact]
    public void CollinearSegmentsWhoseOffsetsMeetStayOneRun()
    {
        PolyCurve straight = PolyCurve.FromJoinedCurves(
            [
                new Line(new Point3d(0, 0, 0), new Point3d(2, 0, 0)),
                new Line(new Point3d(2, 0, 0), new Point3d(5, 0, 0)),
            ],
            Fine);

        (Curve Curve, bool Exact) only = Assert.Single(CurveOffset.OffsetMany(straight, 1.0, Up, Fine));

        Assert.True(only.Exact, "two lines offset exactly, so the run is exact.");

        // Positive is towards the left of travel seen from the normal side, which for a line
        // running east under a +Z normal is north. Written out because the first version of this
        // test guessed the other way.
        Assert.True(only.Curve.StartPoint.EqualsWithin(new Point3d(0, 1, 0), Fine), only.Curve.StartPoint.ToString());
        Assert.True(only.Curve.EndPoint.EqualsWithin(new Point3d(5, 1, 0), Fine), only.Curve.EndPoint.ToString());
    }

    /// <summary>
    /// <b>A corner breaks the run, and that is the documented answer rather than a shortcoming.</b>
    /// The offsets of two segments meeting at an angle end at different points: outwards there is a
    /// wedge of empty space between them, inwards they overlap. Both are real, and closing either
    /// one is a decision — fill with an arc, or extend to the intersection — that this member does
    /// not make on the caller's behalf.
    /// </summary>
    [Fact]
    public void ACornerBreaksTheRunBecauseTheOffsetsDoNotMeetThere()
    {
        PolyCurve corner = PolyCurve.FromJoinedCurves(
            [
                new Line(new Point3d(0, 0, 0), new Point3d(4, 0, 0)),
                new Line(new Point3d(4, 0, 0), new Point3d(4, 3, 0)),
            ],
            Fine);

        IReadOnlyList<(Curve Curve, bool Exact)> pieces = CurveOffset.OffsetMany(corner, 1.0, Up, Fine);

        Assert.Equal(2, pieces.Count);
        Assert.All(pieces, piece => Assert.True(piece.Exact));

        // And the gap is real: the first piece ends where the second does not begin.
        Assert.False(pieces[0].Curve.EndPoint.EqualsWithin(pieces[1].Curve.StartPoint, Fine));
    }

    /// <summary>
    /// <b>The member's whole reason: a segment with no offset in this plane ends the run, and the
    /// rest of the curve still comes back.</b> A segment running along the normal leaves the plane
    /// the offset happens in — there is no offset of it there — and <c>Offset</c> would fit one
    /// curve straight across the hole and say nothing about it.
    /// </summary>
    /// <remarks>
    /// <b>The first version of this test used an arc offset past its own radius, and that was
    /// wrong.</b> It does not collapse: the exact branch declines a negative radius and the fitted
    /// branch then returns the true locus, an arc of the same centre on the other side, which is a
    /// real curve and a correct answer. Assuming otherwise cost a red test and is why the member's
    /// remarks now say so explicitly.
    /// </remarks>
    [Fact]
    public void ASegmentThatLeavesThePlaneEndsTheRunAndTheRestSurvives()
    {
        PolyCurve chain = PolyCurve.FromJoinedCurves(
            [
                new Line(new Point3d(0, 0, 0), new Point3d(4, 0, 0)),
                new Line(new Point3d(4, 0, 0), new Point3d(4, 0, 3)),
                new Line(new Point3d(4, 0, 3), new Point3d(8, 0, 3)),
            ],
            Fine);

        IReadOnlyList<(Curve Curve, bool Exact)> pieces = CurveOffset.OffsetMany(chain, 1.0, Up, Fine);

        // The vertical segment has no offset in the XY plane; the two horizontal ones do.
        Assert.Equal(2, pieces.Count);
        Assert.All(pieces, piece => Assert.True(piece.Exact));

        Assert.True(pieces[0].Curve.StartPoint.EqualsWithin(new Point3d(0, 1, 0), Fine), pieces[0].Curve.StartPoint.ToString());
        Assert.True(pieces[1].Curve.EndPoint.EqualsWithin(new Point3d(8, 1, 3), Fine), pieces[1].Curve.EndPoint.ToString());

        // And Offset, asked the same question, refuses outright rather than reporting the two
        // pieces that do exist. That difference is the row.
        Assert.Throws<ArgumentException>(() => CurveOffset.Offset(chain, 1.0, Up, Fine));
    }

    /// <summary>
    /// <b>A curve with no offset anywhere is a refusal, not an empty list.</b> Returning nothing
    /// would make *the offset does not exist* indistinguishable from *the offset is nothing*, and
    /// the caller would go on to draw an empty result.
    /// </summary>
    /// <remarks>
    /// The message names the **normal** rather than the distance, because a curve every one of
    /// whose segments leaves the plane is a curve in the wrong plane, and telling that caller to
    /// change their distance would send them the wrong way.
    /// </remarks>
    [Fact]
    public void ACurveWithNoOffsetAnywhereRefusesRatherThanReturningNothing()
    {
        PolyCurve upright = PolyCurve.FromJoinedCurves(
            [
                new Line(new Point3d(0, 0, 0), new Point3d(0, 0, 2)),
                new Line(new Point3d(0, 0, 2), new Point3d(0, 0, 5)),
            ],
            Fine);

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => CurveOffset.OffsetMany(upright, 1.0, Up, Fine));

        Assert.Contains("an offset in the plane given", refused.Message, StringComparison.Ordinal);
        Assert.Contains("the normal is the thing to change", refused.Message, StringComparison.Ordinal);
        Assert.Equal("curve", refused.ParamName);
    }

    /// <summary>
    /// <b>A line along the offset normal is refused by name, not by arithmetic.</b> Until
    /// 2026-09-15 the exact branch for a <see cref="Line"/> reached
    /// <c>Vector3d.Normalised()</c> on a zero vector and reported *a zero-length vector cannot be
    /// normalised* as an <see cref="InvalidOperationException"/> — while every other curve shape
    /// reported the same condition as an <see cref="ArgumentException"/> naming the curve. One
    /// caller could not catch both, and the message described the arithmetic rather than the
    /// mistake.
    /// </summary>
    [Fact]
    public void ALineAlongTheNormalIsRefusedTheSameWayEveryOtherCurveIs()
    {
        Line upright = new(new Point3d(0, 0, 0), new Point3d(0, 0, 4));

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => CurveOffset.Offset(upright, 1.0, Up, Fine));

        Assert.Equal("curve", refused.ParamName);
        Assert.Contains("does not lie in that plane", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The pieces come back in order along the original curve.</b> A caller joining them, or
    /// drawing them, or measuring the gaps between them needs the order, and a set would be a
    /// different and less useful answer.
    /// </summary>
    [Fact]
    public void ThePiecesAreInOrderAlongTheOriginalCurve()
    {
        PolyCurve zigzag = PolyCurve.FromJoinedCurves(
            [
                new Line(new Point3d(0, 0, 0), new Point3d(2, 0, 0)),
                new Line(new Point3d(2, 0, 0), new Point3d(4, 2, 0)),
                new Line(new Point3d(4, 2, 0), new Point3d(6, 0, 0)),
            ],
            Fine);

        double[] startsX = [.. CurveOffset.OffsetMany(zigzag, 0.5, Up, Fine).Select(p => p.Curve.StartPoint.X)];

        Assert.Equal(3, startsX.Length);
        Assert.Equal(startsX.OrderBy(x => x), startsX);
    }

    /// <summary>
    /// <b>A validation failure is still a validation failure.</b> The per-segment
    /// <c>try</c>/<c>catch</c> that lets a collapsed piece end a run must not also swallow *this
    /// normal has no length*, which is a caller mistake about the whole call and not a fact about
    /// one segment.
    /// </summary>
    /// <remarks>
    /// This is the test that stops the catch widening. It is the shape of defect the catch invites:
    /// a handler written for one condition quietly absorbing every other one that shares its type.
    /// </remarks>
    [Fact]
    public void ABadNormalIsRefusedRatherThanTreatedAsACollapsedPiece()
    {
        PolyCurve chain = PolyCurve.FromJoinedCurves(
            [
                new Line(new Point3d(0, 0, 0), new Point3d(2, 0, 0)),
                new Line(new Point3d(2, 0, 0), new Point3d(5, 0, 0)),
            ],
            Fine);

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => CurveOffset.OffsetMany(chain, 1.0, Vector3d.Zero, Fine));

        Assert.Equal("normal", refused.ParamName);
    }
}
