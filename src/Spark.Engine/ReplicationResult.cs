using System;
using System.Collections.Generic;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// What one node invocation produced: a value for every output port, and everything the engine has
/// to say about it.
/// </summary>
/// <remarks>
/// <see cref="HasOutput"/> and the severity of the diagnostics are two different questions.
/// A warning means output with caveats and downstream still evaluates; only an error means no
/// output, and downstream of an error is marked <i>not evaluated</i> rather than given errors of
/// its own.
/// </remarks>
public sealed class ReplicationResult
{
    private ReplicationResult(bool hasOutput, IReadOnlyList<object?> outputs, IReadOnlyList<SparkDiagnostic> diagnostics)
    {
        HasOutput = hasOutput;
        Outputs = outputs;
        Diagnostics = diagnostics;
    }

    /// <summary>Whether the node produced values at all.</summary>
    public bool HasOutput { get; }

    /// <summary>
    /// One value per output port, in port order. Every port has the same shape and the same rank as
    /// every other, because a multi-output node replicates once and transposes on the way out.
    /// Empty when <see cref="HasOutput"/> is <see langword="false"/>.
    /// </summary>
    public IReadOnlyList<object?> Outputs { get; }

    /// <summary>Everything the engine has to say about this invocation.</summary>
    public IReadOnlyList<SparkDiagnostic> Diagnostics { get; }

    /// <summary>Creates a successful result.</summary>
    /// <param name="outputs">One value per output port.</param>
    /// <param name="diagnostics">Warnings and information, if any.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="outputs"/> is <see langword="null"/>.</exception>
    public static ReplicationResult Success(IReadOnlyList<object?> outputs, IReadOnlyList<SparkDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        return new ReplicationResult(true, outputs, diagnostics ?? []);
    }

    /// <summary>Creates a failed result, which carries no output.</summary>
    /// <param name="diagnostic">The error.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="diagnostic"/> is <see langword="null"/>.</exception>
    public static ReplicationResult Failure(SparkDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return new ReplicationResult(false, [], [diagnostic]);
    }
}
