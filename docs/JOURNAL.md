# Spark — Development Journal

The resumable record of the marathon run to 1.0. **Current state** is where the work is *right
now*; **Log** is how it got there. Everything else in `docs/` says what the product should be —
this file says what is happening.

**Last updated:** 2026-08-31 10:30 +0530
**Protocol version:** 2

---

## Current state

> Rewritten at the start and the end of every step. If a session dies, this is what the next one
> reads. It is deliberately short: detail belongs in the log entry, not here.

| | |
|---|---|
| **Milestone** | **M1, M1.5 and M2 are done** — M2 closed on 2026-08-30. M1.6 is deferred; its Windows toolchain now exists but its Linux leg does not. The work in flight is **M3, NURBS curves** |
| **Working on** | Nothing. Between steps, inside M3 |
| **Step status** | `CLEAN` |
| **Last completed step** | Queue **10**, `E2-T10` step **(g)** — least-squares approximation |
| **Working tree** | Clean at the time of writing; verify with `git status` |
| **Next action** | **Split the `E2-T10` row in [TASKS.md](TASKS.md) before writing any more code against it.** It has absorbed seven steps — knot vector, curve, insertion, exact trim, closest point, degree elevation, interpolation, approximation — and one table cell is no longer a readable place to record what is done and what is not. Give each remaining piece its own row: **fit to a stated tolerance**, **knot removal**, and **split as its own operation**. That is a documentation step and should be committed as one. **Then** knot removal, which is the interesting one of the three: it is the inverse of insertion and the only operation here that is *allowed* to change the curve, so it needs a tolerance and a stated rule for what 'unchanged enough to drop a knot' means — and until it exists, degree elevation's output is exact but not minimal, which is written down in the elevation remarks. |
| **Verify with** | `dotnet build Spark.slnx --no-incremental -warnaserror`, the per-project executables (**1313**: Geometry.Tests 543, Engine.Tests 318, UI.Tests 327, Viewport.Tests 69, Geometry.Properties 43, Architecture.Tests 8, Docs.Verify 5), `dotnet format`, and `dotnet run --project src/Spark.Desktop -- --graph curves --screenshot PREFIX`. **Check the counts** — [N30](NOTES.md). |
| **Blocked on** | Nothing. **Three things need a human**: opening an exported OBJ in a third-party viewer (M1's stated acceptance), watching the first nightly benchmark run, and `wsl --install -d Ubuntu` plus a reboot if M1.6 is to be attempted on this machine rather than on CI. |

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
- **A C++ toolchain exists as of 2026-08-31, and it is half of what M1.6 needs.** Installed and
  **verified by compiling, not by looking**: CMake 4.4.3 and Ninja 1.13.2 on `PATH`, vcpkg at
  `C:\dev\vcpkg` with `VCPKG_ROOT` set, and MSVC 14.51.36231 inside Visual Studio Community 2026
  (`C:\Program Files\Microsoft Visual Studio\18\Community`). A CMake + Ninja + MSVC project
  configures, builds, links and runs; `_MSVC_LANG` reports `202002`, so the standard really is
  applied — `__cplusplus` reads `199711` under MSVC without `/Zc:__cplusplus` and means nothing.
  `cl.exe` is **not** on the ambient `PATH` by design; a build has to source
  `VC\Auxiliary\Build\vcvars64.bat` first, which is what CI does too.
- **There is still no Linux, so `M1.6-C1` is still not satisfiable here.** WSL is not installed and
  `M1.6-C1` is a real OCCT build on **two** operating systems. The Windows leg can now be attempted
  locally; the Linux leg needs `wsl --install -d Ubuntu` and a reboot, or a CI runner. **Do not
  record M1.6 as unblocked** — it is half unblocked, and the half that is missing is the half that
  makes the criterion a cross-platform claim.
- **vcpkg builds ports here.** `vcpkg install zlib:x64-windows` compiled from source and passed
  post-build validation in 32 seconds, so vcpkg finds MSVC, drives CMake and completes a real port
  unaided. That was the cheapest thing that could have failed before M1.6, and it did not.
  **OCCT is a different order of magnitude** — 47 toolkits against zlib's one — so this says the
  pipeline works, not that the OCCT build will.
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
| 3 | ~~`Quaternion` — the last piece of the value layer~~ | `E2-T1` | M | **Done** 2026-08-29 |
| 4 | ~~`RayCaster` and its BVH~~ — landed as `Ray` and `BoundingVolumeHierarchy` — pays for itself across mesh booleans, viewport picking, intersection seeding, and `Curve.ClosestPoint` waits on it | `E2-T15` | L | **Done** 2026-08-29 |
| 5 | **Geometry serialization v1 and the reflection-driven round-trip test** — get it in before there are twenty types to retrofit it onto; there are now **twenty-two** | `E2-T29`, `E2-T31` | M | **Done** 2026-08-29 |
| 6 | **`Spark.Geometry.Io`: the OBJ writer, and `spark` writing a polyline a third-party viewer opens** — this is **M1's demoable** | `E2-T34`, `E12-T5` | M | **Done** 2026-08-29 |
| 7 | **The C2VGeometry test harvest**, timeboxed to one week with a hard stop. Harvest assertions, not generators | `E2-T32` | L | **Blocked** — needs the C2VGeometry source, which is not in this repository and not on this machine. Skipped 2026-08-30 |
| 8 | **M1.5 spike (c): AvaloniaEdit plus a Roslyn completion popup** — the last unproven part of M1.5, gating M4 | `E11-T21` | M | **Done** 2026-08-30 |
| 9 | ~~**What is left of M2** — real docking (`E8-T2`), group/note/align (`E8-T6`), watch nodes (`E8-T10`), `spark run` (`E12-T5`)~~ | | L | **Done** 2026-08-30 |
| 10 | **M3 — NURBS curves** *(in progress)* — knot vector, curve, insertion, exact trim, closest point and degree elevation all done 2026-08-30; knot removal, fit and interpolate remain | `E2-T10` … | XL | Open |
| + | **Persist the workspace layout between sessions** — `WorkspaceLayout` already serialises and round-trips under test; nothing writes it. A dragged arrangement dies with the window, which is the one thing a dock is for | `E8-T2`-adjacent | S | Open |
| + | **A guard that no test project reports zero tests** — one line, and it catches a truncated test file, a discovery failure and the `dotnet test` anomaly alike ([N30](NOTES.md)) | `E11`-adjacent | S | Open, take it with the next CI change |

**Deferred, with a reason rather than by omission:**

- **M1.6 / `E13-T1`** — **half unblocked as of 2026-08-31.** The Windows toolchain is installed
  and verified by an actual compile; WSL is not, so `M1.6-C1`'s *two operating systems* cannot be
  met on this machine yet. Its criteria are written
  ([TASKS.md](TASKS.md#m16--the-passfail-criteria-written-before-the-spike)). The Windows leg is
  now startable; whether to start it before the Linux one is a scheduling question and no longer
  an environment one.
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

### 2026-08-29 — Queue 3: `Quaternion` (`E2-T1`)

**What.** The last type in the value layer. `ByAxisAngle`, `ByRotationBetween`, `operator *`,
`OfVector`/`OfPoint`, `Slerp`, `ToAxisAngle`, `ToTransform`, `TryGetInverse`, `Conjugate`,
`Normalised`/`TryNormalise`, `IsUnit`, `IsSameRotation`, `EqualsWithin` and the equality trio.
**999 tests** now, up from 966: 28 example-based, 4 CsCheck properties, one invalid-value case.

**The design decisions worth having made, rather than the code.**

*Why it exists at all*, since `Transform` already rotates: composition that does not drift into
shear, interpolation that is actually defined, and four numbers instead of sixteen to store. That
paragraph is on the type, because *why is this here beside that* is the question a reader of a
geometry kernel asks first.

*`IsSameRotation` beside `EqualsWithin`.* `q` and `-q` are the same rotation and their components
are not equal. The trap belongs in the API — two methods, each answering a different question,
each documented as such — rather than in a comment on one method that quietly answers both wrong.

*`OfVector` and `OfPoint`, not `Rotate`.* Names the result, matches `Transform`, and obeys the
rule `E2-T49` put into `NamespaceDoc` two steps ago. The convention is already doing work.

*Non-unit quaternions are handled rather than rejected.* `OfVector` divides by the squared
length, which costs a division and no square root, so a long composed chain can be normalised
once at the end instead of at every step — the accurate path and the fast path are the same one.

*Two exclusions, stated on the type.* No matrix-to-quaternion extraction: it needs a policy for a
matrix that is nearly-but-not-quite a rotation, and that policy belongs with the surface work that
first needs it. No Euler angles: twelve conventions, no defensible default, and gimbal lock. An
absence that is written down reads as a decision; the same absence unwritten reads as an
oversight.

**Verified.** Build clean with `-warnaserror`; 999 tests, 0 failures; format clean; docs harness
green. Two independent checks that the convention is right rather than merely self-consistent:
every rotation is compared against `Transform.Rotation` **by example** at four axis-angle pairs
and **by property** across nine decades of scale. A sign error or a half-angle error fails both.
**Mutation-tested**: removing Slerp's short-path negation turns
`SlerpTakesTheShortPathWhenAnInputIsNegated` red — that is the classic quaternion animation bug,
a 45° interpolation becoming a 315° spin, and it is now pinned by a named test.

**Cost.** About an hour. The public-API baseline took a fifth of it, and the way to do that
quickly is worth recording: build with `--no-incremental`, and the RS0016 warnings *are* the
missing baseline lines — extract the symbol names, merge, and `LC_ALL=C sort`, which is exactly
the order the file is already in. Building without `--no-incremental` produced no warnings at all
and looked like success, which is [N15](NOTES.md) biting on its own terms.

**Fixed in passing.** `docs/help/concepts/geometry-basics.md` still told readers that lines, arcs
and circles were *M3 — not written*, four days after the curve layer landed. A help topic that is
wrong about what exists is worse than one that is silent.

### 2026-08-29 — Queue 4: `Ray` and `BoundingVolumeHierarchy` (`E2-T15`)

**What.** The register called this *extract `RayCaster.cs` and its BVH*, and the first useful
thing the step produced was the discovery that the name was wrong for what can exist today.
C2VGeometry's file bundles three things — a ray, an acceleration structure, and ray-triangle
maths — and **there are no triangles in this kernel yet**. So the extraction is `Ray` and
`BoundingVolumeHierarchy`, separately, and separating them is better regardless: a hierarchy over
*boxes* serves mesh booleans, viewport picking, intersection seeding and `Curve.ClosestPoint` from
one implementation instead of four. 25 new tests; the suite is **1024**.

**Decisions written down because each one had a defensible alternative.**

*The ray's direction is normalised on construction*, so a parameter is a **distance** rather than
a multiple of whatever length was passed in. And a ray **excludes what is behind its origin** —
the difference between a ray and a line, and exactly the difference that decides whether a click
selects something behind the camera.

*The split is a median on the index, not on the coordinate.* A coordinate median is the usual
choice and degenerates into a linked list on coincident boxes, which is not a rare input — it is
what a thousand instances at the same location gives you. An index median guarantees
`ceil(log2 n) + 1` depth on any input at all. A surface-area heuristic would beat both on typical
ray-tracing scenes and gives up that guarantee; it is the obvious later change and it is named as
such on the type.

*Invalid boxes keep their index.* Dropping them at build time would renumber every item after
them and silently break the caller's mapping — a bug that surfaces a long way from its cause.
They are indexed and never returned.

*`FirstHit` prunes on the caller's reported distance, not on the box.* A large box entered early
can hold geometry hit late, so pruning on box entry would return the wrong item. There is a test
whose only job is that case.

*Immutable, therefore thread-safe by construction.* `ParallelEvaluationScheduler` runs a level's
nodes in parallel, so anything the evaluator can reach must expect concurrent readers. No query
touches instance state; the traversal stack is on the caller's stack. A `Parallel.For` test says
so rather than a comment.

**Verified.** Build clean with `-warnaserror`; 1024 tests, 0 failures; format clean; docs harness
green. The strongest tests are the two that **agree with brute force**: 200 pseudo-random rays and
200 random regions from fixed seeds, each compared against the linear scan the hierarchy exists to
replace. An accelerator that answers differently from the loop it replaces is worse than no
accelerator.

**[N31](NOTES.md) records the one genuinely subtle thing**, and it is not the tree. The slab test
divides by the direction, and dividing by zero is *correct* — a parallel ray gets ±∞ and the
comparisons handle it. But `0 × ∞` is `NaN`, which happens when the ray is parallel to a slab and
its origin lies exactly on one of the planes, and every comparison against `NaN` is false, so the
obvious implementation reports a **miss** for a ray that grazes the box. That is what a click
along an edge does. Mutation-tested: removing the guard turns
`RayTests.ARayLyingExactlyOnAFaceStillHits` red.

### 2026-08-29 — Queue 5: geometry serialization, and a test that says which types cannot be saved (`E2-T29`, `E2-T31`)

**What.** `GeometryJson` — twenty-two types in and out — and the reflection-driven round-trip test
that fails when a geometry type has neither a sample nor a stated reason for not having one. 12
new tests; the suite is **1036**.

**The format decision that mattered.** Every value carries its **own** `type` and `version`,
nested values included, so a `Circle` document contains a versioned `Plane` containing versioned
`Point3d` and `Vector3d` values. It is verbose, and the alternative — one version at the top of
the document — cannot express the requirement the row states in its own words: *a `NurbsCurve` at
v2 and a `Mesh` at v1 must coexist*. Adding per-type versions later would break every file already
written, so it is not a thing to defer.

**An unknown version is refused by name.** The tempting behaviour is to read what you recognise
and ignore the rest, and it turns a file from a newer Spark into subtly wrong geometry with no
error anywhere.

**Non-finite numbers are written as strings**, because `BoundingBox.Empty` is built from
infinities and is a legal, useful value — the correct seed for accumulating a bound. A serializer
that cannot write what a caller can hold is not finished.

**One deviation from the row, recorded rather than quietly taken.** It is hand-written, not
source-generated. With an explicit converter per type the generator's job is done by hand, and what
it would still buy is trimming and AOT — which ADR-0020 has ruled out for the shipping application.
If trimming returns, so does the decision.

**The test earned its keep on its first run, which is the entire argument for writing it now.**
`BoundingBox.Empty` did not survive a round trip. The public two-corner constructor sorts its
corners — correct, and the reason a caller with two opposite points does not have to think — so it
turns the *inverted* infinite box into the infinite box: the value that contains nothing becomes
the value that contains everything, silently. Nobody would find that by reading `BoundingBox`; it
would have surfaced much later as a graph that opens with everything selected. The fix is an
`internal FromSortedCorners` used by the deserializer alone, and [N32](NOTES.md) carries the
general question to the next value type: **can this type's public constructors reproduce every
value it can hold?**

**Verified.** Build clean with `-warnaserror`; 1036 tests, 0 failures; format clean; docs harness
green. The completeness check was **proven to fire**: deleting the `Ray` sample fails the run and
names `Ray`. It is a two-way diff, so a sample for a type that no longer exists fails too — dead
coverage looks exactly like coverage.

### 2026-08-29 — Queue 6: `ObjWriter` and `spark export` — M1's demoable

**What.** `Spark.Geometry.Io` stops being an empty project and `Spark.Cli` stops being a stub.
`spark export --open docs/examples/curves.spark --out curves.obj --tolerance 0.001` opens the
committed example, evaluates it with **no window anywhere**, and writes ten curves and 1,255
vertices. 9 new tests; the suite is **1045**.

**Writer only, and it is a decision rather than a stage.** An OBJ *reader* would need a position
on materials, groups, negative indices, free-form surfaces and a decade of dialects, in exchange
for importing a format that carries no curves and no precision. Spark's import story is STEP and
`.spark`; OBJ is how geometry leaves.

**A curve becomes a polyline and the tolerance goes in the header.** OBJ's own `curv` elements are
free-form NURBS that effectively no viewer reads. Writing the tessellation tolerance into the file
means *how round is this circle* has an answer inside the artefact rather than in whoever ran the
export.

**The first version of the export rule was wrong, and the failure was the useful part.** It took
the geometry of nodes nothing consumes — ingredients are not results — and exported **nothing at
all** from the example graph, because that graph ends in `Display.ByGeometryColour` nodes whose
output is an appearance. The rule is now every node's outputs, deduplicated **by reference**,
which is exactly right for a pass-through node: `Display` yields the same instance its input had.
The lesson generalises past this bug: **a graph's interesting geometry is routinely mid-chain**,
and a rule that only reads the leaves exports the labelling.

**Invariant culture, always.** A German locale writes `1,5` for one and a half and produces an OBJ
that viewers misread or reject — on some machines only, which is the worst way to find a bug.
There is a test that sets `CurrentCulture` to `de-DE` and asserts the file contains no comma at
all. The Linux CI leg exists partly for this class of difference.

**Verified.** Build clean with `-warnaserror`; 1045 tests, 0 failures; format clean; docs harness
green. The structural traps are pinned by name: **one-based, file-global vertex indices** (the
classic way to write an OBJ that opens and draws nonsense, which is worse than one that fails to
open), no byte-order mark, nulls skipped rather than throwing.

**What is honestly not done.** M1's criterion is *an OBJ polyline that a third-party viewer
opens*, and **no human has opened it**. The structure is asserted by tests; the acceptance is a
person looking at the file. That is written into TODO rather than quietly counted as met, and no
generated `.obj` is committed — a derived artefact in the tree is diff noise, and the command that
produces it is one line.

### 2026-08-30 — Queue 8: M1.5 spike (c), and M1.5 is complete (`E11-T21`)

**The question.** Is AvaloniaEdit plus a Roslyn completion popup acceptable to build the M4 code
block on? **The answer is go.** Five criteria, written before the spike ran, all met. The suite is
**1050**.

**C2 is the one that mattered, and Roslyn answered *nothing* twice before it worked.** Neither
failure raised an error, and an empty completion list looks exactly like a caret with nothing to
suggest. First: `MefHostServices.DefaultAssemblies` composes the *workspace* layer only, and
`CompletionService` lives in Features — without naming the Features assemblies,
`CompletionService.GetService` returns null. Second, and worse: the **document** carries its own
`SourceCodeKind` and it defaults to `Regular`; setting the project's parse options to `Script` does
not override it, and a snippet parsed as a compilation unit is a file of syntax errors about which
the semantic model has nothing to say. Both are in [N33](NOTES.md). With them right, `p.` completes
to `X`, `DistanceTo` and `EqualsWithin` against a type that came from an **expression** — which is
exactly the case M4 promises, *IntelliSense that knows the type on the incoming wire*.

**C3 was the criterion the row predicted would be awkward, and it was, in a way that is now
pinned.** `TextView.GetVisualPosition` answers in **document** coordinates, so a popup anchor is
that minus `TextView.ScrollOffset` — forget the subtraction and the popup is right only on the
first screenful. And `BringCaretToView` does nothing until the view has been laid out once, so the
order is text, layout, caret, scroll, layout. Getting it wrong put the caret fifteen pixels above
the viewport with no error anywhere.

**What the harness cannot answer, stated rather than glossed.** Headless drawing has no font
metrics, so every glyph measures zero wide and the caret's X is always zero. Vertical placement is
real, because line height needs no font. Horizontal placement needs the running application — and
it is the smaller half: a popup one character to the left is a cosmetic complaint, a popup on the
wrong screenful is not.

**Kept rather than deleted, which departs from *throwaway* deliberately.** Spikes (a) and (b) were
UI experiments whose findings survived as prose. This one's central claim — *the caret's position
tracks scrolling* — is executable, and deleting the spike would keep the finding and remove the
thing that notices when it stops being true.

**One repair to the existing tests, and it is worth knowing.** `HeadlessUnitTestSession.StartNew`
can be called **once per process**. A second call from a second test class leaves both sessions
broken and every test in both classes fails with nothing pointing at the cause. The session moved
into `HeadlessSession` and both classes share it.

**Verified.** Build clean with `-warnaserror`; 1050 tests, 0 failures; format clean; docs harness
green.

---

### 2026-08-30 — Queue 7 skipped: the C2VGeometry harvest is blocked

`E2-T32` wants a week of harvesting pure-maths tests out of C2VGeometry, taking **assertions rather
than generators**. The source tree is not in this repository and not on this machine, so it cannot
start here. Recorded as blocked with the reason rather than left looking un-started, and the queue
routes around it exactly as it does around M1.6.

### 2026-08-30 — Queue 9, `E8-T2` step (a): the panes become controls of their own

**What.** `src/Spark.UI/Views/Panes/` — `LibraryPane`, `CanvasPane`, `ViewportPane` and
`InspectorPane`, each a `UserControl` with its own markup and its own handlers. `MainWindow.axaml`
loses 114 lines and keeps its `Grid` and both splitters exactly as they were;
`MainWindow.axaml.cs` loses the creation-box gesture (to `CanvasPane`) and the literal-commit
handlers (to `InspectorPane`), and reaches the canvas and the viewport through two private
properties so that **every remaining call site is unchanged**.

**Why this is a step and not part of the docking step.** `Tool.Content` in `Dock.Model.Avalonia`
is `[TemplateContent]`: pane markup written inline inside a `Tool` is built into its own namescope
and the window's `x:Name` fields are never assigned. That is a `NullReferenceException` at
runtime, not a compile error, and about seven hundred lines of code-behind reach through those
fields. Written up as [N34](NOTES.md#n34--docks-toolcontent-is-templatecontent-so-pane-markup-inside-it-loses-the-windows-names).
**It was established by reflecting over the property's attributes before writing any code**, which
is the difference between one paragraph and an afternoon.

**Verified.** Build clean, 0 warnings; **1050 tests, 0 failures**, unchanged — which is the
correct result for a refactor that moved code without changing it; `dotnet format` clean. **The
gate that actually proves this one is the screenshot**: `--graph curves --screenshot` before and
after are the same picture, down to the library scroll position and the node colours, differing
only in the frame timings, which are timings. The viewport read-back agrees exactly — 53 distinct
colours, mean luminance 30.5/255, both runs.

**AGENTS.md step 7 does not apply**: nothing was fixed, so there is no test to watch go red.
Saying so beats skipping it quietly.

**One thing changed on purpose rather than mechanically.** The benchmark used to hide the viewport
by setting `Viewport.IsVisible = false` on the control; it now hides `ViewportPane`, the whole
pane. With the row collapsed to zero the two were indistinguishable, which is exactly why it was
worth correcting while the code was in hand rather than leaving a line that is right by accident.

**Cost.** Under an hour, most of it reading the seven hundred lines of code-behind carefully
enough to know which handlers were pane-local and which were about the document.


### 2026-08-30 — The shell becomes a dock, and two bugs that looked like working code

**`E8-T2` step (b), and with it `E8-T2`.** Queue **9**'s first item. The shell's `Grid` and
`GridSplitter`s are gone; `MainWindow.axaml` holds a `DockControl` whose tree is built by
`src/Spark.UI/Shell/SparkDockFactory.cs`. Panes drag into one another, float out and dock back.
`Dock.Avalonia.Themes.Fluent` and `Dock.Model.Avalonia` are now `PackageReference`s rather than
pins, and `<dock:DockFluentTheme/>` sits above `SparkStyles` in `App.axaml`.

**The layout model finally does something.** `WorkspaceLayout` has existed since step 0 and until
today nothing consumed it: pressing *Modelling* updated a correct model and moved nothing.
`MainWindowViewModel.WorkspaceChanged` now fires after a preset or a reset, and the window answers
it with `SparkDockFactory.Apply`. The tree is built **once** and adjusted in place, because
rebuilding it would re-parent the OpenGL viewport and buy a black frame for nothing.

**I resumed a part-written step.** The tree was dirty on top of `0829543` and the journal said so,
which is the whole point of the write-ahead. The dock itself was already up and rendering; what
was left was a regression, two defects underneath it, and the debugging scaffolding.

**The regression: every bound row in every pane drew nothing.** The library list with 57 entries
in the view model showed no rows, under a heading reading `LIBRARY` that rendered perfectly. So
did *Nothing selected* and the diagnostics text. Dock puts the **dockable** on the presented
content's `DataContext`, so panes compiled against `MainWindowViewModel` were resolving their
bindings against a `Tool` — and a compiled binding handed the wrong type does not throw, it binds
to nothing. `SetContext` now sets the pane control's `DataContext` as well as `Tool.Context`.
[N35](NOTES.md#n35--dock-puts-the-dockable-on-the-panes-datacontext-and-compiled-bindings-say-nothing-about-it).

**The defect underneath it was worse, and only a test could have found it.** `IsShowing` asked
`tool.Owner is not null`. `HideDockable` leaves `Owner` set — it has to, since that is where
`RestoreDockable` puts the tool back — so the predicate reported every pane as showing, always.
Hiding still worked, because the hide branch ran anyway; **restoring never ran at all**, because
it was guarded by `!showing`. *Presenting* looked perfect and *Reset layout* afterwards did
nothing, and the two side panes stayed gone until the application was restarted. A predicate that
is wrong only in the direction that looks like success survives every screenshot you take of it.
[N36](NOTES.md#n36--hidedockable-leaves-owner-set-so-owner-is-not-null-is-not-is-it-showing).

**Verified.** Build clean with `-warnaserror`, 0 warnings; **1058 tests, 0 failures** — 1050 plus
the eight new ones; `dotnet format` clean. **AGENTS.md step 7:** `SparkDockFactoryTests` is the
named guard, and it is not decorative — `TheDefaultLayoutBringsBackWhatAPresetHid` and
`PresentingHidesTheLibraryAndTheInspector` **went red first and caught the `Owner` defect**, and
`SettingTheContextReachesEachPaneControlAndNotOnlyItsTool` goes red if either half of `SetContext`
is dropped. The screenshot was expected to change and did: the four panes now carry dock title
bars and grips, and the library list, *Nothing selected* and the diagnostics text are all back
after the `DataContext` fix. A preset was verified visually the only way it currently can be — a
temporary line forcing *Presenting* at startup, screenshot, revert — and the shell rearranged as
the model says: no library, no inspector, canvas and viewport full width, the viewport read-back
growing from 1401×516 to 2208×642 because it now owns the whole width.

**What surprised me.** Both defects were invisible to the gate the previous session was leaning
on. The build was clean, the format was clean, the screenshot showed a plausible shell — and two
of the four panes were, in different senses, not working. The screenshot is a good gate for *did
the layout change*; it is a poor one for *does the layout change back*.

**Left undone, and named rather than omitted:** `WorkspaceLayout` serialises and round-trips under
test but is never written to disk, so a dragged arrangement does not survive a restart. That is
now a queue item rather than an implication.

**Cost.** About an hour and a half, of which the tests were half and worth it twice over.

### 2026-08-30 — Align, and a headless failure that came from somewhere else

**`E8-T6` step (a) — align.** Queue **9**'s second item, taken in three steps because group and
note need something align does not: a file format that carries an object the engine never reads.
Align moves nodes, and nodes already save and already undo, so it is the slice that stands alone.

**`src/Spark.UI/Canvas/CanvasAlignment.cs`** is six alignments and two distributions as arithmetic
over `CanvasBounds` — no Avalonia type in the signature, so every case is a unit test rather than a
window and a gesture, which is the same argument that keeps `SceneIndex` and the LOD rules pure.
`GraphCanvas.AlignSelection` adds the three things arithmetic cannot know: which nodes are
selected, that the spatial index has to be told, and that the edit does not require a run — a
position is not in a node's provenance, exactly as a drag already argues.

**Two decisions worth their sentences.** *Distribution equalises gaps, not centres*: a node's
height is its port count, so no two are alike, and evenly spaced centres leave a wide node visibly
crowding its neighbours while the arithmetic insists everything is even. And *one toolbar button
with a flyout, not eight buttons* — aligning is occasional, and eight of them would push the
things used every minute off the end of the bar. The button disables below two selected nodes and
the two distribute items below three, so nothing on it is ever offered and then ignored.

**Align does not need the group question answered.** The journal flagged *is a group a document
object or a canvas annotation* as expensive to reverse. It is, and it is still open — but it
belongs to the steps where the file format actually changes, and deciding it early would have been
deciding it with less information.

**The thing that cost the time was not align.** Five of the seven new canvas tests failed with
`ObjectDisposedException` thrown inside `DrawText`, on a stack naming `FontManager` and the
control's `Render` and nothing in the test file. The cause is that a headless window left open has
its pending frame drained during the session's teardown, after the application — fonts included —
has been disposed. It presents as flakiness in the new code: it lands on whichever tests are still
draining, so adding an unrelated class can turn green tests red. One `finally { window.Close(); }`
fixes it. [N37](NOTES.md#n37--a-headless-window-left-open-renders-after-the-fonts-are-gone).

**Verified.** Build clean with `-warnaserror`; **1076 tests, 0 failures** (1058 + 18);
`dotnet format` clean. **AGENTS.md step 7:** `AligningTwiceRecordsOneEdit` is the named guard —
revert the *did anything actually move* check and it goes red, which is the N19 lesson the drag
gesture already had to learn, arriving here from a second direction. `AnAlignedNodeIsHitTestableWhereItNowIs`
is the guard for the index update, and it is the bug this epic has already been bitten by once.
The screenshot shows **Align ▾** on the toolbar, correctly greyed with nothing selected.

**Cost.** About an hour, of which the headless teardown was half.

### 2026-08-30 — Notes reach the file, and the version rule that let them

**`E8-T6` step (b)** — notes as far as the model and the file. Drawing them is step (c); this step
is the half that is expensive to change later, and it is the half with a decision in it.

**A note is a canvas annotation, not a document object.** No `NodeId`, no ports, no provenance,
never evaluated, nothing can wire to it. So it goes into `GraphDocument` beside the node
coordinates — the existing precedent for data the file must remember and the evaluator must never
read — and never into `Graph`. `GraphNoteTests.ANoteNeverReachesTheGraph` is the assertion that
keeps it there. ADR-0017 already listed notes as `.spark` content, so this was planned rather than
invented.

**The version rule is the part I did not expect to have to decide.** Carrying notes changes the
format, and the obvious move is to bump `CurrentFormatVersion` to 2 and write 2 from now on. That
is wrong here, and [ADR-0016](adr/0016-no-dynamo-interoperability.md) is what makes it wrong: a
graph referencing a missing package has to re-save **byte-identically**, and stamping every save
with the current version would rewrite the first line of every version-1 graph in existence the
first time anybody opened one.

So **the version written is the minimum version that can read the file**, derived from content. No
notes, and the file is version 1 to the byte, exactly as before. Notes, and it is version 2 — and
a version-1 build then refuses it loudly rather than opening it, showing the graph, and throwing
every note away on the next save. The `notes` key is omitted entirely rather than written as an
empty array, for the same reason: `"notes": []` would add two lines to the diff of every graph
that has never had a note in it.

**ADR-0017 requires a golden-file test against a real old-version graph**, and there is one:
`docs/examples/curves.spark` is version 1, and `TheCheckedInVersionOneExampleReSavesUnchanged`
opens it and asserts the bytes back. It is the test that would have caught an unconditional bump.

**Verified.** Build clean with `-warnaserror`; **1094 tests, 0 failures** (1076 + 18);
`dotnet format` clean. **AGENTS.md step 7:** `AGraphWithNoNotesIsStillWrittenAsVersionOne` and
`TheCheckedInVersionOneExampleReSavesUnchanged` are the named guards — set the version
unconditionally and both go red.

**One thing the gates caught that I had got wrong.** I cited ADR-0016 in the journal and in an XML
doc comment as `0016-not-a-dynamo-fork.md`, which is not its filename. `Spark.Docs.Verify`'s
relative-link check failed on it. That check has now earned itself twice — it caught two dangling
ADR citations when it was extended to build files, and now a third from prose written this
afternoon.

**Cost.** About fifty minutes, most of it reading two ADRs before writing anything, which is what
turned a version bump from a one-line change into the right one-line change.

### 2026-08-30 — Notes become visible, and a third thing the canvas can select

**`E8-T6` step (c).** The model and the file landed in step (b); nothing drew them. Now a note is
a rectangle on the canvas that can be made, moved, typed into and deleted.

**Drawn behind the wires and the nodes**, on `canvas.group` — the design language's existing
surface for canvas annotation, reused rather than duplicated because a note and a group are the
same kind of thing to a reader, and because its text contrast is already verified at 14.58:1 by
`PaletteContrastTests`. A note drawn over its own nodes would be annotating them by hiding them.

**A third kind of selection.** `_selection` is a set of *slots* that index `Graph.Nodes`; a note
has no slot, and giving it a fake one would have made every existing loop over the selection wrong
in a way the compiler could not see. So `SelectedNote` sits beside `SelectedWire`, and selecting a
note clears the node selection — the two cannot be dragged or deleted together without Delete
having to answer which it meant.

**Hit-tested by a linear scan, not through `SceneIndex`**, and that is a decision rather than an
omission. The index earns itself over thousands of nodes; a graph with thousands of *notes* is not
a thing anybody has, and a second index would be a second structure to keep in step for a loop
currently shorter than the call that would replace it. Written down so the next person can see it
was weighed. Notes lose clicks to nodes and to wires, matching the order they are drawn in.

**The text is typed in the properties pane, not on the canvas.** The canvas is one immediate-mode
surface that hosts no controls at all — [ADR-0013](adr/0013-immediate-mode-node-canvas.md), and it
is what lets two thousand nodes draw in a frame — so putting a caret in it would mean writing a
text editor to avoid writing a binding. The pane raises `NoteEdited`, the window tells the canvas
to redraw and the shell to record a step. `CommitNoteText` returns whether anything changed, so
clicking into the box and out again without typing records nothing: the same rule the node drag
and the alignment both had to learn, arriving for the third time.

**One thing found by looking rather than by testing.** With the notes seeded and *Zoom to fit*
pressed, a note beside the graph sat off the edge. `ComputeBounds` only knew about nodes. It is
the one gesture whose whole promise is that nothing is off the edge any more, so it now fits the
document rather than the part of it that evaluates.

**Verified.** Build clean with `-warnaserror`; **1106 tests, 0 failures** (1094 + 12);
`dotnet format` clean. **AGENTS.md step 7:** `ANodeOnTopOfANoteStillWinsTheClick` and
`DraggingANoteBackToWhereItStartedRecordsNothing` are the named guards. The screenshot is the
verification that matters here and it was taken twice — the first attempt drew nothing, because
the seed ran in `OnDataContextChanged` before `BindGraph` replaced the canvas's graph, which is a
fact about the shell's startup order rather than about notes. The second shows a plain note behind
an overlapping node, a selected note carrying the same accent ring a node gets, and the properties
pane editing its text.

**Cost.** About an hour and a quarter.

### 2026-08-30 — Group, which closes `E8-T6` — and a regression the screenshot caught

**`E8-T6` step (d), and with it `E8-T6`.** Pan, zoom, box select, drag, wire, unwire, delete,
group, note and align: all of it.

**A group stores which nodes it contains and derives its rectangle.** The alternative — store a
rectangle, decide membership by containment — is what most editors do, and it is why a node can
quietly join a group it was merely dragged past, or leave one it was nudged out of, with no record
in the file of what the group used to hold. Membership by identity means a node leaves a group only
when somebody says so. `AGroupsMembershipDoesNotChangeWhenANodeIsDraggedOutOfItsFrame` and
`ANodeDraggedIntoAFrameDoesNotJoinTheGroup` pin both directions.

**Only the title strip takes a click**, and this is the decision that makes groups usable rather
than infuriating. A group's rectangle is mostly the gap between its own nodes. A frame that took
clicks across all of it would make a node inside a group unclickable and a marquee inside one
impossible — which is the gesture people reach for most once nodes are grouped.
`OnlyTheTitleStripTakesTheClick` is the guard.

**Deleting a group keeps its nodes.** Named, tested, and the one users arrive expecting to go
wrong.

**The format grew a second array and did not grow a second version.** Groups are version 2, the
same as notes: inventing a 3 for the second field to land in the same week would refuse a file to
a reader that can in fact read it. Written up together with the version rule itself as
[N38](NOTES.md#n38--a-format-version-is-the-minimum-version-that-can-read-the-file-not-a-stamp-of-the-writer),
because the rule is now load-bearing for two fields rather than one and the next person to add a
third should not have to re-derive it.

**Verified.** Build clean with `-warnaserror`; **1130 tests, 0 failures** (1106 + 24);
`dotnet format` clean. The screenshot shows a group framing `Plane.XY` with its title in the strip,
the accent selection ring a node gets, and the properties pane reading *1 node. Deleting the group
leaves them where they are.* over an editable title.

**The screenshot also caught something that is not about groups at all.** The frame statistics read
`zoom 100%, 7/18 nodes drawn`. Before the shell became a `DockControl` they read `zoom 35%, 18/18`.
**Zoom-to-fit at startup stopped fitting when the dock landed**, and I did not notice at the time
because the picture was *expected* to change and I read the dock chrome instead of the numbers.
`GraphCanvas.ZoomToFit` returns silently when its `Bounds` are still zero, and inside Dock the
canvas is laid out later than it was inside a `Grid`. That is the next step rather than a quiet fix
here, because it is a separate defect with a separate lesson — **a guard that returns silently is a
bug that waits for a layout change** — and folding it into a group commit would hide both.

**Cost.** About an hour and a half.

### 2026-08-30 — The fit that was asked for and dropped

**A regression, not a feature.** `E8-T2` step (b) introduced it; the `E8-T6` step (d) screenshot
caught it three commits later. `--graph curves --screenshot` read `zoom 100%, 7/18 nodes drawn`
where before the dock it read `zoom 35%, 18/18`.

**The cause is one line that was correct.** `GraphCanvas.ZoomToFit` opened with
`if (Bounds.Width < 1 || Bounds.Height < 1) { return; }`, which is true — you cannot fit a graph
into a control with no size — and was fine for months. Then Dock began laying its content out later
than the `Grid` had, the startup fit started arriving before the canvas's first arrange, the guard
did its job, and the request evaporated.

**The repair is to make the impossible request pending rather than discarded**, and to put it on
the canvas rather than re-timing the call from the window. Asking the shell to call `ZoomToFit`
later would put the container's layout schedule into the window's head, and the next container
change would break it again in exactly the same silent way. `ArrangeOverride` performs a deferred
fit at the first arrange that produces a real size, once — a canvas that re-fitted on every arrange
would throw away the user's pan every time a pane was resized, which is a worse bug than the one
being fixed. There is a test for that too.

**Verified.** Build clean; **1133 tests, 0 failures** (1130 + 3); `dotnet format` clean.
**AGENTS.md step 7, done properly:** `AFitAskedForBeforeLayoutHappensOnceThereIsALayout` was run
against the reverted fix and **went red**, then green with it restored. The screenshot now reads
`zoom 37%, 18/18 nodes drawn` — 37 rather than the old 35 because the dock chrome takes a little
of the pane, which is the honest difference.

**What this cost, and the lesson.** Three commits of a shell that opened wrong, and it was visible
in every screenshot I took in that time. The number was in the corner of each one and I read the
chrome instead, because the picture was *supposed* to have changed. Written up as
[N39](NOTES.md#n39--a-guard-that-returns-silently-is-a-bug-waiting-for-a-layout-change): **when a
precondition cannot be met yet, decide between refusing loudly and deferring — returning quietly
is neither, and it is the one that survives every test you have.** That is the fourth note in this
file about an API that answers *nothing* where it means something specific, so it is stated as a
rule rather than as another anecdote.

**Cost.** Twenty-five minutes, against three commits of being wrong.

### 2026-08-30 — Preview bubbles, and the rank line that is the point of them

**`E8-T10` step (a).** `CanvasNode.ResultSummary` has existed since the walking skeleton and has
only ever reached the properties pane. Now a bubble under the node says what it produced.

**Rank gets its own line, and that is the feature.** The `E8-T10` row says *must show rank, not
only value — rank is what users get wrong*, and it is right: `[[1], [2]]` and `[1, 2]` read alike
at a glance and behave completely differently under lacing, and a node that quietly produced a
list of lists is the commonest way a graph goes wrong without ever erroring. So `ResultRank` and
`ResultCount` became fields rather than being left as a substring of the summary, and the bubble
reads `rank 1 · 8 items` above the value. **Rank 0 says *one value*, never *0 items*** — a scalar
and an empty list are precisely the two things the line exists to tell apart, and wording them
alike would defeat it at the one moment it matters.

**Only the hovered node and the selected nodes get a bubble**, and that is a budget decision as
much as a design one: laying out text for two thousand nodes would spend `E8-T15`'s entire 16.7 ms
frame on strings nobody is reading. It is also the better design — a bubble answers *what is this
one doing*, and a permanent readout is what a `Watch` node is for, which is step (b). The
consequence worth noting is what it avoided: no per-node toggle, and therefore **no new field in
the file format** three days after the last two.

**Two things the screenshot found that the tests could not.** First, the bubble read
`rank 1 · 8 items` above `8 items, rank 1  [...]` — the same fact twice, in two wordings, in
adjacent lines. `Summarise` had been prefixing rank and length since before `RankLine` existed; it
now renders the value and nothing else, and the properties pane composes the same two lines the
bubble does. Second, and more serious: **`--zoom` stopped working**. The deferred fit added in the
previous step was firing on a later arrange and overwriting a zoom set deliberately afterwards. A
pending fit now records where the view was when it was deferred and **stands down if anything has
moved it since** — a more recent instruction wins. That is a defect I introduced an hour ago and
would not have found from the failing test I wrote for it, because the test only asked whether the
fit happened.

**Verified.** Build clean with `-warnaserror`; **1141 tests, 0 failures** (1133 + 8);
`dotnet format` clean. The canvas benchmark over 2000 nodes reads **1.54 ms median, 3.40 ms p95**
against a 16.7 ms budget — unchanged from the 1.60 ms on record, which is the number that had to
not move. The bubbles were photographed by temporarily drawing them for every node, since a
screenshot cannot hover and the selected node was outside the pinned view; the *which nodes* rule
is covered by the code and the tests rather than by that picture, and saying so is better than
implying the picture proved more than it did.

**Cost.** An hour, a third of it on the `--zoom` regression.

### 2026-08-30 — The watch node, and one attribute that is the whole node

**`E8-T10` step (b), and with it `E8-T10`.** A bubble answers *what is this node under my pointer
doing*. A watch answers *what is happening here*, while you go and look at something else. That is
the whole difference, and it is why a watch is a node you place rather than a per-node toggle —
which would also have meant a fourth new field in the file format this week.

**`[KeepStructure]` on the input port is the node.** `object` is rank 0, so a plain `object` port
replicates: the engine would have handed the watch one item at a time, and the list is precisely
what somebody opened a watch to look at. The attribute already existed for exactly this shape of
problem; finding it was worth more than writing it would have been.

**How the canvas knows a watch is a watch, without naming one.** The canvas has no node library and
must not name an engine type — [ADR-0005](adr/0005-api-engine-host-layering.md), and the `E8-T19`
row already writes the rule down for the double-click search box. So the node **declares** it:
`[ShowsValue]` in `Spark.Api`, surfacing as `NodeDefinition.ShowsValue`, travelling the same route
`Category` already travels — a fact the engine carries for the shell and never reads itself.
**A `NodeCategories.Watch` was considered and rejected**: it would have meant inventing a
design-language colour, and a contrast-verified row in `PaletteContrastTests` to go with it, to
answer a question that has nothing to do with how the node is painted.

**The watch panel is a second rendering of the value on purpose.** `Summarise` cuts at sixty
characters, which is right for a bubble and useless for reading, so `Expand` renders it in full —
capped at 20,000 characters, with the cut **announced**. A truncation that trails off is one a
reader mistakes for the end of their data.

**Verified.** Build clean with `-warnaserror`; **1155 tests, 0 failures** (1141 + 14);
`dotnet format` clean. The screenshot shows the node on the canvas, the library at 58 nodes, and
the **WATCH** section rendering `rank 1 · 5 items` above `[1, 2, 3, 4, 5]`.

**What I did not photograph, and why I am saying so.** The bubble under a *watch* node is not in
that picture. The temporary harness that placed the node re-evaluated the graph from inside the
capture callback and left `ResultSummary` null by render time — an artefact of poking the model in
a way the application never does, not a defect: the previous step's screenshot shows bubbles
rendering. Rather than debug the harness, the **rule** moved out of the drawing into
`GraphCanvas.ShowsPreview`, which three tests now pin — a watch shows with nothing selected and
nothing hovered, an ordinary node does not, and an impossible slot is false rather than an
exception. The decision is the part with a judgement in it; the pixels were already proven.

**Cost.** An hour and a quarter, and it closes the last of queue 9's canvas work.

### 2026-08-30 — `spark run`, and making "identical output" structural

**`E12-T5`'s `run` verb**, which is queue **9**'s last item. `spark export` already opened a
`.spark`, restored it against `SparkSession.Library` and evaluated it with no window, so the verb
itself was half a day's worth of plumbing. The row's other sentence was the work.

**"`spark run` must produce output identical to the desktop app's."** That is a claim about two
programs, and the value rendering lived in `CanvasGraph.Summarise`/`RankLine`/`Expand` — inside
`Spark.UI`, which the CLI must not reference. Writing the CLI's output by hand would have made the
row true on the day it was ticked and false soon after, and nothing would have noticed. So the
rendering moved **down**: `Spark.Api.ValueText`, beneath both, with `CanvasGraph` delegating to it
and the CLI calling it directly. `ValueRenderingTests` asserts the delegation, so the day somebody
reintroduces a second rendering is the day a test goes red rather than the day the two silently
diverge.

**What it prints by default is the watch nodes**, which is what a watch is for and what makes the
agreement visible: the same node, the same line, in the application and on the command line.
`--all` prints every node, for a diff. Diagnostics go to stderr and values to stdout, so
`spark run g.spark > values.txt` captures the answer and still shows the problems.

**Two things found by running it rather than by writing it.** The values printed in
`Graph.Nodes()` order, which walks a dictionary — so two runs of one file could print the same
values in a different order, defeating the only reason to print them at all. They now follow the
**document's** order, which is sorted by identity for exactly this reason. And the rank line's `·`
came out mangled: a Windows console defaults to a code page that cannot represent it, **including
when the output is redirected to a file**, which is the case that matters.
`Console.OutputEncoding = UTF8`, guarded for the no-console case.

**Verified.** Build clean with `-warnaserror`; **1171 tests, 0 failures** (1155 + 16);
`dotnet format` clean. `spark run docs/examples/curves.spark --all` run twice into two files and
`diff`ed: **identical**, and readable UTF-8. A hand-written graph carrying a `Watch.Value` wired to
a `Number.Range` prints `Watch.Value  rank 1 · 6 items  [0, 1, 2, 3, 4, 5]` — which is also the
end-to-end proof of `[KeepStructure]`: without it the watch would have been replicated and the run
would have printed six scalars.

**One constant was lying and is not now.** `SummaryLength = 60` named a threshold but produced 58
characters, because the ellipsis was added after a 57-character cut. It now means what a caller
needs it to mean — the longest a summary can be, ellipsis included.

**Cost.** Fifty minutes. **Queue 9 is finished**: `E8-T2`, `E8-T6`, `E8-T10` and `E12-T5`'s `run`
are all done, which is what was left of M2.

### 2026-08-30 — M3 begins underneath the curve, at the knot vector

**Queue 10, `E2-T10` step (a).** The first geometry work in a while, and the first slice is
deliberately not the curve.

**`Curve` is abstract over six members** — `Domain`, `IsClosed`, `Evaluate`,
`EvaluateDerivative`, `EvaluateSecondDerivative`, `Reversed`, `Trimmed`, `TransformedBy` — so
**a `NurbsCurve` cannot exist half-built.** There is no slice of it that compiles and does less.
What there is instead is the piece underneath it, and that piece happens to be the one that
matters most: almost every hard-to-diagnose spline fault is a knot-vector fault, and every one of
them is arithmetic that needs no curve, no control points and no evaluation to test.

**`KnotVector` owns the invariants** — non-decreasing, finite, at least `2p + 2` knots, interior
multiplicity at most `degree`, end multiplicity at most `degree + 1`, and a domain that is not a
point — enforced in the constructor and never re-checked, which is what lets the curve be written
assuming they hold. It also owns the two things that are always subtly wrong: `Domain`, which runs
between the **interior** end knots and not the first and last, and `FindSpan`, whose special case
at the very last parameter is the difference between evaluating at `t = 1` and reading past the
control points.

**Equality is exact, and that is a decision.** A knot vector is data: two vectors are the same or
they are not, and a tolerant `Equals` would let `a == b` and `b == c` fail to imply `a == c`. Where
the tolerant question is the right one — *is this end knot repeated `degree + 1` times, after a
refinement has drifted the arithmetic* — `Multiplicity` takes a `Tolerance` and answers it.

**The reflection round-trip test caught the new type within seconds of it existing**, which is
`E2-T31` doing exactly what it was built for: *a new type that forgets serialization fails the
build*. It has now earned itself on the first type added since it was written. Adding the sample
then failed a second time, on `Assert.Equal` falling through to reference equality — which is how
the type came to have value equality at all. **Two gates in sequence each told me something I had
not thought about**, and neither was a test I wrote.

**Verified.** Build clean with `-warnaserror`; **1196 tests, 0 failures** (1171 + 25);
`dotnet format` clean. The basis functions are asserted to be a partition of unity at 41 parameters
across four degrees, and non-negative across the domain — the two properties whose failure looks
like a modelling mistake rather than a kernel one.

**Also done: the documents caught up with reality.** `TODO.md` still said M2 was outstanding and
quoted a test count from two days and 244 tests ago.

**Cost.** Fifty minutes.

### 2026-08-30 — `NurbsCurve`, checked against curves that already work

**`E2-T10` step (b).** Control points, weights and a `KnotVector`, satisfying the whole `Curve`
contract — because `Curve` is abstract over all of it and there is no smaller version that
compiles.

**Homogeneous throughout.** Each control point is stored as `(w·x, w·y, w·z, w)`, de Boor runs on
those four components, and the projection happens once at the end. Dividing at every step of the
recurrence is slower, less accurate, and makes the derivative formulae unreadable — the quotient
rule applies once to `C = A/w` instead of being threaded through the recursion.

**Weights are refused if they are not positive.** A zero or negative weight lets the denominator
reach zero somewhere inside the domain, and the curve then has a pole in it: a parameter that looks
exactly like its neighbours returns infinities. The constructor is the only place that failure can
be attributed to its cause.

**The tests that matter are the agreement tests, and they are why I trust the result.** A spline
implementation can be entirely self-consistent and entirely wrong. What cannot be faked is a
degree-1 curve agreeing with `Line` and with `PolyLine`, and a rational quadratic with weights
`1, cos 45°, 1` being *exactly* a quarter circle — every sampled point at the radius to nine
decimal places. Those two check the arithmetic against geometry this repository already trusts,
written long before this and exercised by hundreds of tests of their own. The derivatives are
checked against central differences for the same reason: an analytic derivative verified against
the analysis that produced it proves only that it was copied consistently.

**One real defect, found by a test that was about something else.** `Curve.ComputeLength`
integrates over equal spans across the whole domain, and a NURBS curve's speed is generally
**discontinuous at every interior knot** — a degree-1 curve is the extreme case, being a polyline
with piecewise-constant speed. Gauss–Legendre across a corner is wrong by an amount that looks like
rounding. `ComputeLength` and `TessellationSeedSpans` now both work one knot span at a time, so
every piece the rule sees is smooth. The polyline-agreement test is what surfaced it.

**Two gates spoke again.** `EveryPublicGeometryTypeHasASample` caught `NurbsCurve` the moment it
existed, exactly as it caught `KnotVector` an hour earlier. And `AnUnknownTypeIsRefused` failed —
because it used the string `"NurbsCurve"` as its example of a type this build does not know, which
was true when it was written and stopped being true today. **Naming a planned type as a stand-in
for an unplanned one is a trap**, and the fix says so in a comment beside a name that can never
become real.

**Verified.** Build clean with `-warnaserror`; **1222 tests, 0 failures** (1196 + 26);
`dotnet format` clean.

**Left undone and named:** knot insertion, and therefore `Trimmed`, which throws
`NotSupportedException` naming what is missing rather than returning an approximation a caller has
no way to detect. Degree elevation, split, closest point, fit and interpolate are the rest of the
row.

**Cost.** An hour and a quarter.

### 2026-08-30 — Knot insertion, and a trim that is exact rather than nearly

**`E2-T10` step (c).** Boehm's algorithm, and the operation it unlocks.

**Insertion is done on the homogeneous control points.** Blending the projected ones is the classic
mistake: it is right for a non-rational curve, wrong for a rational one, and produces a curve that
is *visibly close* to the original and not equal to it — which is the hardest kind of wrong to
notice, because everything looks fine.

**The test that proves it is the one that says nothing changed.** Insert a knot anywhere, and every
one of two hundred sampled points must be identical to ten decimal places while the control-point
count goes up by exactly one. Both the rational and the non-rational sample are checked, because
the homogeneous mistake passes the second and fails the first. And the arc stays a circle: after
inserting a knot into the rational quadratic, every sampled point is still at the radius to nine
places.

**`Trimmed` stopped throwing.** It raises both ends to full multiplicity and keeps the control-point
window between them, so it is **exact**: the trimmed curve occupies the same points the original did
over that range, to nine decimal places, rather than an approximation a caller has no way to detect.
It also keeps the requested parameter range as its domain instead of reparameterising to 0..1, so
parameters a caller was already holding still mean what they meant.

**The index arithmetic was wrong the first time, and the tests said which.** Insertion passed
immediately; all five trim tests failed. The fix was to derive the window from the knots rather than
count it: the **last** knot equal to the start and the **first** equal to the end, with the window
running from `la - degree` to `fb - 1`. Last-of-the-start rather than first, because a clamped
curve's own ends already repeat `degree + 1` times — taking the first would land one index early on
exactly the case where the range is the whole domain, which is the case `TrimmingToTheWholeDomainIsTheSameCurve`
covers.

**Verified.** Build clean with `-warnaserror`; **1236 tests, 0 failures** (1222 + 14);
`dotnet format` clean. `TrimmingSaysItIsNotBuiltYet` was **replaced rather than deleted** — a test
that a feature is missing is a reminder to remove when it arrives, and the eight tests that took its
place are what it was standing in for. `TwoAbuttingTrimsCoverTheWholeCurvesLength` is `E2-T33`'s
*Split(t) rejoined equals the original* property applied to NURBS before the property suite has a
generator for one.

**Cost.** Fifty minutes, a third of it on the extraction window.

### 2026-08-30 — Closest point, and it belonged on `Curve`

**`E2-T10` step (d).** The step was written as *closest point on a NURBS curve*, and the first
thing looking found was that **there was no `Curve.ClosestPoint` at all** — not on the base class,
not on `Line`, not anywhere. So it landed where it belongs, and every curve type gained one at
once.

**Two stages, because neither works alone.** Newton finds *a* stationary point of the distance and
there are usually several; on a closed curve the wrong one is the far side. So a coarse sweep
brackets the basin first, proportional to `TessellationSeedSpans` so a curve made of many pieces
gets a proportionally finer bracket, and Newton then refines on `(C(t) − P) · C′(t) = 0` — the root
where the vector to the point is perpendicular to the tangent, which is what *nearest* means.

**Three guards, each for a specific failure.** Every iterate is **clamped to the domain**, or a
point off the end of an open curve drives the iteration past the last parameter and the answer
comes back as a parameter the curve does not have — which then throws somewhere else, in a caller
that did nothing wrong. A step that **does not improve the distance is rejected**, because Newton
near an inflection can overshoot into a worse basin and return a point further away than the one it
started from. And the iteration count is capped, so a pathological curve cannot spin.

**The property is the test, and it is the whole test.** `E2-T33` states it: *ClosestPoint is never
farther than any sampled point.* Two thousand samples per probe, six probes, **eight curve types** —
line, circle, arc, ellipse, polyline, polycurve, and NURBS both rational and not. That one
assertion fails for every way the search can go wrong, which is why it is worth more than any
number of hand-picked cases. `Line` answers in closed form and is the cross-check: the general
search has to agree with arithmetic that cannot be wrong.

**Verified.** Build clean with `-warnaserror`; **1272 tests, 0 failures** (1236 + 36);
`dotnet format` clean. All 36 passed first time, which is unusual enough to be worth saying — the
derivatives they lean on were themselves checked against central differences two steps ago, and
that is what a foundation being right looks like from above.

**Cost.** Forty minutes.

### 2026-08-30 — Degree elevation, and a flake the gates coughed up

**`E2-T10` step (e).** Elevation by **Bézier decomposition**: insert knots until every interior one
has multiplicity `degree`, which turns the curve into a chain of Bézier segments sharing endpoints;
elevate each segment, where the rule is a one-line blend; reassemble. The direct algorithm is faster
and considerably longer, and all of its extra complexity is in *avoiding* the decomposition — a
trade worth making later, with a benchmark, and not now.

**It is exact but not minimal, and saying so is the point.** Decomposing raises every interior knot
to full multiplicity and nothing here lowers it again, so a curve that was smooth across a knot
comes back describing the same shape with more control points than it needs. That is a
representation cost, not a geometric one — every sampled point is identical to ten decimal places.
Removing it needs knot removal, which carries a tolerance question — *how nearly equal must two
curves be before a knot may be dropped?* — and answering that casually inside an operation whose
whole promise is that it changes nothing would be the worst place in the kernel to put an
approximation.

**Nine tests, all green first time**, including two that check elevation against something other
than itself: a line stays a line when elevated to degree 3, and the rational quarter circle is still
a circle to nine places — which a blend done on the projected points instead of the homogeneous ones
would fail. `ElevatingAndTrimmingCommute` is the one I would keep if I could keep only one: two
operations that each claim to preserve shape had better agree with each other.

**The gates coughed up something that is not mine.**
`ValueLayerProperties.TheSignedAngleBetweenTwoVectorsDoesNotDependOnTheirLengths` failed once,
in `Spark.Geometry.Properties` — a project this session has not touched — and then passed five runs
out of five. **A gate that fails at random is worse than no gate**, because the next session runs
it, sees red, and has to work out whether it inherited a broken tree, which is precisely the cost
the journal exists to remove. So it is the next step rather than a footnote, and the *Next action*
says to confirm the cause before changing anything: a guess that happens to make a failure stop is
not a fix.

**Verified.** Build clean with `-warnaserror`; **1281 tests** with the one intermittent failure
described above and nothing else; `dotnet format` clean.

**Cost.** Forty minutes.

### 2026-08-30 — The flake, diagnosed from its seed rather than guessed at

**Not a feature step.** The degree-elevation gates threw one failure in
`Spark.Geometry.Properties`, a project this session had not touched, and then passed five runs out
of five. A gate that fails at random is worse than no gate, so it came before more feature work.

**I guessed twice and was wrong twice.** Both guesses were that the generated turn had landed near a
multiple of 360°, making the two vectors nearly parallel or nearly opposed. I wrote a hand-rolled
search over the generator's own space — **four hundred thousand trials** — and it found nothing,
because a uniform draw over −720°..720° essentially never produces the value that matters.

**Running the suite forty times and reading CsCheck's counterexample took two minutes and gave the
answer outright.** The turn was `-3.844e-15°`. Vanishingly small, not near anything. The two vectors
are the same direction to within about `1e-17` radians, and scaling them by `0.01` and `4.05e-5`
sends the cross product to *exactly zero*. So `Math.Sign(+1e-17)` is 1, `Math.Sign(0.0)` is **0**,
and the assertion read *no sign at all* as *the opposite sign*. The property under test — that the
angle does not depend on the lengths — held the entire time. **The code was right and the assertion
beside it was over-strict**, which is why the failure was rare and looked like nothing.

**The fix guards the sign comparison** by the angular tolerance already in the file: below it, two
directions are the same as far as this assembly is concerned, so their relative sign is not a fact
about the geometry. Written up as
[N40](NOTES.md#n40--mathsign-of-a-near-zero-value-is-a-third-answer-not-the-other-sign), whose
second lesson is the expensive one: **do not guess at a randomised failure — the seed is the
evidence.**

**Verified.** `CsCheck_Seed=3Y_SvlbuBiDf` reproduces the failure with the guard reverted and passes
with it, which is AGENTS.md step 7 exactly. Then **thirty consecutive clean runs** of the suite,
because one clean run proves nothing about a test that failed one time in forty. Build clean with
`-warnaserror`; **1282 tests, 0 failures**; `dotnet format` clean.
`ASignedAngleTooSmallToHaveASignIsNotAsserted` pins the counterexample as an ordinary test, so the
case survives even if CsCheck's seed format ever changes.

**Cost.** Thirty-five minutes, twenty of them spent on two wrong guesses that a two-minute loop
would have skipped.

### 2026-08-30 — Interpolation, and the closest-point bug it walked into

**`E2-T10` step (f).** `NurbsCurve.InterpolatePoints` — the curve that passes exactly through a
sequence of points. Chord-length parameters, de Boor's averaged knots, and a Gaussian solve with
partial pivoting.

**Chord length rather than uniform, and it is not a detail.** Uniform parameterisation is a line
shorter and produces visible overshoot whenever the points are unevenly spaced: the curve has to
cross a long gap in the same amount of parameter as a short one, so it accelerates and swings wide.
`UnevenlySpacedPointsDoNotProduceAnOvershoot` is the test, on six collinear points with one
hundred-fold gap, and it asserts both that the curve does not leave the line and that it does not
run past the ends and come back.

**The averaged knots are load-bearing too.** They are what makes every diagonal of the interpolation
matrix non-zero — the Schoenberg–Whitney condition — which is the difference between a system that
is banded and well conditioned and one that is merely square.

**Then that overshoot test found a bug in code from two steps ago.** It reported the curve 1.36
away from a polygon it was lying exactly on. The curve was fine: **`Curve.ClosestParameter` was
wrong**, and wrong in a way its own property test could not see. Its sweep was uniform in
*parameter*, and a polyline whose segments run 1, 1, 98, 1, 1 units covers each in the same amount
of parameter — so the long segment got a hundredth of the sample density per unit length, and a
query point on it was answered from a different segment altogether. The property test passed
because its probes were all far from the curve, where a coarse bracket is good enough.

**Two changes, each for a distinct half of the failure.** The sweep now samples **per span**, via a
new `Curve.SpanBoundaries` that `PolyLine`, `PolyCurve` and `NurbsCurve` override — the speed of a
parameterisation changes at span boundaries, so that is where the sampling density has to be
decided. And the bracket is narrowed by **golden section before Newton**, because at a span
boundary the derivative belongs to whichever side the curve reports, and a Newton step computed
from the wrong side points away from the answer and is then correctly rejected, leaving the search
stuck. A derivative-free search does not care which side it is on.

**Verified.** Build clean with `-warnaserror`; **1301 tests, 0 failures** (1282 + 19);
`dotnet format` clean. Two regression tests pin the closest-point defect directly — one on the
polyline, one on a NURBS curve with the same shape — so it cannot come back through either door.

**Two tolerances were guessed and are now measured.** The circle test asserted a radial error I had
estimated; each time I raised the bound the *first* failing sample reported a slightly larger
number, which is what chasing a first failure instead of a maximum looks like. Measured properly
over four thousand samples it is **6.5e-4 relative**, and the bound is set just above that with the
number written down. Most of that error is at the seam, because this is an *open* interpolation of
points that happen to close and nothing ties the two ends together — which is worth knowing and is
now in the test.

**Cost.** An hour and a half, half of it on the closest-point defect, which was worth every minute:
it was live in `Curve` for two steps and would have been found eventually by something much less
convenient than a test.

### 2026-08-31 — The C++ toolchain exists, and M1.6 is half unblocked

**Not a code step.** An environment change, recorded because the journal's *Environment facts* told
the next session something that is no longer true — and that section is the one place here where a
false fact costs a whole session.

**What is installed:** CMake 4.4.3 and Ninja 1.13.2 on `PATH` via winget; vcpkg cloned to
`C:\dev\vcpkg`, bootstrapped, with `VCPKG_ROOT` and `VCPKG_DISABLE_METRICS` set as user variables;
and the **Desktop development with C++** workload added to the Visual Studio Community 2026 that
was already there, which is what brings MSVC 14.51.36231 and `cl.exe`. The diagnosis worth keeping
is that Visual Studio was present all along and simply had no C++ workload — `vswhere` reported an
installation happily while `cl` was absent, which is why the check that matters is
`vswhere -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64` rather than *is VS
installed*.

**Verified by compiling, not by looking.** `cl.exe` existing on disk is not the claim worth
recording. A CMake project configures with the **Ninja** generator against MSVC, builds, links and
runs — the same generator and compiler `M1.6-C1` will use. One detail nearly went into the note
wrong: the probe printed `__cplusplus` as `199711`, which reads exactly like the C++20 setting
being ignored. It is MSVC's documented behaviour without `/Zc:__cplusplus`; `_MSVC_LANG` reports
`202002` and the standard is applied. **Thirty seconds of checking stopped a false fact entering
the environment notes.**

**M1.6 is half unblocked, and the journal says exactly that.** `M1.6-C1` is an OCCT build on **two**
operating systems. WSL is not installed, so the Linux leg still cannot run here. *M1.6 is
unblocked* would have been the convenient sentence and the wrong one — the missing half is
precisely the half that makes the criterion a cross-platform claim. *Blocked on* now names
`wsl --install -d Ubuntu` as a third thing needing a human, beside the OBJ viewer and the nightly.

**Also recorded, because it is the cheapest thing that could fail:** vcpkg has never built a port
here. It bootstraps and reports a version; nothing has exercised it against a real package. That is
the first thing to try when M1.6 is picked up.

**No gates beyond `Spark.Docs.Verify`** — nothing changed but documentation.

### 2026-08-31 — Approximation, and three test premises that were wrong

**`E2-T10` step (g).** `NurbsCurve.ApproximatePoints` — a curve *near* a set of points rather than
through them, with fewer control points than points. Least squares through the normal equations,
reusing interpolation's chord-length parameters and its pivoting solver.

**The two end points are pinned and taken out of the system.** A fitted curve whose ends float is
unusable for anything that joins curves, and it is the first thing a caller notices. Their
contribution is subtracted from the right-hand side rather than left for the solver to approximate.

**The caller says how many control points, not how close.** A tolerance-driven overload — *fit
within 0.1 mm* — is the friendlier signature and is deliberately not this one: it needs a loop that
raises the count until the deviation fits, which on noisy data terminates only at one control point
per sample, and that silently returns an interpolation dressed as a fit. It needs a cap and a
policy for hitting the cap, and that is a step with its own tests rather than a parameter.

**Then three tests failed, and all three of my premises were wrong rather than the code.** This is
the part worth recording.

- *A fit to points sampled from a curve it can represent is exact* — **false.** The fit is
  parameterised by chord length and the sampled curve is not, and a cubic in one parameterisation
  is not a cubic in the other. What is true is that the geometric deviation converges as control
  points are added.
- *More control points never fit worse* — **false as stated.** That holds over a nested sequence of
  spaces, and these are not nested: every control-point count gets its own knot vector. The
  measured series really does rise once, 0.1127 to 0.1128, between four and five.
- *A fit stays within the noise amplitude* — **arithmetic done carelessly.** The noise is ±0.5 on
  each of two axes, so a point can sit 0.707 from the line it was scattered around, not 0.5.

**And I nearly recorded a bug that did not exist.** Diagnosing the first failure, I compared the two
curves *at the same parameter* — which cannot converge even for a perfect fit, for exactly the
reason the test premise was wrong. The error plateaued at 0.34 across four to thirty control points
and looked precisely like a broken solver. Measured geometrically — distance from each point to the
curve — the same fits converge **0.113 → 2.4e-5**. The lesson is narrow and sharp: **compare curves
by where they are, never by what their parameters say**, and it is now in the class remarks so the
next person does not spend the same twenty minutes.

**Verified.** Build clean with `-warnaserror`; **1313 tests, 0 failures** (1301 + 12);
`dotnet format` clean. The convergence figures above are asserted rather than described — the test
requires thirty control points to fit a hundred times better than six.

**Cost.** An hour, half of it establishing that the code was right.
