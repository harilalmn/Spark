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
        Point3d center = new(1, 1.5, 2);

        for (int index = 0; index < box.FaceCount; index++)
        {
            BrepFaceView face = box.Face(index);

            double u = face.Surface.DomainU.Mid;
            double v = face.Surface.DomainV.Mid;

            Vector3d outwards = face.Surface.PointAt(u, v) - center;

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

    /// <summary>
    /// <b>A loop's positions wrap, and that is arithmetic rather than a stored link</b> (`E2-T65`).
    /// Dynamo's <c>CoEdge.Next</c> and <c>CoEdge.Previous</c> are pointers between objects; here the
    /// trims of a loop are contiguous from <see cref="BrepLoop.FirstTrim"/>, so the same question is
    /// answered without storing anything and without building an index.
    /// </summary>
    [Fact]
    public void ALoopsPositionsWrapAtBothEnds()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        BrepLoopView loop = box.Face(0).OuterLoop();

        Assert.Equal(4, loop.TrimCount);

        Assert.Equal(1, loop.NextPosition(0));
        Assert.Equal(0, loop.NextPosition(3));
        Assert.Equal(2, loop.PreviousPosition(3));
        Assert.Equal(3, loop.PreviousPosition(0));

        for (int position = 0; position < loop.TrimCount; position++)
        {
            Assert.Equal(position, loop.PreviousPosition(loop.NextPosition(position)));
        }
    }

    /// <summary>
    /// <b>A trim's own ends follow its direction, not its edge's</b>, so one trim's end is the next
    /// trim's start all the way round — which is what makes a loop a circuit rather than a bag of
    /// edges. The seam of a cylinder is the case that breaks a version reading the edge instead.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EachTrimEndsWhereTheNextOneStarts(int face)
    {
        Brep cylinder = BrepPrimitives.Cylinder(Plane.WorldXY, 1, 2);
        BrepLoopView loop = cylinder.Face(face).OuterLoop();

        for (int position = 0; position < loop.TrimCount; position++)
        {
            Assert.Equal(loop.StartVertex(loop.NextPosition(position)), loop.EndVertex(position));
        }

        int[] starts = new int[loop.TrimCount];

        for (int position = 0; position < loop.TrimCount; position++)
        {
            starts[position] = loop.StartVertex(position);
        }

        Assert.Equal(loop.VertexIndices(), starts);
    }

    /// <summary>A position outside the loop is refused by each of the four.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void APositionOutsideTheLoopIsRefused(int position)
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => box.Face(0).OuterLoop().NextPosition(position));
        Assert.Throws<ArgumentOutOfRangeException>(() => box.Face(0).OuterLoop().PreviousPosition(position));
        Assert.Throws<ArgumentOutOfRangeException>(() => box.Face(0).OuterLoop().StartVertex(position));
        Assert.Throws<ArgumentOutOfRangeException>(() => box.Face(0).OuterLoop().EndVertex(position));
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

    /// <summary>A solid moves, and every vertex moves with it — `E2-T70`.</summary>
    /// <remarks>
    /// The plain case, and it is the one that had no member at all until 2026-09-13: a solid could
    /// be unioned, filleted, hollowed and exported, and not moved.
    /// </remarks>
    [Fact]
    public void ASolidCanBeMoved()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        Vector3d offset = new(10, -20, 30);
        Brep moved = box.TransformedBy(Transform.Translation(offset));

        Assert.Equal(box.VertexCount, moved.VertexCount);
        Assert.Equal(box.FaceCount, moved.FaceCount);

        for (int i = 0; i < box.VertexCount; i++)
        {
            Assert.True(
                moved.VertexPoint(i).EqualsWithin(box.VertexPoint(i) + offset),
                $"vertex {i} went to {moved.VertexPoint(i)} rather than {box.VertexPoint(i) + offset}");
        }

        // The surfaces move too, not only the vertices - a box whose points moved and whose planes
        // did not is a model whose faces are nowhere near its corners, and every index still checks
        // out, so nothing but geometry would catch it.
        Assert.True(
            moved.BoundingBox.Min.EqualsWithin(box.BoundingBox.Min + offset),
            $"the box is at {moved.BoundingBox.Min} rather than {box.BoundingBox.Min + offset}");
    }

    /// <summary>Moving a solid changes no index, because an affine map changes no connectivity.</summary>
    [Fact]
    public void MovingASolidLeavesItsTopologyAlone()
    {
        Brep cylinder = BrepPrimitives.Cylinder(Plane.WorldXY, 2, 5);
        Brep moved = cylinder.TransformedBy(
            Transform.Rotation(Vector3d.XAxis, Angle.FromDegrees(37), new Point3d(1, 2, 3)));

        Assert.Equal(cylinder.Edges(), moved.Edges());
        Assert.Equal(cylinder.Trims(), moved.Trims());
        Assert.Equal(cylinder.Loops(), moved.Loops());
        Assert.Equal(cylinder.Shells(), moved.Shells());
        Assert.Empty(moved.Validate());
        Assert.True(moved.IsSolid);
    }

    /// <summary>
    /// A mirrored solid is not inside out, because every face flips with the handedness — `E2-T70`.
    /// </summary>
    /// <remarks>
    /// <b>This is the test that goes red when the `Determinant &lt; 0` branch is removed</b>, and it
    /// is the reason the branch exists. A mirror reverses handedness, so each surface's own normal —
    /// the cross product of its two parameter directions — ends up pointing the opposite way
    /// relative to the moved shape. Every index still checks out, `Validate` is still empty and
    /// `IsSolid` is still true; the only symptom is that the volume comes out negative, which is to
    /// say the solid is inside out.
    /// </remarks>
    [Fact]
    public void AMirroredSolidIsNotInsideOut()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        Brep mirrored = box.TransformedBy(Transform.Mirror(Plane.WorldYZ));

        Assert.Empty(mirrored.Validate());
        Assert.True(mirrored.IsSolid);

        for (int i = 0; i < box.FaceCount; i++)
        {
            Assert.NotEqual(box.Faces()[i].IsReversed, mirrored.Faces()[i].IsReversed);
        }

        // And the flags are not merely different - they are right. Every face's outward normal must
        // point away from the centre, which is the property "inside out" actually means, and it is
        // checked here rather than through a tessellated volume because that needs a kernel.
        Point3d centre = mirrored.BoundingBox.Center;

        for (int i = 0; i < mirrored.FaceCount; i++)
        {
            BrepFace face = mirrored.Faces()[i];
            Surface surface = mirrored.Surfaces()[face.Surface];
            double u = surface.DomainU.Mid;
            double v = surface.DomainV.Mid;
            Vector3d outward = surface.NormalAt(u, v) * (face.IsReversed ? -1.0 : 1.0);

            Assert.True(
                outward.Dot(surface.PointAt(u, v) - centre) > 0.0,
                $"face {i} of the mirrored box faces inwards");
        }
    }

    /// <summary>A rotation does not flip anything, because it does not reverse handedness.</summary>
    [Fact]
    public void ARotationLeavesEveryFacesOrientationAlone()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        Brep turned = box.TransformedBy(Transform.Rotation(Vector3d.ZAxis, Angle.FromDegrees(90)));

        Assert.Equal(box.Faces(), turned.Faces());
    }

    /// <summary>
    /// A non-uniform scale on a solid with an analytic face is refused, in the words the surface
    /// already uses.
    /// </summary>
    /// <remarks>
    /// A cylinder scaled 2x in x and 1x in y is an elliptic cylinder, and there is no type for that
    /// — so the analytic geometry throws and this inherits the refusal rather than catching it and
    /// returning something that is the wrong shape. **The refusal arrives from the edge circle
    /// rather than from the cylindrical face**, because curves are moved before surfaces, and that
    /// is worth knowing rather than worth changing: both refuse, and the first one to notice is the
    /// one that gets to explain.
    /// </remarks>
    [Fact]
    public void ANonUniformScaleOnAnAnalyticSolidIsRefused()
    {
        Brep cylinder = BrepPrimitives.Cylinder(Plane.WorldXY, 1, 2);

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => cylinder.TransformedBy(Transform.Scale(2, 1, 1)));

        Assert.Contains("scales", refused.Message, StringComparison.Ordinal);

        // And a UNIFORM scale is fine, which is what makes the refusal about the shape rather than
        // about scaling at all.
        Assert.Empty(cylinder.TransformedBy(Transform.Scale(3)).Validate());
    }
}
