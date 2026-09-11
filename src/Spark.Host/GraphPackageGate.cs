using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using Spark.Packages;

namespace Spark.Host;

/// <summary>
/// Which assemblies in a graph's own package folder code blocks may compile against, and which
/// are still waiting for the user to agree (`E7-T16`,
/// [ADR-0024](../../docs/adr/0024-graph-local-package-folder.md)).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the gate, and it ships in the same commit as the loader because the loader without
/// it is remote code execution.</b> A <c>.spark</c> and a <c>.packages</c> folder of DLLs arrive by
/// email; the graph opens; a code block calls into one of them and it runs with the user's full
/// permissions. <c>E6-T16</c> already refuses to run a graph's code blocks on open — text the user
/// can read. A folder of binaries nobody can read is not held to a lower standard.
/// </para>
/// <para>
/// <b>Consent is per content hash</b>, the client's call, recorded in
/// <see cref="PackageTrustStore"/> beside the per-version decisions for feed packages. An
/// unchanged assembly never asks twice, a rebuilt one asks again, and one that cannot be read
/// hashes to nothing and can never match a decision — so it fails closed.
/// </para>
/// <para>
/// <b>Referenced, not loaded.</b> Agreeing hands the paths to the compiler's catalogue as metadata
/// references; nothing in them runs until a code block calls into one, and a code block runs only
/// under <c>E6-T16</c>'s own rule. What this gate adds is that the call cannot even compile against
/// an assembly nobody agreed to — and <c>ScriptLoadContext</c> resolves only what the catalogue
/// references, so it cannot load one either.
/// </para>
/// <para>
/// <b>It must run before the graph is built</b>, because building a code block's definition
/// compiles it. That is ADR-0024's load-order point, and why a caller opens this ahead of
/// <c>CanvasDocument.Open</c> rather than after it.
/// </para>
/// </remarks>
public sealed class GraphPackageGate
{
    /// <summary>How many assemblies a banner names before it says <i>and N more</i>.</summary>
    private const int Named = 5;

    private readonly PackageTrustStore _trust;
    private readonly Func<Spark.Scripting.ReferenceCatalog?> _catalogue;

    /// <summary>
    /// The catalogue this graph's assemblies were handed to, so releasing takes them out of the
    /// same one. Null when nothing was referenced — which is what keeps a graph with no package
    /// folder from ever touching Roslyn (`E6-T14`).
    /// </summary>
    private Spark.Scripting.ReferenceCatalog? _into;

    /// <summary>Creates a gate over a trust record and a way to reach the compiler's catalogue.</summary>
    /// <param name="trust">Where decisions are recorded.</param>
    /// <param name="catalogue">
    /// How to reach the catalogue, or a delegate returning null when scripting is off. Called only
    /// when there is an agreed assembly to reference.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public GraphPackageGate(PackageTrustStore trust, Func<Spark.Scripting.ReferenceCatalog?> catalogue)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(catalogue);

        _trust = trust;
        _catalogue = catalogue;
    }

    /// <summary>The folder the open graph's packages live in, or empty when it has none.</summary>
    public string Folder { get; private set; } = string.Empty;

    /// <summary>The assemblies code blocks now compile against.</summary>
    public ImmutableArray<GraphAssembly> Referenced { get; private set; } = [];

    /// <summary>The assemblies found and not referenced, because nobody has agreed to them.</summary>
    public ImmutableArray<GraphAssembly> Pending { get; private set; } = [];

    /// <summary>Whether anything in the folder is waiting on the user.</summary>
    public bool IsAwaitingConsent => !Pending.IsEmpty;

    /// <summary>
    /// What to tell the user about the folder, or null when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// <b>It names the files and their hashes</b>, because the list is the information a person
    /// needs to decide — <i>three assemblies</i> tells them nothing about whether they expected them.
    /// </remarks>
    public string? Banner
    {
        get
        {
            GraphAssembly[] readable = [.. Pending.Where(assembly => assembly.Hash.Length > 0)];
            int unreadable = Pending.Length - readable.Length;

            if (readable.Length == 0 && unreadable == 0)
            {
                return null;
            }

            List<string> sentences = [];

            if (readable.Length > 0)
            {
                string named = string.Join(
                    ", ",
                    readable.Take(Named).Select(assembly => $"{assembly.Describe()} ({assembly.ShortHash})"));

                string more = readable.Length > Named
                    ? string.Create(CultureInfo.InvariantCulture, $" and {readable.Length - Named} more")
                    : string.Empty;

                sentences.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"This graph's packages folder holds {Count(readable.Length)} you have not agreed to load: {named}{more}. Code blocks cannot use {(readable.Length == 1 ? "it" : "them")} until you do."));
            }

            if (unreadable > 0)
            {
                sentences.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Count(unreadable)} could not be read, which usually means a build is still writing {(unreadable == 1 ? "it" : "them")}; reopen the graph to be asked about {(unreadable == 1 ? "it" : "them")}."));
            }

            return string.Join(" ", sentences);
        }
    }

    /// <summary>
    /// Releases the previous graph's packages, then references what the user has already agreed
    /// to in this one's folder and holds back the rest.
    /// </summary>
    /// <param name="graphPath">The <c>.spark</c> file's path, or null for a graph with no file.</param>
    /// <returns>How many assemblies were referenced.</returns>
    public int Open(string? graphPath)
    {
        Release();

        GraphPackages found = GraphPackages.Discover(graphPath);
        Folder = found.Folder;

        List<GraphAssembly> agreed = [];
        List<GraphAssembly> pending = [];

        foreach (GraphAssembly assembly in found.Assemblies)
        {
            (_trust.IsTrusted(assembly.Hash) ? agreed : pending).Add(assembly);
        }

        Pending = [.. pending];
        return Reference(agreed);
    }

    /// <summary>
    /// Records the user's agreement to every readable pending assembly, and references them.
    /// </summary>
    /// <returns>How many were agreed to.</returns>
    /// <exception cref="SparkPackageException">The decision could not be saved.</exception>
    /// <remarks>
    /// <b>An unreadable file stays pending.</b> It has no hash, so there is nothing to agree to — and
    /// agreeing to a path instead would agree once to a filename and then load whatever later
    /// occupied it, which is exactly the decision the client ruled out.
    /// </remarks>
    public int Agree()
    {
        GraphAssembly[] readable = [.. Pending.Where(assembly => assembly.Hash.Length > 0)];

        foreach (GraphAssembly assembly in readable)
        {
            _trust.Trust(assembly.Hash);
        }

        Pending = [.. Pending.Where(assembly => assembly.Hash.Length == 0)];
        _ = Reference(readable);

        return readable.Length;
    }

    /// <summary>
    /// Takes every assembly in the folder out of the catalogue, however it got there, and forgets
    /// the folder.
    /// </summary>
    /// <remarks>
    /// <b>Two graphs may disagree about a library version</b> — the reason ADR-0024 chose a folder
    /// per graph — and they can only do that if opening the second one lets go of the first.
    /// </remarks>
    public void Release()
    {
        // Whenever the folder is there, and not only when this gate referenced something: `Add as
        // a library…` installs into the same folder and references what it installed, and a gate
        // that had agreed to nothing itself would otherwise leave that behind for the next graph.
        // A graph with no folder still never reaches the catalogue.
        if (Folder.Length > 0 && (_into is not null || Directory.Exists(Folder)))
        {
            _ = (_into ?? _catalogue())?.RemoveUnder(Folder);
        }

        _into = null;
        Folder = string.Empty;
        Referenced = [];
        Pending = [];
    }

    private int Reference(IReadOnlyList<GraphAssembly> assemblies)
    {
        if (assemblies.Count == 0)
        {
            return 0;
        }

        // Asked for only now, with something to hand it: reaching the catalogue builds the script
        // factory, and a graph whose folder held nothing agreed to has no reason to.
        if (_catalogue() is not { } catalogue)
        {
            return 0;
        }

        _ = catalogue.Add(assemblies.Select(assembly => assembly.Path));
        _into = catalogue;
        Referenced = Referenced.AddRange(assemblies);

        return assemblies.Count;
    }

    private static string Count(int count) =>
        count == 1
            ? "1 assembly"
            : string.Create(CultureInfo.InvariantCulture, $"{count} assemblies");
}
