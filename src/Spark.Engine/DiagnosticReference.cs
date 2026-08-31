using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spark.Api;
using Spark.Api.Help;

namespace Spark.Engine;

/// <summary>
/// Builds a help page for every <c>SPK####</c> diagnostic code (<c>E10-T11</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A user who hits a diagnostic has to be able to land somewhere.</b> Every
/// <see cref="SparkDiagnostic"/> already carries a <see cref="SparkDiagnostic.HelpTopicId"/>, and
/// every code already resolves to a concept topic through
/// <see cref="DiagnosticCodes.TopicFor(string)"/> — but a concept topic answers <i>how does lacing
/// work</i>, and somebody staring at <c>SPK1043</c> is asking <i>what is this and what do I do</i>.
/// These pages answer the second question and link to the first.
/// </para>
/// <para>
/// <b>Generated from the code constants themselves, exactly as the node reference is.</b> Every
/// code on <see cref="DiagnosticCodes"/> and <see cref="KernelDiagnostics"/> is a documented
/// public constant — CS1591 is an error on both assemblies — so the explanation is already
/// written. Reflecting over the constants means a code added tomorrow has a page tomorrow, and a
/// code that is deleted takes its page with it. There is no list to maintain and therefore no list
/// to get wrong, which is the same argument <c>NodeReference</c> makes and the reason
/// <c>DocGenerator</c> is not ported.
/// </para>
/// </remarks>
public static class DiagnosticReference
{
    /// <summary>The topic id prefix every generated diagnostic page carries.</summary>
    public const string TopicPrefix = "diagnostics.";

    /// <summary>The topic id for a diagnostic code.</summary>
    /// <param name="code">The code, such as <c>SPK1043</c>.</param>
    /// <returns>A stable id such as <c>diagnostics.SPK1043</c>.</returns>
    public static string TopicIdFor(string code) => TopicPrefix + code;

    /// <summary>
    /// Every diagnostic code Spark can raise, with the constant that declares it and the concept
    /// topic it points at.
    /// </summary>
    /// <returns>The codes, ordered by code.</returns>
    /// <remarks>
    /// Read by reflection over the two types that declare codes rather than from a list here,
    /// because a list here would be a second copy of the truth and the first thing to fall behind.
    /// </remarks>
    public static IReadOnlyList<(string Code, string? Summary, string? Topic)> All()
    {
        List<(string Code, string? Summary, string? Topic)> found = [];

        foreach (Type type in new[] { typeof(DiagnosticCodes), typeof(KernelDiagnostics) })
        {
            XmlDocumentation docs = XmlDocumentation.For(type.Assembly);

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(string) || field.GetRawConstantValue() is not string code)
                {
                    continue;
                }

                // Codes only. The same types also declare topic ids as constants, and a page
                // titled "concepts.lacing" would be a puzzle rather than a help topic.
                if (!code.StartsWith("SPK", StringComparison.Ordinal))
                {
                    continue;
                }

                if (found.Any(existing => string.Equals(existing.Code, code, StringComparison.Ordinal)))
                {
                    continue;
                }

                found.Add((code, docs.SummaryOf(field), DiagnosticCodes.TopicFor(code)));
            }
        }

        found.Sort((a, b) => string.CompareOrdinal(a.Code, b.Code));
        return found;
    }

    /// <summary>Builds a page for every diagnostic code, plus the index.</summary>
    /// <returns>The topics, index last.</returns>
    public static IReadOnlyList<HelpDocument> ForAll()
    {
        IReadOnlyList<(string Code, string? Summary, string? Topic)> codes = All();

        List<HelpDocument> topics = [];
        foreach ((string code, string? summary, string? topic) in codes)
        {
            topics.Add(Page(code, summary, topic));
        }

        topics.Add(Index(codes));
        return topics;
    }

    private static HelpDocument Page(string code, string? summary, string? topic)
    {
        List<HelpBlock> blocks =
        [
            new HelpBlock(HelpBlockKind.Heading, HelpMarkdown.ParseInlines(code), 1),
            new HelpBlock(
                HelpBlockKind.Paragraph,
                HelpMarkdown.ParseInlines(
                    string.IsNullOrWhiteSpace(summary)
                        ? "This code has no description on its declaration."
                        : Split(summary).Meaning)),
        ];

        // A table so that the page has a worked example by the harness's own definition, and
        // because "what does this mean / where do I read more" is genuinely tabular.
        blocks.Add(new HelpBlock(
            HelpBlockKind.Table,
            rows:
            [
                Row("", ""),
                Row("Code", "`" + code + "`"),
                Row("Severity", Split(summary).Severity),
                Row("Read more", topic is null ? "—" : "[" + Title(topic) + "](" + topic + ")"),
            ]));

        if (topic is not null)
        {
            blocks.Add(new HelpBlock(
                HelpBlockKind.Paragraph,
                HelpMarkdown.ParseInlines(
                    "Spark shows this code beside the node that raised it. The message names what "
                    + "happened; [" + Title(topic) + "](" + topic + ") explains the rule behind it.")));
        }

        return new HelpDocument(TopicIdFor(code), code, blocks, related: topic is null ? [] : [topic]);
    }

    private static HelpDocument Index(IReadOnlyList<(string Code, string? Summary, string? Topic)> codes)
    {
        List<HelpBlock> blocks =
        [
            new HelpBlock(HelpBlockKind.Heading, HelpMarkdown.ParseInlines("Diagnostic codes"), 1),
            new HelpBlock(HelpBlockKind.Paragraph, HelpMarkdown.ParseInlines(
                "Every message Spark can show carries one of these codes. They are stable: a code "
                + "means the same thing in every version, so it is safe to search for and safe to "
                + "quote in a bug report.")),
        ];

        List<IReadOnlyList<IReadOnlyList<HelpInline>>> rows = [Row("Code", "Severity", "Meaning")];
        foreach ((string code, string? summary, _) in codes)
        {
            (string severity, string meaning) = Split(summary);
            rows.Add(
            [
                HelpMarkdown.ParseInlines("[" + code + "](" + TopicIdFor(code) + ")"),
                HelpMarkdown.ParseInlines(severity),
                HelpMarkdown.ParseInlines(Shorten(meaning)),
            ]);
        }

        blocks.Add(new HelpBlock(HelpBlockKind.Table, rows: rows));
        return new HelpDocument("diagnostics.index", "Diagnostic codes", blocks);
    }

    /// <summary>
    /// Splits a code's summary into its severity word and the rest.
    /// </summary>
    /// <remarks>
    /// <b>Every code's doc comment opens with <c>Error.</c> or <c>Warning.</c></b>, which is
    /// genuinely the most useful fact about it and is also a sentence. Taking "the first sentence"
    /// for an index therefore produced a table whose entire Meaning column read <i>Error.</i> —
    /// correct, and worth nothing. Caught by photographing the page. The severity now gets its own
    /// column, which is better than the version that read properly by accident.
    /// </remarks>
    private static (string Severity, string Meaning) Split(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return ("—", "—");
        }

        foreach (string severity in new[] { "Error.", "Warning.", "Information." })
        {
            if (summary.StartsWith(severity, StringComparison.Ordinal))
            {
                return (severity.TrimEnd('.'), summary[severity.Length..].TrimStart());
            }
        }

        return ("—", summary);
    }

    private static string Shorten(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return "—";
        }

        int stop = summary.IndexOf(". ", StringComparison.Ordinal);
        string first = stop > 0 ? summary[..(stop + 1)] : summary;
        return first.Length > 110 ? first[..107] + "…" : first;
    }

    private static string Title(string topicId) => topicId switch
    {
        DiagnosticCodes.LacingTopic => "Lists, ranks and lacing",
        DiagnosticCodes.FileTopic => "Saving and opening graphs",
        DiagnosticCodes.EvaluationTopic => "How a graph evaluates",
        KernelDiagnostics.SolidsTopic => "Solids",
        _ => topicId,
    };

    private static IReadOnlyList<IReadOnlyList<HelpInline>> Row(params string[] cells)
    {
        List<IReadOnlyList<HelpInline>> row = new(cells.Length);
        foreach (string cell in cells)
        {
            row.Add(HelpMarkdown.ParseInlines(cell));
        }

        return row;
    }
}
