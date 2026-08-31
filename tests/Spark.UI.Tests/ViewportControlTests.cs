using Spark.UI.Controls;

namespace Spark.UI.Tests;

/// <summary>
/// Which backend is presenting the viewport, which is a question with two wrong answers and one
/// of them is silent. See <c>N64</c>.
/// </summary>
public sealed class ViewportControlTests
{
    /// <summary>
    /// <b>The regression test for N64.</b> A control that has not yet heard from OpenGL must not
    /// claim the software renderer is presenting. Avalonia paints before <c>OnOpenGlInit</c>
    /// fires, so this state is reached on every launch on every machine — and while it held the
    /// opposite answer, a <c>--screenshot</c> on a healthy GPU wrote a CPU-rendered image and
    /// printed the GL driver string underneath it.
    /// </summary>
    [Fact]
    public void ANewControlDoesNotClaimSoftwareIsPresentingBeforeGlHasBeenHeardFrom()
    {
        HeadlessSession.Run(() =>
        {
            ViewportControl viewport = new();

            Assert.False(viewport.IsSoftwarePresenting);
        });
    }

    /// <summary>The switch commits immediately; there is nothing to wait for once it is set.</summary>
    [Fact]
    public void ForcingTheSoftwareRendererCommitsToItAtOnce()
    {
        HeadlessSession.Run(() =>
        {
            ViewportControl viewport = new() { ForceSoftwareRenderer = true };

            Assert.True(viewport.IsSoftwarePresenting);
        });
    }

    /// <summary>
    /// The camera is the control's own and survives being read back, which is what the fallback's
    /// frame signature depends on to decide whether to re-rasterise.
    /// </summary>
    [Fact]
    public void TheCameraIsReachableAndFramesAnEmptySceneWithoutThrowing()
    {
        HeadlessSession.Run(() =>
        {
            ViewportControl viewport = new();

            viewport.ZoomToFit();

            Assert.NotNull(viewport.Camera);
        });
    }
}
