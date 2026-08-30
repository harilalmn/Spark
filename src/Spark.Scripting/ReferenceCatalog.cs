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
    /// <returns>How many were added.</returns>
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
