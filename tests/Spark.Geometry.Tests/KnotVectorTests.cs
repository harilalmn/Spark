using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The knot vector: its invariants, its domain, span lookup and the basis functions.
/// </summary>
/// <remarks>
/// This is where NURBS goes wrong, which is why it is a type and a test file of its own before
/// any curve exists. The three faults that account for most spline bugs are all here: a vector
/// whose length disagrees with the control-point count, a multiplicity one too high, and a span
/// index that walks off the end of the array at the last parameter of the domain.
/// </remarks>
public sealed class KnotVectorTests
{
    /// <summary>The textbook degree-2 example: knots 0,0,0,1,2,3,4,4,5,5,5.</summary>
    private static KnotVector Textbook =>
        new(2, [0, 0, 0, 1, 2, 3, 4, 4, 5, 5, 5]);

    [Fact]
    public void TheDefiningRelationHolds()
    {
        KnotVector knots = Textbook;

        // knots = controlPoints + degree + 1. Getting this wrong is one of the two classic NURBS
        // faults, so the arithmetic exists once and is asserted once.
        Assert.Equal(knots.Count - knots.Degree - 1, knots.ControlPointCount);
        Assert.Equal(8, knots.ControlPointCount);
    }

    /// <summary>
    /// <b>The domain is not the first and last knots.</b> For a clamped vector those repeat, and
    /// using them would put the domain's ends where the basis functions are not yet a partition of
    /// unity.
    /// </summary>
    [Fact]
    public void TheDomainRunsBetweenTheInteriorEndKnots()
    {
        KnotVector clamped = KnotVector.CreateClamped(3, 6);

        Assert.Equal(0.0, clamped.Domain.Min);
        Assert.Equal(1.0, clamped.Domain.Max);

        // And the vector really does repeat its ends, so the assertion above is not vacuous.
        Assert.Equal(0.0, clamped[0]);
        Assert.Equal(0.0, clamped[clamped.Degree]);
        Assert.Equal(1.0, clamped[^1]);
    }

    [Fact]
    public void AClampedVectorKnowsItIsClamped()
    {
        Assert.True(KnotVector.CreateClamped(3, 6).IsClamped);
        Assert.True(Textbook.IsClamped);

        // Uniform and unclamped: every knot distinct, so neither end repeats.
        Assert.False(new KnotVector(2, [0, 1, 2, 3, 4, 5, 6, 7]).IsClamped);
    }

    [Fact]
    public void AClampedVectorSpacesItsInteriorKnotsEvenly()
    {
        KnotVector knots = KnotVector.CreateClamped(2, 6);

        // Degree 2, 6 control points: 9 knots, 3 interior.
        Assert.Equal(9, knots.Count);
        Assert.Equal([0, 0, 0, 0.25, 0.5, 0.75, 1, 1, 1], knots.ToArray());
    }

    /// <summary>
    /// <b>The span lookup at the end of the domain.</b> At the last parameter the half-open rule
    /// finds no span — there is no knot greater than it — and the naive answer indexes one past the
    /// last control point. This is the single most common off-by-one in a spline kernel.
    /// </summary>
    [Fact]
    public void TheLastParameterFindsTheLastNonEmptySpan()
    {
        KnotVector knots = KnotVector.CreateClamped(3, 6);

        int span = knots.FindSpan(knots.Domain.Max);

        // The span index must address a real control point: span - degree .. span must all be
        // valid indices into the control points.
        Assert.InRange(span, knots.Degree, knots.ControlPointCount - 1);

        // And it must be a span with width, not one of the repeated end knots.
        Assert.True(knots[span] < knots[span + 1], "The chosen span must not be empty.");
    }

    [Fact]
    public void SpansAreFoundCorrectlyAcrossTheDomain()
    {
        KnotVector knots = Textbook;

        // Half-open: a parameter exactly on a knot belongs to the span that knot starts.
        Assert.Equal(2, knots.FindSpan(0.0));
        Assert.Equal(2, knots.FindSpan(0.5));
        Assert.Equal(3, knots.FindSpan(1.0));
        Assert.Equal(3, knots.FindSpan(1.5));
        Assert.Equal(4, knots.FindSpan(2.5));
        Assert.Equal(7, knots.FindSpan(4.5));
    }

    [Fact]
    public void AParameterOutsideTheDomainIsClampedRatherThanRejected()
    {
        KnotVector knots = KnotVector.CreateClamped(2, 5);

        Assert.Equal(knots.FindSpan(knots.Domain.Min), knots.FindSpan(-100.0));
        Assert.Equal(knots.FindSpan(knots.Domain.Max), knots.FindSpan(100.0));
    }

    /// <summary>
    /// The defining property of the B-spline basis: at every parameter, the non-zero basis
    /// functions sum to exactly one. A basis that does not is a curve that drifts off its control
    /// polygon, which looks like a modelling mistake rather than a kernel one.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void TheBasisFunctionsArePartitionOfUnityEverywhere(int degree)
    {
        KnotVector knots = KnotVector.CreateClamped(degree, degree + 4);
        Interval domain = knots.Domain;

        for (int step = 0; step <= 40; step++)
        {
            double t = domain.Min + ((domain.Max - domain.Min) * step / 40.0);
            double[] basis = knots.BasisFunctions(knots.FindSpan(t), t);

            Assert.Equal(degree + 1, basis.Length);
            Assert.Equal(1.0, basis.Sum(), 12);
        }
    }

    /// <summary>
    /// Every basis function is non-negative. A negative one is not merely wrong, it breaks the
    /// convex-hull property that a great deal of downstream geometry relies on.
    /// </summary>
    [Fact]
    public void TheBasisFunctionsAreNeverNegative()
    {
        KnotVector knots = Textbook;
        Interval domain = knots.Domain;

        for (int step = 0; step <= 50; step++)
        {
            double t = domain.Min + ((domain.Max - domain.Min) * step / 50.0);

            Assert.All(
                knots.BasisFunctions(knots.FindSpan(t), t),
                value => Assert.True(value >= 0.0, $"Basis value {value} at t = {t} is negative."));
        }
    }

    /// <summary>
    /// At a clamped end the first control point takes the whole weight, which is what makes a
    /// clamped curve start exactly at it.
    /// </summary>
    [Fact]
    public void AClampedEndGivesAllTheWeightToTheEndControlPoint()
    {
        KnotVector knots = KnotVector.CreateClamped(3, 6);

        double[] atStart = knots.BasisFunctions(knots.FindSpan(0.0), 0.0);
        Assert.Equal(1.0, atStart[0], 12);
        Assert.All(atStart[1..], value => Assert.Equal(0.0, value, 12));

        double[] atEnd = knots.BasisFunctions(knots.FindSpan(1.0), 1.0);
        Assert.Equal(1.0, atEnd[^1], 12);
        Assert.All(atEnd[..^1], value => Assert.Equal(0.0, value, 12));
    }

    [Fact]
    public void DecreasingKnotsAreRefused()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new KnotVector(2, [0, 0, 0, 1, 0.5, 2, 2, 2]));

        Assert.Contains("decrease", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonFiniteKnotsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => new KnotVector(2, [0, 0, 0, double.NaN, 1, 1, 1]));
        Assert.Throws<ArgumentException>(
            () => new KnotVector(2, [0, 0, 0, double.PositiveInfinity, 1, 1, 1]));
    }

    /// <summary>
    /// An interior knot repeated more than <c>degree</c> times splits the curve in two while
    /// pretending to be one curve; more than <c>degree + 1</c> anywhere leaves a control point with
    /// no support. Both evaluate to nonsense rather than throwing, so they are refused at the door.
    /// </summary>
    [Fact]
    public void ExcessiveMultiplicityIsRefused()
    {
        // Degree 2, interior knot 1 repeated three times.
        Assert.Throws<ArgumentException>(() => new KnotVector(2, [0, 0, 0, 1, 1, 1, 2, 2, 2]));

        // Degree 2, end knot repeated four times.
        Assert.Throws<ArgumentException>(() => new KnotVector(2, [0, 0, 0, 0, 1, 2, 2, 2]));
    }

    /// <summary>
    /// An interior knot repeated exactly <c>degree</c> times is legal and is how a spline is given
    /// a corner — so the multiplicity check must not be one stricter than the rule.
    /// </summary>
    [Fact]
    public void AnInteriorKnotRepeatedDegreeTimesIsAllowed()
    {
        KnotVector kinked = new(2, [0, 0, 0, 1, 1, 2, 2, 2]);

        Assert.Equal(2, kinked.Multiplicity(1.0));
        Assert.Equal(5, kinked.ControlPointCount);
    }

    [Fact]
    public void TooFewKnotsForTheDegreeIsRefused()
    {
        // A degree-3 curve needs at least 8 knots; this is 7.
        Assert.Throws<ArgumentException>(() => new KnotVector(3, [0, 0, 0, 0, 1, 1, 1]));
    }

    [Fact]
    public void AnEmptyDomainIsRefused()
    {
        // Every knot the same: the interior end knots coincide, so no parameter is inside.
        Assert.Throws<ArgumentException>(() => new KnotVector(2, [0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void ADegreeBelowOneIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KnotVector(0, [0, 0, 1, 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => KnotVector.CreateClamped(0, 4));
    }

    [Fact]
    public void FewerControlPointsThanTheDegreeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => KnotVector.CreateClamped(3, 3));
    }

    /// <summary>
    /// The array is copied in, so a caller who reuses their buffer cannot retroactively invalidate
    /// a vector that has already been checked.
    /// </summary>
    [Fact]
    public void TheKnotsAreCopiedRatherThanAdopted()
    {
        double[] source = [0, 0, 0, 1, 2, 2, 2];
        KnotVector knots = new(2, source);

        source[3] = -50.0;

        Assert.Equal(1.0, knots[3]);
    }

    /// <summary>And handing the array back out cannot alter it either.</summary>
    [Fact]
    public void TheReturnedArrayIsACopy()
    {
        KnotVector knots = Textbook;

        knots.ToArray()[0] = -1.0;

        Assert.Equal(0.0, knots[0]);
    }

    /// <summary>
    /// Equality is exact, because a knot vector is data. A tolerant equality would make
    /// <c>a == b</c> and <c>b == c</c> fail to imply <c>a == c</c>, which is not a defensible thing
    /// for <c>Equals</c> to do — the tolerant question belongs to <c>Multiplicity</c>.
    /// </summary>
    [Fact]
    public void EqualityIsExactAndStructural()
    {
        KnotVector a = new(2, [0, 0, 0, 1, 2, 2, 2]);
        KnotVector b = new(2, [0, 0, 0, 1, 2, 2, 2]);
        KnotVector nearly = new(2, [0, 0, 0, 1 + 1e-15, 2, 2, 2]);
        KnotVector otherDegree = new(1, [0, 0, 1, 2, 2]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, nearly);
        Assert.NotEqual(a, otherDegree);
        Assert.False(a.Equals(null));
    }

    /// <summary>
    /// Multiplicity is measured with a tolerance, never with <c>==</c>. A clamped vector produced
    /// by arithmetic rarely has bitwise-equal end knots, and a stricter check would report 1 where
    /// the answer is <c>degree + 1</c>.
    /// </summary>
    [Fact]
    public void MultiplicityToleratesArithmeticDrift()
    {
        double nearlyOne = 1.0 - 1e-12;
        KnotVector knots = new(2, [0, 0, 0, 0.5, nearlyOne, 1.0, 1.0]);

        Assert.Equal(3, knots.Multiplicity(1.0));
    }
}
