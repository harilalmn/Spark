using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;
using Spark.UI.Theming;

namespace Spark.UI.Views.Controls;

/// <summary>One candidate in the completion list, as the editor draws it.</summary>
/// <param name="DisplayText">The text to insert and to show.</param>
/// <param name="Kind">What it is — <c>Method</c>, <c>Property</c>, <c>Field</c>.</param>
/// <remarks>
/// <b>Deliberately not Roslyn's type.</b> Completion is a <c>Spark.Scripting</c> concern and
/// drawing a list is this assembly's; keeping the compiler's vocabulary out of the control is what
/// stops the language service leaking into the shell (ADR-0005).
/// </remarks>
public readonly record struct CodeCompletionCandidate(string DisplayText, string Kind);

/// <summary>
/// The code block's editing surface: an AvaloniaEdit text editor with a completion popup
/// (`E6-T11`, `E6-T12`).
/// </summary>
/// <remarks>
/// <para>
/// <b>The popup is placed at the caret's visual position minus the text view's scroll
/// offset.</b> That subtraction is the whole of what the M1.5 spike measured
/// (<c>E11-T21</c>'s C3): <c>GetVisualPosition</c> answers in document coordinates, so a popup
/// placed at it is correct on the first screenful and wrong on every one after.
/// </para>
/// <para>
/// <b>The editor keeps focus while the list is open</b>, and that is the design rather than an
/// accident. A completion list that takes focus stops the user typing, so the arrow keys, Enter,
/// Tab and Escape are handled here and forwarded to the list, and every other key goes to the
/// document and re-filters. This is the part of the port `E6-T12` budgets rework for, because it is
/// where AvalonEdit and AvaloniaEdit diverge most.
/// </para>
/// <para>
/// <b>The control knows nothing about how a candidate is found.</b> It calls
/// <see cref="CompletionSource"/>, which the shell supplies; a control that reached for Roslyn
/// itself would put the compiler in the type graph of every window that hosts an inspector.
/// </para>
/// </remarks>
public sealed partial class CodeBlockEditor : UserControl
{
    /// <summary>The characters that open the list without being asked.</summary>
    /// <remarks>
    /// A dot only. Opening on every letter is what a language service in an IDE does and it is
    /// wrong in a one-line editor: the list covers the code the moment you begin a variable name.
    /// Ctrl+Space is the explicit request.
    /// </remarks>
    private const char TriggerCharacter = '.';

    private readonly ObservableCollection<CodeCompletionCandidate> _candidates = [];

    private readonly TextEditor? _editor;
    private readonly Border? _frame;
    private readonly ListBox? _list;
    private CancellationTokenSource? _pending;
    private int _filterStart;
    private bool _suppressTextChanged;

    /// <summary>Creates the editor.</summary>
    public CodeBlockEditor()
    {
        InitializeComponent();

        _editor = this.FindControl<TextEditor>("Editor");
        _frame = this.FindControl<Border>("CompletionFrame");
        _list = this.FindControl<ListBox>("CompletionList");

        if (_list is not null)
        {
            _list.ItemsSource = _candidates;

            // The layer is not hit-testable, so that a click through the empty part of it reaches
            // the editor underneath; the list itself has to be, or the mouse cannot pick a member.
            _list.IsHitTestVisible = true;
        }

        if (_editor is null)
        {
            return;
        }

        // The highlighting definition is looked up by name and may be absent - AvaloniaEdit ships
        // the .xshd files as embedded resources, and a trimmed build can lose them. Plain text is a
        // perfectly usable editor; a null reference in a constructor is not.
        _editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");

        Recolour(_editor);

        _editor.TextChanged += OnTextChanged;
        _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        _editor.LostFocus += OnEditorLostFocus;
    }

    /// <summary>
    /// Puts the editor on Spark's palette instead of AvaloniaEdit's defaults.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The stock C# highlighting is written for a light background.</b> Its keywords are navy,
    /// its strings are dark red and its comments are mid-green, all chosen against white — and on
    /// <c>surface.sunken</c> at <c>#1A1E24</c> they are very close to invisible. The first person
    /// to type in this editor said the text was difficult to see, and they were reading dark blue
    /// on near-black.
    /// </para>
    /// <para>
    /// <b>Every colour here is a token the design language already publishes with a contrast
    /// figure</b>, rather than something picked to look right: the surface is
    /// <c>surface.sunken</c>, which that document names for *inset wells: text fields*; the body is
    /// <c>text.primary</c>; and the syntax colours are the node category fills, which carry
    /// measured ratios of 5.39:1 and better against the dark ground. Reusing them also means a
    /// keyword is the same blue as a Script node, which is the sort of coincidence worth keeping.
    /// </para>
    /// </remarks>
    /// <param name="editor">The editor to recolour.</param>
    private static void Recolour(TextEditor editor)
    {
        editor.Background = SparkPalette.Frozen(SparkPalette.SurfaceSunken);
        editor.Foreground = SparkPalette.TextPrimaryBrush;

        // The line-number gutter is supporting information, not body copy.
        editor.LineNumbersForeground = SparkPalette.TextMutedBrush;

        if (editor.SyntaxHighlighting is not { } highlighting)
        {
            return;
        }

        // Named colours in AvaloniaEdit's C# definition. A name that is not there is skipped
        // rather than assumed: the .xshd is somebody else's file and it is entitled to change.
        (string Name, Color Colour)[] scheme =
        [
            ("Comment", SparkPalette.TextMuted),
            ("String", NodeCategoryColours.ColourOf(NodeCategory.Display)),
            ("Char", NodeCategoryColours.ColourOf(NodeCategory.Display)),
            ("Preprocessor", SparkPalette.TextSecondary),
            ("Punctuation", SparkPalette.TextSecondary),
            ("ValueTypeKeywords", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("ReferenceTypeKeywords", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("MethodCall", NodeCategoryColours.ColourOf(NodeCategory.Curve)),
            ("NumberLiteral", NodeCategoryColours.ColourOf(NodeCategory.Math)),
            ("ThisOrBaseReference", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("Keywords", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("GotoKeywords", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("ContextKeywords", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("ExceptionKeywords", NodeCategoryColours.ColourOf(NodeCategory.List)),
            ("CheckedKeyword", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("UnsafeKeywords", NodeCategoryColours.ColourOf(NodeCategory.List)),
            ("OperatorKeywords", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("ParameterModifiers", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("Modifiers", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("Visibility", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("NamespaceKeywords", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("GetSetAddRemove", NodeCategoryColours.ColourOf(NodeCategory.Script)),
            ("TrueFalse", NodeCategoryColours.ColourOf(NodeCategory.Math)),
            ("TypeKeywords", NodeCategoryColours.ColourOf(NodeCategory.Script)),
        ];

        foreach ((string name, Color colour) in scheme)
        {
            if (highlighting.GetNamedColor(name) is { } named)
            {
                named.Foreground = new SimpleHighlightingBrush(colour);
            }
        }
    }

    /// <summary>Raised when the text has been changed and committed — on losing focus.</summary>
    public event EventHandler? Committed;

    /// <summary>Where candidates come from: the text, the caret, and a token.</summary>
    /// <remarks>
    /// Null disables completion entirely, which is what an inspector with no scripting session
    /// wants — and it means this control never touches the compiler on its own.
    /// </remarks>
    public Func<string, int, CancellationToken, Task<IReadOnlyList<CodeCompletionCandidate>>>? CompletionSource
    {
        get;
        set;
    }

    /// <summary>The source the editor is showing.</summary>
    /// <remarks>
    /// A plain property rather than a styled one: the inspector sets it when the selection changes
    /// and reads it when the edit is committed, and a two-way binding onto a document that the user
    /// is typing into invites a feedback loop for no benefit.
    /// </remarks>
    public string Text
    {
        get => _editor?.Document?.Text ?? string.Empty;

        set
        {
            if (_editor?.Document is not { } document || document.Text == value)
            {
                return;
            }

            // The assignment is not a user edit, so it must not open a completion list or be
            // mistaken for one.
            _suppressTextChanged = true;

            try
            {
                document.Text = value ?? string.Empty;
                _editor.CaretOffset = document.TextLength;
            }
            finally
            {
                _suppressTextChanged = false;
            }
        }
    }

    /// <summary>Whether the completion list is on screen.</summary>
    public bool IsCompletionOpen => _frame?.IsVisible == true;

    /// <summary>Where the list is drawn, in the control's own coordinates.</summary>
    /// <remarks>
    /// Readable so that the placement rule can be asserted rather than looked at. It is the
    /// caret's position in the text view minus the view's scroll offset, and the difference
    /// between those two is the whole of the M1.5 spike's C3 finding.
    /// </remarks>
    public Point CompletionOrigin { get; private set; }

    /// <summary>The candidate Enter would commit, or null when the list is closed.</summary>
    public CodeCompletionCandidate? SelectedCandidate =>
        IsCompletionOpen && _list?.SelectedItem is CodeCompletionCandidate chosen ? chosen : null;

    /// <summary>The candidates currently listed, for a test to read.</summary>
    public IReadOnlyList<CodeCompletionCandidate> Candidates => _candidates;

    /// <summary>Opens the completion list at the caret, if there is a source for it.</summary>
    /// <returns>A task that completes when the list has been filled or abandoned.</returns>
    public async Task RequestCompletionAsync()
    {
        if (_editor?.Document is not { } document || CompletionSource is not { } source)
        {
            return;
        }

        // A keystroke supersedes the request in flight. Without this a slow first call - Roslyn
        // composes its host services on first use - lands after the user has typed three more
        // characters and replaces the list with one for a caret that has moved.
        CancellationTokenSource cancellation = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _pending, cancellation);
        previous?.Cancel();
        previous?.Dispose();

        int caret = _editor.CaretOffset;
        string text = document.Text;

        IReadOnlyList<CodeCompletionCandidate> candidates;

        try
        {
            candidates = await source(text, caret, cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellation.IsCancellationRequested)
        {
            return;
        }

        _filterStart = WordStart(text, caret);
        Show(candidates);
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (_suppressTextChanged || _editor?.Document is not { } document)
        {
            return;
        }

        int caret = _editor.CaretOffset;

        if (IsCompletionOpen)
        {
            Filter();
            return;
        }

        if (caret > 0 && document.GetCharAt(caret - 1) == TriggerCharacter)
        {
            _ = RequestCompletionAsync();
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = RequestCompletionAsync();

            return;
        }

        if (!IsCompletionOpen || _list is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;

            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;

            case Key.Down:
                Move(1);
                e.Handled = true;
                break;

            case Key.Enter:
            case Key.Tab:
                e.Handled = Commit();
                break;

            default:
                break;
        }
    }

    private void OnEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
        Committed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows the list under the caret, or closes it when there is nothing to show.</summary>
    private void Show(IReadOnlyList<CodeCompletionCandidate> candidates)
    {
        _candidates.Clear();

        foreach (CodeCompletionCandidate candidate in candidates)
        {
            _candidates.Add(candidate);
        }

        if (_candidates.Count == 0 || _frame is null || _list is null)
        {
            Close();
            return;
        }

        _list.SelectedIndex = 0;
        Place();
        _frame.IsVisible = true;
    }

    /// <summary>Puts the popup where the caret is <i>on screen</i>.</summary>
    /// <remarks>
    /// <c>GetVisualPosition</c> answers in the text view's own document coordinates, so the scroll
    /// offset has to come off. Forgetting it gives a popup that is correct on the first screenful
    /// and drifts further away with every line scrolled — which is the finding the M1.5 spike
    /// exists to have written down.
    /// </remarks>
    private void Place()
    {
        if (_editor is null || _frame is null)
        {
            return;
        }

        TextView view = _editor.TextArea.TextView;
        Point visual = view.GetVisualPosition(_editor.TextArea.Caret.Position, VisualYPosition.LineBottom);

        CompletionOrigin = visual - view.ScrollOffset;

        Avalonia.Controls.Canvas.SetLeft(_frame, CompletionOrigin.X);
        Avalonia.Controls.Canvas.SetTop(_frame, CompletionOrigin.Y);
    }

    /// <summary>Narrows the list to what has been typed since it opened.</summary>
    /// <remarks>
    /// Filtering locally rather than asking again is what makes typing feel immediate: a request
    /// per keystroke would be correct and would also make the list flicker on a slow first call.
    /// When nothing matches the list closes, because a list of nothing is a rectangle covering the
    /// user's code.
    /// </remarks>
    private void Filter()
    {
        if (_editor?.Document is not { } document || _list is null)
        {
            return;
        }

        int caret = _editor.CaretOffset;

        if (caret < _filterStart)
        {
            Close();
            return;
        }

        string typed = document.GetText(_filterStart, caret - _filterStart);

        if (typed.Length == 0)
        {
            _list.SelectedIndex = _candidates.Count > 0 ? 0 : -1;
            return;
        }

        for (int i = 0; i < _candidates.Count; i++)
        {
            if (_candidates[i].DisplayText.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            {
                _list.SelectedIndex = i;
                return;
            }
        }

        Close();
    }

    private void Move(int delta)
    {
        if (_list is null || _candidates.Count == 0)
        {
            return;
        }

        int index = _list.SelectedIndex + delta;
        _list.SelectedIndex = Math.Clamp(index, 0, _candidates.Count - 1);
    }

    /// <summary>Replaces what has been typed with the selected candidate.</summary>
    /// <returns>True when something was inserted.</returns>
    private bool Commit()
    {
        if (_editor?.Document is not { } document
            || _list?.SelectedItem is not CodeCompletionCandidate chosen)
        {
            return false;
        }

        int caret = _editor.CaretOffset;

        _suppressTextChanged = true;

        try
        {
            // Replacing from where the word started rather than inserting at the caret is what
            // makes committing after typing three letters give one member rather than a name with
            // its prefix doubled.
            document.Replace(_filterStart, Math.Max(0, caret - _filterStart), chosen.DisplayText);
            _editor.CaretOffset = _filterStart + chosen.DisplayText.Length;
        }
        finally
        {
            _suppressTextChanged = false;
        }

        Close();

        return true;
    }

    private void Close()
    {
        if (_frame is not null)
        {
            _frame.IsVisible = false;
        }
    }

    /// <summary>Where the identifier under the caret begins.</summary>
    private static int WordStart(string text, int caret)
    {
        int start = Math.Min(caret, text.Length);

        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        return start;
    }
}
