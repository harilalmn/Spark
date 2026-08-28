using System;
using System.Collections.Generic;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Engine;

/// <summary>
/// Estimates how many bytes a graph value occupies, so that the evaluation cache can hold a
/// memory budget rather than a count of entries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every number here is an estimate, and the three things it cannot see are named rather
/// than left to be discovered.</b> A budget built on a measurement nobody has stated the limits
/// of is worse than a count, because a count is obviously crude and an estimate looks precise.
/// </para>
/// <para>
/// <b>It cannot see native memory.</b> A value holding a handle into another process's or
/// another allocator's heap reports the size of the handle.
/// <see href="https://github.com/harilalmn/Spark/blob/main/docs/adr/0021-brep-kernel-residency.md">ADR-0021</see>
/// is explicit that a provider must <i>report</i> its own native budget rather than have one
/// inferred, and nothing here infers one: an unrecognised object is charged a flat overhead and
/// no more. When a `Brep` arrives carrying an OCCT handle, this estimator must be extended to
/// ask it, not guessed at.
/// </para>
/// <para>
/// <b>It cannot see sharing.</b> A list holding the same curve a thousand times is charged for
/// a thousand curves. Following references to find out would need identity tracking across the
/// whole cache, and the error is in the safe direction — the cache believes it holds more than
/// it does, and evicts sooner.
/// </para>
/// <para>
/// <b>It does not walk a curve's tessellation.</b> A curve is charged for the state it stores,
/// not for anything it computes and remembers on demand. That is the same direction of error as
/// the last one is not: a curve that has been tessellated finely holds more than this reports.
/// It is accepted because the alternative — forcing a tessellation in order to measure one — is
/// a cache that costs more than the computation it is saving.
/// </para>
/// </remarks>
public static class GraphValueSize
{
    /// <summary>
    /// What an object costs before anything it holds: the CLR's object header, method-table
    /// pointer and the reference pointing at it, on a 64-bit runtime.
    /// </summary>
    public const int ObjectOverhead = 24;

    /// <summary>
    /// What a value of a type this estimator does not recognise is charged.
    /// </summary>
    /// <remarks>
    /// Deliberately small, and deliberately not zero. Zero would let a graph full of unknown
    /// values fill memory while the cache reported nothing; a large guess would evict real
    /// results to make room for an imagined cost. A node library whose values dominate a
    /// document should be taught to this estimator rather than absorbed by this constant.
    /// </remarks>
    public const int UnknownValue = 64;

    /// <summary>
    /// Estimates the bytes a value occupies.
    /// </summary>
    /// <param name="value">The value. May be <see langword="null"/>.</param>
    /// <returns>An estimate in bytes, never negative.</returns>
    public static long Estimate(object? value) => Estimate(value, depth: 0);

    /// <summary>
    /// Estimates the bytes a cached result occupies, outputs and diagnostics together.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>An estimate in bytes, never negative.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="result"/> is <see langword="null"/>.
    /// </exception>
    public static long Estimate(CachedResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        long total = ObjectOverhead;

        foreach (object? output in result.Outputs)
        {
            total += Estimate(output);
        }

        foreach (SparkDiagnostic diagnostic in result.Diagnostics)
        {
            // A diagnostic is a handful of short strings. Charging them the same as any other
            // string keeps a graph that produces thousands of warnings from looking free.
            total += ObjectOverhead
                + Text(diagnostic.Code)
                + Text(diagnostic.Message)
                + Text(diagnostic.Detail)
                + Text(diagnostic.HelpTopicId);
        }

        return total;
    }

    private static long Estimate(object? value, int depth)
    {
        // A list nested deeper than this is not a value anybody built on purpose, and the
        // recursion has to stop somewhere that is not the stack.
        const int MaximumDepth = 64;

        if (depth > MaximumDepth)
        {
            return UnknownValue;
        }

        return value switch
        {
            null => 0,

            // The struct sizes are the sums of their fields: three doubles for a Point3d, four
            // for a Quaternion, sixteen for a Transform. They are written out rather than taken
            // from Marshal.SizeOf, which reports the UNMANAGED layout and is a different number.
            double or long or int or bool or float or char or short or byte => 8,
            Point2d or Vector2d or UV => 16,
            Angle => 8,
            Interval => 16,
            Point3d or Vector3d => 24,
            Quaternion => 32,
            Tolerance => 24,
            BoundingBox or Ray => 48,
            Plane => 96,
            CoordinateSystem => 96,
            Transform => 128,

            string text => Text(text),

            Displayable displayable => ObjectOverhead + 16 + Estimate(displayable.Geometry, depth + 1),

            SparkList list => EstimateList(list, depth),

            Curve curve => Estimate(curve),

            _ => UnknownValue,
        };
    }

    private static long EstimateList(SparkList list, int depth)
    {
        // The array of references, plus the objects behind them. A list of a million doubles is
        // 8 MB of boxes and 8 MB of pointers, and both halves are real.
        long total = ObjectOverhead + (8L * list.Count);

        foreach (object? item in list)
        {
            total += Estimate(item, depth + 1);
        }

        return total;
    }

    private static long Estimate(Curve curve) => curve switch
    {
        // What each type STORES, not what it can produce. A polyline is its points; everything
        // else here is a frame and a few numbers.
        PolyLine polyLine => ObjectOverhead + (24L * (polyLine.SegmentCount + 1)),

        PolyCurve polyCurve => EstimatePolyCurve(polyCurve),

        _ => ObjectOverhead + 128,
    };

    private static long EstimatePolyCurve(PolyCurve polyCurve)
    {
        long total = ObjectOverhead + (8L * polyCurve.SegmentCount);

        for (int index = 0; index < polyCurve.SegmentCount; index++)
        {
            total += Estimate(polyCurve.SegmentAt(index));
        }

        return total;
    }

    private static long Text(string? value) =>
        value is null ? 0 : ObjectOverhead + (2L * value.Length);
}
