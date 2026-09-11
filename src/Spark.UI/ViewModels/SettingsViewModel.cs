using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Spark.Host;

namespace Spark.UI.ViewModels;

/// <summary>
/// The Settings window's model: one place for the preferences Spark remembers between sessions
/// (<c>E8-T12</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A view over preferences that already existed, not a settings system.</b> Each one is a
/// <see cref="PreferenceFile"/> of its own (<c>E8-T59</c>), and that type's remarks say why there is
/// no framework behind it: nothing yet needs a schema, sections or migration. What was missing was a
/// place to find them - the update check was a menu tick, the code font a list in the properties
/// pane, and tidying on <kbd>Enter</kbd> a checkbox beside it.
/// </para>
/// <para>
/// <b>One writer per preference.</b> The code font and the tidy switch go through the main view
/// model's own properties, which are what the properties pane binds, and the update check through
/// the same <see cref="UpdatePreference"/> the Help menu's tick reads. A second instance over the same
/// file would hold a stale answer and skip a write it needed, and two surfaces would disagree about
/// what is remembered.
/// </para>
/// </remarks>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _model;
    private readonly UpdatePreference _updates;

    /// <summary>Creates the settings over the session's own preferences.</summary>
    /// <param name="model">The main view model, which owns the code-block preferences.</param>
    /// <param name="updates">The update-check preference the Help menu also reads.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public SettingsViewModel(MainWindowViewModel model, UpdatePreference updates)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(updates);

        _model = model;
        _updates = updates;
    }

    /// <summary>Whether Spark looks for a newer version when it starts (<c>E12-T21</c>).</summary>
    public bool ChecksForUpdates
    {
        get => _updates.Enabled;

        set
        {
            if (_updates.Enabled == value)
            {
                return;
            }

            _updates.Enabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The faces a code block can be drawn in, the shipped face first.</summary>
    public static IReadOnlyList<string> CodeFontNames => MainWindowViewModel.CodeFontNames;

    /// <summary>The face code blocks are drawn and edited in (<c>E8-T59</c>).</summary>
    /// <remarks>
    /// A face that no longer resolves falls back to the shipped one, so reading this back after
    /// setting it answers what the user got rather than what they clicked.
    /// </remarks>
    public string CodeFontName
    {
        get => _model.CodeFontName;

        set
        {
            if (value is null || value == _model.CodeFontName)
            {
                return;
            }

            _model.CodeFontName = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether pressing <kbd>Enter</kbd> tidies a code block's finished lines (<c>E8-T84</c>).</summary>
    public bool FormatsOnLineBreak
    {
        get => _model.FormatsOnLineBreak;

        set
        {
            if (_model.FormatsOnLineBreak == value)
            {
                return;
            }

            _model.FormatsOnLineBreak = value;
            OnPropertyChanged();
        }
    }
}
