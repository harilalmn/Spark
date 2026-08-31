using System;
using System.Collections.Generic;
using System.Globalization;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// What a graph gets in place of a node whose package is not installed (<c>E7-T6</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The promise this exists to keep is that nobody's graph is ever damaged by opening it on a
/// machine without a package.</b> Refusing to open is safe but useless — the user cannot see what
/// they have, cannot find out which package they need, and cannot work on the rest of the graph.
/// Opening with the node dropped is far worse: the file re-saves without it, and a wire the user
/// spent an afternoon on is gone with no record that it ever existed.
/// </para>
/// <para>
/// A placeholder is therefore <b>a faithful carrier, not a repair</b>. It keeps the original
/// <see cref="NodeKey"/>, so the file re-saves naming the same node. It exposes as many ports as
/// the file actually uses, so every literal and every wire attaches exactly where it did. It
/// refuses to evaluate, naming the missing package, so the graph is honest about what it cannot
/// do rather than quietly producing nulls.
/// </para>
/// <para>
/// <b>Port counts are inferred from the file, not guessed.</b> The definition is not available —
/// that is the whole situation — so the only evidence of the node's shape is how the graph uses
/// it: the highest literal index and the highest wire index on each side. A placeholder built
/// this way is exactly wide enough to hold what is there, which is the precise condition for a
/// byte-identical re-save (<c>E7-T7</c>).
/// </para>
/// </remarks>
public static class PlaceholderNode
{
    /// <summary>
    /// The category placeholders are filed under, so the canvas and the library can style them
    /// distinctly without inspecting anything else.
    /// </summary>
    public const string Category = "Missing";

    /// <summary>
    /// Whether a definition is a placeholder standing in for an uninstalled package.
    /// </summary>
    /// <param name="definition">The definition to test. Null returns false.</param>
    /// <returns>True when this node cannot run because its package is absent.</returns>
    /// <remarks>
    /// Tested by category rather than by a type check, because a placeholder has to be an ordinary
    /// <see cref="NodeDefinition"/> — anything else would need every consumer of a definition to
    /// learn about a second kind, which is a change with no end to it.
    /// </remarks>
    public static bool IsPlaceholder(NodeDefinition? definition) =>
        definition is not null
        && string.Equals(definition.Category, Category, StringComparison.Ordinal);

    /// <summary>
    /// Builds a placeholder for a node the library does not have.
    /// </summary>
    /// <param name="key">The original node key, preserved verbatim so the file re-saves unchanged.</param>
    /// <param name="inputCount">
    /// How many input ports the file uses. Zero is allowed; a node may take everything from
    /// literals it does not have, or take nothing at all.
    /// </param>
    /// <param name="outputCount">
    /// How many output ports the file uses. Raised to one when zero, because
    /// <see cref="NodeDefinition"/> requires an output and a node with none could not be wired
    /// downstream even to say it failed.
    /// </param>
    /// <returns>The placeholder definition.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either count is negative.</exception>
    public static NodeDefinition For(NodeKey key, int inputCount, int outputCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputCount);
        ArgumentOutOfRangeException.ThrowIfNegative(outputCount);

        List<PortDefinition> inputs = new(inputCount);
        for (int index = 0; index < inputCount; index++)
        {
            inputs.Add(Port("in" + index.ToString(CultureInfo.InvariantCulture)));
        }

        List<PortDefinition> outputs = new(Math.Max(1, outputCount));
        for (int index = 0; index < Math.Max(1, outputCount); index++)
        {
            outputs.Add(Port("out" + index.ToString(CultureInfo.InvariantCulture)));
        }

        string package = string.IsNullOrEmpty(key.Package) ? "(unknown)" : key.Package;

        return new NodeDefinition(
            key,
            key.Name,
            inputs,
            outputs,
            _ => throw new MissingPackageException(key),
            defaultLacing: LacingMode.Longest,
            description: $"This node comes from '{package}', which is not installed. Its inputs, "
                + "its values and its wires have been kept exactly as they were saved; install the "
                + "package and reopen the graph to bring it back.",
            category: Category);
    }

    /// <summary>
    /// The port every placeholder port is.
    /// </summary>
    /// <remarks>
    /// <c>object</c> so any wire attaches. A <b>null default</b> so
    /// <see cref="GraphDocument.Capture"/> writes every literal back out rather than suppressing
    /// one that happened to match a default this node never declared — which is the difference
    /// between a byte-identical re-save and a silently shortened file. And
    /// <c>keepStructure</c> so a list arriving here is passed whole rather than replicated over:
    /// the node is going to refuse either way, and refusing once is a diagnostic where refusing
    /// per element is a wall of them.
    /// </remarks>
    private static PortDefinition Port(string name) =>
        new(name, typeof(object), declaredRank: 0, keepStructure: true);
}

/// <summary>
/// Thrown when a placeholder node is evaluated, because the package that defines it is absent.
/// </summary>
/// <remarks>
/// A placeholder throws rather than returning null. Null is a value, and a graph that quietly
/// produces one downstream of a missing package would compute a wrong answer confidently — which
/// is the one outcome worse than not computing at all.
/// </remarks>
public sealed class MissingPackageException : InvalidOperationException
{
    /// <summary>Creates the exception for a node key.</summary>
    /// <param name="key">The key of the node that cannot run.</param>
    public MissingPackageException(NodeKey key)
        : base(Describe(key))
    {
        Key = key;
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public MissingPackageException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public MissingPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with no message.</summary>
    public MissingPackageException()
        : base("A node cannot run because the package that defines it is not installed.")
    {
    }

    /// <summary>The key of the node that could not run.</summary>
    public NodeKey Key { get; }

    private static string Describe(NodeKey key)
    {
        string package = string.IsNullOrEmpty(key.Package) ? "(unknown)" : key.Package;
        return $"'{key}' cannot run: the package '{package}' is not installed. "
            + "The node, its values and its wires have been kept; install the package and reopen the graph.";
    }
}
