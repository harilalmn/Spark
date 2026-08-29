# Spark — Development Journal

The resumable record of the marathon run to 1.0. **Current state** is where the work is *right
now*; **Log** is how it got there. Everything else in `docs/` says what the product should be —
this file says what is happening.

**Last updated:** 2026-08-29 18:05 +0530
**Protocol version:** 1

---

## Current state

> Rewritten at the start and the end of every step. If a session dies, this is what the next one
> reads. It is deliberately short: detail belongs in the log entry, not here.

| | |
|---|---|
| **Milestone** | M1 — geometry core, finishing it |
| **Working on** | Nothing. Between steps, **awaiting the go-ahead to start the run.** |
| **Step status** | `CLEAN` |
| **Last commit** | `96c3e99` — Make an interrupted session cheap: a write-ahead development journal |
| **Working tree** | Clean at the time of writing; verify with `git status` |
| **Next action** | Take queue item **1**, the past-participle rename. **It has been scouted, so do not re-grep:** `Plane.Flip()` is defined at `src/Spark.Geometry/Plane.cs:325` and referenced in its own XML docs at lines 28 and 418; `BoundingBox.Inflate` is two overloads at `BoundingBox.cs:321` and `:333`; `Interval.Expand` is at `Interval.cs:271`. Call sites outside the definitions: `Curve.cs:533`, and eleven in `Spark.Geometry.Tests` and `Spark.Geometry.Properties`. Four lines in `PublicAPI.Unshipped.txt` (31, 32, 86, 110). **`Rect.Inflate` in `Spark.UI/Controls/GraphCanvas.cs` is Avalonia's and must not be touched.** Documents to follow: [DYNAMO-COVERAGE §4](DYNAMO-COVERAGE.md#member-names-we-will-not-copy) (which also asks for the convention to be written into `NamespaceDoc.cs`), the `E2-T3` and `E2-T6` rows in [TASKS.md](TASKS.md), and the item in [TODO.md](TODO.md). |
| **Verify with** | `dotnet build Spark.slnx --no-incremental -warnaserror` must be clean — a missed call site is a compile error, which is what makes this rename safe; then the per-project test executables (952) and `dotnet format`. |
| **Blocked on** | Nothing technical. Paused deliberately: the marathon was set up and reported before starting, at the user's request. |

**Step status vocabulary**, and it means exactly this:

- `CLEAN` — between steps. The tree is committed, the gates were green, anybody can start.
- `IN PROGRESS` — a step is part-written. The tree may not build. *Next action* says what was
  being done and what remains.
- `VERIFYING` — the code is written and the gates are running or being read. Nothing is committed.
- `BLOCKED` — the step cannot proceed. *Blocked on* says why and what would unblock it.

---

## The protocol

Nine steps. Steps 1 and 8 are the ones that make an interruption cheap, and they are the ones a
hurried session skips.

1. **Reconcile.** Read *Current state*, then `git status --short --branch` and
   `git log --oneline -5`. Where they disagree, the repository is right.
2. **Choose.** Resume the in-flight item, or take the top of the *Queue*. If you take something
   that is not the top of the queue, say why in the log.
3. **Write ahead.** *Before touching code*, update *Current state*: the item, `IN PROGRESS`, a
   concrete *Next action*, and how it will be verified. **This is the write-ahead log, and it is
   the whole mechanism** — a session that dies now leaves a note saying what it was in the middle
   of.
4. **Work**, in the smallest slice that is worth committing on its own.
5. **Verify.** The three gates, plus whatever proves *this* change: a named test that goes red
   when the fix is reverted (AGENTS.md step 7), a benchmark, a screenshot, a run of the app.
6. **Document.** The standing instruction in [AGENTS.md](../AGENTS.md) applies to every step:
   TASKS row, TODO, EPICS criterion, NOTES entry, ADR, help topic, XML docs — whichever the
   change actually touches, and their `Last updated` dates.
7. **Log.** Append an entry below: what changed, what was verified and *how*, what surprised you,
   and what it cost.
8. **Update *Current state*** to `CLEAN`, with the next item as *Next action*. Do this **before**
   committing, so the commit contains a journal that is already true.
9. **Commit and push.** `git commit -s`, work and documents and journal together, message naming
   the task IDs. Push, so the checkpoint survives the machine.

**The invariant:** at any instant, either the tree is clean and the journal says `CLEAN`, or the
journal names exactly what is half-done. There is no third state, and producing one is the only
way to make an interruption expensive.

**On committing often.** A step is a commit. If a step is taking so long that it cannot be
committed, it was too big — split it and commit the part that stands on its own. The git history
is the recovery point; the journal is the map.

---

## Environment facts a resuming session needs

Discovered the hard way, and each one costs an hour if rediscovered.

- **`dotnet test Spark.slnx` reports `Zero tests ran`, exit 5, on this machine** — under SDK
  10.0.400, for every project, including untouched ones. The suite is *not* red: each project's
  own executable runs green. Until it is diagnosed, verify with:
  ```
  for p in tests/*/; do n=$(basename "$p"); (cd "$p/bin/Debug/net10.0" && ./"$n.exe"); done
  ```
  which should total **952 passing, 0 failed** across seven projects. See
  [AGENTS.md](../AGENTS.md#before-you-commit).
- **No C++ toolchain.** `cmake`, `ninja`, `vcpkg`, `cl` and `g++` are all absent. **M1.6 /
  `E13-T1` cannot be started here** — `M1.6-C1` is a real build on two operating systems. This is
  an environment gap, not a scheduling choice, and it is why the queue routes around M1.6.
- **No `gh` CLI**, so CI results cannot be read from here. A run's outcome has to be pasted in.
- **The nightly benchmark workflow has never run on a hosted runner.** `E8-T15` closes on its
  first green run, and the canvas step is the part that might not survive a runner with no GPU.
- **Git identity is set per-repository** — `harilalmn <146122512+harilalmn@users.noreply.github.com>`,
  distinct from the global Zyeta identity. `git commit -s` picks it up; do not override it.
- **`git config core.filemode` is false here**, so a `chmod +x` never reaches the index. Any new
  script in `scripts/` needs `git update-index --chmod=+x`, and CI should invoke it through
  `bash script.sh` rather than executing it ([N28](NOTES.md)).

---

## Queue

Priority order for the near horizon. The far horizon is [TODO.md](TODO.md), and this list is a
finer-grained read of the same order, not a competing one. **Re-derive it from TODO.md whenever
the two disagree.**

| # | Item | Rows | Size | State |
|---|---|---|---|---|
| 1 | **The past-participle naming rule**, applied. `Plane.Flip` → `Flipped`, `BoundingBox.Inflate` → `Inflated`, `Interval.Expand` → `Expanded`, ~19 call sites | `E2-T40`-adjacent, [DYNAMO-COVERAGE §4](DYNAMO-COVERAGE.md#member-names-we-will-not-copy) | S | **Next** |
| 2 | **The three value-layer parity gaps** — `BoundingBox.Intersection`, `Plane.Offset`, `Plane.ByOriginNormalXAxis` | `E2-T40` | S | Open |
| 3 | **`Quaternion`** — the last piece of the value layer | `E2-T1` | M | Open |
| 4 | **`RayCaster` and its BVH** — pays for itself across mesh booleans, viewport picking, intersection seeding, and `Curve.ClosestPoint` waits on it | `E2-T15` | L | Open |
| 5 | **Geometry serialization v1 and the reflection-driven round-trip test** — get it in before there are twenty types to retrofit it onto; there are nineteen | `E2-T29`, `E2-T31` | M | Open |
| 6 | **`Spark.Geometry.Io`: the OBJ writer, and `spark` writing a polyline a third-party viewer opens** — this is **M1's demoable** | `E2-T33`, `E12-T5` | M | Open |
| 7 | **The C2VGeometry test harvest**, timeboxed to one week with a hard stop. Harvest assertions, not generators | `E2-T32` | L | Open — **needs the C2VGeometry source, which is not in this repository** |
| 8 | **M1.5 spike (c): AvaloniaEdit plus a Roslyn completion popup** — the last unproven part of M1.5, gating M4 | `E11-T21` | M | Open |
| 9 | **What is left of M2** — real docking (`E8-T2`), group/note/align (`E8-T6`), watch nodes (`E8-T10`), `spark run` (`E12-T5`) | | L | Open |
| 10 | **M3 — NURBS curves** | `E2-T10` … | XL | Open |

**Deferred, with a reason rather than by omission:**

- **M1.6 / `E13-T1`** — no C++ toolchain in this environment. Its criteria are written
  ([TASKS.md](TASKS.md#m16--the-passfail-criteria-written-before-the-spike)); the spike itself
  needs a machine with cmake, ninja and vcpkg.
- **Watching the first nightly** (`E8-T15`) — needs a CI result, which needs `gh` or a paste.
- **The six counsel questions** (`Q13`) — not engineering work.

---

## Log

Newest last. One entry per step. Keep them short and factual; the point is that a stranger can
reconstruct the reasoning, not that the prose is good.

### 2026-08-29 — The marathon harness

**What.** `CLAUDE.md`, this journal, and the protocol above. A session that ends abruptly now
leaves a written note of what it was in the middle of, and a session that starts cold has four
things to do before it touches anything.

**Why it is shaped this way.** The write-ahead step is the whole mechanism. A journal written
only *after* a step is finished is worthless in exactly the case it exists for — the session that
died half way through one. Writing the intent first costs a minute and makes the failure mode
cheap.

**Verified.** The build and the full suite were green immediately before this
(`dfa2803`, 952 tests, 0 failures). This step adds two Markdown files and touches no code.

**Recorded, because they cost time to rediscover:** no C++ toolchain here, so M1.6 is deferred
rather than merely not-started; and `dotnet test` at the solution level does not work on this
machine. Both are in *Environment facts* above.

**The protocol was exercised once before anything else happened, which is the only way to find
out whether it works.** Queue item 1 was written ahead — `IN PROGRESS`, with a concrete next
action — and then the run was paused before any code changed. Rolling *Current state* back to
`CLEAN` was a two-line edit, and the scouting the write-ahead had already produced was kept in
*Next action* rather than thrown away, so the next session starts item 1 without repeating the
search. That is the shape a real interruption should have.
