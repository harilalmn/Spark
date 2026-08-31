using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spark.UI.Tests;

/// <summary>
/// The two accessibility claims that can be checked mechanically (<c>E12-T13</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every gesture reachable without a mouse, and every control named.</b> Those are the bar this
/// pass set itself, and both are properties of the markup rather than matters of taste, which is
/// what makes them testable at all.
/// </para>
/// <para>
/// <b>What these tests cannot do is tell you the application is accessible.</b> No screen reader
/// was run against it — none is available here — so what is asserted is that a name exists and is
/// not the visual label repeated. Whether it reads well aloud is a judgement a person makes with a
/// screen reader running, and it has not been made.
/// </para>
/// <para>
/// <b>The markup is read as text rather than through Avalonia.</b> Instantiating the window to walk
/// its visual tree needs a dispatcher and gives back only the controls that have been realised;
/// the file is the whole truth and it is the thing a future edit changes.
/// </para>
/// </remarks>
public sealed class AccessibilityTests
{
    private static readonly string Markup = ReadMarkup();

    /// <summary>
    /// <b>Every toolbar button carries an automation name.</b> Without one a screen reader reads
    /// the button's content, which for <c>Align ▾</c> is a word and a caret, and for a button whose
    /// content is an icon would be nothing at all.
    /// </summary>
    [Fact]
    public void EveryToolbarButtonIsNamed()
    {
        List<string> unnamed = [];

        foreach (Match button in Regex.Matches(Markup, @"<Button\b[^>]*?/>|<Button\b[^>]*?>", RegexOptions.Singleline))
        {
            if (!button.Value.Contains("Classes=\"toolbar\"", StringComparison.Ordinal))
            {
                continue;
            }

            if (!button.Value.Contains("AutomationProperties.Name=", StringComparison.Ordinal))
            {
                unnamed.Add(Content(button.Value));
            }
        }

        Assert.True(
            unnamed.Count == 0,
            "toolbar buttons with no AutomationProperties.Name: " + string.Join(", ", unnamed));
    }

    /// <summary>
    /// And there are enough of them that the test above is checking something. A regex that matched
    /// nothing would pass silently, which is the failure mode of every test written against text.
    /// </summary>
    [Fact]
    public void TheToolbarHasTheButtonsThisSuiteThinksItHas()
    {
        int buttons = Regex.Matches(Markup, "Classes=\"toolbar\"").Count;

        Assert.True(buttons >= 20, $"expected the toolbar to have at least 20 buttons, found {buttons}");
    }

    /// <summary>
    /// <b>A name that merely repeats the visible label earns nothing.</b> <c>Open…</c> read aloud
    /// is "open ellipsis"; the point of the name is to say what the button does.
    /// </summary>
    [Fact]
    public void NoNameIsJustTheVisibleLabel()
    {
        List<string> lazy = [];

        foreach (Match button in Regex.Matches(Markup, @"<Button\b[^>]*?/>|<Button\b[^>]*?>", RegexOptions.Singleline))
        {
            if (!button.Value.Contains("Classes=\"toolbar\"", StringComparison.Ordinal))
            {
                continue;
            }

            string content = Content(button.Value);
            string name = Attribute(button.Value, "AutomationProperties.Name");

            if (content.Length > 0 && string.Equals(content, name, StringComparison.Ordinal))
            {
                lazy.Add(content);
            }
        }

        // Undo and Redo are the exceptions and they are the right ones: the word IS the action, and
        // "Undo the last change" would be worse read aloud than "Undo".
        Assert.Equal(["Undo", "Redo"], lazy.Order(StringComparer.Ordinal).Reverse());
    }

    /// <summary>
    /// <b>Opening, saving and running have a keyboard path.</b> They are the three things a user
    /// does most and before this pass they were reachable by mouse alone.
    /// </summary>
    [Theory]
    [InlineData("Key.O")]
    [InlineData("Key.S")]
    [InlineData("Key.F5")]
    public void TheCommonGesturesHaveAKey(string key)
    {
        string handler = File.ReadAllText(Path.Combine(Root(), "src", "Spark.UI", "Views", "MainWindow.axaml.cs"));

        Assert.Contains(key, handler, StringComparison.Ordinal);
    }

    /// <summary>Undo and redo keep theirs, declared in markup.</summary>
    [Theory]
    [InlineData("Ctrl+Z")]
    [InlineData("Ctrl+Y")]
    [InlineData("Ctrl+Shift+Z")]
    public void UndoAndRedoKeepTheirKeys(string gesture)
    {
        Assert.Contains($"Gesture=\"{gesture}\"", Markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A bare letter is never bound at window level.</b> It would be taken from a user typing
    /// into the library search or a code block, and the rule is easier to keep than the exceptions
    /// would be to remember.
    /// </summary>
    [Fact]
    public void NoWindowLevelGestureIsABareLetter()
    {
        foreach (Match gesture in Regex.Matches(Markup, "Gesture=\"([^\"]+)\""))
        {
            string value = gesture.Groups[1].Value;

            Assert.True(
                value.Contains('+', StringComparison.Ordinal) || value.StartsWith('F'),
                $"'{value}' is a bare key at window level, which would be taken from a text box.");
        }
    }

    private static string Content(string button) => Attribute(button, "Content");

    private static string Attribute(string element, string name)
    {
        Match found = Regex.Match(element, Regex.Escape(name) + "=\"([^\"]*)\"");
        return found.Success ? found.Groups[1].Value : string.Empty;
    }

    private static string ReadMarkup() =>
        File.ReadAllText(Path.Combine(Root(), "src", "Spark.UI", "Views", "MainWindow.axaml"));

    /// <summary>Walks up to the repository root, which is where the markup lives.</summary>
    private static string Root()
    {
        DirectoryInfo? here = new(AppContext.BaseDirectory);

        while (here is not null)
        {
            if (File.Exists(Path.Combine(here.FullName, "Spark.slnx")))
            {
                return here.FullName;
            }

            here = here.Parent;
        }

        throw new InvalidOperationException("the repository root was not found above " + AppContext.BaseDirectory);
    }
}
