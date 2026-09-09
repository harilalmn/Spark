using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Spark.Scripting;

/// <summary>
/// The types every code block in a graph declares, compiled once into one assembly they all share
/// (`E6-T35`).
/// </summary>
/// <remarks>
/// <para>
/// <b>`E6-T34` let a block declare a type and use it; this is what lets the block <i>next to it</i>
/// use the same one.</b> The client's original report was two blocks — one declaring
/// <c>TestClass</c>, one calling <c>TestClass.Test()</c> — and the second half needs the two
/// compilations to agree on what <c>TestClass</c> is.
/// </para>
/// <para>
/// <b>One shared compilation, not blocks referencing each other, and that choice removes two whole
/// problems rather than solving them.</b> The obvious design gives each block its own assembly and
/// makes a consumer reference its definers: that needs a compile <i>order</i> derived from who uses
/// what, and it needs an answer for a <i>cycle</i> — two types in different blocks that name each
/// other. Gathering every declaration into a single compilation makes both questions disappear,
/// because within one compilation C# has resolved mutual references since version 1. What is left
/// is a name <i>collision</i>, which arrives as an ordinary <c>CS0101</c>, and <i>invalidation</i>,
/// which is real and is paid by putting <see cref="Fingerprint"/> into every block's cache key.
/// </para>
/// <para>
/// <b>A collision is reported once, against one of the two blocks, and that is the compiler's own
/// behaviour rather than a choice made here.</b> <c>CS0101</c> lands on whichever declaration was
/// compiled second — the declarations are sorted by text, so which one that is does not depend on
/// where the nodes sit — and the message names the type, which is what a user needs to find the
/// other one. It also names <c>SparkGenerated</c>, a namespace they have never heard of; putting
/// something better in front of them belongs with the code that shows it to them.
/// </para>
/// <para>
/// <b>A declaration lives in exactly one assembly, and that is forced by type identity rather than
/// chosen for tidiness.</b> Two assemblies each holding <c>SparkGenerated.Helper</c> hold two
/// different CLR types with the same name — so a block that emitted its own copy <i>and</i>
/// referenced the shared one would hand an object down a wire that the receiving block could not
/// cast, and the message would name the same type twice. <see cref="Holds"/> is therefore what
/// <c>ScriptNodeFactory</c> asks before it re-emits anything locally: a script in the set emits
/// none of its own.
/// </para>
/// <para>
/// <b>When the shared compilation does not build, sharing switches itself off.</b> Every script
/// then falls back to `E6-T34`'s behaviour — its own declarations, its own assembly, no
/// cross-block visibility — which is exactly where the product was a moment ago, rather than a
/// graph in which every block has stopped working because one of them has an unclosed brace. The
/// errors are kept on <see cref="Diagnostics"/>, placed on the lines of the block that wrote them,
/// so a caller can still say why.
/// </para>
/// <para>
/// <b>The assembly's name carries the fingerprint</b>, so a rebuilt set is a different assembly
/// rather than a second one with the same name in the same load context — which would bind to
/// whichever arrived first and give a block the members of a version it was not compiled against.
/// The <i>namespace</i> stays <c>SparkGenerated</c>, because that is what makes the user's own
/// <c>Helper</c> nameable as <c>Helper</c>.
/// </para>
/// </remarks>
public sealed class ScriptDeclarations
{
    /// <summary>The namespace every generated type is emitted into.</summary>
    internal const string Namespace = "SparkGenerated";

    private readonly HashSet<string> _shared;
    private readonly Dictionary<string, ImmutableArray<ScriptDiagnostic>> _diagnostics;

    private ScriptDeclarations(
        string fingerprint,
        HashSet<string> shared,
        Dictionary<string, ImmutableArray<ScriptDiagnostic>> diagnostics,
        MetadataReference? reference)
    {
        Fingerprint = fingerprint;
        _shared = shared;
        _diagnostics = diagnostics;
        Reference = reference;
    }

    /// <summary>The empty set: nothing is shared and every block behaves as `E6-T34` left it.</summary>
    public static ScriptDeclarations None { get; } =
        new(string.Empty, new HashSet<string>(StringComparer.Ordinal), [], null);

    /// <summary>
    /// A stable hash of everything in the set, which is what a compile-cache key carries.
    /// </summary>
    /// <remarks>
    /// <b>Independent of the order the scripts arrived in</b>, because the declarations are sorted
    /// before they are hashed and before they are emitted. A user dragging a node into a different
    /// position must not recompile the graph.
    /// </remarks>
    public string Fingerprint { get; }

    /// <summary>Whether the shared compilation produced an assembly.</summary>
    public bool IsShared => Reference is not null;

    /// <summary>The shared assembly as a reference, or null when nothing was emitted.</summary>
    internal MetadataReference? Reference { get; }

    /// <summary>
    /// Whether a script's declarations are in the shared assembly rather than its own.
    /// </summary>
    /// <param name="script">The block's source, as the user holds it.</param>
    /// <returns>True when the script contributed to a shared assembly that built.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is null.</exception>
    public bool Holds(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        return Reference is not null && _shared.Contains(Normalise(script));
    }

    /// <summary>
    /// What the shared compilation said about one script's declarations, on that script's lines.
    /// </summary>
    /// <param name="script">The block's source.</param>
    /// <returns>Its diagnostics, or empty when it has none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is null.</exception>
    public IReadOnlyList<ScriptDiagnostic> Diagnostics(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        return _diagnostics.TryGetValue(Normalise(script), out ImmutableArray<ScriptDiagnostic> found)
            ? found
            : [];
    }

    /// <summary>
    /// Gathers, compiles and loads the declarations of every script in a graph.
    /// </summary>
    /// <param name="scripts">Every code block's source. Order does not matter.</param>
    /// <param name="references">The assemblies to compile against.</param>
    /// <param name="guards">The weaver, because a declared type's loops need bounding too.</param>
    /// <param name="context">The collectible context the assembly is loaded into.</param>
    /// <param name="current">
    /// What the factory already holds. Returned unchanged when the declarations hash the same,
    /// which is what stops a second telling of the same set from trying to load a second assembly
    /// under the same name.
    /// </param>
    /// <returns>The set, which is <see cref="None"/> when no script declares anything.</returns>
    internal static ScriptDeclarations Build(
        IReadOnlyList<string> scripts,
        ReferenceCatalog references,
        GuardWeaver guards,
        ScriptLoadContext context,
        ScriptDeclarations current)
    {
        List<Declaration> declarations = [];

        // `E6-T39`: THE GRAPH'S OWN `using` LINES COME IN HERE AS WELL, MERGED AND DEDUPLICATED.
        //
        // A block that writes `using Autodesk.Revit.DB;` and then declares a type in terms of it
        // has moved half of what that type needs into this assembly and left the other half behind.
        // Without this the shared compile fails, sharing switches itself off, and the graph
        // silently loses cross-block types for a reason nothing states. Merging is consistent with
        // what this type already does to the declarations themselves - one namespace, one
        // compilation - and a merge that introduces an ambiguity reports it as `CS0104` on the
        // declaration that used the name.
        SortedSet<string> usings = new(StringComparer.Ordinal);

        foreach (string script in scripts)
        {
            string blanked = ScriptRanges.Blank(script).Text;

            foreach (TextSpan span in ScriptDeclarationSpans.Usings(blanked))
            {
                usings.Add(blanked[span.Start..span.End].Trim());
            }

            foreach (TextSpan span in ScriptDeclarationSpans.Of(blanked))
            {
                declarations.Add(new Declaration(
                    Normalise(script),
                    ScriptDeclarationSpans.LineAt(blanked, span.Start),
                    span.Start - ScriptDeclarationSpans.StartOfLine(blanked, span.Start),
                    ScriptDeclarationSpans.Published(blanked[span.Start..span.End])));
            }
        }

        if (declarations.Count == 0)
        {
            return None;
        }

        // Sorted so that the fingerprint - and therefore every block's cache key - does not move
        // when a node is dragged into a different position in the graph. The script is part of the
        // ordering only to break a tie between two identical declarations.
        declarations.Sort((left, right) =>
            string.CompareOrdinal(left.Text, right.Text) is var byText && byText != 0
                ? byText
                : string.CompareOrdinal(left.Script, right.Script));

        string fingerprint = FingerprintOf(declarations, usings);

        // BEFORE ANYTHING IS COMPILED, AND THAT ORDERING IS NOT AN OPTIMISATION. Emitting an
        // assembly whose name already exists in the load context throws - *assembly with the same
        // name is already loaded* - so a graph that is told twice about the same set of
        // declarations would fail on the second telling. The name carries the fingerprint on
        // purpose (see the remarks on this type), which is exactly what makes this check the same
        // question as "have we built this already".
        if (fingerprint == current.Fingerprint)
        {
            return current;
        }

        StringBuilder source = new();
        source.AppendLine(references.Prelude());
        source.Append("namespace ").Append(Namespace).AppendLine(";");

        // Inside the namespace rather than above it, for the reason `ScriptNodeFactory.Imported`
        // records: an alias here shadows one of the same name in the prelude instead of colliding
        // with it, which is what lets a user overrule `E6-T30`'s pinning.
        foreach (string import in usings)
        {
            source.AppendLine(import);
        }

        List<Placed> placed = [];

        foreach (Declaration declaration in declarations)
        {
            int start = LinesIn(source) + 1;

            source.Append(' ', declaration.Column).AppendLine(declaration.Text);

            placed.Add(new Placed(start, LinesIn(source) - start + 1, declaration));
        }

        return Compile(source.ToString(), fingerprint, placed, references, guards, context);
    }

    private static ScriptDeclarations Compile(
        string source,
        string fingerprint,
        List<Placed> placed,
        ReferenceCatalog references,
        GuardWeaver guards,
        ScriptLoadContext context)
    {
        // Woven like any other generated source: a loop inside a declared type is still a way to
        // hang the application, and a method that calls itself is still `R11`. Nothing here can
        // name `__token`, so every guard takes `E6-T34`'s thread-reading form.
        SyntaxTree tree = CSharpSyntaxTree.Create(
            (CSharpSyntaxNode)guards.Weave(CSharpSyntaxTree.ParseText(source).GetRoot()));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "SparkShared_" + fingerprint,
            [tree],
            references.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

        using MemoryStream assembly = new();
        EmitResult emitted = compilation.Emit(assembly);

        Dictionary<string, ImmutableArray<ScriptDiagnostic>> diagnostics = Place(emitted.Diagnostics, placed);

        HashSet<string> shared = new(placed.Select(entry => entry.Declaration.Script), StringComparer.Ordinal);

        if (!emitted.Success)
        {
            // Sharing switches itself off rather than taking every block down with it. The reason
            // is on the type's remarks: this is exactly where the product was before this row, and
            // a graph in which one unclosed brace stops every other block is worse than one in
            // which two blocks cannot see each other's types.
            return new ScriptDeclarations(fingerprint, shared, diagnostics, null);
        }

        byte[] bytes = assembly.ToArray();

        // Loaded before it is handed out: a block compiled against this reference has to be able
        // to bind to it at run time, and an assembly nothing loaded is a `FileNotFoundException`
        // the first time a script touches one of its types.
        context.Load(bytes);

        return new ScriptDeclarations(
            fingerprint, shared, diagnostics, MetadataReference.CreateFromImage(bytes));
    }

    /// <summary>Puts each diagnostic back on the line of the block that wrote the declaration.</summary>
    private static Dictionary<string, ImmutableArray<ScriptDiagnostic>> Place(
        IEnumerable<Diagnostic> diagnostics, List<Placed> placed)
    {
        Dictionary<string, ImmutableArray<ScriptDiagnostic>.Builder> found = new(StringComparer.Ordinal);

        foreach (Diagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                || !diagnostic.Location.IsInSource)
            {
                continue;
            }

            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            int line = span.StartLinePosition.Line + 1;

            if (Owner(placed, line) is not { } owner)
            {
                // On the prelude rather than on anything a user wrote. Dropped for the reason
                // `ScriptSourceMap.UserLine` returns zero: a message with no line the user can see
                // must not be shown against a line that is correct.
                continue;
            }

            if (!found.TryGetValue(owner.Declaration.Script, out ImmutableArray<ScriptDiagnostic>.Builder? builder))
            {
                builder = ImmutableArray.CreateBuilder<ScriptDiagnostic>();
                found[owner.Declaration.Script] = builder;
            }

            builder.Add(new ScriptDiagnostic(
                diagnostic.Id,
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                diagnostic.Severity == DiagnosticSeverity.Error,
                owner.Declaration.Line + (line - owner.GeneratedStart),
                span.StartLinePosition.Character + 1,
                System.Math.Max(
                    1,
                    span.EndLinePosition.Line == span.StartLinePosition.Line
                        ? span.EndLinePosition.Character - span.StartLinePosition.Character
                        : 1)));
        }

        return found.ToDictionary(pair => pair.Key, pair => pair.Value.ToImmutable(), StringComparer.Ordinal);
    }

    private static Placed? Owner(List<Placed> placed, int generatedLine)
    {
        foreach (Placed entry in placed)
        {
            if (generatedLine >= entry.GeneratedStart && generatedLine < entry.GeneratedStart + entry.Lines)
            {
                return entry;
            }
        }

        return null;
    }

    private static string FingerprintOf(List<Declaration> declarations, SortedSet<string> usings)
    {
        StringBuilder all = new();

        foreach (Declaration declaration in declarations)
        {
            all.Append(declaration.Text.ReplaceLineEndings("\n")).Append(' ');
        }

        // The imports are part of what was compiled, so they are part of what identifies it. A set
        // of declarations that hashed the same under two different preludes would be handed back
        // unbuilt by the check below, and the second graph would bind against the first one's
        // meaning of a name.
        foreach (string import in usings)
        {
            all.Append(import.ReplaceLineEndings("\n")).Append(' ');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(all.ToString())))[..16];
    }

    /// <summary>The key a script is filed under, which is its text with line endings settled.</summary>
    private static string Normalise(string script) => script.ReplaceLineEndings("\n");

    private static int LinesIn(StringBuilder source)
    {
        int lines = 0;

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    /// <summary>One declaration, and which script it came from.</summary>
    private readonly record struct Declaration(string Script, int Line, int Column, string Text);

    /// <summary>Where a declaration ended up in the shared source.</summary>
    private readonly record struct Placed(int GeneratedStart, int Lines, Declaration Declaration);
}

/// <summary>
/// Where a script's top-level type declarations are, and the script with them blanked out
/// (`E6-T34`, `E6-T35`).
/// </summary>
/// <remarks>
/// <para>
/// <b>C# has no local class, and a code block's text is a method body</b> — so
/// <c>public class TestClass { … }</c>, which is an ordinary thing to write, was answered with
/// <c>CS1022</c> <i>type or namespace definition, or end-of-file expected</i> and <c>CS0161</c>
/// <i>not all code paths return a value</i>, neither of which names what is wrong. The declaration
/// has to move, and this is the half that finds it.
/// </para>
/// <para>
/// <b>Blanked to spaces rather than cut, and that is the whole trick.</b> Removing the text would
/// shorten every offset after it and join the line before it to the line after it, which would move
/// `E10-T15`'s range markers and turn `E6-T1`'s source map into a table for the statements as well
/// as for the declarations. Overwriting each character with a space and leaving the newlines alone
/// changes neither the length nor the line count, so nothing above, below or beside a declaration
/// notices that it left ([N125](../../docs/NOTES.md)).
/// </para>
/// <para>
/// <b>Types and delegates only.</b> A <c>public static</c> method at the top level is not hoisted:
/// a method has nowhere legal to go at namespace scope, and the thing a block wants there — a
/// helper — is a local function, which has always worked and is guarded (`E6-T4`).
/// </para>
/// </remarks>
internal static class ScriptDeclarationSpans
{
    /// <summary>The spans of every top-level type declaration in an already-blanked script.</summary>
    internal static ImmutableArray<TextSpan> Of(string blanked)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(blanked).GetCompilationUnitRoot();

        return
        [
            .. root.Members
                .Where(member => member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
                .Select(member => member.Span),
        ];
    }

    /// <summary>
    /// The spans of every <c>using</c> directive written at the top of an already-blanked script
    /// (`E6-T39`).
    /// </summary>
    /// <param name="blanked">The script, with `E10-T15`'s range markers already blanked.</param>
    /// <returns>The directives' spans, in the order they were written.</returns>
    /// <remarks>
    /// <para>
    /// <b>Roslyn's own answer, not a text scan, and that is what keeps <c>using</c> the
    /// <i>statement</i> out of it.</b> <c>using var file = File.OpenRead(path);</c> and
    /// <c>using (var scope = …) { }</c> both begin with the same keyword and both belong exactly
    /// where the user put them; the parser has already decided which is which, and
    /// <see cref="CompilationUnitSyntax.Usings"/> holds only the directives. Hoisting a
    /// <c>using</c> statement out of the method body would move a disposal to namespace scope,
    /// which does not compile — and it is the kind of thing a regular expression would do.
    /// </para>
    /// </remarks>
    internal static ImmutableArray<TextSpan> Usings(string blanked)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(blanked).GetCompilationUnitRoot();

        return [.. root.Usings.Select(directive => directive.Span)];
    }

    /// <summary>
    /// The script with every top-level type declaration and <c>using</c> directive overwritten with
    /// spaces.
    /// </summary>
    internal static (string Statements, ImmutableArray<TextSpan> Declarations, ImmutableArray<TextSpan> Usings)
        Without(string blanked)
    {
        ImmutableArray<TextSpan> declarations = Of(blanked);
        ImmutableArray<TextSpan> usings = Usings(blanked);

        if (declarations.IsEmpty && usings.IsEmpty)
        {
            return (blanked, declarations, usings);
        }

        char[] text = blanked.ToCharArray();

        Blank(text, declarations);
        Blank(text, usings);

        return (new string(text), declarations, usings);
    }

    /// <summary>Overwrites spans with spaces, leaving the line endings where they were.</summary>
    private static void Blank(char[] text, ImmutableArray<TextSpan> spans)
    {
        foreach (TextSpan span in spans)
        {
            for (int i = span.Start; i < span.End; i++)
            {
                if (text[i] is not ('\n' or '\r'))
                {
                    text[i] = ' ';
                }
            }
        }
    }

    /// <summary>
    /// A declaration's text with its accessibility promoted to <c>public</c> (`E6-T38`).
    /// </summary>
    /// <param name="declaration">The declaration, exactly as the user wrote it.</param>
    /// <returns>The same text, reachable from another block.</returns>
    /// <remarks>
    /// <para>
    /// <b>Reported by the client</b>: <c>class Test{ … }</c> in one block and
    /// <c>new Test()</c> in the next gave <c>CS0122: 'Test' is inaccessible due to its protection
    /// level</c>. A type with no access modifier at namespace scope is <b>internal</b>, and
    /// `E6-T35` compiles declarations into a different assembly from the block that uses them — so
    /// it worked inside one block and not across two, which is the worst shape a rule can have.
    /// And <c>class Foo</c> is not an edge case; it is how most people spell it.
    /// </para>
    /// <para>
    /// <b><c>internal</c> has no meaning a code block can express.</b> There is no assembly here
    /// that a user chose, or could see, or would want to draw a line around — the assemblies are an
    /// implementation detail of how a graph is compiled. So a type a block declares is public,
    /// whether or not it says so, and a block that writes <c>internal</c> gets what it plainly
    /// meant rather than a lecture about a boundary it did not know existed.
    /// </para>
    /// <para>
    /// <b>A text edit rather than a syntax rewrite</b>, so that the line count cannot move: the
    /// declaration is re-emitted verbatim and `E6-T34`'s map is a line map. Only the columns on the
    /// one line the word sits on shift, which is the trade
    /// [N122](../../docs/NOTES.md) already records.
    /// </para>
    /// <para>
    /// <b>The insertion point is not offset zero</b>, and that is the part that would fail quietly:
    /// <c>[Obsolete] class C</c> would become <c>public [Obsolete] class C</c>, which does not
    /// compile. It goes before the first modifier when there is one, and before the type keyword
    /// when there is not.
    /// </para>
    /// </remarks>
    internal static string Published(string declaration)
    {
        MemberDeclarationSyntax? member = CSharpSyntaxTree
            .ParseText(declaration)
            .GetCompilationUnitRoot()
            .Members
            .FirstOrDefault();

        if (member is null)
        {
            return declaration;
        }

        SyntaxTokenList modifiers = member.Modifiers;

        if (modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return declaration;
        }

        // `internal` and `file` are replaced where they stand; anything else - `static`, `sealed`,
        // `abstract`, `partial`, or nothing at all - gains a `public` in front of it.
        foreach (SyntaxToken modifier in modifiers)
        {
            if (modifier.IsKind(SyntaxKind.InternalKeyword) || modifier.IsKind(SyntaxKind.FileKeyword))
            {
                return declaration[..modifier.SpanStart] + "public" + declaration[modifier.Span.End..];
            }
        }

        int at = modifiers.Count > 0 ? modifiers[0].SpanStart : Keyword(member);

        return declaration[..at] + "public " + declaration[at..];
    }

    /// <summary>Where a declaration's own keyword starts, past any attribute lists.</summary>
    private static int Keyword(MemberDeclarationSyntax member) => member switch
    {
        TypeDeclarationSyntax type => type.Keyword.SpanStart,
        EnumDeclarationSyntax @enum => @enum.EnumKeyword.SpanStart,
        DelegateDeclarationSyntax @delegate => @delegate.DelegateKeyword.SpanStart,
        _ => member.SpanStart,
    };

    /// <summary>The offset the line containing a position begins at.</summary>
    internal static int StartOfLine(string text, int position)
    {
        if (position <= 0)
        {
            return 0;
        }

        int start = text.LastIndexOf('\n', position - 1);

        return start < 0 ? 0 : start + 1;
    }

    /// <summary>The one-based line a position is on.</summary>
    internal static int LineAt(string text, int position)
    {
        int line = 1;

        for (int i = 0; i < position; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
