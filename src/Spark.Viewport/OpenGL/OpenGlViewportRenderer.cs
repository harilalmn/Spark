using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Spark.Viewport.Meshes;

namespace Spark.Viewport.OpenGL;

/// <summary>
/// The OpenGL backend. Draws the background gradient, the ground grid and axes, and then one
/// buffer set per <see cref="GeometryKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>One buffer set per key, uploaded lazily.</b> The renderer keeps a dictionary of GPU
/// geometry keyed by the same tuple the scene uses, and reconciles it against the scene's
/// snapshot at the top of each frame: new keys are uploaded, changed packages are re-uploaded,
/// and keys that have gone are deleted. That is what makes re-evaluating one node cost one
/// upload rather than a scene rebuild, and it is why nothing here needs to know how the graph
/// changed — only what it now contains.
/// </para>
/// <para>
/// <b>Nothing here throws into a paint handler.</b> Initialisation failures are reported through
/// <see cref="Diagnostic"/> and leave <see cref="IsInitialised"/> false;
/// <see cref="Render(ViewportScene, Camera)"/> then does nothing. A throw out of a GL callback
/// takes the window down, and a viewport that fails to start is a support case, not a crash.
/// </para>
/// </remarks>
public sealed class OpenGlViewportRenderer : IViewportRenderer
{
    private readonly IGlApi _gl;
    private readonly Dictionary<GeometryKey, GpuGeometry> _uploaded = [];
    private readonly List<GeometryKey> _doomed = [];
    private readonly HashSet<GeometryKey> _live = [];

    private MeshProgram _meshProgram;
    private LineProgram _lineProgram;
    private BackgroundProgram _backgroundProgram;

    private GpuLines? _grid;
    private int _fullScreenBuffer;
    private int _fullScreenVertexArray;

    private int _widthPixels = 1;
    private int _heightPixels = 1;
    private bool _disposed;

    /// <summary>Creates a renderer over a bound GL context.</summary>
    /// <param name="gl">
    /// The GL entry points. The context must already be current on the calling thread, and must
    /// stay current for every subsequent call.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="gl"/> is null.</exception>
    public OpenGlViewportRenderer(IGlApi gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
    }

    /// <inheritdoc/>
    public string Name => "OpenGL";

    /// <inheritdoc/>
    public bool IsInitialised { get; private set; }

    /// <inheritdoc/>
    public string? Diagnostic { get; private set; }

    /// <summary>
    /// The framebuffer object the next frame is drawn into. Avalonia hands its
    /// <c>OpenGlControlBase</c> a target FBO on every render callback and it is not guaranteed to
    /// be the same one twice, so it is set per frame rather than captured at initialisation.
    /// </summary>
    public int TargetFramebuffer { get; set; }

    /// <summary>The number of buffer sets currently resident on the GPU.</summary>
    public int ResidentGeometryCount => _uploaded.Count;

    /// <inheritdoc/>
    public bool Initialise()
    {
        if (IsInitialised)
        {
            return true;
        }

        try
        {
            GlDialect dialect = _gl.Dialect;

            if (!TryBuildProgram(
                    GlShaders.MeshVertex(dialect), GlShaders.MeshFragment(dialect), "mesh", out int mesh))
            {
                return false;
            }

            if (!TryBuildProgram(
                    GlShaders.LineVertex(dialect), GlShaders.LineFragment(dialect), "line", out int line))
            {
                return false;
            }

            if (!TryBuildProgram(
                    GlShaders.BackgroundVertex(dialect),
                    GlShaders.BackgroundFragment(dialect),
                    "background",
                    out int background))
            {
                return false;
            }

            _meshProgram = MeshProgram.Bind(_gl, mesh);
            _lineProgram = LineProgram.Bind(_gl, line);
            _backgroundProgram = BackgroundProgram.Bind(_gl, background);

            CreateFullScreenTriangle();
            _grid = GpuLines.Upload(_gl, GroundGrid.Build());

            IsInitialised = true;
            Diagnostic = string.Create(
                CultureInfo.InvariantCulture,
                $"OpenGL ready. Version '{_gl.VersionString}', renderer '{_gl.RendererString}', " +
                $"GLSL dialect {dialect}, vertex arrays {(_gl.SupportsVertexArrays ? "yes" : "no")}.");
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Diagnostic = "OpenGL initialisation threw: " + ex.Message;
            IsInitialised = false;
            return false;
        }
    }

    /// <inheritdoc/>
    public void Resize(int widthPixels, int heightPixels)
    {
        _widthPixels = Math.Max(1, widthPixels);
        _heightPixels = Math.Max(1, heightPixels);
    }

    /// <inheritdoc/>
    public void Render(ViewportScene scene, Camera camera)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        if (!IsInitialised || _disposed)
        {
            return;
        }

        _gl.BindFramebuffer(GlConst.Framebuffer, TargetFramebuffer);
        _gl.Viewport(0, 0, _widthPixels, _heightPixels);

        _gl.Disable(GlConst.DepthTest);
        _gl.DepthMask(false);
        _gl.Disable(GlConst.Blend);
        DrawBackground();

        _gl.Enable(GlConst.DepthTest);
        _gl.DepthFunc(GlConst.LessEqual);
        _gl.DepthMask(true);
        _gl.Clear(GlConst.DepthBufferBit);

        Matrix4x4 viewProjection = camera.ViewProjection;
        RenderPackage[] packages = scene.Snapshot();
        Reconcile(packages);

        DrawGrid(viewProjection);
        DrawGeometry(packages, camera, viewProjection);

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsInitialised = false;

        foreach (GpuGeometry geometry in _uploaded.Values)
        {
            geometry.Delete(_gl);
        }

        _uploaded.Clear();
        _grid?.Delete(_gl);
        _grid = null;

        if (_fullScreenVertexArray != 0)
        {
            _gl.DeleteVertexArray(_fullScreenVertexArray);
            _fullScreenVertexArray = 0;
        }

        if (_fullScreenBuffer != 0)
        {
            _gl.DeleteBuffer(_fullScreenBuffer);
            _fullScreenBuffer = 0;
        }

        DeleteProgram(_meshProgram.Program);
        DeleteProgram(_lineProgram.Program);
        DeleteProgram(_backgroundProgram.Program);
    }

    private void DeleteProgram(int program)
    {
        if (program != 0)
        {
            _gl.DeleteProgram(program);
        }
    }

    private bool TryBuildProgram(string vertexSource, string fragmentSource, string label, out int program)
    {
        program = 0;

        int vertex = _gl.CompileShader(GlConst.VertexShader, vertexSource, out string? vertexError);
        if (vertex == 0)
        {
            Diagnostic = $"The {label} vertex shader did not compile: {vertexError}";
            return false;
        }

        int fragment = _gl.CompileShader(GlConst.FragmentShader, fragmentSource, out string? fragmentError);
        if (fragment == 0)
        {
            _gl.DeleteShader(vertex);
            Diagnostic = $"The {label} fragment shader did not compile: {fragmentError}";
            return false;
        }

        program = _gl.LinkProgram(vertex, fragment, out string? linkError);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);

        if (program == 0)
        {
            Diagnostic = $"The {label} program did not link: {linkError}";
            return false;
        }

        return true;
    }

    private void CreateFullScreenTriangle()
    {
        // One oversized triangle rather than two triangles making a quad: it covers the viewport
        // with three vertices instead of six and has no diagonal seam for the dither to catch on.
        float[] vertices = [-1f, -1f, 3f, -1f, -1f, 3f];

        _fullScreenBuffer = _gl.CreateBuffer();
        _gl.BindBuffer(GlConst.ArrayBuffer, _fullScreenBuffer);
        _gl.BufferData(GlConst.ArrayBuffer, vertices, GlConst.StaticDraw);

        if (_gl.SupportsVertexArrays)
        {
            _fullScreenVertexArray = _gl.CreateVertexArray();
            _gl.BindVertexArray(_fullScreenVertexArray);
            _gl.BindBuffer(GlConst.ArrayBuffer, _fullScreenBuffer);
            _gl.EnableVertexAttribArray(_backgroundProgram.Position);
            _gl.VertexAttribPointer(_backgroundProgram.Position, 2, GlConst.Float, false, 0, 0);
            _gl.BindVertexArray(0);
        }
    }

    private void DrawBackground()
    {
        _gl.UseProgram(_backgroundProgram.Program);
        SetColour(_backgroundProgram.Top, ViewportPalette.BackgroundTop);
        SetColour(_backgroundProgram.Bottom, ViewportPalette.BackgroundBottom);

        if (_fullScreenVertexArray != 0)
        {
            _gl.BindVertexArray(_fullScreenVertexArray);
        }
        else
        {
            _gl.BindBuffer(GlConst.ArrayBuffer, _fullScreenBuffer);
            _gl.EnableVertexAttribArray(_backgroundProgram.Position);
            _gl.VertexAttribPointer(_backgroundProgram.Position, 2, GlConst.Float, false, 0, 0);
        }

        _gl.DrawArrays(GlConst.Triangles, 0, 3);
    }

    private void DrawGrid(in Matrix4x4 viewProjection)
    {
        if (_grid is null)
        {
            return;
        }

        _gl.UseProgram(_lineProgram.Program);
        _gl.UniformMatrix4(_lineProgram.ViewProjection, viewProjection);
        _gl.Uniform1f(_lineProgram.DepthBias, 0f);
        _gl.Uniform1f(_lineProgram.UseUniformColour, 0f);
        _grid.BindForDraw(_gl, _lineProgram);
        _gl.DrawArrays(GlConst.Lines, 0, _grid.VertexCount);
    }

    private void DrawGeometry(RenderPackage[] packages, Camera camera, in Matrix4x4 viewProjection)
    {
        Vector3 eye = camera.Position;

        // The light is fixed relative to the camera rather than to the world, at the top-left,
        // matching the interface's own fixed light source (design language §3). A world-fixed
        // light leaves a face unlit no matter how the user orbits, which reads as a hole.
        Vector3 forward = Vector3.Normalize(camera.Target - eye);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Camera.WorldUp));
        Vector3 up = Vector3.Cross(right, forward);
        Vector3 keyLight = Vector3.Normalize((-forward * 0.55f) + (right * -0.55f) + (up * 0.62f));

        _gl.UseProgram(_meshProgram.Program);
        _gl.UniformMatrix4(_meshProgram.ViewProjection, viewProjection);
        _gl.Uniform3f(_meshProgram.Eye, eye.X, eye.Y, eye.Z);
        _gl.Uniform3f(_meshProgram.KeyLight, keyLight.X, keyLight.Y, keyLight.Z);

        foreach (RenderPackage package in packages)
        {
            if (package.Appearance.IsGhosted || package.TriangleCount == 0)
            {
                continue;
            }

            if (!_uploaded.TryGetValue(package.Key, out GpuGeometry? geometry))
            {
                continue;
            }

            ViewportColor surface = package.Appearance.Surface;
            if (package.Appearance.IsSelected)
            {
                // A 15% accent lighting tint, never on its own: the accent-coloured edge drawn
                // below is the authoritative selection signal (design language §8.3).
                surface = Blend(surface, ViewportPalette.Accent, 0.15f);
            }

            SetColour(_meshProgram.Surface, surface);
            geometry.BindForTriangles(_gl, _meshProgram);
            _gl.DrawElements(GlConst.Triangles, geometry.TriangleIndexCount, GlConst.UnsignedInt, 0);
        }

        _gl.UseProgram(_lineProgram.Program);
        _gl.UniformMatrix4(_lineProgram.ViewProjection, viewProjection);
        _gl.Uniform1f(_lineProgram.DepthBias, 0.0006f);
        _gl.Uniform1f(_lineProgram.UseUniformColour, 1f);

        foreach (RenderPackage package in packages)
        {
            if (package.EdgeCount == 0 || !_uploaded.TryGetValue(package.Key, out GpuGeometry? geometry))
            {
                continue;
            }

            ViewportColor edge = package.Appearance.IsSelected
                ? ViewportPalette.Accent
                : package.Appearance.IsGhosted
                    ? ViewportPalette.GeometryGhost
                    : package.Appearance.Edge;

            SetColour(_lineProgram.Colour, edge);
            geometry.BindForEdges(_gl, _lineProgram);
            _gl.DrawElements(GlConst.Lines, geometry.EdgeIndexCount, GlConst.UnsignedInt, 0);
        }
    }

    private void Reconcile(RenderPackage[] packages)
    {
        foreach (RenderPackage package in packages)
        {
            if (_uploaded.TryGetValue(package.Key, out GpuGeometry? existing))
            {
                if (ReferenceEquals(existing.Source, package)
                    || (ReferenceEquals(existing.Source.PositionData, package.PositionData)
                        && ReferenceEquals(existing.Source.IndexData, package.IndexData)))
                {
                    // Same geometry, possibly a new appearance. Appearance is a uniform, not a
                    // buffer, so there is nothing to re-upload.
                    existing.Source = package;
                    continue;
                }

                existing.Delete(_gl);
                _uploaded.Remove(package.Key);
            }

            _uploaded[package.Key] = GpuGeometry.Upload(_gl, package, _meshProgram, _lineProgram);
        }

        _live.Clear();
        foreach (RenderPackage package in packages)
        {
            _live.Add(package.Key);
        }

        _doomed.Clear();
        foreach (GeometryKey key in _uploaded.Keys)
        {
            if (!_live.Contains(key))
            {
                _doomed.Add(key);
            }
        }

        foreach (GeometryKey key in _doomed)
        {
            _uploaded[key].Delete(_gl);
            _uploaded.Remove(key);
        }
    }

    private void SetColour(int location, ViewportColor colour) =>
        _gl.Uniform4f(location, colour.R, colour.G, colour.B, colour.A);

    private static ViewportColor Blend(ViewportColor from, ViewportColor to, float amount) => new(
        from.R + ((to.R - from.R) * amount),
        from.G + ((to.G - from.G) * amount),
        from.B + ((to.B - from.B) * amount),
        from.A);

    private readonly struct MeshProgram
    {
        private MeshProgram(
            int program, int position, int normal, int viewProjection, int eye, int keyLight, int surface)
        {
            Program = program;
            Position = position;
            Normal = normal;
            ViewProjection = viewProjection;
            Eye = eye;
            KeyLight = keyLight;
            Surface = surface;
        }

        internal int Program { get; }

        internal int Position { get; }

        internal int Normal { get; }

        internal int ViewProjection { get; }

        internal int Eye { get; }

        internal int KeyLight { get; }

        internal int Surface { get; }

        internal static MeshProgram Bind(IGlApi gl, int program) => new(
            program,
            gl.GetAttribLocation(program, "aPosition"),
            gl.GetAttribLocation(program, "aNormal"),
            gl.GetUniformLocation(program, "uViewProjection"),
            gl.GetUniformLocation(program, "uEye"),
            gl.GetUniformLocation(program, "uKeyLight"),
            gl.GetUniformLocation(program, "uSurface"));
    }

    private readonly struct LineProgram
    {
        private LineProgram(
            int program,
            int position,
            int colourAttribute,
            int viewProjection,
            int depthBias,
            int colour,
            int useUniformColour)
        {
            Program = program;
            Position = position;
            ColourAttribute = colourAttribute;
            ViewProjection = viewProjection;
            DepthBias = depthBias;
            Colour = colour;
            UseUniformColour = useUniformColour;
        }

        internal int Program { get; }

        internal int Position { get; }

        internal int ColourAttribute { get; }

        internal int ViewProjection { get; }

        internal int DepthBias { get; }

        internal int Colour { get; }

        internal int UseUniformColour { get; }

        internal static LineProgram Bind(IGlApi gl, int program) => new(
            program,
            gl.GetAttribLocation(program, "aPosition"),
            gl.GetAttribLocation(program, "aColour"),
            gl.GetUniformLocation(program, "uViewProjection"),
            gl.GetUniformLocation(program, "uDepthBias"),
            gl.GetUniformLocation(program, "uColour"),
            gl.GetUniformLocation(program, "uUseUniformColour"));
    }

    private readonly struct BackgroundProgram
    {
        private BackgroundProgram(int program, int position, int top, int bottom)
        {
            Program = program;
            Position = position;
            Top = top;
            Bottom = bottom;
        }

        internal int Program { get; }

        internal int Position { get; }

        internal int Top { get; }

        internal int Bottom { get; }

        internal static BackgroundProgram Bind(IGlApi gl, int program) => new(
            program,
            gl.GetAttribLocation(program, "aPosition"),
            gl.GetUniformLocation(program, "uTop"),
            gl.GetUniformLocation(program, "uBottom"));
    }

    private sealed class GpuGeometry
    {
        private int _positionBuffer;
        private int _normalBuffer;
        private int _triangleIndexBuffer;
        private int _edgeIndexBuffer;
        private int _triangleVertexArray;
        private int _edgeVertexArray;

        private GpuGeometry(RenderPackage source) => Source = source;

        internal RenderPackage Source { get; set; }

        internal int TriangleIndexCount { get; private set; }

        internal int EdgeIndexCount { get; private set; }

        internal static GpuGeometry Upload(IGlApi gl, RenderPackage package, in MeshProgram mesh, in LineProgram line)
        {
            GpuGeometry geometry = new(package)
            {
                TriangleIndexCount = package.IndexData.Length,
                EdgeIndexCount = package.EdgeIndexData.Length,
                _positionBuffer = gl.CreateBuffer(),
            };

            gl.BindBuffer(GlConst.ArrayBuffer, geometry._positionBuffer);
            gl.BufferData(GlConst.ArrayBuffer, package.PositionData, GlConst.StaticDraw);

            if (package.NormalData.Length != 0)
            {
                geometry._normalBuffer = gl.CreateBuffer();
                gl.BindBuffer(GlConst.ArrayBuffer, geometry._normalBuffer);
                gl.BufferData(GlConst.ArrayBuffer, package.NormalData, GlConst.StaticDraw);
            }

            if (geometry.TriangleIndexCount != 0)
            {
                geometry._triangleIndexBuffer = gl.CreateBuffer();
                gl.BindBuffer(GlConst.ElementArrayBuffer, geometry._triangleIndexBuffer);
                gl.BufferData(GlConst.ElementArrayBuffer, package.IndexData, GlConst.StaticDraw);
            }

            if (geometry.EdgeIndexCount != 0)
            {
                geometry._edgeIndexBuffer = gl.CreateBuffer();
                gl.BindBuffer(GlConst.ElementArrayBuffer, geometry._edgeIndexBuffer);
                gl.BufferData(GlConst.ElementArrayBuffer, package.EdgeIndexData, GlConst.StaticDraw);
            }

            if (gl.SupportsVertexArrays)
            {
                if (geometry.TriangleIndexCount != 0)
                {
                    geometry._triangleVertexArray = gl.CreateVertexArray();
                    gl.BindVertexArray(geometry._triangleVertexArray);
                    geometry.SetUpTriangleAttributes(gl, mesh);
                    gl.BindVertexArray(0);
                }

                if (geometry.EdgeIndexCount != 0)
                {
                    geometry._edgeVertexArray = gl.CreateVertexArray();
                    gl.BindVertexArray(geometry._edgeVertexArray);
                    geometry.SetUpEdgeAttributes(gl, line);
                    gl.BindVertexArray(0);
                }
            }

            return geometry;
        }

        internal void BindForTriangles(IGlApi gl, in MeshProgram mesh)
        {
            if (_triangleVertexArray != 0)
            {
                gl.BindVertexArray(_triangleVertexArray);
                return;
            }

            SetUpTriangleAttributes(gl, mesh);
        }

        internal void BindForEdges(IGlApi gl, in LineProgram line)
        {
            if (_edgeVertexArray != 0)
            {
                gl.BindVertexArray(_edgeVertexArray);
                return;
            }

            SetUpEdgeAttributes(gl, line);
        }

        internal void Delete(IGlApi gl)
        {
            DeleteVertexArray(gl, ref _triangleVertexArray);
            DeleteVertexArray(gl, ref _edgeVertexArray);
            DeleteBuffer(gl, ref _positionBuffer);
            DeleteBuffer(gl, ref _normalBuffer);
            DeleteBuffer(gl, ref _triangleIndexBuffer);
            DeleteBuffer(gl, ref _edgeIndexBuffer);
        }

        private static void DeleteBuffer(IGlApi gl, ref int buffer)
        {
            if (buffer != 0)
            {
                gl.DeleteBuffer(buffer);
                buffer = 0;
            }
        }

        private static void DeleteVertexArray(IGlApi gl, ref int vertexArray)
        {
            if (vertexArray != 0)
            {
                gl.DeleteVertexArray(vertexArray);
                vertexArray = 0;
            }
        }

        private void SetUpTriangleAttributes(IGlApi gl, in MeshProgram mesh)
        {
            gl.BindBuffer(GlConst.ArrayBuffer, _positionBuffer);
            gl.EnableVertexAttribArray(mesh.Position);
            gl.VertexAttribPointer(mesh.Position, 3, GlConst.Float, false, 0, 0);

            if (_normalBuffer != 0 && mesh.Normal >= 0)
            {
                gl.BindBuffer(GlConst.ArrayBuffer, _normalBuffer);
                gl.EnableVertexAttribArray(mesh.Normal);
                gl.VertexAttribPointer(mesh.Normal, 3, GlConst.Float, false, 0, 0);
            }

            gl.BindBuffer(GlConst.ElementArrayBuffer, _triangleIndexBuffer);
        }

        private void SetUpEdgeAttributes(IGlApi gl, in LineProgram line)
        {
            gl.BindBuffer(GlConst.ArrayBuffer, _positionBuffer);
            gl.EnableVertexAttribArray(line.Position);
            gl.VertexAttribPointer(line.Position, 3, GlConst.Float, false, 0, 0);
            gl.BindBuffer(GlConst.ElementArrayBuffer, _edgeIndexBuffer);
        }
    }

    private sealed class GpuLines
    {
        private int _positionBuffer;
        private int _colourBuffer;
        private int _vertexArray;

        internal int VertexCount { get; private set; }

        internal static GpuLines Upload(IGlApi gl, LineBatch batch)
        {
            GpuLines lines = new()
            {
                VertexCount = batch.VertexCount,
                _positionBuffer = gl.CreateBuffer(),
                _colourBuffer = gl.CreateBuffer(),
            };

            gl.BindBuffer(GlConst.ArrayBuffer, lines._positionBuffer);
            gl.BufferData(GlConst.ArrayBuffer, batch.PositionData, GlConst.StaticDraw);
            gl.BindBuffer(GlConst.ArrayBuffer, lines._colourBuffer);
            gl.BufferData(GlConst.ArrayBuffer, batch.ColourData, GlConst.StaticDraw);
            return lines;
        }

        internal void BindForDraw(IGlApi gl, in LineProgram line)
        {
            if (_vertexArray == 0 && gl.SupportsVertexArrays)
            {
                _vertexArray = gl.CreateVertexArray();
                gl.BindVertexArray(_vertexArray);
                SetUpAttributes(gl, line);
                return;
            }

            if (_vertexArray != 0)
            {
                gl.BindVertexArray(_vertexArray);
                return;
            }

            SetUpAttributes(gl, line);
        }

        internal void Delete(IGlApi gl)
        {
            if (_vertexArray != 0)
            {
                gl.DeleteVertexArray(_vertexArray);
                _vertexArray = 0;
            }

            if (_positionBuffer != 0)
            {
                gl.DeleteBuffer(_positionBuffer);
                _positionBuffer = 0;
            }

            if (_colourBuffer != 0)
            {
                gl.DeleteBuffer(_colourBuffer);
                _colourBuffer = 0;
            }
        }

        private void SetUpAttributes(IGlApi gl, in LineProgram line)
        {
            gl.BindBuffer(GlConst.ArrayBuffer, _positionBuffer);
            gl.EnableVertexAttribArray(line.Position);
            gl.VertexAttribPointer(line.Position, 3, GlConst.Float, false, 0, 0);

            if (line.ColourAttribute >= 0)
            {
                gl.BindBuffer(GlConst.ArrayBuffer, _colourBuffer);
                gl.EnableVertexAttribArray(line.ColourAttribute);
                gl.VertexAttribPointer(line.ColourAttribute, 4, GlConst.Float, false, 0, 0);
            }
        }
    }
}
