using System;
using System.Collections.Generic;
using System.Reflection;

namespace Spark.Engine;

/// <summary>
/// One node the importer produced, together with the member it came from.
/// </summary>
/// <param name="Definition">The generated definition.</param>
/// <param name="Member">
/// The member it was generated from. Kept so the two-way coverage test can assert that every node
/// still resolves to a live member — the direction of drift that
/// <c>DoodleSharp</c>'s help generator never checked, and where seven of its entries pointed at
/// members that had been deleted.
/// </param>
public sealed record ImportedNode(NodeDefinition Definition, MemberInfo Member);

/// <summary>
/// One public member the importer deliberately did not turn into a node, and why.
/// </summary>
/// <param name="Member">The member.</param>
/// <param name="Reason">
/// Why it is not a node. Never blank: the reason is the thing that stops the exclusion set from
/// silently becoming the hand-maintained mapping ADR-0004 exists to avoid.
/// </param>
public sealed record ExcludedMember(MemberInfo Member, string Reason)
{
    /// <summary>The member's declaring type and name, for a test failure message.</summary>
    /// <returns>For example <c>Number.MaximumRangeCount</c>.</returns>
    public override string ToString() =>
        $"{Member.DeclaringType?.Name ?? Member.Module.Name}.{Member.Name}";
}

/// <summary>
/// Everything one import produced: the nodes, and every public member that did not become one.
/// </summary>
/// <remarks>
/// <para>
/// The report exists so the coverage test can run in <b>both</b> directions from a single import.
/// Every public member is either a node or an exclusion with a stated reason, and every node
/// resolves to a live member. Writing that test before the importer was finished is the whole
/// reason this type carries the exclusions at all — an importer that simply skipped what it could
/// not handle would pass any test written afterwards, because the test would be written against
/// what it happened to do.
/// </para>
/// </remarks>
public sealed class ImportReport
{
    internal ImportReport(
        string package,
        IReadOnlyList<ImportedNode> nodes,
        IReadOnlyList<ExcludedMember> exclusions)
    {
        Package = package;
        Nodes = nodes;
        Exclusions = exclusions;
    }

    /// <summary>The package identity every generated key carries.</summary>
    public string Package { get; }

    /// <summary>The generated nodes.</summary>
    public IReadOnlyList<ImportedNode> Nodes { get; }

    /// <summary>The public members that are not nodes, each with its reason.</summary>
    public IReadOnlyList<ExcludedMember> Exclusions { get; }

    /// <summary>The definitions alone, which is what a library registry wants.</summary>
    /// <returns>One definition per imported node.</returns>
    public IReadOnlyList<NodeDefinition> Definitions()
    {
        NodeDefinition[] definitions = new NodeDefinition[Nodes.Count];
        for (int index = 0; index < Nodes.Count; index++)
        {
            definitions[index] = Nodes[index].Definition;
        }

        return definitions;
    }

    /// <summary>The reason a member was excluded, or <see langword="null"/> when it was not.</summary>
    /// <param name="member">The member to look up.</param>
    /// <returns>The reason, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <see langword="null"/>.</exception>
    public string? ReasonFor(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);

        foreach (ExcludedMember exclusion in Exclusions)
        {
            if (exclusion.Member == member)
            {
                return exclusion.Reason;
            }
        }

        return null;
    }
}
