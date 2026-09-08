using System;

namespace Spark.Host;

/// <summary>
/// Which face code blocks are drawn in, remembered between sessions (<c>E8-T59</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>One setting for the whole application, not one per block.</b> The client chose this: a font
/// is about the person reading, not about the document. It also keeps the <c>.spark</c> format
/// untouched, which matters more than it sounds — <c>E7-T7</c> promises a graph survives a round
/// trip byte for byte, and a per-block font would have been a new field on every node to keep that
/// promise about.
/// </para>
/// <para>
/// <b>The stored value is a family name and nothing else.</b> Not a path, not an index into a list
/// — a machine can gain and lose fonts between sessions, and a name that no longer resolves falls
/// back to the shipped face rather than to whatever now sits at position four.
/// </para>
/// <para>
/// <b>Unset means the shipped face, and so does unreadable.</b> Spark bundles Source Code Pro
/// precisely so there is an answer that works on every machine, so failing towards it is failing
/// towards the thing the client asked for.
/// </para>
/// </remarks>
public sealed class CodeFontPreference
{
    /// <summary>The file the answer lives in, beside the other remembered choices.</summary>
    public const string FileName = "code-font.txt";

    private readonly PreferenceFile _file;
    private string? _family;

    /// <summary>Reads the preference from the user's local application data.</summary>
    public CodeFontPreference()
        : this(PreferenceFile.Named(FileName))
    {
    }

    /// <summary>Reads the preference from a chosen file.</summary>
    /// <param name="file">Where the answer lives.</param>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
    public CodeFontPreference(PreferenceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        _file = file;
        _family = file.Read();
    }

    /// <summary>
    /// The chosen family, or null when the user has not chosen one and the shipped face applies.
    /// </summary>
    /// <remarks>
    /// <b>Null rather than the default name, and the difference is not pedantic.</b> Storing the
    /// default would freeze it: a user who never touched the setting would be pinned to whatever
    /// the default was on the day they first ran Spark, and would not follow it if it ever
    /// changed. Unset has to stay unset.
    /// </remarks>
    public string? Family
    {
        get => _family;

        set
        {
            string? chosen = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

            if (string.Equals(_family, chosen, StringComparison.Ordinal))
            {
                return;
            }

            _family = chosen;
            _file.Write(chosen);
        }
    }

    /// <summary>Where the preference is kept, or null when it is not kept anywhere.</summary>
    public string? Path => _file.Path;
}
