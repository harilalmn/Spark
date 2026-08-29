using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spark.Geometry;

/// <summary>
/// The primitives every geometry converter is built from: a versioned object envelope, and a
/// number format that admits the non-finite values the kernel actually has.
/// </summary>
internal static class GeometryJsonFormat
{
    internal const string VersionProperty = "$v";
    internal const string TypeProperty = "$type";

    /// <summary>
    /// Writes a number, spelling out the three values JSON has no literal for.
    /// </summary>
    internal static void WriteNumber(Utf8JsonWriter writer, string name, double value)
    {
        if (double.IsFinite(value))
        {
            writer.WriteNumber(name, value);
            return;
        }

        // Utf8JsonWriter.WriteNumber THROWS on NaN and infinity rather than writing something
        // wrong, which is the right behaviour and is why this branch exists rather than a
        // serializer option: the option covers JsonSerializer's own number handling, not a
        // converter writing a number itself.
        writer.WriteString(
            name,
            double.IsNaN(value) ? "NaN" : value > 0.0 ? "Infinity" : "-Infinity");
    }

    /// <summary>
    /// Reads a number written by <see cref="WriteNumber"/>, from either spelling.
    /// </summary>
    internal static double ReadNumber(ref Utf8JsonReader reader, string name)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetDouble();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() switch
            {
                "NaN" => double.NaN,
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                string other => throw new JsonException(
                    $"'{name}' is '{other}', which is not a number or a named floating-point value."),
                null => throw new JsonException($"'{name}' is null rather than a number."),
            };
        }

        throw new JsonException($"'{name}' is a {reader.TokenType} rather than a number.");
    }

    /// <summary>
    /// Checks a document's <c>$v</c> against what this release understands.
    /// </summary>
    internal static void CheckVersion(int version, string typeName)
    {
        if (version > GeometryJson.SchemaVersion)
        {
            throw new JsonException(
                $"This {typeName} is at schema version {version} and this release reads up to "
                + $"{GeometryJson.SchemaVersion}. Reading it would need a newer Spark, not a "
                + "more forgiving one.");
        }
    }

    /// <summary>
    /// Reads a flat object of named numbers, plus <c>$v</c>.
    /// </summary>
    /// <remarks>
    /// Every converter for a simple value goes through here rather than writing its own
    /// property loop, because a property loop is where the errors are: a missing
    /// <c>reader.Read()</c> reads a value as a name and the failure surfaces four types later.
    /// One loop, tested once, used by all of them.
    /// </remarks>
    internal static Fields ReadFields(ref Utf8JsonReader reader, string typeName)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"A {typeName} must be a JSON object.");
        }

        Dictionary<string, double> numbers = [];
        int version = 1;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"A {typeName} has a malformed property.");
            }

            string name = reader.GetString()!;
            reader.Read();

            if (name == VersionProperty)
            {
                version = reader.GetInt32();
                continue;
            }

            // Unknown properties are read and discarded rather than refused. A file written by
            // a LATER release carrying a field this one does not know is exactly the case the
            // per-type version exists to make survivable, and refusing it would make every
            // forward-compatible addition a breaking change.
            numbers[name] = ReadNumber(ref reader, name);
        }

        CheckVersion(version, typeName);

        return new Fields(numbers);
    }

    internal sealed class Fields(Dictionary<string, double> numbers)
    {
        public double Number(string name, string typeName) =>
            numbers.TryGetValue(name, out double value)
                ? value
                : throw new JsonException($"A {typeName} is missing its '{name}'.");
    }
}

internal sealed class AngleJsonConverter : JsonConverter<Angle>
{
    public override Angle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(Angle));

        return Angle.FromRadians(fields.Number("radians", nameof(Angle)));
    }

    public override void Write(Utf8JsonWriter writer, Angle value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);

        // Radians, not degrees, because radians is what the type stores. Writing degrees would
        // make a round trip a multiplication by pi/180 and back, which is not the identity.
        GeometryJsonFormat.WriteNumber(writer, "radians", value.Radians);
        writer.WriteEndObject();
    }
}

internal sealed class IntervalJsonConverter : JsonConverter<Interval>
{
    public override Interval Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(Interval));

        return new Interval(
            fields.Number("min", nameof(Interval)),
            fields.Number("max", nameof(Interval)));
    }

    public override void Write(Utf8JsonWriter writer, Interval value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);
        GeometryJsonFormat.WriteNumber(writer, "min", value.Min);
        GeometryJsonFormat.WriteNumber(writer, "max", value.Max);
        writer.WriteEndObject();
    }
}

internal sealed class ToleranceJsonConverter : JsonConverter<Tolerance>
{
    public override Tolerance Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(Tolerance));

        return new Tolerance(
            fields.Number("linear", nameof(Tolerance)),
            Angle.FromRadians(fields.Number("angular", nameof(Tolerance))),
            fields.Number("relativeEpsilon", nameof(Tolerance)));
    }

    public override void Write(Utf8JsonWriter writer, Tolerance value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);

        // The RESOLVED components, which is what the properties give. A default-constructed
        // Tolerance stores zeros and means "the defaults"; writing the zeros would make the
        // file mean "the defaults of whichever release reads it", and a tolerance that changes
        // between releases is a geometry file that changes shape between releases.
        GeometryJsonFormat.WriteNumber(writer, "linear", value.Linear);
        GeometryJsonFormat.WriteNumber(writer, "angular", value.Angular.Radians);
        GeometryJsonFormat.WriteNumber(writer, "relativeEpsilon", value.RelativeEpsilon);
        writer.WriteEndObject();
    }
}

internal sealed class Point2dJsonConverter : JsonConverter<Point2d>
{
    public override Point2d Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(Point2d));

        return new Point2d(fields.Number("x", nameof(Point2d)), fields.Number("y", nameof(Point2d)));
    }

    public override void Write(Utf8JsonWriter writer, Point2d value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);
        GeometryJsonFormat.WriteNumber(writer, "x", value.X);
        GeometryJsonFormat.WriteNumber(writer, "y", value.Y);
        writer.WriteEndObject();
    }
}

internal sealed class Point3dJsonConverter : JsonConverter<Point3d>
{
    public override Point3d Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(Point3d));

        return new Point3d(
            fields.Number("x", nameof(Point3d)),
            fields.Number("y", nameof(Point3d)),
            fields.Number("z", nameof(Point3d)));
    }

    public override void Write(Utf8JsonWriter writer, Point3d value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);
        GeometryJsonFormat.WriteNumber(writer, "x", value.X);
        GeometryJsonFormat.WriteNumber(writer, "y", value.Y);
        GeometryJsonFormat.WriteNumber(writer, "z", value.Z);
        writer.WriteEndObject();
    }
}

internal sealed class Vector2dJsonConverter : JsonConverter<Vector2d>
{
    public override Vector2d Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(Vector2d));

        return new Vector2d(fields.Number("x", nameof(Vector2d)), fields.Number("y", nameof(Vector2d)));
    }

    public override void Write(Utf8JsonWriter writer, Vector2d value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);
        GeometryJsonFormat.WriteNumber(writer, "x", value.X);
        GeometryJsonFormat.WriteNumber(writer, "y", value.Y);
        writer.WriteEndObject();
    }
}

internal sealed class Vector3dJsonConverter : JsonConverter<Vector3d>
{
    public override Vector3d Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(Vector3d));

        return new Vector3d(
            fields.Number("x", nameof(Vector3d)),
            fields.Number("y", nameof(Vector3d)),
            fields.Number("z", nameof(Vector3d)));
    }

    public override void Write(Utf8JsonWriter writer, Vector3d value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);
        GeometryJsonFormat.WriteNumber(writer, "x", value.X);
        GeometryJsonFormat.WriteNumber(writer, "y", value.Y);
        GeometryJsonFormat.WriteNumber(writer, "z", value.Z);
        writer.WriteEndObject();
    }
}

internal sealed class UVJsonConverter : JsonConverter<UV>
{
    public override UV Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(UV));

        return new UV(fields.Number("u", nameof(UV)), fields.Number("v", nameof(UV)));
    }

    public override void Write(Utf8JsonWriter writer, UV value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);
        GeometryJsonFormat.WriteNumber(writer, "u", value.U);
        GeometryJsonFormat.WriteNumber(writer, "v", value.V);
        writer.WriteEndObject();
    }
}

internal sealed class QuaternionJsonConverter : JsonConverter<Quaternion>
{
    public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(Quaternion));

        return new Quaternion(
            fields.Number("x", nameof(Quaternion)),
            fields.Number("y", nameof(Quaternion)),
            fields.Number("z", nameof(Quaternion)),
            fields.Number("w", nameof(Quaternion)));
    }

    public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);

        // The components as stored, NOT normalised on the way out. A quaternion that has
        // drifted off the unit sphere is a value the type admits, and a writer that quietly
        // corrected it would make a round trip change the value it was given.
        GeometryJsonFormat.WriteNumber(writer, "w", value.W);
        GeometryJsonFormat.WriteNumber(writer, "x", value.X);
        GeometryJsonFormat.WriteNumber(writer, "y", value.Y);
        GeometryJsonFormat.WriteNumber(writer, "z", value.Z);
        writer.WriteEndObject();
    }
}

internal sealed class BoundingBoxJsonConverter : JsonConverter<BoundingBox>
{
    public override BoundingBox Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(BoundingBox));

        // The bounds are assigned as read rather than sorted. Empty is an INVERTED box, and the
        // public constructor sorts its corners - so reading Empty through it would return the
        // box containing everything, which is the exact inversion of what the file said.
        return new BoundingBox(
            fields.Number("minX", nameof(BoundingBox)),
            fields.Number("minY", nameof(BoundingBox)),
            fields.Number("minZ", nameof(BoundingBox)),
            fields.Number("maxX", nameof(BoundingBox)),
            fields.Number("maxY", nameof(BoundingBox)),
            fields.Number("maxZ", nameof(BoundingBox)));
    }

    public override void Write(Utf8JsonWriter writer, BoundingBox value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);

        // Six numbers rather than two nested points: a box is one value, its corners are not
        // separately meaningful, and this keeps a document that is mostly boxes readable.
        GeometryJsonFormat.WriteNumber(writer, "minX", value.Min.X);
        GeometryJsonFormat.WriteNumber(writer, "minY", value.Min.Y);
        GeometryJsonFormat.WriteNumber(writer, "minZ", value.Min.Z);
        GeometryJsonFormat.WriteNumber(writer, "maxX", value.Max.X);
        GeometryJsonFormat.WriteNumber(writer, "maxY", value.Max.Y);
        GeometryJsonFormat.WriteNumber(writer, "maxZ", value.Max.Z);
        writer.WriteEndObject();
    }
}

internal sealed class PlaneJsonConverter : JsonConverter<Plane>
{
    public override Plane Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(Plane));

        return new Plane(
            new Point3d(
                fields.Number("originX", nameof(Plane)),
                fields.Number("originY", nameof(Plane)),
                fields.Number("originZ", nameof(Plane))),
            new Vector3d(
                fields.Number("xAxisX", nameof(Plane)),
                fields.Number("xAxisY", nameof(Plane)),
                fields.Number("xAxisZ", nameof(Plane))),
            new Vector3d(
                fields.Number("yAxisX", nameof(Plane)),
                fields.Number("yAxisY", nameof(Plane)),
                fields.Number("yAxisZ", nameof(Plane))),
            new Vector3d(
                fields.Number("normalX", nameof(Plane)),
                fields.Number("normalY", nameof(Plane)),
                fields.Number("normalZ", nameof(Plane))));
    }

    public override void Write(Utf8JsonWriter writer, Plane value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);

        // All four vectors, although origin and normal generate a plane. Re-deriving the
        // in-plane axes on read would pick them arbitrarily rather than recovering the ones
        // that were written, so a plane's 2d coordinate system - which is what To2d and
        // everything built on it consume - would come back rotated.
        GeometryJsonFormat.WriteNumber(writer, "originX", value.Origin.X);
        GeometryJsonFormat.WriteNumber(writer, "originY", value.Origin.Y);
        GeometryJsonFormat.WriteNumber(writer, "originZ", value.Origin.Z);
        GeometryJsonFormat.WriteNumber(writer, "xAxisX", value.XAxis.X);
        GeometryJsonFormat.WriteNumber(writer, "xAxisY", value.XAxis.Y);
        GeometryJsonFormat.WriteNumber(writer, "xAxisZ", value.XAxis.Z);
        GeometryJsonFormat.WriteNumber(writer, "yAxisX", value.YAxis.X);
        GeometryJsonFormat.WriteNumber(writer, "yAxisY", value.YAxis.Y);
        GeometryJsonFormat.WriteNumber(writer, "yAxisZ", value.YAxis.Z);
        GeometryJsonFormat.WriteNumber(writer, "normalX", value.Normal.X);
        GeometryJsonFormat.WriteNumber(writer, "normalY", value.Normal.Y);
        GeometryJsonFormat.WriteNumber(writer, "normalZ", value.Normal.Z);
        writer.WriteEndObject();
    }
}

internal sealed class CoordinateSystemJsonConverter : JsonConverter<CoordinateSystem>
{
    public override CoordinateSystem Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(CoordinateSystem));

        return new CoordinateSystem(
            new Point3d(
                fields.Number("originX", nameof(CoordinateSystem)),
                fields.Number("originY", nameof(CoordinateSystem)),
                fields.Number("originZ", nameof(CoordinateSystem))),
            new Vector3d(
                fields.Number("xAxisX", nameof(CoordinateSystem)),
                fields.Number("xAxisY", nameof(CoordinateSystem)),
                fields.Number("xAxisZ", nameof(CoordinateSystem))),
            new Vector3d(
                fields.Number("yAxisX", nameof(CoordinateSystem)),
                fields.Number("yAxisY", nameof(CoordinateSystem)),
                fields.Number("yAxisZ", nameof(CoordinateSystem))),
            new Vector3d(
                fields.Number("zAxisX", nameof(CoordinateSystem)),
                fields.Number("zAxisY", nameof(CoordinateSystem)),
                fields.Number("zAxisZ", nameof(CoordinateSystem))));
    }

    public override void Write(Utf8JsonWriter writer, CoordinateSystem value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);
        GeometryJsonFormat.WriteNumber(writer, "originX", value.Origin.X);
        GeometryJsonFormat.WriteNumber(writer, "originY", value.Origin.Y);
        GeometryJsonFormat.WriteNumber(writer, "originZ", value.Origin.Z);
        GeometryJsonFormat.WriteNumber(writer, "xAxisX", value.XAxis.X);
        GeometryJsonFormat.WriteNumber(writer, "xAxisY", value.XAxis.Y);
        GeometryJsonFormat.WriteNumber(writer, "xAxisZ", value.XAxis.Z);
        GeometryJsonFormat.WriteNumber(writer, "yAxisX", value.YAxis.X);
        GeometryJsonFormat.WriteNumber(writer, "yAxisY", value.YAxis.Y);
        GeometryJsonFormat.WriteNumber(writer, "yAxisZ", value.YAxis.Z);
        GeometryJsonFormat.WriteNumber(writer, "zAxisX", value.ZAxis.X);
        GeometryJsonFormat.WriteNumber(writer, "zAxisY", value.ZAxis.Y);
        GeometryJsonFormat.WriteNumber(writer, "zAxisZ", value.ZAxis.Z);
        writer.WriteEndObject();
    }
}

internal sealed class TransformJsonConverter : JsonConverter<Transform>
{
    public override Transform Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(Transform));

        double Entry(int row, int column) =>
            fields.Number(
                string.Create(CultureInfo.InvariantCulture, $"m{row}{column}"),
                nameof(Transform));

        return new Transform(
            Entry(0, 0), Entry(0, 1), Entry(0, 2), Entry(0, 3),
            Entry(1, 0), Entry(1, 1), Entry(1, 2), Entry(1, 3),
            Entry(2, 0), Entry(2, 1), Entry(2, 2), Entry(2, 3),
            Entry(3, 0), Entry(3, 1), Entry(3, 2), Entry(3, 3));
    }

    public override void Write(Utf8JsonWriter writer, Transform value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);

        // Named entries rather than a flat array of sixteen numbers, because a matrix written
        // as an array is a matrix whose row-versus-column order has to be documented somewhere
        // else and will eventually be read the other way round.
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                GeometryJsonFormat.WriteNumber(
                    writer,
                    string.Create(CultureInfo.InvariantCulture, $"m{row}{column}"),
                    value[row, column]);
            }
        }

        writer.WriteEndObject();
    }
}

internal sealed class RayJsonConverter : JsonConverter<Ray>
{
    public override Ray Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        GeometryJsonFormat.Fields fields = GeometryJsonFormat.ReadFields(ref reader, nameof(Ray));

        return new Ray(
            new Point3d(
                fields.Number("originX", nameof(Ray)),
                fields.Number("originY", nameof(Ray)),
                fields.Number("originZ", nameof(Ray))),
            new Vector3d(
                fields.Number("directionX", nameof(Ray)),
                fields.Number("directionY", nameof(Ray)),
                fields.Number("directionZ", nameof(Ray))));
    }

    public override void Write(Utf8JsonWriter writer, Ray value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);
        GeometryJsonFormat.WriteNumber(writer, "originX", value.Origin.X);
        GeometryJsonFormat.WriteNumber(writer, "originY", value.Origin.Y);
        GeometryJsonFormat.WriteNumber(writer, "originZ", value.Origin.Z);
        GeometryJsonFormat.WriteNumber(writer, "directionX", value.Direction.X);
        GeometryJsonFormat.WriteNumber(writer, "directionY", value.Direction.Y);
        GeometryJsonFormat.WriteNumber(writer, "directionZ", value.Direction.Z);
        writer.WriteEndObject();
    }
}

/// <summary>
/// The polymorphic converter: the one place a document says which kind of curve it holds.
/// </summary>
/// <remarks>
/// The discriminator is the type's own name, written in full and matched exactly. It is
/// deliberately not a number and deliberately not a namespace-qualified assembly name: a number
/// is unreadable in a file somebody is diagnosing, and an assembly-qualified name is an
/// instruction to a deserializer to load a type named in the document, which is how a geometry
/// file becomes an execution vector.
/// </remarks>
internal sealed class CurveJsonConverter : JsonConverter<Curve>
{
    public override bool CanConvert(Type typeToConvert) => typeof(Curve).IsAssignableFrom(typeToConvert);

    public override Curve Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        JsonElement document = JsonElement.ParseValue(ref reader);

        return ReadCurve(document, options);
    }

    public override void Write(Utf8JsonWriter writer, Curve value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteNumber(GeometryJsonFormat.VersionProperty, GeometryJson.SchemaVersion);

        switch (value)
        {
            case Line line:
                writer.WriteString(GeometryJsonFormat.TypeProperty, nameof(Line));
                WritePoint(writer, "start", line.StartPoint);
                WritePoint(writer, "end", line.EndPoint);
                break;

            case Arc arc:
                writer.WriteString(GeometryJsonFormat.TypeProperty, nameof(Arc));
                WritePlane(writer, arc.Plane);
                GeometryJsonFormat.WriteNumber(writer, "radius", arc.Radius);
                GeometryJsonFormat.WriteNumber(writer, "startAngle", arc.StartAngle.Radians);
                GeometryJsonFormat.WriteNumber(writer, "sweepAngle", arc.SweepAngle.Radians);
                break;

            case Circle circle:
                writer.WriteString(GeometryJsonFormat.TypeProperty, nameof(Circle));
                WritePlane(writer, circle.Plane);
                GeometryJsonFormat.WriteNumber(writer, "radius", circle.Radius);
                break;

            case EllipseCurve ellipse:
                writer.WriteString(GeometryJsonFormat.TypeProperty, nameof(EllipseCurve));
                WritePlane(writer, ellipse.Plane);
                GeometryJsonFormat.WriteNumber(writer, "xRadius", ellipse.XRadius);
                GeometryJsonFormat.WriteNumber(writer, "yRadius", ellipse.YRadius);
                GeometryJsonFormat.WriteNumber(writer, "startAngle", ellipse.StartAngle.Radians);
                GeometryJsonFormat.WriteNumber(writer, "sweepAngle", ellipse.SweepAngle.Radians);
                break;

            case PolyLine polyLine:
                writer.WriteString(GeometryJsonFormat.TypeProperty, nameof(PolyLine));

                // One flat array of numbers, not an array of versioned point objects. The
                // polyline owns this layout and its own version covers it - see GeometryJson.
                writer.WriteStartArray("points");

                foreach (Point3d point in polyLine.Points())
                {
                    WriteBareNumber(writer, point.X);
                    WriteBareNumber(writer, point.Y);
                    WriteBareNumber(writer, point.Z);
                }

                writer.WriteEndArray();
                break;

            case PolyCurve polyCurve:
                writer.WriteString(GeometryJsonFormat.TypeProperty, nameof(PolyCurve));
                writer.WriteStartArray("segments");

                for (int index = 0; index < polyCurve.SegmentCount; index++)
                {
                    Write(writer, polyCurve.SegmentAt(index), options);
                }

                writer.WriteEndArray();
                break;

            default:
                throw new NotSupportedException(
                    $"{value.GetType().Name} has no place in the geometry format yet. Adding a "
                    + "curve type means adding it here; the round-trip test in the suite is what "
                    + "makes forgetting a red build rather than a surprise in somebody's file.");
        }

        writer.WriteEndObject();
    }

    private static Curve ReadCurve(JsonElement document, JsonSerializerOptions options)
    {
        if (document.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A curve must be a JSON object.");
        }

        int version = document.TryGetProperty(GeometryJsonFormat.VersionProperty, out JsonElement carried)
            ? carried.GetInt32()
            : 1;

        string discriminator = document.TryGetProperty(GeometryJsonFormat.TypeProperty, out JsonElement type)
            ? type.GetString() ?? throw new JsonException("A curve's '$type' is null.")
            : throw new JsonException("A curve is missing its '$type'.");

        GeometryJsonFormat.CheckVersion(version, discriminator);

        return discriminator switch
        {
            nameof(Line) => new Line(ReadPoint(document, "start"), ReadPoint(document, "end")),

            nameof(Arc) => Arc.ByPlaneRadiusAngles(
                ReadPlane(document),
                Number(document, "radius"),
                Angle.FromRadians(Number(document, "startAngle")),
                Angle.FromRadians(Number(document, "sweepAngle"))),

            nameof(Circle) => new Circle(ReadPlane(document), Number(document, "radius")),

            nameof(EllipseCurve) => EllipseCurve.ByPlaneRadiiAngles(
                ReadPlane(document),
                Number(document, "xRadius"),
                Number(document, "yRadius"),
                Angle.FromRadians(Number(document, "startAngle")),
                Angle.FromRadians(Number(document, "sweepAngle"))),

            nameof(PolyLine) => PolyLine.ByPoints(ReadPoints(document)),

            nameof(PolyCurve) => PolyCurve.ByJoinedCurves(ReadSegments(document, options)),

            _ => throw new JsonException(
                $"'{discriminator}' is not a curve type this release knows. Either the file "
                + "came from a newer Spark, or its '$type' is wrong."),
        };
    }

    private static List<Curve> ReadSegments(JsonElement document, JsonSerializerOptions options)
    {
        if (!document.TryGetProperty("segments", out JsonElement segments)
            || segments.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("A PolyCurve is missing its 'segments'.");
        }

        List<Curve> curves = [];

        foreach (JsonElement segment in segments.EnumerateArray())
        {
            curves.Add(ReadCurve(segment, options));
        }

        return curves;
    }

    private static List<Point3d> ReadPoints(JsonElement document)
    {
        if (!document.TryGetProperty("points", out JsonElement points)
            || points.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("A PolyLine is missing its 'points'.");
        }

        List<double> numbers = [];

        foreach (JsonElement number in points.EnumerateArray())
        {
            numbers.Add(ReadBareNumber(number, "points"));
        }

        if (numbers.Count % 3 != 0)
        {
            throw new JsonException(
                $"A PolyLine's 'points' holds {numbers.Count} numbers, which is not a whole "
                + "number of points.");
        }

        List<Point3d> result = new(numbers.Count / 3);

        for (int index = 0; index < numbers.Count; index += 3)
        {
            result.Add(new Point3d(numbers[index], numbers[index + 1], numbers[index + 2]));
        }

        return result;
    }

    private static void WritePoint(Utf8JsonWriter writer, string prefix, in Point3d point)
    {
        GeometryJsonFormat.WriteNumber(writer, prefix + "X", point.X);
        GeometryJsonFormat.WriteNumber(writer, prefix + "Y", point.Y);
        GeometryJsonFormat.WriteNumber(writer, prefix + "Z", point.Z);
    }

    private static void WritePlane(Utf8JsonWriter writer, in Plane plane)
    {
        WritePoint(writer, "origin", plane.Origin);
        GeometryJsonFormat.WriteNumber(writer, "xAxisX", plane.XAxis.X);
        GeometryJsonFormat.WriteNumber(writer, "xAxisY", plane.XAxis.Y);
        GeometryJsonFormat.WriteNumber(writer, "xAxisZ", plane.XAxis.Z);
        GeometryJsonFormat.WriteNumber(writer, "yAxisX", plane.YAxis.X);
        GeometryJsonFormat.WriteNumber(writer, "yAxisY", plane.YAxis.Y);
        GeometryJsonFormat.WriteNumber(writer, "yAxisZ", plane.YAxis.Z);
        GeometryJsonFormat.WriteNumber(writer, "normalX", plane.Normal.X);
        GeometryJsonFormat.WriteNumber(writer, "normalY", plane.Normal.Y);
        GeometryJsonFormat.WriteNumber(writer, "normalZ", plane.Normal.Z);
    }

    private static Plane ReadPlane(JsonElement document) => new(
        ReadPoint(document, "origin"),
        new Vector3d(Number(document, "xAxisX"), Number(document, "xAxisY"), Number(document, "xAxisZ")),
        new Vector3d(Number(document, "yAxisX"), Number(document, "yAxisY"), Number(document, "yAxisZ")),
        new Vector3d(Number(document, "normalX"), Number(document, "normalY"), Number(document, "normalZ")));

    private static Point3d ReadPoint(JsonElement document, string prefix) => new(
        Number(document, prefix + "X"),
        Number(document, prefix + "Y"),
        Number(document, prefix + "Z"));

    private static double Number(JsonElement document, string name) =>
        document.TryGetProperty(name, out JsonElement value)
            ? ReadBareNumber(value, name)
            : throw new JsonException($"A curve is missing its '{name}'.");

    private static void WriteBareNumber(Utf8JsonWriter writer, double value)
    {
        if (double.IsFinite(value))
        {
            writer.WriteNumberValue(value);
            return;
        }

        writer.WriteStringValue(double.IsNaN(value) ? "NaN" : value > 0.0 ? "Infinity" : "-Infinity");
    }

    private static double ReadBareNumber(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => value.GetString() switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            string other => throw new JsonException($"'{name}' holds '{other}', which is not a number."),
            null => throw new JsonException($"'{name}' holds a null rather than a number."),
        },
        _ => throw new JsonException($"'{name}' holds a {value.ValueKind} rather than a number."),
    };
}
