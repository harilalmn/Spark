using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The geometry format, and the reflection diff that keeps it honest.
/// </summary>
/// <remarks>
/// <para>
/// The important test in this file is <see cref="EveryPublicGeometryTypeIsSerialisedOrExcluded"/>.
/// It enumerates the public types of <c>Spark.Geometry</c> by reflection and requires each one to
/// be either in the sample registry below or in the exclusion list with a stated reason. **Adding
/// a geometry type and forgetting serialization is a red build**, which is the same discipline
/// the node importer's two-way diff applies to <c>Spark.Nodes.Core</c> and for the same reason:
/// a register that is maintained by remembering is a register that drifts.
/// </para>
/// <para>
/// Round-tripping is asserted as <b>write, read, write again, and compare the two documents byte
/// for byte</b> rather than by comparing values. That is not a convenience — it is the only
/// comparison available for curves, which deliberately have no value equality, and it is a
/// stricter test than equality for the structs, because it fails on a component that came back
/// one bit different even where <c>Equals</c> would not notice.
/// </para>
/// </remarks>
public sealed class GeometrySerializationTests
{
    /// <summary>
    /// One sample per serialisable public type, chosen to be awkward rather than convenient.
    /// </summary>
    private static readonly Dictionary<Type, object> Samples = new()
    {
        [typeof(Angle)] = Angle.FromDegrees(137.5),
        [typeof(Interval)] = new Interval(-4.25, 11.75),
        [typeof(Tolerance)] = new Tolerance(1e-7, Angle.FromDegrees(0.002), 1e-11),
        [typeof(Point2d)] = new Point2d(1.5, -2.25),
        [typeof(Point3d)] = new Point3d(1.5, -2.25, 1e12),
        [typeof(Vector2d)] = new Vector2d(0.5, -0.25),
        [typeof(Vector3d)] = new Vector3d(0.5, -0.25, 1e-9),
        [typeof(UV)] = new UV(0.25, 0.75),
        [typeof(Quaternion)] = Quaternion.ByAxisAngle(new Vector3d(1.0, 2.0, 3.0), Angle.FromDegrees(41.0)),
        [typeof(BoundingBox)] = new BoundingBox(new Point3d(-1.0, -2.0, -3.0), new Point3d(4.0, 5.0, 6.0)),
        [typeof(Plane)] = Plane.ByOriginNormalXAxis(
            new Point3d(1.0, 2.0, 3.0),
            new Vector3d(1.0, 1.0, 1.0),
            new Vector3d(0.0, 1.0, 0.0)),
        [typeof(CoordinateSystem)] = CoordinateSystem.ByPlane(
            Plane.ByOriginNormal(new Point3d(3.0, 2.0, 1.0), new Vector3d(1.0, -2.0, 0.5))),
        [typeof(Transform)] = Transform.Translation(new Vector3d(1.0, 2.0, 3.0))
            * Transform.Rotation(new Vector3d(1.0, 1.0, 0.0), Angle.FromDegrees(37.0)),
        [typeof(Ray)] = new Ray(new Point3d(1.0, 2.0, 3.0), new Vector3d(0.0, -1.0, 2.0)),
        [typeof(Line)] = new Line(new Point3d(-3.0, -1.0, 0.5), new Point3d(7.0, 4.0, -2.0)),
        [typeof(Arc)] = Arc.ByPlaneRadiusAngles(
            Plane.WorldXY, 5.0, Angle.FromDegrees(17.0), Angle.FromDegrees(203.0)),
        [typeof(Circle)] = new Circle(Plane.WorldYZ, 3.0),
        [typeof(EllipseCurve)] = EllipseCurve.ByPlaneRadiiAngles(
            Plane.WorldXY, 7.0, 2.0, Angle.FromDegrees(10.0), Angle.FromDegrees(190.0)),
        [typeof(PolyLine)] = PolyLine.ByPoints(
        [
            Point3d.Origin,
            new Point3d(4.0, 0.0, 0.0),
            new Point3d(4.0, 4.0, 0.0),
            new Point3d(0.0, 4.0, 3.0),
        ]),
        [typeof(PolyCurve)] = PolyCurve.ByJoinedCurves(
        [
            new Line(new Point3d(-10.0, 0.0, 0.0), Point3d.Origin),
            Arc.ByPlaneRadiusAngles(
                Plane.ByOriginNormal(new Point3d(0.0, 5.0, 0.0), Vector3d.ZAxis),
                5.0,
                -Angle.QuarterTurn,
                Angle.QuarterTurn),
        ]),
    };

    /// <summary>
    /// The public types that are deliberately not serialised, each with the reason.
    /// </summary>
    /// <remarks>
    /// An exclusion is a decision, so it is written down with its argument rather than achieved
    /// by omission. That is the difference between this list and a list of types somebody has
    /// not got to yet.
    /// </remarks>
    private static readonly Dictionary<Type, string> Excluded = new()
    {
        [typeof(Curve)] =
            "The abstract base. Its concrete types are in the registry and a document names one "
            + "of them; there is nothing for a Curve itself to serialise.",
        [typeof(Bvh<>)] =
            "A derived acceleration structure over data it does not own. Writing one to a file "
            + "would let a stale index outlive the geometry it indexes, and rebuilding it is "
            + "cheap; the thing worth storing is always what it was built from.",
        [typeof(GeometryJson)] =
            "The serializer itself.",
    };

    [Fact]
    public void EveryPublicGeometryTypeIsSerialisedOrExcluded()
    {
        List<string> unaccounted = [];
        List<string> countedTwice = [];

        foreach (Type type in typeof(Point3d).Assembly.GetExportedTypes())
        {
            Type key = type.IsGenericType ? type.GetGenericTypeDefinition() : type;

            bool sampled = Samples.ContainsKey(key);
            bool excluded = Excluded.ContainsKey(key);

            if (!sampled && !excluded)
            {
                unaccounted.Add(type.Name);
            }

            if (sampled && excluded)
            {
                countedTwice.Add(type.Name);
            }
        }

        Assert.True(
            unaccounted.Count == 0,
            "Public geometry types that neither round-trip nor carry a reason for not doing so: "
            + $"{string.Join(", ", unaccounted)}. Add a sample to the registry, or an exclusion "
            + "with its argument.");

        Assert.True(
            countedTwice.Count == 0,
            $"Types both sampled and excluded: {string.Join(", ", countedTwice)}.");
    }

    [Fact]
    public void EveryExclusionNamesATypeThatStillExists()
    {
        // The other direction of the diff. An exclusion for a type that has since been renamed
        // or deleted is a comment nobody will ever read again, and it makes the list look more
        // considered than it is.
        HashSet<Type> exported =
        [
            .. typeof(Point3d).Assembly.GetExportedTypes()
                .Select(type => type.IsGenericType ? type.GetGenericTypeDefinition() : type),
        ];

        List<string> stale = [.. Excluded.Keys.Where(type => !exported.Contains(type)).Select(type => type.Name)];
        List<string> unsampled = [.. Samples.Keys.Where(type => !exported.Contains(type)).Select(type => type.Name)];

        Assert.True(stale.Count == 0, $"Exclusions for types that no longer exist: {string.Join(", ", stale)}.");
        Assert.True(unsampled.Count == 0, $"Samples for types that no longer exist: {string.Join(", ", unsampled)}.");
    }

    [Fact]
    public void EverySampleRoundTripsByteForByte()
    {
        List<string> broken = [];

        foreach ((Type type, object sample) in Samples)
        {
            string first = WriteSample(type, sample);
            object read = ReadSample(type, first);
            string second = WriteSample(type, read);

            if (first != second)
            {
                broken.Add($"{type.Name}:\n  wrote {first}\n  reread {second}");
            }
        }

        Assert.True(broken.Count == 0, string.Join("\n", broken));
    }

    [Fact]
    public void AValueThatSurvivesTheRoundTripIsEqualToWhatWentIn()
    {
        // Byte-identical documents are the strict test; this is the readable one, and it covers
        // the types that DO have value equality. A curve has none by design, so it is not here.
        Assert.Equal(
            (Point3d)Samples[typeof(Point3d)],
            GeometryJson.Read<Point3d>(GeometryJson.Write((Point3d)Samples[typeof(Point3d)])));

        Assert.Equal(
            (Transform)Samples[typeof(Transform)],
            GeometryJson.Read<Transform>(GeometryJson.Write((Transform)Samples[typeof(Transform)])));

        Assert.Equal(
            (Plane)Samples[typeof(Plane)],
            GeometryJson.Read<Plane>(GeometryJson.Write((Plane)Samples[typeof(Plane)])));
    }

    [Fact]
    public void TheValuesJsonHasNoLiteralForSurviveTheRoundTrip()
    {
        // Point3d.Unset is NaN in every component and BoundingBox.Empty is built from
        // infinities. Both are ordinary values in this kernel; JSON has a numeric literal for
        // neither, which is the whole reason the format spells them out as strings.
        Assert.Equal(Point3d.Unset, GeometryJson.Read<Point3d>(GeometryJson.Write(Point3d.Unset)));
        Assert.Equal(BoundingBox.Empty, GeometryJson.Read<BoundingBox>(GeometryJson.Write(BoundingBox.Empty)));

        Assert.Contains("NaN", GeometryJson.Write(Point3d.Unset), StringComparison.Ordinal);
        Assert.Contains("Infinity", GeometryJson.Write(BoundingBox.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyBoxComesBackEmptyRatherThanContainingEverything()
    {
        // The public constructor sorts its corners, so a reader that used it would turn the box
        // containing nothing into the box containing everything - the exact inversion of what
        // the file says, and silent.
        BoundingBox read = GeometryJson.Read<BoundingBox>(GeometryJson.Write(BoundingBox.Empty));

        Assert.False(read.IsValid);
        Assert.False(read.Contains(Point3d.Origin));
    }

    [Fact]
    public void ACurveIsWrittenWithItsTypeAndComesBackAsThatType()
    {
        Curve arc = (Curve)Samples[typeof(Arc)];
        string json = GeometryJson.Write(arc);

        Assert.Contains("\"$type\":\"Arc\"", json, StringComparison.Ordinal);

        Curve read = GeometryJson.Read<Curve>(json);

        Assert.IsType<Arc>(read);
        Assert.Equal(GeometryJson.Write(arc), GeometryJson.Write(read));
    }

    [Fact]
    public void APolyCurveCarriesItsSegmentsWithTheirOwnTypes()
    {
        Curve polyCurve = (Curve)Samples[typeof(PolyCurve)];
        string json = GeometryJson.Write(polyCurve);

        Assert.Contains("\"$type\":\"PolyCurve\"", json, StringComparison.Ordinal);
        Assert.Contains("\"$type\":\"Line\"", json, StringComparison.Ordinal);
        Assert.Contains("\"$type\":\"Arc\"", json, StringComparison.Ordinal);

        PolyCurve read = Assert.IsType<PolyCurve>(GeometryJson.Read<Curve>(json));

        Assert.Equal(2, read.SegmentCount);
        Assert.IsType<Line>(read.SegmentAt(0));
        Assert.IsType<Arc>(read.SegmentAt(1));
    }

    [Fact]
    public void EveryObjectCarriesItsOwnSchemaVersion()
    {
        // Not a document-level version: a NurbsCurve at v2 and a Mesh at v1 have to coexist in
        // one file, and a single version forces every type to move together.
        Assert.Contains("\"$v\":1", GeometryJson.Write(Point3d.Origin), StringComparison.Ordinal);
        Assert.Contains("\"$v\":1", GeometryJson.Write<Curve>((Curve)Samples[typeof(Line)]), StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentFromANewerReleaseIsRefusedRatherThanMisread()
    {
        string fromTheFuture = GeometryJson.Write(Point3d.Origin).Replace("\"$v\":1", "\"$v\":99", StringComparison.Ordinal);

        JsonException failure = Assert.Throws<JsonException>(() => GeometryJson.Read<Point3d>(fromTheFuture));

        Assert.Contains("99", failure.Message, StringComparison.Ordinal);
        Assert.Contains("1", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingFieldIsNamedRatherThanDefaulted()
    {
        string missingZ = "{\"$v\":1,\"x\":1,\"y\":2}";

        JsonException failure = Assert.Throws<JsonException>(() => GeometryJson.Read<Point3d>(missingZ));

        // A zero would be a plausible point in the wrong place, which is worse than a failure.
        Assert.Contains("'z'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownCurveTypeIsRefusedByName()
    {
        string unknown = "{\"$v\":1,\"$type\":\"NurbsCurve\"}";

        JsonException failure = Assert.Throws<JsonException>(() => GeometryJson.Read<Curve>(unknown));

        Assert.Contains("NurbsCurve", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldAddedByALaterReleaseIsIgnoredRatherThanRefused()
    {
        // Forward compatibility is what the per-type version is for. A file carrying a field
        // this release does not know, at a version it does, must still read.
        string withExtra = "{\"$v\":1,\"x\":1,\"y\":2,\"z\":3,\"weight\":4}";

        Assert.Equal(new Point3d(1.0, 2.0, 3.0), GeometryJson.Read<Point3d>(withExtra));
    }

    [Fact]
    public void TheIndentedFormIsTheSameDocumentLaidOutDifferently()
    {
        Point3d point = new(1.5, -2.25, 1e12);

        Assert.Equal(point, GeometryJson.Read<Point3d>(GeometryJson.Write(point, indented: true)));
        Assert.Contains('\n', GeometryJson.Write(point, indented: true));
        Assert.DoesNotContain('\n', GeometryJson.Write(point));
    }

    [Fact]
    public void ReadingNullIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => GeometryJson.Read<Point3d>(null!));
    }

    private static string WriteSample(Type type, object sample) =>
        (string)typeof(GeometryJson)
            .GetMethod(nameof(GeometryJson.Write), BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(type)
            .Invoke(null, [sample, false])!;

    private static object ReadSample(Type type, string json) =>
        typeof(GeometryJson)
            .GetMethod(nameof(GeometryJson.Read), BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(type)
            .Invoke(null, [json])!;
}
