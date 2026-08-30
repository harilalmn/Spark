using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Spark.Geometry;

/// <summary>
/// Reads and writes geometry as JSON, one self-describing value at a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every value carries its own type and its own version.</b> The envelope is
/// <c>{"type": "Circle", "version": 1, ...}</c>, and a nested value carries the same two fields,
/// so a document is readable without a schema and **each type versions on its own timetable** —
/// which is the requirement `E2-T29` states as *a `NurbsCurve` at v2 and a `Mesh` at v1 must
/// coexist*. A single document-wide version number cannot express that, and adding one later
/// would be a breaking change to every file already written.
/// </para>
/// <para>
/// <b>Reading a version this build does not know is an error, not a guess.</b>
/// <see cref="Deserialize(string)"/> throws <see cref="NotSupportedException"/> naming the type
/// and the version, because the alternative — ignoring fields it does not recognise — turns a
/// file written by a newer Spark into silently wrong geometry.
/// </para>
/// <para>
/// <b>Non-finite numbers are written as strings</b>, <c>"NaN"</c>, <c>"Infinity"</c> and
/// <c>"-Infinity"</c>. JSON has no way to write them as numbers, and they are not exotic here:
/// <see cref="BoundingBox.Empty"/> is built from infinities and is a value a caller can
/// legitimately hold. Refusing to write it would mean a legal value that cannot be saved.
/// </para>
/// <para>
/// <b>What is deliberately not here.</b> The compact binary form for bulk data is `E2-T30` and a
/// separate decision — JSON for a large mesh is roughly thirty times the size. The serializer is
/// also **hand-written rather than source-generated**, which departs from `E2-T29` as originally
/// worded: with an explicit converter per type the generator's job is already done by hand, and
/// what source generation buys beyond that is trimming and AOT support, which
/// [ADR-0020](../../docs/adr/0020-occt-via-c-abi-shim.md) has ruled out for the shipping
/// application anyway. If trimming ever comes back, so does this decision.
/// </para>
/// </remarks>
public static class GeometryJson
{
    /// <summary>
    /// The version written for every type this build produces. It is written **per value**
    /// rather than per document, so it is the version of *that type's* wire shape.
    /// </summary>
    public const int CurrentVersion = 1;

    private const string TypeField = "type";
    private const string VersionField = "version";

    /// <summary>
    /// Writes a geometry value as JSON.
    /// </summary>
    /// <param name="value">The value. Any public geometry type is accepted.</param>
    /// <param name="indented">Whether to write human-readable JSON.</param>
    /// <returns>The JSON text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    /// <exception cref="NotSupportedException">
    /// Thrown for a type this serializer does not know. A new geometry type that has not been
    /// added here fails a test rather than silently losing data — see `E2-T31`.
    /// </exception>
    public static string Serialize(object value, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(value);

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = indented }))
        {
            Write(writer, value);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Reads a geometry value from JSON.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The value, as the concrete type its envelope names.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is null.</exception>
    /// <exception cref="JsonException">Thrown when the text is not valid JSON.</exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when the envelope names a type this build does not know, or a version it cannot
    /// read.
    /// </exception>
    public static object Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using JsonDocument document = JsonDocument.Parse(json);

        return Read(document.RootElement);
    }

    /// <summary>
    /// Reads a geometry value from JSON and checks that it is the expected type.
    /// </summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="json">The JSON text.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the document holds some other kind of geometry. That is a different failure
    /// from a corrupt file and says so.
    /// </exception>
    public static T Deserialize<T>(string json)
    {
        object value = Deserialize(json);

        if (value is not T typed)
        {
            throw new InvalidOperationException(
                $"The document holds a {value.GetType().Name}, not a {typeof(T).Name}.");
        }

        return typed;
    }

    private static void Write(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case Point2d v: Open(writer, nameof(Point2d)); Number(writer, "x", v.X); Number(writer, "y", v.Y); break;
            case Vector2d v: Open(writer, nameof(Vector2d)); Number(writer, "x", v.X); Number(writer, "y", v.Y); break;
            case UV v: Open(writer, nameof(UV)); Number(writer, "u", v.U); Number(writer, "v", v.V); break;
            case Point3d v: Open(writer, nameof(Point3d)); Number(writer, "x", v.X); Number(writer, "y", v.Y); Number(writer, "z", v.Z); break;
            case Vector3d v: Open(writer, nameof(Vector3d)); Number(writer, "x", v.X); Number(writer, "y", v.Y); Number(writer, "z", v.Z); break;
            case Quaternion v:
                Open(writer, nameof(Quaternion));
                Number(writer, "x", v.X);
                Number(writer, "y", v.Y);
                Number(writer, "z", v.Z);
                Number(writer, "w", v.W);
                break;

            case Angle v: Open(writer, nameof(Angle)); Number(writer, "radians", v.Radians); break;
            case Interval v: Open(writer, nameof(Interval)); Number(writer, "min", v.Min); Number(writer, "max", v.Max); break;

            case Tolerance v:
                Open(writer, nameof(Tolerance));
                Number(writer, "linear", v.Linear);
                Number(writer, "angular", v.Angular.Radians);
                Number(writer, "relativeEpsilon", v.RelativeEpsilon);
                break;

            case BoundingBox v:
                Open(writer, nameof(BoundingBox));
                Member(writer, "min", v.Min);
                Member(writer, "max", v.Max);
                break;

            case Plane v:
                Open(writer, nameof(Plane));
                Member(writer, "origin", v.Origin);
                Member(writer, "xAxis", v.XAxis);
                Member(writer, "yAxis", v.YAxis);
                break;

            case CoordinateSystem v:
                Open(writer, nameof(CoordinateSystem));
                Member(writer, "origin", v.Origin);
                Member(writer, "xAxis", v.XAxis);
                Member(writer, "yAxis", v.YAxis);
                break;

            case Ray v:
                Open(writer, nameof(Ray));
                Member(writer, "origin", v.Origin);
                Member(writer, "direction", v.Direction);
                break;

            case Transform v:
                Open(writer, nameof(Transform));
                writer.WriteStartArray("m");
                foreach (double element in Elements(v))
                {
                    Number(writer, element);
                }

                writer.WriteEndArray();
                break;

            case Line v:
                Open(writer, nameof(Line));
                Member(writer, "start", v.StartPoint);
                Member(writer, "end", v.EndPoint);
                break;

            case Circle v:
                Open(writer, nameof(Circle));
                Member(writer, "plane", v.Plane);
                Number(writer, "radius", v.Radius);
                break;

            case Arc v:
                Open(writer, nameof(Arc));
                Member(writer, "plane", v.Plane);
                Number(writer, "radius", v.Radius);
                Number(writer, "startAngle", v.StartAngle.Radians);
                Number(writer, "sweepAngle", v.SweepAngle.Radians);
                break;

            case EllipseCurve v:
                Open(writer, nameof(EllipseCurve));
                Member(writer, "plane", v.Plane);
                Number(writer, "xRadius", v.XRadius);
                Number(writer, "yRadius", v.YRadius);
                Number(writer, "startAngle", v.StartAngle.Radians);
                Number(writer, "sweepAngle", v.SweepAngle.Radians);
                break;

            // Surfaces. Each writes what its constructor takes and nothing derived, which is what
            // makes a round trip a fact about the arguments rather than about the maths.
            case PlaneSurface v:
                Open(writer, nameof(PlaneSurface));
                Member(writer, "plane", v.Plane);
                Member(writer, "domainU", v.DomainU);
                Member(writer, "domainV", v.DomainV);
                break;

            case SphericalSurface v:
                Open(writer, nameof(SphericalSurface));
                Member(writer, "frame", v.Frame);
                Number(writer, "radius", v.Radius);
                Member(writer, "domainU", v.DomainU);
                Member(writer, "domainV", v.DomainV);
                break;

            case CylindricalSurface v:
                Open(writer, nameof(CylindricalSurface));
                Member(writer, "frame", v.Frame);
                Number(writer, "radius", v.Radius);
                Member(writer, "domainU", v.DomainU);
                Member(writer, "domainV", v.DomainV);
                break;

            case ConicalSurface v:
                Open(writer, nameof(ConicalSurface));
                Member(writer, "frame", v.Frame);
                Number(writer, "radius", v.Radius);
                Number(writer, "halfAngle", v.HalfAngle.Radians);
                Member(writer, "domainU", v.DomainU);
                Member(writer, "domainV", v.DomainV);
                break;

            case ToroidalSurface v:
                Open(writer, nameof(ToroidalSurface));
                Member(writer, "frame", v.Frame);
                Number(writer, "majorRadius", v.MajorRadius);
                Number(writer, "minorRadius", v.MinorRadius);
                Member(writer, "domainU", v.DomainU);
                Member(writer, "domainV", v.DomainV);
                break;

            case ExtrusionSurface v:
                Open(writer, nameof(ExtrusionSurface));
                Member(writer, "profile", v.Profile);
                Member(writer, "direction", v.Direction);
                Member(writer, "height", v.DomainV);
                break;

            case RevolutionSurface v:
                Open(writer, nameof(RevolutionSurface));
                Member(writer, "profile", v.Profile);
                Member(writer, "axisOrigin", v.AxisOrigin);
                Member(writer, "axis", v.Axis);
                Member(writer, "sweep", v.DomainU);
                break;

            case RuledSurface v:
                Open(writer, nameof(RuledSurface));
                Member(writer, "first", v.First);
                Member(writer, "second", v.Second);
                break;

            case NurbsSurface v:
                Open(writer, nameof(NurbsSurface));
                Member(writer, "knotsU", v.KnotsU);
                Member(writer, "knotsV", v.KnotsV);
                writer.WriteNumber("countU", v.ControlPointCountU);
                writer.WriteNumber("countV", v.ControlPointCountV);
                writer.WriteStartArray("controlPoints");

                // Flattened row-major, u outermost, which is the same order the net is indexed in.
                // Writing a nested array would look tidier and would let a hand-edited file get the
                // two dimensions out of step with `countU` and `countV`, which nothing could catch.
                for (int i = 0; i < v.ControlPointCountU; i++)
                {
                    for (int j = 0; j < v.ControlPointCountV; j++)
                    {
                        Write(writer, v.ControlPoint(i, j));
                    }
                }

                writer.WriteEndArray();

                if (v.IsRational)
                {
                    writer.WriteStartArray("weights");

                    for (int i = 0; i < v.ControlPointCountU; i++)
                    {
                        for (int j = 0; j < v.ControlPointCountV; j++)
                        {
                            writer.WriteNumberValue(v.Weight(i, j));
                        }
                    }

                    writer.WriteEndArray();
                }

                break;

            case KnotVector v:
                Open(writer, nameof(KnotVector));
                writer.WriteNumber("degree", v.Degree);
                writer.WriteStartArray("knots");
                foreach (double knot in v.ToArray())
                {
                    writer.WriteNumberValue(knot);
                }

                writer.WriteEndArray();
                break;

            case NurbsCurve v:
                Open(writer, nameof(NurbsCurve));
                Member(writer, "knots", v.Knots);
                writer.WriteStartArray("controlPoints");
                foreach (Point3d point in v.ControlPoints())
                {
                    Write(writer, point);
                }

                writer.WriteEndArray();

                // Written always, even when every weight is one. A curve is defined by its
                // weights, and omitting them when they happen to be uniform would make the file's
                // shape depend on the curve's values - which is the kind of conditional that reads
                // as a saving and costs a reader an hour.
                writer.WriteStartArray("weights");
                foreach (double weight in v.Weights())
                {
                    writer.WriteNumberValue(weight);
                }

                writer.WriteEndArray();
                break;

            case PolyLine v:
                Open(writer, nameof(PolyLine));
                writer.WriteStartArray("points");
                foreach (Point3d point in v.Points())
                {
                    Write(writer, point);
                }

                writer.WriteEndArray();
                break;

            case PolyCurve v:
                Open(writer, nameof(PolyCurve));
                writer.WriteStartArray("segments");
                foreach (Curve segment in v.Segments())
                {
                    Write(writer, segment);
                }

                writer.WriteEndArray();
                break;

            default:
                throw new NotSupportedException(
                    $"{value.GetType().Name} has no JSON form. Add one to GeometryJson: a public "
                    + "geometry type that cannot be written is a type whose values cannot be saved.");
        }

        writer.WriteEndObject();
    }

    private static object Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(TypeField, out JsonElement typeElement)
            || typeElement.GetString() is not string type)
        {
            throw new NotSupportedException("A geometry document must be an object naming its type.");
        }

        int version = element.TryGetProperty(VersionField, out JsonElement versionElement)
            ? versionElement.GetInt32()
            : 0;

        if (version != CurrentVersion)
        {
            throw new NotSupportedException(
                $"This build reads {type} at version {CurrentVersion} and the document says "
                + $"version {version}. A file written by a newer Spark is not read approximately.");
        }

        return type switch
        {
            nameof(Point2d) => new Point2d(Number(element, "x"), Number(element, "y")),
            nameof(Vector2d) => new Vector2d(Number(element, "x"), Number(element, "y")),
            nameof(UV) => new UV(Number(element, "u"), Number(element, "v")),
            nameof(Point3d) => new Point3d(Number(element, "x"), Number(element, "y"), Number(element, "z")),
            nameof(Vector3d) => new Vector3d(Number(element, "x"), Number(element, "y"), Number(element, "z")),
            nameof(Quaternion) => new Quaternion(
                Number(element, "x"), Number(element, "y"), Number(element, "z"), Number(element, "w")),
            nameof(Angle) => Angle.FromRadians(Number(element, "radians")),
            nameof(Interval) => new Interval(Number(element, "min"), Number(element, "max")),
            nameof(Tolerance) => new Tolerance(
                Number(element, "linear"),
                Angle.FromRadians(Number(element, "angular")),
                Number(element, "relativeEpsilon")),
            nameof(BoundingBox) => BoundingBox.FromSortedCorners(Point(element, "min"), Point(element, "max")),
            nameof(Plane) => Spark.Geometry.Plane.ByOriginXAxisYAxis(
                Point(element, "origin"), Vector(element, "xAxis"), Vector(element, "yAxis")),
            nameof(CoordinateSystem) => CoordinateSystem.ByOriginXAxisYAxis(
                Point(element, "origin"), Vector(element, "xAxis"), Vector(element, "yAxis")),
            nameof(Ray) => new Ray(Point(element, "origin"), Vector(element, "direction")),
            nameof(Transform) => ReadTransform(element),
            nameof(Line) => new Line(Point(element, "start"), Point(element, "end")),
            nameof(Circle) => Circle.ByPlaneRadius(ReadPlane(element, "plane"), Number(element, "radius")),
            nameof(Arc) => Arc.ByPlaneRadiusAngles(
                ReadPlane(element, "plane"),
                Number(element, "radius"),
                Angle.FromRadians(Number(element, "startAngle")),
                Angle.FromRadians(Number(element, "sweepAngle"))),
            nameof(EllipseCurve) => EllipseCurve.ByPlaneRadiiAngles(
                ReadPlane(element, "plane"),
                Number(element, "xRadius"),
                Number(element, "yRadius"),
                Angle.FromRadians(Number(element, "startAngle")),
                Angle.FromRadians(Number(element, "sweepAngle"))),
            nameof(PlaneSurface) => new PlaneSurface(
                ReadPlane(element, "plane"), ReadInterval(element, "domainU"), ReadInterval(element, "domainV")),
            nameof(SphericalSurface) => new SphericalSurface(
                ReadPlane(element, "frame"),
                Number(element, "radius"),
                ReadInterval(element, "domainU"),
                ReadInterval(element, "domainV")),
            nameof(CylindricalSurface) => new CylindricalSurface(
                ReadPlane(element, "frame"),
                Number(element, "radius"),
                ReadInterval(element, "domainU"),
                ReadInterval(element, "domainV")),
            nameof(ConicalSurface) => new ConicalSurface(
                ReadPlane(element, "frame"),
                Number(element, "radius"),
                Angle.FromRadians(Number(element, "halfAngle")),
                ReadInterval(element, "domainU"),
                ReadInterval(element, "domainV")),
            nameof(ToroidalSurface) => new ToroidalSurface(
                ReadPlane(element, "frame"),
                Number(element, "majorRadius"),
                Number(element, "minorRadius"),
                ReadInterval(element, "domainU"),
                ReadInterval(element, "domainV")),
            nameof(ExtrusionSurface) => new ExtrusionSurface(
                ReadCurve(element, "profile"),
                Vector(element, "direction"),
                ReadInterval(element, "height")),
            nameof(RevolutionSurface) => new RevolutionSurface(
                ReadCurve(element, "profile"),
                Point(element, "axisOrigin"),
                Vector(element, "axis"),
                ReadInterval(element, "sweep")),
            nameof(RuledSurface) => new RuledSurface(
                ReadCurve(element, "first"), ReadCurve(element, "second")),
            nameof(NurbsSurface) => ReadNurbsSurface(element),
            nameof(KnotVector) => ReadKnotVector(element),
            nameof(NurbsCurve) => ReadNurbsCurve(element),
            nameof(PolyLine) => ReadPolyLine(element),
            nameof(PolyCurve) => ReadPolyCurve(element),
            _ => throw new NotSupportedException(
                $"'{type}' is not a geometry type this build knows how to read."),
        };
    }

    private static Transform ReadTransform(JsonElement element)
    {
        JsonElement array = element.GetProperty("m");
        Span<double> m = stackalloc double[16];
        int index = 0;

        foreach (JsonElement value in array.EnumerateArray())
        {
            if (index == 16)
            {
                throw new NotSupportedException("A Transform has exactly sixteen elements.");
            }

            m[index++] = Number(value);
        }

        if (index != 16)
        {
            throw new NotSupportedException("A Transform has exactly sixteen elements.");
        }

        return new Transform(
            m[0], m[1], m[2], m[3],
            m[4], m[5], m[6], m[7],
            m[8], m[9], m[10], m[11],
            m[12], m[13], m[14], m[15]);
    }

    /// <summary>
    /// Reads a knot vector, which re-checks every invariant on the way in.
    /// </summary>
    /// <remarks>
    /// The constructor is the only way to make one, so a hand-edited or corrupted file cannot
    /// produce a vector that a curve would then evaluate into nonsense — it produces an exception
    /// naming what is wrong with it instead.
    /// </remarks>
    private static KnotVector ReadKnotVector(JsonElement element)
    {
        List<double> knots = [];

        foreach (JsonElement knot in element.GetProperty("knots").EnumerateArray())
        {
            knots.Add(knot.GetDouble());
        }

        return new KnotVector(element.GetProperty("degree").GetInt32(), knots);
    }

    private static NurbsCurve ReadNurbsCurve(JsonElement element)
    {
        List<Point3d> points = [];
        foreach (JsonElement point in element.GetProperty("controlPoints").EnumerateArray())
        {
            points.Add((Point3d)Read(point));
        }

        List<double> weights = [];
        foreach (JsonElement weight in element.GetProperty("weights").EnumerateArray())
        {
            weights.Add(weight.GetDouble());
        }

        return new NurbsCurve(
            points, (KnotVector)Read(element.GetProperty("knots")), weights);
    }

    private static PolyLine ReadPolyLine(JsonElement element)
    {
        List<Point3d> points = [];

        foreach (JsonElement point in element.GetProperty("points").EnumerateArray())
        {
            points.Add((Point3d)Read(point));
        }

        return new PolyLine(points);
    }

    private static PolyCurve ReadPolyCurve(JsonElement element)
    {
        List<Curve> segments = [];

        foreach (JsonElement segment in element.GetProperty("segments").EnumerateArray())
        {
            segments.Add((Curve)Read(segment));
        }

        return PolyCurve.ByJoinedCurves(segments);
    }

    private static IEnumerable<double> Elements(Transform t)
    {
        yield return t.M00;
        yield return t.M01;
        yield return t.M02;
        yield return t.M03;
        yield return t.M10;
        yield return t.M11;
        yield return t.M12;
        yield return t.M13;
        yield return t.M20;
        yield return t.M21;
        yield return t.M22;
        yield return t.M23;
        yield return t.M30;
        yield return t.M31;
        yield return t.M32;
        yield return t.M33;
    }

    private static void Open(Utf8JsonWriter writer, string type)
    {
        writer.WriteStartObject();
        writer.WriteString(TypeField, type);
        writer.WriteNumber(VersionField, CurrentVersion);
    }

    private static void Member(Utf8JsonWriter writer, string name, object value)
    {
        writer.WritePropertyName(name);
        Write(writer, value);
    }

    private static void Number(Utf8JsonWriter writer, string name, double value)
    {
        if (double.IsFinite(value))
        {
            writer.WriteNumber(name, value);
        }
        else
        {
            writer.WriteString(name, value.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    private static void Number(Utf8JsonWriter writer, double value)
    {
        if (double.IsFinite(value))
        {
            writer.WriteNumberValue(value);
        }
        else
        {
            writer.WriteStringValue(value.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    private static double Number(JsonElement element, string name) => Number(element.GetProperty(name));

    private static double Number(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.String => double.Parse(
            element.GetString() ?? string.Empty,
            NumberStyles.Float,
            CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException("A geometry number must be a number or a named literal."),
    };

    private static Point3d Point(JsonElement element, string name) => (Point3d)Read(element.GetProperty(name));

    private static Vector3d Vector(JsonElement element, string name) => (Vector3d)Read(element.GetProperty(name));

    private static Plane ReadPlane(JsonElement element, string name) => (Plane)Read(element.GetProperty(name));

    private static NurbsSurface ReadNurbsSurface(JsonElement element)
    {
        KnotVector knotsU = (KnotVector)Read(element.GetProperty("knotsU"));
        KnotVector knotsV = (KnotVector)Read(element.GetProperty("knotsV"));

        int countU = element.GetProperty("countU").GetInt32();
        int countV = element.GetProperty("countV").GetInt32();

        JsonElement points = element.GetProperty("controlPoints");
        Point3d[,] net = new Point3d[countU, countV];

        for (int i = 0; i < countU; i++)
        {
            for (int j = 0; j < countV; j++)
            {
                net[i, j] = (Point3d)Read(points[(i * countV) + j]);
            }
        }

        if (!element.TryGetProperty("weights", out JsonElement weightElements))
        {
            return new NurbsSurface(knotsU, knotsV, net);
        }

        double[,] weights = new double[countU, countV];

        for (int i = 0; i < countU; i++)
        {
            for (int j = 0; j < countV; j++)
            {
                weights[i, j] = weightElements[(i * countV) + j].GetDouble();
            }
        }

        return new NurbsSurface(knotsU, knotsV, net, weights);
    }

    private static Interval ReadInterval(JsonElement element, string name) =>
        (Interval)Read(element.GetProperty(name));

    private static Curve ReadCurve(JsonElement element, string name) =>
        (Curve)Read(element.GetProperty(name));
}
