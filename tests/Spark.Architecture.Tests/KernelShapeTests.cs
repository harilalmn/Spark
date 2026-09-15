using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spark.Architecture.Tests;

/// <summary>
/// The two shape rules the kernel is held to that nothing was holding it to (<c>E11-T34</c>):
/// geometry is sealed and immutable, and no drafting or annotation type exists in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>These were acceptance criteria with no guard and no register row.</b> Both are `EPICS.md`
/// boxes; both were ticked on 2026-09-15 by a search somebody ran once, and a search run once is
/// worth nothing next month. They also cite **no row**, so
/// <c>Spark.Docs.Verify.AcceptanceCriterionChecks</c> — which compares a box against the rows it
/// cites — could never reach them however it was written. Eleven criteria are like that; these two
/// are the two that assert something about the code rather than about the scaffolding, and the
/// answer to *no comparison can reach it* is a test, not a weaker comparison
/// ([N181](../../docs/NOTES.md)).
/// </para>
/// <para>
/// <b>Read as text, like everything else in this project.</b> A test that referenced
/// <c>Spark.Geometry</c> could see a public setter through reflection but not a
/// <c>private protected</c> constructor's intent, nor the difference between a member that hands
/// back its backing array and one that copies — and the reference itself would put this assembly
/// inside the graph it is checking. <c>ReferenceGraphTests</c> makes the same argument at length.
/// </para>
/// <para>
/// <b>What is deliberately not asserted.</b> Immutability here is **observable**, not bitwise, so
/// nothing forbids a private mutable field: <c>Brep</c> caches its bounding box and its residency
/// behind a <c>Lock</c>, and the criterion permits exactly that. What is forbidden is state
/// escaping — a public field, a settable property, or a member handing out the array it is
/// built on.
/// </para>
/// </remarks>
public sealed class KernelShapeTests
{
    /// <summary>The kernel's own sources, which is what "in the kernel" means in <c>D13</c>.</summary>
    private static readonly string[] KernelFiles = SourcesOf("Spark.Geometry");

    /// <summary>
    /// <b>Every concrete public geometry type is sealed.</b> An unsealed one is an invitation to
    /// subclass a value type's semantics — a `Circle` that overrides `PointAt` and lies about
    /// being a circle — and the kernel's whole contract is that a type of a given name behaves one
    /// way.
    /// </summary>
    /// <remarks>
    /// <c>abstract</c> is the deliberate exception and there are three: <c>Curve</c>,
    /// <c>Surface</c> and <c>BrepResidency</c>. The first two close their own hierarchies with a
    /// <c>private protected</c> constructor, which
    /// <see cref="TheAbstractBasesCloseTheirHierarchies"/> asserts.
    /// </remarks>
    [Fact]
    public void EveryConcretePublicGeometryTypeIsSealed()
    {
        Regex declaration = new(@"^public\s+(?<modifiers>[\w\s]*?)class\s+(?<name>\w+)", RegexOptions.Multiline);
        List<string> unsealed = [];

        foreach (string file in KernelFiles)
        {
            foreach (Match match in declaration.Matches(File.ReadAllText(file)))
            {
                string modifiers = match.Groups["modifiers"].Value;

                if (modifiers.Contains("sealed", StringComparison.Ordinal)
                    || modifiers.Contains("abstract", StringComparison.Ordinal)
                    || modifiers.Contains("static", StringComparison.Ordinal))
                {
                    continue;
                }

                unsealed.Add($"{Path.GetFileName(file)}: public class {match.Groups["name"].Value}");
            }
        }

        Assert.True(
            unsealed.Count == 0,
            "these public geometry classes are neither sealed, abstract nor static:\n  " + string.Join("\n  ", unsealed));
    }

    /// <summary>
    /// <b>The abstract bases close their hierarchies to the assembly.</b> `Curve` and `Surface` are
    /// abstract so that a curve can be a curve, not so that anybody can add a ninth kind: a
    /// <c>private protected</c> constructor means the set of curve types is fixed at compile time,
    /// which is what lets every exhaustive switch over them be exhaustive.
    /// </summary>
    [Fact]
    public void TheAbstractBasesCloseTheirHierarchies()
    {
        foreach (string type in (string[])["Curve", "Surface"])
        {
            string source = File.ReadAllText(KernelFile(type + ".cs"));

            Assert.True(
                source.Contains($"private protected {type}(", StringComparison.Ordinal),
                $"{type} has no private protected constructor, so its hierarchy is open outside the assembly.");
        }
    }

    /// <summary>
    /// <b>No public mutable state anywhere in the kernel.</b> No settable or initable property, and
    /// no public instance field. Both are the same defect wearing different syntax: a value that a
    /// caller can change after it was validated.
    /// </summary>
    /// <remarks>
    /// <c>const</c> and <c>static readonly</c> are permitted and are neither mutable nor state —
    /// <c>Point3d.Origin</c> and <c>Vector3d.XAxis</c> are the reason the exception exists.
    /// </remarks>
    [Fact]
    public void NoPublicMutableStateIsExposed()
    {
        Regex settable = new(@"public\s[^;{}()]*\{\s*get;\s*(set|init);\s*\}");
        // The `(?![>=])` is not decoration: without it the `=` of an expression-bodied property
        // matches, and every `public double Degrees => …` in the kernel reads as a public field.
        // That is what the first run of this test reported, in the hundreds.
        Regex field = new(
            @"^\s+public\s+(?!const\s|static\s+readonly\s|readonly\s)[\w<>,\[\]?\.]+\s+\w+\s*(=(?![>=])|;)",
            RegexOptions.Multiline);
        List<string> found = [];

        foreach (string file in KernelFiles)
        {
            string source = File.ReadAllText(file);

            found.AddRange(settable.Matches(source).Select(m => $"{Path.GetFileName(file)}: settable — {m.Value.Trim()}"));
            found.AddRange(field.Matches(source).Select(m => $"{Path.GetFileName(file)}: public field — {m.Value.Trim()}"));
        }

        Assert.True(
            found.Count == 0,
            "public mutable state in the geometry kernel:\n  " + string.Join("\n  ", found));
    }

    /// <summary>
    /// <b>No member hands back the array it is built on.</b> This is the clause the criterion is
    /// really about — <i>backing state never handed out</i> — and it is the one a reviewer misses,
    /// because <c>public Point3d[] Vertices() =&gt; _vertices;</c> looks exactly like the correct
    /// version and differs from it by two characters.
    /// </summary>
    /// <remarks>
    /// <b>An array is handed out by returning a field, and that is the whole pattern to find.</b>
    /// A returned collection expression, <c>ToArray</c>, or anything constructed is a copy by
    /// definition. So the rule is stated as a prohibition on the shape <c>=&gt; _field;</c> and
    /// <c>return _field;</c> from an array-returning member, rather than as an attempt to prove a
    /// copy happened — the first is exact, and the second would be a parser.
    /// </remarks>
    [Fact]
    public void NoPublicMemberHandsBackItsBackingArray()
    {
        Regex expressionBodied = new(@"public\s+[\w<>,\.]+\[\]\s+\w+\s*\([^)]*\)\s*=>\s*(?<returned>_\w+)\s*;");
        Regex blockBodied = new(@"public\s+[\w<>,\.]+\[\]\s+\w+\s*\([^)]*\)[^{]*\{[^{}]*?\breturn\s+(?<returned>_\w+)\s*;");
        List<string> leaks = [];

        foreach (string file in KernelFiles)
        {
            string source = File.ReadAllText(file);

            foreach (Regex pattern in (Regex[])[expressionBodied, blockBodied])
            {
                leaks.AddRange(pattern.Matches(source)
                    .Select(m => $"{Path.GetFileName(file)}: returns {m.Groups["returned"].Value} directly — {Shorten(m.Value)}"));
            }
        }

        Assert.True(
            leaks.Count == 0,
            "these members hand out their backing array instead of a copy:\n  " + string.Join("\n  ", leaks));
    }

    /// <summary>
    /// <b>No drafting or annotation type exists in the kernel (<c>D13</c>).</b> Spark models
    /// geometry; dimensions, leaders, hatches and text notes are a drawing product, and the
    /// decision was to not become one by accident — one `Dimension` type invites a `DimensionStyle`
    /// and then a paper space.
    /// </summary>
    /// <remarks>
    /// <b>A negative criterion has to be checked by looking for the thing</b>, so the vocabulary is
    /// written out rather than inferred, and it is matched on **type declarations** rather than on
    /// text: the word *annotation* is fine in a comment and fatal as a class.
    /// </remarks>
    [Fact]
    public void NoDraftingOrAnnotationTypeExistsInTheKernel()
    {
        string[] forbidden =
        [
            "Dimension", "Annotation", "Leader", "Hatch", "Callout", "Balloon", "TextNote",
            "Label", "Tag", "Symbol", "TitleBlock", "PaperSpace", "Viewport", "DrawingView",
        ];

        Regex declaration = new(
            @"^\s*(public|internal)\s+[\w\s]*?(class|struct|record|interface|enum)\s+(?<name>\w+)",
            RegexOptions.Multiline);

        List<string> found = [];

        foreach (string file in KernelFiles)
        {
            foreach (Match match in declaration.Matches(File.ReadAllText(file)))
            {
                string name = match.Groups["name"].Value;

                if (forbidden.Any(word => name.Contains(word, StringComparison.Ordinal)))
                {
                    found.Add($"{Path.GetFileName(file)}: {name}");
                }
            }
        }

        Assert.True(
            found.Count == 0,
            "D13 forbids drafting and annotation types in the kernel, and these are declared:\n  "
            + string.Join("\n  ", found));
    }

    /// <summary>
    /// <b>The scan finds the kernel at all.</b> Every assertion above is over a file list, and a
    /// list that came back empty would make all five pass while checking nothing —
    /// [N167](../../docs/NOTES.md) is this project's note about precisely that, and it cost a red
    /// commit.
    /// </summary>
    [Fact]
    public void TheKernelSourcesAreFound()
    {
        Assert.True(KernelFiles.Length >= 60, $"found {KernelFiles.Length} files in Spark.Geometry, expected at least 60");

        // And that the scan sees the types it is supposed to be reasoning about, rather than a
        // directory of the wrong things.
        string joined = string.Join("\n", KernelFiles.Select(Path.GetFileName));

        foreach (string expected in (string[])["Mesh.cs", "Brep.cs", "Curve.cs", "Surface.cs", "BrepBuilder.cs"])
        {
            Assert.Contains(expected, joined, StringComparison.Ordinal);
        }
    }

    private static string Shorten(string text) =>
        text.Length <= 110 ? Whitespace().Replace(text, " ") : Whitespace().Replace(text[..110], " ") + " …";

    private static Regex Whitespace() => new(@"\s+");

    private static string KernelFile(string name) =>
        KernelFiles.Single(file => Path.GetFileName(file) == name);

    private static string[] SourcesOf(string project)
    {
        string directory = Path.Combine(LocateSourceDirectory(), project);

        Assert.True(Directory.Exists(directory), $"no {project} directory at {directory}.");

        return
        [
            .. Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .OrderBy(file => file, StringComparer.Ordinal),
        ];
    }

    private static string LocateSourceDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException($"No Spark.slnx above {AppContext.BaseDirectory}.")
            : Path.Combine(directory.FullName, "src");
    }
}
