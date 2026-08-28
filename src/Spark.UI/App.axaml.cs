using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Spark.UI.Theming;
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
            if (Options.ShowSplash)
            {
                StartWithSplash(desktop);
            }
            else
            {
                desktop.MainWindow = CreateShell();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Shows the splash, then builds the shell behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The shell is built from a posted continuation rather than inline, and that is the whole
    /// mechanism.</b> Constructing <see cref="MainWindowViewModel"/> imports the node library by
    /// reflection and evaluates the seeded graph, and it does both synchronously on this thread.
    /// Built inline, the splash would be created, shown, and then never painted until the work it
    /// exists to cover had already finished — an empty rectangle for two seconds, which is worse
    /// than no splash. Posting at <see cref="DispatcherPriority.Background"/> lets the render pass
    /// run first, so the splash is on screen before the expensive work starts.
    /// </para>
    /// <para>
    /// <b>The shell is shown before the splash is closed</b>, so the window count never reaches
    /// zero. Closing first would satisfy the desktop lifetime's shutdown-on-last-window-closed
    /// rule and exit the application between the two statements.
    /// </para>
    /// </remarks>
    /// <param name="desktop">The desktop lifetime.</param>
    private void StartWithSplash(IClassicDesktopStyleApplicationLifetime desktop)
    {
        SplashWindow splash = new() { Icon = SparkLogo.CreateWindowIcon() };
        splash.Show();

        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    MainWindow shell = CreateShell();
                    desktop.MainWindow = shell;
                    shell.Show();
                }
                finally
                {
                    // In a finally block because a splash left on top of a crashed startup hides
                    // the dialog telling you what went wrong.
                    splash.Close();
                }
            },
            DispatcherPriority.Background);
    }

    private MainWindow CreateShell() => new()
    {
        DataContext = new MainWindowViewModel(Options.Graph, Options.OpenPath),
        Options = Options,
        Icon = SparkLogo.CreateWindowIcon(),
    };
}
