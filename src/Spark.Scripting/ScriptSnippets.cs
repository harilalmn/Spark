using System;
using System.Collections.Generic;
using System.Text;

namespace Spark.Scripting;

/// <summary>What a segment of an expanded snippet is.</summary>
public enum ScriptSnippetSegmentKind
{
    /// <summary>Text inserted as written.</summary>
    Literal,

    /// <summary>An editable field, and the first occurrence of its number.</summary>
    Field,

    /// <summary>A later occurrence of a field, which follows the first as it is typed.</summary>
    Bound,

    /// <summary>Where the caret rests once the fields are left — the template's <c>$0</c>.</summary>
    Caret,
}

/// <summary>One piece of a parsed snippet body.</summary>
/// <param name="Kind">What this piece is.</param>
/// <param name="Text">The literal text, or a field's default. Empty for a caret.</param>
/// <param name="Number">The field's number, or zero when it is not a field.</param>
/// <remarks>
/// <b>Deliberately not AvaloniaEdit's <c>SnippetElement</c>.</b> Parsing a template is a language
/// concern and belongs here; turning the result into tab stops is the editor's, and lives in
/// <c>Spark.UI</c>. Splitting them this way is what lets the parser be tested without a UI thread,
/// a window or a text area — and it is the same boundary <c>ScriptCompletionItem</c> keeps.
/// </remarks>
public readonly record struct ScriptSnippetSegment(
    ScriptSnippetSegmentKind Kind,
    string Text,
    int Number);

/// <summary>One snippet: what the user types to reach it, and what it expands to.</summary>
/// <param name="Prefix">What the user types, and what the completion list filters on.</param>
/// <param name="Group">The heading it sits under.</param>
/// <param name="Description">One line, shown beside it.</param>
/// <param name="Body">
/// The template. <c>${1:name}</c> is an editable field — repeat the number to bind several
/// occurrences together — and <c>$0</c> is where the caret lands when the last field is left.
/// A tab is one indent level and a newline is one line.
/// </param>
public sealed record ScriptSnippet(string Prefix, string Group, string Description, string Body);

/// <summary>
/// The built-in C# snippets for a code block, and the parser that reads one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ported from RCS's <c>SnippetCatalog</c></b> (<c>C:\Zyeta\Projects\RCS</c>), whose set is
/// Visual Studio's. It is not the same set, and the difference is not a matter of taste.
/// </para>
/// <para>
/// <b>A Spark code block is a method body, not a script.</b> `ScriptNodeFactory.Wrap` puts the
/// user's text inside <c>public static object Run(object[] __in, CancellationToken __token)</c>,
/// and C# does not allow a type, a namespace, a property, an indexer, a constructor, a finalizer
/// or a member method to be declared inside a method. So nineteen of RCS's thirty-six —
/// <c>class</c>, <c>struct</c>, <c>interface</c>, <c>enum</c>, <c>namespace</c>, <c>ctor</c>,
/// <c>~</c>, <c>attribute</c>, <c>exception</c>, <c>prop</c>, <c>propfull</c>, <c>propg</c>,
/// <c>propi</c>, <c>indexer</c>, <c>equals</c>, <c>iterator</c>, <c>svm</c>, <c>sim</c> and
/// <c>cw</c> — would insert code that cannot compile. <b>A snippet that inserts an error is worse
/// than no snippet</b>, because the user has to work out that the tool was wrong rather than
/// their code, so they are not here. <c>unsafe</c> goes with them: `AllowUnsafeBlocks` is off.
/// </para>
/// <para>
/// <c>cw</c> reached RCS's console, which Spark has no equivalent of — a block's output is what it
/// returns. <c>ret</c> and <c>lf</c> take the place of what was dropped, and are the two things a
/// method body actually wants that RCS's list has no reason to carry.
/// </para>
/// <para>
/// <b>Two ways in, and both are wanted</b>, exactly as in RCS: the list offers them, so a snippet
/// is discoverable without knowing it exists; and typing a prefix then <b>Tab</b> expands it with
/// no list at all, which is the reflex a Visual Studio user brings.
/// </para>
/// </remarks>
public static class ScriptSnippets
{
    private const string Flow = "Control flow and loops";
    private const string Resources = "Exceptions and resources";
    private const string Output = "Values and helpers";
    private const string Preprocessor = "Preprocessor directives";

    private static readonly ScriptSnippet[] All =
    [
        // ------------------------------------------------------ control flow and loops
        new("if", Flow, "Standard if block", "if (${1:condition})\n{\n\t$0\n}"),

        new("ifelse", Flow, "if-else branch structure", "if (${1:condition})\n{\n\t$0\n}\nelse\n{\n}"),

        new(
            "switch",
            Flow,
            "Standard switch-case statement",
            "switch (${1:value})\n"
            + "{\n"
            + "\tcase ${2:0}:\n"
            + "\t\t$0\n"
            + "\t\tbreak;\n\n"
            + "\tdefault:\n"
            + "\t\tbreak;\n"
            + "}"),

        new(
            "for",
            Flow,
            "Standard incrementing for loop",
            "for (int ${1:i} = 0; ${1:i} < ${2:length}; ${1:i}++)\n{\n\t$0\n}"),

        new(
            "forr",
            Flow,
            "Reverse, decrementing for loop",
            "for (int ${1:i} = ${2:length} - 1; ${1:i} >= 0; ${1:i}--)\n{\n\t$0\n}"),

        new(
            "foreach",
            Flow,
            "foreach loop over a collection",
            "foreach (${1:var} ${2:item} in ${3:collection})\n{\n\t$0\n}"),

        new("do", Flow, "do-while loop", "do\n{\n\t$0\n}\nwhile (${1:condition});"),

        new("while", Flow, "while loop", "while (${1:condition})\n{\n\t$0\n}"),

        // ------------------------------------------------- exceptions and resources
        new(
            "try",
            Resources,
            "try-catch block",
            "try\n{\n\t$0\n}\ncatch (${1:Exception} ${2:exception})\n{\n}"),

        new("tryf", Resources, "try-finally block", "try\n{\n\t$0\n}\nfinally\n{\n}"),

        new(
            "using",
            Resources,
            "using statement for an IDisposable",
            "using (${1:var} ${2:resource} = ${3:expression})\n{\n\t$0\n}"),

        new("lock", Resources, "lock synchronisation block", "lock (${1:this})\n{\n\t$0\n}"),

        new("checked", Resources, "Arithmetic overflow checked block", "checked\n{\n\t$0\n}"),

        new("unchecked", Resources, "Arithmetic overflow unchecked block", "unchecked\n{\n\t$0\n}"),

        // ------------------------------------------------------- values and helpers
        //
        // SPARK'S OWN, AND THEY ARE WHAT `cw` AND `iterator` WOULD HAVE BEEN. A block's output is
        // what it returns - there is no console to write to - and a local function is the only kind
        // of method a method body may declare.
        new("ret", Output, "Returns a value from the block", "return $0;"),

        new(
            "lf",
            Output,
            "Local function, the only method a code block may declare",
            "${1:double} ${2:Compute}(${3:double value})\n{\n\t$0\n}"),

        // ------------------------------------------------------ preprocessor directives
        new(
            "#region",
            Preprocessor,
            "Inserts a #region and #endregion block",
            "#region ${1:name}\n$0\n#endregion"),

        new("#if", Preprocessor, "Inserts an #if and #endif block", "#if ${1:DEBUG}\n$0\n#endif"),
    ];

    /// <summary>Every snippet, in the order a guide would list them.</summary>
    public static IReadOnlyList<ScriptSnippet> Snippets => All;

    /// <summary>The snippet with exactly this prefix, or null. Prefixes are case sensitive.</summary>
    /// <param name="prefix">The prefix to look for.</param>
    /// <returns>The snippet, or <see langword="null"/>.</returns>
    public static ScriptSnippet? Find(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return null;
        }

        foreach (ScriptSnippet snippet in All)
        {
            if (string.Equals(snippet.Prefix, prefix, StringComparison.Ordinal))
            {
                return snippet;
            }
        }

        return null;
    }

    /// <summary>
    /// Characters a prefix can contain.
    /// </summary>
    /// <param name="candidate">The character to test.</param>
    /// <returns>True when it can appear in a prefix.</returns>
    /// <remarks>
    /// Wider than an identifier because two of the prefixes are not identifiers: <c>#region</c>
    /// and <c>#if</c>.
    /// </remarks>
    public static bool IsPrefixChar(char candidate) =>
        char.IsLetterOrDigit(candidate) || candidate == '_' || candidate == '#';

    /// <summary>
    /// The snippet whose prefix ends at <paramref name="offset"/>, or null.
    /// </summary>
    /// <param name="text">The document.</param>
    /// <param name="offset">The caret offset.</param>
    /// <returns>The snippet to expand, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <b>Exact matches only</b>, so that Tab keeps its ordinary meaning everywhere else. A prefix
    /// has to stand on its own: <c>myif</c> is not the <c>if</c> snippet, and a Tab after it
    /// indents like any other.
    /// </remarks>
    public static ScriptSnippet? PrefixBefore(string? text, int offset)
    {
        if (text is null || offset <= 0 || offset > text.Length)
        {
            return null;
        }

        int start = offset;
        while (start > 0 && IsPrefixChar(text[start - 1]))
        {
            start--;
        }

        if (start == offset)
        {
            return null;
        }

        if (Find(text[start..offset]) is { } exact)
        {
            return exact;
        }

        // `#region` and `#if` are reached by taking only the tail of the run, because the `#` is
        // part of the prefix and a scan backwards over identifier characters walks past it.
        for (int from = start + 1; from < offset; from++)
        {
            if (Find(text[from..offset]) is { Prefix: ['#', ..] } directive)
            {
                return directive;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a body into segments: literals, editable fields, their repeats, and the caret.
    /// </summary>
    /// <param name="body">The template.</param>
    /// <returns>The segments, in insertion order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    public static IReadOnlyList<ScriptSnippetSegment> Parse(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        List<ScriptSnippetSegment> segments = [];
        HashSet<int> placed = [];
        StringBuilder literal = new();

        void FlushLiteral()
        {
            if (literal.Length == 0)
            {
                return;
            }

            segments.Add(new ScriptSnippetSegment(ScriptSnippetSegmentKind.Literal, literal.ToString(), 0));
            literal.Clear();
        }

        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];

            // An escaped dollar, so a template can contain one literally.
            if (c == '\\' && i + 1 < body.Length && body[i + 1] == '$')
            {
                literal.Append('$');
                i++;
                continue;
            }

            if (c != '$')
            {
                literal.Append(c);
                continue;
            }

            if (i + 1 < body.Length && body[i + 1] == '0')
            {
                FlushLiteral();
                segments.Add(new ScriptSnippetSegment(ScriptSnippetSegmentKind.Caret, string.Empty, 0));
                i++;
                continue;
            }

            if (!TryReadField(body, i, out int number, out string defaultText, out int next))
            {
                literal.Append(c);
                continue;
            }

            FlushLiteral();
            i = next;

            // The first occurrence is the field; the rest follow it as the user types.
            ScriptSnippetSegmentKind kind = placed.Add(number)
                ? ScriptSnippetSegmentKind.Field
                : ScriptSnippetSegmentKind.Bound;

            segments.Add(new ScriptSnippetSegment(kind, defaultText, number));
        }

        FlushLiteral();

        return segments;
    }

    /// <summary>
    /// The body as it reads once inserted, with every field showing its default.
    /// </summary>
    /// <param name="body">The template.</param>
    /// <returns>The text a user would see.</returns>
    /// <remarks>
    /// What the completion list shows as a description, and what the tests compile to prove that a
    /// snippet inserts something a code block accepts.
    /// </remarks>
    public static string Preview(string body)
    {
        StringBuilder text = new();
        Dictionary<int, string> defaults = [];

        foreach (ScriptSnippetSegment segment in Parse(body))
        {
            switch (segment.Kind)
            {
                case ScriptSnippetSegmentKind.Literal:
                    text.Append(segment.Text);
                    break;

                case ScriptSnippetSegmentKind.Field:
                    defaults[segment.Number] = segment.Text;
                    text.Append(segment.Text);
                    break;

                case ScriptSnippetSegmentKind.Bound:
                    text.Append(defaults.TryGetValue(segment.Number, out string? first) ? first : segment.Text);
                    break;

                case ScriptSnippetSegmentKind.Caret:
                default:
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>Reads <c>${1:default}</c> or a bare <c>$1</c> starting at the dollar.</summary>
    private static bool TryReadField(string body, int dollar, out int number, out string defaultText, out int end)
    {
        number = 0;
        defaultText = string.Empty;
        end = dollar;

        int i = dollar + 1;
        bool braced = i < body.Length && body[i] == '{';

        if (braced)
        {
            i++;
        }

        int digits = i;
        while (i < body.Length && char.IsAsciiDigit(body[i]))
        {
            i++;
        }

        if (i == digits)
        {
            return false;
        }

        number = int.Parse(body[digits..i], System.Globalization.CultureInfo.InvariantCulture);

        if (!braced)
        {
            end = i - 1;
            return true;
        }

        if (i < body.Length && body[i] == ':')
        {
            int text = ++i;
            while (i < body.Length && body[i] != '}')
            {
                i++;
            }

            defaultText = body[text..i];
        }

        if (i >= body.Length || body[i] != '}')
        {
            return false;
        }

        end = i;
        return true;
    }
}
