using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// The node library is callable from a code block — `E6-T30`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client: every node in the library available in a block.</b> Most already
/// were, and saying so precisely matters — the geometry-shaped nodes are thin façades over
/// <c>Spark.Geometry</c>, which a block has always imported, so
/// <c>Circle.FromCenterRadius(pt, 5)</c> has worked all along. What was genuinely out of reach is
/// the façades with no geometry equivalent, and <c>Solid</c> alone is 38 of them.
/// </para>
/// <para>
/// <b>The danger the whole row has to avoid is `CS0104`.</b> Ten of the library's twenty-five
/// type names collide with <c>Spark.Geometry</c> and one with <c>System.Math</c>, so importing the
/// namespace naively breaks every block anybody has written — which is why it was excluded in the
/// first place. <see cref="TheGeometryTypesStillWin"/> is the test that holds that line, and
/// <see cref="EveryCollidingNameIsPinned"/> is the one that keeps the pin list from going stale
/// — which it did, silently, the day `E2-T73` added a tenth façade.
/// </para>
/// </remarks>
public sealed class CodeBlockLibraryReachTests
{
    private static ScriptNodeFactory Factory()
    {
        _ = typeof(Point3d).Assembly.Location;
        _ = typeof(Spark.Nodes.Core.Point).Assembly.Location;

        return new ScriptNodeFactory();
    }

    /// <summary>The catalogue the shell builds, whose prelude carries the pins.</summary>
    /// <returns>A catalogue over the assemblies this process has loaded.</returns>
    private static ReferenceCatalog Catalogue()
    {
        // A referenced assembly does not load until something touches a type in it, and the
        // catalogue sweeps what is loaded - so both are touched first, as Factory does.
        _ = typeof(Point3d).Assembly.Location;
        _ = typeof(Spark.Nodes.Core.Point).Assembly.Location;

        return new ReferenceCatalog();
    }

    private static object? Run(string script) =>
        Factory().Create(script).Invoke([], CancellationToken.None).LastOrDefault();

    private static void Compiles(string script) =>
        Assert.DoesNotContain(
            Factory().Diagnose(script),
            diagnostic => diagnostic.IsError);

    /// <summary>
    /// <b>The colliding names still mean what they have always meant.</b> An explicit alias beats a
    /// namespace import, which is the whole mechanism: every block that compiled before this row
    /// compiles after it, and means the same thing.
    /// </summary>
    [Theory]
    [InlineData("Circle.FromCenterRadius(Point3d.Origin, 5.0);")]
    [InlineData("Line.FromStartPointEndPoint(Point3d.Origin, new Point3d(1, 0, 0));")]
    [InlineData("Plane.WorldXY;")]
    [InlineData("Arc.FromThreePoints(Point3d.Origin, new Point3d(1, 1, 0), new Point3d(2, 0, 0));")]
    [InlineData("Helix.FromAxis(Point3d.Origin, Vector3d.ZAxis, new Point3d(1, 0, 0), 1.0, Angle.FullTurn);")]
    public void TheGeometryTypesStillWin(string script) => Compiles(script);

    /// <summary>
    /// <b>Every type name the node library shares with <c>Spark.Geometry</c> is pinned in the
    /// prelude, and this test derives the set rather than restating it.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>`E2-T73` is why this exists.</b> The pin list in <c>ReferenceCatalog</c> is written by
    /// hand, and adding a <c>Helix</c> façade beside <c>Spark.Geometry.Helix</c> made <c>Helix</c>
    /// ambiguous in every code block with nothing in this suite noticing — the help-sample
    /// compiler caught it, and only because a help topic happened to name the type. A façade
    /// nobody documented would have shipped broken.
    /// </para>
    /// <para>
    /// <b>It is a reflection diff for the reason <c>ConstructorParityTests</c> is one</b>: the
    /// parity it guards rots silently, and a list of examples can only ever say that the names
    /// somebody thought of are pinned. Here, the eleventh façade is a red build naming itself.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCollidingNameIsPinned()
    {
        HashSet<string> geometry =
        [
            .. typeof(Point3d).Assembly.GetExportedTypes()
                .Where(type => type.Namespace == "Spark.Geometry" && !type.IsNested)
                .Select(type => type.Name),
        ];

        List<string> colliding =
        [
            .. typeof(Spark.Nodes.Core.Point).Assembly.GetExportedTypes()
                .Where(type => type.Namespace == "Spark.Nodes.Core" && !type.IsNested)
                .Select(type => type.Name)
                .Where(geometry.Contains)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        ImmutableArray<string> prelude = Catalogue().Imports;
        List<string> unpinned =
        [
            .. colliding.Where(
                name => !prelude.Contains($"{name} = Spark.Geometry.{name}", StringComparer.Ordinal)),
        ];

        Assert.True(
            unpinned.Count == 0,
            "These node-library type names collide with Spark.Geometry and are not pinned in "
            + "ReferenceCatalog.NodeLibraryImports, so a code block naming one gets CS0104: "
            + string.Join(", ", unpinned)
            + ". Add \"<name> = Spark.Geometry.<name>\" to that list.");
    }

    /// <summary>
    /// <b><c>Math</c> is <c>System.Math</c>, and that is the collision that kept the namespace out
    /// of the prelude.</b> A block is C#; <c>Math.PI</c> meaning anything else would be a trap.
    /// </summary>
    [Fact]
    public void MathIsStillSystemMath() => Assert.Equal(System.Math.PI, Run("Math.PI;"));

    /// <summary>
    /// <b>The façades with no geometry equivalent are reachable now, and this is the point of the
    /// row.</b> <c>Solid</c> is 38 nodes — every boolean, fillet and shell — and none of them could
    /// be called from a block before.
    /// </summary>
    [Theory]
    [InlineData("Solid.Box(Plane.WorldXY, 2.0, 2.0, 2.0);")]
    [InlineData("var a = Solid.Box(Plane.WorldXY, 2.0, 2.0, 2.0); var b = Solid.Cylinder(Plane.WorldXY, 0.5, 4.0); Solid.Difference(a, b);")]
    [InlineData("Logic.And(true, true);")]
    [InlineData("Text.FromNumber(3.5, 1);")]
    [InlineData("Colour.FromRgb(1, 2, 3);")]
    public void TheUtilityFacadesAreReachable(string script) => Compiles(script);

    /// <summary>And they really run, rather than merely compiling.</summary>
    /// <remarks>
    /// <b>The value is put in a variable, and that is `E6-T27`'s rule rather than a nicety.</b> A
    /// bare <c>Logic.And(true, true);</c> is an <i>invocation</i>, which C# accepts as a statement,
    /// so the block claims no port from it and returns null - exactly as <c>points.Add(p);</c>
    /// does. The first version of this test asserted against that null and was measuring the
    /// wrong thing.
    /// </remarks>
    [Fact]
    public void AUtilityFacadeRuns() => Assert.Equal(true, Run("var both = Logic.And(true, true);"));

    /// <summary>
    /// <b><c>List</c> is still <c>List&lt;T&gt;</c>, and the node called <c>List.Count</c> is
    /// <c>ListNodes.Count</c> in a block.</b> The four façades whose <i>node</i> name is a C#
    /// keyword-shaped type — <c>List</c>, <c>String</c>, <c>DateTime</c>, <c>TimeSpan</c> — are
    /// declared under another type name for exactly that reason, and no import can change it.
    /// </summary>
    /// <remarks>
    /// This is a wart and it is written down rather than smoothed over: the canvas says
    /// <c>List.Count</c> and a block says <c>ListNodes.Count</c>. The alternative is shadowing
    /// <c>List&lt;T&gt;</c> in every code block, which is not a trade worth making.
    /// </remarks>
    [Fact]
    public void ListIsStillTheGenericList()
    {
        Compiles("var xs = new List<int> { 1, 2, 3 }; xs.Count;");
        Compiles("var many = ListNodes.Count(new List<object> { 1, 2, 3 });");
    }

    /// <summary>
    /// <b>A list node counts a <c>SparkList</c>, and a plain C# list is one value to it.</b> Worth
    /// a test of its own because it is the trap this row opens: reaching the list nodes from a
    /// block does not make a <c>List&lt;T&gt;</c> into a graph list, and
    /// <c>ListNodes.Count(new List&lt;object&gt; { 1, 2, 3 })</c> is <b>1</b>, not 3.
    /// </summary>
    /// <remarks>
    /// The node's own parameter is <c>[KeepStructure] object?</c> and its body reads
    /// <c>list is SparkList items ? items.Count : 1</c> — rank is the graph's idea, not C#'s, and a
    /// code block is on the C# side of that line. This test asserts the behaviour rather than
    /// wishing it away; the first draft asserted 3 and was measuring what I expected instead of
    /// what the node does.
    /// </remarks>
    [Fact]
    public void AListNodeCountsAGraphListAndNotACSharpOne() =>
        Assert.Equal(1, Run("var many = ListNodes.Count(new List<object> { 1, 2, 3 });"));

    /// <summary>
    /// The library's own façade is still reachable in full, which is what every node reference page
    /// prints under <i>In a code block</i>.
    /// </summary>
    [Fact]
    public void TheFullyQualifiedFacadeStillWorks() =>
        Compiles("Spark.Nodes.Core.Circle.FromCenterRadius(Point3d.Origin, 5.0);");

    /// <summary>
    /// <b>Geometry can be built the way C# builds things — `E2-T59`.</b> Asked for by the client
    /// after typing <c>new Circle(center, 10)</c> into a block and being told there was no such
    /// constructor.
    /// </summary>
    /// <remarks>
    /// The kernel reached its geometry through named factories, which is what a node needs and not
    /// what a person writing C# reaches for first. Parity between the two is now held by
    /// <c>ConstructorParityTests</c> over in the geometry suite, by reflection; this is the other
    /// end of the same claim, checking that the constructors are reachable from inside a block
    /// rather than merely present on the type.
    /// </remarks>
    [Theory]
    [InlineData("new Circle(Point3d.Origin, 5.0);")]
    [InlineData("new Circle(Point3d.Origin, Vector3d.ZAxis, 5.0);")]
    [InlineData("new Circle(Point3d.Origin, new Point3d(1, 1, 0), new Point3d(2, 0, 0));")]
    [InlineData("new Line(Point3d.Origin, Vector3d.XAxis, 3.0);")]
    [InlineData("new Arc(Point3d.Origin, new Point3d(1, 1, 0), new Point3d(2, 0, 0));")]
    [InlineData("new Plane(Point3d.Origin, new Point3d(1, 0, 0), new Point3d(0, 1, 0));")]
    [InlineData("new PolyLine(Plane.WorldXY, 4.0, 2.0);")]
    [InlineData("new PolyLine(Plane.WorldXY, 2.0, 6);")]
    [InlineData(
        "var factory = Circle.FromCenterRadius(Point3d.Origin, 2.0);\n"
        + "var constructed = new Circle(Point3d.Origin, 2.0);")]
    public void TheConstructorsAreReachable(string script) => Compiles(script);

    /// <summary>
    /// <b>A constructor and the factory it mirrors give the same answer.</b> Every one of them
    /// forwards to its factory rather than repeating the arithmetic, and this is the test that
    /// says so from outside — a constructor that agreed only at compile time would be worse than
    /// none.
    /// </summary>
    [Fact]
    public void AConstructorAgreesWithItsFactory() =>
        Assert.Equal(
            true,
            Run("""
                var made = new Circle(Point3d.Origin, Vector3d.ZAxis, 5.0);
                var factory = Circle.FromCenterNormalRadius(Point3d.Origin, Vector3d.ZAxis, 5.0);
                var agree = made.Radius == factory.Radius
                    && made.Plane.Origin == factory.Plane.Origin
                    && made.Plane.Normal == factory.Plane.Normal;
                """));

    /// <summary>
    /// <b>A block that names nothing from the library is untouched.</b> The imports are a prelude,
    /// so a script that never uses them must not pay for them — least of all in ambiguity.
    /// </summary>
    [Fact]
    public void AnOrdinaryBlockIsUnaffected()
    {
        Assert.Equal(4, Run("var n = 10 / 5; var p = 8 / 4; n + p;"));
        Assert.Equal("test", Run("$\"test\";"));
    }
}
