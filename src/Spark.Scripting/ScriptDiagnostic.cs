namespace Spark.Scripting;

/// <summary>
/// One compiler message, placed on the user's own line.
/// </summary>
/// <param name="Id">The compiler's code — <c>CS0103</c>, <c>CS1002</c>.</param>
/// <param name="Message">What it says, in invariant culture.</param>
/// <param name="IsError">True for an error, false for a warning.</param>
/// <param name="Line">The user's line, one-based. Never inside the generated frame.</param>
/// <param name="Column">The column on that line, one-based.</param>
/// <param name="Length">How many characters it covers, at least one.</param>
/// <remarks>
/// <para>
/// <b>Lines and columns rather than offsets, deliberately.</b> The compiler reports positions in
/// the generated source — the script plus a frame the user has never seen — and
/// <see cref="ScriptSourceMap"/> maps *lines* back, which is all it needs to for the message in
/// the diagnostics panel. Handing the editor a line and a column lets it resolve an offset against
/// the document it is actually showing, which is the only document whose offsets are meaningful.
/// </para>
/// <para>
/// <b>And it keeps Roslyn out of <c>Spark.UI</c></b>, the same boundary
/// <see cref="ScriptCompletionItem"/> keeps and <c>C5</c> asserts.
/// </para>
/// </remarks>
public readonly record struct ScriptDiagnostic(
    string Id,
    string Message,
    bool IsError,
    int Line,
    int Column,
    int Length);
