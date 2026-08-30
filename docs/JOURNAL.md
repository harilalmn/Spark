# Spark — Development Journal

The resumable record of the marathon run to 1.0. **Current state** is where the work is *right
now*; **Log** is how it got there. Everything else in `docs/` says what the product should be —
this file says what is happening.

**Last updated:** 2026-08-30 17:40 +0530
**Protocol version:** 2

---

## Current state

> Rewritten at the start and the end of every step. If a session dies, this is what the next one
> reads. It is deliberately short: detail belongs in the log entry, not here.

| | |
|---|---|
| **Milestone** | **M1 and M1.5 are both done.** M1.6 is deferred for want of a C++ toolchain; the work in flight is **M2's remainder** |
| **Working on** | Nothing. Between steps, part way through queue **9** |
| **Step status** | `CLEAN` |
| **Last completed step** | Queue **9**, `E8-T2` step **(b)** — the shell is a real `DockControl` driven by `WorkspaceLayout`. **`E8-T2` is `Done`** |
| **Working tree** | Clean at the time of writing; verify with `git status` |
| **Next action** | Queue **9**'s next item: **`E8-T6` — group, note and align on the canvas.** Read the `E8-T6` row in [TASKS.md](TASKS.md) first; the canvas already has selection, the `SceneIndex` and undo over whole-document snapshots, so all three operations are edits to the document rather than new machinery. Take **align** first — it is the smallest of the three, it needs no new document type, and it proves the edit-plus-undo path before group and note add one. Before starting, decide whether a group is a document object or a canvas annotation, because that decision is expensive to reverse and `E8-T6` does not make it for you. |
| **Verify with** | `dotnet build Spark.slnx --no-incremental -warnaserror`, the per-project executables (**1058** after step (b): Geometry.Tests 402, Engine.Tests 289, UI.Tests 243, Viewport.Tests 69, Geometry.Properties 42, Architecture.Tests 8, Docs.Verify 5), `dotnet format`, and `dotnet run --project src/Spark.Desktop -- --graph curves --screenshot PREFIX`. **Check the counts** — [N30](NOTES.md). |
| **Blocked on** | Nothing. **Two things need a human**: opening an exported OBJ in a third-party viewer (M1's stated acceptance), and watching the first nightly benchmark run. |

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
| 3 | ~~`Quaternion` — the last piece of the value layer~~ | `E2-T1` | M | **Done** 2026-08-29 |
| 4 | ~~`RayCaster` and its BVH~~ — landed as `Ray` and `BoundingVolumeHierarchy` — pays for itself across mesh booleans, viewport picking, intersection seeding, and `Curve.ClosestPoint` waits on it | `E2-T15` | L | **Done** 2026-08-29 |
| 5 | **Geometry serialization v1 and the reflection-driven round-trip test** — get it in before there are twenty types to retrofit it onto; there are now **twenty-two** | `E2-T29`, `E2-T31` | M | **Done** 2026-08-29 |
| 6 | **`Spark.Geometry.Io`: the OBJ writer, and `spark` writing a polyline a third-party viewer opens** — this is **M1's demoable** | `E2-T34`, `E12-T5` | M | **Done** 2026-08-29 |
| 7 | **The C2VGeometry test harvest**, timeboxed to one week with a hard stop. Harvest assertions, not generators | `E2-T32` | L | **Blocked** — needs the C2VGeometry source, which is not in this repository and not on this machine. Skipped 2026-08-30 |
| 8 | **M1.5 spike (c): AvaloniaEdit plus a Roslyn completion popup** — the last unproven part of M1.5, gating M4 | `E11-T21` | M | **Done** 2026-08-30 |
| 9 | **What is left of M2** *(in progress)* — ~~real docking (`E8-T2`)~~ **done 2026-08-30**; group/note/align (`E8-T6`) *(next)*, watch nodes (`E8-T10`), `spark run` (`E12-T5`) | | L | Open |
| 10 | **M3 — NURBS curves** | `E2-T10` … | XL | Open |
| + | **Persist the workspace layout between sessions** — `WorkspaceLayout` already serialises and round-trips under test; nothing writes it. A dragged arrangement dies with the window, which is the one thing a dock is for | `E8-T2`-adjacent | S | Open |
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
