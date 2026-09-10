using System;
using System.Globalization;

namespace Spark.Host;

/// <summary>
/// Whether a code block tidies itself when the typist presses <kbd>Enter</kbd>, remembered
/// between sessions (<c>E8-T84</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>One setting for the whole application, not one per block</b>, on
/// <see cref="CodeFontPreference"/>'s reasoning and for the same gain: it is a fact about how
/// somebody likes to type, not about the document, so no <c>.spark</c> field carries it and
/// <c>E7-T7</c>'s promise that a graph round-trips byte for byte is untouched.
/// </para>
/// <para>
/// <b>On by default, and that is the direction the failures go too.</b> Asked for that way. Unset
/// means on, and so does an unreadable file — the whole point of the setting is that the tidying
/// happens without being asked for, so a machine that cannot read its preferences should still
/// behave the way the client described rather than falling back to inert.
/// </para>
/// <para>
/// <b>Stored as the word rather than as a flag</b>, because a person may well open this file to
/// see what it says, and <c>off</c> answers that question where <c>0</c> raises another one.
/// Anything that is not <c>off</c> reads as on, which is the same asymmetry as the paragraph
/// above.
/// </para>
/// </remarks>
public sealed class CodeFormatPreference
{
    /// <summary>The file the answer lives in, beside the other remembered choices.</summary>
    public const string FileName = "code-format-on-newline.txt";

    /// <summary>What the file holds when the tidying is switched off.</summary>
    private const string Off = "off";

    /// <summary>And when it is on. Written explicitly, so the file is never ambiguous.</summary>
    private const string On = "on";

    private readonly PreferenceFile _file;
    private bool _formatsOnLineBreak;

    /// <summary>Reads the preference from the user's local application data.</summary>
    public CodeFormatPreference()
        : this(PreferenceFile.Named(FileName))
    {
    }

    /// <summary>Reads the preference from a chosen file.</summary>
    /// <param name="file">Where the answer lives.</param>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
    public CodeFormatPreference(PreferenceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        _file = file;
        _formatsOnLineBreak = !string.Equals(file.Read(), Off, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether pressing <kbd>Enter</kbd> in a code block tidies what is above the caret.</summary>
    public bool FormatsOnLineBreak
    {
        get => _formatsOnLineBreak;

        set
        {
            if (_formatsOnLineBreak == value)
            {
                return;
            }

            _formatsOnLineBreak = value;
            _file.Write(value ? On : Off);
        }
    }

    /// <summary>Where the preference is kept, or null when it is not kept anywhere.</summary>
    public string? Path => _file.Path;

    /// <summary>The stored word, for a caller that wants to show it.</summary>
    /// <returns><c>on</c> or <c>off</c>.</returns>
    public override string ToString() =>
        (_formatsOnLineBreak ? On : Off).ToString(CultureInfo.InvariantCulture);
}
