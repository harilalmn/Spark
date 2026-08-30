using System;

namespace Spark.Geometry;

/// <summary>
/// A navigator over one face of a <see cref="Brep"/> (`E2-T23`).
/// </summary>
/// <remarks>
/// <para>
/// <b>A <c>readonly ref struct</c>, and the choice is the whole point of the row.</b> The index
/// model is correct and unusable on its own — <c>brep.RawTrims[brep.RawLoops[face.FirstLoop].FirstTrim + i].Edge</c>
/// is not something anybody should type twice. A view walks the model in the terms it is about,
/// and costs nothing: a pair of registers, no allocation, and the compiler will not let it escape
/// to the heap, so it cannot outlive the BRep it points into or be captured into a closure that
/// does.
/// </para>
/// <para>
/// <b>The views are the ergonomics, not the model.</b> Anything that has to *store* a reference to
/// a face stores its index, which is what the model deals in and what survives serialization.
/// </para>
/// </remarks>
public readonly ref struct BrepFaceView
{
    private readonly Brep _brep;

    internal BrepFaceView(Brep brep, int index)
    {
        _brep = brep;
        Index = index;
    }

    /// <summary>Which face this is.</summary>
    public int Index { get; }

    /// <summary>The face record itself.</summary>
    public BrepFace Face => _brep.RawFaces[Index];

    /// <summary>The surface this face lies on.</summary>
    public Surface Surface => _brep.RawSurfaces[Face.Surface];

    /// <summary>Whether the face's outward normal is the opposite of its surface's.</summary>
    public bool IsReversed => Face.IsReversed;

    /// <summary>How many loops bound this face.</summary>
    public int LoopCount => Face.LoopCount;

    /// <summary>One of this face's loops.</summary>
    /// <param name="position">Its position within the face, from zero.</param>
    /// <returns>A navigator over that loop.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the face.</exception>
    public BrepLoopView Loop(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, Face.LoopCount);

        return new BrepLoopView(_brep, Face.FirstLoop + position);
    }

    /// <summary>The loop that bounds the face's material.</summary>
    /// <returns>A navigator over the outer loop.</returns>
    /// <exception cref="InvalidOperationException">The face has no outer loop.</exception>
    public BrepLoopView OuterLoop()
    {
        for (int position = 0; position < Face.LoopCount; position++)
        {
            if (_brep.RawLoops[Face.FirstLoop + position].Kind == BrepLoopKind.Outer)
            {
                return new BrepLoopView(_brep, Face.FirstLoop + position);
            }
        }

        throw new InvalidOperationException(
            $"Face {Index} has no outer loop, so it does not bound anything. Brep.Validate reports this.");
    }

    /// <summary>
    /// The outward normal at a parameter, with the face's own orientation applied.
    /// </summary>
    /// <param name="u">A parameter in the surface's first domain.</param>
    /// <param name="v">A parameter in its second.</param>
    /// <returns>A unit vector pointing out of the solid.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    /// <exception cref="InvalidOperationException">The surface is degenerate there.</exception>
    /// <remarks>
    /// <b>This, and not <see cref="Geometry.Surface.NormalAt"/>, is what anything outside the
    /// kernel should ask.</b> A face may be the reverse of its surface — that is how one surface
    /// serves the inner and outer walls of a shelled solid — and code that asked the surface
    /// directly would get the right answer on half a model.
    /// </remarks>
    public Vector3d NormalAt(double u, double v)
    {
        Vector3d normal = Surface.NormalAt(u, v);

        return IsReversed ? -normal : normal;
    }
}

/// <summary>A navigator over one loop of a <see cref="Brep"/>.</summary>
public readonly ref struct BrepLoopView
{
    private readonly Brep _brep;

    internal BrepLoopView(Brep brep, int index)
    {
        _brep = brep;
        Index = index;
    }

    /// <summary>Which loop this is.</summary>
    public int Index { get; }

    /// <summary>The loop record itself.</summary>
    public BrepLoop Loop => _brep.RawLoops[Index];

    /// <summary>Whether this loop bounds the face or cuts a hole in it.</summary>
    public BrepLoopKind Kind => Loop.Kind;

    /// <summary>How many trims this loop has.</summary>
    public int TrimCount => Loop.TrimCount;

    /// <summary>One of this loop's trims.</summary>
    /// <param name="position">Its position within the loop, from zero.</param>
    /// <returns>The trim.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the loop.</exception>
    public BrepTrim Trim(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, Loop.TrimCount);

        return _brep.RawTrims[Loop.FirstTrim + position];
    }

    /// <summary>The edge one of this loop's trims uses.</summary>
    /// <param name="position">The trim's position within the loop, from zero.</param>
    /// <returns>A navigator over that edge.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the loop.</exception>
    public BrepEdgeView Edge(int position) => new(_brep, Trim(position).Edge);

    /// <summary>
    /// The vertices this loop passes through, in the order it traverses them.
    /// </summary>
    /// <returns>One vertex index per trim, starting where the loop starts.</returns>
    /// <remarks>
    /// <b>The direction is taken from each trim, not from its edge.</b> An edge runs from its start
    /// vertex to its end; a loop may run the other way, and reading the edge's own direction would
    /// give a circuit that jumps.
    /// </remarks>
    public int[] VertexIndices()
    {
        int[] vertices = new int[Loop.TrimCount];

        for (int position = 0; position < Loop.TrimCount; position++)
        {
            BrepTrim trim = _brep.RawTrims[Loop.FirstTrim + position];
            BrepEdge edge = _brep.RawEdges[trim.Edge];

            vertices[position] = trim.IsReversed ? edge.End : edge.Start;
        }

        return vertices;
    }
}

/// <summary>A navigator over one edge of a <see cref="Brep"/>.</summary>
public readonly ref struct BrepEdgeView
{
    private readonly Brep _brep;

    internal BrepEdgeView(Brep brep, int index)
    {
        _brep = brep;
        Index = index;
    }

    /// <summary>Which edge this is.</summary>
    public int Index { get; }

    /// <summary>The edge record itself.</summary>
    public BrepEdge Edge => _brep.RawEdges[Index];

    /// <summary>The curve this edge lies on, running from its start vertex to its end.</summary>
    public Curve Curve => _brep.RawCurves[Edge.Curve];

    /// <summary>Where the edge begins.</summary>
    public Point3d StartPoint => _brep.RawPoints[_brep.RawVertices[Edge.Start].Point];

    /// <summary>Where it ends.</summary>
    public Point3d EndPoint => _brep.RawPoints[_brep.RawVertices[Edge.End].Point];

    /// <summary>
    /// The faces that meet along this edge.
    /// </summary>
    /// <returns>Between zero and two face indices, in ascending order.</returns>
    /// <remarks>
    /// <b>Found by scanning the trims, because the model stores the relationship one way.</b> A
    /// trim knows its edge; an edge does not know its trims, and a back-pointer array would be a
    /// second description of the same fact that has to be kept in step. For a model of any size a
    /// caller wanting this repeatedly should build the reverse index once and keep it, which is a
    /// decision the caller is in a position to make and this type is not.
    /// </remarks>
    public int[] AdjacentFaces()
    {
        Span<int> found = stackalloc int[2];
        int count = 0;

        for (int loop = 0; loop < _brep.RawLoops.Length && count < 2; loop++)
        {
            BrepLoop record = _brep.RawLoops[loop];

            for (int position = 0; position < record.TrimCount; position++)
            {
                if (_brep.RawTrims[record.FirstTrim + position].Edge != Index)
                {
                    continue;
                }

                int face = FaceOf(loop);

                if (face >= 0 && (count == 0 || found[0] != face))
                {
                    found[count++] = face;
                }

                break;
            }
        }

        return count switch
        {
            0 => [],
            1 => [found[0]],
            _ => found[0] <= found[1] ? [found[0], found[1]] : [found[1], found[0]],
        };
    }

    private int FaceOf(int loop)
    {
        for (int face = 0; face < _brep.RawFaces.Length; face++)
        {
            BrepFace record = _brep.RawFaces[face];

            if (loop >= record.FirstLoop && loop < record.FirstLoop + record.LoopCount)
            {
                return face;
            }
        }

        return -1;
    }
}

/// <summary>A navigator over one shell of a <see cref="Brep"/>.</summary>
public readonly ref struct BrepShellView
{
    private readonly Brep _brep;

    internal BrepShellView(Brep brep, int index)
    {
        _brep = brep;
        Index = index;
    }

    /// <summary>Which shell this is.</summary>
    public int Index { get; }

    /// <summary>The shell record itself.</summary>
    public BrepShell Shell => _brep.RawShells[Index];

    /// <summary>How many faces this shell has.</summary>
    public int FaceCount => Shell.FaceCount;

    /// <summary>One of this shell's faces.</summary>
    /// <param name="position">Its position within the shell, from zero.</param>
    /// <returns>A navigator over that face.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the shell.</exception>
    public BrepFaceView Face(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, Shell.FaceCount);

        return new BrepFaceView(_brep, Shell.FirstFace + position);
    }
}
