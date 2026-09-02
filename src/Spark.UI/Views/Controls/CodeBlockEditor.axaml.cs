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
public readonly record struct CodeCompletionCandidate(string DisplayText, string Kind)
{
    /// <summary>The single letter drawn in the candidate's badge.</summary>
    /// <remarks>
    /// <b>Computed here rather than carried</b>, so that the record stays two strings and
    /// <c>C5</c>'s boundary argument still holds one layer up: what a kind looks like is this
    /// assembly's business, and it can be derived from the kind whenever the list is drawn.
    /// </remarks>
    public string Glyph => CompletionGlyph.LetterFor(Kind);

    /// <summary>The badge's fill, from RCS's palette.</summary>
    public IBrush GlyphBrush => CompletionGlyph.BrushFor(Kind);
}

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
    private readonly ObservableCollection<CodeCompletionCandidate> _candidates = [];

    private readonly TextEditor? _editor;
    private readonly Border? _frame;
    private readonly ListBox? _list;
    private readonly Border? _signatureFrame;
    private readonly TextBlock? _signatureText;
    private readonly TextBlock? _signatureOverloads;
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
        _signatureFrame = this.FindControl<Border>("SignatureFrame");
        _signatureText = this.FindControl<TextBlock>("SignatureText");
        _signatureOverloads = this.FindControl<TextBlock>("SignatureOverloads");

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

        // THE EDITOR'S OWN SETTINGS, TAKEN FROM RCS'S `CodeEditor` (`C:\Zyeta\Projects\RCS`).
        //
        // Defaults, in AvaloniaEdit as in AvalonEdit, are a plain-text editor's: real tabs, no
        // indentation strategy, no current-line highlight. That is defensible for a text box and
        // wrong for a C# editor, and the difference shows up the first time somebody presses Enter
        // inside a block and the caret lands in column one.
        //
        // `CSharpIndentationStrategy` is the one that matters most. It indents after `{`, outdents
        // on `}`, and keeps the level across a continuation line — which is the whole reason typing
        // more than two lines here is bearable.
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.Options.IndentationSize = 4;
        _editor.Options.HighlightCurrentLine = true;

        // A code block is not a document with links in it, and a Ctrl+click that navigates away is
        // a Ctrl+click that did not add a caret.
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;

        _editor.TextArea.IndentationStrategy =
            new AvaloniaEdit.Indentation.CSharp.CSharpIndentationStrategy(_editor.Options);

        Recolour(_editor);

        _editor.TextChanged += OnTextChanged;
        _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        _editor.LostFocus += OnEditorLostFocus;

        // The Selection commands and the carets they need (`E6-T24`). Text input is tunnelled so
        // that a multi-caret edit can be applied everywhere as one document update; the pointer
        // handlers add a caret on Alt+Click and drag the box in column-selection mode.
        _editor.AddHandler(TextInputEvent, OnEditorTextInput, RoutingStrategies.Tunnel);
        _editor.AddHandler(PointerPressedEvent, OnEditorPointerPressed, RoutingStrategies.Tunnel);
        _editor.AddHandler(PointerMovedEvent, OnEditorPointerMoved, RoutingStrategies.Tunnel);
        _editor.AddHandler(PointerReleasedEvent, OnEditorPointerReleased, RoutingStrategies.Tunnel);

        _editor.TextArea.TextView.BackgroundRenderers.Add(
            new ExtraCaretRenderer(this, AvaloniaEdit.Rendering.KnownLayer.Selection));
        _editor.TextArea.TextView.BackgroundRenderers.Add(
            new ExtraCaretRenderer(this, AvaloniaEdit.Rendering.KnownLayer.Caret));

        if (_editor.ContextMenu is { } menu)
        {
            menu.Opening += (_, _) => ShowModes(menu);
        }
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

        if (editor.SyntaxHighlighting is { } highlighting)
        {
            EditorHighlightPalette.Apply(highlighting);
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

    /// <summary>Where the caret is, in characters from the start of the source.</summary>
    /// <remarks>
    /// The inner editor is private, and a caller that wants the caret somewhere — a screenshot
    /// pose, a test — should not have to reach through the visual tree to find out where it is.
    /// </remarks>
    public int CaretOffset
    {
        get => _editor?.CaretOffset ?? 0;

        set
        {
            if (_editor?.Document is { } document)
            {
                _editor.CaretOffset = Math.Clamp(value, 0, document.TextLength);
            }
        }
    }

    /// <summary>Puts the keyboard focus in the text, rather than on this control.</summary>
    /// <remarks>
    /// The inner editor is what handles keys, so focusing the <see cref="UserControl"/> would put
    /// the caret nowhere. Public because the screenshot pose has to do exactly what a user does.
    /// </remarks>
    public void FocusEditor() => _editor?.Focus();

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
        string text = document.Text;
        char typed = caret > 0 && caret <= text.Length ? text[caret - 1] : '\0';

        // An open signature popup is re-asked on every change, because its whole content is the
        // parameter the caret is on: a comma moves it and a `)` ends it, and a popup that
        // remembers the answer to a question the caret has moved past is worse than none at all.
        if (IsSignatureOpen || typed is '(' or ',' or ')')
        {
            _ = RequestSignatureAsync();
        }

        // **A word narrows the open list; anything else ends it and is then offered to the trigger
        // rules afresh.** Without the second half, opening on `=` would be pointless: the space
        // after it matches no candidate, so the list would close half a keystroke after it opened
        // and never come back. Closing and re-asking is also what makes `centre.Position.` list
        // the second type rather than filtering the first one to nothing.
        if (IsCompletionOpen)
        {
            if (Identifier(typed))
            {
                Filter();
                return;
            }

            Close();
        }

        if (Opens(text, caret))
        {
            _ = RequestCompletionAsync();
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;

            // Ctrl+Space asks what can be written here; Ctrl+Shift+Space asks what the call being
            // written wants. VS Code spells them the same way, and this editor is judged against
            // VS Code by everybody who opens it.
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _ = RequestSignatureAsync();
            }
            else
            {
                _ = RequestCompletionAsync();
            }

            return;
        }

        // **Cycling overloads outranks moving lines, and only while the popup is up.** Alt+Up and
        // Alt+Down are Move Line Up and Move Line Down (`E6-T24`) at every other moment; VS Code
        // resolves the same collision the same way, by letting the visible popup win.
        if (IsSignatureOpen && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Up or Key.Down)
        {
            CycleSignature(e.Key == Key.Up ? -1 : 1);
            e.Handled = true;

            return;
        }

        if (!IsCompletionOpen || _list is null)
        {
            if (e.Key == Key.Escape && IsSignatureOpen)
            {
                CloseSignature();
                e.Handled = true;

                return;
            }

            e.Handled = HandleSelectionKey(e);

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
        CloseSignature();
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

        CompletionOrigin = Fit(_frame, (visual - view.ScrollOffset) + new Point(0, SignatureClearance));

        Avalonia.Controls.Canvas.SetLeft(_frame, CompletionOrigin.X);
        Avalonia.Controls.Canvas.SetTop(_frame, CompletionOrigin.Y);
    }

    /// <summary>Keeps a popup inside the pane it is drawn on.</summary>
    /// <remarks>
    /// <b>The overlay is clipped to the pane, so a popup that starts past the right edge is not
    /// merely awkward — it is invisible.</b> That is the trade this control made deliberately when
    /// it chose a canvas over a <c>Popup</c> (`E6-T12`), and the first screenshot of the signature
    /// popup is what showed the other half of the bargain being owed: the caret was at the end of a
    /// long line, so both popups were a sliver at the edge of the properties pane. They are pulled
    /// back inside instead, which is what an editor with a narrow gutter has to do.
    /// </remarks>
    private Point Fit(Avalonia.Layout.Layoutable frame, Point origin)
    {
        frame.Measure(Size.Infinity);

        double width = frame.DesiredSize.Width;
        double right = Math.Max(0.0, Bounds.Width - width);

        return new Point(
            Bounds.Width > width ? Math.Clamp(origin.X, 0.0, right) : 0.0,
            Math.Max(0.0, origin.Y));
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

    /// <summary>Whether what was just typed should open the completion list unasked.</summary>
    /// <remarks>
    /// <para>
    /// <b>Four openings, and the last three were asked for from the running application.</b> A dot
    /// was the only one, on the reasoning that opening on every letter covers the code in a narrow
    /// pane — which was right about a one-line editor and wrong about the pane this became. The
    /// client's report is exact: <c>var circle = </c> and <c>new </c> are the two places a person
    /// most wants to be told what exists, and both were silent unless Ctrl+Space was pressed.
    /// </para>
    /// <para>
    /// <b>The first letter of an identifier opens the list; the rest of the word filters it.</b>
    /// That is what keeps this to one request per word rather than one per keystroke, and it is
    /// also why a prefix that has matched nothing stays closed until the next word — the list shut
    /// because the user is writing a name Roslyn has never heard of, and reopening it on every
    /// further letter would be arguing with them.
    /// </para>
    /// </remarks>
    /// <param name="text">The document, as it now reads.</param>
    /// <param name="caret">Where the caret is, immediately after the character just typed.</param>
    private static bool Opens(string text, int caret)
    {
        if (caret <= 0 || caret > text.Length)
        {
            return false;
        }

        char typed = text[caret - 1];

        if (Quoted(text, caret))
        {
            return false;
        }

        if (typed == '.')
        {
            return true;
        }

        // `=` opens the list; `==`, `!=`, `<=`, `>=` and `=>` are comparisons and lambdas, and a
        // list of everything in scope is not what somebody writing one is asking for.
        if (typed == '=')
        {
            return !Operator(text, caret - 2);
        }

        // A space opens it after `new` and after an assignment, which are the two places where the
        // next thing typed is a name the user is trying to remember.
        if (typed == ' ')
        {
            string before = text[..(caret - 1)].TrimEnd();

            if (before.EndsWith("new", StringComparison.Ordinal)
                && (before.Length == 3 || !Identifier(before[^4])))
            {
                return true;
            }

            return before.EndsWith('=') && !Operator(before, before.Length - 2);
        }

        return Identifier(typed) && !char.IsDigit(typed) && WordStart(text, caret) == caret - 1;
    }

    /// <summary>Whether a character can appear in an identifier.</summary>
    private static bool Identifier(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Whether the character at an offset makes an `=` after it part of a longer operator.</summary>
    private static bool Operator(string text, int index) =>
        index >= 0 && index < text.Length && "=!<>+-*/%&|^".Contains(text[index], StringComparison.Ordinal);

    /// <summary>Whether the caret is inside a line comment, or a string on its own line.</summary>
    /// <remarks>
    /// <b>A heuristic, and it can only ever suppress a request.</b> Roslyn answers an empty list
    /// inside a comment anyway, so the worst this can be wrong by is one request that would have
    /// come back with nothing — which is why it is a scan of one line rather than a lexer. Block
    /// comments and verbatim strings that span lines are not tracked, for the same reason.
    /// </remarks>
    private static bool Quoted(string text, int caret)
    {
        int start = text.LastIndexOf('\n', Math.Min(caret - 1, text.Length - 1)) + 1;

        bool inString = false;
        bool inChar = false;

        for (int i = start; i < caret; i++)
        {
            char c = text[i];

            if (c == '\\' && (inString || inChar))
            {
                i++;
            }
            else if (c == '"' && !inChar)
            {
                inString = !inString;
            }
            else if (c == '\'' && !inString)
            {
                inChar = !inChar;
            }
            else if (c == '/' && !inString && !inChar && i + 1 < caret && text[i + 1] == '/')
            {
                return true;
            }
        }

        return inString || inChar;
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
