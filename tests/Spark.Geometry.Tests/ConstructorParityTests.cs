using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Spark.Geometry.Tests;

/// <summary>
/// Every construction the kernel offers as a factory is also a constructor — `E2-T59`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client, in these words: whatever constructors are available in the library,
/// all those should be available in a code block too.</b> A code block is C#, and in C# the way
/// one asks for a circle is <c>new Circle(centre, radius)</c>. The node library reaches its
/// geometry through named factories, so before this row half the kernel could only be built the
/// long way round.
/// </para>
/// <para>
/// <b>This test is a reflection diff rather than a list of examples, and that is deliberate.</b>
/// The parity it guards is the sort that rots silently: someone adds
/// <c>Cone.FromBaseAndApex</c> six months from now, every existing test stays green, and the
/// constructor nobody remembered is missing until a user tries to type it. Here, that addition is
/// a red build with the new factory named in the message, and the author has to either add the
/// constructor or say in <see cref="Exempt"/> why there is not one.
/// </para>
/// </remarks>
public sealed class ConstructorParityTests
{
    /// <summary>
    /// The factories that deliberately have no constructor, each with the reason.
    /// </summary>
    /// <remarks>
    /// <b>Every entry here is an ambiguity, not an oversight.</b> A constructor is chosen by its
    /// parameter types alone, so where two factories on one type take the same types, a
    /// constructor would have to silently pick one of them — and a call whose meaning depends on
    /// which of two things the author had in mind is worse than no call at all.
    /// </remarks>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        ["Angle(Double)"] =
            "FromDegrees and FromRadians are both (double), and an angle carries no unit in its "
            + "type. new Angle(90) would have to quietly mean one of them, and the reader of that "
            + "line could not tell which — a wrong answer that looks right. Say which you mean.",
        ["Plane(in Point3d, in Vector3d, in Vector3d)"] =
            "FromOriginXAxisYAxis and FromOriginNormalXAxis are both (Point3d, Vector3d, Vector3d) "
            + "and disagree about what the second vector is. Getting it wrong tilts the plane "
            + "rather than failing, so the name has to stay on the call.",
    };

    /// <summary>
    /// A factory's shape, as a constructor would have to declare it.
    /// </summary>
    /// <param name="Type">The type being built.</param>
    /// <param name="Names">The factories sharing this parameter sequence. More than one is a clash.</param>
    /// <param name="Key">The parameter type sequence, which is what picks an overload.</param>
    /// <param name="Signature">The sequence written out, for the failure message.</param>
    private sealed record Shape(Type Type, List<string> Names, string Key, string Signature);

    private static string Key(IEnumerable<ParameterInfo> parameters) =>
        string.Join(
            "|",
            parameters.Select(p =>
                p.ParameterType.IsByRef
                    ? p.ParameterType.GetElementType()!.FullName
                    : p.ParameterType.FullName));

    private static string Written(IEnumerable<ParameterInfo> parameters) =>
        string.Join(
            ", ",
            parameters.Select(p =>
                (p.ParameterType.IsByRef ? "in " : string.Empty)
                + (p.ParameterType.IsByRef
                    ? p.ParameterType.GetElementType()!.Name
                    : p.ParameterType.Name)));

    /// <summary>Every factory in the kernel, grouped by the constructor it would need.</summary>
    private static IEnumerable<Shape> Shapes()
    {
        foreach (Type type in typeof(Point3d).Assembly.GetExportedTypes().OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            IEnumerable<MethodInfo> factories = type
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.ReturnType == type
                    && (m.Name.StartsWith("From", StringComparison.Ordinal)
                        || m.Name.StartsWith("Create", StringComparison.Ordinal)));

            foreach (IGrouping<string, MethodInfo> group in factories
                .GroupBy(m => Key(m.GetParameters()), StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                yield return new Shape(
                    type,
                    [.. group.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal)],
                    group.Key,
                    Written(group.First().GetParameters()));
            }
        }
    }

    private static string Label(Shape shape) =>
        $"{shape.Type.Name}({shape.Signature})";

    /// <summary>
    /// <b>Direction one: nothing the kernel can make is missing its constructor.</b> This is the
    /// client's sentence turned into a build failure.
    /// </summary>
    [Fact]
    public void EveryFactoryHasAConstructorOfTheSameShape()
    {
        List<string> missing = [];

        foreach (Shape shape in Shapes())
        {
            if (Exempt.ContainsKey(Label(shape)))
            {
                continue;
            }

            bool has = shape.Type
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Any(c => Key(c.GetParameters()) == shape.Key);

            if (!has)
            {
                missing.Add($"{Label(shape)} — for {string.Join(" / ", shape.Names)}");
            }
        }

        Assert.True(
            missing.Count == 0,
            "Factories with no constructor of the same shape. Add the constructor, forwarding to "
            + "the factory so the two cannot drift, or add the signature to Exempt with the reason:"
            + $"\n  {string.Join("\n  ", missing)}");
    }

    /// <summary>
    /// <b>Direction two: every exemption is real.</b> An exemption that has quietly been fixed, or
    /// that names a factory nobody kept, is the half of a hand-maintained list that rots without
    /// anybody noticing — so the list is checked against the assembly in both directions.
    /// </summary>
    [Fact]
    public void EveryExemptionStillNamesAFactoryWithNoConstructor()
    {
        HashSet<string> live = [.. Shapes()
            .Where(shape => !shape.Type
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Any(c => Key(c.GetParameters()) == shape.Key))
            .Select(Label)];

        List<string> stale = [.. Exempt.Keys.Where(key => !live.Contains(key))];

        Assert.True(
            stale.Count == 0,
            $"Exemptions that no longer describe anything — delete them: {string.Join(", ", stale)}.");
    }

    /// <summary>Every exemption states why, because a bare list teaches the next reader nothing.</summary>
    [Fact]
    public void EveryExemptionGivesAReason()
    {
        List<string> blank = [.. Exempt.Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => e.Key)];

        Assert.True(blank.Count == 0, $"Exemptions with no reason: {string.Join(", ", blank)}.");
    }
}
