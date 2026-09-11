using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Spark.UI.Theming;
using Spark.UI.ViewModels;

namespace Spark.UI.Views;

/// <summary>
/// Settings: the preferences Spark remembers between sessions, in one place (<c>E8-T12</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every change is saved as it is made</b>, and the window says so. There is no OK and no Cancel
/// because each preference already writes itself immediately - a setting that only persisted on a
/// button press would be lost by exactly the crash that made the user change it, which is the reason
/// <c>PreferenceFile</c> gives for writing at once.
/// </para>
/// <para>
/// <b>The old places still work.</b> The update check stays in the Help menu, where PRD NFR-13 needs a
/// user to be able to find the one outbound request Spark makes; the code font and the tidy switch
/// stay in the properties pane beside a code block. All of them read the same preferences, so they
/// cannot disagree.
/// </para>
/// </remarks>
public sealed class SettingsWindow : Window
{
    /// <summary>Creates the window over the session's settings.</summary>
    /// <param name="settings">The settings to show and change.</param>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    public SettingsWindow(SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Title = "Settings";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = SparkPalette.Frozen(SparkPalette.BackgroundVoid);

        UpdatesSetting = new CheckBox
        {
            Content = "Check for a newer version when Spark starts",
            IsChecked = settings.ChecksForUpdates,
            Foreground = SparkPalette.TextPrimaryBrush,
        };
        UpdatesSetting.IsCheckedChanged += (_, _) => settings.ChecksForUpdates = UpdatesSetting.IsChecked == true;

        CodeFontSetting = new ComboBox
        {
            ItemsSource = SettingsViewModel.CodeFontNames,
            SelectedItem = settings.CodeFontName,
            MinWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        CodeFontSetting.SelectionChanged += (_, _) =>
        {
            if (CodeFontSetting.SelectedItem is string name)
            {
                settings.CodeFontName = name;
            }
        };

        TidySetting = new CheckBox
        {
            Content = "Tidy the finished lines of a code block when Enter is pressed",
            IsChecked = settings.FormatsOnLineBreak,
            Foreground = SparkPalette.TextPrimaryBrush,
        };
        TidySetting.IsCheckedChanged += (_, _) => settings.FormatsOnLineBreak = TidySetting.IsChecked == true;

        Button close = new() { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        close.Click += (_, _) => Close();

        StackPanel body = new() { Spacing = 18, Margin = new Thickness(28, 24, 28, 24) };

        body.Children.Add(Section(
            "Updates",
            UpdatesSetting,
            Note("The only request Spark makes on its own behalf. It sends nothing about you or your graphs.")));

        body.Children.Add(Section(
            "Code blocks",
            Labelled("Font", CodeFontSetting),
            Note("Only monospaced faces are offered, because a code block's width is counted in characters."),
            TidySetting));

        body.Children.Add(Note("Each setting is saved as soon as it changes."));
        body.Children.Add(close);

        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = body,
        };
    }

    /// <summary>Whether Spark checks for updates; internal so a test can drive it.</summary>
    internal CheckBox UpdatesSetting { get; }

    /// <summary>The code font; internal so a test can drive it.</summary>
    internal ComboBox CodeFontSetting { get; }

    /// <summary>Whether Enter tidies a code block; internal so a test can drive it.</summary>
    internal CheckBox TidySetting { get; }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private static Control Section(string heading, params Control[] rows)
    {
        StackPanel section = new() { Spacing = 8 };

        section.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = SparkPalette.TextPrimaryBrush,
        });

        foreach (Control row in rows)
        {
            section.Children.Add(row);
        }

        return section;
    }

    private static Control Labelled(string label, Control control)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 12 };

        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SparkPalette.TextPrimaryBrush,
        });
        row.Children.Add(control);

        return row;
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = SparkPalette.TextMutedBrush,
    };
}
