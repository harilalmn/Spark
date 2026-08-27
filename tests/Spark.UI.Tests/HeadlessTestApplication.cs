using Avalonia;
using Avalonia.Headless;
using Spark.UI.Tests;

[assembly: AvaloniaTestApplication(typeof(HeadlessTestApplication))]

namespace Spark.UI.Tests;

/// <summary>
/// The Avalonia application the headless input tests run inside.
/// </summary>
/// <remarks>
/// It is deliberately not <c>Spark.UI.App</c>. Loading the real application would pull in the
/// Fluent theme, the palette dictionary and the main window, none of which the canvas needs and
/// all of which would turn a failing input test into a failing theme load. What is being tested is
/// input routing, and the canvas routes its own.
/// </remarks>
public static class HeadlessTestApplication
{
    /// <summary>Builds the headless application. Found by name; the attribute above points at it.</summary>
    /// <returns>The configured builder.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
