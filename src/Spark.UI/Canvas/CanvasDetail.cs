namespace Spark.UI.Canvas;

/// <summary>
/// What a node draws at the current zoom, from the table in
/// <c>docs/help/concepts/design-language.md</c> §7.3. Each cue is dropped at the zoom where it
/// stops paying for itself, and something cheaper takes over its job.
/// </summary>
/// <remarks>
/// The ordering is not arbitrary and must not be reordered for convenience. Body text is dropped
/// at the same zoom the body fill starts lerping towards the category colour — not one step later
/// — because brightening a surface under light text is forbidden by Principle 2. The rule survives
/// only because the ordering was chosen to make it survive.
/// </remarks>
public enum CanvasDetail
{
    /// <summary>
    /// Below 40%: a plain category-coloured rounded rectangle. No text, no ports, no shadow, no
    /// outline — the fill clears 3:1 against the canvas on its own (5.39:1 at worst).
    /// </summary>
    Silhouette,

    /// <summary>
    /// 40–67%: flat, no text at all, body lerping towards the category colour, ports as 2 px
    /// screen-space dots, 1 px outline.
    /// </summary>
    Fill,

    /// <summary>67–73%: flat, header title only, port labels dropped, 4 px port discs.</summary>
    Title,

    /// <summary>73–82%: the lip is retained, the shadow is gone, ports become plain discs.</summary>
    Lip,

    /// <summary>82–100%: the highlight half of the depth pair is dropped; everything else stands.</summary>
    Shadow,

    /// <summary>At or above 100%: full E2 depth, all text, shaped ports, the preview toggle.</summary>
    Full,
}

/// <summary>
/// Maps a zoom factor to a <see cref="CanvasDetail"/>, and answers the two questions the renderer
/// asks most often.
/// </summary>
public static class CanvasLevelOfDetail
{
    /// <summary>Below this zoom a node is a plain coloured rectangle (ADR-0013).</summary>
    public const double SilhouetteThreshold = 0.40;

    /// <summary>Below this zoom all node text is dropped: 12 px × 0.67 is 8.04 px.</summary>
    public const double TextThreshold = 0.67;

    /// <summary>Below this zoom port labels are dropped: 11 px × 0.73 is 8.03 px.</summary>
    public const double PortLabelThreshold = 0.73;

    /// <summary>Below this zoom the E2 shadow is dropped; its blur falls under five device pixels.</summary>
    public const double ShadowThreshold = 0.82;

    /// <summary>At or above this zoom the full depth pair is drawn.</summary>
    public const double FullDepthThreshold = 1.00;

    /// <summary>
    /// Below this zoom, or above <see cref="MaximumAnimatedNodes"/> visible nodes, per-node state
    /// transitions are switched off entirely (design language §10.2).
    /// </summary>
    public const double AnimationThreshold = 0.60;

    /// <summary>
    /// The most nodes that may animate at once. Above this, state changes are instantaneous —
    /// which is also what a user zoomed out to survey a graph actually wants.
    /// </summary>
    public const int MaximumAnimatedNodes = 400;

    /// <summary>The detail level for a zoom factor.</summary>
    /// <param name="zoom">Screen pixels per world unit.</param>
    /// <returns>The level to draw at.</returns>
    public static CanvasDetail For(double zoom) => zoom switch
    {
        < SilhouetteThreshold => CanvasDetail.Silhouette,
        < TextThreshold => CanvasDetail.Fill,
        < PortLabelThreshold => CanvasDetail.Title,
        < ShadowThreshold => CanvasDetail.Lip,
        < FullDepthThreshold => CanvasDetail.Shadow,
        _ => CanvasDetail.Full,
    };

    /// <summary>Whether the node header title is drawn at this level.</summary>
    /// <param name="detail">The level.</param>
    /// <returns>True at <see cref="CanvasDetail.Title"/> and above.</returns>
    public static bool DrawsTitle(CanvasDetail detail) => detail >= CanvasDetail.Title;

    /// <summary>Whether port labels are drawn at this level.</summary>
    /// <param name="detail">The level.</param>
    /// <returns>True at <see cref="CanvasDetail.Lip"/> and above.</returns>
    public static bool DrawsPortLabels(CanvasDetail detail) => detail >= CanvasDetail.Lip;

    /// <summary>Whether the node's 1 px <c>border.control</c> outline is drawn.</summary>
    /// <param name="detail">The level.</param>
    /// <returns>
    /// True everywhere except <see cref="CanvasDetail.Silhouette"/>, where the category fill is
    /// its own boundary and the outline would only muddy it.
    /// </returns>
    public static bool DrawsOutline(CanvasDetail detail) => detail > CanvasDetail.Silhouette;

    /// <summary>Whether the E2 drop shadow is drawn.</summary>
    /// <param name="detail">The level.</param>
    /// <returns>True at <see cref="CanvasDetail.Shadow"/> and above.</returns>
    public static bool DrawsShadow(CanvasDetail detail) => detail >= CanvasDetail.Shadow;

    /// <summary>Whether the 1 px lit lip along the top and left edges is drawn.</summary>
    /// <param name="detail">The level.</param>
    /// <returns>True at <see cref="CanvasDetail.Lip"/> and above.</returns>
    public static bool DrawsLip(CanvasDetail detail) => detail >= CanvasDetail.Lip;

    /// <summary>
    /// How far the node body has lerped from <c>node.body</c> towards its category colour, 0..1.
    /// </summary>
    /// <param name="zoom">Screen pixels per world unit.</param>
    /// <returns>
    /// Zero at and above <see cref="TextThreshold"/>, rising to one at
    /// <see cref="SilhouetteThreshold"/>, so the level-of-detail transition is a fade rather than
    /// a jump from a grey-blue node to a saturated rectangle.
    /// </returns>
    public static double CategoryFillBlend(double zoom)
    {
        if (zoom >= TextThreshold)
        {
            return 0;
        }

        if (zoom <= SilhouetteThreshold)
        {
            return 1;
        }

        return (TextThreshold - zoom) / (TextThreshold - SilhouetteThreshold);
    }

    /// <summary>Whether per-node hover and selection transitions may animate.</summary>
    /// <param name="zoom">Screen pixels per world unit.</param>
    /// <param name="visibleNodes">How many nodes the last cull found.</param>
    /// <returns>True only when both the zoom and the visible count are inside budget.</returns>
    public static bool AllowsAnimation(double zoom, int visibleNodes) =>
        zoom >= AnimationThreshold && visibleNodes <= MaximumAnimatedNodes;
}
