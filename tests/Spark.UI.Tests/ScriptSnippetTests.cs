using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// The C# snippets, ported from RCS's <c>SnippetCatalog</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The set is smaller than RCS's on purpose, and this file is the proof.</b> A Spark code block
/// is wrapped in <c>public static object Run(object[] __in, CancellationToken __token)</c>, so it
/// is a method body — and C# will not accept a type, a namespace, a property, an indexer, a
/// constructor or a member method inside one. Nineteen of RCS's thirty-six would therefore insert
/// code that cannot compile.
/// </para>
/// <para>
/// <b>A snippet that inserts an error is worse than no snippet</b>, because the user has to work
/// out that the tool was wrong rather than their code. So the catalogue is checked against the
/// parser rather than against a reviewer's memory, and the check is written so that it fails if
/// anybody adds one of the nineteen back.
/// </para>
/// </remarks>
public sealed class ScriptSnippetTests
{
    /// <summary>The bodies RCS ships that a method body cannot hold. Kept as the negative case.</summary>
    private static readonly string[] RejectedByAMethodBody =
    [
        "public class ${1:MyClass}\n{\n\t$0\n}",
        "public struct ${1:MyStruct}\n{\n\t$0\n}",
        "public interface ${1:IMyInterface}\n{\n\t$0\n}",
        "public enum ${1:MyEnum}\n{\n\t$0\n}",
        "namespace ${1:MyNamespace}\n{\n\t$0\n}",
        "public ${1:int} ${2:MyProperty} { get; set; }$0",
        "public ${1:object} this[${2:int} ${3:index}]\n{\n\tget { return default(${1:object}); }$0\n\tset { }\n}",
    ];

    /// <summary>
    /// <b>Every snippet the catalogue ships parses where it will be inserted.</b> Syntax only —
    /// the fields expand to placeholder names like <c>condition</c> that nothing declares, so
    /// binding them was never the question. Whether a construct is *allowed* in a method body is,
    /// and the parser answers exactly that.
    /// </summary>
    [Fact]
    public void EverySnippetParsesInsideACodeBlock()
    {
        Assert.NotEmpty(ScriptSnippets.Snippets);

        foreach (ScriptSnippet snippet in ScriptSnippets.Snippets)
        {
            IReadOnlyList<Diagnostic> errors = Parse(ScriptSnippets.Preview(snippet.Body));

            Assert.True(
                errors.Count == 0,
                $"`{snippet.Prefix}` does not parse in a code block: {string.Join("; ", errors.Select(e => e.GetMessage()))}");
        }
    }

    /// <summary>
    /// <b>And the check has teeth.</b> The nineteen that were left out are left out for a reason,
    /// and this asserts the reason rather than restating it in a comment — if a method body ever
    /// does accept a class declaration, this test says so and the catalogue can grow.
    /// </summary>
    [Fact]
    public void TheSnippetsRcsShipsThatSparkCannotAreRejectedByTheParser()
    {
        foreach (string body in RejectedByAMethodBody)
        {
            Assert.NotEmpty(Parse(ScriptSnippets.Preview(body)));
        }
    }

    /// <summary>Prefixes are unique, or two rows in the list do the same thing.</summary>
    [Fact]
    public void EveryPrefixIsUnique()
    {
        Assert.Equal(
            ScriptSnippets.Snippets.Count,
            ScriptSnippets.Snippets.Select(snippet => snippet.Prefix).Distinct(System.StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Tab expands only an exact prefix, so that Tab keeps its ordinary meaning everywhere else.
    /// </summary>
    [Theory]
    [InlineData("if", "if")]
    [InlineData("var x = 1;\nforeach", "foreach")]
    [InlineData("#region", "#region")]
    [InlineData("    try", "try")]
    public void AnExactPrefixExpands(string text, string expected)
    {
        ScriptSnippet snippet = Assert.IsType<ScriptSnippet>(ScriptSnippets.PrefixBefore(text, text.Length));
        Assert.Equal(expected, snippet.Prefix);
    }

    /// <summary>
    /// <b>A prefix has to stand on its own.</b> <c>myif</c> is not the <c>if</c> snippet, and a
    /// Tab after it indents like any other — which is the difference between a feature and a
    /// keyboard that occasionally rewrites your code.
    /// </summary>
    [Theory]
    [InlineData("myif")]
    [InlineData("iffy")]
    [InlineData("")]
    [InlineData("notasnippet")]
    public void APrefixInsideAWordDoesNotExpand(string text) =>
        Assert.Null(ScriptSnippets.PrefixBefore(text, text.Length));

    /// <summary>
    /// Repeated field numbers bind: <c>for</c> writes <c>i</c> three times and renaming the first
    /// renames all three, which is the whole reason the loop snippet is worth having.
    /// </summary>
    [Fact]
    public void ARepeatedFieldIsBoundToItsFirstOccurrence()
    {
        ScriptSnippet loop = Assert.IsType<ScriptSnippet>(ScriptSnippets.Find("for"));
        IReadOnlyList<ScriptSnippetSegment> segments = ScriptSnippets.Parse(loop.Body);

        ScriptSnippetSegment[] ones = [.. segments.Where(segment => segment.Number == 1)];

        Assert.Equal(3, ones.Length);
        Assert.Equal(ScriptSnippetSegmentKind.Field, ones[0].Kind);
        Assert.All(ones.Skip(1), segment => Assert.Equal(ScriptSnippetSegmentKind.Bound, segment.Kind));

        // And the preview reads as the code it will insert.
        Assert.Equal("for (int i = 0; i < length; i++)\n{\n\t\n}", ScriptSnippets.Preview(loop.Body));
    }

    /// <summary>Every snippet names where the caret should end up.</summary>
    [Fact]
    public void EverySnippetPlacesTheCaret() =>
        Assert.All(
            ScriptSnippets.Snippets,
            snippet => Assert.Contains(
                ScriptSnippets.Parse(snippet.Body),
                segment => segment.Kind == ScriptSnippetSegmentKind.Caret));

    /// <summary>Parses a snippet's expansion where a code block puts it, and returns the errors.</summary>
    private static IReadOnlyList<Diagnostic> Parse(string body)
    {
        // The shape `ScriptNodeFactory.Wrap` produces, reduced to what changes the answer: a method
        // body inside a static class. `return null;` keeps "not all code paths return a value" out
        // of the way, which is a semantic complaint and not what this is asking about.
        string source =
            "using System;\n"
            + "using System.Collections.Generic;\n"
            + "namespace SparkGenerated;\n"
            + "public static class Block {\n"
            + "public static object Run(object[] __in, System.Threading.CancellationToken __token) {\n"
            + body
            + "\nreturn null;\n}\n}\n";

        return [.. CSharpSyntaxTree.ParseText(source).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)];
    }
}
