using System;

namespace Spark.Viewport;

/// <summary>
/// A backend that draws a <see cref="ViewportScene"/> through a <see cref="Camera"/>. There are
/// two implementations by design (ADR-0014): an OpenGL one, which is the real one, and a software
/// rasteriser, which covers GL initialisation failures on virtual machines and over remote
/// desktop, renders headless thumbnails, and is the only path whose output is comparable between
/// machines and therefore the only one CI can assert on.
/// </summary>
/// <remarks>
/// Implementations are <b>not</b> thread-safe. Every method must be called from the thread that
/// owns the rendering context — for the GL backend, the thread Avalonia calls
/// <c>OnOpenGlRender</c> on.
/// </remarks>
public interface IViewportRenderer : IDisposable
{
    /// <summary>A short name for the backend, for diagnostics and the viewport's status readout.</summary>
    string Name { get; }

    /// <summary>
    /// True once <see cref="Initialise"/> has succeeded. A renderer that is not initialised must
    /// silently do nothing when asked to render rather than throwing, because the caller is a
    /// paint handler and a throw there takes the window down.
    /// </summary>
    bool IsInitialised { get; }

    /// <summary>
    /// Why initialisation failed, or a description of what was obtained when it succeeded — the
    /// GL version string, for instance. Never null after <see cref="Initialise"/> has returned.
    /// <para>
    /// This is a first-class output, not a debugging aid. A GL context that fails to come up on a
    /// virtual machine is the single most common viewport support case there is, and the answer
    /// has to be legible to a user who cannot attach a debugger.
    /// </para>
    /// </summary>
    string? Diagnostic { get; }

    /// <summary>
    /// Acquires whatever the backend needs: compiles shaders, allocates buffers, queries limits.
    /// </summary>
    /// <returns>
    /// True on success. On failure this returns false and sets <see cref="Diagnostic"/> rather
    /// than throwing, so the caller can fall back to another backend.
    /// </returns>
    bool Initialise();

    /// <summary>Tells the backend the size of the surface it is drawing into.</summary>
    /// <param name="widthPixels">Width in physical pixels.</param>
    /// <param name="heightPixels">Height in physical pixels.</param>
    void Resize(int widthPixels, int heightPixels);

    /// <summary>Draws one frame.</summary>
    /// <param name="scene">The geometry to draw.</param>
    /// <param name="camera">The camera to draw it through.</param>
    void Render(ViewportScene scene, Camera camera);
}
