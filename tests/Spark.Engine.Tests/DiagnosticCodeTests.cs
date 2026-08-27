using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The <c>SPK####</c> code space: well formed, unique, and every one of them reachable from a help
/// topic.
/// </summary>
public sealed class DiagnosticCodeTests
{
    private static readonly FieldInfo[] CodeFields = [.. typeof(DiagnosticCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral
            && field.FieldType == typeof(string)
            && ((string)field.GetRawConstantValue()!).StartsWith("SPK", StringComparison.Ordinal))];

    /// <summary>
    /// Every code declared on <see cref="DiagnosticCodes"/> resolves to a help topic.
    /// </summary>
    /// <remarks>
    /// This is the check that makes "every code has a topic" true rather than aspirational. Adding a
    /// constant and forgetting to register its topic is a red build, which is the only reliable way
    /// to keep a mapping like this honest — the alternative is the hand-maintained dictionary that
    /// rotted in <c>DoodleSharp</c>'s help generator until 101 of 108 entries rendered blank.
    /// </remarks>
    [Fact]
    public void EveryDeclaredCodeResolvesToAHelpTopic()
    {
        Assert.NotEmpty(CodeFields);

        List<string> missing = [];
        foreach (FieldInfo field in CodeFields)
        {
            string code = (string)field.GetRawConstantValue()!;
            if (string.IsNullOrWhiteSpace(DiagnosticCodes.TopicFor(code)))
            {
                missing.Add($"{field.Name} ({code})");
            }
        }

        Assert.True(missing.Count == 0, $"Codes with no help topic: {string.Join(", ", missing)}.");
    }

    /// <summary>Codes are stable and never reused, so two constants must never share one.</summary>
    [Fact]
    public void NoTwoConstantsShareACode()
    {
        string[] codes = [.. CodeFields.Select(field => (string)field.GetRawConstantValue()!)];

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A code that is not of the form <c>SPK####</c> is refused when a diagnostic is built.</summary>
    [Fact]
    public void AMalformedCodeIsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            new SparkDiagnostic(DiagnosticSeverity.Error, "SPK99", "message"));

        Assert.Throws<ArgumentException>(() =>
            new SparkDiagnostic(DiagnosticSeverity.Error, "ERR1040", "message"));
    }

    /// <summary>The index path is rendered as the specification writes it, outermost index first.</summary>
    [Fact]
    public void AnElementPathIsRenderedOutermostIndexFirst()
    {
        SparkDiagnostic diagnostic = new(
            DiagnosticSeverity.Warning, "SPK1042", "message", elementPath: [3, 1]);

        Assert.Equal("[3][1]", diagnostic.ElementPathText());
        Assert.Equal(string.Empty, SparkDiagnostic.FormatElementPath([]));
    }
}
