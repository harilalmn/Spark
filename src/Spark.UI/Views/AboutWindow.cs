using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Spark.Api;
using Spark.UI.Theming;

namespace Spark.UI.Views;

/// <summary>
/// The About box (<c>E12-T18</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a licence obligation as much as a courtesy.</b> The Open CASCADE exception requires
/// prominent notice that the work uses facilities provided by the Open CASCADE Technology
/// software, and About is where a user with only an installer looks. <c>E13-T16</c> and
/// <c>R21</c> both name it. <b>Nothing here is legal advice</b> — <c>Q13</c> is with counsel.
/// </para>
/// <para>
/// <b>The text lives in <see cref="ProductNotice"/>, not here.</b> The command line prints the
/// same notice from <c>spark --version</c>, and two copies of a licence statement is one copy that
/// eventually stops matching the build.
/// </para>
/// </remarks>
public sealed class AboutWindow : Window
{
    /// <summary>Creates the About box for the current process.</summary>
    /// <param name="version">The application version, or null.</param>
    /// <param name="kernelDescription">
    /// How the loaded solid-modelling kernel describes itself, or null when none is loaded.
    /// </param>
    public AboutWindow(string? version, string? kernelDescription)
    {
        Title = "About " + ProductNotice.ProductName;
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = SparkPalette.Frozen(SparkPalette.BackgroundVoid);

        StackPanel body = new() { Spacing = 14, Margin = new Thickness(28, 24, 28, 24) };

        body.Children.Add(new SelectableTextBlock
        {
            Text = ProductNotice.ProductName,
            FontSize = 26,
            FontWeight = FontWeight.SemiBold,
            Foreground = SparkPalette.TextPrimaryBrush,
        });

        foreach (NoticeLine line in ProductNotice.Build(version, kernelDescription))
        {
            body.Children.Add(Entry(line));
        }

        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = body,
        };
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is Key.Escape or Key.Enter)
        {
            Close();
            e.Handled = true;
        }
    }

    /// <summary>
    /// One labelled paragraph. Selectable, because a user reporting a problem needs to copy the
    /// version and the kernel line out of here, and a notice they cannot quote is a notice they
    /// retype wrongly.
    /// </summary>
    private static Control Entry(NoticeLine line)
    {
        StackPanel entry = new() { Spacing = 3 };

        entry.Children.Add(new TextBlock
        {
            Text = line.Label,
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = SparkPalette.TextMutedBrush,
        });

        entry.Children.Add(new SelectableTextBlock
        {
            Text = line.Text,
            FontSize = 13.5,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            Foreground = SparkPalette.TextPrimaryBrush,
        });

        return entry;
    }
}
