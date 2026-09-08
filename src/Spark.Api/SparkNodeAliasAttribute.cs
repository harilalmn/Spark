using System;

namespace Spark.Api;

/// <summary>
/// A name this node used to be called, so a graph that names it still opens (<c>E3-T23</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A node's key is written into every file that uses it</b>, so renaming one breaks every graph
/// anybody has already saved. Without this the failure is quiet and total: the loader does not
/// find the key, <c>MissingNodePolicy.Placeholder</c> substitutes a node that keeps the wires and
/// refuses to evaluate, and the user is told the node is unknown rather than that it was renamed.
/// <c>E3-T17</c> promises a graph outlives the process, and a rename without this breaks that
/// promise for every file at once.
/// </para>
/// <para>
/// <b>It sits on the member it renames rather than in a central table, and that is the whole
/// design.</b> A list of old-to-new pairs somewhere else is a list that drifts: delete the method
/// and the entry survives, rename it twice and the chain has to be maintained by hand. Here,
/// deleting the method deletes its aliases, and a member carries its own history where the next
/// person to rename it will be standing.
/// </para>
/// <para>
/// <b>Several are allowed, because a member can be renamed twice.</b> Each one names a key that
/// once meant this member, and they are all equal — there is no chain to walk, because every alias
/// points at the member as it is now.
/// </para>
/// <para>
/// <b>The name is the key's member half, not the whole key.</b> <c>Circle.ByCentreRadius</c>, not
/// <c>Spark.Nodes.Core/Circle.ByCentreRadius</c>: the package comes from the assembly, so an alias
/// that spelt it out would be wrong the moment an assembly was renamed, and would let a member in
/// one package claim a name in another.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [SparkNodeAlias("Circle.ByCentreRadius")]
/// public static Circle FromCentreRadius(Point3d centre, double radius) => …
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property
        | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = true,
    Inherited = false)]
public sealed class SparkNodeAliasAttribute : Attribute
{
    /// <summary>Records a name this member used to be known by.</summary>
    /// <param name="name">
    /// The old <c>Type.Member</c> name, without the package. Never null or blank.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or blank.</exception>
    public SparkNodeAliasAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    /// <summary>The old name, as files still spell it.</summary>
    public string Name { get; }
}
