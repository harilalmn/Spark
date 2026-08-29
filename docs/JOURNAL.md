# Spark — Development Journal

The resumable record of the marathon run to 1.0. **Current state** is where the work is *right
now*; **Log** is how it got there. Everything else in `docs/` says what the product should be —
this file says what is happening.

**Last updated:** 2026-08-29 20:10 +0530
**Protocol version:** 2

---

## Current state

> Rewritten at the start and the end of every step. If a session dies, this is what the next one
> reads. It is deliberately short: detail belongs in the log entry, not here.

| | |
|---|---|
| **Milestone** | M1 — geometry core, finishing it |
| **Working on** | Nothing. Between steps. |
| **Step status** | `CLEAN` |
| **Last completed step** | Queue **2** — the three value-layer parity gaps (`E2-T40`) |
| **Working tree** | Clean at the time of writing; verify with `git status` |
| **Next action** | Take queue item **3**, `Quaternion` (`E2-T1`) — the last piece of the value layer. `Transform` already carries the rotation algebra, so the questions to settle first are **what `Quaternion` is for that `Transform` is not**: composing rotations without accumulating shear, interpolating between orientations (`Slerp`), and being the compact form a serialised camera or frame wants. Follow the layer's existing conventions exactly — `readonly struct`, exact `operator ==`, `EqualsWithin(other, in Tolerance)`, a `TryNormalise` sibling if normalisation can fail, and every public member documented or the build fails. `default(Quaternion)` must not be a usable rotation, on the `default(Plane)` precedent. |
| **Verify with** | `dotnet build Spark.slnx --no-incremental -warnaserror`, the per-project executables (**966** after this step: Geometry.Tests is 327), `dotnet format`. **Check the counts, do not just read *green*** — [N30](NOTES.md) is why. |
| **Blocked on** | Nothing. |

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

**The journal never records its own commit hash.** *Last completed step* names the step, and
`git log -1` supplies the hash — a journal committed *with* the work cannot contain the hash of
the commit that contains it. Protocol v1 tried, and the correction is v2. Naming the step is
also the more useful half: a hash tells a resumer where, a step name tells them what.

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
| 1 | ~~The past-participle naming rule, applied~~ | `E2-T49` | S | **Done** 2026-08-29 |
| 2 | ~~The three value-layer parity gaps~~ | `E2-T40` | S | **Done** 2026-08-29 |
| 3 | **`Quaternion`** — the last piece of the value layer | `E2-T1` | M | **Next** |
| 4 | **`RayCaster` and its BVH** — pays for itself across mesh booleans, viewport picking, intersection seeding, and `Curve.ClosestPoint` waits on it | `E2-T15` | L | Open |
| 5 | **Geometry serialization v1 and the reflection-driven round-trip test** — get it in before there are twenty types to retrofit it onto; there are nineteen | `E2-T29`, `E2-T31` | M | Open |
| 6 | **`Spark.Geometry.Io`: the OBJ writer, and `spark` writing a polyline a third-party viewer opens** — this is **M1's demoable** | `E2-T33`, `E12-T5` | M | Open |
| 7 | **The C2VGeometry test harvest**, timeboxed to one week with a hard stop. Harvest assertions, not generators | `E2-T32` | L | Open — **needs the C2VGeometry source, which is not in this repository** |
| 8 | **M1.5 spike (c): AvaloniaEdit plus a Roslyn completion popup** — the last unproven part of M1.5, gating M4 | `E11-T21` | M | Open |
| 9 | **What is left of M2** — real docking (`E8-T2`), group/note/align (`E8-T6`), watch nodes (`E8-T10`), `spark run` (`E12-T5`) | | L | Open |
| 10 | **M3 — NURBS curves** | `E2-T10` … | XL | Open |
| + | **A guard that no test project reports zero tests** — one line, and it catches a truncated test file, a discovery failure and the `dotnet test` anomaly alike ([N30](NOTES.md)) | `E11`-adjacent | S | Open, take it with the next CI change |

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

### 2026-08-29 — Queue 1: the past-participle naming rule (`E2-T49`)

**What.** `Plane.Flip` → `Flipped`, `BoundingBox.Inflate` → `Inflated` (both overloads),
`Interval.Expand` → `Expanded`. Twelve call sites across `Spark.Geometry`,
`Spark.Geometry.Tests` and `Spark.Geometry.Properties`; four lines in
`PublicAPI.Unshipped.txt`; and — the one a grep of `src/` would have missed — **a worked
example in `docs/help/concepts/geometry-basics.md`**, which the standing documentation
instruction is precisely there to catch.

**The part worth more than the rename.** The rule is now a paragraph in `NamespaceDoc.cs`: a
member that returns a new value is named for the **result**, never for the act. It was living in
[DYNAMO-COVERAGE §4](DYNAMO-COVERAGE.md#member-names-we-will-not-copy), which is a survey of
somebody else's API and not where anybody looks before adding a member. In `NamespaceDoc` it
binds every type added afterwards.

**Verified.** `dotnet build Spark.slnx --no-incremental -warnaserror` clean; **952 tests, 0
failures** across seven projects, unchanged from before, which is the correct result for a rename;
`dotnet format` clean; the docs harness green. **The compiler is the verification here** — a
missed call site is a build error — and AGENTS.md step 7, revert-and-watch-a-test-go-red, does
not apply because nothing behavioural changed. Saying so beats silently skipping it.

**Cost.** Under an hour, and the reason to spend it now rather than later is that it is the one
piece of work in the queue that gets strictly more expensive with every type added, and free while
nothing is shipped.

**Two things noticed in passing, neither acted on.** `Interval.MakeIncreasing()` reads as an
imperative too; DYNAMO-COVERAGE §4 explicitly counts it as following the rule, so it was left
alone rather than quietly widening the step. And `Rect.Inflate` in
`src/Spark.UI/Controls/GraphCanvas.cs` is **Avalonia's**, not ours, and must never be swept up in
a rename like this one — it is called six times there.

**Run parameters, decided by the user before the run started:** report at the end of each **queue
item** rather than each step; **M1.6 stays deferred** while this machine has no C++ toolchain.

### 2026-08-29 — Protocol v2: a journal cannot record its own hash

**What.** *Current state* recorded a **Last commit** hash, and the first real step showed that it
cannot: the journal is committed together with the work, so the hash of that commit does not exist
until after the journal is written. Filling it afterwards needs a second commit or an amend, and an
amend changes the hash again.

**Fixed by deleting the requirement rather than working around it.** The row is now **Last
completed step**, which names the step; `git log -1` supplies the hash, and a resuming session runs
that anyway as protocol step 1. The step name is the more useful half in any case — a hash says
where the work stopped, a name says what it was.

**Worth noticing about how it was found.** The protocol survived being written, reviewed and
committed, and failed on its first genuine use. That is the same lesson N28 records about the
native-binary check: proven to detect is not proven to run, and a procedure's first real execution
is part of adding it.

### 2026-08-29 — Queue 2: the three value-layer parity gaps (`E2-T40`)

**What.** `BoundingBox.Intersection`, `Plane.Offset` and `Plane.ByOriginNormalXAxis`. Parity moves
from **92 to 95 of 837**, and from 12/16 to 14/16 on `Plane`. Fourteen new tests; the suite is
**966**, with `Spark.Geometry.Tests` at 327.

**Two of the three were not as trivial as the register called them, and the difference is the
whole value of the step.**

`BoundingBox.Intersection` delegates each axis to `Interval.Intersection` rather than
reimplementing the tolerance rule. That is not tidiness: the only interesting behaviour in either
member is at the boundary — touching faces, gaps narrower than the tolerance — and two independent
implementations of the same rule drift there first. `Empty` forced the one real decision: its
corners are min-above-max on every axis, so an interval built from them and normalised would
report the empty box as overlapping *everything*. The guard is `IsValid` on both operands, and a
named test pins it.

`Plane.Offset` refuses a non-finite distance. Without that, `Plane.WorldXY.Offset(double.NaN)` is
the one route to an invalid `Plane` out of a factory, which would quietly break the guarantee
`default(Plane)`'s whole design rests on.

`Interval.Intersect` was renamed **`Intersection`** in the same change. Adding
`BoundingBox.Intersection` beside `Interval.Intersect` would have created an inconsistency that
gets quoted later, and both types already name their other set operation `Union`. Nouns for set
operations is now written into `NamespaceDoc` beside yesterday's past-participle rule.

**Verified.** Build clean with `-warnaserror`; **966 tests, 0 failures**; `dotnet format` clean;
docs harness green. **The mutation test was run**: removing the `IsValid` guard from
`Intersection` turns `BoundingBoxTests.IntersectionAgreesWithIntersectsIncludingAboutEmptyAndNaN`
red, and the guard was restored afterwards. That is AGENTS.md step 7 done properly rather than
claimed.

**The near miss, which is the part worth reading.** A scripted edit truncated
`InvalidValueTests.cs` to zero bytes, and **the build, the formatter and the suite all stayed
green** — 114 lines of tests had vanished and nothing objected. It was caught by arithmetic: 14
tests added, 327 expected, 319 reported. [N30](NOTES.md) records it, along with the two
consequences — that a quoted test count is load-bearing rather than decorative, and that a
one-line guard asserting no test project reports zero would have caught this, a discovery failure,
and the `dotnet test` anomaly, all three. It is queued.

**Documents.** DYNAMO-COVERAGE §3.1 and its summary table renumbered (the anchor changed with the
heading, and both references were updated), the `E2-T40` row, PRD's FR-81 status line, TODO, and
`docs/help/concepts/geometry-basics.md` — which gained a *choosing a factory* subsection and an
entirely new **§7 Bounding boxes**, because the type had no prose anywhere and a new public member
with no worked example is an unfinished change.

