using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Surface.ToNurbsSurface"/> on the base — `E2-T66`'s second item — and the three
/// conversions that did not exist before it: extrusion, revolution and ruled.
/// </summary>
/// <remarks>
/// <para>
/// <b>Agreement is as a set of points unless the result says the parameterisation is kept</b>, in
/// which case it is point against point at equal parameters. The two claims are different and the
/// result type carries both, so the tests pin both: a plane and an extruded line keep their
/// parameters, a sphere and an extruded arc do not, and the ruled surface between two arcs is not
/// even exact, because its rulings would join the wrong pairs of points.
/// </para>
/// <para>
/// <b>The branch is the knot merge.</b> A ruled surface between rails of different degree and
/// different knots is right along both rails whatever the merge does; it is wrong <i>between</i> the
/// knots when the merge is skipped or half done, so the assertion samples there.
/// </para>
/// </remarks>
public sealed class SurfaceToNurbsTests
{
    private const double Tight = 1e-9;

    /// <summary>
    /// <b>The guard.</b> The base member refuses, so every concrete surface type has to answer for
    /// itself — and a tenth surface type that forgets fails here rather than at a caller.
    /// </summary>
    [Fact]
    public void EveryConcreteSurfaceTypeSaysWhetherItConverts()
    {
        Type[] concrete = typeof(Surface).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(Surface)) && !type.IsAbstract)
            .ToArray();

        Assert.True(concrete.Length >= 9, $"expected the nine surface types and found {concrete.Length}.");

        foreach (Type type in concrete)
        {
            MethodInfo? member = type.GetMethod(
                nameof(Surface.ToNurbsSurface), [typeof(Tolerance).MakeByRefType()]);

            Assert.True(
                member is not null && member.DeclaringType == type,
                $"{type.Name} inherits the base ToNurbsSurface, which refuses. Every concrete surface has to override it.");
        }
    }

    /// <summary>
    /// Every surface type reaches the member through the base, and the exact ones are the same
    /// sheet with the same corners. The ruled surface in the list runs between two circles and is
    /// the one that is not exact, on purpose: see the ruled tests below.
    /// </summary>
    [Theory]
    [MemberData(nameof(AnalyticSurfaceTests.EverySurface), MemberType = typeof(AnalyticSurfaceTests))]
    public void TheConvertedSheetIsTheOriginalSheet(Surface surface)
    {
        NurbsSurfaceConversion conversion = surface.ToNurbsSurface();

        Assert.Equal(surface is not RuledSurface, conversion.IsExact);

        if (!conversion.IsExact)
        {
            return;
        }

        AssertSameCorners(surface, conversion.Surface);
        AssertSameSheet(surface, conversion.Surface);
    }

    [Fact]
    public void APlaneKeepsItsParameterisation()
    {
        Surface plane = new PlaneSurface(Plane.WorldXY, new Interval(1, 3), new Interval(-2, 5));

        NurbsSurfaceConversion conversion = plane.ToNurbsSurface();

        Assert.True(conversion.IsExact);
        Assert.True(conversion.PreservesParameterisation);
        AssertSameParameters(plane, conversion.Surface);
    }

    [Fact]
    public void ASphereIsTheSameSheetAtDifferentParameters()
    {
        Surface sphere = new SphericalSurface(Plane.WorldXY, 3.0);

        NurbsSurfaceConversion conversion = sphere.ToNurbsSurface();

        Assert.True(conversion.IsExact);
        Assert.False(conversion.PreservesParameterisation);
    }

    [Fact]
    public void AnExtrudedLineKeepsItsParameterisation()
    {
        Line profile = new(new Point3d(1.0, 2.0, 0.0), new Point3d(4.0, 3.0, 1.0));
        ExtrusionSurface extrusion = new(profile, new Vector3d(0.5, 0.0, 2.0), new Interval(1.0, 3.0));

        NurbsSurfaceConversion conversion = extrusion.ToNurbsSurface();

        Assert.True(conversion.IsExact);
        Assert.True(conversion.PreservesParameterisation);
        Assert.Equal(1, conversion.Surface.KnotsU.Degree);
        Assert.Equal(1, conversion.Surface.KnotsV.Degree);
        Assert.Equal(extrusion.DomainV, conversion.Surface.DomainV);
        AssertSameParameters(extrusion, conversion.Surface);
    }

    [Fact]
    public void AnExtrudedArcIsTheSameSheetAtDifferentParameters()
    {
        Arc profile = Arc.FromThreePoints(new Point3d(2, 0, 0), new Point3d(0, 2, 0), new Point3d(-2, 0, 0));
        ExtrusionSurface extrusion = new(profile, Vector3d.ZAxis, new Interval(0.0, 5.0));

        NurbsSurfaceConversion conversion = extrusion.ToNurbsSurface();

        Assert.True(conversion.IsExact);
        Assert.False(conversion.PreservesParameterisation);
        Assert.Equal(2, conversion.Surface.KnotsU.Degree);
        Assert.True(conversion.Surface.IsRational);
        AssertSameCorners(extrusion, conversion.Surface);
        AssertSameSheet(extrusion, conversion.Surface);

        // The flag means what it says: a tenth of the way along the domain is not a tenth of the
        // way along the arc, so the two surfaces disagree there even though they are one sheet. Not a
        // quarter, because a half turn converts as two spans and a quarter is a span's midpoint, where
        // the rational parameterisation and the angle happen to agree.
        double u = extrusion.DomainU.Denormalise(0.1);
        Assert.True(extrusion.PointAt(u, 1.0).DistanceTo(conversion.Surface.PointAt(u, 1.0)) > 1e-3);
    }

    [Fact]
    public void AnExtrudedHelixIsAnApproximationAndSaysSo()
    {
        Helix profile = Helix.FromAxis(Point3d.Origin, Vector3d.ZAxis, new Point3d(1.5, 0, 0), 0.5, Angle.FromDegrees(540.0));
        ExtrusionSurface extrusion = new(profile, Vector3d.XAxis, new Interval(0.0, 2.0));

        NurbsSurfaceConversion conversion = extrusion.ToNurbsSurface(new Tolerance(1e-4, Angle.FromDegrees(0.001), 1e-12));
        NurbsSurfaceConversion coarse = extrusion.ToNurbsSurface(new Tolerance(1e-1, Angle.FromDegrees(1.0), 1e-12));

        Assert.False(conversion.IsExact);
        Assert.False(conversion.PreservesParameterisation);

        // Measured against the PROFILE rather than through the surface's closest-point search. The
        // sweep keeps v exactly - both surfaces are C(u) + direction·v - so subtracting the sweep
        // from a sample leaves a point that should be on the helix, and Curve.DistanceTo answers
        // that honestly. Going through Surface.ClosestPoint instead measures the seed grid's luck
        // on a surface that turns through 540°, and reports 0.038 however fine the conversion is.
        double worst = WorstProfileDistance(extrusion, conversion.Surface, profile);
        double worstCoarse = WorstProfileDistance(extrusion, coarse.Surface, profile);

        Assert.True(worst < 1e-3, $"the approximation should still be close, and is off by {worst}.");
        Assert.True(worst < worstCoarse, $"tightening the tolerance should tighten the surface: {worstCoarse} then {worst}.");
    }

    /// <summary>
    /// A semicircle about the axis, revolved, is a sphere — which is a check with an answer that
    /// does not come from the code under test: every point of the converted surface is at the
    /// radius from the centre.
    /// </summary>
    [Fact]
    public void ARevolvedSemicircleIsASphere()
    {
        const double radius = 2.0;
        Arc meridian = Arc.FromThreePoints(new Point3d(0, 0, -radius), new Point3d(radius, 0, 0), new Point3d(0, 0, radius));
        RevolutionSurface revolution = new(meridian, Point3d.Origin, Vector3d.ZAxis);

        NurbsSurfaceConversion conversion = revolution.ToNurbsSurface();

        Assert.True(conversion.IsExact);
        Assert.False(conversion.PreservesParameterisation);
        Assert.True(conversion.Surface.IsRational);

        for (int i = 0; i <= 12; i++)
        {
            for (int j = 0; j <= 12; j++)
            {
                double u = conversion.Surface.DomainU.Denormalise(i / 12.0);
                double v = conversion.Surface.DomainV.Denormalise(j / 12.0);

                Assert.Equal(radius, conversion.Surface.PointAt(u, v).DistanceTo(Point3d.Origin), Tight);
            }
        }
    }

    [Fact]
    public void APartialRevolutionKeepsItsExtentAndItsSheet()
    {
        Line profile = new(new Point3d(2.0, 0.0, 0.0), new Point3d(3.0, 0.0, 5.0));
        RevolutionSurface revolution = new(profile, new Point3d(0, 0, 1), Vector3d.ZAxis, new Interval(0.3, 2.0));

        NurbsSurfaceConversion conversion = revolution.ToNurbsSurface();

        Assert.True(conversion.IsExact);
        Assert.Equal(revolution.DomainU, conversion.Surface.DomainU);
        Assert.Equal(revolution.DomainV, conversion.Surface.DomainV);
        AssertSameCorners(revolution, conversion.Surface);
        AssertSameSheet(revolution, conversion.Surface);
    }

    [Fact]
    public void ARevolvedHelixIsAnApproximationAndSaysSo()
    {
        Helix profile = Helix.FromAxis(new Point3d(5, 0, 0), Vector3d.XAxis, new Point3d(5, 1, 0), 0.4, Angle.FromDegrees(360.0));
        RevolutionSurface revolution = new(profile, Point3d.Origin, Vector3d.ZAxis, new Interval(0.0, 1.0));

        NurbsSurfaceConversion conversion = revolution.ToNurbsSurface();

        Assert.False(conversion.IsExact);
    }

    [Fact]
    public void ARuledSurfaceBetweenTwoLinesKeepsItsParameterisation()
    {
        RuledSurface ruled = new(
            new Line(new Point3d(0, 0, 0), new Point3d(4, 0, 0)),
            new Line(new Point3d(0, 3, 1), new Point3d(5, 3, 2)));

        NurbsSurfaceConversion conversion = ruled.ToNurbsSurface();

        Assert.True(conversion.IsExact);
        Assert.True(conversion.PreservesParameterisation);
        AssertSameParameters(ruled, conversion.Surface);
    }

    /// <summary>
    /// <b>The branch.</b> Rails of different degree and different knots: the first is a quadratic
    /// with a knot at 0.5, the second a cubic with knots at 0.3 and 0.7. Brought together they are
    /// still the same rails, and the surface between them agrees with the original <i>between</i>
    /// the knots, which is where a skipped or half-done merge shows.
    /// </summary>
    [Fact]
    public void RailsOfDifferentDegreeAndKnotsAreBroughtTogetherExactly()
    {
        NurbsCurve first = new(
            [new Point3d(0, 0, 0), new Point3d(1, 2, 0), new Point3d(3, 2, 1), new Point3d(4, 0, 0)],
            new KnotVector(2, [0, 0, 0, 0.5, 1, 1, 1]));
        NurbsCurve second = new(
            [new Point3d(0, 5, 2), new Point3d(1, 6, 3), new Point3d(2, 4, 2), new Point3d(3, 7, 3), new Point3d(4, 5, 2), new Point3d(5, 6, 1)],
            new KnotVector(3, [0, 0, 0, 0, 0.3, 0.7, 1, 1, 1, 1]));
        RuledSurface ruled = new(first, second);

        NurbsSurfaceConversion conversion = ruled.ToNurbsSurface();

        Assert.True(conversion.IsExact);
        Assert.True(conversion.PreservesParameterisation);
        Assert.Equal(3, conversion.Surface.KnotsU.Degree);
        Assert.True(conversion.Surface.KnotsU.Multiplicity(0.3) >= 1);
        Assert.True(conversion.Surface.KnotsU.Multiplicity(0.5) >= 1);
        Assert.True(conversion.Surface.KnotsU.Multiplicity(0.7) >= 1);

        foreach (double u in new[] { 0.1, 0.4, 0.55, 0.65, 0.9 })
        {
            foreach (double v in new[] { 0.0, 0.25, 0.5, 1.0 })
            {
                Point3d expected = ruled.PointAt(u, v);
                Point3d actual = conversion.Surface.PointAt(u, v);

                Assert.True(expected.DistanceTo(actual) < Tight, $"at ({u}, {v}): {expected} against {actual}.");
            }
        }
    }

    /// <summary>
    /// <b>The merge matches knots within a tolerance, and this is the case where that matters.</b>
    /// Two rails over different domains whose interior knots are at the <i>same</i> fraction along —
    /// one third — reach <c>[0, 1]</c> as 0.3333333333333333 and 0.33333333333333337, one unit in the
    /// last place apart. Matched exactly they are two knots, and the merge manufactures a
    /// <b>zero-length span</b> in both rails: a span whose basis functions have no support, which is a
    /// degeneracy every later evaluation has to survive. Matched within a tolerance they are one knot
    /// and the merged vector has the single interior knot it should.
    /// </summary>
    [Fact]
    public void KnotsAtTheSameFractionOfDifferentDomainsAreOneKnotAndNotTwo()
    {
        NurbsCurve first = new(
            [new Point3d(0, 0, 0), new Point3d(1, 2, 0), new Point3d(3, 2, 1), new Point3d(4, 0, 0)],
            new KnotVector(2, [0, 0, 0, 1.0, 3.0, 3.0, 3.0]));
        NurbsCurve second = new(
            [new Point3d(0, 5, 2), new Point3d(1, 6, 3), new Point3d(3, 4, 3), new Point3d(4, 5, 2)],
            new KnotVector(2, [0, 0, 0, 7.0 / 3.0, 7.0, 7.0, 7.0]));

        NurbsSurfaceConversion conversion = new RuledSurface(first, second).ToNurbsSurface();

        Assert.True(conversion.IsExact);

        // Degree 2 over [0, 1] with one interior knot: 4 control points, 7 knots. A second knot a
        // hair away from the first would make it 5 and 8.
        Assert.Equal(4, conversion.Surface.KnotsU.ControlPointCount);
        Assert.Equal(1, conversion.Surface.KnotsU.Multiplicity(1.0 / 3.0));

        for (int i = 0; i <= 20; i++)
        {
            double u = i / 20.0;
            Assert.True(
                new RuledSurface(first, second).PointAt(u, 0.5).DistanceTo(conversion.Surface.PointAt(u, 0.5)) < Tight,
                $"at u = {u} the merged surface left the original.");
        }
    }

    /// <summary>
    /// A rational rail: every ruling is still the straight segment between the two rails, but the
    /// segment is walked projectively, so the parameter across is not kept and the result says so.
    /// </summary>
    [Fact]
    public void RationalRailsGiveTheSameRulingsButNotTheSameParameterAcross()
    {
        Line first = new(new Point3d(0, 0, 0), new Point3d(4, 0, 0));
        NurbsCurve second = new(
            [new Point3d(0, 3, 0), new Point3d(1, 5, 1), new Point3d(3, 5, 1), new Point3d(4, 3, 0)],
            new KnotVector(2, [0, 0, 0, 0.5, 1, 1, 1]),
            [1.0, 0.5, 2.0, 1.0]);
        RuledSurface ruled = new(first, second);

        NurbsSurfaceConversion conversion = ruled.ToNurbsSurface();

        Assert.True(conversion.IsExact);
        Assert.False(conversion.PreservesParameterisation);

        bool somewhereDifferent = false;
        foreach (double u in new[] { 0.1, 0.35, 0.6, 0.85 })
        {
            Point3d start = ruled.PointAt(u, 0.0);
            Point3d end = ruled.PointAt(u, 1.0);
            Point3d across = conversion.Surface.PointAt(u, 0.5);

            // On the segment: the distance to the segment's line is zero and it lies between the ends.
            Line ruling = new(start, end);
            Assert.True(ruling.DistanceTo(across) < Tight, $"at u = {u} the converted point leaves the ruling.");

            somewhereDifferent |= across.DistanceTo(ruled.PointAt(u, 0.5)) > 1e-3;
        }

        Assert.True(somewhereDifferent, "with unequal weights the midpoint parameter should not be the midpoint.");
    }

    [Fact]
    public void ARuledSurfaceBetweenALineAndACircleIsNotExactAndSaysSo()
    {
        Line first = new(new Point3d(-2, 0, 5), new Point3d(2, 0, 5));
        Circle second = Circle.FromCenterRadius(Point3d.Origin, 2.0);
        RuledSurface ruled = new(first, second);

        NurbsSurfaceConversion conversion = ruled.ToNurbsSurface();

        Assert.False(conversion.IsExact);
        Assert.False(conversion.PreservesParameterisation);

        // The rails themselves are still exactly the rails: the sheet between them is what differs.
        for (int i = 0; i <= 10; i++)
        {
            double u = i / 10.0;
            Assert.Equal(2.0, conversion.Surface.PointAt(u, 1.0).DistanceTo(Point3d.Origin), Tight);
            Assert.True(first.DistanceTo(conversion.Surface.PointAt(u, 0.0)) < Tight);
        }
    }

    [Fact]
    public void ANurbsSurfaceIsItsOwnConversion()
    {
        Surface surface = NurbsSurface.FromCorners(
            [new Point3d(0, 0, 0), new Point3d(0, 1, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 1)]);

        NurbsSurfaceConversion conversion = surface.ToNurbsSurface();

        Assert.Same(surface, conversion.Surface);
        Assert.True(conversion.IsExact);
        Assert.True(conversion.PreservesParameterisation);
    }

    [Fact]
    public void TheResultDescribesItself()
    {
        Surface sphere = new SphericalSurface(Plane.WorldXY, 1.0);
        Surface plane = new PlaneSurface(Plane.WorldXY, Interval.Unit, Interval.Unit);

        Assert.Contains("exact", sphere.ToNurbsSurface().ToString());
        Assert.Contains("same parameters", plane.ToNurbsSurface().ToString());
        Assert.Throws<ArgumentException>(() => new NurbsSurfaceConversion(plane.ToNurbsSurface().Surface, false, true));
    }

    /// <summary>
    /// How far the swept profile strays from the curve it was made from, measured on the curve
    /// rather than on the surface: an extrusion keeps <c>v</c> exactly, so a sample with the sweep
    /// subtracted is a point that ought to lie on the profile.
    /// </summary>
    private static double WorstProfileDistance(ExtrusionSurface original, Surface converted, Curve profile)
    {
        double worst = 0.0;

        for (int i = 0; i <= 40; i++)
        {
            for (int j = 0; j <= 2; j++)
            {
                double u = converted.DomainU.Denormalise(i / 40.0);
                double v = converted.DomainV.Denormalise(j / 2.0);
                Point3d swept = converted.PointAt(u, v) - (original.Direction * v);

                worst = Math.Max(worst, profile.DistanceTo(swept));
            }
        }

        return worst;
    }

    private static void AssertSameParameters(Surface original, Surface converted)
    {
        Assert.Equal(original.DomainU, converted.DomainU);
        Assert.Equal(original.DomainV, converted.DomainV);

        for (int i = 0; i <= 8; i++)
        {
            for (int j = 0; j <= 8; j++)
            {
                double u = original.DomainU.Denormalise(i / 8.0);
                double v = original.DomainV.Denormalise(j / 8.0);

                Assert.True(
                    original.PointAt(u, v).DistanceTo(converted.PointAt(u, v)) < Tight,
                    $"at ({u}, {v}) the two surfaces are at different points.");
            }
        }
    }

    private static void AssertSameCorners(Surface original, Surface converted)
    {
        Assert.Equal(original.DomainU, converted.DomainU);
        Assert.Equal(original.DomainV, converted.DomainV);

        foreach (double u in new[] { original.DomainU.Min, original.DomainU.Max })
        {
            foreach (double v in new[] { original.DomainV.Min, original.DomainV.Max })
            {
                Assert.True(original.PointAt(u, v).DistanceTo(converted.PointAt(u, v)) < 1e-7, $"corner ({u}, {v}) moved.");
            }
        }
    }

    /// <summary>The same set of points, checked in both directions over an interior grid.</summary>
    private static void AssertSameSheet(Surface original, Surface converted)
    {
        double worst = Math.Max(WorstDistance(original, converted), WorstDistance(converted, original));

        Assert.True(worst < 1e-7, $"the sheets differ by {worst}.");
    }

    /// <summary>The worst distance from an interior grid of one surface to the other.</summary>
    private static double WorstDistance(Surface from, Surface to)
    {
        double worst = 0.0;

        for (int i = 1; i < 7; i++)
        {
            for (int j = 1; j < 7; j++)
            {
                Point3d sample = from.PointAt(from.DomainU.Denormalise(i / 7.0), from.DomainV.Denormalise(j / 7.0));
                Point3d nearest = to.ClosestPoint(sample, out _, out _);

                worst = Math.Max(worst, sample.DistanceTo(nearest));
            }
        }

        return worst;
    }
}
