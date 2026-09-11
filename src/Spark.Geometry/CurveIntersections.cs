using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>Where two curves meet at a point: the point, and the parameter on each (<c>E2-T11</c>).</summary>
/// <param name="Point">Where they meet.</param>
/// <param name="ParameterA">The parameter on the curve the question was asked of.</param>
/// <param name="ParameterB">The parameter on the other curve.</param>
public readonly record struct CurveIntersectionPoint(Point3d Point, double ParameterA, double ParameterB);

/// <summary>Where two curves run together: the stretch of each curve's domain they share (<c>E2-T11</c>).</summary>
/// <param name="OnA">The shared stretch, in the parameters of the curve the question was asked of.</param>
/// <param name="OnB">
/// The same stretch in the other curve's parameters - decreasing when the other curve runs the opposite
/// way, which is information rather than an error.
/// </param>
public readonly record struct CurveOverlap(Interval OnA, Interval OnB);

/// <summary>
/// Everything two curves have in common: the points where they cross or touch, and the stretches
/// where they run together (<c>E2-T11</c>).
/// </summary>
/// <remarks>
/// A class rather than a record, so that what it publishes is the two lists and nothing a record
/// would add - a copy constructor and an equality contract nobody asked for.
/// </remarks>
public sealed class CurveIntersections
{
    /// <summary>Creates the result.</summary>
    /// <param name="points">The points, in order along the curve the question was asked of.</param>
    /// <param name="overlaps">The stretches the two curves share.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public CurveIntersections(IReadOnlyList<CurveIntersectionPoint> points, IReadOnlyList<CurveOverlap> overlaps)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(overlaps);

        Points = points;
        Overlaps = overlaps;
    }

    /// <summary>Nothing in common.</summary>
    public static CurveIntersections None { get; } = new([], []);

    /// <summary>The points, in order along the curve the question was asked of.</summary>
    public IReadOnlyList<CurveIntersectionPoint> Points { get; }

    /// <summary>The stretches the two curves share.</summary>
    public IReadOnlyList<CurveOverlap> Overlaps { get; }

    /// <summary>Whether the curves have nothing in common.</summary>
    public bool IsEmpty => Points.Count == 0 && Overlaps.Count == 0;
}
