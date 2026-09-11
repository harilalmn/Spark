using System;
using System.Numerics;
using Spark.Geometry;
using Spark.Viewport;

namespace Spark.Viewport.Tests;

/// <summary>
/// Picking geometry under a pixel (`E9-T8`).
/// </summary>
/// <remarks>
/// <b>The first test is the one the rest depend on</b>: a point projected to a pixel with the
/// camera's own <see cref="Camera.ViewProjection"/> lies on the ray through that pixel. If the ray and
/// the drawing ever disagree, every other test here would still pass on its own numbers and the user
/// would click one thing and select another.
/// </remarks>
public sealed class ViewportPickerTests
{
    private static readonly GeometryKey Near = new("near", 0);
    private static readonly GeometryKey Far = new("far", 0);
    private static readonly GeometryKey Wire = new("wire", 0);

    private static Camera Looking()
    {
        Camera camera = new() { Target = Vector3.Zero, Distance = 20f };
        camera.SetViewportSize(800, 600);

        return camera;
    }

    /// <summary>The pixel a world point is drawn at, by the renderer's own matrix.</summary>
    private static (double X, double Y) Drawn(Camera camera, Vector3 world)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), camera.ViewProjection);
        double ndcX = clip.X / clip.W;
        double ndcY = clip.Y / clip.W;

        return ((ndcX + 1.0) * 0.5 * camera.ViewportWidth, (1.0 - ndcY) * 0.5 * camera.ViewportHeight);
    }

    /// <summary>A flat triangle around a centre, facing +Z.</summary>
    private static RenderPackage Triangle(GeometryKey key, Vector3 centre, float size = 1f) =>
        new(
            key,
            "0",
            [
                centre.X - size, centre.Y - size, centre.Z,
                centre.X + size, centre.Y - size, centre.Z,
                centre.X, centre.Y + size, centre.Z,
            ],
            [0, 0, 1, 0, 0, 1, 0, 0, 1],
            [0, 1, 2],
            [],
            Appearance.Default);

    private static double Distance(Ray ray, Vector3 point)
    {
        Point3d p = new(point.X, point.Y, point.Z);
        Vector3d d = ray.Direction.Normalised();
        double t = (p - ray.Origin).Dot(d);

        return p.DistanceTo(ray.Origin + (d * t));
    }

    /// <summary>
    /// <b>The ray and the drawing agree.</b> Points projected with <see cref="Camera.ViewProjection"/>
    /// lie on the rays through their own pixels, at the centre and out towards the corners.
    /// </summary>
    [Fact]
    public void APointLiesOnTheRayThroughThePixelItIsDrawnAt()
    {
        Camera camera = Looking();

        foreach (Vector3 point in new[] { Vector3.Zero, new(3, -2, 1), new(-5, 4, -2), new(6, 6, 0) })
        {
            (double x, double y) = Drawn(camera, point);
            Ray ray = camera.RayThrough(x, y);

            Assert.True(Distance(ray, point) < 1e-4, $"{point} is {Distance(ray, point)} off the ray through its own pixel");
        }
    }

    /// <summary>The centre pixel's ray points at the target.</summary>
    [Fact]
    public void TheCentreRayPointsAtTheTarget()
    {
        Camera camera = Looking();
        Ray ray = camera.RayThrough(400, 300);

        Assert.True(Distance(ray, camera.Target) < 1e-4);
    }

    /// <summary>
    /// <b>The row.</b> A pixel showing a triangle picks the package that drew it; a pixel showing
    /// nothing picks nothing.
    /// </summary>
    [Fact]
    public void APixelOnATrianglePicksItsPackageAndEmptySpacePicksNothing()
    {
        Camera camera = Looking();
        RenderPackage[] scene = [Triangle(Near, new Vector3(-4, 0, 0)), Triangle(Far, new Vector3(4, 0, 0))];

        (double x, double y) = Drawn(camera, new Vector3(-4, 0, 0));
        Assert.Equal(Near, ViewportPicker.Pick(camera, scene, x, y)?.Key);

        (x, y) = Drawn(camera, new Vector3(4, 0, 0));
        Assert.Equal(Far, ViewportPicker.Pick(camera, scene, x, y)?.Key);

        (x, y) = Drawn(camera, new Vector3(0, 8, 0));
        Assert.Null(ViewportPicker.Pick(camera, scene, x, y));
    }

    /// <summary>Where two triangles overlap on screen, the one nearer the eye wins.</summary>
    [Fact]
    public void TheNearerOfTwoOverlappingTrianglesWins()
    {
        Camera camera = Looking();
        Vector3 towardsEye = Vector3.Normalize(camera.Position - camera.Target) * 3f;

        RenderPackage[] scene =
        [
            Triangle(Far, Vector3.Zero, size: 2f),
            Triangle(Near, towardsEye, size: 2f),
        ];

        Assert.Equal(Near, ViewportPicker.Pick(camera, scene, 400, 300)?.Key);
    }

    /// <summary>
    /// A line is picked within a few pixels of where it is drawn, and not from across the screen.
    /// </summary>
    [Fact]
    public void ALineIsPickedWithinAFewPixels()
    {
        Camera camera = Looking();
        RenderPackage line = new(Wire, "0", [-5, 0, 0, 5, 0, 0], [], [], [0, 1], Appearance.Default);

        (double x, double y) = Drawn(camera, Vector3.Zero);

        Assert.Equal(Wire, ViewportPicker.Pick(camera, [line], x, y + 3)?.Key);
        Assert.Null(ViewportPicker.Pick(camera, [line], x, y + 40));
    }
}
