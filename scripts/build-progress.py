#!/usr/bin/env python3
"""Regenerate docs/progress.html from the documents it reports on.

    python scripts/build-progress.py          # rewrite docs/progress.html
    python scripts/build-progress.py --check  # exit 1 if it is out of date

**Why this is a generator and not a hand-written page.** The dashboard carries roughly forty
derived numbers. Hand-maintaining forty numbers across a register that moves every step is
exactly `DocGenerator` again -- 1,478 hand-maintained entries that drifted until 101 of 108
public constructors rendered blank (docs/NOTES.md). The client's standing instruction of
2026-09-12 is that the dashboard is updated whenever EPICS, TASKS or TODO is updated, *without
fail*, and a rule that needs forty careful edits to honour is a rule that will be broken.

**The split, and it is the whole design.** Everything NUMERIC is derived here, on every run,
from the documents and the repository. Everything NARRATIVE -- what a milestone means, what is
waiting on a person, what the next action is -- lives in the `NARRATIVE` block below and is
edited by a human, because prose that is generated is prose nobody reads.

**The page carries its own numbers back out** in a JSON island (`#spark-progress-data`), which
is what `Spark.Docs.Verify`'s ProgressDashboardChecks re-derives independently and compares
against. That check is the mechanism; this script is only the convenient way to satisfy it.
"""

from __future__ import annotations

import argparse
import collections
import datetime as dt
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# --------------------------------------------------------------------------------------
# NARRATIVE -- edited by a human. Nothing below this block is hand-maintained.
# --------------------------------------------------------------------------------------

NARRATIVE = {
    "tagline": (
        "A parametric design environment for AEC, in marathon development towards 1.0."
    ),
    # Milestones: (id, what it delivers, state) where state is "done" or "active".
    "milestones": [
        ("M0", "Foundations and docs", "done"),
        ("M1", "Geometry core", "done"),
        ("M1.5", "De-risk spike", "done"),
        ("M1.6", "OCCT spike", "done"),
        ("M2", "Walking skeleton and lacing", "done"),
        ("M3", "NURBS curves", "done"),
        ("M4", "C# code block", "done"),
        ("M5", "Surfaces and mesh", "done"),
        ("M6", "BRep and exact solids", "done"),
        ("M7", "Packages and extensibility", "done"),
        ("M8", "Embedding and 1.0", "active"),
    ],
    "milestone_note": (
        "The standing instruction is <b>no tag and no release until the register is worked "
        "out</b>, and the end-user Help pass is deliberately sequenced after 1.0 (D19)."
    ),
    # The gates. The suite count is read from JOURNAL.md's Current state; these are the words.
    "gates": [
        ("Build &mdash; clean",
         "<span class=\"mono\">dotnet build --no-incremental -warnaserror</span> over sixteen "
         "projects, zero warnings."),
        ("Tests &mdash; {tests} passing",
         "Ten test executables. Zero failures and <b>zero skips</b> &mdash; the native shim was "
         "present, so the OCCT provider was really exercised."),
        ("Format &mdash; clean",
         "<span class=\"mono\">dotnet format --verify-no-changes --severity warn</span>."),
    ],
    "ci_warning": (
        "<b>&#9888; Nothing runs in CI.</b> GitHub Actions was switched off on 2026-09-09 when "
        "the repository went private and minutes stopped being free. A green local run is the "
        "whole of the evidence, and four rows are <span class=\"pill blocked\">Blocked</span> on "
        "nothing but a workflow that cannot execute &mdash; they unblock themselves at the "
        "open-source release."
    ),
    "parity_warning": (
        "<b>&#9888; Two numbers disagree, and deliberately.</b> The coverage document's headline "
        "table still reads <i>99 reachable, 11.8%</i>, which predates the curve, surface and "
        "solid layers; the per-subsystem counts above and the manifest are current. Re-counting "
        "the headline is <span class=\"mono\">E2-T46</span>'s job, not a patch. And <b>a "
        "percentage of members is not a percentage of work</b> &mdash; "
        "<span class=\"mono\">Solid.Difference</span> is one row and a multi-year research problem."
    ),
    # What is waiting on a person rather than on code.
    "waiting": [
        ("1 &middot; The OpenCascade toolchain.",
         "The install the shim was built against is gone from this machine, and that is the whole "
         "of what blocks <span class=\"mono\">E13-T18</span> and "
         "<span class=\"mono\">E13-T21</span>. The client reinstalls it by hand."),
        ("2 &middot; Counsel.",
         "Q13's questions, the first being whether <span class=\"mono\">spark_occt</span> is a "
         "<i>work that uses the Library</i> or a derivative work under LGPL &sect;5. Reduced by "
         "D25 &mdash; open source at release &mdash; but not closed."),
        ("3 &middot; A signing identity.",
         "Nothing is signed and the release notes say so, so a first run shows SmartScreen. D26 "
         "settles that no certificate is bought for now."),
        ("4 &middot; T-Splines, Q12.",
         "169 members &mdash; 20% of ProtoGeometry &mdash; sit behind one decision no commit can "
         "make. It is the denominator of every parity figure on this page."),
    ],
    # Epic milestone tags, for the sub-label under each epic name.
    "epic_milestones": {
        "E1": "M0", "E2": "M1 M3 M5 M6 M8", "E3": "M2", "E4": "M2", "E5": "M2 M3",
        "E6": "M4", "E7": "M7", "E8": "M2", "E9": "M2 M5", "E10": "M0 onwards",
        "E11": "M0 onwards", "E12": "M8", "E13": "M1.6 M6 M8",
    },
    # A one-line gloss for rows that are not self-explanatory from their task title.
    "row_gloss": {
        "E2-T41": "Dynamo capability parity: curves &mdash; 187 members, the largest subsystem left",
        "E2-T45": "<b>Next action</b> &mdash; Dynamo capability parity: mesh, &sect;3.6's 65 members",
        "E2-T46": "Dynamo capability parity: infrastructure, and the coverage document's stale headline",
        "E2-T66": "&sect;3.3's eighteen missing members; no sphere and no cone in <span class=\"mono\">BrepPrimitives</span>",
        "E2-T67": "Mass properties on a <span class=\"mono\">Brep</span> exactly, not off the tessellation",
        "E2-T64": "<span class=\"mono\">Curve2d</span> for trim pcurves &mdash; deliberately waiting for a consumer",
        "E2-T48": "Decision: is T-Splines in scope at all? (Q12 &mdash; 169 members hang on it)",
        "E2-T27": "Robust mesh boolean &mdash; reduced to 1.x, not withdrawn",
        "E6-T14": "The two node types over one pipeline",
        "E8-T14": "Help panel and F1 &mdash; the generated-reference drill-down waits on the post-1.0 Help pass",
        "E11-T16": "Benchmark suite, run nightly &mdash; the budgets are a manual gate while Actions is off",
        "E12-T5": "The <span class=\"mono\">spark</span> verbs &mdash; <span class=\"mono\">render</span>, <span class=\"mono\">pkg</span>, <span class=\"mono\">docs</span>, <span class=\"mono\">graph</span> unwritten",
        "E13-T18": "An imported model's closed shells should come back as solids &mdash; the OpenCascade install is gone from this machine",
        "E13-T21": "The shim meshes a deep copy of the shape &mdash; same toolchain block",
        "E1-T19": "CI job: headless UI smoke &mdash; Actions is off",
        "E1-T21": "Nightly benchmark workflow &mdash; Actions is off",
        "E8-T15": "2,000-node canvas benchmark, nightly &mdash; Actions is off",
        "E11-T14": "<span class=\"mono\">docs-freshness</span> CI job &mdash; Actions is off",
        "E12-T2": "<span class=\"mono\">IHostServices</span> seam (D20)",
        "E12-T4": "Prove <span class=\"mono\">Spark.Host</span> inside a real Revit or AutoCAD add-in (D20)",
    },
    # Per-subsystem state words for the Dynamo table, keyed by section number.
    "parity_state": {
        "3.1": "Assessed",
        "3.2": "<span class=\"pill progress\">E2-T41</span> largest remaining",
        "3.3": "Assessed 2026-09-12",
        "3.4": "Assessed 2026-09-12",
        "3.5": "Assessed 2026-09-12",
        "3.6": "<span class=\"pill open\">E2-T45</span> next",
        "3.7": "<span class=\"pill deferred\">Q12</span> a second product",
        "3.8": "<span class=\"pill open\">E2-T46</span>",
    },
}

# Statuses, in the order they are shown, and the CSS class each uses.
STATUS_ORDER = ["Done", "In progress", "Open", "Blocked", "Deferred", "Withdrawn"]
STATUS_CLASS = {
    "Done": "done", "In progress": "progress", "Open": "open",
    "Blocked": "blocked", "Deferred": "deferred", "Withdrawn": "withdrawn",
}
# Rows in these states are decisions rather than debt, so they leave the denominator.
OUT_OF_SCOPE = {"Withdrawn", "Deferred"}


# --------------------------------------------------------------------------------------
# Derivation
# --------------------------------------------------------------------------------------

def read(*parts: str) -> str:
    with open(os.path.join(ROOT, *parts), encoding="utf-8") as handle:
        return handle.read()


def git(*args: str) -> str:
    return subprocess.run(
        ["git", "-C", ROOT, *args], capture_output=True, text=True, check=True
    ).stdout.strip()


def normalise_status(raw: str) -> str:
    """Reduce a register status cell to one of STATUS_ORDER.

    The column is prose as often as it is a word: `Blocked 2026-09-11 on the toolchain`,
    `Deferred past 1.0`, `Done, in the half that is reachable`. Take the leading clause and
    match it, so a row that explains itself in the status column still counts once.
    """
    text = re.sub(r"[*`]", "", raw).strip()
    head = re.split(r"[,—–]", text)[0].strip()
    for status in STATUS_ORDER:
        if head.lower().startswith(status.lower()):
            return status
    raise ValueError(f"unrecognised task status: {raw!r}")


def parse_tasks() -> tuple[list[dict], list[dict]]:
    """Every `E<n>-T<n>` row in TASKS.md, with its epic and its normalised status.

    The status column is NOT at a fixed index: E13's table carries an `Est` column the others
    do not. Read each table's own header rather than assuming a position -- assuming one counted
    seventeen estimates as seventeen statuses the first time this was written.
    """
    epic = None
    epics: list[dict] = []
    rows: list[dict] = []
    status_col = 2

    for line in read("docs", "TASKS.md").split("\n"):
        header = re.match(r"^## (E\d+) — (.+)$", line)
        if header:
            epic = header.group(1)
            epics.append({"id": epic, "title": header.group(2).strip()})
            continue
        if not epic or not line.startswith("|"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if cells[0] == "ID":
            status_col = cells.index("Status") if "Status" in cells else 2
            continue
        if len(cells) <= status_col or not re.match(r"^E\d+-T\d+$", cells[0]):
            continue
        rows.append({
            "id": cells[0],
            "epic": epic,
            "task": re.sub(r"[*`]", "", cells[1]).strip(),
            "status": normalise_status(cells[status_col]),
        })

    per = collections.defaultdict(collections.Counter)
    for row in rows:
        per[row["epic"]][row["status"]] += 1
    for item in epics:
        counts = per[item["id"]]
        item["counts"] = {s: counts.get(s, 0) for s in STATUS_ORDER if counts.get(s, 0)}
        item["total"] = sum(counts.values())
        item["scope"] = item["total"] - sum(counts.get(s, 0) for s in OUT_OF_SCOPE)
        item["done"] = counts.get("Done", 0)
    return epics, rows


def parse_parity() -> dict[str, int]:
    """Status counts from the parity manifest -- the register that guards the register."""
    counts: collections.Counter = collections.Counter()
    for line in read("tests", "corpus", "dynamo-parity.tsv").split("\n"):
        if not line or line.startswith("#"):
            continue
        cells = line.split("\t")
        if len(cells) < 3 or cells[0] == "DynamoType":
            continue
        counts[cells[2]] += 1
    return dict(counts)


def parse_subsystems() -> list[dict]:
    """The §3 subsystem headers of DYNAMO-COVERAGE.md, which carry their own counts."""
    out = []
    pattern = re.compile(
        r"^### (3\.\d) (.+?) — (\d+) types?, (\d+) members?, (.+)$", re.M
    )
    for match in pattern.finditer(read("docs", "DYNAMO-COVERAGE.md")):
        reach = match.group(5).strip()
        number = re.match(r"^(\d+) reachable$", reach)
        out.append({
            "section": match.group(1),
            "name": match.group(2).strip(),
            "types": int(match.group(3)),
            "members": int(match.group(4)),
            "reachable": int(number.group(1)) if number else None,
        })
    return out


def count_lines(*globs: str) -> int:
    total = 0
    for top in globs:
        for base, dirs, files in os.walk(os.path.join(ROOT, top)):
            dirs[:] = [d for d in dirs if d not in ("bin", "obj", "TestResults")]
            for name in files:
                if name.endswith((".cs", ".cpp", ".h")):
                    with open(os.path.join(base, name), encoding="utf-8", errors="replace") as f:
                        total += sum(1 for _ in f)
    return total


def project_count(top: str) -> int:
    path = os.path.join(ROOT, top)
    return sum(
        1 for d in os.listdir(path)
        if os.path.isdir(os.path.join(path, d))
        and any(f.endswith(".csproj") for f in os.listdir(os.path.join(path, d)))
    )


def suite_count() -> int:
    """The suite size, read from JOURNAL.md's Current state rather than restated here."""
    journal = read("docs", "JOURNAL.md")
    state = journal.split("## Log")[0]
    matches = re.findall(r"\*\*([\d,]+)\*\* tests over \*\*ten\*\* executables", state)
    if not matches:
        raise ValueError("could not read the suite count from JOURNAL.md's Current state")
    return int(matches[-1].replace(",", ""))


def commits_per_day() -> list[tuple[str, int]]:
    """Commits per calendar day, with the quiet days kept rather than closed up."""
    days = collections.Counter(git("log", "--format=%ad", "--date=short").split("\n"))
    first = dt.date.fromisoformat(min(days))
    last = dt.date.fromisoformat(max(days))
    out = []
    day = first
    while day <= last:
        out.append((day.isoformat(), days.get(day.isoformat(), 0)))
        day += dt.timedelta(days=1)
    return out


def gather() -> dict:
    epics, rows = parse_tasks()
    totals = collections.Counter(r["status"] for r in rows)
    total = len(rows)
    out = sum(totals.get(s, 0) for s in OUT_OF_SCOPE)
    scope = total - out
    parity = parse_parity()
    parity_total = sum(parity.values())
    committed = parity_total - parity.get("Not planned", 0) - parity.get("Needs a decision", 0)

    src_lines = count_lines("src")
    test_lines = count_lines("tests")
    native_lines = count_lines("native")
    days = commits_per_day()

    return {
        "generated": dt.date.today().isoformat(),
        "commit": git("rev-parse", "--short", "HEAD"),
        "epics": epics,
        "rows": rows,
        "totals": {s: totals.get(s, 0) for s in STATUS_ORDER},
        "total": total,
        "scope": scope,
        "done": totals.get("Done", 0),
        "percent": round(100 * totals.get("Done", 0) / scope),
        "percent_all": round(100 * totals.get("Done", 0) / total, 1),
        "parity": parity,
        "parity_total": parity_total,
        "parity_committed": committed,
        "parity_percent": round(100 * parity.get("Done", 0) / committed),
        "subsystems": parse_subsystems(),
        "tests": suite_count(),
        "src_lines": src_lines,
        "test_lines": test_lines,
        "native_lines": native_lines,
        "total_lines": src_lines + test_lines + native_lines,
        "src_projects": project_count("src"),
        "test_projects": project_count("tests"),
        "node_families": len([
            f for f in os.listdir(os.path.join(ROOT, "src", "Spark.Nodes.Core"))
            if f.endswith(".cs")
        ]),
        "adrs": len([f for f in os.listdir(os.path.join(ROOT, "docs", "adr")) if f.endswith(".md")]),
        "notes": len(re.findall(r"^## N\d+", read("docs", "NOTES.md"), re.M)),
        "journal_entries": len(re.findall(r"^### ", read("docs", "JOURNAL.md"), re.M)),
        "help_topics": sum(
            1 for base, _, files in os.walk(os.path.join(ROOT, "docs", "help"))
            for f in files if f.endswith(".md")
        ),
        "commits": int(git("rev-list", "--count", "HEAD")),
        "tags": len([t for t in git("tag", "-l").split("\n") if t]),
        "days": days,
        "working_days": sum(1 for _, n in days if n),
    }


# --------------------------------------------------------------------------------------
# Rendering
# --------------------------------------------------------------------------------------

def esc(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def bar(counts: dict[str, int], height: str = "16px", radius: str = "3px") -> str:
    title = ", ".join(f"{n} {s.lower()}" for s, n in counts.items() if n)
    parts = "".join(
        f'<i class="{STATUS_CLASS[s]}" style="flex:{counts[s]}"></i>'
        for s in STATUS_ORDER if counts.get(s)
    )
    return (f'<div class="bar" style="height:{height}; border-radius:{radius}" '
            f'title="{title}">{parts}</div>')


def render_epics(data: dict) -> str:
    out = []
    for epic in data["epics"]:
        pct = round(100 * epic["done"] / epic["scope"]) if epic["scope"] else 0
        tag = NARRATIVE["epic_milestones"].get(epic["id"], "")
        out.append(f"""      <div class="epic">
        <div class="name"><b>{epic['id']} &middot; {esc(epic['title'])}</b><em>{tag} &middot; {epic['total']} rows</em></div>
        {bar(epic['counts'])}
        <div class="pct">{pct}%</div>
      </div>""")
    return "\n\n".join(out)


def render_remaining(data: dict) -> str:
    order = {"In progress": 0, "Open": 1, "Blocked": 2, "Deferred": 3}
    rows = [r for r in data["rows"] if r["status"] in order]
    rows.sort(key=lambda r: (order[r["status"]], r["epic"], r["id"]))
    label = {"In progress": "In progress", "Open": "Open", "Blocked": "Blocked", "Deferred": "Post-1.0"}
    out = []
    for row in rows:
        gloss = NARRATIVE["row_gloss"].get(row["id"], esc(row["task"]))
        cls = STATUS_CLASS[row["status"]]
        out.append(
            f'          <tr><td class="mono">{row["id"]}</td>'
            f'<td><span class="pill {cls}">{label[row["status"]]}</span></td>'
            f"<td>{gloss}</td></tr>"
        )
    return "\n".join(out)


def render_subsystems(data: dict) -> str:
    out = []
    for sub in data["subsystems"]:
        reach = sub["reachable"] if sub["reachable"] is not None else "&mdash;"
        state = NARRATIVE["parity_state"].get(sub["section"], "")
        out.append(
            f'          <tr><td>{sub["section"]} {esc(sub["name"])}</td>'
            f'<td class="num">{sub["types"]}</td><td class="num">{sub["members"]}</td>'
            f'<td class="num">{reach}</td><td>{state}</td></tr>'
        )
    return "\n".join(out)


def render_velocity(data: dict) -> str:
    peak = max(n for _, n in data["days"]) or 1
    bars, labels = [], []
    months = {}
    for iso, n in data["days"]:
        day = dt.date.fromisoformat(iso)
        months.setdefault(day.strftime("%b %Y"), 0)
        months[day.strftime("%b %Y")] += n
        pretty = day.strftime("%-d %b") if os.name != "nt" else f"{day.day} {day:%b}"
        if n:
            bars.append(f'        <div style="height:{max(2, round(100 * n / peak))}%" '
                        f'title="{pretty} &mdash; {n}"></div>')
        else:
            bars.append(f'        <div class="zero" style="height:2px" title="{pretty} &mdash; 0"></div>')
        labels.append(f"<div>{day.day}</div>")
    return "\n".join(bars), "".join(labels), peak


def render(data: dict) -> str:
    gates = "\n".join(
        f"""      <div class="gate">
        <div class="g">{g.format(tests=f"{data['tests']:,}")}</div>
        <div class="d">{d}</div>
      </div>""" for g, d in NARRATIVE["gates"]
    )
    miles = "\n".join(
        f'      <div class="mile {"shipped" if s == "done" else "active"}"><div class="m">{m}</div>'
        f'<div class="t">{t}</div><div class="s">{"Done" if s == "done" else "In flight"}</div></div>'
        for m, t, s in NARRATIVE["milestones"]
    )
    tiles = "\n".join(
        f'          <div class="tile {STATUS_CLASS[s]}"><div class="n">{data["totals"][s]}</div>'
        f'<div class="l">{s}</div></div>' for s in STATUS_ORDER
    )
    waiting = "\n".join(
        f'    <div class="note"><b>{h}</b> {b}</div>' for h, b in NARRATIVE["waiting"]
    )
    parity_bar = bar(
        {
            "Done": data["parity"].get("Done", 0),
            "In progress": data["parity"].get("Planned", 0),
            "Open": data["parity"].get("Unassessed", 0),
            "Withdrawn": data["parity"].get("Not planned", 0),
            "Deferred": data["parity"].get("Needs a decision", 0),
        },
        height="26px", radius="5px",
    )
    parity_legend = "\n".join(
        f'        <span><i class="sw" style="background:var(--{c})"></i>{n} &mdash; {data["parity"].get(k, 0)}</span>'
        for k, n, c in [
            ("Done", "Done", "done"), ("Planned", "Planned", "progress"),
            ("Unassessed", "Unassessed", "open"), ("Not planned", "Not planned", "withdrawn"),
            ("Needs a decision", "Needs a decision", "deferred"),
        ]
    )
    vel_bars, vel_labels, _ = render_velocity(data)

    circumference = 2 * 3.14159265 * 62
    filled = round(circumference * data["done"] / data["scope"], 1)
    gap = round(circumference - filled, 1)

    remaining_count = sum(
        data["totals"][s] for s in ("In progress", "Open", "Blocked", "Deferred")
    )
    island = json.dumps({
        "generated": data["generated"],
        "commit": data["commit"],
        "totals": data["totals"],
        "total": data["total"],
        "scope": data["scope"],
        "epics": {e["id"]: e["counts"] for e in data["epics"]},
        "parity": data["parity"],
        "tests": data["tests"],
    }, indent=2, sort_keys=True)

    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Spark &mdash; Progress</title>
<!--
  GENERATED FILE. Do not edit by hand.

      python scripts/build-progress.py            # rewrite this file
      python scripts/build-progress.py --check    # fail if it is out of date

  The client's standing instruction of 2026-09-12 is that this page is updated whenever EPICS,
  TASKS or TODO is updated, without fail. `ProgressDashboardChecks` in tests/Spark.Docs.Verify
  re-derives the numbers below from docs/TASKS.md independently and fails the build when they
  disagree, so the instruction is a gate rather than a thing to remember. Narrative text is
  edited in the NARRATIVE block of the script; numbers are never edited anywhere.
-->
{STYLE}
</head>
<body>
<div class="wrap">

  <header>
    <h1>Spark &mdash; progress</h1>
    <p class="sub">
      {NARRATIVE['tagline']}
      Generated <b>{data['generated']}</b> from the task register, the journal and the repository
      at <span class="mono">{data['commit']}</span>.
    </p>
  </header>

  <hr class="rule">

  <section style="margin-top:0">
    <div class="card headline">
      <svg class="ring" width="168" height="168" viewBox="0 0 168 168" role="img"
           aria-label="{data['done']} of {data['scope']} active task rows are done, {data['percent']} percent">
        <circle cx="84" cy="84" r="62" fill="none" stroke="var(--rule)" stroke-width="16"/>
        <circle cx="84" cy="84" r="62" fill="none" stroke="var(--done)" stroke-width="16"
                stroke-dasharray="{filled} {gap}" transform="rotate(-90 84 84)"/>
        <text class="big" x="84" y="80" text-anchor="middle">{data['percent']}%</text>
        <text class="small" x="84" y="98" text-anchor="middle">{data['done']} / {data['scope']} ROWS</text>
      </svg>

      <div>
        <h3 style="margin-bottom:12px">{data['total']} rows in the register &mdash; {data['scope']} of them still in scope</h3>
        <div class="tiles">
{tiles}
        </div>
        <p class="sub" style="margin-top:12px; font-size:13px">
          The {data['totals']['Withdrawn']} withdrawn and {data['totals']['Deferred']} deferred rows are out of the
          denominator: they are decisions, not debt. Against all {data['total']} rows the figure is {data['percent_all']}%.
        </p>
      </div>
    </div>
  </section>

  <section>
    <h2>The three gates, and the suite</h2>
    <div class="gates">
{gates}
    </div>
    <div class="note" style="margin-top:12px">
      {NARRATIVE['ci_warning']}
    </div>
  </section>

  <section>
    <h2>Milestones</h2>
    <div class="miles">
{miles}
    </div>
    <p class="sub" style="margin-top:12px; font-size:13px">
      {data['tags']} releases are tagged. {NARRATIVE['milestone_note']}
    </p>
  </section>

  <section>
    <h2>Epics</h2>
    <div class="legend">
      <span><i class="sw" style="background:var(--done)"></i>Done</span>
      <span><i class="sw" style="background:var(--progress)"></i>In progress</span>
      <span><i class="sw" style="background:var(--open)"></i>Open</span>
      <span><i class="sw" style="background:var(--blocked)"></i>Blocked</span>
      <span><i class="sw" style="background:var(--deferred)"></i>Deferred past 1.0</span>
      <span><i class="sw" style="background:var(--withdrawn)"></i>Withdrawn</span>
    </div>
    <div class="card" style="padding:8px 20px">

{render_epics(data)}

    </div>
    <p class="sub" style="margin-top:12px; font-size:13px">
      Percentages are <i>done &divide; (rows less withdrawn and deferred)</i>.
    </p>
  </section>

  <section>
    <h2>What remains &mdash; {remaining_count} rows</h2>
    <div class="scroll card" style="padding:8px 12px">
      <table>
        <thead>
          <tr><th style="width:80px">Row</th><th style="width:104px">State</th><th>Task</th></tr>
        </thead>
        <tbody>
{render_remaining(data)}
        </tbody>
      </table>
    </div>
  </section>

  <section>
    <h2>Dynamo capability parity</h2>
    <div class="card">
      <p style="margin-top:0">
        <span class="mono">ProtoGeometry.dll</span> as shipped with Revit 2026 declares
        <b>51 public types</b> and <b>{data['parity_total']} public members</b>. Every one is tracked on its own
        row in <span class="mono">tests/corpus/dynamo-parity.tsv</span>, checked against the
        coverage document in both directions on every build.
      </p>

      {parity_bar}
      <div class="legend" style="margin-top:10px; margin-bottom:0">
{parity_legend}
      </div>

      <p style="margin:16px 0 0; font-size:13.5px; color:var(--ink-2)">
        Against the <b>committed</b> surface &mdash; {data['parity_total']} less the
        {data['parity'].get('Not planned', 0)} we refuse and the {data['parity'].get('Needs a decision', 0)} awaiting a
        decision, so <b>{data['parity_committed']} members</b> &mdash; Spark stands at
        <b>{data['parity'].get('Done', 0)}, or {data['parity_percent']}%</b>.
      </p>
    </div>

    <div class="scroll card" style="margin-top:12px; padding:8px 12px">
      <table>
        <thead>
          <tr><th>Subsystem</th><th class="num">Types</th><th class="num">Members</th><th class="num">Reachable</th><th>State</th></tr>
        </thead>
        <tbody>
{render_subsystems(data)}
        </tbody>
      </table>
    </div>

    <div class="note" style="margin-top:12px">
      {NARRATIVE['parity_warning']}
    </div>
  </section>

  <section>
    <h2>The tree</h2>
    <div class="two">
      <div class="card">
        <h3>Code</h3>
        <div class="scroll">
          <table>
            <tbody>
              <tr><td>C# in <span class="mono">src/</span> &mdash; {data['src_projects']} projects</td><td class="num">{data['src_lines']:,}</td></tr>
              <tr><td>C# in <span class="mono">tests/</span> &mdash; {data['test_projects']} projects</td><td class="num">{data['test_lines']:,}</td></tr>
              <tr><td>C++ in <span class="mono">native/spark_occt</span></td><td class="num">{data['native_lines']:,}</td></tr>
              <tr><td><b>Total lines</b></td><td class="num"><b>{data['total_lines']:,}</b></td></tr>
              <tr><td>Tests passing</td><td class="num">{data['tests']:,}</td></tr>
              <tr><td>Node families</td><td class="num">{data['node_families']}</td></tr>
            </tbody>
          </table>
        </div>
      </div>

      <div class="card">
        <h3>Documents</h3>
        <div class="scroll">
          <table>
            <tbody>
              <tr><td>Architecture decision records</td><td class="num">{data['adrs']}</td></tr>
              <tr><td>Notes &mdash; traps paid for once</td><td class="num">{data['notes']}</td></tr>
              <tr><td>Journal entries</td><td class="num">{data['journal_entries']}</td></tr>
              <tr><td>Help topics written</td><td class="num">{data['help_topics']}</td></tr>
              <tr><td>Commits</td><td class="num">{data['commits']}</td></tr>
              <tr><td>Tags released</td><td class="num">{data['tags']}</td></tr>
              <tr><td>Working days</td><td class="num">{data['working_days']}</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </section>

  <section>
    <h2>Commits per day</h2>
    <div class="card">
      <div class="vel" role="img" aria-label="Commits per day, {data['commits']} over {data['working_days']} working days">
{vel_bars}
      </div>
      <div class="velx">{vel_labels}</div>
      <p class="sub" style="margin:10px 0 0; font-size:12.5px">
        {data['commits']} commits over {data['working_days']} working days; a step is a commit.
      </p>
    </div>
  </section>

  <section>
    <h2>Waiting on a person, not on code</h2>
{waiting}
  </section>

  <footer>
    <p><b>Provenance.</b> Row counts parsed from <a href="TASKS.md">TASKS.md</a>; parity counts
      from <span class="mono">tests/corpus/dynamo-parity.tsv</span> and
      <a href="DYNAMO-COVERAGE.md">DYNAMO-COVERAGE.md</a>; the suite figure from
      <a href="JOURNAL.md">JOURNAL.md</a>'s <i>Current state</i>; line counts, commits and tags
      from the repository at <span class="mono">{data['commit']}</span>.</p>
    <p><b>Generated, not written.</b> <span class="mono">scripts/build-progress.py</span> rebuilds
      this page from those documents, and <span class="mono">ProgressDashboardChecks</span> in
      <span class="mono">tests/Spark.Docs.Verify</span> fails the build when it is out of date.
      Do not edit it by hand &mdash; correct the document it reads, and regenerate.
      The single source of truth for where the work stopped is <a href="JOURNAL.md">JOURNAL.md</a>;
      priority order is <a href="TODO.md">TODO.md</a>; the full register is
      <a href="TASKS.md">TASKS.md</a>.</p>
  </footer>

</div>
<script type="application/json" id="spark-progress-data">
{island}
</script>
</body>
</html>
"""


STYLE = """<style>
  :root {
    color-scheme: light dark;
    --bg: #f7f7f5; --panel: #ffffff; --panel-2: #fbfbfa;
    --ink: #1b1b19; --ink-2: #55534e; --ink-3: #8a8780;
    --rule: #e3e1dc; --rule-2: #d2cfc8; --accent: #b4530a;
    --done: #3f7d58; --progress: #b8860b; --open: #4a6fa5;
    --blocked: #a63d40; --deferred: #6b5b95; --withdrawn: #9b9894;
    --done-soft: #e6efe9; --progress-soft: #f6eeda;
    --open-soft: #e6ecf4; --blocked-soft: #f5e5e5;
  }
  @media (prefers-color-scheme: dark) {
    :root:not([data-theme="light"]) {
      --bg: #16161a; --panel: #1e1e23; --panel-2: #24242a;
      --ink: #eceae5; --ink-2: #b0ada6; --ink-3: #7c7972;
      --rule: #32323a; --rule-2: #43434d; --accent: #e08a42;
      --done: #6aab84; --progress: #d9a63c; --open: #7c9dcb;
      --blocked: #d4696c; --deferred: #9d8ec4; --withdrawn: #6e6b66;
      --done-soft: #23322a; --progress-soft: #342c1a;
      --open-soft: #222934; --blocked-soft: #342224;
    }
  }
  :root[data-theme="dark"] {
    --bg: #16161a; --panel: #1e1e23; --panel-2: #24242a;
    --ink: #eceae5; --ink-2: #b0ada6; --ink-3: #7c7972;
    --rule: #32323a; --rule-2: #43434d; --accent: #e08a42;
    --done: #6aab84; --progress: #d9a63c; --open: #7c9dcb;
    --blocked: #d4696c; --deferred: #9d8ec4; --withdrawn: #6e6b66;
    --done-soft: #23322a; --progress-soft: #342c1a;
    --open-soft: #222934; --blocked-soft: #342224;
  }

  * { box-sizing: border-box; }
  body {
    margin: 0; background: var(--bg); color: var(--ink);
    font: 15px/1.55 -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
    -webkit-font-smoothing: antialiased;
  }
  .wrap { max-width: 1120px; margin: 0 auto; padding-block: 40px 72px; padding-left: 20px; padding-right: 20px; }

  h1 { font-size: 30px; letter-spacing: -0.02em; margin: 0 0 4px; font-weight: 640; }
  h2 { font-size: 12px; letter-spacing: 0.11em; text-transform: uppercase;
       color: var(--ink-3); font-weight: 660; margin: 0 0 14px; }
  h3 { font-size: 15px; margin: 0 0 10px; font-weight: 640; }
  p { margin: 0 0 12px; }
  a { color: var(--accent); }

  .sub { color: var(--ink-2); font-size: 14px; margin: 0; }
  .rule { height: 1px; background: var(--rule); border: 0; margin: 36px 0; }
  section { margin-top: 40px; }

  .card { background: var(--panel); border: 1px solid var(--rule); border-radius: 10px; padding: 20px; }

  .headline { display: grid; grid-template-columns: auto 1fr; gap: 28px; align-items: center; }
  @media (max-width: 700px) {
    .headline { grid-template-columns: 1fr; gap: 20px; justify-items: center; text-align: center; }
  }
  .ring { flex: none; max-width: 100%; }
  .ring text.big { font-size: 30px; font-weight: 680; fill: var(--ink); }
  .ring text.small { font-size: 10px; fill: var(--ink-3); letter-spacing: 0.08em; }

  .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(108px, 1fr));
           gap: 1px; background: var(--rule); border: 1px solid var(--rule);
           border-radius: 8px; overflow: hidden; }
  .tile { background: var(--panel-2); padding: 12px 14px; }
  .tile .n { font-size: 22px; font-weight: 660; line-height: 1.15; font-variant-numeric: tabular-nums; }
  .tile .l { font-size: 11px; color: var(--ink-3); letter-spacing: 0.05em; text-transform: uppercase; margin-top: 2px; }
  .tile.done .n { color: var(--done); }
  .tile.progress .n { color: var(--progress); }
  .tile.open .n { color: var(--open); }
  .tile.blocked .n { color: var(--blocked); }
  .tile.deferred .n { color: var(--deferred); }
  .tile.withdrawn .n { color: var(--withdrawn); }

  .legend { display: flex; flex-wrap: wrap; gap: 6px 18px; font-size: 12px; color: var(--ink-2); margin-bottom: 16px; }
  .legend span { display: inline-flex; align-items: center; gap: 6px; }
  .sw { width: 10px; height: 10px; border-radius: 2px; flex: none; display: inline-block; }

  .epic { display: grid; grid-template-columns: 222px 1fr 56px; gap: 14px; align-items: center; padding: 7px 0; }
  .epic + .epic { border-top: 1px solid var(--rule); }
  .epic .name { font-size: 13.5px; }
  .epic .name b { font-weight: 640; }
  .epic .name em { font-style: normal; color: var(--ink-3); font-size: 11.5px; display: block; letter-spacing: 0.03em; }
  .epic .pct { text-align: right; font-variant-numeric: tabular-nums; font-size: 13px; color: var(--ink-2); }
  .bar { display: flex; overflow: hidden; background: var(--rule); }
  .bar i { display: block; height: 100%; }
  .bar i.done { background: var(--done); }
  .bar i.progress { background: var(--progress); }
  .bar i.open { background: var(--open); }
  .bar i.blocked { background: var(--blocked); }
  .bar i.deferred { background: var(--deferred); }
  .bar i.withdrawn { background: var(--withdrawn); }
  @media (max-width: 640px) {
    .epic { grid-template-columns: 1fr 48px; grid-template-areas: "name pct" "bar bar"; gap: 6px 10px; }
    .epic .name { grid-area: name; } .epic .pct { grid-area: pct; } .epic .bar { grid-area: bar; }
  }

  .miles { display: flex; flex-wrap: wrap; gap: 8px; }
  .mile { flex: 1 1 96px; min-width: 92px; border: 1px solid var(--rule-2);
          border-radius: 7px; padding: 10px 11px; background: var(--panel-2); }
  .mile.shipped { background: var(--done-soft); border-color: var(--done); }
  .mile.active { background: var(--progress-soft); border-color: var(--progress); }
  .mile .m { font-weight: 680; font-size: 14px; }
  .mile .t { font-size: 11.5px; color: var(--ink-2); line-height: 1.35; margin-top: 2px; }
  .mile .s { font-size: 10px; letter-spacing: 0.08em; text-transform: uppercase; margin-top: 7px; color: var(--ink-3); }
  .mile.shipped .s { color: var(--done); font-weight: 640; }
  .mile.active .s { color: var(--progress); font-weight: 640; }

  table { width: 100%; border-collapse: collapse; font-size: 13.5px; }
  th, td { text-align: left; padding: 7px 10px; border-bottom: 1px solid var(--rule); vertical-align: top; }
  th { font-size: 11px; letter-spacing: 0.07em; text-transform: uppercase; color: var(--ink-3); font-weight: 620; }
  td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
  tbody tr:last-child td { border-bottom: 0; }
  .scroll { overflow-x: auto; }

  code, .mono { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 0.92em; }

  .pill { display: inline-block; padding: 1px 7px; border-radius: 999px;
          font-size: 11px; font-weight: 620; letter-spacing: 0.02em; white-space: nowrap; }
  .pill.progress { background: var(--progress-soft); color: var(--progress); }
  .pill.open { background: var(--open-soft); color: var(--open); }
  .pill.blocked { background: var(--blocked-soft); color: var(--blocked); }
  .pill.deferred { background: var(--panel-2); color: var(--deferred); border: 1px solid var(--rule-2); }

  .gates { display: grid; grid-template-columns: repeat(auto-fit, minmax(196px, 1fr)); gap: 12px; }
  .gate { border: 1px solid var(--done); background: var(--done-soft); border-radius: 8px; padding: 12px 14px; }
  .gate .g { font-weight: 640; font-size: 13.5px; color: var(--done); }
  .gate .d { font-size: 12.5px; color: var(--ink-2); margin-top: 3px; }

  .vel { display: flex; align-items: flex-end; gap: 4px; height: 110px; }
  .vel div { flex: 1; background: var(--accent); border-radius: 2px 2px 0 0; min-height: 2px; opacity: 0.85; }
  .vel div.zero { background: var(--rule-2); opacity: 1; }
  .velx { display: flex; gap: 4px; margin-top: 6px; font-size: 9.5px; color: var(--ink-3); }
  .velx div { flex: 1; text-align: center; }

  .note { border-left: 3px solid var(--progress); background: var(--progress-soft);
          padding: 11px 14px; border-radius: 0 7px 7px 0; font-size: 13.5px; color: var(--ink-2); }
  .note b { color: var(--ink); }
  .note + .note { margin-top: 10px; }

  .two { display: grid; grid-template-columns: 1fr 1fr; gap: 18px; }
  @media (max-width: 780px) { .two { grid-template-columns: 1fr; } }

  footer { margin-top: 52px; padding-top: 18px; border-top: 1px solid var(--rule); font-size: 12.5px; color: var(--ink-3); }
  footer p { margin: 0 0 6px; }
</style>"""


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true",
                        help="exit 1 if docs/progress.html is out of date, writing nothing")
    args = parser.parse_args()

    page = render(gather())
    target = os.path.join(ROOT, "docs", "progress.html")

    if args.check:
        current = read("docs", "progress.html") if os.path.exists(target) else ""
        # The generated date moves every day and is not evidence of drift; ignore it.
        strip = lambda t: re.sub(r'"generated": "[^"]*"', "", re.sub(r"Generated <b>[^<]*</b>", "", t))
        if strip(current) != strip(page):
            print("docs/progress.html is out of date. Run: python scripts/build-progress.py")
            return 1
        print("docs/progress.html is up to date.")
        return 0

    with open(target, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(page)
    print(f"docs/progress.html written ({len(page):,} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
