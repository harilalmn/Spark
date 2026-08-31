namespace Spark.Api;

/// <summary>
/// What a node <i>does</i> to the type its library group is about: makes one, changes one, or
/// reports something about one. This is the second axis the library panel files nodes on, under
/// the category (<c>E8-T29</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Dynamo's three, and users arrive already knowing them.</b> A category with thirty nodes in
/// it is still a wall of names; split into <i>Create</i>, <i>Action</i> and <i>Query</i> it becomes
/// three short lists, and a user who wants to <i>make</i> a circle never reads the twenty nodes
/// that measure one.
/// </para>
/// <para>
/// <b>Why it is declared rather than only inferred.</b> Dynamo can read this off the CLR member —
/// a constructor is Create, a property getter is Query, an instance method is Action — because a
/// zero-touch node <i>is</i> the member. Spark's node library is a facade of static methods over
/// value types (<c>Point.Translate(Point3d, …)</c>), so that structure is not present to be read:
/// every member is a static method on a static class. The importer infers what it honestly can
/// from the shape of the ports and the naming convention ADR-0004 already relies on, and
/// <see cref="SparkNodeAttribute.Kind"/> is how an author says otherwise.
/// </para>
/// </remarks>
public enum NodeMemberKind
{
    /// <summary>
    /// <b>Not a kind.</b> A sentinel meaning "I have not said, so infer it" — the value an
    /// attribute carries when its author left <see cref="SparkNodeAttribute.Kind"/> alone. It is
    /// resolved by the importer and never reaches a node definition, on the same pattern
    /// <see cref="LacingMode.Auto"/> follows and for the same reason: the absence of an opinion has
    /// to be representable, and it is not the same thing as any of the opinions.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Makes a new thing out of values that are not one — <c>Point.ByCoordinates</c>,
    /// <c>Circle.ByCentreRadius</c>, <c>Vector.ZAxis</c>. The green <c>+</c>.
    /// </summary>
    Create = 1,

    /// <summary>
    /// Takes one of the thing and produces another — <c>Point.Translate</c>, <c>Solid.Union</c>,
    /// <c>Math.Divide</c>. The amber bolt, and the bucket everything unclassifiable falls into,
    /// because "it does something with it" is the honest description of a node nothing else fits.
    /// </summary>
    Action = 2,

    /// <summary>
    /// Reports something <i>about</i> one of the thing without producing another —
    /// <c>Curve.Length</c>, <c>Solid.IsClosed</c>, <c>List.Count</c>. The blue <c>?</c>.
    /// </summary>
    Query = 3,
}
