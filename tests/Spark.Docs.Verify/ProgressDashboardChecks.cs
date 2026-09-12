using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spark.Docs.Verify;

/// <summary>
/// The progress dashboard, checked against the register it reports on
/// (<c>docs/progress.html</c>, <c>scripts/build-progress.py</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The client's standing instruction of 2026-09-12 is that the dashboard is updated whenever
/// EPICS, TASKS or TODO is updated, <i>without fail</i>.</b> This is the mechanism that makes the
/// instruction true rather than remembered. AGENTS.md already says why that distinction matters:
/// <i>a rule nobody enforces is a preference</i>, and this repository has the scar to prove it -
/// <c>DocGenerator</c> was 1,478 hand-maintained entries that drifted until 101 of 108 public
/// constructors rendered blank.
/// </para>
/// <para>
/// <b>What it actually compares, and why it is not a re-render.</b> The generator writes the
/// numbers it derived into a JSON island at the foot of the page. This check parses
/// <c>docs/TASKS.md</c> and <c>tests/corpus/dynamo-parity.tsv</c> <i>itself</i>, by its own reading
/// of them, and compares. So a green run means two independent readings of the register agree with
/// the page - not that a file round-trips through a script, which would only prove the script is
/// deterministic. The same shape as <c>DynamoParityChecks</c>: re-derive, never re-run.
/// </para>
/// <para>
/// <b>The status column is prose as often as it is a word</b>, and the parser here must fail loudly
/// rather than silently bucket a row it does not understand. <c>Blocked 2026-09-11 on the
/// toolchain</c>, <c>Deferred past 1.0</c> and <c>Done, in the half that is reachable</c> are all
/// real cells in the register today. An earlier reading took a fixed column index and counted E13's
/// seventeen <c>Est</c> cells as seventeen statuses, which is why the header is read per table.
/// </para>
/// <para>
/// <b>What this cannot check.</b> It does not look at the page's prose, its layout or its colours,
/// and it never will: those are a human's to read. It checks that no number on the page contradicts
/// the documents. A dashboard can be accurate and still be misleading, and only mechanism four in
/// AGENTS.md - somebody reads it - catches that.
/// </para>
/// </remarks>
public sealed class ProgressDashboardChecks
{
    private static readonly string Root = RepositoryRoot();

    /// <summary>The statuses the register uses, longest-first so <c>Done</c> cannot shadow a longer word.</summary>
    private static readonly string[] Statuses =
        ["Done", "In progress", "Open", "Blocked", "Deferred", "Withdrawn"];

    /// <summary>Rows in these states are decisions rather than debt, so they leave the denominator.</summary>
    private static readonly string[] OutOfScope = ["Withdrawn", "Deferred"];

    private static readonly Lazy<JsonElement> Island = new(ReadIsland);

    /// <summary>
    /// The page exists, carries its generator's warning, and publishes the numbers it used.
    /// Everything below rests on the island being readable, so it is asserted on its own.
    /// </summary>
    [Fact]
    public void TheDashboardPublishesTheNumbersItWasBuiltFrom()
    {
        string page = File.ReadAllText(Path.Combine(Root, "docs", "progress.html"));

        Assert.Contains("GENERATED FILE. Do not edit by hand.", page, StringComparison.Ordinal);
        Assert.Contains("scripts/build-progress.py", page, StringComparison.Ordinal);

        JsonElement island = Island.Value;
        Assert.True(island.TryGetProperty("totals", out _), "the data island has no 'totals'.");
        Assert.True(island.TryGetProperty("epics", out _), "the data island has no 'epics'.");
    }

    /// <summary>
    /// The dashboard's overall counts are the register's own. This is the check the standing
    /// instruction actually buys: close a task, forget the dashboard, and the build goes red.
    /// </summary>
    [Fact]
    public void TheDashboardAgreesWithTheRegisterOverall()
    {
        IReadOnlyList<TaskRow> rows = ReadRegister();
        JsonElement totals = Island.Value.GetProperty("totals");
        List<string> problems = [];

        foreach (string status in Statuses)
        {
            int register = rows.Count(r => r.Status == status);
            int page = totals.TryGetProperty(status, out JsonElement value) ? value.GetInt32() : 0;

            if (register != page)
            {
                problems.Add($"'{status}': the register has {register}, the dashboard says {page}.");
            }
        }

        int scope = rows.Count(r => !OutOfScope.Contains(r.Status, StringComparer.Ordinal));

        if (Island.Value.GetProperty("total").GetInt32() != rows.Count)
        {
            problems.Add($"row count: the register has {rows.Count}, the dashboard says "
                         + $"{Island.Value.GetProperty("total").GetInt32()}.");
        }

        if (Island.Value.GetProperty("scope").GetInt32() != scope)
        {
            problems.Add($"in-scope rows: the register has {scope}, the dashboard says "
                         + $"{Island.Value.GetProperty("scope").GetInt32()}.");
        }

        Assert.Empty(Explain(problems));
    }

    /// <summary>
    /// Every epic's breakdown matches, not merely the totals. Two epics can move in opposite
    /// directions and leave the total alone, which is exactly the drift a headline figure hides.
    /// </summary>
    [Fact]
    public void TheDashboardAgreesWithTheRegisterEpicByEpic()
    {
        IReadOnlyList<TaskRow> rows = ReadRegister();
        JsonElement epics = Island.Value.GetProperty("epics");
        List<string> problems = [];

        foreach (IGrouping<string, TaskRow> epic in rows.GroupBy(r => r.Epic).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (!epics.TryGetProperty(epic.Key, out JsonElement counts))
            {
                problems.Add($"{epic.Key}: in the register, absent from the dashboard.");
                continue;
            }

            foreach (string status in Statuses)
            {
                int register = epic.Count(r => r.Status == status);
                int page = counts.TryGetProperty(status, out JsonElement value) ? value.GetInt32() : 0;

                if (register != page)
                {
                    problems.Add($"{epic.Key} '{status}': the register has {register}, the dashboard says {page}.");
                }
            }
        }

        foreach (JsonProperty listed in epics.EnumerateObject())
        {
            if (!rows.Any(r => r.Epic == listed.Name))
            {
                problems.Add($"{listed.Name}: on the dashboard, absent from the register.");
            }
        }

        Assert.Empty(Explain(problems));
    }

    /// <summary>
    /// The dashboard's parity figures are the manifest's. The manifest is itself guarded by
    /// <see cref="DynamoParityChecks"/>, so this closes the chain from ProtoGeometry to the page.
    /// </summary>
    [Fact]
    public void TheDashboardAgreesWithTheParityManifest()
    {
        Dictionary<string, int> manifest = ReadParityManifest();
        JsonElement parity = Island.Value.GetProperty("parity");
        List<string> problems = [];

        foreach ((string status, int count) in manifest.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            int page = parity.TryGetProperty(status, out JsonElement value) ? value.GetInt32() : 0;

            if (count != page)
            {
                problems.Add($"parity '{status}': the manifest has {count}, the dashboard says {page}.");
            }
        }

        Assert.Empty(Explain(problems));
    }

    /// <summary>
    /// The suite figure on the page is the one the journal's <i>Current state</i> claims. The
    /// dashboard must never be the place a test count is invented.
    /// </summary>
    [Fact]
    public void TheDashboardsSuiteFigureComesFromTheJournal()
    {
        string journal = File.ReadAllText(Path.Combine(Root, "docs", "JOURNAL.md"));
        string state = journal.Split("## Log", StringSplitOptions.None)[0];

        MatchCollection matches = Regex.Matches(
            state, @"\*\*([\d,]+)\*\* tests over \*\*ten\*\* executables");

        Assert.True(matches.Count > 0, "JOURNAL.md's Current state no longer states the suite size.");

        int journalCount = int.Parse(
            matches[^1].Groups[1].Value.Replace(",", "", StringComparison.Ordinal),
            CultureInfo.InvariantCulture);

        Assert.Equal(journalCount, Island.Value.GetProperty("tests").GetInt32());
    }

    // ----------------------------------------------------------------------------------------

    private static List<string> Explain(List<string> problems)
    {
        if (problems.Count > 0)
        {
            problems.Add("Run: python scripts/build-progress.py");
        }

        return problems;
    }

    private static JsonElement ReadIsland()
    {
        string page = File.ReadAllText(Path.Combine(Root, "docs", "progress.html"));

        Match match = Regex.Match(
            page,
            "<script type=\"application/json\" id=\"spark-progress-data\">(.*?)</script>",
            RegexOptions.Singleline);

        Assert.True(match.Success, "docs/progress.html carries no #spark-progress-data island.");

        using JsonDocument document = JsonDocument.Parse(match.Groups[1].Value);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Every <c>E&lt;n&gt;-T&lt;n&gt;</c> row in TASKS.md, with its epic and normalised status.
    /// The <c>Status</c> column is located from each table's own header, because E13's table
    /// carries an <c>Est</c> column the other twelve do not.
    /// </summary>
    private static IReadOnlyList<TaskRow> ReadRegister()
    {
        List<TaskRow> rows = [];
        string? epic = null;
        int statusColumn = 2;

        foreach (string line in File.ReadAllLines(Path.Combine(Root, "docs", "TASKS.md")))
        {
            Match header = Regex.Match(line, @"^## (E\d+) — ");

            if (header.Success)
            {
                epic = header.Groups[1].Value;
                continue;
            }

            if (epic is null || !line.StartsWith('|'))
            {
                continue;
            }

            string[] cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();

            if (cells[0] == "ID")
            {
                int index = Array.IndexOf(cells, "Status");
                statusColumn = index >= 0 ? index : 2;
                continue;
            }

            if (cells.Length <= statusColumn || !Regex.IsMatch(cells[0], @"^E\d+-T\d+$"))
            {
                continue;
            }

            rows.Add(new TaskRow(cells[0], epic, Normalise(cells[0], cells[statusColumn])));
        }

        Assert.NotEmpty(rows);
        return rows;
    }

    /// <summary>
    /// Reduce a status cell to one of <see cref="Statuses"/>, taking the leading clause so that a
    /// row which explains itself in the status column still counts once. An unrecognised cell fails
    /// the test rather than being bucketed, because a silent default is how a count goes quietly
    /// wrong.
    /// </summary>
    private static string Normalise(string id, string cell)
    {
        string text = Regex.Replace(cell, "[*`]", string.Empty).Trim();
        string head = text.Split(',', '—', '–')[0].Trim();

        foreach (string status in Statuses)
        {
            if (head.StartsWith(status, StringComparison.OrdinalIgnoreCase))
            {
                return status;
            }
        }

        Assert.Fail($"TASKS.md row {id} has an unrecognised status '{cell}'. "
                    + "Add it to ProgressDashboardChecks.Statuses and to scripts/build-progress.py, "
                    + "or correct the row.");
        return string.Empty;
    }

    private static Dictionary<string, int> ReadParityManifest()
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);

        foreach (string line in File.ReadAllLines(Path.Combine(Root, "tests", "corpus", "dynamo-parity.tsv")))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] cells = line.Split('\t');

            if (cells.Length < 3 || cells[0] == "DynamoType")
            {
                continue;
            }

            counts[cells[2]] = counts.GetValueOrDefault(cells[2]) + 1;
        }

        Assert.NotEmpty(counts);
        return counts;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private sealed record TaskRow(string Id, string Epic, string Status);
}
