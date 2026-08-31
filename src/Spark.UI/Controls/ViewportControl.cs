using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Spark.UI.Interop;
using Spark.UI.Theming;
using Spark.Viewport;
using Spark.Viewport.OpenGL;
using Spark.Viewport.Software;

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
/// create one presents as those callbacks never firing and no renderer ever being built. A context
/// that comes up but cannot compile the shaders reports through
/// <see cref="IViewportRenderer.Diagnostic"/>. <b>Either way the control falls back to
/// <see cref="SoftwareViewportRenderer"/> and draws the real scene</b>, with the diagnostic plate
/// over it saying which path is in use — a user on a virtual machine gets their model rather than
/// an apology, and still knows why it is slow. Before the rasteriser existed this path showed the
/// message alone, which is what the message used to say.
/// </para>
/// </remarks>
public sealed class ViewportControl : OpenGlControlBase
{
    private readonly Camera _camera = new();
    private readonly SoftwareViewportRenderer _software = new();
    private WriteableBitmap? _softwareBitmap;
    private long _softwareSignature = -1;
    private bool _glReported;
    private bool _softwareCommitted;
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

    /// <summary>
    /// How long to wait for a GL callback before concluding none is coming. Long enough not to
    /// race a slow driver, short enough that a user on a virtual machine is not staring at an
    /// empty viewport wondering whether the application has hung.
    /// </summary>
    private static TimeSpan GlPatience => TimeSpan.FromMilliseconds(1500);

    private enum CameraDrag
    {
        None,
        Orbit,
        Pan,
    }

    /// <summary>The camera. Right-handed and +Z up, matching the kernel.</summary>
    public Camera Camera => _camera;

    /// <summary>
    /// When true, no OpenGL context is ever requested and every frame is rasterised on the CPU.
    /// </summary>
    /// <remarks>
    /// Set from <c>--software-renderer</c>. The fallback happens by itself when GL fails; this
    /// makes it reachable on purpose, which is what a support conversation needs — "run it with
    /// <c>--software-renderer</c>" is an answer, where "your driver is broken" is not — and it is
    /// what allows the software path to be photographed by <c>--screenshot</c> and therefore
    /// checked, rather than trusted because it compiles.
    /// </remarks>
    public bool ForceSoftwareRenderer { get; set; }

    /// <summary>
    /// Whether the software rasteriser is the backend actually presenting this control, as
    /// opposed to merely being available.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The distinction is not pedantry; getting it wrong silently photographs the wrong
    /// backend.</b> Avalonia paints the control before <see cref="OnOpenGlInit(GlInterface)"/>
    /// has run, so "no GL renderer yet" is the normal state during startup and is
    /// indistinguishable, from inside <see cref="Render(DrawingContext)"/>, from "GL is never
    /// coming". Drawing software frames in that window is merely wasteful; <i>servicing a capture
    /// request</i> from one meant <c>--screenshot</c> returned a CPU frame from a healthy GPU
    /// session — see <c>N64</c>.
    /// </para>
    /// <para>
    /// Three ways to become committed, and the third is why a timer is needed at all: the switch
    /// was given; a GL callback ran and left no renderer, which is initialisation failing or the
    /// context being lost; or no GL callback arrived within <see cref="GlPatience"/> of the
    /// control being attached, which is what a total failure to create a context looks like from
    /// here, because in that case the callbacks simply never fire and nothing reports anything.
    /// </para>
    /// </remarks>
    public bool IsSoftwarePresenting =>
        ForceSoftwareRenderer || _softwareCommitted || (_glReported && _renderer is null);

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
    /// 8-bit RGBA, <b>top row first</b>, or null when no capture has completed.
    /// </returns>
    /// <remarks>
    /// The row order is normalised here rather than at the call site. <c>glReadPixels</c> hands
    /// back the bottom row first and the software rasteriser hands back the top row first; which
    /// of the two produced a given frame is precisely what a caller should not have to know, and
    /// a caller that guesses wrong gets an upside-down image rather than an error.
    /// </remarks>
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

        // No GL renderer. If software is the committed backend, rasterise the scene on the CPU
        // and draw that rather than showing a message where a model should be — the first of the
        // three jobs E9-T5 exists to do. The message stays either way, because a user is entitled
        // to know which path they are on.
        if (IsSoftwarePresenting)
        {
            DrawSoftwareFrame(context);
        }

        string message = _status
            ?? "Waiting for the OpenGL context.";

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

    /// <summary>
    /// Rasterises the scene on the CPU and blits it into the control. Only reached when GL is
    /// unavailable, which on a virtual machine or over remote desktop is a normal Tuesday rather
    /// than an exceptional case.
    /// </summary>
    /// <remarks>
    /// The frame is cached against the scene version, the camera and the control size, because
    /// Avalonia may paint for reasons that have nothing to do with the viewport — a tooltip
    /// closing over it, for instance — and re-rasterising a model to answer that would make the
    /// whole window feel broken rather than merely slow.
    /// </remarks>
    private void DrawSoftwareFrame(DrawingContext context)
    {
        // Deliberately at one device pixel per layout unit, where the GL path multiplies by
        // RenderScaling. On a 200% display that is a quarter of the fragments, and this backend
        // runs on one CPU core precisely when the machine has already proved it has no usable
        // GPU. The image is scaled up by the compositor and is softer than the GL one; that is
        // the trade, and it is a choice rather than an oversight.
        int width = Math.Max(1, (int)Math.Round(Bounds.Width));
        int height = Math.Max(1, (int)Math.Round(Bounds.Height));

        if (width <= 1 || height <= 1)
        {
            return;
        }

        long signature = SoftwareFrameSignature(width, height);
        if (_softwareBitmap is null || signature != _softwareSignature)
        {
            _software.Initialise();
            _software.Resize(width, height);
            _camera.SetViewportSize(width, height);
            _software.Render(_scene, _camera);

            if (_softwareBitmap is null || _softwareBitmap.PixelSize.Width != width || _softwareBitmap.PixelSize.Height != height)
            {
                _softwareBitmap?.Dispose();
                _softwareBitmap = new WriteableBitmap(
                    new PixelSize(width, height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
            }

            byte[] pixels = new byte[width * height * 4];
            _software.Framebuffer.CopyPixels(pixels);

            using (ILockedFramebuffer locked = _softwareBitmap.Lock())
            {
                int rowBytes = width * 4;
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(pixels, y * rowBytes, IntPtr.Add(locked.Address, y * locked.RowBytes), rowBytes);
                }
            }

            _softwareSignature = signature;
        }

        if (_captureRequested)
        {
            _captureRequested = false;
            byte[] capture = new byte[width * height * 4];
            _software.Framebuffer.CopyPixels(capture);
            _capture = capture;
            _captureWidth = width;
            _captureHeight = height;
        }

        context.DrawImage(_softwareBitmap, new Rect(0, 0, Bounds.Width, Bounds.Height));
    }

    /// <summary>
    /// A cheap value that changes whenever the software frame would look different. Not a hash of
    /// the scene: the scene's own version counter already increments on every mutation, which is
    /// the thing a hash would be reconstructing at far greater cost.
    /// </summary>
    private long SoftwareFrameSignature(int width, int height)
    {
        HashCode hash = default;
        hash.Add(_scene.Version);
        hash.Add(width);
        hash.Add(height);
        hash.Add(_camera.Distance);
        hash.Add(_camera.Azimuth);
        hash.Add(_camera.Elevation);
        hash.Add(_camera.Target);
        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // A context that fails to be created at all presents as these callbacks never firing, so
        // there is no event to hang the decision on and it has to be a timeout.
        DispatcherTimer.RunOnce(
            () =>
            {
                if (_glReported || _softwareCommitted)
                {
                    return;
                }

                _softwareCommitted = true;
                _status ??= "No OpenGL context arrived. Drawing with the software renderer.";
                RequestNextFrameRendering();
            },
            GlPatience);
    }

    /// <inheritdoc/>
    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        _glReported = true;

        if (ForceSoftwareRenderer)
        {
            _renderer = null;
            _glApi = null;
            _status = "Software renderer, forced by --software-renderer.";
            RequestNextFrameRendering();
            return;
        }

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
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // The bitmap holds unmanaged pixels and the control is not IDisposable, so this is the
        // only place it can be released. Dropping it also drops the cached signature, so a
        // re-attached control rasterises afresh rather than blitting a stale frame.
        _softwareBitmap?.Dispose();
        _softwareBitmap = null;
        _softwareSignature = -1;
        _software.Dispose();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    protected override void OnOpenGlLost()
    {
        // The context has gone — a driver reset, a display change, a laptop switching GPUs. The
        // renderer's handles are all invalid now, so it is dropped rather than disposed: calling
        // GL against a dead context is how a context loss turns into a crash.
        _renderer = null;
        _glApi = null;
        _glReported = true;
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

        if (_glApi?.ReadPixels(0, 0, width, height, pixels) != true)
        {
            return;
        }

        // Flip into top-down order here, so TakeCapture has one documented convention rather
        // than one per backend.
        byte[] flipped = new byte[pixels.Length];
        int stride = width * 4;
        for (int row = 0; row < height; row++)
        {
            Array.Copy(pixels, (height - 1 - row) * stride, flipped, row * stride, stride);
        }

        _capture = flipped;
        _captureWidth = width;
        _captureHeight = height;
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
