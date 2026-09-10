using System;

namespace Spark.UI.Theming;

/// <summary>
/// Whether code blocks tidy themselves on a line break, for the whole application
/// (<c>E8-T84</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Static for the same reason <see cref="CodeFont"/> is.</b> Every open editor has to agree,
/// and an editor is created and destroyed as the selection moves — so the answer cannot live on
/// one of them. The pane that draws the checkbox and the editors that obey it are different
/// objects with no reference to each other, and this is the thing they share.
/// </para>
/// <para>
/// <b>The file is read once and written on change, not read per keystroke.</b> This is consulted
/// on every <kbd>Enter</kbd> in a code block; going to disk for that would put a file system call
/// on the typing path, which is the sort of thing that is invisible until somebody's machine is
/// busy.
/// </para>
/// <para>
/// <b>On until something says otherwise</b>, including when the preference cannot be read at all.
/// The feature was asked for as the default behaviour, so the fallback is the behaviour rather
/// than the absence of it.
/// </para>
/// </remarks>
public static class CodeFormatting
{
    private static Spark.Host.CodeFormatPreference? _preference;
    private static bool _onLineBreak = true;

    /// <summary>Raised when the setting changes, so open editors can follow it.</summary>
    public static event EventHandler? Changed;

    /// <summary>Whether pressing <kbd>Enter</kbd> tidies the finished lines above the caret.</summary>
    public static bool OnLineBreak
    {
        get => _onLineBreak;

        set
        {
            if (_onLineBreak == value)
            {
                return;
            }

            _onLineBreak = value;
            (_preference ??= new Spark.Host.CodeFormatPreference()).FormatsOnLineBreak = value;

            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Reads the remembered answer, once, at startup.
    /// </summary>
    /// <remarks>
    /// Called beside <c>ApplyRememberedCodeFont</c>, and it has to happen before the first editor
    /// is built — an editor that read the default and never heard otherwise would tidy for a user
    /// who had turned it off, which is worse than the reverse.
    /// </remarks>
    public static void ApplyRemembered()
    {
        _preference ??= new Spark.Host.CodeFormatPreference();
        _onLineBreak = _preference.FormatsOnLineBreak;

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Forgets the loaded preference, so a test can supply its own.</summary>
    /// <param name="preference">The preference to use, or null to read the user's again.</param>
    /// <remarks>
    /// <b>Static state needs a way back to a known one.</b> Without this a test that switched the
    /// setting off would leave it off for every test that ran afterwards in the same process,
    /// which is the failure that only appears when the order changes.
    /// </remarks>
    public static void UseForTesting(Spark.Host.CodeFormatPreference? preference)
    {
        _preference = preference;
        _onLineBreak = preference?.FormatsOnLineBreak ?? true;

        Changed?.Invoke(null, EventArgs.Empty);
    }
}
