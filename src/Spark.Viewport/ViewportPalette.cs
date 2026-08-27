namespace Spark.Viewport;

/// <summary>
/// The viewport half of Spark's palette, taken verbatim from
/// <c>docs/help/concepts/design-language.md</c> §8. Every value is the exact hex printed there.
/// If the document and this class disagree, the document is right.
/// </summary>
/// <remarks>
/// The grid ratios are deliberately low — <c>grid.minor</c> is 1.26:1 against the ground — and
/// are covered by the scene-element exemption in §4.2. A grid has to be legible when you look
/// for it and invisible when you do not, which is the opposite of a contrast floor.
/// </remarks>
public static class ViewportPalette
{
    /// <summary><c>viewport.top</c> <c>#1B1F26</c>. Top of the background gradient.</summary>
    public static ViewportColor BackgroundTop { get; } = ViewportColor.FromHex("#1B1F26");

    /// <summary><c>viewport.bottom</c> <c>#14171D</c>. Bottom of the background gradient.</summary>
    public static ViewportColor BackgroundBottom { get; } = ViewportColor.FromHex("#14171D");

    /// <summary><c>grid.minor</c> <c>#2A313C</c>. One model unit.</summary>
    public static ViewportColor GridMinor { get; } = ViewportColor.FromHex("#2A313C");

    /// <summary><c>grid.major</c> <c>#3A414D</c>. Every ten units.</summary>
    public static ViewportColor GridMajor { get; } = ViewportColor.FromHex("#3A414D");

    /// <summary><c>axis.x</c> <c>#DE7176</c>. Deliberately a distinct hex from <c>state.error</c>.</summary>
    public static ViewportColor AxisX { get; } = ViewportColor.FromHex("#DE7176");

    /// <summary><c>axis.y</c> <c>#6DC576</c>. Deliberately a distinct hex from <c>state.success</c>.</summary>
    public static ViewportColor AxisY { get; } = ViewportColor.FromHex("#6DC576");

    /// <summary><c>axis.z</c> <c>#6699E0</c>. Deliberately a distinct hex from <c>state.info</c>.</summary>
    public static ViewportColor AxisZ { get; } = ViewportColor.FromHex("#6699E0");

    /// <summary><c>geometry.surface</c> <c>#AEB7C6</c>. Default shaded surface at full lighting.</summary>
    public static ViewportColor GeometrySurface { get; } = ViewportColor.FromHex("#AEB7C6");

    /// <summary><c>geometry.edge</c> <c>#E6EAF1</c>. Edges, isoparms and curve strokes.</summary>
    public static ViewportColor GeometryEdge { get; } = ViewportColor.FromHex("#E6EAF1");

    /// <summary><c>geometry.casing</c> <c>#0E1116</c>. The dark casing under every overlay stroke.</summary>
    public static ViewportColor GeometryCasing { get; } = ViewportColor.FromHex("#0E1116");

    /// <summary><c>geometry.ghost</c> <c>#616A79</c>. The one declared contrast exception (§8.4).</summary>
    public static ViewportColor GeometryGhost { get; } = ViewportColor.FromHex("#616A79");

    /// <summary><c>accent</c> <c>#A98BFF</c>. The core of a selection outline.</summary>
    public static ViewportColor Accent { get; } = ViewportColor.FromHex("#A98BFF");
}
