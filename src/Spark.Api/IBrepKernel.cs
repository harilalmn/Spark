using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Api;

/// <summary>
/// What a BRep kernel provider can do (`E2-T28`).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what greys a button out, rather than letting a user find out by pressing it.</b> The
/// node library reads it once and marks the operations that are not there as unavailable, with the
/// provider's name in the tooltip — which is a materially different experience from an exception
/// arriving after somebody has built the graph.
/// </para>
/// <para>
/// <b>Its character changed with [ADR-0020] and its shape did not.</b> It was designed so users
/// could see what a *staged* kernel had not implemented yet; with a real provider most of those
/// gaps are filled on day one, and what it greys out instead is the operations the provider is
/// genuinely poor at — mesh booleans above all, deferred to 1.x for that reason.
/// </para>
/// </remarks>
[Flags]
public enum BrepCapabilities
{
    /// <summary>Nothing. What a session with no provider reports.</summary>
    None = 0,

    /// <summary>Union, difference and intersection of solids.</summary>
    Boolean = 1 << 0,

    /// <summary>Sweeping a profile along a straight direction into a solid.</summary>
    Extrude = 1 << 1,

    /// <summary>Turning a profile about an axis into a solid.</summary>
    Revolve = 1 << 2,

    /// <summary>Building a solid through a series of profiles.</summary>
    Loft = 1 << 3,

    /// <summary>Sweeping a profile along a rail.</summary>
    Sweep = 1 << 4,

    /// <summary>Rounding edges.</summary>
    Fillet = 1 << 5,

    /// <summary>Bevelling edges.</summary>
    Chamfer = 1 << 6,

    /// <summary>Hollowing a solid, optionally opening faces.</summary>
    Shell = 1 << 7,

    /// <summary>Offsetting faces or a whole solid.</summary>
    Offset = 1 << 8,

    /// <summary>Cutting a solid with a surface or a plane.</summary>
    Split = 1 << 9,

    /// <summary>Joining loose faces into a shell, and repairing what does not quite meet.</summary>
    Sew = 1 << 10,

    /// <summary>Fixing tolerance and topology problems in an imported model.</summary>
    Heal = 1 << 11,

    /// <summary>Tessellating a trimmed BRep into a mesh.</summary>
    /// <remarks>
    /// <b>Behind the seam on purpose</b> ([ADR-0021]): tessellating a trimmed face is genuinely
    /// hard and a real kernel solves it. Mesh tessellation stays ours, and the consequence — that
    /// `NFR-8`'s watertightness property then tests somebody else's mesher — is recorded rather
    /// than discovered.
    /// </remarks>
    Tessellate = 1 << 12,

    /// <summary>Reading and writing STEP.</summary>
    Step = 1 << 13,

    /// <summary>Reading and writing IGES.</summary>
    Iges = 1 << 14,

    /// <summary>Boolean operations between meshes rather than solids.</summary>
    /// <remarks>
    /// Deferred to 1.x, and it is the operation <see cref="BrepCapabilities"/> most exists to grey
    /// out — the provider chosen in [ADR-0020] is poor at it and Dynamo has it, so its absence is a
    /// real gap that a user deserves to see rather than discover.
    /// </remarks>
    MeshBoolean = 1 << 15,
}

/// <summary>
/// What a kernel operation produced, or why it did not.
/// </summary>
/// <typeparam name="T">What the operation makes.</typeparam>
/// <remarks>
/// <para>
/// <b>A refusal is a value here, not an exception, and an exact kernel refuses often and
/// legitimately.</b> A fillet whose radius does not fit, a boolean of two solids that do not
/// intersect, a loft between profiles that cannot be matched — none of those is exceptional, all of
/// them are the user asking for something the geometry does not permit, and each has a sentence
/// worth saying. An exception would make the ordinary case cost a stack trace and would make the
/// node importer's job harder, not easier.
/// </para>
/// <para>
/// <b>It carries a <see cref="SparkDiagnostic"/> rather than a string</b>, so a failure reaches the
/// canvas the same way every other failure does: on the node, with a code, with a help topic.
/// </para>
/// </remarks>
public readonly struct KernelResult<T>
{
    private readonly T? _value;

    private KernelResult(T? value, SparkDiagnostic? diagnostic)
    {
        _value = value;
        Diagnostic = diagnostic;
    }

    /// <summary>Whether the operation produced something.</summary>
    public bool IsSuccess => Diagnostic is null;

    /// <summary>Why it did not, or null when it did.</summary>
    public SparkDiagnostic? Diagnostic { get; }

    /// <summary>What it produced.</summary>
    /// <exception cref="InvalidOperationException">The operation failed.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"This kernel operation did not succeed: {Diagnostic?.Message}. Check IsSuccess, or read Diagnostic.");

    /// <summary>A successful result.</summary>
    /// <param name="value">What the operation produced.</param>
    /// <returns>The result.</returns>
    public static KernelResult<T> Success(T value) => new(value, null);

    /// <summary>A refusal.</summary>
    /// <param name="diagnostic">Why.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="diagnostic"/> is null.</exception>
    public static KernelResult<T> Failure(SparkDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        return new KernelResult<T>(default, diagnostic);
    }

    /// <summary>Takes the value out, or the diagnostic.</summary>
    /// <param name="value">What the operation produced, when it did.</param>
    /// <returns>Whether it succeeded.</returns>
    public bool TryGetValue(out T value)
    {
        value = _value!;

        return IsSuccess;
    }
}

/// <summary>
/// The seam between Spark's geometry and an exact solid-modelling kernel (`E2-T28`).
/// </summary>
/// <remarks>
/// <para>
/// <b>Operations cross this seam; the data model never does.</b> That is
/// [ADR-0003](../../docs/adr/0003-ibrepkernel-seams-operations.md)'s decision and it survives
/// intact: abstracting the geometry *types* would cost the readonly structs their reason for
/// existing, break reflection-driven serialization, and make cross-assembly <c>Type</c> identity a
/// property of the provider rather than of Spark.
/// </para>
/// <para>
/// <b>In front of the seam, always ours:</b> every value type, analytic and NURBS evaluation, the
/// <see cref="Brep"/> and <see cref="Mesh"/> models and their validation, mesh tessellation,
/// bounding boxes, transforms, all serialization, the planar layer, and the ray caster.
/// <b>Behind it:</b> everything on this interface.
/// </para>
/// <para>
/// <b>Residency is canonical, not cached</b> ([ADR-0021]). An operation on a resident shape
/// returns another resident shape; nothing is read back until something asks a structural question.
/// A chain of ten operations performs zero imports and one materialisation, and that is a fidelity
/// rule rather than a performance one — a round trip through a tolerant kernel is not identity, so
/// converting at every step would let the user's geometry drift under an idle graph.
/// </para>
/// <para>
/// <b>Round-trip is not identity and no test may assert that it is.</b> What is asserted is
/// tolerance-bounded equivalence: volume, area, bounding box, topology counts, watertightness.
/// Anything stronger is a test of the provider's internals wearing Spark's name.
/// </para>
/// <para>
/// <b>For 1.0 there is exactly one provider and a second is not planned.</b> The seam is kept for
/// <see cref="KernelResult{T}"/>, for <see cref="Capabilities"/>, and as insurance — <b>and a
/// second provider must not be built to justify the abstraction</b>.
/// </para>
/// </remarks>
public interface IBrepKernel
{
    /// <summary>What to call this provider in a diagnostic or a tooltip.</summary>
    string Name { get; }

    /// <summary>What it can do.</summary>
    BrepCapabilities Capabilities { get; }

    /// <summary>Everything in either solid.</summary>
    /// <param name="first">One solid.</param>
    /// <param name="second">The other.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The union, or why not.</returns>
    KernelResult<Brep> Union(Brep first, Brep second, in Tolerance tolerance);

    /// <summary>The first solid with the second taken out of it.</summary>
    /// <param name="first">The solid to cut.</param>
    /// <param name="second">The cutter.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The difference, or why not.</returns>
    KernelResult<Brep> Difference(Brep first, Brep second, in Tolerance tolerance);

    /// <summary>Only what is in both solids.</summary>
    /// <param name="first">One solid.</param>
    /// <param name="second">The other.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The intersection, or why not.</returns>
    KernelResult<Brep> Intersection(Brep first, Brep second, in Tolerance tolerance);

    /// <summary>Sweeps a closed profile along a straight direction.</summary>
    /// <param name="profile">The closed curve to sweep.</param>
    /// <param name="direction">Which way and how far.</param>
    /// <param name="cap">Whether to close the ends into a solid.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The solid, or why not.</returns>
    KernelResult<Brep> Extrude(Curve profile, in Vector3d direction, bool cap, in Tolerance tolerance);

    /// <summary>Turns a profile about an axis.</summary>
    /// <param name="profile">The curve to revolve.</param>
    /// <param name="axisOrigin">A point on the axis.</param>
    /// <param name="axisDirection">The axis direction.</param>
    /// <param name="angle">How far to turn.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The solid, or why not.</returns>
    KernelResult<Brep> Revolve(
        Curve profile, in Point3d axisOrigin, in Vector3d axisDirection, Angle angle, in Tolerance tolerance);

    /// <summary>Builds a solid or a surface through a series of profiles.</summary>
    /// <param name="profiles">The profiles, in order.</param>
    /// <param name="closed">Whether to loop the last back to the first.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The shape, or why not.</returns>
    KernelResult<Brep> Loft(IReadOnlyList<Curve> profiles, bool closed, in Tolerance tolerance);

    /// <summary>Sweeps a profile along a rail.</summary>
    /// <param name="profile">The curve to sweep.</param>
    /// <param name="rail">The path to sweep it along.</param>
    /// <param name="cap">Whether to close the ends into a solid.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The swept shape, or why not.</returns>
    /// <remarks>
    /// The general case of <see cref="Extrude"/>, which is a sweep along a straight line. Kept
    /// separate because the straight case is what most graphs want and needs no second curve.
    /// </remarks>
    KernelResult<Brep> Sweep(Curve profile, Curve rail, bool cap, in Tolerance tolerance);

    /// <summary>Fills a closed boundary with a surface that meets it.</summary>
    /// <param name="boundary">The curves that bound the patch.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The patch, or why not.</returns>
    /// <remarks>
    /// <b>Unlike <see cref="Loft"/>, the boundary has no order.</b> A loft goes *through* profiles
    /// in sequence; a patch is given a circuit and finds a surface for it. They are different
    /// questions and a graph that asked one when it meant the other would get a plausible answer.
    /// </remarks>
    KernelResult<Brep> Patch(IReadOnlyList<Curve> boundary, in Tolerance tolerance);

    /// <summary>Rounds a solid's edges.</summary>
    /// <param name="solid">The solid.</param>
    /// <param name="edges">The indices of the edges to round.</param>
    /// <param name="radius">The fillet radius.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The filleted solid, or why not.</returns>
    KernelResult<Brep> Fillet(
        Brep solid, IReadOnlyList<int> edges, double radius, in Tolerance tolerance);

    /// <summary>Bevels a solid's edges.</summary>
    /// <param name="solid">The solid.</param>
    /// <param name="edges">The indices of the edges to bevel.</param>
    /// <param name="distance">How far back to cut.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The chamfered solid, or why not.</returns>
    KernelResult<Brep> Chamfer(
        Brep solid, IReadOnlyList<int> edges, double distance, in Tolerance tolerance);

    /// <summary>Hollows a solid, optionally opening some of its faces.</summary>
    /// <param name="solid">The solid.</param>
    /// <param name="facesToOpen">The indices of the faces to remove, or empty for a closed hollow.</param>
    /// <param name="thickness">The wall thickness. Negative hollows inwards.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The shelled solid, or why not.</returns>
    KernelResult<Brep> Shell(
        Brep solid, IReadOnlyList<int> facesToOpen, double thickness, in Tolerance tolerance);

    /// <summary>Cuts a shape into pieces, keeping every one of them.</summary>
    /// <param name="shape">The shape to cut.</param>
    /// <param name="tools">What to cut it with.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The pieces, or why not.</returns>
    /// <remarks>
    /// <b>A difference throws the far side away and this does not</b>, which is the whole
    /// distinction and the reason it is not a fourth boolean. A shape that the tools miss entirely
    /// comes back as one piece — itself — rather than as a refusal, because *nothing was cut* is a
    /// true answer to *cut this*.
    /// </remarks>
    KernelResult<IReadOnlyList<Brep>> Split(
        Brep shape, IReadOnlyList<Brep> tools, in Tolerance tolerance);

    /// <summary>Cuts a shape and keeps only the piece a point lies in.</summary>
    /// <param name="shape">The shape to cut.</param>
    /// <param name="tools">What to cut it with.</param>
    /// <param name="keep">A point inside the piece to keep.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The surviving piece, or why not.</returns>
    /// <remarks>
    /// <b>Which side to keep has to be said somehow, and a point is the least surprising way.</b>
    /// The alternatives are an index into an order nobody can predict, or a normal direction that
    /// means nothing for a tool that cuts more than once.
    /// </remarks>
    KernelResult<Brep> Trim(
        Brep shape, IReadOnlyList<Brep> tools, in Point3d keep, in Tolerance tolerance);

    /// <summary>Moves every face of a shape outwards or inwards.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="distance">How far. Negative moves inwards.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The offset shape, or why not.</returns>
    KernelResult<Brep> Offset(Brep shape, double distance, in Tolerance tolerance);

    /// <summary>Gives a sheet a thickness, turning it into a solid.</summary>
    /// <param name="sheet">The open shape to thicken.</param>
    /// <param name="thickness">How thick. Negative thickens the other way.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The solid, or why not.</returns>
    KernelResult<Brep> Thicken(Brep sheet, double thickness, in Tolerance tolerance);

    /// <summary>Tilts faces away from a pull direction, the way a moulded part is drafted.</summary>
    /// <param name="solid">The solid.</param>
    /// <param name="faces">The indices of the faces to tilt, or empty for all of them.</param>
    /// <param name="pullDirection">The direction the part comes out of the mould.</param>
    /// <param name="angle">How far to tilt. Positive tilts outwards.</param>
    /// <param name="neutral">The plane the tilt pivots about, where the shape keeps its size.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The drafted solid, or why not.</returns>
    /// <remarks>
    /// <b>The neutral plane has to be named, and that is not ceremony.</b> "Tilt this face by two
    /// degrees" does not say around *what*, and the answer changes the part: pivoting about the
    /// top and pivoting about the bottom give the same angle and different sizes.
    /// </remarks>
    KernelResult<Brep> Draft(
        Brep solid,
        IReadOnlyList<int> faces,
        in Vector3d pullDirection,
        Angle angle,
        in Plane neutral,
        in Tolerance tolerance);

    /// <summary>Joins loose faces into shells, closing what nearly meets.</summary>
    /// <param name="pieces">The shapes to join.</param>
    /// <param name="tolerance">How far apart edges may be and still be sewn.</param>
    /// <returns>The sewn shape, or why not.</returns>
    KernelResult<Brep> Sew(IReadOnlyList<Brep> pieces, in Tolerance tolerance);

    /// <summary>Repairs an imported shape's tolerances and topology.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>The repaired shape, or why not.</returns>
    KernelResult<Brep> Heal(Brep shape, in Tolerance tolerance);

    /// <summary>Reads a solid-modelling interchange file.</summary>
    /// <param name="path">The file. The format is taken from the extension.</param>
    /// <param name="tolerance">The tolerance to repair the file's geometry to.</param>
    /// <returns>Everything the file contained, as one shape, or why not.</returns>
    /// <remarks>
    /// <b>The seam carries this rather than <c>Spark.Geometry.Io</c>, because a STEP file is a
    /// BRep and nothing else can read one.</b> The mesh formats are Spark's own and stay there;
    /// this is the one interchange family whose reader is the kernel.
    /// </remarks>
    KernelResult<Brep> ReadFile(string path, in Tolerance tolerance);

    /// <summary>Writes a shape to a solid-modelling interchange file.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="path">The file. The format is taken from the extension.</param>
    /// <param name="tolerance">The modelling tolerance.</param>
    /// <returns>True, or why not.</returns>
    KernelResult<bool> WriteFile(Brep shape, string path, in Tolerance tolerance);

    /// <summary>Tessellates a shape, trimmed faces and all.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="tolerance">The largest distance the mesh may stray from the shape.</param>
    /// <returns>The mesh, or why not.</returns>
    KernelResult<Mesh> Tessellate(Brep shape, in Tolerance tolerance);
}
