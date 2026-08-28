using System.Collections.Generic;

namespace Spark.Scripting;

/// <summary>
/// Two-way offset translation between the text the user typed into a code block and the C#
/// compilation unit the rewriter generated from it.
/// </summary>
/// <remarks>
/// <para>
/// The rewriter copies the user's text <b>verbatim</b>, in chunks, never reformatting it. Every
/// chunk is therefore a straight linear shift, and the whole map is just the list of those chunks.
/// That is the entire trick, and it is what keeps a caret position, a completion offset and a
/// squiggle aligned with what the user can actually see: nothing has to be re-derived, because
/// nothing was rewritten.
/// </para>
/// <para>
/// A generated offset that falls in scaffolding — the header, the injected input declarations, a
/// woven guard call — maps to <c>-1</c> rather than to some nearby user offset. Guessing there
/// would put a compiler message on a line the user did not write, which is worse than declining to
/// place it at all.
/// </para>
/// </remarks>
public sealed class SourceMap
{
    private readonly List<Segment> _byGenerated = [];
    private List<Segment>? _byUser;

    /// <summary>
    /// The generated offset to use when a user offset maps nowhere — typically the start of the
    /// copied user text, so that completion in an empty code block still has a place to run.
    /// </summary>
    public int FallbackGeneratedOffset { get; internal set; }

    /// <summary>Maps an offset in the user's text onto the generated compilation unit.</summary>
    /// <param name="userOffset">The offset in the text the user typed.</param>
    /// <returns>The corresponding generated offset, or <see cref="FallbackGeneratedOffset"/>.</returns>
    public int ToGenerated(int userOffset)
    {
        if (_byUser is null)
        {
            List<Segment> sorted = [.. _byGenerated];
            sorted.Sort(static (left, right) => left.UserStart.CompareTo(right.UserStart));
            _byUser = sorted;
        }

        if (_byUser.Count == 0)
        {
            return FallbackGeneratedOffset;
        }

        int index = FindFloor(_byUser, userOffset, static segment => segment.UserStart);
        if (index < 0)
        {
            return FallbackGeneratedOffset;
        }

        Segment segment = _byUser[index];
        int delta = userOffset - segment.UserStart;

        // A caret sitting exactly at the end of a chunk still belongs to that chunk.
        return delta > segment.Length ? FallbackGeneratedOffset : segment.GeneratedStart + delta;
    }

    /// <summary>Maps a generated offset back to the user's text.</summary>
    /// <param name="generatedOffset">The offset in the generated compilation unit.</param>
    /// <returns>The user offset, or <c>-1</c> when the offset lands in scaffolding.</returns>
    public int ToUser(int generatedOffset)
    {
        if (_byGenerated.Count == 0)
        {
            return -1;
        }

        int index = FindFloor(_byGenerated, generatedOffset, static segment => segment.GeneratedStart);
        if (index < 0)
        {
            return -1;
        }

        Segment segment = _byGenerated[index];
        int delta = generatedOffset - segment.GeneratedStart;

        return delta > segment.Length ? -1 : segment.UserStart + delta;
    }

    internal void Add(int userStart, int generatedStart, int length)
    {
        if (length <= 0)
        {
            return;
        }

        _byGenerated.Add(new Segment(userStart, generatedStart, length));
        _byUser = null;
    }

    private static int FindFloor(List<Segment> segments, int offset, System.Func<Segment, int> key)
    {
        int low = 0;
        int high = segments.Count - 1;
        int found = -1;

        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (key(segments[middle]) <= offset)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    private readonly record struct Segment(int UserStart, int GeneratedStart, int Length);
}
