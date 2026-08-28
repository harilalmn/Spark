using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Spark.Scripting;

/// <summary>
/// Writes a CLR type as C# source, and reads a Roslyn type symbol back as a CLR type.
/// </summary>
/// <remarks>
/// Both directions are needed for the same reason: a code block's ports are CLR types the graph
/// wires with, and the same ports are C# declarations the user's code reads. Every name written here
/// is <c>global::</c>-rooted, because a code block may declare any name it likes and a port
/// declaration that could be shadowed by user code is a port declaration that will be, eventually.
/// </remarks>
internal static class TypeNames
{
    private static readonly Dictionary<SpecialType, Type> SpecialTypes = new()
    {
        [SpecialType.System_Object] = typeof(object),
        [SpecialType.System_Boolean] = typeof(bool),
        [SpecialType.System_Char] = typeof(char),
        [SpecialType.System_SByte] = typeof(sbyte),
        [SpecialType.System_Byte] = typeof(byte),
        [SpecialType.System_Int16] = typeof(short),
        [SpecialType.System_UInt16] = typeof(ushort),
        [SpecialType.System_Int32] = typeof(int),
        [SpecialType.System_UInt32] = typeof(uint),
        [SpecialType.System_Int64] = typeof(long),
        [SpecialType.System_UInt64] = typeof(ulong),
        [SpecialType.System_Decimal] = typeof(decimal),
        [SpecialType.System_Single] = typeof(float),
        [SpecialType.System_Double] = typeof(double),
        [SpecialType.System_String] = typeof(string),
        [SpecialType.System_IntPtr] = typeof(IntPtr),
        [SpecialType.System_UIntPtr] = typeof(UIntPtr),
        [SpecialType.System_DateTime] = typeof(DateTime),
    };

    /// <summary>Writes a CLR type as a fully qualified, <c>global::</c>-rooted C# type name.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The C# source name, or <c>object</c> when the type cannot be written as one.</returns>
    internal static string CSharpName(Type type)
    {
        if (type == typeof(object) || type == typeof(void))
        {
            return "object";
        }

        if (type.IsArray)
        {
            int rank = type.GetArrayRank();
            string commas = rank > 1 ? new string(',', rank - 1) : string.Empty;
            Type? element = type.GetElementType();
            return element is null ? "object" : $"{CSharpName(element)}[{commas}]";
        }

        if (type.IsGenericParameter || type.IsPointer || type.IsByRef || type.FullName is null)
        {
            return "object";
        }

        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            string name = StripArity(definition.FullName ?? definition.Name).Replace('+', '.');
            string arguments = string.Join(", ", type.GetGenericArguments().Select(CSharpName));
            return $"global::{name}<{arguments}>";
        }

        return $"global::{type.FullName.Replace('+', '.')}";
    }

    /// <summary>Resolves a Roslyn type symbol to the CLR type the graph should wire with.</summary>
    /// <param name="symbol">The symbol, or <see langword="null"/>.</param>
    /// <returns>
    /// The CLR type, or <see cref="object"/> when it cannot be resolved in this process — which is
    /// the honest answer for a type the script itself declares, since that type does not exist until
    /// the script's assembly is loaded and cannot outlive it.
    /// </returns>
    internal static Type Resolve(ITypeSymbol? symbol)
    {
        if (symbol is null || symbol.TypeKind == TypeKind.Error)
        {
            return typeof(object);
        }

        if (SpecialTypes.TryGetValue(symbol.SpecialType, out Type? special))
        {
            return special;
        }

        if (symbol is IArrayTypeSymbol array)
        {
            Type element = Resolve(array.ElementType);
            return element == typeof(object) && array.ElementType.TypeKind == TypeKind.Error
                ? typeof(object)
                : element.MakeArrayType();
        }

        if (symbol is not INamedTypeSymbol named || named.IsTupleType || named.IsAnonymousType)
        {
            return typeof(object);
        }

        Type? definition = FindType(named.ConstructedFrom);
        if (definition is null)
        {
            return typeof(object);
        }

        if (!named.IsGenericType || named.TypeArguments.Length == 0)
        {
            return definition;
        }

        Type[] arguments = new Type[named.TypeArguments.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            arguments[index] = Resolve(named.TypeArguments[index]);
        }

        try
        {
            return definition.MakeGenericType(arguments);
        }
        catch (ArgumentException)
        {
            // A constraint the resolved arguments do not satisfy, most often because one of them
            // fell back to object. The port types down to object rather than failing the compile.
            return typeof(object);
        }
    }

    /// <summary>Finds a CLR type by assembly-qualified name, falling back to a scan of loaded assemblies.</summary>
    /// <param name="assemblyQualifiedName">The name written by <see cref="Type.AssemblyQualifiedName"/>.</param>
    /// <returns>The type, or <see langword="null"/>.</returns>
    internal static Type? FromAssemblyQualifiedName(string? assemblyQualifiedName)
    {
        if (string.IsNullOrWhiteSpace(assemblyQualifiedName))
        {
            return null;
        }

        Type? found = Type.GetType(assemblyQualifiedName, throwOnError: false);
        if (found is not null)
        {
            return found;
        }

        int comma = assemblyQualifiedName.IndexOf(',', StringComparison.Ordinal);
        if (comma <= 0)
        {
            return null;
        }

        string typeName = assemblyQualifiedName[..comma].Trim();

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            Type? candidate = assembly.GetType(typeName, throwOnError: false);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static Type? FindType(INamedTypeSymbol symbol)
    {
        string? metadataName = MetadataNameOf(symbol);
        if (metadataName is null)
        {
            return null;
        }

        string? assemblyName = symbol.ContainingAssembly?.Identity.Name;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            if (assemblyName is not null
                && !string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Type? candidate = assembly.GetType(metadataName, throwOnError: false);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        // The declaring assembly may be referenced as metadata without being loaded — type
        // forwarding across the shared framework is the usual reason. Ask everything else.
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            Type? candidate = assembly.GetType(metadataName, throwOnError: false);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The reflection metadata name of a symbol: <c>Namespace.Outer+Inner`1</c>.</summary>
    private static string? MetadataNameOf(INamedTypeSymbol symbol)
    {
        List<string> parts = [];

        for (INamedTypeSymbol? current = symbol; current is not null; current = current.ContainingType)
        {
            parts.Add(current.MetadataName);
        }

        parts.Reverse();

        StringBuilder builder = new();

        if (symbol.ContainingNamespace is { IsGlobalNamespace: false })
        {
            builder.Append(symbol.ContainingNamespace.ToDisplayString()).Append('.');
        }

        builder.AppendJoin('+', parts);

        string name = builder.ToString();
        return name.Length == 0 ? null : name;
    }

    private static string StripArity(string name)
    {
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }

    /// <summary>Renders a type for a message, without the <c>global::</c> ceremony.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The short name.</returns>
    internal static string Describe(Type type) =>
        type.IsGenericType
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{StripArity(type.Name)}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>")
            : type.Name;
}
