using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Spark.Host;
using Spark.UI.Theming;

namespace Spark.UI.Tests;

/// <summary>
/// The code font is a setting: one for the application, remembered between sessions — `E8-T59`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The client chose application-wide over per-block.</b> A font is about the person reading
/// rather than the document, and it leaves the <c>.spark</c> format alone — `E7-T7` promises a
/// graph survives a round trip byte for byte, and a per-block font would have been a new field on
/// every node to keep that promise about.
/// </para>
/// <para>
/// <b>The tests that matter here are the fallbacks.</b> A machine can lose a font between
/// sessions and a font list can contain a face that would break node sizing; both of those are
/// silent, and both are the reason this is not simply a string in a file.
/// </para>
/// </remarks>
public sealed class CodeFontSettingTests : IDisposable
{
    /// <summary>Every test leaves the shipped face selected, because the setting is static.</summary>
    public void Dispose() => CodeFont.UseDefault();

    /// <summary>Fresh, with nothing remembered, the shipped face is the answer.</summary>
    [Fact]
    public void TheDefaultIsTheShippedFace()
    {
        CodeFont.UseDefault();

        Assert.Equal(CodeFont.Name, CodeFont.Current);
        Assert.Equal(CodeFont.DefaultFamily, CodeFont.FontFamily.ToString());
    }

    /// <summary>
    /// <b>The shipped face is first in the list</b>, because it is the answer that works on every
    /// machine and a dropdown's first entry is the one people take.
    /// </summary>
    [Fact]
    public void TheShippedFaceLeadsTheList() => HeadlessSession.Run(() =>
    {
        IReadOnlyList<string> available = CodeFont.Available();

        Assert.Equal(CodeFont.Name, available[0]);
        Assert.Single(available, name => name == CodeFont.Name);
    });

    /// <summary>
    /// <b>Only monospaced faces are offered.</b> Node width is <i>characters × one character's
    /// width</i>, so a proportional face would draw a block's source over its own port tabs — the
    /// defect `E8-T58` fixed once, and one a bad setting could reintroduce everywhere at once.
    /// </summary>
    /// <remarks>
    /// Inter is the application's own UI face and is certainly present, which makes it the one
    /// proportional font this can name without depending on what a machine happens to have.
    /// </remarks>
    [Fact]
    public void AProportionalFaceIsNotOffered() => HeadlessSession.Run(() =>
    {
        Assert.DoesNotContain("Inter", CodeFont.Available());

        foreach (string name in CodeFont.Available())
        {
            Assert.True(
                Monospaced(name),
                $"'{name}' was offered as a code font and is not monospaced");
        }
    });

    /// <summary>
    /// <b>A remembered name that no longer resolves falls back to the shipped face.</b> A machine
    /// can lose a font between sessions, and the alternative is every code block drawn in
    /// Avalonia's fallback with nothing saying why.
    /// </summary>
    [Fact]
    public void AFontThatIsNoLongerInstalledFallsBack() => HeadlessSession.Run(() =>
    {
        CodeFont.Use("A Font Nobody Has Installed");

        Assert.Equal(CodeFont.Name, CodeFont.Current);
        Assert.Equal(CodeFont.DefaultFamily, CodeFont.FontFamily.ToString());
    });

    /// <summary>Choosing a face that <i>is</i> there takes, and reports itself.</summary>
    [Fact]
    public void AnInstalledFaceIsTaken() => HeadlessSession.Run(() =>
    {
        if (CodeFont.Available().FirstOrDefault(name => name != CodeFont.Name) is not { } other)
        {
            // A machine with no monospaced font but the shipped one. Nothing to assert, and
            // skipping quietly is better than an assertion that depends on the machine.
            return;
        }

        CodeFont.Use(other);

        Assert.Equal(other, CodeFont.Current);
    });

    /// <summary>
    /// <b>The advance ratio follows the chosen face.</b> This is what keeps node sizing right when
    /// the face is not the shipped one: the offered faces are all monospaced and they are not all
    /// the same width.
    /// </summary>
    [Fact]
    public void TheAdvanceRatioFollowsTheChosenFace() => HeadlessSession.Run(() =>
    {
        CodeFont.UseDefault();

        Assert.Equal(CodeFont.AdvanceRatio, CodeFont.CurrentAdvanceRatio, 2);

        foreach (string name in CodeFont.Available().Where(name => name != CodeFont.Name))
        {
            CodeFont.Use(name);

            Assert.True(
                CodeFont.CurrentAdvanceRatio is > 0.3 and < 1.2,
                $"'{name}' measured {CodeFont.CurrentAdvanceRatio} per character, which is not a code font");
        }
    });

    /// <summary>Changing the face announces it, because three surfaces have to redraw.</summary>
    [Fact]
    public void ChangingTheFaceIsAnnounced() => HeadlessSession.Run(() =>
    {
        if (CodeFont.Available().FirstOrDefault(name => name != CodeFont.Name) is not { } other)
        {
            return;
        }

        CodeFont.UseDefault();

        int changes = 0;
        void Count(object? sender, EventArgs e) => changes++;

        CodeFont.Changed += Count;

        try
        {
            CodeFont.Use(other);
            Assert.Equal(1, changes);

            // Choosing the same face again is not a change, or every surface would redraw for
            // nothing each time the dropdown was opened and closed.
            CodeFont.Use(other);
            Assert.Equal(1, changes);
        }
        finally
        {
            CodeFont.Changed -= Count;
        }
    });

    /// <summary>The choice survives a restart, which is the whole point of remembering it.</summary>
    [Fact]
    public void TheChoiceIsRemembered()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            CodeFontPreference first = new(PreferenceFile.At(path));

            Assert.Null(first.Family);

            first.Family = "Consolas";

            Assert.Equal("Consolas", new CodeFontPreference(PreferenceFile.At(path)).Family);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// <b>Going back to the default forgets the setting rather than storing its name.</b> Storing
    /// it would pin a user who never chose anything to whatever the default was the day they first
    /// ran Spark.
    /// </summary>
    [Fact]
    public void ChoosingTheDefaultForgetsIt()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            CodeFontPreference preference = new(PreferenceFile.At(path)) { Family = "Consolas" };

            preference.Family = null;

            Assert.Null(preference.Family);
            Assert.False(File.Exists(path), "the preference file outlived the preference");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A preference with nowhere to write still works for the session.</summary>
    [Fact]
    public void NowhereToWriteIsNotAnError()
    {
        CodeFontPreference preference = new(PreferenceFile.At(null)) { Family = "Consolas" };

        Assert.Equal("Consolas", preference.Family);
        Assert.Null(preference.Path);
    }

    private static bool Monospaced(string name)
    {
        FontFamily family = name == CodeFont.Name ? new FontFamily(CodeFont.DefaultFamily) : new FontFamily(name);

        double narrow = Width("i", family);
        double wide = Width("W", family);

        return narrow > 0 && Math.Abs(narrow - wide) < 0.01;
    }

    private static double Width(string text, FontFamily family) =>
        new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(family),
            12,
            Brushes.Black).WidthIncludingTrailingWhitespace;
}
