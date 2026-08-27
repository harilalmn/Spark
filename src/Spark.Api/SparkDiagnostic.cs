using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Spark.Api;

/// <summary>
/// One thing the engine has to say about one node: a severity, a stable <c>SPK####</c> code, a
/// message, and enough identity to point at the exact place it happened.
/// </summary>
/// <remarks>
/// <para>
/// The identity triple <c>(NodeId, PortIndex, ElementPath)</c> is the same key the viewport,
/// the selection model and the watch panel use, which is why a diagnostic can be clicked
/// through to the element that caused it. <see cref="ElementPath"/> is the full index path, so
/// a failure inside a nested result reports <c>[3][1]</c> rather than <c>4</c>.
/// </para>
/// <para>
/// Every code carries a <see cref="HelpTopicId"/>. A diagnostic that cannot be looked up is a
/// diagnostic that gets screenshotted into an issue instead of being fixed by the person who
/// hit it.
/// </para>
/// </remarks>
public sealed class SparkDiagnostic
{
    private static readonly int[] NoPath = [];

    private readonly int[] _elementPath;

    /// <summary>
    /// Creates a diagnostic.
    /// </summary>
    /// <param name="severity">Whether the node still produced output.</param>
    /// <param name="code">
    /// The stable code, of the form <c>SPK</c> followed by exactly four digits. Codes are never
    /// reused and each one has a help topic.
    /// </param>
    /// <param name="message">
    /// A single sentence a user can act on. Say what happened and what would fix it, not what
    /// the code was doing at the time.
    /// </param>
    /// <param name="helpTopicId">The help topic that explains this code, for example <c>concepts.lacing</c>.</param>
    /// <param name="detail">Supporting text, shown when the user asks for it. Optional.</param>
    /// <param name="nodeId">The node the diagnostic belongs to, if it is known yet.</param>
    /// <param name="portIndex">The port index the diagnostic belongs to, if it is about one port.</param>
    /// <param name="elementPath">
    /// The full index path into the value, outermost index first. Copied on construction.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not of the form <c>SPK####</c>.</exception>
    public SparkDiagnostic(
        DiagnosticSeverity severity,
        string code,
        string message,
        string? helpTopicId = null,
        string? detail = null,
        Guid? nodeId = null,
        int? portIndex = null,
        IReadOnlyList<int>? elementPath = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);

        if (!IsWellFormedCode(code))
        {
            throw new ArgumentException($"'{code}' is not a diagnostic code of the form SPK####.", nameof(code));
        }

        Severity = severity;
        Code = code;
        Message = message;
        HelpTopicId = helpTopicId;
        Detail = detail;
        NodeId = nodeId;
        PortIndex = portIndex;
        _elementPath = elementPath is null || elementPath.Count == 0 ? NoPath : [.. elementPath];
    }

    /// <summary>Whether the node still produced output.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>The stable <c>SPK####</c> code.</summary>
    public string Code { get; }

    /// <summary>A single sentence the user can act on.</summary>
    public string Message { get; }

    /// <summary>Supporting text, or <see langword="null"/>.</summary>
    public string? Detail { get; }

    /// <summary>The node this diagnostic belongs to, or <see langword="null"/> if it is not yet attached to one.</summary>
    public Guid? NodeId { get; }

    /// <summary>The port index this diagnostic is about, or <see langword="null"/> if it is about the whole node.</summary>
    public int? PortIndex { get; }

    /// <summary>
    /// The full index path into the value, outermost index first. Empty when the diagnostic is
    /// about the node rather than about one element of its output.
    /// </summary>
    public IReadOnlyList<int> ElementPath => _elementPath;

    /// <summary>The help topic that explains this code, or <see langword="null"/>.</summary>
    public string? HelpTopicId { get; }

    /// <summary>
    /// Returns a copy of this diagnostic attached to a node, for use when a diagnostic is
    /// raised by code that does not know which node instance it is running for.
    /// </summary>
    /// <param name="nodeId">The node identity.</param>
    /// <param name="portIndex">The port index, if the diagnostic is about one port.</param>
    /// <returns>A new diagnostic; this one is unchanged.</returns>
    public SparkDiagnostic WithNode(Guid nodeId, int? portIndex = null) =>
        new(Severity, Code, Message, HelpTopicId, Detail, nodeId, portIndex ?? PortIndex, _elementPath);

    /// <summary>
    /// Renders the index path in the bracket notation the help topics use, for example
    /// <c>[3][1]</c>. Returns an empty string when there is no path.
    /// </summary>
    /// <returns>The rendered path.</returns>
    public string ElementPathText() => FormatElementPath(_elementPath);

    /// <summary>
    /// Renders an index path in the bracket notation the help topics use, for example
    /// <c>[3][1]</c>.
    /// </summary>
    /// <param name="elementPath">The path, outermost index first.</param>
    /// <returns>The rendered path, or an empty string when the path is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="elementPath"/> is <see langword="null"/>.</exception>
    public static string FormatElementPath(IReadOnlyList<int> elementPath)
    {
        ArgumentNullException.ThrowIfNull(elementPath);

        if (elementPath.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        foreach (int index in elementPath)
        {
            builder.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        return builder.ToString();
    }

    /// <summary>Renders the diagnostic as <c>Severity SPK####: message</c>.</summary>
    /// <returns>The rendered diagnostic.</returns>
    public override string ToString()
    {
        string path = ElementPathText();
        return path.Length == 0
            ? $"{Severity} {Code}: {Message}"
            : $"{Severity} {Code} at {path}: {Message}";
    }

    private static bool IsWellFormedCode(string code)
    {
        if (code.Length != 7 || !code.StartsWith("SPK", StringComparison.Ordinal))
        {
            return false;
        }

        for (int index = 3; index < 7; index++)
        {
            if (!char.IsAsciiDigit(code[index]))
            {
                return false;
            }
        }

        return true;
    }
}
