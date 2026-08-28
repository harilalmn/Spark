using System;
using System.Collections.Generic;
using Spark.Api;

namespace Spark.Scripting;

/// <summary>
/// The <c>SPK17##</c> codes <c>Spark.Scripting</c> raises, and the help topic each resolves to.
/// </summary>
/// <remarks>
/// Codes are stable and never reused. A withdrawn code leaves a gap, because a user searching for
/// the code they saw last year must not land on a different problem.
/// </remarks>
public static class ScriptDiagnosticCodes
{
    /// <summary>Help topic for everything a code block can say.</summary>
    public const string CodeBlockTopic = "concepts.code-block";

    /// <summary>Error. The C# in a code block did not compile. The message is the compiler's own.</summary>
    public const string CompilerError = "SPK1700";

    /// <summary>Warning. The C# compiler warned about something in a code block.</summary>
    public const string CompilerWarning = "SPK1701";

    /// <summary>Error. An <c>// in:</c> port declaration could not be read.</summary>
    public const string MalformedInputDirective = "SPK1702";

    /// <summary>Error. The code block was stopped: its time budget ran out, or the run was cancelled.</summary>
    public const string ScriptStopped = "SPK1703";

    /// <summary>
    /// Warning. Two <c>return</c> statements named different tuple elements, so the output ports come
    /// from the first one.
    /// </summary>
    public const string InconsistentTupleNames = "SPK1704";

    /// <summary>Error. A code block threw.</summary>
    public const string ScriptThrew = "SPK1705";

    private static readonly Dictionary<string, string> Topics = new(StringComparer.Ordinal)
    {
        [CompilerError] = CodeBlockTopic,
        [CompilerWarning] = CodeBlockTopic,
        [MalformedInputDirective] = CodeBlockTopic,
        [ScriptStopped] = CodeBlockTopic,
        [InconsistentTupleNames] = CodeBlockTopic,
        [ScriptThrew] = CodeBlockTopic,
    };

    /// <summary>Every code this assembly can raise.</summary>
    public static IReadOnlyCollection<string> All => Topics.Keys;

    /// <summary>The help topic a code resolves to.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The topic id, or <see langword="null"/> when the code is not one of ours.</returns>
    public static string? TopicFor(string code) => Topics.TryGetValue(code, out string? topic) ? topic : null;
}

/// <summary>
/// One thing to say about one code block, positioned in <b>the text the user typed</b> rather than in
/// the compilation unit the rewriter generated from it.
/// </summary>
/// <remarks>
/// The translation from generated position to user position runs through the <see cref="SourceMap"/>,
/// which is why <see cref="Line"/> is the line the user can see and not the line Roslyn reported. A
/// diagnostic on the wrong line is worse than no diagnostic, because it sends the reader to look at
/// code that is fine.
/// </remarks>
public sealed class ScriptDiagnostic
{
    /// <summary>Creates a diagnostic.</summary>
    /// <param name="severity">Whether the code block still produced anything.</param>
    /// <param name="code">The stable <c>SPK####</c> code.</param>
    /// <param name="message">What happened, phrased for the person who wrote the script.</param>
    /// <param name="compilerId">The underlying compiler id such as <c>CS0103</c>, when there is one.</param>
    /// <param name="line">The one-based line in the user's own text, or <c>0</c> when it maps nowhere.</param>
    /// <param name="column">The one-based column in the user's own text, or <c>0</c>.</param>
    /// <param name="start">The offset in the user's own text, or <c>-1</c> when it maps nowhere.</param>
    /// <param name="length">The length of the span in the user's own text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    public ScriptDiagnostic(
        DiagnosticSeverity severity,
        string code,
        string message,
        string? compilerId = null,
        int line = 0,
        int column = 0,
        int start = -1,
        int length = 0)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);

        Severity = severity;
        Code = code;
        Message = message;
        CompilerId = compilerId;
        Line = line;
        Column = column;
        Start = start;
        Length = length;
    }

    /// <summary>Whether the code block still produced anything.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>The stable <c>SPK####</c> code.</summary>
    public string Code { get; }

    /// <summary>What happened.</summary>
    public string Message { get; }

    /// <summary>The underlying compiler id such as <c>CS0103</c>, or <see langword="null"/>.</summary>
    public string? CompilerId { get; }

    /// <summary>The one-based line in the user's own text, or <c>0</c> when the position maps nowhere.</summary>
    public int Line { get; }

    /// <summary>The one-based column in the user's own text, or <c>0</c>.</summary>
    public int Column { get; }

    /// <summary>The offset in the user's own text, or <c>-1</c> when the position maps nowhere.</summary>
    public int Start { get; }

    /// <summary>The length of the span in the user's own text.</summary>
    public int Length { get; }

    /// <summary>Renders this as an engine diagnostic, so it can appear beside every other node's.</summary>
    /// <param name="portIndex">The port the diagnostic is about, if it is about one port.</param>
    /// <returns>The engine diagnostic, not yet attached to a node.</returns>
    public SparkDiagnostic ToSparkDiagnostic(int? portIndex = null) =>
        new(Severity,
            Code,
            Line > 0 ? $"Line {Line}: {Message}" : Message,
            ScriptDiagnosticCodes.TopicFor(Code),
            CompilerId,
            null,
            portIndex);

    /// <inheritdoc/>
    public override string ToString() =>
        Line > 0
            ? $"{Severity} {Code} ({CompilerId ?? "-"}) at line {Line}, column {Column}: {Message}"
            : $"{Severity} {Code} ({CompilerId ?? "-"}): {Message}";
}
