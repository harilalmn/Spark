using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make surfaces.
/// </summary>
/// <remarks>
/// <b>The parameterisations are the kernel's, not renormalised to [0, 1].</b> A node library that
/// re-scaled every domain would be a second parameterisation to reconcile with the first the moment
/// a user put a code block next to a surface node — which is exactly the sort of seam a graph tool
/// should not have. Where a normalised parameter is genuinely the friendlier thing to type, the
/// node takes a fraction and says so in its parameter name.
/// </remarks>
[SparkNode(Category = NodeCategories.Solid)]
public static class Surface
{
    /// <summary>Makes a rectangular piece of a plane, centred on the plane's origin.</summary>
    /// <param name="plane">The plane it lies in, and its centre.</param>
    /// <param name="width">Its extent along the plane's x-axis.</param>
    /// <param name="height">Its extent along the plane's y-axis.</param>
    /// <returns>The surface.</returns>
    [return: NodePort("surface")]
    public static PlaneSurface ByPlaneSize(Spark.Geometry.Plane plane, double width = 1, double height = 1) =>
        PlaneSurface.ByPlaneSize(plane, width, height);

    /// <summary>Makes a whole sphere.</summary>
    /// <param name="centre">The centre.</param>
    /// <param name="radius">The radius.</param>
    /// <returns>The surface.</returns>
    [return: NodePort("surface")]
    public static SphericalSurface Sphere(Point3d centre, double radius = 1) =>
        new(Spark.Geometry.Plane.ByOriginNormal(centre, Vector3d.ZAxis), radius);

    /// <summary>Makes a cylinder standing on a plane.</summary>
    /// <param name="plane">The base: its origin is on the axis and its normal is the axis.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="height">How far it extends along the axis.</param>
    /// <returns>The surface.</returns>
    [return: NodePort("surface")]
    public static CylindricalSurface Cylinder(Spark.Geometry.Plane plane, double radius = 1, double height = 1) =>
        new(plane, radius, new Interval(0.0, height));

    /// <summary>Makes a cone standing on a plane.</summary>
    /// <param name="plane">The base: its origin is on the axis and its normal is the axis.</param>
    /// <param name="baseRadius">The radius where the cone meets the plane.</param>
    /// <param name="topRadius">The radius at the far end. Zero gives a point.</param>
    /// <param name="height">How far it extends along the axis.</param>
    /// <returns>The surface.</returns>
    /// <remarks>
    /// <b>Two radii rather than an angle</b>, because that is what somebody modelling a cone
    /// actually has: the kernel's half-angle is the honest internal parameterisation and a
    /// truncated cone described by its two ends is what a user types.
    /// </remarks>
    [return: NodePort("surface")]
    public static ConicalSurface Cone(Spark.Geometry.Plane plane, double baseRadius = 1, double topRadius = 0, double height = 1) =>
        new(
            plane,
            baseRadius,
            Angle.FromRadians(System.Math.Atan2(topRadius - baseRadius, height)),
            new Interval(0.0, height));

    /// <summary>Makes a torus lying in a plane.</summary>
    /// <param name="plane">The centre, and the plane the tube's centreline lies in.</param>
    /// <param name="majorRadius">From the axis to the centre of the tube.</param>
    /// <param name="minorRadius">The radius of the tube.</param>
    /// <returns>The surface.</returns>
    [return: NodePort("surface")]
    public static ToroidalSurface Torus(Spark.Geometry.Plane plane, double majorRadius = 2, double minorRadius = 0.5) =>
        new(plane, majorRadius, minorRadius);

    /// <summary>Sweeps a curve along a straight direction.</summary>
    /// <param name="curve">The profile to sweep.</param>
    /// <param name="direction">Which way and how far to sweep it.</param>
    /// <returns>The surface.</returns>
    [return: NodePort("surface")]
    public static ExtrusionSurface Extrude(Spark.Geometry.Curve curve, Vector3d direction) =>
        new(curve, direction);

    /// <summary>Turns a curve about an axis.</summary>
    /// <param name="curve">The profile to revolve.</param>
    /// <param name="axisOrigin">A point on the axis.</param>
    /// <param name="axisDirection">The axis direction.</param>
    /// <param name="sweepAngle">How far to turn.</param>
    /// <returns>The surface.</returns>
    [return: NodePort("surface")]
    public static RevolutionSurface Revolve(
        Spark.Geometry.Curve curve, Point3d axisOrigin, Vector3d axisDirection, Angle sweepAngle) =>
        new(curve, axisOrigin, axisDirection, new Interval(0.0, sweepAngle.Radians));

    /// <summary>Rules a straight line between two curves.</summary>
    /// <param name="first">The curve at one edge.</param>
    /// <param name="second">The curve at the other.</param>
    /// <returns>The surface.</returns>
    [return: NodePort("surface")]
    public static RuledSurface Loft(Spark.Geometry.Curve first, Spark.Geometry.Curve second) =>
        new(first, second);

    /// <summary>The point on a surface at a pair of parameters, as fractions of its domains.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="u">A fraction of the way along the first direction.</param>
    /// <param name="v">A fraction of the way along the second.</param>
    /// <returns>The point.</returns>
    [return: NodePort("point")]
    public static Point3d PointAtParameter(Spark.Geometry.Surface surface, double u = 0.5, double v = 0.5) =>
        surface.PointAt(surface.DomainU.Denormalise(u), surface.DomainV.Denormalise(v));

    /// <summary>The unit normal of a surface at a pair of parameters.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="u">A fraction of the way along the first direction.</param>
    /// <param name="v">A fraction of the way along the second.</param>
    /// <returns>The normal.</returns>
    [return: NodePort("normal")]
    public static Vector3d NormalAtParameter(Spark.Geometry.Surface surface, double u = 0.5, double v = 0.5) =>
        surface.NormalAt(surface.DomainU.Denormalise(u), surface.DomainV.Denormalise(v));

    /// <summary>The area of a surface.</summary>
    /// <param name="surface">The surface.</param>
    /// <returns>The area.</returns>
    [return: NodePort("area")]
    public static double Area(Spark.Geometry.Surface surface) => surface.Area;

    /// <summary>The closest point on a surface to another point.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="point">The point to measure from.</param>
    /// <returns>The closest point on the surface.</returns>
    [return: NodePort("point")]
    public static Point3d ClosestPoint(Spark.Geometry.Surface surface, Point3d point) =>
        surface.ClosestPoint(point, out _, out _);

    /// <summary>The curve across a surface at a fixed parameter in the other direction.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="v">A fraction of the way along the second direction.</param>
    /// <returns>The iso-curve.</returns>
    [return: NodePort("curve")]
    public static Spark.Geometry.Curve IsoCurve(Spark.Geometry.Surface surface, double v = 0.5) =>
        surface.IsoCurveU(surface.DomainV.Denormalise(v));

    /// <summary>Turns a surface into a mesh to a tolerance.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="tolerance">The largest distance the mesh may stray from the surface.</param>
    /// <returns>The mesh.</returns>
    [return: NodePort("mesh")]
    public static Mesh ToMesh(Spark.Geometry.Surface surface, double tolerance = 0.01) =>
        surface.ToMesh(new Tolerance(tolerance, Angle.FromDegrees(1), 1e-12));

    /// <summary>The exact NURBS surface a sphere is.</summary>
    /// <param name="surface">The sphere.</param>
    /// <returns>A rational NURBS surface tracing the same sheet.</returns>
    /// <remarks>
    /// Exact rather than fitted: a sphere is a rational quadric. What is <i>not</i> preserved is the
    /// parameterisation — a rational quadratic's parameter is a projective function of the angle —
    /// so the corners line up and the interior does not.
    /// </remarks>
    [return: NodePort("surface")]
    public static NurbsSurface ToNurbs(SphericalSurface surface) => surface.ToNurbsSurface();
}
