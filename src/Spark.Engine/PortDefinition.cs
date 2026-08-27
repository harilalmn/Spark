using System;
using System.Collections.Generic;
using System.Reflection;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// One port on a node definition: its name, the CLR type it is declared as, the rank that type
/// implies, and the replication opt-outs its author applied.
/// </summary>
/// <remarks>
/// <para>
/// <b>Declared rank is the load-bearing field.</b> It comes from the signature: <c>double x</c> is
/// rank 0, <c>IReadOnlyList&lt;double&gt; xs</c> is rank 1, and a list of lists is rank 2. Note
/// that <c>object</c> declares rank <b>0</b> — a parameter typed <c>object</c> will happily hold a
/// list at run time, but as far as replication is concerned it wants a single value, so passing it
/// a list replicates. An author who wants the list itself says so with
/// <see cref="KeepStructureAttribute"/>.
/// </para>
/// </remarks>
public sealed class PortDefinition
{
    /// <summary>Creates a port with an explicitly stated declared rank.</summary>
    /// <param name="name">The port's display name.</param>
    /// <param name="valueType">The CLR type the port is declared as.</param>
    /// <param name="declaredRank">
    /// How deeply nested a value this port wants. Ignored when <paramref name="keepStructure"/> is
    /// set, because that port has no rank that is wrong for it.
    /// </param>
    /// <param name="description">One line describing the port. Optional.</param>
    /// <param name="keepStructure">
    /// The port's declared rank is unbounded: it never replicates, never promotes and never
    /// rank-errors. Implies <paramref name="noReplication"/>.
    /// </param>
    /// <param name="noReplication">
    /// The port is excluded from the replication depth and from the iteration count, and never
    /// iterates. It is still rank-checked.
    /// </param>
    /// <param name="replicationGuide">
    /// The port's Cross Product dimension order, or <see langword="null"/> to use the port index.
    /// </param>
    /// <param name="defaultValue">The value used when nothing is wired and no literal is set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="valueType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="declaredRank"/> is negative.</exception>
    public PortDefinition(
        string name,
        Type valueType,
        int declaredRank,
        string? description = null,
        bool keepStructure = false,
        bool noReplication = false,
        int? replicationGuide = null,
        object? defaultValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(valueType);
        ArgumentOutOfRangeException.ThrowIfNegative(declaredRank);

        Name = name;
        ValueType = valueType;
        DeclaredRank = declaredRank;
        Description = description;
        KeepStructure = keepStructure;

        // [KeepStructure] implies [NoReplication]: a port that receives its value verbatim
        // obviously cannot be the thing the node fans out over.
        NoReplication = noReplication || keepStructure;
        ReplicationGuide = replicationGuide;
        DefaultValue = defaultValue;
    }

    /// <summary>
    /// Creates a port, inferring the declared rank from the CLR type by counting nested list
    /// types. This is what the importer uses; a hand-built definition may prefer the explicit
    /// constructor.
    /// </summary>
    /// <param name="name">The port's display name.</param>
    /// <param name="valueType">The CLR type the port is declared as.</param>
    /// <returns>The port.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="valueType"/> is <see langword="null"/>.</exception>
    public static PortDefinition Inferred(string name, Type valueType) =>
        new(name, valueType, RankOfType(valueType));

    /// <summary>The port's display name.</summary>
    public string Name { get; }

    /// <summary>One line describing the port, or <see langword="null"/>.</summary>
    public string? Description { get; }

    /// <summary>The CLR type the port is declared as.</summary>
    public Type ValueType { get; }

    /// <summary>
    /// How deeply nested a value this port wants: 0 for a scalar, 1 for a list, 2 for a list of
    /// lists. Meaningless when <see cref="KeepStructure"/> is set.
    /// </summary>
    public int DeclaredRank { get; }

    /// <summary>
    /// The port receives its value exactly as supplied: no replication, no promotion, no rank
    /// error. See <see cref="KeepStructureAttribute"/>.
    /// </summary>
    public bool KeepStructure { get; }

    /// <summary>
    /// The port never iterates and never contributes to the iteration count. Set implicitly by
    /// <see cref="KeepStructure"/>. See <see cref="NoReplicationAttribute"/>.
    /// </summary>
    public bool NoReplication { get; }

    /// <summary>
    /// The port's Cross Product dimension order, or <see langword="null"/> to use the port index.
    /// Lower guides nest further out.
    /// </summary>
    public int? ReplicationGuide { get; }

    /// <summary>The value used when nothing is wired and no literal has been set.</summary>
    public object? DefaultValue { get; }

    /// <summary>
    /// The declared rank a CLR type implies: the number of nested list layers it names.
    /// <c>double</c> is 0, <c>IReadOnlyList&lt;double&gt;</c> is 1,
    /// <c>IReadOnlyList&lt;IReadOnlyList&lt;double&gt;&gt;</c> is 2.
    /// </summary>
    /// <remarks>
    /// <see cref="string"/> is rank 0 even though it is a sequence of characters, and
    /// <see cref="object"/> is rank 0 even though it can hold anything. Both of those are the
    /// answers the lacing specification requires, and both are the ones a naive
    /// "is it enumerable?" test gets wrong.
    /// </remarks>
    /// <param name="type">The type to measure.</param>
    /// <returns>The rank.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    public static int RankOfType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        int rank = 0;
        Type? current = type;

        while (current is not null && ElementTypeOf(current) is { } element)
        {
            rank++;
            current = element;
        }

        return rank;
    }

    /// <summary>
    /// The element type of a list-shaped type, or <see langword="null"/> when the type is a scalar
    /// as far as Spark is concerned.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>The element type, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    public static Type? ElementTypeOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type == typeof(string) || type == typeof(object))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            if (definition == typeof(IReadOnlyList<>)
                || definition == typeof(IList<>)
                || definition == typeof(List<>)
                || definition == typeof(IReadOnlyCollection<>)
                || definition == typeof(ICollection<>)
                || definition == typeof(IEnumerable<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a port from a method parameter, reading the authoring attributes off it. Used by the
    /// importer and by hand-built definitions that want the same rules applied.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns>The port.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parameter"/> is <see langword="null"/>.</exception>
    public static PortDefinition FromParameter(ParameterInfo parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        Type type = parameter.ParameterType;
        if (type.IsByRef)
        {
            type = type.GetElementType()!;
        }

        NodePortAttribute? port = parameter.GetCustomAttribute<NodePortAttribute>();
        ReplicationGuideAttribute? guide = parameter.GetCustomAttribute<ReplicationGuideAttribute>();

        return new PortDefinition(
            port?.Name ?? parameter.Name ?? $"arg{parameter.Position}",
            type,
            RankOfType(type),
            port?.Description,
            parameter.GetCustomAttribute<KeepStructureAttribute>() is not null,
            parameter.GetCustomAttribute<NoReplicationAttribute>() is not null,
            guide?.Guide,
            parameter.HasDefaultValue ? parameter.DefaultValue : null);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name}: {ValueType.Name} (rank {(KeepStructure ? "*" : DeclaredRank.ToString(System.Globalization.CultureInfo.InvariantCulture))})";
}
