using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Spark.Engine;

namespace Spark.Scripting;

/// <summary>How much work a cache has saved. A snapshot; the counters keep moving.</summary>
/// <param name="Compilations">Code blocks Roslyn actually compiled.</param>
/// <param name="ResidentHits">Code blocks answered from the in-memory cache, with no I/O at all.</param>
/// <param name="DiskHits">Code blocks answered from the on-disk cache, with no Roslyn compile.</param>
public readonly record struct ScriptCacheStatistics(int Compilations, int ResidentHits, int DiskHits);

/// <summary>
/// Two levels of compile cache for code blocks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Resident.</b> The same script text with the same input types is compiled once and the loaded
/// assembly is kept. Dragging a slider into a code block changes the input <i>values</i>, never the
/// key, so every tick after the first costs one dictionary probe and a delegate call. That is what
/// makes a code block feel live rather than sluggish, and it is the whole reason Roslyn's cold start
/// is survivable.
/// </para>
/// <para>
/// <b>Persistent.</b> The emitted assembly is also written under
/// <c>%LOCALAPPDATA%/Spark/scriptcache</c>, keyed by content, so reopening a graph tomorrow does not
/// recompile it. The Spark version is part of the directory and the reference catalog's version is
/// part of the key, so upgrading Spark or rebuilding a referenced library invalidates exactly the
/// entries that were compiled against the old one.
/// </para>
/// <para>
/// Ten nodes containing identical text share one key and therefore one compile.
/// </para>
/// <para>
/// <b>This cache owns collectible load contexts.</b> Clearing it disposes them, which drops the
/// delegates first and only then unloads — see <see cref="CompiledScript"/>. Any
/// <see cref="NodeDefinition"/> still holding an invoker keeps that assembly alive regardless; the
/// documented answer to "the old version is still loaded" is to restart, not to insist.
/// </para>
/// </remarks>
public sealed class ScriptCompilationCache
{
    private const string MetadataVersion = "1";

    private readonly ConcurrentDictionary<string, CompiledScript> _resident = new(StringComparer.Ordinal);
    private readonly string? _directory;
    private int _compilations;
    private int _residentHits;
    private int _diskHits;

    /// <summary>Creates a cache.</summary>
    /// <param name="directory">
    /// Where to keep compiled assemblies between sessions. <see langword="null"/> uses the default
    /// location under the user's local application data. Pass <see cref="string.Empty"/> for a cache
    /// that never touches the disk, which is what a test wants.
    /// </param>
    public ScriptCompilationCache(string? directory = null)
    {
        if (directory is null)
        {
            _directory = DefaultDirectory();
            return;
        }

        _directory = directory.Length == 0 ? null : directory;
    }

    /// <summary>
    /// The process-wide cache. Shared deliberately: two graphs open at once containing the same code
    /// block should compile it once between them.
    /// </summary>
    public static ScriptCompilationCache Shared { get; } = new();

    /// <summary>Where compiled assemblies are kept between sessions, or <see langword="null"/> for memory only.</summary>
    public string? Directory => _directory;

    /// <summary>A snapshot of how much work this cache has saved.</summary>
    public ScriptCacheStatistics Statistics => new(
        Volatile.Read(ref _compilations),
        Volatile.Read(ref _residentHits),
        Volatile.Read(ref _diskHits));

    /// <summary>
    /// Drops every cached compilation and unloads the assemblies. Any node definition still holding
    /// an invoker keeps its own assembly alive; see the type-level remarks.
    /// </summary>
    public void Clear()
    {
        foreach (KeyValuePair<string, CompiledScript> entry in _resident)
        {
            if (_resident.TryRemove(entry.Key, out CompiledScript? script))
            {
                script.Dispose();
            }
        }
    }

    internal bool TryGetResident(string key, out CompiledScript? script)
    {
        if (_resident.TryGetValue(key, out CompiledScript? found) && found.IsAlive)
        {
            Interlocked.Increment(ref _residentHits);
            script = found;
            return true;
        }

        script = null;
        return false;
    }

    /// <summary>Loads a previously emitted assembly from disk, or returns <see langword="false"/>.</summary>
    internal bool TryLoadFromDisk(string key, out CompiledScript? script)
    {
        script = null;

        if (_directory is null)
        {
            return false;
        }

        try
        {
            string assemblyPath = Path.Combine(_directory, key + ".dll");
            string symbolsPath = Path.Combine(_directory, key + ".pdb");
            string metadataPath = Path.Combine(_directory, key + ".meta");

            if (!File.Exists(assemblyPath) || !File.Exists(symbolsPath) || !File.Exists(metadataPath))
            {
                return false;
            }

            if (!TryReadMetadata(File.ReadAllLines(metadataPath),
                    out List<PortDefinition> inputs, out List<PortDefinition> outputs))
            {
                return false;
            }

            script = Load(key, File.ReadAllBytes(assemblyPath), File.ReadAllBytes(symbolsPath), inputs, outputs);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }

        if (script is null)
        {
            return false;
        }

        Interlocked.Increment(ref _diskHits);
        _resident[key] = script;
        return true;
    }

    /// <summary>Loads a freshly emitted assembly, records it, and writes it through to disk.</summary>
    internal CompiledScript Store(
        string key,
        byte[] assembly,
        byte[] symbols,
        IReadOnlyList<PortDefinition> inputs,
        IReadOnlyList<PortDefinition> outputs)
    {
        Interlocked.Increment(ref _compilations);

        CompiledScript script = Load(key, assembly, symbols, inputs, outputs);
        _resident[key] = script;
        WriteThrough(key, assembly, symbols, inputs, outputs);

        return script;
    }

    private static CompiledScript Load(
        string key,
        byte[] assembly,
        byte[] symbols,
        IReadOnlyList<PortDefinition> inputs,
        IReadOnlyList<PortDefinition> outputs)
    {
        ScriptLoadContext context = new("SparkCodeBlock-" + key[..Math.Min(12, key.Length)]);

        using MemoryStream assemblyStream = new(assembly, writable: false);
        using MemoryStream symbolStream = new(symbols, writable: false);

        Assembly loaded = context.LoadFromStream(assemblyStream, symbolStream);

        Type type = loaded.GetType(ScriptRewriter.GeneratedTypeName, throwOnError: true)
            ?? throw new BadImageFormatException($"A compiled code block has no {ScriptRewriter.GeneratedTypeName}.");

        MethodInfo method = type.GetMethod(
                ScriptRewriter.GeneratedMethodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new BadImageFormatException($"A compiled code block has no {ScriptRewriter.GeneratedMethodName}.");

        Func<object[], object[]> run =
            (Func<object[], object[]>)method.CreateDelegate(typeof(Func<object[], object[]>));

        return new CompiledScript(key, context, run, inputs, outputs);
    }

    private void WriteThrough(
        string key,
        byte[] assembly,
        byte[] symbols,
        IReadOnlyList<PortDefinition> inputs,
        IReadOnlyList<PortDefinition> outputs)
    {
        if (_directory is null)
        {
            return;
        }

        try
        {
            System.IO.Directory.CreateDirectory(_directory);

            WriteAtomic(Path.Combine(_directory, key + ".dll"), assembly);
            WriteAtomic(Path.Combine(_directory, key + ".pdb"), symbols);
            WriteAtomic(Path.Combine(_directory, key + ".meta"), Encoding.UTF8.GetBytes(WriteMetadata(inputs, outputs)));
        }
        catch (IOException)
        {
            // A cache that cannot be written is slower, not broken.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteAtomic(string path, byte[] content)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    private static string WriteMetadata(IReadOnlyList<PortDefinition> inputs, IReadOnlyList<PortDefinition> outputs)
    {
        StringBuilder builder = new();
        builder.Append(MetadataVersion).Append('\n');

        foreach (PortDefinition port in inputs)
        {
            AppendPort(builder, "in", port);
        }

        foreach (PortDefinition port in outputs)
        {
            AppendPort(builder, "out", port);
        }

        return builder.ToString();
    }

    private static void AppendPort(StringBuilder builder, string kind, PortDefinition port)
    {
        builder.Append(kind).Append('\t')
            .Append(port.Name).Append('\t')
            .Append(port.ValueType.AssemblyQualifiedName ?? "System.Object").Append('\t')
            .Append(EncodeValue(port.DefaultValue)).Append('\n');
    }

    private static bool TryReadMetadata(
        string[] lines, out List<PortDefinition> inputs, out List<PortDefinition> outputs)
    {
        inputs = [];
        outputs = [];

        if (lines.Length == 0 || !string.Equals(lines[0].Trim(), MetadataVersion, StringComparison.Ordinal))
        {
            return false;
        }

        for (int index = 1; index < lines.Length; index++)
        {
            if (lines[index].Length == 0)
            {
                continue;
            }

            string[] parts = lines[index].Split('\t');
            if (parts.Length != 4)
            {
                return false;
            }

            Type? type = TypeNames.FromAssemblyQualifiedName(parts[2]);
            if (type is null)
            {
                // A port type that no longer resolves means the cached ports are not trustworthy.
                return false;
            }

            PortDefinition port = new(
                parts[1], type, PortDefinition.RankOfType(type), defaultValue: DecodeValue(parts[3]));

            if (string.Equals(parts[0], "in", StringComparison.Ordinal))
            {
                inputs.Add(port);
            }
            else
            {
                outputs.Add(port);
            }
        }

        return outputs.Count > 0;
    }

    private static string EncodeValue(object? value) => value switch
    {
        null => "-",
        double number => "d:" + number.ToString("R", CultureInfo.InvariantCulture),
        float number => "f:" + number.ToString("R", CultureInfo.InvariantCulture),
        decimal number => "m:" + number.ToString(CultureInfo.InvariantCulture),
        int number => "i:" + number.ToString(CultureInfo.InvariantCulture),
        long number => "l:" + number.ToString(CultureInfo.InvariantCulture),
        bool flag => "b:" + (flag ? "true" : "false"),
        string text => "s:" + Uri.EscapeDataString(text),
        char character => "c:" + Uri.EscapeDataString(character.ToString()),
        _ => "-",
    };

    private static object? DecodeValue(string encoded)
    {
        if (encoded.Length < 2 || encoded[1] != ':')
        {
            return null;
        }

        string body = encoded[2..];

        return encoded[0] switch
        {
            'd' => double.Parse(body, CultureInfo.InvariantCulture),
            'f' => float.Parse(body, CultureInfo.InvariantCulture),
            'm' => decimal.Parse(body, CultureInfo.InvariantCulture),
            'i' => int.Parse(body, CultureInfo.InvariantCulture),
            'l' => long.Parse(body, CultureInfo.InvariantCulture),
            'b' => string.Equals(body, "true", StringComparison.Ordinal),
            's' => Uri.UnescapeDataString(body),
            'c' => Uri.UnescapeDataString(body) is { Length: > 0 } text ? text[0] : null,
            _ => null,
        };
    }

    private static string DefaultDirectory()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrEmpty(root))
        {
            root = Path.GetTempPath();
        }

        return Path.Combine(root, "Spark", "scriptcache", SparkVersion());
    }

    /// <summary>
    /// The Spark build the cache belongs to. Part of the directory rather than the key, so an upgrade
    /// leaves the old cache behind in one place that can be deleted whole.
    /// </summary>
    internal static string SparkVersion()
    {
        string version = typeof(ScriptCompilationCache).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(ScriptCompilationCache).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        StringBuilder safe = new(version.Length);
        foreach (char character in version)
        {
            safe.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' ? character : '_');
        }

        return safe.ToString();
    }
}
