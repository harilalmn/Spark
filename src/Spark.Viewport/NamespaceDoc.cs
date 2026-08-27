namespace Spark.Viewport;

/// <summary>
/// Documents the conventions every type in the <c>Spark.Viewport</c> namespace obeys. This
/// type exists only to carry that documentation; it has no members and is never instantiated.
/// </summary>
/// <remarks>
/// <para>
/// <b>Handedness.</b> The viewport is <b>right-handed</b> and matches the kernel exactly
/// (<c>Spark.Geometry.NamespaceDoc</c>): the cross product of world X with world Y is world Z,
/// and a positive rotation about an axis is counter-clockwise viewed from that axis's positive
/// end. This is stated once, here, and held to everywhere. A disagreement between viewport and
/// kernel handedness presents as geometry that looks correct until it is mirrored, which is the
/// worst possible way to find out.
/// </para>
/// <para>
/// <b>Up is +Z.</b> The ground plane is the world XY plane and the camera's up vector is +Z.
/// That follows from the kernel: <c>Plane.WorldXY</c> has a normal of +Z, so the plane a user
/// draws on by default is the one the grid is drawn on.
/// </para>
/// <para>
/// <b>Winding and normals.</b> Triangles are wound <b>counter-clockwise when seen from
/// outside</b> the solid, and a vertex normal points away from the material. Back-face culling
/// is nevertheless <b>off</b> and the shading is two-sided: a fragment whose normal faces away
/// from the eye is lit with the negated normal. That is deliberate. Culling turns an incoming
/// winding defect into invisible geometry, which is indistinguishable from "the renderer is
/// broken"; two-sided shading turns the same defect into geometry that is visible but shaded
/// oddly, which is diagnosable. Watertightness and winding are asserted on the producer side —
/// see <c>Spark.Viewport.Meshes</c> — not papered over here.
/// </para>
/// <para>
/// <b>No Avalonia.</b> Nothing in this assembly may reference Avalonia, and
/// <c>Spark.Architecture.Tests</c> enforces it. Keeping the renderer UI-agnostic is what lets a
/// software backend run headlessly, which is the only thing that makes viewport output
/// deterministic and therefore testable at all. GL entry points reach this assembly through
/// <see cref="OpenGL.IGlApi"/>, which <c>Spark.UI</c> implements over Avalonia's
/// <c>GlInterface</c>.
/// </para>
/// <para>
/// <b>Identity comes from the graph.</b> Geometry has no identity of its own. A
/// <see cref="RenderPackage"/> is keyed by <see cref="GeometryKey"/> — the
/// <c>(NodeId, PortIndex)</c> tuple — and there is exactly one GPU buffer set per key, so
/// re-evaluating one node re-uploads one buffer and selection synchronisation falls out of the
/// same tuple with no parallel bookkeeping.
/// </para>
/// </remarks>
internal static class NamespaceDoc
{
}
