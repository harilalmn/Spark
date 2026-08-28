using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace Spark.Scripting;

/// <summary>One input port as the rewriter needs to write it: a name, a C# type, and an optional default.</summary>
/// <param name="Name">The variable the user's code reads. This is the port's identity.</param>
/// <param name="TypeName">The C# type name to declare it as, <c>global::</c>-rooted where Spark wrote it.</param>
/// <param name="DefaultExpression">A C# expression used when nothing is wired, or <see langword="null"/>.</param>
internal readonly record struct InputDeclaration(string Name, string TypeName, string? DefaultExpression);

/// <summary>The generated compilation unit for one code block, and the map back to what the user typed.</summary>
internal sealed class RewrittenCodeBlock
{
    internal RewrittenCodeBlock(string text, SourceMap map, string filePath)
    {
        Text = text;
        Map = map;
        FilePath = filePath;
    }

    /// <summary>The generated C#.</summary>
    internal string Text { get; }

    /// <summary>The offset map between the user's text and <see cref="Text"/>.</summary>
    internal SourceMap Map { get; }

    /// <summary>The path used for <c>#line</c> directives and diagnostics.</summary>
    internal string FilePath { get; }
}

/// <summary>
/// Turns a code block into an ordinary C# compilation unit.
/// </summary>
/// <remarks>
/// <para>
/// The user's text is copied <b>verbatim</b> into the body of a lambda, split only around the guard
/// calls woven into it. Nothing is reformatted and <b>no prelude is injected into the user's text</b>:
/// the header sits above a <c>#line 1</c> directive, so the compiler's line numbers land on the
/// editor's lines exactly, and the <see cref="SourceMap"/> carries the column-accurate offsets that
/// completion and squiggles need.
/// </para>
/// <para>
/// A lambda rather than a method with a written return type, because the block's result type is
/// whatever the user's <c>return</c> produces — including a named tuple, which is where the output
/// ports come from. <c>var __body = () =&gt; { ... };</c> gives that type a name without anyone having
/// to write it down.
/// </para>
/// </remarks>
internal static class ScriptRewriter
{
    /// <summary>The namespace every generated code block lives in.</summary>
    internal const string GeneratedNamespace = "Spark.UserScripts";

    /// <summary>The generated type name.</summary>
    internal const string GeneratedClassName = "SparkCodeBlock";

    /// <summary>The generated entry point.</summary>
    internal const string GeneratedMethodName = "Run";

    /// <summary>The generated local holding the user's body, found by name to read its inferred type.</summary>
    internal const string BodyVariableName = "__body";

    /// <summary>The full name of the generated type, for reflection after loading.</summary>
    internal const string GeneratedTypeName = GeneratedNamespace + "." + GeneratedClassName;

    /// <summary>
    /// Namespaces every code block gets for free. <c>Spark.Geometry</c> is on the list deliberately:
    /// typing <c>Point3d</c> and having it resolve is most of what a code block is for.
    /// </summary>
    internal static IReadOnlyList<string> DefaultUsings { get; } =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "Spark.Api",
        "Spark.Geometry",
    ];

    /// <summary>Rewrites one code block.</summary>
    /// <param name="analysis">The parsed user text.</param>
    /// <param name="inputs">The input ports to declare, in port order.</param>
    /// <param name="filePath">The path to name in <c>#line</c> directives.</param>
    /// <returns>The generated unit and its source map.</returns>
    internal static RewrittenCodeBlock Rewrite(
        ScriptTextAnalysis analysis, IReadOnlyList<InputDeclaration> inputs, string filePath)
    {
        string sanitized = Blank(analysis.UserText, analysis.Blanks);
        Builder builder = new(sanitized, analysis.Injections);

        builder.RawLine("#line hidden");

        foreach (string import in DefaultUsings)
        {
            builder.RawLine($"using {import};");
        }

        foreach (string import in analysis.HeaderUsings)
        {
            builder.RawLine(import);
        }

        builder.RawLine($"namespace {GeneratedNamespace}");
        builder.RawLine("{");
        builder.RawLine($"    internal static class {GeneratedClassName}");
        builder.RawLine("    {");
        builder.RawLine($"        public static object[] {GeneratedMethodName}(object[] __in)");
        builder.RawLine("        {");

        for (int index = 0; index < inputs.Count; index++)
        {
            builder.RawLine("            " + Declaration(index, inputs[index]));
        }

        builder.RawLine($"            var {BodyVariableName} = () =>");
        builder.RawLine("            {");
        builder.RawLine($"#line 1 \"{filePath}\"");
        builder.MarkFallback();

        builder.CopyAll();

        builder.RawLine();
        builder.RawLine("#line hidden");
        builder.RawLine("            };");

        EmitResult(builder, analysis);

        builder.RawLine("        }");
        builder.RawLine("    }");
        builder.RawLine("}");

        return builder.Build(filePath);
    }

    private static void EmitResult(Builder builder, ScriptTextAnalysis analysis)
    {
        switch (analysis.ResultKind)
        {
            case ScriptResultKind.None:
                builder.RawLine($"            {BodyVariableName}();");
                builder.RawLine("            return new object[] { null };");
                return;

            case ScriptResultKind.NamedTuple:
                builder.RawLine($"            var __result = {BodyVariableName}();");
                builder.RawLine(
                    "            return new object[] { "
                    + string.Join(", ", Prefixed(analysis.TupleNames))
                    + " };");
                return;

            default:
                builder.RawLine($"            var __result = {BodyVariableName}();");
                builder.RawLine("            return new object[] { __result };");
                return;
        }
    }

    private static IEnumerable<string> Prefixed(IReadOnlyList<string> names)
    {
        foreach (string name in names)
        {
            yield return "__result." + name;
        }
    }

    /// <summary>
    /// One input port, declared on one line so that no line number below it moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the differentiator.</b> When a port is wired, the upstream port's declared type is
    /// known, so the declaration reads <c>Point3d centre = ...</c> rather than <c>object centre</c> —
    /// and everything downstream of that, the compiler and completion alike, knows the type on the
    /// incoming wire.
    /// </para>
    /// <para>
    /// The value arrives through <c>ValueMarshal</c> rather than a plain cast, so the same widening
    /// the engine applies at every other port applies here: an <c>int</c> on the wire reaches a
    /// <c>double</c> port. A null on a wire to a value-typed port becomes that type's default rather
    /// than an exception, because a code block that has not been wired up yet should still run.
    /// </para>
    /// </remarks>
    private static string Declaration(int index, InputDeclaration input)
    {
        string slot = string.Create(CultureInfo.InvariantCulture, $"__in[{index}]");

        if (string.Equals(input.TypeName, "object", StringComparison.Ordinal))
        {
            return input.DefaultExpression is null
                ? $"object {input.Name} = {slot};"
                : $"object {input.Name} = {slot} ?? ({input.DefaultExpression});";
        }

        string fallback = input.DefaultExpression is null
            ? $"default({input.TypeName})"
            : $"({input.DefaultExpression})";

        return $"{input.TypeName} {input.Name} = {slot} is null ? {fallback} : "
            + $"({input.TypeName})global::Spark.Engine.ValueMarshal.ToClr({slot}, typeof({input.TypeName}));";
    }

    /// <summary>
    /// Replaces the given spans with spaces, keeping line breaks, so that every character offset and
    /// every line number in the copy still lines up with the text on screen.
    /// </summary>
    private static string Blank(string text, IReadOnlyList<TextSpan> spans)
    {
        if (spans.Count == 0)
        {
            return text;
        }

        char[] buffer = text.ToCharArray();

        foreach (TextSpan span in spans)
        {
            int end = Math.Min(span.End, buffer.Length);
            for (int index = Math.Max(0, span.Start); index < end; index++)
            {
                if (buffer[index] is not '\r' and not '\n')
                {
                    buffer[index] = ' ';
                }
            }
        }

        return new string(buffer);
    }

    /// <summary>Appends scaffolding and verbatim user chunks, recording the map as it goes.</summary>
    private sealed class Builder
    {
        private readonly StringBuilder _output = new();
        private readonly SourceMap _map = new();
        private readonly string _sanitized;
        private readonly IReadOnlyList<SourceInjection> _injections;

        internal Builder(string sanitized, IReadOnlyList<SourceInjection> injections)
        {
            _sanitized = sanitized;
            _injections = injections;
        }

        internal void RawLine(string text = "") => _output.Append(text).Append('\n');

        internal void MarkFallback() => _map.FallbackGeneratedOffset = _output.Length;

        /// <summary>
        /// Copies the whole of the user's text, split around every injection that falls inside it.
        /// </summary>
        /// <remarks>
        /// Splitting here rather than patching the map afterwards is the whole trick: the map records
        /// one entry per chunk, so a guard woven between two chunks costs the map nothing and every
        /// user offset either side of it still maps exactly.
        /// </remarks>
        internal void CopyAll()
        {
            int cursor = 0;

            foreach (SourceInjection injection in _injections)
            {
                int offset = Math.Clamp(injection.Offset, cursor, _sanitized.Length);
                Append(cursor, offset - cursor);
                _output.Append(injection.Text);
                cursor = offset;
            }

            Append(cursor, _sanitized.Length - cursor);
        }

        internal RewrittenCodeBlock Build(string filePath) => new(_output.ToString(), _map, filePath);

        private void Append(int start, int length)
        {
            if (length <= 0)
            {
                return;
            }

            _map.Add(start, _output.Length, length);
            _output.Append(_sanitized, start, length);
        }
    }
}
