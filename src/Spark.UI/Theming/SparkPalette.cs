using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Spark.UI.Theming;

/// <summary>
/// Every colour token from <c>docs/help/concepts/design-language.md</c> §2, as
/// <see cref="Color"/> values, plus frozen brushes for the ones the canvas renderer uses on every
/// frame.
/// </summary>
/// <remarks>
/// <para>
/// The canvas is drawn by hand rather than by XAML (ADR-0013), so its colours cannot come from a
/// resource dictionary lookup — a dictionary probe per node per frame at two thousand nodes is
/// not affordable. They live here as static readonly brushes, already frozen, and
/// <c>SparkTheme.axaml</c> re-declares the same values as resources for the parts of the shell
/// that <i>are</i> XAML.
/// </para>
/// <para>
/// That duplication is deliberate and is the price of the immediate-mode canvas. The hex literals
/// are identical in both places on purpose, so a mismatch is found by grepping for the value.
/// </para>
/// </remarks>
public static class SparkPalette
{
    // ── The surface ladder (§2.2) ────────────────────────────────────────────────────────────
    // Hover and press move a surface DOWN this ladder, not up. That is unusual, it is
    // deliberate, and §5.1 explains why it is the only direction that raises contrast on a dark
    // theme carrying light text.

    /// <summary><c>bg.void</c> <c>#12151A</c>. Window chrome behind panels, dock gutters, splitters.</summary>
    public static Color BackgroundVoid { get; } = Color.FromRgb(0x12, 0x15, 0x1A);

    /// <summary><c>canvas.bg</c> <c>#171B21</c>. The node canvas ground.</summary>
    public static Color CanvasBackground { get; } = Color.FromRgb(0x17, 0x1B, 0x21);

    /// <summary><c>canvas.group</c> <c>#1E222A</c>. Group frame fill on the canvas.</summary>
    public static Color CanvasGroup { get; } = Color.FromRgb(0x1E, 0x22, 0x2A);

    /// <summary><c>surface.sunken</c> <c>#1A1E24</c>. Inset wells.</summary>
    public static Color SurfaceSunken { get; } = Color.FromRgb(0x1A, 0x1E, 0x24);

    /// <summary><c>surface.base</c> <c>#23272F</c>. Panel bodies and list rows.</summary>
    public static Color SurfaceBase { get; } = Color.FromRgb(0x23, 0x27, 0x2F);

    /// <summary><c>surface.base</c> −1 <c>#1D2128</c>. The hover step.</summary>
    public static Color SurfaceBaseHover { get; } = Color.FromRgb(0x1D, 0x21, 0x28);

    /// <summary><c>surface.base</c> −2 <c>#181C22</c>. The pressed and selected step.</summary>
    public static Color SurfaceBasePressed { get; } = Color.FromRgb(0x18, 0x1C, 0x22);

    /// <summary><c>node.body</c> <c>#262B33</c>. The body of a node on the canvas.</summary>
    public static Color NodeBody { get; } = Color.FromRgb(0x26, 0x2B, 0x33);

    /// <summary><c>node.body</c> −1 <c>#20242B</c>. The hover step; text contrast rises 13.02 → 14.24.</summary>
    public static Color NodeBodyHover { get; } = Color.FromRgb(0x20, 0x24, 0x2B);

    /// <summary><c>node.body</c> −2 <c>#1B1F26</c>. The selected step; text contrast rises to 15.12.</summary>
    public static Color NodeBodySelected { get; } = Color.FromRgb(0x1B, 0x1F, 0x26);

    /// <summary><c>surface.raised</c> <c>#2A2F38</c>. Buttons, toolbar chips, tabs, cards.</summary>
    public static Color SurfaceRaised { get; } = Color.FromRgb(0x2A, 0x2F, 0x38);

    /// <summary><c>surface.raised</c> −1 <c>#232830</c>.</summary>
    public static Color SurfaceRaisedHover { get; } = Color.FromRgb(0x23, 0x28, 0x30);

    /// <summary><c>surface.raised</c> −2 <c>#1C2027</c>.</summary>
    public static Color SurfaceRaisedPressed { get; } = Color.FromRgb(0x1C, 0x20, 0x27);

    /// <summary><c>surface.float</c> <c>#2E3440</c>. Menus, popups, dialogs, tooltips.</summary>
    public static Color SurfaceFloat { get; } = Color.FromRgb(0x2E, 0x34, 0x40);

    // ── Depth (§2.3) — decorative only, never the sole boundary of anything ──────────────────

    /// <summary><c>depth.lo.raised</c> <c>#0C0E13</c>. The shadow side at elevation 2.</summary>
    public static Color DepthLowRaised { get; } = Color.FromRgb(0x0C, 0x0E, 0x13);

    /// <summary><c>depth.hi.raised</c> <c>#3C4452</c>. The lit side at elevation 2.</summary>
    public static Color DepthHighRaised { get; } = Color.FromRgb(0x3C, 0x44, 0x52);

    /// <summary><c>lip.rest</c> <c>#3E4654</c>. The 1 px lit lip along a raised control's top and left edges.</summary>
    public static Color LipRest { get; } = Color.FromRgb(0x3E, 0x46, 0x54);

    /// <summary><c>lip.hover</c> <c>#8674D6</c>. The lip on hover; 3.86:1 on <c>surface.raised</c>−1.</summary>
    public static Color LipHover { get; } = Color.FromRgb(0x86, 0x74, 0xD6);

    // ── Text (§2.4) ─────────────────────────────────────────────────────────────────────────

    /// <summary><c>text.primary</c> <c>#F2F5FA</c>.</summary>
    public static Color TextPrimary { get; } = Color.FromRgb(0xF2, 0xF5, 0xFA);

    /// <summary><c>text.secondary</c> <c>#C6CDD9</c>. Labels, port names, column headers.</summary>
    public static Color TextSecondary { get; } = Color.FromRgb(0xC6, 0xCD, 0xD9);

    /// <summary><c>text.muted</c> <c>#A4ADBB</c>. Units, counts, placeholders, timestamps.</summary>
    public static Color TextMuted { get; } = Color.FromRgb(0xA4, 0xAD, 0xBB);

    /// <summary><c>text.disabled</c> <c>#949DAC</c>. Still above 4.5:1 everywhere — see §5.5.</summary>
    public static Color TextDisabled { get; } = Color.FromRgb(0x94, 0x9D, 0xAC);

    /// <summary><c>text.inverse</c> <c>#141821</c>. Dark text on a bright fill: node headers, accent buttons.</summary>
    public static Color TextInverse { get; } = Color.FromRgb(0x14, 0x18, 0x21);

    // ── Borders, accent, semantics (§2.5) ───────────────────────────────────────────────────

    /// <summary><c>border.hairline</c> <c>#343A45</c>. Decorative dividers only. Never a control boundary.</summary>
    public static Color BorderHairline { get; } = Color.FromRgb(0x34, 0x3A, 0x45);

    /// <summary>
    /// <c>border.control</c> <c>#7C8595</c>. The boundary token — ≥3.35:1 against every surface in
    /// the palette, and mandatory on every node on the canvas (Decision V5).
    /// </summary>
    public static Color BorderControl { get; } = Color.FromRgb(0x7C, 0x85, 0x95);

    /// <summary><c>border.strong</c> <c>#9AA2B1</c>. Table rules, the active dock edge.</summary>
    public static Color BorderStrong { get; } = Color.FromRgb(0x9A, 0xA2, 0xB1);

    /// <summary><c>accent</c> <c>#A98BFF</c>. Selection, focus, active tab, primary action.</summary>
    public static Color Accent { get; } = Color.FromRgb(0xA9, 0x8B, 0xFF);

    /// <summary><c>accent.hover</c> <c>#C0A8FF</c>.</summary>
    public static Color AccentHover { get; } = Color.FromRgb(0xC0, 0xA8, 0xFF);

    /// <summary><c>accent.press</c> <c>#D4C4FF</c>.</summary>
    public static Color AccentPress { get; } = Color.FromRgb(0xD4, 0xC4, 0xFF);

    /// <summary><c>focus.ring</c> <c>#CDBCFF</c>. The light slice of the focus sandwich.</summary>
    public static Color FocusRing { get; } = Color.FromRgb(0xCD, 0xBC, 0xFF);

    /// <summary><c>focus.contour</c> <c>#0C0E13</c>. The 1 px dark separator on both sides of the ring.</summary>
    public static Color FocusContour { get; } = Color.FromRgb(0x0C, 0x0E, 0x13);

    /// <summary><c>state.error</c> <c>#FF7B82</c>.</summary>
    public static Color StateError { get; } = Color.FromRgb(0xFF, 0x7B, 0x82);

    /// <summary><c>state.warning</c> <c>#F0A63C</c>.</summary>
    public static Color StateWarning { get; } = Color.FromRgb(0xF0, 0xA6, 0x3C);

    /// <summary><c>state.success</c> <c>#5FD39A</c>.</summary>
    public static Color StateSuccess { get; } = Color.FromRgb(0x5F, 0xD3, 0x9A);

    /// <summary><c>state.info</c> <c>#68B6F2</c>.</summary>
    public static Color StateInfo { get; } = Color.FromRgb(0x68, 0xB6, 0xF2);

    // ── Wires and ports (§7.5, §7.6) ────────────────────────────────────────────────────────

    /// <summary><c>wire.casing</c> <c>#0E1116</c>. The dark half of the casing-and-core pair.</summary>
    public static Color WireCasing { get; } = Color.FromRgb(0x0E, 0x11, 0x16);

    /// <summary><c>wire.core</c> <c>#C6CDDA</c>. The light half; 10.81:1 on the canvas.</summary>
    public static Color WireCore { get; } = Color.FromRgb(0xC6, 0xCD, 0xDA);

    /// <summary><c>port.rest</c> <c>#8A93A2</c>. An unconnected port; 4.59:1 on <c>node.body</c>.</summary>
    public static Color PortRest { get; } = Color.FromRgb(0x8A, 0x93, 0xA2);

    /// <summary>
    /// <c>port.connected</c> <c>#C6CDDA</c> — the same value as <c>wire.core</c>, so a wire
    /// visually terminates in its port rather than stopping next to it.
    /// </summary>
    public static Color PortConnected { get; } = Color.FromRgb(0xC6, 0xCD, 0xDA);

    // ── Frozen brushes for the canvas hot path ──────────────────────────────────────────────

    /// <summary>A frozen brush for <see cref="CanvasBackground"/>.</summary>
    public static IBrush CanvasBackgroundBrush { get; } = Frozen(CanvasBackground);

    /// <summary>A frozen brush for <see cref="BackgroundVoid"/>.</summary>
    public static IBrush BackgroundVoidBrush { get; } = Frozen(BackgroundVoid);

    /// <summary>A frozen brush for <see cref="SurfaceBase"/>.</summary>
    public static IBrush SurfaceBaseBrush { get; } = Frozen(SurfaceBase);

    /// <summary>A frozen brush for <see cref="SurfaceRaised"/>.</summary>
    public static IBrush SurfaceRaisedBrush { get; } = Frozen(SurfaceRaised);

    /// <summary>A frozen brush for <see cref="NodeBody"/>.</summary>
    public static IBrush NodeBodyBrush { get; } = Frozen(NodeBody);

    /// <summary>A frozen brush for <see cref="NodeBodyHover"/>.</summary>
    public static IBrush NodeBodyHoverBrush { get; } = Frozen(NodeBodyHover);

    /// <summary>A frozen brush for <see cref="NodeBodySelected"/>.</summary>
    public static IBrush NodeBodySelectedBrush { get; } = Frozen(NodeBodySelected);

    /// <summary>A frozen brush for <see cref="TextPrimary"/>.</summary>
    public static IBrush TextPrimaryBrush { get; } = Frozen(TextPrimary);

    /// <summary>A frozen brush for <see cref="TextSecondary"/>.</summary>
    public static IBrush TextSecondaryBrush { get; } = Frozen(TextSecondary);

    /// <summary>A frozen brush for <see cref="TextMuted"/>.</summary>
    public static IBrush TextMutedBrush { get; } = Frozen(TextMuted);

    /// <summary>A frozen brush for <see cref="TextInverse"/>.</summary>
    public static IBrush TextInverseBrush { get; } = Frozen(TextInverse);

    /// <summary>A frozen brush for <see cref="PortRest"/>.</summary>
    public static IBrush PortRestBrush { get; } = Frozen(PortRest);

    /// <summary>A frozen brush for <see cref="PortConnected"/>.</summary>
    public static IBrush PortConnectedBrush { get; } = Frozen(PortConnected);

    /// <summary>A frozen brush for <see cref="Accent"/>.</summary>
    public static IBrush AccentBrush { get; } = Frozen(Accent);

    /// <summary>
    /// The marquee fill: <c>accent</c> at 14%. Permitted because a marquee lands only on empty
    /// canvas — an accent tint is forbidden anywhere text sits over it (§5.4).
    /// </summary>
    public static IBrush MarqueeFillBrush { get; } = Frozen(Color.FromArgb(0x24, 0xA9, 0x8B, 0xFF));

    /// <summary>Builds a frozen solid brush, which is safe to share across threads and draws.</summary>
    /// <param name="colour">The colour.</param>
    /// <returns>An immutable brush.</returns>
    public static IBrush Frozen(Color colour) => new ImmutableSolidColorBrush(colour);

    /// <summary>
    /// Mixes two colours in sRGB component space.
    /// </summary>
    /// <param name="from">The colour at <paramref name="amount"/> zero.</param>
    /// <param name="to">The colour at <paramref name="amount"/> one.</param>
    /// <param name="amount">The blend factor, clamped to 0..1.</param>
    /// <returns>The blended colour.</returns>
    /// <remarks>
    /// Component-space mixing is what the design language's own numbers were computed with — the
    /// "+14% white" hover values in §7.2 are component mixes — so mixing in a perceptual space
    /// here would produce colours that do not match the table.
    /// </remarks>
    public static Color Mix(Color from, Color to, double amount)
    {
        double t = amount < 0 ? 0 : amount > 1 ? 1 : amount;
        return Color.FromArgb(
            (byte)(from.A + ((to.A - from.A) * t)),
            (byte)(from.R + ((to.R - from.R) * t)),
            (byte)(from.G + ((to.G - from.G) * t)),
            (byte)(from.B + ((to.B - from.B) * t)));
    }
}
