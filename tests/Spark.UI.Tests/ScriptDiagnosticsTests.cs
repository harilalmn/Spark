using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Spark.Geometry;
using Spark.Scripting;
using Spark.UI.Views.Controls;

namespace Spark.UI.Tests;

/// <summary>
/// Compiler diagnostics for a code block, and the underlines the editor draws from them.
/// </summary>
/// <remarks>
/// <b>The point of <c>Diagnose</c> is that it is the same compile.</b> A second Roslyn workspace,
/// configured slightly differently, would eventually underline something that compiles or stay
/// silent on something that does not — and `E6-T13` says a language service that disagrees with
/// the compiler is worse than not having one. So the first test here is not that errors are found;
/// it is that they land on the user's line rather than on the generated frame's.
/// </remarks>
public sealed class ScriptDiagnosticsTests
{
    /// <summary>
    /// A broken script reports an error, and it is on the line the user typed it on.
    /// </summary>
    /// <remarks>
    /// <b>The mistake is a missing member, not an undefined name</b>, and the first version of this
    /// test got that wrong. An identifier that resolves to nothing is how Spark infers an input
    /// port (`E6-T5`) — <c>nonexistent</c> becomes a port and is declared, so it is never an
    /// error. <b>In a code block, the one diagnostic every other C# editor shows most often cannot
    /// happen.</b>
    /// </remarks>
    [Fact]
    public void AnErrorLandsOnTheUsersOwnLine()
    {
        ScriptNodeFactory factory = new(new ReferenceCatalog());

        // Three lines, and the mistake is on the third: a double has no such method.
        IReadOnlyList<ScriptDiagnostic> found = factory.Diagnose(
            "var a = 1.0;\nvar b = 2.0;\nreturn a.NoSuchMethod(b);");

        ScriptDiagnostic error = Assert.Single(found, d => d.IsError && d.Id == "CS1061");

        Assert.Equal(3, error.Line);
        Assert.True(error.Column > 0, $"column should be one-based: {error.Column}");
        Assert.Contains("NoSuchMethod", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A script that compiles reports no errors.</summary>
    [Fact]
    public void AGoodScriptReportsNothing()
    {
        ScriptNodeFactory factory = new(new ReferenceCatalog());

        Assert.DoesNotContain(
            factory.Diagnose("return new Point3d(1, 2, 3);"),
            diagnostic => diagnostic.IsError);
    }

    /// <summary>
    /// <b>Nothing is reported against the generated frame.</b> The wrapper is code the user has
    /// never seen; a message placed on it would underline a line that is not theirs, or — worse,
    /// if it were clamped — a line of theirs that is correct.
    /// </summary>
    [Fact]
    public void NothingIsReportedAgainstTheGeneratedFrame()
    {
        ScriptNodeFactory factory = new(new ReferenceCatalog());

        // A block with no return at all: the compiler complains about `Block.Run`, which is the
        // frame's line and not the user's.
        Assert.All(
            factory.Diagnose("var unused = 1.0;"),
            diagnostic => Assert.True(diagnostic.Line >= 1, $"line {diagnostic.Line} is not the user's"));
    }

    /// <summary>Hovering a symbol says what it is, and what its documentation says.</summary>
    [Fact]
    public async Task HoveringASymbolDescribesIt()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        const string Code = "var p = new Point3d(1, 2, 3);\nreturn p.DistanceTo(p);";
        int offset = Code.IndexOf("DistanceTo", StringComparison.Ordinal) + 2;

        ScriptQuickInfo info = Assert.IsType<ScriptQuickInfo>(
            await completion.DescribeAsync(Code, offset, null, TestContext.Current.CancellationToken));

        Assert.Contains("DistanceTo", info.Signature, StringComparison.Ordinal);
    }

    /// <summary>Hovering empty space describes nothing rather than guessing.</summary>
    [Fact]
    public async Task HoveringNothingDescribesNothing()
    {
        using ScriptCompletion completion = new([typeof(Point3d).Assembly]);

        Assert.Null(await completion.DescribeAsync("   ", 1, null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The editor turns a line and column into a document offset, so a hover over an underlined
    /// word finds the message that put it there.
    /// </summary>
    [Fact]
    public void TheEditorFindsTheMessageUnderThePointer() => HeadlessSession.Run(() =>
    {
        CodeBlockEditor editor = new()
        {
            DiagnosticsSource = (_, _) => Task.FromResult<IReadOnlyList<CodeDiagnostic>>(
                [new CodeDiagnostic(2, 5, 4, "CS0103", "the name does not exist", true)]),
        };

        Window window = new() { Width = 600, Height = 400, Content = editor };
        window.Show();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        TextEditor inner = editor.GetVisualDescendants().OfType<TextEditor>().First();
        inner.Document.Text = "var a = 1;\nvar bbbb = 2;";

        Task analysis = editor.AnalyseAsync();
        while (!analysis.IsCompleted)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        analysis.GetAwaiter().GetResult();

        Assert.Single(editor.Diagnostics);

        // Line 2 column 5 is `bbbb`, four characters in from the start of the second line.
        int start = inner.Document.GetLineByNumber(2).Offset + 4;

        Assert.NotNull(editor.DiagnosticAt(start));
        Assert.NotNull(editor.DiagnosticAt(start + 3));

        // And nothing is claimed on the line above it.
        Assert.Null(editor.DiagnosticAt(0));

        window.Close();
    });
}
