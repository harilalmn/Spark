using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The serialization round trip, and the reflection-driven completeness check that stops a new
/// geometry type quietly shipping without one (`E2-T29`, `E2-T31`).
/// </summary>
public sealed class GeometryJsonTests
{
    /// <summary>
    /// One instance of every concrete public geometry type. **A new type with no entry here fails
    /// `EveryPublicGeometryTypeHasASample`**, which is the whole mechanism: the sample list cannot
    /// drift behind the kernel, because the kernel is what it is checked against.
    /// </summary>
    private static readonly Dictionary<Type, object> Samples = new()
    {
        [typeof(Point2d)] = new Point2d(1.5, -2.5),
        [typeof(Vector2d)] = new Vector2d(0.25, 4.0),
        [typeof(UV)] = new UV(0.3, 0.7),
        [typeof(Point3d)] = new Point3d(1.0, -2.0, 3.5),
        [typeof(Vector3d)] = new Vector3d(-0.5, 0.25, 8.0),
        [typeof(Quaternion)] = Quaternion.ByAxisAngle(new Vector3d(1.0, 2.0, 3.0), Angle.FromDegrees(37.0)),
        [typeof(Angle)] = Angle.FromDegrees(123.75),
        [typeof(Interval)] = new Interval(-4.0, 9.5),

        // Deliberately not the uniform clamped default: repeated end knots, an unequal interior
        // spacing and a non-zero start are all things a round trip can get wrong while a tidy
        // 0..1 vector survives by accident.
        [typeof(KnotVector)] = new KnotVector(3, [2, 2, 2, 2, 3.5, 5, 7, 7, 7, 7]),
        [typeof(Tolerance)] = new Tolerance(1e-7, Angle.FromDegrees(0.01), 1e-11),
        [typeof(BoundingBox)] = new BoundingBox(new Point3d(-1.0, -2.0, -3.0), new Point3d(4.0, 5.0, 6.0)),
        [typeof(Plane)] = Plane.ByOriginXAxisYAxis(
            new Point3d(1.0, 2.0, 3.0), new Vector3d(1.0, 1.0, 0.0), new Vector3d(0.0, 1.0, 1.0)),
        [typeof(CoordinateSystem)] = CoordinateSystem.ByOriginXAxisYAxis(
            new Point3d(-1.0, 0.5, 2.0), new Vector3d(0.0, 1.0, 0.0), new Vector3d(0.0, 0.0, 1.0)),
        [typeof(Ray)] = new Ray(new Point3d(1.0, 1.0, 1.0), new Vector3d(0.0, -1.0, 2.0)),
        [typeof(Transform)] = Transform.Translation(new Vector3d(3.0, 4.0, 5.0))
            * Transform.Rotation(Vector3d.ZAxis, Angle.FromDegrees(30.0)),
        [typeof(Line)] = new Line(new Point3d(0.0, 0.0, 0.0), new Point3d(3.0, 4.0, 12.0)),
        [typeof(Circle)] = Circle.ByCentreRadius(new Point3d(1.0, 2.0, 3.0), 2.5),
        [typeof(Arc)] = Arc.ByPlaneRadiusAngles(
            Plane.WorldXY, 3.0, Angle.FromDegrees(15.0), Angle.FromDegrees(220.0)),
        [typeof(EllipseCurve)] = EllipseCurve.ByPlaneRadiiAngles(
            Plane.WorldYZ, 4.0, 2.0, Angle.Zero, Angle.FromDegrees(300.0)),
        // Rational, degree 3, with an interior knot and unequal weights. A non-rational curve
        // over a uniform 0..1 vector would round-trip even if the weights or the knots were being
        // dropped, which is the failure a sample exists to catch.
        [typeof(NurbsCurve)] = new NurbsCurve(
            3,
            [
                new Point3d(0, 0, 0),
                new Point3d(1, 4, 1),
                new Point3d(4, 5, -1),
                new Point3d(7, 1, 2),
                new Point3d(9, 2, 0),
            ],
            [2, 2, 2, 2, 4, 6, 6, 6, 6],
            [1.0, 2.5, 0.4, 1.8, 1.0]),

        [typeof(PolyLine)] = PolyLine.ByPoints(
        [
            new Point3d(0.0, 0.0, 0.0),
            new Point3d(1.0, 0.0, 0.0),
            new Point3d(1.0, 1.0, 0.5),
        ]),
        [typeof(PolyCurve)] = PolyCurve.ByJoinedCurves(
        [
            new Line(new Point3d(0.0, 0.0, 0.0), new Point3d(1.0, 0.0, 0.0)),
            new Line(new Point3d(1.0, 0.0, 0.0), new Point3d(1.0, 2.0, 0.0)),
        ]),

        // Surfaces. Each sample uses a *patch* rather than a whole sphere or torus wherever the
        // type allows one, because a partial domain is what a round trip can actually get wrong:
        // a whole one round-trips through a default and looks correct whatever was written.
        [typeof(PlaneSurface)] = new PlaneSurface(
            Plane.WorldXY, new Interval(-1.0, 2.0), new Interval(0.5, 4.0)),
        [typeof(SphericalSurface)] = new SphericalSurface(
            Plane.WorldXY, 2.5, new Interval(0.2, 3.0), new Interval(-0.5, 1.1)),
        [typeof(CylindricalSurface)] = new CylindricalSurface(
            Plane.WorldXZ, 1.5, new Interval(0.1, 2.0), new Interval(-1.0, 3.0)),
        [typeof(ConicalSurface)] = new ConicalSurface(
            Plane.WorldXY, 1.25, Angle.FromRadians(0.35), new Interval(0.0, 2.5), new Interval(0.0, 4.0)),
        [typeof(ToroidalSurface)] = new ToroidalSurface(
            Plane.WorldXY, 5.0, 1.5, new Interval(0.0, 2.0), new Interval(0.5, 3.5)),
        [typeof(ExtrusionSurface)] = new ExtrusionSurface(
            new Line(new Point3d(0.0, 0.0, 0.0), new Point3d(3.0, 0.0, 0.0)),
            new Vector3d(0.0, 0.0, 1.0),
            new Interval(0.0, 4.0)),
        [typeof(RevolutionSurface)] = new RevolutionSurface(
            new Line(new Point3d(2.0, 0.0, 0.0), new Point3d(2.0, 0.0, 5.0)),
            Point3d.Origin,
            Vector3d.ZAxis,
            new Interval(0.0, 2.0)),
        // A rational sample, because a non-rational one round-trips through the weightless path and
        // would never exercise the weights at all.
        [typeof(NurbsSurface)] = new SphericalSurface(
            Plane.WorldXY, 2.5, new Interval(0.2, 3.0), new Interval(-0.5, 1.1)).ToNurbsSurface(),
        [typeof(RuledSurface)] = new RuledSurface(
            new Line(new Point3d(0.0, 0.0, 0.0), new Point3d(3.0, 0.0, 0.0)),
            new Line(new Point3d(0.0, 4.0, 1.0), new Point3d(3.0, 4.0, 1.0))),
    };

    /// <summary>
    /// Types that are public geometry and deliberately have no serialized form, each with the
    /// reason. **An exclusion has to be argued for here**, which is the difference between a
    /// decision and an omission.
    /// </summary>
    private static readonly Dictionary<Type, string> Excluded = new()
    {
        [typeof(Curve)] = "abstract; its concrete subclasses each have a sample",
        [typeof(Surface)] = "abstract; its concrete subclasses each have a sample",
        [typeof(BoundingVolumeHierarchy)] =
            "an index over other things, derived and rebuildable in microseconds. Storing it "
            + "would mean storing a second copy of the boxes and a promise that they still agree",
    };

    [Fact]
    public void EveryPublicGeometryTypeHasASample()
    {
        Type[] uncovered =
        [
            .. typeof(Point3d).Assembly
                .GetExportedTypes()
                .Where(IsGeometryType)
                .Where(type => !Samples.ContainsKey(type) && !Excluded.ContainsKey(type))
                .OrderBy(type => type.Name),
        ];

        Assert.True(
            uncovered.Length == 0,
            "These public geometry types have no serialization sample, so nothing checks that "
            + "they survive being saved and reopened: "
            + string.Join(", ", uncovered.Select(type => type.Name))
            + ". Add a sample to GeometryJsonTests, or an entry to Excluded with the reason.");
    }

    [Fact]
    public void NoSampleNamesATypeThatNoLongerExists()
    {
        // The other direction of the same diff. A sample for a deleted or renamed type is dead
        // weight that looks like coverage.
        Type[] stale =
        [
            .. Samples.Keys
                .Concat(Excluded.Keys)
                .Where(type => !typeof(Point3d).Assembly.GetExportedTypes().Contains(type))
                .OrderBy(type => type.Name),
        ];

        Assert.Empty(stale);
    }

    [Fact]
    public void EverySampleSurvivesARoundTrip()
    {
        foreach ((Type type, object sample) in Samples.OrderBy(pair => pair.Key.Name))
        {
            string json = GeometryJson.Serialize(sample);
            object restored = GeometryJson.Deserialize(json);

            Assert.Equal(type, restored.GetType());
            AssertSame(sample, restored);
        }
    }

    [Fact]
    public void EverySampleDeclaresItsTypeAndVersionInTheDocument()
    {
        foreach ((Type type, object sample) in Samples)
        {
            string json = GeometryJson.Serialize(sample);

            Assert.Contains($"\"type\":\"{type.Name}\"", json);
            Assert.Contains("\"version\":1", json);
        }
    }

    [Fact]
    public void ANestedValueCarriesItsOwnEnvelope()
    {
        // A Circle holds a Plane which holds three Point3d and Vector3d values. Each one is
        // self-describing, which is what lets a type version on its own timetable.
        string json = GeometryJson.Serialize(Circle.ByCentreRadius(Point3d.Origin, 1.0));

        Assert.Contains("\"type\":\"Circle\"", json);
        Assert.Contains("\"type\":\"Plane\"", json);
        Assert.Contains("\"type\":\"Point3d\"", json);
        Assert.Contains("\"type\":\"Vector3d\"", json);
    }

    [Fact]
    public void NonFiniteNumbersSurviveBecauseTheyAreRealValues()
    {
        // BoundingBox.Empty is built from infinities and is the correct seed for accumulating a
        // box. A serializer that cannot write it is a serializer that cannot save a legal value.
        string json = GeometryJson.Serialize(BoundingBox.Empty);

        Assert.Contains("Infinity", json);
        Assert.Equal(BoundingBox.Empty, GeometryJson.Deserialize<BoundingBox>(json));

        string nan = GeometryJson.Serialize(new Point3d(double.NaN, 1.0, 2.0));
        Point3d restored = GeometryJson.Deserialize<Point3d>(nan);

        Assert.True(double.IsNaN(restored.X));
        Assert.Equal(1.0, restored.Y);
    }

    [Fact]
    public void AVersionThisBuildCannotReadIsRefusedRatherThanGuessedAt()
    {
        string future = GeometryJson.Serialize(Point3d.Origin).Replace("\"version\":1", "\"version\":2");

        NotSupportedException failure = Assert.Throws<NotSupportedException>(
            () => GeometryJson.Deserialize(future));

        Assert.Contains("Point3d", failure.Message);
        Assert.Contains("version 2", failure.Message);
    }

    [Fact]
    public void AnUnknownTypeIsRefused()
    {
        // The name is deliberately one that cannot ever become a real type. This test used to say
        // "NurbsCurve", which was unknown when it was written and stopped being unknown the day
        // NurbsCurve landed - at which point the test failed for a reason that had nothing to do
        // with what it checks. Naming a planned type as a stand-in for an unplanned one is a trap.
        Assert.Throws<NotSupportedException>(
            () => GeometryJson.Deserialize("{\"type\":\"NotATypeThisBuildKnows\",\"version\":1}"));
        Assert.Throws<NotSupportedException>(() => GeometryJson.Deserialize("{\"version\":1}"));
        Assert.Throws<NotSupportedException>(() => GeometryJson.Deserialize("42"));
    }

    [Fact]
    public void SerializingATypeWithNoFormIsRefusedRatherThanSilentlyEmpty()
    {
        NotSupportedException failure = Assert.Throws<NotSupportedException>(
            () => GeometryJson.Serialize(BoundingVolumeHierarchy.Build(ReadOnlySpan<BoundingBox>.Empty)));

        Assert.Contains(nameof(BoundingVolumeHierarchy), failure.Message);
    }

    [Fact]
    public void TheTypedOverloadSaysSoWhenTheDocumentHoldsSomethingElse()
    {
        string json = GeometryJson.Serialize(Point3d.Origin);

        Assert.Equal(Point3d.Origin, GeometryJson.Deserialize<Point3d>(json));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => GeometryJson.Deserialize<Circle>(json));

        Assert.Contains("Point3d", failure.Message);
        Assert.Contains("Circle", failure.Message);
    }

    [Fact]
    public void NullIsRefusedOnBothSides()
    {
        Assert.Throws<ArgumentNullException>(() => GeometryJson.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => GeometryJson.Deserialize(null!));
    }

    [Fact]
    public void IndentedOutputIsTheSameDocument()
    {
        object sample = Samples[typeof(Arc)];

        AssertSame(sample, GeometryJson.Deserialize(GeometryJson.Serialize(sample, indented: true)));
    }

    private static bool IsGeometryType(Type type) =>
        type.Namespace == "Spark.Geometry"
        && !type.IsEnum
        && !type.IsInterface
        && !(type.IsAbstract && type.IsSealed);

    private static void AssertSame(object expected, object actual)
    {
        if (expected is Surface surface)
        {
            AssertSameSurface(surface, (Surface)actual);

            return;
        }

        if (expected is Curve curve)
        {
            AssertSameCurve(curve, (Curve)actual);
            return;
        }

        // Doubles are written in their shortest round-trippable form, so a value type comes back
        // bit-identical and exact equality is the right assertion. The two exceptions are the
        // frames, which are rebuilt through a factory that re-orthonormalises.
        switch (expected)
        {
            case Plane plane:
                Assert.True(plane.EqualsWithin((Plane)actual));
                break;

            case CoordinateSystem frame:
                Assert.True(frame.Origin.EqualsWithin(((CoordinateSystem)actual).Origin));
                Assert.True(frame.XAxis.EqualsWithin(((CoordinateSystem)actual).XAxis));
                Assert.True(frame.YAxis.EqualsWithin(((CoordinateSystem)actual).YAxis));
                break;

            default:
                Assert.Equal(expected, actual);
                break;
        }
    }

    /// <summary>
    /// Two surfaces are the same when they occupy the same positions over the same domains.
    /// </summary>
    /// <remarks>
    /// <b>The same reasoning as <see cref="AssertSameCurve"/>, for the same reason.</b> Equality on
    /// surfaces is deliberately not defined — two surfaces that describe the same sheet under
    /// different parameterisations are a tolerance question — so sameness is asserted the only way
    /// it is defined: sample the grid and compare. A reference-equality assertion would pass on a
    /// type that came back with the wrong radius and the right frame, because the frame is what
    /// <c>ToString</c> shows.
    /// </remarks>
    private static void AssertSameSurface(Surface expected, Surface actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.DomainU.Min, actual.DomainU.Min, 12);
        Assert.Equal(expected.DomainU.Max, actual.DomainU.Max, 12);
        Assert.Equal(expected.DomainV.Min, actual.DomainV.Min, 12);
        Assert.Equal(expected.DomainV.Max, actual.DomainV.Max, 12);
        Assert.Equal(expected.Area, actual.Area, 9);

        for (int i = 0; i <= 5; i++)
        {
            for (int j = 0; j <= 5; j++)
            {
                double u = expected.DomainU.Denormalise(i / 5.0);
                double v = expected.DomainV.Denormalise(j / 5.0);

                Assert.True(
                    expected.PointAt(u, v).EqualsWithin(actual.PointAt(u, v)),
                    $"the surfaces differ at ({u}, {v})");
            }
        }
    }

    private static void AssertSameCurve(Curve expected, Curve actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Domain.Min, actual.Domain.Min, 12);
        Assert.Equal(expected.Domain.Max, actual.Domain.Max, 12);
        Assert.Equal(expected.Length, actual.Length, 9);

        // Equality on curves is deliberately not defined (E2-T9), so sameness is asserted the
        // only way it is defined: the two occupy the same positions.
        for (int i = 0; i <= 10; i++)
        {
            double t = expected.Domain.Min + ((expected.Domain.Max - expected.Domain.Min) * (i / 10.0));

            Assert.True(expected.PointAt(t).EqualsWithin(actual.PointAt(t)));
        }
    }
}
