using System;
using System.IO;

namespace Spark.Host;

/// <summary>
/// Whether the user wants Spark to look for updates, remembered between sessions
/// (<c>E12-T21</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>On by default, and off is remembered.</b> An update nobody hears about is an update nobody
/// installs, so the default has to be on for the feature to be worth building — and a user who
/// turns it off has said something, so it stays off without being asked again. That is the same
/// shape <see cref="ScriptTrustStore"/> uses and it lives beside it on disk.
/// </para>
/// <para>
/// <b>A file holding one word, rather than a settings framework.</b> There is exactly one setting
/// and this is the whole of it; a configuration system introduced for one boolean is a
/// configuration system nobody designed. When there is a second persisted preference, that is the
/// moment to build the thing that holds both — not before.
/// </para>
/// <para>
/// <b>Nowhere to write means on, not off.</b> A machine with no writable local application data
/// gets a working update check that forgets the answer, rather than a silently disabled one:
/// failing towards "the user hears about a security fix" is the safe direction here, and it is the
/// opposite direction from the trust store, where failing towards "ask again" is safe.
/// </para>
/// </remarks>
public sealed class UpdatePreference
{
    private const string Off = "off";

    private readonly string? _path;
    private bool _enabled = true;

    /// <summary>Reads the preference from the user's local application data.</summary>
    public UpdatePreference()
        : this(DefaultPath())
    {
    }

    /// <summary>Reads the preference from a chosen file.</summary>
    /// <param name="path">Where the answer lives, or null to keep it in memory only.</param>
    public UpdatePreference(string? path)
    {
        _path = path;

        Read();
    }

    /// <summary>Whether Spark should look for updates.</summary>
    /// <remarks>
    /// Setting this writes immediately. A preference that only persisted on a clean exit would be
    /// lost by exactly the crash that made the user turn it off.
    /// </remarks>
    public bool Enabled
    {
        get => _enabled;

        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            Write();
        }
    }

    /// <summary>Where the preference is kept, or null when it is not kept anywhere.</summary>
    public string? Path => _path;

    private void Read()
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        try
        {
            _enabled = !string.Equals(
                File.ReadAllText(_path).Trim(), Off, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            // Unreadable is treated as unset, which is on. See the remarks on this type.
            _enabled = true;
        }
    }

    private void Write()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            string? directory = System.IO.Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, _enabled ? "on" : Off);
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            // The choice still holds for this session; it is only not remembered for the next.
        }
    }

    private static string? DefaultPath()
    {
        try
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            return string.IsNullOrEmpty(root)
                ? null
                : System.IO.Path.Combine(root, "Spark", "update-check.txt");
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
}
