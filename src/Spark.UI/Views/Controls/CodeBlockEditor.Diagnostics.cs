using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Spark.UI.Theming;

namespace Spark.UI.Views.Controls;

/// <summary>One compiler message, in the coordinates the editor draws in.</summary>
/// <param name="Line">The user's line, one-based.</param>
/// <param name="Column">The column on that line, one-based.</param>
/// <param name="Length">How many characters it covers, at least one.</param>
/// <param name="Id">The compiler's code — <c>CS0103</c>.</param>
/// <param name="Message">What it says.</param>
/// <param name="IsError">True for an error, false for a warning.</param>
public readonly record struct CodeDiagnostic(
    int Line,
    int Column,
    int Length,
    string Id,
    string Message,
    bool IsError);

/// <summary>What sits under the pointer: a symbol, and what its documentation says.</summary>
/// <param name="Signature">The symbol as it would be written.</param>
/// <param name="Summary">Its <c>&lt;summary&gt;</c>, or null.</param>
public readonly record struct CodeQuickInfo(string Signature, string? Summary);

/// <summary>
/// Squiggles under the compiler's complaints, and a tooltip that explains them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ported from RCS's <c>DiagnosticsRenderer</c> and the hover half of its <c>CodeEditor</c></b>
/// (<c>C:\Zyeta\Projects\RCS</c>). Analysis runs behind the typing rather than in front of it: an
/// idle delay restarts on every keystroke, so a burst of typing costs one compile at the end of it
/// and not one per character.
/// </para>
/// <para>
/// <b>The squiggles come from the same compile the DIAGNOSTICS panel does</b>, through
/// <c>ScriptNodeFactory.Diagnose</c>, which wraps the script exactly as the real compile does. A
/// second Roslyn workspace configured slightly differently would have been quicker and would
/// eventually underline something that compiles — and `E6-T13` is explicit that a language service
/// which disagrees with the compiler is worse than none at all.
/// </para>
/// </remarks>
public sealed partial class CodeBlockEditor
{
    /// <summary>How long the editor stays quiet before asking the compiler what it thinks.</summary>
    /// <remarks>
    /// RCS's 400 ms. Long enough that ordinary typing never triggers it, short enough that stopping
    /// to think produces underlines before you have started reading the code again.
    /// </remarks>
    private const int AnalysisDelayMilliseconds = 400;

    private readonly List<CodeDiagnostic> _diagnostics = [];

    private DispatcherTimer? _analysisTimer;
    private CancellationTokenSource? _analysis;
    private ToolTip? _hover;
    private int _hoverOffset = -1;

    /// <summary>Asks the compiler what is wrong with the block, on idle.</summary>
    /// <remarks>
    /// A delegate for the reason <see cref="CompletionSource"/> is one: the control is testable
    /// without a compiler behind it, and the shell decides what a code block is compiled against.
    /// </remarks>
    public Func<string, CancellationToken, Task<IReadOnlyList<CodeDiagnostic>>>? DiagnosticsSource { get; set; }

    /// <summary>Asks what a symbol under the pointer is.</summary>
    public Func<string, int, CancellationToken, Task<CodeQuickInfo?>>? QuickInfoSource { get; set; }

    /// <summary>The messages currently underlined.</summary>
    public IReadOnlyList<CodeDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Runs the analysis now rather than waiting for the idle delay.</summary>
    /// <returns>A task that completes when the underlines have been updated.</returns>
    public async Task AnalyseAsync()
    {
        if (_editor?.Document is not { } document || DiagnosticsSource is not { } source)
        {
            return;
        }

        _analysis?.Cancel();
        _analysis?.Dispose();
        _analysis = new CancellationTokenSource();

        CancellationToken token = _analysis.Token;
        string text = document.Text;

        try
        {
            IReadOnlyList<CodeDiagnostic> found = await source(text, token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            _diagnostics.Clear();
            _diagnostics.AddRange(found);

            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke, which is the common case rather than a fault.
        }
    }

    /// <summary>The message covering an offset, if any.</summary>
    /// <param name="offset">A document offset.</param>
    /// <returns>The diagnostic, or null.</returns>
    public CodeDiagnostic? DiagnosticAt(int offset)
    {
        if (_editor?.Document is not { } document)
        {
            return null;
        }

        foreach (CodeDiagnostic diagnostic in _diagnostics)
        {
            if (Span(document, diagnostic) is not (int start, int length))
            {
                continue;
            }

            if (offset >= start && offset <= start + length)
            {
                return diagnostic;
            }
        }

        return null;
    }

    /// <summary>Where a diagnostic sits in the document, or null when its line is not there.</summary>
    private static (int Start, int Length)? Span(TextDocument document, CodeDiagnostic diagnostic)
    {
        if (diagnostic.Line < 1 || diagnostic.Line > document.LineCount)
        {
            return null;
        }

        DocumentLine line = document.GetLineByNumber(diagnostic.Line);
        int start = line.Offset + Math.Clamp(diagnostic.Column - 1, 0, line.Length);
        int length = Math.Clamp(diagnostic.Length, 1, Math.Max(1, line.EndOffset - start));

        return (start, length);
    }

    private void StartAnalysisTimer()
    {
        if (DiagnosticsSource is null)
        {
            return;
        }

        _analysisTimer ??= CreateAnalysisTimer();

        // Restarted on every keystroke, so the delay measures how long the user has been still
        // rather than how long since they started.
        _analysisTimer.Stop();
        _analysisTimer.Start();
    }

    private DispatcherTimer CreateAnalysisTimer()
    {
        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(AnalysisDelayMilliseconds),
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = AnalyseAsync();
        };

        timer.Stop();

        return timer;
    }

    private async void OnPointerMovedForHover(object? sender, PointerEventArgs e)
    {
        try
        {
            if (_editor?.Document is not { } document || Offset(e) is not { } offset)
            {
                HideHover();

                return;
            }

            if (offset == _hoverOffset && _hover is not null)
            {
                return;
            }

            _hoverOffset = offset;

            // The compiler's complaint wins over the symbol's documentation. Somebody hovering a
            // red underline is asking about the red underline.
            if (DiagnosticAt(offset) is { } diagnostic)
            {
                ShowHover(
                    diagnostic.Id + ": " + diagnostic.Message,
                    diagnostic.IsError ? SparkPalette.StateError : SparkPalette.StateWarning);

                return;
            }

            if (QuickInfoSource is not { } source)
            {
                HideHover();

                return;
            }

            CodeQuickInfo? info = await source(document.Text, offset, CancellationToken.None)
                .ConfigureAwait(true);

            if (info is not { } described || offset != _hoverOffset)
            {
                HideHover();

                return;
            }

            ShowHover(
                described.Summary is { Length: > 0 } summary
                    ? described.Signature + "\n" + summary
                    : described.Signature,
                null);
        }
        catch (OperationCanceledException)
        {
            // A tooltip is never worth interrupting anybody for.
        }
    }

    private void ShowHover(string text, Color? accent)
    {
        HideHover();

        if (_editor is null)
        {
            return;
        }

        TextBlock content = new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
        };

        if (accent is { } colour)
        {
            content.Foreground = new SolidColorBrush(colour).ToImmutable();
        }

        _hover = new ToolTip { Content = content };

        ToolTip.SetTip(_editor, _hover);
        ToolTip.SetIsOpen(_editor, true);
    }

    private void HideHover()
    {
        if (_hover is null || _editor is null)
        {
            return;
        }

        ToolTip.SetIsOpen(_editor, false);
        ToolTip.SetTip(_editor, null);

        _hover = null;
        _hoverOffset = -1;
    }

    /// <summary>Draws the wavy underlines. RCS's sawtooth, and RCS's two colours.</summary>
    private sealed class SquiggleRenderer(CodeBlockEditor owner) : IBackgroundRenderer
    {
        private const double Period = 4;
        private const double Amplitude = 2.5;

        private static readonly IPen ErrorPen =
            new Pen(new SolidColorBrush(SparkPalette.StateError).ToImmutable(), 1.1).ToImmutable();

        private static readonly IPen WarningPen =
            new Pen(new SolidColorBrush(SparkPalette.StateWarning).ToImmutable(), 1.1).ToImmutable();

        /// <inheritdoc/>
        public KnownLayer Layer => KnownLayer.Selection;

        /// <inheritdoc/>
        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (owner._diagnostics.Count == 0
                || textView?.Document is not { } document
                || !textView.VisualLinesValid)
            {
                return;
            }

            textView.EnsureVisualLines();

            foreach (CodeDiagnostic diagnostic in owner._diagnostics)
            {
                if (Span(document, diagnostic) is not (int start, int length))
                {
                    continue;
                }

                TextSegment segment = new() { StartOffset = start, Length = length };
                IPen pen = diagnostic.IsError ? ErrorPen : WarningPen;

                foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    Draw(drawingContext, pen, rect);
                }
            }
        }

        /// <summary>A sawtooth along the bottom of the text — the usual squiggle.</summary>
        private static void Draw(DrawingContext context, IPen pen, Rect rect)
        {
            bool up = false;
            Point from = new(rect.Left, rect.Bottom - Amplitude);

            for (double x = rect.Left; x < rect.Right; x += Period / 2)
            {
                Point to = new(
                    Math.Min(x + (Period / 2), rect.Right),
                    rect.Bottom - (up ? Amplitude : 0));

                context.DrawLine(pen, from, to);

                from = to;
                up = !up;
            }
        }
    }
}
