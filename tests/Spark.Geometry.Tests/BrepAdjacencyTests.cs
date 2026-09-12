using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="BrepAdjacency"/> — the topology read in the direction the model does not store
/// (`E2-T65`, out of `E2-T44`'s assessment of DYNAMO-COVERAGE §3.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>The plate with a square hole is the fixture that matters.</b> A box exercises the counts and
/// the two-faces-per-edge case, and every one of its faces has exactly one loop; a face carrying an
/// <b>inner</b> loop is where a reverse lookup that stops at a face's first loop gives a wrong
/// answer that a box would never show. It is also the only fixture here whose edges are used once
/// rather than twice, which is what <see cref="BrepAdjacency.PartnerOf"/> has to say <i>no</i> to.
/// </para>
/// <para>
/// <b>Every derived answer is checked against the model it was derived from</b>, wherever
/// transcribing an expectation would be a second implementation of the thing under test. Where a
/// hand-checkable number exists — a box corner touches three edges and three faces — it is
/// asserted as a number, because a self-consistent wrong answer is exactly what a
/// derivation-checked test cannot see.
/// </para>
/// </remarks>
public sealed class BrepAdjacencyTests
{
    // -- Vertices --------------------------------------------------------------------------------

    /// <summary>Every corner of a box touches three edges, and a box has no other kind of corner.</summary>
    [Fact]
    public void EveryBoxVertexTouchesThreeEdges()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepAdjacency adjacency = BrepAdjacency.Of(box);

        for (int vertex = 0; vertex < box.VertexCount; vertex++)
        {
            Assert.Equal(3, adjacency.EdgesAt(vertex).Length);
        }
    }

    /// <summary>Every corner of a box touches three faces.</summary>
    [Fact]
    public void EveryBoxVertexTouchesThreeFaces()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepAdjacency adjacency = BrepAdjacency.Of(box);

        for (int vertex = 0; vertex < box.VertexCount; vertex++)
        {
            Assert.Equal(3, adjacency.FacesAt(vertex).Length);
        }
    }

    /// <summary>
    /// <b>The edges at a vertex are exactly the edges that name it</b>, which is the whole claim the
    /// reverse index makes and the only one worth checking against the model itself.
    /// </summary>
    [Fact]
    public void TheEdgesAtAVertexAreTheEdgesThatNameIt()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepAdjacency adjacency = BrepAdjacency.Of(box);
        BrepEdge[] edges = box.Edges();

        for (int vertex = 0; vertex < box.VertexCount; vertex++)
        {
            int[] expected = [.. Enumerable
                .Range(0, edges.Length)
                .Where(edge => edges[edge].Start == vertex || edges[edge].End == vertex)];

            Assert.Equal(expected, adjacency.EdgesAt(vertex).ToArray());
        }
    }

    /// <summary>They come back in ascending index order, so two answers can be compared directly.</summary>
    [Fact]
    public void TheEdgesAtAVertexAreInAscendingOrder()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepAdjacency adjacency = BrepAdjacency.Of(box);

        for (int vertex = 0; vertex < box.VertexCount; vertex++)
        {
            int[] found = adjacency.EdgesAt(vertex).ToArray();

            Assert.Equal([.. found.Order()], found);
        }
    }

    /// <summary>
    /// <b>An edge that starts and ends at the same vertex is listed once, not twice.</b> A counting
    /// pass that adds a contribution per endpoint without noticing reports such a vertex as touching
    /// one more edge than it does, and leaves a duplicate in the answer.
    /// </summary>
    [Fact]
    public void AnEdgeThatStartsAndEndsAtOneVertexIsListedOnce()
    {
        Brep brep = ClosedEdgeFixture();
        BrepAdjacency adjacency = BrepAdjacency.Of(brep);
        BrepEdge[] edges = brep.Edges();

        int closed = Array.FindIndex(edges, edge => edge.Start == edge.End);

        Assert.True(closed >= 0, "the fixture no longer has an edge that closes on itself");

        int[] at = adjacency.EdgesAt(edges[closed].Start).ToArray();

        Assert.Equal(at.Distinct().Count(), at.Length);
        Assert.Contains(closed, at);
    }

    // -- Edges and their trims -------------------------------------------------------------------

    /// <summary>Every edge of a solid is used by exactly two trims.</summary>
    [Fact]
    public void EveryEdgeOfASolidHasTwoTrims()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepAdjacency adjacency = BrepAdjacency.Of(box);

        for (int edge = 0; edge < box.EdgeCount; edge++)
        {
            Assert.Equal(2, adjacency.TrimsOf(edge).Length);
        }
    }

    /// <summary>The trims of an edge are exactly the trims that name it.</summary>
    [Fact]
    public void TheTrimsOfAnEdgeAreTheTrimsThatNameIt()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepAdjacency adjacency = BrepAdjacency.Of(box);
        BrepTrim[] trims = box.Trims();

        for (int edge = 0; edge < box.EdgeCount; edge++)
        {
            int[] expected = [.. Enumerable.Range(0, trims.Length).Where(trim => trims[trim].Edge == edge)];

            Assert.Equal(expected, adjacency.TrimsOf(edge).ToArray());
        }
    }

    /// <summary>A trim's partner is the other trim on its edge, and partnering is symmetric.</summary>
    [Fact]
    public void ATrimsPartnerIsTheOtherTrimOnItsEdge()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepAdjacency adjacency = BrepAdjacency.Of(box);
        BrepTrim[] trims = box.Trims();

        for (int trim = 0; trim < box.TrimCount; trim++)
        {
            int partner = adjacency.PartnerOf(trim);

            Assert.NotEqual(trim, partner);
            Assert.Equal(trims[trim].Edge, trims[partner].Edge);
            Assert.Equal(trim, adjacency.PartnerOf(partner));
        }
    }

    /// <summary>
    /// <b>A trim on an edge nothing else uses has no partner, and the answer is -1 rather than an
    /// exception.</b> A naked edge is a legitimate model — every edge of the plate is one — so a
    /// caller walking every trim must be able to ask without guarding first.
    /// </summary>
    [Fact]
    public void ATrimOnANakedEdgeHasNoPartner()
    {
        Brep plate = PlateWithASquareHole();
        BrepAdjacency adjacency = BrepAdjacency.Of(plate);

        for (int trim = 0; trim < plate.TrimCount; trim++)
        {
            Assert.Equal(-1, adjacency.PartnerOf(trim));
        }
    }

    // -- The one-way relationships read backwards ------------------------------------------------

    /// <summary>A trim's loop is the loop whose span contains it.</summary>
    [Fact]
    public void ATrimsLoopIsTheLoopWhoseSpanContainsIt()
    {
        Brep plate = PlateWithASquareHole();
        BrepAdjacency adjacency = BrepAdjacency.Of(plate);
        BrepLoop[] loops = plate.Loops();

        for (int trim = 0; trim < plate.TrimCount; trim++)
        {
            BrepLoop loop = loops[adjacency.LoopOf(trim)];

            Assert.InRange(trim, loop.FirstTrim, loop.FirstTrim + loop.TrimCount - 1);
        }
    }

    /// <summary>A loop's face is the face whose span contains it.</summary>
    [Fact]
    public void ALoopsFaceIsTheFaceWhoseSpanContainsIt()
    {
        Brep plate = PlateWithASquareHole();
        BrepAdjacency adjacency = BrepAdjacency.Of(plate);
        BrepFace[] faces = plate.Faces();

        for (int loop = 0; loop < plate.LoopCount; loop++)
        {
            BrepFace face = faces[adjacency.FaceOf(loop)];

            Assert.InRange(loop, face.FirstLoop, face.FirstLoop + face.LoopCount - 1);
        }
    }

    /// <summary>
    /// <b>Both of the plate's loops belong to its one face</b>, which is the assertion a reverse
    /// lookup that stops at a face's first loop fails.
    /// </summary>
    [Fact]
    public void AnInnerLoopBelongsToItsFaceJustAsTheOuterOneDoes()
    {
        Brep plate = PlateWithASquareHole();
        BrepAdjacency adjacency = BrepAdjacency.Of(plate);

        Assert.Equal(2, plate.LoopCount);
        Assert.Equal(BrepLoopKind.Inner, plate.Loops()[1].Kind);
        Assert.Equal(0, adjacency.FaceOf(0));
        Assert.Equal(0, adjacency.FaceOf(1));
    }

    // -- Faces -----------------------------------------------------------------------------------

    /// <summary>
    /// <b>A face with a hole has the edges of both its loops</b>, which is the case that makes
    /// <c>Face.Edges</c> more than the outer loop's.
    /// </summary>
    [Fact]
    public void AFaceWithAHoleHasTheEdgesOfBothItsLoops()
    {
        Brep plate = PlateWithASquareHole();
        BrepAdjacency adjacency = BrepAdjacency.Of(plate);

        Assert.Equal(8, adjacency.EdgesOf(0).Length);
        Assert.Equal(8, adjacency.VerticesOf(0).Length);
    }

    /// <summary>Each of a box's faces has four edges and four vertices.</summary>
    [Fact]
    public void EveryBoxFaceHasFourEdgesAndFourVertices()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepAdjacency adjacency = BrepAdjacency.Of(box);

        for (int face = 0; face < box.FaceCount; face++)
        {
            Assert.Equal(4, adjacency.EdgesOf(face).Length);
            Assert.Equal(4, adjacency.VerticesOf(face).Length);
        }
    }

    /// <summary>
    /// <b>A face's vertices are its loops' vertices with no repeats</b>, and the duplicate a naive
    /// concatenation leaves is what this asserts against.
    /// </summary>
    [Fact]
    public void AFacesVerticesAreDistinct()
    {
        Brep cylinder = BrepPrimitives.Cylinder(Plane.WorldXY, 1, 2);
        BrepAdjacency adjacency = BrepAdjacency.Of(cylinder);

        for (int face = 0; face < cylinder.FaceCount; face++)
        {
            int[] vertices = adjacency.VerticesOf(face);

            Assert.Equal(vertices.Distinct().Count(), vertices.Length);
        }
    }

    // -- Agreement with the navigator that scans -------------------------------------------------

    /// <summary>
    /// <b>The index and the scan give the same answer, on every edge of three fixtures.</b>
    /// <see cref="BrepEdgeView.AdjacentFaces"/> stays a scan on purpose — building an index for one
    /// call is worse than the scan it would replace — so there are deliberately two ways to ask this
    /// question, and two ways to ask a question means two answers unless something says otherwise.
    /// </summary>
    [Fact]
    public void TheIndexAndTheScanAgreeOnEveryEdge()
    {
        Brep[] fixtures =
        [
            BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4),
            BrepPrimitives.Cylinder(Plane.WorldXY, 1, 2),
            PlateWithASquareHole(),
        ];

        foreach (Brep brep in fixtures)
        {
            BrepAdjacency adjacency = BrepAdjacency.Of(brep);

            for (int edge = 0; edge < brep.EdgeCount; edge++)
            {
                int[] indexed = [.. adjacency
                    .TrimsOf(edge)
                    .ToArray()
                    .Select(trim => adjacency.FaceOf(adjacency.LoopOf(trim)))
                    .Where(face => face >= 0)
                    .Distinct()
                    .Order()];

                Assert.Equal(brep.Edge(edge).AdjacentFaces(), indexed);
            }
        }
    }

    // -- Refusals --------------------------------------------------------------------------------

    /// <summary>Every lookup refuses an index outside the model rather than reading past it.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(9999)]
    public void AnIndexOutsideTheModelIsRefused(int index)
    {
        BrepAdjacency adjacency = BrepAdjacency.Of(BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4));

        Assert.Throws<ArgumentOutOfRangeException>(() => adjacency.EdgesAt(index).Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => adjacency.FacesAt(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => adjacency.TrimsOf(index).Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => adjacency.LoopOf(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => adjacency.PartnerOf(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => adjacency.FaceOf(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => adjacency.EdgesOf(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => adjacency.VerticesOf(index));
    }

    /// <summary>A null model is refused by name.</summary>
    [Fact]
    public void ANullModelIsRefused() => Assert.Throws<ArgumentNullException>(() => BrepAdjacency.Of(null!));

    // -- The fixtures ----------------------------------------------------------------------------

    /// <summary>
    /// A square plate in the world XY plane with a square hole through it: one face, an outer loop
    /// of four edges and an inner loop of four more. Every edge is used exactly once, so nothing
    /// here has a partner — which is the other half of what this fixture is for.
    /// </summary>
    private static Brep PlateWithASquareHole()
    {
        BrepBuilder builder = new();

        int[] outer =
        [
            builder.AddVertex(new Point3d(0, 0, 0)),
            builder.AddVertex(new Point3d(4, 0, 0)),
            builder.AddVertex(new Point3d(4, 4, 0)),
            builder.AddVertex(new Point3d(0, 4, 0)),
        ];

        int[] inner =
        [
            builder.AddVertex(new Point3d(1, 1, 0)),
            builder.AddVertex(new Point3d(3, 1, 0)),
            builder.AddVertex(new Point3d(3, 3, 0)),
            builder.AddVertex(new Point3d(1, 3, 0)),
        ];

        (int Edge, bool IsReversed)[] outerEdges =
        [
            (builder.AddLineEdge(outer[0], outer[1]), false),
            (builder.AddLineEdge(outer[1], outer[2]), false),
            (builder.AddLineEdge(outer[2], outer[3]), false),
            (builder.AddLineEdge(outer[3], outer[0]), false),
        ];

        // The hole is wound the other way round, which is what makes it a hole.
        (int Edge, bool IsReversed)[] innerEdges =
        [
            (builder.AddLineEdge(inner[0], inner[3]), false),
            (builder.AddLineEdge(inner[3], inner[2]), false),
            (builder.AddLineEdge(inner[2], inner[1]), false),
            (builder.AddLineEdge(inner[1], inner[0]), false),
        ];

        int outerLoop = builder.AddLoop(outerEdges);
        int innerLoop = builder.AddLoop(innerEdges, BrepLoopKind.Inner);

        builder.AddFace(
            new PlaneSurface(Plane.WorldXY, new Interval(0, 4), new Interval(0, 4)),
            [outerLoop, innerLoop]);

        builder.CloseShell();

        return builder.Build();
    }

    /// <summary>
    /// A triangle whose third side is replaced by a circle closing on the vertex it starts from, so
    /// one edge has the same start and end vertex.
    /// </summary>
    private static Brep ClosedEdgeFixture()
    {
        BrepBuilder builder = new();

        int a = builder.AddVertex(new Point3d(0, 0, 0));
        int b = builder.AddVertex(new Point3d(2, 0, 0));

        int ab = builder.AddLineEdge(a, b);
        int ba = builder.AddLineEdge(b, a);
        int loopEdge = builder.AddEdge(a, a, new Circle(new Plane(new Point3d(0, -1, 0), Vector3d.ZAxis), 1));

        int loop = builder.AddLoop([(ab, false), (ba, false), (loopEdge, false)]);

        builder.AddFace(new PlaneSurface(Plane.WorldXY, new Interval(-2, 2), new Interval(-2, 2)), [loop]);
        builder.CloseShell();

        return builder.Build();
    }
}
