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
        DisplayName = definition.DisplayName;
        Category = definition.Category;
        Description = definition.Description ?? "No description.";
        Signature = Describe(definition);
    }

    /// <summary>The node's name, as it appears on the canvas.</summary>
    public string DisplayName { get; }

    /// <summary>The library category.</summary>
    public string Category { get; }

    /// <summary>One paragraph describing the node, from its author's XML comment.</summary>
    public string Description { get; }

    /// <summary>The port names, as one line — the tooltip's second row.</summary>
    public string Signature { get; }

    internal NodeDefinition Definition { get; }

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
