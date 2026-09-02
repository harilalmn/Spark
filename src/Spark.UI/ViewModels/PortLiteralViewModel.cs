using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Spark.Geometry;

namespace Spark.UI.ViewModels;

/// <summary>
/// One editable literal in the properties panel: the value typed into an unwired input port.
/// </summary>
/// <remarks>
/// <para>
/// This is where "editing a literal re-runs the graph" actually happens. Committing a value calls
/// back into the view model, which writes it through the engine graph — marking the node and
/// everything downstream dirty — and starts an evaluation off the UI thread.
/// </para>
/// <para>
/// <b>A wired port is not editable.</b> The wire wins, and a text box that accepted a value which
/// then had no effect would be worse than one that refuses it.
/// </para>
/// </remarks>
public sealed partial class PortLiteralViewModel : ObservableObject
{
    private readonly Action<PortLiteralViewModel, object?> _commit;
    private readonly Action<PortLiteralViewModel, Type?>? _declare;
    private readonly Type _valueType;

    private bool _applyingDeclaration;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string? _declaredTypeName;

    internal PortLiteralViewModel(
        int slot,
        int portIndex,
        string name,
        Type valueType,
        object? value,
        bool isWired,
        string? description,
        Action<PortLiteralViewModel, object?> commit,
        Action<PortLiteralViewModel, Type?>? declare = null,
        Type? declaredType = null)
    {
        Slot = slot;
        PortIndex = portIndex;
        Name = name;
        _valueType = valueType;
        _commit = commit;
        _declare = declare;
        IsWired = isWired;
        Description = description;
        TypeName = Spark.UI.Graph.PortTypeName.Describe(valueType);
        IsEditable = !isWired && IsSupported(valueType);
        CanDeclareType = declare is not null;

        // Assigned through the backing fields rather than the properties, so the generated change
        // notifications do not fire before the object is fully built — and, for the declaration,
        // so that building the panel does not read as the user having chosen something.
        _text = Format(value);
        _declaredTypeName = declaredType is null ? NotDeclared : NameOf(declaredType);
    }

    /// <summary>
    /// Whether the box currently has the caret, and must not be overwritten from underneath.
    /// </summary>
    /// <remarks>
    /// Set by the pane on focus, because a view model cannot see focus and <see cref="Show"/> has
    /// to know. Half-typed text is the user's, and a graph run landing mid-keystroke is not a
    /// reason to take it away.
    /// </remarks>
    internal bool IsEditing { get; set; }

    /// <summary>
    /// Shows a value that came from somewhere other than this box — a slider drag, an undo, a run.
    /// </summary>
    /// <param name="value">The literal now on the port.</param>
    /// <returns>True when the box was updated, false when it was left alone mid-edit.</returns>
    /// <remarks>
    /// <b>This does not commit and must not.</b> It is the opposite direction: the graph telling
    /// the panel what it now holds. Committing here would turn every evaluation into an edit, and
    /// dragging a slider would fill the undo stack with one entry per pixel — which is exactly what
    /// <c>GraphEditedEventArgs.RecordsUndo</c> exists to prevent one layer up.
    /// </remarks>
    internal bool Show(object? value)
    {
        if (IsEditing)
        {
            return false;
        }

        string text = Format(value);

        if (!string.Equals(text, Text, StringComparison.Ordinal))
        {
            Text = text;

            // A value arriving from the graph supersedes whatever the last rejected keystroke
            // said. Leaving the old parse error beside a good number reads as the number being
            // wrong.
            Error = null;
        }

        return true;
    }

    /// <summary>The node's slot on the canvas.</summary>
    public int Slot { get; }

    /// <summary>The input port index.</summary>
    public int PortIndex { get; }

    /// <summary>The port's display name.</summary>
    public string Name { get; }

    /// <summary>
    /// The port's declared type, in the words a user types it in — <c>number</c>, <c>degrees</c>.
    /// </summary>
    /// <remarks>
    /// Shown in the panel rather than only in a tooltip. A box labelled <c>radius</c> with nothing
    /// else on the row does not tell somebody what belongs in it, and a tooltip only answers a
    /// question the user has to already have. It comes from
    /// <see cref="Spark.UI.Graph.PortTypeName"/>, which is also where the canvas gets it, so the
    /// two cannot drift.
    /// </remarks>
    public string TypeName { get; }

    /// <summary>One line describing the port, or null.</summary>
    public string? Description { get; }

    /// <summary>Whether a wire feeds this port, in which case the literal is ignored.</summary>
    public bool IsWired { get; }

    /// <summary>Whether the panel offers a text box for this port.</summary>
    public bool IsEditable { get; }

    /// <summary>
    /// Whether the panel offers a type dropdown for this port (<c>E6-T11</c>).
    /// </summary>
    /// <remarks>
    /// <b>Only a code block's ports get one.</b> Every other node's port type comes from the method
    /// it was imported from and is not the user's to change; a dropdown there would be a control
    /// that either does nothing or breaks the node.
    /// </remarks>
    public bool CanDeclareType { get; }

    /// <summary>
    /// The names the type dropdown offers, with <see cref="NotDeclared"/> first.
    /// </summary>
    /// <remarks>
    /// The words are <see cref="Spark.UI.Graph.PortTypeName"/>'s, so the dropdown and the label
    /// underneath it say the same thing — a dropdown reading <c>Double</c> above a label reading
    /// <c>number</c> would make a user wonder which one the port actually is.
    /// </remarks>
    public static IReadOnlyList<string> TypeChoices { get; } = BuildChoices();

    /// <summary>
    /// What the dropdown shows when the user has declared nothing: the port takes its type from
    /// whatever is wired into it.
    /// </summary>
    /// <remarks>
    /// Worded as a source rather than as a type name, because it names a *source*, not a type.
    /// "anything" would be wrong — an unwired port is `dynamic` and a wired one is whatever the
    /// wire carries, and neither of those is a choice the user made. It read "from the wire" until
    /// the dropdown moved into the narrow label column (<c>E8-T30</c>), where two words fit and
    /// four did not.
    /// </remarks>
    public const string NotDeclared = "from wire";

    /// <summary>
    /// The second line of the row: the port's type, marked when a wire is feeding it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A wired port keeps whatever literal was last typed into it, and showing that value in a
    /// disabled box with no explanation reads as the editor having forgotten the wire. The marker
    /// is the explanation.
    /// </para>
    /// <para>
    /// <b>It used to be on the name, and the name is the half that cannot afford it.</b> The label
    /// column is narrow by design (<c>E8-T30</c>), so <c>direction (wired)</c> trimmed to
    /// <c>direction (wi…</c> and a row full of those is a column of identical prefixes. The type
    /// line underneath is short, so the marker rides there and the name always fits.
    /// </para>
    /// </remarks>
    public string TypeLine => IsWired ? TypeName + " · wired" : TypeName;

    /// <summary>
    /// Parses the text and, if it is valid for the port's type, commits it.
    /// </summary>
    /// <remarks>
    /// Invalid text sets <see cref="Error"/> and commits nothing. Committing a fallback — zero for
    /// an unparseable number — would silently discard what the user typed and re-run the graph on a
    /// value they never asked for.
    /// </remarks>
    public void Commit()
    {
        if (!IsEditable)
        {
            return;
        }

        if (!TryParse(Text, out object? value))
        {
            Error = $"Not a valid {TypeName}.";
            return;
        }

        Error = null;
        _commit(this, value);
    }

    partial void OnTextChanged(string value) => Error = null;

    /// <summary>
    /// Declaring a type takes effect the moment it is chosen, unlike a literal, which waits for
    /// focus to leave the box.
    /// </summary>
    /// <remarks>
    /// <b>The asymmetry is deliberate.</b> A half-typed number is not a number, so a literal has to
    /// wait until the user has stopped typing; a dropdown has no half-chosen state, and making
    /// somebody click elsewhere to find out whether their choice worked would hide the one effect
    /// worth seeing — the port's type changing on the canvas, and completion starting to work.
    /// </remarks>
    /// <param name="value">The chosen name.</param>
    partial void OnDeclaredTypeNameChanged(string? value)
    {
        // Set while the panel is being built, or while it is putting the box back after a rebuild.
        // Neither is a choice, and treating one as a choice would rebuild the node on selection.
        if (_applyingDeclaration || _declare is null)
        {
            return;
        }

        // NULL IS NEVER A CHOICE A USER MADE, and this guard is not defensive tidiness.
        //
        // "The user declared nothing" is spelled `NotDeclared`, which is an entry in the list. A
        // ComboBox, though, writes null back through a two-way SelectedItem binding whenever it
        // cannot find the bound value among its items - which happens transiently while the
        // control is being realised, before ItemsSource has been applied. Acting on that would
        // silently clear a declaration the moment the panel was rebuilt, which is every time the
        // selection changes.
        if (value is null)
        {
            return;
        }

        _declare(this, TypeNamed(value));
    }

    /// <summary>
    /// Puts the dropdown on a value without treating it as something the user chose.
    /// </summary>
    /// <param name="declaredType">The declared type, or null for <see cref="NotDeclared"/>.</param>
    internal void ShowDeclaredType(Type? declaredType)
    {
        _applyingDeclaration = true;
        try
        {
            DeclaredTypeName = declaredType is null ? NotDeclared : NameOf(declaredType);
        }
        finally
        {
            _applyingDeclaration = false;
        }
    }

    /// <summary>The type a dropdown entry names, or null for <see cref="NotDeclared"/>.</summary>
    private static Type? TypeNamed(string? choice)
    {
        if (choice is null || choice == NotDeclared)
        {
            return null;
        }

        foreach ((_, Type type) in Spark.Engine.ScriptInputTypes.Catalogue)
        {
            if (NameOf(type) == choice)
            {
                return type;
            }
        }

        return null;
    }

    private static string NameOf(Type type) => Spark.UI.Graph.PortTypeName.Describe(type);

    private static IReadOnlyList<string> BuildChoices()
    {
        List<string> choices = [NotDeclared];
        foreach ((_, Type type) in Spark.Engine.ScriptInputTypes.Catalogue)
        {
            choices.Add(NameOf(type));
        }

        return choices;
    }

    private bool TryParse(string text, out object? value)
    {
        value = null;

        if (_valueType == typeof(double) || _valueType == typeof(float))
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                return false;
            }

            value = _valueType == typeof(float) ? (float)number : number;
            return true;
        }

        if (_valueType == typeof(int) || _valueType == typeof(long))
        {
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
            {
                return false;
            }

            value = _valueType == typeof(int) ? (int)number : number;
            return true;
        }

        if (_valueType == typeof(bool))
        {
            if (!bool.TryParse(text, out bool flag))
            {
                return false;
            }

            value = flag;
            return true;
        }

        if (_valueType == typeof(string))
        {
            value = text;
            return true;
        }

        if (_valueType == typeof(Angle))
        {
            // Typed in degrees, held in radians. The kernel takes an Angle in every angular
            // signature precisely so that the editor knows a number is an angle and can choose the
            // unit a person thinks in; a bare double could not be told apart from a length.
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double degrees))
            {
                return false;
            }

            value = Angle.FromDegrees(degrees);
            return true;
        }

        return false;
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        Angle angle => angle.Degrees.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static bool IsSupported(Type type) =>
        type == typeof(double)
        || type == typeof(float)
        || type == typeof(int)
        || type == typeof(long)
        || type == typeof(bool)
        || type == typeof(string)
        || type == typeof(Angle);
}
