using System;
using System.Numerics;
using Spark.Viewport;

namespace Spark.Viewport.Tests;

/// <summary>
/// The camera is where a handedness mistake hides. Geometry rendered through a left-handed camera
/// looks entirely correct until someone notices it is mirrored, which is the worst possible way to
/// find out — so the convention is asserted rather than assumed.
/// </summary>
public sealed class CameraTests
{
    [Fact]
    public void WorldUpIsPositiveZMatchingTheKernel()
    {
        Assert.Equal(Vector3.UnitZ, Camera.WorldUp);

        // The kernel's statement of right-handedness, restated as an assertion the viewport
        // shares: X crossed with Y gives +Z.
        Assert.Equal(Vector3.UnitZ, Vector3.Cross(Vector3.UnitX, Vector3.UnitY));
    }

    [Fact]
    public void TheCameraLooksDownItsOwnNegativeZ()
    {
        Camera camera = new() { Target = Vector3.Zero, Distance = 10 };
        camera.SetViewportSize(800, 600);

        // A point one unit in front of the eye must land at a negative view-space z. That is the
        // definition of a right-handed view matrix, and it is the half of the convention that
        // System.Numerics' CreateLookAt supplies.
        Vector3 inFront = camera.Position + Vector3.Normalize(camera.Target - camera.Position);
        Vector3 viewSpace = Vector3.Transform(inFront, camera.View);

        Assert.True(viewSpace.Z < 0, $"Expected a negative view-space z, got {viewSpace.Z}.");
    }

    [Fact]
    public void TheTargetProjectsToTheCentreOfTheViewport()
    {
        Camera camera = new() { Target = new Vector3(3, -4, 2), Distance = 25 };
        camera.SetViewportSize(1000, 500);

        Assert.True(camera.TryWorldToScreen(camera.Target, out Vector2 screen));
        Assert.Equal(500, screen.X, 3);
        Assert.Equal(250, screen.Y, 3);
    }

    [Fact]
    public void APointBehindTheEyeHasNoScreenPosition()
    {
        Camera camera = new() { Target = Vector3.Zero, Distance = 10 };
        camera.SetViewportSize(800, 600);

        Vector3 behind = camera.Position + ((camera.Position - camera.Target) * 2);

        Assert.False(camera.TryWorldToScreen(behind, out _));
    }

    [Fact]
    public void ScreenYIncreasesDownwards()
    {
        Camera camera = new() { Target = Vector3.Zero, Distance = 10, Elevation = 0, Azimuth = 0 };
        camera.SetViewportSize(800, 600);

        // With the camera on the +X axis looking back at the origin, a point higher in the world
        // must appear higher on screen, which means a SMALLER y in the top-left origin the whole
        // UI stack uses.
        Assert.True(camera.TryWorldToScreen(new Vector3(0, 0, 1), out Vector2 high));
        Assert.True(camera.TryWorldToScreen(new Vector3(0, 0, -1), out Vector2 low));

        Assert.True(high.Y < low.Y, $"Expected {high.Y} to be above {low.Y}.");
    }

    [Fact]
    public void ElevationIsClampedShortOfThePole()
    {
        Camera camera = new();

        camera.Elevation = 3f;
        Assert.True(camera.Elevation < MathF.PI / 2);

        camera.Elevation = -3f;
        Assert.True(camera.Elevation > -MathF.PI / 2);

        // The clamp exists so the view direction never becomes parallel to the up vector. If it
        // did, CreateLookAt would produce a matrix full of NaN and the whole scene would vanish.
        camera.SetViewportSize(800, 600);
        Matrix4x4 view = camera.View;
        Assert.False(float.IsNaN(view.M11));
    }

    [Fact]
    public void DollyIsMultiplicativeAndReversible()
    {
        Camera camera = new() { Distance = 20 };

        camera.Dolly(3);
        Assert.True(camera.Distance < 20);

        camera.Dolly(-3);
        Assert.Equal(20, camera.Distance, 3);
    }

    [Fact]
    public void ZoomToFitPutsEveryCornerOfTheBoxOnScreen()
    {
        Bounds3 bounds = Bounds3.Empty
            .Union(new Vector3(-4, -1, 0))
            .Union(new Vector3(6, 9, 3));

        Camera camera = new();
        camera.SetViewportSize(1200, 400);
        camera.ZoomToFit(bounds);

        Assert.Equal(bounds.Centre, camera.Target);

        foreach (Vector3 corner in Corners(bounds))
        {
            Assert.True(camera.TryWorldToScreen(corner, out Vector2 screen), "A corner fell behind the eye.");
            Assert.InRange(screen.X, 0, 1200);
            Assert.InRange(screen.Y, 0, 400);
        }
    }

    [Fact]
    public void ZoomToFitOnAWideViewportStillFitsHorizontally()
    {
        // The horizontal field of view is the tighter of the two on a short, wide viewport, so
        // fitting only the vertical angle would crop a wide model. This is the case that catches
        // it.
        Bounds3 bounds = Bounds3.Empty.Union(new Vector3(-20, -20, 0)).Union(new Vector3(20, 20, 1));

        Camera camera = new();
        camera.SetViewportSize(400, 1200);
        camera.ZoomToFit(bounds);

        foreach (Vector3 corner in Corners(bounds))
        {
            Assert.True(camera.TryWorldToScreen(corner, out Vector2 screen));
            Assert.InRange(screen.X, 0, 400);
        }
    }

    [Fact]
    public void ZoomToFitOnAnEmptyBoxChangesNothing()
    {
        Camera camera = new() { Distance = 7, Target = new Vector3(1, 2, 3) };
        camera.ZoomToFit(Bounds3.Empty);

        Assert.Equal(7, camera.Distance);
        Assert.Equal(new Vector3(1, 2, 3), camera.Target);
    }

    [Fact]
    public void PanMovesTheTargetPerpendicularToTheViewDirection()
    {
        Camera camera = new() { Target = Vector3.Zero, Distance = 30 };
        camera.SetViewportSize(900, 600);

        Vector3 before = camera.Target;
        Vector3 viewDirection = Vector3.Normalize(camera.Target - camera.Position);
        camera.Pan(40, -25);

        Vector3 movement = camera.Target - before;

        Assert.True(movement.Length() > 0);
        Assert.Equal(0, Vector3.Dot(Vector3.Normalize(movement), viewDirection), 4);
    }

    [Fact]
    public void PanScaleTracksDistanceSoDraggingFeelsTheSameAtEveryZoom()
    {
        Camera near = new() { Distance = 5 };
        Camera far = new() { Distance = 50 };
        near.SetViewportSize(800, 600);
        far.SetViewportSize(800, 600);

        near.Pan(100, 0);
        far.Pan(100, 0);

        Assert.Equal(10, far.Target.Length() / near.Target.Length(), 3);
    }

    [Fact]
    public void OrbitFollowsTheKernelsRotationSign()
    {
        Camera camera = new() { Azimuth = 0, Elevation = 0 };

        camera.Orbit(100, 0);

        // Dragging right swings the camera clockwise about +Z as seen from above, which is a
        // decreasing azimuth under the kernel's counter-clockwise-positive convention.
        Assert.True(camera.Azimuth < 0);
    }

    [Fact]
    public void NearAndFarTrackDistanceRatherThanBeingFixed()
    {
        Camera close = new() { Distance = 0.5f };
        Camera distant = new() { Distance = 500f };

        Assert.True(close.NearPlane < distant.NearPlane);
        Assert.True(close.FarPlane < distant.FarPlane);
        Assert.True(close.NearPlane > 0);

        // A fixed near plane is the most common cause of depth fighting in a viewport that has to
        // show a 2 mm fillet and a 200 m building; the ratio is what depth precision depends on.
        Assert.InRange(distant.FarPlane / distant.NearPlane, 1, 100_000);
    }

    [Fact]
    public void ViewportSizeNeverBecomesZero()
    {
        Camera camera = new();
        camera.SetViewportSize(0, -4);

        Assert.Equal(1, camera.ViewportWidth);
        Assert.Equal(1, camera.ViewportHeight);
        Assert.Equal(1, camera.AspectRatio);
    }

    private static Vector3[] Corners(Bounds3 bounds) =>
    [
        new(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
        new(bounds.Max.X, bounds.Min.Y, bounds.Min.Z),
        new(bounds.Min.X, bounds.Max.Y, bounds.Min.Z),
        new(bounds.Max.X, bounds.Max.Y, bounds.Min.Z),
        new(bounds.Min.X, bounds.Min.Y, bounds.Max.Z),
        new(bounds.Max.X, bounds.Min.Y, bounds.Max.Z),
        new(bounds.Min.X, bounds.Max.Y, bounds.Max.Z),
        new(bounds.Max.X, bounds.Max.Y, bounds.Max.Z),
    ];
}
