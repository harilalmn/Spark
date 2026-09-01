using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Spark.Api.Help;
using Spark.UI.Controls;
using Spark.UI.Theming;

namespace Spark.UI.Views;

/// <summary>
/// The help window: a searchable list of topics on the left, the topic on the right
/// (<c>E10-T13</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A window rather than a dock pane, deliberately.</b> Help is consulted, not inhabited: it is
/// opened when a user is stuck, read, and closed. A pane would take permanent room in a layout
/// whose whole point is that the canvas and the viewport get it, and it would add a fifth member
/// to <c>WorkspacePane</c> that every preset, every serialised layout and every layout test would
/// have to learn about, for a panel most sessions never open.
/// </para>
/// <para>
/// <b>The list joins two sources and does not distinguish them.</b> Hand-written concept topics
/// and generated node pages sit in one list, because a reader looking for "fillet" does not care
/// which kind of page answers them, and a split list would make them choose before they know.
/// </para>
/// </remarks>
public sealed class HelpWindow : Window
{
    private readonly HelpLibrary _library;
    private readonly HelpView _view = new();
    private readonly ListBox _list = new();
    private readonly TextBox _search = new();
    private readonly ObservableCollection<Entry> _entries = [];
    private readonly List<string> _history = [];

    // Spelt here rather than referenced from Spark.Engine: the window is a view, and ADR-0005 keeps
    // views off engine types. These are topic ids, which are strings in the library either way.
    private const string NodePrefix = "nodes.";
    private const string NodeIndexId = "nodes.index";
    private const string DiagnosticPrefix = "diagnostics.";
    private const string DiagnosticIndexId = "diagnostics.index";

    /// <summary>Creates the window over a help library.</summary>
    /// <param name="library">Every topic available, hand-written and generated alike.</param>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    public HelpWindow(HelpLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        _library = library;

        Title = "Spark Help";
        Width = 1000;
        Height = 700;
        Background = SparkPalette.Frozen(SparkPalette.BackgroundVoid);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _search.PlaceholderText = "Search help";
        _search.Margin = new Thickness(10, 10, 10, 6);
        _search.TextChanged += (_, _) => Refresh(_search.Text);

        _list.ItemsSource = _entries;
        _list.Margin = new Thickness(4, 0, 4, 8);
        _list.SelectionChanged += OnListSelectionChanged;
        // The item may be null: Avalonia builds the template with a null datum while measuring
        // and while recycling virtualised rows, and dereferencing it there takes the application
        // down with a NullReferenceException from inside the list. Found by scrolling far enough
        // down the node index for virtualisation to start.
        _list.ItemTemplate = new FuncDataTemplate<Entry?>((entry, _) => new TextBlock
        {
            Text = entry?.Label ?? string.Empty,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 4, 6, 4),
            Foreground = SparkPalette.TextPrimaryBrush,
        });

        _view.LinkClicked += (_, target) => Navigate(target);

        DockPanel sidebar = new() { Width = 300, Background = SparkPalette.Frozen(SparkPalette.SurfaceSunken) };
        DockPanel.SetDock(_search, Avalonia.Controls.Dock.Top);
        sidebar.Children.Add(_search);
        sidebar.Children.Add(_list);

        DockPanel root = new();
        DockPanel.SetDock(sidebar, Avalonia.Controls.Dock.Left);
        root.Children.Add(sidebar);
        root.Children.Add(_view);
        Content = root;

        Refresh(null);
    }

    /// <summary>The id of the topic on screen, or null.</summary>
    public string? CurrentTopicId => _view.Topic?.Id;

    /// <summary>How many entries the list is currently showing.</summary>
    public int VisibleEntryCount => _entries.Count;

    /// <summary>
    /// The labels the list is currently showing, in order, indentation included.
    /// </summary>
    /// <remarks>
    /// Exposed so a test can assert the <i>shape</i> of the navigation rather than only its size:
    /// that the generated pages sit under their index and are indented, which is a claim about the
    /// label text and the order together.
    /// </remarks>
    public IReadOnlyList<string> VisibleEntryLabels => [.. _entries.Select(entry => entry.Label)];

    /// <summary>Shows a topic by id, or the nearest thing to it.</summary>
    /// <param name="topicId">The topic to show. Unknown ids fall back to the index.</param>
    public void Navigate(string? topicId)
    {
        if (_library.TryGet(Resolve(topicId), out HelpDocument? topic) && topic is not null)
        {
            _history.Add(topic.Id);
            _view.Show(topic);
            SelectInList(topic.Id);
            return;
        }

        // A link that resolves to nothing is shown as nothing found rather than ignored: a reader
        // who clicked something and saw no reaction assumes the window is broken.
        _view.Show(null);
    }

    /// <summary>Shows the topic documenting a node, preferring a hand-written one.</summary>
    /// <param name="nodeKey">The node key, as <c>Package/Name</c>.</param>
    public void NavigateToNode(string? nodeKey)
    {
        HelpDocument? topic = _library.ForNode(nodeKey);
        if (topic is not null)
        {
            Navigate(topic.Id);
            return;
        }

        Navigate("nodes.index");
    }

    /// <summary>
    /// Turns a link target into a topic id.
    /// </summary>
    /// <remarks>
    /// <b>Help topics carry two kinds of link and both have to work.</b> Generated pages link by
    /// topic id, because they are produced at runtime and have no file. Hand-written topics link
    /// by relative path - <c>lacing.md</c> - and that is deliberate rather than legacy: those files
    /// are also read on GitHub, where a topic id is dead text and a path is a working link. So a
    /// target ending in <c>.md</c> is resolved to the topic whose id ends with the same name.
    /// </remarks>
    private string? Resolve(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return target;
        }

        string cleaned = target.Split('#')[0];
        if (!cleaned.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return cleaned;
        }

        string name = System.IO.Path.GetFileNameWithoutExtension(cleaned);
        foreach (HelpDocument topic in _library.Topics)
        {
            if (topic.Id.EndsWith("." + name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(topic.Id, name, StringComparison.OrdinalIgnoreCase))
            {
                return topic.Id;
            }
        }

        return cleaned;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_list.SelectedItem is Entry entry
            && _library.TryGet(entry.Id, out HelpDocument? topic)
            && topic is not null)
        {
            _view.Show(topic);
        }
    }

    private void SelectInList(string id)
    {
        for (int index = 0; index < _entries.Count; index++)
        {
            if (string.Equals(_entries[index].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                _list.SelectedIndex = index;
                return;
            }
        }
    }

    /// <summary>
    /// Rebuilds the list. With no query the whole library is shown, concept topics first; with one
    /// the ranked search results are shown instead.
    /// </summary>
    private void Refresh(string? query)
    {
        _entries.Clear();

        IReadOnlyList<HelpDocument> topics = string.IsNullOrWhiteSpace(query)
            ? Ordered()
            : _library.Search(query, 200);

        foreach (HelpDocument topic in topics)
        {
            _entries.Add(new Entry(topic.Id, Label(topic)));
        }
    }

    /// <summary>
    /// The default order: concepts, then each generated section behind its own index. Concepts
    /// first because they are the pages that answer <i>how does this work</i>, and a reader who
    /// opened help without searching usually has that question rather than a specific node in mind.
    /// </summary>
    /// <remarks>
    /// <b>A generated page is listed under the index it belongs to, never beside a concept.</b>
    /// There are 136 node pages and 19 <c>SPK####</c> pages against eleven hand-written topics, so
    /// a flat list is a list of generated pages with the topics lost in it — which is what the
    /// diagnostics were, until a reader asked what the <c>SPK</c> entries filling the top of their
    /// navigation were and whether they could be removed. They cannot: a node in error shows its
    /// code and this is where <i>read more</i> lands. So they are filed rather than deleted, the
    /// way the node pages already were.
    /// </remarks>
    private IReadOnlyList<HelpDocument> Ordered()
    {
        List<HelpDocument> concepts = [];
        List<HelpDocument> nodes = [];
        List<HelpDocument> diagnostics = [];
        HelpDocument? nodeIndex = null;
        HelpDocument? diagnosticIndex = null;

        foreach (HelpDocument topic in _library.Topics)
        {
            if (string.Equals(topic.Id, NodeIndexId, StringComparison.Ordinal))
            {
                nodeIndex = topic;
            }
            else if (string.Equals(topic.Id, DiagnosticIndexId, StringComparison.Ordinal))
            {
                diagnosticIndex = topic;
            }
            else if (topic.Id.StartsWith(NodePrefix, StringComparison.Ordinal))
            {
                nodes.Add(topic);
            }
            else if (topic.Id.StartsWith(DiagnosticPrefix, StringComparison.Ordinal))
            {
                diagnostics.Add(topic);
            }
            else
            {
                concepts.Add(topic);
            }
        }

        List<HelpDocument> ordered = [.. concepts];

        if (diagnosticIndex is not null)
        {
            ordered.Add(diagnosticIndex);
        }

        ordered.AddRange(diagnostics);

        if (nodeIndex is not null)
        {
            ordered.Add(nodeIndex);
        }

        ordered.AddRange(nodes);
        return ordered;
    }

    /// <summary>
    /// A row's label: a generated page is indented under the index it belongs to, so the two
    /// levels are visible without a tree control and without a second list.
    /// </summary>
    private static string Label(HelpDocument topic) =>
        IsGeneratedPage(topic.Id)
            ? string.Create(CultureInfo.InvariantCulture, $"    {topic.Title}")
            : topic.Title;

    private static bool IsGeneratedPage(string id) =>
        (id.StartsWith(NodePrefix, StringComparison.Ordinal)
            && !string.Equals(id, NodeIndexId, StringComparison.Ordinal))
        || (id.StartsWith(DiagnosticPrefix, StringComparison.Ordinal)
            && !string.Equals(id, DiagnosticIndexId, StringComparison.Ordinal));

    private sealed record Entry(string Id, string Label);
}
