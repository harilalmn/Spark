using System;
using System.Collections.Generic;

namespace Spark.UI.ViewModels;

/// <summary>
/// When the graph runs: on every edit, on a timer, or only when asked (<c>E3-T13</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Dynamo's three, and the client asked for them by name.</b> Automatic is what a graph author
/// wants for the first hour and what they stop wanting the moment one node takes four seconds:
/// every literal typed, every wire drawn and every slider dragged re-runs the graph, and past a
/// certain size the editor stops being usable while the work is still correct.
/// </para>
/// <para>
/// <b>The mode is a session preference, not part of the document.</b> A graph that arrived set to
/// Manual would silently do nothing when opened, and the person opening it would have no reason to
/// suspect a mode they never chose. It is deliberately not written to the <c>.spark</c> file.
/// </para>
/// </remarks>
public enum RunMode
{
    /// <summary>
    /// Runs after every edit that could change an answer. The default, and what the shell did
    /// unconditionally before this existed.
    /// </summary>
    Automatic = 0,

    /// <summary>
    /// Runs only when asked — the Run button, or F5. Edits are still recorded and still mark the
    /// graph dirty; the status bar says so, because a graph that quietly stops updating is the
    /// most confusing thing an editor can do.
    /// </summary>
    Manual = 1,

    /// <summary>
    /// Runs on a timer while there is something to run, at
    /// <see cref="MainWindowViewModel.PeriodicRunInterval"/>.
    /// </summary>
    /// <remarks>
    /// The mode for a graph that reads something outside itself — a clock, a file, a service. Its
    /// point is not the edits, which Automatic already covers, but the runs that happen when
    /// nobody has touched anything.
    /// </remarks>
    Periodic = 2,
}

/// <summary>
/// The three run modes in the words the ribbon writes them, and back again.
/// </summary>
/// <remarks>
/// The same shape as <see cref="LacingNames"/>, and for the same reason: the control is a list of
/// strings, and round-tripping through one place is what stops the ribbon, the menu and the status
/// bar from disagreeing about what a mode is called.
/// </remarks>
public static class RunModeNames
{
    private static readonly (RunMode Mode, string Name)[] Pairs =
    [
        (RunMode.Automatic, "Automatic"),
        (RunMode.Manual, "Manual"),
        (RunMode.Periodic, "Periodic"),
    ];

    /// <summary>Every mode's word, in the order the dropdown offers them.</summary>
    public static IReadOnlyList<string> All { get; } = BuildNames();

    /// <summary>The word for a mode.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The word.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not one of the three.</exception>
    public static string Of(RunMode mode)
    {
        foreach ((RunMode candidate, string name) in Pairs)
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
    /// <param name="mode">The mode, when the word is one of the three.</param>
    /// <returns>True when the word was recognised.</returns>
    public static bool TryParse(string? name, out RunMode mode)
    {
        foreach ((RunMode candidate, string candidateName) in Pairs)
        {
            if (string.Equals(candidateName, name, StringComparison.Ordinal))
            {
                mode = candidate;
                return true;
            }
        }

        mode = RunMode.Automatic;
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
