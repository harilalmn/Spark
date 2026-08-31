using Avalonia;
using Spark.UI;

namespace Spark.Desktop;

/// <summary>
/// Entry point for the Spark desktop application.
/// </summary>
/// <remarks>
/// This assembly is deliberately almost empty. Everything the application <i>is</i> lives in
/// <c>Spark.UI</c>, so that embedding Spark inside a CAD host — where the host owns the process
/// and the message loop — means constructing the same <see cref="App"/> against a different
/// lifetime rather than reimplementing the shell.
/// </remarks>
internal static class Program
{
    /// <summary>
    /// The process entry point.
    /// </summary>
    /// <param name="args">
    /// The command line. <c>--nodes N</c> loads N synthetic nodes; <c>--canvas-benchmark [frames]</c>
    /// runs the ADR-0013 measurement and exits. See <see cref="StartupOptions"/>.
    /// </param>
    /// <remarks>
    /// <c>[STAThread]</c> is required by Avalonia's Win32 backend for clipboard and drag-and-drop,
    /// and is harmless everywhere else.
    /// </remarks>
    [System.STAThread]
    private static void Main(string[] args)
    {
        // ADR-0020: install the solid-modelling kernel before anything can evaluate a graph. Its
        // absence is not an error - the whole application works without it and the solid nodes
        // say so by name - so the answer is ignored here rather than reported at startup.
        _ = Spark.Geometry.Occt.OcctKernel.TryInstall(out _);

        BuildAvaloniaApp(StartupOptions.Parse(args))
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Configures the Avalonia application. Public and parameterless-friendly because the XAML
    /// previewer and the designer look for exactly this shape.
    /// </summary>
    /// <returns>The configured builder.</returns>
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(StartupOptions.Default);

    private static AppBuilder BuildAvaloniaApp(StartupOptions options) =>
        AppBuilder.Configure(() => new App { Options = options })
            .UsePlatformDetect()

            // Inter ships with the application rather than being assumed present, so a Linux build
            // renders identically to a Windows one (design language §9.1). Segoe UI is the
            // fallback on Windows and has similar metrics, so a missing Inter degrades without
            // reflowing the layout.
            .WithInterFont()
            .LogToTrace();
}
