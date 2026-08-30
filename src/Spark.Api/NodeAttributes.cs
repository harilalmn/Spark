using System;

namespace Spark.Api;

/// <summary>
/// Marks a type, method, constructor or property as something the node importer should surface,
/// and lets its author override what the importer would otherwise infer.
/// </summary>
/// <remarks>
/// <para>
/// The importer is zero-config: a public member becomes a node with no attribute at all, because
/// third-party assemblies have to produce a sane library without cooperating. This attribute is
/// therefore always optional, and exists to say the things reflection cannot work out — a better
/// name, a category, and the lacing the author knows is right for the node.
/// </para>
/// <para>
/// <see cref="DefaultLacing"/> is the one that reaches users most directly. A node that produces
/// a grid can declare <see cref="LacingMode.CrossProduct"/> and it will behave as a grid node the
/// moment somebody drops it on the canvas, without them having to know Cross Product was the
/// right answer.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method
        | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class SparkNodeAttribute : Attribute
{
    /// <summary>Creates the attribute with everything left to the importer's own inference.</summary>
    public SparkNodeAttribute()
    {
    }

    /// <summary>
    /// The node's display name, overriding the <c>Type.Member</c> name the importer would derive.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>The library category the node is filed under, for example <c>Geometry.Curves</c>.</summary>
    public string? Category { get; set; }

    /// <summary>
    /// The lacing an instance of this node uses when the user has not overridden it — that is,
    /// what <see cref="LacingMode.Auto"/> resolves to.
    /// </summary>
    /// <remarks>
    /// This may not itself be <see cref="LacingMode.Auto"/>: there is exactly one hop, never a
    /// chain. A definition that declares no default gets <see cref="LacingMode.Longest"/>.
    /// </remarks>
    public LacingMode DefaultLacing { get; set; } = LacingMode.Longest;
}

/// <summary>
/// Names and documents one port, overriding what the importer would take from the parameter.
/// </summary>
/// <remarks>
/// Applied to a parameter it renames an input port; applied to the return value it renames the
/// output. Multi-output nodes — a method with <c>out</c> parameters — get one port per value, and
/// this attribute is how each of them gets a name a user would recognise.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Parameter | AttributeTargets.ReturnValue,
    AllowMultiple = false,
    Inherited = false)]
public sealed class NodePortAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public NodePortAttribute()
    {
    }

    /// <summary>Creates the attribute with a port name.</summary>
    /// <param name="name">The port's display name.</param>
    public NodePortAttribute(string name) => Name = name;

    /// <summary>The port's display name.</summary>
    public string? Name { get; set; }

    /// <summary>One line describing what the port is for. Becomes the port's tooltip.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Excludes a public member from node generation entirely, with a reason.
/// </summary>
/// <remarks>
/// The two-way coverage test requires every public member to be reachable as exactly one node or
/// to be excluded deliberately, so the reason is not decoration — it is the thing that stops the
/// exclusion list quietly becoming the hand-maintained mapping that
/// <c>DoodleSharp</c>'s help generator rotted into.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method
        | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class NodeIgnoreAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="reason">Why this member is not a node. Required, and read by the coverage test.</param>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is null or blank.</exception>
    public NodeIgnoreAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Why this member is not a node.</summary>
    public string Reason { get; }
}

/// <summary>
/// Excludes a port from replication: it never iterates, never contributes to the iteration count,
/// and broadcasts whole into every leaf call.
/// </summary>
/// <remarks>
/// <para>
/// Use it for options and settings objects, where fanning the node out over a list of settings is
/// never what the user meant. The port is still rank-checked: supplying a list to one is an error
/// naming the port, not a silent lacing.
/// </para>
/// <para>
/// This is deliberately not the same attribute as <see cref="KeepStructureAttribute"/>. This one
/// says <i>do not fan my node out over this port</i> while still type-checking it;
/// <see cref="KeepStructureAttribute"/> says <i>this port is about structure, hand it over
/// untouched</i>, which necessarily disables the rank check as well.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class NoReplicationAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public NoReplicationAttribute()
    {
    }
}

/// <summary>
/// Treats the port's declared rank as unbounded: it never replicates, never promotes and never
/// rank-errors, and the node receives the value exactly as supplied. Implies
/// <see cref="NoReplicationAttribute"/>.
/// </summary>
/// <remarks>
/// This is the author's fix for every node that is <i>about</i> list structure — count, reverse,
/// flatten, transpose, get-item-at-index. Those nodes are declared at rank 1 and routinely receive
/// rank 2, and under any replicating mode they would run once per inner list and count each row
/// instead of the rows. Unlike setting the node's default lacing to
/// <see cref="LacingMode.Disabled"/>, this cannot be broken by a user changing lacing on the node.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class KeepStructureAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public KeepStructureAttribute()
    {
    }
}

/// <summary>
/// Marks a node whose value the canvas should show permanently, rather than only when it is
/// hovered or selected. A watch node.
/// </summary>
/// <remarks>
/// <para>
/// <b>Declared by the node, not recognised by the canvas.</b> The canvas has no node library and
/// must never name an engine type — [ADR-0005](../../docs/adr/0005-api-engine-host-layering.md) —
/// so <i>is this a watch?</i> has to be something the definition says about itself. It travels the
/// same route as <c>Category</c>: a fact the engine carries for the shell's benefit and never reads
/// itself.
/// </para>
/// <para>
/// It is an attribute rather than a category because it is not a question about colour. Giving
/// watches their own category would mean inventing a design-language colour, and a
/// contrast-verified row to go with it, to answer a question that has nothing to do with how the
/// node is painted.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ShowsValueAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public ShowsValueAttribute()
    {
    }
}

/// <summary>
/// Sets a port's Cross Product dimension. Dimensions nest in ascending guide order, outermost
/// first; ports without a guide keep their port index. Has no effect in any other mode.
/// </summary>
/// <remarks>
/// Two replicating ports declaring the same guide is an error, not a tie broken silently: the
/// nesting order of the result is the whole point of Cross Product, and a coin toss over it would
/// produce a transposed grid that looks entirely plausible.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ReplicationGuideAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="guide">The dimension order. Lower guides nest further out.</param>
    public ReplicationGuideAttribute(int guide) => Guide = guide;

    /// <summary>The dimension order. Lower guides nest further out.</summary>
    public int Guide { get; }
}

/// <summary>
/// Declares that a node has a side effect or an observable dependency on something outside the
/// graph — the clock, the file system, a random source, a live model.
/// </summary>
/// <remarks>
/// <para>
/// The evaluation cache is keyed by provenance, not by value, so a node whose result depends on
/// anything the key does not name will serve a stale result forever and never look wrong. An
/// impure node mixes the run epoch into its key and poisons the keys of everything downstream, so
/// the subgraph below it re-evaluates on every run.
/// </para>
/// <para>
/// There is no way to detect this by inspection, which is why it has to be declared. An undeclared
/// impure node is the worst failure available here: it poisons nothing and therefore silently
/// serves stale results.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method
        | AttributeTargets.Constructor | AttributeTargets.Property,
    AllowMultiple = false,
    Inherited = false)]
public sealed class NodeSideEffectAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public NodeSideEffectAttribute()
    {
    }

    /// <summary>Creates the attribute with a reason.</summary>
    /// <param name="reason">What outside the graph this node depends on or changes.</param>
    public NodeSideEffectAttribute(string reason) => Reason = reason;

    /// <summary>What outside the graph this node depends on or changes.</summary>
    public string? Reason { get; }
}
