using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Mesh.MadeWatertight"/> — `E2-T68`.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="MeshTopology.IsClosed"/> is the oracle, and it answers two different failures</b>
/// — a hole left open, and a patch wound the wrong way round, which makes the mesh non-manifold
/// instead. A single <c>IsClosed</c> assertion cannot say which went wrong, so
/// <see cref="MeshTopology.NakedEdgeCount"/> and <see cref="MeshTopology.NonManifoldEdgeCount"/>
/// are asserted separately.
/// </para>
/// <para>
/// <b>Two holes is the fixture that matters</b>, because an implementation that finds one cycle and
/// stops passes every test a single-holed mesh can offer.
/// </para>
/// </remarks>
public sealed class MeshWatertightTests
{
    /// <summary>A box with one face removed comes back closed.</summary>
    [Fact]
    public void AMeshWithOneHoleComesBackClosed()
    {
        Mesh holed = BoxWithFacesRemoved(0);

        Assert.False(holed.Topology.IsClosed, "the fixture is not holed.");

        Mesh closed = holed.MadeWatertight();

        Assert.Equal(0, closed.Topology.NakedEdgeCount);
        Assert.Equal(0, closed.Topology.NonManifoldEdgeCount);
        Assert.True(closed.Topology.IsClosed, "the mesh is still not closed.");
    }

    /// <summary>
    /// <b>The branch.</b> Two separate holes, and both must be closed — one cycle found and the
    /// walk stopped is the failure this catches.
    /// </summary>
    [Fact]
    public void AMeshWithTwoSeparateHolesComesBackClosed()
    {
        // Opposite faces of the box, so the two boundaries share no vertex.
        Mesh holed = BoxWithFacesRemoved(0, 1);

        Assert.Equal(8, holed.Topology.NakedEdgeCount);

        Mesh closed = holed.MadeWatertight();

        Assert.Equal(0, closed.Topology.NakedEdgeCount);
        Assert.Equal(0, closed.Topology.NonManifoldEdgeCount);
        Assert.True(closed.Topology.IsClosed, "one of the two holes is still open.");
    }

    /// <summary>
    /// <b>The fixture that found the real defect, kept for that reason.</b> Three faces off a box
    /// leaves one boundary running through vertices that are <i>still joined to each other</i> — so
    /// fanning the patch from one of them draws a chord that duplicates an edge the mesh already
    /// has, and the result is non-manifold rather than closed. It is the case that says a fan from
    /// a boundary vertex is not merely ugly but invalid, and it is why the patch is hubbed on a new
    /// vertex instead.
    /// </summary>
    [Fact]
    public void AFanFromABoundaryVertexWouldDuplicateAnEdgeTheMeshAlreadyHas()
    {
        Mesh holed = BoxWithFacesRemoved(0, 1, 2);

        Mesh closed = holed.MadeWatertight();

        Assert.Equal(0, closed.Topology.NakedEdgeCount);
        Assert.Equal(
            0,
            closed.Topology.NonManifoldEdgeCount);
        Assert.True(
            closed.Topology.IsClosed,
            "the patch duplicated an edge the mesh already had, which is non-manifold rather than "
            + "closed.");
    }

    /// <summary>
    /// <b>The second branch.</b> The patch is wound against its boundary. Wound with it instead,
    /// every shared edge is traversed twice the same way and the mesh is <i>non-manifold</i> —
    /// a different defect with the same symptom, which is why this is asserted on its own.
    /// </summary>
    [Fact]
    public void ThePatchIsWoundAgainstItsBoundary()
    {
        Mesh closed = BoxWithFacesRemoved(0).MadeWatertight();

        Assert.Equal(
            0,
            closed.Topology.NonManifoldEdgeCount);
        Assert.True(
            closed.Topology.IsManifold,
            "the patch was wound the same way as its boundary, so the shared edges are traversed "
            + "twice in the same direction.");
    }

    /// <summary>
    /// The patch keeps every face the mesh had and adds <b>one</b> vertex per hole — the hub the
    /// fan is drawn from.
    /// </summary>
    [Fact]
    public void TheOriginalFacesAreKeptAndOneHubIsAddedPerHole()
    {
        Mesh holed = BoxWithFacesRemoved(0);

        Mesh closed = holed.MadeWatertight();

        Assert.True(closed.FaceCount > holed.FaceCount, "no faces were added at all.");
        Assert.Equal(holed.VertexCount + 1, closed.VertexCount);

        // Two holes, two hubs.
        Mesh twice = BoxWithFacesRemoved(0, 1).MadeWatertight();

        Assert.Equal(holed.VertexCount + 2, twice.VertexCount);
    }

    /// <summary>
    /// <b>A triangular hole is the one case that needs no hub</b>, because its three boundary edges
    /// already are the patch — so nothing is invented for it.
    /// </summary>
    [Fact]
    public void ATriangularHoleIsClosedWithoutAHub()
    {
        Mesh holed = TetrahedronWithAFaceRemoved();

        Assert.Equal(3, holed.Topology.NakedEdgeCount);

        Mesh closed = holed.MadeWatertight();

        Assert.Equal(4, closed.VertexCount);
        Assert.Equal(4, closed.FaceCount);
        Assert.Equal(0, closed.Topology.NakedEdgeCount);
        Assert.Equal(0, closed.Topology.NonManifoldEdgeCount);
    }

    /// <summary>A mesh that is already closed comes back unchanged rather than rebuilt.</summary>
    [Fact]
    public void AClosedMeshIsUnchanged()
    {
        Mesh box = Box();

        Assert.True(box.Topology.IsClosed, "the fixture is not closed.");

        Mesh again = box.MadeWatertight();

        Assert.Equal(box.FaceCount, again.FaceCount);
        Assert.Equal(box.VertexCount, again.VertexCount);
        Assert.Equal(0, again.Topology.NakedEdgeCount);
    }

    /// <summary>
    /// <b>A cracked mesh closes too</b>, which is what the weld is for: the two sides of a crack
    /// are different vertices, so the boundary never meets itself and no patch can close it.
    /// </summary>
    /// <remarks>
    /// <b>The fixture is the raw primitive, and that it works as one is worth recording.</b>
    /// <see cref="MeshPrimitives.Cuboid"/> gives its six sides their own vertices on purpose, so
    /// that each shades flat — which means the primitive is <i>entirely</i> crack, twenty-four
    /// vertices where a box has eight and every edge naked. It is the honest fixture for this and a
    /// misleading one for everything else here.
    /// </remarks>
    [Fact]
    public void ACrackedMeshIsWeldedBeforeItsHolesAreWalked()
    {
        Mesh cracked = MeshPrimitives.Cuboid(Plane.WorldXY, 2.0, 2.0, 2.0);

        Assert.Equal(24, cracked.VertexCount);
        Assert.True(cracked.Topology.NakedEdgeCount > 0, "the fixture has no crack in it.");

        Mesh closed = cracked.MadeWatertight(1e-9);

        Assert.Equal(8, closed.VertexCount);
        Assert.Equal(0, closed.Topology.NakedEdgeCount);
        Assert.Equal(0, closed.Topology.NonManifoldEdgeCount);
    }

    /// <summary>The volume a closed mesh encloses is the box's, which a bad patch would not give.</summary>
    [Fact]
    public void ThePatchedBoxEnclosesTheRightVolume()
    {
        Mesh closed = BoxWithFacesRemoved(0).MadeWatertight();

        Assert.Equal(8.0, Math.Abs(SignedVolume(closed)), 1e-9);
    }

    /// <summary>A tetrahedron with one of its four triangles taken away.</summary>
    /// <returns>The holed tetrahedron: four vertices, three faces, a triangular boundary.</returns>
    private static Mesh TetrahedronWithAFaceRemoved() =>
        new(
            [
                new Point3d(0, 0, 0),
                new Point3d(4, 0, 0),
                new Point3d(0, 4, 0),
                new Point3d(0, 0, 4),
            ],
            [
                new MeshFace(0, 2, 1),
                new MeshFace(0, 1, 3),
                new MeshFace(0, 3, 2),
            ],
            null,
            null,
            null);

    /// <summary>A closed two-by-two-by-two box.</summary>
    /// <returns>The box, welded — see the remark on the cracked-mesh test for why that matters.</returns>
    private static Mesh Box() => MeshPrimitives.Cuboid(Plane.WorldXY, 2.0, 2.0, 2.0).Welded();

    /// <summary>A closed box with the named faces taken out of it.</summary>
    /// <param name="removed">Which face indices to leave out.</param>
    /// <returns>The holed mesh.</returns>
    private static Mesh BoxWithFacesRemoved(params int[] removed)
    {
        Mesh box = Box();
        HashSet<int> drop = [.. removed];
        List<MeshFace> kept = [];

        MeshFace[] faces = box.Faces();
        for (int index = 0; index < faces.Length; index++)
        {
            if (!drop.Contains(index))
            {
                kept.Add(faces[index]);
            }
        }

        return new Mesh(box.Vertices(), kept, null, null, null).Repaired();
    }

    /// <summary>Six times the signed volume a closed mesh encloses, divided back down.</summary>
    /// <param name="mesh">The mesh.</param>
    /// <returns>The signed volume.</returns>
    /// <remarks>
    /// The divergence theorem over triangle fans: a patch that closed the hole with the wrong
    /// shape, or left it open, does not give the box's volume.
    /// </remarks>
    private static double SignedVolume(Mesh mesh)
    {
        Point3d[] vertices = mesh.Vertices();
        double total = 0.0;

        foreach (MeshFace face in mesh.Faces())
        {
            for (int corner = 2; corner < face.Count; corner++)
            {
                Vector3d a = vertices[face[0]] - Point3d.Origin;
                Vector3d b = vertices[face[corner - 1]] - Point3d.Origin;
                Vector3d c = vertices[face[corner]] - Point3d.Origin;

                total += a.Dot(b.Cross(c));
            }
        }

        return total / 6.0;
    }
}
