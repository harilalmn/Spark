using System;
using System.Collections.Generic;
using System.Linq;

namespace Spark.Geometry.Tests;

/// <summary>
/// `E2-T61` — putting several models into one, as separate shells.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written for an export</b> (`E9-T15`): a viewport showing three solids has to become one
/// STEP file, and the interchange writer takes one model. A boolean union would answer a question
/// nobody asked — what is the combined solid — and can refuse on geometry that is merely
/// near-tangent. A join asserts nothing about how the parts relate, and therefore cannot fail.
/// </para>
/// <para>
/// <b>What can go wrong here is index arithmetic and nothing else</b>, so that is what is
/// asserted: every offset is the count <i>before</i> a part's own elements went in, and getting
/// one of the eight wrong produces a model that still has the right number of everything and walks
/// to the wrong place. <see cref="Brep.Validate"/> catches most of that, and the geometry checks
/// below catch the rest — a face of the second box that resolves to the first box's surface is a
/// valid model and a wrong one.
/// </para>
/// </remarks>
public sealed class BrepJoinTests
{
    private static Brep First => BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);

    private static Brep Second => BrepPrimitives.Box(
        Plane.FromOriginXAxisYAxis(new Point3d(100, 0, 0), Vector3d.XAxis, Vector3d.YAxis), 5, 6, 7);

    /// <summary>A joined model is a valid model, which is the claim everything else rests on.</summary>
    [Fact]
    public void AJoinedModelValidates()
    {
        Brep joined = Brep.Join([First, Second]);

        Assert.Empty(joined.Validate());
    }

    /// <summary>Every count is the sum, because nothing is welded and nothing is dropped.</summary>
    [Fact]
    public void EveryCountIsTheSumOfTheParts()
    {
        Brep first = First;
        Brep second = Second;
        Brep joined = Brep.Join([first, second]);

        Assert.Equal(first.ShellCount + second.ShellCount, joined.ShellCount);
        Assert.Equal(first.FaceCount + second.FaceCount, joined.FaceCount);
        Assert.Equal(first.EdgeCount + second.EdgeCount, joined.EdgeCount);
        Assert.Equal(first.Vertices().Length + second.Vertices().Length, joined.Vertices().Length);
        Assert.Equal(first.Trims().Length + second.Trims().Length, joined.Trims().Length);
        Assert.Equal(first.Loops().Length + second.Loops().Length, joined.Loops().Length);
    }

    /// <summary>
    /// <b>The second part's geometry is still where it was.</b> A vertex whose point index was not
    /// shifted lands on the first part's point — a valid model, a wrong one, and one no count
    /// check can see.
    /// </summary>
    [Fact]
    public void EveryPartKeepsItsOwnGeometry()
    {
        Brep joined = Brep.Join([First, Second]);

        List<Point3d> points = [];

        for (int index = 0; index < joined.Vertices().Length; index++)
        {
            points.Add(joined.VertexPoint(index));
        }

        // The second box starts a hundred units away, so its corners are the only ones out there.
        Assert.Equal(8, points.Count(point => point.X >= 100));
        Assert.Equal(8, points.Count(point => point.X < 100));
    }

    /// <summary>
    /// <b>Every face still resolves to its own surface.</b> The two boxes are different sizes, so
    /// a face that walked to the wrong surface would put a 2×3 rectangle where a 5×6 one belongs.
    /// </summary>
    [Fact]
    public void EveryFaceStillResolvesToItsOwnSurface()
    {
        Brep first = First;
        Brep joined = Brep.Join([first, Second]);

        for (int index = 0; index < first.FaceCount; index++)
        {
            Assert.Equal(first.Faces()[index].Surface, joined.Faces()[index].Surface);
        }

        // And the second part's faces name surfaces that did not exist before it was added.
        for (int index = first.FaceCount; index < joined.FaceCount; index++)
        {
            Assert.True(
                joined.Faces()[index].Surface >= first.Surfaces().Length,
                $"face {index} of the second part resolves into the first part's surfaces");
        }
    }

    /// <summary>
    /// <b>Every trim still walks to a vertex that exists.</b> The full chain — face to loop to
    /// trim to edge to vertex to point — is where seven of the eight offsets are used.
    /// </summary>
    [Fact]
    public void EveryChainWalksToTheEnd()
    {
        Brep joined = Brep.Join([First, Second, First]);

        foreach (BrepShell shell in joined.Shells())
        {
            for (int face = shell.FirstFace; face < shell.FirstFace + shell.FaceCount; face++)
            {
                BrepFace it = joined.Faces()[face];

                Assert.InRange(it.Surface, 0, joined.Surfaces().Length - 1);

                for (int loop = it.FirstLoop; loop < it.FirstLoop + it.LoopCount; loop++)
                {
                    BrepLoop ring = joined.Loops()[loop];

                    for (int trim = ring.FirstTrim; trim < ring.FirstTrim + ring.TrimCount; trim++)
                    {
                        BrepEdge edge = joined.Edges()[joined.Trims()[trim].Edge];

                        Assert.InRange(edge.Curve, 0, joined.Curves().Length - 1);
                        Assert.InRange(edge.Start, 0, joined.Vertices().Length - 1);
                        Assert.InRange(edge.End, 0, joined.Vertices().Length - 1);
                    }
                }
            }
        }
    }

    /// <summary>
    /// One part is returned as it came, rather than copied — which is what keeps a single
    /// kernel-held solid resident, and therefore what keeps an export of one solid a solid.
    /// </summary>
    [Fact]
    public void OnePartIsReturnedUnchanged()
    {
        Brep only = First;

        Assert.Same(only, Brep.Join([only]));
    }

    /// <summary>No parts is an empty model rather than a refusal: exporting nothing is not a fault.</summary>
    [Fact]
    public void NoPartsGivesAnEmptyModel()
    {
        Brep joined = Brep.Join([]);

        Assert.Equal(0, joined.ShellCount);
        Assert.Equal(0, joined.FaceCount);
        Assert.Empty(joined.Validate());
    }

    /// <summary>A null list, or a null part, is a caller's mistake and is named as one.</summary>
    [Fact]
    public void NullIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => Brep.Join(null!));
        Assert.Throws<ArgumentNullException>(() => Brep.Join([First, null!]));
    }
}
