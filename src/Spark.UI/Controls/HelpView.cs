using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Spark.Api.Help;
using Spark.UI.Theming;

namespace Spark.UI.Controls;

/// <summary>
/// Draws a <see cref="HelpDocument"/> (<c>E10-T13</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole of the Markdown knowledge is in <c>Spark.Api</c>, and none of it is here.</b> This
/// control walks a block list and makes text controls; it does not know what an asterisk means.
/// That split is what lets the documentation harness and the command line read the same topics
/// without either of them depending on Avalonia, and it is why the parser could be tested against
/// the real corpus before any of this existed.
/// </para>
/// <para>
/// Links are not followed here either. <see cref="LinkClicked"/> reports the target and the host
/// decides &#8212; a topic id navigates, a URL opens a browser &#8212; which keeps a decision
/// about the user's machine out of a rendering control.
/// </para>
/// </remarks>
public sealed class HelpView : UserControl
{
    private readonly StackPanel _blocks = new() { Spacing = 10 };

    /// <summary>Creates an empty view.</summary>
    public HelpView()
    {
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Padding = new Thickness(24, 20, 24, 32),
            Content = _blocks,
        };
    }

    /// <summary>Raised when a reader clicks a link. The argument is the link's target.</summary>
    public event EventHandler<string>? LinkClicked;

    /// <summary>The topic currently shown, or null.</summary>
    public HelpDocument? Topic { get; private set; }

    /// <summary>Shows a topic, replacing whatever was there.</summary>
    /// <param name="topic">The topic, or null to clear.</param>
    public void Show(HelpDocument? topic)
    {
        Topic = topic;
        _blocks.Children.Clear();

        if (topic is null)
        {
            _blocks.Children.Add(Paragraph([
                new HelpInline(HelpInlineKind.Text, "No help topic for this. Press Escape to close."),
            ]));
            return;
        }

        foreach (HelpBlock block in topic.Blocks)
        {
            Control? control = Build(block);
            if (control is not null)
            {
                _blocks.Children.Add(control);
            }
        }
    }

    private Control? Build(HelpBlock block) => block.Kind switch
    {
        HelpBlockKind.Heading => Heading(block),
        HelpBlockKind.Paragraph => Paragraph(block.Inlines),
        HelpBlockKind.ListItem => ListItem(block),
        HelpBlockKind.Quote => Quote(block),
        HelpBlockKind.Code => Code(block),
        HelpBlockKind.Table => Table(block),
        HelpBlockKind.Rule => Rule(),
        _ => null,
    };

    private Control Heading(HelpBlock block)
    {
        // The design language's type scale, compressed: a help topic that used six distinct sizes
        // would look like a specification rather than something to read.
        double size = block.Level switch
        {
            1 => 22,
            2 => 17,
            3 => 15,
            _ => 14,
        };

        SelectableTextBlock heading = new()
        {
            FontSize = size,
            FontWeight = block.Level <= 2 ? FontWeight.SemiBold : FontWeight.Medium,
            Foreground = SparkPalette.TextPrimaryBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, block.Level == 1 ? 0 : 10, 0, 0),
        };

        Fill(heading.Inlines!, block.Inlines, size);
        return heading;
    }

    private Control Paragraph(IReadOnlyList<HelpInline> inlines)
    {
        // A fixed line height is what gives body text its rhythm, and it is also what clips a line
        // containing a link: an inline link is a real control, so it makes the line box taller than
        // the text, and a fixed height then cuts the descenders off every word beside it. Found by
        // photographing the node index, where every entry is a link followed by prose.
        bool hasLink = false;
        foreach (HelpInline inline in inlines)
        {
            if (inline.Kind == HelpInlineKind.Link)
            {
                hasLink = true;
                break;
            }
        }

        SelectableTextBlock text = new()
        {
            FontSize = 13.5,
            Foreground = SparkPalette.TextPrimaryBrush,
            TextWrapping = TextWrapping.Wrap,
        };

        if (!hasLink)
        {
            text.LineHeight = 21;
        }

        Fill(text.Inlines!, inlines, 13.5);
        return text;
    }

    private Control ListItem(HelpBlock block)
    {
        DockPanel row = new() { Margin = new Thickness(6, 0, 0, 0) };

        TextBlock bullet = new()
        {
            Text = "•",
            FontSize = 13.5,
            Margin = new Thickness(0, 0, 8, 0),
            Foreground = SparkPalette.TextSecondaryBrush,
            VerticalAlignment = VerticalAlignment.Top,
        };

        DockPanel.SetDock(bullet, Avalonia.Controls.Dock.Left);
        row.Children.Add(bullet);
        row.Children.Add(Paragraph(block.Inlines));
        return row;
    }

    private Control Quote(HelpBlock block)
    {
        Border border = new()
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = SparkPalette.AccentBrush,
            Padding = new Thickness(12, 2, 0, 2),
            Child = Paragraph(block.Inlines),
        };

        return border;
    }

    private static Control Code(HelpBlock block) => new Border
    {
        Background = SparkPalette.Frozen(SparkPalette.SurfaceSunken),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(12, 10, 12, 10),
        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new SelectableTextBlock
            {
                Text = block.Text ?? string.Empty,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
                FontSize = 12.5,
                Foreground = SparkPalette.TextPrimaryBrush,
            },
        },
    };

    private Control Table(HelpBlock block)
    {
        Grid grid = new() { ColumnSpacing = 18, RowSpacing = 6 };

        int columns = 0;
        foreach (IReadOnlyList<IReadOnlyList<HelpInline>> row in block.Rows)
        {
            columns = Math.Max(columns, row.Count);
        }

        for (int column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        for (int row = 0; row < block.Rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (int column = 0; column < block.Rows[row].Count; column++)
            {
                SelectableTextBlock cell = new()
                {
                    FontSize = 13,
                    // The header row is the first one; the alignment row of dashes was dropped by
                    // the parser, so row 0 is genuinely the header rather than usually the header.
                    FontWeight = row == 0 ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = row == 0 ? SparkPalette.TextSecondaryBrush : SparkPalette.TextPrimaryBrush,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420,
                };

                Fill(cell.Inlines!, block.Rows[row][column], 13);
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = grid,
        };
    }

    private static Control Rule() => new Border
    {
        Height = 1,
        Margin = new Thickness(0, 8, 0, 8),
        Background = SparkPalette.Frozen(SparkPalette.BorderHairline),
    };

    private void Fill(InlineCollection target, IReadOnlyList<HelpInline> inlines, double size)
    {
        foreach (HelpInline inline in inlines)
        {
            switch (inline.Kind)
            {
                case HelpInlineKind.Strong:
                    target.Add(new Run(inline.Text) { FontWeight = FontWeight.SemiBold });
                    break;

                case HelpInlineKind.Emphasis:
                    target.Add(new Run(inline.Text) { FontStyle = FontStyle.Italic });
                    break;

                case HelpInlineKind.Code:
                    target.Add(new Run(inline.Text)
                    {
                        FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
                        FontSize = size - 1,
                        Foreground = SparkPalette.AccentBrush,
                    });
                    break;

                case HelpInlineKind.Link:
                    target.Add(BuildLink(inline));
                    break;

                default:
                    target.Add(new Run(inline.Text));
                    break;
            }
        }
    }

    /// <summary>
    /// Builds a clickable inline link.
    /// </summary>
    /// <remarks>
    /// An <see cref="InlineUIContainer"/> around a flat button, rather than a styled
    /// <see cref="Run"/>. A <c>Run</c> is not a control and raises no pointer events, so an
    /// underlined run would <i>look</i> like a link and do nothing when clicked - which is worse
    /// than not styling it at all. The cost is that a link does not break across lines; link
    /// labels in these topics are short phrases, so that is a trade rather than a defect.
    /// </remarks>
    private Inline BuildLink(HelpInline inline)
    {
        Button link = new()
        {
            Content = inline.Text,
            Foreground = SparkPalette.AccentBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            FontSize = 13.5,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };

        link.Click += (_, _) =>
        {
            if (inline.Target is { } target)
            {
                LinkClicked?.Invoke(this, target);
            }
        };

        return new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.TextBottom };
    }
}
