# Spark — Development Journal

The resumable record of the marathon run to 1.0. **Current state** is where the work is *right
now*; **Log** is how it got there. Everything else in `docs/` says what the product should be —
this file says what is happening.

**Last updated:** 2026-09-01 23:55 +0530
**Protocol version:** 2

---

## Current state

> Rewritten at the start and the end of every step. If a session dies, this is what the next one
> reads. It is deliberately short: detail belongs in the log entry, not here.

| | |
|---|---|
| **Milestone** | **M1, M1.5, M2, M3, M4, M5, M6 and M7 are done.** M7 closed on 2026-09-01 when `E7`'s last row landed: a package can be found on nuget.org, read, installed, used and removed; a graph missing one opens unharmed and offers to fetch it; a local DLL can be referenced **without locking it**; and a branch can be frozen. **M1.6 is taken**: all nine criteria answered, `C2` passed, ADR-0020 stands. |
| **Working on** | **Nothing. The tree is clean and the gates are green.** |
| **Step status** | `CLEAN` |
| **Last completed step** | **`E6-T18` and `E6-T19` - code block inputs.** A new block now has **no** input ports (the starter was `return a;`), and every input port has a **type dropdown** defaulting to *from the wire*. The client asked for `+`/`-` buttons with name **and** type dropdowns; the trade-off was put to them and they took the narrower design, because declaring names would put a second source of truth beside the code. A declaration **beats** the wire, is held by port name, survives a rebuild, and round-trips as a short token. **2093 tests.** |
| **Working tree** | Clean at the moment this was written. The step has not started. |
| **Next action** | **`E6-T20` - a rendering test for the properties pane, which is owed.** Every properties-pane defect found by a person today was a **rendering** defect, and rendering is the one thing the headless session cannot assert: showing `InspectorPane` with a bound view model hangs the dispatcher. [N90](NOTES.md) has the bisection - not the session, not the window, not construction, not binding, and **not the pane's contents**, since it survives hiding the editor and emptying the port list. **Start with the narrower case**: capture a frame of the row `DataTemplate` alone, hosted in a bare `ItemsControl` with a hand-built `PortLiteralViewModel`, and find out whether **any** bound Spark control renders in this session or only this pane fails. If the narrow case renders, bisect the pane downward from the top; if it hangs too, the question is about the headless session itself and the answer may be that this class of test needs a real window. Also owed: a contrast test for the editor colours beside `PaletteContrastTests`, and the `tessellate` verb wired into `nightly.yml` (Windows runs it, the other legs pass `--no-tessellation`). |
| **Verify with** | `dotnet build Spark.slnx --no-incremental -warnaserror`, `dotnet test Spark.slnx` (**2093** over **nine** projects: Geometry.Tests 763, UI.Tests 573, Engine.Tests 432, Viewport.Tests 108, Geometry.Properties 43, Geometry.Occt.Tests 63, Architecture.Tests 15, Packages.Tests 71, Docs.Verify 5), `dotnet format`, `--graph solids --screenshot`, `spark export --open docs/examples/solids.spark --out OUT.step`, and `pwsh scripts/publish.ps1` followed by running the staged `spark.exe`. **Check the counts** - [N30](NOTES.md) - **and the SKIP count**: build the shim first with `pwsh scripts/build-native.ps1` from a Visual Studio developer prompt. |
| **Blocked on** | **Three things need a human, and the list is shorter than it was.** **(1)** `E13-T12`'s acceptance: a public STEP corpus and a **third-party viewer, never our own reader** — the round trip and the file's own text are evidence, a viewer is not. **(2)** `Q13`'s six counsel questions, the first of which is whether `spark_occt` is a *work that uses the Library* or a derivative work. **(3)** `E13-T17`'s installer, code signing and antivirus submissions, which need an identity to sign with — which is why `release.yml` drafts and never publishes. *And still: opening an exported OBJ or STEP in a third-party viewer, which is also M1's stated acceptance, and watching the first nightly benchmark run.* **`E12-T4` was on this list and should not have been.** It needs a Revit or AutoCAD licence, but it proves a **second** claim — that the engine can be embedded — and Spark ships standalone without it. [D20](PRD.md#13-decision-log) moves it and `E12-T2` past 1.0. Listing it beside the signing identity implied Spark could not ship without a CAD licence, which was wrong, and the client caught it. |

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
  which should total **1,724 passing, 0 failed, 0 skipped** across eight projects, with the native
  shim built. See [AGENTS.md](../AGENTS.md#before-you-commit).
- **A C++ toolchain exists as of 2026-08-31, and it is half of what M1.6 needs.** Installed and
  **verified by compiling, not by looking**: CMake 4.4.3 and Ninja 1.13.2 on `PATH`, vcpkg at
  `C:\dev\vcpkg` with `VCPKG_ROOT` set, and MSVC 14.51.36231 inside Visual Studio Community 2026
  (`C:\Program Files\Microsoft Visual Studio\18\Community`). A CMake + Ninja + MSVC project
  configures, builds, links and runs; `_MSVC_LANG` reports `202002`, so the standard really is
  applied — `__cplusplus` reads `199711` under MSVC without `/Zc:__cplusplus` and means nothing.
  `cl.exe` is **not** on the ambient `PATH` by design; a build has to source
  `VC\Auxiliary\Build\vcvars64.bat` first, which is what CI does too.
- **`Q15` is answered in the two parts that were blocking, and `M1.6-C1`'s Linux leg is void.**
  **D17** (`docs/PRD.md` §13) records both: the C-ABI shim stays, and the two-operating-system
  requirement in `M1.6-C1`/`C2` is void under **D16**. **WSL is still not installed and no longer
  needs to be.** What survives is  — whether the ubuntu CI job survives — which is a
  question about a *test technique*, not a release commitment, and it now has to be argued
  alongside `E13-T15`'s cost for a per-RID native build.
- **OpenCascade 8.0.1 is installed at `C:\dev\vcpkg`, and it took 1.3 hours.**
  `vcpkg install opencascade:x64-windows` at baseline `abb6dda5cc32914d2e64d7d72b974dc301d1fc8a`,
  installing `opencascade[core,freetype]` — 57 DLLs in `installed/x64-windows/bin`. **It is
  already done; do not run it again** unless the baseline moves. It saturates every core for the
  duration and the GPU read-back in `--screenshot` fails while it runs.
- **Building the shim needs a developer prompt, and `VCPKG_ROOT` will lie to you inside one.**
  `vcvars64.bat` sets `VCPKG_ROOT` to the vcpkg **bundled with Visual Studio**, which is a real
  vcpkg with nothing installed in it. `scripts/build-native.ps1` therefore prefers the root that
  *has* OpenCascade over the first one that exists. Build it with:
  ```
  cmd /c "\"C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat\" >nul && powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-native.ps1"
  ```
  It stages `artifacts/native/win-x64/` (gitignored, 58 DLLs, 52.0 MB) and runs the C smoke test.
  **`Spark.Geometry.Occt.Tests` skips itself when that directory is missing**, so a green managed
  run proves nothing about the provider unless the skip count is zero.
- **vcpkg builds ports here.** `vcpkg install zlib:x64-windows` compiled from source and passed
  post-build validation in 32 seconds, so vcpkg finds MSVC, drives CMake and completes a real port
  unaided. That was the cheapest thing that could have failed before M1.6, and it did not.
  **OCCT is a different order of magnitude** — 47 toolkits against zlib's one — so this says the
  pipeline works, not that the OCCT build will.
- **RCS, CADScript and DoodleSharp are not on this machine**, and neither is C2VGeometry. Five `E6`
  rows say *port X from RCS* or *from CADScript* — `E6-T1`, `E6-T2`, `E6-T3`, `E6-T4`, `E6-T13`.
  **Porting is a strategy, not the deliverable**, so those are written from scratch here against
  the behaviour the rows describe. Where a row names a specific lesson from the original — the
  non-locking read, the uncleaned registry pinning a collectible context — that lesson is the part
  worth keeping and it is in the row.
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
| 10 | ~~**M3 — NURBS curves**~~ | `E2-T10`, `E2-T12`, `E2-T50`–`E2-T57`, `E5-T13`, `E5-T16` | XL | **Done** 2026-08-31 |
| 11 | **M4 — the C# code block** *(next)* | `E6` | L | Open |
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

### 2026-08-31 — One row became nine

**A documentation step, committed as one.** `E2-T10` had absorbed seven work steps and its
description cell had grown to **4,473 characters** — a paragraph-long wall in which *done* and
*not done* were interleaved and neither was findable. The register's job is to answer *what is
built* at a glance, and that cell had stopped doing it.

**Split by operation, not by session.** `E2-T10` keeps the curve type, its evaluation and its
derivatives; `E2-T50` … `E2-T54` take the knot vector, insertion and trimming, closest point,
degree elevation, and interpolation with approximation. The three that remain each get a row of
their own — **`E2-T55` knot removal**, **`E2-T56` fit to a stated tolerance**, **`E2-T57` split** —
which is the point of the exercise: three open pieces buried in a sentence at the end of a
four-thousand-character cell are three pieces nobody schedules.

**`E2-T10` was kept rather than retired.** It is referenced from `PRD.md`, from `EPICS.md` and from
nine journal entries, and renumbering a stable identifier to tidy a table would break every one of
them for no gain. New work took new numbers from the end of the range.

**Each new row carries the *why*, not just the *what*** — the homogeneous blend, the exactness of
the trim, the property that is the whole closest-point test, the tolerance question that makes knot
removal different from everything around it. That was already the register's convention and it is
what made splitting the cell possible at all: the material was there, it simply had nowhere to sit.

**No gates beyond `Spark.Docs.Verify`** — nothing changed but documentation.

### 2026-08-31 — Knot removal, split and fit-to-tolerance, and two more premises of mine

**Three rows in one step**, because split and fit are thin layers over things already proved and
only knot removal had a decision in it.

**`E2-T55` — removal is the only operation here allowed to change the curve.** Insertion, trimming
and elevation are all tested by asserting *nothing moved*; removal cannot be, because moving the
curve slightly is the point. So the tolerance is a parameter and **the deviation is measured, not
bounded**: the textbook check uses Wolters' algebraic bound in the middle of the recurrence and
reports success without ever measuring, which makes the tolerance the caller passed not quite the
tolerance they got. Here the candidate is built, the two curves are sampled against each other, and
the removal is kept only if the measured deviation is inside. `Reduced` measures every candidate
against the **original** rather than the previous step, so a hundred removals each just inside
tolerance cannot accumulate past it — and that is asserted.

**It did not work the first time and the tests said so loudly** — all eight red, zero removals. The
cause was `first` and `last`: I had them as multiplicity offsets when A5.8 defines them as
`r − p` and `r − s`. Rewritten faithfully, all eight passed.

**`E2-T53`'s remark is now actionable.** Degree elevation was *exact but not minimal*;
`ReducingAnElevatedCurveTakesBackWhatElevationAdded` shows reduction taking the redundant control
points back off without moving the curve.

**`E2-T57` — split is two trims**, and therefore exact for free. Both halves keep their share of the
original parameter range, so `Left.Domain.Max == Right.Domain.Min == t` and a caller's existing
parameters still mean something.

**`E2-T56` — the loop is the difficulty, not the algebra**, and it turned out to be more difficult
than the row predicted. The stated risk was that raising the count on noisy data never terminates
usefully; the cap and the honest `Fits = false` handle that. **The unstated one bit instead: more
control points do not always fit better.** As the count nears the number of points the system is
nearly square and the normal equations ill-conditioned — on a fifty-point wave the deviation falls
to **0.0037 at forty control points and rises to 0.33 at forty-nine**. A search trusting
monotonicity returns a visibly worse curve than one it had already computed, and no care in the
caller could detect it. The search now keeps the **best measured** result rather than the last, and
a test drives it to the end deliberately to prove it.

**Two of my test premises were wrong again, and both were tolerances I chose instead of measured.**
I asked a cubic to fit that wave within 1e-3 when its floor is 0.0037, and asserted a
never-worse property against counts the search never visits. Both are now written from measurements
with the numbers in the comments. That is three steps running in which the code was right and my
expectations were not; the pattern is specific enough to name — **a tolerance in a test is a
measurement, not a preference**.

**Verified.** Build clean with `-warnaserror`; **1335 tests, 0 failures** (1313 + 22);
`dotnet format` clean.

### 2026-08-31 — Offset, fillet, the curated categories, and M3 closes

**Three rows, and the milestone.** `E2-T12`, `E5-T13`, `E5-T16`.

**`E2-T12` — offset and fillet, in a class of their own.** Neither fits on `Curve`: an offset needs
a plane to happen in, so *offset by 5* is not a curve on its own in three dimensions, and a fillet
is a relationship between two curves rather than a property of either. Putting them on the base
would mean a wrong signature or a method most curves throw from.

**The offset of a NURBS curve is not a NURBS curve** — a fact about the mathematics, not a
limitation here — so `Offset` takes a tolerance and fits, on `E2-T56`, which had landed an hour
earlier and is exactly what it needed. Lines, circles and arcs *are* answered exactly, and that
matters more than it looks: a fitted circle is a circle only to within a tolerance, and everything
downstream that asks *is this an arc?* would start saying no.

**Two things are stated as absent rather than approximated.** An offset that self-intersects is not
repaired, because trimming the loops needs curve-curve intersection and that is not built — the
result is the true offset locus, loops included. And fillet is **two lines only**: a general
curve-curve fillet is a tangency problem that needs intersection to find the corner at all, and
approximating it now would produce something that looks like the feature and is not. The lines come
back trimmed, because a fillet that leaves the original corner in place is not what anybody asked
for.

**`E5-T13` — the curated categories, and one design fact runs through the List half.** Every input
is `[KeepStructure]`, because these are the nodes that look *at* a list rather than *through* it: a
replicating port hands `List.Count` one item at a time and it answers 1 for every element,
producing a list of ones where a number was expected, silently. `Logic.Equal` takes a tolerance and
defaults it away from zero — a node answering false to `0.1 + 0.2 == 0.3` is technically correct
and useless. Text renders and parses in the invariant culture always, or graphs produce files that
differ by locale and undo what ADR-0017 bought by choosing text.

**`E5-T16` closed without a commit of its own**, because `E8-T10` had already built it: `Watch.Value`
and the preview bubbles landed on 2026-08-30. Checking before working was worth the two minutes it
took, and the row now says why it closes rather than merely that it did.

**M3 is complete.** `E2-T10`, `E2-T12`, `E2-T50` … `E2-T57`, `E5-T13`, `E5-T16` — the knot vector,
the curve, insertion, exact trimming, closest point on every curve type, degree elevation,
interpolation, approximation, knot removal, fit to tolerance, split, offset, fillet, and the
curated node categories.

**Verified.** Build clean with `-warnaserror`; **1368 tests, 0 failures** (1335 + 33);
`dotnet format` clean. The offset tests assert the one property that defines the operation — every
point of the offset is the offset distance from the original — rather than comparing against a
hand-computed shape, and the fillet tests check tangency directly by measuring the arc's centre
against both original lines.

### 2026-08-31 — M4 begins at the seam, not at the editor

**The journal's next action said to decide the shape before touching Roslyn, because it reaches
into `Graph`, the cache key and the file format.** It does, and the decision is made.

**Half of it was already true.** `Graph.AddNode` takes a `NodeDefinition` and nothing requires that
definition to have come from a library — so per-instance definitions needed no change at all. What
needed changing was the *file*: a node was looked up by key on open, and a code block's definition
does not exist to be looked up. `GraphDocumentNode` carries `Script` now, and `Restore` rebuilds the
definition from it.

**Rebuilding needs Roslyn, and a graph of boxes and circles must never load Roslyn** — `E6-T14`
states it. So `Restore` takes an **`IScriptNodeFactory`**: the engine holds the contract, the host
supplies an implementation, and a document with no scripts never asks. `Spark.Engine` does not
reference `Spark.Scripting` and still does not.

**That same seam gives `E6-T16` its meaning at the right place.** Running with scripting disabled
*is* passing no factory, and a graph containing a code block then **refuses to open, naming the
node**. It does not open with the node quietly missing — a Spark graph is executable code, and a
switch that silently dropped the executable parts would be worse than no switch.

**Format version 3, by the rule notes established.** A version-2 reader does not know the `script`
field exists; it would open the graph, show an empty code block, and write the code away on the next
save. Sharing version 2 would have been convenient and wrong — versions 1 and 2 have shipped, and a
reader that shipped is a reader that exists. A graph with no code block is still version 1, byte for
byte.

**The key carries a hash of the script.** The evaluation cache keys on the definition's key, so two
blocks with different code must not collide — and two with the *same* code should, which is
`E6-T10`'s *identical text in ten nodes compiles once* falling out of the design rather than being
added to it.

**One test broke, and it is the second time this exact trap has fired.**
`AVersionNewerThanThisBuildIsStillRefused` read a file at `formatVersion: 3` to prove a future
version is refused, and version 3 became real today. The first was `AnUnknownTypeIsRefused` naming
`"NurbsCurve"`. Written up as
[N41](NOTES.md#n41--a-placeholder-for-something-that-does-not-exist-must-be-something-that-cannot-come-to-exist):
**a stand-in for something that does not exist must be something that cannot come to exist.**

**Also recorded in the environment facts: RCS, CADScript and DoodleSharp are not on this machine.**
Five `E6` rows say *port X from* one of them. Porting is a strategy and not the deliverable, so
those get written here against the behaviour the rows describe — and where a row names a specific
lesson from the original, that lesson is the part worth keeping.

**Verified.** Build clean with `-warnaserror`; **1376 tests, 0 failures** (1368 + 8);
`dotnet format` clean. The seam is tested with a stub factory containing no compiler at all, which
is the point: none of it waits on Roslyn, and it should go on testing the seam after the real
factory exists.

### 2026-08-31 — The Roslyn pipeline: a code block that compiles and runs

**M4 step (b)** — `E6-T2`, `E6-T5`, `E6-T8`, `E6-T9`. A code block now compiles C#, infers its
ports, and runs.

**`E6-T5` — port inference is semantic and it earns the choice.** The script is compiled once
against the prelude with nothing declared; every identifier it expected from outside comes back as
`CS0103` or `CS0117`, and those, in source order, are the input ports. The row argued this beats a
syntax walk and the tests show exactly where: **a local is not a port, a lambda parameter is not a
port, and `Point3d` is not a port** — each of those is a case a syntax walk has to re-implement
scoping to get right, and each is one line of test here. *An identifier that resolves to anything at
all is not an input* is the whole rule, and only the compiler can apply it.

**`E6-T8` — outputs are read from the syntax, and that was forced rather than chosen.** The first
version reflected over `TupleElementNamesAttribute` on the compiled entry point and found nothing,
every time: tuple element names are a compile-time fiction, and the generated method returns
`object`, so by the time there is an assembly the names are gone. Reading the return statement's
syntax is not a compromise — **tuple element names *are* syntax**, and
`return (area: a, perimeter: p);` says what the ports are called in the only place that information
ever exists.

**Inputs are `dynamic`, and that is a placeholder with a date on it.** Declaring them `object` is
what a first attempt does and it does not compile — `a * 2` is not an operation on `object`, so the
friendliest possible code block would reject the simplest possible script. `E6-T6` replaces it: once
a port is wired the upstream type is known and the declaration becomes
`Point3d centre = (Point3d)__in[0];`, which is also what makes `E6-T7`'s wire-typed IntelliSense
possible, because completion needs a type to offer members from.

**Two hours of that step went to one error message**, and it is worth recording:
`Missing compiler required member 'Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo.Create'`.
`dynamic` binds through `Microsoft.CSharp`, which is not loaded until something touches it — so a
catalogue built by sweeping loaded assemblies misses it, and every script with an input port fails
with a message that names nothing the user wrote. **Touching the type was not enough**; the
assembly's location has to be added by name. That is exactly the failure mode `E6-T2`'s row
predicted — *a missing reference produces errors that look like the user's fault* — arriving in the
implementation of the row that predicted it.

**`E6-T2` — the catalogue reads without locking**, swapping an immutable snapshot, so a compile in
flight finishes against the references it started with. That is what `E7-T9`'s auto-reload will need
and it is cheaper to build now than to retrofit. Its version is in the compile-cache key, because a
script whose text has not changed still has to recompile when the assemblies underneath it have —
otherwise a user who has just fixed a bug in their own library keeps getting the old behaviour with
no way to explain it.

**`E6-T9` — the same script compiles once**, which is what makes a slider feeding a code block feel
live: every drag is an invocation of an assembly already loaded.

**A script that does not compile still yields a node.** It keeps its place and its wires while the
user fixes a semicolon, and reports the failure when it runs — which is where they are looking.

**Verified.** Build clean with `-warnaserror`; **1390 tests, 0 failures** (1376 + 14);
`dotnet format` clean. The inference tests are the ones to keep: they assert the four cases that
distinguish a semantic answer from a syntactic one.

### 2026-08-31 — A code block on the canvas, end to end

**M4 step (c).** The pipeline compiled and ran in a test; nothing in the application could reach
it. Now a **Code block** button places one, its source is edited in the properties pane, and it
evaluates.

**The screenshot is the verification and it shows the whole chain working.** A `CodeBlock` node in
the Script category with one input port `a` and — from
`return (doubled: a * 2, squared: a * a);` — **two named output ports, `doubled` and `squared`**.
The properties pane holds the source in a monospace box, the preview bubble under the node reads
`rank 0 · one value / 42`, and the status bar says `Ran 1 (18 cached)`: the code block compiled and
evaluated while everything else came from the provenance cache, which is exactly the behaviour
`E6-T9` exists for.

**Roslyn is still not loaded until somebody asks for it.** `SparkSession.Scripts` is null until
`EnableScripting()` is called, and the first call is `PlaceCodeBlock`. A session that never places
one never touches the compiler — `E6-T14`, honoured in the only place it can be.

**`DisableScripting` is one-way**, and that is deliberate. A switch that any code path could
reverse would not be a trust boundary; `--no-script` has to mean *not in this session*, not *not
yet*.

**Editing a script changes a node's ports, which is unlike every other edit in the application.**
`ReplaceDefinition` rebuilds the node and **re-makes its wires by port name rather than by index** —
indices shift when a script gains an identifier, and reconnecting by index would silently rewire
the graph to something the user never drew. Wires into ports that no longer exist are lost, which
is inherent: a script that stops mentioning `radius` has no `radius` port.

**The properties pane is the editing surface for now and it is meant to be replaced.** `E6-T11`'s
AvaloniaEdit host and `E6-T7`'s wire-typed completion are the real answer. A plain text box gets a
working code block on screen without waiting for them, and **a code block you cannot type into is
not a code block** — the ordering follows from that rather than from what was convenient.

**Verified.** Build clean with `-warnaserror`; **1390 tests, 0 failures**; `dotnet format` clean;
and the screenshot above, which is the only thing that could have shown the tuple ports arriving on
the canvas.

### 2026-08-30 — The token reaches the script, and a wrapper that would have eaten it

**M4 step (d)(i), `E6-T17`.** A code block could take the application with it, and the token that
should stop it was not reaching it. Now it is. **The row is not closed**, and that is the honest
part of this entry: `while (true) { }` still hangs. What exists is the channel and the entry check;
`E6-T4`'s guard weaver is what makes a running loop hit it.

**The token stops at scripts rather than reaching every node, deliberately.**
`NodeDefinitionSource.Invoke` is now a `ScriptInvocation` taking a `CancellationToken`;
`NodeDefinition` gained `InvokeScript` and a `Call(arguments, token)` that the replicator uses in
place of `Invoke`. Giving `NodeInvocation` the token instead was the tidier-looking option and was
wrong: a library node is a method somebody wrote intending it to return, and handing all of them a
token they ignore spreads the cost of one hazard across the whole node model. **A code block is the
only node whose body a user can write non-terminating by accident.**

**`NodeDefinition.Invoke` still exists and still drops the token**, which is a trap, so `Call` is
documented as the one to use and the seam test `OnlyAScriptDefinitionCarriesACancellableInvocation`
pins the distinction.

**The thing that would have made all of this useless was three layers down.** The replicator's two
broad catch filters already excluded `OperationCanceledException` — good design, already there. But
the generated entry point was reached through `MethodInfo.Invoke`, which wraps whatever the script
threw in a `TargetInvocationException`, and *that* does not match the filter. The full sequence:
user presses stop, token cancels, the script's check fires, the wrapper hides it, the replicator
reports `'CodeBlock' failed` and **carries on to the next node**. Every piece correct, the whole
thing broken. Binding with `CreateDelegate` removes the wrapper; it is also faster, which is the
lesser reason and would have been the wrong one to record. [N42](NOTES.md).

**Reverted both halves to watch four named tests go red**, separately: routing the replicator back
through `Invoke` reddens `AScriptIsInvokedWithTheEvaluationsOwnToken` and
`AScriptThatObservesCancellationStopsTheEvaluation`; putting `MethodInfo.Invoke` back reddens
`ACancelledTokenStopsAScriptBeforeItRuns` and `AScriptsExceptionIsNotWrappedByReflection`. Two
independent failures rather than one, because they are two independent defects.

**The token is asserted by identity, not by observing a cancellation.** A seam that fabricated a
fresh token, or passed `CancellationToken.None`, satisfies any test that only checks *something was
passed* and then never cancels anything. `Assert.Equal(source.Token, seen)` is the assertion that
cannot be faked.

**An unrelated thing found on the way:** `ScriptNodeFactory.cs` contained a **raw NUL byte** — the
cache-key separator in `script + "\u0000" + version`, written as the character rather than the escape.
It is a sound separator and the string is unchanged, but grep classified the whole file as binary
and silently omitted it from every content search. Replaced with `"\u0000"`. Nothing behavioural, and
the file is greppable again.

**No help topic.** Nothing user-facing changed — there is still no stop button — so there is
nothing to document with a worked example yet. That arrives with `E6-T4`.

**Verified.** Build clean with `-warnaserror`, zero warnings; **1396 tests, 0 failures**
(Engine 340 → 343, UI 341 → 344); `dotnet format --verify-no-changes` clean; docs harness green;
and the four reverts above.

### 2026-08-31 — D16: Windows, and nothing else, ever

**Not a queue item.** The client stated a decision and asked for it to be recorded, so this step is
documentation only and no code changed.

**The decision.** Spark supports Windows and no other operating system, permanently. **D14** already
said *v1 releases target Windows only*; **D16** removes the *v1*. It is now a statement about the
product rather than about a version, and the README, installer and website say Windows without a
"for now". `N5` and the §9 out-of-scope bullet are hardened to match, and D14 is left otherwise
unedited with a pointer to D16 — the same treatment D2 got when it was reversed, so the narrower
decision actually taken then stays legible.

**The part worth more than the decision is what it reopens, and D16 does not settle either.** Both
were bought with the cross-platform option D16 gives up, so both are now unfunded, and both are
recorded as **Q15** rather than resolved here:

- **The binding.** ADR-0020 chose a hand-written C-ABI shim over C++/CLI at a stated **15–25%
  effort premium**, and the payoff it names is *buying back the entire cross-platform option*. Under
  D16 that payoff is worth nothing. **The honest counter belongs in the same breath**: the shim was
  also chosen for a small, deliberately chosen ABI surface and for surviving OCCT upgrades, and
  neither reason is about operating systems — a generated C++/CLI binding cannot reduce the surface,
  which is why Macad3D's runs to 170 files. C++/CLI would also reverse **D7** and break the
  `-windows`-free architecture test. **The moment to ask is now**: `spark_occt` is unwritten, so
  reopening is cheap today and expensive at M6.
- **The two-OS criteria.** `M1.6-C1` and `M1.6-C2` demand the build and one boolean on Windows *and*
  Linux. **This is what blocks M1.6 today**, and answering Q15 is cheaper than installing WSL and may
  make it unnecessary. The journal's environment fact now says so, because the next session's
  instinct will be to install WSL.

**The distinction the whole record turns on: supporting an OS is a release commitment; running CI on
one is a test technique.** D14 already held them apart and D16 settles only the first. The ubuntu
job's *rot-guard* justification is void — it guards an option the product has renounced — but the
job has independently caught a real defect that had nothing to do with shipping on Linux
([N28](NOTES.md)), and it is where floating-point and culture-dependent differences surface. That is
a different argument for keeping it, and it has to be made on its own merits or the job has to go.
Q15(c).

**`AGENTS.md`'s no-`-windows`-TFM rule is left in force with a warning attached**, because its
stated justification is the rot-guard and a reader who now finds that justification void would draw
the wrong conclusion. The rule is cheap, a `-windows` TFM is very hard to remove once it spreads,
and Q15 may keep the Linux job anyway.

**Verified.** Docs harness green — 5 checks, which is what covers `Last updated` lines, relative
links and ADR citations across the four documents touched. No code changed, so no other gate applies.

### 2026-08-31 — The guard weaver: `while (true) { }` finally stops

**`E6-T4`, and `E6-T17` closes with it.** The cancellation seam has existed since two steps ago and
did nothing: a token reached the generated `Run(object[] __in, CancellationToken __token)`, and
nothing the C# compiler emits ever reads a token. So a script already inside a loop still hung the
evaluation thread, and .NET has no safe thread abort to fall back on. **The only place a check can
go is inside the loop, and the only moment it can be put there is between parsing and compiling.**

**What is woven.** A `CSharpSyntaxRewriter` over the generated tree adds four things: a
`ScriptGuard.Tick(__token)` at the top of every `for`, `foreach`, `while` and `do` body — which
tests the token *and* counts the iteration; the same before every `goto`, because a label and a jump
are the second way to write an unbounded loop and a weaver that only looked at loop keywords would
miss it entirely; `Enter`/`try`/`finally`/`Exit` around every local function body; and a
`ScriptGuard.Begin(…)` at the top of the entry point, so the budget is **per invocation** rather
than per node or per session.

**The two ceilings do different jobs and only one of them is really a safety net.** Cancellation is
what a user experiences. The hundred-million-iteration ceiling is for when nobody is watching —
`spark run` in a build — and is deliberately generous, because a ceiling low enough to catch a bad
script is low enough to break a good one. **The depth ceiling is the only one with no alternative**:
`StackOverflowException` cannot be caught in .NET, so depth has to be bounded *before* the stack
runs out rather than caught after ([R11](PRD.md#12-risks)).

**Two things the plan did not contain, and both are the interesting half.**

- **A `static` local function is exactly what a woven guard cannot live in.** `static` is a promise
  not to capture, and `__token` is a capture — so an ordinary `static int total() { for (…) … }`
  would have failed with `CS8421` naming a parameter the user never wrote, because of a rewrite
  they did not know had happened. The weaver drops the modifier. That only widens what is legal.
  [N44](NOTES.md).
- **Every woven statement carries no trivia at all**, so the tree the compiler sees has exactly the
  line count the text did. That is not tidiness: it is the property `E6-T1`'s source map will be
  built on, it is free today and expensive to reconstruct later, and it is **asserted** by
  `WeavingDoesNotMoveAnyLine` rather than intended.

**Two limits are stated on the type rather than left to be discovered.** Recursion through an
expression-bodied *lambda* is not bounded — bracketing a body means turning it into a block, which
needs the return type, and a lambda is the one construct that declines to state one; local functions
do state theirs, which is why they are covered. Recursion inside a library the script calls is not
bounded either, because it is not our code to rewrite. Both still end in `R11`, and the help topic
says so in the same words.

**A latent bug the new tests exposed, and it had nothing to do with guards.** `GuardWeaverTests`
is the first test class that compiles a script *before anything else in the process has run*, and
every script with an input port failed: `Missing compiler required member
'Microsoft.CSharp.RuntimeBinder.Binder.BinaryOperation'`. The catalogue names `Microsoft.CSharp`
explicitly and sweeps up whatever else is loaded — but `dynamic` needs a **second** assembly,
`System.Linq.Expressions`, for the call site the binder dispatches through, and nothing had loaded
it. **The diagnostic names the assembly that is present, not the one that is absent**, which is
what made it cost what it did. Now named by `typeof(...).Assembly.Location` like the first, with a
test that asserts both are in the catalogue. [N43](NOTES.md).

**The NUL byte is gone this time.** The previous entry says `ScriptNodeFactory.cs`'s cache-key
separator was replaced; it was not — the raw NUL was still in the file at `c383acb`, and grep still
classified it as binary and silently skipped it in every content search. It is now `"\u0000"`, an
escape rather than a character, which keeps the separator unambiguous and makes the file text.
**Trusting the tree over the journal, as the protocol says.**

**Verified, and the revert is the part worth reading.** Build clean with `-warnaserror`, zero
warnings; **1411 tests, 0 failures** (UI 344 → 359); `dotnet format --verify-no-changes` clean; docs
harness green. With the single `Weave` call taken out, the guard tests do not merely go red —
`UnboundedRecursionIsStoppedBeforeTheStackOverflows` **ends the test process**, which is `R11`
demonstrated rather than described. The tests that assert a *stop* therefore run on a worker with a
twenty-second deadline: a guard test that fails by hanging is worse than no test, because on CI a
hang reads as an infrastructure problem rather than as this regression.

**A help topic, `concepts.code-blocks`**, which the code block had been missing since it landed —
writing one, ports from free identifiers, named-tuple outputs, and a section on exactly what stops
a loop, quoting the messages a user will actually see.

### 2026-08-31 — Typed inputs: what a wire teaches a code block

**`E6-T6`.** An input port with nothing wired into it has no type, so a code block declares it
`dynamic` and finds out at run time. Once a wire lands, the upstream port's type is known and there
is no longer any excuse: the block is recompiled with `Point3d centre = …;` and
`centre.X` resolves at compile time. That is the row's own justification, but it is not the reason
it is worth doing — **`E6-T7`'s wire-typed IntelliSense is impossible without it**, because
completion needs a type to offer members from and `dynamic` offers none.

**Keyed by port name, not by index, and that is forced rather than chosen.** Which identifiers
become ports, and in what order, is decided by *compiling the script* — so the caller that wants to
compile it cannot know an index yet. A name is also the only key that survives an edit: inserting
one identifier moves every index after it.

**The types are in the content hash.** The evaluation cache keys on the node's key, and the same
source over a `double` does not compute what the same source over a `Point3d` computes. Two blocks
that hashed the same would serve each other's results.

**The conversion is `ScriptInput.As<T>` rather than a cast**, and the reason is the message.
`(Point3d)__in[0]` fails with *Unable to cast object of type 'System.String' to type
'Spark.Geometry.Point3d'* — two CLR types, no port, no node, nothing to act on. It also refuses an
`int` where the script wants a `double`, which is the commonest thing a graph delivers, so the
typed path would have felt *worse* than `dynamic` rather than better. `As<T>` widens what a graph
widens and otherwise says *the port 'centre' received a String, but the script uses it as a
Point3d*.

**A type that source cannot name is the same as no type.** An internal type, an anonymous type, an
open generic — `ScriptTypeName.Of` returns null for each, and null means *use dynamic*. Emitting a
name that will not compile, inside a file the user cannot see, is the worst thing this code could
do; refusing is a first-class answer and is tested as one.

**Two defects this exposed, and neither is in the new code.** `CanvasGraph.ReplaceDefinition`
rebuilds a node by removing it and adding it back, and removal correctly drops the node's wires and
its group membership. It restored the wires *in* — visibly the hard case — and nothing else. So
editing a code block's source had always **silently detached everything downstream of it** and
dropped it out of its group. It ran once per deliberate edit, so nobody caught it. `E6-T6` makes the
same path run on every connect, where a defect like that is not a bug but an unusable feature. Both
are fixed and both have a test; the general shape is [N45](NOTES.md).

**Where the re-typing is triggered from.** The canvas, not the engine: `CanvasGraph.Scripts` is the
factory, null when scripting is off, and `Retype` runs after a connect or a disconnect and **does
nothing when the definition's key is unchanged** — which is not an optimisation, because a rebuild
moves a node's slot and doing that on every wire in the graph would renumber the canvas for no
reason. Opening a document re-types afterwards rather than during, because a code block is restored
before its wires exist and at that moment nothing is connected.

**Verified.** Build clean with `-warnaserror`, zero warnings; **1431 tests, 0 failures**
(UI 359 → 379); `dotnet format --verify-no-changes` clean; docs harness green. Reverted the
outgoing-wire restoration once and watched `RetypingKeepsTheWiresLeavingTheBlock` go red.

### 2026-08-31 — Completion that follows the wires

**`E6-T7`, the language service half.** A code block's port is called `centre` and nothing in the
text says what a `centre` is. `ScriptCompletion.CompleteAsync` now takes the block's ports and what
the graph knows each one carries, so typing `centre.` lists the members of whatever is wired in.
That is the demo this project has been describing since M0, and it is now a passing test rather
than a claim.

**The declarations go in as a one-line prefix of top-level statements, not as the generated
frame.** The completion document is parsed as `SourceCodeKind.Script`, which is what makes a bare
`var p = new Point3d(…);` parse at all ([N33](NOTES.md)); wrapping the snippet in the class and
method the compiler sees would need `Regular` and would give that up. One line, because the caret
offset is what an editor sends and gets back, and a newline here would move every line of the
user's snippet relative to what Roslyn is looking at. The caret is shifted by the prefix's length,
and there is a test that would otherwise pass by completing whatever happens to sit under the
unshifted offset.

**An unwired port completes as `dynamic`, and the negative test is the important one.** The
compiler will declare it `dynamic`, so offering `Point3d`'s members there would be a promise the
compile does not keep — `E6-T13`'s invariant, that a list which disagrees with the compiler is
worse than no list, is either held here or lost here.

**`E6-T7` stays `In progress`, on purpose.** There is no editor in the application yet, so nothing
a user can see has changed. The row closes with `E6-T11` and `E6-T12`, and saying so is cheaper
than a row that reads `Done` and demos nothing.

**Verified.** Build clean with `-warnaserror`; **1436 tests, 0 failures** (UI 379 → 384);
`dotnet format` clean; docs harness green.

### 2026-08-31 — The completion invariant, a repair that was deleted, and the bug it was hiding

**`E6-T13`, and it is the most useful step of the day for a reason that is not in the row.**

**The invariant half landed as written.** `ScriptCompletion` gains a constructor over a
`ReferenceCatalog` — the same catalogue a code block compiles against — so the list and the compiler
are given the same references and the same imports from one source rather than two that drift. A
list that disagrees with the compiler is worse than no list, because the user believes it, and
taking both from one object is the only way to be certain they cannot.

**The other half was withdrawn on measurement.** The row asks for a port of CADScript's
`ScriptTextRepair`: balance the delimiters a user has not closed yet, so the parser can see past
them. It was written — a proper one, ignoring braces inside strings, comments, verbatim strings and
character literals — and then, *before being trusted*, measured against eight half-typed snippets
with it and without it. **It made no difference to a single one.** Roslyn recovers from an unclosed
brace, bracket, parenthesis and lambda body unaided. Kept, it would have been a hundred lines with
a dozen tests that pass whatever the completion engine does. **It was deleted.** Porting was always
a strategy and never the deliverable.

**And the measurement found the real defect, which nothing else would have.** In the first run
*every* snippet after the first missed, with and without the repair — which is not the shape a
repair-shaped problem has. `ScriptCompletion` was adding a Roslyn `Document` per request and
removing none, and two script documents in one project are two sets of top-level statements: from
the second request on, the semantic model sees duplicate definitions and completion returns
**nothing**. Not an error and not a slow list. It survived because every M1.5 spike test built its
own instance, so no test ever made a second request. **An editor makes one request per keystroke**,
so the code block's headline feature would have worked exactly once per code block. One document,
replaced through `TryApplyChanges`. [N46](NOTES.md).

**What is left is three tests that can fail.** Completion still answers on the tenth request — a
loop, because one call could never catch it, and it goes red the moment the old shape comes back.
Completion answers inside unfinished text — the user-facing guarantee, asserted without claiming
anything about *how*. And the list and the compiler share a catalogue.

**Verified.** Build clean with `-warnaserror`; **1442 tests, 0 failures** (UI 384 → 390);
`dotnet format` clean; docs harness green. Reverted the one-document fix once and watched
`CompletionKeepsAnsweringAcrossRequests` go red.

### 2026-08-31 — The editor, and `E6-T7` closes: completion that follows the wires, on screen

**`E6-T11` and `E6-T12`, and with them `E6-T7`.** The inspector's plain text box is gone.
`CodeBlockEditor` is an AvaloniaEdit host with C# highlighting, line numbers and a completion list
built from the ports the graph knows about — so placing a code block, wiring a point into `centre`
and typing `centre.` now lists `Point3d`'s members **in the application**, which is what the row
always meant and what three earlier steps could only assert in a unit test.

**The list is not a `Popup`, and that decision is the step's finding.** A popup can extend past the
pane's edge, which a narrow inspector wants. It cannot be tested: the headless session every UI test
here runs in has no window overlay layer, and `IsOpen = true` throws. **The way that failure
presented is the part worth remembering** — the setter throws *after* the property has taken its
value, and the control opens the list from a fire-and-forget task, so `IsCompletionOpen` answered
`true` while the exception went into an abandoned `Task`. Eight of twelve tests passed *over a
thrown exception*; only the two that awaited the request saw it. [N47](NOTES.md). The list is now a
`Border` on a `Canvas` inside the control: clipped to the pane, which is a real loss, and every
behaviour asserted rather than looked at.

**Placement is the M1.5 spike's C3 finding, turned into a guard.** `GetVisualPosition` answers in
the text view's document coordinates, so the scroll offset comes off; without it the list is right
on the first screenful and further wrong with every line scrolled. `CompletionOrigin` is readable
for exactly that test, and taking the subtraction out turns it red.

**The keyboard is the other half of `E6-T12`.** The editor keeps focus while the list is open and
forwards Up, Down, Enter, Tab and Escape to it, so typing never stops. Committing **replaces what
was typed** rather than inserting after it — get that wrong and `centre.Di` + Enter gives
`centre.DiDistanceTo`, which reads as an engine that does not understand its own list. Filtering is
local rather than a fresh request per keystroke, and a prefix matching nothing closes the list,
because a rectangle with nothing in it sitting over the user's code is worse than no list.

**And a plain answer to a question the row did not ask.** An empty completion answer does not open
an empty box, and a block with no completion source at all — an inspector in a session with
scripting off — never reaches for a compiler.

**Verified.** Build clean with `-warnaserror`; **1454 tests, 0 failures** (UI 390 → 402);
`dotnet format` clean; docs harness green; and the application runs — `--graph curves --screenshot`
draws 18 nodes, 15 wires, 4 buffer sets, with the OpenGL viewport reporting ready. The scroll-offset
subtraction was reverted once and its test went red.

### 2026-08-31 — The compile cache that survives the process, and errors on the user's line

**`E6-T10` and `E6-T1`, taken together because they share the wrapper.**

**The source map is one subtraction, and it is only one because two earlier steps kept it one.**
Everything the generated frame adds — the prelude's `using` lines, the namespace, the class, the
method, the cancellation check, the guard budget, one declaration per port — goes *before* the
user's first line; and the guard weaver adds no lines at all, because its statements carry no
trivia. So the user's line is the diagnostic's line minus a constant, rather than a table of ranges
somebody has to maintain. A compile error now reads *line 3, column 9: ; expected* instead of naming
a line in the teens that the user has never seen. **A position genuinely inside the frame maps to 0
and is reported unplaced** — blaming it on the user's first line would send them to inspect code
that is correct.

**The persistent cache could not use the key the row specifies, and finding that out is the step.**
`E6-T10` says `Hash(normalizedText, inputPortTypes, referenceCatalogVersion, langVersion)`. The
catalogue's *version* is a counter of how many times it has changed **in this process**, so it is 0
in every fresh one — which is exactly the situation the on-disk cache exists for. Two different sets
of references would have shared an entry across runs: same text, same counter, wrong assembly. The
disk key carries `ReferenceCatalog.Fingerprint` instead — every reference's path, length and
last-write time, sorted and hashed — plus the guard limits and a `GeneratorVersion` constant that
must be bumped whenever the generated frame changes.

**Only the input names are written beside the assembly**, and the reason is worth stating: they are
the one thing that cost a compilation to learn (`E6-T5` infers them from what the compiler says is
undefined). Output ports come from the script's syntax and cost nothing; an input's *type* is
already in the key, so a cached assembly cannot be read back under types it was not compiled for.
An entry is therefore two small files, and reading it back skips **both** Roslyn passes rather than
one.

**Every failure in the cache is a miss.** A read-only directory, a full disk, a file half-written by
a process that was killed, an assembly emitted by a build whose frame has since changed — all have
the same right answer, which is to compile it. Three tests corrupt an entry deliberately: truncated
bytes, a missing ports file, and no cache directory at all.

**Verified.** Build clean with `-warnaserror`; **1467 tests, 0 failures** (UI 402 → 415);
`dotnet format` clean; docs harness green. The cache is tested across **two factories over one
directory**, because a single factory would answer from the resident cache and prove nothing — two
is the shape of the case the row exists for, which is closing Spark and opening the graph again.

### 2026-08-31 — M4 closes: the collectible context, and a graph that is not run because it was opened

**`E6-T3`, `E6-T15` and the rest of `E6-T16` — and with them M4.**

**Script assemblies were permanent.** `Assembly.Load(bytes)` puts them in the default context,
where nothing can ever unload them; a code block is recompiled on every edit and on every change to
what is wired into it, so ten minutes' work on one script left dozens of assemblies in the process
for good. They now load into a collectible `ScriptLoadContext`. **Its `Load` override returns null
on purpose** — that defers to the default context, so a script's `Point3d` is the graph's
`Point3d`. A context that resolved its own copy would hand the script a type with the same name and
the same shape that nothing could be assigned to, and that is the most confusing failure this layer
could produce.

**`E6-T15`'s warning turned out to be exact.** A delegate into user code pins the context it lives
in, and the resident cache is full of them — so `Unload` clears the registry *first*. Reverting
that one ordering turns the test red, and the test is a **weak reference that has to go dead**:
`AssemblyLoadContext.Unload` returns whether or not the context can actually go, so a test
asserting *no exception was thrown* would have passed in precisely the case the row exists to
prevent. A second test pins the other half — a definition still on somebody's canvas keeps the
context alive, correctly, and goes on working.

**And the trust posture, which is the user-facing half.** A graph containing a code block is
opened, drawn, and **not run**; a banner in the properties pane says how many code blocks it
contains and offers *Run once* and *Always trust this file*. Those are two decisions and are
offered as two, because a store that recorded every run would quietly turn a one-off into a
standing permission. The allowlist is keyed on **origin and exact content** — the file alone would
inherit a colleague's edits, the content alone would let a graph carry its permission wherever it
travelled. `--no-script` works on `spark run` and on the desktop, and refuses rather than dropping
the executable parts: a graph that ran with its code blocks silently missing would produce a wrong
answer quietly, which is worse than an error.

**One bug this exposed, and it had been there since the code block landed.** A saved graph
containing a code block could not be reopened in a session that had never *placed* one — the shell
passed whatever `_session.Scripts` happened to be, which is null until something enables it. So
"save a graph with a code block, restart, open it" failed, and nothing tested it because every test
that opened one had placed one first. Scripting is now enabled when the document turns out to need
it, and **only** then, which is also exactly what `E6-T14` asks for.

**M4 is complete but for one row's second half.** `E6-T14`'s docked C# Script Node is not built; the
inline Code Block covers every behaviour in the epic and the docked variant is a second
presentation of the same pipeline rather than new machinery. Everything else in `E6` is done: the
Roslyn pipeline, semantic port inference, named-tuple outputs, typed inputs, wire-typed
IntelliSense in a real editor, guard weaving, cancellation, both compile caches, the source map, the
collectible context and the trust posture.

**Verified.** Build clean with `-warnaserror`; **1487 tests, 0 failures** (UI 415 → 435);
`dotnet format` clean; docs harness green. The registry-clear ordering was reverted once and two
tests went red.

### 2026-08-31 — M5 opens: the surface layer

**`E2-T17` and `E2-T18`.** `Surface` beside `Curve`, and eight concrete surfaces on it: plane,
sphere, cylinder, cone, torus, extrusion, revolution and ruled. This is the spine of M5 and the
prerequisite for M6's BRep faces, so it was taken first and taken whole rather than one type at a
time.

**The contract mirrors `Curve` on purpose, and differs only where a surface differs.** Sealed types
over a `private protected` base, so the set is closed to the assembly and a tessellator can know
every one there is. Two domains rather than one, neither assumed to be [0, 1]. Wrapping in a closed
direction, exactly as a curve wraps. And **numeric derivatives with analytic overrides**: every
surface *can* be differentiated by central differences, so a new type is correct before it is fast,
and every analytic type overrides because central differences cost about half the available
precision.

**Iso-curves are real `Curve` objects.** Arc length, division, tessellation to a tolerance and the
bounding box all work on them with no second implementation — and that is why `Surface` has a
`TransformedBy`: an iso-curve has to be transformable, which it can only be if its surface is.

**Three things the row asked for are deliberately not built, and say so on the type.** `Trim` needs
the planar layer (`E2-T13`); `ToNurbsSurface` needs `NurbsSurface` (`E2-T19`); surface/surface
intersection is exact-kernel work behind `E2-T28`'s seam. Each is a row, not an omission.

**The degeneracy test was the one genuinely subtle piece.** A sphere's pole has no normal, and the
obvious check — *is the cross product zero* — fails twice over. At `v = π/2` exactly, `cos v` is
6.1e-17 rather than 0, so nothing is zero and a normal is confidently returned where there is none.
And the two derivatives at a pole are still perfectly *perpendicular*, so a ratio of the cross
product to the product of the lengths is order one and passes as well. What works is comparing one
derivative's length to the other's: at a pole one has collapsed and the other has not.

**Two closed forms were deliberately not written, and both were tempting.** A cone's lateral area is
measured along the **slant**, and the obvious version measured along the axis is short by exactly
`sec α` while looking entirely plausible. An extrusion has no closed form worth having at all —
*length × height* is right only when the sweep is perpendicular to the profile, and extruding a line
along its own direction gives a surface of zero area that the formula says is twelve. Both have a
test, and the extrusion type carries a note saying why it does *not* override `Area`.

**The sphere, the cylinder, the cone and the torus refuse a non-uniform scale.** The kernel has no
ellipsoid, so returning a sphere of some averaged radius would be wrong in a way nothing downstream
could detect. Refusing is the honest answer and all four give it in the same words.

**`E2-T31` earned itself again.** The reflection-driven round-trip test went red the moment the
eight new types existed — *these public geometry types have no serialization sample* — which is
precisely what it is for. All eight now serialise, and surfaces are compared **by sampling their
grid** rather than by equality, for the same reason curves are: equality on a surface is a tolerance
question, and `Assert.Equal` on two records compares what `ToString` shows, which would pass a
surface that came back with the right frame and the wrong radius.

**One flake fixed on the way.** `UnloadingReleasesTheScriptAssemblies` passed alone and failed
inside the full suite: an unload completes over several collections and how many depends on what
else the process is doing. It now polls to a deadline and exits as soon as the answer is known,
**and the negative case uses the same helper** — which is what stops *the context is still alive*
passing merely because the collector had not got round to it.

**Verified.** Build clean with `-warnaserror`; **1704 tests, 0 failures** (Geometry 584 → 652);
`dotnet format` clean; docs harness green; and the UI suite run three times over to confirm the
unload assertion is stable.

### 2026-08-31 — `NurbsSurface`, and what "exact" actually covers

**`E2-T19`, and most of it was already written.** A NURBS surface is a tensor product, so its basis
functions are the curve's evaluated in two directions — which is why the first move was to take
`BasisDerivatives` off `NurbsCurve` and put it on `KnotVector`, where it belonged. Two callers of
one implementation of de Boor's A2.3, rather than two copies with somewhere to drift.

**What the surface adds over the curve is small and worth naming.** Weights are held homogeneously
once at construction, so evaluation is a four-dimensional weighted sum and one divide. The bounding
box is the control net's, **exactly**, by the convex-hull property — no sampling, no padding, no
possibility of a bulge escaping it, which is a genuinely better answer than the base class's. And
every affine transform is exact, because the basis functions do not depend on where the control
points are; a NURBS surface takes the non-uniform scale that every analytic surface refuses.

**`ToNurbsSurface` came with it, exact, for five types.** Plane, cylinder, cone, sphere and torus.
Everything rests on one fact — three control points with weights `1, cos(θ/2), 1` reproduce a
circular arc of sweep θ exactly — and on two details that are easy to lose: **the weight is
`cos(θ/2)` and not `cos θ`**, and **the corner control point is the tangent intersection at
`r / cos(θ/2)`, not the arc's midpoint**. Either mistake gives a curve through the right end points
that bulges wrongly in between, which is exactly the shape a sparse test does not see.

**The finding is what "exact" does and does not cover, and it cost six failing tests to state
properly.** The first version asserted that the original and the converted surface agree point for
point at the same parameter. They do not, and the code was right: **a rational quadratic's parameter
is a projective function of the angle rather than the angle**. Halfway along a quarter circle's span
is the arc's midpoint; a quarter of the way along is not 22.5°. There is no representation that is
both exact and angle-parameterised, and every kernel makes this trade the same way.

**So the assertion changed to the right one: the implicit equation.** *Every point of a converted
sphere is exactly one radius from the centre*, at 1e-9, over an odd grid. It is a statement about the
sheet rather than about the parameterisation, and it is **stronger** — breaking the weight
deliberately turns eight tests red where the point-for-point version would have caught six. The
grid is odd on purpose: an even one lands on span boundaries, which is precisely where a wrong
rational construction is still right, because the control points are on the curve there.
[N48](NOTES.md).

**And the difference is pinned rather than only documented.** `TheParameterisationIsNotPreserved`
asserts that the two surfaces *disagree* at a quarter of the way into a span, so a future change
that quietly reparameterised — and therefore stopped being exact — turns it red.

**What is preserved is the domain and therefore the extent**: the corners and edges line up and a
patch converts to a patch, which is what trimming and a BRep face rely on.

**Verified.** Build clean with `-warnaserror`; **1722 tests, 0 failures** (Geometry 652 → 670);
`dotnet format` clean; docs harness green. The arc weight was broken deliberately once and eight
tests went red; `E2-T31` went red again on the new type until `NurbsSurface` serialised, with a
**rational** sample, because a non-rational one takes the weightless path and never exercises the
weights.

### 2026-08-31 — `Mesh`, and a halfedge structure that describes malformed meshes rather than refusing them

**`E2-T20`.** The type three separate things meet at — tessellation writes it, the viewport draws
it, and every mesh format reads and writes it — so its contract was settled before any of the three
rather than after one of them.

**Triangles and quads in one struct.** `MeshFace` carries four indices with `D = -1` on a triangle.
The other convention, repeating `C` in `D`, makes a degenerate quad and a triangle
indistinguishable and gives four edges where there are three. Two separate face *types* would
double every loop, and splitting quads at the boundary would lose the quad structure permanently —
a tessellated NURBS surface is naturally quads with triangles at the poles.

**Three decisions in the measurement code are worth more than they look.**

- **A quad's normal is Newell's, not the cross product of its first three corners.** A warped quad
  has no single plane, and the three-corner version gives a normal that *flips* depending on which
  corner the winding is listed from. There is a test that starts the same quad at two corners.
- **Vertex normals are area-weighted**, so a vertex where one large face meets three slivers does
  not point almost entirely at the slivers. It costs nothing: an unnormalised Newell normal already
  has twice the face's area as its length.
- **`Volume` is signed, and stays signed.** A closed mesh wound inwards reports a negative volume,
  which is the cheapest reliable way to notice a mesh that will shade inside-out. Wrapping it in an
  absolute value would throw away the only cheap detector there is.

**Colours are packed `uint`s and that is a layering decision, not a preference.** `Rgba` lives in
`Spark.Api` beside `Appearance` because the kernel carries no styling (`E2-T1`), and `Spark.Api`
references the kernel — so the kernel cannot reference it back. **But a scanned or baked vertex
colour is data rather than styling**, and a PLY carrying them would otherwise be read lossily. So
they are here as `0xRRGGBBAA`, the packing every format already uses, with the conversion left as
one line at the display layer. The alternative — a second colour type in the kernel that has to
agree with the first — is worse than the shift.

**The adjacency is lazy and it is the part with the strongest opinion.** Most meshes are produced,
drawn and discarded without anybody asking a topological question, and building a halfedge
structure for them roughly doubles what a mesh costs. So `Mesh.Topology` builds once, on demand,
and keeps it.

**And it describes a malformed mesh rather than refusing to build.** Three faces on one edge is not
a manifold and it is also exactly what a careless boolean produces; a structure that threw would
leave the caller no way to *find* the problem. So the third halfedge gets no twin, it is counted,
and `IsManifold` says so. The same reasoning gives naked edges — the diagnostic a *show me the
hole* tool draws — and `IsConsistentlyWound`, which has to be a separate question from `IsClosed`
because a closed mesh can still be wound inconsistently, and that mesh shades inside-out in patches
and reports a nonsense volume.

**One deliberate slowness, recorded on the method.** `FacesAroundVertex` scans rather than walking
the halfedge fan. The fan is faster and stops at a boundary or a non-manifold vertex, silently
returning *some* of the faces — and on a mesh that may be neither closed nor manifold, which is the
only kind worth defending against, the complete answer is worth more than the quick one.

**Verified.** Build clean with `-warnaserror`; **1751 tests, 0 failures** (Geometry 670 → 699);
`dotnet format` clean; docs harness green. `E2-T31` went red on `Mesh` and `MeshFace` until both
serialised — with a sample carrying **every** optional channel and both a quad and a triangle,
because the channels and the triangle sentinel are the two things a round trip can silently drop —
and `MeshTopology` is excluded with the reason every derived index gets: storing it would mean
storing a second description of the same faces and a promise that the two still agree.

### 2026-08-31 — Tessellation, and a sphere that shades like a sphere

**`E2-T26`, and with it the shaded viewport.** A `Surface` becomes a `Mesh` to a tolerance, and the
viewport draws it.

**Adaptive in each direction on a tensor grid**, refined until the chord sag is inside tolerance.
The limitation is stated rather than hidden: a surface with one tight feature gets refinement across
the whole row and column containing it, where a genuinely adaptive scheme would refine only there.
That is a later row; this is what the viewport needs now.

**Sag is probed at several parameters in the *other* direction, not one.** Measuring a cone's
u-direction sag along a single v samples either the narrow end or the wide one and under-refines the
other, and the failure looks like a tessellator that mostly works. Three probes cost three
evaluations per test and remove the whole class of it. There is a test that measures a cone's wide
end specifically.

**Seams and poles are welded, and that is the decision that makes the output worth anything.** On a
closed direction the last column of samples *is* the first; on a degenerate row every sample is the
same point. Emitted as distinct vertices, a sphere looks perfect from every angle, has naked edges
everywhere it should not, reports a nonsense volume and cannot be booleaned — and none of that is
visible except by asking the topology. So a closed direction reuses the first column's indices, a
collapsed row becomes one vertex with a fan around it, and a cell touching a pole is emitted as a
**triangle** rather than a quad naming one vertex twice. **The price is the texture seam**, which is
a texturing concern and the right thing to give up.

**A pole has no normal and every triangle that meets it needs one.** Refusing leaves a hole where a
sphere's cap should be; inventing the axis is wrong on a cone whose apex is off-axis. Stepping a
thousandth of a span into the surface gives the limit the surface is approaching, which is the
answer a renderer wants.

**The sink is what the viewport actually wanted.** A tessellator returning a `Mesh` allocates the
whole thing before anything can be drawn, and the renderer then copies it again. `ITessellationSink`
takes vertices by index, so a pole can be one vertex used by a whole fan and a seam can be one
column used from both sides — a sink taking whole triangles by position could express neither.
`MeshBuilder` is the reference implementation and the one the tests measure.

**And the viewport draws it smoothly.** `SceneBuilder` tessellates a surface at a display tolerance
derived from its own bounding box — the kernel's 1e-6 would give a one-unit sphere hundreds of
thousands of facets for something a few hundred pixels across — and streams triangles with the
surface's *own* normals. Two details matter: the mesh is triangulated at the drawable rather than at
the accumulator, because `AddQuad` also emits the quad's four edges and a surface would be drawn
with its whole tessellation grid as wireframe over the shading; and a new `AddShadedTriangle` keeps
the per-vertex normals, because everything else the accumulator builds is a faceted marker where
flat shading is right and a sphere is the case where it is not.

**Nine surface nodes and a `--graph surfaces` demo make it visible**, and the screenshot is the
evidence: a smooth sphere, a cylinder, a cone and a torus, each shaded in its own colour, 2,552
distinct colours in the frame where the curve demo has 53.

**Verified.** Build clean with `-warnaserror`; **1626 tests, 0 failures** (Geometry 699 → 718,
Viewport 69 → 74); `dotnet format` clean; docs harness green; and
`--graph surfaces --screenshot` renders all four with the OpenGL viewport reporting ready.

### 2026-08-31 — The mesh formats: OBJ, STL, PLY and glTF

**`E2-T34` and `E2-T35`.** Geometry can now leave Spark in four formats, and come back in two.

**OBJ gained meshes.** Quads stay quads — OBJ has always allowed any arity, every viewer reads
them, and splitting would double a tessellated surface's face count for nothing. The indices are
one-based and **file-global**, which is the single most common way to write an OBJ that opens and
draws the wrong thing: a second object's indices continue from the first's rather than restarting,
and the three streams — vertices, texture coordinates, normals — are numbered independently. And a
mesh with normals and no texture coordinates writes `v//vn`, not `v/vn`, which a reader would take
as a texture index.

**STL is the one format here whose reader is unambiguous, which is why it has one and OBJ does
not.** An STL file contains triangles and nothing else: no materials, no groups, no dialects. There
is exactly one decision on the way in, and it is welding — read unwelded, a printed cube arrives as
36 vertices with no shared edges at all, so it is never closed, never manifold, and nothing
downstream can ask it a topological question. **The match is exact rather than tolerant**: STL
stores singles, so two triangles that meant to share a corner wrote the same four bytes, and a
tolerance would additionally weld corners that were never meant to meet — which is a repair
operation and belongs to whoever asked for one.

**Which form an STL is in cannot be decided by its leading word.** An ASCII STL begins with `solid`
and so do a great many binary ones, because some exporters write it into the 80-byte header. The
reliable test is arithmetic: a binary STL is exactly `84 + 50n` bytes for the count it declares.
Both forms are read, because the ASCII one is what a person hand-edits and what a bug report
arrives as.

**PLY is here for the colours**, and that is the whole reason `Mesh` has a colour channel at all
(a scan carries measured colour and every other format here would drop it).
It reads its **header as a description** rather than assuming a property order: a file whose
vertices carry `x y z nx ny nz red green blue` would otherwise have its normals read as colours,
which is a wrong mesh rather than an error. The binary forms are refused **by name**, because a
half-implemented reader for two endiannesses and arbitrary scalar types would produce a wrong mesh
where a refusal produces a sentence.

**glTF is written by hand, and `NFR-5` is why.** Every glTF package on NuGet brings either a native
dependency or a large object model, and what is needed is one mesh in one scene. It is the binary
`.glb` rather than the JSON form, because a `.gltf` references its buffers by URI and exporting one
produces a *directory* — a user who emails the `.gltf` alone has sent nothing.

**Two glTF conventions have to be right or the model arrives rotated and inside out.** It is y-up
where Spark is z-up, and **the change is a rotation, not a swap**: exchanging y and z alone flips
the handedness, so every face is wound the wrong way and every normal points in. There is a test
that reads the written positions back out of the binary chunk and checks the cube still has a
*positive* volume, which is the only assertion that can tell a rotation from a mirror.

**And the CLI dispatches on the extension.** `spark export --out model.stl` writes STL;
`.ply`, `.glb` and `.obj` do what they say. A user who typed the extension has said what they want,
and writing OBJ regardless would produce a file whose name lies about its contents. Surfaces are
tessellated on the way out at the export tolerance, so the flag that has always meant *how round is
this circle* now also means *how round is this sphere*.

**A second example graph is checked in.** `docs/examples/surfaces.spark`, on the same golden-file
terms as the curve one: it is exactly what this build saves, it opens, it evaluates without error,
and it produces four surfaces. Exporting it writes 1,296 STL facets, 675 PLY vertices and a 32 KB
`.glb`.

**Verified.** Build clean with `-warnaserror`; **1641 tests, 0 failures** (Geometry 718 → 733,
UI 435 → 437); `dotnet format` clean; docs harness green; and all four formats written from the
command line against the checked-in example.

### 2026-08-31 — M6 opens: BRep topology, and a winding the tests caught

**`E2-T22` and `E2-T23`, taken together** because the index model without its navigators is correct
and unusable, which is the whole of the second row.

**Everything is contiguous, and that is the layout's one real idea.** Trims within a loop, loops
within a face, faces within a shell: each is an offset and a count rather than a list of indices.
A whole BRep is nine flat arrays with no indirection and no cycles — which serialises with no
reconstruction step, is immutable without a graph walk, and is **the shape that marshals across a C
ABI in one copy**, which is what [ADR-0020](adr/0020-occt-via-c-abi-shim.md) chose for the OCCT
shim. The decision was made at M0 for reasons that have now been paid for twice.

**Validation returns a list and the constructor checks nothing, on purpose.** A constructor that
threw on a malformed BRep would make it impossible to *read* one in order to find out what is wrong
with it — which is exactly what a repair tool does. So `Brep` takes any nine arrays, `Validate`
reports every problem in one pass, and `BrepBuilder` is what code that is *making* a model uses,
because there an index out of range is a bug to report at the line that wrote it.

**`IsSolid` is the topological form of the question `MeshTopology.IsClosed` asks**: every edge used
exactly twice, once forwards and once backwards. It catches a hole and a face wound backwards with
the same count, which is the same trick the mesh layer uses and is worth having in both places.

**`BrepPrimitives` builds a box and a cylinder**, and they exist so the kernel seam has something to
be tested against before a provider does. The cylinder is the interesting one: **three faces and two
vertices**, where the same shape as a mesh is hundreds of triangles and an approximation. Its seam
edge is used twice by *one* face — once each way — which is precisely the case that makes a trim's
direction flag necessary and that an implementation using two seam edges gets subtly wrong.

**The winding was wrong on first writing, and the tests are what said so.** `ABoxIsASolid` and
`ACylindersSeamIsUsedBothWaysByOneFace` both failed: the box's bottom loop ran the obvious circuit
seen from *above*, and the rule is anticlockwise seen from **outside**, so all four of its trims
reverse. The cylinder's wall used the top circle forwards where the cap already did. Both models
looked entirely plausible, validated clean, and were not solids — which is the failure mode this
whole layer exists to make visible, arriving on the day the layer was written.

**The navigators are `readonly ref struct`s.** A pair of registers, no allocation, and the compiler
will not let one escape to the heap and outlive the model it points into. `BrepFaceView.NormalAt` is
the one that matters most: a face may be the *reverse* of its surface — that is how one surface
serves the inner and outer walls of a shelled solid — so code asking the surface directly would get
the right answer on half a model.

**What this BRep cannot do is stated on the types rather than discovered.** A trim carries no
parameter-space curve, because that needs the planar layer's `Curve2d` (`E2-T13`), so a face's
boundary is described in three dimensions and only an *untrimmed* face can be tessellated —
`IsUntrimmed` says which kind a model is. And every operation that makes new topology — boolean,
fillet, extrude, sew, heal — is behind the kernel seam (`E2-T28`) and is not here. What is here is
the model, its construction, its validation, its measurement and its serialization, which is exactly
the half `E2-T28` says never crosses.

**Verified.** Build clean with `-warnaserror`; **1667 tests, 0 failures** (Geometry 733 → 757);
`dotnet format` clean; docs harness green. `E2-T31` went red on `Brep` until it serialised — nine
arrays written as nine arrays, which is the one geometry type whose round trip needs no
reconstruction at all — with a **cylinder** as the sample rather than a box, because its
twice-used seam edge is the relationship a round trip is most likely to lose. The six topology
records are excluded from serialisation with the reason that they are indices into one model's
arrays and mean nothing outside it.

### 2026-08-31 — The kernel seam, and residency that is provably lazy

**`E2-T28`.** `IBrepKernel`, `KernelResult<T>`, `BrepCapabilities`, `BrepResidency`, and a
no-provider kernel that does exactly one thing.

**A refusal is a value, because an exact kernel refuses constantly and correctly.** A fillet whose
radius does not fit, a boolean of two solids that do not touch, a loft between profiles that cannot
be matched — none of those is exceptional, and an exception would make the ordinary case cost a
stack trace. `KernelResult<T>` carries a `SparkDiagnostic` rather than a string, so a kernel failure
reaches the canvas exactly the way every other failure does: on the node, with a code, with a help
topic. **Reading the value of a refusal throws with the reason in the message**, rather than handing
back a null that fails somewhere else.

**`UnavailableBrepKernel` is a null object, not a null reference**, and it is doing two jobs. It
means no call site needs a null check and none can forget one. And it is what keeps
[ADR-0021](adr/0021-brep-kernel-residency.md)'s requirement — *`Spark.Geometry` must remain useful
with no native component present* — **testable**: a seam whose only implementation was the provider
could not be exercised without one.

**It does one real thing: it tessellates an untrimmed shape.** That is not a special case sneaking
past the seam. A face whose only loop is its surface's own boundary **is** a surface, and
tessellating a surface is `E2-T26`, which is in front of the seam by ADR-0003's own split. What
needs a provider is a *trimmed* face, and that is what it refuses, by name.

**And it flips per face, which the first version did not.** A box has exactly one reversed face —
its bottom — and flipping the *finished* mesh, which is shorter and was what I wrote, turns the
other five over as well. The test said `the volume came out -24`, which is the right magnitude and
the wrong sign, and is precisely the sort of failure a mesh renders convincingly.

**Residency is implemented and its laziness is asserted rather than described.** `BrepResidency` is
the opaque hold; `Brep(BrepResidency)` builds a shape that has not been read out; and **every
structural member of `Brep` goes through the `Raw*` accessors**, which materialise on the way past.
That last part is what makes *lazily, on structural demand* a property of the code — a member
reading a field directly would silently work on an empty model — and a counting fake residency
proves both halves: nothing is read until something asks, and six different questions read once.

**`Brep.NativeBytes` exists for a consequence rather than for symmetry.** ADR-0021 records that an
evaluation cache evicting by *managed* size cannot see a provider's heap, so a graph holding two
hundred resident shapes reports megabytes while holding gigabytes. The number is visible **without
materialising**, which is the only way a cache could use it.

**The kernel is ambient, and that is the one place in Spark that is.** A node is a plain public
static method discovered by reflection (ADR-0005): it has no constructor to receive a kernel
through, and a kernel *parameter* would appear on the canvas as a port on every solid node that
nobody would ever wire. `BrepKernel.Current` defaults to the no-provider kernel, so there is no
unset state. It is deliberately per-process rather than per-session, because two providers would be
two native heaps whose shapes could not be mixed.

**Eleven solid nodes and a help topic.** `Solid.Box` and `Solid.Cylinder` are constructions and work
with no provider; `Union`, `Difference`, `Intersection`, `Extrude`, `FilletAll` and `Hollow` are
operations and refuse without one. `Solid.Volume` measures the *tessellation* and the node's
documentation says so — a node reporting an exact figure it had not computed would be worse than one
that names which it is. The viewport draws a `Brep` through the kernel rather than around it.

**Verified.** Build clean with `-warnaserror`; **1680 tests, 0 failures** (Engine 343 → 356);
`dotnet format` clean; docs harness green; and the surfaces demo now evaluates 25 nodes into 5
buffer sets, the fifth being a solid.

**One thing not verified this time, and it is worth saying rather than glossing.** The GPU
read-back — `--screenshot`'s viewport image — returned *the GL context produced no frame* on four
attempts, on the curve demo as well as the surface one, having worked an hour earlier on the same
code path. An OCCT build is saturating every core of this machine in the background, which is the
obvious explanation and is not a proven one. **The shell image still writes and the status line
still reports the buffer sets**, so the evaluation half is evidenced; the render half is not, and
will be re-checked when the machine is quiet.

### 2026-08-31 — The OpenCascade provider, and the boolean that decided ADR-0020

**`E13-T2`, `E13-T4`, and the working halves of `E13-T1`, `T3`, `T5`, `T6`, `T7`, `T8`, `T9`,
`T10`, `T11` and `T13`.** `native/spark_occt` and `Spark.Geometry.Occt`. **M1.6 was taken and
ADR-0020 stands.**

**`M1.6-C2` is the criterion this whole run was for, and it passed.** Two managed `Brep`s built by
`BrepPrimitives` go out through `ModelWriter` and `LibraryImport` into `spark_occt_import`, are
fused by `BRepAlgoAPI_Fuse`, and come back as a resident `Brep` whose tessellation measures
**42.0** against arithmetic's 42, with twelve faces where each box had six. The same trip runs in
**C**, in `native/spark_occt/test/smoke.c`, so a failure in one and not the other says which half
is wrong. It was the only criterion that could have reopened the binding decision.

**The ABI is about thirty entry points and the estimate was 350–500.** That is not a saving, it is
a different shape. A binding that exposes OpenCascade *types* needs a call per type per operation,
which is where `opencascade-rs`'s 538 comes from; this one exposes **one flat tagged encoding** —
a curve or surface as `(kind, int[], double[])`, a whole BRep as one `spark_model_desc` of
seventeen arrays — so a new surface kind is a number in an array rather than a function.
[N49](NOTES.md). The estimate in ADR-0020 is left standing, because a decision record records what
was believed at the time and that is part of the record.

**The encoding is written twice in two languages and neither compiler can see the other**, so the
round trip is not a nicety. `ModelWriter` and `ModelReader` mirror `spark_occt_import` and
`spark_occt_read`; an off-by-one in an offset table is a build error in neither. Twenty-five tests,
all of which skip rather than fail when the shim is absent — because a build with no native
component is a supported configuration and a suite that went red on it would report a supported
state as a defect.

**Three bugs are worth the space, because all three looked fine.**

**A cylinder with square caps.** The first importer ignored the loops and bounded each face by its
surface's own domain — Spark's trims carry no parameter-space curve, so there was nothing to build
a wire from. That gives a correct box and a cylinder that is a tube with two flat plates. It sews,
it meshes, it draws, and every boolean on it refuses. **The demo graph found it**, which is an
argument for demo graphs. The fix is the path an IGES import already takes: build the wires from
the 3D edges and let `ShapeFix_Face` project them onto the surface. [N51](NOTES.md).

**Every imported solid inside out, and half a fix that looked like a whole one.** Sewing orients a
shell consistently and picks the global sign **arbitrarily**; changing how the faces were built
changed the sign with nothing about the geometry changing. Every imported box measured **−24**.
`BRepLib::OrientClosedSolid` flips the solid's *flag*, which is enough to mesh correctly and not
enough for the boolean operators, which read the faces — with only the flag flipped, a union of two
24-unit boxes came back as **50** and a difference removed material that was never inside.
`ShapeFix_Solid` turns the faces. **A shape that meshes correctly has not been shown to be
correctly oriented**, and the test that separates them is a boolean, not a picture.
[N50](NOTES.md).

**A tolerance that meant one thing to a boolean and another to a mesh.** A test reused its
operation tolerance — `1e-6` — for a tessellation of a two-metre sphere. That is a legal request.
The process reached **31 GB** before it was killed. `Spark.Geometry`'s own tessellator has always
had a cap; the provider path now has one too, clamped to a hundred-thousandth of the bounding-box
diagonal. [N52](NOTES.md).

**Three of M1.6's nine criteria are answered and six are not, and the six are listed rather than
glossed.** `C1` passed on Windows — its two-operating-system half is void under **D17**, recorded
in `docs/PRD.md` §13 in this same run. `C3` is **measured: 52.0 MB uncompressed staged, 28.4 MB for
the fifteen toolkits the shim links**, against R15's unmeasured 40–160 MB bracket and well under
the 100 MB that would reopen shipping OCCT by default. `C7` is *partly* answered by observation
rather than experiment — the vcpkg port installs `opencascade[core,freetype]`, so FreeType is a
feature of the port and not only a consequence of Visualization. **`C4`, `C5`, `C6`, `C8` and `C9`
were not taken at all.** `C6` matters more than it did: `ShapeFix` is now on the *import* path, not
only behind `Heal`.

**One row's cost estimate met reality and lost.** [R18](PRD.md#12-risks) said fillet on complex
vertex blends would be hard. Filleting every edge of a box fused to a tangent cylinder and then
drilled took **48 seconds** at a radius that fits and refused outright at the radius that looked
right. The demo rounds a plain box instead, and the reason is written where somebody would
otherwise change it back.

**Verified.** The shim builds with **zero warnings** under `/W4` and its C smoke test passes;
`dotnet build Spark.slnx --no-incremental -warnaserror` is clean over eighteen projects;
**1,707 tests, 0 failures, 0 skipped** (Geometry 757, UI 437, Engine 356, Viewport 74,
Geometry.Properties 43, **Geometry.Occt 25**, Architecture 11, Docs.Verify 5); `dotnet format` is
clean. **And the GPU read-back works again** — it was failing at the end of the last step with an
OCCT build saturating the machine, which was the suspected cause and now looks like the right one.
`--graph solids --screenshot` writes a viewport showing a drilled box-and-cylinder, a hollowed box
and a box with visibly rounded edges.

**What is not done, said plainly.** STEP and IGES are not exposed though the provider can do both.
Split, trim, thicken, draft and offset are not written. The evaluation cache still does not read
`NativeBytes`, so NFR-4 is half done — the number exists and nothing consumes it. `Message_Report`
translation and the `.brep` dump for reproducing a bug upstream are not written. Trimmed faces come
*back* from the provider and still cannot be authored.

### 2026-08-31 — Trimmed, and exported to STEP: M6's headline sentence is finished

**`E13-T7` and the rest of `E13-T8`; `E13-T12` opened and mostly landed.** M6 promises **solids
that can be combined, filleted, shelled, trimmed and exported to STEP**. The provider brought the
first three. This brings the other two.

**Split is not a fourth boolean, and the test says so in arithmetic.** `BRepAlgoAPI_Splitter` keeps
every piece where a `Cut` throws the far side away — a 4-cube split by a plate comes back as three
solids whose volumes *add back up to 64*, and the same cube differenced by the same plate comes
back at 60.8. **`Trim` is a managed composition of `Split` and a point test** rather than a seventh
entry point: `spark_occt_shape_contains` is worth having on its own, and the ABI stays small, which
is D17's whole argument for keeping it hand-written.

**Offset and thicken close `E13-T8` apart from draft.** Thickening needed the same orientation fix
the importer did, for a reason worth stating: **a sheet has no inside until the call gives it one**,
and `MakeThickSolidBySimple` follows the sheet's face normal rather than asking. Thickening a
world-XY plate upwards measured **−8**.

**STEP and IGES are two entry points, not four.** `spark_occt_write_file` and
`spark_occt_read_file` take a format opcode, and the managed side dispatches on the extension with
a refusal that names what it does know. STEP goes out as **AP214** — the schema most CAD systems
read most reliably; AP242 is richer and there are no assemblies, names or colours here to put in
it. IGES goes out in **BRep mode**, because the alternative loses the topology. A read is healed on
the way in, which is the one place ADR-0021's caution about `ShapeFix` does not apply: a shape that
has just crossed a file format has no parameterisation of ours to drift from.

**`spark export --out part.step` works end to end.** `docs/examples/solids.spark` — a new golden
file, checked in like the curve and surface ones — writes **9 solids, 74 faces, 188 KB** of AP214.

**Two of M1.6's open criteria fell out of the linker, and both answers are no.** `spark_occt.dll`
imports fifteen OpenCascade toolkits directly and `TKXCAF` is not among them — which was the
encouraging half and is not the answer. **The transitive closure is thirty-three DLLs and 45.1 MB**,
and `TKDESTEP` pulls `TKXCAF`, `TKLCAF`, `TKCAF`, `TKVCAF` and `TKCDF` in with it. So **`C8` is no:
STEP cannot be shipped without XCAF.** `freetype`, `TKV3d` and `TKService` are in the closure too,
arriving through the interchange toolkits, so **`C7` is also no**. Both criteria said in advance
that a finding either way passes, and both findings cost nothing: 45.1 MB is *smaller* than the
52.0 MB the build script stages, and that is now the number `E13-T17` should plan against.
[N53](NOTES.md).

**One thing was found by reading the CLI's own output.** OpenCascade's default messenger writes to
`cout` — a transfer banner per shape, then `** WorkSession : Sending all data` — which landed in
the middle of `spark export`'s output and made it undiffable. `RemovePrinters` at initialisation,
beside `OSD::SetSignal(false)`, and for the same reason: **a library on the far side of a C ABI has
no business owning the caller's process-wide state.** [N54](NOTES.md).

**Verified.** The shim builds with zero warnings and its C smoke test now covers a split, a piece
walk and a point test as well as the boolean; `dotnet build --no-incremental -warnaserror` clean;
**1,724 tests, 0 failures, 0 skipped** (Geometry 757, UI 439, Engine 356, Viewport 74,
Geometry.Properties 43, **Geometry.Occt 39**, Architecture 11, Docs.Verify 5); `dotnet format`
clean; and `spark export --open docs/examples/solids.spark --out part.step` writes a file whose
text says `CYLINDRICAL_SURFACE` and `ADVANCED_FACE` and never says `POLY_LOOP`.

**What is still owed, said plainly.** `E13-T12`'s real acceptance is a **public corpus and a
third-party viewer, never our own reader**, and neither has been done — asserting on the file's
text is evidence a round trip cannot give and is still not a viewer. Draft angles are unwritten.
The evaluation cache still does not read `NativeBytes`. The threading policy (`E13-T14`, `Q14`) is
untouched, and so are the licence pipeline (`E13-T16`) and the per-RID distribution (`E13-T17`).

### 2026-08-31 — The cache learns what a solid weighs, and a failure leaves evidence

**`E13-T3` and `E13-T13`, both closed.** Two of `E13`'s acceptance criteria that are neither the
headline nor a pipeline.

**NFR-4 was a sentence and is now a test.** `Brep.NativeBytes` has reported a real figure since the
provider landed and **nothing read it**: `EvaluationCache` evicted on entry count alone, so two
hundred resident shapes sat inside any ceiling anybody would set while holding gigabytes. The cache
now has a **second ceiling** — 512 MB by default, walked out of each entry's outputs through lists
and `Displayable`s — and `TheCacheEvictsOnBytesWithTheCountNowhereNearItsCeiling` is the test that
could not previously be written: ten entries against a ceiling of a thousand, evicted anyway.

**Two decisions inside it are worth the words.** **One entry is always kept**, because a single
result larger than the whole budget would otherwise be evicted the instant it was stored and every
lookup would miss on something just computed — a cache worse than no cache. And **a shape held by
two entries is counted twice**, because comparing values for identity across entries would buy
exactness at the cost of speed, and the error is an over-estimate, which evicts sooner than needed
rather than later than it should.

**The fake residency is in `Spark.Engine.Tests` and the real one is not.** The arithmetic — a total
accumulated, subtracted on eviction, enforced independently of the count — is a question about the
cache and a fake answers it. Whether the *number* is real is a question about the provider, and
`Spark.Geometry.Occt.Tests` answers it against shapes OpenCascade is actually holding.

**R16's mitigation is all three parts now.** An algorithm's `Message_Report` alerts are appended by
key when a boolean or a split fails — not translated, because the keys are OpenCascade's own and
meant for its developers, and deliberately better than *the operation did not complete*.
`BRepCheck_Analyzer` is available through `OcctBrepKernel.Check`, and names *how many* bad faces,
edges, vertices and wires. And a failing operation writes its inputs as **Draw-Harness-compatible
`.brep` files** when `SPARK_OCCT_DUMP` names a directory.

**The dump's first version dumped nothing, and the reason is the interesting part.** It wrote the
*managed* `Brep`s it was given — and an imported box has no residency of its own, so there was
nothing to write. The shapes worth capturing are the **handles actually handed across**, which the
operation is already holding. A test asserting the diagnostic names the files is what caught it;
a test asserting *a failure is refused* would have passed.

**It is off unless asked for, and that is the design rather than a default.** An exact kernel
refuses constantly and correctly, so a build that wrote a file on every refusal would fill a disk
with evidence of things working as designed. Setting the variable is what somebody does when they
are reproducing something.

**Verified.** Build clean with `-warnaserror`; **1,744 tests, 0 failures, 0 skipped** (Engine 356 →
367, Geometry.Occt 39 → 48); `dotnet format` clean; docs harness green.

### 2026-08-31 — Four criteria, one requirement restated, and a mesh that welds

**`M1.6-C4`, `C5`, `C6` and `C9`; `E13-T11`'s NFR-8 question; `E13-T14`.** Every one of these was
written before the spike and says a **finding either way passes**. The only failure available was
not asking, and they had not been asked.

**`M1.6-C4` — a materialisation costs 0.44 ms.** On a drilled plate — six holes cut into a
20 × 12 × 2 block — the first structural question costs **0.44 ms** and **two thousand further
questions cost 0.04 ms**. The arrays are built once and everything after is a field access, which is
what ADR-0021 claims. The assertion is on the **ratio**, not the milliseconds: a bound on the
absolute time would be a bound on this machine, and the claim under test is *paid once*.

**`M1.6-C5` and `E13-T14` — independent work is independent.** Twenty threads × twenty-five
union-and-tessellate: **500 results in 2.73 seconds, zero failures**, and all five hundred volumes
came back 42. The assertion is on the volume rather than on the absence of an exception, because a
race corrupting a shared table shows up as a **wrong number** first. The thread-local error channel
was checked rather than assumed — twenty threads failing at once each read their own reason. **The
policy is written down**: distinct shapes concurrently, one shape never from two threads. R20's
single-writer fallback is not needed for the case replication actually produces.

**`M1.6-C6` — yes, and the record's own Notes anticipated it.** `ShapeFix_Face` and
`ShapeFix_Solid` expose their fixes as individually settable modes, which is the mechanism the
question was about. Measured on behaviour rather than API: **healing a shape that needs nothing
changes nothing that can be seen** — a healed box keeps six faces, twelve edges, eight vertices, one
shell, every surface still a plane, its volume to six decimal places and its corners to nine. So
ADR-0021's drift argument is about what `ShapeFix` is *allowed* to do rather than what it does
unprompted, which is exactly the revisiting that record invited.

**`M1.6-C9` — the row was spent rather than estimated.** `E13-T3` is `Done`, and the 2–4 week
bracket was never re-estimated because it was overtaken. The honest form: the bracket was wrong in
the safe direction, and [N49](NOTES.md) says why — the ABI is thirty entry points rather than
hundreds, so the handle table it budgeted for is three `SafeHandle` subclasses.

**NFR-8 is restated, which is one of the two outcomes it demanded and is not the suppression.** The
provider's mesh of a box has **twenty-four naked edges**, and welding by default would be the wrong
repair. Every kernel tessellates a BRep **face by face** — ours and OpenCascade's alike — so every
vertex on a shared edge exists twice, at identical coordinates. **The mesh is geometrically closed
and topologically split.** The split is what makes shading right: a vertex carries one normal, and a
welded cube shades like a ball.

So `Mesh.Welded(tolerance)` is an **operation**, and the guarantee is *a mesh that welds closed at a
tolerance the caller chooses*. Measured against the provider's own output: a box 24 vertices → 8, a
cylinder 1442 → 720, a union 60 → 20, a drilled plate 8676 → 4328, every one closing with zero naked
edges. **Ask for it when the topology is what matters — a volume, an STL, a printer — and not when
the shading is.** [N55](NOTES.md).

**One implementation detail in the weld is a correctness detail.** The merge hashes positions into a
grid, and **a grid is not a metric**: two points a hair apart can land in adjacent cells. All
twenty-seven neighbours are checked, so the same mesh translated by half a cell welds the same way.
`WeldingIsNotSensitiveToWhereTheGridFalls` translates it eight times and is the test for it.

**Verified.** Build clean with `-warnaserror`; **1,750 tests, 0 failures, 0 skipped** (Geometry
757 → 763, Geometry.Occt 48 → 54); `dotnet format` clean; docs harness green. Every measurement in
this entry is printed by the test that made it and recorded against its criterion in
[TASKS.md](TASKS.md).

### 2026-08-31 — Draft angles, and licence obligations met by the pipeline

**`E13-T8` closes; `E13-T16` lands everything that does not need counsel; `E12-T18`'s command-line
half.** ABI version 5.

**Drafting refused all six faces of a box, and the reason took three attempts to find because each
attempt hid the next.** OpenCascade only tapers planar, cylindrical and conical faces, and a box's
top and bottom are parallel to the neutral plane — no line to tilt about. Expected. What is not
documented anywhere obvious is the consequence: **a failed `Add` poisons the algorithm**, so every
later `Add` raises `Standard_ConstructionError` until `Remove` cancels the bad one. Catching and
removing got past that, and then **`Build()` itself raised, with an empty message**.

**The fix is to not ask.** Look at each face's surface first, skip a plane whose normal is parallel
to the pull, skip anything that is not planar, cylindrical or conical, and only then call `Add`.
Simpler than the recovery, and it is what a moulder means by *draft this part* — refusing a whole
solid because its top is flat would be the wrong answer to the right question. **The general shape,
which is not about drafting:** when a library's failure mode is *poisons the object* rather than
*returns false*, a precondition check is not defensive programming, it is the only correct
structure. [N58](NOTES.md).

**The neutral plane is a parameter and that is not ceremony.** "Tilt this face by two degrees" does
not say around *what*, and the answer changes the part: pivot about the top and pivot about the
bottom give the same angle and different sizes.

**The licence obligations are met by the pipeline now, which is what `E13-T16` asks for.**
`THIRD-PARTY-NOTICES.md` names OpenCascade, its licence and the exception, carries the sentence the
exception actually requires, and maps every obligation to where the build meets it. `licences/`
ships the LGPL-2.1 text and the Open CASCADE exception rather than linking them. And
`spark_occt.buildkey.json` is written beside the binaries recording
`(rid, configuration, occt-version, vcpkg-baseline, shim-source-hash, spark-commit)`.

**The shim hash is over the source files rather than a git commit, deliberately.** An uncommitted
edit changes the artefact, so it has to change the key — R22 is that a source offer must be
honourable against *a specific artefact*, and a commit hash that no longer describes the binary is
worse than no key.

**The notices travel with the binaries.** A notice left behind in a source tree is a notice nobody
who received the software can read, so `build-native.ps1` stages it and the licence texts beside the
DLLs. `spark --version` prints the OpenCascade version and the required sentence, or says no kernel
is installed — that is `E12-T18`'s command-line half; the About box still needs a dialog that does
not exist.

**Four architecture tests hold all of it**, including that nothing in the repository turns on
`PublishSingleFile` or `PublishAot`, which the relink obligation forbids over OpenCascade.

**And the docs harness was right about a document it had never seen.** Staging the notices beside
the binaries put a second copy in `artifacts/`, whose relative links resolve from the repository root
and not from where it lands. Three broken links, correctly reported. The repair is to exclude
`artifacts/` from the scan — a staged copy is build output — and the shape will recur for anything
else the build copies there. [N59](NOTES.md).

**What is explicitly not done.** `Q13`'s six counsel questions, which cannot be settled by writing
more of this. The About box. And this file is not a compliance audit — **nothing in it is legal
advice**.

**Verified.** Shim builds with zero warnings; its C smoke test drafts a box and checks the face
count stays six; build clean with `-warnaserror`; **1,762 tests, 0 failures, 0 skipped**
(Geometry.Occt 54 → 56, Architecture 11 → 15); `dotnet format` clean; docs harness green.

### 2026-08-31 — `Q15` closes, the shim gets a CI job, and the payload is finally weighed

**`E13-T15` and `E13-T17`; `Q15(c)` decided and recorded as `D18`.** The last two rows of M6 that
are engineering rather than an errand for a person.

**`Q15(c)` was one question wearing two arguments, and only one of them survives D16.** The ubuntu
CI leg was justified as a **rot-guard** — the thing that stopped cross-platform support decaying and
kept ADR-0001's Avalonia investment real. D16 voids that completely: there is no option left to keep
alive. But the leg has independently caught a real defect ([N28](NOTES.md)), and *that* is a
different argument — a **second implementation of the same arithmetic**, on a different libc, a
different floating-point library and a different culture default. It never depended on shipping a
Linux build.

**So the leg survives and never builds the provider.** Building OpenCascade for `linux-x64` in CI
would cost an hour per cache miss to guard a platform nobody ships: the expensive half of the
argument is exactly the half D16 killed. And because `Spark.Geometry.Occt.Tests` skips itself when
the shim is absent, the ubuntu leg becomes a **standing test of the supported no-provider
configuration** for nothing at all. **D18**, and `Q15` closes.

**The native job caches twice, and the split matters.** OpenCascade is cached on
`(occt-tag, vcpkg-baseline)` alone — editing the shim must not throw away an hour of dependency
build — and the shim on the full `(occt-tag, vcpkg-baseline, shim-source-hash, rid)`, **the same key
`build-native.ps1` writes beside the binaries**, because an artefact and the thing that identifies it
must not be able to disagree.

**And the job fails on a non-zero skip count**, which is the one failure this arrangement could
otherwise hide: a green provider run that skipped everything looks exactly like a green provider run.

**R15's bracket is measured, and it was about the wrong thing.** The staged `win-x64` payload:

| | |
|---|---|
| **total** | **224.4 MB** |
| the solid-modelling kernel | 52.0 MB (58 native DLLs) |
| everything else | 172.4 MB |

**OpenCascade is 23% of it.** The other 77% is the framework-dependent .NET publish — Roslyn,
Avalonia, Skia, HarfBuzz — which was there before ADR-0020 and which nobody had weighed either. The
kernel is well inside R15's 40–160 MB bracket and nowhere near the 100 MB that would have reopened
shipping it by default. **If the installer is ever too big, OpenCascade is not where to look
first**, which is the opposite of what R15's framing would have led somebody to do. [N60](NOTES.md).

**The publish is framework-dependent on purpose, and that is a licence decision rather than a
packaging one.** The LGPL relink obligation needs OpenCascade to ship unmodified and replaceable; a
single-file bundle that unpacks to a temp directory does not obviously preserve that and NativeAOT
does not preserve it at all. So the two switches that would most obviously shrink the payload are
the two that are foreclosed, and `NothingPublishesSingleFileOrNativeAot` stops either being turned
on by somebody optimising in good faith. [N61](NOTES.md).

**Verified from the staged folder rather than from the build tree**, which is the only verification
that means anything for a distribution row: `spark.exe --version` prints the OpenCascade version and
the notice the exception requires; `spark.exe export --open docs/examples/solids.spark --out
part.step` writes 9 solids and 74 faces; and `Spark.Desktop.exe --graph solids --screenshot` renders
the drilled box-and-cylinder, the hollowed box and the rounded box, with 837 distinct colours.

**What still needs a person, and no script can do:** the installer, code signing and the antivirus
submissions, none of which can invent an identity to sign with. `E13-T12`'s acceptance — a public
corpus and a third-party viewer. `Q13`'s counsel questions. `E12-T18`'s About box.

**Verified.** Build clean with `-warnaserror`; **1,762 tests, 0 failures, 0 skipped**;
`dotnet format` clean; docs harness green; the CI workflow parses and now has four jobs.

### 2026-08-31 — Sweep, patch, and a profile that is a wire

**`E13-T9` closes; `E13-T5`, `E13-T6`, `E13-T10` and `E13-T1` close with it.** Everything in `E13`
that is engineering rather than an errand for a person is now `Done`.

**A profile was a curve and should have been a wire, and the encoding already knew.** `build_wires`
read only the curve table of a `spark_model_desc` — the edges, trims and loops were ignored on that
path — so one Spark curve became one wire. Which meant a `PolyCurve` had to be squeezed into a
single NURBS, and `ModelWriter` did it by **interpolating through sampled points**: an
approximation, for a profile every piece of which was exactly representable.

**Honouring the loop table costs about forty lines and removes the fallback entirely.** A loop is a
list of trims, a trim names an edge, an edge names a curve — that is a circuit, which is what a wire
is. Polycurves and polylines now go out as their own segments: lines as lines, arcs as arcs.

**What proves it is a face count, not a tolerance.** A square extruded from four lines has **six
planar faces**; the interpolated version had a curved wall. A chain of line-arc-line extrudes into
two planes and **one cylindrical surface**. Neither number is reachable by a spline that merely
passes close to the right points, which is why they are the assertions rather than a distance.
[N62](NOTES.md).

**Sweep and patch complete `§6.1`'s modelling members.** `spark_occt_sweep` is
`BRepOffsetAPI_MakePipe` along a rail — the general case of extrude, kept as its own operation
because the straight case is what most graphs want and needs no second curve. `spark_occt_patch` is
`BRepFill_Filling`, and it takes **edges rather than a wire**, because a patch does not require its
boundary to be one connected circuit.

**A patch is not a loft, and the difference is worth a sentence in the node's documentation.** A
loft goes *through* profiles in the order it is given them; a patch is handed a circuit and finds a
surface that meets it. Asking for one when you meant the other produces a plausible answer to the
wrong question, which is the worst kind.

**Four rows closed by review rather than by code, and the review is the point.** `E13-T1` has all
nine `M1.6` criteria answered — **six of the nine came back with the answer nobody was hoping for
and every one of them cost nothing**, which is exactly what writing the criteria in advance was
for. `E13-T6` needed nothing more for M6. `E13-T10`'s policy question is `M1.6-C6`, answered.
Leaving a row `In progress` because nobody looked at it again is how a register stops being true.

**Verified.** Shim builds with zero warnings; build clean with `-warnaserror`; **1,769 tests, 0
failures, 0 skipped** (Geometry.Occt 56 → 63); `dotnet format` clean; docs harness green.

### 2026-08-31 — D19: 1.0 first, then the Help — and a next-action task ID that pointed at the wrong row

**What.** No code. Five documents: **D19** in `docs/PRD.md` §13, an amendment to the standing
instruction in `AGENTS.md`, a new *After 1.0 — the Help pass* section in `docs/TODO.md`, a
recounted E10 status in `docs/EPICS.md`, and two corrected E10 rows in `docs/TASKS.md`.

**Why.** The client was asked where the project stood and what the end-user Help looked like, and
answered with an ordering: **finish the product, then complete the Help.** That is a sequencing
decision, so it goes in the decision log rather than into a session's memory.

**The thing that made this a step rather than a one-line note.** D19 **contradicts the standing
instruction in `AGENTS.md`** — *everything is documented as end-user help topics, with worked
examples* — which every milestone so far has followed. Left alone, that instruction would be
violated by every step between here and 1.0, and this project has already written down why that
is the worst outcome available: a rule nobody enforces is a preference. So the instruction is
**amended and dated rather than quietly rewritten**, in the same shape D14 and ADR-0009 were
handled: the blockquote is left exactly as written, because it is what the rule returns to on the
day 1.0 ships, and a paragraph beneath it says what is suspended and what is not. **What is not
suspended:** XML doc comments on the contract projects, and the project documents. Those are the
two mechanisms that actually fail a build.

**The trap, recorded in three places because it is the part the client's decision cannot
reorder.** Deferring the Help guarantees it gets written **in bulk**, and a bulk write with no
harness in front of it is `DocGenerator` again — 1,478 hand-maintained entries that drifted until
101 of 108 public constructors rendered blank. So `E11-T2`, `E11-T4`, `E11-T5` and `E11-T6` are
the **first** rows of the post-1.0 pass, not the last. E10 was designed around a Help that grew
continuously; it no longer will, and the harness is what replaces the growth.

**The defect this step actually found, and it was not what the step was for.** *Current state*
and `TODO.md` both named **`E11-T16`** as the next piece of work — "the software renderer and the
CI visual regression". `E11-T16` is the **benchmark suite, run nightly**, and it is `In progress`
for reasons that have nothing to do with rendering. The software renderer is **`E9-T5`**, headless
thumbnails **`E9-T11`**, CI visual regression **`E9-T12`**, all three `Open`. A resuming session
following *Next action* verbatim would have opened the wrong row. Corrected in both files, with
the correction stated on the TODO line rather than tidied away.

**Two stale claims corrected while in there, both in the same direction — the documents
understated the tree.** `EPICS.md` and `TASKS.md` said **three help topics exist** and that
`docs/examples/` was **an empty directory**. There are **nine topics, 3,755 lines**, and three
example graphs. `E10-T7` moves `Open` → `In progress` on the strength of that, and the summary
counts follow: 133 done · 17 in progress · 108 open · 9 withdrawn.

**What the Help genuinely is, now that it has been counted rather than recalled:** 35 C# samples
across three of nine topics, `solids.md` with none, one topic naming ten of **108** nodes, no
index, no generated reference, no in-product renderer — **F1 does nothing** — and no topic for
any of the **18** `SPK####` codes, every one of which already carries a `HelpTopicId` seam
pointing at a document that does not exist. That paragraph is in EPICS and TODO so that *ships
without a Help* is a decision on the record and not a discovery at release.

**Verified.** All three gates, before and after. `dotnet build Spark.slnx --no-incremental
-warnaserror` clean, 0 warnings. The eight per-project executables: **1,769 passing, 0 failed,
0 skipped** — Geometry.Tests 763, UI.Tests 439, Engine.Tests 367, Viewport.Tests 74,
Geometry.Properties 43, **Geometry.Occt.Tests 63 with a zero skip count**, so the native provider
was really exercised, Architecture.Tests 15, Docs.Verify 5. `dotnet format --verify-no-changes`
clean. `Spark.Docs.Verify` was re-run **after** the Markdown edits specifically, because its
link-resolution and `Last updated` checks are the only thing standing behind five edited
documents — still 5/5.

**Cost.** One step, documents only. **What it did not do:** touch `E9-T5`. That is the next step.

### 2026-08-31 — `E9-T5`: the software rasteriser, and a depth buffer that was never cleared

**What.** `src/Spark.Viewport/Software/SoftwareViewportRenderer.cs` and `SoftwareFramebuffer.cs`,
plus `tests/Spark.Viewport.Tests/SoftwareRendererTests.cs`. A CPU rasteriser behind
`IViewportRenderer`: near-plane polygon clipping, a depth buffer, perspective-correct
interpolation, DDA lines with Liang–Barsky viewport clipping, and the GL path's draw order,
lighting model, key-light derivation, selection tint and `0.0006` edge depth bias reproduced term
for term.

**Why it is a match rather than an approximation.** A fallback that draws a recognisably
different picture is a fallback nobody trusts, and — more to the point — `E9-T12` is going to diff
its output against a golden image. Three divergences are deliberate and documented on the type,
all three in the direction of reproducibility rather than fidelity: an **integer-hash dither**
instead of the shader's `fract(sin(...))`, because the sine of a large argument is exactly where
two conforming IEEE 754 implementations may still disagree and this backend exists to produce the
same bytes everywhere; a **DDA line walk** rather than GL's diamond-exit rule; and **no
multisampling**. None affects a software-against-software comparison, which is the only comparison
that will ever be made.

**The defect, and it is [N63](NOTES.md).** A freshly allocated depth buffer is all zeroes, and
**zero is the nearest representable depth, not the furthest** — so a buffer that has not been
cleared rejects every fragment and renders a perfect background with no geometry on it. That is
indistinguishable from an empty scene, from a camera pointing the wrong way, and from a
tessellator that produced nothing. It survived every test except the one that renders *without*
calling `Render` first, because every other test clears depth as its first act.
**Regression-proven the way this project requires:** reverting the two-line fix turns
`AnUninitialisedRendererDrawsNothingAndDoesNotThrow` red, and it was red before the fix went in.

**A claim removed rather than left standing.** The first draft of the lighting method's doc
comment said `SoftwareRendererTests` asserts the software and GL shading agree. **It does not, and
it cannot** — comparing them needs a GPU, which is the thing this backend exists to do without.
The comment now says so, and two tests hold what actually can be held: every shaded pixel lies
between the ambient floor and the fully-lit ceiling computed from the shader's own coefficients,
and a face turned towards the key light is brighter than the same face turned away. The second
test was checked by hand against the derived light vector before it was trusted — the two cases
give Lambert terms of 0.909 and 0.285, so it is not a test that cannot fail.

**Also recorded in N63, because it is a trap:** `System.Numerics`'s perspective matrix is
right-handed with a **Direct3D depth range**, so NDC z runs 0..1, not −1..1. The GL backend has
always fed that matrix to GL and therefore uses only half its depth buffer. The rasteriser matches
the convention deliberately instead of correcting it, because two backends disagreeing about what
a depth means would make every cross-check meaningless.

**Verified.** `Spark.Viewport.Tests` 74 → **87**, all passing. Full build clean at 0 warnings.
Every assertion in the new file reads pixels or depths back; a renderer that initialises and draws
nothing would fail eleven of the thirteen.

**Cost.** One step. **What it does not yet do:** it is not wired into `ViewportControl` as the
actual GL-failure fallback, there is no thumbnail entry point, and there is no CI job. Those are
`E9-T5`'s three justifications and they are the next two steps.

### 2026-08-31 — `E9-T11`: headless thumbnails, a real fallback, and a screenshot that lied

**What.** `ThumbnailRenderer` in `Spark.Viewport/Software/` — a scene to top-down RGBA with no
window and no device. `ViewportControl` now falls back to `SoftwareViewportRenderer` when no
OpenGL context arrives, blitting through a `WriteableBitmap`. And **`--software-renderer`**, which
reaches that path on purpose. Seventeen new tests across two projects: eight for the thumbnail path, nine for the switch and the committed-backend rule.

**Why the switch exists, given the fallback is automatic.** Two reasons, and neither is
convenience. It is the answer to *the viewport is black on my virtual machine* — a support reply
that is an instruction rather than a diagnosis. And it is the only way the software path gets
**photographed**: `--graph solids --software-renderer --screenshot` writes a PNG that a human can
look at, which is how this step was actually verified. The picture is the solids demo, correct:
the fused box and cylinder with the hole drilled through both, the shelled box, the filleted box,
the ground grid and the three coloured axes, with depth and lighting right.

**The defect, and how it was caught, is [N64](NOTES.md) and it is the most useful thing here.**
Adding the fallback gave the control **two** places that could service one `RequestCapture()`
flag. Avalonia paints before `OnOpenGlInit` fires, so `_renderer` is null at that moment and
nothing inside `Render(DrawingContext)` can tell *GL has not arrived yet* from *GL is never
arriving*. On a healthy GPU the software path won the race: `--screenshot` wrote a CPU-rendered
image and printed `OpenGL ready. Version 'OpenGL ES 3.0 (ANGLE ...)'` on the line below it. Both
lines were true and together they were a lie.

**Nothing failed.** The picture was correct, because the two renderers agree by design — that
agreement is the whole point of `E9-T5`. What did not survive scrutiny was a coincidence: the GL
and software runs reported `663 distinct colours, mean luminance 34.7/255` **identically**, and
the two PNGs had the same MD5. Two rasterisers with different dither functions and different line
rules cannot produce identical bytes. **The evidence was that the outputs agreed too well.** A
one-line probe naming the servicing branch settled it in a single run, and the instinct worth
generalising is that an implausible agreement deserves exactly the suspicion an implausible
disagreement gets.

**The fix is a committed-backend rule** — `IsSoftwarePresenting`, with three ways to commit: the
switch, a GL callback that ran and left no renderer, or **no GL callback at all within 1.5
seconds of attachment**. The third has to be a timeout rather than an event, because a context
that fails to be created never calls anything: the absence *is* the signal, and an absence has to
be waited for. `ANewControlDoesNotClaimSoftwareIsPresentingBeforeGlHasBeenHeardFrom` is the
regression test, and it goes red the moment the rule is relaxed.

**Two things tidied under the same fix.** `TakeCapture` now normalises to top-down rows whichever
backend drew the frame — `MainWindow` used to flip unconditionally, which was right for GL and
would have silently inverted every software capture. And the software path renders at one device
pixel per layout unit rather than at `RenderScaling`: a quarter of the fragments on a 200%
display, on the one path that runs when the machine has already proved it has no usable GPU. Both
are choices now, and both are written down.

**Verified.** Build clean at 0 warnings. `Viewport.Tests` 87 → 95, `UI.Tests` 439 → 448, suite
**1,799**, 0 failed, 0 skipped. And — the part no test covers — the application was run on both
backends and both PNGs were opened and looked at. They agree in appearance and differ in bytes,
which is exactly the right answer and is the answer that was wrong an hour ago.

**Cost.** One step. **Next:** `E9-T12`, the CI visual regression, which is what the determinism
tests were built for.

### 2026-08-31 — `E9-T12`: a golden image, proven to fail — and M5 is finished

**What.** `VisualRegressionTests` renders a fixed scene through `ThumbnailRenderer` and compares
it byte for byte against `tests/corpus/viewport/reference-scene.png`. Plus `PngImage`, a 250-line
8-bit RGBA PNG reader and writer, because `Spark.Viewport` takes no UI dependency and a golden
image nobody can open is not a golden image.

**No new CI job, and that is the result rather than a shortcut.** It is an ordinary xunit test, so
`dotnet test Spark.slnx` already runs it on both legs on every push. A bespoke workflow would have
been a second thing to keep working.

**Proven to fail before it was trusted.** One lighting coefficient moved by 0.1% — `0.60` to
`0.601` — and the check reported *3,415 pixels differ (4.45% of 320x240). Worst is 1/255 at
(200, 53): expected rgba(150, 158, 171, 255), got rgba(151, 158, 171, 255).* That is the smallest
change the renderer can express, and it is caught and described. On failure it also writes the
render and an **amplified difference map** beside the golden, which is `E11-T11`'s complaint —
a bare hash mismatch tells you nothing — answered for images.

**`SPARK_UPDATE_GOLDEN=1` rewrites the golden and then fails anyway**, deliberately. A check that
silently rewrites its own expectation when it disagrees is a check that cannot fail, and this
project has already paid for two of those ([N19](NOTES.md), [N20](NOTES.md)).

**One real change to the renderer came out of writing the test.** The specular term used
`MathF.Pow(x, 40)`. `Pow` is transcendental and its last bit is **not** guaranteed identical
across runtimes and platforms — and the whole premise here is that the bytes are the bytes. It is
now seven float multiplications by repeated squaring, which is IEEE-exact everywhere and happens
also to be faster. That is a determinism requirement wearing an optimisation's clothes, and the
method says so.

**What is honestly still unverified, written into the failure message rather than left to be
rediscovered.** `PrimitiveMeshes.Sphere` and `Camera.OffsetDirection` use `MathF.Sin` and
`MathF.Cos` to build the scene's own vertices, and those are not guaranteed bit-identical across
platforms either. The golden was produced on Windows; **whether the Linux leg agrees has been
reasoned about and never observed**, because there is no Linux here. If it goes red there with
nothing changed, the failure message says what to look at and how to tell that case from a real
regression: a small difference concentrated on the sphere is the platform, a large or scattered
one is not.

**A scare that was not a bug, worth recording because the next reader will see it too.** The first
golden showed the sphere as a white wireframe with what looked like ground grid visible through
it. A probe settled it in one run: 576 triangles, normals present, centre pixel `rgba(0.40, 0.42,
0.455)` at depth 0.994. It renders. `PrimitiveMeshes.Sphere` emits 360 near-white edge segments,
and at fifty pixels across they dominate the dark shaded surface behind them. The scene was then
rearranged so the sphere straddles the selected box, which makes the depth test visible rather
than merely present.

**`M5` is finished.** The software renderer, headless thumbnails and the CI visual regression were
the three things it still owed. `E9-T7` (parallel streamed tessellation), `E9-T8` (picking) and
`E9-T9` (selection sync) remain open in `E9` and are M2-era viewport work rather than M5's.

**Verified.** Build clean at 0 warnings. `Viewport.Tests` 95 → 101. Full suite **1,805**, 0 failed,
0 skipped. The golden was opened and looked at before it was committed, twice — the first version
was rejected.

### 2026-08-31 - `E7-T3` and `E7-T4`: the package load layer, and an order that is the whole design

**What.** `Spark.Packages` had a `.csproj` and nothing else. It now has `PackageIdentity`,
`ContractAssemblies` and `PackageLoadContext`, plus a new `tests/Spark.Packages.Tests` with twelve
tests. **M7 has started.**

**The one thing to understand here is a sequence of two `if`s.** `Load(AssemblyName)` checks the
contract set **first** and only then file existence in the package's own folder. Reversed, it
compiles, runs, and produces a `Circle` that cannot be assigned to a `Circle` - because NuGet
packages routinely ship copies of what they were compiled against, so a package built against
`Spark.Api` very often carries `Spark.Api.dll`, and file-existence-first picks it up every time.
The resulting error message names the same type twice and explains nothing.

**The decisive test stages a deliberately invalid `Spark.Api.dll`** in the package folder and
asserts the real one still resolves. If the order were wrong the test would not merely assert the
wrong thing - it would throw `BadImageFormatException`, which is a far kinder failure than the one
the real bug produces.

**Deciding by file existence rather than by a name list** is the other half, and the row already
said why: a hardcoded list rots the moment a package adds a dependency, and it rots *silently*,
because the symptom is a type from the wrong context rather than an error.

**Per version, not per package.** One context per package makes two versions impossible to have
loaded at once, which is the case a graph saved last year and a graph saved today put in front of
us in the first week. `TwoVersionsOfOnePackageLoadSideBySide` proves both halves at once: distinct
`Camera` types from the two contexts, and the *same* `Spark.Api` shared by both - which is what
lets nodes from two unrelated packages be wired together at all.

**The unload proof is the honest kind and it is paired.** `AContextUnloadsOnceNothingReferencesIt`
asserts a weak reference goes dead; `AContextDoesNotUnloadWhileATypeFromItIsHeld` asserts the
converse, so the first is not a test that would pass on a runtime that unloads regardless. That
pairing is the difference between proving unloading and observing a garbage collector.

**A consequence documented rather than discovered.** Four tests failed on the first run - all of
them in *cleanup*, deleting a temp folder whose DLL was still loaded, with
`UnauthorizedAccessException`. Loading by path pins the file on Windows. That is the same fact as
best-effort unloading seen from the filesystem, and the same reason **restart is the documented
default** for an upgrade. It is kept rather than worked around: loading from a byte array would
free the lock and lose `Assembly.Location`, which is what a diagnostic prints to answer *where did
this node come from*, and packages live in an immutable version-scoped cache where the lock costs
nothing restart does not already cover. **`E7-T9`'s local DLL references are the opposite case** -
a user rebuilds those while Spark is open - so that row cannot reuse this path, and now says so.

**`E7-T5` moves to `In progress` rather than `Done`, with the missing half named.** The unload
mechanism is built and proven; the *purge* it has to perform first empties registries that do not
exist yet, because there is no package node library and no install cache until `E7-T1` and
`E7-T2`.

**Verified.** Build clean at 0 warnings. **1,817** tests over **nine** projects, 0 failed, 0
skipped. `dotnet format` clean.

### 2026-08-31 - `E7-T6` and `E7-T7`: nobody's graph is damaged, and it is now a fact

**What.** `PlaceholderNode`, `MissingPackageException` and `MissingNodePolicy` in `Spark.Engine`,
and `GraphDocument.Restore` now defaults to substituting a placeholder rather than refusing. Ten
new tests, and one existing test rewritten rather than deleted.

**The promise is narrow and absolute, so the test is too.** A real graph is written; one node key
is rewritten to a package nothing has - which is exactly what a user's file looks like on a
machine without it - and the reopened document is re-serialised. **The strings are equal.** Not
"mostly preserved", not "recoverable": the file that goes in is the file that comes back out,
having been through a session that could not understand part of it.

**Port counts are inferred from the file, and that is the only evidence available.** The
definition is absent - that is the entire situation - so the graph's own usage stands in for it:
one past the highest literal index, one past the highest wire index on each side. A placeholder
exactly that wide is the precise condition for a byte-identical re-save. Too narrow and the wires
cannot attach; too wide and the node grows phantom inputs a user can type into.

**Two smaller decisions that are the difference between working and nearly working.** The
placeholder's ports carry a **null default**, because `Capture` suppresses any literal equal to
its port's default - a placeholder that invented a default would silently shorten the file it was
supposed to preserve. And they carry **`keepStructure`**, so a list arriving at a placeholder is
passed whole rather than replicated over: the node refuses either way, and refusing once is a
diagnostic where refusing per element is a wall of them.

**It throws rather than returning null.** Null is a value. A graph that quietly produced one
downstream of a missing package would compute a confident wrong answer, which is the one outcome
worse than not computing at all.

**The default changed direction, and that was the decision worth making carefully.** `Restore`
used to refuse; it now placeholders, with `MissingNodePolicy.Refuse` still available for a
headless check that must not proceed on an incomplete graph. A caller who wanted strictness and
gets a placeholder sees a graph that reports errors - visible, recoverable. A user who wanted
their graph open and gets a refusal loses access to everything else in it. The first is an
inconvenience; the second is what this row exists to prevent.

**One existing test was rewritten rather than deleted, and the distinction matters.**
`AnUnknownNodeIsNamedRatherThanSkipped` asserted the old refusal. Its *property* - that a node
never silently vanishes - is still true and is now more important, so it is asserted directly
against the placeholder, and the old exception assertion moved to a second test covering the
strict policy. Deleting it would have removed the guard along with the outdated mechanism.

**`E7-T6` is `In progress`, not `Done`, and the missing half is named.** The row also asks for a
**banner offering one-click install**. That needs the NuGet client (`E7-T2`) and the package
manager UI (`E7-T10`), neither of which exists, and a banner with nothing behind it would be
worse than none.

**Verified.** Build clean at 0 warnings. `Engine.Tests` 367 -> 377. Suite **1,827** over nine
projects, 0 failed, 0 skipped. `dotnet format` clean.

### 2026-08-31 - `E7-T11`, `E7-T13`, `E7-T15`: a graph packaged as a node

**What.** `CustomNodePorts`, `CustomNodeFile` and `CustomNodeLibrary` in `Spark.Engine`, plus
`NodeDefinition.FromNestedGraph`. Twelve tests. A user can now define a node by drawing a graph.

**The row's claim was that graph-in-graph is the same mechanism, not a separate feature, and the
implementation had to make that literally true rather than approximately.** What
`CustomNodeLibrary` produces is a plain `NodeDefinition` whose invocation happens to evaluate a
graph. The replicator, the evaluation cache, the canvas, the file writer and the placeholder logic
all handle it without knowing it exists. If any of them had needed a special case, the claim would
have been false and the second mechanism would have started growing.

**The format is the graph format plus one object, and `Write` proves it by construction.** It
splices the `interface` block into the string `SparkFile.Write` produced rather than running a
second serialiser over the same document. Two writers is two sets of formatting decisions, and the
byte-for-byte round trip this format promises would then rest on them agreeing forever. A
consequence falls out for free: `SparkFile.Read` ignores the extra property, so **a `.sparkcustom`
file opens in the ordinary graph reader as the definition the user wrote** — which is how you
edit one.

**Ports are drawn, not declared.** Placing an Input node adds an input; placing an Output node adds
an output. There is no separate port list, because a list that can disagree with the graph
eventually will. Order is canvas order — top to bottom, then left to right — which is
the only rule a user can predict without being told it, because it is what they already see.

**Recursion is refused when the definition is built, not when it runs.** Refusing at evaluation
time would mean a graph that opens, looks fine, and hangs the first time somebody presses run. The
exception carries the containment **path**, so the message reads *'Acme/A' contains 'Acme/B'
contains 'Acme/A'* rather than "recursion detected" — the difference between something a user
can act on and something they can only report. Indirect recursion is tested, not just direct.

**Build order is worked out rather than demanded.** A custom node whose body uses another needs
that one first; asking the caller to register in dependency order would be asking them to
topologically sort by hand. `OneCustomNodeCanUseAnotherInEitherRegistrationOrder` registers the
outer one first on purpose.

**Two costs, written down rather than left to be profiled.** The inner graph is built once and
reused **under a lock**, so one custom node's body evaluates one call at a time even when the outer
graph runs in parallel — rebuilding per invocation would be simpler and would restore a
thousand graphs for a node replicated over a thousand items. And each call gets a **fresh cache**,
because two invocations with different arguments must not see each other's results, which is
exactly what a shared one would arrange.

**`FromNestedGraph` exists for one reason: the cancellation token.** `Invoke` takes none, and a
nested graph is precisely the kind of work a user cancels — it can hold a thousand nodes.
Routing through `InvokeScript` means `Call` hands the token down. A custom node built on `Invoke`
would swallow it silently, which is the defect `E6-T17` already named once for code blocks.

**`E7-T15` is reserved and deliberately unused.** `ViewKey` is written, read, and consulted by
nothing. The test asserts it survives a round trip *although nothing uses it*, which is the whole
value: a file written by a future version that does use it is not quietly stripped by this one.
Adding a property to a format before anyone has files is a line of code; adding it afterwards is a
migration.

**Verified.** Build clean at 0 warnings. `Engine.Tests` 377 -> 389. Suite **1,839** over nine
projects, 0 failed, 0 skipped. `dotnet format` clean.

### 2026-08-31 - `E10-T5`, `E10-T13`: the help model, and a check that could hardly fail

**What.** `Spark.Api.Help` — `HelpDocument`, `HelpBlock`, `HelpInline`, `HelpMarkdown`,
`HelpLibrary` — and `NodeReference` in `Spark.Engine`. Sixteen tests. **The Help pass has
started, and it started with the harness and the generator, which is D19's own rule.**

**The node reference is generated at runtime from the live library, not written to files.** That
is the strongest available form of `E10-T5`: a page produced from the definition it describes
cannot drift from it, because there is no second copy to drift. Add a node and it has a page;
rename a port and the page renames with it. No build step, no stale file, nothing to forget.

**And the content was already there.** CS1591 is an error on `Spark.Nodes.Core`, so every node
already carries an XML summary and every port a description — the build refuses an assembly
without them. `NodeReference` only arranges what exists, which is why the reference is complete on
the day it is switched on. `DocGenerator`'s 1,478 hand-maintained entries are the counter-example
this project keeps in view, and this is the shape that avoids them.

**A hand-written Markdown subset rather than a package**, because `Spark.Api` is a contract
assembly: every dependency it takes is inherited by every package author and can never be
side-by-sided (ADR-0019). The subset is defined by what the topics already use, and it is checked
against `docs/help/` rather than against a specification — a parser correct against a
specification and wrong about `lacing.md` would be useless.

**Three real defects, found by pointing the parser at the real corpus.**
*One:* `HelpBlock.PlainText` ignored table rows, so every table was invisible to search — including the lacing case table, which is the most searched thing in the help. *Two:*
`StringBuilder.AppendLine` writes `Environment.NewLine`, so a code fence produced different text on
Windows and Linux; it is an explicit `'\n'` now, the same decision the `.spark` writer made for
the same reason. *Three:* unterminated front matter consumed the entire document, so a topic with a
typo in its header rendered as a blank page rather than as a topic with no front matter.

**The finding worth more than the three fixes: a check that could hardly fail.** The docs harness
has asserted since M0 that *every help topic contains a worked example*. Its implementation
accepted the bare string `.spark` **anywhere in the file** — so any topic mentioning "a
.spark file" passed, whether or not it showed anything at all. A stricter reading flagged
`concepts/undo.md`, and looking at it settled the matter in the opposite direction from the one
expected: **undo.md contains a perfectly good worked example**, a numbered walkthrough, which the
old rule never noticed and a fence-only rule would have rejected. This is a node-graph tool and its
best examples are walkthroughs.

So the rule is now three shapes — a fenced block, a table, or a section headed *example* — applied in both places, and it was **proven to fail** before being trusted: a probe topic
mentioning `.spark` in prose and showing nothing is now rejected, where the old rule accepted it.
**The two implementations are separate and must be kept in step by hand**, and both say so;
`Spark.Docs.Verify` deliberately references no Spark project so that it cannot constrain what it
observes.

**Verified.** Build clean at 0 warnings. `Engine.Tests` 389 -> 402. Suite **1,852** over nine
projects, 0 failed, 0 skipped. `dotnet format` clean. The new public surface is declared in
`PublicAPI.Unshipped.txt`, which is the ADR-0019 guard doing its job.

### 2026-08-31 - `E10-T13`: F1 does something, and every port finally says what it is for

**What.** `HelpView` and `HelpWindow` in `Spark.UI`, F1 in the shell, `--help-window [topic]`, and
`XmlDocumentation` extended to read `<param>` and `<returns>`. Six new tests. **The help system is
usable: press F1 on a node and its page opens.**

**Verified by photographing it, which is the only way a renderer can be.** `--help-window [topic]`
opens the window at startup and `--screenshot` now writes `PREFIX-help.png` beside the other two.
Three captures were taken and looked at: the node index listing all **115** loaded nodes grouped by
category, and two node pages. Every test in this step would have passed on a control that laid out
wrongly, because a test can only ask a window which topic it is showing.

**Two defects the pictures caught and nothing else would have.**
*One:* the first capture showed the descenders clipped off every line of the node index. A fixed
`LineHeight` gives body text its rhythm and also clips any line containing an inline link, because
a link is a real control and makes the line box taller than the text. The height is now applied
only to link-free paragraphs.
*Two:* navigating deep enough into the list to trigger virtualisation **crashed the application**.
Avalonia builds an item template with a **null** datum while measuring and recycling, and the
template dereferenced it. That is a crash every user would have hit by scrolling.

**[N65](NOTES.md) is the finding worth reading.** `XmlDocumentation` read only `<summary>`, and
`NodeImporter` took each port's description from an optional `[NodePort(Description: ...)]`
attribute. **There are zero such attributes in the entire node library.** So every port on all 115
nodes had a null description, and the generated page showed a full column of names, types and
defaults beside a completely empty Description column - while the text sat in the source, where
CS1591-as-error had made writing it mandatory.

It went unnoticed because **nothing had ever displayed it**: the canvas shows port names, the
tooltip shows a signature, and the first thing that ever asked for a port description was the help
page written that morning. Roughly **380 input ports gained a description** without a word being
written. Proven without reverting anything: `grep` for a `[NodePort]` carrying a description
returns zero, so the test asserting more than nine ports in ten are described would have measured
0% before the change.

**The `<param>` reader has one trap in it**, recorded on the method: the element's `name` attribute
must be read **before** `ReadInnerXml`, which advances past the element and takes its attributes
with it. And the attribute still beats the doc comment where both exist, because they address
different readers - `[NodePort]` speaks to somebody looking at a node, `<param>` to somebody
looking at the API.

**A help window rather than a dock pane**, deliberately. Help is consulted, not inhabited. A pane
would take permanent room in a layout whose point is that the canvas and viewport get it, and add
a fifth member to `WorkspacePane` that every preset, every serialised layout and every layout test
would have to learn, for a panel most sessions never open.

**Verified.** Build clean at 0 warnings. `UI.Tests` 448 -> 454. Suite **1,858** over nine projects,
0 failed, 0 skipped. `dotnet format` clean. Three help screenshots opened and read.

### 2026-08-31 - `E10-T11`, `E11-T6`: a page per diagnostic, and a mapping nobody had checked

**What.** `DiagnosticReference` in `Spark.Engine`, two new hand-written concept topics, three
coverage checks, and a link resolver that makes both kinds of help link work. Nine new tests.

**The pages are reflected out of the code constants, not written.** Every `SPK####` is a public
constant with an XML summary, because CS1591 is an error on both assemblies that declare them - so
the explanation already existed and only needed arranging. A code added tomorrow has a page
tomorrow; a deleted one takes its page with it. Same argument as `NodeReference`, same reason
`DocGenerator` is not ported.

**The check found something the mapping had been hiding since M0.** `DiagnosticCodes` has mapped
every code to a concept topic since the beginning, and **nothing had ever checked the far end of
that mapping**. Five codes - `SPK1010` through `SPK1014`, the whole wiring family - pointed at
`concepts.evaluation`, which **did not exist**. A user hitting *incompatible port types* and asking
for help landed on nothing at all.

So `docs/help/concepts/evaluation.md` is written: evaluation order and why the canvas has no say
in it, provenance caching and the two consequences a user actually notices, cycles refused at draw
time and reported at load time, the five wiring diagnostics in a table, and cancellation.
`concepts/lists.md` followed, because `lacing.md` has linked to it since M0 as well.

**Both topics were written against the source, not from memory**, which caught one error before it
shipped: `List.GetItemAtIndex` **supports negative indices** - `-1` is the last item - and the
first draft said only that indexing counts from zero. The `Number.Range` table was checked against
the actual arithmetic, `count = floor(span / |step| + 1 + 1e-9)`, rather than assumed.

**Two link kinds now coexist and both had to work.** Generated pages link by topic id, having no
file; hand-written topics link by relative path, which is deliberate rather than legacy, because
those files are also read on GitHub where a topic id is dead text. The docs harness now skips
targets that are topic ids - narrowly: a dot, no slash, no known extension - and `HelpWindow`
resolves a `.md` target back to a topic. Both directions are tested.

**A defect caught only by photographing the page.** Every code's doc comment opens with `Error.` or
`Warning.`, which is the most useful fact about it and is also a sentence. The index took "the
first sentence" for its Meaning column and produced a table whose every row read *Error.* -
perfectly correct and worth nothing. Severity is now its own column. **Six of the eighteen show a
dash there**, because their declarations do not open with a severity word; that is left visible
rather than guessed at, and it is a nudge to whoever next edits those comments.

**Verified.** Build clean at 0 warnings. `Engine.Tests` 402 -> 405, `UI.Tests` 454 -> 458. Suite
**1,865** over nine projects, 0 failed, 0 skipped. `dotnet format` clean. The diagnostics index was
opened and read twice; the first version was rejected.

### 2026-08-31 - `E11-T2`: the samples compile, and three of them never could have

**What.** `DocumentationSampleTests` compiles every ` ```csharp ` fence in every help topic through
the same `ReferenceCatalog` a real code block gets. Four tests. **This is the last piece of the
Help harness D19 said had to exist before any bulk writing.**

**Two kinds of sample, compiled two ways, because they are two different things.** A sample in
`code-blocks.md` *is* a code block: its bare identifiers are input ports the node supplies and
`return` is how it produces a value. Compiling one as an ordinary method body reports *the name
'radius' does not exist* - true of the method, false of the sample. Those go through
`ScriptNodeFactory`, literally the thing that compiles a code block. Everywhere else a fence is
ordinary C# and compiles on its own.

**Classified by topic id, not by heuristic**, and that is the important line. A heuristic - *does
it use an undefined identifier?* - would silently reclassify an ordinary sample **containing a
typo** as a code block and then compile it successfully. That is the one outcome this check must
never produce, and it is the outcome a clever version would have produced.

**Three samples in `geometry-basics.md` could never have been pasted and run**, which is what the
check was built to find. Samples 17, 18 and 19 quoted `yaw`, `camera`, `target`, `someBox`,
`bounds` and `regionBox` - **none of them declared anywhere, in that fence or any other** - and
sample 19 called `TestRealGeometry(...)`, a placeholder that does not exist. They read perfectly
well and were, as code, fiction. All three now carry their own setup, and the callback is a real
local function that shows what the caller has to supply.

**A fourth error was mine, and the speed of it is the point.** Adding that setup I wrote
`BoundingBox.ByCorners(...)`, which does not exist - the type has a two-corner constructor. The
check reported it on the next run. That is exactly the loop the two errors caught by hand in
`geometry-basics.md` had to be found the slow way.

**A decision inside the harness worth naming.** Fences are compiled **individually**, not
concatenated into one program per topic. Concatenating was tried first and failed honestly: two
fences in that topic both declare a variable called `same`, which is entirely reasonable for two
independent illustrations and a redeclaration for one program. The cost of compiling individually
is that a fence must carry its own setup - and making three of them do so was an improvement, not
a concession.

**`AllowedSkips` is 0 and is asserted.** `<!-- spark:skip -->` exists, nothing uses it, and the
count is a test rather than a convention, because every skip is a sample nothing is checking and
the first one makes the second easy.

**What this proves and what it does not**, stated on the type so nobody over-reads it: a sample
*compiles*, so a rename or a changed signature turns the build red. It does **not** prove a
sample's stated result is right - a comment claiming `// 120` compiles whatever the answer is.
`Angle.FullTurn / 3.0` is `119.99999999999999`, and only a person reading it catches that.

**`E11-T2` stays `In progress`**: the XML `<example>` half is not built, because no contract
project currently writes one.

**Verified.** Build clean at 0 warnings. `UI.Tests` 458 -> 462. Suite **1,869** over nine projects,
0 failed, 0 skipped. `dotnet format` clean.

### 2026-08-31 - `E11-T3`: the example graphs are executed, not just committed

**What.** `ExampleGraphTests` opens, evaluates and re-saves every file in `docs/examples/`,
headless. Four checks: it opens, it evaluates without errors, it produces output, and it re-saves
byte-identically.

**This is the check `E11-T2` cannot be.** Compiling a C# fence proves it is well formed against the
current API. Running a graph proves there is still an answer at the end of it. And an executed
graph is the strongest anti-rot mechanism a node-graph tool has: a screenshot rots silently -
rename a node and the picture is still a picture - where a graph that is opened and evaluated goes
red the same day.

**Two modes, because running without a kernel provider is supported rather than broken.** The
first run reported three errors from `solids.spark`: *no solid-modelling kernel is installed, so
this build cannot shell / fillet / union.* That is ADR-0021's stated configuration working exactly
as designed, not a fault - and accepting it as a pass would have made the check worthless on the
one machine where it means most. So:

- **With the provider installed**, every example must evaluate completely, and the count is
  asserted **exactly**. Asserting only "more than none" would let the solid examples quietly stop
  being checked on the only configuration that can check them.
- **Without it**, the solid examples are counted **unchecked** rather than passed, and the run
  still fails if nothing at all was fully evaluated.

That is the same shape D18 already uses for the native test project, where a green managed run
proves nothing unless the skip count is zero.

**The tolerance is matched on the diagnostic code, not the message**, with one deliberate
exception: a missing kernel and a node that genuinely threw share `SPK1046`, so that case also
matches the sentence the unavailable kernel raises. Tolerating every `SPK1046` would tolerate the
real failures this test exists to find.

**`tests/Spark.UI.Tests` now references `Spark.Geometry.Occt`**, so the solid example is evaluated
for real here rather than excused. On this machine all three examples evaluate completely, through
the actual OpenCascade kernel - booleans, fillets and shelling included.

**Verified.** Build clean at 0 warnings. `UI.Tests` 462 -> 466. Suite **1,873** over nine projects,
0 failed, 0 skipped. `dotnet format` clean. Architecture tests still 15 green, so the new project
reference breaks no layering rule.

### 2026-08-31 - `E12-T18`: the About box, and a row that was never blocked on a person

**What.** `ProductNotice` in `Spark.Api`, `AboutWindow` in `Spark.UI`, Help and About buttons on
the toolbar, `--about-window` for photographing it, and `spark --version` rewritten to print the
same text. Nine tests.

**The row had been sitting under *Blocked on: four things need a human*, and it should not have
been.** Reading it again, what it needed was a dialog. A dialog is code. The three genuine
human-blocked items - a third-party STEP viewer, counsel, a signing identity - are unchanged; this
was miscategorised, and moving it out is worth more than building it.

**One text, in `Spark.Api`, printed by both surfaces.** The command line already carried the
notice and the About box was going to carry it again; two copies of a licence statement is one
copy that eventually stops matching the build. `spark --version` now prints
`ProductNotice.ToText(...)` and the dialog renders `ProductNotice.Build(...)`.

**The tests assert an obligation rather than a preference.** With a kernel loaded the notice
**must** contain *Open CASCADE Technology*, *LGPL-2.1*, *linked dynamically*, *replaceable* and a
pointer to `THIRD-PARTY-NOTICES.md`. Without one it must **not** contain them - claiming to link
something absent is its own kind of wrong. A notice that quietly stops appearing is not a cosmetic
regression, and the only thing that keeps it true is something failing when it is gone. *Nothing
here is legal advice; `Q13` is with counsel.*

**One contract change, additive and recorded.** The dialog first showed `opencascade` where the
command line showed `OpenCascade 8.0.1`, because `IBrepKernel` exposed only `Name` and `Spark.UI`
deliberately does not reference the provider. `IBrepKernel.Description` is now a **default
interface member** returning `Name`, overridden by the OCCT provider to return its version: a
third-party provider compiled against the previous contract still builds and still works, which is
the easy case ADR-0019 allows. The two audiences differ - a diagnostic wants a short stable token,
a licence notice wants the number somebody would quote in a bug report.

**Photographed, and the picture is why the version is there.** `--about-window --screenshot` writes
`PREFIX-about.png`. The first capture read *Solid modelling: opencascade*, which is correct and
useless on that screen; the second reads *OpenCascade 8.0.1*.

**Also on the toolbar now: Help.** F1 already worked, and a key nobody knows about is a key nobody
presses.

**`E12-T18` is `In progress`, not `Done`.** The About box is built; the row also asks for the
**licence texts and the build key** `(occt-tag, vcpkg-baseline, shim-source-hash, rid)` to be
shipped, so a source offer can be honoured against a specific artefact. That belongs with the
installer, which needs a signing identity, and is genuinely blocked.

**Verified.** Build clean at 0 warnings. `Engine.Tests` 405 -> 414. Suite **1,882** over nine
projects, 0 failed, 0 skipped. `dotnet format` clean. `spark --version` run and read; the About box
captured and read.

### 2026-08-31 - `E11-T4`, `E11-T5`: coverage in both directions, and one of them was checking nothing

**What.** `NodeTopicCoverageTests`, four checks. Forward: every built-in node resolves to a help
topic. Reverse: every node named in a topic's front matter still exists.

**Both directions, because they fail differently and neither implies the other.** A node with no
topic ships undocumented; a topic naming a node that is gone sends a reader to a page about
something that no longer exists. DoodleSharp had both at once and neither was visible until
somebody wrote a reflection diff.

**The forward direction is already true by construction** — the reference pages are generated
from the live library, so a node cannot lack one. It is asserted anyway, and the reason is worth
keeping: the property that matters is *every node has a topic*, not *we generate pages*. Asserting
the mechanism rather than the property is how a guarantee quietly turns into an implementation
detail that somebody later replaces.

**The reverse direction was checked by nothing at all.** `curves.md` has listed ten node names in
its front matter since M0 and no test had ever read them. It now accepts either a full
`Package/Name` or a bare name, because a bare name is what an author reasonably writes, and rejects
anything matching neither.

**Proven to fail before being trusted**, by renaming one entry to `List.CountRenamedAway`: it
reports *concepts.lists names 'List.CountRenamedAway'*, naming both the topic and the dangling
node. And a fourth test asserts that at least ten node names appear in front matter at all, so the
reverse check can never pass by walking an empty list — which is the shape of every
cannot-fail test this project has found so far.

**A small thing worth noting.** The reverse check passed on its first run, which means the node
names written into `lists.md` earlier today were all correct against the real library. That was
luck rather than method, and it is now method.

**Verified.** Build clean at 0 warnings. `UI.Tests` 466 -> 470. Suite **1,886** over nine projects,
0 failed, 0 skipped. `dotnet format` clean.

### 2026-08-31 - `E9-T9`, and two defects the step went looking for something else and found

**What.** `ViewportScene.SetSelectedNodes`, called from the canvas's selection handler. Seven
tests. Selecting a node now outlines the geometry it produced, in the accent colour, in the
viewport.

**The row's claim was that this falls out of node-keyed identity with no extra bookkeeping, and it
did.** The scene is already keyed by `(NodeId, PortIndex)`; the canvas already knows which nodes
are selected; there is no third structure mapping one to the other and therefore nothing to keep in
step. **It also costs no GPU work**: `RenderPackage.WithAppearance` shares the geometry arrays, and
the renderer's reconcile step already recognised a package whose buffers are the same object as one
it had uploaded - *appearance is a uniform, not a buffer*. Selecting a node carrying a million
triangles re-uploads nothing, and a test asserts the arrays are shared rather than copied.

**The rendering half was already covered and I nearly re-proved it.** `E9-T12`'s golden image
contains a selected box, so the accent tint and outline are asserted pixel for pixel already. Only
the *driving* was missing, and that is what the new tests cover. Worth noticing before writing a
second visual check for something a first one already held.

**Then the screenshot stopped working, and it was not this change.** A verification run reported
*no viewport read-back: neither backend produced a frame*. Stashing the selection work reproduced
it, so it was older. [N66](NOTES.md) has it: `--screenshot` waited a **fixed** 600 + 400 ms, tuned
when the only backend was GL on a warm driver, and OpenGL on a machine that had been building all
day simply took longer than a second. The delay had always been a race and had always won until
now.

**The diagnostic that would have explained it was the one line that never printed.** The failure
path said *neither backend produced a frame* and then returned **before** the `viewport status:`
line - so the single most useful fact, `no GL callback ran`, was emitted in every case except the
one that needed it. It now prints on the failure path too. The fix proper is to **wait for a frame
rather than for a clock**: `HasCapture` reports completion, and the caller polls, re-requesting
each time because a viewport with nothing changing produces no frames.

**And one flaky test, fixed rather than re-run until green.** [N67](NOTES.md):
`UnloadingReleasesTheScriptAssemblies` failed about **one full-suite run in four** and passed every
time in isolation. `Compile` was already `NoInlining` for exactly this reason - but the **factory
itself was still a live local in the asserting frame**, and under a debug JIT a local is rooted
until its method returns. The whole create-compile-unload now happens in a `NoInlining` helper
returning only the `WeakReference`. Four consecutive full-suite runs clean.

**Verified.** Build clean at 0 warnings. `Viewport.Tests` 101 -> 108. Suite **1,893** over nine
projects, 0 failed, 0 skipped. `dotnet format` clean. `--graph solids --screenshot` run again and
the viewport image written and read.

### 2026-08-31 - `E7-T12`, `E7-T13`: collapse a selection into a node, and a layering rule that caught me

**What.** `CanvasCollapse` in `Spark.UI/Graph/`, the gesture on the view model, a toolbar button,
and the save-side half of `E7-T13`. Twelve tests. **Custom nodes are now something a user can
reach**, rather than an engine capability with no door.

**The interface is inferred from the wires that crossed the boundary**, which is the whole
feature. A user selects a working piece of a graph and asks for it to be a node; what its ports are
is not a question they should answer, because the graph already answered it. Anything wired in from
outside is an input, anything read from outside is an output, anything wired entirely within is now
private.

**One port per distinct crossing *source*, not per crossing wire.** One node feeding three ports
inside the selection is one value arriving, and three ports would make the user wire the same thing
three times. Ports take the name of the inner port they attach to, so collapsing a
`Circle.ByCentreRadius` gives a node with a `radius` rather than an `in0`.

**Split into Plan and Apply**, because the inference is the part with judgement in it and the part
worth testing, and because a plan that changes nothing can be shown to a user before it happens.
Everything is expressed in `NodeId`s: removing the absorbed nodes renumbers every slot after them,
and a plan holding slot indices would rewire whichever nodes moved into them.

**The body is built by constructing a real `Graph` and capturing it**, rather than writing
`GraphDocumentNode`s by hand. `GraphDocument.Capture` already knows to suppress a literal equal to
its port's default and to refuse one a file cannot represent; a second copy of either rule is a
rule that is only correct once. And the Input/Output nodes are **positioned deliberately** -
`CustomNodePorts.Collect` orders ports by canvas Y then X, so their positions *are* the interface.

**The architecture test caught a real mistake, which is what it is for.** The obvious place for the
gesture was `GraphCanvas`, which owns the selection - so I put it there, and
`NoViewFileReferencesTheEngine` went red: a view file must not name `Spark.Engine` at all
(`E8-T11`). The rule's own failure message says where it belongs, *add what you need to CanvasGraph
or to a view model instead*, and it is right. The gesture moved to `MainWindowViewModel`; the
canvas keeps `CollapsedInto(slot)`, which does selection bookkeeping and nothing else. **The
version that broke the rule worked**, which is exactly why the rule is a test rather than a
paragraph.

**`E7-T13`'s save side closes with it**, because collapse is what constructs a recursive definition
by accident. `CustomNodeFile.Write` refuses a body naming its own key - writing it and refusing to
open it afterwards would leave the user's work in a file nothing can load. **Only the direct case
is checkable there** and the method says so: *A contains B contains A* needs every other definition
to resolve, and a writer has no library.

**Verified in the running application, not only headlessly.** `--collapse N` selects the first N
nodes and presses the button, so the gesture can be photographed - a button needs a click, and a
click is the one thing a headless run cannot do. Two nodes of the curves demo became `Custom.1`;
the shell shows 17 nodes and 14 wires where there were 18 and 15, the properties pane names the new
node, **18 nodes evaluated with no diagnostics**, and the viewport image is **byte-identical** to
the run before the collapse. The graph's shape changed and its result did not.

**Verified.** Build clean at 0 warnings. `UI.Tests` 470 -> 482. Suite **1,905** over nine projects,
0 failed, 0 skipped. `dotnet format` clean. Architecture tests 15 green after the move.

### 2026-08-31 - `E7-T1`, `E7-T2`: the package convention, and an unzip that writes anywhere

**What.** `SparkPackageManifest`, `PackageStore` and `NuGetPackageClient` in `Spark.Packages`.
Twenty-four new tests. **A Spark package can be searched for and installed.**

**NuGet is the registry; this is the part that is ours.** Protocol, hosting, auth, SemVer, private
feeds and nuget.org's reach come free from being an ordinary NuGet package. What NuGet cannot say
is *which assemblies in here are node libraries*, and that is the whole job of `tools/spark.json`.
Two things mark a Spark package and both are needed: the `spark` tag so it can be found, and the
manifest so it can be loaded.

**Assemblies are named, not discovered.** A package's `lib` folder holds everything it depends on
as well as its own work, and reflecting over all of it would turn every public static method in
somebody's maths helper into a node. The author says which of their assemblies are node libraries.

**[N68](NOTES.md) is the finding, and it is the kind worth being blunt about.** A `.nupkg` is a
zip, installing one is an extract, and **a zip entry's name is data supplied by whoever built the
archive**. Nothing stops it being `../../something`. An extractor that joins that onto a
destination has turned *install a package* into *write an arbitrary file*, with the application's
privileges, **before any of the package's code has run**. The guard resolves each entry to a full
path and refuses anything outside the destination - checked on the *resolved* path, because
`a/../../b` is the same attack spelled differently and a name-based check for `..` misses it.

**Proven load-bearing rather than assumed.** With the guard disabled, the test that installs a
package containing `../../escaped.txt` reports *no exception was thrown*: the extract succeeded and
wrote outside the folder.

**[N69](NOTES.md) is smaller and would have looked right in review.** Recovering a package identity
from an `id.version` folder name looks like `IndexOf('.')` and is not, because **a package id
contains dots**: `Acme.Nodes.Geometry.2.1.0` would come back as `Acme` at version
`Nodes.Geometry.2.1.0`. The split is before the first digit-led segment, and it is a named method
for exactly that reason.

**Install stages and then moves.** Extracting straight into the final folder would leave an
interrupted download looking installed, and the next run would load half a package. The manifest is
validated in staging, so a package that is not a Spark package never reaches the store at all.

**The tests are arranged so a network is optional and a green run is never empty.** The install
path runs against a **local folder feed** - NuGet reads a directory of `.nupkg` files as a feed -
with a package built in the test, so the whole path is exercised offline and without installing a
stranger's code as a side effect. The nuget.org tests return early when the feed is unreachable,
and `TheOfflineHalfIsAlwaysChecked` exists so that a machine with no network still asserts
something. **That they really query the feed was proven**, not assumed: inverting one assertion
made it fail against live data.

**A pleasing confirmation.** A tagged search for *newtonsoft* returns nothing, because no package
on nuget.org carries the `spark` tag yet. That is the tag filter working: applied after the fact it
would have returned the whole first page.

**`E7-T2` stays `In progress`: dependency resolution is not built.** A package's own NuGet
dependencies are not yet walked, so a package that needs another one installs without it.

**Verified.** Build clean at 0 warnings. `Packages.Tests` 12 -> 36. Suite **1,929** over nine
projects, 0 failed, 0 skipped. `dotnet format` clean. Searches ran against the real nuget.org.

### 2026-08-31 - `E7-T8`: the disclosure comes before the decision

**What.** `PackageDisclosure`, `PackageInspector`, `PendingInstall` and `PackageTrustStore`, and
`NuGetPackageClient` restructured into prepare-then-commit. Twelve tests.

**The row says install *shows* the user these things, and that word decides the shape.** A user
cannot weigh a package's licence, its dependencies or whether it carries native binaries until
those have been read out of it, and reading them means downloading it. So `PrepareAsync` downloads
and extracts into a **staging folder** - somewhere `PackageStore` deliberately does not consider
installed and `PackageLoadContext` will never load from - and the decision happens afterwards.
`Commit` moves it into place; `Discard` throws it away; `Dispose` discards, so the ordinary
`using` shape cannot leak a downloaded package into a folder nobody remembers.

**Every field is read out of the package, never declared by it.** Publisher, licence, project URL
and dependencies come from the `.nuspec`; node assemblies from the manifest. A disclosure a package
could assert about itself would be worth nothing to the user it is shown to.

**The native-binary check is the one this row exists for**, and it over-reports on purpose. Spark's
own promise is no native dependencies; a package is entitled to break that on its own behalf -
plenty of useful libraries are native - but not silently and not on the user's behalf. It looks
both for NuGet's `runtimes/{rid}/native` convention **and** for native extensions anywhere in the
tree, because a check that only knew the convention would report *no native binaries* for a package
that had simply dropped a `.so` beside its managed files. Telling a user about a harmless file
costs them a moment; missing a real one costs the promise.

**Signature is reported as *present but unverified*, and the wording is the point.** Spark reads
whether a signature entry exists. It does not build a certificate chain, check revocation, or
decide who the signer is. Reporting *signed* would imply all three, and a user who read that would
be relying on something nobody did.

**Trust is per package *version*, not per publisher.** Agreeing to `Acme.Nodes 1.0.0` is not
agreeing to `2.0.0`, because everything a user weighed can change between them - a patch release
can acquire a native dependency, which is exactly the disclosure this protects. A per-publisher
store would let that through silently. An unreadable trust file trusts nothing, which is the safe
direction: the worst outcome is being asked again.

**`InstallAsync` survives as prepare-then-commit** for callers with nobody to ask - a command line,
a test, a scripted setup. Anything with a user in front of it should prepare and show them.

**Verified.** Build clean at 0 warnings. `Packages.Tests` 36 -> 48. Suite **1,941** over nine
projects, 0 failed, 0 skipped. `dotnet format` clean. The disclosure tests build packages that
actually carry a licence, dependencies, a signature entry and a native binary, and assert those
come back out.

### 2026-08-31 - `E7-T5`: install a package and use its nodes, which is the sentence the epic is for

**What.** `PackageManager`, `NodeLibrary.Remove`, and nine tests that install a package and use
what is in it. **`E7`'s goal sentence is now true**: a package can be found on nuget.org,
inspected, installed, loaded and unloaded.

**The test installs a package carrying a real assembly**, not a stub: `Spark.Nodes.Core.dll`,
copied into a `.nupkg` the test builds. A stub would have proven the plumbing and nothing about
whether the importer, the load context and the contract rule work together on something with a
hundred real nodes in it. They do - over fifty node definitions arrive, keyed by the package.

**And it exercises the rule that matters most.** `Spark.Nodes.Core` references `Spark.Api` and
`Spark.Geometry`, and the package deliberately does **not** ship them. A test asserts that a
`Point3d` returned by a packaged node comes from the host's assembly - `Assert.Same` on the
assembly, not merely `Assert.Equal` on the type name - which is the difference between a wire that
connects and an error naming the same type twice.

**Nodes are keyed by the package, not the assembly.** Two packages shipping an assembly of the same
name do not collide, and a node's key names the package a user would have to install - which is
what makes `E7-T6`'s placeholder legible rather than a mystery.

**`Unload` returns a `WeakReference`, and the return type is the honest part.** Purging the library
is the half this class can guarantee; whether the context then unloads depends on every other thing
that might hold a reference into it - a cached value of a package type, a compiled invoker, a
viewport buffer, an undo entry. A method returning `true` would be claiming to know about all of
them. The test proves collectability from a `NoInlining` frame, the shape [N67](NOTES.md) taught.

**[N70](NOTES.md) is a claim narrowed rather than a defect.** *Side-by-side* is the phrase `E7-T3`
uses, and building this showed it buys less than it reads as promising. It buys the case that
matters: package A depending on `Foo 1.0` and package B on `Foo 2.0`, both loading. It does **not**
buy two versions of the same node library both contributing - they claim the same keys, since
`Acme.Nodes/Point.ByX` carries no version. That is correct rather than a limitation: a `.spark`
file names that key, so if both could be active a graph's meaning would depend on load order. The
clash is reported rather than resolved, because either rule leaves a user with a node that quietly
changed meaning.

**One bad assembly does not sink a package.** A manifest naming something the package does not
contain is reported and the rest still loads; and one bad package does not stop the application
starting, because the user needs to get in to remove it.

**Verified.** Build clean at 0 warnings. `Packages.Tests` 48 -> 57. Suite **1,950** over nine
projects, 0 failed, 0 skipped. `dotnet format` clean.

### 2026-08-31 — `E7-T10`: the package manager, and the disclosure as a gate

**What.** `PackageBrowserViewModel`, `PackageWindow`, a **Packages** toolbar button, three startup
switches, and eighteen tests. **`E7`'s user-facing half is now built**: a person can search a feed,
read what a package is, install it, use its nodes and take them out again.

**A view model with a thin window over it, not a control.** `Spark.Architecture.Tests` forbids a
file under `Views` or `Controls` from naming `Spark.Engine`, and a package browser that installs
nodes into a library names it on the first line. The window's whole job is `Sync()` — read the
model, write the controls — and its handlers are the thinnest possible awaits.

**The disclosure is a gate rather than a notice, and the layout says so.** While one is pending the
window offers exactly two answers, the *Install...* button that would start another is disabled,
and the native-code sentence is lifted out of the block and set in the warning colour. That last
part was a change made after looking at the photograph: five facts in one grey paragraph, and the
one sentence that says *this package will run native code with your full permissions* read exactly
like the four above it. It is now the only coloured line on the screen.

**Closing discards an unanswered install, and that is proven in the real application** rather than
only in a test. After a run that prepared an install and then exited, the default store under
local application data was empty. A download must not outlive the question it was fetched to
answer.

**Two defects, both found by tests that asserted what the user is told.**
[N72](NOTES.md): a single `GC.Collect` before deleting a removed package's folder is not enough —
the context has not finished unloading, the `.dll` is still mapped, and the delete fails part-way,
leaving a half-deleted folder and a status line blaming a lock that would have gone in another
millisecond. `Remove` now collects in a bounded loop, the shape `PackageManagerTests` already used,
and the restart advice is reserved for the case where the reference really is still alive.
[N71](NOTES.md): awaiting inside `HeadlessSession.Run` deadlocks silently — the first run of these
tests hung for seven minutes and had to be killed — so the asynchronous half now happens outside
the dispatcher and only the window is driven within.

**And one wart the photographs found that had nothing to do with this row.** The toolbar was a
single-row `StackPanel` of twenty-two buttons; at 1480 pixels the last three, *Help* among them,
sat past the right edge and could not be clicked at all. It is a `WrapPanel` now. A toolbar that
overflows hides controls with no indication that it has.

**Installed packages load before anything reads the library**, in the view model's constructor, so
the library pane is built from it and a document opened at startup resolves against it. Loading
them later would hand a user placeholders for nodes they had already installed, which is the one
outcome `E7-T6` exists to avoid. A package that will not load is reported into the diagnostics
pane, never thrown: the user needs to get in to remove whatever is broken.

**An installed package now keeps the capitalisation its author chose.** The folder is lower case,
because that is NuGet's convention and case-insensitive lookup depends on it, but a manager listing
`acme.nodes` beside a feed offering `Acme.Nodes` reads as two different packages. The id is read
back out of the `.nuspec`; every comparison still ignores case.

**Verified by running it.** A folder feed carrying a package built around a real assembly:
searched, listed, prepared, and both disclosures photographed — the managed-only one and one
carrying three native binaries. Then the package placed in the real store and the application
started: **115 nodes from the package, 230 in the library, listed as `Acme.Nodes 1.0.0 — 115
node(s)`**. Build clean at 0 warnings. `UI.Tests` 482 -> 499, `Packages.Tests` 57 -> 58. Suite
**1,968** over nine projects, 0 failed, 0 skipped. `dotnet format` clean.

### 2026-09-01 — `E7-T9`: local DLL references, and a claim that was two claims

**What.** `LocalReferenceStore`, `LocalReferenceWatcher`, `LocalReferencesViewModel`, a **Local
assemblies** tab beside packages, `ScriptLoadContext` resolution for a user's own assemblies, and
twenty-three tests. **`E7`'s engineering rows are now all `Done`** bar dependency resolution and
the placeholder banner.

**Trust is keyed on the path and the hash together**, the same shape and the same reasoning as
`ScriptTrustStore`. Keyed on the path alone a user who agreed to `MyNodes.dll` in March would still
be trusting whatever that file says today, which is exactly what a rebuild changes; keyed on the
hash alone, agreeing to one copy would agree to every copy anywhere. So a rebuild re-prompts, which
is the row's own sentence, and in the ordinary case a developer glances and presses the button.

**The watcher offers and never reloads.** A reference that swapped itself out underneath a running
graph would change what the graph computes without anybody asking, and the user would have no way
to tell that the answer on screen came from different code than a second ago. Changes are coalesced
over 400ms, because a build is not one write — four events for one rebuild would be four prompts.

**[N73](NOTES.md) is the finding of the day, and it came from one test.** The row's *never locks it*
is **two** claims, not one: compiling against a file and loading it are separate open handles.
The compile side was already safe, by Roslyn's grace — it opens metadata sharing read, write and
delete. The load side was not. A script calling into a user's DLL **compiled perfectly and then
failed at evaluation** with `Could not load file or assembly`, because `ScriptLoadContext.Load`
returns null on purpose and the default context has never heard of a file in some folder of the
user's. It now resolves on the `Resolving` event — which fires only after the default context has
failed, so nothing found this way can shadow a contract assembly — and loads **from bytes**.
`LoadFromAssemblyPath` was tried deliberately afterwards and the rebuild failed with *the process
cannot access the file*, which is the proof the byte load is load-bearing.

**And the test that found it is the point.** Every other test asserted a path had reached a list.
That one compiles a real assembly at test time, references it, and calls a method that exists only
in it. The assembly is compiled rather than copied because it has to contain a type this process
has never loaded; a copy of something already in memory would resolve against the loaded one and
prove nothing.

**[N75](NOTES.md), found on the way.** `DefaultImports` puts `using Spark.Geometry;` in front of
every script, but the references were swept from what the process had already loaded — and a
referenced assembly does not load until something touches it. A catalogue built early enough
promised an import it could not satisfy, and the user saw *the type or namespace name 'Geometry'
does not exist in the namespace 'Spark'* on a line they did not write. `Microsoft.CSharp` was
already added by name for exactly this reason; `Spark.Api` and `Spark.Geometry` now are too.
Anything the prelude names must be referenced by name, not hoped for.

**[N74](NOTES.md), which bit twice.** `ReferenceCatalog.Add` returns how much the catalogue grew,
and rebuilding the snapshot also sweeps newly loaded assemblies — so adding one can return two. It
broke `Apply`'s count, and then broke a test that **passed alone and failed in the full suite**,
because five hundred tests load more assemblies first. Both now ask a question the catalogue can
answer honestly.

**Verified by running it.** An assembly agreed to, then rebuilt between sessions: listed and marked
in amber, *rebuilt — reload to use it*, and **not compiled against**. Restored: *referenced*. The
prompt itself photographed, naming the file, the folder, the SHA-256, and saying plainly that the
code will run with the user's full permissions. Build clean at 0 warnings. `UI.Tests` 499 -> 523.
Suite **1,992** over nine projects, 0 failed, 0 skipped. `dotnet format` clean.

### 2026-09-01 — `E7-T6` closes: the banner, and two tests that lied under load

**What.** `MainWindowViewModel.MissingPackages()`, a banner strip between the toolbar and the
workspace, five tests, and the last of `E7-T6`. **A graph naming a package this machine does not
have now opens, says so, and offers to go and get it.**

**The ids are read off the placeholders rather than remembered from the load.** A placeholder keeps
the original `NodeKey` verbatim — that is the guarantee the whole row is built on — so the package
a user has to install is written on the node. Recomputing also means the banner clears itself once
the package is installed and the nodes resolve: there is nothing to invalidate and nothing to get
wrong.

**It searches rather than installs, and the row's *one-click install* survives that.** Installing
straight from a banner would skip the disclosure, which is the one screen where a user decides
whether to run somebody else's code. One click gets them to the package with the answer in front of
them; the click after it is the agreement.

**A first reading of this step was wrong, and the journal recorded the correction before the code
did.** I wrote that a placeholder does not look like one, because its category string `Missing` is
unknown to `NodeCategoryNames.Parse` and falls through to `Custom`. It does look like one: a
placeholder throws when invoked, so evaluation marks it `Error` and the canvas draws the red ring
and the glyph it draws for anything that cannot run. The screenshot shows six of them. **A
`Missing` category colour would have been wrong anyway** — Principle 4 says a category fill must
never be read as a state, and *missing* is a state. The design language already had the answer.

**[N76](NOTES.md): two tests written earlier in this run asserted more than the code promises**, and
both said so only under load — one run in three and one in six, in the full parallel suite, which is
the worst shape a failure can have because it looks like a regression somewhere else. One asserted
that removing a package always deletes its folder, which removal cannot promise and already said it
could not. The other compared two reference catalogues built moments apart, which is a fact about
what the process had loaded rather than about the fingerprint. Both now assert what the code
actually guarantees.

**And one real improvement came out of the first.** `PackageStore.Uninstall` retries the delete for
up to 200ms, because unmapping lags the collection that freed it: a single attempt could fail, or
half-succeed and leave a folder with some of its files gone.

**Verified by running it.** `docs/examples/solids.spark` repointed at a package that does not
exist: it opens, twenty-six nodes and twenty-seven wires all present, six placeholders ringed in
red, the diagnostics naming `SPK1046` and the package, and the banner reading *This graph uses
nodes from 'Acme.Nodes', which is not installed. Those nodes are kept exactly as they were and the
file will save unchanged*, with **Find Acme.Nodes** beside it. A test asserts the same file saves
back byte for byte. Build clean at 0 warnings. `UI.Tests` 523 -> 528. Suite **1,997** over nine
projects, 0 failed, 0 skipped, and the UI suite run **eight times** to confirm the flakes are gone.
`dotnet format` clean.

### 2026-09-01 — `E7-T2` closes: every package test passed, and no real package could be installed

**What.** NuGet-layout resolution in `PackageLoadContext`, a transitive dependency walk in
`NuGetPackageClient`, `PackageInspector.DependenciesIn`, and thirteen tests. `E7`'s engineering
rows are now all `Done` bar **freeze**.

**It began with a blocking defect found while planning something else.** The context resolved
assemblies from exactly one path, `<folder>/<name>.dll`. Extraction is verbatim by design, and
`dotnet pack` puts assemblies at `lib/{tfm}/Name.dll` — so **every package on nuget.org would have
failed to load**, saying *Package 'X' has no assembly 'Y.dll'*. Fifty-eight tests covered this
layer and all of them passed, because every one built its package by hand and put the assembly at
the root. [N77](NOTES.md) is about that rather than about layout: a fixture that constructs the
subject in the convenient shape hides every defect living in the difference, and hides them
uniformly, so the suite's greenness is evidence of nothing.

**The test that found it was one sentence long** — build the package the way `dotnet pack` builds
one, then load it — and it was written before the fix and went red immediately. `FrameworkReducer`
picks the folder rather than a hand-written ordering, because choosing between `net8.0`,
`netstandard2.0` and `net472` for a `net10.0` host looks like three lines of string comparison and
is not. The package root stays last, so a flat folder from a private feed or a build directory
still resolves.

**Then dependencies.** The nuspec's **nearest framework group** is walked breadth-first — not every
group, because a package supporting `net472` as well usually asks for a family of shims this build
has no use for, and taking the union would install them. Each is resolved to the **lowest version
satisfying the range**, which is NuGet's own rule: taking the highest would mean two installs on
different days quietly getting different code.

**The disclosure now lists what will actually be installed**, resolved and transitive, with
versions. That was the question the last entry left open, and this is the answer: agreeing to one
package should not silently agree to five, and the only way to say how many is to have resolved
them. A dependency the feed cannot satisfy **refuses the whole install** rather than producing a
`TypeLoadException` at first use naming an assembly the user never heard of.

**[N78](NOTES.md): dependencies live inside the package's own folder**, at `.deps/<id>.<version>/`,
rather than shared. Two packages needing the same library each get a copy. That is the trade-off
this layer already made when it chose download-and-extract over restore, and the reasons are the
same: removing a package removes exactly what it brought, no package can be broken by another's
uninstall, and the load context stays a rule about file existence rather than a resolver.

**Verified with a package that was actually packed.** A project written for the purpose — two node
methods, a `tools/spark.json`, `dotnet pack` — produced `lib/net10.0/Acme.Nodes.dll`, which is
precisely the shape that did not work an hour earlier. Spark found it on a folder feed, showed its
disclosure with publisher and licence read out of the package, and after installing reported
**Acme.Nodes 1.0.0 — 2 node(s)** with the status bar at **Library: 117 nodes**, 115 core and 2 from
the package.

**Also proven by inversion.** Disabling the dependency walk turns six of the nine dependency tests
red; the layout test was red before its fix.

**One honest note about the run.** A single failure appeared in one batch of the suite and was not
reproduced in five subsequent runs; running the UI tests deliberately alongside two other test
executables reproduced it as `C4SteadyStateCompletionIsInteractive`, a **wall-clock completion
budget** that measured 344 ms on a machine running three test processes. It is doing its job —
[N29](NOTES.md) already says wall-clock ceilings only catch step changes — and it is not touched.

**Verified.** Build clean at 0 warnings. `Packages.Tests` 58 -> 71. Suite **2,010** over nine
projects, 0 failed, 0 skipped. `dotnet format` clean.

### 2026-09-01 — `E7-T14`, and `E7` closes: freezing a branch

**What.** `NodeInstance.IsFrozen`, `NodeState.Frozen` and `NodeState.UpstreamFrozen`, `SPK1070`, a
**Freeze** button, a help section, and seventeen tests. **Every row in `E7` is now `Done`.**

**The previous entry guessed this row wrong and the journal caught it.** It read `E7-T14` as
freezing the *package API* and wrote a whole plan for documenting a compatibility promise. The row
is `Groups, notes and freeze`: *freezing a node or group skips it; downstream reports upstream
frozen, not an error*. Reading the row before starting cost a minute; the plan built on the guess
would have cost an afternoon and delivered the wrong thing.

**Frozen is a state of its own, not a reuse of not-evaluated.** Both mean *this did not run*, so
they share the desaturation and the dashed outline that say so. Only one of them is something the
user asked for, and a canvas that greyed them identically would leave somebody hunting a fault they
created on purpose. So a frozen node carries `‖` where a merely unevaluated one carries `○`.

**Reported once, on the node that was frozen, as information.** Not a warning: freezing is
deliberate, and a graph full of yellow for a state somebody chose teaches them to ignore the colour
that means something is wrong. Not on every node downstream either — that is the fifty-error wall
`NotEvaluated` already exists to avoid, and it would be worse here because the user made it happen.

**A group freezes together**, which is what the row title pairs freeze with groups for. A group is
the user's own statement that these nodes are one thing; leaving half of it running gives a branch
that is neither on nor off. The button also decides freeze-or-unfreeze by **all**, not by majority:
a mixed selection freezes, so pressing twice always ends with everything frozen and then everything
thawed.

**The flag is written only when true.** A graph nobody has frozen anything in saves exactly the
bytes it saved before freezing existed, which keeps `E7-T7`'s byte-for-byte round trip an assertion
about every file rather than about files this build wrote. A test asserts the word `frozen` does not
appear in an ordinary graph.

**One thing the screenshot found.** `--freeze N` applied in `OnOpened` did nothing visible: adopting
the graph starts an evaluation, freezing afterwards starts a second, and the capture photographed
whichever landed first — which was the unfrozen one, every time. The freeze now happens in the view
model **before the graph is adopted**, so there is one evaluation rather than a race to wait out. A
probe printing `froze 9 of 9` is what separated *the flag is not being set* from *the picture is
stale*; without it the obvious conclusion was the wrong one.

**And the diagnostics harness earned its place again.** `EveryDeclaredCodeResolvesToAHelpTopic`
failed the moment `SPK1070` existed, so the code points at `concepts.evaluation`, and that topic
gained a section on freezing — what it is for, what the two marks mean, what happens to a group, and
four questions with answers.

**Verified by running it.** `--freeze 2` on the demo graph: the two `Number.Range` nodes desaturated
and marked `‖`, the three nodes downstream desaturated and marked `○`, two `Information SPK1070`
lines naming them, **Ran 3 rather than 7**, and an empty viewport — while `Colour.ByRgb`,
`Number.Value` and `Vector.ZAxis`, which are not downstream of anything frozen, still ran.

**Verified.** Build clean at 0 warnings. `Engine.Tests` 414 -> 423, `UI.Tests` 528 -> 536. Suite
**2,027** over nine projects, 0 failed, 0 skipped. `dotnet format` clean.

**`E7` is complete.** A package can be found on nuget.org, read, installed, used and removed; a
graph missing one opens unharmed and says so; a local DLL can be referenced without locking it; and
a branch can be switched off. What is left before 1.0 is **M8 / `E12`**.

### 2026-09-01 @ `E12-T3`: the host-thread scheduler, and what it does not need

**What.** `HostThreadEvaluationScheduler`, nine tests, and `E3-T11`, `E12-T1` and `E12-T3` all
closed. **M8 has started.**

**The row calls this the entire embedding mechanism and it is not overstating it.** Revit's and
AutoCAD's APIs are callable from one thread and no other, so a node that asks the host for a wall
must run there. Spark's evaluator never assumed it owned its thread — that is what
`IEvaluationScheduler` has been for since `E3` — so making Spark work inside a CAD
application is a matter of supplying an implementation rather than of porting anything. This is the
implementation.

**Two delegates and no named host type.** One says whether the calling thread is the host's; the
other runs a delegate there and waits. Revit calls the second an external event, AutoCAD a
document-lock invoke, a dispatcher an `Invoke`. Asking for the shape rather than for a name keeps
this file free of all three, and keeps `Spark.Engine` free of any reference to any of them.

**Running inline when already on the host thread is not an optimisation, and the test says so
loudly.** A host thread services its own marshalled work in a message loop, so posting to it and
then blocking it waiting for the answer is a deadlock — and it is the *first* thing that
happens, because an add-in evaluates in response to the host calling it. **The fake host throws on
a re-entrant marshal rather than deadlocking**, which is the difference between a test that fails in
a second naming the problem and a run that hangs and tells nobody why. Removing the check was tried:
it fails exactly that test, with exactly that message.

**Two smaller decisions worth the sentence.** The batch is marshalled **once**, not per operation:
two hundred nodes marshalled one at a time is two hundred round trips through a message loop, and on
a host that pumps between them it is two hundred chances for the user to start something else
mid-evaluation. And an exception is captured and rethrown on the caller's side, because one left on
the far side of a marshal vanishes onto a thread nobody is watching, and the evaluation would look
successful with a node silently missing its output.

**`E12-T1` closed with evidence rather than code.** `SparkSession` has been the composition root
since `E3`; what was owed was the claim being checkable, and it is: `Spark.Architecture.Tests`
already forbids Avalonia anywhere in its reference graph, and `E12-T3`'s last test now evaluates a
whole graph through it on a foreign thread.

**`E12-T2` stays open on purpose.** An `IHostServices` designed without a host to try it against is
speculation, and [PRD Q5](PRD.md#14-open-questions) still says *which host* is unanswered. The seam
that mattered was the scheduler, and it is the one that existed already.

**Verified.** Build clean at 0 warnings. `Engine.Tests` 423 -> 432. Suite **2,036** over nine
projects, 0 failed, 0 skipped. `dotnet format` clean. The tests run a real second thread with a real
work queue, because every interesting property here is about which thread something happened on and
a fake that ran everything inline would assert nothing at all.

### 2026-09-01 — `E12-T10`, `E12-T11`, `E12-T14`: a release somebody could verify

**What.** `scripts/pack-portable.ps1`, `scripts/check-version.ps1`, a `portable` CI job and
`.github/workflows/release.yml`. Three rows, because they are one subject.

**[N79](NOTES.md) is the finding, and it invalidated the first version of the CI check.**
`OcctKernel` walks up from the executable looking for `artifacts/native/win-x64`, which is a
deliberate convenience for developers running out of a build tree. It also makes any in-tree check
of a *packaged* build vacuous — and **the CI runner is not immune**, because the portable job
downloads the shim into exactly that folder so `publish.ps1` can stage it. Measured rather than
assumed: a build staged with `-SkipNative`, **zero native DLLs in the folder**, exported nine solids
from inside the repository and failed with `SPK1080` from a temporary directory outside it. Both the
CI job and the release workflow now unpack into `RUNNER_TEMP`.

**The first draft asserted on `--version`, and that was worse.** It prints
`Solid modelling: OpenCascade 8.0.1` whether or not the provider loaded, because it reports the
configured provider rather than a loaded one. The check would have passed on an empty zip. What
distinguishes them is doing something that needs the kernel, so the step exports a solid.

**[N80](NOTES.md): the zip is written by hand, for a narrower reason than I first wrote down.**
The script's own documentation claimed `Compress-Archive` produces different bytes on two runs over
one folder. It does not — that was checked, and it is stable. What it does not survive is a
**rebuild**: it stamps entries with the file's last-write time, so the same source compiled again
yields a different archive and a different checksum. Touching every timestamp in a staged folder and
re-packing demonstrates it: `Compress-Archive` differs, `pack-portable.ps1` does not. The claim in
the script was corrected to the one that is true.

**The version gate is one line of YAML defended twice.** MinVer derives the version from the nearest
tag, so a tag and its assemblies cannot disagree — unless the checkout is shallow, in which case
MinVer finds no tags, stamps `0.0.0-alpha.0`, and the workflow publishes it as `v1.0.0`. That
release installs, runs, and makes every bug report from it name a version that never existed.
`fetch-depth: 0` prevents it and `check-version.ps1` catches it, reading the version **out of the
built assembly** rather than out of the build inputs, because the artefact is what ships.

**Both paths were run against this repository**, which turns out to have **no tags at all**: the
gate reports `expected 0.0.0` against `assembly 0.0.0-alpha.0.108` and refuses, which is precisely
the scenario it is for, demonstrated on the real thing rather than on a fixture.

**The release is drafted, never published.** Signing, the installer and the antivirus submissions
need an identity to sign with, and a workflow that published automatically would be claiming those
steps had happened.

**What was verified here and what was not.** Verified locally: the staging script, the zip, its
determinism across a rebuild, the checksum, both branches of the version gate, and the
outside-the-tree kernel behaviour that shapes both workflows. **Not verified: the workflows
themselves**, which cannot run on this machine. They are read, and their YAML parses, and every
step in them is a script that was run by hand — but the first green run of `release.yml` will be on
GitHub and nowhere else.

**Verified.** Build clean at 0 warnings. Suite **2,036** over nine projects, 0 failed, 0 skipped.
`dotnet format` clean. The staged payload is **173 MB managed** without the kernel; the packed zip
is **51.7 MB over 228 files**.

### 2026-09-01 — `E12-T18`: a licence obligation that was already met, and a guard that was not

**What.** One guard in `scripts/publish.ps1`, and a row closed by checking rather than by building.

**The row said the licence texts and the build key were still owed. They were not.** `publish.ps1`
has been copying `LICENSE`, `THIRD-PARTY-NOTICES.md` and `licences\*` into the staged build, and the
native staging copies the provider folder wholesale, which contains `spark_occt.buildkey.json`. A
full staged build — 225 MB, 58 native DLLs — was made and every one of them listed.

**I nearly created the problem I was looking for.** Checking whether the texts shipped, I listed
`licenses/` — the American spelling — found nothing, concluded they were missing, fetched the
canonical LGPL-2.1 from gnu.org and wrote it into `licences/`. That overwrote a **tracked file that
was already correct**, and the tidy-up afterwards deleted it. `git status` caught it and it was
restored from git rather than from my copy. The fetch was not wasted: the repository's copy is
identical to gnu.org's, 26,419 bytes, byte-for-byte after normalising line endings, and the OCCT
exception text matches the one OCCT itself distributes at the tag we build against.

**What was genuinely missing is a guard.** Every one of those files is a `Copy-Item` that a future
edit could drop, and **the application runs perfectly without any of them**, so no test, no gate and
no smoke check would notice. A staged folder missing a licence is the one defect in this script that
is not a bug — it is a compliance failure that ships. `publish.ps1` now refuses to finish a
staged build missing a file it is obliged to carry, and names which one.

**Verified by making it bite**: a licence moved aside, and the script threw
*The staged build is missing files it is obliged to ship: licences\LGPL-2.1.txt.* An earlier,
sloppier version of that test appeared to show the guard passing when it should have failed, because
the throw was swallowed by a pipeline; the second attempt checked the file count before and after
and left no room for doubt. **Nothing in this repository is legal advice.**

**Verified.** Build clean at 0 warnings. Suite **2,036** over nine projects, 0 failed, 0 skipped.
`dotnet format` clean.

### 2026-09-01 @ `D20`: Spark ships standalone, and the CAD proof defers

**What.** A decision, and four documents corrected. No code.

**The client asked why a Revit or AutoCAD licence bore on a standalone application.** It does not,
and the register said so before this entry did: `E12`'s own scope note reads *M8 proves the seam, it
does not ship a Revit plugin*. `Spark.Desktop` references `Spark.UI` and the geometry provider and
nothing else, and the only mentions of either CAD product in the entire source tree are **doc
comments** on `IEvaluationScheduler` explaining why the seam exists. Everything demonstrated this
week — the graph, the solids, the viewport, packages, the portable zip — was the
standalone application.

**The previous report listed `E12-T4` beside the signing identity**, which implied Spark could not
ship without a CAD licence. That was wrong, and it is the kind of wrong that matters: a status list
whose blocked items are not really blocking teaches the reader to discount all of them.

**What defers and what does not.** The embedding **mechanism** ships in 1.0 and is built:
`SparkSession` is a composition root with no UI in its reference graph, enforced by
`Spark.Architecture.Tests` rather than asserted in a comment, and `HostThreadEvaluationScheduler`
runs a whole graph on a foreign thread against a real work queue. What defers is the
**demonstration inside a commercial product**, and `E12-T2`'s `IHostServices` with it — an
interface designed without a host to try it against is speculation, and designing it after the first
real add-in costs nothing that designing it now would save.

**The cost is written into `D20` rather than left to be discovered.** 1.0 ships with a seam tested
against a *fake* host — one thread, one queue, a re-entrant marshal that throws — and
never against Revit's external events or AutoCAD's document lock, which is where the surprises live.
So the honest public wording is **designed to be embedded, seam tested, not yet run inside a CAD
application**, and that sentence is now in the README rather than only in a decision row nobody
reads.

**[Q5](PRD.md#14-open-questions) defers with it rather than being answered**, and it is worth noting
that it was always this row's first obstacle: `E12-T4` could not begin until somebody chose which
host, and nobody had.

**Verified.** No code changed, so the gates are unchanged; `Spark.Docs.Verify` green, `dotnet format`
clean. Suite still **2,036** over nine projects.

### 2026-09-01 @ `E12-T12`: a performance pass that mostly found the work already done

**What.** Six lines of benchmark output, [N81](NOTES.md), [N82](NOTES.md), and a row closed on
measurements rather than on effort.

**Reading before measuring is what made this cheap.** Already budgeted and checked by the nightly:
evaluation cold and warm, marshalling both directions, the scene index, and **the canvas frame
against ADR-0013's own ceiling** — 16.7 ms median, one frame at 60 fps. `R15`'s payload was
measured and reconciled on 2026-08-31. There was far less missing here than the row's title implies,
and finding that out cost twenty minutes rather than a day.

**The headline claim holds with room.** Release, 2 000 nodes: render pass **1.2-1.4 ms median,
2.8-3.7 ms p95**, thirteen times inside the ceiling.

**[N81](NOTES.md): the benchmark prints two numbers and only one answers the claim.** Beside the
render pass it prints a wall clock of 24 fps, which read cold looks like a 60 fps claim missed by
two and a half times. `bench/budgets.jsonc` already explains why the render pass is what is judged,
but nobody reads a budget file while looking at a benchmark's output — so the output now says
so itself, in full, once.

**What settles it is a measurement rather than an argument.** The wall-clock floor **does not scale
with node count**: 28.5 ms at 100 nodes, 36.5 ms at 500, 41.1 ms at 2 000. The canvas contributes
about 12 ms across a twentyfold increase; something else contributes a fixed ~27 ms that is there
when the canvas is nearly empty. A second observation says the same thing: Release renders *faster*
than Debug (1.32 ms against 1.75 ms) and reports a *worse* wall clock (45.7 against 31.8). Two
numbers moving in opposite directions between two builds of one program are not measuring one thing.

**The budget was not touched.** Widening a claim to fit a measurement is the failure that note
exists to prevent. The nightly's regexes were re-run against the new output to confirm they still
match, because appending to a line a machine parses is exactly how a guard stops guarding.

**[N82](NOTES.md): startup was measured by nothing.** Now: **48 ms** for `spark --version`, and
**3.0 s** median for the desktop from launch to a rendered shell with geometry and the process
exited. The second is an upper bound rather than a startup time — it goes through the screenshot
path, which waits for a full evaluation and polls for a GL frame at 150 ms granularity — and it is
still the number a user would feel. **The first attempt measured 4 ms**, because
`Measure-Command { & $exe }` does not wait for a `WinExe`; five runs of a plausible-looking wrong
answer is how that kind of mistake survives.

**Not budgeted in CI, deliberately.** Wall-clock startup on a hosted runner is dominated by disk
cache and antivirus, and [N29](NOTES.md) already makes that argument.

**What this pass does not cover, said plainly rather than left implied:** the viewport's own render,
and opening or saving a large document. Neither is budgeted and neither was measured.

**Verified.** Build clean at 0 warnings, `dotnet format` clean, suite unchanged at **2,036**. The
canvas benchmark re-run after the output change: `nodes=2000 frames=250`, median 1.23 ms, p95
2.84 ms, and the nightly's two regexes matched the new text.

### 2026-09-01 @ `E12-T13`: an accessibility bar written as two checkable sentences

**What.** Twenty-eight automation names, three keyboard paths, ten tests, [N83](NOTES.md). **`E12`'s
two 1.0 passes are both done.**

***Make it accessible* is not a task anybody can finish**, so the bar came first and it is two
sentences that are properties of the markup rather than matters of taste: **every gesture reachable
without a mouse**, and **every control named**.

**The colour half was already done, and done properly.** The design language carries contrast
figures, `PaletteContrastTests` asserts them against the real tokens, and Principle 4 already
forbids colour being the only carrier of a state — which is why the frozen node built
yesterday got a mark as well as a desaturation.

**Everything else was missing.** `AutomationProperties` appeared **nowhere in the application**: to
a screen reader every control was anonymous. And the only key bindings were undo and redo, so
opening, saving and running a graph — the three things a user does most — were
reachable by mouse alone. They now have `Ctrl+O`, `Ctrl+S` and `F5`, with the keys in the tooltips
so they are discoverable rather than folklore.

**A bare letter is never bound at window level**, and a test enforces it: it would be taken from
somebody typing into the library search or a code block, and the rule is easier to keep than the
exceptions would be to remember.

**The tests read the markup as text, deliberately.** Instantiating the window to walk its visual
tree needs a dispatcher and returns only the controls that have been realised; the `.axaml` is the
whole truth and it is what a future edit changes. **The risk with a text test is that a regex
matching nothing passes silently**, so a second test asserts the toolbar still has at least twenty
buttons — without it, `EveryToolbarButtonIsNamed` goes green the day somebody renames the
class.

**It found one on the first run**: the missing-package banner's button, whose label is built at
runtime. Its name is now set in code beside its content, because a static name reading *find the
missing package* while the button says *Find Acme.Nodes* is worse than either alone.

**A name that repeats the label earns nothing**, and a third test refuses that too — `Open…`
read aloud is *open ellipsis*. Undo and Redo are the two exceptions and they are the right ones: the
word is the action, and *Undo the last change* would be worse.

**What this pass cannot claim, and the row says so.** No screen reader was run — none is
available here. What is asserted is that a name exists and is not the label repeated. Whether it
reads well aloud is a judgement a person makes with a screen reader running, and nobody has made it.

**Verified.** Build clean at 0 warnings. `UI.Tests` 536 -> 546. Suite **2,046** over nine projects,
0 failed, 0 skipped. `dotnet format` clean. The shell photographed after the markup change, to
confirm twenty-eight new attributes had not disturbed a single pixel of layout.

### 2026-09-01 @ `E12-T8`: three words, three answers, and only two of them are about licensing

**What.** `D21`, and a measurement that took twenty minutes and settled a row that had been open
since the epic was written. No product code.

**The row's title names three things and treats them as one.** *Self-contained single-file
ReadyToRun Windows build.* Its note explains the LGPL relink obligation and concludes, reasonably,
that the row is constrained — and a reader would take from it that **all three** are waiting
on counsel. Only two are.

**Self-contained and single-file: no, on the relink obligation.** OCCT's libraries have to stay
unmodified and replaceable, and a bundle that extracts itself to a temporary directory does not
obviously preserve that. `scripts/publish.ps1` has said so since it was written and was right to.

**ReadyToRun: no, and it is not a licence question at all.** R2R precompiles IL inside the same
managed assemblies; it never touches OpenCascade, which is native and sits beside them. So it was
measurable, and it was measured rather than reasoned about:

| | plain | ReadyToRun | |
|---|---|---|---|
| CLI size | 41.2 MB | 84.5 MB | **+105%** |
| Desktop size | 172.7 MB | 233.2 MB | **+35%** |
| `spark --version`, 7 runs | 52 ms | 51 ms | nothing |
| Desktop launch to rendered shell, 5 runs | 2,032 ms | 2,035 ms | nothing |

**Sixty megabytes for nothing, twice.**

**What the measurement could not see, and `D21` says so rather than leaving it implied.** The
desktop figure goes through the screenshot path, which waits for a full evaluation and then polls
for a frame at 150 ms granularity, so **a JIT saving under about 150 ms is invisible to it**. Even
granting one: 60 MB on a 225 MB payload for a tenth of a second is a poor trade, and `R15` already
established that the payload is the framework-dependent publish rather than OpenCascade.

**It is also the reversible one.** `PublishReadyToRun` is a single MSBuild property, so a no today
costs nothing the day a better measurement arrives — which is exactly why it was worth
answering now instead of leaving the row open beside two that genuinely are blocked.

**The reason for splitting it is not tidiness.** A row that reads *blocked on counsel* when a third
of it is blocked on nobody is a row that stays open for months for the wrong reason. The
measurement cost less than the conversation about it would have.

**Verified.** No product code changed. `publish.ps1` gained a paragraph saying why R2R is off, next
to the switches that turn the other two off, because the reasoning belongs where somebody would
otherwise turn it on. Build clean, `dotnet format` clean, suite unchanged at **2,046**.

### 2026-09-01 @ The Help pass begins: two topics that claimed to predate their own code

**What.** `E10-T6` and `E10-T4`, fourteen schema tests, `docs/HELP-AUTHORING.md`, and two status
lines reconciled against the code rather than edited.

**Surveying first changed what the pass is.** `docs/TODO.md` described the Help as nine topics with
no node reference, no in-product renderer and *F1 does nothing*. That was true when `D19` was taken
and is not true now: **eleven** concept topics, **115 node pages and 18 diagnostic pages generated
from the live library**, a renderer with context-sensitive F1 and search, and the entire `E11`
harness `Done`. The section has been brought level with the repository, because a plan describing a
state that ended a week ago sends the next reader to build things that exist.

**What had genuinely rotted is exactly what `D19` predicted would.** Two topics carried
`Status: Specification` — *written before the engine exists*, *written before any UI code
exists*. The engine has existed since M2 and the UI since M3. **Both sentences were false, in the
two topics a reader is most likely to treat as authoritative.**

**Retiring `Specification` means re-reading the page, not editing the line** [N84](NOTES.md), and
the answer was different for each:

- **`lacing.md` is fully executed.** Its 90-row case table is run twice over on every build —
  once against the values it specifies, once to check every diagnostic it raises carries a help
  topic. 2 x 90 + 1 is the 181 tests that class reports, which is how the 90 was confirmed rather
  than assumed. Its claim that *if the table and the implementation disagree, the table is right*
  is enforced, and the status now says so.
- **`design-language.md` is only partly executed**, and saying so was the honest outcome.
  `PaletteContrastTests` asserts the contrast arithmetic; **the colour tables are not asserted in
  full**. A naive check comparing every hex in the topic against the palette reports 25 unmatched,
  and inspecting five of them showed most are worked examples, rejected candidates or derived
  ladder steps rather than tokens. **A test that cannot tell those apart would cry wolf**, so none
  was written, and the topic now states which half is enforced instead of implying both are.

**`E10-T6`: the schema was already unanimous, which is the argument for writing it down.** All
eleven topics agreed on `id, title, nodes, related, since` and nothing checked it. **The
`related:` check is the one that earns its place** — every entry named a real topic on the day
it was written, and did so **by luck**. Proven to bite by pointing one at a topic that does not
exist, and it named the file and the id.

**`examples[]` was in the row and is deliberately not adopted.** The harness already requires a
worked example in the body; a second, unenforced list of them would drift from the thing it
duplicates.

**[N85](NOTES.md): the docs harness stopped the guide being filed as a help topic.** It was first
written to `docs/help/AUTHORING.md` and `Spark.Docs.Verify` failed at once — *no YAML front
matter*. The check was right and the file was wrong: everything under `docs/help/` is end-user help,
listed by the help window and checked as a topic, and a contributor guide is none of those things.
**The tempting fix, narrowing the harness to `concepts/`, would have traded a real invariant for one
file's convenience.** The guide moved, and says so in its own first paragraph.

**One more stale sentence found while linking it**: the README still described `lacing.md` as
*written before the engine, and the engine will be written to match it*. Same debt, different
document.

**Verified.** Build clean at 0 warnings. `UI.Tests` 546 -> 560. Suite **2,060** over nine projects,
0 failed, 0 skipped. `dotnet format` clean.

### 2026-09-01 — Staging a build for a hands-on, and finding a 15-second stall

**What.** No code. A staged Release build, a question answered, and [N86](NOTES.md).

**The build runs and the headline works.** `scripts/publish.ps1` stages 225 MB, the solids demo
opens, and the viewport shows what M6 promised: a shelled cylinder unioned onto a plate, a plain
box, a filleted box, all through OCCT. `spark.exe` in the same folder reports the kernel.

**And opening that demo takes eighteen seconds.** Measured against its neighbours and against the
same graph by every other route:

| | |
|---|---|
| Desktop, points | 2.1 s |
| Desktop, curves | 3.1 s |
| **Desktop, solids** | **18.2 s** (15.1 / 22.2 / 19.1 over three runs) |
| `spark export`, same file | **~290 ms** |
| `GraphEvaluator.Evaluate`, same file, kernel installed | **31-77 ms** |

**So it is not the solver and not the evaluator.** The CLI runs the same 26 nodes through the same
provider and writes STEP in under a third of a second.

**The first hypothesis was the scheduler and it was wrong.** The desktop runs the parallel scheduler
and the CLI the sequential one, and `Q14` had already established that OCCT tolerates concurrency
only under conditions. Timed side by side: sequential 77 ms, parallel 33 ms. **Parallel is the
faster of the two.**

**The probe that tested it was wronger, and nearly convincing.** Its first run reported 3 ms and
76 ms — and **three diagnostics**, because the test host had never installed the kernel, so every
solid operation failed instantly. Both numbers were real; neither was about solids. Printing the
diagnostic count beside the timing is the only reason that did not become the answer.

**What is left is the path between an evaluated solid and a frame** — tessellating a BRep into a
mesh, and building the viewport's buffers — for **three objects**. `spark export` never goes
there, because STEP carries BRep rather than triangles, which is exactly why the CLI does not show
it.

**This is the gap `E12-T12` named and did not measure.** That pass said in as many words that it did
not cover the viewport's own render, and nothing in `bench/budgets.jsonc` touches tessellation. The
one demo that exercises it is fifteen seconds slower than the two that do not, and **no test would
have said so** — which is the more useful half of this entry. Recorded as `E12-T19` rather than
fixed in passing, because it deserves the same discipline as the passes did: measure where the time
goes before changing anything.

**Verified.** No code changed. Suite unchanged at **2,060**.

### 2026-09-01 — `E12-T19`: half a degree, and the third wrong hypothesis in a row

**What.** One constant changed, two tests, [N87](NOTES.md). **18.2 s to 2.0 s.**

**The whole of it was one line.** `SceneBuilder.DisplayTolerance` asked the kernel for an angular
deflection of **0.5 degrees**. That reads like a sensible smoothness figure and is not: it is around
fifty-seven times finer than the half a *radian* a mesher of this kind conventionally defaults to,
and the cost against it is nowhere near linear. On the demo's nine solids, sag held constant:
**0.5 deg gave 17,440 ms and 1,110,772 triangles; 6 deg gives 61 ms and 11,636.** Two hundred and
eighty-six times.

**Checked by looking, not only by measuring.** The re-rendered demo is indistinguishable: the
cylinder is still round, the fillet still reads as a fillet. Six degrees gives a cylinder sixty
segments, which is smooth at any zoom this viewport reaches. Desktop wall clock over four runs:
**2.02, 2.02, 2.03, 2.05 s**, the same as the points demo.

**Three hypotheses, three wrong, and a measurement killed each one.**

**(1) The scheduler**, because the desktop runs parallel and the CLI sequential and `Q14` had
established OCCT tolerates concurrency only under conditions. Timed: sequential 77 ms, parallel
33 ms. Parallel is the *faster* one.

**(2) The probe that tested it**, whose first run reported 3 ms — and **three diagnostics**,
because the test host had never installed the kernel and every solid operation failed instantly.
Both numbers were real; neither was about solids. Printing the diagnostic count beside the timing is
the only reason that did not become the answer.

**(3) The first tolerance sweep**, which reported that the angle barely mattered — 1,110,772
triangles at 0.5 deg against 1,102,132 at 2 deg. **Every row after the first was a cache hit.**
`Tessellate` caches against the shape and **not** against the tolerance, so one set of solids swept
through six tolerances is one tessellation and five lookups. Putting the coarse row *first* exposed
it: 35 ms and 1,332 triangles, and then the same coarse request after a fine one returned 1,099,460.
**A parameter sweep over a cached function measures the cache**, and the tell is a result that does
not vary when it obviously should.

**That cache behaviour is real and is recorded as `E12-T20` rather than fixed here.** It produces no
wrong picture — a finer mesh is still valid geometry, which is why nobody noticed — but a coarse
request cannot make anything cheaper once a fine one has been made.

**The tests assert the triangle count, not the time.** A wall-clock ceiling on a shared machine is
the flakiest test there is, [N29](NOTES.md) already argues it, and the count is what actually moved:
1,110,772 against 11,636. The ceiling is 50,000 — four times the real figure and a fortieth of the
old one. A second test reads the constant itself and refuses anything finer than a degree, because a
failure naming the count is one somebody has to go and diagnose, and a failure naming the cause is
not.

**And one number that looked wrong and was not.** `CurveDrawable` tessellates at
`Angle.FromDegrees(0.001)`, five hundred times finer again. Measured: 2 ms, 63 points, and **the
point count is identical at 0.001, 0.5, 2 and 6 degrees**, because sag dominates for a curve. Left
alone. Changing it on suspicion would have been the fourth wrong hypothesis.

**Verified.** Build clean at 0 warnings. `UI.Tests` 560 -> 562. Suite **2,062** over nine projects,
0 failed, 0 skipped. `dotnet format` clean. Staged build re-rendered and compared by eye.

### 2026-09-01 — A tessellation budget, and three defects a person found by opening the app

**What.** Two unrelated things that belong in one entry because the second explains the first.

The budget: a `tessellate` verb in `bench/Spark.Benchmarks`, a `tessellation` section in
`bench/budgets.jsonc` (`minSolids: 9`, `maxTriangles: 50000`), and `--tessellation` /
`--no-tessellation` on `BudgetCheck`. `E12-T19`'s eighteen seconds hid because nothing in `bench/`
touched tessellation at all; now a regression in `DisplayTolerance` fails a nightly rather than
waiting for somebody to notice the app is slow. `CheckAtMost` grew a `unit` parameter so a
triangle count stops printing as `11636.00 ms`.

Then the app was opened by a person, and three things fell out in a row.

**One: the viewport ignored the mouse entirely.** The wheel, the middle button and the right
button were all wired correctly and not one of them had ever run. `Render` returned early once GL
had initialised, so the control drew **nothing** — and Avalonia hit-tests against what a control
actually drew. The 3D content is a compositor-owned GL surface that is not in Avalonia's scene
graph, so there was no geometry to hit and every pointer event went to whatever was behind it. The
fix is one `FillRectangle` with `Brushes.Transparent` before the early return: invisible, and
hit-testable. Shift-and-middle now orbits too, which is the binding that was being reached for
when this surfaced.

**Two: the code editor was invisible, and I shipped the wrong fix first.** Four controls shared
`Grid.Row="3"` in `InspectorPane.axaml` and the port list had no `IsVisible`, so it painted over
the editor. That was real, and it was not the cause. I said it was the fix and relaunched
**without confirming the editor rendered**, because my harness was hanging — and it was still
invisible. The actual cause: **AvaloniaEdit's theme was never registered**, and a control with no
theme has no template and renders nothing. One `StyleInclude` in `App.axaml`. The row split stayed
because it was a genuine defect sitting behind the first one.

**Three: the text was unreadable once it appeared.** AvaloniaEdit's stock C# highlighting is
written for a light background — navy keywords, dark red strings — on `surface.sunken` at
`#1A1E24`. `Recolour` puts it on tokens the design language already publishes with measured
contrast figures, reusing the node category fills, so a keyword is the same blue as a Script node.

**Verified.** Three gates green: clean `-warnaserror` build, `dotnet test Spark.slnx` **2071
passed / 0 failed** over nine projects, `dotnet format --verify-no-changes` clean.
`ViewportNavigationTests` (6) presses actual buttons through the headless window and
`TheViewportIsHitTestable` asserts the property the other five depend on, so a failure names the
cause instead of leaving five gesture tests to fail together. `DisplayTessellationTests` (2) puts
a ceiling on the triangle count. `CodeBlockReachabilityTests` (3) covers selection.

**What surprised me, and it is the entry's real content.** All three defects were **wired
correctly and covered by tests that never touched the surface a person touches**. The viewport had
tests for its camera, its renderer, its read-back and its tessellation; the one thing none of them
did was press a button. `CodeBlockReachabilityTests` passed green the entire time the editor was
invisible, because it proved the view model, not the pixel. Every one of the three was found by a
human opening the application. That is not a gap in coverage — the coverage was there — it is a
gap in **what** was covered, and no amount of the same kind of test would have caught any of them.

**Owed, and named so it is not quietly dropped.** A regression test for the inspector/editor
visibility: I wrote one, it hung in the headless dispatcher, and I **deleted it** rather than
leave a hanging test in the suite. The `App.axaml` fix is therefore verified by a person's eyes
and nothing else, which fails AGENTS.md step 7. A contrast test for the editor colours alongside
`PaletteContrastTests` is owed too. Both are on the queue.

### 2026-09-01 — `E6-T19`: a type per port, and the design the client asked for was not the one they took

**What was asked.** "Allow multiple input for codeblock. provide + button to add new input and -
button to remove input. Let the default be zero inputs. Provide dropdown for each input to set
variable name and data type. You may suggest if there is a better way."

**What was built, and why it is narrower.** The trade-off was put to the client before any code
was written, with three options and the honest cost of each. They took the middle one.

Declaring **names** in the panel would put a second source of truth beside the code. Today a port
exists because the script uses a name it has not declared — one answer to "what are this block's
inputs?", and the answer is the code. With a declared list, `radius` in the list against `radus`
in the source is an unused input plus an undeclared identifier, and renaming through the dropdown
has to rewrite the user's source. It is also how Dynamo works, and `DYNAMO-COVERAGE.md` treats
that parity as a goal.

Declaring **types** has no such problem, because there is nowhere else a type can be said. So:
names stay inferred, and every input port gets a type dropdown.

**Two changes, committed separately.**

`E6-T18`: the starter script was `return a;`, which is why a new block arrived with an input `a`
nobody asked for — and removing it requires already knowing that you delete an identifier from the
source, which is exactly what the starter failed to teach. An empty script turns out to be legal
(zero inputs, one `result` output), so the starter is one comment line stating the rule.

`E6-T19`: a dropdown on each input row, defaulting to *from the wire*. Choosing a type recompiles
the block immediately. **A declaration beats the wire** — the wire is the better source whenever
there is one, which is why it is the default, but a setting that is quietly overruled is worse
than no setting.

**Where the pieces went, because the shape is the interesting part.** `inputTypes` was already
plumbed end to end — `CanvasGraph.Retype`, then `Graph.InputTypes`, then
`ScriptNodeFactory.Create` — so this is a **new source for an existing input**, not new machinery.
`Graph.InputTypes` walks the wires first and overlays declarations last, which is how precedence is
expressed. Declarations are held by port **name**, for the reason that method already gives: a code
block's port indices move when its source gains an identifier. `ReplaceDefinition` carries them
across a rebuild exactly as it already carries wires and group memberships. They round-trip as
short tokens (`point`, not an assembly-qualified name), and an unrecognised token costs the
setting, never the document.

**Verified.** Three gates: clean `-warnaserror` build, **2093 passed / 0 failed**, format clean.
Nine tests on the engine half, eight on the panel, three on the round trip — including that a
declaration survives an edit to the script, that undo undoes it, and that a graph declaring
nothing writes no `inputTypes` at all, so files written before this are byte-identical.

**What the reversion check bought, twice.** `E6-T18`'s two tests were watched red against the old
starter before it was committed. And building the panel test turned up a real defect no
view-model test would have found on its own: a `ComboBox` writes **null** back through a two-way
`SelectedItem` binding while it is being realised, before `ItemsSource` has been applied. Acting on
that would have silently cleared a declaration every time the panel was rebuilt — which is every
time the selection changes. "The user declared nothing" is spelled `NotDeclared`, an entry in the
list; null is never a choice. Guarded, and `ANullFromTheBindingDoesNotClearTheDeclaration` holds it.

**What could not be verified, stated rather than glossed.** The dropdown's *rendering* is verified
by a person's eyes and by nothing else. A test that shows `InspectorPane` with a bound view model
**hangs the headless dispatcher** — the same hang that killed `InspectorLayoutTests` earlier
today. This time it was bisected properly ([N90](NOTES.md)): a plain `ComboBox` in a window renders,
the pane constructs, the pane shows *unbound*, binding succeeds — and capturing a frame with the
data context attached hangs. It is **not** the pane's contents; it survives hiding the code editor
and emptying the port list, so the failing case is a pane with almost nothing left to draw. That
is `E6-T20`, and it is on the queue rather than implied.

**The pattern is now three sessions old.** The invisible editor, the dead viewport, the overlapping
grid rows, and now this: every one of them lives in the gap between a view model that tests can
reach and a surface that tests cannot. The view-model tests here are good and they would all have
passed with the dropdown drawing nothing at all.
