using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The two-way reflection diff between <c>Spark.Nodes.Core</c> and the node library the importer
/// generates from it.
/// </summary>
/// <remarks>
/// <para>
/// This is the test the whole importer exists to be checked by, and it is written the way it is
/// because of a failure that has already been paid for once. <c>DoodleSharp</c>'s help generator
/// was 6,784 lines around three hand-maintained dictionaries keyed by string. It drifted in
/// <b>both</b> directions at once — 101 of 108 public constructors rendered blank while seven
/// carefully written entries pointed at members that had been deleted — and neither direction was
/// visible until a reflection diff was finally written, years too late.
/// </para>
/// <para>
/// So: every public member is either exactly one node or an exclusion with a stated reason, and
/// every node resolves to a live member of the assembly it claims to come from. Adding a public
/// method to <c>Spark.Nodes.Core</c> and forgetting to think about whether it is a node is a red
/// build.
/// </para>
/// </remarks>
public sealed class NodeLibraryCoverageTests
{
    private static readonly Assembly CoreNodes = typeof(Spark.Nodes.Core.Point).Assembly;

    private static readonly ImportReport Report = NodeImporter.Import(CoreNodes);

    private const BindingFlags Surface =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Direction one: nothing public is unreachable. Every public member of every public type is
    /// either a node or carries a reason for not being one.
    /// </summary>
    [Fact]
    public void EveryPublicMemberIsExactlyOneNodeOrIsExcludedWithAReason()
    {
        HashSet<MemberInfo> nodes = [.. Report.Nodes.Select(node => node.Member)];
        HashSet<MemberInfo> excluded = [.. Report.Exclusions.Select(exclusion => exclusion.Member)];

        List<string> unaccounted = [];
        List<string> counted = [];

        foreach (Type type in CoreNodes.GetExportedTypes())
        {
            if (excluded.Contains(type))
            {
                // The type itself is excluded, so its members are not part of the surface.
                continue;
            }

            foreach (MemberInfo member in type.GetMembers(Surface))
            {
                bool isNode = nodes.Contains(member);
                bool isExcluded = excluded.Contains(member);

                if (!isNode && !isExcluded)
                {
                    unaccounted.Add($"{type.Name}.{member.Name} ({member.MemberType})");
                }

                if (isNode && isExcluded)
                {
                    counted.Add($"{type.Name}.{member.Name}");
                }
            }
        }

        Assert.True(
            unaccounted.Count == 0,
            $"Public members that are neither a node nor an exclusion: {string.Join(", ", unaccounted)}.");

        Assert.True(
            counted.Count == 0,
            $"Members counted both as a node and as an exclusion: {string.Join(", ", counted)}.");
    }

    /// <summary>
    /// Direction two: nothing in the library points at nothing. Every generated node resolves to a
    /// member that is still there, on a type that is still exported.
    /// </summary>
    [Fact]
    public void EveryNodeResolvesToALiveMember()
    {
        Assert.NotEmpty(Report.Nodes);

        List<string> dangling = [];

        foreach (ImportedNode node in Report.Nodes)
        {
            Type? declaring = node.Member.DeclaringType;

            if (declaring is null || declaring.Assembly != CoreNodes)
            {
                dangling.Add($"{node.Definition.Key} -> {node.Member.Name}");
                continue;
            }

            bool present = declaring
                .GetMembers(Surface)
                .Any(member => member == node.Member);

            if (!present)
            {
                dangling.Add($"{node.Definition.Key} -> {declaring.Name}.{node.Member.Name}");
            }
        }

        Assert.True(dangling.Count == 0, $"Nodes pointing at members that are not there: {string.Join(", ", dangling)}.");
    }

    /// <summary>
    /// A duplicate key is a failure, not a last-one-wins. Two definitions with the same key make
    /// one of them permanently unreachable, and a saved graph would bind to whichever the
    /// dictionary happened to keep.
    /// </summary>
    [Fact]
    public void NoTwoNodesShareAKey()
    {
        List<string> keys = [.. Report.Nodes.Select(node => node.Definition.Key.Value)];
        List<string> duplicated = [.. keys.GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)];

        Assert.True(duplicated.Count == 0, $"Duplicate node keys: {string.Join(", ", duplicated)}.");

        // And the library refuses them too, rather than relying on the importer never producing one.
        NodeLibrary library = new();
        library.Add(Report);
        Assert.Equal(Report.Nodes.Count, library.Count);
    }

    /// <summary>
    /// An exclusion with a blank reason is the beginning of the hand-maintained list ADR-0004 says
    /// not to build. The reason is what a reviewer reads when deciding whether the exclusion is
    /// still right.
    /// </summary>
    [Fact]
    public void EveryExclusionStatesAReason()
    {
        List<string> blank = [.. Report.Exclusions
            .Where(exclusion => string.IsNullOrWhiteSpace(exclusion.Reason))
            .Select(exclusion => exclusion.ToString())];

        Assert.True(blank.Count == 0, $"Exclusions with no reason: {string.Join(", ", blank)}.");
    }

    /// <summary>
    /// ADR-0004's own warning, as a test: "if the exclusions file grows past a page, that is the
    /// signal that the dedup rule is too blunt". A page is about forty lines.
    /// </summary>
    [Fact]
    public void TheExclusionSetStaysSmallerThanAPage()
    {
        string listed = string.Join(
            "\n",
            Report.Exclusions.Select(exclusion => $"  {exclusion} — {exclusion.Reason}"));

        Assert.True(
            Report.Exclusions.Count <= 40,
            $"{Report.Exclusions.Count} exclusions, which is past a page. Revisit the importer rather than the list:\n{listed}");
    }

    /// <summary>
    /// The nodes the walking skeleton is built from are present, named as the design language and
    /// the task describe, and filed under a category the canvas can colour.
    /// </summary>
    [Theory]
    [InlineData("Point.ByCoordinates", "Point", 3, 1)]
    [InlineData("Point.Origin", "Point", 0, 1)]
    [InlineData("Point.Translate", "Point", 3, 1)]
    [InlineData("Vector.ByCoordinates", "Point", 3, 1)]
    [InlineData("Vector.XAxis", "Point", 0, 1)]
    [InlineData("Number.Range", "Input", 3, 1)]
    [InlineData("Math.Add", "Math", 2, 1)]
    [InlineData("Math.Sin", "Math", 1, 1)]
    [InlineData("Plane.ByOriginNormal", "Solid", 2, 1)]
    [InlineData("BoundingBox.ByCorners", "Solid", 2, 1)]
    [InlineData("Display.ByGeometryColour", "Display", 3, 1)]
    public void TheSkeletonNodesAreImportedWithTheRightShape(
        string name, string category, int inputs, int outputs)
    {
        NodeDefinition definition = Definition(name);

        Assert.Equal(category, definition.Category);
        Assert.Equal(inputs, definition.Inputs.Count);
        Assert.Equal(outputs, definition.Outputs.Count);
        Assert.Equal("Spark.Nodes.Core", definition.Key.Package);
    }

    /// <summary>
    /// <c>out</c> parameters become extra output ports after the return value, which is the only
    /// way a node gets more than one output.
    /// </summary>
    [Fact]
    public void OutParametersBecomeOutputPorts()
    {
        NodeDefinition definition = Definition("Point.Coordinates");

        Assert.Single(definition.Inputs);
        Assert.Equal(["x", "y", "z"], definition.Outputs.Select(port => port.Name));
    }

    /// <summary>
    /// An optional parameter's default becomes the port's literal, so a freshly placed node runs
    /// before the user has typed anything. A struct default arrives from reflection as null and has
    /// to be materialised, which is the case this pins.
    /// </summary>
    [Fact]
    public void OptionalParametersBecomePortLiterals()
    {
        NodeDefinition range = Definition("Number.Range");

        Assert.Equal(0.0, range.Inputs[0].DefaultValue);
        Assert.Equal(10.0, range.Inputs[1].DefaultValue);
        Assert.Equal(1.0, range.Inputs[2].DefaultValue);

        // Point3d has no declared default at all; the port still has to hold something.
        NodeDefinition translate = Definition("Point.Translate");
        Assert.NotNull(translate.Inputs[0].DefaultValue);
        Assert.IsType<Spark.Geometry.Point3d>(translate.Inputs[0].DefaultValue);
    }

    /// <summary>
    /// <c>Number.Range</c> declares a list output, so the port's rank is 1 and everything
    /// downstream of it laces. This is the single fact the whole demo rests on.
    /// </summary>
    [Fact]
    public void RangeDeclaresARankOneOutput()
    {
        NodeDefinition range = Definition("Number.Range");

        Assert.Equal(1, range.Outputs[0].DeclaredRank);
        Assert.Equal(0, Definition("Point.ByCoordinates").Inputs[0].DeclaredRank);
    }

    /// <summary>
    /// A port marked <c>[NoReplication]</c> keeps that flag through the import, or a display node
    /// would fan out over a list of colours.
    /// </summary>
    [Fact]
    public void ReplicationOptOutsSurviveTheImport()
    {
        NodeDefinition display = Definition("Display.ByGeometryColour");

        Assert.False(display.Inputs[0].NoReplication);
        Assert.True(display.Inputs[1].NoReplication);
        Assert.True(display.Inputs[2].NoReplication);
    }

    /// <summary>
    /// The XML documentation beside the assembly becomes the node's description, so a tooltip is
    /// the comment the author already wrote rather than a dictionary entry that will rot.
    /// </summary>
    [Fact]
    public void DescriptionsComeFromTheXmlDocumentation()
    {
        string? description = Definition("Point.Origin").Description;

        Assert.False(
            string.IsNullOrWhiteSpace(description),
            "Point.Origin has no description: the XML documentation file was not found or not parsed.");
        Assert.Contains("origin", description!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every imported node can actually be invoked, which a compile does not establish.</summary>
    [Fact]
    public void EveryNodeCompilesToAWorkingInvoker()
    {
        object?[] result = Definition("Point.ByCoordinates").Invoke([1.0, 2.0, 3.0]);

        Assert.Single(result);
        Assert.Equal(new Spark.Geometry.Point3d(1, 2, 3), result[0]);

        object?[] coordinates = Definition("Point.Coordinates").Invoke([new Spark.Geometry.Point3d(4, 5, 6)]);
        Assert.Equal([4.0, 5.0, 6.0], coordinates);
    }

    private static NodeDefinition Definition(string name)
    {
        ImportedNode? node = Report.Nodes.FirstOrDefault(
            candidate => string.Equals(candidate.Definition.DisplayName, name, StringComparison.Ordinal));

        Assert.True(
            node is not null,
            string.Create(
                CultureInfo.InvariantCulture,
                $"No node named '{name}'. Imported: {string.Join(", ", Report.Nodes.Select(n => n.Definition.DisplayName))}."));

        return node!.Definition;
    }
}
