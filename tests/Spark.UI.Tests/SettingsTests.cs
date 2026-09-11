using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spark.Host;
using Spark.UI.Theming;
using Spark.UI.ViewModels;
using Spark.UI.Views;

namespace Spark.UI.Tests;

/// <summary>
/// Runs alone: these tests change the code font and the tidy switch, which are application-wide
/// statics that code-block tests elsewhere read while they run.
/// </summary>
[CollectionDefinition(nameof(ApplicationSettingsCollection), DisableParallelization = true)]
public sealed class ApplicationSettingsCollection
{
}

/// <summary>
/// Settings: one window over the preferences Spark remembers (`E8-T12`).
/// </summary>
/// <remarks>
/// <b>Every preference here is pointed at a temporary file</b>, so a test run never changes what the
/// person running it has chosen. The claim under test is the same for each: a change made in Settings
/// is written at once, a fresh read of the same file sees it, and the surface that already showed the
/// setting follows.
/// </remarks>
[Collection(nameof(ApplicationSettingsCollection))]
public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "spark-settings-" + Path.GetRandomFileName());

    private string File(string name) => Path.Combine(_folder, name);

    /// <summary>Puts the statics back and removes the files.</summary>
    public void Dispose()
    {
        CodeFont.UseDefault();
        CodeFormatting.UseForTesting(null);

        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// <b>The update check</b>: turned off in Settings, and a fresh read of the same file says off.
    /// </summary>
    [Fact]
    public void TurningTheUpdateCheckOffIsRemembered()
    {
        using MainWindowViewModel model = new();
        UpdatePreference updates = new(File("update-check.txt"));
        SettingsViewModel settings = new(model, updates);

        Assert.True(settings.ChecksForUpdates);

        settings.ChecksForUpdates = false;

        Assert.False(updates.Enabled);
        Assert.False(new UpdatePreference(File("update-check.txt")).Enabled);
    }

    /// <summary>
    /// <b>Tidying on Enter</b>: switched off in Settings, remembered, and the properties pane - which
    /// binds the main view model - is told.
    /// </summary>
    [Fact]
    public void TheTidySwitchIsRememberedAndThePaneFollows()
    {
        CodeFormatting.UseForTesting(new CodeFormatPreference(PreferenceFile.At(File("format.txt"))));

        using MainWindowViewModel model = new();
        SettingsViewModel settings = new(model, new UpdatePreference(File("update-check.txt")));

        List<string?> changed = [];
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.FormatsOnLineBreak = false;

        Assert.False(CodeFormatting.OnLineBreak);
        Assert.False(new CodeFormatPreference(PreferenceFile.At(File("format.txt"))).FormatsOnLineBreak);
        Assert.Contains(nameof(MainWindowViewModel.FormatsOnLineBreak), changed);
    }

    /// <summary>
    /// <b>The code font</b>: chosen in Settings, remembered, and the properties pane is told.
    /// </summary>
    [Fact]
    public void AChosenFontIsRememberedAndThePaneFollows() => HeadlessSession.Run(() =>
    {
        CodeFont.UseDefault();

        using MainWindowViewModel model = new();
        model.UseCodeFontPreference(new CodeFontPreference(PreferenceFile.At(File("code-font.txt"))));
        SettingsViewModel settings = new(model, new UpdatePreference(File("update-check.txt")));

        if (SettingsViewModel.CodeFontNames.FirstOrDefault(name => name != CodeFont.Name) is not { } other)
        {
            // A machine whose only monospaced face is the shipped one: nothing to choose between.
            return;
        }

        List<string?> changed = [];
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.CodeFontName = other;

        Assert.Equal(other, CodeFont.Current);
        Assert.Equal(other, new CodeFontPreference(PreferenceFile.At(File("code-font.txt"))).Family);
        Assert.Contains(nameof(MainWindowViewModel.CodeFontName), changed);
    });

    /// <summary>
    /// <b>The window writes through the same settings.</b> Its controls start at what is remembered,
    /// and changing one changes the preference behind it.
    /// </summary>
    [Fact]
    public void TheWindowShowsAndChangesTheRememberedSettings() => HeadlessSession.Run(() =>
    {
        CodeFormatting.UseForTesting(new CodeFormatPreference(PreferenceFile.At(File("format.txt"))));

        using MainWindowViewModel model = new();
        UpdatePreference updates = new(File("update-check.txt"));
        SettingsViewModel settings = new(model, updates);

        SettingsWindow window = new(settings);

        // Closed however the test ends: a window left open by a failing assertion was seen to take
        // the next test in this collection down with it.
        try
        {
            Assert.True(window.UpdatesSetting.IsChecked);
            Assert.True(window.TidySetting.IsChecked);
            Assert.Equal(settings.CodeFontName, window.CodeFontSetting.SelectedItem);

            window.UpdatesSetting.IsChecked = false;
            window.TidySetting.IsChecked = false;

            Assert.False(updates.Enabled);
            Assert.False(CodeFormatting.OnLineBreak);
        }
        finally
        {
            window.Close();
        }
    });
}
