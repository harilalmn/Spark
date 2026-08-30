using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Knot removal — the one operation in this family allowed to change the curve.
/// </summary>
/// <remarks>
/// <para>
/// Insertion, trimming and elevation are all tested by asserting that <i>nothing changed</i>.
/// Removal cannot be, because changing the curve slightly is the point: a knot is removable only
/// if the curve is smooth enough across it to be described without one. So the tests are shaped
/// differently — <b>round trips</b> (insert then remove returns the original), <b>refusals</b> (a
/// knot that matters is not removed), and <b>the tolerance meaning what it says</b> (a removal
/// accepted within a tolerance really did stay inside it).
/// </para>
/// <para>
/// A generous tolerance for these tests rather than the default, because the default is a
/// modelling tolerance and what is under test is the decision procedure, not the number.
/// </para>
/// </remarks>
public sealed class NurbsKnotRemovalTests
{
    private static Tolerance Loose => new(1e-6, Angle.FromDegrees(0.001), 1e-12);

    /// <summary>
    /// <b>The round trip.</b> A knot that was just inserted carries no information — insertion is
    /// exact — so removing it must give back the curve that was there before, control point for
    /// control point.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AKnotThatWasJustInsertedCanAlwaysBeRemoved(bool rational)
    {
        NurbsCurve original = Sample(rational);
        double t = original.Domain.Min + (original.Domain.Length * 0.37);

        NurbsCurve refined = original.WithKnotInserted(t);
        (NurbsCurve reduced, int removed) = refined.WithKnotRemoved(t, 1, Loose);

        Assert.Equal(1, removed);
        Assert.Equal(original.Knots.Count, reduced.Knots.Count);
        Assert.Equal(original.ControlPoints().Length, reduced.ControlPoints().Length);

        Point3d[] before = original.ControlPoints();
        Point3d[] after = reduced.ControlPoints();

        for (int i = 0; i < before.Length; i++)
        {
            Assert.True(
                before[i].EqualsWithin(after[i], Loose),
                $"Control point {i} came back as {after[i]} instead of {before[i]}.");
        }
    }

    /// <summary>And the curve itself is unmoved, which is the claim a caller cares about.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInsertRemoveRoundTripLeavesTheCurveWhereItWas(bool rational)
    {
        NurbsCurve original = Sample(rational);
        double t = original.Domain.Min + (original.Domain.Length * 0.62);

        (NurbsCurve reduced, int removed) = original.WithKnotInserted(t, 2).WithKnotRemoved(t, 2, Loose);

        Assert.Equal(2, removed);

        for (int i = 0; i <= 200; i++)
        {
            double u = original.Domain.Min + (original.Domain.Length * i / 200.0);
            Assert.True(original.PointAt(u).EqualsWithin(reduced.PointAt(u), Loose));
        }
    }

    /// <summary>
    /// <b>The refusal.</b> A knot carrying real shape — one at full multiplicity, making a corner —
    /// cannot be removed without moving the curve, so it is not removed. Zero is an ordinary
    /// answer.
    /// </summary>
    [Fact]
    public void AKnotThatCarriesShapeIsRefused()
    {
        // Degree 2 with an interior knot at multiplicity 2: a kink, and the control polygon is
        // bent hard across it so smoothing it out would move the curve a long way.
        NurbsCurve kinked = new(
            [
                new Point3d(0, 0, 0), new Point3d(5, 0, 0), new Point3d(5, 5, 0),
                new Point3d(10, 5, 0), new Point3d(10, 0, 0),
            ],
            new KnotVector(2, [0, 0, 0, 1, 1, 2, 2, 2]));

        (NurbsCurve result, int removed) = kinked.WithKnotRemoved(1.0, 1, Loose);

        Assert.Equal(0, removed);
        Assert.Equal(kinked.Knots.Count, result.Knots.Count);
    }

    /// <summary>
    /// Asking for more removals than are available returns what was possible rather than throwing.
    /// A caller learns which happened from the count.
    /// </summary>
    [Fact]
    public void AskingForMoreRemovalsThanArePossibleReturnsWhatWasDone()
    {
        NurbsCurve original = Sample(rational: false);
        double t = original.Domain.Min + (original.Domain.Length * 0.5);

        (NurbsCurve reduced, int removed) = original.WithKnotInserted(t, 1).WithKnotRemoved(t, 5, Loose);

        Assert.Equal(1, removed);
        Assert.Equal(original.Knots.Count, reduced.Knots.Count);
    }

    /// <summary>
    /// <b>The tolerance means what it says.</b> A removal that was accepted really did keep the
    /// curve inside the tolerance it was given — measured afterwards, independently of the decision
    /// that accepted it.
    /// </summary>
    [Fact]
    public void AnAcceptedRemovalStaysInsideTheToleranceItWasGiven()
    {
        Tolerance tight = new(1e-9, Angle.FromDegrees(0.001), 1e-12);

        NurbsCurve original = Sample(rational: true);
        double t = original.Domain.Min + (original.Domain.Length * 0.44);

        (NurbsCurve reduced, int removed) = original.WithKnotInserted(t).WithKnotRemoved(t, 1, tight);

        Assert.Equal(1, removed);

        for (int i = 0; i <= 300; i++)
        {
            double u = original.Domain.Min + (original.Domain.Length * i / 300.0);
            Assert.True(
                original.PointAt(u).DistanceTo(reduced.PointAt(u)) <= 1e-9,
                $"An accepted removal moved the curve by "
                + $"{original.PointAt(u).DistanceTo(reduced.PointAt(u))}, outside the tolerance given.");
        }
    }

    /// <summary>
    /// <b>What makes degree elevation minimal.</b> Elevation raises every interior knot to full
    /// multiplicity and never lowers it again, so an elevated curve carries control points it does
    /// not need. Reduction takes them back off without moving the curve.
    /// </summary>
    [Fact]
    public void ReducingAnElevatedCurveTakesBackWhatElevationAdded()
    {
        NurbsCurve original = Sample(rational: false);
        NurbsCurve elevated = original.WithDegreeElevated();

        (NurbsCurve reduced, int removed) = elevated.Reduced(Loose);

        Assert.True(removed > 0, "Elevation adds redundant knots and reduction should find them.");
        Assert.True(
            reduced.ControlPoints().Length < elevated.ControlPoints().Length,
            $"Reduced to {reduced.ControlPoints().Length} from {elevated.ControlPoints().Length}.");

        // And the curve is still the elevated one, which is still the original one.
        for (int i = 0; i <= 200; i++)
        {
            double u = original.Domain.Min + (original.Domain.Length * i / 200.0);
            Assert.True(original.PointAt(u).EqualsWithin(reduced.PointAt(u), Loose));
        }
    }

    /// <summary>
    /// Reduction never moves the curve further than its tolerance, even over many removals —
    /// because every candidate is measured against the <b>original</b> rather than against the
    /// previous step, so small errors cannot accumulate.
    /// </summary>
    [Fact]
    public void RepeatedReductionDoesNotAccumulateError()
    {
        NurbsCurve original = Sample(rational: true);

        // Refine heavily first so there is a great deal to take back off.
        NurbsCurve refined = original;
        foreach (double u in new[] { 0.13, 0.29, 0.41, 0.58, 0.67, 0.81 })
        {
            refined = refined.WithKnotInserted(original.Domain.Min + (original.Domain.Length * u));
        }

        (NurbsCurve reduced, int removed) = refined.Reduced(Loose);

        Assert.Equal(6, removed);

        for (int i = 0; i <= 300; i++)
        {
            double u = original.Domain.Min + (original.Domain.Length * i / 300.0);
            Assert.True(
                original.PointAt(u).DistanceTo(reduced.PointAt(u)) <= 1e-6,
                "Six removals accumulated past the tolerance each one was judged against.");
        }
    }

    /// <summary>
    /// Reducing a curve that has nothing to spare is a no-op, not an error and not a degradation.
    /// </summary>
    [Fact]
    public void ACurveWithNothingToSpareIsUnchanged()
    {
        NurbsCurve minimal = new(
            [new Point3d(0, 0, 0), new Point3d(1, 4, 0), new Point3d(4, 5, 0), new Point3d(7, 1, 0)],
            KnotVector.CreateClamped(3, 4));

        (NurbsCurve result, int removed) = minimal.Reduced(Loose);

        Assert.Equal(0, removed);
        Assert.Equal(minimal.Knots.Count, result.Knots.Count);
    }

    /// <summary>
    /// The end knots clamp the curve to its first and last control points and are not removable —
    /// asking is a programming error rather than a refusal, because no tolerance would make it
    /// sensible.
    /// </summary>
    [Fact]
    public void RemovingAnEndKnotIsRefusedAsAMistake()
    {
        NurbsCurve curve = Sample(rational: false);

        Assert.Throws<ArgumentException>(() => curve.WithKnotRemoved(curve.Domain.Min, 1, Loose));
        Assert.Throws<ArgumentException>(() => curve.WithKnotRemoved(curve.Domain.Max, 1, Loose));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => curve.WithKnotRemoved(curve.Domain.Min + (curve.Domain.Length / 2), 0, Loose));
    }

    private static NurbsCurve Sample(bool rational)
    {
        Point3d[] points = [new(0, 0, 0), new(1, 4, 1), new(4, 5, -1), new(7, 1, 2), new(9, 2, 0)];
        double[]? weights = rational ? [1.0, 2.5, 0.4, 1.8, 1.0] : null;

        return new NurbsCurve(points, KnotVector.CreateClamped(3, points.Length), weights);
    }
}
