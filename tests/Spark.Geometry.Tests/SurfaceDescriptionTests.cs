using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Every surface says what it is, and the ruled loft over a sequence — `E2-T66`'s sixth item.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch for <c>ToString</c> is that each type declares its own, not that the text reads
/// well.</b> A type that inherits <see cref="object.ToString"/> prints its class name and looks
/// plausible in a debugger, which is exactly why the gap survived until the register found it
/// ([N161](../../docs/NOTES.md)). So the guard is by reflection, and a second test requires the
/// descriptions to differ from one another — an override copied from a neighbour would satisfy the
/// first test and mislead every reader of the second.
/// </para>
/// </remarks>
public sealed class SurfaceDescriptionTests
{
    /// <summary>
    /// <b>The guard.</b> Every concrete surface type declares its own <c>ToString</c>, so a tenth
    /// type cannot ship printing nothing but its class name.
    /// </summary>
    [Fact]
    public void EveryConcreteSurfaceTypeSaysWhatItIs()
    {
        foreach (Type type in ConcreteSurfaceTypes())
        {
            MethodInfo? member = type.GetMethod(nameof(ToString), Type.EmptyTypes);

            Assert.True(
                member is not null && member.DeclaringType == type,
                $"{type.Name} inherits ToString, so it prints only its class name. Give it one that says what the surface is.");
        }
    }

    /// <summary>
    /// Each description carries the type's own name and something measured, and no two are the
    /// same — which an override copied from a neighbour would fail.
    /// </summary>
    [Theory]
    [MemberData(nameof(AnalyticSurfaceTests.EverySurface), MemberType = typeof(AnalyticSurfaceTests))]
    public void EachDescriptionNamesItsTypeAndSaysSomething(Surface surface)
    {
        string description = surface.ToString() ?? string.Empty;

        Assert.StartsWith(surface.GetType().Name, description, StringComparison.Ordinal);
        Assert.Contains("(", description, StringComparison.Ordinal);
        Assert.True(
            description.Length > surface.GetType().Name.Length + 2,
            $"{description} says nothing the type name did not.");
    }

    /// <summary>Two surfaces of the same type but different size describe themselves differently.</summary>
    [Fact]
    public void TheDescriptionReflectsTheSurfaceAndNotJustItsType()
    {
        SphericalSurface small = new(Plane.WorldXY, 1.0);
        SphericalSurface large = new(Plane.WorldXY, 7.0);

        Assert.NotEqual(small.ToString(), large.ToString());
        Assert.Contains("7", large.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The loft's branch is the count.</b> N curves give N − 1 surfaces, which is the difference
    /// from Dynamo the register records; an implementation returning one, or N, would pass any test
    /// that only looked at the first.
    /// </summary>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(5, 4)]
    public void ASequenceOfCurvesGivesOneFewerSurfaces(int curves, int expected)
    {
        Assert.Equal(expected, RuledSurface.FromLoft(Stack(curves)).Count);
    }

    /// <summary>
    /// <b>And the pairing.</b> Surface <c>i</c> runs between curves <c>i</c> and <c>i + 1</c>, which
    /// a reversed or shifted loop would fail while still returning the right number of surfaces.
    /// </summary>
    [Fact]
    public void EachSurfaceRunsBetweenItsOwnPairOfCurves()
    {
        IReadOnlyList<Curve> curves = Stack(4);

        IReadOnlyList<RuledSurface> surfaces = RuledSurface.FromLoft(curves);

        for (int index = 0; index < surfaces.Count; index++)
        {
            Assert.Same(curves[index], surfaces[index].First);
            Assert.Same(curves[index + 1], surfaces[index].Second);
        }
    }

    /// <summary>Two curves is the degenerate case and agrees with the constructor exactly.</summary>
    [Fact]
    public void TwoCurvesAgreeWithTheConstructor()
    {
        IReadOnlyList<Curve> curves = Stack(2);

        RuledSurface lofted = Assert.Single(RuledSurface.FromLoft(curves));
        RuledSurface built = new(curves[0], curves[1]);

        for (int i = 0; i <= 5; i++)
        {
            for (int j = 0; j <= 5; j++)
            {
                Assert.True(lofted.PointAt(i / 5.0, j / 5.0).DistanceTo(built.PointAt(i / 5.0, j / 5.0)) < 1e-12);
            }
        }
    }

    [Fact]
    public void FewerThanTwoCurvesIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => RuledSurface.FromLoft(null!));
        Assert.Throws<ArgumentException>(() => RuledSurface.FromLoft([]));
        Assert.Throws<ArgumentException>(() => RuledSurface.FromLoft(Stack(1)));
    }

    /// <summary>A stack of parallel lines at increasing heights, each a little wider than the last.</summary>
    private static IReadOnlyList<Curve> Stack(int count)
    {
        List<Curve> curves = [];

        for (int index = 0; index < count; index++)
        {
            double width = 2.0 + index;

            curves.Add(new Line(new Point3d(-width, 0.0, index), new Point3d(width, 0.0, index)));
        }

        return curves;
    }

    private static IEnumerable<Type> ConcreteSurfaceTypes() =>
        typeof(Surface).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(Surface)) && !type.IsAbstract && type.IsPublic);
}
