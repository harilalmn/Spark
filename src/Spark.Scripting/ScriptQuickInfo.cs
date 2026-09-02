namespace Spark.Scripting;

/// <summary>
/// What sits under the pointer in a code block: a symbol, and what its documentation says.
/// </summary>
/// <param name="Signature">
/// The symbol as it would be written — <c>Point3d.DistanceTo(Point3d other)</c>. Minimally
/// qualified, so it reads the way the user would type it rather than the way a compiler spells it.
/// </param>
/// <param name="Summary">The <c>&lt;summary&gt;</c> from its XML documentation, or null.</param>
/// <remarks>
/// <b>Two strings, for the reason <see cref="ScriptCompletionItem"/> is three.</b> Drawing a
/// tooltip is <c>Spark.UI</c>'s business and knowing what a symbol is is this assembly's, and
/// <c>C5</c> asserts structurally that no Roslyn type crosses between them.
/// </remarks>
public readonly record struct ScriptQuickInfo(string Signature, string? Summary);
