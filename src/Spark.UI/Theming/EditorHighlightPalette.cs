using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;

namespace Spark.UI.Theming;

/// <summary>
/// Puts AvaloniaEdit's syntax highlighting on Spark's palette (<c>E6-T21</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The stock C# highlighting is written for a light background.</b> Its keywords are navy, its
/// strings are blue and its interpolation holes are <i>black</i>, all chosen against white — and on
/// <c>surface.sunken</c> at <c>#1A1E24</c> they range from hard to read down to invisible. Measured
/// against that ground: <c>ContextKeywords</c> 1.05:1, <c>MethodCall</c> 1.13:1,
/// <c>StringInterpolation</c> 1.26:1. The floor for body text is 4.5:1.
/// </para>
/// <para>
/// <b>Every colour here is a token the design language already publishes with a contrast
/// figure</b>, rather than something picked to look right: the syntax colours are the node category
/// fills, so a keyword is the same blue as a Script node — a coincidence worth keeping.
/// </para>
/// <para>
/// <b>The sweep at the end is the part that matters, and it is there because the first attempt did
/// not have one.</b> That version named twenty-four colours and remapped exactly those, on the
/// reasoning that an unrecognised name should be left alone because the <c>.xshd</c> belongs to
/// somebody else. The reasoning was backwards. A name this code does not know is precisely the one
/// nobody has checked, and three of them were missed — including <c>StringInterpolation</c>, which
/// is pure black, so the first person to type an interpolated string got invisible text. Leaving a
/// colour alone is not the safe default when the ground has been replaced underneath it.
/// </para>
/// </remarks>
public static class EditorHighlightPalette
{
    /// <summary>
    /// The colours whose meaning earns them a specific category, rather than the body colour.
    /// </summary>
    /// <remarks>
    /// A name not in here is not left as it was — see <see cref="Apply"/>. It is set to
    /// <c>text.primary</c>, which is legible by construction and says "this is code" rather than
    /// claiming a meaning this table has not thought about.
    /// </remarks>
    private static IReadOnlyDictionary<string, Color> Scheme { get; } = BuildScheme();

    /// <summary>
    /// Recolours a highlighting definition in place.
    /// </summary>
    /// <param name="highlighting">The definition to recolour.</param>
    /// <exception cref="ArgumentNullException"><paramref name="highlighting"/> is null.</exception>
    /// <remarks>
    /// <b>In place, on the shared definition.</b> <see cref="HighlightingManager"/> hands out one
    /// instance per language, so this is global to the process — which is what is wanted, since
    /// every editor in Spark should look the same, and it is why calling it twice has to be
    /// harmless. It is: every assignment is absolute rather than relative to what was there.
    /// </remarks>
    public static void Apply(IHighlightingDefinition highlighting)
    {
        ArgumentNullException.ThrowIfNull(highlighting);

        foreach (HighlightingColor colour in highlighting.NamedHighlightingColors)
        {
            if (colour.Name is not { } name)
            {
                continue;
            }

            if (Scheme.TryGetValue(name, out Color mapped))
            {
                colour.Foreground = new SimpleHighlightingBrush(mapped);
                continue;
            }

            // A COLOUR THIS TABLE HAS NOT HEARD OF IS THE DANGEROUS ONE, NOT THE SAFE ONE.
            //
            // It was chosen against a white page by somebody who has never seen Spark's editor, so
            // "leave it alone" means "keep whatever a light theme wanted", which is how a black
            // foreground survived onto a #1A1E24 ground. Only a colour that sets no foreground at
            // all is left alone, and that one is safe by construction: it inherits the editor's,
            // which is text.primary.
            if (colour.Foreground is not null)
            {
                colour.Foreground = new SimpleHighlightingBrush(SparkPalette.TextPrimary);
            }
        }
    }

    private static IReadOnlyDictionary<string, Color> BuildScheme()
    {
        Color script = NodeCategoryColours.ColourOf(NodeCategory.Script);
        Color text = NodeCategoryColours.ColourOf(NodeCategory.Display);
        Color number = NodeCategoryColours.ColourOf(NodeCategory.Math);
        Color call = NodeCategoryColours.ColourOf(NodeCategory.Curve);
        Color alarm = NodeCategoryColours.ColourOf(NodeCategory.List);

        return new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["Comment"] = SparkPalette.TextMuted,
            ["Preprocessor"] = SparkPalette.TextSecondary,
            ["Punctuation"] = SparkPalette.TextSecondary,

            ["String"] = text,
            ["Char"] = text,

            // The code inside a `$"{...}"` hole is code, not string, so it takes the body colour
            // and stands out against the literal around it. Stock, this is #000000.
            ["StringInterpolation"] = SparkPalette.TextPrimary,

            ["NumberLiteral"] = number,
            ["TrueFalse"] = number,

            ["MethodCall"] = call,

            ["ExceptionKeywords"] = alarm,
            ["UnsafeKeywords"] = alarm,

            ["Keywords"] = script,
            ["GotoKeywords"] = script,
            ["ContextKeywords"] = script,
            ["SemanticKeywords"] = script,
            ["NullOrValueKeywords"] = script,
            ["CheckedKeyword"] = script,
            ["OperatorKeywords"] = script,
            ["ParameterModifiers"] = script,
            ["Modifiers"] = script,
            ["Visibility"] = script,
            ["NamespaceKeywords"] = script,
            ["GetSetAddRemove"] = script,
            ["ThisOrBaseReference"] = script,
            ["TypeKeywords"] = script,
            ["ValueTypeKeywords"] = script,
            ["ReferenceTypeKeywords"] = script,
        };
    }
}
