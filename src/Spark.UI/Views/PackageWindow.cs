using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
    private readonly LocalReferencesViewModel _local;
    private readonly ListBox _assemblies = new();
    private readonly Button _addButton = new();
    private readonly Button _reloadButton = new();
    private readonly Button _forgetButton = new();
    private readonly Button _referenceButton = new();
    private readonly Button _declineButton = new();
    private readonly TextBlock _localStatus = new();
    private readonly SelectableTextBlock _promptText = new();
    private readonly Border _promptPanel = new();
    private readonly TabControl _tabs = new();

    /// <summary>Creates the window over a browser and a reference list.</summary>
    /// <param name="model">The browser's state and operations.</param>
    /// <param name="local">
    /// The local assemblies list, or null for an empty in-memory one. <b>A local DLL and a package
    /// are the same idea</b> — code from outside Spark, agreed to once and remembered —
    /// so they share a window rather than each getting one.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public PackageWindow(PackageBrowserViewModel model, LocalReferencesViewModel? local = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        _model = model;
        _model.PropertyChanged += OnModelChanged;

        _local = local ?? new LocalReferencesViewModel(new Spark.Host.LocalReferenceStore(path: null));
        _local.PropertyChanged += OnModelChanged;

        Title = "Spark Packages";
        Width = 1000;
        Height = 660;
        Background = SparkPalette.Frozen(SparkPalette.BackgroundVoid);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Control search = BuildSearchRow();
        Control disclosure = BuildDisclosurePanel();
        Control status = BuildStatus();

        DockPanel packages = new();
        DockPanel.SetDock(search, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(disclosure, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(status, Avalonia.Controls.Dock.Bottom);
        packages.Children.Add(search);
        packages.Children.Add(disclosure);
        packages.Children.Add(status);
        packages.Children.Add(BuildLists());

        _tabs.Items.Add(new TabItem { Header = "Packages", Content = packages });
        _tabs.Items.Add(new TabItem { Header = "Local assemblies", Content = BuildLocalTab() });

        Content = _tabs;
        Sync();
    }

    /// <summary>The browser this window is showing.</summary>
    public PackageBrowserViewModel Model => _model;

    /// <summary>The local assemblies list this window is showing.</summary>
    public LocalReferencesViewModel Local => _local;

    /// <summary>Whether the reference prompt is on screen awaiting an answer.</summary>
    public bool IsShowingPrompt => _promptPanel.IsVisible;

    /// <summary>The reference prompt as a user would read it, or empty.</summary>
    public string PromptText => _promptText.Text ?? string.Empty;

    /// <summary>Selects a local assembly, as clicking its row would.</summary>
    /// <param name="index">The row, or -1 for none.</param>
    public void SelectAssembly(int index) => _assemblies.SelectedIndex = index;

    /// <summary>Brings the local assemblies tab to the front.</summary>
    public void ShowLocalAssemblies() => _tabs.SelectedIndex = 1;

    /// <summary>Which tab is showing: 0 for packages, 1 for local assemblies.</summary>
    public int SelectedTab => _tabs.SelectedIndex;

    /// <summary>Whether the button that reloads a rebuilt assembly is available.</summary>
    public bool CanReload => _reloadButton.IsEnabled;

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
        _local.Cancel();
        _local.PropertyChanged -= OnModelChanged;
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

    /// <summary>
    /// The local assemblies tab: what is referenced, and the gate for adding one (<c>E7-T9</c>).
    /// </summary>
    private Control BuildLocalTab()
    {
        _assemblies.ItemsSource = _local.References;
        _assemblies.ItemTemplate = AssemblyTemplate();
        _assemblies.SelectionChanged += (_, _) => Sync();

        _addButton.Content = "Add a .dll...";
        _addButton.Click += OnAddAssembly;

        _reloadButton.Content = "Reload";
        _reloadButton.Click += OnReloadAssembly;

        _forgetButton.Content = "Forget";
        _forgetButton.Click += OnForgetAssembly;

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
        };
        actions.Children.Add(_addButton);
        _reloadButton.Margin = new Thickness(8, 0, 0, 0);
        _forgetButton.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(_reloadButton);
        actions.Children.Add(_forgetButton);

        _localStatus.TextWrapping = TextWrapping.Wrap;
        _localStatus.Margin = new Thickness(0, 8, 0, 0);
        _localStatus.Foreground = SparkPalette.TextSecondaryBrush;

        Control heading = BuildLocalHeading();
        Control prompt = BuildPromptPanel();

        DockPanel panel = new() { Margin = new Thickness(12) };
        DockPanel.SetDock(heading, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(prompt, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(_localStatus, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(actions, Avalonia.Controls.Dock.Bottom);
        panel.Children.Add(heading);
        panel.Children.Add(prompt);
        panel.Children.Add(_localStatus);
        panel.Children.Add(actions);
        panel.Children.Add(_assemblies);
        return panel;
    }

    private static Control BuildLocalHeading() => new TextBlock
    {
        Text = "Assemblies your code blocks can use. Spark reads them without locking them, "
            + "so you can rebuild while Spark is open.",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 0, 2, 10),
        Foreground = SparkPalette.TextMutedBrush,
    };

    private Control BuildPromptPanel()
    {
        _promptText.TextWrapping = TextWrapping.Wrap;
        _promptText.FontSize = 12.5;
        _promptText.LineHeight = 19;
        _promptText.Foreground = SparkPalette.TextPrimaryBrush;

        _referenceButton.Content = "Reference it";
        _referenceButton.Click += (_, _) => _local.Confirm();

        _declineButton.Content = "Do not reference";
        _declineButton.Margin = new Thickness(8, 0, 0, 0);
        _declineButton.Click += (_, _) => _local.Cancel();

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(_referenceButton);
        buttons.Children.Add(_declineButton);

        StackPanel body = new();
        body.Children.Add(new TextBlock
        {
            Text = "What this assembly is, before you reference it",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = SparkPalette.TextPrimaryBrush,
        });
        body.Children.Add(_promptText);
        body.Children.Add(buttons);

        _promptPanel.Child = body;
        _promptPanel.Padding = new Thickness(14);
        _promptPanel.Margin = new Thickness(0, 10, 0, 0);
        _promptPanel.Background = SparkPalette.Frozen(SparkPalette.SurfaceRaised);
        _promptPanel.IsVisible = false;
        return _promptPanel;
    }

    private static IDataTemplate AssemblyTemplate() => new FuncDataTemplate<LocalReferenceRow?>((row, _) =>
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
            // A rebuilt or missing assembly is the one thing in this list worth a colour: it is
            // not being compiled against until somebody looks at it.
            Foreground = row is { NeedsAttention: true }
                ? SparkPalette.Frozen(SparkPalette.StateWarning)
                : SparkPalette.TextMutedBrush,
        });

        return item;
    });

    private async void OnAddAssembly(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> chosen = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Add an assembly",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(".NET assembly") { Patterns = ["*.dll"] }],
            }).ConfigureAwait(true);

        if (chosen.Count > 0 && chosen[0].TryGetLocalPath() is { } path)
        {
            _local.Choose(path);
        }
    }

    private void OnReloadAssembly(object? sender, RoutedEventArgs e)
    {
        if (_assemblies.SelectedItem is LocalReferenceRow row)
        {
            _local.Reload(row);
        }
    }

    private void OnForgetAssembly(object? sender, RoutedEventArgs e)
    {
        if (_assemblies.SelectedItem is LocalReferenceRow row)
        {
            _local.Remove(row);
        }
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

        _localStatus.Text = _local.Status;
        _promptText.Text = _local.Prompt;
        _promptPanel.IsVisible = _local.HasPendingTrust;
        _reloadButton.IsEnabled = _assemblies.SelectedItem is LocalReferenceRow && !_local.HasPendingTrust;
        _forgetButton.IsEnabled = _assemblies.SelectedItem is LocalReferenceRow;
        _addButton.IsEnabled = !_local.HasPendingTrust;

        bool idle = !_model.IsBusy;
        _searchButton.IsEnabled = idle;
        _installButton.IsEnabled = idle && !_model.HasPendingInstall && _results.SelectedItem is PackageRow;
        _removeButton.IsEnabled = idle && _installed.SelectedItem is PackageRow;
        _confirmButton.IsEnabled = idle;
        _cancelButton.IsEnabled = idle;
    }
}
