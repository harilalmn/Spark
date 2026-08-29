using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Spark.UI.ViewModels;

namespace Spark.UI.Views.Panes;

/// <summary>
/// The properties inspector and the diagnostics it sits above: the literals of the selected node,
/// and what the last run had to say.
/// </summary>
/// <remarks>
/// Both handlers here commit the row they are on and nothing else. The commit itself belongs to
/// <see cref="PortLiteralViewModel"/>, which knows how to parse the text and what to do when it
/// does not parse; this pane only decides <i>when</i> — losing focus, or pressing Enter.
/// </remarks>
public sealed partial class InspectorPane : UserControl
{
    /// <summary>Creates the pane.</summary>
    public InspectorPane() => InitializeComponent();

    private void OnLiteralCommitted(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PortLiteralViewModel literal })
        {
            literal.Commit();
        }
    }

    private void OnLiteralKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter and not Key.Return)
        {
            return;
        }

        if (sender is Control { DataContext: PortLiteralViewModel literal })
        {
            literal.Commit();
            e.Handled = true;
        }
    }
}
