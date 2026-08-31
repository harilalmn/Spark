using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Spark.Viewport;
using Spark.Viewport.Meshes;
using Spark.Viewport.Software;

namespace Spark.Viewport.Tests;

/// <summary>
/// The visual regression check (<c>E9-T12</c>): a fixed scene, rendered by the software
/// rasteriser, compared against a picture committed to the repository.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this can exist at all.</b> GPU output is not comparable between machines — driver
/// version, vendor, precision and multisampling all move it — so no golden image of a GL frame
/// could ever be trusted. Software output is comparable, and <c>E9-T5</c> asserts that it is:
/// two independently constructed renderers produce identical bytes. This check is the reason that
/// property was worth building.
/// </para>
/// <para>
/// <b>The scene is built from explicit meshes, not from the geometry kernel.</b> That is
/// deliberate and it is a scoping decision rather than a convenience: this test guards the
/// <i>rasteriser</i>, so a legitimate improvement to tessellation must not turn it red. What
/// guards the kernel end of the pipeline is <c>E11-T3</c>, executing the example graphs.
/// </para>
/// <para>
/// <b>On failure it prints something usable.</b> A bare "the bytes differ" tells a reader
/// nothing they can act on, which is the complaint <c>E11-T11</c> exists to answer. This reports
/// how many pixels moved, by how much, and where the worst one is, then writes the rendered image
/// and a difference map next to the golden so they can be opened and looked at.
/// </para>
/// </remarks>
public sealed class VisualRegressionTests
{
    /// <summary>
    /// Set <c>SPARK_UPDATE_GOLDEN=1</c> to rewrite the golden instead of asserting against it.
    /// </summary>
    /// <remarks>
    /// A deliberate, visible act. The alternative — a check that rewrites its own expectation
    /// whenever it disagrees — is a check that can never fail, and this project has already found
    /// two of those the expensive way ([N19], [N20]).
    /// </remarks>
    private const string UpdateVariable = "SPARK_UPDATE_GOLDEN";

    private const int Width = 320;
    private const int Height = 240;

    /// <summary>The rendered frame matches the committed golden image, pixel for pixel.</summary>
    [Fact]
    public void TheReferenceSceneMatchesTheCommittedGoldenImage()
    {
        byte[] rendered = RenderReferenceScene();
        string goldenPath = Path.Combine(CorpusDirectory(), "reference-scene.png");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllBytes(goldenPath, PngImage.Encode(rendered, Width, Height));
            Assert.Fail(
                $"Golden image rewritten at {goldenPath}. Unset {UpdateVariable} and re-run; " +
                "review the image before committing it.");
        }

        Assert.True(File.Exists(goldenPath), $"No golden image at {goldenPath}. Run with {UpdateVariable}=1 to create one.");

        byte[] golden = PngImage.Decode(File.ReadAllBytes(goldenPath), out int goldenWidth, out int goldenHeight);

        Assert.Equal(Width, goldenWidth);
        Assert.Equal(Height, goldenHeight);

        string? report = Compare(golden, rendered);
        if (report is null)
        {
            return;
        }

        string actualPath = Path.Combine(CorpusDirectory(), "reference-scene.actual.png");
        File.WriteAllBytes(actualPath, PngImage.Encode(rendered, Width, Height));
        File.WriteAllBytes(
            Path.Combine(CorpusDirectory(), "reference-scene.diff.png"),
            PngImage.Encode(DifferenceMap(golden, rendered), Width, Height));

        Assert.Fail(
            $"{report}\nThe render was written to {actualPath} and a difference map beside it. " +
            $"If the change is intended, re-run with {UpdateVariable}=1 and commit the new golden.\n" +
            "If this is red on Linux and green on Windows and nothing in the renderer changed, read " +
            "this first. The golden was produced on Windows, and the one thing in this pipeline not " +
            "guaranteed bit-identical across platforms is the transcendental maths behind the " +
            "scene's own vertices - MathF.Sin and MathF.Cos, in PrimitiveMeshes.Sphere and in " +
            "Camera.OffsetDirection. The rasteriser itself uses only IEEE-exact arithmetic, and the " +
            "specular exponent was moved off MathF.Pow for precisely this reason. A small difference " +
            "concentrated on the sphere is that; a large or scattered one is a real regression. " +
            "This has been reasoned about and never observed, which is why it is written here " +
            "rather than asserted somewhere.");
    }

    /// <summary>
    /// The check is only worth having if it notices. A single channel moved by one on a single
    /// pixel is the smallest change there is, and it must fail.
    /// </summary>
    [Fact]
    public void OnePixelOffByOneIsDetected()
    {
        byte[] rendered = RenderReferenceScene();
        byte[] tampered = (byte[])rendered.Clone();
        int middle = (((Height / 2) * Width) + (Width / 2)) * 4;
        tampered[middle] = (byte)(tampered[middle] ^ 1);

        string? report = Compare(rendered, tampered);

        Assert.NotNull(report);
        Assert.Contains("1 pixel", report, StringComparison.Ordinal);
    }

    /// <summary>An identical pair reports no difference, so the comparison is not simply always red.</summary>
    [Fact]
    public void AnIdenticalPairReportsNoDifference()
    {
        byte[] rendered = RenderReferenceScene();

        Assert.Null(Compare(rendered, (byte[])rendered.Clone()));
    }

    /// <summary>
    /// The reference scene renders the same on every call. If this ever fails, the golden check
    /// above is meaningless and this is the test that says so first.
    /// </summary>
    [Fact]
    public void TheReferenceSceneIsDeterministic()
    {
        Assert.Equal(RenderReferenceScene(), RenderReferenceScene());
    }

    /// <summary>A PNG this writer produces is one it can read back unchanged.</summary>
    [Fact]
    public void PngSurvivesAnEncodeAndDecodeRoundTrip()
    {
        byte[] rendered = RenderReferenceScene();

        byte[] decoded = PngImage.Decode(PngImage.Encode(rendered, Width, Height), out int width, out int height);

        Assert.Equal(Width, width);
        Assert.Equal(Height, height);
        Assert.Equal(rendered, decoded);
    }

    /// <summary>
    /// The decoder refuses what it does not support instead of returning something plausible. A
    /// golden comparison against a silently mis-decoded image is worse than no comparison.
    /// </summary>
    [Fact]
    public void TheDecoderRefusesWhatItDoesNotSupport()
    {
        InvalidDataException notPng = Assert.Throws<InvalidDataException>(
            () => PngImage.Decode([1, 2, 3, 4, 5, 6, 7, 8, 9], out _, out _));
        Assert.Contains("signature", notPng.Message, StringComparison.OrdinalIgnoreCase);

        byte[] png = PngImage.Encode(new byte[4], 1, 1);
        png[25] = 0;    // IHDR colour type: 6 (RGBA) becomes 0 (greyscale).
        InvalidDataException unsupported = Assert.Throws<InvalidDataException>(
            () => PngImage.Decode(png, out _, out _));
        Assert.Contains("colour type", unsupported.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The reference scene: two boxes and a sphere at fixed positions, a fixed camera, the grid
    /// on. Between them they exercise shaded triangles, a dense edge set, the ground grid, the
    /// three axes, the background gradient, the selection tint on the right-hand box, and — the
    /// sphere straddling that box's top face — the depth test.
    /// </summary>
    /// <remarks>
    /// Nothing here is arbitrary and nothing here should be tidied. Every position and count was
    /// chosen so that some part of the renderer would notice if it changed, and a scene adjusted
    /// to look nicer is a scene that may have stopped covering something.
    /// </remarks>
    private static byte[] RenderReferenceScene()
    {
        ViewportScene scene = new();

        scene.Set(PrimitiveMeshes.Box(new Vector3(-1.5f, -0.6f, 0f), new Vector3(-0.3f, 0.6f, 1.2f))
            .ToRenderPackage(new GeometryKey("box-left", 0), "0", Appearance.Default));

        scene.Set(PrimitiveMeshes.Box(new Vector3(0.4f, -0.5f, 0f), new Vector3(1.6f, 0.7f, 0.9f))
            .ToRenderPackage(
                new GeometryKey("box-right", 0),
                "0",
                new Appearance(
                    ViewportPalette.GeometrySurface,
                    ViewportPalette.GeometryEdge,
                    IsSelected: true,
                    IsGhosted: false)));

        scene.Set(PrimitiveMeshes.Sphere(new Vector3(0.9f, 0.4f, 1.5f), 0.85f, segments: 24, rings: 12)
            .ToRenderPackage(new GeometryKey("sphere", 0), "0", Appearance.Default));

        Camera camera = new()
        {
            Target = new Vector3(0f, 0f, 0.5f),
            Distance = 7.5f,
            Azimuth = -0.9599f,
            Elevation = 0.5980f,
        };

        return ThumbnailRenderer.Render(scene, camera, Width, Height, drawGroundGrid: true);
    }

    /// <summary>
    /// Compares two images and describes the difference, or returns null when they are identical.
    /// </summary>
    private static string? Compare(byte[] expected, byte[] actual)
    {
        if (expected.Length != actual.Length)
        {
            return $"Size mismatch: expected {expected.Length} bytes, got {actual.Length}.";
        }

        int differing = 0;
        int worstDelta = 0;
        int worstPixel = -1;

        for (int p = 0; p < expected.Length; p += 4)
        {
            int delta = 0;
            for (int c = 0; c < 4; c++)
            {
                delta = Math.Max(delta, Math.Abs(expected[p + c] - actual[p + c]));
            }

            if (delta == 0)
            {
                continue;
            }

            differing++;
            if (delta > worstDelta)
            {
                worstDelta = delta;
                worstPixel = p / 4;
            }
        }

        if (differing == 0)
        {
            return null;
        }

        int x = worstPixel % Width;
        int y = worstPixel / Width;
        double percent = differing * 100.0 / (Width * Height);

        StringBuilder report = new();
        report.Append(CultureInfo.InvariantCulture, $"{differing} pixel");
        report.Append(differing == 1 ? " differs" : "s differ");
        report.Append(CultureInfo.InvariantCulture, $" ({percent:F2}% of {Width}x{Height}). ");
        report.Append(CultureInfo.InvariantCulture, $"Worst is {worstDelta}/255 at ({x}, {y}): ");
        report.Append(CultureInfo.InvariantCulture, $"expected {Describe(expected, worstPixel)}, got {Describe(actual, worstPixel)}.");
        return report.ToString();
    }

    private static string Describe(byte[] pixels, int pixel)
    {
        int i = pixel * 4;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"rgba({pixels[i]}, {pixels[i + 1]}, {pixels[i + 2]}, {pixels[i + 3]})");
    }

    /// <summary>
    /// A picture of where two images disagree: black where they match, and increasingly bright
    /// where they do not. Far more useful than either image on its own when the change is a
    /// one-pixel shift along an edge.
    /// </summary>
    private static byte[] DifferenceMap(byte[] expected, byte[] actual)
    {
        byte[] map = new byte[expected.Length];
        for (int p = 0; p < expected.Length; p += 4)
        {
            int delta = 0;
            for (int c = 0; c < 4; c++)
            {
                delta = Math.Max(delta, Math.Abs(expected[p + c] - actual[p + c]));
            }

            // Amplified: a delta of one is invisible at its own magnitude and is exactly the case
            // a reader most needs to see.
            byte value = (byte)Math.Min(255, delta * 8);
            map[p] = value;
            map[p + 1] = value;
            map[p + 2] = value;
            map[p + 3] = 255;
        }

        return map;
    }

    private static string CorpusDirectory() => Path.Combine(RepositoryRoot(), "tests", "corpus", "viewport");

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
