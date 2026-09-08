using System;
using Avalonia.Media;

namespace Spark.UI.Theming;

/// <summary>
/// The two marks on a preview bubble: the toggle that expands it and the pin that keeps it
/// (<c>E8-T72</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Drawn as geometry rather than set as text</b>, for the reason <see cref="NodeKindGlyphs"/>
/// records: a glyph taken from a font is a glyph that is missing on the machine that does not have
/// that font. A pushpin is an emoji on most systems and a missing box on the rest, and a
/// disclosure triangle is a geometric-shapes character Inter does not carry. Each path is authored
/// inside a 16×16 box and scaled by the control that draws it.
/// </para>
/// <para>
/// <b>Lazy, and that is not an optimisation.</b> <c>Avalonia.Media.Geometry.Parse</c> needs Avalonia's render
/// interface, and a static field initialiser runs the moment any member of this class is touched —
/// which would make touching it from a view model throw in every test with no rendering platform.
/// The same trap <see cref="NodeKindGlyphs"/> fell into and records.
/// </para>
/// </remarks>
public static class PreviewGlyphs
{
    private static readonly Lazy<Avalonia.Media.Geometry> ExpandGeometry =
        new(() => Avalonia.Media.Geometry.Parse("M4,6 L12,6 L8,11 Z"));

    private static readonly Lazy<Avalonia.Media.Geometry> CollapseGeometry =
        new(() => Avalonia.Media.Geometry.Parse("M4,10 L12,10 L8,5 Z"));

    // A pushpin pointing down: a head bar, a body, the flare where the pin meets the board, and
    // the needle. Symmetric about x=8, so it reads the same at 11 px as at 40.
    private static readonly Lazy<Avalonia.Media.Geometry> PinGeometry = new(
        () => Avalonia.Media.Geometry.Parse(
            "M6,2 H10 V3.4 H9.1 V7 L11.2,9.6 H8.6 V14 H7.4 V9.6 H4.8 L6.9,7 V3.4 H6 Z"));

    /// <summary>The size of the box every path here is authored in.</summary>
    /// <remarks>
    /// Public because the caller scales by <c>size / DesignSize</c>, and a caller that guessed 16
    /// would be reading a number out of this file's comments rather than out of this file.
    /// </remarks>
    public const double DesignSize = 16;

    /// <summary>The triangle that opens a collapsed bubble.</summary>
    public static Avalonia.Media.Geometry Expand => ExpandGeometry.Value;

    /// <summary>The triangle that closes an open one.</summary>
    public static Avalonia.Media.Geometry Collapse => CollapseGeometry.Value;

    /// <summary>The pin that keeps an open bubble after the node stops being selected.</summary>
    public static Avalonia.Media.Geometry Pin => PinGeometry.Value;
}
