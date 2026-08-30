using System;
using System.Globalization;

namespace Spark.Scripting;

/// <summary>
/// Converts a value arriving on an input port into the type a typed declaration expects
/// (`E6-T6`).
/// </summary>
/// <remarks>
/// <para>
/// <b>Called only by generated code.</b> When a port's type is known, the weaver-side generator
/// declares <c>Point3d centre = ScriptInput.As&lt;Point3d&gt;(__in[0], "centre");</c> rather than
/// <c>dynamic centre = __in[0];</c>.
/// </para>
/// <para>
/// <b>Why not a plain cast.</b> <c>(Point3d)__in[0]</c> is shorter and is what a first version
/// writes. It fails with <c>Unable to cast object of type 'System.Double' to type
/// 'Spark.Geometry.Point3d'</c>, which names two CLR types, no port, and no node — a message that
/// tells a user nothing about the wire they drew. It also fails on the two cases the graph produces
/// most: a number that arrived as <see cref="int"/> where the script wants <see cref="double"/>,
/// and a null on a port whose type is a struct. Each of those is a sentence, and the sentence is
/// worth more than the cast.
/// </para>
/// <para>
/// <b>The type is still known statically at the declaration</b>, which is the whole point: this
/// runs at the boundary, once per port per invocation, and everything after it is ordinary
/// statically-typed C#.
/// </para>
/// </remarks>
public static class ScriptInput
{
    /// <summary>Converts one input value to the type its declaration expects.</summary>
    /// <typeparam name="T">The declared type of the port.</typeparam>
    /// <param name="value">The value the graph delivered.</param>
    /// <param name="port">The port's name, for the message when it cannot be done.</param>
    /// <returns>The value as <typeparamref name="T"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The value is not a <typeparamref name="T"/> and cannot be converted to one.
    /// </exception>
    public static T As<T>(object? value, string port)
    {
        if (value is T typed)
        {
            return typed;
        }

        if (value is null)
        {
            // Null is legitimate on a reference-typed port and is passed through. On a struct port
            // it is not, and saying so beats a NullReferenceException from the cast.
            if (default(T) is null)
            {
                return default!;
            }

            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Nothing arrived on the port '{0}', which the script uses as a {1}. A {1} cannot be nothing — connect it, or give it a value.",
                    port,
                    typeof(T).Name));
        }

        // The numeric widenings a graph produces on its own: a literal typed as an integer feeding
        // a script that does arithmetic in doubles is the common case, and refusing it would make
        // typed inputs feel worse than `dynamic` rather than better.
        if (value is IConvertible && typeof(T).IsPrimitive)
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            }
            catch (Exception failure) when (failure is InvalidCastException or FormatException or OverflowException)
            {
                // Falls through to the message below, which names the port. Convert's own
                // exceptions name neither the port nor the node.
            }
        }

        throw new InvalidOperationException(
            string.Format(
                CultureInfo.InvariantCulture,
                "The port '{0}' received a {1}, but the script uses it as a {2}.",
                port,
                value.GetType().Name,
                typeof(T).Name));
    }
}
