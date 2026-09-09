using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Spark.Api;

namespace Spark.UI.Views.Panes;

/// <summary>
/// What a graph wrote, for a person to read (<c>E8-T80</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>, together with the three methods and the three nodes that fill
/// it. It is a <i>reader</i> and not a terminal: there is nothing to type into, because the graph
/// is what produces the text and the canvas is where that is edited.
/// </para>
/// <para>
/// <b>The pane owns the subscription, and only while it is on screen.</b>
/// <see cref="SparkConsole.Changed"/> is static, so anything that subscribes for its whole life is
/// kept alive by it — and the first version of this had the view model subscribe in its
/// constructor. Every view model a test built stayed subscribed and went on posting to a dispatcher
/// after its headless session had ended, which turned unrelated tests red at random. Attaching to
/// the visual tree and detaching from it bounds the lifetime to something that genuinely exists.
/// </para>
/// <para>
/// <b>It follows the tail, unless the reader has scrolled away from it.</b> A console that keeps
/// its position while lines arrive shows the part already read; one that always jumps to the end
/// drags somebody away from the line they went back to look at. So it scrolls to the end only when
/// it was already there.
/// </para>
/// </remarks>
public sealed partial class ConsolePane : UserControl
{
    private bool _following = true;

    /// <summary>What the pane is showing, as a test would read it.</summary>
    public string Text => Output.Text ?? string.Empty;

    /// <summary>Creates the pane.</summary>
    public ConsolePane()
    {
        InitializeComponent();

        Clear.Click += (_, _) => SparkConsole.Clear();

        Scroller.ScrollChanged += (_, _) =>
            _following = Scroller.Offset.Y >= Scroller.Extent.Height - Scroller.Viewport.Height - 1;
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        SparkConsole.Changed += OnConsoleChanged;
        Refresh();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        SparkConsole.Changed -= OnConsoleChanged;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// A write has happened, on whatever thread the node that made it was running on.
    /// </summary>
    /// <remarks>
    /// <b>A graph evaluates on the thread pool</b>, so the read and the assignment are posted to
    /// the user interface thread. <see cref="Dispatcher.UIThread"/> is asked whether it already is
    /// that thread, because posting from it would leave the pane a frame behind for no reason.
    /// </remarks>
    private void OnConsoleChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    private void Refresh()
    {
        Output.Text = SparkConsole.Text();

        // Said out loud, because the buffer is bounded: somebody reading from the top of a console
        // that has quietly dropped nine thousand lines is reading the wrong thing and does not know
        // it.
        int dropped = SparkConsole.Dropped;
        Dropped.Text = dropped > 0
            ? string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{dropped} earlier line(s) dropped — the console keeps the last {SparkConsole.Capacity:N0}.")
            : string.Empty;

        if (_following)
        {
            Scroller.ScrollToEnd();
        }
    }
}
