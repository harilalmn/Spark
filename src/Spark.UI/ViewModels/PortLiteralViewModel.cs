using System;
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
    private readonly Type _valueType;


    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string? _error;

    internal PortLiteralViewModel(
        int slot,
        int portIndex,
        string name,
        Type valueType,
        object? value,
        bool isWired,
        string? description,
        Action<PortLiteralViewModel, object?> commit)
    {
        Slot = slot;
        PortIndex = portIndex;
        Name = name;
        _valueType = valueType;
        _commit = commit;
        IsWired = isWired;
        Description = description;
        TypeName = Spark.UI.Graph.PortTypeName.Describe(valueType);
        IsEditable = !isWired && IsSupported(valueType);

        // Assigned through the backing field rather than the property, so the generated change
        // notification does not fire before the object is fully built.
        _text = Format(value);
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
    /// The label the panel shows: the port name, marked when a wire is feeding it.
    /// </summary>
    /// <remarks>
    /// A wired port keeps whatever literal was last typed into it, and showing that value in a
    /// disabled box with no explanation reads as the editor having forgotten the wire. The marker
    /// is the explanation.
    /// </remarks>
    public string Label => IsWired ? Name + " (wired)" : Name;

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
