using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// Wire-typed IntelliSense — `E6-T7`.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the demo.</b> A code block's port is called <c>centre</c>; nothing in the text says
/// what a <c>centre</c> is. Wire a point into it and typing <c>centre.</c> lists the members of
/// <see cref="Point3d"/> — completion following the wires rather than the text is the one thing
/// Spark can show that a graph tool without a compiler cannot.
/// </para>
/// <para>
/// <b>The negative case matters as much as the positive one.</b> With nothing wired in, the port is
/// <c>dynamic</c> — and completion must say so rather than invent a list, because the compiler will
/// not know either. `E6-T13`'s invariant is that a list which disagrees with the compiler is worse
/// than no list.
/// </para>
/// </remarks>
public sealed class WireTypedCompletionTests
{
    private const string Snippet = "return centre.";

    /// <summary>
    /// <b>A wired port completes against the type the wire carries.</b> Nothing in the snippet
    /// declares <c>centre</c>; the graph does.
    /// </summary>
    [Fact]
    public async Task AWiredPortCompletesAgainstItsType()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        IReadOnlyList<ScriptCompletionItem> items = await completion.CompleteAsync(
            Snippet,
            Snippet.Length,
            new Dictionary<string, Type?> { ["centre"] = typeof(Point3d) },
            TestContext.Current.CancellationToken);

        string[] names = [.. items.Select(item => item.DisplayText)];

        Assert.Contains("X", names);
        Assert.Contains("DistanceTo", names);
    }

    /// <summary>
    /// The port itself is offered by name too, which is what makes the ports discoverable at all —
    /// a user who cannot remember whether they called it <c>centre</c> or <c>center</c> types
    /// <c>ce</c> and finds out.
    /// </summary>
    [Fact]
    public async Task ThePortNameIsOfferedAsAnIdentifier()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        const string Start = "return ce";

        IReadOnlyList<ScriptCompletionItem> items = await completion.CompleteAsync(
            Start,
            Start.Length,
            new Dictionary<string, Type?> { ["centre"] = typeof(Point3d) },
            TestContext.Current.CancellationToken);

        Assert.Contains("centre", items.Select(item => item.DisplayText));
    }

    /// <summary>
    /// <b>An unwired port completes as <c>dynamic</c>, which is honest rather than helpful.</b> The
    /// compiler will declare it <c>dynamic</c>, so a list of <see cref="Point3d"/>'s members here
    /// would be a promise the compile does not keep.
    /// </summary>
    [Fact]
    public async Task AnUnwiredPortDoesNotInventAType()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        IReadOnlyList<ScriptCompletionItem> items = await completion.CompleteAsync(
            Snippet,
            Snippet.Length,
            new Dictionary<string, Type?> { ["centre"] = null },
            TestContext.Current.CancellationToken);

        // Roslyn offers nothing after a `dynamic` receiver's dot, because there is nothing it can
        // know. An empty list is the correct answer and the assertion is that it is empty of
        // *geometry*, not that it is empty - the editor may still show snippets and keywords.
        Assert.DoesNotContain("DistanceTo", items.Select(item => item.DisplayText));
    }

    /// <summary>
    /// The caret is moved with the declarations, so the completion is taken at the position the
    /// editor meant. Getting this wrong offers the members of whatever happens to be under the
    /// unshifted offset, which looks like a list rather than like a bug.
    /// </summary>
    [Fact]
    public async Task TheCaretIsShiftedWithTheDeclarations()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        // Two ports, so the prefix is long enough that an unshifted caret would land inside it.
        Dictionary<string, Type?> ports = new()
        {
            ["centre"] = typeof(Point3d),
            ["direction"] = typeof(Vector3d),
        };

        const string Code = "return direction.";

        IReadOnlyList<ScriptCompletionItem> items = await completion.CompleteAsync(
            Code, Code.Length, ports, TestContext.Current.CancellationToken);

        string[] names = [.. items.Select(item => item.DisplayText)];

        Assert.Contains("Normalised", names);
        Assert.DoesNotContain("DistanceTo", names);
    }

    /// <summary>
    /// A port whose name is not a C# identifier is skipped rather than emitted. One declaration
    /// that does not parse takes the whole list down, silently.
    /// </summary>
    [Fact]
    public async Task APortNameThatIsNotAnIdentifierIsSkipped()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        Dictionary<string, Type?> ports = new()
        {
            ["centre"] = typeof(Point3d),
            ["not a name"] = typeof(Point3d),
        };

        IReadOnlyList<ScriptCompletionItem> items = await completion.CompleteAsync(
            Snippet, Snippet.Length, ports, TestContext.Current.CancellationToken);

        Assert.Contains("X", items.Select(item => item.DisplayText));
    }
}
