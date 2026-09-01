using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spark.Api;

namespace Spark.Scripting;

/// <summary>
/// The type each of a code block's output ports carries, read from the script's return statement
/// (`E6-T25`).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every output port used to be <see cref="object"/>, and that made half the library
/// unreachable.</b> A block returning a <c>Circle</c> could not be wired into a port declared
/// <c>Curve</c>, because <c>object</c> into <c>Curve</c> is a *narrowing* and
/// <c>TypeCompatibility</c> refuses those at the moment the wire is drawn — deliberately, so that
/// a downcast is a node on the canvas rather than a silent cast inside a wire. The block's inputs
/// have taken their type from the wire since `E6-T7` and from a declaration since `E6-T19`; the
/// outputs had no equivalent, and the asymmetry is what a user hits.
/// </para>
/// <para>
/// <b>The compiler already knows.</b> The generated frame's <c>Run</c> returns <c>object</c> —
/// it has to, the invocation contract is <c>object?[] (object?[], CancellationToken)</c> — but the
/// *expression* in the user's <c>return</c> has a natural type, and the semantic model of the
/// compilation that is being emitted anyway will say what it is. Nothing extra is compiled for
/// this.
/// </para>
/// <para>
/// <b>The mapping from a symbol to a <see cref="Type"/> is deliberately partial.</b> Roslyn's type
/// system is bigger than the reflection one that a port needs, and a port typed *wrongly* is worse
/// than a port typed <see cref="object"/>: it refuses wires that ought to be legal, and the refusal
/// names a type the user never wrote. So everything this cannot resolve with certainty — an
/// anonymous type, an error type, <c>dynamic</c>, a nullable value type, a type from an assembly
/// that is not loaded — comes back as <see cref="object"/>, which is exactly the behaviour that
/// existed before.
/// </para>
/// </remarks>
internal static class ScriptOutputTypes
{
    /// <summary>The generated method the user's return statements belong to.</summary>
    private const string EntryPoint = "Run";

    /// <summary>
    /// Types the output ports a script's syntax already named.
    /// </summary>
    /// <param name="compilation">The compilation that is about to be emitted.</param>
    /// <param name="tree">The syntax tree inside it — the generated frame, guards woven in.</param>
    /// <param name="ports">The ports read from the syntax, in order, all typed object.</param>
    /// <returns>The same ports, each carrying whatever the compiler says the script returns.</returns>
    /// <remarks>
    /// The ports come in from <c>OutputsOf</c> rather than being rebuilt here, because their
    /// <i>names</i> are syntax — a tuple element name exists nowhere else — and only their types
    /// are semantic. Two passes over the same tree that disagreed about how many ports there are
    /// would be a defect nothing could diagnose.
    /// </remarks>
    internal static ScriptPort[] Infer(Compilation compilation, SyntaxTree tree, ScriptPort[] ports)
    {
        if (ports.Length == 0)
        {
            return ports;
        }

        SemanticModel model;

        try
        {
            model = compilation.GetSemanticModel(tree);
        }
        catch (ArgumentException)
        {
            // The tree is not part of this compilation. Nothing is knowable, and object is what
            // the port would have been anyway.
            return ports;
        }

        List<ExpressionSyntax>[] returned = new List<ExpressionSyntax>[ports.Length];

        for (int i = 0; i < returned.Length; i++)
        {
            returned[i] = [];
        }

        foreach (ReturnStatementSyntax statement in Returns(tree))
        {
            if (statement.Expression is null)
            {
                continue;
            }

            // A tuple return feeds one port per element; anything else feeds the single port.
            if (ports.Length > 1 && statement.Expression is TupleExpressionSyntax tuple)
            {
                if (tuple.Arguments.Count != ports.Length)
                {
                    continue;
                }

                for (int i = 0; i < ports.Length; i++)
                {
                    returned[i].Add(tuple.Arguments[i].Expression);
                }

                continue;
            }

            if (ports.Length == 1)
            {
                returned[0].Add(statement.Expression);
            }
        }

        ScriptPort[] typed = new ScriptPort[ports.Length];

        for (int i = 0; i < ports.Length; i++)
        {
            typed[i] = ports[i] with { ValueType = TypeOf(model, returned[i]) ?? typeof(object) };
        }

        return typed;
    }

    /// <summary>
    /// The return statements of the generated entry point, and of nothing else.
    /// </summary>
    /// <remarks>
    /// <b>A <c>return</c> inside a local function or a lambda belongs to that function</b>, and
    /// typing the block's port from it would be nonsense — a script whose last line is
    /// <c>return points.Select(p =&gt; p.X).ToList();</c> has a <c>return</c> in a lambda that
    /// yields a <see cref="double"/> and a real return that yields a list. So the nearest enclosing
    /// function of each candidate is checked, rather than its nearest enclosing *method*.
    /// </remarks>
    private static IEnumerable<ReturnStatementSyntax> Returns(SyntaxTree tree)
    {
        MethodDeclarationSyntax? entry = tree
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == EntryPoint);

        if (entry is null)
        {
            yield break;
        }

        foreach (ReturnStatementSyntax statement in entry.DescendantNodes().OfType<ReturnStatementSyntax>())
        {
            if (Owner(statement) == entry)
            {
                yield return statement;
            }
        }
    }

    /// <summary>The function a statement returns from.</summary>
    private static SyntaxNode? Owner(SyntaxNode statement)
    {
        for (SyntaxNode? node = statement.Parent; node is not null; node = node.Parent)
        {
            if (node is MethodDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AnonymousFunctionExpressionSyntax
                or AccessorDeclarationSyntax)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>
    /// The one type every candidate expression has, or null when they do not agree on one.
    /// </summary>
    /// <remarks>
    /// <b>Disagreement means <see cref="object"/> rather than a common base.</b> Two returns of
    /// <c>Circle</c> and <c>Line</c> could be called <c>Curve</c>, and working that out requires
    /// walking two hierarchies and choosing between several equally true answers — one of which is
    /// <c>object</c>. A port typed from the *only* type the script can return is a fact; a port
    /// typed from a guess is the thing this class exists not to produce.
    /// </remarks>
    private static Type? TypeOf(SemanticModel model, IReadOnlyList<ExpressionSyntax> expressions)
    {
        Type? agreed = null;

        foreach (ExpressionSyntax expression in expressions)
        {
            Microsoft.CodeAnalysis.TypeInfo info = model.GetTypeInfo(expression);
            Type? candidate = Resolve(info.Type);

            if (candidate is null)
            {
                return null;
            }

            if (agreed is null)
            {
                agreed = candidate;
                continue;
            }

            if (agreed != candidate)
            {
                return null;
            }
        }

        return agreed;
    }

    /// <summary>The runtime type a symbol names, or null when it cannot be named with certainty.</summary>
    private static Type? Resolve(ITypeSymbol? symbol)
    {
        if (symbol is null || symbol.TypeKind is TypeKind.Error or TypeKind.Dynamic or TypeKind.Pointer)
        {
            return null;
        }

        if (Special(symbol.SpecialType) is { } special)
        {
            return special;
        }

        if (symbol is IArrayTypeSymbol array)
        {
            return array.Rank == 1 && Resolve(array.ElementType) is { } element
                ? element.MakeArrayType()
                : null;
        }

        if (symbol is not INamedTypeSymbol named || named.IsAnonymousType)
        {
            return null;
        }

        // A nullable value type would give a port that refuses the very value it is meant to
        // carry: `double?` is not assignable from `double`, so the wire the user is trying to draw
        // would be refused by the type this inferred for them.
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return null;
        }

        Type? definition = Named(named.ConstructedFrom ?? named);

        if (definition is null || !named.IsGenericType)
        {
            return definition;
        }

        Type[] arguments = new Type[named.TypeArguments.Length];

        for (int i = 0; i < arguments.Length; i++)
        {
            if (Resolve(named.TypeArguments[i]) is not { } argument)
            {
                return null;
            }

            arguments[i] = argument;
        }

        try
        {
            return definition.MakeGenericType(arguments);
        }
        catch (Exception failure) when (failure is ArgumentException or TypeLoadException)
        {
            return null;
        }
    }

    /// <summary>The reflection type for a named symbol, found in an assembly already loaded.</summary>
    /// <remarks>
    /// <b>Loaded assemblies rather than the reference paths</b>, because a port carries a
    /// <see cref="Type"/> that the engine compares with <c>IsAssignableFrom</c> against types the
    /// process is already using — and a type loaded a second time from its file is a *different*
    /// type to that comparison, which is the same trap `TypeCompatibility`'s same-name rule exists
    /// to explain. An assembly nobody has loaded therefore resolves to nothing, and the port stays
    /// <see cref="object"/>.
    /// </remarks>
    private static Type? Named(INamedTypeSymbol symbol)
    {
        if (symbol.ContainingAssembly?.Identity.Name is not { Length: > 0 } assemblyName)
        {
            return null;
        }

        string? metadataName = MetadataNameOf(symbol);

        if (metadataName is null)
        {
            return null;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || assembly.GetName().Name != assemblyName)
            {
                continue;
            }

            if (assembly.GetType(metadataName, throwOnError: false) is { } found)
            {
                return found;
            }
        }

        // The BCL is spread over facade assemblies — a symbol from `System.Runtime` is a type in
        // `System.Private.CoreLib` — so the name alone gets one more chance.
        return Type.GetType(metadataName, throwOnError: false);
    }

    /// <summary>A symbol's name as reflection spells it: namespace, nested types with a `+`.</summary>
    private static string? MetadataNameOf(INamedTypeSymbol symbol)
    {
        string name = symbol.MetadataName;

        for (INamedTypeSymbol? containing = symbol.ContainingType;
            containing is not null;
            containing = containing.ContainingType)
        {
            name = containing.MetadataName + "+" + name;
        }

        INamespaceSymbol? space = symbol.ContainingNamespace;

        return space is null || space.IsGlobalNamespace
            ? name
            : space.ToDisplayString() + "." + name;
    }

    /// <summary>The runtime type of a special symbol, or null when it is not one.</summary>
    private static Type? Special(SpecialType special) => special switch
    {
        SpecialType.System_Boolean => typeof(bool),
        SpecialType.System_Byte => typeof(byte),
        SpecialType.System_SByte => typeof(sbyte),
        SpecialType.System_Int16 => typeof(short),
        SpecialType.System_UInt16 => typeof(ushort),
        SpecialType.System_Int32 => typeof(int),
        SpecialType.System_UInt32 => typeof(uint),
        SpecialType.System_Int64 => typeof(long),
        SpecialType.System_UInt64 => typeof(ulong),
        SpecialType.System_Single => typeof(float),
        SpecialType.System_Double => typeof(double),
        SpecialType.System_Decimal => typeof(decimal),
        SpecialType.System_Char => typeof(char),
        SpecialType.System_String => typeof(string),
        SpecialType.System_Object => typeof(object),
        _ => null,
    };
}
