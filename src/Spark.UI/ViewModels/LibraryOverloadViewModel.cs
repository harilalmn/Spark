using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;

namespace Spark.UI.ViewModels;

/// <summary>
/// Several overloads of one member, shown as a single row in the library panel with a flyout
/// holding the variants (<c>E5-T4</c>, <c>FR-23</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>One node per overload is settled and is not what this changes.</b> The importer emits a
/// separate definition for each overload and disambiguates them by their differing parameter names
/// rather than by a numeric suffix (ADR-0004), because a suffix would make the second overload's
/// key depend on declaration order, which reflection does not guarantee. That is right and it is
/// also why the panel showed <c>Combine(a, b)</c> and <c>Combine(a, b, c)</c> as two adjacent rows
/// that differ only in a parenthesis — correct, and a thing a reader has to decode.
/// </para>
/// <para>
/// <b>The grouping key is the name before the bracket, and a bracket is itself the signal.</b> The
/// importer appends a parameter list only when two candidates collide on a name, so a display name
/// containing <c>(</c> is by construction one of at least two. Nothing else has to be recorded on
/// the definition, and nothing has to be inferred from the member — the naming rule already carries
/// the fact.
/// </para>
/// <para>
/// <b>It is a flyout rather than a fourth level of the tree, and that is settled rather than
/// preferred.</b> The design language lists <i>the node library flyout</i> by name among the
/// <b>E3 — Floating</b> surfaces, alongside menus and the autocomplete popup. A tree level would
/// have been cheaper and would have made overloads look like a category, which is the one thing
/// they are not: they are a single operation you are choosing an argument list for.
/// </para>
/// <para>
/// <b>What a first-party user sees today is nothing, and that is worth stating.</b>
/// <c>Spark.Nodes.Core</c> contains no overloads at all — all 221 node pages, and not one name with
/// a bracket in it — because a constructor is named from its parameters instead
/// (<c>ByCenterRadiusNormal</c>) and so never collides. This exists for imported packages, which is
/// the case the whole of <c>E5</c> is built for, and it is proved against an imported type.
/// </para>
/// </remarks>
public sealed class LibraryOverloadViewModel
{
    internal LibraryOverloadViewModel(string displayName, IEnumerable<LibraryEntryViewModel> overloads)
    {
        ArgumentNullException.ThrowIfNull(overloads);

        DisplayName = displayName;
        Overloads = [.. overloads];

        if (Overloads.Count == 0)
        {
            throw new ArgumentException(
                "An overload family with no members is a row describing nothing.", nameof(overloads));
        }

        LibraryEntryViewModel first = Overloads[0];

        Rail = first.Rail;
        Key = first.Key;

        // The description is the member's, and every overload of a member shares it: they come from
        // one XML doc comment per method name in the ordinary case, and where they differ the first
        // is as good a summary of the family as any. What tells them apart is the signature, which
        // is on each variant in the flyout where the choice is actually made.
        Description = first.Description;
    }

    /// <summary>The member's name, without any parameter list — what the single row says.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// The key of the first variant, so the panel can reach a help topic for the family.
    /// </summary>
    /// <remarks>
    /// Every overload has a page of its own and the family has none, so this points at one of them
    /// rather than at nothing. A reader who opens help on <c>Combine</c> wants to know what
    /// <c>Combine</c> does, and the first variant's page says so.
    /// </remarks>
    public string Key { get; }

    /// <summary>The variants, in library order.</summary>
    public ObservableCollection<LibraryEntryViewModel> Overloads { get; }

    /// <summary>One paragraph describing the member, from its author's XML comment.</summary>
    public string Description { get; }

    /// <summary>The colour of the vertical rail drawn down the left of the row.</summary>
    public IBrush Rail { get; }

    /// <summary>How many variants there are, shown on the row.</summary>
    public int Count => Overloads.Count;

    /// <summary>What the row says under the name: how many ways there are to call it.</summary>
    public string Summary => string.Create(
        CultureInfo.InvariantCulture, $"{Count} overloads");

    /// <inheritdoc/>
    public override string ToString() => DisplayName + " (" + Count.ToString(
        CultureInfo.InvariantCulture) + " overloads)";
}
