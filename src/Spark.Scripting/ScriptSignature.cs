using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spark.Scripting;

/// <summary>
/// One overload, reduced to what an editor needs to draw it.
/// </summary>
/// <param name="Name">The method's name, or the type's name for a constructor.</param>
/// <param name="Parameters">Each parameter as <c>Point3d centre</c> — type then name.</param>
/// <param name="ReturnType">What the call evaluates to, spelt shortly; empty for a constructor.</param>
/// <remarks>
/// <b>Split into its parts rather than handed over as one string.</b> The editor bolds the
/// parameter the caret is on, and a caller given <c>ByCentreNormalRadius(Point3d centre, …)</c> as
/// one line would have to parse the commas back out of it — inside type arguments and default
/// values, where that is wrong.
/// </remarks>
public readonly record struct ScriptSignatureItem(
    string Name,
    IReadOnlyList<string> Parameters,
    string ReturnType);

/// <summary>
/// The overloads available at a caret inside an argument list, and which one is being written.
/// </summary>
/// <param name="Signatures">The overloads, in ascending parameter count.</param>
/// <param name="ActiveSignature">The index of the one the argument list best fits.</param>
/// <param name="ActiveParameter">The parameter the caret is on, counting from zero.</param>
public readonly record struct ScriptSignatureHelp(
    IReadOnlyList<ScriptSignatureItem> Signatures,
    int ActiveSignature,
    int ActiveParameter);

/// <summary>
/// Finds the overloads for the call a caret is inside, from a Roslyn document.
/// </summary>
/// <remarks>
/// <para>
/// <b>Roslyn does not publish a signature-help service.</b> <c>ISignatureHelpProvider</c> and
/// everything around it is internal to the Features layer — unlike <c>CompletionService</c>, which
/// is public and is what <see cref="ScriptCompletion.CompleteAsync"/> uses. So this is the
/// semantic model directly: find the innermost argument list containing the caret, ask for the
/// member group of the thing being called, and count the separators before the caret.
/// </para>
/// <para>
/// <b>The member group, not the resolved symbol.</b> While a call is being typed it does not bind
/// — <c>Circle.ByCentreNormalRadius(</c> has no arguments at all, so overload resolution fails —
/// which means <c>GetSymbolInfo(...).Symbol</c> is null exactly when signature help is wanted.
/// <c>GetMemberGroup</c> answers with every accessible overload regardless, and that is the whole
/// list the popup cycles through anyway.
/// </para>
/// </remarks>
internal static class ScriptSignature
{
    /// <summary>How a signature and its parameters are spelt: short type names, no namespaces.</summary>
    private static readonly SymbolDisplayFormat Short = SymbolDisplayFormat.MinimallyQualifiedFormat;

    /// <summary>Finds the overloads for the call surrounding a caret.</summary>
    /// <param name="document">The script document, already holding the text.</param>
    /// <param name="caret">The caret offset within that document.</param>
    /// <param name="cancellationToken">Cancels a request a later keystroke has superseded.</param>
    /// <returns>The overloads, or null when the caret is not inside a call's arguments.</returns>
    internal static async Task<ScriptSignatureHelp?> FindAsync(
        Document document,
        int caret,
        CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        if (root is null || model is null)
        {
            return null;
        }

        (BaseArgumentListSyntax? list, ExpressionSyntax? call) = Enclosing(root, caret);

        if (list is null || call is null)
        {
            return null;
        }

        ImmutableArray<IMethodSymbol> overloads = Overloads(model, call, cancellationToken);

        if (overloads.IsDefaultOrEmpty)
        {
            return null;
        }

        int active = Separators(list, caret);

        ScriptSignatureItem[] items =
        [
            .. overloads
                .OrderBy(method => method.Parameters.Length)
                .Select(Describe),
        ];

        return new ScriptSignatureHelp(items, Best(items, active), active);
    }

    /// <summary>
    /// The innermost argument list the caret is inside, with the expression being called.
    /// </summary>
    /// <remarks>
    /// <b>The token is looked up one before the caret</b>, because a caret sitting immediately
    /// after <c>(</c> is at the <i>start</i> of the token that follows it — and with nothing typed
    /// yet, that token is whatever comes after the call entirely. Looking backwards lands on the
    /// parenthesis, whose parent is the argument list, which is what makes <c>Foo(</c> answer at
    /// all.
    /// </remarks>
    private static (BaseArgumentListSyntax? List, ExpressionSyntax? Call) Enclosing(SyntaxNode root, int caret)
    {
        SyntaxToken token = root.FindToken(Math.Max(0, caret - 1));

        for (SyntaxNode? node = token.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocation when Inside(invocation.ArgumentList, caret):
                    return (invocation.ArgumentList, invocation.Expression);

                case ObjectCreationExpressionSyntax creation
                    when creation.ArgumentList is { } arguments && Inside(arguments, caret):
                    return (arguments, creation.Type);

                default:
                    break;
            }
        }

        return (null, null);
    }

    /// <summary>Whether a caret is between an argument list's parentheses.</summary>
    /// <remarks>
    /// The closing parenthesis is usually <i>missing</i> while the call is being typed, and a
    /// missing token has a zero-width span sitting at the end of what was typed — so treating it
    /// as the far edge is right in both the finished and the unfinished case.
    /// </remarks>
    private static bool Inside(BaseArgumentListSyntax list, int caret)
    {
        SyntaxToken open = default;
        SyntaxToken close = default;

        foreach (SyntaxToken token in list.ChildTokens())
        {
            if (token.IsKind(SyntaxKind.OpenParenToken) || token.IsKind(SyntaxKind.OpenBracketToken))
            {
                open = token;
            }
            else if (token.IsKind(SyntaxKind.CloseParenToken) || token.IsKind(SyntaxKind.CloseBracketToken))
            {
                close = token;
            }
        }

        if (open == default)
        {
            return false;
        }

        return caret > open.SpanStart
            && (close == default || close.IsMissing || caret <= close.SpanStart);
    }

    /// <summary>Every overload of the thing being called that a script may use.</summary>
    private static ImmutableArray<IMethodSymbol> Overloads(
        SemanticModel model,
        ExpressionSyntax call,
        CancellationToken cancellationToken)
    {
        SymbolInfo info = model.GetSymbolInfo(call, cancellationToken);

        // `new Point3d(` — the expression is the *type*, and its constructors are the overloads.
        if (info.Symbol is INamedTypeSymbol type)
        {
            return [.. type.InstanceConstructors.Where(Visible)];
        }

        ImmutableArray<ISymbol> group = model.GetMemberGroup(call, cancellationToken);

        if (!group.IsDefaultOrEmpty)
        {
            return [.. group.OfType<IMethodSymbol>().Where(Visible)];
        }

        // A call that *does* bind has an empty member group in some positions, so the resolved
        // symbol — and the candidates it lost to — are the fallback rather than the first choice.
        return
        [
            .. (info.Symbol is null ? info.CandidateSymbols : [info.Symbol])
                .OfType<IMethodSymbol>()
                .Where(Visible),
        ];
    }

    /// <summary>Whether a script could call this overload at all.</summary>
    private static bool Visible(IMethodSymbol method) =>
        method.DeclaredAccessibility == Accessibility.Public;

    /// <summary>How many argument separators sit before the caret — which parameter is being typed.</summary>
    private static int Separators(BaseArgumentListSyntax list, int caret)
    {
        SeparatedSyntaxList<ArgumentSyntax> arguments = list switch
        {
            ArgumentListSyntax parenthesised => parenthesised.Arguments,
            BracketedArgumentListSyntax bracketed => bracketed.Arguments,
            _ => default,
        };

        int separators = 0;

        foreach (SyntaxToken separator in arguments.GetSeparators())
        {
            if (separator.SpanStart < caret)
            {
                separators++;
            }
        }

        return separators;
    }

    /// <summary>The overload an argument list this long best fits.</summary>
    /// <remarks>
    /// The first that has a parameter for the one being typed, which is what an editor should show
    /// while the call is still short: with two arguments written, the two-parameter overload is the
    /// honest guess and the six-parameter one is a distraction. When none is long enough — more
    /// arguments written than any overload takes — the longest is shown, because that is the one
    /// whose remaining parameters are worth reading.
    /// </remarks>
    private static int Best(IReadOnlyList<ScriptSignatureItem> items, int active)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Parameters.Count > active)
            {
                return i;
            }
        }

        return Math.Max(0, items.Count - 1);
    }

    /// <summary>One overload, as the editor draws it.</summary>
    private static ScriptSignatureItem Describe(IMethodSymbol method) =>
        new(
            method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name,
            [.. method.Parameters.Select(parameter => parameter.ToDisplayString(Short))],
            method.MethodKind == MethodKind.Constructor
                ? string.Empty
                : method.ReturnType.ToDisplayString(Short));
}
