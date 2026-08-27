using System;
using System.Collections.Generic;

namespace Spark.Engine;

/// <summary>
/// Which numeric conversions Spark performs without being asked.
/// </summary>
/// <remarks>
/// <para>
/// Only widening conversions are automatic — those that cannot lose information for any value of
/// the source type. Narrowing, parsing and rounding are things a user asks for with a node, so that
/// the loss is visible on the canvas rather than buried in a wire.
/// </para>
/// <para>
/// <see cref="long"/> to <see cref="double"/> is included even though a 64-bit integer above
/// 2^53 loses precision, because C# itself defines that conversion as implicit and refusing it here
/// would make Spark's rules disagree with the language a code block is written in.
/// </para>
/// </remarks>
public static class NumericConversions
{
    private static readonly Dictionary<Type, HashSet<Type>> Widenings = BuildWidenings();

    /// <summary>Whether a type is one of the numeric primitives Spark widens between.</summary>
    /// <param name="type">The type to test.</param>
    /// <returns><see langword="true"/> when it is numeric.</returns>
    public static bool IsNumeric(Type type) => Widenings.ContainsKey(type);

    /// <summary>
    /// Whether a value of <paramref name="source"/> can be converted to <paramref name="target"/>
    /// with no loss, for every value of the source type.
    /// </summary>
    /// <param name="source">The source type.</param>
    /// <param name="target">The target type.</param>
    /// <returns><see langword="true"/> when the conversion is widening.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static bool IsWidening(Type source, Type target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (source == target)
        {
            return true;
        }

        return Widenings.TryGetValue(source, out HashSet<Type>? targets) && targets.Contains(target);
    }

    private static Dictionary<Type, HashSet<Type>> BuildWidenings() => new()
    {
        [typeof(sbyte)] = [typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)],
        [typeof(byte)] = [typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
        [typeof(short)] = [typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)],
        [typeof(ushort)] = [typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
        [typeof(int)] = [typeof(long), typeof(float), typeof(double), typeof(decimal)],
        [typeof(uint)] = [typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
        [typeof(long)] = [typeof(float), typeof(double), typeof(decimal)],
        [typeof(ulong)] = [typeof(float), typeof(double), typeof(decimal)],
        [typeof(float)] = [typeof(double)],
        [typeof(double)] = [],
        [typeof(decimal)] = [],
    };
}
