using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// Runs a node over its arguments, replicating where an argument arrived deeper than the port that
/// wanted it.
/// </summary>
/// <remarks>
/// <para>
/// This is a direct transcription of <c>docs/help/concepts/lacing.md</c> §2.15, which is the
/// specification rather than a description: it was written before this code existed, and where the
/// two disagree the document is right. Its case table is consumed as theory data by
/// <c>tests/Spark.Engine.Tests</c>.
/// </para>
/// <para>
/// The mechanism in one paragraph. For each input, <c>excess = rank(supplied) − declaredRank</c>,
/// and <c>depth</c> is the largest excess over the inputs that are allowed to replicate. At depth 0
/// the node is called once. Above it, the engine <b>replicates one level and recurses</b> — it never
/// flattens a nested list, computes over the flat form and reshapes. That is what makes ragged
/// input produce ragged output of exactly the same shape, and what makes a rank-6 input work with
/// no line of code that knows what 6 is.
/// </para>
/// </remarks>
public static class Replicator
{
    /// <summary>
    /// Evaluates a node over its arguments.
    /// </summary>
    /// <param name="definition">The node definition.</param>
    /// <param name="instanceLacing">
    /// The lacing stored on the node instance. <see cref="LacingMode.Auto"/> is resolved to the
    /// definition's default here, once, before anything else happens.
    /// </param>
    /// <param name="arguments">One graph value per input port, in port order.</param>
    /// <param name="cancellationToken">Checked between elements, so a runaway replication stops.</param>
    /// <returns>The outputs and diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> or <paramref name="arguments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="arguments"/> does not have one entry per input port.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static ReplicationResult Replicate(
        NodeDefinition definition,
        LacingMode instanceLacing,
        IReadOnlyList<object?> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count != definition.Inputs.Count)
        {
            throw new ArgumentException(
                $"Node '{definition.DisplayName}' has {definition.Inputs.Count} input ports but was given {arguments.Count} arguments.",
                nameof(arguments));
        }

        // Decision D4, and the only place Auto is ever mentioned again. Exactly one hop.
        LacingMode mode = definition.ResolveLacing(instanceLacing);

        Run run = new(definition, mode, cancellationToken);

        object?[] outputs;
        try
        {
            outputs = Level(run, [.. arguments], []);
        }
        catch (ReplicationErrorException error)
        {
            return ReplicationResult.Failure(error.Diagnostic);
        }
        catch (FastPathAbandoned)
        {
            // The happy path runs with no exception handling at all, so a graph where nothing fails
            // pays nothing for the isolation machinery. The first failure abandons that run and
            // replays it with catching enabled, at the cost of recomputing the elements before the
            // failure. See ADR-0012.
            run.RestartCatching();

            try
            {
                outputs = Level(run, [.. arguments], []);
            }
            catch (ReplicationErrorException error)
            {
                return ReplicationResult.Failure(error.Diagnostic);
            }
        }

        return ReplicationResult.Success(outputs, run.BuildDiagnostics());
    }

    private static object?[] Level(Run run, object?[] arguments, int[] path)
    {
        NodeDefinition definition = run.Definition;
        int inputCount = definition.Inputs.Count;

        int[] excess = new int[inputCount];
        List<int> replicating = [];
        int depth = 0;

        for (int index = 0; index < inputCount; index++)
        {
            PortDefinition port = definition.Inputs[index];

            // [KeepStructure] means the port has no rank that is wrong for it, so its excess is
            // zero by definition and it can never be a reason to replicate.
            excess[index] = port.KeepStructure
                ? 0
                : SparkList.RankOf(arguments[index]) - port.DeclaredRank;

            if (excess[index] > 0 && !port.NoReplication && !port.KeepStructure)
            {
                replicating.Add(index);
                if (excess[index] > depth)
                {
                    depth = excess[index];
                }
            }
        }

        if (run.Mode == LacingMode.Disabled || depth == 0)
        {
            return Leaf(run, arguments, excess, path);
        }

        if (run.Mode == LacingMode.CrossProduct)
        {
            IReadOnlyList<int> dimensions = definition.OrderDimensions(replicating);
            RefuseDuplicateGuides(definition, replicating);
            return Cross(run, arguments, path, dimensions, 0);
        }

        int iterations = IterationCount(run, arguments, replicating);

        List<object?[]> results = [];
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            run.CancellationToken.ThrowIfCancellationRequested();

            object?[] cell = (object?[])arguments.Clone();
            foreach (int index in replicating)
            {
                SparkList list = (SparkList)arguments[index]!;

                // Longest repeats the LAST element, never cycling: [1,5] extended to four is
                // [1,5,5,5]. Under Shortest, iteration is always below every length, so the same
                // clamp is a no-op.
                cell[index] = list[Math.Min(iteration, list.Count - 1)];
            }

            results.Add(Level(run, cell, Append(path, iteration)));
        }

        return Transpose(run, results, arguments, replicating, 1);
    }

    private static object?[] Cross(Run run, object?[] arguments, int[] path, IReadOnlyList<int> dimensions, int dimension)
    {
        if (dimension == dimensions.Count)
        {
            // Every dimension is bound. Recurse into the ordinary procedure, which is what makes
            // Cross Product compound through recursion rather than stopping at k levels.
            return Level(run, arguments, path);
        }

        int port = dimensions[dimension];
        SparkList list = (SparkList)arguments[port]!;

        List<object?[]> results = [];
        for (int index = 0; index < list.Count; index++)
        {
            run.CancellationToken.ThrowIfCancellationRequested();

            object?[] cell = (object?[])arguments.Clone();
            cell[port] = list[index];
            results.Add(Cross(run, cell, Append(path, index), dimensions, dimension + 1));
        }

        if (results.Count > 0)
        {
            return TransposeNonEmpty(run, results);
        }

        // An empty dimension contributes zero iterations, so the nested loops produce an empty
        // skeleton - but the skeleton still has the rank the full loop nest would have had.
        // Decision D8: emptiness does not erase shape.
        int[] ranks = RanksOf(arguments);
        for (int remaining = dimension; remaining < dimensions.Count; remaining++)
        {
            ranks[dimensions[remaining]]--;
        }

        int levelsHere = dimensions.Count - dimension;
        return EmptyOutputs(run, ranks, levelsHere);
    }

    private static object?[] Leaf(Run run, object?[] arguments, int[] excess, int[] path)
    {
        NodeDefinition definition = run.Definition;
        int inputCount = definition.Inputs.Count;
        object?[] clrArguments = new object?[inputCount];

        run.LeafCount++;

        try
        {
            for (int index = 0; index < inputCount; index++)
            {
                PortDefinition port = definition.Inputs[index];
                object? value = arguments[index];

                if (port.KeepStructure)
                {
                    clrArguments[index] = ValueMarshal.ToClr(value, port.ValueType);
                    continue;
                }

                if (excess[index] > 0 && port.NoReplication)
                {
                    // Structural, not value-dependent, so it is an error for the whole node even
                    // when it is discovered inside a replication. Decision D5: [NoReplication] is
                    // still type-checked, which is the entire difference from [KeepStructure].
                    throw new ReplicationErrorException(DiagnosticCodes.Create(
                        DiagnosticSeverity.Error,
                        DiagnosticCodes.ListIntoNoReplicationPort,
                        $"Port '{port.Name}' of '{definition.DisplayName}' cannot be laced, and was given a rank-{SparkList.RankOf(value)} list. Build the list of results a different way, or supply a single value.",
                        portIndex: index));
                }

                bool promoted = false;
                if (excess[index] < 0)
                {
                    // Decision D2: promotion happens here, at the leaf, not before replication.
                    value = ValueMarshal.Promote(value, -excess[index]);
                    promoted = true;
                }

                try
                {
                    clrArguments[index] = ValueMarshal.ToClr(value, port.ValueType);
                }
                catch (ValueMarshallingException marshalling)
                {
                    throw new PortMarshallingFailure(index, promoted, marshalling.Message, marshalling);
                }
            }

            object?[] produced = definition.Invoke(clrArguments);
            object?[] outputs = new object?[definition.Outputs.Count];

            for (int index = 0; index < outputs.Length; index++)
            {
                outputs[index] = ValueMarshal.FromClr(
                    index < produced.Length ? produced[index] : null,
                    definition.Outputs[index].DeclaredRank);
            }

            return outputs;
        }
        catch (PortMarshallingFailure failure) when (path.Length == 0)
        {
            PortDefinition port = definition.Inputs[failure.PortIndex];
            string code = failure.Promoted ? DiagnosticCodes.PromotionFailed : DiagnosticCodes.MarshallingFailed;
            string reason = failure.Promoted
                ? $"Port '{port.Name}' of '{definition.DisplayName}' wanted rank {port.DeclaredRank}, and the supplied value could not be promoted to it: {failure.Reason}"
                : $"Port '{port.Name}' of '{definition.DisplayName}' could not accept the supplied value: {failure.Reason}";

            throw new ReplicationErrorException(DiagnosticCodes.Create(
                DiagnosticSeverity.Error, code, reason, portIndex: failure.PortIndex));
        }
        catch (Exception exception) when (
            path.Length == 0
            && exception is not ReplicationErrorException
            && exception is not OperationCanceledException)
        {
            throw new ReplicationErrorException(DiagnosticCodes.Create(
                DiagnosticSeverity.Error,
                DiagnosticCodes.NodeThrewAtDepthZero,
                $"'{definition.DisplayName}' failed: {exception.Message}",
                detail: exception.ToString()));
        }
        catch (Exception exception) when (
            exception is not ReplicationErrorException
            && exception is not OperationCanceledException
            && exception is not FastPathAbandoned)
        {
            if (!run.Catching)
            {
                throw new FastPathAbandoned();
            }

            run.RecordFailure(path, MessageOf(exception));
            return new object?[definition.Outputs.Count];
        }
    }

    private static int IterationCount(Run run, object?[] arguments, List<int> replicating)
    {
        int shortest = int.MaxValue;
        int longest = 0;
        bool anyEmpty = false;
        bool anyNonEmpty = false;

        foreach (int index in replicating)
        {
            int count = ((SparkList)arguments[index]!).Count;
            shortest = Math.Min(shortest, count);
            longest = Math.Max(longest, count);

            if (count == 0)
            {
                anyEmpty = true;
            }
            else
            {
                anyNonEmpty = true;
            }
        }

        if (run.Mode == LacingMode.Shortest)
        {
            // min = 0 is exactly what the user asked for, so Shortest says nothing about it.
            return shortest;
        }

        // Decision D7. max would say "pad to the longest", but an empty input has no last element
        // to repeat, so emptiness propagates instead. Because max would have suggested otherwise,
        // the mixed case is worth a word.
        if (anyEmpty)
        {
            if (anyNonEmpty)
            {
                run.WarnLongestEmptyPropagated();
            }

            return 0;
        }

        return longest;
    }

    private static void RefuseDuplicateGuides(NodeDefinition definition, List<int> replicating)
    {
        HashSet<int> seen = [];
        foreach (int index in replicating)
        {
            int guide = definition.Inputs[index].ReplicationGuide ?? index;
            if (!seen.Add(guide))
            {
                throw new ReplicationErrorException(DiagnosticCodes.Create(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.DuplicateReplicationGuide,
                    $"Two replicating ports of '{definition.DisplayName}' claim Cross Product dimension {guide.ToString(CultureInfo.InvariantCulture)}. The nesting order of the result is what Cross Product is for, so it cannot be decided by a tie-break.",
                    portIndex: index));
            }
        }
    }

    private static object?[] Transpose(Run run, List<object?[]> results, object?[] arguments, List<int> replicating, int levelsHere)
    {
        if (results.Count > 0)
        {
            return TransposeNonEmpty(run, results);
        }

        int[] ranks = RanksOf(arguments);
        foreach (int index in replicating)
        {
            ranks[index]--;
        }

        return EmptyOutputs(run, ranks, levelsHere);
    }

    private static object?[] TransposeNonEmpty(Run run, List<object?[]> results)
    {
        // A multi-output node replicates ONCE and is transposed on the way out, so each port
        // carries a list of that output's values - never one port carrying a list of tuples.
        int outputCount = run.Definition.Outputs.Count;
        object?[] transposed = new object?[outputCount];

        for (int output = 0; output < outputCount; output++)
        {
            object?[] items = new object?[results.Count];
            int deepest = 0;

            for (int index = 0; index < results.Count; index++)
            {
                object? item = results[index][output];
                items[index] = item;

                int rank = SparkList.RankOf(item);
                if (rank > deepest)
                {
                    deepest = rank;
                }
            }

            transposed[output] = new SparkList(items, deepest + 1);
        }

        return transposed;
    }

    private static object?[] EmptyOutputs(Run run, int[] childRanks, int levelsHere)
    {
        int outputCount = run.Definition.Outputs.Count;
        object?[] outputs = new object?[outputCount];

        for (int output = 0; output < outputCount; output++)
        {
            outputs[output] = SparkList.Empty(levelsHere + PredictRank(run, childRanks, output));
        }

        return outputs;
    }

    /// <summary>
    /// The rank the result would have had, computed from ranks alone with no values involved.
    /// </summary>
    /// <remarks>
    /// This exists only for empty results. Everywhere else the rank comes from what was actually
    /// produced, which is what makes ragged output ragged; but an empty list has no contents to
    /// read a rank off, and decision D8 says it still has one. This walks the same recursion the
    /// real procedure walks, over ranks instead of values.
    /// </remarks>
    private static int PredictRank(Run run, int[] ranks, int outputIndex)
    {
        NodeDefinition definition = run.Definition;
        List<int> replicating = [];
        int depth = 0;

        for (int index = 0; index < ranks.Length; index++)
        {
            PortDefinition port = definition.Inputs[index];
            if (port.KeepStructure || port.NoReplication)
            {
                continue;
            }

            int excess = ranks[index] - port.DeclaredRank;
            if (excess > 0)
            {
                replicating.Add(index);
                depth = Math.Max(depth, excess);
            }
        }

        if (run.Mode == LacingMode.Disabled || depth == 0)
        {
            return definition.Outputs[outputIndex].DeclaredRank;
        }

        int[] childRanks = (int[])ranks.Clone();
        foreach (int index in replicating)
        {
            childRanks[index]--;
        }

        int levels = run.Mode == LacingMode.CrossProduct ? replicating.Count : 1;
        return levels + PredictRank(run, childRanks, outputIndex);
    }

    private static int[] RanksOf(object?[] arguments)
    {
        int[] ranks = new int[arguments.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            ranks[index] = SparkList.RankOf(arguments[index]);
        }

        return ranks;
    }

    private static int[] Append(int[] path, int index)
    {
        int[] extended = new int[path.Length + 1];
        Array.Copy(path, extended, path.Length);
        extended[path.Length] = index;
        return extended;
    }

    private static string MessageOf(Exception exception) =>
        exception is PortMarshallingFailure failure ? failure.Reason : exception.Message;

    private sealed class Run
    {
        private readonly List<SparkDiagnostic> _diagnostics = [];
        private readonly List<(int[] Path, string Message)> _failures = [];
        private bool _warnedEmptyPropagated;

        internal Run(NodeDefinition definition, LacingMode mode, CancellationToken cancellationToken)
        {
            Definition = definition;
            Mode = mode;
            CancellationToken = cancellationToken;
        }

        internal NodeDefinition Definition { get; }

        internal LacingMode Mode { get; }

        internal CancellationToken CancellationToken { get; }

        internal bool Catching { get; private set; }

        internal int LeafCount { get; set; }

        internal void RestartCatching()
        {
            Catching = true;
            LeafCount = 0;
            _failures.Clear();
            _diagnostics.Clear();
            _warnedEmptyPropagated = false;
        }

        internal void RecordFailure(int[] path, string message) => _failures.Add((path, message));

        internal void WarnLongestEmptyPropagated()
        {
            if (_warnedEmptyPropagated)
            {
                return;
            }

            _warnedEmptyPropagated = true;
            _diagnostics.Add(DiagnosticCodes.Create(
                DiagnosticSeverity.Warning,
                DiagnosticCodes.LongestEmptyPropagated,
                $"'{Definition.DisplayName}' was given an empty list alongside a non-empty one under Longest, so the result is empty. Longest repeats a short input's last element, and an empty list has none."));
        }

        internal IReadOnlyList<SparkDiagnostic> BuildDiagnostics()
        {
            if (_failures.Count == 0)
            {
                return _diagnostics;
            }

            (int[] path, string message) = _failures[0];
            List<SparkDiagnostic> all = [.. _diagnostics];

            all.Add(new SparkDiagnostic(
                DiagnosticSeverity.Warning,
                DiagnosticCodes.ElementsFailed,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} of {1} elements failed; first at {2}: {3}",
                    _failures.Count,
                    LeafCount,
                    SparkDiagnostic.FormatElementPath(path),
                    message),
                DiagnosticCodes.TopicFor(DiagnosticCodes.ElementsFailed),
                elementPath: path));

            return all;
        }
    }
}

/// <summary>
/// Carries a typed diagnostic out of a replication as an error for the whole node.
/// </summary>
internal sealed class ReplicationErrorException : Exception
{
    internal ReplicationErrorException(SparkDiagnostic diagnostic)
        : base(diagnostic.Message) => Diagnostic = diagnostic;

    internal SparkDiagnostic Diagnostic { get; }
}

/// <summary>
/// Signals that the uncaught fast path hit its first failure and the level must be replayed with
/// per-element catching enabled.
/// </summary>
internal sealed class FastPathAbandoned : Exception
{
}

/// <summary>
/// A value could not be marshalled into a port's declared type. Carries which port, and whether
/// promotion had been applied, because those choose between two different diagnostic codes.
/// </summary>
internal sealed class PortMarshallingFailure : Exception
{
    internal PortMarshallingFailure(int portIndex, bool promoted, string reason, Exception innerException)
        : base(reason, innerException)
    {
        PortIndex = portIndex;
        Promoted = promoted;
        Reason = reason;
    }

    internal int PortIndex { get; }

    internal bool Promoted { get; }

    internal string Reason { get; }
}
