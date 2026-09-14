using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spark.Docs.Verify;

/// <summary>
/// Register rows whose own text contradicts their status column (<c>E11-T33</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two rules, and the reason there are only two is a measurement that killed a broader one.</b>
/// [N176](../../docs/NOTES.md) records the first attempt at this: *a note that opens by declaring
/// the work done, closed, deferred, withdrawn or superseded, against an unsettled status* fires
/// **zero times over 120 commits** of `docs/TASKS.md`, and the phrasing that caught the row which
/// prompted it was a list fitted to that row. Generalising it produced three false positives and
/// nothing else.
/// </para>
/// <para>
/// <b>What was measured wrongly was the family, not that rule.</b> Surveying the remaining `Open`
/// rows a day later turned up two with exactly this fault in phrasings the first attempt never
/// tested — `E13-T22`, whose note ends *Blocked on `E13-T21`* under a status of `Open`, and
/// `E12-T16`, whose **title** is *Crash-reporting decision, deferred* under a status of `Open` —
/// and **both had been that way in every one of those 120 commits**. The yield of this shape is
/// standing rather than churning: rows that have been wrong for weeks, which is precisely the kind
/// nobody notices, and precisely what a check is for.
/// </para>
/// <para>
/// <b>Both rules read a row's own words rather than a keyword that might be about something else.</b>
/// *Blocked on <c>E&lt;n&gt;-T&lt;m&gt;</c>* names the row that blocks it, and a **title** is short
/// enough that a word in it is about the row and nothing else — which is what the first attempt's
/// note-scanning got wrong, where *this sub-item closed* in a multi-part row's note is ordinary and
/// correct.
/// </para>
/// </remarks>
public sealed class RegisterStatusChecks
{
    /// <summary>A note saying it is blocked on a named row.</summary>
    private static readonly Regex BlockedOn = new(@"[Bb]locked on\s+E\d+-T\d+", RegexOptions.Compiled);

    /// <summary>
    /// The statuses a row can hold while still being wrong about itself, and the reason the rules
    /// below only look at these.
    /// </summary>
    /// <remarks>
    /// <b>Found by the check's own first run, on the row that describes it.</b> `E11-T33`'s note
    /// quotes the phrase *Blocked on `E13-T21`* while recounting the fault it was written to catch,
    /// and the rule matched it — a false positive of exactly the kind this file claims to avoid,
    /// arriving within minutes of the claim. **A settled row cannot be blocked on anything**, so
    /// only an unsettled one is a candidate, and a row that merely mentions the words is not one.
    /// That is the rule stated properly rather than a filter bolted on: what is being checked is
    /// *an unfinished row that says it is waiting*, not *a row containing a phrase*.
    /// </remarks>
    private static readonly string[] Unsettled = ["Open", "In progress"];

    /// <summary>A title saying the row is deferred.</summary>
    private static readonly Regex Deferred = new(@"\b[Dd]eferred\b", RegexOptions.Compiled);

    /// <summary>
    /// <b>There are rows to check.</b> First, because the status column is not at a fixed index —
    /// E13's table carries an <c>Est</c> column the other twelve do not — so a reader that assumed
    /// one would find no statuses and pass by finding nothing.
    /// </summary>
    [Fact]
    public void TheRowsAreFound()
    {
        List<Row> rows = Rows();

        Assert.True(rows.Count >= 400, $"found {rows.Count} rows in TASKS.md, expected at least 400");
        Assert.True(
            rows.Count(r => r.Status.StartsWith("Done", StringComparison.OrdinalIgnoreCase)) >= 300,
            "found fewer than 300 Done rows, so the status column is being read from the wrong place");
    }

    /// <summary>
    /// <b>A row that says it is blocked on another row is <c>Blocked</c>.</b> Saying it in the note
    /// and not in the column means the register counts it as work somebody could pick up, and the
    /// journal's own *Blocked on* list is assembled by reading that column.
    /// </summary>
    [Fact]
    public void ARowBlockedOnAnotherSaysSoInItsStatus()
    {
        List<string> problems =
        [
            .. Rows()
                .Where(r => IsUnsettled(r) && BlockedOn.IsMatch(r.Note))
                .Select(r => $"{r.Id} is '{r.Status}' and its note says it is blocked on another row."),
        ];

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// <b>A row whose title says it is deferred is <c>Deferred</c>.</b> The title rather than the
    /// note, deliberately: a note can discuss the deferral of something else, and a title cannot.
    /// </summary>
    [Fact]
    public void ARowWhoseTitleSaysDeferredIsDeferred()
    {
        List<string> problems =
        [
            .. Rows()
                .Where(r => IsUnsettled(r) && Deferred.IsMatch(r.Title))
                .Select(r => $"{r.Id} is '{r.Status}' and its title says it is deferred: {r.Title}"),
        ];

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Whether a row is still claiming to be unfinished work.</summary>
    /// <param name="row">The row.</param>
    /// <returns><see langword="true"/> when its status is <c>Open</c> or <c>In progress</c>.</returns>
    private static bool IsUnsettled(Row row) =>
        Unsettled.Any(u => row.Status.StartsWith(u, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every <c>E&lt;n&gt;-T&lt;m&gt;</c> row, with its title, status and note.</summary>
    private static List<Row> Rows()
    {
        List<Row> rows = [];
        int column = 2;

        foreach (string raw in File.ReadAllText(Path.Combine(Root(), "docs", "TASKS.md"))
            .ReplaceLineEndings("\n")
            .Split('\n'))
        {
            if (!raw.StartsWith('|'))
            {
                continue;
            }

            string[] cells = [.. raw.Trim().Trim('|').Split('|').Select(c => c.Trim())];

            if (cells.Length > 0 && cells[0] == "ID")
            {
                column = Array.IndexOf(cells, "Status") is var found and >= 0 ? found : 2;
                continue;
            }

            if (cells.Length <= column || !Regex.IsMatch(cells[0], @"^E\d+-T\d+$"))
            {
                continue;
            }

            rows.Add(new Row(
                cells[0],
                Plain(cells[1]),
                Plain(cells[column]),
                Plain(string.Join(" | ", cells.Skip(column + 1)))));
        }

        return rows;
    }

    /// <summary>Strips the emphasis and code markers, so a rule matches words rather than markup.</summary>
    /// <param name="cell">The cell's text.</param>
    /// <returns>The text with <c>*</c> and <c>`</c> removed.</returns>
    private static string Plain(string cell) =>
        cell.Replace("*", string.Empty).Replace("`", string.Empty).Trim();

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    /// <summary>One register row.</summary>
    /// <param name="Id">Its <c>E&lt;n&gt;-T&lt;m&gt;</c>.</param>
    /// <param name="Title">Its title cell.</param>
    /// <param name="Status">Its status cell.</param>
    /// <param name="Note">Everything after the status.</param>
    private sealed record Row(string Id, string Title, string Status, string Note);
}
