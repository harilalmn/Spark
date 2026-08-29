using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spark.Geometry;

/// <summary>
/// Reads and writes geometry as JSON. Version 1 of Spark's own geometry format.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every geometry object carries its own <c>$v</c>, and that is the point of the format
/// rather than an overhead of it.</b> A <c>NurbsCurve</c> at version 2 and a <c>Mesh</c> at
/// version 1 have to coexist in one file, because they will be written by different releases
/// and read by one. A single document-level version cannot express that: it forces every type
/// to move together, so adding a field to one of them invalidates files that never contained
/// it. The cost is a handful of bytes per object in a text format that is already the wrong
/// choice for bulk data — which is exactly why a compact binary form is a separate task rather
/// than an argument for compromising this one.
/// </para>
/// <para>
/// <b>A type that inlines another type's data owns that data's layout.</b> A
/// <see cref="PolyLine"/> writes its points as one flat array of numbers rather than as an
/// array of versioned point objects, because a thousand copies of <c>"$v":1</c> is noise and
/// because the polyline is what a reader has to understand in order to read them. The rule
/// that follows is the one to remember: <b>if the inlined layout changes, the inlining type's
/// version changes</b>, whatever happens to the inlined type's own.
/// </para>
/// <para>
/// <b>Round-tripping is byte-identical, and the format is designed backwards from that.</b>
/// Write, read, and write again, and the two documents are the same bytes — which is why a
/// <see cref="Plane"/> stores all four of its vectors rather than the two that generate them.
/// Re-deriving a frame on read would re-orthonormalise it, moving the last bit of an axis that
/// was already unit length, and a file whose diff is floating-point noise is a file nobody can
/// review. The reflection-driven round-trip test in the suite is what keeps this true for
/// every type rather than for the ones somebody remembered.
/// </para>
/// <para>
/// <b>Non-finite numbers are written as strings, because the kernel has values that need
/// them.</b> <see cref="Point3d.Unset"/> is <see cref="double.NaN"/> in every component and
/// <see cref="BoundingBox.Empty"/> is built from infinities; both are ordinary values here and
/// neither has a JSON numeric literal. They are written as <c>"NaN"</c>, <c>"Infinity"</c> and
/// <c>"-Infinity"</c> — the spelling the rest of the JSON ecosystem settled on — and read back
/// from either a number or one of those strings.
/// </para>
/// <para>
/// <b>The converters are written by hand rather than source-generated, and that is a decision
/// rather than an omission.</b> Every type here is an immutable <c>readonly struct</c> or a
/// sealed immutable class with no parameterless constructor and no settable property, so
/// source generation would need a mutable data-transfer object per type and a mapping in each
/// direction — the same amount of code, plus a second definition of what each type is, which
/// is the thing that drifts. Hand-written converters use no reflection at all, so they are
/// trim-safe and AOT-safe outright rather than by attribute.
/// </para>
/// </remarks>
public static class GeometryJson
{
    /// <summary>
    /// The version this release writes for every type. Readers accept this and anything below
    /// it, and refuse anything above with a message naming both numbers.
    /// </summary>
    public const int SchemaVersion = 1;

    // One instance of each converter, looked up by type. A dictionary rather than a
    // JsonSerializerOptions because the serializer's own entry points need a type-info
    // resolver, and the only ones available are reflection-based or source-generated - the
    // first defeats the trim-safety this file claims, and the second needs the very
    // data-transfer objects hand-written converters exist to avoid. Driving the converters
    // directly is a dozen lines and owes nothing to either.
    private static readonly Dictionary<Type, object> Converters = new()
    {
        [typeof(Angle)] = new AngleJsonConverter(),
        [typeof(Interval)] = new IntervalJsonConverter(),
        [typeof(Tolerance)] = new ToleranceJsonConverter(),
        [typeof(Point2d)] = new Point2dJsonConverter(),
        [typeof(Point3d)] = new Point3dJsonConverter(),
        [typeof(Vector2d)] = new Vector2dJsonConverter(),
        [typeof(Vector3d)] = new Vector3dJsonConverter(),
        [typeof(UV)] = new UVJsonConverter(),
        [typeof(Quaternion)] = new QuaternionJsonConverter(),
        [typeof(BoundingBox)] = new BoundingBoxJsonConverter(),
        [typeof(Plane)] = new PlaneJsonConverter(),
        [typeof(CoordinateSystem)] = new CoordinateSystemJsonConverter(),
        [typeof(Transform)] = new TransformJsonConverter(),
        [typeof(Ray)] = new RayJsonConverter(),
    };

    private static readonly CurveJsonConverter Curves = new();

    private static readonly JsonSerializerOptions Unused = new();

    /// <summary>
    /// Writes a geometry value as JSON.
    /// </summary>
    /// <typeparam name="T">
    /// The type to write. May be <see cref="Curve"/> or any curve type, in which case the
    /// concrete type is recorded in the document and recovered on reading.
    /// </typeparam>
    /// <param name="value">The value.</param>
    /// <param name="indented">
    /// Whether to lay the document out over several lines. Off by default: the format's
    /// purpose is interchange and storage, and a human reading one is the exception.
    /// </param>
    /// <returns>The JSON.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when <typeparamref name="T"/> is not a geometry type this release writes. The
    /// message names it, so the failure is a sentence rather than a stack trace.
    /// </exception>
    public static string Write<T>(T value, bool indented = false)
    {
        using MemoryStream stream = new();

        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = indented }))
        {
            if (value is Curve curve)
            {
                Curves.Write(writer, curve, Unused);
            }
            else
            {
                ConverterFor<T>().Write(writer, value, Unused);
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Reads a geometry value from JSON.
    /// </summary>
    /// <typeparam name="T">
    /// The type to read. May be <see cref="Curve"/>, in which case the concrete type comes
    /// from the document, or a concrete curve type, in which case a document naming a
    /// different one is an error rather than a silent <see langword="null"/>.
    /// </typeparam>
    /// <param name="json">The JSON.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="json"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when <typeparamref name="T"/> is not a geometry type this release reads.
    /// </exception>
    /// <exception cref="JsonException">
    /// Thrown when the document is malformed, is missing a field the type requires, carries a
    /// <c>$v</c> this release does not understand, or holds a different type from the one
    /// asked for.
    /// </exception>
    public static T Read<T>(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));

        if (!reader.Read())
        {
            throw new JsonException("The document is empty.");
        }

        if (typeof(Curve).IsAssignableFrom(typeof(T)))
        {
            Curve curve = Curves.Read(ref reader, typeof(Curve), Unused);

            return curve is T typed
                ? typed
                : throw new JsonException(
                    $"This document holds a {curve.GetType().Name}, and a {typeof(T).Name} was asked for.");
        }

        // The bang is safe and the reason is structural: every converter in the table returns a
        // struct, and the only nullable case - a curve - is handled above.
        return ConverterFor<T>().Read(ref reader, typeof(T), Unused)!;
    }

    private static JsonConverter<T> ConverterFor<T>() =>
        Converters.TryGetValue(typeof(T), out object? converter)
            ? (JsonConverter<T>)converter
            : throw new NotSupportedException(
                $"{typeof(T).Name} is not part of the geometry format. Adding a geometry type "
                + "means adding a converter here; the reflection-driven round-trip test in the "
                + "suite is what makes forgetting a red build rather than a surprise in "
                + "somebody's file.");
}
