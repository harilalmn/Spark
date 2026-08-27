using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spark.UI.Shell;

namespace Spark.UI.ViewModels;

/// <summary>
/// The main window's view model. Deliberately thin: the canvas and the viewport own their own
/// state because both are drawn by hand rather than bound, and pushing two thousand nodes through
/// change notification would be the exact cost ADR-0013 exists to avoid.
/// </summary>
/// <remarks>
/// <para>
/// <b>CommunityToolkit.Mvvm, not ReactiveUI</b> — fewer concepts for a drive-by contributor, and
/// no runtime reflection on property change.
/// </para>
/// <para>
/// This view model knows nothing about <c>Spark.Engine</c>, and neither does any view. Evaluation
/// arrives later over a progress channel; nothing here blocks the UI thread and nothing here is
/// allowed to start.
/// </para>
/// </remarks>
public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "Ready.";

    [ObservableProperty]
    private string _viewportStatusText = "Waiting for the OpenGL context.";

    [ObservableProperty]
    private string _selectedWorkspace = "Default";

    /// <summary>Creates the view model with the default workspace layout.</summary>
    public MainWindowViewModel()
    {
        Layout = WorkspaceLayout.Default;
    }

    /// <summary>The shell's pane arrangement.</summary>
    public WorkspaceLayout Layout { get; }

    /// <summary>The named workspace presets, for the workspace selector.</summary>
    public IReadOnlyList<string> Workspaces { get; } = ["Default", "Modelling", "Authoring", "Presenting"];

    /// <summary>Applies a named preset, or the default when the name is not one of them.</summary>
    /// <param name="name">The preset name.</param>
    [RelayCommand]
    public void ApplyWorkspace(string? name)
    {
        IReadOnlyDictionary<string, WorkspaceLayout> presets = WorkspaceLayout.Presets();

        if (name is null || !presets.TryGetValue(name, out WorkspaceLayout? preset))
        {
            preset = WorkspaceLayout.Default;
            name = "Default";
        }

        Layout.CopyFrom(preset);
        SelectedWorkspace = name;
        StatusText = string.Create(CultureInfo.InvariantCulture, $"Workspace '{name}' applied.");
        OnPropertyChanged(nameof(Layout));
    }

    /// <summary>Returns every pane to its default size and makes them all visible.</summary>
    [RelayCommand]
    public void ResetLayout() => ApplyWorkspace("Default");
}
