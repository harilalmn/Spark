# Spark — TODO

What to do next, in priority order. Full context in [EPICS.md](EPICS.md), full inventory in
[TASKS.md](TASKS.md), the reasoning in [PRD.md](PRD.md).

**Last updated:** 2026-08-28

**M2 is complete.** There is an application: launch it and you get a shell with a library, a
node canvas, a 3D viewport and a properties panel, a demo graph that laces two ranges into a
hundred points and draws them, and a node deliberately given a divisor of zero so you can watch
an error stay where it belongs. That was the milestone at which anything became usable, and it
has arrived **before** M1 finished — the walking skeleton did not need curves, and waiting for
them would have deferred the five architectural questions M2 answers.

Three distinctions do the work in what follows, and all three are easy to blur:

- **The three gates genuinely pass, at commit `35107f0`, locally, on Windows, on 2026-08-28.**
  `dotnet build Spark.slnx --no-incremental -warnaserror` is clean with zero warnings;
  `dotnet test Spark.slnx` runs **821 tests across seven projects**; `dotnet format Spark.slnx
  --verify-no-changes --severity warn` is clean.
- **CI has still not run on any of it.** `.github/workflows/ci.yml` has been green on Windows
  and Linux for M0 commits and has seen neither the kernel nor the engine nor the UI. Linux is
  where the surprises live — and [NOTES.md N19](NOTES.md) is a reminder that they run in the
  other direction too: the shader defect that broke the first Windows run would have passed on
  Linux.
- **Gates are not review, and this project has its own proof.** The kernel's first slice
  passed all three and was **rejected**, with three of its eight claims false and both guarding
  tests structurally incapable of failing. What came out of it is now standing policy: every new
  subsystem is accepted on a **mutation sweep** — break it in small plausible ways and name the
  test that goes red for each. The graph engine was accepted on 33, the walking skeleton on 30,
  all killed ([NOTES.md N18](NOTES.md), [N23](NOTES.md)).

**The kernel is still values only.** There are no curves, surfaces, meshes or BRep types, and
nothing below should be read as implying otherwise. **Nothing of OCCT is built** — no `native/`
directory, no `Spark.Geometry.Occt` project, no OCCT anywhere in the tree — and the decision
recorded in [ADR-0020](adr/0020-occt-via-c-abi-shim.md) and
[ADR-0021](adr/0021-brep-kernel-residency.md) is unchanged.

## Now — the four things that are wrong, and the gate that would have caught them

Every item in this section was found by **executing** code that three green gates had already
passed. That is the shape of the section, and it is deliberate.

- [ ] **Fix the importer crash on `Spark.Geometry`** — `E5-T17`. `NodeImporter.Import` throws
      `NotSupportedException: Cannot create boxed ByRef-like values` on
      `BoundingBox.FromPoints(ReadOnlySpan<Point3d>)`, so **the geometry kernel cannot be
      imported as nodes at all** — the other eleven value types produce 176 nodes and
      `BoundingBox` takes the assembly down with them. It also breaks the importer's own
      contract, *every public member is either a node or an exclusion carrying a reason*, with a
      member that is neither. This blocks `E5-T14`, which is the whole point of having a kernel.
      **Fix it by adding a `ref struct` exclusion**, not by deleting the overload.
- [ ] **Fix the mangled node tooltips** — `E5-T18`. `<paramref>` is dropped rather than
      substituted, so `Number.Range` currently reads *"A list of numbers from up to , stepping
      by . is included when the step lands on it."* The author wrote a correct sentence and the
      reader is shown a broken one, which is worse than showing nothing. One node today; every
      node author who writes a `<paramref>` tomorrow.
- [ ] **Apply registered converters at run time** — `E3-T23`. A wire accepted through a
      registered converter or a reflected `implicit operator` is validated at design time,
      warned about if lossy, and then **fails at the leaf with `SPK1041`**. A warning the engine
      does not honour is worse than a refusal. Reproduced end to end.
- [ ] **Build the shadow sprite cache** — `E8-T18`. Between 81% and 83% zoom, at identical node
      counts, frame rate falls **57 → 40 fps**, exactly where the drop shadow crosses its
      threshold and Avalonia starts running a real Gaussian per node. The design language §3
      already specifies the fix and it is small: nodes have one width and about four heights, so
      **the cache holds eight sprites**.
- [ ] **Make the canvas benchmark a nightly gate** — `E8-T15`, `E11-T16`. This is the item that
      makes the previous one impossible to reintroduce. The harness exists and prints median,
      p95 and fps; nothing records a threshold, `bench/` is still an empty directory, and
      BenchmarkDotNet is still pinned and unreferenced. **The two numbers this project now
      argues from — the 2,000-node timings and the shadow cliff — live in a console line and a
      commit message.**

## Next — close the documentation gates that are now overdue

All three were deliberately not stubbed, on the grounds that a test which passes by doing
nothing is worse than no test. The things they check now exist, so the grounds have gone.

- [ ] **Compile every fenced sample in `docs/help/`** — `E11-T24`. Until this exists, *every
      example was run against the assembly* is a claim held up by whoever last wrote it. It has
      been true twice; it will not stay true by itself.
- [ ] **Write worked example graphs into `docs/examples/`** — `E10-T7`. Still an empty
      directory, and now the largest gap in the documentation strategy: for a node-graph tool an
      executed graph is the strongest anti-rot mechanism available, and none is executed.
- [ ] **Assert every `SPK####` code resolves to a topic that exists** — `E11-T26`. Twelve codes
      and two topics, so it is cheap **now**, which is exactly when to add it.
- [ ] **Fail the build for a node family with no help topic** — `E11-T25`. Eight families, none
      documented.
- [ ] **Get CI green on GitHub** — `E1-T14` … `E1-T18`. Push, open a pull request, watch all
      three jobs. Everything verified so far was verified on Windows, and `docs-freshness` is
      `pull_request`-only so it cannot run until there is a PR.
- [ ] **Give `Spark.Host` a test project** — `E11-T27`. `SparkSession` holds the edit gate,
      in-flight cancellation and the run semaphore — the three pieces of genuinely concurrent
      code in the product — and no test project references it.

## Then — finish M1, the geometry core

**Done:**

- [x] Value types, `Transform`, `Plane`, `CoordinateSystem`, `Tolerance`, `Angle` — `E2-T2`
      … `E2-T6`, with `E2-T1` short only its `Quaternion`. Thirteen types, 387
      public members, reviewed and repaired. Scale-aware tolerance was built in from the
      first commit rather than retrofitted, which is the whole mitigation for `R3`.
- [x] Property-based tests with CsCheck on the value layer — 28 properties, generators
      spanning 1e-9 to 1e9 per **ADR-0018**. `E2-T33` stays open for the criteria that need
      curves and meshes.

**Still to do, in rough order:**

- [ ] `Quaternion` — `E2-T1`. **`Rgba` is settled and is no longer in scope here:** it now
      lives in `Spark.Api` beside `Appearance` and `Displayable`, which is where a type that
      knows about colour belongs. The kernel carries no styling and no screen awareness.
- [ ] `Line`, `Arc`, `Circle`, `EllipseCurve`, `PolyLine`, `PolyCurve` — `E2-T7` …
      `E2-T9`. Harvest `VArc`'s eight construction algorithms; they are fiddly, correct
      and costly to recreate. The value layer they sit on is settled, and **the graph engine
      and viewport that will consume them now exist**, which turns this from a leap into a
      next step.
- [ ] Extract `RayCaster.cs` and its BVH — `E2-T15`. The highest-value file in
      C2VGeometry, and it pays for itself three times over: mesh booleans, viewport
      picking, intersection seeding.
- [ ] Geometry serialization v1 and the reflection-driven round-trip test — `E2-T29`,
      `E2-T31`. Get the test in before there are twenty types to retrofit it onto.
- [ ] **The C2VGeometry test harvest, timeboxed to one week with a hard stop** — `E2-T32`.
      `R10` is that this sprawls into a multi-week rewrite. Harvest only pure-maths-on-values
      tests; anything needing a `Shape` is discarded without argument. **Harvest the
      assertions, not the generators** — a harvested test whose inputs never approach the
      boundary it checks is a test that cannot fail, which is the trap this project has
      already fallen into once.
- [ ] **Close the three small parity gaps in the value layer** — `E2-T40`. `BoundingBox.Intersection`,
      `Plane.Offset` and `Plane.ByOriginNormalXAxis` are omissions rather than design
      differences, found by reading Spark's public surface against ProtoGeometry's member by
      member ([DYNAMO-COVERAGE §3.1](DYNAMO-COVERAGE.md#31-values-and-frames--6-types-133-members-92-reachable)).
      Cheap now, and each one is a member somebody will otherwise hit at M3.
- [ ] **Settle the curve contract against the parity register before writing curves** —
      `E2-T41`. FR-48 names fifteen members; `Curve` in ProtoGeometry has **82**, and the gap
      is structural rather than incidental: four parameterisations of every evaluation query
      and a ten-member division family, all of which fall out of arc-length
      reparameterisation. Cheap to build in, expensive to retrofit — which is exactly why it
      belongs in the contract rather than after it.
- [ ] `spark run` writes an OBJ polyline that a third-party viewer opens — `E12-T5`,
      `E12-T19`. That is the M1 demo, and `Spark.Cli` currently has no behaviour at all.

## After that — M2's tail, then M1.6

M2 is complete in the sense that matters — the walking skeleton walks — but three things it was
scoped to include are not built, and they are the difference between a demo and a tool.

- [ ] **Save, load, undo and redo** — `E3-T17`, `E3-T18`, `E8-T9`. **A graph cannot currently
      be saved.** Undo is the one that is nearly free: the provenance cache already makes
      reverting an edit cost nothing, verified by execution, so what is missing is the command
      stack rather than any performance work.
- [ ] **Library search ranking** — `E8-T8`. The panel filters; the camel-hump ranking that
      makes it usable across thousands of nodes does not exist.
- [ ] **Real docking** — `E8-T2`. The serialisable layout model is done and is the part that
      carries over; the `Grid` and `GridSplitter`s are not.
- [ ] **The M1.6 OCCT spike, against criteria written down beforehand** — `E13-T1`. It answers
      four of the seven things [ADR-0020](adr/0020-occt-via-c-abi-shim.md) records as open, and
      **it is the only place they can be answered** — the rest of that list needs counsel or a
      publisher, not a build.

**M1.5 is withdrawn as a milestone, and that is a result rather than a cancellation.** Its three
spikes existed to answer three questions before committing to an architecture. Two are now
answered by shipped code rather than by throwaway code: the **GL viewport initialises and draws**
(`E11-T19`), and the **immediate-mode canvas holds 2,000 nodes at 0.87 ms median / 2.26 ms p95**
(`E11-T20`). The third — **AvaloniaEdit with a Roslyn completion popup** (`E11-T21`) — is
unanswered and still gates M4. It should be spiked before `E6` starts, rather than before a
milestone that has already happened.

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
| **Q1** | **Two-thirds answered, and both answers are yes.** The GL viewport initialises and draws, verified by reading the framebuffer back; the immediate-mode canvas holds 2,000 nodes at 0.87 ms median / 2.26 ms p95. Neither needed a throwaway spike in the end — both were answered by the shipped code, which is a better outcome than the plan asked for. **The third is untouched:** is AvaloniaEdit plus a Roslyn completion popup acceptable to use? Completion-popup placement and focus are where AvalonEdit and AvaloniaEdit diverge most, and nothing in this repository has tested it. | `E11-T21`, and M4 |
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

*Q9 — whether xunit v3 was viable — is withdrawn. All **seven** test projects consume it and
**821** tests run green; the 2.9.x fallback turned out to be moot rather than costless, because
the .NET 10 SDK has removed the VSTest bridge entirely. The one real cost has surfaced since
and is not xunit's fault: `Avalonia.Headless.XUnit`'s `[AvaloniaFact]` is built against
xunit.v3 3.2.2 and fails at **discovery** under 4.0.0, so headless UI tests drive the session
directly. See [NOTES.md N11](NOTES.md) and [N21](NOTES.md).*

*Q4 — whether `Spark.Geometry.Io`'s inclusion in the CS1591 promotion was deliberate — is
**answered by precedent rather than by decision, and stays open until somebody says so.**
`Directory.Build.props` applies the public-API baselines to the same four **contract** projects
— `Spark.Api`, `Spark.Geometry`, `Spark.Geometry.Io` and `Spark.Nodes.Core` — so two independent
mechanisms have converged on the same list. That is evidence the list is right; it is not a
record that anyone chose it. Two of the four now carry real surface: `Spark.Api` declares 104
public members and `Spark.Nodes.Core` 36, both fully documented because CS1591-as-error leaves
no alternative.*

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
- **The importer excludes nine categories of member, each with a stated reason, and that is
  the design rather than a shortfall.** Generics, extension methods, operators, nested types,
  indexers, events, `ref` and `in` parameters, write-only properties, and `void` methods with
  no `out` parameter are all named on the exclusion they produce. **The importer never skips a
  member silently**, because a silent skip passes every test written after the fact — which is
  why the coverage test can run in both directions from one import. Adding a category is a
  design decision of its own (how does a user pick a type argument on a canvas?), not a gap to
  be quietly closed. `E5-T3`.
- **Preview policy is terminal ports only.** Geometry appears in the viewport for nodes whose
  output feeds nothing else. A node in the middle of a chain does not draw and there is no
  per-node preview toggle. Do not read a mid-chain node showing nothing as a bug.
- **A point renders as a solid octahedron, not a screen-space disc.** Eight faces, sized at
  1.2% of the scene diagonal, so it reads as a dot from any direction — but it is world-space,
  and points shrink as you zoom out. The design language §8.3 asks for a 5 px disc; the reason
  it was not built is that the mesh path carries no per-vertex orientation, so a billboard
  needs a geometry stage or a dedicated point shader. `E9-T13`.
- **Literal editing is in the properties panel, not on the node.** Select a node and its
  unwired input ports appear as editable rows; a wired port is not editable, because the wire
  wins. In-canvas editing is a later slice, not an omission.
- **One `Appearance` per `RenderPackage`, so per-element selection is not expressible.** A
  diagnostic can already name element `[3][1]` and the viewport has no way to highlight it.
  This is the gap between the identity tuple the design promises — `(NodeId, PortIndex,
  ElementPath)` — and the two-thirds of it the renderer keys on. `E9-T14`.
- **The evaluation cache evicts by entry count, not by bytes**, and `ADR-0021` requires bytes.
  A thousand points and a thousand meshes weigh the same to it. The count is a bound that
  stops the cache growing without pretending to be a memory budget, and it is labelled as
  crude in the code rather than presented as the design. When OCCT arrives the estimate must
  come **from the shim**, because a managed estimator cannot see OCCT's heap. `E3-T9`.
- **Avalonia on Windows is ANGLE, so the viewport runs on OpenGL ES 3.0 over Direct3D 11 —
  never desktop GL 3.3.** Write shaders for GLSL ES first. A desktop-GL-only shader compiles
  on a Linux development machine and fails on the platform Spark ships to, and a shader that
  fails to compile produces a blank viewport rather than an error anyone associates with a
  shader. This does not change `ADR-0014`; it changes what *an OpenGL viewport* means in
  practice. [NOTES.md N19](NOTES.md).
- **`[AvaloniaFact]` is not used and adding it back would break the build.** It is compiled
  against xunit.v3 3.2.2 and fails at **discovery** under 4.0.0. Headless UI tests drive
  `HeadlessUnitTestSession` directly under a plain `[Fact]`, which is the better shape
  independently of the bug: it makes the Avalonia lifetime explicit in the test rather than
  hidden in an attribute. [NOTES.md N21](NOTES.md).
- **`dotnet test` reporting "Zero tests ran" is build contention, not a broken suite.** Two
  agents have hit it; the suite runs correctly at the same commit every time and CI has been
  green throughout. The cause is a concurrent build holding a lock on an output file. Check
  nothing else is building, then re-run. A genuinely disabled suite does not intermittently
  pass. [NOTES.md N22](NOTES.md).
- **A new subsystem is accepted on a mutation sweep, not on a green suite.** Break the
  implementation in small, individually plausible ways and name the test that goes red for
  each one. A survivor is a missing test and it is written before the sweep is finished. The
  count is not a target: thirty mutations that each probe a different decision are worth more
  than a hundred that probe the same loop. [NOTES.md N23](NOTES.md).
- **A fix to the kernel is not finished until it is regression-proven by reverting it and
  naming the test that goes red.** Not "a test exists nearby", not "the suite is green" — the
  specific test, identified by having watched it fail. A fix with no test that notices its
  absence is a fix the next refactor silently undoes. This is the standard because its absence
  is exactly what let the kernel's first slice pass three gates while three of its eight
  claims were false. [NOTES.md N18](NOTES.md).
