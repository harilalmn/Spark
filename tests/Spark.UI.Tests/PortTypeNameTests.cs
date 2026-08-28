using System;
using System.Collections.Generic;
using Spark.Geometry;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// The words a port's type is shown under, on the canvas and in the properties panel.
/// </summary>
/// <remarks>
/// These names are read by somebody deciding what to plug in, so they are chosen for that person
/// rather than for the compiler. The tests are here to keep them that way: a future refactor that
/// "simplifies" this to <c>type.Name</c> would put `Double` and `Angle` on the canvas and would
/// pass every other test in the suite.
/// </remarks>
public sealed class PortTypeNameTests
{
    [Theory]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(float), "number")]
    [InlineData(typeof(int), "integer")]
    [InlineData(typeof(long), "integer")]
    [InlineData(typeof(bool), "true/false")]
    [InlineData(typeof(string), "text")]
    [InlineData(typeof(object), "anything")]
    public void PrimitivesAreNamedInWordsRatherThanInTypeNames(Type type, string expected) =>
        Assert.Equal(expected, PortTypeName.Describe(type));

    /// <summary>
    /// An <see cref="Angle"/> is called <c>degrees</c>, because the unit is the whole question.
    /// </summary>
    /// <remarks>
    /// The kernel holds angles in radians and the editor takes them in degrees. A port labelled
    /// "angle" invites somebody to type 1.5708 and get a quarter of a degree.
    /// </remarks>
    [Fact]
    public void AnAngleIsNamedByTheUnitItIsTypedIn() =>
        Assert.Equal("degrees", PortTypeName.Describe(typeof(Angle)));

    /// <summary>A type with no friendlier name keeps its own.</summary>
    [Fact]
    public void AKernelTypeKeepsItsName() =>
        Assert.Equal("Point3d", PortTypeName.Describe(typeof(Point3d)));

    /// <summary>
    /// A list port is named by its element, because the port's shape already says it is a list.
    /// </summary>
    /// <remarks>
    /// Rank is drawn as a ring around the port disc (design language §7.6). Repeating it in text
    /// would cost width on every node in the graph to say something already said.
    /// </remarks>
    [Fact]
    public void AListIsNamedByItsElementBecauseTheRingSaysItIsAList()
    {
        Assert.Equal("Point3d", PortTypeName.Describe(typeof(IReadOnlyList<Point3d>)));
        Assert.Equal("number", PortTypeName.Describe(typeof(IReadOnlyList<double>)));
    }

    /// <summary>A port whose name already says its type is not told twice.</summary>
    [Fact]
    public void ANameThatAlreadySaysTheTypeSuppressesIt()
    {
        Assert.Null(PortTypeName.Beside("circle", typeof(Circle)));
        Assert.Null(PortTypeName.Beside("Circle", typeof(Circle)));
        Assert.Null(PortTypeName.Beside("plane", typeof(Plane)));
    }

    /// <summary>
    /// A plural name over a list of that type is the same word, and is suppressed too.
    /// </summary>
    /// <remarks>
    /// <c>PolyCurve.ByJoinedCurves</c> takes <c>curves</c> as a list of <c>Curve</c>. Without this
    /// it would read "curves Curve", which is the node saying the same thing twice — and the ring
    /// on the port has already said the third thing, that there are several of them.
    /// </remarks>
    [Fact]
    public void APluralNameOverAListOfThatTypeIsSuppressed() =>
        Assert.Null(PortTypeName.Beside("curves", typeof(IReadOnlyList<Curve>)));

    /// <summary>
    /// The suppression is on the words, not on the concept, and <c>points</c> is the case that
    /// shows the difference.
    /// </summary>
    /// <remarks>
    /// A list of <c>Point3d</c> under a port called <c>points</c> still shows <c>Point3d</c>,
    /// because the name and the type are not the same word. That is the right answer rather than a
    /// missed case: the kernel type really is <c>Point3d</c> and a user hunting the library for
    /// something that makes one is better off knowing it.
    /// </remarks>
    [Fact]
    public void APluralNameOverADifferentlySpelledTypeKeepsIt() =>
        Assert.Equal("Point3d", PortTypeName.Beside("points", typeof(IReadOnlyList<Point3d>)));

    /// <summary>A name that says nothing about the type keeps it. This is the whole point.</summary>
    [Fact]
    public void ANameThatSaysNothingAboutTheTypeKeepsIt()
    {
        Assert.Equal("Point3d", PortTypeName.Beside("centre", typeof(Point3d)));
        Assert.Equal("number", PortTypeName.Beside("radius", typeof(double)));
        Assert.Equal("degrees", PortTypeName.Beside("sweepAngle", typeof(Angle)));
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => PortTypeName.Describe(null!));
        Assert.Throws<ArgumentNullException>(() => PortTypeName.Beside(null!, typeof(double)));
        Assert.Throws<ArgumentNullException>(() => PortTypeName.Beside("x", null!));
    }
}
