using System;
using CsCheck;
using Spark.Geometry;

namespace Spark.Geometry.Properties;

/// <summary>
/// Shared CsCheck generators for the value layer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scale is the point.</b> ADR-0018 names coordinates at 1e-9 and 1e9 as the range the
/// kernel has to survive, and ADR-0010 claims property tests at extreme scale as a benefit of
/// making tolerance a parameter. Generators confined to [-100, 100] deliver neither, and they
/// mask real failures: the interval round-trip below genuinely loses six significant figures
/// at an origin of 1e10, and nothing narrow enough to stay near the origin will ever see it.
/// Every generator here therefore draws a <see cref="Scales"/> value spanning nine decades
/// either side of one, and builds its geometry at that scale.
/// </para>
/// <para>
/// <b>Degeneracy is not the point.</b> Zero-length axes, unset points and NaN coordinates are
/// covered by the example-based tests, where the expected behaviour can be stated exactly. A
/// property that spends half its samples in a guard clause is testing the guard.
/// </para>
/// <para>
/// Because a meaningful tolerance depends on the working scale, the generators hand back a
/// <see cref="Scene"/> that carries its own scale, and assertions use
/// <see cref="Scene.PositionTolerance"/> or <see cref="Scene.MatrixTolerance"/> rather than a
/// single fixed epsilon that would be absurdly tight at 1e9 and uselessly loose at 1e-9.
/// </para>
/// </remarks>
internal static class GeometryGenerators
{
    /// <summary>
    /// A dimensionless tolerance, for quantities that are ratios or direction cosines and so
    /// carry no scale of their own.
    /// </summary>
    public static readonly Tolerance Dimensionless = new(1e-12, Angle.FromDegrees(0.001), 1e-12);

    /// <summary>
    /// Working scales from 1e-9 to 1e9, log-uniform so that every decade is equally likely
    /// rather than the largest decade taking almost every sample.
    /// </summary>
    public static readonly Gen<double> Scales = Gen.Double[-9.0, 9.0].Select(exponent => Math.Pow(10.0, exponent));

    /// <summary>
    /// Uniformly distributed unit vectors, built from an even distribution over the height of
    /// a cylinder and an angle around it — Archimedes' theorem. Rejection sampling on a cube
    /// would bias towards the corners.
    /// </summary>
    public static readonly Gen<Vector3d> UnitVectors = Gen.Select(
        Gen.Double[-1.0, 1.0],
        Gen.Double[0.0, 2.0 * Math.PI],
        (height, around) =>
        {
            double radius = Math.Sqrt(Math.Max(0.0, 1.0 - (height * height)));

            return new Vector3d(radius * Math.Cos(around), radius * Math.Sin(around), height);
        });

    public static readonly Gen<Angle> Angles = Gen.Double[-720.0, 720.0].Select(Angle.FromDegrees);

    /// <summary>
    /// Coordinates spanning the full range of working scales, in both signs.
    /// </summary>
    public static readonly Gen<double> Coordinate =
        Gen.Select(Scales, Gen.Double[-1.0, 1.0], (scale, fraction) => scale * fraction);

    /// <summary>
    /// Points spanning the full range of working scales. A single point's three coordinates
    /// share a scale, which is what real models look like — a model is not usually a metre
    /// wide and a nanometre tall.
    /// </summary>
    public static readonly Gen<Point3d> Points = Gen.Select(
        Scales,
        UnitCube,
        (scale, unit) => (Point3d)(unit * scale));

    public static readonly Gen<Vector3d> Vectors = Gen.Select(
        Scales,
        UnitVectors,
        Gen.Double[0.01, 1.0],
        (scale, direction, fraction) => direction * (scale * fraction));

    public static readonly Gen<BoundingBox> Boxes =
        Gen.Select(Points, Points, (corner, opposite) => new BoundingBox(corner, opposite));

    /// <summary>
    /// A working scale together with the raw draws needed to build geometry at it. Everything
    /// a property needs comes off one <see cref="Scene"/> so that the plane, the points, the
    /// interval and the transform all share a scale — comparing a plane at 1e9 against a
    /// planar coordinate at 1e-9 tests nothing but the loss of the smaller one.
    /// </summary>
    public static readonly Gen<Scene> Scenes = Gen.Select(
        Scales,
        UnitCube,
        UnitCube,
        UnitVectors,
        Angles,
        Gen.Double[0.25, 4.0],
        (scale, first, second, axis, turn, factor) => new Scene(scale, first, second, axis, turn, factor));

    /// <summary>
    /// Points inside the unit cube, used as the shape of a value before a scale is applied.
    /// </summary>
    private static Gen<Vector3d> UnitCube => Gen.Select(
        Gen.Double[-1.0, 1.0],
        Gen.Double[-1.0, 1.0],
        Gen.Double[-1.0, 1.0],
        (x, y, z) => new Vector3d(x, y, z));
}
