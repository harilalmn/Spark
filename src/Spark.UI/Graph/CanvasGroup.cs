using System;
using System.Collections.Generic;
using Spark.Engine;

namespace Spark.UI.Graph;

/// <summary>
/// A titled frame around a set of nodes.
/// </summary>
/// <remarks>
/// <para>
/// A group is the same kind of object as a <see cref="CanvasNote"/> — a canvas annotation with no
/// <see cref="NodeId"/>, no ports and no provenance, which the engine never sees. It differs in one
/// way, and that difference is the whole design: <b>it holds the identities of the nodes it
/// contains, and derives its rectangle from them.</b>
/// </para>
/// <para>
/// The alternative is to store a rectangle and decide membership by containment, and it is worse in
/// a way that is hard to undo. A group whose membership comes from geometry gains a node the moment
/// somebody drags one across its edge and loses one when they drag it out — silently, and with no
/// record in the file of what it used to contain. Storing membership means a node leaves a group
/// only when somebody says so.
/// </para>
/// <para>
/// Membership is by <see cref="NodeId"/> and not by slot. Slots renumber when a node is deleted;
/// identities do not, which is the same reason the file is keyed by them.
/// </para>
/// </remarks>
public sealed class CanvasGroup
{
    /// <summary>The gap between a group's frame and the nodes it contains.</summary>
    public const double Padding = 18;

    /// <summary>The height of the strip the title is drawn in, above the padded members.</summary>
    public const double TitleHeight = 22;

    private readonly HashSet<NodeId> _members = [];
    private string _title = "Group";

    /// <summary>Creates a group with a fresh identity.</summary>
    public CanvasGroup() : this(Guid.NewGuid())
    {
    }

    /// <summary>Creates a group with a known identity, which is what opening a file does.</summary>
    /// <param name="id">The identity to keep.</param>
    public CanvasGroup(Guid id) => Id = id;

    /// <summary>The group's identity, stable across save and load.</summary>
    public Guid Id { get; }

    /// <summary>What the group is called. Never null.</summary>
    public string Title
    {
        get => _title;
        set => _title = value ?? string.Empty;
    }

    /// <summary>The nodes inside the group, by identity.</summary>
    public IReadOnlyCollection<NodeId> Members => _members;

    /// <summary>Puts a node in the group.</summary>
    /// <param name="id">The node.</param>
    /// <returns>True when it was not already a member.</returns>
    public bool Add(NodeId id) => _members.Add(id);

    /// <summary>Takes a node out of the group.</summary>
    /// <param name="id">The node.</param>
    /// <returns>True when it was a member.</returns>
    public bool Remove(NodeId id) => _members.Remove(id);

    /// <summary>Whether a node is in the group.</summary>
    /// <param name="id">The node.</param>
    /// <returns>True when it is a member.</returns>
    public bool Contains(NodeId id) => _members.Contains(id);
}
