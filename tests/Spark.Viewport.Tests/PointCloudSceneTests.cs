using Spark.Geometry;
using Spark.Viewport;

namespace Spark.Viewport.Tests;

/// <summary>
/// A <see cref="PointCloud"/> reaches the viewport (`E2-T21`).
/// </summary>
/// <remarks>
/// Drawn as its points are - one marker each, in one buffer set for its key - so a cloud and the list
/// of points it was made from look the same, which is what they are from where the viewport stands.
/// </remarks>
public sealed class PointCloudSceneTests
{
    /// <summary>A cloud of five points is one package of five markers.</summary>
    [Fact]
    public void ACloudIsDrawnAsItsPoints()
    {
        PointCloud cloud = new(
        [
            new Point3d(0, 0, 0),
            new Point3d(1, 0, 0),
            new Point3d(0, 1, 0),
            new Point3d(0, 0, 1),
            new Point3d(1, 1, 1),
        ]);

        SceneBuilder builder = new();
        builder.Add(new GeometryKey("cloud", 0), cloud);

        RenderPackage package = Assert.Single(builder.Build());

        Assert.Equal(5, builder.RenderableCount);
        Assert.Equal(0, builder.UnrenderableCount);

        // Eight faces per marker, as for any point.
        Assert.Equal(5 * 8, package.TriangleCount);
    }
}
