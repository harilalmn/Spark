using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Spark.UI.ViewModels;
using Spark.UI.Views;

namespace Spark.UI;

/// <summary>
/// The Avalonia application object: loads the theme and opens the main window.
/// </summary>
/// <remarks>
/// Composition happens here rather than in <c>Spark.Desktop</c>'s entry point because the same
/// application object has to serve an embedded host later — a CAD add-in owns the process and the
/// message loop, and only hands Spark a lifetime.
/// </remarks>
public sealed class App : Application
{
    /// <summary>
    /// Options the entry point passes through, chiefly the benchmark switches.
    /// </summary>
    public StartupOptions Options { get; set; } = StartupOptions.Default;

    /// <inheritdoc/>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Before the view model is constructed, because it loads installed packages there and
            // an organisation's feed is not a setting that can arrive afterwards.
            MainWindowViewModel.PackageSource = Options.PackageSource;
            MainWindowViewModel.FreezeFirst = Options.FreezeFirst;

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(Options.Graph, Options.OpenPath, Options.NoScript),
                Options = Options,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
