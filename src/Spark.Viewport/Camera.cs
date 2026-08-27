using System;
using System.Numerics;

namespace Spark.Viewport;

/// <summary>
/// The viewport's orbit camera. Right-handed and +Z up, matching the kernel
/// (<see cref="NamespaceDoc"/>): the camera looks down its own −Z, world X crossed with world Y
/// gives world Z, and the ground plane the grid is drawn on is the world XY plane.
/// </summary>
/// <remarks>
/// <para>
/// The camera is stored in spherical coordinates about a target rather than as a free matrix,
/// because every interaction a user performs on a CAD viewport — orbit, pan, dolly, zoom to fit,
/// a named view — is naturally expressed in those terms, and because a stored matrix drifts out
/// of orthonormality after a few thousand incremental rotations.
/// </para>
/// <para>
/// The near and far planes are derived from <see cref="Distance"/> rather than fixed. A fixed
/// near plane is the single most common cause of depth fighting in a viewport that has to show
/// both a 2 mm fillet and a 200 m building, and deriving it costs nothing.
/// </para>
/// </remarks>
public sealed class Camera
{
    /// <summary>The world up direction: +Z, matching the kernel's <c>Plane.WorldXY</c> normal.</summary>
    public static readonly Vector3 WorldUp = Vector3.UnitZ;

    private const float MaxElevation = 1.5533431f;  // 89 degrees; keeps up and view non-parallel.
    private const float MinDistance = 1e-3f;
    private const float MaxDistance = 1e7f;

    private float _elevation = 0.5980f;             // ~34.3 degrees: the standard three-quarter view.
    private float _azimuth = -0.9599f;              // ~-55 degrees.
    private float _distance = 12f;
    private float _fieldOfView = 0.7853982f;        // 45 degrees.

    /// <summary>The point the camera orbits and looks at.</summary>
    public Vector3 Target { get; set; } = Vector3.Zero;

    /// <summary>
    /// Distance from <see cref="Target"/> to the eye, clamped to a range that keeps the derived
    /// near and far planes representable.
    /// </summary>
    public float Distance
    {
        get => _distance;
        set => _distance = Math.Clamp(value, MinDistance, MaxDistance);
    }

    /// <summary>
    /// Rotation about world +Z in radians, measured counter-clockwise from world +X — the
    /// kernel's sign convention for a rotation about an axis.
    /// </summary>
    public float Azimuth
    {
        get => _azimuth;
        set => _azimuth = value;
    }

    /// <summary>
    /// Angle above the world XY plane in radians, clamped to ±89° so the view direction never
    /// becomes parallel to <see cref="WorldUp"/> and the view matrix never degenerates.
    /// </summary>
    public float Elevation
    {
        get => _elevation;
        set => _elevation = Math.Clamp(value, -MaxElevation, MaxElevation);
    }

    /// <summary>Vertical field of view in radians. Clamped to 5°..150°.</summary>
    public float FieldOfView
    {
        get => _fieldOfView;
        set => _fieldOfView = Math.Clamp(value, 0.0872665f, 2.6179939f);
    }

    /// <summary>Viewport width in pixels. Never zero; a zero would make the aspect ratio undefined.</summary>
    public int ViewportWidth { get; private set; } = 1;

    /// <summary>Viewport height in pixels. Never zero.</summary>
    public int ViewportHeight { get; private set; } = 1;

    /// <summary>The aspect ratio the projection matrix is built with.</summary>
    public float AspectRatio => ViewportWidth / (float)ViewportHeight;

    /// <summary>The near clip plane, derived from <see cref="Distance"/>.</summary>
    public float NearPlane => Math.Max(_distance * 0.005f, 1e-4f);

    /// <summary>The far clip plane, derived from <see cref="Distance"/>.</summary>
    public float FarPlane => Math.Max(_distance * 200f, NearPlane * 1000f);

    /// <summary>The unit vector from <see cref="Target"/> towards the eye.</summary>
    public Vector3 OffsetDirection
    {
        get
        {
            float cosElevation = MathF.Cos(_elevation);
            return new Vector3(
                cosElevation * MathF.Cos(_azimuth),
                cosElevation * MathF.Sin(_azimuth),
                MathF.Sin(_elevation));
        }
    }

    /// <summary>The eye position in world space.</summary>
    public Vector3 Position => Target + (OffsetDirection * _distance);

    /// <summary>The right-handed view matrix. The camera looks down its own −Z.</summary>
    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Target, WorldUp);

    /// <summary>The right-handed perspective projection matrix.</summary>
    public Matrix4x4 Projection =>
        Matrix4x4.CreatePerspectiveFieldOfView(_fieldOfView, AspectRatio, NearPlane, FarPlane);

    /// <summary>The combined view-projection matrix, which is what a shader receives.</summary>
    public Matrix4x4 ViewProjection => View * Projection;

    /// <summary>Records the size of the surface being rendered into.</summary>
    /// <param name="widthPixels">Width in pixels. Values below one are treated as one.</param>
    /// <param name="heightPixels">Height in pixels. Values below one are treated as one.</param>
    public void SetViewportSize(int widthPixels, int heightPixels)
    {
        ViewportWidth = Math.Max(1, widthPixels);
        ViewportHeight = Math.Max(1, heightPixels);
    }

    /// <summary>
    /// Orbits the camera about <see cref="Target"/> by a pointer delta in pixels. Dragging right
    /// swings the camera anti-clockwise about +Z; dragging up raises it.
    /// </summary>
    /// <param name="deltaXPixels">Horizontal pointer movement in pixels.</param>
    /// <param name="deltaYPixels">Vertical pointer movement in pixels, positive downwards.</param>
    public void Orbit(double deltaXPixels, double deltaYPixels)
    {
        const float RadiansPerPixel = 0.006f;
        Azimuth -= (float)deltaXPixels * RadiansPerPixel;
        Elevation += (float)deltaYPixels * RadiansPerPixel;
    }

    /// <summary>
    /// Pans the camera by a pointer delta in pixels, moving <see cref="Target"/> in the plane
    /// perpendicular to the view direction. The scale is derived from the distance and the field
    /// of view, so a point under the cursor stays under the cursor at any zoom.
    /// </summary>
    /// <param name="deltaXPixels">Horizontal pointer movement in pixels.</param>
    /// <param name="deltaYPixels">Vertical pointer movement in pixels, positive downwards.</param>
    public void Pan(double deltaXPixels, double deltaYPixels)
    {
        float worldPerPixel = 2f * _distance * MathF.Tan(_fieldOfView * 0.5f) / ViewportHeight;
        Vector3 forward = -OffsetDirection;
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, WorldUp));
        Vector3 up = Vector3.Cross(right, forward);
        Target += (right * (float)-deltaXPixels * worldPerPixel) + (up * (float)deltaYPixels * worldPerPixel);
    }

    /// <summary>
    /// Dollies towards or away from <see cref="Target"/>. Multiplicative rather than additive, so
    /// one wheel notch covers the same proportion of the remaining distance at every scale, which
    /// is what stops a zoom from crawling when far away and overshooting when close.
    /// </summary>
    /// <param name="notches">Wheel notches. Positive moves towards the target.</param>
    public void Dolly(double notches)
    {
        Distance = _distance * MathF.Exp((float)-notches * 0.15f);
    }

    /// <summary>
    /// Frames a bounding box: centres the target on it and backs off far enough that the box's
    /// bounding sphere fits inside the vertical field of view, with a small margin.
    /// </summary>
    /// <param name="bounds">The box to frame. An empty box leaves the camera unchanged.</param>
    public void ZoomToFit(Bounds3 bounds)
    {
        if (bounds.IsEmpty)
        {
            return;
        }

        Target = bounds.Centre;

        float radius = Math.Max(bounds.Radius, 1e-3f);
        float verticalFit = radius / MathF.Sin(_fieldOfView * 0.5f);

        // The horizontal field of view is narrower than the vertical one on a tall viewport, so
        // fitting only the vertical angle would crop a wide model. Fit whichever is tighter.
        float horizontalHalfAngle = MathF.Atan(MathF.Tan(_fieldOfView * 0.5f) * AspectRatio);
        float horizontalFit = radius / MathF.Sin(horizontalHalfAngle);

        Distance = Math.Max(verticalFit, horizontalFit) * 1.15f;
    }

    /// <summary>
    /// Projects a world point to pixel coordinates with the origin at the top-left of the
    /// viewport, which is the convention every UI framework Spark draws overlays with uses.
    /// </summary>
    /// <param name="world">The world-space point.</param>
    /// <param name="screen">The pixel position, valid only when this method returns true.</param>
    /// <returns>
    /// False when the point is behind the eye or otherwise outside the clip volume's w range, in
    /// which case no meaningful pixel position exists and the caller must not draw.
    /// </returns>
    public bool TryWorldToScreen(Vector3 world, out Vector2 screen)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), ViewProjection);

        if (clip.W <= 1e-6f)
        {
            screen = default;
            return false;
        }

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;

        screen = new Vector2(
            (ndcX + 1f) * 0.5f * ViewportWidth,
            (1f - ndcY) * 0.5f * ViewportHeight);
        return true;
    }
}
