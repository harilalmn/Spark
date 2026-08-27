using Spark.UI.Canvas;

namespace Spark.UI.Tests;

/// <summary>
/// The single pan-and-zoom transform, and the level-of-detail thresholds it drives.
/// </summary>
public sealed class CanvasTransformTests
{
    [Fact]
    public void WorldAndScreenRoundTrip()
    {
        CanvasTransform transform = new() { Zoom = 0.375, OffsetX = -412.5, OffsetY = 91.25 };

        Assert.Equal(123.75, transform.ToWorldX(transform.ToScreenX(123.75)), 9);
        Assert.Equal(-40.5, transform.ToWorldY(transform.ToScreenY(-40.5)), 9);
    }

    [Fact]
    public void ZoomIsClampedToAUsableRange()
    {
        CanvasTransform transform = new() { Zoom = 1000 };
        Assert.Equal(CanvasTransform.MaximumZoom, transform.Zoom);

        transform.Zoom = 0.0001;
        Assert.Equal(CanvasTransform.MinimumZoom, transform.Zoom);
    }

    [Fact]
    public void ZoomingKeepsTheWorldPointUnderTheCursorFixed()
    {
        CanvasTransform transform = new() { Zoom = 1, OffsetX = 40, OffsetY = 90 };

        double worldXBefore = transform.ToWorldX(300);
        double worldYBefore = transform.ToWorldY(200);

        transform.ZoomAbout(1.15, 300, 200);

        // Anything else makes a wheel zoom feel like it is fighting the user.
        Assert.Equal(worldXBefore, transform.ToWorldX(300), 9);
        Assert.Equal(worldYBefore, transform.ToWorldY(200), 9);
    }

    [Fact]
    public void ZoomingAboutAPointStaysFixedEvenWhenTheZoomClamps()
    {
        CanvasTransform transform = new() { Zoom = CanvasTransform.MaximumZoom };
        double worldBefore = transform.ToWorldX(500);

        transform.ZoomAbout(4, 500, 500);

        Assert.Equal(worldBefore, transform.ToWorldX(500), 9);
    }

    [Fact]
    public void ANonPositiveZoomFactorIsIgnoredRatherThanCorruptingTheView()
    {
        CanvasTransform transform = new() { Zoom = 1, OffsetX = 10 };

        transform.ZoomAbout(0, 100, 100);
        transform.ZoomAbout(double.NaN, 100, 100);

        Assert.Equal(1, transform.Zoom);
        Assert.Equal(10, transform.OffsetX);
    }

    [Fact]
    public void PanningMovesTheViewByTheScreenDeltaDividedByZoom()
    {
        CanvasTransform transform = new() { Zoom = 2 };
        transform.PanByScreen(100, -50);

        Assert.Equal(-50, transform.OffsetX);
        Assert.Equal(25, transform.OffsetY);
    }

    [Fact]
    public void TheVisibleWorldMatchesTheControlSize()
    {
        CanvasTransform transform = new() { Zoom = 0.5, OffsetX = 100, OffsetY = 200 };
        CanvasBounds visible = transform.VisibleWorld(800, 600);

        Assert.Equal(100, visible.MinX);
        Assert.Equal(200, visible.MinY);
        Assert.Equal(1700, visible.MaxX);
        Assert.Equal(1400, visible.MaxY);
    }

    [Fact]
    public void FittingCentresTheContentAndLeavesAMargin()
    {
        CanvasTransform transform = new();
        CanvasBounds world = new(0, 0, 1000, 500);

        transform.FitTo(world, 900, 600, marginPixels: 50);

        CanvasBounds visible = transform.VisibleWorld(900, 600);

        Assert.True(visible.MinX < world.MinX);
        Assert.True(visible.MaxX > world.MaxX);
        Assert.True(visible.MinY < world.MinY);
        Assert.True(visible.MaxY > world.MaxY);

        Assert.Equal(500, (visible.MinX + visible.MaxX) / 2, 6);
        Assert.Equal(250, (visible.MinY + visible.MaxY) / 2, 6);
    }

    [Fact]
    public void FittingADegenerateRectangleDoesNotDivideByZero()
    {
        CanvasTransform transform = new();
        transform.FitTo(new CanvasBounds(5, 5, 5, 5), 800, 600);

        Assert.True(double.IsFinite(transform.Zoom));
        Assert.True(double.IsFinite(transform.OffsetX));
        Assert.Equal(CanvasTransform.MaximumZoom, transform.Zoom);
    }

    [Fact]
    public void BoundsContainmentAndIntersectionIncludeTheEdges()
    {
        CanvasBounds bounds = CanvasBounds.FromSize(10, 20, 30, 40);

        Assert.Equal(30, bounds.Width);
        Assert.Equal(40, bounds.Height);
        Assert.True(bounds.Contains(10, 20));
        Assert.True(bounds.Contains(40, 60));
        Assert.False(bounds.Contains(41, 60));
        Assert.True(bounds.Intersects(CanvasBounds.FromSize(40, 60, 5, 5)));
        Assert.False(bounds.Intersects(CanvasBounds.FromSize(41, 61, 5, 5)));
    }
}
