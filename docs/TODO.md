# Spark — TODO

What to do next, in priority order. Full context in [EPICS.md](EPICS.md), full inventory in
[TASKS.md](TASKS.md), the reasoning in [PRD.md](PRD.md).

**Last updated:** 2026-08-27

M0 has essentially landed and **M1 has started**. The solution, twelve project stubs, the
reference graph, the build properties, these documents, nineteen ADRs, the lacing
specification, the CI workflow, the public-API baselines, and four test projects that pass all
exist. So does the first slice of the geometry kernel: `src/Spark.Geometry` holds thirteen
value types, declares 387 public members, and is covered by 304 of the 315 tests in the
solution.

Three distinctions do the work in what follows, and all three are easy to blur:

- **The three gates genuinely pass, locally, on Windows, on 2026-08-27.**
  `dotnet build Spark.slnx --no-incremental -warnaserror` is clean over sixteen projects;
  `dotnet test Spark.slnx` runs **315 tests** across four projects; `dotnet format Spark.slnx
  --verify-no-changes --severity warn` is clean.
- **CI has not run on this commit.** `.github/workflows/ci.yml` has been green on Windows and
  Linux for earlier commits, and has seen nothing of the geometry kernel. Linux is where the
  surprises live, and none of the above is a Linux result.
- **Gates are not review, and this project now has its own proof.** The first attempt at the
  kernel's value layer passed all three gates and was **rejected**: an independent review
  found three of its eight claims false, including a `default(Plane)` on which every point in
  space silently lay. The two tests guarding that type were structurally incapable of failing.
  See *Known and deliberately accepted* below, and [NOTES.md N18](NOTES.md).

**The kernel is values only.** There are no curves, surfaces, meshes or BRep types, and
nothing below should be read as implying otherwise.

---

## Now — get the gates in front of real code

M0 is *foundations*, and its whole value is that the gates exist before the code does. There
is now real code for them to be in front of, which makes the first item below more urgent
than it was, not less.

- [ ] **Get CI green on GitHub against the kernel** — `E1-T14` … `E1-T18`. Push, open a pull
      request, watch all three jobs. **The Linux leg has seen none of the geometry kernel**,
      and floating-point results, culture-dependent formatting and case-sensitive paths are
      exactly the things that differ. Everything verified so far was verified on Windows. The
      Linux job is a rot-guard, not a release target — see **D14**. `docs-freshness` is
      `pull_request`-only and cannot run until there is a PR, so opening one closes two items
      at once.
- [ ] **Write the three remaining agent definitions** — `E1-T30`. `ui-shell`, `viewport` and
      `reviewer`. Needed by M2, which is the first milestone to touch any of the three areas
      they own. File ownership must stay disjoint, so parallel agents never conflict.
      **`reviewer` has stopped being a formality:** the kernel's first slice passed all three
      gates and was rejected on review, and nothing in the repository currently describes how
      that review is meant to be conducted.
- [ ] **Add the no-native-binaries CI check** — `E1-T20`. It was blocked on there being a
      published output to inspect. `Spark.Geometry` now builds a real assembly and, having
      shed its unused Clipper2 reference (`E2-T39`), references nothing but the BCL — so the
      check is trivially satisfiable today, which is precisely when a gate should be added.
- [ ] **Create `bench/Spark.Benchmarks`** — `E1-T13`. `bench/` and `scripts/` are still empty
      directories. BenchmarkDotNet is pinned and unreferenced.

**Two items left this section rather than being done**, and both were about publishing to
nuget.org: *settle whether `Spark.Host` publishes* (`E12-T17`, `Q10`) and *reserve the NuGet
IDs* (`E1-T24`, which was the only M0 item with an outside clock on it). Both are
**withdrawn**. Spark consumes NuGet packages and publishes none — PRD decision **D11** — so
`IsPackable` is now `false` for every project and there is nothing to reserve, rename or
reconcile. M0 lost a blocker rather than gaining one.

## Next — name the M1.5 criteria, before M1 starts

Small, and deliberately its own step rather than a bullet inside M0, because the whole value
of it is the *order*.

- [ ] **Write the three M1.5 pass/fail criteria into TASKS.md** — `E11-T19`, `E11-T20`,
      `E11-T21`. What counts as a pass for a shaded lit triangle on Windows *and* Linux; what
      counts as 60 fps over 2000 synthetic nodes and for how long; what counts as an
      acceptable AvaloniaEdit completion popup. Written down in advance is what makes the
      gate honest; written down afterwards is what makes it a rationalisation. A failed
      criterion changes the architecture, which is the entire point of spending the week.

## Then — M1, the geometry core

**Done, and the reason this section is shorter than it was:**

- [x] Value types, `Transform`, `Plane`, `CoordinateSystem`, `Tolerance`, `Angle` — `E2-T2`
      … `E2-T6`, with `E2-T1` short only its `Quaternion`. Thirteen types, 387
      public members, reviewed and repaired. Scale-aware tolerance was built in from the
      first commit rather than retrofitted, which is the whole mitigation for `R3`.
- [x] Property-based tests with CsCheck on the value layer — 28 properties, generators
      spanning 1e-9 to 1e9 per **ADR-0018**. `E2-T33` stays open for the criteria that need
      curves and meshes.

**Still to do, in rough order:**

- [ ] `Quaternion` — `E2-T1`. **`Rgba` is settled and no longer in scope here:** the kernel
      carries no styling, no screen awareness and no appearance, so a colour type belongs
      beside `Appearance` in `Spark.Api` (`E5`). It was listed as a geometry value type in
      the original plan, on the same page as the rule forbidding exactly that.
- [ ] `Line`, `Arc`, `Circle`, `EllipseCurve`, `PolyLine`, `PolyCurve` — `E2-T7` …
      `E2-T9`. Harvest `VArc`'s eight construction algorithms; they are fiddly, correct
      and costly to recreate. The value layer they sit on is now settled, which is what
      makes this the next thing rather than a parallel thing.
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
- [ ] `spark` writes an OBJ polyline that a third-party viewer opens. That is the M1 demo.

## After that — M1.5 and M2, the walking skeleton

M1.5 is a week of throwaway spikes, deleted afterwards. M2 is the highest-information
milestone in the project: it simultaneously validates Avalonia GL, the canvas rendering
strategy, the reflection importer, the lacing engine and the layering split — the five
things that could still force an architectural change.

- [ ] The three M1.5 spikes, against criteria written down beforehand — `E11-T19`,
      `E11-T20`, `E11-T21`.
- [ ] Graph model, topological evaluation, provenance cache — `E3-T1` … `E3-T8`.
- [ ] **The full replication engine against the lacing specification** — `E4-T2` …
      `E4-T12`. Lacing is folded into M2 rather than deferred, because a graph engine
      without replication is a toy to an AEC user, and retrofitting rank semantics into a
      shipped evaluator is far more expensive than building them in.
- [ ] The zero-config reflection importer over `Spark.Geometry` — `E5-T2` … `E5-T5`.
- [ ] Avalonia shell, docking, `GraphCanvas` with drag, wire, pan, zoom, select, delete —
      `E8-T1` … `E8-T6`.
- [ ] Library search with camel-hump ranking — `E8-T8`.
- [ ] GL viewport for points, lines and curves — `E9-T1` … `E9-T6`.
- [ ] Save, load, undo, redo — `E3-T17`, `E3-T18`, `E8-T9`.
- [ ] `spark run` — `E12-T5`.
- [ ] **Manual acceptance: launch it, drag two nodes, wire them, see geometry in the
      viewport — and watch it lace over lists.** That is the whole point, end to end.

**Deliberately excluded from M2:** code blocks, packages, surfaces, custom nodes. Naming
the exclusions is what keeps a walking skeleton from becoming a death march.

## Later — M3 onward

- [ ] `NurbsCurve` complete, curve intersection, offset, fillet; `Planar` regions and 2D
      boolean; List/Math/String/Logic categories; watch nodes and preview bubbles — M3.
- [ ] The C# code block: the RCS/CADScript/DoodleSharp port campaign, input inference with
      wire-typed IntelliSense, tuple-named outputs, resident and persistent compile caches,
      guard weaving — M4, `E6`. **Demo this publicly when it lands** — it is the signature
      differentiator.
- [ ] Surfaces, `NurbsSurface`, `Mesh`, tessellation and `RenderPackage` streaming, shaded
      viewport, OBJ/STL/PLY read and write, glTF write, the software renderer and CI visual
      regression — M5, plus the throwaway SSI spike (`E2-T37`) while it is still cheap to
      learn SSI is hard.
- [ ] BRep, modelling operations, `IBrepKernel` with `Capabilities` gating the UI, and the
      robust mesh boolean — M6, `E2-T22` … `E2-T28`.
- [ ] Packages, per-package-version ALCs, missing-package placeholders, the trust store,
      local DLLs with hot reload, custom nodes, groups, notes, freeze — M7, `E7`.
- [ ] `Spark.Host` proven inside a real Revit or AutoCAD add-in; STEP; the Windows
      installer; the website; performance and accessibility passes — M8, `E12`. **1.0.**

## Decisions waiting on someone

| # | Question | Blocks |
|---|---|---|
| Q1 | Do the three M1.5 spikes pass? A failure changes the architecture, which is what they are for. | M2 design |
| Q4 | `Directory.Build.props` promotes CS1591 to an error on **four** projects; the plan named three. Is `Spark.Geometry.Io` deliberately included? | `E10-T8` scope |
| Q5 | Revit or AutoCAD as the M8 embedding proof host? The scheduler is the same either way; the add-in shell, licensing and test loop are not. | `E12-T4` |
| Q6 | If `R1` forces the OCCT fallback, does an *optional* OCCT-backed package breach the no-native-dependencies promise? `E7-T8` already discloses native binaries, which suggests it can be lived with. | M6 |
| Q7 | Which public STEP corpus is authoritative, and which third-party viewer is the reference? | `E2-T36` |
| Q8 | Where does the website live, and who maintains it? | `E10-T14` |

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
- **Exact NURBS booleans, and fillet and chamfer on solids, are post-1.0.** Robust
  surface-surface intersection with tangential and degenerate cases is a research-grade
  problem and is what makes commercial kernels cost millions. 1.0 ships on **mesh
  booleans** with `IBrepKernel` documented as the extension point, and this is stated
  publicly rather than discovered by a disappointed user. `R1`.
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
