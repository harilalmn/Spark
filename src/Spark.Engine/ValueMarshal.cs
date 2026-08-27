using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// Thrown when a graph value cannot be turned into the CLR type a port declares, or back again.
/// </summary>
/// <remarks>
/// This is caught by the replicator and turned into either a typed diagnostic or an isolated
/// per-element failure, depending on whether replication was in progress. It is not intended to
/// escape the engine.
/// </remarks>
public sealed class ValueMarshallingException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What could not be converted, and to what.</param>
    public ValueMarshallingException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What could not be converted, and to what.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ValueMarshallingException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with no message. Provided for the framework's benefit.</summary>
    public ValueMarshallingException()
    {
    }
}

/// <summary>
/// Converts between graph values — scalars and <see cref="SparkList"/> — and the CLR types node
/// members are written against.
/// </summary>
/// <remarks>
/// <para>
/// This is the performance-critical path of the whole engine: replication over a hundred thousand
/// items runs through it a hundred thousand times. The implementation deliberately does no boxing
/// it can avoid and no reflection per element beyond a cached element type.
/// </para>
/// <para>
/// <b>Conversions are widening only.</b> An <see cref="int"/> becomes a <see cref="double"/>
/// because nothing is lost; a <see cref="double"/> does not become an <see cref="int"/>, and
/// <c>"5"</c> does not become <c>5</c>. Parsing and narrowing are things a user asks for with a
/// node, so that the intent is visible on the canvas.
/// </para>
/// </remarks>
public static class ValueMarshal
{
    /// <summary>
    /// Wraps a value in one-element lists until its rank rises by <paramref name="levels"/>. This is
    /// promotion, and by decision D2 it happens at the leaf call rather than before replication, so
    /// that the ranks the engine reports stay equal to the ranks a user can see on the wire.
    /// </summary>
    /// <param name="value">The value to promote.</param>
    /// <param name="levels">How many levels to add. Zero returns the value unchanged.</param>
    /// <returns>The promoted value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="levels"/> is negative.</exception>
    public static object? Promote(object? value, int levels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(levels);

        object? promoted = value;
        for (int level = 0; level < levels; level++)
        {
            promoted = new SparkList([promoted], SparkList.RankOf(promoted) + 1);
        }

        return promoted;
    }

    /// <summary>
    /// Converts a graph value into the CLR type a port declares.
    /// </summary>
    /// <param name="value">The graph value.</param>
    /// <param name="targetType">The declared CLR type.</param>
    /// <returns>The converted value, ready to be passed to a compiled invoker.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="targetType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ValueMarshallingException">The value cannot be represented as that type.</exception>
    public static object? ToClr(object? value, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        // An object port takes anything, including a SparkList. This is what makes
        // [KeepStructure] over object work, and it is why object declares rank 0.
        if (targetType == typeof(object))
        {
            return value;
        }

        if (value is null)
        {
            if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null)
            {
                return null;
            }

            throw new ValueMarshallingException($"null cannot be supplied to a port declared {Describe(targetType)}.");
        }

        Type? elementType = PortDefinition.ElementTypeOf(targetType);
        if (elementType is not null)
        {
            if (value is not SparkList list)
            {
                throw new ValueMarshallingException(
                    $"a {Describe(value.GetType())} cannot be supplied to a port declared {Describe(targetType)}.");
            }

            return ToClrList(list, targetType, elementType);
        }

        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (value is SparkList suppliedList)
        {
            throw new ValueMarshallingException(
                $"a rank-{suppliedList.Rank} list cannot be supplied to a port declared {Describe(targetType)}.");
        }

        if (TryWidenNumeric(value, targetType, out object? widened))
        {
            return widened;
        }

        throw new ValueMarshallingException(
            $"a {Describe(value.GetType())} cannot be supplied to a port declared {Describe(targetType)}.");
    }

    /// <summary>
    /// Converts a value produced by a node back into a graph value, turning declared list types
    /// into <see cref="SparkList"/> so that rank is explicit from that point on.
    /// </summary>
    /// <param name="value">The value the node produced.</param>
    /// <param name="declaredRank">The output port's declared rank.</param>
    /// <returns>The graph value.</returns>
    /// <remarks>
    /// The rank of the result comes from its contents where there are any, and from
    /// <paramref name="declaredRank"/> where the list is empty. That second half is decision D8:
    /// an empty list keeps the rank of the structure that produced it.
    /// </remarks>
    public static object? FromClr(object? value, int declaredRank)
    {
        if (value is SparkList || value is null)
        {
            return value;
        }

        if (declaredRank <= 0 || value is string || value is not IEnumerable enumerable)
        {
            return value;
        }

        List<object?> items = [];
        int deepest = 0;

        foreach (object? item in enumerable)
        {
            object? converted = FromClr(item, declaredRank - 1);
            items.Add(converted);

            int rank = SparkList.RankOf(converted);
            if (rank > deepest)
            {
                deepest = rank;
            }
        }

        return new SparkList(items, items.Count == 0 ? declaredRank : deepest + 1);
    }

    private static object ToClrList(SparkList list, Type targetType, Type elementType)
    {
        Array array = Array.CreateInstance(elementType, list.Count);
        for (int index = 0; index < list.Count; index++)
        {
            array.SetValue(ToClr(list[index], elementType), index);
        }

        if (targetType.IsInstanceOfType(array))
        {
            return array;
        }

        // The declared type is something an array does not satisfy - List<T>, for instance.
        // Materialise it rather than refusing, because refusing would make a perfectly ordinary
        // node signature unusable for a reason the author cannot see.
        object materialised = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType), array)
            ?? throw new ValueMarshallingException($"could not build a {Describe(targetType)}.");

        return targetType.IsInstanceOfType(materialised)
            ? materialised
            : throw new ValueMarshallingException($"a list cannot be supplied to a port declared {Describe(targetType)}.");
    }

    private static bool TryWidenNumeric(object value, Type targetType, out object? widened)
    {
        widened = null;

        Type sourceType = value.GetType();
        if (!IsNumeric(sourceType) || !IsNumeric(targetType))
        {
            return false;
        }

        if (!NumericConversions.IsWidening(sourceType, targetType))
        {
            return false;
        }

        widened = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool IsNumeric(Type type) => NumericConversions.IsNumeric(type);

    private static string Describe(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        string name = type.Name;
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick > 0)
        {
            name = name[..tick];
        }

        Type[] arguments = type.GetGenericArguments();
        string[] described = new string[arguments.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            described[index] = Describe(arguments[index]);
        }

        return $"{name}<{string.Join(", ", described)}>";
    }
}
