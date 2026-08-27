using System;

namespace Spark.Viewport.OpenGL;

/// <summary>
/// The renderer's GLSL, written once and adapted to whichever dialect the context reports.
/// </summary>
/// <remarks>
/// The bodies are shared and only the preamble changes: a set of <c>#define</c>s maps the three
/// keywords that differ between GLSL ES 1.00, GLSL ES 3.00 and desktop GLSL 3.30. Writing three
/// copies of each shader was the alternative, and three copies is three places for a fix to be
/// applied twice.
/// </remarks>
internal static class GlShaders
{
    internal static string MeshVertex(GlDialect dialect) => Vertex(dialect) + """

        uniform mat4 uViewProjection;

        SPARK_IN vec3 aPosition;
        SPARK_IN vec3 aNormal;

        SPARK_VARY_OUT vec3 vNormal;
        SPARK_VARY_OUT vec3 vWorld;

        void main()
        {
            vNormal = aNormal;
            vWorld = aPosition;
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    internal static string MeshFragment(GlDialect dialect) => Fragment(dialect) + """

        uniform vec3 uEye;
        uniform vec3 uKeyLight;
        uniform vec4 uSurface;

        SPARK_VARY_IN vec3 vNormal;
        SPARK_VARY_IN vec3 vWorld;

        void main()
        {
            vec3 n = normalize(vNormal);
            vec3 v = normalize(uEye - vWorld);

            // Two-sided. A fragment whose normal faces away from the eye is lit with the
            // negated normal rather than culled, so an incoming winding defect shows up as
            // geometry that is shaded oddly instead of geometry that is not there.
            if (dot(n, v) < 0.0)
            {
                n = -n;
            }

            vec3 key = normalize(uKeyLight);
            vec3 fill = normalize(vec3(-0.45, 0.35, 0.30));

            float lambert = 0.26
                + (0.60 * max(dot(n, key), 0.0))
                + (0.16 * max(dot(n, fill), 0.0));

            vec3 halfway = normalize(key + v);
            float specular = pow(max(dot(n, halfway), 0.0), 40.0) * 0.22;

            vec3 lit = min((uSurface.rgb * lambert) + vec3(specular), vec3(1.0));
            SPARK_FRAG = vec4(lit, uSurface.a);
        }
        """;

    internal static string LineVertex(GlDialect dialect) => Vertex(dialect) + """

        uniform mat4 uViewProjection;
        uniform float uDepthBias;

        SPARK_IN vec3 aPosition;
        SPARK_IN vec4 aColour;

        SPARK_VARY_OUT vec4 vColour;

        void main()
        {
            vColour = aColour;
            vec4 clip = uViewProjection * vec4(aPosition, 1.0);

            // Pull edges towards the eye by a constant amount in normalised device depth. This
            // replaces glPolygonOffset, which is an OpenGL 1.1 entry point and therefore the
            // kind of function that is missing exactly on the drivers that need it most.
            clip.z = clip.z - (uDepthBias * clip.w);
            gl_Position = clip;
        }
        """;

    internal static string LineFragment(GlDialect dialect) => Fragment(dialect) + """

        uniform vec4 uColour;
        uniform float uUseUniformColour;

        SPARK_VARY_IN vec4 vColour;

        void main()
        {
            SPARK_FRAG = uUseUniformColour > 0.5 ? uColour : vColour;
        }
        """;

    internal static string BackgroundVertex(GlDialect dialect) => Vertex(dialect) + """

        SPARK_IN vec2 aPosition;
        SPARK_VARY_OUT float vHeight;

        void main()
        {
            vHeight = (aPosition.y * 0.5) + 0.5;
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

    internal static string BackgroundFragment(GlDialect dialect) => Fragment(dialect) + """

        uniform vec4 uTop;
        uniform vec4 uBottom;

        SPARK_VARY_IN float vHeight;

        void main()
        {
            vec3 base = mix(uBottom.rgb, uTop.rgb, vHeight);

            // 1.5% monochrome dither. A gradient from #14171D to #1B1F26 traverses about seven
            // 8-bit code values over the height of the viewport, which bands visibly on a large
            // display; the noise costs one instruction and removes it entirely.
            float noise = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453);
            SPARK_FRAG = vec4(base + ((noise - 0.5) * 0.015), 1.0);
        }
        """;

    /// <summary>
    /// The vertex-shader preamble for a dialect.
    /// </summary>
    /// <remarks>
    /// The <c>precision</c> statement has to come before any declaration that uses the type, which
    /// is why the preamble is written out per dialect rather than appended after a shared block.
    /// Getting that order wrong fails only on an ES context — which on Windows means it fails
    /// under ANGLE and passes on a Linux development machine.
    /// </remarks>
    private static string Vertex(GlDialect dialect) => dialect switch
    {
        GlDialect.Core330 => """
            #version 330 core
            #define SPARK_IN in
            #define SPARK_VARY_OUT out
            """,
        GlDialect.Es300 => """
            #version 300 es
            precision highp float;
            #define SPARK_IN in
            #define SPARK_VARY_OUT out
            """,
        GlDialect.Es100 => """
            #version 100
            precision highp float;
            #define SPARK_IN attribute
            #define SPARK_VARY_OUT varying
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
    };

    /// <summary>The fragment-shader preamble for a dialect.</summary>
    private static string Fragment(GlDialect dialect) => dialect switch
    {
        GlDialect.Core330 => """
            #version 330 core
            #define SPARK_VARY_IN in
            #define SPARK_FRAG sparkFragColour
            out vec4 sparkFragColour;
            """,
        GlDialect.Es300 => """
            #version 300 es
            precision highp float;
            #define SPARK_VARY_IN in
            #define SPARK_FRAG sparkFragColour
            out vec4 sparkFragColour;
            """,
        GlDialect.Es100 => """
            #version 100
            precision highp float;
            #define SPARK_VARY_IN varying
            #define SPARK_FRAG gl_FragColor
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
    };
}
