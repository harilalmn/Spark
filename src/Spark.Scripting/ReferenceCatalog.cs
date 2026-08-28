using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Spark.Scripting;

/// <summary>
/// The set of metadata references a code block compiles against: the whole shared framework,
/// everything this process has already loaded — Spark's own assemblies among them — and any extra
/// assembly the host has been told about.
/// </summary>
/// <remarks>
/// <para>
/// <b>Extra assemblies are read from memory, not from disk.</b> Referencing a file holds a lock on it
/// for the life of the process, which means a user could not rebuild their own node library in Visual
/// Studio while Spark was open. That is the difference between a usable workflow and an infuriating
/// one, and it costs one <see cref="File.ReadAllBytes(string)"/>.
/// </para>
/// <para>
/// <b>Bad images are rejected here, once.</b> The metadata is read eagerly through
/// <see cref="MetadataReader"/>, because a native image or a bare <c>.netmodule</c> is accepted
/// happily by <see cref="AssemblyMetadata.CreateFromFile(string)"/> and only fails later — as CS0009
/// on <i>every</i> compile, pointing at nothing in particular. Failing at the point of the mistake is
/// worth the read.
/// </para>
/// <para>
/// <b>One reference per assembly name.</b> Two assemblies of one name in a single compilation is
/// CS1704 on every script the user runs, and the same file genuinely does arrive from two places. The
/// framework copy always wins, because that is the one a running script binds to; everywhere else the
/// newest metadata wins, with an exact tie broken by source, so the answer does not depend on the
/// order assemblies happened to load in.
/// </para>
/// <para>
/// <b>Security.</b> Every assembly named here is loaded into this process and can do anything this
/// process can. .NET has no code-access security, so a reference path is a trust decision. See
/// <see cref="CodeBlockCompiler"/> for the whole posture.
/// </para>
/// </remarks>
public sealed class ReferenceCatalog
{
    private static readonly ConcurrentDictionary<string, Entry?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly string[] _additionalPaths;
    private readonly Lock _gate = new();
    private IReadOnlyList<MetadataReference>? _references;
    private IReadOnlyList<string>? _conflicts;
    private string? _version;

    /// <summary>Creates a catalog.</summary>
    /// <param name="additionalAssemblyPaths">
    /// Assemblies to reference beyond the framework and what is already loaded — a user's own node
    /// DLLs, typically. Read into memory so the files stay writable. Missing paths are ignored.
    /// </param>
    public ReferenceCatalog(IEnumerable<string>? additionalAssemblyPaths = null) =>
        _additionalPaths = additionalAssemblyPaths is null ? [] : [.. additionalAssemblyPaths];

    /// <summary>
    /// The catalog with no extra assemblies: the framework, Spark, and whatever else is loaded.
    /// </summary>
    public static ReferenceCatalog Default { get; } = new();

    /// <summary>The references, built once and then reused. Assembling them costs real time.</summary>
    public IReadOnlyList<MetadataReference> References
    {
        get
        {
            Build();
            return _references!;
        }
    }

    /// <summary>
    /// Every name that appeared more than once, with the version kept and the version dropped. Worth
    /// showing when a script fails to see a type it should be able to see.
    /// </summary>
    public IReadOnlyList<string> Conflicts
    {
        get
        {
            Build();
            return _conflicts!;
        }
    }

    /// <summary>
    /// A short hash of the exact assembly identities in this catalog. It goes into every compile
    /// cache key, so pointing Spark at a rebuilt library invalidates the cached compilations that
    /// were made against the old one — and nothing else.
    /// </summary>
    public string Version
    {
        get
        {
            Build();
            return _version!;
        }
    }

    private void Build()
    {
        if (_references is not null)
        {
            return;
        }

        lock (_gate)
        {
            if (_references is not null)
            {
                return;
            }

            List<KeyValuePair<string, Source>> candidates = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            void Add(string? path, Source source)
            {
                if (!string.IsNullOrEmpty(path) && seen.Add(path))
                {
                    candidates.Add(new KeyValuePair<string, Source>(path, source));
                }
            }

            // The host's trusted platform assemblies are the complete, version-matched BCL. This is
            // the copy a running script actually binds to, which is why it outranks everything else.
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
            {
                foreach (string path in trusted.Split(Path.PathSeparator))
                {
                    if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        Add(path, Source.Framework);
                    }
                }
            }

            // Touch the Spark assemblies a code block scripts against, so they are loaded before the
            // sweep below rather than turning up only if something else happened to pull them in.
            foreach (Type anchor in SparkAnchors)
            {
                Add(SafeLocation(anchor.Assembly), Source.Loaded);
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic)
                {
                    Add(SafeLocation(assembly), Source.Loaded);
                }
            }

            foreach (string path in _additionalPaths)
            {
                if (File.Exists(path))
                {
                    Add(path, Source.Additional);
                }
            }

            Deduplicate(candidates, out List<MetadataReference> references, out List<string> conflicts, out string version);

            _conflicts = conflicts;
            _version = version;
            _references = references;
        }
    }

    private static readonly Type[] SparkAnchors =
    [
        typeof(Spark.Api.SparkList),
        typeof(Spark.Engine.NodeDefinition),
        typeof(Spark.Geometry.Point3d),
        typeof(ScriptGuard),
    ];

    private static void Deduplicate(
        List<KeyValuePair<string, Source>> candidates,
        out List<MetadataReference> references,
        out List<string> conflicts,
        out string version)
    {
        Dictionary<string, Entry> chosen = new(candidates.Count, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Source> from = new(candidates.Count, StringComparer.OrdinalIgnoreCase);
        conflicts = [];

        foreach (KeyValuePair<string, Source> candidate in candidates)
        {
            Entry? entry = TryRead(candidate.Key, copyIntoMemory: candidate.Value == Source.Additional);
            if (entry is null)
            {
                continue;
            }

            if (!chosen.TryGetValue(entry.Name, out Entry? incumbent))
            {
                chosen[entry.Name] = entry;
                from[entry.Name] = candidate.Value;
                continue;
            }

            bool wins = from[entry.Name] != Source.Framework
                && candidate.Value != Source.Framework
                && entry.Version > incumbent.Version;

            Entry kept = wins ? entry : incumbent;
            Entry dropped = wins ? incumbent : entry;
            conflicts.Add($"{entry.Name}: kept {kept.Version} from {kept.Path}, dropped {dropped.Version} from {dropped.Path}");

            if (!wins)
            {
                continue;
            }

            chosen[entry.Name] = entry;
            from[entry.Name] = candidate.Value;
        }

        List<string> identities = [];
        references = new List<MetadataReference>(chosen.Count);

        foreach (KeyValuePair<string, Entry> pair in chosen)
        {
            references.Add(pair.Value.Reference);
            identities.Add(string.Create(CultureInfo.InvariantCulture, $"{pair.Value.Name}/{pair.Value.Version}"));
        }

        identities.Sort(StringComparer.Ordinal);
        version = ShortHash(string.Join("\n", identities));
    }

    private static string ShortHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash)[..16];
    }

    /// <summary>
    /// Turns a file into a reference plus the identity it is deduplicated on, or <see langword="null"/>
    /// if it cannot be one.
    /// </summary>
    private static Entry? TryRead(string path, bool copyIntoMemory)
    {
        return Cache.GetOrAdd((copyIntoMemory ? "copy:" : "file:") + path, _ =>
        {
            try
            {
                AssemblyMetadata metadata = copyIntoMemory
                    ? AssemblyMetadata.CreateFromImage(File.ReadAllBytes(path))
                    : AssemblyMetadata.CreateFromFile(path);

                // Forces the read. Native and malformed images throw here rather than later.
                ImmutableArray<ModuleMetadata> modules = metadata.GetModules();
                if (modules.Length == 0)
                {
                    return null;
                }

                MetadataReader reader = modules[0].GetMetadataReader();

                // A bare .netmodule has no identity to deduplicate on and cannot be referenced alone.
                if (!reader.IsAssembly)
                {
                    return null;
                }

                AssemblyDefinition definition = reader.GetAssemblyDefinition();

                return new Entry(
                    metadata.GetReference(DocumentationFor(path), filePath: path),
                    reader.GetString(definition.Name),
                    definition.Version,
                    path);
            }
            catch (IOException)
            {
                return null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        });
    }

    /// <summary>
    /// Picks up the XML documentation sitting beside an assembly. This is what puts a library's own
    /// summaries into completion tooltips and signature help inside a code block, which is most of
    /// what makes a third-party DLL pleasant to script against.
    /// </summary>
    private static DocumentationProvider? DocumentationFor(string assemblyPath)
    {
        try
        {
            string xml = Path.ChangeExtension(assemblyPath, ".xml");
            return File.Exists(xml) ? XmlDocumentationProvider.CreateFromFile(xml) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? SafeLocation(Assembly assembly)
    {
        try
        {
            string location = assembly.Location;
            return string.IsNullOrEmpty(location) || !File.Exists(location) ? null : location;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Where a candidate came from, best first.</summary>
    private enum Source
    {
        Framework = 0,
        Loaded = 1,
        Additional = 2,
    }

    private sealed record Entry(MetadataReference Reference, string Name, Version Version, string Path);
}
