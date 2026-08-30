using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Spark.Scripting;

/// <summary>
/// What a compiled script needs on the next run: the assembly, and the port names that came out of
/// compiling it (`E6-T10`).
/// </summary>
/// <param name="Assembly">The emitted assembly's bytes.</param>
/// <param name="Inputs">The input port names, in port order.</param>
public readonly record struct CachedScript(byte[] Assembly, IReadOnlyList<string> Inputs);

/// <summary>
/// The on-disk half of the compile cache: a script that has been compiled once is not compiled
/// again, in this run or in any later one (`E6-T10`).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this buys is the reopen.</b> The resident cache (`E6-T9`) already makes a slider feeding
/// a code block feel live, because the assembly is in memory. It does nothing for the case that
/// actually annoys people: opening a saved graph with ten code blocks in it and waiting for Roslyn
/// to start, twice per block — once to infer the ports and once to emit.
/// </para>
/// <para>
/// <b>The resident key cannot be the disk key, and the reason is worth stating.</b> The resident
/// key carries <see cref="ReferenceCatalog.Version"/>, which is a counter of how many times the
/// catalogue has changed *in this process* — it is 0 in every fresh one. Two different sets of
/// references would therefore share a cache entry across runs. The disk key carries
/// <see cref="ReferenceCatalog.Fingerprint"/> instead, which is derived from the reference files
/// themselves and means the same thing tomorrow.
/// </para>
/// <para>
/// <b>Only the input names are stored beside the assembly.</b> Everything else is re-derivable
/// without a compiler: the output ports come from the script's syntax, and an input's type is
/// whatever the graph has wired into it — which the caller already knows, and which is part of the
/// key, so a cached entry cannot be read back under different types. The input *names* are the one
/// thing that took a compilation to learn (`E6-T5`), which is exactly why they are the one thing
/// written down.
/// </para>
/// <para>
/// <b>Every failure here is silent and falls back to compiling.</b> A cache that throws is worse
/// than no cache: the directory may be read-only, the disk full, the file half-written by a process
/// that was killed, or the entry left by a build of Spark that generated a different frame. All of
/// those have the same correct answer — compile it.
/// </para>
/// </remarks>
public sealed class ScriptAssemblyCache
{
    /// <summary>
    /// The shape of the generated frame, in the key.
    /// </summary>
    /// <remarks>
    /// <b>Bump this whenever the generated source changes</b> — a new guard, a different input
    /// declaration, another wrapper line. Without it, an old entry compiled against the previous
    /// frame is loaded by a new build and behaves like the code that build no longer writes, which
    /// is the hardest class of bug this cache could produce.
    /// </remarks>
    public const int GeneratorVersion = 1;

    private readonly string? _directory;

    /// <summary>Creates a cache under the user's local application data.</summary>
    public ScriptAssemblyCache() : this(DefaultDirectory())
    {
    }

    /// <summary>Creates a cache in a chosen directory.</summary>
    /// <param name="directory">Where entries live, or null to disable the cache entirely.</param>
    /// <remarks>
    /// Null is a supported state rather than an error: a host with nowhere to write — a sandbox, a
    /// build agent, a session started with a read-only profile — should still run scripts.
    /// </remarks>
    public ScriptAssemblyCache(string? directory) => _directory = directory;

    /// <summary>Whether anything will actually be written.</summary>
    public bool IsEnabled => _directory is not null;

    /// <summary>Where entries are kept, or null when the cache is off.</summary>
    public string? Directory => _directory;

    /// <summary>Reads a compiled script back, if it is there and readable.</summary>
    /// <param name="key">The compile key.</param>
    /// <param name="cached">The assembly and its input port names.</param>
    /// <returns>True when the entry was read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public bool TryRead(string key, out CachedScript cached)
    {
        ArgumentNullException.ThrowIfNull(key);

        cached = default;

        if (_directory is null)
        {
            return false;
        }

        try
        {
            string assembly = Path.Combine(_directory, key + ".dll");
            string ports = Path.Combine(_directory, key + ".ports");

            if (!File.Exists(assembly) || !File.Exists(ports))
            {
                return false;
            }

            // The ports file is read first and on purpose: it is written *after* the assembly, so
            // its presence is what says the pair is complete. A process killed between the two
            // leaves a .dll with no .ports, which is correctly treated as a miss.
            string[] names = File.ReadAllLines(ports);
            byte[] bytes = File.ReadAllBytes(assembly);

            if (bytes.Length == 0)
            {
                return false;
            }

            cached = new CachedScript(bytes, [.. names]);

            return true;
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            return false;
        }
    }

    /// <summary>Writes a compiled script for the next run.</summary>
    /// <param name="key">The compile key.</param>
    /// <param name="assembly">The emitted assembly's bytes.</param>
    /// <param name="inputs">The input port names, in port order.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// <b>Written through a temporary file and moved into place</b>, so a reader never sees a
    /// half-written assembly — and the ports file is moved last, because it is what a reader checks
    /// for.
    /// </remarks>
    public void Write(string key, byte[] assembly, IReadOnlyList<string> inputs)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(inputs);

        if (_directory is null)
        {
            return;
        }

        try
        {
            System.IO.Directory.CreateDirectory(_directory);

            string assemblyPath = Path.Combine(_directory, key + ".dll");
            string portsPath = Path.Combine(_directory, key + ".ports");
            string scratch = Path.Combine(_directory, key + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

            File.WriteAllBytes(scratch, assembly);
            File.Move(scratch, assemblyPath, overwrite: true);

            File.WriteAllLines(scratch, inputs);
            File.Move(scratch, portsPath, overwrite: true);
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            // Nothing to do and nothing to report: the script has already compiled, and the only
            // thing lost is a faster start next time.
        }
    }

    /// <summary>The default cache directory, or null when there is nowhere sensible to write.</summary>
    private static string? DefaultDirectory()
    {
        try
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            return string.IsNullOrEmpty(root)
                ? null
                : Path.Combine(root, "Spark", "script-cache", GeneratorVersion.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            return null;
        }
    }

    private static bool IsRecoverable(Exception failure) =>
        failure is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException;

    /// <summary>A file-name-safe key from the parts that decide what a script compiles to.</summary>
    /// <param name="parts">The parts, in a fixed order.</param>
    /// <returns>A hexadecimal name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parts"/> is null.</exception>
    public static string Key(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\u0000', parts)));

        return Convert.ToHexString(hash);
    }
}
