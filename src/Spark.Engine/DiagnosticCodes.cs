using System.Collections.Generic;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// The <c>SPK####</c> codes this assembly raises, and the help topic each of them resolves to.
/// </summary>
/// <remarks>
/// <para>
/// Codes are stable and are never reused. A withdrawn code leaves a gap rather than being recycled,
/// because a user searching for the code they saw last year must not land on a different problem.
/// </para>
/// <para>
/// The 1040 block is specified in <c>docs/help/concepts/lacing.md</c> §7, which is the authority for
/// those seven codes. The 1010 block belongs to wire validation and graph structure.
/// </para>
/// </remarks>
public static class DiagnosticCodes
{
    /// <summary>Help topic for everything in the replication block.</summary>
    public const string LacingTopic = "concepts.lacing";

    /// <summary>Help topic for evaluation, wiring and graph structure.</summary>
    public const string EvaluationTopic = "concepts.evaluation";

    /// <summary>Error. Two ports cannot be connected: no rule in the compatibility order matched.</summary>
    public const string IncompatiblePortTypes = "SPK1010";

    /// <summary>
    /// Error. Two ports name types with the same full name from different assemblies. Refused at
    /// design time so that it never becomes a runtime <i>cannot cast Foo to Foo</i>.
    /// </summary>
    public const string SameNameDifferentAssembly = "SPK1011";

    /// <summary>Error. The wire was refused because it would close a cycle.</summary>
    public const string WireWouldCloseCycle = "SPK1012";

    /// <summary>Warning. The connection is accepted through a conversion that may lose information.</summary>
    public const string LossyConversion = "SPK1013";

    /// <summary>Error. The node is part of a cycle found when the graph was loaded, so it cannot evaluate.</summary>
    public const string NodeInCycle = "SPK1014";

    /// <summary>Error. A value could not be promoted to the port's declared rank and type.</summary>
    public const string PromotionFailed = "SPK1040";

    /// <summary>
    /// Error. A value could not be marshalled into the port's declared type — usually a rank that
    /// replication was not permitted to reduce.
    /// </summary>
    public const string MarshallingFailed = "SPK1041";

    /// <summary>
    /// Warning. Some elements failed during replication. Names the failed count, the total, and the
    /// index path and message of the first failure.
    /// </summary>
    public const string ElementsFailed = "SPK1042";

    /// <summary>Error. A list was supplied to a <c>[NoReplication]</c> port.</summary>
    public const string ListIntoNoReplicationPort = "SPK1043";

    /// <summary>Error. Two replicating ports declared the same <c>[ReplicationGuide]</c> value.</summary>
    public const string DuplicateReplicationGuide = "SPK1044";

    /// <summary>
    /// Warning. Under Longest, some replicating inputs were empty and others were not, so the
    /// result is empty rather than padded.
    /// </summary>
    public const string LongestEmptyPropagated = "SPK1045";

    /// <summary>
    /// Error. The node threw at replication depth 0, so there was no per-element isolation to fall
    /// back on and there is no output.
    /// </summary>
    public const string NodeThrewAtDepthZero = "SPK1046";

    private static readonly Dictionary<string, string> Topics = new(System.StringComparer.Ordinal)
    {
        [IncompatiblePortTypes] = EvaluationTopic,
        [SameNameDifferentAssembly] = EvaluationTopic,
        [WireWouldCloseCycle] = EvaluationTopic,
        [LossyConversion] = EvaluationTopic,
        [NodeInCycle] = EvaluationTopic,
        [PromotionFailed] = LacingTopic,
        [MarshallingFailed] = LacingTopic,
        [ElementsFailed] = LacingTopic,
        [ListIntoNoReplicationPort] = LacingTopic,
        [DuplicateReplicationGuide] = LacingTopic,
        [LongestEmptyPropagated] = LacingTopic,
        [NodeThrewAtDepthZero] = LacingTopic,
        [MalformedGraphFile] = FileTopic,
        [UnreadableFormatVersion] = FileTopic,
        [UnknownNodeDefinition] = FileTopic,
        [UnwritableLiteral] = FileTopic,
    };

    /// <summary>Every code this assembly can raise.</summary>
    public static IReadOnlyCollection<string> All => Topics.Keys;

    /// <summary>The help topic a code resolves to.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The topic id, or <see langword="null"/> when the code is not one of ours.</returns>
    public static string? TopicFor(string code) => Topics.TryGetValue(code, out string? topic) ? topic : null;

    /// <summary>
    /// Creates a diagnostic with the help topic already resolved, so that no call site can raise a
    /// code with the wrong topic or with none.
    /// </summary>
    /// <param name="severity">The severity.</param>
    /// <param name="code">One of the codes on this type.</param>
    /// <param name="message">The message.</param>
    /// <param name="detail">Supporting text. Optional.</param>
    /// <param name="portIndex">The port the diagnostic is about. Optional.</param>
    /// <param name="elementPath">The index path into the value. Optional.</param>
    /// <returns>The diagnostic, not yet attached to a node.</returns>
    public static SparkDiagnostic Create(
        DiagnosticSeverity severity,
        string code,
        string message,
        string? detail = null,
        int? portIndex = null,
        IReadOnlyList<int>? elementPath = null) =>
        new(severity, code, message, TopicFor(code), detail, null, portIndex, elementPath);

    /// <summary>
    /// The help topic for anything about saving and opening a graph.
    /// </summary>
    public const string FileTopic = "concepts.files";

    /// <summary>
    /// A `.spark` file is not valid JSON, is not a graph, or is missing something it must have.
    /// </summary>
    public const string MalformedGraphFile = "SPK1060";

    /// <summary>
    /// A `.spark` file names a format version newer than this build can read. Refused whole rather
    /// than partly read, because guessing at an unknown format is how a graph silently loses work.
    /// </summary>
    public const string UnreadableFormatVersion = "SPK1061";

    /// <summary>
    /// A `.spark` file names a node definition that is not loaded — a package that is not
    /// installed, or a node that has been renamed since the graph was saved.
    /// </summary>
    public const string UnknownNodeDefinition = "SPK1062";

    /// <summary>
    /// A port holds a value no `.spark` file can represent, so saving would lose it. Refused at
    /// save time, where the user still has the value, rather than at load time when it is gone.
    /// </summary>
    public const string UnwritableLiteral = "SPK1063";
}
