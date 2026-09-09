using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
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

    /// <summary>The assembly holding the node library, named rather than referenced.</summary>
    /// <remarks>
    /// <b><c>Spark.Scripting</c> does not reference <c>Spark.Nodes.Core</c> and must not.</b> The
    /// node library is a *consumer* of the engine, and a scripting layer that depended on it would
    /// invert that — so the assembly is recognised by name among the ones the process has loaded,
    /// exactly as the sweep in <see cref="Build"/> already finds everything else.
    /// </remarks>
    private const string NodeLibrary = "Spark.Nodes.Core";

    /// <summary>
    /// What a code block gains when the node library is loaded (`E6-T30`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asked for by the client: every node in the library callable from a block.</b> Most
    /// already were — the geometry-shaped nodes are thin façades over <c>Spark.Geometry</c>, which
    /// a block has always imported, so <c>Circle.FromCenterRadius(pt, 5)</c> has worked all along.
    /// What was out of reach is the façades with no geometry equivalent, and there are a lot of
    /// them: <c>Solid</c>'s 38 booleans and fillets, <c>List</c>, <c>Logic</c>, <c>String</c>,
    /// <c>Number</c>, <c>Colour</c>, <c>Display</c>, <c>DateTime</c>, <c>TimeSpan</c>.
    /// </para>
    /// <para>
    /// <b>The import alone would break every block anybody has written</b>, which is why the
    /// namespace was excluded in the first place. Nine of the library's twenty-three type names
    /// collide with <c>Spark.Geometry</c> — <c>Arc</c>, <c>BoundingBox</c>, <c>Circle</c>,
    /// <c>Curve</c>, <c>Line</c>, <c>Plane</c>, <c>PolyCurve</c>, <c>PolyLine</c>, <c>Surface</c> —
    /// and <c>Math</c> collides with <c>System.Math</c>. Two namespace imports offering the same
    /// name is <c>CS0104</c>, on the user's line, for code that compiled yesterday.
    /// </para>
    /// <para>
    /// <b>An explicit alias beats a namespace import, and that is the whole mechanism.</b> Each
    /// colliding name is pinned to what it has always meant, so nothing that compiles today changes
    /// meaning and the other fourteen façades become reachable unqualified. The library's own
    /// versions stay available in full — <c>Spark.Nodes.Core.Circle.FromCenterRadius</c> — which is
    /// what the reference pages have always printed.
    /// </para>
    /// <para>
    /// <b><c>Math</c> is pinned to <c>System.Math</c> deliberately.</b> A block is C#, and
    /// <c>Math.PI</c> meaning anything else would be a trap; the library's <c>Math.Sin</c> does what
    /// <c>System.Math.Sin</c> does anyway. This is the collision that kept the namespace out of the
    /// prelude for a year, recorded in <c>NodeDefinition</c> and <c>NodeImporter</c>.
    /// </para>
    /// </remarks>
    private static readonly string[] NodeLibraryImports =
    [
        NodeLibrary,

        // `E8-T80`: AND `Console`, PINNED THE OTHER WAY - TO SPARK'S, NOT SYSTEM'S.
        //
        // Every block imports `System`, so the node library's `Console` would be `CS0104` against
        // `System.Console` without a pin. The direction is the opposite of `Math`'s and for the
        // same reason `Math`'s goes the way it does: what would the user mean? A windowed
        // application has no terminal, so `System.Console.WriteLine` writes where nobody can look -
        // whereas Spark's puts the line in the Console pane, which is what somebody typing it in a
        // code block is asking for. `System.Console` is still there under its full name.

        // `E8-T80`: AND `Console`, PINNED THE OTHER WAY - TO SPARK'S, NOT SYSTEM'S.
        //
        // Every block imports `System`, so the node library's `Console` would be `CS0104` against
        // `System.Console` without a pin. The direction is the opposite of `Math`'s and for the
        // same reason `Math`'s goes the way it does: what would the user mean? A windowed
        // application has no terminal, so `System.Console.WriteLine` writes where nobody can look -
        // whereas Spark's puts the line in the Console pane, which is what somebody typing it in a
        // code block is asking for. `System.Console` is still there under its full name.
        "Console = Spark.Nodes.Core.Console",

        // The nine that collide with Spark.Geometry, pinned to the geometry type a block has always
        // meant by them, and `Math`, pinned to System's.
        "Math = System.Math",
        "Arc = Spark.Geometry.Arc",
        "BoundingBox = Spark.Geometry.BoundingBox",
        "Circle = Spark.Geometry.Circle",
        "Curve = Spark.Geometry.Curve",
        "Line = Spark.Geometry.Line",
        "Plane = Spark.Geometry.Plane",
        "PolyCurve = Spark.Geometry.PolyCurve",
        "PolyLine = Spark.Geometry.PolyLine",
        "Surface = Spark.Geometry.Surface",
    ];

    private Snapshot _current;

    /// <summary>
    /// The assemblies a user added by choice, whose namespaces are imported for them (`E7-T21`).
    /// </summary>
    private readonly HashSet<string> _libraries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a catalogue over the assemblies this process already has loaded.</summary>
    public ReferenceCatalog() => _current = Build([]);

    /// <summary>The assemblies a compilation should reference.</summary>
    public ImmutableArray<MetadataReference> References => _current.References;

    /// <summary>The namespaces a code block is compiled with already imported.</summary>
    public ImmutableArray<string> Imports => _current.Imports;

    /// <summary>
    /// A sentence for each namespace of an added library that was <b>not</b> imported, because a
    /// name in it already means something else (`E6-T39`).
    /// </summary>
    /// <remarks>
    /// <b>Silence would be the worst of the three options.</b> Importing a colliding namespace
    /// breaks blocks that worked yesterday; skipping it without saying so leaves a user typing a
    /// type name that plainly exists and being told it does not. Each sentence names the types that
    /// stopped it and the <c>using</c> to write instead.
    /// </remarks>
    public ImmutableArray<string> SkippedImports => _current.Skipped;

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

        string[] extra = [.. paths];

        // `E7-T21`: REMEMBERED SEPARATELY FROM THE SWEEP, BECAUSE ONLY THESE ARE IMPORTED.
        //
        // Everything else in the catalogue is whatever the process happens to have loaded - the
        // whole of the framework included - and importing the namespaces of *that* would put every
        // BCL namespace in front of every code block and make half the names in .NET ambiguous.
        // A library the user chose is a different thing, and choosing it is the consent that makes
        // importing it reasonable.
        foreach (string path in extra)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                _libraries.Add(Full(path));
            }
        }

        Snapshot replacement = Build(extra);
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

        // `E6-T30`: THE NODE LIBRARY IS IMPORTED ONLY WHEN IT IS ACTUALLY REFERENCED.
        //
        // The sweep above finds what the process has loaded, and a host that never loaded
        // `Spark.Nodes.Core` - a test, an embedder, a tool - would otherwise be told
        // `using Spark.Nodes.Core;` for an assembly that is not there, and EVERY script would
        // fail to compile on a line the user did not write. That is the failure mode the comment
        // above this method already records for Spark.Geometry, met a second time.
        bool nodes = byPath.Keys.Any(path =>
            string.Equals(Path.GetFileNameWithoutExtension(path), NodeLibrary, StringComparison.OrdinalIgnoreCase));

        string[] imports = nodes ? [.. DefaultImports, .. NodeLibraryImports] : [.. DefaultImports];

        (ImmutableArray<string> libraries, ImmutableArray<string> skipped) =
            LibraryImports(imports, byPath.Keys);

        return new Snapshot(
            [.. byPath.Values],
            [.. imports, .. libraries],
            skipped,
            _current?.Version ?? 0);
    }

    /// <summary>
    /// The namespaces of the libraries a user added that can be imported without changing what a
    /// name already means, and a sentence about each one that cannot (`E7-T21`, `E6-T39`).
    /// </summary>
    /// <param name="already">What every block imports anyway, aliases included.</param>
    /// <param name="referencePaths">Every assembly in the snapshot, to read those names from.</param>
    /// <returns>The namespaces to add to the prelude, and the refusals to show the user.</returns>
    /// <remarks>
    /// <para>
    /// <b>Asked for by the client</b>: <i>I hope the user does not have to declare the using
    /// statement to use the classes from the NuGet libraries installed.</i> Adding a library and
    /// then being told its types do not exist is the ceremony the whole feature was meant to
    /// remove — so a library's namespaces go into the prelude, and a block names its types the way
    /// it names <c>Point3d</c>.
    /// </para>
    /// <para>
    /// <b>And the client found the hole in that an hour after it shipped, which is why the import
    /// is conditional rather than wholesale.</b> Their words: <i>take care of the possible
    /// ambiguity, as the user cannot specify</i> <c>using Circle = SomeNameSpace.Circle;</c>. Both
    /// halves of that are right. <c>Autodesk.Revit.DB</c> — the library they actually want — defines
    /// <c>Arc</c>, <c>Curve</c>, <c>Line</c>, <c>Mesh</c>, <c>Plane</c>, <c>Point</c> and
    /// <c>Transform</c>, and so does <c>Spark.Geometry</c>; importing it would answer every
    /// geometry block anybody has written with <c>CS0104</c>. `E6-T30` learnt exactly this against
    /// <c>Spark.Nodes.Core</c>, and the second telling is not cheaper than the first.
    /// </para>
    /// <para>
    /// <b>A namespace is imported only when <i>none</i> of its public type names is already spoken
    /// for, and that is coarse on purpose.</b> Importing the harmless half of a namespace is not
    /// something a <c>using</c> can express, and the finer rule anybody would reach for — alias
    /// each colliding name to what it means today, as `E6-T30` did — does not survive contact with
    /// a real library: <c>RevitAPI</c> has some ten thousand public types, and a prelude with ten
    /// thousand lines in it is not a prelude. <i>Why does <c>Foo</c> resolve and <c>Bar</c> not</i>
    /// is a worse question to leave a user holding than <i>this namespace was not imported, and
    /// here are the names that stopped it</i>.
    /// </para>
    /// <para>
    /// <b>Which leaves the user needing a way in, and `E6-T39` is the half that gives them one</b>:
    /// a <c>using</c> written at the top of a code block is now hoisted out of the method body to
    /// namespace scope, so <c>using Autodesk.Revit.DB;</c> works in the block that wants it. It is
    /// emitted one scope inside the prelude, so it does not merely add to the prelude — it
    /// <i>beats</i> it, and <c>Line</c> in that block is Revit's without any ambiguity to resolve.
    /// Where a user does import two namespaces that clash, <c>using RevitLine =
    /// Autodesk.Revit.DB.Line;</c> — the exact line the client said could not be written — settles
    /// it the ordinary C# way. Refusing an import that a user could not then ask for by hand would
    /// have been half an answer.
    /// </para>
    /// <para>
    /// <b>Public top-level types only.</b> A nested type is not nameable through a namespace
    /// import, so it can neither be gained by one nor collide with one, and counting it would
    /// refuse imports over a clash that cannot happen.
    /// </para>
    /// </remarks>
    private (ImmutableArray<string> Imports, ImmutableArray<string> Skipped) LibraryImports(
        IReadOnlyList<string> already, IEnumerable<string> referencePaths)
    {
        if (_libraries.Count == 0)
        {
            return ([], []);
        }

        HashSet<string> spaces = new(StringComparer.Ordinal);
        HashSet<string> taken = new(StringComparer.Ordinal);

        foreach (string import in already)
        {
            int equals = import.IndexOf('=', StringComparison.Ordinal);

            // `E6-T30`'s aliases are in this list too, and an alias occupies exactly the one name
            // it defines - reading `Circle = Spark.Geometry.Circle` as a namespace would send this
            // looking for the types of a namespace nobody has.
            if (equals < 0)
            {
                spaces.Add(import);
            }
            else
            {
                taken.Add(import[..equals].Trim());
            }
        }

        foreach (string path in referencePaths)
        {
            foreach ((string space, SortedSet<string> types) in PublicTypesIn(path))
            {
                if (spaces.Contains(space))
                {
                    taken.UnionWith(types);
                }
            }
        }

        // Merged across the library's assemblies before anything is decided, because a namespace
        // split over two DLLs - which is most packages of any size - has to be judged on all of its
        // names at once rather than accepted on the first DLL and re-judged on the second.
        Dictionary<string, SortedSet<string>> offered = new(StringComparer.Ordinal);

        foreach (string path in _libraries)
        {
            foreach ((string space, SortedSet<string> types) in PublicTypesIn(path))
            {
                if (!offered.TryGetValue(space, out SortedSet<string>? all))
                {
                    all = new SortedSet<string>(StringComparer.Ordinal);
                    offered[space] = all;
                }

                all.UnionWith(types);
            }
        }

        List<string> imports = [];
        List<string> skipped = [];

        // Ordinal, so that two libraries whose namespaces collide with *each other* resolve the
        // same way on every machine and every run rather than by whatever order the files came in.
        foreach (string space in offered.Keys.Order(StringComparer.Ordinal))
        {
            if (spaces.Contains(space))
            {
                continue;
            }

            string[] clashes = [.. offered[space].Where(taken.Contains)];

            if (clashes.Length > 0)
            {
                skipped.Add(Clash(space, clashes));
                continue;
            }

            imports.Add(space);
            taken.UnionWith(offered[space]);
        }

        return ([.. imports], [.. skipped]);
    }

    /// <summary>Why one namespace was left out, and what to write instead.</summary>
    /// <remarks>
    /// <b>It names the types rather than saying <i>some names collided</i></b>, because the list is
    /// most of the information: it is how a user tells whether the collision touches anything they
    /// were going to use, and the alias in the last sentence is the part they can act on.
    /// </remarks>
    private static string Clash(string space, IReadOnlyList<string> names)
    {
        const int Most = 6;

        string listed = string.Join(", ", names.Take(Most))
            + (names.Count > Most
                ? FormattableString.Invariant($" and {names.Count - Most} more")
                : string.Empty);

        return $"'{space}' is referenced but not imported for you: {listed} would become "
            + $"ambiguous. Write 'using {space};' at the top of the block that needs it, and "
            + $"'using My{names[0]} = {space}.{names[0]};' to say which '{names[0]}' you mean.";
    }

    /// <summary>
    /// The public top-level type names an assembly declares, by namespace, without loading it.
    /// </summary>
    /// <remarks>
    /// <b>Metadata rather than reflection, because loading is the thing to avoid.</b>
    /// <c>Assembly.Load</c> to ask a question about names would run module initialisers, pin the
    /// file, and put a user's library in this process before anybody agreed to it. A metadata read
    /// answers the same question and does none of that.
    /// </remarks>
    private static Dictionary<string, SortedSet<string>> PublicTypesIn(string path)
    {
        Dictionary<string, SortedSet<string>> found = new(StringComparer.Ordinal);

        try
        {
            using FileStream file = new(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using System.Reflection.PortableExecutable.PEReader reader = new(file);

            if (!reader.HasMetadata)
            {
                // A native DLL beside a managed one, which every package with a runtime component
                // has. It is a reference to nothing and has no namespaces to offer.
                return found;
            }

            MetadataReader metadata = reader.GetMetadataReader();

            foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
            {
                TypeDefinition type = metadata.GetTypeDefinition(handle);

                // `Public`, and deliberately not `NestedPublic`: see the remarks on LibraryImports.
                if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
                {
                    continue;
                }

                string space = metadata.GetString(type.Namespace);

                if (space.Length == 0)
                {
                    continue;
                }

                if (!found.TryGetValue(space, out SortedSet<string>? names))
                {
                    names = new SortedSet<string>(StringComparer.Ordinal);
                    found[space] = names;
                }

                // The arity goes with the backtick: `List` and `List<T>` are one name as far as
                // `CS0104` is concerned, and a check that kept the metadata spelling would miss it.
                string name = metadata.GetString(type.Name);
                int arity = name.IndexOf('`', StringComparison.Ordinal);

                names.Add(arity < 0 ? name : name[..arity]);
            }
        }
        catch (Exception failure) when (failure is IOException
            or UnauthorizedAccessException
            or BadImageFormatException)
        {
            // Unreadable, mid-rebuild, or not a managed assembly at all. No namespaces rather than
            // a failure: the reference itself is handled elsewhere and one that cannot be read
            // will report itself there, on the user's own line.
        }

        return found;
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
        ImmutableArray<MetadataReference> References,
        ImmutableArray<string> Imports,
        ImmutableArray<string> Skipped,
        int Version);
}
