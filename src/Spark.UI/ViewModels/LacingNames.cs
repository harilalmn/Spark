using System;
using System.Collections.Generic;
using Spark.Api;

namespace Spark.UI.ViewModels;

/// <summary>
/// The five lacing modes in the words the properties pane writes them, and back again
/// (<c>E8-T31</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A list of strings rather than the enum bound directly</b>, because the enum's names are not
/// the words a user reads: <c>CrossProduct</c> is one word with a capital in the middle, and the
/// help topic, the diagnostics and the panel should all say <i>Cross product</i>. Round-tripping
/// through this class is what keeps the three of them the same.
/// </para>
/// <para>
/// The order is the enum's, which is <c>Auto</c> first and then the four algorithms in the order
/// <c>docs/help/concepts/lacing.md</c> introduces them. It is not alphabetical, and it should not
/// be: <c>Auto</c> is what a freshly placed node carries and belongs at the top of a list a user
/// opens to get back to it.
/// </para>
/// </remarks>
public static class LacingNames
{
    /// <summary>The word for <see cref="LacingMode.Auto"/>.</summary>
    public const string Auto = "Auto";

    private static readonly (LacingMode Mode, string Name)[] Pairs =
    [
        (LacingMode.Auto, Auto),
        (LacingMode.Shortest, "Shortest"),
        (LacingMode.Longest, "Longest"),
        (LacingMode.CrossProduct, "Cross product"),
        (LacingMode.Disabled, "Disabled"),
    ];

    /// <summary>Every mode's word, in the order the panel offers them.</summary>
    public static IReadOnlyList<string> All { get; } = BuildNames();

    /// <summary>The word for a mode.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The word.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not one of the five.</exception>
    public static string Of(LacingMode mode)
    {
        foreach ((LacingMode candidate, string name) in Pairs)
        {
            if (candidate == mode)
            {
                return name;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(mode));
    }

    /// <summary>The mode a word names.</summary>
    /// <param name="name">The word, as <see cref="Of"/> writes it.</param>
    /// <param name="mode">The mode, when the word is one of the five.</param>
    /// <returns>True when the word was recognised.</returns>
    /// <remarks>
    /// <b>Try-parse rather than parse</b>, because the caller is a property setter fed by a
    /// dropdown: a value that is not one of the five means the binding produced something
    /// unexpected, and doing nothing is a better answer there than throwing on the UI thread.
    /// </remarks>
    public static bool TryParse(string? name, out LacingMode mode)
    {
        foreach ((LacingMode candidate, string candidateName) in Pairs)
        {
            if (string.Equals(candidateName, name, StringComparison.Ordinal))
            {
                mode = candidate;
                return true;
            }
        }

        mode = LacingMode.Auto;
        return false;
    }

    private static string[] BuildNames()
    {
        string[] names = new string[Pairs.Length];
        for (int index = 0; index < Pairs.Length; index++)
        {
            names[index] = Pairs[index].Name;
        }

        return names;
    }
}
