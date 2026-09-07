using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Spark.UI.Controls;

namespace Spark.UI.Tests;

/// <summary>
/// Whether the viewport gets drawn again when what it would draw has changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The viewport has two surfaces and they are repainted by two different requests.</b> The GL
/// scene lives on a composition surface the compositor owns, and
/// <c>OpenGlControlBase.RequestNextFrameRendering</c> is what asks for another frame of it.
/// Everything else the control shows — the status plate, and the entire software frame — is drawn
/// from <c>Render(DrawingContext)</c>, which runs only when the control's <i>visual</i> is
/// invalidated. Asking for the first and calling it a repaint is what left <c>OpenGL ready.
/// Version …</c> sitting over a working scene like a watermark, and it is why nothing but resizing
/// the window would clear it.
/// </para>
/// <para>
/// The software backend is what makes the omission testable without a GPU. A capture is serviced
/// from inside <c>Render(DrawingContext)</c> on that path, so <i>a capture that never completes</i>
/// and <i>a message that never goes away</i> are the same missing invalidation seen from two sides.
/// </para>
/// </remarks>
public sealed class ViewportRepaintTests
{
    /// <summary>
    /// Asking for a capture reaches the screen: the control invalidates its own drawing rather
    /// than only asking the compositor for a GL frame that, with no GL, never arrives.
    /// </summary>
    [Fact]
    public void AskingForAFrameRepaintsWhatTheControlDrawsItself() => HeadlessSession.Run(() =>
    {
        ViewportControl viewport = new() { ForceSoftwareRenderer = true };
        Window window = new() { Width = 400, Height = 300, Content = viewport };

        window.Show();
        window.UpdateLayout();
        Pump();

        Assert.False(viewport.HasCapture);

        viewport.RequestCapture();
        Pump();

        Assert.True(
            viewport.HasCapture,
            "the capture is serviced from Render(DrawingContext), so it only completes if the "
            + "control's own drawing was invalidated as well as the compositor asked for a frame.");
    });

    /// <summary>Runs whatever the control posted, then a render pass over the window.</summary>
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
