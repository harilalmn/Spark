using System.Numerics;

namespace Spark.Viewport.OpenGL;

/// <summary>
/// The only OpenGL entry points <see cref="OpenGlViewportRenderer"/> needs, expressed without any
/// pointer types. This interface is the seam that keeps <c>Spark.Viewport</c> free of Avalonia:
/// <c>Spark.UI</c> implements it over Avalonia's <c>GlInterface</c>, and a test can implement it
/// over nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why so small.</b> Every function here is OpenGL 2.0 / OpenGL ES 2.0 core. That is not
/// minimalism for its own sake: on Windows a WGL context resolves nothing older than 1.2 through
/// <c>wglGetProcAddress</c>, so anything from GL 1.x has to come from the platform's own export
/// table and is the first thing to be missing on an unusual driver. Culling, line width and
/// polygon offset were all designed out for that reason — the renderer shades two-sided, draws
/// every line at one pixel, and biases edge depth in the vertex shader instead.
/// </para>
/// <para>
/// <b>Arrays, not spans.</b> Buffer uploads take arrays because an implementation has to pin the
/// storage to hand GL an address, and a <c>ReadOnlySpan</c> cannot be pinned without unsafe code,
/// which is forbidden repository-wide.
/// </para>
/// </remarks>
public interface IGlApi
{
    /// <summary>The GLSL dialect the compiled shaders must be written in.</summary>
    GlDialect Dialect { get; }

    /// <summary>The driver's version string, for the diagnostic readout.</summary>
    string VersionString { get; }

    /// <summary>The driver's renderer string, for the diagnostic readout.</summary>
    string RendererString { get; }

    /// <summary>
    /// True when vertex array objects are usable. They are core in GL 3.0 and ES 3.0 but only an
    /// extension in ES 2.0, so the renderer has to be able to run without them.
    /// </summary>
    bool SupportsVertexArrays { get; }

    /// <summary>Enables a capability. See <see cref="GlConst"/>.</summary>
    /// <param name="capability">The capability token.</param>
    void Enable(int capability);

    /// <summary>Disables a capability.</summary>
    /// <param name="capability">The capability token.</param>
    void Disable(int capability);

    /// <summary>Sets the colour the next <see cref="Clear"/> writes.</summary>
    /// <param name="r">Red, 0..1.</param>
    /// <param name="g">Green, 0..1.</param>
    /// <param name="b">Blue, 0..1.</param>
    /// <param name="a">Alpha, 0..1.</param>
    void ClearColor(float r, float g, float b, float a);

    /// <summary>Clears the named buffers.</summary>
    /// <param name="mask">A combination of <see cref="GlConst.ColorBufferBit"/> and friends.</param>
    void Clear(int mask);

    /// <summary>Sets the viewport rectangle in pixels.</summary>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Bottom edge.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    void Viewport(int x, int y, int width, int height);

    /// <summary>Sets the depth comparison function.</summary>
    /// <param name="function">The comparison token, such as <see cref="GlConst.LessEqual"/>.</param>
    void DepthFunc(int function);

    /// <summary>Enables or disables writes to the depth buffer.</summary>
    /// <param name="enabled">True to write depth.</param>
    void DepthMask(bool enabled);

    /// <summary>Sets the source and destination blend factors.</summary>
    /// <param name="sourceFactor">The source factor token.</param>
    /// <param name="destinationFactor">The destination factor token.</param>
    void BlendFunc(int sourceFactor, int destinationFactor);

    /// <summary>Binds a framebuffer. Avalonia hands the control its target FBO every frame.</summary>
    /// <param name="target">The framebuffer target token.</param>
    /// <param name="framebuffer">The framebuffer name.</param>
    void BindFramebuffer(int target, int framebuffer);

    /// <summary>
    /// Compiles a shader from source and links nothing.
    /// </summary>
    /// <param name="shaderType">
    /// <see cref="GlConst.VertexShader"/> or <see cref="GlConst.FragmentShader"/>.
    /// </param>
    /// <param name="source">The complete shader source, including its <c>#version</c> line.</param>
    /// <param name="error">The compiler's message when compilation fails, otherwise null.</param>
    /// <returns>The shader name, or zero when compilation failed.</returns>
    int CompileShader(int shaderType, string source, out string? error);

    /// <summary>Links a program from two compiled shaders.</summary>
    /// <param name="vertexShader">The compiled vertex shader.</param>
    /// <param name="fragmentShader">The compiled fragment shader.</param>
    /// <param name="error">The linker's message when linking fails, otherwise null.</param>
    /// <returns>The program name, or zero when linking failed.</returns>
    int LinkProgram(int vertexShader, int fragmentShader, out string? error);

    /// <summary>Deletes a shader.</summary>
    /// <param name="shader">The shader name.</param>
    void DeleteShader(int shader);

    /// <summary>Deletes a program.</summary>
    /// <param name="program">The program name.</param>
    void DeleteProgram(int program);

    /// <summary>Makes a program current.</summary>
    /// <param name="program">The program name.</param>
    void UseProgram(int program);

    /// <summary>Looks up a uniform location.</summary>
    /// <param name="program">The program name.</param>
    /// <param name="name">The uniform's name in the shader.</param>
    /// <returns>The location, or −1 when the uniform was optimised out.</returns>
    int GetUniformLocation(int program, string name);

    /// <summary>Looks up a vertex attribute location.</summary>
    /// <param name="program">The program name.</param>
    /// <param name="name">The attribute's name in the shader.</param>
    /// <returns>The location, or −1 when the attribute is unused.</returns>
    int GetAttribLocation(int program, string name);

    /// <summary>Sets a <c>float</c> uniform.</summary>
    /// <param name="location">The uniform location.</param>
    /// <param name="value">The value.</param>
    void Uniform1f(int location, float value);

    /// <summary>Sets a <c>vec3</c> uniform.</summary>
    /// <param name="location">The uniform location.</param>
    /// <param name="x">The first component.</param>
    /// <param name="y">The second component.</param>
    /// <param name="z">The third component.</param>
    void Uniform3f(int location, float x, float y, float z);

    /// <summary>Sets a <c>vec4</c> uniform.</summary>
    /// <param name="location">The uniform location.</param>
    /// <param name="x">The first component.</param>
    /// <param name="y">The second component.</param>
    /// <param name="z">The third component.</param>
    /// <param name="w">The fourth component.</param>
    void Uniform4f(int location, float x, float y, float z, float w);

    /// <summary>
    /// Sets a <c>mat4</c> uniform from a <see cref="Matrix4x4"/>.
    /// </summary>
    /// <param name="location">The uniform location.</param>
    /// <param name="value">
    /// The matrix, in <c>System.Numerics</c>' row-vector convention. Implementations must upload
    /// its fields in declaration order with <c>transpose</c> false: that byte layout is exactly
    /// the column-major form GLSL expects for the equivalent column-vector matrix, so no
    /// transpose is needed and adding one silently mirrors the scene.
    /// </param>
    void UniformMatrix4(int location, in Matrix4x4 value);

    /// <summary>Creates a buffer object.</summary>
    /// <returns>The buffer name.</returns>
    int CreateBuffer();

    /// <summary>Deletes a buffer object.</summary>
    /// <param name="buffer">The buffer name.</param>
    void DeleteBuffer(int buffer);

    /// <summary>Binds a buffer to a target.</summary>
    /// <param name="target">The target token.</param>
    /// <param name="buffer">The buffer name, or zero to unbind.</param>
    void BindBuffer(int target, int buffer);

    /// <summary>Uploads float data to the bound buffer.</summary>
    /// <param name="target">The target token.</param>
    /// <param name="data">The data. An empty array uploads nothing.</param>
    /// <param name="usage">The usage hint, such as <see cref="GlConst.StaticDraw"/>.</param>
    void BufferData(int target, float[] data, int usage);

    /// <summary>Uploads integer index data to the bound buffer.</summary>
    /// <param name="target">The target token.</param>
    /// <param name="data">The data. An empty array uploads nothing.</param>
    /// <param name="usage">The usage hint.</param>
    void BufferData(int target, int[] data, int usage);

    /// <summary>Creates a vertex array object. Only valid when <see cref="SupportsVertexArrays"/>.</summary>
    /// <returns>The vertex array name.</returns>
    int CreateVertexArray();

    /// <summary>Binds a vertex array object.</summary>
    /// <param name="vertexArray">The vertex array name, or zero to unbind.</param>
    void BindVertexArray(int vertexArray);

    /// <summary>Deletes a vertex array object.</summary>
    /// <param name="vertexArray">The vertex array name.</param>
    void DeleteVertexArray(int vertexArray);

    /// <summary>Enables a vertex attribute array.</summary>
    /// <param name="index">The attribute location.</param>
    void EnableVertexAttribArray(int index);

    /// <summary>Describes the layout of a vertex attribute in the bound array buffer.</summary>
    /// <param name="index">The attribute location.</param>
    /// <param name="size">Components per vertex.</param>
    /// <param name="type">The component type token, such as <see cref="GlConst.Float"/>.</param>
    /// <param name="normalized">Whether integer components are scaled to 0..1.</param>
    /// <param name="strideBytes">Bytes between consecutive vertices, or zero for tightly packed.</param>
    /// <param name="offsetBytes">Byte offset of the first component within the buffer.</param>
    void VertexAttribPointer(int index, int size, int type, bool normalized, int strideBytes, int offsetBytes);

    /// <summary>Draws from the bound array buffers without an index buffer.</summary>
    /// <param name="mode">The primitive mode token.</param>
    /// <param name="first">The first vertex.</param>
    /// <param name="count">The vertex count.</param>
    void DrawArrays(int mode, int first, int count);

    /// <summary>
    /// Reads the bound framebuffer back into a caller-supplied RGBA byte buffer, bottom row first.
    /// </summary>
    /// <param name="x">Left edge in pixels.</param>
    /// <param name="y">Bottom edge in pixels.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="rgba">
    /// A buffer of at least <c>width * height * 4</c> bytes. Filled with 8-bit RGBA, in OpenGL's
    /// bottom-up row order.
    /// </param>
    /// <returns>False when the driver does not expose <c>glReadPixels</c>.</returns>
    /// <remarks>
    /// This exists so that "the viewport renders" can be a checked fact rather than a claim. A GL
    /// backend that compiles, links and runs can still be drawing nothing, drawing black on black
    /// or drawing back faces, and none of those are visible from the managed side without a
    /// read-back. It is also the seam the CI visual-regression path will use once the software
    /// renderer exists.
    /// </remarks>
    bool ReadPixels(int x, int y, int width, int height, byte[] rgba);

    /// <summary>Draws using the bound element array buffer.</summary>
    /// <param name="mode">The primitive mode token.</param>
    /// <param name="count">The index count.</param>
    /// <param name="type">The index type token, such as <see cref="GlConst.UnsignedInt"/>.</param>
    /// <param name="offsetBytes">Byte offset of the first index within the buffer.</param>
    void DrawElements(int mode, int count, int type, int offsetBytes);
}
