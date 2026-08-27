using System;
using Spark.Geometry;

namespace Spark.Geometry.Properties;

/// <summary>
/// A randomly drawn working scale plus the shapes needed to build geometry at that scale, and
/// the tolerances that are meaningful there.
/// </summary>
/// <param name="Scale">The working scale, from 1e-9 to 1e9.</param>
/// <param name="First">A shape in the unit cube, scaled up to give the first position.</param>
/// <param name="Second">A second shape in the unit cube.</param>
/// <param name="Axis">A unit direction.</param>
/// <param name="Turn">An arbitrary rotation angle.</param>
/// <param name="Factor">A scale factor between a quarter and four.</param>
/// <remarks>
/// A single fixed epsilon cannot work across nine decades either way: 1e-8 is absurdly tight
/// against coordinates of 1e9, where the spacing between adjacent doubles is already 2e-7, and
/// uselessly loose against coordinates of 1e-9, where it would pass any answer at all. Both
/// tolerances here are proportional to <see cref="Scale"/>, which is what makes the assertions
/// equally strict everywhere.
/// </remarks>
internal readonly record struct Scene(
    double Scale,
    Vector3d First,
    Vector3d Second,
    Vector3d Axis,
    Angle Turn,
    double Factor)
{
    /// <summary>The first position, at the working scale.</summary>
    public Point3d FirstPoint => (Point3d)(First * Scale);

    /// <summary>The second position, at the working scale.</summary>
    public Point3d SecondPoint => (Point3d)(Second * Scale);

    /// <summary>A plane through <see cref="FirstPoint"/> with an arbitrary normal.</summary>
    public Plane Plane => Plane.ByOriginNormal(FirstPoint, Axis);

    /// <summary>A right-handed frame at the working scale.</summary>
    public CoordinateSystem Frame => CoordinateSystem.ByPlane(Plane);

    /// <summary>In-plane coordinates at the working scale.</summary>
    public Point2d Planar => new(Second.X * Scale, Second.Y * Scale);

    /// <summary>
    /// An increasing interval at the working scale, whose length is between one hundredth of
    /// the scale and twice it. A domain many orders of magnitude shorter than its own distance
    /// from the origin cannot round-trip through normalisation in double precision at all, and
    /// generating such a thing would test arithmetic rather than the kernel.
    /// </summary>
    public Interval Domain
    {
        get
        {
            double min = First.X * Scale;

            return new Interval(min, min + ((Math.Abs(Second.X) + 0.01) * Scale));
        }
    }

    /// <summary>An invertible affine transform built at the working scale.</summary>
    public Transform Motion =>
        Transform.Translation(First * Scale)
        * Transform.Rotation(Axis, Turn)
        * Transform.Scale(Factor);

    /// <summary>
    /// The tolerance for comparing positions and lengths at this scale: a relative precision
    /// of about 1e-12, which is roughly ten thousand units in the last place and therefore
    /// tight enough to catch an algebra error while leaving room for honest rounding.
    /// </summary>
    public Tolerance PositionTolerance => new(1e-12 * Scale, Angle.FromDegrees(0.001), 1e-12);

    /// <summary>
    /// The tolerance for comparing matrix entries. A transform mixes dimensionless entries in
    /// its linear part with scaled entries in its translation column, so the threshold needs
    /// an absolute floor that does not vanish as the scale shrinks — otherwise a rotation
    /// entry off by 1e-16 would fail a test run at a scale of 1e-9.
    /// </summary>
    public Tolerance MatrixTolerance =>
        new(Math.Max(1e-12, 1e-12 * Scale), Angle.FromDegrees(0.001), 1e-12);
}
