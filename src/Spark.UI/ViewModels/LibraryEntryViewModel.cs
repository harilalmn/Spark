using System;
using Spark.Engine;

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
        Description = definition.Description ?? "No description.";
        Signature = Describe(definition);
    }

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
