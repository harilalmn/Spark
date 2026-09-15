using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using Spark.Api;
using Spark.Engine;
using Spark.UI.ViewModels;
using Spark.UI.Views.Panes;

namespace Spark.UI.Tests;

/// <summary>
/// Overloads of one member shown as a single library row with a flyout (<c>E5-T4</c>,
/// <c>FR-23</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The library this tests is imported, not the first-party one, and that is the subject rather
/// than a convenience.</b> <c>Spark.Nodes.Core</c> contains no overloads at all — a constructor is
/// named from its parameters (<c>ByCenterRadiusNormal</c>) and so never collides, and no method in
/// it does either. The feature exists for packages a user imports, which is the case the whole of
/// <c>E5</c> is built for, so a test over the built-in library could only ever assert that nothing
/// was grouped. <see cref="TheFirstPartyLibraryHasNoOverloadsAndIsUnchanged"/> asserts exactly
/// that, on purpose, so the claim is recorded rather than assumed.
/// </para>
/// <para>
/// <b>These are view-model tests and there is no window.</b> What is under test is which rows exist
/// and what they hold; that the flyout opens is markup, and a test that spun up a rendering
/// platform to confirm a <c>Button.Flyout</c> attaches would be testing Avalonia.
/// </para>
/// </remarks>
public sealed class LibraryOverloadGroupingTests
{
    /// <summary>
    /// <b>The row.</b> Two overloads of one member become one row holding both, not two rows.
    /// </summary>
    [Fact]
    public void TwoOverloadsBecomeOneRowHoldingBoth()
    {
        LibraryKindGroupViewModel block = Block(Overloaded());

        LibraryOverloadViewModel family = Assert.Single(block.Rows.OfType<LibraryOverloadViewModel>());

        // `Type.Member`, which is what every other row in the panel says. The bracket is the only
        // thing removed.
        Assert.Equal("ImportedOverloads.Combine", family.DisplayName);
        Assert.Equal(2, family.Count);

        Assert.Equal(
            ["ImportedOverloads.Combine(a, b)", "ImportedOverloads.Combine(a, b, c)"],
            family.Overloads.Select(entry => entry.DisplayName).OrderBy(n => n, StringComparer.Ordinal));

        // One row, where the panel used to draw two.
        Assert.Single(block.Rows);
    }

    /// <summary>
    /// <b><c>Entries</c> stays flat</b>, because it is what the count and every *is this node
    /// present* test read. Folding it would have made both answer a different question for a reason
    /// that is entirely about drawing.
    /// </summary>
    [Fact]
    public void TheFlatEntryListIsUnchanged()
    {
        LibraryKindGroupViewModel block = Block(Overloaded());

        Assert.Equal(2, block.Entries.Count);
        Assert.Equal(2, block.Count);
    }

    /// <summary>A member with one definition is an ordinary row, and is not wrapped.</summary>
    /// <remarks>
    /// Hiding a single node behind a flyout would be strictly worse than the node: one more click
    /// to reach exactly one thing.
    /// </remarks>
    [Fact]
    public void AMemberWithNoOverloadsIsAPlainRow()
    {
        LibraryKindGroupViewModel block = Block(Plain());

        Assert.All(block.Rows, row => Assert.IsType<LibraryEntryViewModel>(row));
        Assert.Equal(block.Entries.Count, block.Rows.Count);
    }

    /// <summary>
    /// A family of one — which happens when a collision is split across two kinds — is drawn as
    /// the node it is, not as a flyout over a single entry.
    /// </summary>
    [Fact]
    public void AFamilyOfOneInThisBlockIsAPlainRow()
    {
        NodeLibrary library = Library(typeof(ImportedOverloads));

        LibraryEntryViewModel single = Entry(library, "ImportedOverloads.Combine(a, b)");

        LibraryKindGroupViewModel block = new(NodeMemberKind.Create, [single]);

        LibraryEntryViewModel drawn = Assert.IsType<LibraryEntryViewModel>(Assert.Single(block.Rows));
        Assert.Equal("ImportedOverloads.Combine(a, b)", drawn.DisplayName);
    }

    /// <summary>Plain entries and families sit in one list, in library order.</summary>
    [Fact]
    public void RowsMixEntriesAndFamiliesInLibraryOrder()
    {
        NodeLibrary library = Library(typeof(ImportedOverloads), typeof(ImportedPlain));

        List<LibraryEntryViewModel> entries =
        [
            .. library.Definitions()
                .OrderBy(definition => definition.DisplayName, StringComparer.Ordinal)
                .Select(definition => new LibraryEntryViewModel(definition)),
        ];

        LibraryKindGroupViewModel block = new(NodeMemberKind.Create, entries);

        Assert.Equal(
            ["ImportedOverloads.Combine", "ImportedPlain.Alone"],
            block.Rows.Select(row => row switch
            {
                LibraryOverloadViewModel family => family.DisplayName,
                LibraryEntryViewModel entry => entry.DisplayName,
                _ => throw new InvalidOperationException("an unexpected row kind"),
            }));
    }

    /// <summary>
    /// The family carries the rail colour and a key, so the row draws like its neighbours and F1
    /// reaches a page rather than nothing.
    /// </summary>
    [Fact]
    public void AFamilyCarriesARailAndAKey()
    {
        LibraryOverloadViewModel family =
            Assert.Single(Block(Overloaded()).Rows.OfType<LibraryOverloadViewModel>());

        Assert.NotNull(family.Rail);
        Assert.False(string.IsNullOrWhiteSpace(family.Key));
        Assert.Contains("2 overloads", family.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The first-party library has no overloads, so no row in it is a family.</b> Recorded
    /// rather than assumed: the feature is invisible in <c>Spark.Nodes.Core</c> today, and a
    /// reader who did not know that would go looking for a flyout that cannot appear.
    /// </summary>
    [Fact]
    public void TheFirstPartyLibraryHasNoOverloadsAndIsUnchanged()
    {
        MainWindowViewModel model = new();

        IEnumerable<object> rows = model.LibraryGroups
            .SelectMany(group => group.Subgroups)
            .SelectMany(block => block.Rows);

        Assert.All(rows, row => Assert.IsType<LibraryEntryViewModel>(row));

        Assert.DoesNotContain(
            model.LibraryEntries,
            entry => entry.DisplayName.Contains('(', StringComparison.Ordinal));
    }

    /// <summary>An empty family is refused rather than drawn as a row describing nothing.</summary>
    [Fact]
    public void AnEmptyFamilyIsRefused() =>
        Assert.Throws<ArgumentException>(() => new LibraryOverloadViewModel("Combine", []));

    /// <summary>
    /// <b>The panel really does build the floating surface for a family</b>, and its bindings
    /// resolve to that family's overloads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Without this the template would be unexercised on every machine.</b> Nothing in
    /// <c>Spark.Nodes.Core</c> produces an overload family, so the application can be opened, used
    /// and photographed without that <c>DataTemplate</c> ever being matched — a mistyped binding or
    /// a template the tree declines to select would look exactly like the feature being invisible,
    /// which it already is. XamlIl compiling the markup proves it parses, not that it is chosen and
    /// not that anything inside it binds.
    /// </para>
    /// <para>
    /// <b>This is the test the markup was shaped around, and the shaping was the right way round.</b>
    /// The row first used a <c>Button.Flyout</c>, whose content is not in the row's logical tree
    /// until it opens; the headless platform cannot open a popup at all
    /// (<i>Unable to create IPopupImpl and no overlay layer is found</i>), which is the same class
    /// of limitation as <c>N90</c>. Between the two, the flyout's bindings could not have been
    /// checked anywhere. A <c>Popup</c> declared in the template is a logical child of it, so the
    /// binding resolves whether or not it is open — and this can say so.
    /// </para>
    /// <para>
    /// The template is built directly rather than by showing the pane: what is under test is which
    /// template matches and what it produces, not layout.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePanelBuildsTheFloatingSurfaceForAFamily() => HeadlessSession.Run(() =>
    {
        LibraryOverloadViewModel family =
            Assert.Single(Block(Overloaded()).Rows.OfType<LibraryOverloadViewModel>());

        LibraryPane pane = new();
        TreeView tree = pane.GetControl<TreeView>("LibraryTree");

        IDataTemplate template = Assert.Single(
            tree.DataTemplates, candidate => candidate.Match(family));

        Control built = Assert.IsAssignableFrom<Control>(template.Build(family));
        built.DataContext = family;

        ToggleButton row = Assert.Single(built.GetLogicalDescendants().OfType<ToggleButton>());
        Assert.Equal(family.DisplayName, AutomationProperties.GetName(row));

        Popup popup = Assert.Single(built.GetLogicalDescendants().OfType<Popup>());
        Border surface = Assert.IsType<Border>(popup.Child);

        // The E3 floating elevation the design language names this popup among by name.
        Assert.Contains("float", surface.Classes);

        ItemsControl variants = Assert.Single(surface.GetLogicalDescendants().OfType<ItemsControl>());

        // The binding resolved, which is the whole reason this is a Popup and not a Flyout.
        Assert.Same(family.Overloads, variants.ItemsSource);
    });

    private static LibraryKindGroupViewModel Block(IEnumerable<LibraryEntryViewModel> entries) =>
        new(NodeMemberKind.Create, entries);

    private static List<LibraryEntryViewModel> Overloaded()
    {
        NodeLibrary library = Library(typeof(ImportedOverloads));

        return
        [
            Entry(library, "ImportedOverloads.Combine(a, b)"),
            Entry(library, "ImportedOverloads.Combine(a, b, c)"),
        ];
    }

    private static List<LibraryEntryViewModel> Plain()
    {
        NodeLibrary library = Library(typeof(ImportedPlain));

        return [.. library.Definitions().Select(definition => new LibraryEntryViewModel(definition))];
    }

    private static NodeLibrary Library(params Type[] types)
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(types, "Overloads"));

        return library;
    }

    private static LibraryEntryViewModel Entry(NodeLibrary library, string displayName) =>
        new(library.Definitions().Single(definition => definition.DisplayName == displayName));
}

/// <summary>Two overloads of one name, which the importer disambiguates by parameter list.</summary>
/// <remarks>
/// Declared here rather than reused from <c>Spark.Engine.Tests</c>: a test project referencing
/// another test project to borrow a fixture couples two suites that have no other reason to know
/// about each other, and this type is six lines.
/// </remarks>
public static class ImportedOverloads
{
    /// <summary>Two arguments.</summary>
    /// <param name="a">The first.</param>
    /// <param name="b">The second.</param>
    /// <returns>The sum.</returns>
    public static double Combine(double a, double b) => a + b;

    /// <summary>Three arguments.</summary>
    /// <param name="a">The first.</param>
    /// <param name="b">The second.</param>
    /// <param name="c">The third.</param>
    /// <returns>The sum.</returns>
    public static double Combine(double a, double b, double c) => a + b + c;
}

/// <summary>A member with no overloads, so the panel draws it as itself.</summary>
public static class ImportedPlain
{
    /// <summary>One argument.</summary>
    /// <param name="a">The only one.</param>
    /// <returns>It, doubled.</returns>
    public static double Alone(double a) => a * 2;
}
