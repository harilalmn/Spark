using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="NurbsSurface.InterpolatePoints"/> and
/// <see cref="Surface.ApproximateWithTolerance"/> — `E2-T66`'s fourth item, two rows and one
/// algorithm.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is where the deviation is measured.</b> An interpolating surface is exact at the
/// points it was built from, so a deviation measured there is zero for every grid however coarse,
/// and a fit that never refined would report a perfect result. Every claim here about closeness is
/// therefore asserted <i>between</i> the samples, and independently of the number the member
/// returns.
/// </para>
/// <para>
/// <b>The interpolation's own defining property is passing through every point</b>, which is what
/// the word means rather than a property of this implementation, so it is asserted over grids of
/// several shapes and degrees.
/// </para>
/// </remarks>
public sealed class SurfaceApproximationTests
{
    private const double Tight = 1e-7;

    /// <summary><b>The defining property.</b> The surface passes through every point of the grid.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(3, 1)]
    [InlineData(2, 4)]
    public void TheSurfacePassesThroughEveryGridPoint(int degreeU, int degreeV)
    {
        Point3d[,] grid = IrregularGrid(6, 7);

        NurbsSurface surface = NurbsSurface.InterpolatePoints(grid, degreeU, degreeV);

        for (int i = 0; i < grid.GetLength(0); i++)
        {
            for (int j = 0; j < grid.GetLength(1); j++)
            {
                Point3d expected = grid[i, j];
                Point3d nearest = surface.ClosestPoint(expected, out _, out _);

                Assert.True(
                    expected.DistanceTo(nearest) < Tight,
                    $"degrees {degreeU}x{degreeV}: the surface misses ({i}, {j}) by {expected.DistanceTo(nearest)}.");
            }
        }
    }

    /// <summary>The four corners are exact, which everything that joins surfaces depends on.</summary>
    [Fact]
    public void TheCornersAreExact()
    {
        Point3d[,] grid = IrregularGrid(5, 4);

        NurbsSurface surface = NurbsSurface.InterpolatePoints(grid);

        Assert.True(surface.PointAt(surface.DomainU.Min, surface.DomainV.Min).DistanceTo(grid[0, 0]) < Tight);
        Assert.True(surface.PointAt(surface.DomainU.Max, surface.DomainV.Min).DistanceTo(grid[4, 0]) < Tight);
        Assert.True(surface.PointAt(surface.DomainU.Min, surface.DomainV.Max).DistanceTo(grid[0, 3]) < Tight);
        Assert.True(surface.PointAt(surface.DomainU.Max, surface.DomainV.Max).DistanceTo(grid[4, 3]) < Tight);
    }

    /// <summary>
    /// <b>Degree 1 each way is the bilinear mesh through the grid</b> — the one case with an answer
    /// known independently of the solver, which is what makes it worth having.
    /// </summary>
    [Fact]
    public void DegreeOneBothWaysIsTheBilinearMesh()
    {
        Point3d[,] grid = IrregularGrid(4, 3);

        NurbsSurface surface = NurbsSurface.InterpolatePoints(grid, 1, 1);

        // A degree-1 tensor product has the grid itself as its control net.
        Point3d[,] control = surface.ControlPoints();

        Assert.Equal(4, control.GetLength(0));
        Assert.Equal(3, control.GetLength(1));

        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                Assert.True(control[i, j].DistanceTo(grid[i, j]) < Tight, $"control point ({i}, {j}) moved.");
            }
        }
    }

    /// <summary>The result is non-rational, as every interpolation in the kernel is.</summary>
    [Fact]
    public void AnInterpolatedSurfaceIsNotRational()
    {
        Assert.False(NurbsSurface.InterpolatePoints(IrregularGrid(5, 5)).IsRational);
    }

    [Fact]
    public void TooSmallAGridOrTooHighADegreeIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => NurbsSurface.InterpolatePoints(null!));
        Assert.Throws<ArgumentException>(() => NurbsSurface.InterpolatePoints(new Point3d[1, 4]));
        Assert.Throws<ArgumentOutOfRangeException>(() => NurbsSurface.InterpolatePoints(IrregularGrid(4, 4), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NurbsSurface.InterpolatePoints(IrregularGrid(4, 4), 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => NurbsSurface.InterpolatePoints(IrregularGrid(4, 4), 3, 4));
    }

    /// <summary>
    /// <b>A sphere is the case with an independent answer.</b> Approximating one and measuring every
    /// sample's distance from the centre is a check the fitting code does not supply.
    /// </summary>
    [Fact]
    public void ApproximatingASphereGivesPointsAtTheRadius()
    {
        const double radius = 3.0;
        SphericalSurface sphere = new(Plane.WorldXY, radius, new Interval(0.2, 2.6), new Interval(-1.0, 1.0));

        (NurbsSurface surface, double deviation, bool fits) =
            sphere.ApproximateWithTolerance(new Tolerance(1e-4, Angle.FromDegrees(0.01), 1e-12));

        Assert.True(fits, $"the sphere was not fitted: the deviation was {deviation}.");

        for (int i = 0; i <= 12; i++)
        {
            for (int j = 0; j <= 12; j++)
            {
                double u = surface.DomainU.Denormalise(i / 12.0);
                double v = surface.DomainV.Denormalise(j / 12.0);

                Assert.Equal(radius, surface.PointAt(u, v).DistanceTo(Point3d.Origin), 1e-3);
            }
        }
    }

    /// <summary>
    /// <b>The returned deviation is the deviation achieved</b>, checked against an independent
    /// measurement taken between the samples. A fit that reported a number it had not measured
    /// would pass every test that only asked whether the surface was close.
    /// </summary>
    [Fact]
    public void TheReportedDeviationMatchesAnIndependentMeasurement()
    {
        ToroidalSurface torus = new(Plane.WorldXY, 5.0, 1.5);

        (NurbsSurface surface, double deviation, _) =
            torus.ApproximateWithTolerance(new Tolerance(1e-3, Angle.FromDegrees(0.01), 1e-12));

        double measured = 0.0;
        for (int i = 0; i <= 60; i++)
        {
            for (int j = 0; j <= 60; j++)
            {
                double u = torus.DomainU.Denormalise(i / 60.0);
                double v = torus.DomainV.Denormalise(j / 60.0);

                measured = Math.Max(
                    measured,
                    torus.PointAt(u, v).DistanceTo(surface.PointAt(
                        surface.DomainU.Denormalise(i / 60.0), surface.DomainV.Denormalise(j / 60.0))));
            }
        }

        Assert.True(
            measured <= deviation * 4.0 + 1e-9,
            $"the reported deviation {deviation} is far below the measured {measured}.");
        Assert.True(deviation > 0.0, "an approximation of a torus is not exact, and should not report zero.");
    }

    /// <summary>
    /// <b>Tightening the tolerance tightens the surface.</b> A fit that ignored its tolerance
    /// argument would fail this and would pass every test that only asked whether the result was
    /// close.
    /// </summary>
    [Fact]
    public void TighteningTheToleranceTightensTheSurface()
    {
        ToroidalSurface torus = new(Plane.WorldXY, 5.0, 1.5);

        (_, double coarse, _) = torus.ApproximateWithTolerance(new Tolerance(1e-1, Angle.FromDegrees(1.0), 1e-12));
        (_, double fine, _) = torus.ApproximateWithTolerance(new Tolerance(1e-5, Angle.FromDegrees(0.001), 1e-12));

        Assert.True(fine < coarse, $"tightening the tolerance made it worse: {coarse} then {fine}.");
    }

    /// <summary>
    /// <b>An offset surface is why this member exists.</b> It has no exact NURBS form and refuses to
    /// pretend; this is how a caller gets an inexact one deliberately, and it works.
    /// </summary>
    [Fact]
    public void AnOffsetSurfaceCanBeApproximatedEvenThoughItCannotBeConverted()
    {
        ConicalSurface cone = new(Plane.WorldXY, 1.0, Angle.FromRadians(0.3), new Interval(0.0, 4.0));
        Surface offset = cone.Offset(0.5);

        Assert.Throws<NotSupportedException>(() => offset.ToNurbsSurface());

        (NurbsSurface surface, double deviation, bool fits) =
            offset.ApproximateWithTolerance(new Tolerance(1e-4, Angle.FromDegrees(0.01), 1e-12));

        Assert.True(fits, $"the offset cone was not fitted: the deviation was {deviation}.");

        for (int i = 1; i < 8; i++)
        {
            for (int j = 1; j < 8; j++)
            {
                double u = offset.DomainU.Denormalise(i / 8.0);
                double v = offset.DomainV.Denormalise(j / 8.0);

                Assert.True(
                    offset.PointAt(u, v).DistanceTo(
                        surface.PointAt(surface.DomainU.Denormalise(i / 8.0), surface.DomainV.Denormalise(j / 8.0)))
                    < 1e-3,
                    $"the approximation left the offset at ({u}, {v}).");
            }
        }
    }

    /// <summary>
    /// A plane is exactly representable, so its approximation is exact and the deviation is zero —
    /// which is the sanity check that the measurement is not simply reporting a number.
    /// </summary>
    [Fact]
    public void APlaneApproximatesExactly()
    {
        PlaneSurface plane = new(Plane.WorldXY, new Interval(0, 3), new Interval(-1, 2));

        (_, double deviation, bool fits) = plane.ApproximateWithTolerance();

        Assert.True(fits);
        Assert.Equal(0.0, deviation, 1e-12);
    }

    [Fact]
    public void ADegreeBelowOneIsRefused()
    {
        PlaneSurface plane = new(Plane.WorldXY, new Interval(0, 3), new Interval(-1, 2));

        Assert.Throws<ArgumentOutOfRangeException>(() => plane.ApproximateWithTolerance(default, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => plane.ApproximateWithTolerance(default, 3, 0));
    }

    /// <summary>
    /// A grid with a collapsed row — the pole of a sphere — interpolates rather than being refused,
    /// because that is the shape a caller sampling a surface of revolution actually has.
    /// </summary>
    [Fact]
    public void AGridWithACollapsedRowIsAccepted()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 2.0);

        (NurbsSurface surface, _, _) = sphere.ApproximateWithTolerance(new Tolerance(1e-2, Angle.FromDegrees(0.1), 1e-12));

        // Away from the poles the fit should be good; the poles themselves are degenerate on the
        // original too.
        for (int i = 1; i < 8; i++)
        {
            for (int j = 2; j < 7; j++)
            {
                double u = surface.DomainU.Denormalise(i / 8.0);
                double v = surface.DomainV.Denormalise(j / 8.0);

                Assert.Equal(2.0, surface.PointAt(u, v).DistanceTo(Point3d.Origin), 1e-1);
            }
        }
    }

    /// <summary>
    /// A grid whose rows have different chord lengths, so that averaging the parameters across the
    /// grid matters: a surface built from one row's parameterisation misses the others.
    /// </summary>
    private static Point3d[,] IrregularGrid(int rows, int columns)
    {
        Point3d[,] grid = new Point3d[rows, columns];

        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                // The x spacing depends on j and the y spacing on i, so no two rows share a
                // chord-length parameterisation.
                double x = (i * (1.0 + (0.35 * j))) + (0.1 * j);
                double y = (j * (1.0 + (0.25 * i))) - (0.2 * i);
                double z = Math.Sin(0.7 * i) * Math.Cos(0.5 * j);

                grid[i, j] = new Point3d(x, y, z);
            }
        }

        return grid;
    }
}
