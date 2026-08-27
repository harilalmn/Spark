# Spark — TODO

What to do next, in priority order. Full context in [EPICS.md](EPICS.md), full inventory in
[TASKS.md](TASKS.md), the reasoning in [PRD.md](PRD.md).

**Last updated:** 2026-08-27

Spark is at M0, and most of M0 has landed. The solution, twelve project stubs, the reference
graph, the build properties, these documents, eighteen ADRs, the lacing specification, the CI
workflow, and two test projects that pass all exist. Of the previous version of this
paragraph — *"there is no CI, no test project and no implementation code of any kind"* — only
the last clause survived, and it is now going too: the first M1 kernel value types began
landing in `src/Spark.Geometry` as this was written. They are not reflected in the statuses
in [TASKS.md](TASKS.md), which describe M0 and were checked on 2026-08-27.

Two distinctions do the work in what follows, and both are easy to blur:

- **The architecture and documentation tests genuinely pass.** `dotnet test Spark.slnx` runs
  eleven tests and they are real checks against the repository, run locally on 2026-08-27.
- **CI has never run.** `.github/workflows/ci.yml` is written and committed and nobody has
  seen it execute. A workflow that has not run is a workflow with unknown YAML, unknown
  runner behaviour and unknown Linux results. It is not a gate yet; it is a file.

---

## Now — finish M0 before anything else

M0 is *foundations*, and its whole value is that the gates exist before the code does.
Everything in this section is cheap now and expensive later.

- [ ] **Get CI to run, and green, on GitHub** — `E1-T14` … `E1-T18`. Push, open a pull
      request, watch all three jobs. The Linux leg is where surprises live: everything
      verified so far was verified on Windows. Doing this before any code exists is the
      point — a gate added later is a gate that gets an exemption for everything that
      already exists. The Linux job is a rot-guard, not a release target — see **D14**.
- [ ] **Write the three remaining agent definitions** — `E1-T30`. `ui-shell`, `viewport` and
      `reviewer`. Needed by M2, which is the first milestone to touch any of the three areas
      they own. File ownership must stay disjoint, so parallel agents never conflict.
- [ ] **Check in the public-API baselines** — `E1-T23`. `Spark.Api` and `Spark.Geometry`
      cannot be side-by-sided, so they must be strictly additive across all of 1.x. The
      baseline is how that becomes a reviewed line in a text file instead of a hope. Cheapest
      to add now, while both surfaces are empty.
- [ ] **Create `bench/Spark.Benchmarks`** — `E1-T13`. `bench/` and `scripts/` are still empty
      directories. BenchmarkDotNet is pinned and unreferenced.
- [ ] **Settle whether `Spark.Host` publishes** — `E12-T17`, `Q10`. It became `IsPackable`
      without a `PackageId` or a comment, which would publish it as `Spark.Host` — an ID
      nobody has checked and one every document said would not be published.
- [ ] **Reserve the NuGet IDs** — `E1-T24`. **Blocked on the client**, not on us: it needs
      the account that will own them. `Spark.Api`, `Spark.Geometry`, `Spark.Graph`,
      `Spark.Nodes`, `Spark.Scripting`, `Spark.Docs`, `Spark.Tool`. Four already went:
      `Spark`, `Spark.Core`, `Spark.Engine`, `Spark.Cli`. This is the only M0 item with
      somebody else's clock on it — an unclaimed ID is one a stranger can take tomorrow.

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

- [ ] Value types, `Transform`, `Plane`, `CoordinateSystem`, `Tolerance`, `Angle` —
      `E2-T1` … `E2-T6`. Scale-aware tolerance is built in from the start, never
      retrofitted: that is the whole mitigation for `R3`.
- [ ] `Line`, `Arc`, `Circle`, `EllipseCurve`, `PolyLine`, `PolyCurve` — `E2-T7` …
      `E2-T9`. Harvest `VArc`'s eight construction algorithms; they are fiddly, correct
      and costly to recreate.
- [ ] Extract `RayCaster.cs` and its BVH — `E2-T15`. The highest-value file in
      C2VGeometry, and it pays for itself three times over: mesh booleans, viewport
      picking, intersection seeding.
- [ ] Geometry serialization v1 and the reflection-driven round-trip test — `E2-T29`,
      `E2-T31`. Get the test in before there are twenty types to retrofit it onto.
- [ ] Property-based tests with CsCheck — `E2-T33`. From M1, non-negotiable.
- [ ] **The C2VGeometry test harvest, timeboxed to one week with a hard stop** — `E2-T32`.
      `R10` is that this sprawls into a multi-week rewrite. Harvest only pure-maths-on-values
      tests; anything needing a `Shape` is discarded without argument.
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
| Q2 | Is `Spark.Geometry.Io` free on nuget.org? It is `IsPackable` but is not among the IDs **D11** records as checked. | `E1-T24` |
| Q3 | **Half answered.** `Spark.Graph` now maps to `Spark.Engine`, which became `IsPackable` with that `PackageId`. `Spark.Docs` still maps to nothing: defensive reservation, or a project missing from the layout? | `E1-T24` |
| Q4 | `Directory.Build.props` promotes CS1591 to an error on **four** projects; the plan named three. Is `Spark.Geometry.Io` deliberately included? | `E10-T8` scope |
| Q5 | Revit or AutoCAD as the M8 embedding proof host? The scheduler is the same either way; the add-in shell, licensing and test loop are not. | `E12-T4` |
| Q6 | If `R1` forces the OCCT fallback, does an *optional* OCCT-backed package breach the no-native-dependencies promise? `E7-T8` already discloses native binaries, which suggests it can be lived with. | M6 |
| Q7 | Which public STEP corpus is authoritative, and which third-party viewer is the reference? | `E2-T36` |
| Q8 | Where does the website live, and who maintains it? | `E10-T14` |
| Q10 | `Spark.Host` became `IsPackable` with no `PackageId` and no comment, so it would publish as `Spark.Host` — an ID nobody has checked, for a project every document says is not published. Deliberate, or an oversight beside `Spark.Engine`'s deliberate rename? | `E12-T17` |

*Q9 — whether xunit v3 was viable — is withdrawn. Both test projects consume it and eleven
tests run green; the 2.9.x fallback turned out to be moot rather than costless, because the
.NET 10 SDK has removed the VSTest bridge entirely. See [NOTES.md N11](NOTES.md).*

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
- **`Spark.Engine`, `Spark.Cli`, `Spark.Core` and `Spark` are taken on nuget.org, and we
  are not renaming.** Only *package* IDs must be unique; project and assembly names are
  unaffected. So the assembly name and the package ID are allowed to differ, three times
  over: `Spark.Cli` publishes as **`Spark.Tool`** with the command `spark`,
  `Spark.Nodes.Core` publishes as `Spark.Nodes`, and `Spark.Engine` publishes as
  **`Spark.Graph`**. The one ID that genuinely had to be public, `Spark.Api`, is free. PRD
  decision **D11**.
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
- **Clipper2 is a third-party dependency of `Spark.Geometry`, and that is fine.** Its C#
  distribution is pure managed and Boost-licensed. It is pinned exactly, isolated behind a
  single internal file, and a CI check asserts no native binaries appear in the published
  output — so the no-native-dependencies promise stays checkable rather than merely
  asserted. `R13`.
