namespace Spark.Scripting;

/// <summary>
/// A fragment of text to be inserted at one offset in the user's own text while it is being copied
/// into the generated compilation unit.
/// </summary>
/// <remarks>
/// <para>
/// Every injection is deliberately free of line breaks. The generated text carries <c>#line</c>
/// directives so a compiler message and a stack frame both land on the line the user wrote, and the
/// user's text is tracked character by character by a <see cref="SourceMap"/> so the caret and the
/// squiggles line up. Adding a line would quietly move everything after it; adding a few columns to
/// the line a brace already sits on moves nothing anyone can see.
/// </para>
/// <para>
/// Offsets are in the user's own text, which is what lets the rewriter apply them <i>while</i> it
/// copies — splitting a verbatim chunk in two around the insertion, so the map stays exact rather
/// than being patched up afterwards.
/// </para>
/// </remarks>
/// <param name="Offset">Where in the user's text the fragment goes.</param>
/// <param name="Order">
/// The start of the construct the injection came from, used to order two insertions landing on the
/// same offset. <c>while (a) while (b) x++;</c> closes both loops after the same semicolon, and the
/// inner one — the one that starts later — has to close first, or the braces cross.
/// </param>
/// <param name="Text">The fragment.</param>
internal readonly record struct SourceInjection(int Offset, int Order, string Text);
