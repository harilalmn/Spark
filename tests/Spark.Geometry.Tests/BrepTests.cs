using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// BRep topology, its builder and its navigators — `E2-T22`, `E2-T23`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The box and the cylinder are the fixtures, and they test different things.</b> A box is six
/// planar faces and twelve edges — the shape whose counts can be checked by hand, and whose every
/// edge is shared by exactly two *different* faces. A cylinder is three faces and two vertices, and
/// its seam edge is shared by one face *with itself*, once forwards and once backwards, which is
/// the case that makes a trim's direction flag earn its keep and the case a naive builder gets
/// wrong.
/// </para>
/// <para>
/// <b>Most of these are about the model being sound rather than about it existing.</b> Every index
/// in range, every loop closed, every edge used exactly twice in opposite directions — those are
/// the properties an exact kernel will assume, and a BRep that has them by accident today will not
/// have them tomorrow.
/// </para>
/// </remarks>
public sealed class BrepTests
{
    // -- The box ---------------------------------------------------------------------------------

    /// <summary>A box has the counts a box has, and they can be checked by hand.</summary>
    [Fact]
    public void ABoxHasSixFacesTwelveEdgesAndEightVertices()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);

        Assert.Equal(8, box.VertexCount);
        Assert.Equal(12, box.EdgeCount);
        Assert.Equal(6, box.FaceCount);
        Assert.Equal(6, box.LoopCount);
        Assert.Equal(24, box.TrimCount);
        Assert.Equal(1, box.ShellCount);
    }

    /// <summary>A box is structurally sound.</summary>
    [Fact]
    public void ABoxValidates() => Assert.Empty(BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4).Validate());

    /// <summary>
    /// <b>A box is a solid: every edge is used exactly twice, once each way.</b> This is the
    /// topological form of the question <see cref="MeshTopology.IsClosed"/> asks, and it is what
    /// says the shell has no holes and no face wound backwards.
    /// </summary>
    [Fact]
    public void ABoxIsASolid() => Assert.True(BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4).IsSolid);

    /// <summary>
    /// <b>Every one of a box's faces points outwards.</b> A primitive whose normals point in is the
    /// most confusing thing a modelling kernel can hand somebody, and it is invisible until
    /// something shades it or subtracts from it.
    /// </summary>
    [Fact]
    public void EveryBoxFacePointsOutwards()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        Point3d centre = new(1, 1.5, 2);

        for (int index = 0; index < box.FaceCount; index++)
        {
            BrepFaceView face = box.Face(index);

            double u = face.Surface.DomainU.Mid;
            double v = face.Surface.DomainV.Mid;

            Vector3d outwards = face.Surface.PointAt(u, v) - centre;

            Assert.True(
                face.NormalAt(u, v).Dot(outwards) > 0.0,
                $"face {index}'s normal points inwards");
        }
    }

    /// <summary>Every edge of a box joins exactly two different faces.</summary>
    [Fact]
    public void EveryBoxEdgeJoinsTwoFaces()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);

        for (int index = 0; index < box.EdgeCount; index++)
        {
            int[] faces = box.Edge(index).AdjacentFaces();

            Assert.Equal(2, faces.Length);
            Assert.NotEqual(faces[0], faces[1]);
        }
    }

    /// <summary>The bounding box is the box, exactly.</summary>
    [Fact]
    public void ABoxsBoundingBoxIsTheBox()
    {
        BoundingBox bounds = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4).BoundingBox;

        Assert.Equal(0.0, bounds.Min.X, 1e-9);
        Assert.Equal(2.0, bounds.Max.X, 1e-9);
        Assert.Equal(3.0, bounds.Max.Y, 1e-9);
        Assert.Equal(4.0, bounds.Max.Z, 1e-9);
    }

    // -- The cylinder ----------------------------------------------------------------------------

    /// <summary>
    /// <b>A cylinder is three faces and two vertices</b>, which is what a BRep is for: the same
    /// shape as a mesh is hundreds of triangles and an approximation.
    /// </summary>
    [Fact]
    public void ACylinderIsThreeFaces()
    {
        Brep cylinder = BrepPrimitives.Cylinder(Plane.WorldXY, 2, 5);

        Assert.Equal(2, cylinder.VertexCount);
        Assert.Equal(3, cylinder.EdgeCount);
        Assert.Equal(3, cylinder.FaceCount);
        Assert.Empty(cylinder.Validate());
    }

    /// <summary>
    /// <b>A cylinder's seam edge is used twice by the same face, once each way</b> — which is the
    /// case that makes a trim's direction flag necessary and that a builder using two seam edges
    /// gets subtly wrong.
    /// </summary>
    [Fact]
    public void ACylindersSeamIsUsedBothWaysByOneFace()
    {
        Brep cylinder = BrepPrimitives.Cylinder(Plane.WorldXY, 2, 5);

        BrepTrim[] trims = cylinder.Trims();

        for (int edge = 0; edge < cylinder.EdgeCount; edge++)
        {
            int forwards = trims.Count(trim => trim.Edge == edge && !trim.IsReversed);
            int backwards = trims.Count(trim => trim.Edge == edge && trim.IsReversed);

            Assert.Equal(1, forwards);
            Assert.Equal(1, backwards);
        }

        Assert.True(cylinder.IsSolid);
    }

    /// <summary>The wall of a cylinder is an exact cylindrical surface, not an approximation.</summary>
    [Fact]
    public void ACylindersWallIsAnExactSurface()
    {
        Brep cylinder = BrepPrimitives.Cylinder(Plane.WorldXY, 2, 5);

        Assert.Contains(cylinder.Surfaces(), surface => surface is CylindricalSurface);
        Assert.Equal(2, cylinder.Surfaces().Count(surface => surface is PlaneSurface));
    }

    // -- The builder -----------------------------------------------------------------------------

    /// <summary>
    /// <b>An open loop is refused, and the message names the position and the two vertices.</b>
    /// One edge listed in the wrong direction is the commonest mistake in a hand-built BRep, and it
    /// is otherwise invisible: every index is in range and the face simply describes a different
    /// shape.
    /// </summary>
    [Fact]
    public void AnOpenLoopIsRefusedWithThePosition()
    {
        BrepBuilder builder = new();

        int a = builder.AddVertex(new Point3d(0, 0, 0));
        int b = builder.AddVertex(new Point3d(1, 0, 0));
        int c = builder.AddVertex(new Point3d(1, 1, 0));

        int ab = builder.AddLineEdge(a, b);
        int bc = builder.AddLineEdge(b, c);
        int ca = builder.AddLineEdge(c, a);

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => builder.AddLoop([(ab, false), (bc, true), (ca, false)]));

        Assert.Contains("broken at position 1", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A loop that does not return to where it started is refused too.</summary>
    [Fact]
    public void AnUnclosedLoopIsRefused()
    {
        BrepBuilder builder = new();

        int a = builder.AddVertex(new Point3d(0, 0, 0));
        int b = builder.AddVertex(new Point3d(1, 0, 0));
        int c = builder.AddVertex(new Point3d(1, 1, 0));

        int ab = builder.AddLineEdge(a, b);
        int bc = builder.AddLineEdge(b, c);

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => builder.AddLoop([(ab, false), (bc, false)]));

        Assert.Contains("does not close", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refused loop leaves no trims behind, so the builder can be used again — which matters
    /// because a caller catching the exception is usually about to try a different winding.
    /// </summary>
    [Fact]
    public void ARefusedLoopLeavesNothingBehind()
    {
        BrepBuilder builder = new();

        int a = builder.AddVertex(new Point3d(0, 0, 0));
        int b = builder.AddVertex(new Point3d(1, 0, 0));
        int c = builder.AddVertex(new Point3d(1, 1, 0));

        int ab = builder.AddLineEdge(a, b);
        int bc = builder.AddLineEdge(b, c);
        int ca = builder.AddLineEdge(c, a);

        Assert.Throws<ArgumentException>(() => builder.AddLoop([(ab, false), (bc, true), (ca, false)]));

        int loop = builder.AddLoop([(ab, false), (bc, false), (ca, false)]);

        builder.AddFace(new PlaneSurface(Plane.WorldXY, Interval.Unit, Interval.Unit), [loop]);
        builder.CloseShell();

        Brep brep = builder.Build();

        Assert.Equal(3, brep.TrimCount);
        Assert.Empty(brep.Validate());
    }

    /// <summary>An index the builder never handed out is refused by name.</summary>
    [Fact]
    public void AnUnknownIndexIsRefused()
    {
        BrepBuilder builder = new();

        builder.AddVertex(Point3d.Origin);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddLineEdge(0, 7));
    }

    /// <summary>A face outside every shell is refused rather than swept into one.</summary>
    [Fact]
    public void AFaceWithNoShellIsRefused()
    {
        BrepBuilder builder = new();

        int a = builder.AddVertex(new Point3d(0, 0, 0));
        int b = builder.AddVertex(new Point3d(1, 0, 0));
        int c = builder.AddVertex(new Point3d(1, 1, 0));

        int loop = builder.AddLoop(
        [
            (builder.AddLineEdge(a, b), false),
            (builder.AddLineEdge(b, c), false),
            (builder.AddLineEdge(c, a), false),
        ]);

        builder.AddFace(new PlaneSurface(Plane.WorldXY, Interval.Unit, Interval.Unit), [loop]);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("belong to no shell", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A face's loops have to be contiguous, and the builder says so rather than reordering.</summary>
    [Fact]
    public void NonContiguousLoopsAreRefused()
    {
        BrepBuilder builder = new();

        int a = builder.AddVertex(new Point3d(0, 0, 0));
        int b = builder.AddVertex(new Point3d(1, 0, 0));
        int c = builder.AddVertex(new Point3d(1, 1, 0));

        int ab = builder.AddLineEdge(a, b);
        int bc = builder.AddLineEdge(b, c);
        int ca = builder.AddLineEdge(c, a);

        int first = builder.AddLoop([(ab, false), (bc, false), (ca, false)]);
        builder.AddLoop([(ab, false), (bc, false), (ca, false)], BrepLoopKind.Inner);
        int third = builder.AddLoop([(ab, false), (bc, false), (ca, false)], BrepLoopKind.Inner);

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => builder.AddFace(
                new PlaneSurface(Plane.WorldXY, Interval.Unit, Interval.Unit), [first, third]));

        Assert.Contains("contiguous", failure.Message, StringComparison.Ordinal);
    }

    // -- Validation ------------------------------------------------------------------------------

    /// <summary>
    /// <b><see cref="Brep"/>'s constructor takes any nine arrays, and validation is why.</b>
    /// Reading a malformed BRep in order to find out what is wrong with it is what a repair tool
    /// does, so the constructor cannot be the gate.
    /// </summary>
    [Fact]
    public void AMalformedBrepIsDescribedRatherThanRefused()
    {
        Brep broken = new(
            [new Point3d(0, 0, 0)],
            [new Line(Point3d.Origin, new Point3d(1, 0, 0))],
            [new PlaneSurface(Plane.WorldXY, Interval.Unit, Interval.Unit)],
            [new BrepVertex(0)],
            [new BrepEdge(0, 9, 0)],
            [new BrepTrim(0, false)],
            [new BrepLoop(0, 1, BrepLoopKind.Outer)],
            [new BrepFace(0, 0, 1, false)],
            [new BrepShell(0, 1)]);

        IReadOnlyList<string> problems = broken.Validate();

        Assert.NotEmpty(problems);
        Assert.Contains(problems, problem => problem.Contains("Edge 0", StringComparison.Ordinal));
    }

    /// <summary>Validation reports every problem in one pass rather than the first one.</summary>
    [Fact]
    public void ValidationReportsEveryProblem()
    {
        Brep broken = new(
            [],
            [],
            [],
            [new BrepVertex(5)],
            [new BrepEdge(9, 9, 9)],
            [new BrepTrim(9, false)],
            [new BrepLoop(0, 1, BrepLoopKind.Outer)],
            [new BrepFace(9, 0, 1, false)],
            [new BrepShell(0, 1)]);

        Assert.True(broken.Validate().Count >= 4, "each independent problem should be reported");
    }

    /// <summary>An edge no loop uses is a problem, because it belongs to no face.</summary>
    [Fact]
    public void AnOrphanEdgeIsAProblem()
    {
        BrepBuilder builder = new();

        int a = builder.AddVertex(new Point3d(0, 0, 0));
        int b = builder.AddVertex(new Point3d(1, 0, 0));
        int c = builder.AddVertex(new Point3d(1, 1, 0));

        int ab = builder.AddLineEdge(a, b);
        int bc = builder.AddLineEdge(b, c);
        int ca = builder.AddLineEdge(c, a);

        builder.AddLineEdge(a, c);

        int loop = builder.AddLoop([(ab, false), (bc, false), (ca, false)]);
        builder.AddFace(new PlaneSurface(Plane.WorldXY, Interval.Unit, Interval.Unit), [loop]);
        builder.CloseShell();

        Assert.Contains(
            builder.Build().Validate(),
            problem => problem.Contains("used by no loop", StringComparison.Ordinal));
    }

    // -- Navigators ------------------------------------------------------------------------------

    /// <summary>A face view walks to its loop, its trims and their edges.</summary>
    [Fact]
    public void AFaceViewWalksToItsEdges()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepFaceView face = box.Face(0);

        Assert.Equal(1, face.LoopCount);

        BrepLoopView loop = face.OuterLoop();

        Assert.Equal(BrepLoopKind.Outer, loop.Kind);
        Assert.Equal(4, loop.TrimCount);

        for (int position = 0; position < loop.TrimCount; position++)
        {
            Assert.NotNull(loop.Edge(position).Curve);
        }
    }

    /// <summary>
    /// <b>A loop's vertices come from its trims' directions, not from its edges'.</b> Reading the
    /// edge's own direction gives a circuit that jumps, which is what makes this worth a method
    /// rather than three lines at every call site.
    /// </summary>
    [Fact]
    public void ALoopsVerticesRunInOrder()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepLoopView loop = box.Face(2).OuterLoop();

        int[] vertices = loop.VertexIndices();

        Assert.Equal(4, vertices.Length);
        Assert.Equal(4, vertices.Distinct().Count());

        // Consecutive vertices are joined by an edge of the loop, which is what "in order" means.
        for (int position = 0; position < vertices.Length; position++)
        {
            BrepTrim trim = loop.Trim(position);
            BrepEdge edge = box.Edges()[trim.Edge];
            int end = trim.IsReversed ? edge.Start : edge.End;

            Assert.Equal(vertices[(position + 1) % vertices.Length], end);
        }
    }

    /// <summary>An edge view knows where it starts and ends in space.</summary>
    [Fact]
    public void AnEdgeViewKnowsItsEnds()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepEdgeView edge = box.Edge(0);

        Assert.Equal(edge.StartPoint, edge.Curve.StartPoint);
        Assert.Equal(edge.EndPoint, edge.Curve.EndPoint);
    }

    /// <summary>A shell view walks to its faces.</summary>
    [Fact]
    public void AShellViewWalksToItsFaces()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepShellView shell = box.Shell(0);

        Assert.Equal(6, shell.FaceCount);

        for (int position = 0; position < shell.FaceCount; position++)
        {
            Assert.Equal(position, shell.Face(position).Index);
        }
    }

    /// <summary>
    /// A face without an outer loop refuses to hand one back, rather than returning the first loop
    /// it finds and letting a caller build on a face that bounds nothing.
    /// </summary>
    [Fact]
    public void AFaceWithNoOuterLoopSaysSo()
    {
        Brep broken = new(
            [new Point3d(0, 0, 0)],
            [new Line(Point3d.Origin, new Point3d(1, 0, 0))],
            [new PlaneSurface(Plane.WorldXY, Interval.Unit, Interval.Unit)],
            [new BrepVertex(0)],
            [new BrepEdge(0, 0, 0)],
            [new BrepTrim(0, false)],
            [new BrepLoop(0, 1, BrepLoopKind.Inner)],
            [new BrepFace(0, 0, 1, false)],
            [new BrepShell(0, 1)]);

        Assert.Throws<InvalidOperationException>(() => broken.Face(0).OuterLoop());
    }

    /// <summary>An untrimmed model says so, which is what decides whether it can be tessellated.</summary>
    [Fact]
    public void APrimitiveIsUntrimmed()
    {
        Assert.True(BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1).IsUntrimmed);
        Assert.True(BrepPrimitives.Cylinder(Plane.WorldXY, 1, 1).IsUntrimmed);
    }
}
