using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The NURBS curve, checked mostly against curves this repository already trusts.
/// </summary>
/// <remarks>
/// <para>
/// <b>The load-bearing tests here are the agreement tests.</b> A spline implementation can be
/// entirely self-consistent and entirely wrong — every internal invariant satisfied by arithmetic
/// that computes the wrong curve. What cannot be faked is agreeing with <see cref="Line"/> and with
/// <see cref="Arc"/>, which were written years apart from this and are exercised by hundreds of
/// tests of their own. A degree-1 NURBS curve <i>is</i> a polyline and a rational quadratic with
/// weights <c>1, cos(θ/2), 1</c> <i>is</i> a circular arc, exactly, and those two facts are the
/// strongest available evidence that de Boor was implemented rather than approximated.
/// </para>
/// <para>
/// The derivative tests compare against central differences rather than against a formula, for the
/// same reason: an analytic derivative checked against the same analysis that produced it proves
/// only that it was copied consistently.
/// </para>
/// </remarks>
public sealed class NurbsCurveTests
{
    /// <summary>
    /// <b>A degree-1 curve through two points is a line.</b> If the span search, the basis
    /// functions and the projection are all right, this is exact; if any is wrong, it is visibly
    /// not.
    /// </summary>
    [Fact]
    public void ADegreeOneCurveThroughTwoPointsAgreesWithALine()
    {
        Point3d start = new(1, 2, 3);
        Point3d end = new(7, -4, 11);

        NurbsCurve curve = NurbsCurve.ByPoints([start, end]);
        Line line = new(start, end);

        for (int i = 0; i <= 20; i++)
        {
            double u = i / 20.0;
            Point3d onCurve = curve.PointAt(curve.Domain.Min + (curve.Domain.Length * u));
            Point3d onLine = line.PointAt(line.Domain.Min + (line.Domain.Length * u));

            Assert.True(
                onCurve.EqualsWithin(onLine),
                $"At u = {u} the curve is at {onCurve} and the line at {onLine}.");
        }
    }

    /// <summary>A degree-1 curve through many points is the polyline through them.</summary>
    [Fact]
    public void ADegreeOneCurveThroughManyPointsAgreesWithAPolyLine()
    {
        Point3d[] points = [new(0, 0, 0), new(2, 3, 0), new(5, 3, 1), new(7, 0, 1)];

        NurbsCurve curve = NurbsCurve.ByPoints(points);
        PolyLine polyline = new(points);

        // The two are parameterised differently — the polyline by segment, the curve by its
        // uniform knots — so they are compared by length and by the points they pass through.
        Assert.Equal(polyline.Length, curve.Length, 6);

        // A degree-1 clamped curve passes through control point i exactly at knot i + 1, so the
        // corners are checked there rather than by sampling — a sampled sweep would miss an
        // interior knot at 1/3 unless the step happened to divide it, which is a test that passes
        // for the wrong reason or fails for one.
        for (int i = 0; i < points.Length; i++)
        {
            Assert.True(
                curve.PointAt(curve.Knots[i + 1]).EqualsWithin(points[i]),
                $"Control point {i} is at {points[i]}, curve is at {curve.PointAt(curve.Knots[i + 1])}.");
        }
    }

    /// <summary>
    /// <b>The rational case, checked against real geometry.</b> A quadratic NURBS with control
    /// points at the corners of a 90° triangle and weights <c>1, cos(45°), 1</c> is exactly a
    /// quarter circle — not approximately. This is the test that distinguishes a working rational
    /// evaluation from a non-rational one that happens to look plausible.
    /// </summary>
    [Fact]
    public void ARationalQuadraticIsExactlyACircularArc()
    {
        const double radius = 5.0;
        double weight = Math.Cos(Math.PI / 4);

        NurbsCurve curve = new(
            2,
            [new Point3d(radius, 0, 0), new Point3d(radius, radius, 0), new Point3d(0, radius, 0)],
            [0, 0, 0, 1, 1, 1],
            [1.0, weight, 1.0]);

        Arc arc = Arc.ByPlaneRadiusAngles(
            Plane.WorldXY, radius, Angle.FromDegrees(0), Angle.FromDegrees(90));

        Assert.True(curve.IsRational);

        for (int i = 0; i <= 20; i++)
        {
            double u = i / 20.0;
            Point3d onCurve = curve.PointAt(curve.Domain.Min + (curve.Domain.Length * u));

            // Every point is at the radius — the definition of a circle, checked directly rather
            // than through the arc's own parameterisation, which differs from the curve's.
            Assert.Equal(radius, onCurve.DistanceTo(Point3d.Origin), 9);
        }

        // The ends really are the arc's ends, so the assertion above is about the right quarter.
        Assert.True(curve.PointAt(curve.Domain.Min).EqualsWithin(arc.PointAt(arc.Domain.Min)));
        Assert.True(curve.PointAt(curve.Domain.Max).EqualsWithin(arc.PointAt(arc.Domain.Max)));
    }

    /// <summary>
    /// A clamped curve starts at its first control point and ends at its last. This is the whole
    /// purpose of clamping, and it is the first thing to break when the span search is wrong at the
    /// end of the domain.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AClampedCurveInterpolatesItsEndPoints(int degree)
    {
        Point3d[] points = [.. Enumerable.Range(0, degree + 3).Select(i => new Point3d(i, i % 3, -i))];
        NurbsCurve curve = new(points, KnotVector.CreateClamped(degree, points.Length));

        Assert.True(curve.PointAt(curve.Domain.Min).EqualsWithin(points[0]));
        Assert.True(curve.PointAt(curve.Domain.Max).EqualsWithin(points[^1]));
    }

    /// <summary>
    /// Every point lies inside the convex hull of the control points — here checked on the axis
    /// bounds, which is the cheap necessary condition. A curve that leaves its control polygon is
    /// a basis-function fault, and it is the fault that looks like the modeller's mistake.
    /// </summary>
    [Fact]
    public void TheCurveStaysWithinItsControlPointBounds()
    {
        Point3d[] points = [new(0, 0, 0), new(1, 5, 0), new(4, -2, 3), new(6, 3, 1), new(9, 0, 0)];
        NurbsCurve curve = new(points, KnotVector.CreateClamped(3, points.Length));

        double minX = points.Min(p => p.X);
        double maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y);
        double maxY = points.Max(p => p.Y);

        for (int i = 0; i <= 100; i++)
        {
            Point3d p = curve.PointAt(curve.Domain.Min + (curve.Domain.Length * i / 100.0));

            Assert.InRange(p.X, minX - 1e-9, maxX + 1e-9);
            Assert.InRange(p.Y, minY - 1e-9, maxY + 1e-9);
        }
    }

    /// <summary>
    /// The tangent agrees with a central difference in direction.
    /// </summary>
    /// <remarks>
    /// Checked against numerics rather than against the formula it came from, because a derivative
    /// verified by its own analysis proves only that it was copied consistently. <c>Curve</c>
    /// exposes the tangent normalised, so this is the direction half; the magnitude half is
    /// <see cref="TheLengthAgreesWithADenselySampledPolyline(bool)"/>, which integrates the same
    /// derivative and would be wrong if its length were.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheTangentAgreesWithACentralDifference(bool rational)
    {
        NurbsCurve curve = Sample(rational);
        Interval domain = curve.Domain;
        const double h = 1e-6;

        for (int i = 2; i <= 18; i++)
        {
            double t = domain.Min + (domain.Length * i / 20.0);

            Vector3d analytic = curve.TangentAt(t);
            Vector3d numeric = ((curve.PointAt(t + h) - curve.PointAt(t - h)) / (2 * h)).Normalised();

            Assert.Equal(numeric.X, analytic.X, 5);
            Assert.Equal(numeric.Y, analytic.Y, 5);
            Assert.Equal(numeric.Z, analytic.Z, 5);
        }
    }

    /// <summary>
    /// <b>The magnitude half of the derivative check.</b> <c>Curve.Length</c> integrates the speed
    /// — the unnormalised derivative's length — with Gauss–Legendre, so a derivative that pointed
    /// the right way with the wrong magnitude would produce a length that disagrees with the curve
    /// somebody can actually measure.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLengthAgreesWithADenselySampledPolyline(bool rational)
    {
        NurbsCurve curve = Sample(rational);
        Interval domain = curve.Domain;

        const int steps = 20000;
        double chordal = 0.0;
        Point3d previous = curve.PointAt(domain.Min);

        for (int i = 1; i <= steps; i++)
        {
            Point3d next = curve.PointAt(domain.Min + (domain.Length * i / (double)steps));
            chordal += previous.DistanceTo(next);
            previous = next;
        }

        // A chord sum underestimates, and converges from below; twenty thousand steps on a curve
        // this size leaves the two within a part in a million.
        Assert.Equal(chordal, curve.Length, 5);
    }

    /// <summary>
    /// The normal — which is built on the second derivative — agrees with the numerically
    /// differentiated tangent. This is where a missing factorial in the basis-derivative table
    /// shows up, and nothing else in the curve would notice it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheNormalAgreesWithTheNumericallyDifferentiatedTangent(bool rational)
    {
        NurbsCurve curve = Sample(rational);
        Interval domain = curve.Domain;
        const double h = 1e-5;

        for (int i = 3; i <= 17; i++)
        {
            double t = domain.Min + (domain.Length * i / 20.0);

            Vector3d analytic = curve.NormalAt(t);
            Vector3d numeric = ((curve.TangentAt(t + h) - curve.TangentAt(t - h)) / (2 * h)).Normalised();

            Assert.Equal(numeric.X, analytic.X, 4);
            Assert.Equal(numeric.Y, analytic.Y, 4);
            Assert.Equal(numeric.Z, analytic.Z, 4);
        }
    }

    /// <summary>Reversing a curve gives the same shape traced the other way.</summary>
    [Fact]
    public void ReversingTracesTheSameShapeBackwards()
    {
        NurbsCurve curve = Sample(rational: true);
        Curve reversed = curve.Reversed();

        Assert.Equal(curve.Domain.Min, reversed.Domain.Min, 12);
        Assert.Equal(curve.Domain.Max, reversed.Domain.Max, 12);

        for (int i = 0; i <= 20; i++)
        {
            double u = i / 20.0;
            Point3d forward = curve.PointAt(curve.Domain.Min + (curve.Domain.Length * u));
            Point3d backward = reversed.PointAt(reversed.Domain.Min + (reversed.Domain.Length * (1 - u)));

            Assert.True(forward.EqualsWithin(backward), $"At u = {u}: {forward} vs {backward}.");
        }
    }

    /// <summary>And reversing twice is the original.</summary>
    [Fact]
    public void ReversingTwiceIsTheOriginal()
    {
        NurbsCurve curve = Sample(rational: true);
        Curve twice = curve.Reversed().Reversed();

        for (int i = 0; i <= 20; i++)
        {
            double t = curve.Domain.Min + (curve.Domain.Length * i / 20.0);
            Assert.True(curve.PointAt(t).EqualsWithin(twice.PointAt(t)));
        }
    }

    /// <summary>
    /// A transform moves the curve and leaves the weights alone. Transforming the homogeneous
    /// coordinates instead would scale the weights and change the curve's shape, which is the
    /// mistake this asserts against.
    /// </summary>
    [Fact]
    public void TransformingMovesTheCurveAndKeepsItsWeights()
    {
        NurbsCurve curve = Sample(rational: true);
        Transform move = Transform.Translation(new Vector3d(10, -3, 4));

        NurbsCurve moved = Assert.IsType<NurbsCurve>(curve.TransformedBy(move));

        Assert.Equal(curve.Weights(), moved.Weights());

        for (int i = 0; i <= 20; i++)
        {
            double t = curve.Domain.Min + (curve.Domain.Length * i / 20.0);
            Assert.True((move * curve.PointAt(t)).EqualsWithin(moved.PointAt(t)));
        }
    }

    [Fact]
    public void AWrongNumberOfControlPointsIsRefused()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new NurbsCurve(2, [new Point3d(0, 0, 0), new Point3d(1, 0, 0)], [0, 0, 0, 1, 1, 1]));

        Assert.Contains("control points", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A weight of zero or less puts a pole inside the curve: the denominator vanishes and the
    /// curve runs to infinity at a parameter that looks no different from its neighbours. Refusing
    /// at construction is the only place the failure can be attributed to its cause.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void ANonPositiveWeightIsRefused(double weight)
    {
        Assert.Throws<ArgumentException>(() => new NurbsCurve(
            2,
            [new Point3d(0, 0, 0), new Point3d(1, 1, 0), new Point3d(2, 0, 0)],
            [0, 0, 0, 1, 1, 1],
            [1.0, weight, 1.0]));
    }

    [Fact]
    public void AWeightPerControlPointIsRequired()
    {
        Assert.Throws<ArgumentException>(() => new NurbsCurve(
            2,
            [new Point3d(0, 0, 0), new Point3d(1, 1, 0), new Point3d(2, 0, 0)],
            [0, 0, 0, 1, 1, 1],
            [1.0, 1.0]));
    }

    [Fact]
    public void EqualWeightsAreNotRational()
    {
        NurbsCurve plain = Sample(rational: false);

        Assert.False(plain.IsRational);
        Assert.All(plain.Weights(), w => Assert.Equal(1.0, w));
    }

    /// <summary>
    /// <b>The test that proves knot insertion: nothing changed.</b> Insert a knot anywhere and the
    /// curve must occupy exactly the same points — the whole point of the operation is that it
    /// alters the representation and not the geometry. A blend done on the projected points instead
    /// of the homogeneous ones passes this for a non-rational curve and fails it for a rational
    /// one, which is why both are checked.
    /// </summary>
    [Theory]
    [InlineData(false, 0.31)]
    [InlineData(true, 0.31)]
    [InlineData(false, 0.5)]
    [InlineData(true, 0.5)]
    [InlineData(true, 0.87)]
    public void InsertingAKnotChangesNothingAboutTheCurve(bool rational, double at)
    {
        NurbsCurve original = Sample(rational);
        double t = original.Domain.Min + (original.Domain.Length * at);

        NurbsCurve inserted = original.WithKnotInserted(t);

        Assert.Equal(original.Knots.Count + 1, inserted.Knots.Count);
        Assert.Equal(original.ControlPoints().Length + 1, inserted.ControlPoints().Length);
        Assert.Equal(original.Degree, inserted.Degree);
        Assert.Equal(original.Domain.Min, inserted.Domain.Min, 12);
        Assert.Equal(original.Domain.Max, inserted.Domain.Max, 12);

        for (int i = 0; i <= 200; i++)
        {
            double u = original.Domain.Min + (original.Domain.Length * i / 200.0);
            Point3d before = original.PointAt(u);
            Point3d after = inserted.PointAt(u);

            Assert.Equal(before.X, after.X, 10);
            Assert.Equal(before.Y, after.Y, 10);
            Assert.Equal(before.Z, after.Z, 10);
        }
    }

    /// <summary>Inserting the same knot repeatedly still changes nothing.</summary>
    [Fact]
    public void InsertingAKnotSeveralTimesStillChangesNothing()
    {
        NurbsCurve original = Sample(rational: true);
        double t = original.Domain.Min + (original.Domain.Length * 0.4);

        NurbsCurve inserted = original.WithKnotInserted(t, 3);

        Assert.Equal(3, inserted.Knots.Multiplicity(t));
        Assert.Equal(original.ControlPoints().Length + 3, inserted.ControlPoints().Length);

        for (int i = 0; i <= 100; i++)
        {
            double u = original.Domain.Min + (original.Domain.Length * i / 100.0);
            Assert.True(original.PointAt(u).EqualsWithin(inserted.PointAt(u)));
        }
    }

    /// <summary>
    /// A rational curve keeps being the arc it was. This is the insertion test with the strongest
    /// external reference: the quarter circle is a circle before and after.
    /// </summary>
    [Fact]
    public void InsertingIntoTheArcKeepsItACircle()
    {
        const double radius = 5.0;
        double weight = Math.Cos(Math.PI / 4);

        NurbsCurve arc = new(
            2,
            [new Point3d(radius, 0, 0), new Point3d(radius, radius, 0), new Point3d(0, radius, 0)],
            [0, 0, 0, 1, 1, 1],
            [1.0, weight, 1.0]);

        NurbsCurve refined = arc.WithKnotInserted(0.5);

        for (int i = 0; i <= 40; i++)
        {
            Point3d p = refined.PointAt(i / 40.0);
            Assert.Equal(radius, p.DistanceTo(Point3d.Origin), 9);
        }
    }

    /// <summary>
    /// Insertion past full multiplicity is refused. A knot at multiplicity `degree` already splits
    /// the curve there; going further leaves a control point with no support at all.
    /// </summary>
    [Fact]
    public void InsertingPastFullMultiplicityIsRefused()
    {
        NurbsCurve curve = Sample(rational: false);
        double t = curve.Domain.Min + (curve.Domain.Length * 0.5);

        Assert.Throws<ArgumentException>(() => curve.WithKnotInserted(t, curve.Degree + 1));
    }

    [Fact]
    public void InsertingOutsideTheDomainIsRefused()
    {
        NurbsCurve curve = Sample(rational: false);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.WithKnotInserted(curve.Domain.Max + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.WithKnotInserted(curve.Domain.Min - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.WithKnotInserted(0.5, 0));
    }

    /// <summary>
    /// <b>Trimming is exact.</b> The trimmed curve occupies exactly the same points as the original
    /// did over that range — not nearly, which is what an approximation would give and what a
    /// caller could not detect.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrimmingKeepsTheOriginalShapeOverTheKeptRange(bool rational)
    {
        NurbsCurve original = Sample(rational);
        Interval whole = original.Domain;
        Interval wanted = new(
            whole.Min + (whole.Length * 0.23), whole.Min + (whole.Length * 0.71));

        Curve trimmed = original.Trimmed(wanted);

        Assert.Equal(wanted.Min, trimmed.Domain.Min, 9);
        Assert.Equal(wanted.Max, trimmed.Domain.Max, 9);

        for (int i = 0; i <= 100; i++)
        {
            double t = wanted.Min + (wanted.Length * i / 100.0);
            Point3d before = original.PointAt(t);
            Point3d after = trimmed.PointAt(t);

            Assert.Equal(before.X, after.X, 9);
            Assert.Equal(before.Y, after.Y, 9);
            Assert.Equal(before.Z, after.Z, 9);
        }
    }

    /// <summary>A trimmed curve starts and ends exactly where it was asked to.</summary>
    [Fact]
    public void ATrimmedCurveInterpolatesItsNewEnds()
    {
        NurbsCurve original = Sample(rational: true);
        Interval whole = original.Domain;
        Interval wanted = new(
            whole.Min + (whole.Length * 0.3), whole.Min + (whole.Length * 0.6));

        Curve trimmed = original.Trimmed(wanted);

        Assert.True(trimmed.PointAt(wanted.Min).EqualsWithin(original.PointAt(wanted.Min)));
        Assert.True(trimmed.PointAt(wanted.Max).EqualsWithin(original.PointAt(wanted.Max)));
    }

    /// <summary>
    /// Two abutting trims rejoin into the whole. This is `E2-T33`'s property applied to NURBS
    /// before the property suite has a generator for one.
    /// </summary>
    [Fact]
    public void TwoAbuttingTrimsCoverTheWholeCurvesLength()
    {
        NurbsCurve original = Sample(rational: true);
        Interval whole = original.Domain;
        double middle = whole.Min + (whole.Length * 0.45);

        Curve left = original.Trimmed(new Interval(whole.Min, middle));
        Curve right = original.Trimmed(new Interval(middle, whole.Max));

        Assert.Equal(original.Length, left.Length + right.Length, 6);
        Assert.True(left.PointAt(middle).EqualsWithin(right.PointAt(middle)));
    }

    [Fact]
    public void TrimmingToTheWholeDomainIsTheSameCurve()
    {
        NurbsCurve original = Sample(rational: true);
        Curve trimmed = original.Trimmed(original.Domain);

        for (int i = 0; i <= 50; i++)
        {
            double t = original.Domain.Min + (original.Domain.Length * i / 50.0);
            Assert.True(original.PointAt(t).EqualsWithin(trimmed.PointAt(t)));
        }
    }

    [Fact]
    public void TrimmingOutsideTheDomainIsRefused()
    {
        NurbsCurve curve = Sample(rational: false);
        Interval whole = curve.Domain;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => curve.Trimmed(new Interval(whole.Min - 1, whole.Max)));
        Assert.Throws<ArgumentException>(
            () => curve.Trimmed(new Interval(whole.Min, whole.Min)));
    }

    [Fact]
    public void TheControlPointsAndWeightsAreCopiesOnTheWayInAndOut()
    {
        Point3d[] points = [new(0, 0, 0), new(1, 1, 0), new(2, 0, 0)];
        double[] weights = [1.0, 2.0, 1.0];

        NurbsCurve curve = new(2, points, [0, 0, 0, 1, 1, 1], weights);

        points[1] = new Point3d(500, 500, 500);
        weights[1] = 99.0;
        curve.ControlPoints()[0] = new Point3d(-1, -1, -1);

        Assert.Equal(new Point3d(1, 1, 0), curve.ControlPoints()[1]);
        Assert.Equal(2.0, curve.Weights()[1]);
        Assert.Equal(new Point3d(0, 0, 0), curve.ControlPoints()[0]);
    }

    [Fact]
    public void ACurveWhoseEndsMeetIsClosed()
    {
        Point3d[] open = [new(0, 0, 0), new(1, 2, 0), new(3, 2, 0), new(4, 0, 0)];
        Point3d[] shut = [new(0, 0, 0), new(1, 2, 0), new(3, 2, 0), new(0, 0, 0)];

        Assert.False(new NurbsCurve(open, KnotVector.CreateClamped(2, 4)).IsClosed);
        Assert.True(new NurbsCurve(shut, KnotVector.CreateClamped(2, 4)).IsClosed);
    }

    /// <summary>
    /// <b>Degree elevation changes nothing about the curve</b>, which is the only thing it is for.
    /// The same assertion knot insertion is proved by, and just as decisive: the representation
    /// changes and the geometry does not.
    /// </summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public void ElevatingTheDegreeChangesNothingAboutTheCurve(bool rational, int by)
    {
        NurbsCurve original = Sample(rational);
        NurbsCurve raised = original.WithDegreeElevated(by);

        Assert.Equal(original.Degree + by, raised.Degree);
        Assert.Equal(original.Domain.Min, raised.Domain.Min, 12);
        Assert.Equal(original.Domain.Max, raised.Domain.Max, 12);

        for (int i = 0; i <= 200; i++)
        {
            double u = original.Domain.Min + (original.Domain.Length * i / 200.0);
            Point3d before = original.PointAt(u);
            Point3d after = raised.PointAt(u);

            Assert.Equal(before.X, after.X, 10);
            Assert.Equal(before.Y, after.Y, 10);
            Assert.Equal(before.Z, after.Z, 10);
        }
    }

    /// <summary>
    /// A line stays a line. Elevating a degree-1 curve to degree 3 and comparing against
    /// <see cref="Line"/> checks the blend against geometry rather than against itself.
    /// </summary>
    [Fact]
    public void ElevatingALineKeepsItStraight()
    {
        Point3d start = new(1, 2, 3);
        Point3d end = new(7, -4, 11);

        NurbsCurve raised = NurbsCurve.ByPoints([start, end]).WithDegreeElevated(2);
        Line line = new(start, end);

        Assert.Equal(3, raised.Degree);

        for (int i = 0; i <= 20; i++)
        {
            double u = i / 20.0;
            Point3d onCurve = raised.PointAt(raised.Domain.Min + (raised.Domain.Length * u));
            Point3d onLine = line.PointAt(line.Domain.Min + (line.Domain.Length * u));

            Assert.True(onCurve.EqualsWithin(onLine), $"At u = {u}: {onCurve} vs {onLine}.");
        }
    }

    /// <summary>
    /// The rational quarter circle stays a circle after elevation — the strongest external check
    /// available, because a blend done on the projected points instead of the homogeneous ones
    /// produces something that is nearly circular and is not.
    /// </summary>
    [Fact]
    public void ElevatingTheArcKeepsItACircle()
    {
        const double radius = 5.0;
        double weight = Math.Cos(Math.PI / 4);

        NurbsCurve arc = new(
            2,
            [new Point3d(radius, 0, 0), new Point3d(radius, radius, 0), new Point3d(0, radius, 0)],
            [0, 0, 0, 1, 1, 1],
            [1.0, weight, 1.0]);

        NurbsCurve raised = arc.WithDegreeElevated();

        Assert.Equal(3, raised.Degree);

        for (int i = 0; i <= 40; i++)
        {
            Point3d p = raised.PointAt(i / 40.0);
            Assert.Equal(radius, p.DistanceTo(Point3d.Origin), 9);
        }
    }

    /// <summary>
    /// A clamped curve stays clamped, so it still passes through its first and last control
    /// points — the property everything downstream assumes.
    /// </summary>
    [Fact]
    public void AnElevatedCurveIsStillClampedToItsEnds()
    {
        NurbsCurve original = Sample(rational: true);
        NurbsCurve raised = original.WithDegreeElevated();

        Assert.True(raised.Knots.IsClamped);
        Assert.True(raised.ControlPoints()[0].EqualsWithin(original.ControlPoints()[0]));
        Assert.True(raised.ControlPoints()[^1].EqualsWithin(original.ControlPoints()[^1]));
    }

    /// <summary>
    /// Elevation and trimming commute: elevate then trim, or trim then elevate, and the same
    /// piece of curve comes out. Two operations that each claim to preserve shape had better
    /// agree with each other.
    /// </summary>
    [Fact]
    public void ElevatingAndTrimmingCommute()
    {
        NurbsCurve original = Sample(rational: true);
        Interval whole = original.Domain;
        Interval wanted = new(
            whole.Min + (whole.Length * 0.2), whole.Min + (whole.Length * 0.75));

        Curve elevatedThenTrimmed = original.WithDegreeElevated().Trimmed(wanted);
        Curve trimmedThenElevated =
            ((NurbsCurve)original.Trimmed(wanted)).WithDegreeElevated();

        for (int i = 0; i <= 100; i++)
        {
            double t = wanted.Min + (wanted.Length * i / 100.0);
            Point3d a = elevatedThenTrimmed.PointAt(t);
            Point3d b = trimmedThenElevated.PointAt(t);

            Assert.Equal(a.X, b.X, 9);
            Assert.Equal(a.Y, b.Y, 9);
            Assert.Equal(a.Z, b.Z, 9);
        }
    }

    [Fact]
    public void ElevatingByLessThanOneIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Sample(rational: false).WithDegreeElevated(0));
    }

    /// <summary>A degree-3 curve, rational or not, with an interior knot so it has two spans.</summary>
    private static NurbsCurve Sample(bool rational)
    {
        Point3d[] points = [new(0, 0, 0), new(1, 4, 1), new(4, 5, -1), new(7, 1, 2), new(9, 2, 0)];
        double[]? weights = rational ? [1.0, 2.5, 0.4, 1.8, 1.0] : null;

        return new NurbsCurve(points, KnotVector.CreateClamped(3, points.Length), weights);
    }
}
