using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Spark.UI.Theming;
using Spark.UI.ViewModels;

namespace Spark.UI.Views;

/// <summary>
/// The package manager: search a feed, read what a package is, install it, and remove what is
/// installed (<c>E7-T10</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A window rather than a dock pane</b>, for the same reason help is one: packages are managed
/// occasionally and then forgotten, and a permanent panel would take room from the canvas and the
/// viewport and add a member to <c>WorkspacePane</c> that every layout preset and every layout
/// test would have to learn about.
/// </para>
/// <para>
/// <b>Everything this window knows about packages lives in
/// <see cref="PackageBrowserViewModel"/>.</b> That is not ceremony: <c>Spark.Architecture.Tests</c>
/// forbids a file under <c>Views</c> or <c>Controls</c> from naming the engine, and a package
/// browser that installs nodes into a library would otherwise name it on the first line.
/// </para>
/// <para>
/// <b>The disclosure is a gate, not a notice.</b> While one is pending the window shows it and
/// offers exactly two answers, because <c>E7-T8</c>'s whole point is that a user weighs a licence,
/// a publisher and native code <i>before</i> agreeing rather than after.
/// </para>
/// </remarks>
public sealed class PackageWindow : Window
{
    private readonly PackageBrowserViewModel _model;
    private readonly TextBox _query = new();
    private readonly Button _searchButton = new();
    private readonly Button _installButton = new();
    private readonly Button _confirmButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _removeButton = new();
    private readonly ListBox _results = new();
    private readonly ListBox _installed = new();
    private readonly TextBlock _status = new();
    private readonly SelectableTextBlock _disclosure = new();
    private readonly SelectableTextBlock _native = new();
    private readonly Border _disclosurePanel = new();

    /// <summary>Creates the window over a browser.</summary>
    /// <param name="model">The browser's state and operations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public PackageWindow(PackageBrowserViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        _model = model;
        _model.PropertyChanged += OnModelChanged;

        Title = "Spark Packages";
        Width = 1000;
        Height = 660;
        Background = SparkPalette.Frozen(SparkPalette.BackgroundVoid);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Control search = BuildSearchRow();
        Control disclosure = BuildDisclosurePanel();
        Control status = BuildStatus();

        DockPanel root = new();
        DockPanel.SetDock(search, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(disclosure, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(status, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(search);
        root.Children.Add(disclosure);
        root.Children.Add(status);
        root.Children.Add(BuildLists());

        Content = root;
        Sync();
    }

    /// <summary>The browser this window is showing.</summary>
    public PackageBrowserViewModel Model => _model;

    /// <summary>Whether the install disclosure is on screen awaiting an answer.</summary>
    public bool IsShowingDisclosure => _disclosurePanel.IsVisible;

    /// <summary>The disclosure text as a user would read it, or empty.</summary>
    public string DisclosureText => _disclosure.Text ?? string.Empty;

    /// <summary>The native-code sentence as a user would read it, or empty.</summary>
    public string NativeNoticeText => _native.Text ?? string.Empty;

    /// <summary>The status line as a user would read it.</summary>
    public string StatusText => _status.Text ?? string.Empty;

    /// <summary>Selects a found package, as clicking its row would.</summary>
    /// <param name="index">The row, or -1 for none.</param>
    public void SelectFound(int index) => _results.SelectedIndex = index;

    /// <summary>Selects an installed package, as clicking its row would.</summary>
    /// <param name="index">The row, or -1 for none.</param>
    public void SelectInstalled(int index) => _installed.SelectedIndex = index;

    /// <summary>Whether the button that begins an install is available.</summary>
    public bool CanInstall => _installButton.IsEnabled;

    /// <summary>Whether the button that removes an installed package is available.</summary>
    public bool CanRemove => _removeButton.IsEnabled;

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            // Escape answers a pending install before it closes anything, because closing the
            // window on a prepared package would leave a download staged with nobody to answer
            // for it.
            if (_model.HasPendingInstall)
            {
                _model.Cancel();
            }
            else
            {
                Close();
            }

            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        // Anything prepared and unanswered is discarded rather than left in staging.
        _model.Cancel();
        _model.PropertyChanged -= OnModelChanged;
        base.OnClosed(e);
    }

    private Control BuildSearchRow()
    {
        _query.PlaceholderText = "Search " + _model.SourceLabel;
        _query.Margin = new Thickness(0, 0, 8, 0);
        _query.KeyDown += OnQueryKeyDown;
        _query.Text = _model.Query;

        _searchButton.Content = "Search";
        _searchButton.Click += OnSearch;

        DockPanel row = new() { Margin = new Thickness(12, 12, 12, 8) };
        DockPanel.SetDock(_searchButton, Avalonia.Controls.Dock.Right);
        row.Children.Add(_searchButton);
        row.Children.Add(_query);
        return row;
    }

    private Control BuildLists()
    {
        _results.ItemsSource = _model.Results;
        _results.ItemTemplate = RowTemplate();
        _results.SelectionChanged += (_, _) => Sync();

        _installed.ItemsSource = _model.Installed;
        _installed.ItemTemplate = RowTemplate();
        _installed.SelectionChanged += (_, _) => Sync();

        _installButton.Content = "Install...";
        _installButton.Click += OnPrepare;

        _removeButton.Content = "Remove";
        _removeButton.Click += OnRemove;

        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Margin = new Thickness(12, 0, 12, 8),
        };

        Control found = Column("Found", _results, _installButton);
        Control here = Column("Installed", _installed, _removeButton);
        Grid.SetColumn(here, 1);
        grid.Children.Add(found);
        grid.Children.Add(here);
        return grid;
    }

    private Control BuildStatus()
    {
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(12, 8, 12, 12);
        _status.Foreground = SparkPalette.TextSecondaryBrush;
        return _status;
    }

    private Control BuildDisclosurePanel()
    {
        _disclosure.TextWrapping = TextWrapping.Wrap;
        _disclosure.FontSize = 12.5;
        _disclosure.LineHeight = 19;
        _disclosure.Foreground = SparkPalette.TextPrimaryBrush;

        _native.TextWrapping = TextWrapping.Wrap;
        _native.FontSize = 12.5;
        _native.LineHeight = 19;
        _native.Margin = new Thickness(0, 10, 0, 0);

        _confirmButton.Content = "Install";
        _confirmButton.Click += OnConfirm;

        _cancelButton.Content = "Do not install";
        _cancelButton.Margin = new Thickness(8, 0, 0, 0);
        _cancelButton.Click += (_, _) => _model.Cancel();

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(_confirmButton);
        buttons.Children.Add(_cancelButton);

        StackPanel body = new();
        body.Children.Add(new TextBlock
        {
            Text = "What this package is, before you install it",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = SparkPalette.TextPrimaryBrush,
        });
        body.Children.Add(_disclosure);
        body.Children.Add(_native);
        body.Children.Add(buttons);

        _disclosurePanel.Child = body;
        _disclosurePanel.Padding = new Thickness(14);
        _disclosurePanel.Margin = new Thickness(12, 0, 12, 0);
        _disclosurePanel.Background = SparkPalette.Frozen(SparkPalette.SurfaceRaised);
        _disclosurePanel.IsVisible = false;
        return _disclosurePanel;
    }

    private static Control Column(string heading, ListBox list, Button action)
    {
        DockPanel panel = new() { Margin = new Thickness(0, 0, 6, 0) };

        TextBlock label = new()
        {
            Text = heading,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(2, 0, 0, 6),
            Foreground = SparkPalette.TextMutedBrush,
        };

        action.HorizontalAlignment = HorizontalAlignment.Left;
        action.Margin = new Thickness(0, 8, 0, 0);

        DockPanel.SetDock(label, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(action, Avalonia.Controls.Dock.Bottom);
        panel.Children.Add(label);
        panel.Children.Add(action);
        panel.Children.Add(list);
        return panel;
    }

    /// <summary>
    /// The row template. The datum may be null while Avalonia measures or recycles a virtualised
    /// row, and dereferencing it there takes the application down.
    /// </summary>
    private static IDataTemplate RowTemplate() => new FuncDataTemplate<PackageRow?>((row, _) =>
    {
        StackPanel item = new() { Margin = new Thickness(6, 5, 6, 5) };

        item.Children.Add(new TextBlock
        {
            Text = row is null ? string.Empty : row.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = SparkPalette.TextPrimaryBrush,
        });

        item.Children.Add(new TextBlock
        {
            Text = row is null ? string.Empty : row.Detail,
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = SparkPalette.TextMutedBrush,
        });

        return item;
    });

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSearch(sender, e);
            e.Handled = true;
        }
    }

    private async void OnSearch(object? sender, RoutedEventArgs e)
    {
        _model.Query = _query.Text ?? string.Empty;
        await _model.SearchAsync().ConfigureAwait(true);
    }

    private async void OnPrepare(object? sender, RoutedEventArgs e)
    {
        if (_results.SelectedItem is PackageRow row)
        {
            await _model.PrepareAsync(row).ConfigureAwait(true);
        }
    }

    private async void OnConfirm(object? sender, RoutedEventArgs e) =>
        await _model.ConfirmAsync().ConfigureAwait(true);

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (_installed.SelectedItem is PackageRow row)
        {
            _model.Remove(row);
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    /// <summary>Pulls the whole of the view model's state onto the controls.</summary>
    private void Sync()
    {
        _status.Text = _model.Status;
        _disclosure.Text = _model.Disclosure;
        _native.Text = _model.NativeNotice;
        _native.FontWeight = _model.CarriesNativeCode ? FontWeight.SemiBold : FontWeight.Normal;
        _native.Foreground = _model.CarriesNativeCode
            ? SparkPalette.Frozen(SparkPalette.StateWarning)
            : SparkPalette.TextMutedBrush;
        _disclosurePanel.IsVisible = _model.HasPendingInstall;

        bool idle = !_model.IsBusy;
        _searchButton.IsEnabled = idle;
        _installButton.IsEnabled = idle && !_model.HasPendingInstall && _results.SelectedItem is PackageRow;
        _removeButton.IsEnabled = idle && _installed.SelectedItem is PackageRow;
        _confirmButton.IsEnabled = idle;
        _cancelButton.IsEnabled = idle;
    }
}
