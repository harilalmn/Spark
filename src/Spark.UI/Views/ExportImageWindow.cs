using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Spark.UI.Canvas;
using Spark.UI.Theming;

namespace Spark.UI.Views;

/// <summary>
/// Asks how large an exported image should be (<c>E8-T69</c>, <c>E9-T15</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: <i>user should be able to set the export resolution, with
/// option to lock aspect ratio and default width and height being the canvas size.</i> Both
/// exports use this one dialog — the graph and the viewport — because two dialogs asking the same
/// question is how they come to disagree about what the lock does.
/// </para>
/// <para>
/// <b>The defaults are the surface's own size, and that is what makes the dialog answerable.</b>
/// Somebody who does not care presses Enter and gets a picture of what they were already looking
/// at; somebody who does has the number they are scaling <i>from</i> in front of them.
/// </para>
/// <para>
/// <b>The aspect ratio is taken once, when the dialog opens, and never re-derived.</b> Reading it
/// back out of the two boxes means each keystroke derives one from the other from the first, and
/// the ratio drifts by a rounding error every time. <see cref="CanvasExport"/> holds the
/// arithmetic, and holds it away from this window so it can be tested without one.
/// </para>
/// </remarks>
public sealed class ExportImageWindow : Window
{
    private readonly NumericUpDown _width;
    private readonly NumericUpDown _height;
    private readonly CheckBox _lock;
    private readonly double _aspect;

    private bool _deriving;

    /// <summary>Creates the dialog over a surface of a given size.</summary>
    /// <param name="what">What is being exported, for the title: <c>graph</c> or <c>viewport</c>.</param>
    /// <param name="defaultWidth">The surface's own width in pixels, which is the default.</param>
    /// <param name="defaultHeight">Its own height, likewise.</param>
    public ExportImageWindow(string what, int defaultWidth, int defaultHeight)
    {
        int startWidth = CanvasExport.Clamp(defaultWidth);
        int startHeight = CanvasExport.Clamp(defaultHeight);

        _aspect = CanvasExport.Aspect(startWidth, startHeight);

        Title = "Export " + what + " as PNG";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = SparkPalette.Frozen(SparkPalette.BackgroundVoid);

        _width = Box(startWidth);
        _height = Box(startHeight);

        _lock = new CheckBox
        {
            Content = "Lock aspect ratio",
            IsChecked = true,
            Foreground = SparkPalette.TextSecondaryBrush,
        };

        _width.ValueChanged += (_, _) => Derive(fromWidth: true);
        _height.ValueChanged += (_, _) => Derive(fromWidth: false);

        Button export = new()
        {
            Content = "Export…",
            IsDefault = true,
            MinWidth = 96,
        };

        Button cancel = new()
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 96,
        };

        export.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };

        cancel.Click += (_, _) => Close();

        Grid fields = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 8,
        };

        fields.Children.Add(Label("Width", 0));
        fields.Children.Add(Cell(_width, 0));
        fields.Children.Add(Label("Height", 1));
        fields.Children.Add(Cell(_height, 1));

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(export);

        StackPanel body = new() { Spacing = 14, Margin = new Thickness(22, 20, 22, 20) };

        body.Children.Add(new TextBlock
        {
            Text = string.Create(
                CultureInfo.CurrentCulture,
                $"The {what} is fitted to the image, whatever size you choose. It is {startWidth} × {startHeight} on screen."),
            TextWrapping = TextWrapping.NoWrap,
            Foreground = SparkPalette.TextMutedBrush,
            FontSize = 11,
        });

        body.Children.Add(fields);
        body.Children.Add(_lock);
        body.Children.Add(buttons);

        Content = body;
    }

    /// <summary>Whether the user pressed Export rather than closing the dialog.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>The chosen width in pixels.</summary>
    public int PixelWidth => CanvasExport.Clamp((int)(_width.Value ?? CanvasExport.MinimumPixels));

    /// <summary>The chosen height in pixels.</summary>
    public int PixelHeight => CanvasExport.Clamp((int)(_height.Value ?? CanvasExport.MinimumPixels));

    /// <summary>Whether the aspect ratio is being held.</summary>
    public bool LocksAspect => _lock.IsChecked == true;

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

    /// <summary>
    /// Keeps the other box at the ratio the dialog opened with.
    /// </summary>
    /// <param name="fromWidth">Which box was typed into.</param>
    /// <remarks>
    /// <b>Re-entrant, and the guard is the whole of why this is a method.</b> Setting the other
    /// box raises its own <c>ValueChanged</c>, which would derive the first one back from it —
    /// two boxes each deriving from the other is an oscillation, and with rounding it is a
    /// divergent one.
    /// </remarks>
    private void Derive(bool fromWidth)
    {
        if (_deriving || !LocksAspect)
        {
            return;
        }

        _deriving = true;

        try
        {
            if (fromWidth)
            {
                _height.Value = CanvasExport.HeightFor((int)(_width.Value ?? 0), _aspect);
            }
            else
            {
                _width.Value = CanvasExport.WidthFor((int)(_height.Value ?? 0), _aspect);
            }
        }
        finally
        {
            _deriving = false;
        }
    }

    private static NumericUpDown Box(int value) => new()
    {
        Value = value,
        Minimum = CanvasExport.MinimumPixels,
        Maximum = CanvasExport.MaximumPixels,
        Increment = 100,
        FormatString = "0",
        ParsingNumberStyle = NumberStyles.Integer,
    };

    private static TextBlock Label(string text, int row)
    {
        TextBlock label = new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SparkPalette.TextSecondaryBrush,
        };

        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);

        return label;
    }

    private static Control Cell(Control control, int row)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);

        return control;
    }
}
