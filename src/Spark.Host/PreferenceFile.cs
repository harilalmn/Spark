using System;
using System.IO;

namespace Spark.Host;

/// <summary>
/// One remembered answer, in one small file under the user's local application data
/// (<c>E8-T59</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because <see cref="UpdatePreference"/> said it should.</b> Its own remarks read:
/// <i>"a configuration system introduced for one boolean is a configuration system nobody
/// designed. When there is a second persisted preference, that is the moment to build the thing
/// that holds both — not before."</i> The code block's font is the second one, so this is that
/// moment, and it is deliberately the smallest thing that answers it.
/// </para>
/// <para>
/// <b>Still a file holding one line, and not a settings framework.</b> What is shared is the part
/// that was about to be copied for the third time: where the file goes, what counts as a
/// recoverable failure, and that every failure is silent. What is *not* shared is any notion of
/// schema, sections, migration or change notification — none of those have a second caller either,
/// and inventing them here would be the mistake the remark warns about, one level up.
/// </para>
/// <para>
/// <b>Every failure is silent, and the caller decides what silence means.</b> The directory may be
/// read-only, the disk full, the file half-written by a process that was killed. This returns null
/// and the caller falls back to its own default — which is on for the update check and the shipped
/// face for the font, and in both cases the answer that works rather than the answer that is
/// safest to store.
/// </para>
/// </remarks>
public sealed class PreferenceFile
{
    private readonly string? _path;

    private PreferenceFile(string? path) => _path = path;

    /// <summary>A preference kept in the standard place under a chosen file name.</summary>
    /// <param name="fileName">The file's name, such as <c>code-font.txt</c>.</param>
    /// <returns>The preference.</returns>
    /// <remarks>
    /// <b>Two named factories rather than two constructors</b>, because both take a string and
    /// <i>a name</i> and <i>a path</i> are not the same argument. An overload pair here would
    /// compile whichever one the caller did not mean.
    /// </remarks>
    public static PreferenceFile Named(string fileName) => new(DefaultPath(fileName));

    /// <summary>A preference at an explicit path, which is what a test wants.</summary>
    /// <param name="path">The file, or null to keep the answer in memory only.</param>
    /// <returns>The preference.</returns>
    public static PreferenceFile At(string? path) => new(path);

    /// <summary>Where the answer is kept, or null when it is not kept anywhere.</summary>
    public string? Path => _path;

    /// <summary>The remembered answer, or null when there is not one.</summary>
    /// <returns>The single line the file holds, trimmed, or null.</returns>
    public string? Read()
    {
        if (_path is null || !File.Exists(_path))
        {
            return null;
        }

        try
        {
            string text = File.ReadAllText(_path).Trim();

            return text.Length > 0 ? text : null;
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            return null;
        }
    }

    /// <summary>Remembers an answer, or forgets it.</summary>
    /// <param name="value">The answer, or null to remove the file.</param>
    /// <remarks>
    /// <b>Written immediately rather than on exit.</b> A preference that only persisted on a clean
    /// shutdown would be lost by exactly the crash that made the user change it.
    /// </remarks>
    public void Write(string? value)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            if (value is null)
            {
                File.Delete(_path);
                return;
            }

            string? directory = System.IO.Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, value);
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            // The choice still holds for this session; it is only not remembered for the next.
        }
    }

    /// <summary>The standard location for a named preference file.</summary>
    private static string? DefaultPath(string fileName)
    {
        try
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            return string.IsNullOrEmpty(root)
                ? null
                : System.IO.Path.Combine(root, "Spark", fileName);
        }
        catch (Exception failure) when (IsRecoverable(failure))
        {
            return null;
        }
    }

    /// <summary>
    /// The failures a preference recovers from by forgetting rather than by throwing.
    /// </summary>
    /// <remarks>
    /// Deliberately a list rather than <c>catch (Exception)</c>: a
    /// <see cref="NullReferenceException"/> here is a defect in this class and should reach a
    /// developer, while a read-only directory is a Tuesday.
    /// </remarks>
    private static bool IsRecoverable(Exception failure) =>
        failure is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException;
}
