namespace Spark.Api;

/// <summary>
/// How badly a <see cref="SparkDiagnostic"/> went wrong, and therefore what happens to the
/// rest of the graph.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// Something worth saying that changed nothing. The node produced its output normally.
    /// </summary>
    Information = 0,

    /// <summary>
    /// Output with caveats. The node produced a value and everything downstream still
    /// evaluates — a per-element replication failure is the canonical case.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// No output. The node produced nothing, and everything downstream of it is marked
    /// <i>not evaluated</i> rather than being given errors of its own. Cascading would turn a
    /// one-node problem into a fifty-error wall that hides the cause.
    /// </summary>
    Error = 2,
}
