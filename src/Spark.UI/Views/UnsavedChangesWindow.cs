using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Spark.UI.Theming;

namespace Spark.UI.Views;

/// <summary>What a user chose to do about a document with unsaved changes (<c>E8-T79</c>).</summary>
public enum UnsavedChoice
{
    /// <summary>Go back — do not close anything.</summary>
    Cancel,

    /// <summary>Write the document first, then carry on.</summary>
    Save,

    /// <summary>Carry on and lose the changes.</summary>
    Discard,
}

/// <summary>
/// Asks what to do about unsaved changes before the document goes away (<c>E8-T79</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: <i>confirm with user to save unsaved changes on exit.</i> Until
/// now Spark closed without a word, and <c>E8-T37</c>'s <i>New</i> command said so in its own doc
/// comment — <i>nothing is asked and nothing is saved</i> — because there was no way to know
/// whether there was anything to ask about.
/// </para>
/// <para>
/// <b>Three answers, not two.</b> <i>Cancel</i> is the one that matters and the one a two-button
/// dialog cannot offer: somebody who hit close by accident needs a way back that is not <i>save
/// this thing I did not mean to save</i>. It is also the default — Escape and the close button
/// both mean cancel, because the safe answer should be the one you get by flinching.
/// </para>
/// <para>
/// <b>It names the file.</b> A prompt that says <i>you have unsaved changes</i> is asking about a
/// document the user has to identify from memory, and the answer differs depending on which one it
/// is.
/// </para>
/// </remarks>
public sealed class UnsavedChangesWindow : Window
{
    /// <summary>Creates the prompt for a named document, or an untitled one.</summary>
    /// <param name="document">The file's name, or null when the graph has never been saved.</param>
    /// <param name="what">What is about to happen, such as <c>closing Spark</c>.</param>
    public UnsavedChangesWindow(string? document, string what)
    {
        Title = "Unsaved changes";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = SparkPalette.Frozen(SparkPalette.BackgroundVoid);

        string named = string.IsNullOrWhiteSpace(document)
            ? "This graph has never been saved."
            : "'" + document + "' has changes you have not saved.";

        StackPanel body = new() { Margin = new Thickness(18) };

        body.Children.Add(new TextBlock
        {
            Text = named,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = SparkPalette.TextPrimaryBrush,
        });

        body.Children.Add(new TextBlock
        {
            Text = "Save before " + what + "?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = SparkPalette.TextSecondaryBrush,
        });

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };

        buttons.Children.Add(Answer("Save", UnsavedChoice.Save, isDefault: true));
        buttons.Children.Add(Answer("Discard", UnsavedChoice.Discard, isDefault: false));
        buttons.Children.Add(Answer("Cancel", UnsavedChoice.Cancel, isDefault: false));

        body.Children.Add(buttons);
        Content = body;
    }

    /// <summary>
    /// What the user answered. <b>Cancel until they say otherwise</b>, so a prompt dismissed by any
    /// means keeps the document.
    /// </summary>
    public UnsavedChoice Choice { get; private set; } = UnsavedChoice.Cancel;

    /// <summary>Answers as a test would, without a click.</summary>
    /// <param name="choice">The answer to give.</param>
    public void Answer(UnsavedChoice choice)
    {
        Choice = choice;
        Close();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            // Escape is Cancel, which is already the standing answer.
            Close();
            e.Handled = true;
        }
    }

    private Button Answer(string text, UnsavedChoice choice, bool isDefault)
    {
        Button button = new()
        {
            Content = text,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = isDefault,
        };

        button.Click += (_, _) => Answer(choice);
        return button;
    }
}
