using System;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.UI.Graph;

/// <summary>
/// What a port's type is called on screen — the one place the canvas and the properties panel
/// both take it from.
/// </summary>
/// <remarks>
/// <para>
/// A port name alone does not tell a user what to plug into it. `centre` is a `Point3d` and
/// `radius` is a number, and nothing on the node said so; the two mechanisms that would have
/// answered it — the library entry's signature and the wire-drag preview — are both somewhere
/// other than where the question is asked.
/// </para>
/// <para>
/// <b>Two rules keep the answer short enough to draw.</b> First, <b>listness is not repeated
/// here</b>: a port that wants a list already says so with a ring around its disc
/// (<c>docs/help/concepts/design-language.md</c> §7.6), so this unwraps the list and names the
/// element. Second, <b>a name that already says the type does not get it twice</b> — an output
/// called `circle` returning a <c>Circle</c> reads "circle", not "circle Circle". See
/// <see cref="Beside"/>.
/// </para>
/// <para>
/// The words are chosen for the person typing the value rather than for the compiler: `number`,
/// not `Double`; `degrees`, not `Angle`. That is the same choice the properties panel was already
/// making privately, and the reason this type exists is that two places making it separately
/// would eventually disagree.
/// </para>
/// </remarks>
public static class PortTypeName
{
    /// <summary>The display name for a port's declared type.</summary>
    /// <param name="valueType">The port's type. A list type is named by its element.</param>
    /// <returns>A short lower-case word for a primitive, or the type's own name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="valueType"/> is <see langword="null"/>.</exception>
    public static string Describe(Type valueType)
    {
        ArgumentNullException.ThrowIfNull(valueType);

        // The port's shape carries its rank, so the text carries the element type and nothing
        // about how many of them there are. Saying it twice costs width and adds nothing.
        Type element = PortDefinition.ElementTypeOf(valueType) ?? valueType;
        Type type = Nullable.GetUnderlyingType(element) ?? element;

        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            return "number";
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
        {
            return "integer";
        }

        if (type == typeof(bool))
        {
            return "true/false";
        }

        if (type == typeof(string) || type == typeof(char))
        {
            return "text";
        }

        if (type == typeof(Angle))
        {
            // Held in radians and typed in degrees, and the unit is the whole reason a user needs
            // to be told: a bare "angle" invites somebody to type 1.5708.
            return "degrees";
        }

        if (type == typeof(object))
        {
            return "anything";
        }

        return type.Name;
    }

    /// <summary>
    /// The type name to show beside a port, or <see langword="null"/> when the port's own name
    /// already says it.
    /// </summary>
    /// <remarks>
    /// The suppression is not tidiness. Nodes are laid out to fit their widest row, so a redundant
    /// word makes every `Circle.By*` node wider for no information — and a canvas of nodes reading
    /// "circle Circle" and "point Point" teaches a user to stop reading the column that is
    /// sometimes the answer.
    /// </remarks>
    /// <param name="portName">The port's display name.</param>
    /// <param name="valueType">The port's declared type.</param>
    /// <returns>The type name, or null when it would only repeat the port name.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static string? Beside(string portName, Type valueType)
    {
        ArgumentNullException.ThrowIfNull(portName);

        string described = Describe(valueType);

        if (string.Equals(portName, described, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // `points` returning a list of `Point` is the same word, and the ring on the port is what
        // says there are several of them.
        if (portName.EndsWith('s')
            && string.Equals(portName[..^1], described, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return described;
    }
}
