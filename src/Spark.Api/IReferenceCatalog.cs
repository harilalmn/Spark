using System.Collections.Generic;
using System.Collections.Immutable;

namespace Spark.Api;

/// <summary>
/// The set of assemblies a code block compiles against, seen from outside the scripting assembly.
/// </summary>
/// <remarks>
/// <para>
/// <b>This interface exists for the same reason <see cref="IScriptNodeFactory"/> does, and it was
/// added because that reason turned out not to be met.</b> <c>E6-T14</c> promises that a graph
/// containing no script nodes never loads <c>Spark.Scripting</c>, and the shell broke the promise
/// at application startup — not by calling anything, but by <i>constructing a delegate</i>. A
/// <c>Func&lt;ReferenceCatalog?&gt;</c> passed to the package browser and the local-references
/// list, written precisely so that the catalogue would be fetched lazily, is still a mention of
/// the return type; the JIT resolves it when it compiles the method that builds the delegate, and
/// twenty megabytes of Roslyn arrive before the first document is opened. Laziness expressed as a
/// delegate is not laziness if the delegate's type is the thing you are deferring
/// (<c>N188</c>).
/// </para>
/// <para>
/// <b>So the deferred type has to be one the caller can name without loading anything</b>, which
/// is this. The host and the shell hold <c>Func&lt;IReferenceCatalog?&gt;</c>, and
/// <c>Spark.Scripting.ReferenceCatalog</c> is the only implementation — it is not an abstraction
/// over two things and is not meant to become one. The members are exactly those a host calls;
/// everything Roslyn-shaped that a catalogue also offers, <c>MetadataReference</c>s and the
/// prelude among them, stays on the concrete class where only the compiler reaches it.
/// </para>
/// </remarks>
public interface IReferenceCatalog
{
    /// <summary>
    /// The imports that were asked for and not taken, each with the reason.
    /// </summary>
    /// <remarks>
    /// A namespace is skipped when one of its type names is already spoken for, so that adding a
    /// library cannot change what an existing code block's identifiers mean. A user who has just
    /// added a package and finds their types unavailable needs this sentence, which is why it is
    /// on the interface rather than left to the compiler.
    /// </remarks>
    ImmutableArray<string> SkippedImports { get; }

    /// <summary>How many times the catalogue has changed.</summary>
    /// <remarks>
    /// Read to decide whether a cached compilation is still valid. It is a counter rather than a
    /// hash of the contents because the question is only ever <i>has this moved</i>.
    /// </remarks>
    int Version { get; }

    /// <summary>Adds assemblies to the set code blocks compile against.</summary>
    /// <param name="paths">The assembly files, in the order they should be considered.</param>
    /// <returns>How many were added, which is not the number offered: a duplicate counts once.</returns>
    int Add(IEnumerable<string> paths);

    /// <summary>Drops one assembly from the set.</summary>
    /// <param name="path">The assembly file.</param>
    /// <returns>Whether it was there to drop.</returns>
    bool Remove(string path);

    /// <summary>Drops every assembly the catalogue holds from beneath a folder.</summary>
    /// <param name="folder">The folder, matched as a prefix of each held path.</param>
    /// <returns>How many were dropped.</returns>
    /// <remarks>
    /// A graph's package folder is referenced and unreferenced as a unit when the document is
    /// opened and closed, so the host needs to undo an <see cref="Add"/> it did not itemise.
    /// </remarks>
    int RemoveUnder(string folder);

    /// <summary>Re-reads one assembly that is already referenced, or adds it if it is not.</summary>
    /// <param name="path">The assembly file.</param>
    /// <returns>Whether the catalogue now references it.</returns>
    /// <remarks>
    /// The answer to an assembly being rebuilt while Spark is open. It returns false rather than
    /// throwing when the file cannot be read as an assembly, because the caller is a user
    /// interface showing a list and a half-written build output is an ordinary thing to find.
    /// </remarks>
    bool Reload(string path);
}
