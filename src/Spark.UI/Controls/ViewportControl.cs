using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Spark.UI.Interop;
using Spark.UI.Theming;
using Spark.Viewport;
using Spark.Viewport.OpenGL;

namespace Spark.UI.Controls;

/// <summary>
/// Hosts <see cref="IViewportRenderer"/> inside Avalonia. This is the entire adaptation layer
/// between the framework and the Avalonia-free renderer: it owns the camera, translates pointer
/// input into camera moves, and forwards Avalonia's GL callbacks.
/// </summary>
/// <remarks>
/// <para>
/// Surface interop — getting a GPU surface composited into a control correctly, on each platform,
/// with the right lifetime and resize behaviour — is the expensive half of the viewport problem
/// and is exactly what <see cref="OpenGlControlBase"/> already solves (ADR-0014). That is why the
/// GL entry points are hand-declared rather than taken from a bindings package: a bindings package
/// solves the half that is not hard.
/// </para>
/// <para>
/// <b>What happens when GL does not come up.</b> Avalonia calls
/// <see cref="OnOpenGlInit(GlInterface)"/> only after it has a context, so a total failure to
/// create one presents as those callbacks never firing: no renderer is ever built and this
/// control paints its diagnostic plate instead of a scene. A context that comes up but cannot compile the shaders
/// reports through <see cref="IViewportRenderer.Diagnostic"/>. Both paths end in a legible message
/// on screen rather than a black rectangle, because a black rectangle is indistinguishable from
/// geometry that failed to draw.
/// </para>
/// </remarks>
public sealed class ViewportControl : OpenGlControlBase
{
    private readonly Camera _camera = new();
    private OpenGlViewportRenderer? _renderer;
    private AvaloniaGlApi? _glApi;
    private ViewportScene _scene = new();
    private Point _pointerAnchor;
    private CameraDrag _drag;
    private string? _status;
    private byte[]? _capture;
    private int _captureWidth;
    private int _captureHeight;
    private bool _captureRequested;

    /// <summary>Creates a viewport with an empty scene.</summary>
    public ViewportControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    private enum CameraDrag
    {
        None,
        Orbit,
        Pan,
    }

    /// <summary>The camera. Right-handed and +Z up, matching the kernel.</summary>
    public Camera Camera => _camera;

    /// <summary>
    /// What the renderer reported at initialisation: the GL version and driver on success, the
    /// reason on failure. Null until the first GL callback has run.
    /// </summary>
    public string? Status => _status;

    /// <summary>The geometry being drawn.</summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public ViewportScene Scene
    {
        get => _scene;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _scene = value;
            RequestNextFrameRendering();
        }
    }

    /// <summary>
    /// Asks for the next rendered frame to be read back off the GPU.
    /// </summary>
    /// <remarks>
    /// The read-back is how "the viewport renders" becomes a checked fact. A GL backend that
    /// initialises, compiles, links and runs can still be drawing nothing at all, and no amount of
    /// managed-side assertion can tell the difference without looking at the pixels.
    /// </remarks>
    public void RequestCapture()
    {
        _captureRequested = true;
        RequestNextFrameRendering();
    }

    /// <summary>Takes the most recent read-back, if one has completed.</summary>
    /// <param name="width">The captured width in pixels.</param>
    /// <param name="height">The captured height in pixels.</param>
    /// <returns>
    /// 8-bit RGBA in OpenGL's bottom-up row order, or null when no capture has completed.
    /// </returns>
    public byte[]? TakeCapture(out int width, out int height)
    {
        width = _captureWidth;
        height = _captureHeight;
        byte[]? capture = _capture;
        _capture = null;
        return capture;
    }

    /// <summary>
    /// Asks for another GL frame because the scene's contents changed.
    /// </summary>
    /// <remarks>
    /// The scene is mutated in place by the view model rather than replaced, so setting
    /// <see cref="Scene"/> again would be a no-op and the viewport would go on showing the previous
    /// run's geometry. The renderer reconciles against the scene's version counter, so all this has
    /// to do is ask for a frame.
    /// </remarks>
    public void InvalidateGeometry() => RequestNextFrameRendering();

    /// <summary>Frames the whole scene.</summary>
    public void ZoomToFit()
    {
        _camera.ZoomToFit(_scene.ComputeBounds());
        RequestNextFrameRendering();
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Drawn over the GL surface, not into it: an overlay is UI and is fully inside the
        // contrast rules, so it sits on its own fill rather than on unknown geometry (§8.5).
        if (_renderer?.IsInitialised == true)
        {
            return;
        }

        string message = _status
            ?? "The OpenGL context has not initialised. The software renderer is not built yet.";

        FormattedText run = new(
            message,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            13,
            SparkPalette.TextPrimaryBrush);

        Rect plate = new(16, 16, Math.Min(run.Width + 24, Math.Max(64, Bounds.Width - 32)), run.Height + 16);
        context.FillRectangle(SparkPalette.Frozen(SparkPalette.SurfaceFloat), plate);
        context.DrawText(run, new Point(plate.X + 12, plate.Y + 8));
    }

    /// <inheritdoc/>
    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);

        AvaloniaGlApi api = new(gl, GlVersion);
        OpenGlViewportRenderer renderer = new(api);

        if (renderer.Initialise())
        {
            _renderer = renderer;
            _glApi = api;
            _status = api.MissingEntryPoints is null
                ? renderer.Diagnostic
                : $"{renderer.Diagnostic} Missing entry points: {api.MissingEntryPoints}.";
        }
        else
        {
            renderer.Dispose();
            _renderer = null;
            _glApi = null;
            _status = renderer.Diagnostic;
        }

        RequestNextFrameRendering();
    }

    /// <inheritdoc/>
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Dispose();
        _renderer = null;
        _glApi = null;
        base.OnOpenGlDeinit(gl);
    }

    /// <inheritdoc/>
    protected override void OnOpenGlLost()
    {
        // The context has gone — a driver reset, a display change, a laptop switching GPUs. The
        // renderer's handles are all invalid now, so it is dropped rather than disposed: calling
        // GL against a dead context is how a context loss turns into a crash.
        _renderer = null;
        _glApi = null;
        _status = "The OpenGL context was lost. It will be rebuilt on the next frame.";
        base.OnOpenGlLost();
    }

    /// <inheritdoc/>
    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_renderer is null)
        {
            return;
        }

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int width = Math.Max(1, (int)Math.Round(Bounds.Width * scaling));
        int height = Math.Max(1, (int)Math.Round(Bounds.Height * scaling));

        _camera.SetViewportSize(width, height);
        _renderer.TargetFramebuffer = fb;
        _renderer.Resize(width, height);
        _renderer.Render(_scene, _camera);

        if (!_captureRequested)
        {
            return;
        }

        _captureRequested = false;
        byte[] pixels = new byte[width * height * 4];

        if (_glApi?.ReadPixels(0, 0, width, height, pixels) == true)
        {
            _capture = pixels;
            _captureWidth = width;
            _captureHeight = height;
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;

        _drag = properties.IsMiddleButtonPressed
            ? CameraDrag.Pan
            : properties.IsRightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Alt)
                ? CameraDrag.Orbit
                : CameraDrag.None;

        if (_drag is CameraDrag.None)
        {
            return;
        }

        _pointerAnchor = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_drag is CameraDrag.None)
        {
            return;
        }

        Point position = e.GetPosition(this);
        double dx = position.X - _pointerAnchor.X;
        double dy = position.Y - _pointerAnchor.Y;
        _pointerAnchor = position;

        if (_drag is CameraDrag.Orbit)
        {
            _camera.Orbit(dx, dy);
        }
        else
        {
            _camera.Pan(dx, dy);
        }

        RequestNextFrameRendering();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = CameraDrag.None;
        e.Pointer.Capture(null);
    }

    /// <inheritdoc/>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _camera.Dolly(e.Delta.Y);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is Key.Home)
        {
            ZoomToFit();
            e.Handled = true;
        }
    }
}
