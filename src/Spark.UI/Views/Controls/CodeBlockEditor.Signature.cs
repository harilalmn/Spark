using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Spark.UI.Theming;

namespace Spark.UI.Views.Controls;

/// <summary>One overload, as the editor draws it.</summary>
/// <param name="Name">The method's name, or the type's for a constructor.</param>
/// <param name="Parameters">Each parameter as <c>Point3d centre</c> — type then name.</param>
/// <param name="ReturnType">What the call evaluates to, or empty for a constructor.</param>
/// <remarks>
/// <b>Deliberately not Roslyn's type</b>, for the reason
/// <see cref="CodeCompletionCandidate"/> is not: the compiler's vocabulary stops at the edge of
/// this assembly (ADR-0005).
/// </remarks>
public readonly record struct CodeSignatureCandidate(
    string Name,
    IReadOnlyList<string> Parameters,
    string ReturnType);

/// <summary>The overloads for the call the caret is inside, and which parameter is being typed.</summary>
/// <param name="Signatures">The overloads, shortest first.</param>
/// <param name="ActiveSignature">Which one is shown.</param>
/// <param name="ActiveParameter">Which parameter is emphasised.</param>
public readonly record struct CodeSignatureInfo(
    IReadOnlyList<CodeSignatureCandidate> Signatures,
    int ActiveSignature,
    int ActiveParameter);

/// <summary>
/// The signature-help half of the code block's editor (`E6-T22`).
/// </summary>
/// <remarks>
/// <para>
/// <b>A second overlay rather than a second use of the first.</b> The completion list answers
/// <i>what can I write here</i> and signature help answers <i>what does this call want</i>; both
/// are on screen at once while a call is being typed, so they are two frames on the same canvas —
/// the list below the caret's line and the signature above it, which is the one arrangement where
/// neither covers the other or the code being written.
/// </para>
/// <para>
/// <b>It re-asks on every change while it is open.</b> A signature popup's whole content is the
/// parameter the caret is on, so a cached one is wrong the moment a comma is typed; the request is
/// cancelled by the next keystroke exactly as the completion list's is, and a caret that has left
/// the argument list answers null and closes it.
/// </para>
/// </remarks>
public sealed partial class CodeBlockEditor
{
    private CancellationTokenSource? _pendingSignature;
    private CodeSignatureInfo _signature;

    /// <summary>Where signatures come from: the text, the caret, and a token.</summary>
    /// <remarks>
    /// Null disables signature help, which is what an inspector with no scripting session wants —
    /// and it means this control never touches the compiler on its own.
    /// </remarks>
    public Func<string, int, CancellationToken, Task<CodeSignatureInfo?>>? SignatureSource
    {
        get;
        set;
    }

    /// <summary>Whether the signature popup is on screen.</summary>
    public bool IsSignatureOpen => _signatureFrame?.IsVisible == true;

    /// <summary>Where the signature popup is drawn, in the control's own coordinates.</summary>
    public Point SignatureOrigin { get; private set; }

    /// <summary>The overload currently shown, or null when the popup is closed.</summary>
    public CodeSignatureCandidate? ActiveSignature =>
        IsSignatureOpen && _signature.ActiveSignature < _signature.Signatures.Count
            ? _signature.Signatures[_signature.ActiveSignature]
            : null;

    /// <summary>The parameter drawn in bold, or -1 when the popup is closed.</summary>
    public int ActiveParameter => IsSignatureOpen ? _signature.ActiveParameter : -1;

    /// <summary>How many overloads the popup is cycling through.</summary>
    public int SignatureCount => IsSignatureOpen ? _signature.Signatures.Count : 0;

    /// <summary>Asks for the signature of the call around the caret, if there is a source for it.</summary>
    /// <returns>A task that completes when the popup has been filled or closed.</returns>
    public async Task RequestSignatureAsync()
    {
        if (_editor?.Document is not { } document || SignatureSource is not { } source)
        {
            return;
        }

        CancellationTokenSource cancellation = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _pendingSignature, cancellation);
        previous?.Cancel();
        previous?.Dispose();

        int caret = _editor.CaretOffset;
        string text = document.Text;

        CodeSignatureInfo? found;

        try
        {
            found = await source(text, caret, cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellation.IsCancellationRequested)
        {
            return;
        }

        ShowSignature(found);
    }

    /// <summary>Moves to the next or previous overload, wrapping at both ends.</summary>
    /// <param name="delta">+1 for the next, -1 for the previous.</param>
    /// <remarks>
    /// Wrapping rather than clamping: with two overloads, a user who presses Alt+Down twice to see
    /// them both should be back where they started rather than stuck on the second.
    /// </remarks>
    public void CycleSignature(int delta)
    {
        if (!IsSignatureOpen || _signature.Signatures.Count == 0)
        {
            return;
        }

        int count = _signature.Signatures.Count;
        int index = ((_signature.ActiveSignature + delta) % count + count) % count;

        _signature = _signature with { ActiveSignature = index };

        DrawSignature();
    }

    /// <summary>Shows the overloads, or closes the popup when there are none.</summary>
    private void ShowSignature(CodeSignatureInfo? found)
    {
        if (found is not { } info || info.Signatures.Count == 0 || _signatureFrame is null)
        {
            CloseSignature();
            return;
        }

        _signature = info with
        {
            ActiveSignature = Math.Clamp(info.ActiveSignature, 0, info.Signatures.Count - 1),
        };

        _signatureFrame.IsVisible = true;

        DrawSignature();
        PlaceSignature();
    }

    /// <summary>
    /// Writes the overload into the popup, with the parameter being typed in bold.
    /// </summary>
    /// <remarks>
    /// <b>Built as inlines rather than as one string.</b> The emphasis is the entire point of the
    /// popup — a three-parameter call whose middle parameter is not marked tells the reader what
    /// the method wants but not where they are in it, which is the question they actually have.
    /// </remarks>
    private void DrawSignature()
    {
        if (_signatureText is null || _signature.Signatures.Count == 0)
        {
            return;
        }

        CodeSignatureCandidate candidate = _signature.Signatures[
            Math.Clamp(_signature.ActiveSignature, 0, _signature.Signatures.Count - 1)];

        InlineCollection inlines = [];

        inlines.Add(new Run(candidate.Name) { Foreground = SparkPalette.TextPrimaryBrush });
        inlines.Add(new Run("("));

        for (int i = 0; i < candidate.Parameters.Count; i++)
        {
            if (i > 0)
            {
                inlines.Add(new Run(", "));
            }

            bool active = i == _signature.ActiveParameter;

            inlines.Add(new Run(candidate.Parameters[i])
            {
                FontWeight = active ? FontWeight.Bold : FontWeight.Normal,
                Foreground = active ? SparkPalette.TextPrimaryBrush : SparkPalette.TextMutedBrush,
            });
        }

        inlines.Add(new Run(")"));

        if (candidate.ReturnType.Length > 0)
        {
            inlines.Add(new Run(" → " + candidate.ReturnType) { Foreground = SparkPalette.TextMutedBrush });
        }

        _signatureText.Inlines = inlines;

        if (_signatureOverloads is not null)
        {
            _signatureOverloads.IsVisible = _signature.Signatures.Count > 1;
            _signatureOverloads.Text = _signature.Signatures.Count > 1
                ? $"{_signature.ActiveSignature + 1}/{_signature.Signatures.Count}"
                : string.Empty;
        }
    }

    /// <summary>Puts the popup on the line <i>above</i> the caret's.</summary>
    /// <remarks>
    /// The same subtraction the completion list makes — <c>GetVisualPosition</c> answers in
    /// document coordinates, so the scroll offset comes off (the M1.5 spike's C3 finding) — and
    /// then the frame's own height comes off as well, because this one hangs above the line rather
    /// than below it. It is measured before it is placed, since an unmeasured frame has no height
    /// and would land on the caret's own line, over the code it is describing.
    /// <b>On the top line of the pane there is nowhere above to hang</b>, so it is clamped to the
    /// pane's own top edge rather than drawn off it: half a popup at the top of the view is worth
    /// more than a whole one nobody can see.
    /// </remarks>
    private void PlaceSignature()
    {
        if (_editor is null || _signatureFrame is null)
        {
            return;
        }

        TextView view = _editor.TextArea.TextView;
        Point visual = view.GetVisualPosition(_editor.TextArea.Caret.Position, VisualYPosition.LineTop);

        _signatureFrame.Measure(Size.Infinity);

        Point above = (visual - view.ScrollOffset) - new Point(0, _signatureFrame.DesiredSize.Height);

        SignatureOrigin = above.WithY(Math.Max(0.0, above.Y));

        Avalonia.Controls.Canvas.SetLeft(_signatureFrame, SignatureOrigin.X);
        Avalonia.Controls.Canvas.SetTop(_signatureFrame, SignatureOrigin.Y);
    }

    private void CloseSignature()
    {
        if (_signatureFrame is not null)
        {
            _signatureFrame.IsVisible = false;
        }
    }
}
