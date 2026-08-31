using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Spark.UI.Controls;

namespace Spark.UI.Tests;

/// <summary>
/// Navigating the viewport with a mouse (<c>E9-T21</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every handler these exercise already existed and none of them ever ran.</b> The wheel
/// dollied, the middle button panned and the right button orbited, all correctly written, and the
/// viewport ignored the mouse completely: <c>ViewportControl</c> set no <c>Background</c>, and a
/// control with no background is not hit-testable in Avalonia, so the pointer events went to
/// whatever was underneath it.
/// </para>
/// <para>
/// <b>Reported by a person opening the application, not by a test.</b> The canvas has had pointer
/// tests since <c>E8</c> and the viewport had none — it had tests for the camera, for the
/// renderer, for read-back and for tessellation, every one of which called the camera directly.
/// The one thing nothing did was press a button.
/// </para>
/// </remarks>
public sealed class ViewportNavigationTests
{
    /// <summary>The wheel zooms.</summary>
    [Fact]
    public void TheWheelDolliesTheCamera() => HeadlessSession.Run(() =>
    {
        (Window window, ViewportControl viewport) = Open();

        float before = viewport.Camera.Distance;

        window.MouseWheel(Centre, new Avalonia.Vector(0, 1), RawInputModifiers.None);

        Assert.True(
            viewport.Camera.Distance < before,
            $"the wheel did not move the camera: {before} then {viewport.Camera.Distance}");

        window.Close();
    });

    /// <summary>And the other way.</summary>
    [Fact]
    public void TheWheelDolliesBothWays() => HeadlessSession.Run(() =>
    {
        (Window window, ViewportControl viewport) = Open();

        float before = viewport.Camera.Distance;

        window.MouseWheel(Centre, new Avalonia.Vector(0, -1), RawInputModifiers.None);

        Assert.True(viewport.Camera.Distance > before);

        window.Close();
    });

    /// <summary>The middle button drags the camera sideways.</summary>
    [Fact]
    public void TheMiddleButtonPans() => HeadlessSession.Run(() =>
    {
        (Window window, ViewportControl viewport) = Open();

        Vector3 before = viewport.Camera.Target;

        window.MouseDown(Centre, MouseButton.Middle);
        window.MouseMove(Centre + new Point(60, 25), RawInputModifiers.None);
        window.MouseUp(Centre + new Point(60, 25), MouseButton.Middle);

        Assert.True(
            viewport.Camera.Target != before,
            "the middle button did not pan the camera");

        window.Close();
    });

    /// <summary>The right button orbits.</summary>
    [Fact]
    public void TheRightButtonOrbits() => HeadlessSession.Run(() =>
    {
        (Window window, ViewportControl viewport) = Open();

        Vector3 before = viewport.Camera.Position;

        window.MouseDown(Centre, MouseButton.Right);
        window.MouseMove(Centre + new Point(80, 0), RawInputModifiers.None);
        window.MouseUp(Centre + new Point(80, 0), MouseButton.Right);

        Assert.True(
            viewport.Camera.Position != before,
            "the right button did not orbit the camera");

        window.Close();
    });

    /// <summary>
    /// <b>Shift and the middle button orbits too.</b> That is the binding a user coming from other
    /// CAD software reaches for first, and it was the gesture whose absence surfaced this: the
    /// viewport ignored it, and then ignored everything else as well.
    /// </summary>
    [Fact]
    public void ShiftAndTheMiddleButtonOrbits() => HeadlessSession.Run(() =>
    {
        (Window window, ViewportControl viewport) = Open();

        Vector3 before = viewport.Camera.Position;

        window.MouseDown(Centre, MouseButton.Middle, RawInputModifiers.Shift);
        window.MouseMove(Centre + new Point(80, 0), RawInputModifiers.Shift);
        window.MouseUp(Centre + new Point(80, 0), MouseButton.Middle, RawInputModifiers.Shift);

        Assert.True(
            viewport.Camera.Position != before,
            "shift and the middle button did not orbit the camera");

        window.Close();
    });

    /// <summary>
    /// <b>The control is hit-testable at all</b>, which is the property the others depend on and
    /// the one that was missing. Asserted directly so a failure says what is wrong rather than
    /// leaving five gesture tests to fail together with no common message.
    /// </summary>
    [Fact]
    public void TheViewportIsHitTestable() => HeadlessSession.Run(() =>
    {
        (Window window, ViewportControl viewport) = Open();

        // The property the five gestures above all depend on. Asserted on its own so a failure
        // says "the viewport is not hit-testable" rather than leaving five gesture tests to fail
        // together with no common message between them.
        // A frame first. Hit-testing is against what was drawn, and nothing has been drawn until
        // something renders - which is the same fact this whole class is about, seen from the
        // other side.
        window.CaptureRenderedFrame();

        Assert.Same(viewport, window.InputHitTest(Centre));

        window.Close();
    });

    private static Point Centre => new(400, 300);

    private static (Window Window, ViewportControl Viewport) Open()
    {
        ViewportControl viewport = new();
        Window window = new() { Width = 800, Height = 600, Content = viewport };

        window.Show();

        return (window, viewport);
    }
}
