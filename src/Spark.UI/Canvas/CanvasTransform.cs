using System;

namespace Spark.UI.Canvas;

/// <summary>
/// The canvas's single pan-and-zoom transform. Pan and zoom are one transform applied to the
/// whole drawing, never per-node layout (ADR-0013): moving two thousand nodes is moving one
/// matrix.
/// </summary>
/// <remarks>
/// <para>
/// This is a mutable class rather than a struct because it is the canvas's viewport state and is
/// read and written from one place; the arithmetic on it is pure and is tested without a UI.
/// </para>
/// <para>
/// There is no smoothing, no inertia and no easing here, and there must not be. The design
/// language calls pan and zoom <c>motion.instant</c> and says so for a reason: nothing makes a
/// canvas feel worse than a canvas that keeps moving after you stop.
/// </para>
/// </remarks>
public sealed class CanvasTransform
{
    /// <summary>
    /// The smallest zoom the canvas allows. Below this a two-thousand-node graph is a few hundred
    /// pixels across and there is nothing left to see.
    /// </summary>
    public const double MinimumZoom = 0.05;

    /// <summary>The largest zoom the canvas allows.</summary>
    public const double MaximumZoom = 4.0;

    private double _zoom = 1.0;

    /// <summary>The world x coordinate that sits at the left edge of the control.</summary>
    public double OffsetX { get; set; }

    /// <summary>The world y coordinate that sits at the top edge of the control.</summary>
    public double OffsetY { get; set; }

    /// <summary>Screen pixels per world unit, clamped to <see cref="MinimumZoom"/>..<see cref="MaximumZoom"/>.</summary>
    public double Zoom
    {
        get => _zoom;
        set => _zoom = Math.Clamp(value, MinimumZoom, MaximumZoom);
    }

    /// <summary>Converts a world x coordinate to a screen x coordinate.</summary>
    /// <param name="worldX">The world coordinate.</param>
    /// <returns>The screen coordinate.</returns>
    public double ToScreenX(double worldX) => (worldX - OffsetX) * _zoom;

    /// <summary>Converts a world y coordinate to a screen y coordinate.</summary>
    /// <param name="worldY">The world coordinate.</param>
    /// <returns>The screen coordinate.</returns>
    public double ToScreenY(double worldY) => (worldY - OffsetY) * _zoom;

    /// <summary>Converts a screen x coordinate to a world x coordinate.</summary>
    /// <param name="screenX">The screen coordinate.</param>
    /// <returns>The world coordinate.</returns>
    public double ToWorldX(double screenX) => (screenX / _zoom) + OffsetX;

    /// <summary>Converts a screen y coordinate to a world y coordinate.</summary>
    /// <param name="screenY">The screen coordinate.</param>
    /// <returns>The world coordinate.</returns>
    public double ToWorldY(double screenY) => (screenY / _zoom) + OffsetY;

    /// <summary>Pans by a screen-space delta, which is what a middle-drag produces.</summary>
    /// <param name="deltaScreenX">Horizontal movement in pixels.</param>
    /// <param name="deltaScreenY">Vertical movement in pixels.</param>
    public void PanByScreen(double deltaScreenX, double deltaScreenY)
    {
        OffsetX -= deltaScreenX / _zoom;
        OffsetY -= deltaScreenY / _zoom;
    }

    /// <summary>
    /// Zooms about a fixed screen point, so the world position under the cursor stays under the
    /// cursor. Anything else makes a wheel zoom feel like it is fighting the user.
    /// </summary>
    /// <param name="factor">The multiplier to apply to <see cref="Zoom"/>. Must be positive.</param>
    /// <param name="anchorScreenX">The screen x coordinate to hold fixed.</param>
    /// <param name="anchorScreenY">The screen y coordinate to hold fixed.</param>
    public void ZoomAbout(double factor, double anchorScreenX, double anchorScreenY)
    {
        if (factor <= 0 || !double.IsFinite(factor))
        {
            return;
        }

        double worldX = ToWorldX(anchorScreenX);
        double worldY = ToWorldY(anchorScreenY);

        Zoom = _zoom * factor;

        OffsetX = worldX - (anchorScreenX / _zoom);
        OffsetY = worldY - (anchorScreenY / _zoom);
    }

    /// <summary>The world rectangle currently visible in a control of the given size.</summary>
    /// <param name="widthPixels">The control's width in device-independent pixels.</param>
    /// <param name="heightPixels">The control's height.</param>
    /// <returns>The visible world rectangle, which is what <see cref="SceneIndex.Query"/> takes.</returns>
    public CanvasBounds VisibleWorld(double widthPixels, double heightPixels) => new(
        OffsetX,
        OffsetY,
        OffsetX + (widthPixels / _zoom),
        OffsetY + (heightPixels / _zoom));

    /// <summary>
    /// Fits a world rectangle into a control of the given size with a margin, which is what
    /// <i>zoom to fit</i> does.
    /// </summary>
    /// <param name="world">The world rectangle to frame. A degenerate rectangle centres instead.</param>
    /// <param name="widthPixels">The control's width in device-independent pixels.</param>
    /// <param name="heightPixels">The control's height.</param>
    /// <param name="marginPixels">Padding to leave on every side.</param>
    public void FitTo(CanvasBounds world, double widthPixels, double heightPixels, double marginPixels = 48)
    {
        double usableWidth = Math.Max(1, widthPixels - (marginPixels * 2));
        double usableHeight = Math.Max(1, heightPixels - (marginPixels * 2));

        double worldWidth = Math.Max(world.Width, 1e-6);
        double worldHeight = Math.Max(world.Height, 1e-6);

        Zoom = Math.Min(usableWidth / worldWidth, usableHeight / worldHeight);

        double centreX = (world.MinX + world.MaxX) * 0.5;
        double centreY = (world.MinY + world.MaxY) * 0.5;
        OffsetX = centreX - (widthPixels / _zoom * 0.5);
        OffsetY = centreY - (heightPixels / _zoom * 0.5);
    }
}
