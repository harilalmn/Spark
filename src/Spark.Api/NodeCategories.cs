namespace Spark.Api;

/// <summary>
/// The library category names the shell knows how to colour, from
/// <c>docs/help/concepts/design-language.md</c> §7.2.
/// </summary>
/// <remarks>
/// <para>
/// A category is a plain string on <see cref="SparkNodeAttribute.Category"/> rather than an enum,
/// because a third-party package must be able to file its nodes under a name Spark has never heard
/// of. These ten are the names that carry a colour; anything else falls back to
/// <see cref="Custom"/>, which is a legible outcome rather than a failure.
/// </para>
/// <para>
/// There are ten and not fifteen because ten mutually distinguishable hues inside a 60–81 L* band
/// is close to the limit of what is possible while keeping every one of them above 3:1 against the
/// canvas.
/// </para>
/// </remarks>
public static class NodeCategories
{
    /// <summary>Input and constants. <c>cat.input</c>.</summary>
    public const string Input = "Input";

    /// <summary>Logic. <c>cat.logic</c>.</summary>
    public const string Logic = "Logic";

    /// <summary>Display and preview. <c>cat.display</c>.</summary>
    public const string Display = "Display";

    /// <summary>Geometry — surface and solid. <c>cat.solid</c>.</summary>
    public const string Solid = "Solid";

    /// <summary>Geometry — curve. <c>cat.curve</c>.</summary>
    public const string Curve = "Curve";

    /// <summary>Geometry — point and vector. <c>cat.point</c>.</summary>
    public const string Point = "Point";

    /// <summary>Script and code. <c>cat.script</c>.</summary>
    public const string Script = "Script";

    /// <summary>Lists. <c>cat.list</c>.</summary>
    public const string List = "List";

    /// <summary>Math. <c>cat.math</c>.</summary>
    public const string Math = "Math";

    /// <summary>Custom and uncategorised. <c>cat.custom</c>. What an unrecognised name resolves to.</summary>
    public const string Custom = "Custom";
}
