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
    /// <remarks>
    /// <b>Raised from zero to three on 2026-09-09, and only for samples that cannot compile
    /// here by construction</b> (`E7-T21`): <c>concepts/code-blocks.md</c> shows what using a
    /// library added from nuget.org looks like, and the library is not installed on this machine or
    /// on CI. Writing them against a package Spark ships would show the wrong thing — the whole
    /// point of the section is a type that is <i>not</i> in the catalogue until a user adds it.
    /// The count is still asserted, so a fourth skip is a decision somebody has to make on purpose.
    /// </remarks>
    private const int AllowedSkips = 3;

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
    private static string? Compile(ReferenceCatalog catalog, string code) =>
        Compile(catalog, code, SampleScope.Statements);

    /// <summary>Compiles one sample at the scope its author says it belongs at.</summary>
    /// <param name="catalog">The references and imports a real code block gets.</param>
    /// <param name="code">The sample, with its own <c>using</c> lines still on it.</param>
    /// <param name="scope">Whether the sample is statements or a member declaration.</param>
    /// <returns>Null when it compiles, or the first error.</returns>
    private static string? Compile(ReferenceCatalog catalog, string code, SampleScope scope)
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

        if (scope == SampleScope.Statements)
        {
            source.AppendLine("  public static void Run() {");
            source.AppendLine(body);
            source.AppendLine("  }");
        }
        else
        {
            source.AppendLine(body);
        }

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

    /// <summary>
    /// Every <c>&lt;example&gt;&lt;code&gt;</c> block in <c>src/</c> compiles against the real API
    /// (<c>E11-T2</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The other half of the same check, and it was doing nothing until now.</b> Every
    /// <c>csharp</c> fence in <c>docs/help/</c> has compiled since this class was written; the
    /// <c>&lt;example&gt;</c> blocks in the XML documentation were compiled by nothing, because
    /// when the harness was built no contract project used one. There are four now, and one of
    /// them ended in a literal <c>…</c> — a sample no reader could paste, sitting in the public
    /// API for as long as the attribute has existed.
    /// </para>
    /// <para>
    /// <b>The sources are read, not the generated XML.</b> A test that read
    /// <c>bin/Debug/net10.0/Spark.Api.xml</c> would depend on the configuration, the target
    /// framework and on <c>GenerateDocumentationFile</c> staying on — three ways to turn green by
    /// finding nothing. The <c>.cs</c> file is the thing the author edits and it is always there.
    /// </para>
    /// <para>
    /// <b>Not every example is a statement.</b> <see cref="Spark.Api.SparkNodeAliasAttribute"/>'s
    /// is an attribute on a <i>declaration</i>, which cannot compile inside a method body.
    /// <c>&lt;code spark-scope="class"&gt;</c> says so, and the author writes it — a heuristic
    /// that guessed the scope from the text would quietly reclassify a broken statement as a
    /// declaration and compile it. It is an attribute on the element rather than a marker line
    /// inside the sample so that what a reader copies is the declaration and nothing else.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryXmlExampleInTheSourceCompiles()
    {
        IReadOnlyList<XmlExample> examples = XmlExamples();

        Assert.True(
            examples.Count >= 4,
            $"expected the <example> blocks in src/, found {examples.Count} — the parser is broken, "
            + "not the sources");

        ReferenceCatalog catalog = Catalog();
        List<string> failures = [];

        foreach (XmlExample example in examples)
        {
            string? error = Compile(catalog, example.Code, example.Scope);
            if (error is not null)
            {
                failures.Add($"{example.Origin}: {error}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "These <example> blocks do not compile against the current API:\n"
            + string.Join("\n", failures));
    }

    /// <summary>
    /// <b>The same demand made of the fences.</b> A broken <c>&lt;example&gt;</c> has to go red, at
    /// both scopes, or the loop above is decoration.
    /// </summary>
    [Fact]
    public void AnXmlExampleNamingSomethingThatDoesNotExistFails()
    {
        ReferenceCatalog catalog = Catalog();

        string? statements = Compile(
            catalog, "Console.NoSuchNodeExists(\"a\");", SampleScope.Statements);
        string? declaration = Compile(
            catalog,
            "public static Circle Broken() => Circle.NoSuchFactoryExists();",
            SampleScope.Declarations);

        Assert.NotNull(statements);
        Assert.Contains("NoSuchNodeExists", statements!, StringComparison.Ordinal);
        Assert.NotNull(declaration);
        Assert.Contains("NoSuchFactoryExists", declaration!, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a declaration that is fine compiles, so <see cref="SampleScope.Declarations"/> is not
    /// simply always red.
    /// </summary>
    [Fact]
    public void AValidDeclarationExampleCompiles() =>
        Assert.Null(
            Compile(
                Catalog(),
                "public static Point3d Origin() => new Point3d(0, 0, 0);",
                SampleScope.Declarations));

    /// <summary>
    /// Every <c>&lt;example&gt;</c> element in the sources yields a sample the loop above checked.
    /// </summary>
    /// <remarks>
    /// There is no opt-out to grant here, and this is what stands in for the fences'
    /// <c>AllowedSkips</c>: an <c>&lt;example&gt;</c> whose <c>&lt;code&gt;</c> the parser fails to
    /// recognise would otherwise vanish silently, and a check that finds nothing is green.
    /// </remarks>
    [Fact]
    public void EveryExampleElementInTheSourceYieldsACheckedSample()
    {
        int declared = 0;
        foreach (string file in SourceFiles())
        {
            declared += File.ReadAllText(file).Split("<example>").Length - 1;
        }

        Assert.Equal(declared, XmlExamples().Count);
    }

    /// <summary>
    /// Every <c>&lt;code&gt;</c> block inside an <c>&lt;example&gt;</c> in <c>src/</c>.
    /// </summary>
    /// <returns>The samples, with where each came from and what scope it belongs at.</returns>
    private static IReadOnlyList<XmlExample> XmlExamples()
    {
        List<XmlExample> examples = [];

        foreach (string file in SourceFiles())
        {
            string name = Path.GetFileName(file);
            bool inExample = false;
            bool inCode = false;
            SampleScope scope = SampleScope.Statements;
            List<string> code = [];
            int index = 0;

            foreach (string line in File.ReadLines(file))
            {
                string? doc = DocComment(line);
                if (doc is null)
                {
                    continue;
                }

                string tag = doc.Trim();

                if (string.Equals(tag, "<example>", StringComparison.Ordinal))
                {
                    inExample = true;
                    continue;
                }

                if (string.Equals(tag, "</example>", StringComparison.Ordinal))
                {
                    inExample = false;
                    continue;
                }

                if (!inExample)
                {
                    continue;
                }

                if (!inCode && tag.StartsWith("<code", StringComparison.Ordinal))
                {
                    inCode = true;
                    scope = tag.Contains("spark-scope=\"class\"", StringComparison.Ordinal)
                        ? SampleScope.Declarations
                        : SampleScope.Statements;
                    code.Clear();
                    continue;
                }

                if (inCode && string.Equals(tag, "</code>", StringComparison.Ordinal))
                {
                    inCode = false;
                    examples.Add(
                        new XmlExample($"{name} example {index++}", scope, string.Join("\n", code)));
                    continue;
                }

                if (inCode)
                {
                    code.Add(Decode(doc));
                }
            }
        }

        return examples;
    }

    /// <summary>The text of a documentation comment line, without its slashes.</summary>
    /// <param name="line">A line of C# source.</param>
    /// <returns>The text after the slashes, or <see langword="null"/> when the line is not one.</returns>
    /// <remarks>
    /// One space after the slashes is the separator and is removed; anything beyond it is the
    /// sample's own indentation and is kept, because a continuation line that loses its indent
    /// still compiles but stops reading like the code it documents.
    /// </remarks>
    private static string? DocComment(string line)
    {
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith("///", StringComparison.Ordinal))
        {
            return null;
        }

        string rest = trimmed[3..];
        return rest.StartsWith(' ') ? rest[1..] : rest;
    }

    /// <summary>Turns the three XML entities a sample can contain back into characters.</summary>
    /// <param name="text">A line from inside a <c>&lt;code&gt;</c> block.</param>
    /// <returns>The line as the author wrote it.</returns>
    /// <remarks>
    /// The ampersand is undone last, or an author writing about the entity itself would have
    /// their <c>&amp;amp;lt;</c> come back as a bare <c>&lt;</c> and the sample would say
    /// something else.
    /// </remarks>
    private static string Decode(string text) =>
        text.Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);

    /// <summary>Every C# source file in <c>src/</c>, excluding build output.</summary>
    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .OrderBy(file => file, StringComparer.Ordinal);

    /// <summary>Whether a path lies under a <c>bin</c> or <c>obj</c> directory.</summary>
    private static bool IsBuildOutput(string file)
    {
        char separator = Path.DirectorySeparatorChar;
        return file.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
            || file.Contains($"{separator}obj{separator}", StringComparison.Ordinal);
    }

    /// <summary>What a sample is, and therefore where it has to be wrapped to compile.</summary>
    private enum SampleScope
    {
        /// <summary>Statements, wrapped in a method body. What almost every sample is.</summary>
        Statements,

        /// <summary>
        /// Member declarations, wrapped at class scope. An attribute on a method cannot be a
        /// statement, so the sample that shows one has to say so.
        /// </summary>
        Declarations,
    }

    private sealed record Sample(string Topic, int Index, string Code);

    private sealed record XmlExample(string Origin, SampleScope Scope, string Code);
}
