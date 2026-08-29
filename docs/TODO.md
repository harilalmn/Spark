# Spark — TODO

What to do next, in priority order. Full context in [EPICS.md](EPICS.md), full inventory in
[TASKS.md](TASKS.md), the reasoning in [PRD.md](PRD.md).

**Last updated:** 2026-08-29

**M0 and most of M1.5 have landed, M2's walking skeleton runs, M1's geometry core now has curves,
a graph can be saved and opened, and every edit can be undone.** The application opens, a graph evaluates, and an ellipse,
eight circles and a polygon appear in the GPU viewport — from a seeded demo or from a file, and
Ctrl+Z steps back through every edit. `dotnet build`, `dotnet test` (**952 tests over seven
projects**) and `dotnet format` are all clean, and **CI ran green on Windows and Linux on
`53596ab`**, 952 tests on each leg — and the Linux leg has now caught something Windows could
not, which is the first time it has been worth more than it cost ([N28](NOTES.md)).

**The benchmarks stopped being a report and became a guard on 2026-08-29.** A nightly workflow
runs the three suites on both operating systems and the application's own canvas benchmark on
Windows, and checks every number against budgets committed in `bench/budgets.jsonc` — allocation
tightly, ratios sharply, wall-clock loosely and for stated reasons
([ADR-0023](adr/0023-performance-budgets-not-a-benchmark-time-series.md), [N29](NOTES.md)). It is
green locally end to end and **has never run on a hosted runner**, which is the difference between
proven to detect and proven to run.

Three distinctions still do the work in what follows:

- **What is built.** The value layer (13 types), the curve layer (`Line`, `Arc`, `Circle`,
  `EllipseCurve`, `PolyLine`, `PolyCurve` over a `Curve` base, with arc-length
  reparameterisation), the graph engine and replicator, the reflection importer with its
  two-way diff, the Avalonia shell, the immediate-mode canvas, the GL viewport, 57 nodes in
  `Spark.Nodes.Core`, a `.spark` file a graph survives a round trip through byte for byte, and a
  64-step undo stack over that same file format.
- **What is not.** No surfaces, meshes, BRep or solids. No `NurbsCurve`. No `spark run`. No
  packages and no code block. And **no OCCT**: there is no `native/` directory and no
  `Spark.Geometry.Occt` project.
- **Gates are not review, and this project now has its own proof three times over.** The
  kernel's first slice passed all three gates and was rejected on review ([NOTES N18](NOTES.md)).
  The curve layer's mutation sweep then found a test that could not fail and a branch that could
  not be reached ([N19](NOTES.md), [N20](NOTES.md)). The undo sweep found the same shape a third
  time: a test asserting that clicking a node is not an edit passed under a mutation that recorded
  *every* drag, because a click raises no pointer-move event and never reached the guard the test
  was written for. All of it in code that was green.

**The largest decision in the project is unchanged and unbuilt.** The client chose to take an
existing solid-modelling kernel rather than write one: **OpenCascade, reached through a C-ABI
shim we own** ([ADR-0020](adr/0020-occt-via-c-abi-shim.md),
[ADR-0021](adr/0021-brep-kernel-residency.md), PRD **D2** and **D15**). It retires **R1** and
**R12**, adds **R15 … R22**, adds a two-week spike **M1.6**, adds epic
[E13](EPICS.md#e13--occt-provider) of roughly 24 weeks, and costs **+7 to +11 weeks against the
plan as written while saving years against what was actually asked for**. `50a9935` measured
what comparable projects actually ship: a full win-x64 OCCT build is 52.1 MiB across 47
toolkits plus 9.9 MiB of optional third-party libraries, so **R15's 40–160 MB bracket should be
read as 55–70 MB** — but that is a survey, not a build.

**As of 2026-08-29 the spike has criteria and still has no build.** Nine of them, `M1.6-C1` …
`M1.6-C9` in [TASKS.md](TASKS.md#m16--the-passfail-criteria-written-before-the-spike), written
ahead of the work rather than beside it, and each carrying what a failure would mean. The
distinction they draw is the useful one: **exactly one criterion can reopen ADR-0020**, and it
reopens the binding rather than the engine.

**One row on this page came from using the product rather than from planning it**, and it is worth
saying so where the plan lives: `E8-T18`, port type labels. Nothing in the PRD asked for them and
nothing in EPICS was short without them. Somebody opened the application, put down a
`Circle.ByCentreRadius`, and could not tell what `centre` wanted. The requirement was written
afterwards (**FR-82**), which is the right order for a defect nobody predicted and the wrong order
for anything else.

---

## Now — take the M1.6 spike, and the last M1.5 one

- [x] **Walk TASKS.md against E3, E4, E5, E8 and E9** — done, and it moved 41 rows. Ten of them
      came back **`In progress` rather than `Done`**, which is the useful output: the cache
      evicts by entry count rather than by bytes, no node can declare itself impure, the
      host-thread scheduler is missing, cancellation does not reach inside a kernel operation,
      the shell has no real docking, the library panel filters without ranking, and three rows
      wait on the empty `bench/`. Two more are `Open` **with a stated reason** rather than by
      omission, because the importer's two-way diff will not let a public member go unaccounted
      for. Details on the rows and in [EPICS.md](EPICS.md).
- [x] **Save and load a `.spark` file** — `E3-T17`, `E3-T18`, done. Open and Save are on the
      toolbar, `--open PATH` opens one at startup, and `docs/examples/curves.spark` is committed
      as a golden file that the suite re-derives from the seeded demo. Read-then-write is
      byte-identical, and so is the longer path the application takes.
- [x] **Undo and redo** — `E8-T9`, done. A 64-step stack of whole-document `.spark` snapshots
      on Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z, and on the toolbar with the step named in the tooltip
      ([ADR-0022](adr/0022-undo-by-document-snapshot.md)). The choice of a snapshot over an
      inverse-command stack was made on **coverage**: a node's position never enters the engine
      graph, so a command stack over the engine's own mutations would have missed moves and would
      go on missing every future canvas-side edit. **It also closes a claim that had been repeated
      since M0 without a test behind it** — the run after an undo now recomputes zero nodes and
      serves every one from the provenance cache, measured rather than claimed (`E3-T8`).
- [x] **Create `bench/Spark.Benchmarks`** — `E1-T13`, done. Three suites: marshalling (`E4-T3`'s
      standing guard), evaluation cold against warm, and the canvas spatial index at 2 000 nodes.
      Every one was run before the row was ticked, and **two of the three were measuring the wrong
      thing** until the numbers gave them away ([N26](NOTES.md)).
- [x] **Add the no-native-binaries CI check** — `E1-T20`, done, and **inside the window**.
      `scripts/check-no-native-binaries.sh` runs in the CI build job on both operating systems and
      was proven to fire before being trusted: pointed at `Spark.Desktop` it fails on the Skia and
      HarfBuzz natives Avalonia brings.
- [x] **Run the benchmarks on a schedule** — `E1-T21` and `E4-T3`, done; `E8-T15` short only its
      first run. `.github/workflows/nightly.yml` runs the suites on both operating systems, drives
      `--canvas-benchmark` on Windows, and checks every number against budgets committed in
      `bench/budgets.jsonc`. **The storage half of the M0 plan is reversed and the cadence half is
      not**: budgets, not a committed time series
      ([ADR-0023](adr/0023-performance-budgets-not-a-benchmark-time-series.md)). What makes the
      budgets worth anything is that they are not all the same strength — allocation is
      deterministic and is held to ten per cent, ratios between two cases are machine-independent
      and are the sharpest guards in the file, and wall-clock ceilings on a shared runner are an
      order of magnitude out and catch a step change only ([N29](NOTES.md)). Proven to fire before
      being trusted, on eight deliberate breakages including **a case that vanished and a case
      nobody budgeted**, both of which fail the run.
- [ ] **Watch the first nightly, and treat it as part of adding the guard.** The workflow is green
      locally end to end and **has never run on a hosted runner**. Two things can only be learnt
      there: whether a GitHub Windows runner can open a window at all — if it cannot, the honest
      answers are a headless measurement or deleting that step, never `continue-on-error` — and
      whether the wall-clock ceilings, set an order of magnitude above one laptop's numbers, are
      loose enough for a shared machine. `E8-T15` closes on the first green run, not before.
      [N28](NOTES.md) is why *proven to detect* and *proven to run* are different claims.
- [x] **Write the M1.6 pass/fail criteria into TASKS.md** — `E13-T1`, done on 2026-08-29. Nine
      criteria, `M1.6-C1` … `M1.6-C9`, in
      [TASKS.md](TASKS.md#m16--the-passfail-criteria-written-before-the-spike). **The bars were the
      easy half; what each failure would mean is the half that had to be settled while nobody had a
      result to defend** — `M1.6-C2`, one boolean end to end, is the only criterion that can reopen
      ADR-0020, and it reopens the binding rather than the engine; `C1`, `C3` and `C4` change the
      plan without touching the decision; `C5` … `C9` cannot fail on their answers at all, only on
      not being asked. It also writes down what must **not** count as a failure — OCCT being hard
      to debug or slow to upgrade are costs ADR-0020 has already accepted — and the stop rule, since
      a spike that overruns is stopped and reported rather than extended into the implementation.
- [ ] **Take the M1.6 spike** — `E13-T1`, two weeks, and it is now the largest unstarted thing in
      the project. Priority order if the two weeks run out: **`C1`, `C2` and `C3` are the gate** and
      must land, then `C5`'s first read of the threading envelope, because `R20` is a top-three risk
      and M6 needs the answer rather than the reading. The manifest and the build recipe are kept;
      the shim written here is a probe and is deleted. **Two of ADR-0020's seven open items are not
      on this critical path at all** — the counsel question and whether `OcctNet.Wrapper` has a
      source repository — and neither should be reported as *not done* afterwards. The shape of the
      work is unchanged: OCCT builds from a pinned tag through a vcpkg manifest on Windows *and*
      Linux; one boolean runs end to end through a minimal `spark_occt` and `LibraryImport`; the
      per-RID footprint is **measured** against `50a9935`'s 55–70 MB expectation; a `Materialise`
      on a realistic shape is timed, because ADR-0021's whole rule rests on it being paid once; and
      a first read is taken on the threading envelope (`Q14`) and on whether `ShapeFix` can be
      constrained to a policy we choose.
- [ ] **The third M1.5 spike is still outstanding** — `E11-T21`. AvaloniaEdit plus a Roslyn
      completion popup. It gates the M4 code block rather than anything M2 needs, which is why
      the other two were taken first, but it is the last unproven part of M1.5.

## Then — the rest of M1, the geometry core

**Done since the last revision:**

- [x] `Line`, `Arc`, `Circle`, `EllipseCurve`, `PolyLine` and `PolyCurve` over a `Curve` base —
      `E2-T7` … `E2-T9`. The contract was settled against the parity register **before** the
      types were written (`E2-T41`), so the `AtLength` family and arc-length division are in it
      rather than retrofitted onto it. Exclusions are named on the types rather than left to be
      discovered: no closest point, no split, no curvature, no NURBS conversion, and no value
      equality on curves.
- [x] Value types, `Transform`, `Plane`, `CoordinateSystem`, `Tolerance`, `Angle` — `E2-T2` …
      `E2-T6`, with `E2-T1` short only its `Quaternion`.
- [x] Property-based tests with CsCheck — now 38 properties, generators spanning 1e-9 to 1e9 per
      **ADR-0018**, covering curves as well as values.

**Still to do, in rough order:**

- [ ] `Quaternion` — `E2-T1`. **`Rgba` is settled and no longer in scope here**: it lives beside
      `Appearance` in `Spark.Api` (`E5`), because the kernel carries no appearance.
- [x] **Settle the past-participle naming rule and apply it** — `E2-T49`, done 2026-08-29.
      `Plane.Flip` → `Flipped`, `BoundingBox.Inflate` → `Inflated`, `Interval.Expand` → `Expanded`,
      across twelve call sites, the public-API baseline and a worked example in `docs/help/`. **The
      rule now lives in `NamespaceDoc.cs` rather than in a survey document**, so it binds every type
      added after it instead of having to be remembered — which is the half of this item that was
      worth more than the rename. Free today because nothing is shipped and the compiler finds every
      call site; an ADR-0019 change-control question the day after 1.0.
- [ ] Extract `RayCaster.cs` and its BVH — `E2-T15`. The highest-value file in C2VGeometry, and
      it pays for itself three times over across mesh booleans, viewport picking and intersection
      seeding. **`Curve.ClosestPoint` is now waiting on it too.**
- [ ] Geometry serialization v1 and the reflection-driven round-trip test — `E2-T29`, `E2-T31`.
      Get the test in before there are twenty types to retrofit it onto; there are now nineteen.
- [ ] **The C2VGeometry test harvest, timeboxed to one week with a hard stop** — `E2-T32`.
      Harvest only pure-maths-on-values tests; anything needing a `Shape` is discarded without
      argument. **Harvest the assertions, not the generators** — a harvested test whose inputs
      never approach the boundary it checks is a test that cannot fail, which is the trap this
      project has already fallen into twice.
- [x] **Close the three small parity gaps in the value layer** — `E2-T40`, done 2026-08-29.
      `BoundingBox.Intersection`, `Plane.Offset` and `Plane.ByOriginNormalXAxis`. Parity moves
      from 92 to 95 of 837. **Two of the three were not as trivial as the register said**:
      `Intersection` delegates each axis to `Interval.Intersection` rather than reimplementing
      the tolerance rule, which is what stops a box and its three intervals disagreeing about a
      boundary case, and `Offset` rejects a non-finite distance because that is otherwise the one
      route to an invalid `Plane` from a factory. `Interval.Intersect` was renamed `Intersection`
      with them, so both types name their set operations with nouns.
- [ ] `spark` writes an OBJ polyline that a third-party viewer opens. That is the M1 demo, and
      `Spark.Geometry.Io` is still an empty project — but the curves it would write now exist.

## After that — finishing M2, and M1.6

M2 was the highest-information milestone in the project: it simultaneously validated Avalonia
GL, the canvas rendering strategy, the reflection importer, the lacing engine and the layering
split — the five things that could still have forced an architectural change. **All five held.**
What is left of it is the part that makes the skeleton usable rather than demonstrable.

**Done:**

- [x] The two M1.5 spikes that gate M2 — `E11-T19`, `E11-T20`. Both bets held, and the GL one
      returned a finding that changes how every shader is written: Avalonia on Windows defaults
      to ANGLE, so the surface is OpenGL ES 3.0 over Direct3D 11, never desktop GL 3.3.
- [x] Graph model, topological evaluation, provenance cache — `E3-T1` … `E3-T8`.
- [x] **The full replication engine against the lacing specification** — `E4-T2` … `E4-T12`,
      with a corpus test that diffs the corpus against the specification document in both
      directions, and which found two errors in the specification itself.
- [x] The zero-config reflection importer over `Spark.Geometry` — `E5-T2` … `E5-T5`, with the
      two-way diff that makes an unreachable public member a red build.
- [x] Avalonia shell, docking, `GraphCanvas` with drag, wire, pan, zoom, select, delete —
      `E8-T1` … `E8-T6`.
- [x] GL viewport for points, lines and curves — `E9-T1` … `E9-T6`. Curves arrived with the
      curve layer and are drawn from their own tessellation at a display tolerance derived from
      the curve's size, not from the kernel's default.
- [x] **Manual acceptance, run and captured:** the application opens, the graph evaluates, and
      an ellipse divided by arc length, eight circles laced from one node, and a pentagon appear
      in the viewport. `--graph curves --screenshot PREFIX` reproduces it without a human.

**Still to do:**

- [x] Save and load — `E3-T17`, `E3-T18`. A graph now outlives the process.
- [x] Undo and redo — `E8-T9`. Over the same file format, which is what makes it cover a node
      position as readily as a wire.
- [x] Every port shows the type it wants — `E8-T18`. Not in the plan; found by opening the
      application and looking at `Circle.ByCentreRadius`, where a port called `centre` gave no way
      to learn that a `Point3d` belongs in it.
- [x] Library search with camel-hump ranking — `E8-T8`. `cbcr` finds `Circle.ByCentreRadius`.
- [x] Double-click empty canvas to create a node there — `E8-T19`. Asked for as *"let
      double-clicking a blank space add the code block, as in Dynamo"*, and delivered as the half
      that is not blocked: the gesture, and a ranked search box at the pointer. **The code block
      itself is M4** — `Spark.Scripting` is empty, the engine has no per-instance node definitions
      and the file format has nowhere to put script text. When it lands, the same box gains "if
      what you typed is an expression rather than a name, make one of those", which is exactly
      what Dynamo does.
- [ ] `spark run` — `E12-T5`. `Spark.Cli` is still a stub, and it has nothing to run until a
      graph can be loaded from a file.
- [ ] **The M1.6 OCCT spike, against criteria written down beforehand** — `E13-T1`. It answers
      four of the seven things [ADR-0020](adr/0020-occt-via-c-abi-shim.md) records as open, and
      **it is the only place they can be answered** — the rest of that list needs counsel or a
      publisher, not a build. Its scaffolding is throwaway in the M1.5 sense, but the vcpkg
      manifest and the build recipe are kept.

**Deliberately excluded from M2:** code blocks, packages, surfaces, custom nodes. Naming the
exclusions is what keeps a walking skeleton from becoming a death march.

## Later — M3 onward

- [ ] `NurbsCurve` complete, curve intersection, offset, fillet; `Planar` regions and 2D
      boolean; List/Math/String/Logic categories; watch nodes and preview bubbles — M3.
- [ ] The C# code block: the RCS/CADScript/DoodleSharp port campaign, input inference with
      wire-typed IntelliSense, tuple-named outputs, resident and persistent compile caches,
      guard weaving — M4, `E6`. **Demo this publicly when it lands** — it is the signature
      differentiator.
- [ ] Surfaces, `NurbsSurface`, `Mesh`, mesh tessellation and `RenderPackage` streaming,
      shaded viewport, OBJ/STL/PLY read and write, glTF write, the software renderer and CI
      visual regression — M5. **The throwaway SSI spike is gone** (`E2-T37`, withdrawn): there
      is no longer a managed exact-boolean estimate for it to calibrate, and its de-risk budget
      moves to M1.6.
- [ ] BRep, `IBrepKernel` with `Capabilities` gating the UI, and **exact solid operations
      through OCCT** — M6, `E2-T22` … `E2-T28` plus most of `E13`. **M6 is now 20–24 weeks
      rather than 14**, and its demoable improves from *solids that can be combined* to
      *solids that can be combined, filleted, shelled, trimmed and exported to STEP*. The
      robust **mesh** boolean (`E2-T27`) moves out of M6 to 1.x, greyed by `Capabilities` —
      reduced, not cancelled, because OCCT is poor at mesh booleans and Dynamo has them.
- [ ] Packages, per-package-version ALCs, missing-package placeholders, the trust store,
      local DLLs with hot reload, custom nodes, groups, notes, freeze — M7, `E7`.
- [ ] `Spark.Host` proven inside a real Revit or AutoCAD add-in; STEP and IGES through OCCT
      (`E13-T12`); the Windows installer, **now carrying native binaries and the licence
      obligations that come with them** (`E13-T16`, `E13-T17`, `E12-T18`); the website;
      performance and accessibility passes — M8, `E12`. **1.0.**

## Decisions waiting on someone

| # | Question | Blocks |
|---|---|---|
| Q1 | **Two of the three M1.5 spikes are answered and both bets held** (`85e3183`): the GL viewport initialises and draws on the platform we ship to, and the immediate-mode canvas holds 2,000 nodes at 0.87 ms median. The third — AvaloniaEdit plus a Roslyn completion popup, `E11-T21` — is not taken; it gates the M4 code block. | M4 design |
| Q4 | `Directory.Build.props` promotes CS1591 to an error on **four** projects; the plan named three. Is `Spark.Geometry.Io` deliberately included? | `E10-T8` scope |
| Q5 | Revit or AutoCAD as the M8 embedding proof host? The scheduler is the same either way; the add-in shell, licensing and test loop are not. | `E12-T4` |
| **Q13** | **The six licensing questions for counsel, and this is the item on this page with an outside clock on it.** The central one: **is a thin shim whose entire purpose is to expose OCCT a *work that uses the Library* under the Open CASCADE exception, or a derivative work under LGPL §5?** Then — whether single-file, trimmed or AOT publishing is compatible with the relink obligation; whether **vcpkg's port declaring `LGPL-2.1-only`, omitting the exception**, creates exposure; what *prominent notice in supporting documentation* requires concretely; what obligations attach to a user embedding `Spark.Host` in a commercial add-in (`D5`); and whether the source offer is satisfied by a tag reference or needs a hosted archive. **None of this is legal advice and no amount of further reading settles it** — it is a question for a lawyer, and it is on this page for that reason. [ADR-0020](adr/0020-occt-via-c-abi-shim.md) | **Items 1 and 3 before M6.** The rest before 1.0 |
| **Q14** | **What is OCCT's real thread-safety envelope?** May the parallel evaluator call the shim concurrently, and at what granularity? Documented guidance is thin, and `R20` is a **top-three risk that cannot be mitigated until this is known**. *How to find out:* read the upstream source of the packages we actually call, and stress the shim at the evaluator's real thread count — `E13-T14`, started at M1.6. The conservative fallback is a single-writer policy, which would cost throughput on exactly the workload replication makes common. | `E13-T14`, M6 |
| Q7 | Which public STEP corpus is authoritative, and which third-party viewer is the reference? **Downgraded, not closed.** We no longer write a STEP writer, so this stops being about defending a subset and becomes about validating *our use* of OCCT's — smaller, and still necessary, because *OCCT wrote it* is not evidence that a file we produce is correct. | `E13-T12`, and no longer gating anything upstream |
| Q8 | Where does the website live, and who maintains it? | `E10-T14` |
| Q12 | **Is T-Splines in scope at all?** 169 members across 8 types — **20.2% of the entire ProtoGeometry surface**, with `TSplineSurface` alone at 94, more than `Curve`. It is a subdivision-surface modeller, a different discipline from BRep/NURBS, and its API is a sculpting editor (bevel, bridge, weld, crease, slide, fill hole) with its own file formats and its own topology layer. Recommendation: exclude it and say so publicly, as PRD §9 already does for STEP's scope. `ADR-0003`'s closing note calls a subdivision backend *a different decision, not a widening of this one*, so nothing is foreclosed. **The answer sets the denominator of every coverage figure we quote.** [DYNAMO-COVERAGE §6.2](DYNAMO-COVERAGE.md#62-t-splines-is-a-second-product-not-a-subsystem), `E2-T48` | Every parity figure; M5 planning |

*Q6 and Q11 are answered, both by the same client decision, and **the answer to each is the
opposite of what was recommended**. **Q11** asked whether parity moves exact solid booleans into
1.0. It does: the 70 members are 1.0 requirements, delivered by OpenCascade, and **`R1` retires
rather than being mitigated again**. **Q6** asked whether an *optional* OCCT-backed package
would breach the no-native-dependencies promise, and is answered by the package not being
optional — **OCCT ships in the default install**, because a Dynamo user finding booleans greyed
out on first run is precisely what FR-81 forbids. What survives untouched is **NFR-5**:
`Spark.Geometry`'s published output still contains no native binaries, still asserted by CI,
because the native component lives in `Spark.Geometry.Occt`. What replaces the question is
**`R13`, reframed and enlarged** — Spark now acquires a heavyweight native dependency, the
distinction from the one it exists to remove is real, and **it only holds if we say it first,
in our own words**. `E2-T47`, PRD **D2** and **D15**,
[ADR-0020](adr/0020-occt-via-c-abi-shim.md).*

*Q12 stays open and ADR-0020 does not answer it. OCCT has no subdivision modeller either, so
nothing about this decision makes T-Splines cheaper or likelier. The recommendation is
unchanged — exclude it and say so publicly — and so is the reason it must be decided rather
than left: **the answer is the denominator of every parity figure we quote.***

*Q2, Q3 and Q10 — all three about package IDs and which projects would publish — are answered
and withdrawn together. Nothing publishes: `IsPackable` is `false` for every project and no
`PackageId` exists anywhere, so `Spark.Geometry.Io`'s availability does not matter,
`Spark.Docs` was a reservation for a package that will not exist, and `Spark.Host`'s
`IsPackable=true` was an oversight now removed. PRD decision **D11**,
[NOTES.md N14](NOTES.md).*

*Q9 — whether xunit v3 was viable — is withdrawn. All four test projects consume it and 315
tests run green; the 2.9.x fallback turned out to be moot rather than costless, because the
.NET 10 SDK has removed the VSTest bridge entirely. See [NOTES.md N11](NOTES.md).*

*Q4 — whether `Spark.Geometry.Io`'s inclusion in the CS1591 promotion was deliberate — is
**answered by precedent rather than by decision, and stays open until somebody says so.**
`Directory.Build.props` now applies the public-API baselines to the same four projects, which
means two independent mechanisms have converged on the same list. That is evidence the list is
right; it is not a record that anyone chose it.*

## Known and deliberately accepted

Not bugs. Recorded so nobody rediscovers them as surprises, or spends an afternoon
"fixing" a decision.

- **No Dynamo compatibility, in either direction.** No `.dyn` reader, no writer, no
  importer, no seam. A `.dyn` file contains no geometry, so reading one never needed
  ProtoGeometry — but **semantic equivalence** does, and that is unprovable without the
  very dependency Spark exists to remove. A silently mistranslating importer is worse than
  none. PRD decision **D8**. Corollary: the `By*` names carry **no** compatibility
  obligation; they exist for human recognition only.
- **Capability parity with Dynamo is tracked, and it does not reopen D8.** The two are about
  different things and will be confused repeatedly, so: D8 refused `.dyn` because *semantic
  equivalence* is unprovable without ProtoGeometry, and it stands. Capability parity is about
  *presence* — whether a Dynamo user reaches for something and finds it absent — which needs
  no reference implementation to check. [DYNAMO-COVERAGE.md](DYNAMO-COVERAGE.md) is the
  register: 51 ProtoGeometry types, 837 members, 92 reachable today. `Done` in it means
  *present and documented*, never *equivalent*, and the test that keeps it honest
  (`E11-T23`) must say so in its own failure messages.
- **Six curve types, and the exclusions are named on the types rather than discovered.** There
  is no `NurbsCurve` and no `Helix`; no closest-point query, split, curvature, planarity test
  or NURBS conversion on the curve contract; no offset, projection or pull; and **no value
  equality on curves**, because two curves drawing the same path through different
  parameterisations are a tolerance question rather than an `Equals` question, and answering it
  wrongly by default is worse than not answering it. `Curve.ClosestPoint` in particular waits on
  the ray caster and its BVH (`E2-T15`) rather than getting a second implementation.
- **A general affine transform of an ellipse is refused, not approximated.** `TransformedBy`
  accepts similarities. A shear does take an ellipse to an ellipse, but recovering the new axes
  from the mapped conjugate pair is Rytz's construction, and doing it approximately would return
  a curve that is quietly the wrong shape. A non-uniform scale on a `Circle` is refused for the
  same reason — the answer is an ellipse, and a `Circle` cannot hold one.
- **An undo reopens the document, so nothing about a canvas node survives it except its
  identity.** `CanvasNode` objects are new, slots renumber into the file's canonical order, and
  the selection is dropped ([N23](NOTES.md)). This is the price of undo being defined by the same
  writer that saves the file rather than by a second definition of what a document is, and the
  price is worth paying: the alternative drifts. Code that crosses an undo looks a node up by
  `NodeId`, never by slot.
- **The node canvas runs a real Gaussian blur per node between 81% and 83% zoom**, where the
  drop shadow crosses its threshold, costing 57→40 fps at 2,000 nodes. The design language
  already specifies the fix — a sprite cache keyed on a fixed set of blur radii, eight sprites
  in total. Scoped, specified, and not yet built (`E8`).
- **`concepts.evaluation` is a help topic id with no file behind it.** Five diagnostic codes
  resolve to it and `docs/help/concepts/evaluation.md` does not exist, so a user following an
  `SPK101x` code has nowhere to land. The docs harness does not currently check that a topic id
  names a real topic, which is why nobody noticed; both halves are worth fixing together
  (`E10`, `E11-T14`).
- **Coordinates are unitless.** No `UnitSystem`, no unit types, no conversion. Import and
  export assume the file's own units and document that they do. PRD decision **D12**. This
  does **not** remove scale-aware tolerance, which is numerical robustness rather than
  units and stays.
- **No drafting or annotation types, ever.** No dimensions, hatches, text, arrows, grids or
  spatial cells — none of them are concepts Dynamo has. C2VGeometry's versions are
  discarded outright rather than parked, because a parked type is a type someone eventually
  revives. PRD decision **D13**.
- **v1 releases are Windows only.** A signed Inno Setup installer plus a portable zip. An
  ubuntu build-and-test job stays in CI as a rot-guard, because it is nearly free and it is
  the only thing that stops cross-platform support rotting silently — but **no Linux or
  macOS artefact is published**, and macOS is not built at all. PRD decision **D14**.
- **Exact NURBS booleans, and fillet and chamfer on solids, are in 1.0 — and they come from
  OpenCascade.** This reverses what this bullet said, and the reversal is the point rather
  than an embarrassment: robust surface-surface intersection *is* a research-grade problem and
  is what makes commercial kernels cost millions, which is exactly why we are not writing one.
  `R1` **retires**. PRD **D2**, **D15**, [ADR-0020](adr/0020-occt-via-c-abi-shim.md).
- **Spark ships a heavyweight native dependency, and this is the thing to be honest about
  first.** Spark exists because Dynamo Sandbox forces users to have an Autodesk product
  installed, and because solving that by acquiring a different heavyweight dependency would
  move the problem rather than remove it. The distinction is real — **OCCT is open source,
  freely redistributable, installed *with* Spark, and needs no account, no licence purchase and
  no other vendor's product** — and it only holds if we say it first, clearly, in our own
  words. The README says it. `Spark.Geometry` stays pure managed and independently
  distributable, **NFR-5 is unchanged**, and OCCT ships in the default install because a Dynamo
  user finding booleans greyed out on first run is what FR-81 forbids. `R13`.
- **Robust mesh booleans move to 1.x. Reduced, not cancelled.** OCCT is **poor** at mesh
  booleans and Dynamo has them, so `E2-T27` keeps its purpose and loses only its urgency;
  `Capabilities` greys the operation until it lands. Do not read the deferral as a deletion.
- **The Linux CI job is no longer nearly free, and that was its entire justification.**
  ADR-0001 kept it as a rot-guard because it cost almost nothing; it must now build native
  code. The mitigation is a cached per-RID artefact keyed on
  `(occt-tag, vcpkg-baseline, shim-source-hash, rid)`, with the from-clean build nightly —
  **without it the rot-guard will not survive a busy PR queue**, and losing it would quietly
  convert `D1` into wasted effort. `E13-T15`.
- **`Brep` is no longer a pure value.** Under [ADR-0021](adr/0021-brep-kernel-residency.md)
  residency is **canonical, not cached**: after a kernel operation the provider's
  representation is authoritative and ours is materialised lazily. The reason is **fidelity,
  not speed** — a Spark→OCCT→Spark round trip is not identity, because OCCT carries tolerances
  we do not, `ShapeFix` may legitimately merge vertices and split faces at seams, and a face
  from an intersection may come back a B-spline where the input was a cylinder. Ten
  convert-in/convert-out operations would re-sew and re-tolerance the model ten times, and the
  user would watch their geometry drift while doing nothing. Consequences to accept, not fix:
  a finalizable native resource on a geometry value; equality and hashing defined on the
  materialised model and **never on the handle**; and **`NFR-4`'s cache must track a native
  budget reported by the shim**, because a managed size estimator cannot see OCCT's heap.
- **`NFR-8`'s watertightness property now tests a third party's mesher.** `Brep` tessellation
  moved behind the seam because tessellating a trimmed BRep face is genuinely hard and OCCT
  solves it — and OCCT's mesher is not guaranteed watertight at default deflection. Either the
  property holds at a deflection we choose or the requirement is restated to say what it
  guarantees. **It must not quietly become a suppressed test**, which is the failure mode this
  bullet exists to name. `E13-T11`.
- **There will be exactly one `IBrepKernel` provider, and a second is not planned.** The seam
  is retained for `Result<T>`, `Capabilities` and insurance. **Do not build a second provider
  to justify the abstraction.**
- **`AllowUnsafeBlocks` is `false` everywhere except `Spark.Geometry.Occt`.** The
  `LibraryImport` source generator emits unsafe code and will not run without it. The
  repository default stays `false`, the opt-in is in one csproj with a comment naming
  ADR-0020, and an architecture test asserts it is the only one. Likewise
  `SparkGeometryTakesNoThirdPartyDependencyBeyondClipper` **stays exactly as it is** and gains
  a *companion* rule for `Spark.Geometry.Occt`. **Relaxing either test would be the wrong
  repair**, and it will look like the obvious one.
- **Upgrading a package restarts the application by default.** An ALC is pinned by node
  definitions, compiled invokers, cached values, viewport buffers and undo history. Upgrade
  purges all of them, unloads, and verifies by weak reference — and when it does not
  unload, the UI **says so and offers restart**. Live unload is a best-effort optimisation,
  not a promise. Declaring this on day one avoids the entire "why is the old version still
  loaded" bug class. `R8`.
- **`StackOverflowException` kills the process.** It cannot be caught in .NET, and no
  amount of guard weaving changes that — weaving only reduces the frequency. Aggressive
  autosave and crash recovery limit the damage. The real fix is an opt-in out-of-process
  worker, kept viable by the scheduler and ALC seams and **deliberately deferred past
  v1**. `R11`.
- **A Spark graph is executable code.** Opening one from an untrusted source is equivalent
  to running an unknown program. .NET has no code-access security and Spark will not
  pretend otherwise. What actually works, and is what ships: opening never auto-runs
  (Manual mode plus a banner listing script nodes and required packages), a content-hash
  per-origin trust allowlist, and `spark run --no-script` for CI.
- **No telemetry of any kind in v1.** Not anonymous, not opt-out, not "just crash counts".
  Opt-in crash reporting is considered post-1.0, with graphs excluded from any payload.
- **Nothing here is published to nuget.org, and `IsPackable` is `false` everywhere.** Spark
  **consumes** NuGet packages and loose DLLs — that is a core feature and it is untouched —
  and **produces** none. Spark is an application, not a library ecosystem; the two audiences
  who compile against it, embedders and node authors, reference the assemblies from an
  install, which is how CAD add-ins are built anyway. So a project's assembly name is its
  only name, `Spark.Cli` builds `spark.exe` and ships beside the desktop application rather
  than as a dotnet global tool, and *published output* in these documents always means
  `dotnet publish`. Do not add packaging metadata to a `.csproj` because it looks like an
  omission — it is a decision. PRD decision **D11**, [NOTES.md N14](NOTES.md).
- **`Spark.Api` and `Spark.Geometry` are not strictly additive across 1.x**, which reverses
  what ADR-0009 said. That rule was built on a public package ecosystem that will not exist.
  What replaces it is proportionate deliberate change control: prefer adding, break only when
  it is genuinely better, and record it when you do. The real remaining cost of a break is
  that a user recompiles their own node DLL — an annoyance for one person, not an ecosystem
  fracture. Public-API baselines stay, as a **review aid rather than a compatibility
  guarantee**. **ADR-0019**, superseding ADR-0009.
- **`Auto` is not a synonym for `Longest`, and must not be "simplified" into one.** It is a
  sentinel meaning *use this node definition's `DefaultLacing`*, resolved to one of the four
  real modes before replication begins; it has no `n` and no output rank of its own. The
  plan's original reading — "`Longest`, but zero-excess inputs never iterate" — was
  overturned in review, because zero-excess inputs never iterate under *any* mode, which
  would have made `Auto` a menu entry that provably never did anything. Two nodes both set
  to `Auto` may therefore lace differently, and that is the feature. `lacing.md` §2.9 and
  its decision **D4**.
- **A test project containing no tests fails the run**, so test projects are created
  alongside the code they test rather than as empty stubs ahead of it. Three stubs were
  created and deleted before this was understood. It follows from Microsoft.Testing.Platform,
  which the .NET 10 SDK makes the only option — [NOTES.md N11](NOTES.md),
  [N12](NOTES.md).
- **`Spark.Nodes.Core` may not reference `Spark.Engine`,** and this will occasionally be
  inconvenient. That is the point: first-party nodes are forced through the same
  zero-config importer as third-party ones, so the importer cannot quietly special-case us
  and then fail for everyone else.
- **Warnings are errors in CI only, never in the csproj.** A red build on a stray unused
  variable mid-edit trains people to pass `-warnaserror:false`, which defeats the gate
  entirely. Local development stays pleasant; the gate stays absolute.
- **Clipper2 is an accepted third-party dependency of `Spark.Geometry`, but it is not
  referenced right now.** Its C# distribution is pure managed and Boost-licensed, and the
  version stays pinned exactly in `Directory.Packages.props`. The `PackageReference` was
  **removed** on 2026-08-27 because nothing in the value layer used it, and an unused
  reference costs restore time, licence surface and audit noise while making the reference
  graph lie about what the assembly needs. It returns with the planar boolean pipeline
  (`E2-T14`), isolated behind a single internal file as always. The architecture test asserts
  a **ceiling — no third-party dependency beyond Clipper2 — not an exact set**, so it holds
  on both sides of that round trip. Do not "restore" the reference because its absence looks
  like an omission. `R13`.
- **RS0026 is suppressed in `.editorconfig`, deliberately and with the reasoning recorded
  there.** It forbids two overloads of one name both carrying optional parameters, in order to
  prevent a future **source-breaking** change in a published library. Spark publishes nothing
  (**ADR-0019**), so that promise is one Spark no longer makes; the overloads it flagged —
  `Contains(point)` and `Contains(box)`, each with an optional tolerance — differ in a
  **required** parameter type, so every call site resolves unambiguously, and genuine
  ambiguity is caught by the compiler as CS0121 regardless. Mangling a good API to satisfy a
  rule aimed at a constraint we do not have is the tail wagging the dog. **RS0016 — undeclared
  public symbol — stays at its default error severity**, and that is the rule the baselines
  exist for. [NOTES.md N17](NOTES.md).
- **A `dotnet build` reporting "0 warnings" may be reusing a cached analysis.** This one is a
  genuine trap and it will catch somebody. Incremental builds can skip re-running analyzers
  over a project MSBuild considers up to date and still print a clean summary, so a warning
  that a fresh compilation would raise never appears. The public-API findings that led to the
  baselines were **invisible under a plain `dotnet build` and appeared the moment
  `--no-incremental` was added**. Before you believe a clean build — and always before
  claiming one in a document, a review or a commit message — run
  `dotnet build Spark.slnx --no-incremental -warnaserror`. "It built clean" is not a claim
  worth making about an incremental build. [NOTES.md N15](NOTES.md).
- **Private `const` fields are PascalCase, not `_underscored`.** CA1802 actively pushes code
  towards `const`, so requiring an underscore on private consts would have set two analyzers
  arguing with each other over the same field. The rule must sit **before** the underscore
  rule in `.editorconfig`, because naming rules are evaluated in file order and the first
  match wins. It sits beside the same rule for private `static readonly` fields, and the pair
  makes the underscore itself informative: it marks something that can change.
  [NOTES.md N16](NOTES.md).
- **A fix to the kernel is not finished until it is regression-proven by reverting it and
  naming the test that goes red.** Not "a test exists nearby", not "the suite is green" — the
  specific test, identified by having watched it fail. A fix with no test that notices its
  absence is a fix the next refactor silently undoes. This is the standard because its absence
  is exactly what let the kernel's first slice pass three gates while three of its eight
  claims were false. [NOTES.md N18](NOTES.md).
