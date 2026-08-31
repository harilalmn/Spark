using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Spark.Scripting;

/// <summary>
/// The set of assemblies a code block compiles against, and the <c>using</c> lines it starts with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Getting this wrong produces errors that look like the user's fault</b>, which is why it is a
/// type of its own rather than a list built at the compile site. A missing reference does not say
/// <i>the host forgot to include Spark.Geometry</i>; it says <c>CS0246: the type or namespace name
/// 'Point3d' could not be found</c>, on the user's line, under their cursor. They will look at
/// their own spelling first, and they will be wrong to.
/// </para>
/// <para>
/// <b>The catalogue is read without locking and cached by version</b> (`E6-T2`). Reads take no lock
/// at all, which is what lets a user rebuild their node library in Visual Studio while Spark is
/// running: the catalogue swaps atomically to a new immutable snapshot and anything mid-compile
/// finishes against the one it started with. That property is also what `E7-T9`'s auto-reload is
/// built on, so it is here from the start rather than retrofitted.
/// </para>
/// <para>
/// <b>The version is part of every compile-cache key</b> (`E6-T10`). A script whose text has not
/// changed still has to recompile if the assemblies underneath it have, or a user who fixed a bug
/// in their own library would keep getting the old behaviour with no way to explain it.
/// </para>
/// </remarks>
public sealed class ReferenceCatalog
{
    private static readonly string[] DefaultImports =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "Spark.Api",
        "Spark.Geometry",
    ];

    private Snapshot _current;

    /// <summary>Creates a catalogue over the assemblies this process already has loaded.</summary>
    public ReferenceCatalog() => _current = Build([]);

    /// <summary>The assemblies a compilation should reference.</summary>
    public ImmutableArray<MetadataReference> References => _current.References;

    /// <summary>The namespaces a code block is compiled with already imported.</summary>
    public ImmutableArray<string> Imports => _current.Imports;

    /// <summary>
    /// How many times the catalogue has changed. Part of every compile-cache key.
    /// </summary>
    /// <remarks>
    /// A counter rather than a hash of the contents: what a cache needs is *did this change*, and a
    /// counter answers that without reading a hundred files to prove nothing did.
    /// </remarks>
    public int Version => _current.Version;

    /// <summary>
    /// Adds assemblies to the catalogue, replacing the snapshot readers see.
    /// </summary>
    /// <param name="paths">Paths to assemblies. Ones that cannot be read are skipped.</param>
    /// <returns>
    /// How much the catalogue grew, which is <b>not the same as how many of
    /// <paramref name="paths"/> were added</b>: rebuilding the snapshot also picks up assemblies
    /// the process has loaded since the last one. A caller that needs to know whether a particular
    /// path is now referenced should use <see cref="Reload"/>, which answers that question.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="paths"/> is null.</exception>
    /// <remarks>
    /// <b>A path that cannot be read is skipped rather than thrown on.</b> The common cause is a
    /// file being rewritten by a build that is still running, and failing the whole catalogue
    /// because one assembly was briefly locked would take the code block down for a reason that
    /// resolves itself in a second.
    /// </remarks>
    public int Add(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        Snapshot replacement = Build(paths);
        int added = replacement.References.Length - _current.References.Length;

        // One assignment of an immutable snapshot. A reader mid-compile keeps the one it started
        // with, and no reader ever sees a half-built list.
        _current = replacement with { Version = _current.Version + 1 };

        return System.Math.Max(0, added);
    }

    /// <summary>
    /// Drops an added assembly from the catalogue (<c>E7-T9</c>).
    /// </summary>
    /// <param name="path">The assembly's path.</param>
    /// <returns>True when it was in the catalogue.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    /// <remarks>
    /// <para>
    /// <b>Only paths the process has not loaded can be dropped.</b> An assembly already loaded is
    /// put back by the next <see cref="Build"/>, because it is genuinely still referenceable and
    /// pretending otherwise would produce a compile error naming a type that plainly exists. What
    /// this removes is a user's own added reference, which is the only kind anybody asks to
    /// remove.
    /// </para>
    /// <para>
    /// Bumps <see cref="Version"/> like <see cref="Add"/>, so cached compilations against the old
    /// set are not reused. A removal that did not invalidate the cache would leave a script
    /// compiling against an assembly the user had just taken away.
    /// </para>
    /// </remarks>
    public bool Remove(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Full(path);

        ImmutableArray<MetadataReference> kept =
        [
            .. _current.References.Where(reference =>
                reference is not PortableExecutableReference { FilePath: { } existing }
                || !string.Equals(Full(existing), full, StringComparison.OrdinalIgnoreCase)),
        ];

        if (kept.Length == _current.References.Length)
        {
            return false;
        }

        _current = _current with { References = kept, Version = _current.Version + 1 };
        return true;
    }

    /// <summary>
    /// Re-reads an added assembly, replacing the metadata the catalogue holds for it
    /// (<c>E7-T9</c>).
    /// </summary>
    /// <param name="path">The assembly's path.</param>
    /// <returns>True when it could be read.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    /// <remarks>
    /// <b>Remove and then add, in that order, because <see cref="Add"/> alone would not replace
    /// it.</b> <see cref="Build"/> keeps an existing reference for any path the new snapshot does
    /// not already have, which is what makes the catalogue additive — and it is exactly what would
    /// make a reload silently do nothing.
    /// </remarks>
    public bool Reload(string path)
    {
        _ = Remove(path);
        return Add([path]) > 0 || _current.References.Any(reference =>
            reference is PortableExecutableReference { FilePath: { } existing }
            && string.Equals(Full(existing), Full(path), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A hash of the references themselves, stable across runs (`E6-T10`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="Version"/> answers *did this change*; this answers *is this the same*</b>, and
    /// the on-disk compile cache needs the second. A counter that starts at zero in every process
    /// would let two different sets of references share a cache entry across runs — the script's
    /// text would match, the counter would match, and the assembly loaded would have been compiled
    /// against something else.
    /// </para>
    /// <para>
    /// Derived from each reference's path, length and last-write time, sorted, because that is what
    /// changes when a user rebuilds their node library — and it costs one directory read rather
    /// than hashing a hundred megabytes of assemblies to learn the same thing.
    /// </para>
    /// </remarks>
    public string Fingerprint
    {
        get
        {
            List<string> parts = [];

            foreach (MetadataReference reference in _current.References)
            {
                if (reference is not PortableExecutableReference { FilePath: { } path })
                {
                    continue;
                }

                try
                {
                    FileInfo file = new(path);

                    parts.Add(string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"{path}|{file.Length}|{file.LastWriteTimeUtc.Ticks}"));
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // A reference that cannot be stat'd contributes its path alone. It is still in
                    // the fingerprint, so its presence or absence still changes the answer.
                    parts.Add(path);
                }
            }

            parts.Sort(StringComparer.Ordinal);

            byte[] hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, parts)));

            return Convert.ToHexString(hash);
        }
    }

    /// <summary>
    /// Where an assembly the catalogue references lives on disk, by simple name (<c>E7-T9</c>).
    /// </summary>
    /// <param name="simpleName">The assembly's simple name, without extension or version.</param>
    /// <returns>Its path, or <see langword="null"/> when the catalogue does not reference it.</returns>
    /// <remarks>
    /// <b>Compiling against an assembly is not the same as being able to run against it.</b> A
    /// script that calls into a user's own DLL compiles happily and then fails at evaluation with
    /// <c>Could not load file or assembly</c>, because the script's load context defers to the
    /// default one and the default one has never heard of a file in some folder of the user's.
    /// This is how <see cref="ScriptLoadContext"/> finds it, and it is deliberately restricted to
    /// what the catalogue already references — a script cannot reach an assembly nobody
    /// agreed to.
    /// </remarks>
    public string? PathFor(string? simpleName)
    {
        if (string.IsNullOrWhiteSpace(simpleName))
        {
            return null;
        }

        foreach (MetadataReference reference in _current.References)
        {
            if (reference is PortableExecutableReference { FilePath: { } path }
                && string.Equals(
                    Path.GetFileNameWithoutExtension(path), simpleName, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>The prelude a script is wrapped in: the imports, one per line.</summary>
    /// <returns>The using directives, newline separated.</returns>
    public string Prelude() =>
        string.Join(Environment.NewLine, Imports.Select(import => $"using {import};"));

    private Snapshot Build(IEnumerable<string> extraPaths)
    {
        Dictionary<string, MetadataReference> byPath = [];

        // `dynamic` in a generated script binds through Microsoft.CSharp, and that assembly is
        // not loaded until something touches it - so the sweep below would miss it and every
        // script using an input port would fail with "Missing compiler required member
        // 'CSharpArgumentInfo.Create'", which names nothing the user wrote. Added by name rather
        // than hoped for.
        TryAdd(byPath, typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly.Location);

        // And the call site the binder dispatches through, which lives in System.Linq.Expressions
        // and is a *second* assembly `dynamic` needs. Found the same way as the first: a test class
        // that had loaded neither compiled `return count * 2;` and was told
        // "Missing compiler required member 'Binder.BinaryOperation'" - a message that names the
        // binder while the assembly actually missing is the one underneath it.
        TryAdd(byPath, typeof(System.Runtime.CompilerServices.CallSite).Assembly.Location);

        // And the two assemblies DefaultImports promises. Everything else here is discovered by
        // sweeping what the process has loaded, and a referenced assembly does not load until
        // something touches a type in it - so a catalogue built early enough can be missing
        // Spark.Geometry while still telling every script `using Spark.Geometry;`. The user then
        // gets "the type or namespace name 'Geometry' does not exist in the namespace 'Spark'",
        // on a line they did not write. Found by a test that happened to build one early.
        TryAdd(byPath, typeof(Spark.Api.SparkNodeAttribute).Assembly.Location);
        TryAdd(byPath, typeof(Spark.Geometry.Point3d).Assembly.Location);

        // Everything already loaded, which covers the framework, Spark.Api and Spark.Geometry
        // without anybody naming them. Dynamic assemblies have no location and are skipped.
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
            {
                continue;
            }

            TryAdd(byPath, assembly.Location);
        }

        foreach (string path in extraPaths)
        {
            TryAdd(byPath, path);
        }

        foreach (MetadataReference existing in _current?.References ?? [])
        {
            if (existing is PortableExecutableReference portable
                && portable.FilePath is { } path
                && !byPath.ContainsKey(path))
            {
                byPath[path] = existing;
            }
        }

        return new Snapshot(
            [.. byPath.Values],
            [.. DefaultImports],
            _current?.Version ?? 0);
    }

    private static string Full(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception failure) when (failure is ArgumentException or IOException or NotSupportedException)
        {
            return path;
        }
    }

    private static void TryAdd(Dictionary<string, MetadataReference> into, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || into.ContainsKey(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            into[path] = MetadataReference.CreateFromFile(path);
        }
        catch (Exception failure) when (failure is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            // Skipped on purpose: see the remarks on Add. A file being rewritten by a build in
            // progress is the common case and it resolves itself.
        }
    }

    private sealed record Snapshot(
        ImmutableArray<MetadataReference> References, ImmutableArray<string> Imports, int Version);
}
