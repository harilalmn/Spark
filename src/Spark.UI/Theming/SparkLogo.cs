using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Spark.UI.Theming;

/// <summary>
/// The application mark, and the window icon rendered from it.
/// </summary>
/// <remarks>
/// <para>
/// The artwork lives in <c>Theming/SparkLogo.axaml</c> as a <see cref="DrawingImage"/>, whose path
/// strings are the ones in <c>assets/spark-icon.svg</c> verbatim — Avalonia's geometry syntax is
/// SVG path syntax, so there is one source of truth and no export step.
/// </para>
/// <para>
/// <b>The window icon is rendered from that drawing at runtime rather than committed as a
/// <c>.ico</c>.</b> A bitmap in the tree is a second copy of the artwork that silently stops
/// matching the first, and this way the taskbar icon cannot disagree with the splash screen.
/// </para>
/// </remarks>
public static class SparkLogo
{
    /// <summary>The resource key the drawing is registered under in <c>App.axaml</c>.</summary>
    public const string ResourceKey = "SparkLogoImage";

    /// <summary>
    /// Renders the mark to a window icon.
    /// </summary>
    /// <param name="size">The square edge length in pixels. 256 suits a Windows taskbar.</param>
    /// <returns>The icon, or null when the mark or a render target is unavailable.</returns>
    /// <remarks>
    /// <b>Returns null rather than throwing, deliberately.</b> Rendering needs a live rendering
    /// subsystem, which a headless test session or an embedded host may not have provided — and an
    /// application that refuses to start because it could not draw its own icon has its priorities
    /// wrong. Every caller treats null as "no icon", which is what Avalonia does by default anyway.
    /// </remarks>
    public static WindowIcon? CreateWindowIcon(int size = 256)
    {
        if (size <= 0 || Application.Current is not { } application)
        {
            return null;
        }

        if (!application.Resources.TryGetResource(ResourceKey, null, out object? resource)
            || resource is not DrawingImage drawing)
        {
            return null;
        }

        try
        {
            Image host = new()
            {
                Source = drawing,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
            };

            host.Measure(new Size(size, size));
            host.Arrange(new Rect(0, 0, size, size));

            RenderTargetBitmap bitmap = new(new PixelSize(size, size), new Vector(96, 96));
            bitmap.Render(host);
            return new WindowIcon(bitmap);
        }
        catch (Exception)
        {
            // See the remarks: an icon is not worth failing a startup over.
            return null;
        }
    }
}
