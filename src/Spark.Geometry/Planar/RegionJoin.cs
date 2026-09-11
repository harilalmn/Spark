namespace Spark.Geometry.Planar;

/// <summary>How a region's corner is turned when its boundary is offset past it (<c>E2-T13</c>).</summary>
public enum RegionJoin
{
    /// <summary>An arc about the old corner: every point of the new boundary is exactly the distance away.</summary>
    Round,

    /// <summary>The two edges extended until they meet, as far as the miter limit allows.</summary>
    Miter,

    /// <summary>The corner cut off square at the offset distance.</summary>
    Square,
}
