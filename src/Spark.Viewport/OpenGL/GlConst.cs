namespace Spark.Viewport.OpenGL;

/// <summary>
/// The handful of OpenGL enumerant values the renderer uses. Declared here rather than taken from
/// a bindings package because <c>Spark.Viewport</c> has no graphics dependency at all — the whole
/// GL surface it needs is <see cref="IGlApi"/> plus these numbers.
/// </summary>
public static class GlConst
{
    /// <summary><c>GL_DEPTH_BUFFER_BIT</c>.</summary>
    public const int DepthBufferBit = 0x00000100;

    /// <summary><c>GL_COLOR_BUFFER_BIT</c>.</summary>
    public const int ColorBufferBit = 0x00004000;

    /// <summary><c>GL_LINES</c>.</summary>
    public const int Lines = 0x0001;

    /// <summary><c>GL_TRIANGLES</c>.</summary>
    public const int Triangles = 0x0004;

    /// <summary><c>GL_SRC_ALPHA</c>.</summary>
    public const int SrcAlpha = 0x0302;

    /// <summary><c>GL_ONE_MINUS_SRC_ALPHA</c>.</summary>
    public const int OneMinusSrcAlpha = 0x0303;

    /// <summary><c>GL_DEPTH_TEST</c>.</summary>
    public const int DepthTest = 0x0B71;

    /// <summary><c>GL_BLEND</c>.</summary>
    public const int Blend = 0x0BE2;

    /// <summary><c>GL_LEQUAL</c>.</summary>
    public const int LessEqual = 0x0203;

    /// <summary><c>GL_FLOAT</c>.</summary>
    public const int Float = 0x1406;

    /// <summary><c>GL_UNSIGNED_INT</c>.</summary>
    public const int UnsignedInt = 0x1405;

    /// <summary><c>GL_ARRAY_BUFFER</c>.</summary>
    public const int ArrayBuffer = 0x8892;

    /// <summary><c>GL_ELEMENT_ARRAY_BUFFER</c>.</summary>
    public const int ElementArrayBuffer = 0x8893;

    /// <summary><c>GL_STATIC_DRAW</c>.</summary>
    public const int StaticDraw = 0x88E4;

    /// <summary><c>GL_FRAGMENT_SHADER</c>.</summary>
    public const int FragmentShader = 0x8B30;

    /// <summary><c>GL_VERTEX_SHADER</c>.</summary>
    public const int VertexShader = 0x8B31;

    /// <summary><c>GL_FRAMEBUFFER</c>.</summary>
    public const int Framebuffer = 0x8D40;
}
