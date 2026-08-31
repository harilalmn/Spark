using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Spark.Api;
using Spark.UI.Theming;

namespace Spark.UI.ViewModels;

/// <summary>
/// One <b>Create</b> / <b>Action</b> / <b>Query</b> block inside a library category, and the
/// entries filed under it (<c>E8-T29</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A category is still a wall.</b> Grouping by category (<c>E8-T24</c>) turned a hundred and
/// thirty-six names into ten headings, and then <c>Solid</c> alone had thirty-eight entries under
/// it. Split three ways, somebody who wants to <i>make</i> a solid never reads the eleven nodes
/// that change one or the four that measure one. Dynamo does exactly this, the client asked for it
/// by name, and users arrive already able to read the three marks.
/// </para>
/// <para>
/// The rail colour and the glyph live in <see cref="NodeKindGlyphs"/> rather than here, so that a
/// view model carries no drawing decisions and the panel and any future consumer cannot disagree
/// about what a Query looks like.
/// </para>
/// </remarks>
public sealed partial class LibraryKindGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded = true;

    internal LibraryKindGroupViewModel(NodeMemberKind kind, IEnumerable<LibraryEntryViewModel> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Kind = kind;
        Entries = [.. entries];
        Label = NodeKindGlyphs.LabelOf(kind);
        Description = NodeKindGlyphs.DescriptionOf(kind);
        Rail = NodeKindGlyphs.BrushOf(kind);
    }

    /// <summary>Which of the three this block is.</summary>
    public NodeMemberKind Kind { get; }

    /// <summary>The entries filed under it, in library order.</summary>
    public ObservableCollection<LibraryEntryViewModel> Entries { get; }

    /// <summary>The word beside the glyph — <c>Create</c>, <c>Action</c> or <c>Query</c>.</summary>
    public string Label { get; }

    /// <summary>One sentence saying what the kind means, shown as the block's tooltip.</summary>
    public string Description { get; }

    /// <summary>The colour of the vertical rail drawn down the left of the block.</summary>
    public IBrush Rail { get; }

    /// <summary>The glyph drawn at the head of the block, in a sixteen-by-sixteen box.</summary>
    /// <remarks>
    /// <b>Resolved on access rather than in the constructor, and that is not a micro-optimisation.</b>
    /// Building a <c>Geometry</c> needs Avalonia's render interface, and this view model is built
    /// while the library is loaded - which happens in tests that have no rendering platform at all.
    /// Constructing it eagerly made <c>MainWindowViewModel</c>'s constructor throw in those, and it
    /// passed only when some other test class had happened to initialise the platform first, which
    /// is a flake waiting for a machine with different scheduling.
    /// </remarks>
    public Avalonia.Media.Geometry Glyph => NodeKindGlyphs.GeometryOf(Kind);

    /// <summary>How many entries the block holds, shown beside its label.</summary>
    public int Count => Entries.Count;

    /// <inheritdoc/>
    public override string ToString() => Label + " (" + Count.ToString(
        System.Globalization.CultureInfo.InvariantCulture) + ")";
}
