using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Spark.Api.Help;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// Compiles every <c>csharp</c> fence in every help topic, with the references and imports a real
/// code block gets (<c>E11-T2</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This check has a record before it was ever written.</b> When
/// <c>concepts/geometry-basics.md</c> was authored, its samples were run against the compiled
/// assembly rather than written from the signatures, and <b>two came back wrong</b>:
/// <c>Angle.FullTurn / 3.0</c> is <c>119.99999999999999°</c>, not <c>120°</c>, and
/// <c>Tolerance.Default.Scaled(10.0).Linear</c> is <c>9.999999999999999e-6</c>, not <c>1e-5</c>.
/// Both read as perfectly plausible; both were false. Until this existed, running them by hand was
/// the only thing standing between a help topic and a confident lie.
/// </para>
/// <para>
/// <b>What it proves and what it does not.</b> It proves a sample <i>compiles</i> against the real
/// API — so a renamed method, a changed signature or a type that no longer exists turns the build
/// red. It does <b>not</b> prove the sample's stated result is correct; a comment claiming
/// <c>// 120</c> compiles whatever the answer is. Checking results is <c>E11-T3</c>'s job for
/// graphs, and for prose the fourth mechanism still applies: somebody reads it.
/// </para>
/// <para>
/// <c>&lt;!-- spark:skip --&gt;</c> on the line before a fence opts it out. <b>Every skip is a
/// sample nobody is checking</b>, so the count of them is asserted too — a skip added quietly is a
/// skip that spreads.
/// </para>
/// </remarks>
public sealed class DocumentationSampleTests
{
    /// <summary>How many fences are allowed to opt out. Raising this is a decision, not a fix.</summary>
    private const int AllowedSkips = 0;

    /// <summary>Every C# sample in the help compiles against the real API.</summary>
    /// <remarks>
    /// <b>Two kinds of sample, compiled two ways, because they are two different things.</b>
    /// <para>
    /// A sample in <c>concepts/code-blocks.md</c> <i>is a code block</i>: its bare identifiers are
    /// input ports the node supplies, and <c>return</c> is how it produces a value. Compiling one
    /// as an ordinary method body reports <i>the name 'radius' does not exist</i>, which is true of
    /// the method and false of the sample. It goes through <see cref="ScriptNodeFactory"/> —
    /// literally the thing that compiles a code block — so what is checked is what a user would
    /// actually type.
    /// </para>
    /// <para>
    /// Everywhere else each fence is compiled <b>on its own</b>, because each is an independent
    /// illustration rather than a step in one program — two fences in the same topic quite
    /// reasonably both declare a variable called <c>same</c>. The cost is that a fence has to carry
    /// its own setup, and making three of them do so was a real improvement rather than a
    /// concession: they had been quoting variables declared nowhere, which no reader could have
    /// pasted and run.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCsharpSampleInTheHelpCompiles()
    {
        IReadOnlyList<Sample> samples = Samples();

        Assert.True(samples.Count >= 30, $"expected the help's C# samples, found {samples.Count}");

        ReferenceCatalog catalog = Catalog();
        ScriptNodeFactory scripts = new(catalog);
        List<string> failures = [];

        foreach (IGrouping<string, Sample> topic in samples.GroupBy(sample => sample.Topic))
        {
            if (IsCodeBlockTopic(topic.Key))
            {
                foreach (Sample sample in topic)
                {
                    string? blockError = CompileAsCodeBlock(scripts, sample);
                    if (blockError is not null)
                    {
                        failures.Add($"{sample.Topic} sample {sample.Index}: {blockError}");
                    }
                }

                continue;
            }

            foreach (Sample sample in topic)
            {
                string? error = Compile(catalog, sample.Code);
                if (error is not null)
                {
                    failures.Add($"{sample.Topic} sample {sample.Index}: {error}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "These help samples do not compile against the current API:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// Whether a topic's samples are code blocks rather than ordinary C#.
    /// </summary>
    /// <remarks>
    /// By topic id rather than by guessing from the code. A heuristic — <i>does it use an
    /// undefined identifier?</i> — would silently reclassify an ordinary sample with a typo in it
    /// as a code block and then compile it successfully, which is the one outcome this check must
    /// never produce.
    /// </remarks>
    private static bool IsCodeBlockTopic(string topicId) =>
        string.Equals(topicId, "concepts.code-blocks", StringComparison.Ordinal);

    /// <summary>Compiles a sample the way the application compiles a code block.</summary>
    /// <returns>Null when it compiles, or the failure.</returns>
    private static string? CompileAsCodeBlock(ScriptNodeFactory scripts, Sample sample)
    {
        try
        {
            (string _, string body) = SplitLeadingUsings(sample.Code);
            _ = scripts.Create(body);
            return null;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return failure.Message;
        }
    }

    /// <summary>
    /// <b>The check is only worth having if it notices.</b> A sample naming a method that does not
    /// exist must fail, or the whole thing is decoration.
    /// </summary>
    [Fact]
    public void ASampleNamingSomethingThatDoesNotExistFails()
    {
        ReferenceCatalog catalog = Catalog();

        string? error = Compile(
            catalog, "var p = new Point3d(1, 2, 3);\nvar gone = p.ThisMethodDoesNotExist();");

        Assert.NotNull(error);
        Assert.Contains("ThisMethodDoesNotExist", error!, StringComparison.Ordinal);
    }

    /// <summary>And a sample that is fine must pass, so the check is not simply always red.</summary>
    [Fact]
    public void AValidSampleCompiles()
    {
        ReferenceCatalog catalog = Catalog();

        Assert.Null(Compile(catalog, "var p = new Point3d(1, 2, 3);\nvar d = p.X;"));
    }

    /// <summary>
    /// No sample opts out. Every skip is a sample nobody is checking, so the number of them is
    /// asserted rather than left to grow.
    /// </summary>
    [Fact]
    public void NoSampleOptsOutOfBeingChecked()
    {
        int skipped = 0;
        foreach (string file in TopicFiles())
        {
            skipped += File.ReadAllText(file).Split("<!-- spark:skip -->").Length - 1;
        }

        Assert.True(
            skipped <= AllowedSkips,
            $"{skipped} samples opt out of compilation, and {AllowedSkips} are allowed. "
            + "Each skip is a sample nothing is checking.");
    }

    /// <summary>
    /// Compiles one sample the way a code block is compiled: the same references, the same
    /// imports, wrapped in a method body.
    /// </summary>
    /// <returns>Null when it compiles, or the first error.</returns>
    private static string? Compile(ReferenceCatalog catalog, string code)
    {
        // A sample carries its own `using` lines, and it should: a reader looking at a help topic
        // wants code they could paste into a file, not a fragment that only works inside Spark's
        // invisible prelude. They have to be hoisted to file scope, because a using directive
        // inside a method body is a syntax error and reports as "Identifier expected", which names
        // nothing the author did wrong.
        (string usings, string body) = SplitLeadingUsings(code);

        StringBuilder source = new();
        source.AppendLine(catalog.Prelude());
        source.AppendLine(usings);
        source.AppendLine("public static class HelpSample {");
        source.AppendLine("  public static void Run() {");
        source.AppendLine(body);
        source.AppendLine("  }");
        source.AppendLine("}");

        CSharpCompilation compilation = CSharpCompilation.Create(
            "HelpSample",
            [CSharpSyntaxTree.ParseText(source.ToString())],
            catalog.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<Diagnostic> diagnostics = compilation.GetDiagnostics();

        foreach (Diagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
            {
                continue;
            }

            // Unused locals and unreachable code are normal in a sample: an example that assigns a
            // value to show what it is has no reason to use it afterwards.
            if (diagnostic.Id is "CS0219" or "CS0162" or "CS0168")
            {
                continue;
            }

            return diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        }

        return null;
    }

    /// <summary>
    /// A reference catalog with the assemblies a help sample names guaranteed to be loaded.
    /// </summary>
    /// <remarks>
    /// <b>The catalog builds itself from the assemblies currently loaded</b>, and .NET loads an
    /// assembly the first moment something touches it. A test process that has not yet mentioned
    /// <c>Spark.Geometry</c> therefore builds a catalog without it, and every sample fails with
    /// <i>the type or namespace name 'Geometry' does not exist</i> - which looks like a broken
    /// sample and is a broken harness. Touching one type from each assembly first is the fix, and
    /// it is the same trap <c>ReferenceCatalog</c> already documents for <c>Microsoft.CSharp</c>.
    /// </remarks>
    private static ReferenceCatalog Catalog()
    {
        ReferenceCatalog catalog = new();

        catalog.Add(
        [
            typeof(Spark.Geometry.Point3d).Assembly.Location,
            typeof(Spark.Api.SparkList).Assembly.Location,
            typeof(Spark.Engine.NodeKey).Assembly.Location,
            typeof(Spark.Nodes.Core.Point).Assembly.Location,
            typeof(Spark.Geometry.Io.ObjWriter).Assembly.Location,
        ]);

        return catalog;
    }

    /// <summary>
    /// Separates the <c>using</c> directives at the top of a sample from the statements below them.
    /// </summary>
    /// <returns>The directives, and the rest.</returns>
    private static (string Usings, string Body) SplitLeadingUsings(string code)
    {
        List<string> usings = [];
        List<string> body = [];
        bool stillLeading = true;

        foreach (string line in code.ReplaceLineEndings("\n").Split('\n'))
        {
            string trimmed = line.TrimStart();

            if (stillLeading && trimmed.StartsWith("using ", StringComparison.Ordinal) && trimmed.EndsWith(';'))
            {
                usings.Add(trimmed);
                continue;
            }

            if (stillLeading && trimmed.Length == 0)
            {
                continue;
            }

            stillLeading = false;
            body.Add(line);
        }

        return (string.Join("\n", usings), string.Join("\n", body));
    }

    /// <summary>Every <c>csharp</c> fence in every help topic, with where it came from.</summary>
    private static IReadOnlyList<Sample> Samples()
    {
        List<Sample> samples = [];

        foreach (string file in TopicFiles())
        {
            string text = File.ReadAllText(file);
            HelpDocument topic = HelpMarkdown.Parse(text, Path.GetFileNameWithoutExtension(file));

            int index = 0;
            foreach (HelpBlock block in topic.Blocks)
            {
                if (block.Kind != HelpBlockKind.Code)
                {
                    continue;
                }

                if (!string.Equals(block.Language, "csharp", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                samples.Add(new Sample(topic.Id, index++, block.Text ?? string.Empty));
            }
        }

        return samples;
    }

    private static IEnumerable<string> TopicFiles() =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot(), "docs", "help"), "*.md", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private sealed record Sample(string Topic, int Index, string Code);
}
