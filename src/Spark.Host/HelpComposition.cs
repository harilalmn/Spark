using System;
using System.Collections.Generic;
using System.IO;
using Spark.Api.Help;
using Spark.Engine;

namespace Spark.Host;

/// <summary>
/// Assembles the help library a host shows: the hand-written concept topics, a generated page for
/// every node currently loaded, and a page for every diagnostic code (<c>E12-T5</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is here, below the shell, because there are two hosts now.</b> The F1 window built this
/// and `spark docs` writes it out, and two assemblies composing the same library from the same
/// three sources is two things that drift — the window gaining a source the command line never
/// hears about, or the pair disagreeing about where the topics live. <c>ValueText</c> settled the
/// same question for value rendering and this is that decision applied again: one composition,
/// two destinations.
/// </para>
/// <para>
/// <b>Nothing is cached here.</b> The shell holds one library for the session and drops it when a
/// package is installed, because a node that arrived this session must have a page; the command
/// line builds one, writes it and exits. A cache in this class would have to serve both policies
/// and would serve neither, so the caller keeps its own.
/// </para>
/// <para>
/// <b>It loads no assembly and compiles nothing</b>, so it is safe on the path a graph with no
/// code block takes (<c>E6-T14</c>): the node pages come from definitions already in the library,
/// and the concept topics are files.
/// </para>
/// </remarks>
public static class HelpComposition
{
    /// <summary>Builds the library.</summary>
    /// <param name="nodes">The node library to generate reference pages from.</param>
    /// <returns>A new library holding every topic, hand-written and generated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is <see langword="null"/>.</exception>
    public static HelpLibrary Build(NodeLibrary nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        HelpLibrary library = new();

        foreach (string directory in Directories())
        {
            if (library.LoadDirectory(directory) > 0)
            {
                break;
            }
        }

        library.AddRange(NodeReference.ForAll(nodes));
        library.Add(NodeReference.Index(nodes));
        library.AddRange(DiagnosticReference.ForAll());

        return library;
    }

    /// <summary>
    /// Where the hand-written topics might be: beside the executable in an install, or up the tree
    /// in a source checkout.
    /// </summary>
    /// <returns>The candidates, in the order they should be tried.</returns>
    /// <remarks>
    /// Two candidates rather than one, because a developer running from <c>bin/Debug</c> and a user
    /// running an install are both ordinary cases, and help that works for only one of them gets
    /// tested by only one of them.
    /// </remarks>
    public static IEnumerable<string> Directories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "help");

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "docs", "help");
            if (Directory.Exists(candidate))
            {
                yield return candidate;
                yield break;
            }

            directory = directory.Parent;
        }
    }
}
