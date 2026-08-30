using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Spark.Scripting;

/// <summary>
/// Spells a <see cref="Type"/> the way C# source has to spell it, or says that it cannot be spelt
/// (`E6-T6`).
/// </summary>
/// <remarks>
/// <para>
/// A generated declaration needs a type <i>name</i>, and <see cref="Type.FullName"/> is not one:
/// it writes a nested type with a <c>+</c>, a generic type as <c>List`1[[System.Double, …]]</c>,
/// and an array of arrays in an order that is not C#'s. Each of those produces a compiler error on
/// a line the user did not write.
/// </para>
/// <para>
/// <b>Refusing is a first-class answer.</b> Some types cannot appear in a generated declaration at
/// all — an internal type, a nested type inside an internal one, a generic parameter, a pointer, an
/// anonymous type. There is a correct thing to do about every one of them, and it is to fall back
/// to <c>dynamic</c>, which is exactly what an unwired port already gets. So this returns
/// <see langword="null"/> rather than a name that will not compile, and the caller reads null as
/// <i>use dynamic</i>.
/// </para>
/// <para>
/// <b>Every name is written with <c>global::</c>.</b> A script that declares
/// <c>class Point3d { }</c> of its own would otherwise capture the generated declaration and turn a
/// working graph into an error inside code the user cannot see.
/// </para>
/// </remarks>
public static class ScriptTypeName
{
    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char",
        [typeof(decimal)] = "decimal",
        [typeof(double)] = "double",
        [typeof(float)] = "float",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(object)] = "object",
        [typeof(string)] = "string",
    };

    /// <summary>The C# spelling of a type, or null when it has none a generated file could use.</summary>
    /// <param name="type">The type to spell.</param>
    /// <returns>Source such as <c>global::Spark.Geometry.Point3d</c>, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public static string? Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (Keywords.TryGetValue(type, out string? keyword))
        {
            return keyword;
        }

        if (type.IsPointer || type.IsByRef || type.IsGenericParameter || type.ContainsGenericParameters)
        {
            return null;
        }

        if (type.IsArray)
        {
            string? element = Of(type.GetElementType()!);
            int rank = type.GetArrayRank();

            return element is null
                ? null
                : element + "[" + new string(',', rank - 1) + "]";
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            string? inner = Of(underlying);

            return inner is null ? null : inner + "?";
        }

        if (!IsVisible(type))
        {
            return null;
        }

        // A compiler-generated type - an anonymous type, an iterator's state machine - has a name
        // that is not an identifier, and no source can name it.
        if (type.Name.AsSpan().IndexOfAny('<', '>') >= 0 && !type.IsGenericType)
        {
            return null;
        }

        StringBuilder name = new("global::");

        if (!string.IsNullOrEmpty(type.Namespace))
        {
            name.Append(type.Namespace).Append('.');
        }

        // Nested types are written outer-first with dots, which is the one place `FullName`'s `+`
        // has to be undone. The enclosing chain is walked rather than the string split, because a
        // namespace can contain a `+` on some platforms and a type name cannot.
        List<Type> chain = [];
        for (Type? walk = type; walk is not null; walk = walk.DeclaringType)
        {
            chain.Add(walk);
        }

        chain.Reverse();

        Type[] arguments = type.IsGenericType ? type.GetGenericArguments() : [];
        int consumed = 0;

        for (int i = 0; i < chain.Count; i++)
        {
            if (i > 0)
            {
                name.Append('.');
            }

            Type link = chain[i];
            string simple = link.Name;
            int tick = simple.IndexOf('`', StringComparison.Ordinal);

            if (tick < 0)
            {
                name.Append(simple);
                continue;
            }

            name.Append(simple, 0, tick);

            // A nested generic type's argument list is flat and outer-first, so each link takes the
            // next few. Reading them off in order is what makes `Outer<int>.Inner<string>` spell
            // itself correctly rather than repeating the outer arguments.
            int count = int.Parse(simple.AsSpan(tick + 1), CultureInfo.InvariantCulture);
            string?[] spelt = [.. arguments.Skip(consumed).Take(count).Select(Of)];
            consumed += count;

            if (spelt.Any(argument => argument is null))
            {
                return null;
            }

            name.Append('<').Append(string.Join(", ", spelt)).Append('>');
        }

        return name.ToString();
    }

    /// <summary>Whether a type and everything enclosing it can be named from another assembly.</summary>
    private static bool IsVisible(Type type)
    {
        for (Type? walk = type; walk is not null; walk = walk.DeclaringType)
        {
            TypeInfo info = walk.GetTypeInfo();

            if (walk.IsNested ? !info.IsNestedPublic : !info.IsPublic)
            {
                return false;
            }
        }

        return true;
    }
}
