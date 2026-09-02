using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace Spark.Scripting;

/// <summary>
/// One completion candidate, reduced to what an editor needs to draw it.
/// </summary>
/// <param name="DisplayText">The text shown in the list.</param>
/// <param name="Kind">What it is — <c>Method</c>, <c>Property</c>, <c>Class</c> and so on.</param>
/// <param name="SortText">Roslyn's own ordering key, which is not always the display text.</param>
/// <remarks>
/// **This exists so that the editor never sees a Roslyn type.** Completion is a
/// <c>Spark.Scripting</c> concern; drawing a list is a <c>Spark.UI</c> one, and keeping Roslyn
/// out of the UI assembly is what stops the code block's language service leaking into the shell
/// (ADR-0005).
/// </remarks>
public readonly record struct ScriptCompletionItem(string DisplayText, string Kind, string SortText);

/// <summary>
/// Roslyn's C# completion, over a single throwaway document.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is M1.5 spike (c)'s apparatus</b> (`E11-T21`): the question it exists to answer is
/// whether AvaloniaEdit plus a Roslyn completion popup is acceptable to build the M4 code block
/// on. It is deliberately the smallest thing that can answer that — one document, one project, no
/// incremental reuse of a user's edits, no signature help, no diagnostics.
/// </para>
/// <para>
/// <b>The first call is slow and the rest are not.</b> Roslyn composes its host services through
/// MEF on first use, so the first completion pays for the composition and everything after it
/// does not. That is the single most important number this class produces, because it decides
/// whether the code block editor must be warmed up before a user types into it or can be
/// constructed lazily.
/// </para>
/// </remarks>
public sealed class ScriptCompletion : IDisposable
{
    private readonly AdhocWorkspace _workspace;
    private readonly ProjectId _projectId;
    private DocumentId? _documentId;

    /// <summary>
    /// Creates a completion service over a set of referenced assemblies.
    /// </summary>
    /// <param name="references">
    /// The assemblies a snippet may use. The core library is always added; pass
    /// <c>typeof(Point3d).Assembly</c> to complete against the geometry kernel.
    /// </param>
    /// <param name="usings">Namespaces treated as already imported, as a code block's would be.</param>
    public ScriptCompletion(IEnumerable<Assembly>? references = null, IEnumerable<string>? usings = null)
        : this(Metadata(references), usings ?? ["System", "Spark.Geometry"])
    {
    }

    /// <summary>
    /// Creates a completion service over the same catalogue a code block compiles against
    /// (`E6-T13`).
    /// </summary>
    /// <param name="catalogue">The references and imports the compiler is using.</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalogue"/> is null.</exception>
    /// <remarks>
    /// <b>This constructor is the invariant, not a convenience.</b> A completion list built from a
    /// different set of references than the compile is a list that offers members of types the
    /// script cannot use and hides members of types it can — and a list that disagrees with the
    /// compiler is worse than no list, because the user believes it. Taking both from one
    /// <see cref="ReferenceCatalog"/> is the only way to be sure they cannot drift; the assembly
    /// overload above remains for the spike tests, which are deliberately about Roslyn rather than
    /// about Spark.
    /// </remarks>
    public ScriptCompletion(ReferenceCatalog catalogue)
        : this(Referenced(catalogue), Imports(catalogue))
    {
    }

    private ScriptCompletion(ImmutableArray<MetadataReference> metadata, IEnumerable<string> usings)
    {
        // MefHostServices.DefaultAssemblies is the workspace layer only, and completion lives in
        // the *Features* layer. Composing without these, CompletionService.GetService returns
        // null and every request answers with an empty list — a silent no rather than an error,
        // which is the single most expensive thing to discover about this API.
        _workspace = new AdhocWorkspace(MefHostServices.Create(
        [
            .. MefHostServices.DefaultAssemblies,
            typeof(CompletionService).Assembly,
            Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features"),
            Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces"),
        ]));

        // A code block is a *script*, not a compilation unit, and Roslyn has to be told: parsed as
        // SourceCodeKind.Regular, `var p = new Point3d(...);` at the top of a file is a syntax
        // error, the semantic model has nothing to say about it, and completion returns an empty
        // list with no error anywhere.
        ProjectInfo project = ProjectInfo
            .Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Script", "Script", LanguageNames.CSharp)
            .WithMetadataReferences(metadata)
            .WithCompilationOptions(new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                usings: [.. usings]))
            .WithParseOptions(new CSharpParseOptions(kind: SourceCodeKind.Script));

        _projectId = _workspace.AddProject(project).Id;
    }

    /// <summary>
    /// Asks for the completions available at a caret position.
    /// </summary>
    /// <param name="code">The snippet, as the editor currently holds it.</param>
    /// <param name="caret">The caret offset, in characters from the start.</param>
    /// <param name="inputs">
    /// The block's input ports and what the graph knows they carry, by name (`E6-T7`). A port whose
    /// type is unknown — nothing wired into it — maps to <see langword="null"/> and is completed
    /// against <c>dynamic</c>, which is what the compiler will declare it as.
    /// </param>
    /// <param name="cancellationToken">Cancels a slow request — a keystroke supersedes it.</param>
    /// <returns>
    /// The candidates, most likely first: anything Roslyn preselects — the target type of a
    /// <c>new</c>, for instance — ahead of the rest, and Roslyn's own order within each group.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="code"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="caret"/> is outside the snippet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>This is the thing Spark can demonstrate that Dynamo cannot</b> (`E6-T7`). Wire a point
    /// into a port called <c>centre</c>, type <c>centre.</c>, and the list is
    /// <see cref="object"/>'s members no longer — it is whatever the wire carries. The port names
    /// and types come from the graph, so the list follows the wires rather than the text.
    /// </para>
    /// <para>
    /// <b>The declarations are prepended as ordinary statements and the caret is moved with
    /// them</b>, rather than the snippet being wrapped in the generated class and method. That
    /// keeps the document a script, which is what makes a bare <c>var p = new Point3d(…);</c> parse
    /// at all — and it keeps the only difference between what completion sees and what the compiler
    /// sees down to a frame that contributes no names of its own. `E6-T13`'s invariant is that a
    /// completion list which disagrees with the compiler is worse than no list, and this is where
    /// that is either held or lost.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ScriptCompletionItem>> CompleteAsync(
        string code,
        int caret,
        IReadOnlyDictionary<string, Type?>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentOutOfRangeException.ThrowIfNegative(caret);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caret, code.Length);

        string prefix = Declarations(inputs);
        code = prefix + code;
        caret += prefix.Length;

        // **One document, replaced in place, and it has to be one.** The first version added a
        // fresh document per request and never removed any, which is what an editor sending a
        // snapshot per keystroke would do thousands of times. It looked correct because every
        // spike test made its own instance: two script documents in one project are two sets of
        // top-level statements, so the second request onwards the semantic model is looking at
        // duplicate definitions and completion quietly returns nothing (N46).
        //
        // The *document* carries its own SourceCodeKind and it defaults to Regular; the project's
        // parse options do not override it. Miss this and a snippet is parsed as a compilation
        // unit, every statement is a syntax error, and completion returns an empty list without
        // complaining — which is a far more expensive failure than an exception would have been.
        Document document = Replace(code);

        CompletionService? service = CompletionService.GetService(document);

        if (service is null)
        {
            throw new InvalidOperationException(
                "Roslyn has no CompletionService for this document. The host services were "
                + "composed without the Features layer, or the project's language is not C#.");
        }

        CompletionList completions = await service
            .GetCompletionsAsync(document, caret, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // WHAT ROSLYN PRESELECTS COMES FIRST, AND THE LIST USED TO THROW THAT AWAY.
        //
        // `Point2d p2d = new ` had `AccessViolationException` at the top and `Point2d` somewhere
        // down an alphabet the user would have to scroll or retype to reach — which makes the one
        // keystroke the feature exists for, Tab, do the wrong thing. Roslyn already knows the
        // answer: for a target-typed expression it marks the type it expects with
        // `MatchPriority.Preselect`, and `ItemsList` arrives ordered by `SortText` with that
        // priority carried alongside rather than applied.
        //
        // The ordering is done HERE rather than in the editor deliberately. Which candidate is
        // likeliest is a language question, and ADR-0005 keeps language questions behind this
        // assembly — `C5` asserts the UI never sees a Roslyn type, so it cannot be given the
        // priority and asked to sort on it either.
        return
        [
            .. completions.ItemsList
                .OrderByDescending(item => item.Rules.MatchPriority)
                .ThenBy(item => item.SortText, StringComparer.Ordinal)
                .Select(item => new ScriptCompletionItem(
                    item.DisplayText,
                    item.Tags.Length > 0 ? item.Tags[0] : string.Empty,
                    item.SortText)),
        ];
    }

    /// <summary>
    /// Describes the symbol under an offset, for a hover tooltip.
    /// </summary>
    /// <param name="code">The snippet, as the editor currently holds it.</param>
    /// <param name="offset">Where the pointer is, in characters from the start.</param>
    /// <param name="inputs">The block's ports, exactly as for <see cref="CompleteAsync"/>.</param>
    /// <param name="cancellationToken">Cancels a request the pointer has already moved off.</param>
    /// <returns>What is under the pointer, or null when it is not over a symbol.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is outside the snippet.</exception>
    /// <remarks>
    /// <b>Three questions in order, because a token can be any of them.</b> An expression has a
    /// symbol it refers to; a declaration has a symbol it declares; and a literal or an implicitly
    /// typed local has neither but does have a type. Asking only the first — which is the obvious
    /// version — makes hovering a <c>var</c> or a number do nothing, which reads as the feature
    /// being broken rather than as the token being uninteresting.
    /// </remarks>
    public async Task<ScriptQuickInfo?> DescribeAsync(
        string code,
        int offset,
        IReadOnlyDictionary<string, Type?>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, code.Length);

        string prefix = Declarations(inputs);
        Document document = Replace(prefix + code);

        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        if (root is null || model is null)
        {
            return null;
        }

        SyntaxToken token = root.FindToken(offset + prefix.Length);

        if (token.Parent is not { } node)
        {
            return null;
        }

        ISymbol? symbol = model.GetSymbolInfo(node, cancellationToken).Symbol
            ?? model.GetDeclaredSymbol(node, cancellationToken)
            ?? model.GetTypeInfo(node, cancellationToken).Type;

        return symbol is null
            ? null
            : new ScriptQuickInfo(
                symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Summary(symbol));
    }

    /// <summary>The <c>&lt;summary&gt;</c> of a symbol's documentation, as one line.</summary>
    private static string? Summary(ISymbol symbol)
    {
        try
        {
            string? xml = symbol.GetDocumentationCommentXml();

            if (string.IsNullOrWhiteSpace(xml))
            {
                return null;
            }

            if (System.Xml.Linq.XElement.Parse(xml).Element("summary") is not { } summary)
            {
                return null;
            }

            return string.Join(
                ' ',
                summary.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        catch (System.Xml.XmlException)
        {
            // Documentation that will not parse is documentation nobody gets to read. It is not a
            // reason to fail a tooltip.
            return null;
        }
    }

    /// <summary>
    /// Asks which overloads the call around the caret has, and which parameter is being typed.
    /// </summary>
    /// <param name="code">The snippet, as the editor currently holds it.</param>
    /// <param name="caret">The caret offset, in characters from the start.</param>
    /// <param name="inputs">The block's input ports and their wire types, exactly as for
    /// <see cref="CompleteAsync"/>.</param>
    /// <param name="cancellationToken">Cancels a slow request — a keystroke supersedes it.</param>
    /// <returns>The overloads, or null when the caret is not inside a call's arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="code"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="caret"/> is outside the snippet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>The same document, the same references and the same port declarations as the completion
    /// list</b>, for `E6-T13`'s reason: a signature that disagrees with the compiler is worse than
    /// no signature, because the user believes it. Sharing one <see cref="ScriptCompletion"/> is
    /// also what keeps Roslyn's MEF composition to one per session rather than two.
    /// </para>
    /// <para>
    /// <b>Everything below this is syntax and symbols rather than a service</b>, because Roslyn
    /// does not publish one for signature help — see <see cref="ScriptSignature"/>.
    /// </para>
    /// </remarks>
    public async Task<ScriptSignatureHelp?> SignatureAsync(
        string code,
        int caret,
        IReadOnlyDictionary<string, Type?>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentOutOfRangeException.ThrowIfNegative(caret);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caret, code.Length);

        string prefix = Declarations(inputs);

        Document document = Replace(prefix + code);

        return await ScriptSignature
            .FindAsync(document, caret + prefix.Length, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Puts the current text into the one script document, creating it once.</summary>
    /// <remarks>
    /// <c>TryApplyChanges</c> is what keeps the document's identity across
    /// edits, which is also what lets Roslyn reuse everything it has already parsed and bound. The
    /// alternative — a new document per keystroke — is not merely slower; it is wrong, for the
    /// reason the caller records.
    /// </remarks>
    private Document Replace(string code)
    {
        SourceText text = SourceText.From(code);

        if (_documentId is null)
        {
            Document created = _workspace.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(_projectId),
                "Script.csx",
                loader: TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create())),
                sourceCodeKind: SourceCodeKind.Script));

            _documentId = created.Id;

            return created;
        }

        Solution updated = _workspace.CurrentSolution.WithDocumentText(_documentId, text);

        // A refused apply would leave the workspace holding the previous snapshot and answer the
        // completion against text the user has moved on from - a stale list rather than no list,
        // which is the worse of the two.
        if (!_workspace.TryApplyChanges(updated))
        {
            throw new InvalidOperationException(
                "Roslyn refused to update the script document, so the completion would have been "
                + "taken against text the editor no longer holds.");
        }

        return _workspace.CurrentSolution.GetDocument(_documentId)!;
    }

    /// <summary>The declarations a block's ports contribute, as one line of script.</summary>
    /// <remarks>
    /// <b>One line, deliberately.</b> A caret offset is what the editor sends and what it gets back
    /// in an error message, and every newline here would move every line of the user's snippet
    /// relative to what Roslyn is looking at — the same reasoning that keeps
    /// <see cref="GuardWeaver"/>'s woven statements trivia-free.
    /// </remarks>
    private static string Declarations(IReadOnlyDictionary<string, Type?>? inputs)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return string.Empty;
        }

        System.Text.StringBuilder line = new();

        foreach ((string name, Type? type) in inputs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!IsIdentifier(name))
            {
                continue;
            }

            string? spelt = type is null || type == typeof(object) ? null : ScriptTypeName.Of(type);

            // `default!` rather than `null`, because a port may carry a struct and `Point3d p =
            // null;` does not compile - and a declaration that does not compile takes the whole
            // completion list down with it, silently.
            line.Append(spelt is null
                ? "dynamic " + name + " = null; "
                : spelt + " " + name + " = default!; ");
        }

        return line.ToString();
    }

    /// <summary>Whether a port name can be a C# identifier, so a declaration of it will compile.</summary>
    private static bool IsIdentifier(string name) =>
        name.Length > 0
        && (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <inheritdoc/>
    public void Dispose() => _workspace.Dispose();

    private static ImmutableArray<MetadataReference> Metadata(IEnumerable<Assembly>? references) =>
    [
        .. Assemblies(references)
            .Select(assembly => assembly.Location)
            .Where(location => !string.IsNullOrEmpty(location) && File.Exists(location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location)),
    ];

    private static ImmutableArray<MetadataReference> Referenced(ReferenceCatalog catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        return catalogue.References;
    }

    private static IEnumerable<string> Imports(ReferenceCatalog catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        return catalogue.Imports;
    }

    private static IEnumerable<Assembly> Assemblies(IEnumerable<Assembly>? references)
    {
        yield return typeof(object).Assembly;
        yield return Assembly.Load("System.Runtime");
        yield return Assembly.Load("System.Collections");

        foreach (Assembly assembly in references ?? [])
        {
            yield return assembly;
        }
    }
}
