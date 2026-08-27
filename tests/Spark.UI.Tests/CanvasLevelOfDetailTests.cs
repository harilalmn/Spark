using Spark.UI.Canvas;

namespace Spark.UI.Tests;

/// <summary>
/// The level-of-detail table from <c>docs/help/concepts/design-language.md</c> §7.3, asserted at
/// each of its boundaries.
/// </summary>
/// <remarks>
/// The ordering of the thresholds is load-bearing rather than cosmetic. Body text is dropped at the
/// same zoom the body fill starts lerping towards the category colour — not one step later —
/// because brightening a surface under light text is forbidden by Principle 2. The last test here
/// is that rule stated as an assertion.
/// </remarks>
public sealed class CanvasLevelOfDetailTests
{
    [Theory]
    [InlineData(0.05, CanvasDetail.Silhouette)]
    [InlineData(0.39, CanvasDetail.Silhouette)]
    [InlineData(0.40, CanvasDetail.Fill)]
    [InlineData(0.66, CanvasDetail.Fill)]
    [InlineData(0.67, CanvasDetail.Title)]
    [InlineData(0.72, CanvasDetail.Title)]
    [InlineData(0.73, CanvasDetail.Lip)]
    [InlineData(0.81, CanvasDetail.Lip)]
    [InlineData(0.82, CanvasDetail.Shadow)]
    [InlineData(0.99, CanvasDetail.Shadow)]
    [InlineData(1.00, CanvasDetail.Full)]
    [InlineData(4.00, CanvasDetail.Full)]
    public void ZoomMapsToTheDetailLevelTheDesignLanguageNames(double zoom, CanvasDetail expected) =>
        Assert.Equal(expected, CanvasLevelOfDetail.For(zoom));

    [Fact]
    public void BelowFortyPercentANodeIsNothingButItsCategoryFill()
    {
        CanvasDetail detail = CanvasLevelOfDetail.For(0.3);

        Assert.False(CanvasLevelOfDetail.DrawsTitle(detail));
        Assert.False(CanvasLevelOfDetail.DrawsPortLabels(detail));
        Assert.False(CanvasLevelOfDetail.DrawsShadow(detail));
        Assert.False(CanvasLevelOfDetail.DrawsLip(detail));

        // No outline either: the category fill is at least 5.39:1 against the canvas on its own,
        // so an outline would only muddy the one thing still carrying identity.
        Assert.False(CanvasLevelOfDetail.DrawsOutline(detail));
    }

    [Fact]
    public void AtFullZoomEveryCueIsDrawn()
    {
        CanvasDetail detail = CanvasLevelOfDetail.For(1.0);

        Assert.True(CanvasLevelOfDetail.DrawsTitle(detail));
        Assert.True(CanvasLevelOfDetail.DrawsPortLabels(detail));
        Assert.True(CanvasLevelOfDetail.DrawsOutline(detail));
        Assert.True(CanvasLevelOfDetail.DrawsShadow(detail));
        Assert.True(CanvasLevelOfDetail.DrawsLip(detail));
    }

    [Fact]
    public void PortLabelsAreDroppedBeforeTheHeaderTitle()
    {
        // 11 px port labels hit the 8 px floor at 73%; the 12 px header title does not until 67%.
        CanvasDetail atSeventy = CanvasLevelOfDetail.For(0.70);

        Assert.False(CanvasLevelOfDetail.DrawsPortLabels(atSeventy));
        Assert.True(CanvasLevelOfDetail.DrawsTitle(atSeventy));
    }

    [Fact]
    public void TheShadowIsDroppedBeforeTheLip()
    {
        // A blurred shadow stops reading as depth once its radius falls under about four device
        // pixels; a 1 px line does not stop reading as a line until it is gone.
        CanvasDetail atSeventyEight = CanvasLevelOfDetail.For(0.78);

        Assert.False(CanvasLevelOfDetail.DrawsShadow(atSeventyEight));
        Assert.True(CanvasLevelOfDetail.DrawsLip(atSeventyEight));
    }

    [Fact]
    public void TheBodyOnlyStartsLerpingTowardsTheCategoryColourOnceTextIsGone()
    {
        // Principle 2: a state change may never lower contrast. Brightening the body under light
        // text would, so the text goes first and the ordering is what makes the rule survive.
        Assert.Equal(0, CanvasLevelOfDetail.CategoryFillBlend(0.67));
        Assert.Equal(0, CanvasLevelOfDetail.CategoryFillBlend(1.0));

        Assert.True(CanvasLevelOfDetail.CategoryFillBlend(0.66) > 0);
        Assert.False(CanvasLevelOfDetail.DrawsTitle(CanvasLevelOfDetail.For(0.66)));
    }

    [Fact]
    public void TheCategoryBlendRisesMonotonicallyToOneAtTheLevelOfDetailThreshold()
    {
        double previous = 0;
        for (double zoom = 0.67; zoom >= 0.40; zoom -= 0.01)
        {
            double blend = CanvasLevelOfDetail.CategoryFillBlend(zoom);
            Assert.InRange(blend, previous, 1.0);
            previous = blend;
        }

        Assert.Equal(1, CanvasLevelOfDetail.CategoryFillBlend(0.40));
        Assert.Equal(1, CanvasLevelOfDetail.CategoryFillBlend(0.20));
    }

    [Fact]
    public void AnimationIsSwitchedOffWhenEitherTheZoomOrTheNodeCountLeavesBudget()
    {
        Assert.True(CanvasLevelOfDetail.AllowsAnimation(1.0, 100));

        // §10.2: all per-node transitions are switched off entirely when more than 400 nodes are
        // visible or the zoom is below 60%.
        Assert.False(CanvasLevelOfDetail.AllowsAnimation(0.59, 100));
        Assert.False(CanvasLevelOfDetail.AllowsAnimation(1.0, 401));
        Assert.True(CanvasLevelOfDetail.AllowsAnimation(0.60, 400));
    }
}
