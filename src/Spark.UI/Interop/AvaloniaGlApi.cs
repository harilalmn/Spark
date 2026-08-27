using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using Spark.Viewport.OpenGL;

namespace Spark.UI.Interop;

/// <summary>
/// Implements <see cref="IGlApi"/> over Avalonia's <see cref="GlInterface"/>. This is the whole of
/// the adaptation between the Avalonia-free renderer and the framework, and it lives in
/// <c>Spark.UI</c> because <c>Spark.Viewport</c> may not reference Avalonia
/// (<c>Spark.Architecture.Tests</c> enforces it).
/// </summary>
/// <remarks>
/// <para>
/// <b>Three entry points are bound by hand.</b> <c>glUniformMatrix4fv</c> is exposed by
/// <see cref="GlInterface"/> only through a pointer signature, and <c>glUniform3f</c> and
/// <c>glUniform4f</c> are not exposed at all — so all three are resolved through
/// <see cref="GlInterface.GetProcAddress(string)"/> and marshalled as delegates. That is safe
/// code: <see cref="Marshal.GetDelegateForFunctionPointer{TDelegate}(IntPtr)"/> needs no
/// <c>unsafe</c> block, which matters because <c>AllowUnsafeBlocks</c> is false repository-wide.
/// All three are OpenGL 2.0 / ES 2.0 core, so they resolve through both WGL and EGL.
/// </para>
/// <para>
/// <b>Buffer uploads pin.</b> A managed array is pinned with a <see cref="GCHandle"/> for the
/// duration of the <c>glBufferData</c> call and released immediately afterwards. Pinning for one
/// synchronous call does not fragment the heap in any way that matters, and <c>glBufferData</c>
/// copies before it returns.
/// </para>
/// </remarks>
public sealed class AvaloniaGlApi : IGlApi
{
    private readonly GlInterface _gl;
    private readonly UniformMatrix4Fv? _uniformMatrix4;
    private readonly Uniform3F? _uniform3;
    private readonly Uniform4F? _uniform4;
    private readonly ReadPixelsDelegate? _readPixels;

    /// <summary>Wraps a live GL interface.</summary>
    /// <param name="gl">Avalonia's GL entry points for the current context.</param>
    /// <param name="version">The version Avalonia's control reports for that context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="gl"/> is null.</exception>
    public AvaloniaGlApi(GlInterface gl, GlVersion version)
    {
        ArgumentNullException.ThrowIfNull(gl);

        _gl = gl;
        Dialect = DialectFor(version);
        SupportsVertexArrays = gl.IsGenVertexArraysAvailable && gl.IsBindVertexArrayAvailable;

        _uniformMatrix4 = Resolve<UniformMatrix4Fv>("glUniformMatrix4fv");
        _uniform3 = Resolve<Uniform3F>("glUniform3f");
        _uniform4 = Resolve<Uniform4F>("glUniform4f");
        _readPixels = Resolve<ReadPixelsDelegate>("glReadPixels");

        MissingEntryPoints = Describe();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void UniformMatrix4Fv(int location, int count, byte transpose, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void Uniform3F(int location, float x, float y, float z);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void Uniform4F(int location, float x, float y, float z, float w);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ReadPixelsDelegate(
        int x, int y, int width, int height, int format, int type, IntPtr pixels);

    /// <inheritdoc/>
    public GlDialect Dialect { get; }

    /// <inheritdoc/>
    public string VersionString => _gl.Version ?? "unknown";

    /// <inheritdoc/>
    public string RendererString => _gl.Renderer ?? "unknown";

    /// <inheritdoc/>
    public bool SupportsVertexArrays { get; }

    /// <summary>
    /// A description of any hand-bound entry point the driver did not provide, or null when all
    /// three resolved. Reported rather than thrown, because a missing entry point is exactly the
    /// kind of failure that only happens on the user's machine.
    /// </summary>
    public string? MissingEntryPoints { get; }

    /// <summary>Maps Avalonia's reported GL version to the GLSL dialect the shaders must use.</summary>
    /// <param name="version">The version reported for the context.</param>
    /// <returns>
    /// The dialect. An ES context of 3.0 or better takes <see cref="GlDialect.Es300"/>, an older
    /// one <see cref="GlDialect.Es100"/>, and a desktop context 3.3 or better
    /// <see cref="GlDialect.Core330"/>. Anything else falls back to <see cref="GlDialect.Es100"/>,
    /// which is the dialect a GL 2.1 context also accepts.
    /// </returns>
    public static GlDialect DialectFor(GlVersion version)
    {
        if (version.Type == GlProfileType.OpenGLES)
        {
            return version.Major >= 3 ? GlDialect.Es300 : GlDialect.Es100;
        }

        bool atLeast33 = version.Major > 3 || (version.Major == 3 && version.Minor >= 3);
        return atLeast33 ? GlDialect.Core330 : GlDialect.Es100;
    }

    /// <inheritdoc/>
    public void Enable(int capability) => _gl.Enable(capability);

    /// <inheritdoc/>
    public void Disable(int capability) => _gl.Disable(capability);

    /// <inheritdoc/>
    public void ClearColor(float r, float g, float b, float a) => _gl.ClearColor(r, g, b, a);

    /// <inheritdoc/>
    public void Clear(int mask) => _gl.Clear(mask);

    /// <inheritdoc/>
    public void Viewport(int x, int y, int width, int height) => _gl.Viewport(x, y, width, height);

    /// <inheritdoc/>
    public void DepthFunc(int function) => _gl.DepthFunc(function);

    /// <inheritdoc/>
    public void DepthMask(bool enabled) => _gl.DepthMask(enabled ? 1 : 0);

    /// <inheritdoc/>
    public void BlendFunc(int sourceFactor, int destinationFactor)
    {
        // Blending is not used by the current shading path; declared on the interface so a future
        // transparent-appearance package does not have to widen it.
    }

    /// <inheritdoc/>
    public void BindFramebuffer(int target, int framebuffer) => _gl.BindFramebuffer(target, framebuffer);

    /// <inheritdoc/>
    public int CompileShader(int shaderType, string source, out string? error)
    {
        int shader = _gl.CreateShader(shaderType);
        error = _gl.CompileShaderAndGetError(shader, source);

        if (string.IsNullOrEmpty(error))
        {
            error = null;
            return shader;
        }

        _gl.DeleteShader(shader);
        return 0;
    }

    /// <inheritdoc/>
    public int LinkProgram(int vertexShader, int fragmentShader, out string? error)
    {
        int program = _gl.CreateProgram();
        _gl.AttachShader(program, vertexShader);
        _gl.AttachShader(program, fragmentShader);
        error = _gl.LinkProgramAndGetError(program);

        if (string.IsNullOrEmpty(error))
        {
            error = null;
            return program;
        }

        _gl.DeleteProgram(program);
        return 0;
    }

    /// <inheritdoc/>
    public void DeleteShader(int shader) => _gl.DeleteShader(shader);

    /// <inheritdoc/>
    public void DeleteProgram(int program) => _gl.DeleteProgram(program);

    /// <inheritdoc/>
    public void UseProgram(int program) => _gl.UseProgram(program);

    /// <inheritdoc/>
    public int GetUniformLocation(int program, string name) => _gl.GetUniformLocationString(program, name);

    /// <inheritdoc/>
    public int GetAttribLocation(int program, string name) => _gl.GetAttribLocationString(program, name);

    /// <inheritdoc/>
    public void Uniform1f(int location, float value) => _gl.Uniform1f(location, value);

    /// <inheritdoc/>
    public void Uniform3f(int location, float x, float y, float z) => _uniform3?.Invoke(location, x, y, z);

    /// <inheritdoc/>
    public void Uniform4f(int location, float x, float y, float z, float w) =>
        _uniform4?.Invoke(location, x, y, z, w);

    /// <inheritdoc/>
    public void UniformMatrix4(int location, in Matrix4x4 value)
    {
        if (_uniformMatrix4 is null)
        {
            return;
        }

        // System.Numerics stores a row-vector matrix in row-major order. Written out in field
        // order that byte layout is exactly the column-major form GLSL wants for the equivalent
        // column-vector matrix, so transpose stays false. Setting it would mirror the scene.
        float[] elements =
        [
            value.M11, value.M12, value.M13, value.M14,
            value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34,
            value.M41, value.M42, value.M43, value.M44,
        ];

        GCHandle handle = GCHandle.Alloc(elements, GCHandleType.Pinned);
        try
        {
            _uniformMatrix4(location, 1, 0, handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    /// <inheritdoc/>
    public int CreateBuffer() => _gl.GenBuffer();

    /// <inheritdoc/>
    public void DeleteBuffer(int buffer) => _gl.DeleteBuffer(buffer);

    /// <inheritdoc/>
    public void BindBuffer(int target, int buffer) => _gl.BindBuffer(target, buffer);

    /// <inheritdoc/>
    public void BufferData(int target, float[] data, int usage)
    {
        ArgumentNullException.ThrowIfNull(data);
        Upload(target, data, data.Length * sizeof(float), usage);
    }

    /// <inheritdoc/>
    public void BufferData(int target, int[] data, int usage)
    {
        ArgumentNullException.ThrowIfNull(data);
        Upload(target, data, data.Length * sizeof(int), usage);
    }

    /// <inheritdoc/>
    public int CreateVertexArray() => _gl.GenVertexArray();

    /// <inheritdoc/>
    public void BindVertexArray(int vertexArray) => _gl.BindVertexArray(vertexArray);

    /// <inheritdoc/>
    public void DeleteVertexArray(int vertexArray) => _gl.DeleteVertexArray(vertexArray);

    /// <inheritdoc/>
    public void EnableVertexAttribArray(int index)
    {
        if (index >= 0)
        {
            _gl.EnableVertexAttribArray(index);
        }
    }

    /// <inheritdoc/>
    public void VertexAttribPointer(
        int index, int size, int type, bool normalized, int strideBytes, int offsetBytes)
    {
        if (index >= 0)
        {
            _gl.VertexAttribPointer(
                index, size, type, normalized ? 1 : 0, strideBytes, new IntPtr(offsetBytes));
        }
    }

    /// <inheritdoc/>
    public void DrawArrays(int mode, int first, int count) => _gl.DrawArrays(mode, first, new IntPtr(count));

    /// <inheritdoc/>
    public bool ReadPixels(int x, int y, int width, int height, byte[] rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);

        if (_readPixels is null || rgba.Length < width * height * 4)
        {
            return false;
        }

        const int GlRgba = 0x1908;
        const int GlUnsignedByte = 0x1401;

        GCHandle handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            _readPixels(x, y, width, height, GlRgba, GlUnsignedByte, handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }

        return true;
    }

    /// <inheritdoc/>
    public void DrawElements(int mode, int count, int type, int offsetBytes) =>
        _gl.DrawElements(mode, count, type, new IntPtr(offsetBytes));

    private void Upload(int target, Array data, int byteCount, int usage)
    {
        if (byteCount == 0)
        {
            return;
        }

        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            _gl.BufferData(target, new IntPtr(byteCount), handle.AddrOfPinnedObject(), usage);
        }
        finally
        {
            handle.Free();
        }
    }

    private T? Resolve<T>(string name)
        where T : Delegate
    {
        IntPtr address = _gl.GetProcAddress(name);
        return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private string? Describe()
    {
        System.Collections.Generic.List<string> missing = [];

        if (_uniformMatrix4 is null)
        {
            missing.Add("glUniformMatrix4fv");
        }

        if (_uniform3 is null)
        {
            missing.Add("glUniform3f");
        }

        if (_uniform4 is null)
        {
            missing.Add("glUniform4f");
        }

        return missing.Count == 0 ? null : string.Join(", ", missing);
    }
}
