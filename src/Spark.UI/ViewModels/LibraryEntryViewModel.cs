using System;
using Avalonia.Media;
using Spark.Api;
using Spark.Engine;
using Spark.UI.Theming;

namespace Spark.UI.ViewModels;

/// <summary>
/// One entry in the library panel: a name and a category a user reads, over a definition they
/// never see.
/// </summary>
/// <remarks>
/// The definition is deliberately not a bindable property. Views bind to
/// <see cref="DisplayName"/>, <see cref="Category"/> and <see cref="Description"/>; the definition
/// travels back to the view model when the entry is placed, so no XAML ever names an engine type.
/// </remarks>
public sealed class LibraryEntryViewModel
{
    internal LibraryEntryViewModel(NodeDefinition definition)
    {
        Definition = definition;
        Key = definition.Key.Value;
        DisplayName = definition.DisplayName;
        Category = definition.Category;
        Kind = definition.MemberKind;
        Rail = NodeKindGlyphs.BrushOf(definition.MemberKind);
        Description = definition.Description ?? "No description.";
        Signature = Describe(definition);

        BrepCapabilities missing = definition.RequiredCapabilities & ~BrepKernel.Current.Capabilities;

        IsAvailable = missing == BrepCapabilities.None;
        Unavailable = IsAvailable ? null : Explain(missing);
    }

    /// <summary>
    /// Whether this build's solid-modelling kernel can do what the node needs (<c>E2-T28</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read once, when the entry is built, because that is when the library is built.</b>
    /// Installing a kernel is a startup decision — <c>OcctKernel.TryInstall</c> runs before the
    /// first window — so a value that could change under a live entry would be answering a question
    /// nobody can ask.
    /// </para>
    /// <para>
    /// <b>The configuration this exists for is real.</b> A build with no native component is
    /// supported and tested; on one, every solid operation refuses by name, and until this the
    /// library showed all of them exactly as it showed the nodes that work.
    /// </para>
    /// </remarks>
    public bool IsAvailable { get; }

    /// <summary>
    /// One sentence saying why the node cannot run here, or null when it can.
    /// </summary>
    /// <remarks>
    /// <b>It names the capability, not the node.</b> A user looking at a greyed-out
    /// <c>Solid.Union</c> already knows which node it is; what they do not know is that this build
    /// has no solid-modelling kernel, which is one fact explaining forty greyed rows rather than
    /// forty separate mysteries.
    /// </remarks>
    public string? Unavailable { get; }

    /// <summary>What the row's tooltip says: why it cannot run, or what it does.</summary>
    /// <remarks>
    /// <b>The reason displaces the description, rather than being appended to it.</b> The row is
    /// narrow and the reason is trimmed on it — which is how this was found, in a screenshot of a
    /// build with no kernel showing <i>This build has no solid-modelli…</i> and a tooltip
    /// explaining what a union is. Somebody hovering a greyed row is asking one question, and it
    /// is not what the node does.
    /// </remarks>
    public string Tooltip => Unavailable ?? Description;

    /// <summary>Puts the missing flags into a sentence.</summary>
    /// <param name="missing">What the kernel cannot do that the node needs.</param>
    /// <returns>The sentence shown as the row's tooltip.</returns>
    /// <remarks>
    /// <b>A kernel reporting <see cref="BrepCapabilities.None"/> is called out by name</b>, because
    /// it is not one absent operation, it is the whole provider being absent — and *this build has
    /// no solid-modelling kernel* is what the user needs, where *needs Boolean* would send them
    /// looking for a setting.
    /// </remarks>
    private static string Explain(BrepCapabilities missing) =>
        BrepKernel.Current.Capabilities == BrepCapabilities.None
            ? "This build has no solid-modelling kernel, so this node cannot run. "
                + "The application still opens graphs that use it."
            : "This build's solid-modelling kernel cannot " + missing.ToString() + ".";

    /// <summary>
    /// The node's key, as <c>Package/Name</c>.
    /// </summary>
    /// <remarks>
    /// A string rather than a <c>NodeKey</c>, so that no XAML and no view names an engine type -
    /// the same rule <see cref="Definition"/> follows. It is here so the panel can ask for a
    /// node's help topic without placing it first.
    /// </remarks>
    public string Key { get; }

    /// <summary>The node's name, as it appears on the canvas.</summary>
    public string DisplayName { get; }

    /// <summary>The library category.</summary>
    public string Category { get; }

    /// <summary>
    /// Whether the node makes a thing, changes one, or reports something about one — the subgroup
    /// it is filed under inside its category (<c>E8-T29</c>).
    /// </summary>
    public NodeMemberKind Kind { get; }

    /// <summary>
    /// The colour of the vertical rail drawn down the left of the row, from <see cref="Kind"/>.
    /// </summary>
    /// <remarks>
    /// <b>On the row rather than only on the group, and that is what makes the rail continuous.</b>
    /// The line beside a block of entries is the left border of each entry in it, drawn with no
    /// margin between the rows; the spacing the client asked for is padding <i>inside</i> each
    /// border. A margin between rows would have chopped the line into dashes.
    /// </remarks>
    public IBrush Rail { get; }

    /// <summary>One paragraph describing the node, from its author's XML comment.</summary>
    public string Description { get; }

    /// <summary>The port names, as one line — the tooltip's second row.</summary>
    public string Signature { get; }

    /// <summary>
    /// The definition behind this entry.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not bindable and deliberately not used by any view.</b> It is here so the
    /// view model can hand the definition back when the entry is placed, and so tests can assert
    /// on what the library actually loaded. No XAML names an engine type; that rule is the reason
    /// <see cref="Key"/>, <see cref="DisplayName"/> and the rest exist as strings beside it.
    /// </remarks>
    public NodeDefinition Definition { get; }

    /// <inheritdoc/>
    public override string ToString() => DisplayName;

    private static string Describe(NodeDefinition definition)
    {
        string[] inputs = new string[definition.Inputs.Count];
        for (int index = 0; index < inputs.Length; index++)
        {
            inputs[index] = definition.Inputs[index].Name;
        }

        string[] outputs = new string[definition.Outputs.Count];
        for (int index = 0; index < outputs.Length; index++)
        {
            outputs[index] = definition.Outputs[index].Name;
        }

        return $"({string.Join(", ", inputs)}) → {string.Join(", ", outputs)}";
    }
}
