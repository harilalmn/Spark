using Avalonia.Controls;
using Spark.UI.Controls;

namespace Spark.UI.Views.Panes;

/// <summary>The 3D viewport, in the pane border the rest of the shell uses.</summary>
/// <remarks>
/// A wrapper this thin earns its place for one reason: the viewport has to be something the shell
/// can hand to a dock as a whole pane, border and all, without the docking layout knowing that a
/// GPU surface is inside it.
/// </remarks>
public sealed partial class ViewportPane : UserControl
{
    /// <summary>Creates the pane.</summary>
    public ViewportPane() => InitializeComponent();

    /// <summary>
    /// The viewport control itself. Exposed because the window's screenshot and framing paths
    /// drive it directly.
    /// </summary>
    public ViewportControl Viewport => View;
}
