namespace Spark.Viewport.OpenGL;

/// <summary>
/// Which GLSL dialect the shaders must be written in. There is no single source string that
/// compiles everywhere: a desktop core profile rejects <c>attribute</c>, an ES 2.0 context rejects
/// <c>in</c>, and neither accepts the other's <c>#version</c> line.
/// </summary>
/// <remarks>
/// This matters more on Windows than it looks. Avalonia's default Windows rendering mode is ANGLE,
/// which presents as OpenGL <b>ES</b> over Direct3D rather than as desktop GL, so a shader written
/// only for <c>#version 330 core</c> compiles in a Linux development build and fails on the
/// platform the product actually ships to.
/// </remarks>
public enum GlDialect
{
    /// <summary>
    /// OpenGL ES 2.0 / GLSL ES 1.00: <c>attribute</c>, <c>varying</c>, <c>gl_FragColor</c>. The
    /// floor, taken when nothing better is reported.
    /// </summary>
    Es100,

    /// <summary>OpenGL ES 3.0 / GLSL ES 3.00: <c>in</c>, <c>out</c>, an explicit output variable.</summary>
    Es300,

    /// <summary>Desktop OpenGL 3.3 core / GLSL 3.30.</summary>
    Core330,
}
