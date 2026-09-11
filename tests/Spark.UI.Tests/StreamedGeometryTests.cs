using System.Threading;
using System.Threading.Tasks;
using Spark.UI.ViewModels;
using Spark.Viewport;

namespace Spark.UI.Tests;

/// <summary>
/// Geometry reaches the viewport as its node finishes, not when the whole run does (`E9-T7`).
/// </summary>
/// <remarks>
/// <b>Deterministic, not timed.</b> The session reports a node from inside the evaluation, so a node's
/// geometry that is streamed at all is streamed before <see cref="MainWindowViewModel.EvaluateAsync"/>
/// can return. What the test pins is that it is streamed at all, and that the scene holds the package
/// at the moment the view is told.
/// </remarks>
public sealed class StreamedGeometryTests
{
    /// <summary>
    /// <b>The row.</b> The demo's points are in the scene, and the view has been told, before the run
    /// has returned.
    /// </summary>
    [Fact]
    public async Task ANodesGeometryReachesTheSceneBeforeTheRunReturns()
    {
        using MainWindowViewModel model = new("demo");

        int returned = 0;
        int streamedBeforeReturn = 0;
        int packagesWhenStreamed = 0;

        model.GeometryStreamed += (_, _) =>
        {
            if (Volatile.Read(ref returned) == 0)
            {
                Interlocked.Increment(ref streamedBeforeReturn);
                Interlocked.Exchange(ref packagesWhenStreamed, model.Scene.Count);
            }
        };

        await model.EvaluateAsync();
        Volatile.Write(ref returned, 1);

        Assert.True(streamedBeforeReturn > 0, "no geometry reached the scene before the run returned");
        Assert.Equal(1, packagesWhenStreamed);
    }

    /// <summary>
    /// After the run the scene is the full publish's: one package of the hundred points, drawn as it
    /// was before anything was streamed.
    /// </summary>
    [Fact]
    public async Task TheSceneAfterTheRunIsTheFullPublish()
    {
        using MainWindowViewModel model = new("demo");
        await model.EvaluateAsync();

        RenderPackage package = Assert.Single(model.Scene.Snapshot());

        Assert.Equal(100 * 8, package.TriangleCount);
    }
}
