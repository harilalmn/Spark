using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spark.Docs.Verify;

/// <summary>
/// <c>EPICS.md</c>'s acceptance check boxes against <c>TASKS.md</c>'s statuses: where a criterion
/// cites exactly one register row, the box is ticked if and only if that row is <c>Done</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two documents making the same claim in two notations, with nothing reconciling them.</b>
/// <c>ProgressDashboardChecks</c> re-derives the dashboard's numbers from <c>TASKS.md</c>, so the
/// register and the dashboard cannot drift; <c>EPICS.md</c> was reconciled by hand, which means
/// it was reconciled when somebody happened to be editing it. **22 of the 154 single-row criteria
/// disagreed with their row on the day this was written** — two boxes ticked whose rows were
/// <c>Blocked</c> and <c>Withdrawn</c>, and twenty unticked whose rows had been <c>Done</c> for as
/// long as three weeks.
/// </para>
/// <para>
/// <b>This is the check that survived a negative result, and the one that did not is worth
/// recording.</b> The step that produced it set out to mechanise a different pattern — *a row
/// whose note argues with its own status column*, which had caught four rows in four days. Run
/// over 120 commits of <c>TASKS.md</c>, the general form of that rule **fires zero times**; the
/// version that caught the fourth row was a phrase list fitted to that row. That is
/// [N13](../../docs/NOTES.md)'s check that passes by finding nothing, and it was measured rather
/// than argued about. See [N176](../../docs/NOTES.md).
/// </para>
/// <para>
/// <b>A criterion is not its row, and that is why there is an exemptions file rather than a
/// predicate.</b> A criterion can be one third of a row (<c>E1-T28</c>'s concurrency groups, where
/// the row is blocked on branch protection) or broader than it, or met while its row is not —
/// <c>E11-T16</c>, where the benchmarks run nightly against committed budgets and what keeps the
/// row open is one measurement the criterion never named. All of those are legitimate and each is
/// written down with a reason a stranger can check. <see cref="NoExemptionIsStale"/> is what stops
/// the file becoming the place fixed problems go to be forgotten.
/// </para>
/// <para>
/// <b>THE CRITERIA CITING SEVERAL ROWS WERE EXCLUDED, AND THE EXCLUDED SET WAS WHERE THE ERRORS
/// WERE.</b> This check originally read *the* row a criterion cites, which is undefined when there
/// are three of them or none, so 63 of 217 criteria went unchecked and the remark here said they
/// *stay the reader's job*. The reader's job went undone: the sweep of 2026-09-15 read the 25
/// unticked boxes by hand and **ten of them were stale, all in the excluded set** — a higher hit
/// rate than in the set this check was covering. One had been held unticked for weeks by its own
/// sentence, *the CI check itself is still unwritten*, about a script that runs in `ci.yml`.
/// </para>
/// <para>
/// <b>So a multi-row box now ticks if and only if <i>every</i> row it cites is <c>Done</c></b>
/// (<see cref="EveryCriterionCitingSeveralRowsAgreesWithAllOfThem"/>), which is what *this is met
/// when both of these land* means when it is written out. It is a rule rather than a guess about
/// which row governs, and where it is wrong the exemptions file says so and why — matched on any
/// one of the cited rows. **What is still uncovered is the criteria citing no row at all**, and
/// there is no comparison to make for those: the answer is to give them a row, not to weaken this.
/// See [N181](../../docs/NOTES.md).
/// </para>
/// </remarks>
public sealed class AcceptanceCriterionChecks
{
    private static readonly Regex RowId = new(@"\bE\d+-T\d+\b", RegexOptions.Compiled);

    private static readonly Regex CheckBox = new(@"^- \[([ xX])\] (.*)$", RegexOptions.Compiled);

    /// <summary>
    /// <b>The parser finds the criteria at all.</b> First, because every assertion below is over a
    /// collection this produces, and a parser that matched nothing would make all of them pass.
    /// The count is a floor rather than an equality: criteria are added, and a check that had to be
    /// edited every time one was would be edited without being read.
    /// </summary>
    [Fact]
    public void TheCriteriaAreFound()
    {
        List<Criterion> criteria = Criteria();

        Assert.True(criteria.Count >= 200, $"found {criteria.Count} acceptance criteria in EPICS.md, expected at least 200");
        Assert.True(
            SingleRowCriteria().Count >= 160,
            $"found {SingleRowCriteria().Count} criteria citing exactly one register row, expected at least 160");

        // A FLOOR ON THE MULTI-ROW SET TOO, because it is the set that was silently empty before:
        // the check that reads it was added on 2026-09-15 and would pass over nothing at all if
        // the parser or the row expression stopped matching, which is the failure this whole file
        // has a paragraph about.
        Assert.True(
            MultiRowCriteria().Count >= 30,
            $"found {MultiRowCriteria().Count} criteria citing two or more register rows, expected at least 30");

        // AND THAT THE TWO SETS TOGETHER COVER EVERY CRITERION THAT CITES ANYTHING. This is the
        // assertion the 2026-09-15 sweep is the reason for: what went unchecked for weeks was not
        // a criterion anybody had exempted, it was a criterion no rule selected. A gap between
        // "cites a row" and "is checked" must be zero, not small.
        Assert.Equal(
            Cited().Count(c => c.Rows.Length >= 1),
            SingleRowCriteria().Count + MultiRowCriteria().Count);

        Assert.True(Statuses().Count >= 400, $"found {Statuses().Count} rows in TASKS.md, expected at least 400");
    }

    /// <summary>
    /// <b>Every criterion citing exactly one row agrees with that row's status</b>, unless the
    /// exemptions file says why not.
    /// </summary>
    /// <remarks>
    /// Criteria citing two or more rows are
    /// <see cref="EveryCriterionCitingSeveralRowsAgreesWithAllOfThem"/>'s, and were nobody's until
    /// 2026-09-15.
    /// </remarks>
    [Fact]
    public void EveryCriterionAgreesWithTheRowItCites()
    {
        IReadOnlyDictionary<string, string> statuses = Statuses();
        List<Exemption> exemptions = Exemptions();
        List<string> problems = [];

        foreach (Criterion criterion in SingleRowCriteria())
        {
            if (!statuses.TryGetValue(criterion.Row, out string? status))
            {
                problems.Add($"EPICS.md line {criterion.Line}: cites {criterion.Row}, which TASKS.md does not have.");
                continue;
            }

            bool done = status.StartsWith("Done", StringComparison.OrdinalIgnoreCase);

            if (done == criterion.Ticked)
            {
                continue;
            }

            if (exemptions.Any(e => e.Row == criterion.Row && criterion.Text.Contains(e.Match, StringComparison.Ordinal)))
            {
                continue;
            }

            problems.Add(
                $"EPICS.md line {criterion.Line}: the box is {(criterion.Ticked ? "ticked" : "unticked")} and "
                + $"{criterion.Row} is '{status}'. Tick it, change the row, or add a line to "
                + $"tests/corpus/epic-criterion-exemptions.tsv saying why they disagree.\n"
                + $"    {Shorten(criterion.Text)}");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// <b>Every criterion citing two or more rows is ticked if and only if all of them are
    /// <c>Done</c></b>, unless the exemptions file says why not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>All of them, rather than any of them, and the asymmetry is the point.</b> *This is met
    /// when both of these land* is what a multi-row criterion says, so one row short of `Done`
    /// leaves the box unticked and the reader is told **which** row — a box that ticked on its
    /// first finished row would be a box that announces the work before it is done.
    /// </para>
    /// <para>
    /// <b>An exemption matches on any one of the cited rows</b>, because there is no principled
    /// way to pick which of three a reason belongs to; the <c>Match</c> text is what makes it
    /// specific, and <see cref="NoExemptionIsStale"/> holds it to selecting exactly one criterion.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCriterionCitingSeveralRowsAgreesWithAllOfThem()
    {
        IReadOnlyDictionary<string, string> statuses = Statuses();
        List<Exemption> exemptions = Exemptions();
        List<string> problems = [];

        foreach (Criterion criterion in MultiRowCriteria())
        {
            string[] unknown = [.. criterion.Rows.Where(row => !statuses.ContainsKey(row))];

            if (unknown.Length > 0)
            {
                problems.Add(
                    $"EPICS.md line {criterion.Line}: cites {string.Join(", ", unknown)}, which TASKS.md does not have.");
                continue;
            }

            string[] notDone =
            [
                .. criterion.Rows.Where(row => !statuses[row].StartsWith("Done", StringComparison.OrdinalIgnoreCase)),
            ];

            if ((notDone.Length == 0) == criterion.Ticked)
            {
                continue;
            }

            if (exemptions.Any(e =>
                criterion.Rows.Contains(e.Row, StringComparer.Ordinal)
                && criterion.Text.Contains(e.Match, StringComparison.Ordinal)))
            {
                continue;
            }

            problems.Add(criterion.Ticked
                ? $"EPICS.md line {criterion.Line}: the box is ticked and "
                    + $"{string.Join(", ", notDone.Select(row => $"{row} is '{statuses[row]}'"))}. "
                    + "Untick it, finish the row, or add a line to "
                    + "tests/corpus/epic-criterion-exemptions.tsv saying why they disagree.\n"
                    + $"    {Shorten(criterion.Text)}"
                : $"EPICS.md line {criterion.Line}: the box is unticked and all of "
                    + $"{string.Join(", ", criterion.Rows)} are Done. Tick it, change a row, or add a line to "
                    + "tests/corpus/epic-criterion-exemptions.tsv saying why they disagree.\n"
                    + $"    {Shorten(criterion.Text)}");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// <b>No exemption is stale.</b> An exemption whose criterion and row now agree is excusing
    /// nothing, and leaving it there is how a file of reasons becomes a file of history — the next
    /// disagreement on that row would then be excused by a sentence about a problem somebody
    /// already fixed.
    /// </summary>
    [Fact]
    public void NoExemptionIsStale()
    {
        IReadOnlyDictionary<string, string> statuses = Statuses();

        // EVERY ROW-CITING CRITERION, not only the single-row ones. An exemption written for a
        // multi-row box would otherwise select zero criteria and be reported as a broken exemption
        // rather than honoured - the check policing the file would forbid the file's own new case.
        List<Criterion> criteria = [.. SingleRowCriteria(), .. MultiRowCriteria()];
        List<string> problems = [];

        foreach (Exemption exemption in Exemptions())
        {
            List<Criterion> matched =
            [
                .. criteria.Where(c =>
                    c.Rows.Contains(exemption.Row, StringComparer.Ordinal)
                    && c.Text.Contains(exemption.Match, StringComparison.Ordinal)),
            ];

            if (matched.Count != 1)
            {
                problems.Add(
                    $"exemptions line {exemption.Line}: '{exemption.Match}' selects {matched.Count} criteria "
                    + $"citing {exemption.Row}, not 1.");
                continue;
            }

            Criterion criterion = matched[0];

            if (criterion.Ticked != (exemption.Expect == "Ticked"))
            {
                problems.Add(
                    $"exemptions line {exemption.Line}: expects the {exemption.Row} box to be "
                    + $"{exemption.Expect.ToLowerInvariant()} and it is not.");
                continue;
            }

            // The same rule the two checks above apply: one row, or all of several.
            bool done = criterion.Rows.All(row =>
                statuses.TryGetValue(row, out string? status)
                && status.StartsWith("Done", StringComparison.OrdinalIgnoreCase));

            if (done == criterion.Ticked)
            {
                problems.Add(
                    $"exemptions line {exemption.Line}: {exemption.Row} and its criterion now agree, so this "
                    + "exemption excuses nothing. Delete it.");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Every acceptance check box in <c>EPICS.md</c>, with its continuation lines folded in.</summary>
    private static List<Criterion> Criteria()
    {
        string[] lines = File.ReadAllText(Path.Combine(Root(), "docs", "EPICS.md"))
            .ReplaceLineEndings("\n")
            .Split('\n');

        List<Criterion> criteria = [];
        int start = -1;
        string text = string.Empty;
        bool ticked = false;

        void Flush()
        {
            if (start >= 0)
            {
                criteria.Add(new Criterion(start, ticked, text));
            }

            start = -1;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            Match box = CheckBox.Match(lines[i]);

            if (box.Success)
            {
                Flush();
                start = i + 1;
                ticked = box.Groups[1].Value.Equals("x", StringComparison.OrdinalIgnoreCase);
                text = box.Groups[2].Value;
            }
            else if (start >= 0 && lines[i].StartsWith("  ", StringComparison.Ordinal) && lines[i].Trim().Length > 0)
            {
                // A criterion wraps, and its continuation carries the annotation that usually says
                // WHY it is in the state it is in — which is exactly the text an exemption matches
                // on. Reading only the first line would make half the file invisible to this check.
                text += " " + lines[i].Trim();
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return criteria;
    }

    /// <summary>The criteria that cite exactly one register row.</summary>
    private static List<Criterion> SingleRowCriteria() =>
    [.. Cited().Where(c => c.Rows.Length == 1)];

    /// <summary>
    /// Every criterion with the rows it <b>cites</b>, which is not every row its text mentions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A CRITERION CITES ITS ROWS IN A PARENTHESIS, AND NAMES OTHER ROWS IN ITS PROSE.</b>
    /// Reading every <c>E&lt;n&gt;-T&lt;m&gt;</c> in the folded text conflates the two, and the
    /// difference is not cosmetic: the index-based-BRep criterion cites <c>E2-T22</c> and
    /// <c>E2-T23</c> and its annotation goes on to name <c>E2-T44</c>, <c>E2-T64</c> and
    /// <c>E2-T65</c> — a measurement that supports it, a gap it excludes and a row that closed
    /// twelve members. None of the three governs the box, and <c>E2-T64</c> being <c>Deferred</c>
    /// says nothing about whether the topology is index-based.
    /// </para>
    /// <para>
    /// <b>Measured before it was adopted, over all 219 criteria.</b> 208 cite at least one row;
    /// **all 208 cite one inside a parenthesis, and not one cites a row only outside**, so the
    /// rule loses nothing. It also corrects the older reading in the other direction: **18
    /// criteria that really cite a single row** were being dropped from
    /// <see cref="EveryCriterionAgreesWithTheRowItCites"/> because their prose happened to mention
    /// a second, and they were checked by nothing at all. Single-row 152 → 170, multi-row
    /// 55 → 38.
    /// </para>
    /// </remarks>
    private static List<Criterion> Cited() =>
    [
        .. Criteria().Select(c => c with
        {
            Rows = [.. RowId.Matches(Parentheticals(c.Text)).Select(m => m.Value).Distinct()],
        }),
    ];

    /// <summary>The parenthesised spans of a criterion, run together.</summary>
    /// <remarks>
    /// Nesting is counted rather than matched on the first <c>)</c>, because a citation can sit
    /// inside a Markdown link — <c>([E13-T16](#e13--occt-provider), **R21**)</c> — whose own
    /// parentheses would otherwise close the span before the row was read. An unclosed
    /// parenthesis contributes what it has, since a criterion is prose and prose is unbalanced
    /// often enough to matter.
    /// </remarks>
    private static string Parentheticals(string text)
    {
        System.Text.StringBuilder inside = new();
        int depth = 0;

        foreach (char character in text)
        {
            switch (character)
            {
                case '(':
                    depth++;
                    continue;

                case ')':
                    depth = Math.Max(0, depth - 1);
                    inside.Append(' ');
                    continue;

                default:
                    if (depth > 0)
                    {
                        inside.Append(character);
                    }

                    continue;
            }
        }

        return inside.ToString();
    }

    /// <summary>The criteria that cite two or more register rows.</summary>
    /// <remarks>
    /// Unchecked until 2026-09-15, and the set the sweep of that day found ten stale boxes in.
    /// </remarks>
    private static List<Criterion> MultiRowCriteria() =>
    [.. Cited().Where(c => c.Rows.Length >= 2)];

    /// <summary>Every <c>E&lt;n&gt;-T&lt;m&gt;</c> row in <c>TASKS.md</c> with its status.</summary>
    /// <remarks>
    /// <b>The status column is not at a fixed index.</b> E13's table carries an <c>Est</c> column
    /// the other twelve do not, so reading position 2 reads seventeen estimates as statuses —
    /// the trap <c>scripts/build-progress.py</c> records having hit while it was being written.
    /// The header row is read instead.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> Statuses()
    {
        Dictionary<string, string> statuses = [];
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

            statuses[cells[0]] = cells[column].Replace("*", string.Empty).Replace("`", string.Empty).Trim();
        }

        return statuses;
    }

    /// <summary>The exemptions, one per line of the manifest.</summary>
    private static List<Exemption> Exemptions()
    {
        string path = Path.Combine(Root(), "tests", "corpus", "epic-criterion-exemptions.tsv");
        Assert.True(File.Exists(path), $"The criterion exemptions file is missing: {path}.");

        List<Exemption> exemptions = [];
        string[] lines = File.ReadAllLines(path);
        bool header = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!header)
            {
                Assert.Equal("Row\tExpect\tMatch\tReason", line);
                header = true;
                continue;
            }

            string[] cells = line.Split('\t');
            Assert.True(cells.Length == 4, $"line {i + 1} of the criterion exemptions has {cells.Length} cells, not 4.");
            Assert.Contains(cells[1], (string[])["Ticked", "Unticked"], StringComparer.Ordinal);
            Assert.True(cells[3].Length >= 40, $"line {i + 1} of the criterion exemptions has no real reason on it.");
            exemptions.Add(new Exemption(i + 1, cells[0], cells[1], cells[2], cells[3]));
        }

        return exemptions;
    }

    private static string Shorten(string text) =>
        text.Length <= 160 ? text : text[..160] + " …";

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

    /// <summary>One acceptance check box.</summary>
    /// <param name="Line">Its line number in <c>EPICS.md</c>, for the message.</param>
    /// <param name="Ticked">Whether the box is ticked.</param>
    /// <param name="Text">Its text, with continuation lines folded in.</param>
    private sealed record Criterion(int Line, bool Ticked, string Text)
    {
        /// <summary>The distinct register rows it cites.</summary>
        public string[] Rows { get; init; } = [];

        /// <summary>The single row it cites. Only meaningful when <see cref="Rows"/> has one.</summary>
        public string Row => Rows.Length == 1 ? Rows[0] : string.Empty;
    }

    /// <summary>One line of the exemptions manifest.</summary>
    /// <param name="Line">Its line number, for the message.</param>
    /// <param name="Row">The register row the criterion cites.</param>
    /// <param name="Expect">The state the box is expected to be in.</param>
    /// <param name="Match">A substring selecting exactly one criterion citing that row.</param>
    /// <param name="Reason">Why they are allowed to disagree.</param>
    private sealed record Exemption(int Line, string Row, string Expect, string Match, string Reason);
}
