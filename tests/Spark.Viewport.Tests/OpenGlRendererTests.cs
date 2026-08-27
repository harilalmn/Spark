using System;
using System.Collections.Generic;
using System.Numerics;
using Spark.Viewport;
using Spark.Viewport.Meshes;
using Spark.Viewport.OpenGL;

namespace Spark.Viewport.Tests;

/// <summary>
/// The OpenGL backend's bookkeeping, exercised against a recording <see cref="IGlApi"/> rather than
/// a GPU.
/// </summary>
/// <remarks>
/// This is what the <see cref="IGlApi"/> seam buys beyond keeping Avalonia out of the assembly.
/// The claims that matter here — one buffer set per key, one upload per changed package, buffers
/// released when a node goes away, nothing thrown into a paint handler — are all statements about
/// call sequences, and a call sequence is testable without a driver. What this cannot check is
/// whether the resulting pixels are right; that needs the read-back path or the software renderer.
/// </remarks>
public sealed class OpenGlRendererTests
{
    [Fact]
    public void InitialisationReportsTheContextItGot()
    {
        RecordingGl gl = new();
        using OpenGlViewportRenderer renderer = new(gl);

        Assert.True(renderer.Initialise());
        Assert.True(renderer.IsInitialised);
        Assert.Contains("OpenGL ready", renderer.Diagnostic!);
        Assert.Contains("Es300", renderer.Diagnostic!);
    }

    [Fact]
    public void AShaderThatDoesNotCompileFailsWithoutThrowing()
    {
        RecordingGl gl = new() { CompileError = "ERROR: 0:4: syntax error" };
        using OpenGlViewportRenderer renderer = new(gl);

        Assert.False(renderer.Initialise());
        Assert.False(renderer.IsInitialised);
        Assert.Contains("did not compile", renderer.Diagnostic!);
        Assert.Contains("syntax error", renderer.Diagnostic!);
    }

    [Fact]
    public void AProgramThatDoesNotLinkFailsWithoutThrowing()
    {
        RecordingGl gl = new() { LinkError = "ERROR: too many varyings" };
        using OpenGlViewportRenderer renderer = new(gl);

        Assert.False(renderer.Initialise());
        Assert.Contains("did not link", renderer.Diagnostic!);
    }

    [Fact]
    public void RenderingBeforeInitialisationDoesNothingRatherThanThrowing()
    {
        RecordingGl gl = new() { CompileError = "no" };
        using OpenGlViewportRenderer renderer = new(gl);
        renderer.Initialise();

        // The caller is a paint handler. A throw out of one takes the window down, and a viewport
        // that failed to start is a support case rather than a crash.
        renderer.Render(new ViewportScene(), new Camera());

        Assert.Empty(gl.Draws);
    }

    [Fact]
    public void EachPackageGetsOneBufferSetAndOneTriangleDraw()
    {
        RecordingGl gl = new();
        using OpenGlViewportRenderer renderer = new(gl);
        Assert.True(renderer.Initialise());

        ViewportScene scene = new();
        scene.Set(Box("a"));
        scene.Set(Box("b"));

        renderer.Render(scene, new Camera());

        Assert.Equal(2, renderer.ResidentGeometryCount);
        Assert.Equal(2, gl.CountDraws(GlConst.Triangles));
        Assert.Equal(2, gl.CountDraws(GlConst.Lines));   // one edge pass per package
    }

    [Fact]
    public void ReRenderingAnUnchangedSceneUploadsNothingFurther()
    {
        RecordingGl gl = new();
        using OpenGlViewportRenderer renderer = new(gl);
        renderer.Initialise();

        ViewportScene scene = new();
        scene.Set(Box("a"));

        renderer.Render(scene, new Camera());
        int afterFirst = gl.Uploads;

        renderer.Render(scene, new Camera());

        Assert.Equal(afterFirst, gl.Uploads);
    }

    [Fact]
    public void ChangingOnlyTheAppearanceUploadsNothing()
    {
        RecordingGl gl = new();
        using OpenGlViewportRenderer renderer = new(gl);
        renderer.Initialise();

        ViewportScene scene = new();
        RenderPackage package = Box("a");
        scene.Set(package);
        renderer.Render(scene, new Camera());
        int afterFirst = gl.Uploads;

        // Selection is a uniform, not a buffer. This is the assertion behind "selection
        // synchronisation falls out of node-keyed identity for free".
        scene.Set(package.WithAppearance(package.Appearance with { IsSelected = true }));
        renderer.Render(scene, new Camera());

        Assert.Equal(afterFirst, gl.Uploads);
    }

    [Fact]
    public void ReplacingOnePackageReUploadsOnlyThatOne()
    {
        RecordingGl gl = new();
        using OpenGlViewportRenderer renderer = new(gl);
        renderer.Initialise();

        ViewportScene scene = new();
        scene.Set(Box("a"));
        scene.Set(Box("b"));
        renderer.Render(scene, new Camera());

        int before = gl.Uploads;

        scene.Set(Box("a", size: 3f));
        renderer.Render(scene, new Camera());

        // Four buffers make up one package: positions, normals, triangle indices, edge indices.
        // Re-evaluating one node must cost exactly those four uploads and not touch the other
        // package — that is the whole reason geometry is keyed by (NodeId, PortIndex).
        Assert.Equal(4, gl.Uploads - before);
    }

    [Fact]
    public void RemovingANodeReleasesItsBuffers()
    {
        RecordingGl gl = new();
        using OpenGlViewportRenderer renderer = new(gl);
        renderer.Initialise();

        ViewportScene scene = new();
        scene.Set(Box("a"));
        scene.Set(Box("b"));
        renderer.Render(scene, new Camera());

        int livePackageBuffers = gl.LiveBuffers.Count;

        scene.RemoveNode("a");
        renderer.Render(scene, new Camera());

        Assert.Equal(1, renderer.ResidentGeometryCount);
        Assert.True(gl.LiveBuffers.Count < livePackageBuffers, "The removed node's buffers were leaked.");
    }

    [Fact]
    public void DisposeReleasesEveryBufferAndProgram()
    {
        RecordingGl gl = new();
        OpenGlViewportRenderer renderer = new(gl);
        renderer.Initialise();

        ViewportScene scene = new();
        scene.Set(Box("a"));
        renderer.Render(scene, new Camera());

        renderer.Dispose();

        Assert.Empty(gl.LiveBuffers);
        Assert.Empty(gl.LivePrograms);
        Assert.Empty(gl.LiveVertexArrays);
        Assert.False(renderer.IsInitialised);
    }

    [Fact]
    public void GhostedGeometryIsDrawnAsEdgesOnly()
    {
        RecordingGl gl = new();
        using OpenGlViewportRenderer renderer = new(gl);
        renderer.Initialise();

        ViewportScene scene = new();
        scene.Set(Box("a").WithAppearance(Appearance.Default with { IsGhosted = true }));

        renderer.Render(scene, new Camera());

        // §8.4: ghosting is discharged as a rendering-mode difference rather than a contrast
        // ratio, and the difference is absolute — ghosted geometry is never shaded.
        Assert.Equal(0, gl.CountDraws(GlConst.Triangles));
        Assert.Equal(1, gl.CountDraws(GlConst.Lines));
    }

    [Fact]
    public void TheRendererWorksWithoutVertexArrayObjects()
    {
        RecordingGl gl = new() { SupportsVertexArrays = false };
        using OpenGlViewportRenderer renderer = new(gl);
        Assert.True(renderer.Initialise());

        ViewportScene scene = new();
        scene.Set(Box("a"));
        renderer.Render(scene, new Camera());

        // Vertex array objects are core in ES 3.0 but only an extension in ES 2.0, so the renderer
        // has to be able to set the attribute pointers up per draw instead.
        Assert.Empty(gl.LiveVertexArrays);
        Assert.Equal(1, gl.CountDraws(GlConst.Triangles));
    }

    [Fact]
    public void EveryDialectCompilesToSourceWithItsVersionFirstAndPrecisionBeforeAnyDeclaration()
    {
        foreach (GlDialect dialect in Enum.GetValues<GlDialect>())
        {
            RecordingGl gl = new() { Dialect = dialect };
            using OpenGlViewportRenderer renderer = new(gl);
            Assert.True(renderer.Initialise(), $"{dialect} failed: {renderer.Diagnostic}");

            foreach (string source in gl.ShaderSources)
            {
                Assert.StartsWith("#version ", source, StringComparison.Ordinal);

                int precision = source.IndexOf("precision ", StringComparison.Ordinal);
                int declaration = source.IndexOf("out vec4 ", StringComparison.Ordinal);

                // GLSL ES rejects a float declaration before a precision statement, which is a
                // real failure that only shows up on an ES context — on Windows, under ANGLE,
                // after it has already passed on a desktop-GL development machine.
                if (precision >= 0 && declaration >= 0)
                {
                    Assert.True(precision < declaration, $"{dialect}: precision must precede declarations.");
                }
            }
        }
    }

    [Fact]
    public void TheEs100DialectUsesTheOldKeywords()
    {
        RecordingGl gl = new() { Dialect = GlDialect.Es100 };
        using OpenGlViewportRenderer renderer = new(gl);
        renderer.Initialise();

        // The keyword swap is done with preprocessor defines, so it is the defines that are
        // asserted here: the expansion itself happens inside the driver's GLSL compiler.
        Assert.Contains(gl.ShaderSources, s => s.Contains("#define SPARK_IN attribute", StringComparison.Ordinal));
        Assert.Contains(gl.ShaderSources, s => s.Contains("#define SPARK_FRAG gl_FragColor", StringComparison.Ordinal));

        // GLSL ES 1.00 has no user-declared fragment outputs; declaring one fails to compile.
        Assert.DoesNotContain(gl.ShaderSources, s => s.Contains("out vec4 sparkFragColour", StringComparison.Ordinal));
    }

    [Fact]
    public void TheViewProjectionMatrixIsUploadedWithoutBeingTransposed()
    {
        RecordingGl gl = new();
        using OpenGlViewportRenderer renderer = new(gl);
        renderer.Initialise();

        Camera camera = new();
        camera.SetViewportSize(800, 600);
        renderer.Render(new ViewportScene(), camera);

        // System.Numerics stores a row-vector matrix in row-major order, which is byte-identical
        // to the column-major form GLSL wants for the equivalent column-vector matrix. Adding a
        // transpose here mirrors the whole scene, and a mirrored scene looks correct.
        Assert.Contains(camera.ViewProjection, gl.UploadedMatrices);
    }

    private static RenderPackage Box(string nodeId, float size = 1f) =>
        PrimitiveMeshes.Box(Vector3.Zero, new Vector3(size))
            .ToRenderPackage(new GeometryKey(nodeId, 0), "solid", Appearance.Default);

    /// <summary>
    /// An <see cref="IGlApi"/> that hands out increasing names and records what it was asked to do.
    /// </summary>
    private sealed class RecordingGl : IGlApi
    {
        private int _nextName = 1;

        internal List<(int Mode, int Count)> Draws { get; } = [];

        internal HashSet<int> LiveBuffers { get; } = [];

        internal HashSet<int> LivePrograms { get; } = [];

        internal HashSet<int> LiveVertexArrays { get; } = [];

        internal List<string> ShaderSources { get; } = [];

        internal List<Matrix4x4> UploadedMatrices { get; } = [];

        internal int Uploads { get; private set; }

        internal string? CompileError { get; init; }

        internal string? LinkError { get; init; }

        public GlDialect Dialect { get; init; } = GlDialect.Es300;

        public string VersionString => "OpenGL ES 3.0 (recording)";

        public string RendererString => "recording";

        public bool SupportsVertexArrays { get; init; } = true;

        public int CompileShader(int shaderType, string source, out string? error)
        {
            ShaderSources.Add(source);
            error = CompileError;
            return CompileError is null ? _nextName++ : 0;
        }

        public int LinkProgram(int vertexShader, int fragmentShader, out string? error)
        {
            error = LinkError;
            if (LinkError is not null)
            {
                return 0;
            }

            int program = _nextName++;
            LivePrograms.Add(program);
            return program;
        }

        public void DeleteProgram(int program) => LivePrograms.Remove(program);

        public int CreateBuffer()
        {
            int buffer = _nextName++;
            LiveBuffers.Add(buffer);
            return buffer;
        }

        public void DeleteBuffer(int buffer) => LiveBuffers.Remove(buffer);

        public int CreateVertexArray()
        {
            int array = _nextName++;
            LiveVertexArrays.Add(array);
            return array;
        }

        public void DeleteVertexArray(int vertexArray) => LiveVertexArrays.Remove(vertexArray);

        public void BufferData(int target, float[] data, int usage) => Uploads++;

        public void BufferData(int target, int[] data, int usage) => Uploads++;

        public void UniformMatrix4(int location, in Matrix4x4 value) => UploadedMatrices.Add(value);

        public void DrawArrays(int mode, int first, int count) => Draws.Add((mode, count));

        public void DrawElements(int mode, int count, int type, int offsetBytes) => Draws.Add((mode, count));

        internal int CountDraws(int mode)
        {
            int total = 0;
            foreach ((int drawMode, _) in Draws)
            {
                if (drawMode == mode)
                {
                    total++;
                }
            }

            // Exactly one draw of each mode is scene furniture rather than per-package work: the
            // full-screen background triangle and the ground grid's line batch.
            return Math.Max(0, total - 1);
        }

        public void Enable(int capability)
        {
        }

        public void Disable(int capability)
        {
        }

        public void ClearColor(float r, float g, float b, float a)
        {
        }

        public void Clear(int mask)
        {
        }

        public void Viewport(int x, int y, int width, int height)
        {
        }

        public void DepthFunc(int function)
        {
        }

        public void DepthMask(bool enabled)
        {
        }

        public void BlendFunc(int sourceFactor, int destinationFactor)
        {
        }

        public void BindFramebuffer(int target, int framebuffer)
        {
        }

        public void DeleteShader(int shader)
        {
        }

        public void UseProgram(int program)
        {
        }

        public int GetUniformLocation(int program, string name) => 1;

        public int GetAttribLocation(int program, string name) => 0;

        public void Uniform1f(int location, float value)
        {
        }

        public void Uniform3f(int location, float x, float y, float z)
        {
        }

        public void Uniform4f(int location, float x, float y, float z, float w)
        {
        }

        public void BindBuffer(int target, int buffer)
        {
        }

        public void BindVertexArray(int vertexArray)
        {
        }

        public void EnableVertexAttribArray(int index)
        {
        }

        public void VertexAttribPointer(
            int index, int size, int type, bool normalized, int strideBytes, int offsetBytes)
        {
        }

        public bool ReadPixels(int x, int y, int width, int height, byte[] rgba) => false;
    }
}
