# Spark — Epics

Thirteen epics. Each has a goal, a scope boundary, acceptance criteria and a status.
Individual tasks live in [TASKS.md](TASKS.md); what to do next is in [TODO.md](TODO.md);
the requirements they serve are in [PRD.md](PRD.md).

**Last updated:** 2026-08-28

No product code has yet been reviewed as landed, though the first M1 kernel value types
began appearing in `src/Spark.Geometry` as this revision was written and are not reflected
below. Three epics are partly done — foundations, documentation and now
verification — and everything else is `Not started`. Epics are derived from the milestone
plan; the milestone each mostly serves is named, but epics and milestones are not the same
axis and several epics span both.

**A criterion is ticked when something demonstrates it, never when code exists that would
satisfy it.** The CI workflow is the live example: it is written, committed and has never
run on GitHub, so every criterion that depends on CI is untouched. The architecture and
documentation tests are the other side of the same rule — they pass locally, so what they
prove is ticked.

| Epic | Title | Milestones | Status |
|---|---|---|---|
| [E1](#e1--foundations-build-and-ci) | Foundations, build and CI | M0 | Partly done |
| [E2](#e2--geometry-kernel) | Geometry kernel | M1, M3, M5, M6, M8 | Partly done |
| [E3](#e3--graph-engine) | Graph engine | M2 | Partly done |
| [E4](#e4--replication-and-lacing) | Replication and lacing | M2 | Partly done |
| [E5](#e5--node-authoring-and-library) | Node authoring and library | M2, M3 | Partly done |
| [E6](#e6--c-code-block) | C# code block | M4 | Not started |
| [E7](#e7--packages-and-extensibility) | Packages and extensibility | M7 | Not started |
| [E8](#e8--ui-shell-and-node-canvas) | UI shell and node canvas | M2 | Partly done |
| [E9](#e9--3d-viewport) | 3D viewport | M2, M5 | Partly done |
| [E10](#e10--documentation) | Documentation | M0 onwards | Partly done |
| [E11](#e11--quality-and-verification) | Quality and verification | M0 onwards | Partly done |
| [E12](#e12--embedding-and-release) | Embedding and release | M8 | Not started |
| [E13](#e13--occt-provider) | OCCT provider | M1.6, M6, M8 | Not started |

**E13 is new and it is the largest single change to this plan since it was written.** The
client chose to take an existing solid-modelling kernel rather than write one — **D2 reverses**
— and [ADR-0020](adr/0020-occt-via-c-abi-shim.md) records the choice of OpenCascade reached
through a C-ABI shim we own. Read E13 and E2 together: **E2 loses its hardest work and E13
gains harder work of a different kind.**

---

## E1 — Foundations, build and CI

**Goal.** A repository whose build, layering and gates are correct *before* there is any
code in it, so none of them ever has to be retrofitted.

**In scope.** The solution and its twelve project stubs, the reference graph, build and
package properties, `.editorconfig`, licence, git metadata, versioning, the CI matrix and
every CI job, public-API baselines, and the agent definitions.

**Out of scope.** Anything a user sees. Anything under `docs/` — that is [E10](#e10--documentation).
The content of the test projects — that is [E11](#e11--quality-and-verification); this epic
only creates them and wires them into CI.

**Why the order matters.** The docs harness and the architecture tests are build gates from
M0, before there is anything for them to check. A gate added later is a gate that gets an
exemption for everything that already exists.

**Acceptance criteria**

- [x] `Spark.slnx` exists with all twelve `src/` projects, every one `net10.0`.
- [x] The reference graph matches the layout: `Spark.Api` sees only the BCL and
      `Spark.Geometry`; `Spark.Nodes.Core` never references `Spark.Engine`;
      `Spark.Viewport` references no Avalonia package.
- [x] `Directory.Build.props` sets one TFM, nullable on, implicit usings off, unsafe off,
      warnings-as-errors **not** in the csproj, and XML documentation generation on.
- [x] `Directory.Packages.props` pins every package version exactly, with no floating
      ranges anywhere.
- [x] `.editorconfig` exists and is deliberately small.
- [x] MIT `LICENSE`, `.gitignore` and `.gitattributes` are in place, with `.spark` and
      `.sparkcustom` marked text so graphs diff in git.
- [x] MinVer derives the version from git tags, SemVer, tag prefix `v`.
- [x] CS1591 is an error on the contract projects — undocumented public API does not build.
- [x] `dotnet build Spark.slnx -warnaserror` is clean with zero warnings on Windows.
- [ ] Every test project that has something to test exists and is in the solution, and
      `bench/Spark.Benchmarks` exists (**E1-T12**, **E1-T13**). *Two test projects exist and
      pass; `bench/` is still empty. The original "nine projects up front" wording is
      withdrawn — a test project with no tests fails the run under Microsoft.Testing.Platform,
      so they are created alongside the code they test.*
- [x] CI runs on `windows-latest` and `ubuntu-latest` and is green on the empty solution
      (**E1-T14**). *Written; never run.*
- [ ] CI jobs: build `-warnaserror` → test → format check → docs-verify → docs-freshness →
      headless UI smoke (**E1-T15** … **E1-T19**). *Four of the six are written — the docs
      harness needs no job of its own, since it is a test project the `test` step already
      runs. The headless UI smoke has nothing to smoke until `Spark.UI` exists. None has run.*
- [x] A CI check asserts no native binaries appear in `Spark.Geometry`'s published output
      (**E1-T20**). *`dotnet publish` output — nothing here is packaged for nuget.org.*
      `scripts/check-no-native-binaries.sh`, run on both operating systems, and **proven to
      fire**: pointed at `Spark.Desktop` it fails on the Skia and HarfBuzz natives Avalonia
      brings.
- [ ] Benchmarks run nightly, not per-PR, **gating bytes allocated per operation against a
      committed `bench/baseline.json`** (**E1-T21**). *Written; it has never run, so it is not
      ticked.* **The criterion as originally worded — "results committed as a git time series" —
      is deliberately not met**: that needs `contents: write` on a scheduled job and a permanent
      automated writer on `main`, where 90-day artifacts carry the same numbers and a
      human-updated baseline carries the durable part
      ([ADR-0023](adr/0023-benchmarks-gate-allocation-not-time.md)).
- [ ] The release workflow refuses to publish when the computed version and the tag
      disagree (**E1-T22**).
- [x] Public-API baselines for `Spark.Api` and `Spark.Geometry` are checked in, so every
      change to the public surface is a reviewed line in a text file (**E1-T23**). *A review
      aid, not a compatibility guarantee — ADR-0019. Delivered wider than asked: all four
      contract projects carry a `PublicAPI.Shipped.txt` and a `PublicAPI.Unshipped.txt`,
      referenced from `Directory.Build.props`. `Spark.Geometry` declares 387 members; the
      other three are empty. RS0016 stays at error and **was proved to fire** by adding a
      public member and watching the build fail; RS0026 is suppressed with its reasoning
      recorded, because it protects a source-compatibility promise ADR-0019 retired
      ([NOTES.md N17](NOTES.md)).*
- [x] `IsPackable` is `false` for every project and no packaging metadata exists anywhere, so
      the repository cannot publish to nuget.org by accident (**E1-T24**, withdrawn).
      *Reserving NuGet IDs is withdrawn rather than done: **D11** settles that Spark consumes
      packages and publishes none.*
- [x] Every ADR citation in a build file points at the record it claims to (**E1-T29**).
      *The two that did not are now ADR-0017 and ADR-0018, both written and indexed. The
      docs harness checks every citation in build files and source comments, not just in
      Markdown — but only that the record **exists**. A citation naming a real record about
      something else, which is what these two were, is a review matter.*
- [x] The SDK and the test runner are pinned in `global.json`, so every machine and CI
      agent builds with the same toolchain (**E1-T31**).
- [x] The eight agent definitions in `.claude/agents/` exist with disjoint file ownership
      (**E1-T25**, **E1-T30**). *Five of eight.*

**Status.** Partly done, and further along than the milestone plan expected at this point.
The solution, the twelve stubs, the reference graph, the build properties, the pinned package
versions, the licence and the git metadata all exist; the solution builds clean with
`--no-incremental -warnaserror`; `global.json` pins the toolchain; the public-API baselines
are live on all four contract projects; **four test projects run 315 checks**; and
`.github/workflows/ci.yml` and five of the eight agent definitions are written.

**What is not true: CI has run against this.** It has been green on Windows and Linux for
earlier commits and has seen nothing of the geometry kernel — which is the half of the
solution most likely to behave differently on Linux. `bench/` and `scripts/` are still empty
directories and three of the eight agent definitions are unwritten, `reviewer` among them.
Until CI is green on GitHub against the current tree, M0 is not finished.

**One trap this epic owns and everybody should know about.** A `dotnet build` that reports
`0 Warning(s)` may have skipped the analysis entirely: MSBuild's incremental build skips
projects it considers up to date, analyzers run inside the compilation it skipped, and the
summary still prints zero. The public-API findings that produced the baselines were invisible
until `--no-incremental` was added. Verify a clean build with the flag or do not claim one —
[NOTES.md N15](NOTES.md).

---

## E2 — Geometry kernel

**Goal.** A 3D BRep/NURBS geometry model good enough to model with, in which **`Spark.Geometry`
itself is pure managed and ships no native binaries**, and which depends on no commercial CAD
product.

**The scoping of that sentence changed and the change is deliberate.** It used to attach *ships
no native binaries* to the product. It now attaches it to **the assembly**. Under
[ADR-0020](adr/0020-occt-via-c-abi-shim.md) the product ships OpenCascade in its default
install; `Spark.Geometry` does not, stays independently distributable, and **NFR-5's CI
assertion is untouched**. Saying which of the two the promise is about is the whole of the
correction, and leaving it ambiguous is how a promise becomes a broken one.

**In scope.** `Spark.Geometry` and `Spark.Geometry.Io`: value types, tolerance, curves,
surfaces, BRep topology, meshes, the planar supporting layer, mesh tessellation, ray casting,
the `IBrepKernel` seam's managed side, native serialization and interchange formats.

**Out of scope.** Nodes over the geometry — that is [E5](#e5--node-authoring-and-library).
Rendering it — that is [E9](#e9--3d-viewport). Anything with identity, style or screen
awareness: geometry has none of the three, by design. **And, new under ADR-0020: everything
behind the seam** — exact booleans, trim, fillet, chamfer, shell, thicken, draft, extrude,
revolve, loft, sweep, sew, heal, validate, BRep tessellation and STEP. That is
[E13](#e13--occt-provider). The managed implementations of all of them are **discarded, not
descoped**.

**What is being salvaged, and what is not.** `C2VGeometry` is a 2D *drawing* library, not
a kernel. Every `Shape` constructor auto-registers into a global mutable static registry;
`Shape` carries styling, z-order and eight animation fields; every shape ignores Z;
`VTransform` has no matrix, composition or inverse; `VSpline` is Catmull-Rom rather than
NURBS. Copy-and-rename is not available. What is genuinely worth taking: `VXYZ`'s
algorithms, `VPlane` and `VCoordinateSystem`, `GeometryTolerance`'s ~25 helper bodies,
`VArc`'s eight construction algorithms, the planar boolean/offset/simplify pipeline, and
above all **`RayCaster.cs`** with its BVH — the single highest-value file, because it
serves mesh booleans, viewport picking and intersection seeding alike.

**Acceptance criteria**

- [x] Values are readonly structs implementing `IEquatable<T>` and passed by `in`
      (**E2-T1** … **E2-T6**). *Twelve types, 387 public members, checked member by member
      against `PublicAPI.Unshipped.txt`. `operator ==` is exact on every one of them and
      `EqualsWithin(other, in Tolerance)` is the separate, explicit geometric comparison; a
      fuzzy `==` was deliberately rejected because it breaks hashing and transitivity.
      `Quaternion` is still unwritten, which is why **E2-T1** is not `Done` (`Rgba` has moved to **E5**, being a display concern the kernel must not carry) —
      but the criterion is about the shape of a value type, and every value type that exists
      has it.*
- [ ] Curves, surfaces, meshes and BReps are sealed and immutable, with backing state never
      handed out. Mutable **builders** are the only mutable things and never escape into
      the graph. Lazy internal caches are permitted: immutability is observable, not
      bitwise.
- [x] `Tolerance` is explicit and passed, never ambient, defaults per call via
      `in Tolerance tol = default`, and is scale-aware (**E2-T4**). *There is no static,
      thread-local or document-scoped default anywhere in the assembly. `ForScale` and
      `Scaled` derive one from a characteristic length, floored at 1e-15 so a derived value
      can never collapse back into the "use the default" sentinel. `AreEqual`, `IsLessThan`
      and `IsGreaterThan` form a genuine three-way partition because all three compare the
      same subtraction against the same threshold — the version that did not was caught in
      review, not by a gate.*
- [x] `Angle` appears in **every** public angular signature, with no implicit conversion
      from `double` (**E2-T5**). *Verified against the public-API baseline rather than by
      recollection: `Vector3d.Rotate`, `AngleTo`, `SignedAngleTo`, `Vector2d.Rotate` and
      `Transform.Rotation` all take or return `Angle`, and `Tolerance.Angular` is one.*
- [ ] Analytic surfaces are first-class, not NURBS in disguise (**E2-T18**).
- [ ] BRep topology is index-based — arrays and int indices, no object references — with
      `readonly ref struct` navigator views for ergonomics (**E2-T22**, **E2-T23**).
- [ ] Every operation behind `IBrepKernel` returns `Result<T>` carrying diagnostics and
      partial results; kernel failure is diagnosable, never thrown (**E2-T28**). *Unchanged by
      ADR-0020, and more load-bearing than before: the failures are now OCCT's (**R18**), and
      `Result<T>` was designed before anyone knew whose they would be.*
- [ ] A `Capabilities` flag set lets the node library grey out unsupported operations
      instead of throwing — this is what makes staged delivery honest (**E2-T28**). *What it
      greys out has inverted: most of what it was designed to expose arrives on day one, and
      what is absent at 1.0 is **mesh** booleans.*
- [ ] **Residency is canonical, not cached** ([ADR-0021](adr/0021-brep-kernel-residency.md)).
      Exactly two crossings, `Import` and `Materialise`; a ten-operation chain performs zero
      imports and one materialisation; round-trip asserts **tolerance-bounded equivalence,
      never identity**; only `Spark.Geometry.Occt` observes the token (**E2-T28**, **E13**).
- [ ] Mesh booleans are robust, pure managed, built on the ported BVH plus
      adaptive-precision exact predicates (**E2-T27**). *Moved to **1.x** by ADR-0020, with
      `Capabilities` greying it meanwhile. Reduced, not eliminated: OCCT is poor at mesh
      booleans and Dynamo has them.*
- [ ] Serialization carries **per-type `schemaVersion`**, so a `NurbsCurve` at v2 and a
      `Mesh` at v1 coexist, with migrations applied JSON-to-JSON (**E2-T29**).
- [ ] A reflection-driven round-trip test enumerates every concrete geometry type, so a new
      type that forgets serialization **fails the build** (**E2-T31**, [E11-T9](#e11--quality-and-verification)).
- [ ] Property-based tests from M1: `T.Inverse().Inverse() == T`; union volume ≥ max input
      volume; `Split(t)` rejoined equals the original; **tessellation of a closed solid is
      watertight**; `ClosestPoint` never farther than any sampled point (**E2-T33**).
- [ ] The C2VGeometry test harvest is **timeboxed to one week with a hard stop**; anything
      needing a `Shape` is discarded without argument (**E2-T32**).
- [ ] Clipper2 stays isolated behind a single internal file, and CI asserts no native
      binaries in the published output ([E1-T20](#e1--foundations-build-and-ci)). *Not
      referenced at all at present: the `PackageReference` came out on 2026-08-27 once it
      proved unused (**E2-T39**), so `Spark.Geometry` is on the BCL alone, and it returns
      with the planar pipeline (**E2-T14**). The architecture test that guards this now
      asserts a **ceiling rather than an exact set**, which is what lets it hold on both sides
      of that round trip. The CI check itself is still unwritten, so this stays unticked.*
- [ ] OBJ, STL and PLY read and write; glTF write (**E2-T34**, **E2-T35**). *These stay ours
      and must work in a build with no native component at all — M1's demoable is `spark`
      writing an OBJ polyline.*
- [ ] ~~STEP AP203/AP214 read and write over a documented subset~~ — **withdrawn to
      [E13-T12](#e13--occt-provider)** (**E2-T36**). OCCT gives AP203, AP214 and AP242 plus
      IGES, and **R12 retires**. The validation rule survives verbatim: a public corpus and a
      third-party viewer, **never our own reader**.
- [ ] No drafting or annotation types exist anywhere in the kernel (**D13**).

**Status.** Two slices are landed. The **value layer** — `Angle`, `Tolerance`, `Point3d`,
`Vector3d`, `Point2d`, `Vector2d`, `UV`, `Interval`, `BoundingBox`, `Plane`, `Transform` and
`CoordinateSystem`, plus an internal `NamespaceDoc` carrying the conventions the whole namespace
obeys — was reviewed, repaired and accepted. The **curve layer** followed: a `Curve` base whose
constructor is `private protected`, so the set of curve types is closed to the assembly, with
`Line`, `Arc`, `Circle`, `EllipseCurve`, `PolyLine` and `PolyCurve` over it. Everything is
documented, because CS1591 is an error here, and everything is recorded in
`PublicAPI.Unshipped.txt`. Coverage is `tests/Spark.Geometry.Tests` (313 example-based tests)
and `tests/Spark.Geometry.Properties` (38 CsCheck properties), both green.

**What the curve layer settled, and it was settled before it was written.** The contract came
from [DYNAMO-COVERAGE §3.2](DYNAMO-COVERAGE.md#32-curves--11-types-187-members-partially-reachable)
rather than from FR-48, because that section had found the gap between them to be structural
rather than incidental: **arc-length reparameterisation is in the contract**, so *divide this
curve into twelve equal lengths* is a first-class operation rather than a retrofit. It is
analytic on the five constant-speed types and a ten-point Gauss–Legendre integral with a Newton
inverse on the ellipse — which is also the only curve here whose tests can tell an arc-length
division from a parameter division, since every other one travels at a constant speed.

**Still not started in this epic.** No surfaces, no meshes, no BRep topology, no `IBrepKernel`,
no serialization and no interchange. `Spark.Geometry.Io` is still an empty project. No
`NurbsCurve`, and no closest-point, split, offset or intersection on the curves that exist —
each named on its type rather than left to be discovered.

**How the slice was accepted matters more than that it was.** The first attempt passed
`build -warnaserror`, `test` and `format`, and was **rejected**: an independent review found
three of its eight claims false, the worst being a `default(Plane)` that reported every point
in space as lying on it, silently. Both tests guarding the tolerance partition were
structurally incapable of failing — the property drew two independent uniforms, and
simulating its generator gave zero violations in five million draws against the hundred
CsCheck performs per run, where a generator straddling the threshold finds 908 in 12,006
pairs. NaN handling in `Interval.Includes` and `BoundingBox.Intersects`, a sign flip in
`SignedAngleTo` where the cross product underflows near 1e-170, an `ArgumentException` naming
a parameter absent from its own signature, and four over-claiming round-trip doc comments
were repaired in the same pass. **Every fix was regression-proven by reverting it and naming
the test that goes red**, and that is now the standard this epic works to.
[NOTES.md N18](NOTES.md) carries the detail.

---

## E3 — Graph engine

**Goal.** A dataflow evaluator that is correct, cancellable, incremental and never hangs —
and a file format that a user can diff, merge and share on GitHub.

**In scope.** `Spark.Engine` and the contracts in `Spark.Api` it depends on: the graph data
model, wire type compatibility, topological evaluation, caching, scheduling, cancellation,
run modes, diagnostics, serialization and migrations.

**Out of scope.** Replication — that is [E4](#e4--replication-and-lacing), separated because
it is the part most likely to be got subtly wrong. Where node definitions come from — that
is [E5](#e5--node-authoring-and-library). Anything drawn on screen.

**Acceptance criteria**

- [x] The data model is `Graph`, `NodeInstance`, `Wire`, `NodeDefinition` and
      `PortDefinition`, with `NodeId` a stable `Guid` that is never reused (**E3-T1**).
- [x] `DefinitionKey` includes **package identity**, not just a name — otherwise version
      conflicts become silent misbindings (**E3-T2**).
- [x] `Invoke` is an expression-tree-compiled delegate, never `MethodInfo.Invoke`
      (**E3-T3**).
- [x] Type compatibility is evaluated in the documented order, and anything unmatched is
      **refused at wire-creation time**, never at run time (**E3-T4**).
- [x] Two ports whose types share a `FullName` but come from different assemblies are
      refused with both package identities named — turning an incomprehensible runtime
      *cannot cast Foo to Foo* into a design-time message (**E3-T5**).
- [x] Evaluation is a Kahn topological sort over the dirty subgraph only, producing levels,
      parallel within a level (**E3-T6**).
- [x] Cycles are refused at wire creation with the closing path flashed, and detected at
      load — where every node in the cycle errors and the rest of the graph still
      evaluates. **Evaluation never hangs** (**E3-T7**).
- [x] Caching is content-addressed by **provenance**, not by value; hashing a 2M-triangle
      mesh costs more than recomputing it (**E3-T8**).
- [x] Undo, redo, A/B wire toggling and slider reverts are instant, because the old cache
      key is still resident (**E3-T8**). **Met, and measured rather than asserted:** the undo
      stack (`E8-T9`) now exercises it, and the run that follows an undo recomputes **zero**
      nodes and serves every one of them from the cache.
- [ ] Impure nodes declare themselves, mix a run epoch into their key, and poison
      downstream keys (**E3-T10**).
- [ ] The cache is LRU against a memory budget, evicted by last use and estimated size
      (**E3-T9**).
- [ ] `IEvaluationScheduler` has parallel, sequential-deterministic and host-thread
      implementations, and evaluation never runs on the UI thread (**E3-T11**).
- [ ] Cancellation is checked between nodes, between replication elements and inside long
      kernel loops; cancelling leaves completed nodes cached (**E3-T12**).
- [x] `.spark` is plain canonically formatted JSON — stable key order, invariant numbers —
      and save/load round-trips **byte-identically** (**E3-T17**, **E3-T18**).
- [ ] `graph.formatVersion` is a single monotonic integer decoupled from product version;
      migrations are JSON-to-JSON, never against typed models, are never deleted, and each
      ships with a golden-file test against a real old graph (**E3-T19**).
- [x] Errors do not cascade: downstream of a failed node is greyed as *not evaluated*
      (**E3-T16**).
- [x] Every `SPK####` diagnostic code carries a `HelpTopicId` (**E3-T15**).
- [ ] Document tolerance flows through `EvaluationContext` and is **hashed into every
      node's cache key**, so changing it invalidates exactly the affected nodes
      (**E3-T22**).

**Status.** Substantially built in `7ef0919`, and **evaluated by walking the source tree rather
than the commit message**. The graph model, package-qualified node identity, expression-tree
compiled invocation, the wire-compatibility rules with same-name refusal, Kahn ordering over the
dirty subgraph, cycle refusal at wire creation and detection at load, the provenance cache, the
`SPK####` diagnostic space and the non-cascading error rule are all in.

**Four things are half-built, and the halves that are missing are named** rather than left to be
discovered: the cache evicts by **entry count** rather than by a byte budget (`E3-T9`); the run
epoch is plumbed but **no node can declare itself impure**, because the attribute does not exist
(`E3-T10`); two of the three schedulers exist and the **host-thread** one — most of the reason
the seam exists — does not (`E3-T11`); and cancellation reaches between nodes and between
replication elements but **not inside a kernel operation**, none of which takes a token
(`E3-T12`). Run modes and the progress channel are untouched.

**Persistence landed after this paragraph was first written.** A graph is saved and opened as
canonical JSON — `SparkFile` for the text, `GraphDocument` for the seam between the file and a
live `Graph` — and read-then-write is asserted byte-identical, which is the property
[ADR-0017](adr/0017-spark-file-is-plain-json.md) chose text in order to have.
`docs/examples/curves.spark` is committed as a golden file. `graph.formatVersion` exists and a
newer file is refused whole; the JSON-to-JSON migration path is not written, because there is one
version and nothing to migrate from (`E3-T19`).

---

## E4 — Replication and lacing

**Goal.** Get rank semantics right the first time, because graphs saved against wrong
lacing cannot be fixed later without breaking them.

**In scope.** `SparkList`, rank computation, the lacing modes, multi-output transpose,
per-element failure isolation, the replication opt-out attributes, and the case-table
specification that is written before any of it.

**Out of scope.** Evaluation ordering and caching — that is [E3](#e3--graph-engine).

**Why it is its own epic.** This is [R4](PRD.md#12-risks): lacing semantics that get subtly
wrong become unfixable, because by the time anyone notices, real graphs depend on the wrong
behaviour. The countermeasure is that **the specification is written first, as a help
topic, and consumed directly as the test corpus.** Using documentation as the design
instrument is deliberate, not a flourish.

**Acceptance criteria**

- [x] `docs/help/concepts/lacing.md` with the full case table exists **before any
      replication code is written** (**E4-T1**).
- [x] The table crosses declared rank (0/1/2) × actual rank (excess −1 to +2) × input count
      (1–3) × length relationship (equal, shorter, length-1, empty) × mode, plus promotion,
      empty-list propagation, null passthrough, ragged nesting, multi-output transpose,
      three-way cross product, and the two opt-out attributes (**E4-T1**).
- [x] Each row asserts the expected value **and the expected rank separately** — rank bugs
      are precisely the ones that survive value-only tests (**E4-T12**).
- [x] `SparkList` is a first-class engine type, not `List<object>` and not raw
      `IEnumerable`, so rank is O(1) and unambiguous (**E4-T2**).
- [ ] `SparkList` marshalling to and from declared collection types carries a standing
      benchmark (**E4-T3**).
- [x] `excess(i) = rank(actual) − declaredRank(i)`, `depth = max excess`; at `depth > 0`
      replicate **one level and recurse**. There is no flatten-then-reshape anywhere
      (**E4-T4**, **E4-T5**).
- [x] Inputs with zero excess broadcast unchanged; negative excess promotes a scalar into a
      one-element list (**E4-T8**).
- [x] `Shortest`, `Longest`, `CrossProduct` and `Disabled` all behave as specified, with
      `CrossProduct` raising rank by *k* rather than 1 — 10 × 10 yields a 10×10 nested list,
      not a flat 100 (**E4-T6**, **E4-T7**).
- [x] **`Auto` resolves to the node definition's `DefaultLacing` before replication begins**
      and is never itself a replication algorithm. Two nodes both set to `Auto` may lace
      differently; `DefaultLacing` may not itself be `Auto`; a definition declaring no
      default gets `Longest` (**E4-T6**, `lacing.md` §2.9 and its decision **D4**).
- [x] `Disabled` is available on every node, and is the default for inherently rank-1 nodes
      such as `List.Count` (**E4-T6**).
- [x] Multi-output nodes replicate in lockstep then transpose: `(area, centroid)` over five
      items gives two lists of five, not one list of five tuples (**E4-T9**).
- [x] Per-element failure is isolated: element 37 of 500 throwing leaves the other 499
      evaluated, slot 37 `null`, and the node emitting a **Warning** naming the failing
      indices — not an Error (**E4-T10**).
- [x] The fast path runs uncaught until the first failure and then restarts with catching
      enabled, so the happy path pays nothing (**E4-T10**).
- [x] Every row of the case table passes (**E4-T12**). *Not a fixed count: case numbers are
      stable and never reused, and the table is expected to grow as the engine finds
      situations the document did not anticipate.*

**Status.** Built in `7ef0919`, as a direct transcription of the specification written before
it — which was the entire point of doing it in that order. Excess and depth, replicate-one-level
and recurse, promotion at the leaf, Cross Product nesting by *k* rather than by one, multi-output
lockstep-then-transpose, per-element isolation with an uncaught fast path and a catching replay,
and `Auto` as a sentinel resolving to the definition's default rather than as an algorithm.

**The corpus test is a two-way diff against the specification document itself**, parsing its case
numbers and failing if corpus and document name different sets in either direction. It found two
errors in the document within its first run: cases 29 and 30 expected `[11,22,32]` where
repeating the short input's last element gives 23, and case 45 and the worked example both
already said 23. A digit transposition sitting in the corpus that everything downstream would
have been tested against.

**One row is short:** `E4-T3` has the marshalling, the benchmark and now a nightly that runs it,
and is short only because that nightly **has never run**. What guards this path is its *allocation*
ceiling rather than its time, which suits the row exactly: the finding that made it a standing
guard was that the return path allocates roughly six times the argument path at 100 000 elements,
and that figure is now a ceiling in `bench/baseline.json`
([ADR-0023](adr/0023-benchmarks-gate-allocation-not-time.md)).

Writing it has already paid for itself. It settled ten questions the plan left open
(decisions D1–D10 in its §2.16) and **overturned one answer the plan had wrong**: `Auto` was
specified as "`Longest`, but inputs with excess 0 never iterate", which under the rank model
is not a distinction at all — zero-excess inputs never iterate under any mode — so `Auto`
would have shipped as a menu entry that provably never differed from `Longest`. It is now a
sentinel resolving to the node definition's `DefaultLacing`. That defect was found by
writing prose, before a line of engine code existed. No implementation exists.

---

## E5 — Node authoring and library

**Goal.** Any .NET assembly becomes a usable node library with no attributes, no plugin and
no kernel expertise — and the first-party library goes through exactly the same door.

**In scope.** The reflection importer, the authoring attributes, the member-kind rules,
overload and façade handling, XML description ingestion, the node categories, and
`Spark.Nodes.Core` itself.

**Out of scope.** How nodes evaluate — [E3](#e3--graph-engine) and [E4](#e4--replication-and-lacing).
Where third-party assemblies come from — [E7](#e7--packages-and-extensibility).

**The rule that keeps this honest.** `Spark.Nodes.Core` **never references
`Spark.Engine`**. First-party nodes are forced through the same zero-config importer as
third-party ones, so the importer cannot quietly special-case us and then fail for
everybody else. This is enforced by `Spark.Architecture.Tests`, not by discipline.

**Acceptance criteria**

- [ ] Importing a well-known third-party NuGet package with **no Spark attributes at all**
      produces a sane node count with no crashes — acceptance-tested in CI (**E5-T11**).
- [x] `[SparkNode]`, `[NodePort]`, `[NodeIgnore]` and the replication attributes refine what
      reflection infers, for those who want them (**E5-T1**).
- [ ] Member-kind rules are implemented as specified: setters excluded, `out` parameters
      become extra outputs, `Task<T>` is awaited, `void` is excluded unless marked a side
      effect, `op_*` operators are excluded as nodes and harvested as implicit conversions
      instead, and extension methods present as instance methods on the extended type so
      package extensions look native (**E5-T3**, **E5-T9**).
- [ ] **One node per overload**, grouped under one library entry with a flyout,
      disambiguated by differing parameter names (`ByCenterRadius` versus
      `ByCenterRadiusNormal`), never by `_2` (**E5-T4**). **Half met: the importer produces one
      node per overload and disambiguates by parameter names; the library panel does not group
      them under one entry with a flyout, so they list separately.**
- [x] A `By*`/`From*`/`Create*` static on type `T` returning `T` whose parameter type
      sequence matches a constructor's suppresses that constructor. Anything a factory does
      not cover still emits its constructor, so nothing becomes unreachable (**E5-T5**).
- [x] A **two-way test**: every public member is reachable as exactly one node or is listed
      in an exclusions file with a reason, and every node resolves to a live member
      (**E5-T6**).
- [x] Descriptions come from the assembly's sidecar XML file, so any library shipping its
      `.xml` gets tooltips free (**E5-T7**).
- [x] An `Angle` parameter renders as a degree-valued port automatically, for first-party
      and third-party libraries alike — the typed hook that bare doubles could not provide
      (**E5-T8**).
- [ ] `Spark.Nodes.Core` covers geometry, and adds curated List, Math, String and Logic
      categories; a curated `Math` category serves arithmetic in place of operator nodes
      (**E5-T12**, **E5-T13**, **E5-T14**).
- [x] `Appearance` and `Displayable` live in `Spark.Api`, not the kernel, and a
      `Display.ByGeometryColor` node wraps. Unwrapped geometry renders with defaults, so
      `Spark.Geometry` stays usable entirely on its own, with no notion of colour and no
      reference to anything above it (**E5-T15**).

**Status.** Built in `35107f0`. **57 nodes reach the library with no registration anywhere** — no
partial class, no dictionary, no attribute required — and `Spark.Nodes.Core` still holds no
reference to `Spark.Engine`, so the first-party library is imported by exactly the path a
third-party package would take.

**The two-way diff is the part that matters, and it was in place before the importer could
rot.** Every public member is reachable as exactly one node or is excluded **with a stated
reason**; every node resolves to a live member. Generics, extension methods, operators, nested
types, indexers, events and `ref` parameters are all excluded *with reasons* rather than
silently skipped, which is why `E5-T9` and `E5-T10` are `Open` as decisions rather than as gaps.
This is the DoodleSharp failure the project has been designing against since M0: three
hand-maintained dictionaries that drifted in **both** directions at once, invisibly, for years.

Still open: extension methods on their receiver, generics, the third-party import acceptance
test, and the curated List/Math/String/Logic categories. `E5-T14` is `In progress` at 57 nodes,
which is a number rather than a finish line.

---

## E6 — C# code block

**Goal.** Type C# into a node and get IntelliSense that knows the type on the incoming
wire. This is the single most compelling thing Spark can demo that Dynamo cannot.

**In scope.** `Spark.Scripting`: the rewriter and source maps, the reference catalog,
script load contexts, guard weaving, completion, the compile caches, and the two node types
that sit on top — an inline Code Block and a docked C# Script Node, over one pipeline.

**Out of scope.** The editor *control* and its host window — that is
[E8](#e8--ui-shell-and-node-canvas). Package resolution — that is
[E7](#e7--packages-and-extensibility).

**What is being ported.** `ScriptRewriter` and `SourceMap`, `ReferenceCatalog` (the biggest
single time-saver), `ScriptLoadContext`, `GuardWeaver` and the editor controllers from RCS;
`CompletionEngine`'s completion-must-match-the-compiler invariant, `ScriptTextRepair` and
`ScriptRunner`'s threading model from CADScript; the resident-assembly cache from
DoodleSharp **along with its warning** that callback registries must be cleared before
unload, because delegates into user code pin the collectible context. AvalonEdit to
AvaloniaEdit is mechanical **except completion-popup placement and focus**, where the two
diverge most; rework is budgeted there specifically.

**Acceptance criteria**

- [ ] Input ports are inferred **semantically** — compile once against the prelude, collect
      `CS0103`/`CS0117`, take the identifiers in source order (**E6-T5**).
- [ ] Port identity is the variable name, so reordering usages does not rewire (**E6-T5**).
- [ ] Once a port is connected, the rewriter injects a typed local rather than `object`
      (**E6-T6**).
- [ ] **IntelliSense inside the code block resolves members of the upstream wire's type**
      (**E6-T7**).
- [ ] Output ports come from a named tuple return; a plain final expression gives one
      `result` port (**E6-T8**).
- [ ] Compilation is cached on `Hash(normalizedText, inputPortTypes, referenceCatalogVersion,
      langVersion)`, **resident** so a slider feeding a code block feels live, and
      **persistent on disk** so reopening a file does not pay Roslyn cold start. Identical
      text in ten nodes compiles once (**E6-T9**, **E6-T10**).
- [ ] Guard weaving bounds loop iterations and recursion depth, and a deliberately infinite
      loop is cancelled rather than hanging (**E6-T4**, **E6-T17**).
- [ ] Callback registries are cleared before unload, because delegates into user code pin
      the collectible context (**E6-T15**).
- [ ] Opening a graph never auto-runs it: Manual mode plus a banner listing script nodes and
      required packages, with a content-hash per-origin trust allowlist, and
      `spark run --no-script` for CI (**E6-T16**).
- [ ] A graph containing no script nodes never loads `Spark.Scripting` (**E6-T14**).

**Status.** Not started. M1.5 spike (c) — AvaloniaEdit plus a Roslyn completion popup that
is acceptable to use — is a go/no-go gate on this epic's approach
([E11-T21](#e11--quality-and-verification)).

---

## E7 — Packages and extensibility

**Goal.** Install a package from nuget.org and use its nodes. Open a graph missing a package
and lose nothing.

**In scope.** `Spark.Packages`: the NuGet client, install and resolution, per-package-version
load contexts, the trust store, local DLL references with hot reload, the package manager
UI's behaviour, and the graph-level features that hang off the same mechanism — custom
nodes, groups, notes and freeze.

**Out of scope.** Building a registry. NuGet is the registry: protocol, hosting, auth,
SemVer, dependency resolution, private feeds and nuget.org reach all come free. NuGet only
*acquires files*; side-by-side is solved at the load layer, which is the part we build.

**Acceptance criteria**

- [ ] A Spark package is a NuGet package tagged `spark` with a `tools/spark.json` manifest
      (**E7-T1**).
- [ ] **One collectible ALC per package *version*** — not per package, which kills
      side-by-side, and not per assembly, which kills intra-package type identity
      (**E7-T3**).
- [ ] The `Load` override decides by **file existence in the context's own folder**, not by
      a hardcoded name list, which demonstrably rots the moment a package adds a dependency
      (**E7-T3**).
- [ ] **Contract assemblies always resolve from the default context**, because a `Circle`
      from package A must be the same `Type` as one from package B or nothing can be wired
      (**E7-T4**).
- [ ] Upgrade purges node definitions, compiled invokers, cached values, viewport buffers
      and undo history, unloads, and **verifies by weak reference** — and when it does not
      unload, the UI says so and offers restart. **Restart is the documented default**
      (**E7-T5**).
- [ ] A graph referencing a missing package opens with placeholder nodes preserving the
      definition key, every literal and every wire **verbatim**, and **re-saves
      byte-identically** (**E7-T6**, **E7-T7**).
- [ ] Install shows publisher, downloads, licence, signature status, transitive
      dependencies, node count, and **whether the package contains native binaries** —
      users deserve to know when the no-native-dependencies promise is being broken on
      their behalf (**E7-T8**).
- [ ] Local DLLs prompt once and record a content hash; a changed hash re-prompts.
      Auto-reload on file change is offered, and reading a referenced assembly never locks
      it, so users can rebuild their library while Spark is open (**E7-T9**).
- [ ] `.sparkcustom` is the same graph schema plus an interface block; ports come from
      Input/Output nodes placed inside the definition graph. **Graph-in-graph is the same
      mechanism, not a separate feature** (**E7-T11**).
- [ ] *Collapse selection to custom node* extracts a subgraph and infers its interface from
      the cut wires (**E7-T12**).
- [ ] Recursion is refused at save **and** at load, with the containment path reported
      (**E7-T13**).
- [ ] A `CustomViewKey` seam is reserved in the format now, so adding custom node UI later
      needs no format migration (**E7-T15**).

**Status.** Not started.

---

## E8 — UI shell and node canvas

**Goal.** A canvas that stays at 60 fps with 2000 nodes on it, in an application shell that
does not get in the way.

**In scope.** `Spark.UI` and `Spark.Desktop`: the Avalonia shell, docking, the graph canvas
and its interaction model, the node library panel, undo and redo, watch and preview,
settings, autosave and crash recovery, and the help panel.

**Out of scope.** 3D rendering — that is [E9](#e9--3d-viewport), and `Spark.Viewport` is
Avalonia-free by rule. Anything the engine does; views never touch `Spark.Engine`, and the
architecture test enforces it.

**The load-bearing choice.** **Immediate-mode rendering over a retained `SceneIndex`, one
Avalonia control for the whole canvas.** One control per node collapses somewhere between
500 and 2000 nodes, because layout and hit-test costs are per-visual and real graphs exceed
that. Drawing a few thousand rounded rectangles and beziers through Skia is trivial by
comparison, and culling and hit-testing come free from DoodleSharp's `SceneIndex.cs`, which
is pure managed data-structure code with no WPF in it. Input fidelity is preserved by a
**hybrid overlay**: only the node currently being interacted with gets a real Avalonia
control positioned over the drawing, typically one at a time.

Taken from DoodleSharp: the retained index, the culling discipline, and *measure before
optimising*. **Left behind:** the three-backend arbitration with hysteresis. That complexity
was earned by a drawing app pushing tens of thousands of primitives; here it is pure
maintenance cost. One Skia backend, with `SceneIndex` as the seam if profiling ever demands
another.

**Acceptance criteria**

- [x] MVVM through CommunityToolkit.Mvvm source generators, not ReactiveUI — fewer concepts
      for contributors and no runtime reflection on property change, which matters at 2000
      nodes (**E8-T11**).
- [x] Compiled bindings are on by default, so binding errors are compile errors
      (**E8-T11**).
- [ ] The canvas is one control over a retained `SceneIndex`, with a hybrid overlay for the
      node under interaction (**E8-T3**, **E8-T4**, **E8-T5**).
- [ ] Pan, zoom, box select, drag, wire, unwire, delete, group, note and align all work
      (**E8-T6**).
- [x] LOD below 40% zoom (**E8-T7**).
- [ ] A 2000-node synthetic graph pans and zooms at 60 fps, benchmarked nightly from M2
      (**E8-T15**). *The nightly runs it and publishes the figures; it deliberately cannot fail on
      them, because a runner has no GPU and nobody yet knows the spread of a software-rendered
      frame time on that hardware. Setting the threshold from observed data is* **E1-T34**.
- [ ] Docking via `Dock.Avalonia`, with a serialisable, testable layout model, *reset
      layout* and named workspace presets. RCS's from-scratch dock manager is **not**
      ported; only the idea is (**E8-T2**).
- [x] Library search ranks exact → prefix → **camel-hump** → substring → tag → description.
      Camel-hump is the highest-value search feature across thousands of nodes and is cheap
      (**E8-T8**).
- [x] Undo and redo across every graph edit, made instant by the provenance cache
      (**E8-T9**). A bounded stack of whole-document `.spark` snapshots
      ([ADR-0022](adr/0022-undo-by-document-snapshot.md)), which is what makes *every* edit
      undoable — including moving a node, whose position never enters the engine graph and
      which an inverse-command stack over the engine would therefore have missed.
- [x] Every port shows the type it wants, on the node and in the properties panel, in the words a
      user types it in rather than in CLR type names (**E8-T18**). Found by using the application:
      a port called `centre` is a word, not an instruction.
- [x] Double-clicking empty canvas opens a ranked search box there, and Enter places the node at
      that point (**E8-T19**). Dynamo's gesture, with the difference stated where a user will meet
      it: Dynamo makes a code block, and Spark's code block is [E6](#e6--c-code-block), M4.
- [ ] Watch nodes and preview bubbles show a node's output **and its rank** (**E8-T10**). *The
      **bubbles** are built: a collapsible strip under every node whose closed line is
      `8 items · rank 1`. The **watch panel** is not.*
- [x] Node **and port** descriptions come from the assembly's sidecar XML documentation, so any
      library shipping its `.xml` gets tooltips with no extra work (**E5-T7**, FR-25). The port
      half was missing for three commits while the row read `Done` ([N29](NOTES.md)).
- [ ] Aggressive autosave and crash recovery, because
      [R11](PRD.md#12-risks) means the process can die without warning (**E8-T13**).
- [ ] Banners for a missing package and for a graph containing script nodes (**E8-T16**).

**Status.** Built in `85e3183` and `35107f0`, and **the gate it depended on passed**: M1.5 spike
(b) measured 2,000 nodes at 0.87 ms median and 2.26 ms p95 for the whole render pass, with cost
tracking what is on screen rather than graph size ([E11-T20](#e11--quality-and-verification)).

**Those two figures are not trustworthy as figures, and the conclusion they support is
unaffected.** They were taken over a 120-frame window while the run reported five hundred frames,
so they describe the tail of the zoom sweep rather than the whole of it ([N31](NOTES.md), fixed in
`E8-T21`). The gate was never close: the budget is 16.7 ms and the correction moves a number
around one millisecond by a fraction of a millisecond. Re-quote them from a nightly run rather
than re-deriving them by hand — that is `E1-T34`.

The shell, the immediate-mode canvas over a retained spatial index, level-of-detail below 40%
zoom, and pan/zoom/box-select/drag/wire/unwire/delete are all in, driven by headless tests using
real pointer gestures — which found a bug vigilance would not have: hit-testing depended on a
frame having been painted, because the spatial index was only rebuilt inside `Render`.

**Undo and redo landed next (`E8-T9`), and the mutation sweep on them repeated the lesson.** Of
three deliberate mutations, two were killed by named tests and the third survived: the test for
"clicking a node without moving it is not an edit" passed under a mutation that recorded *every*
drag, because a click never raises a pointer-move event and so never reached the guard at all. It
was a test that could not fail, in the shape [N18](NOTES.md) and [N19](NOTES.md) already describe.
The repair was both halves — the canvas now accumulates a **net** displacement rather than
setting a flag on the first move, and a test drags a node out and back to where it started.

**Three rows are short.** The shell is a `Grid` with splitters rather than a `DockControl`,
because Dock.Avalonia's templates live in a companion package that was never pinned and without
it a `DockControl` renders nothing at all (`E8-T2`). Group, note and align are not built
(`E8-T6`). The third has closed since it was written: `bench/Spark.Benchmarks` and
`--canvas-benchmark` now both run nightly (`E1-T21`). `E8-T15` stays short for a narrower reason —
the canvas figures are **recorded and not gated**, because a runner has no GPU and nobody yet
knows the spread of a software-rendered frame time on that hardware. Setting the threshold from
observed data is `E1-T34`. The managed suites are gated from the first run, on bytes allocated
rather than on time.

**The hybrid overlay has its first inhabitant** (`E8-T5`). Nothing is edited in place on a node
yet, but the canvas creation box is a real Avalonia control positioned in screen space over the
immediate-mode drawing, which is the shape that row describes — so the row is a control per
gesture rather than a layer that does not exist.

**One accepted defect, scoped and specified:** between 81% and 83% zoom the drop shadow crosses
its blur threshold, Avalonia runs a real Gaussian per node, and the frame rate falls from 57 to
40 fps. The design language already specifies the fix — a sprite cache keyed on a fixed set of
blur radii, eight sprites in all — and it is not yet built.

---

## E9 — 3D viewport

**Goal.** See the geometry, pick it, and render it deterministically enough that a CI job
can diff the picture.

**In scope.** `Spark.Viewport`: `IViewportRenderer`, the scene and camera, `RenderPackage`,
the OpenGL backend, the software backend, tessellation streaming, picking and selection
sync.

**Out of scope.** Avalonia. **`Spark.Viewport` references no Avalonia package** — only
`Spark.UI` adapts it to `OpenGlControlBase`. That rule is what makes the software renderer
usable headlessly, which is the only way viewport output becomes testable at all.

**Why not Silk.NET or Veldrid.** Avalonia's built-in `OpenGlControlBase` is used because
Silk.NET adds a dependency without solving surface interop, and Veldrid has been effectively
unmaintained since around 2023 — a poor bet on a multi-year horizon.

**Acceptance criteria**

- [x] Everything goes through `IViewportRenderer`; no backend type escapes it (**E9-T1**).
- [x] An OpenGL 3.3 core backend on Avalonia's `OpenGlControlBase` (**E9-T4**).
- [ ] A software fallback that earns its place three ways: GL-init failures on VMs and RDP,
      headless thumbnails, and **deterministic `spark render` for CI visual regression** —
      GPU output is not testable, software output is (**E9-T5**, **E9-T11**, **E9-T12**).
- [x] Geometry reaches the viewport as immutable `RenderPackage` records, one GPU buffer set
      per `(NodeId, PortIndex)`, so re-evaluating one node re-uploads one buffer
      (**E9-T3**, **E9-T6**).
- [ ] Tessellation is parallel and streams during a run (**E9-T7**).
- [ ] Picking uses the kernel's BVH ray caster (**E9-T8**).
- [ ] **Selection sync falls out of node-keyed identity with no extra bookkeeping** — the
      `(NodeId, PortIndex, ElementPath)` tuple keys viewport buffers, selection, diagnostics
      and the watch panel alike, and survives recomputation in a way an object ID would not
      (**E9-T9**).

**Status.** Built in `85e3183`, extended with curves, and **the gate it depended on passed**:
M1.5 spike (a) drew a shaded lit box and sphere with the plinth correctly occluding the ground
grid, verified by reading the framebuffer back rather than by trusting that it compiled
([E11-T19](#e11--quality-and-verification)).

**One finding from that spike changes how every shader here is written.** Avalonia on Windows
defaults to **ANGLE**, so the surface is OpenGL ES 3.0 over Direct3D 11 — never desktop GL 3.3,
which is what ADR-0014 named. The first run failed on a missing precision qualifier. Shaders are
now dialect-adaptive and a test asserts the precision statement precedes any declaration in every
dialect: a defect that passes on a Linux desktop-GL machine and fails on the platform we ship to.

The seam, the scene, the camera, `RenderPackage`, the GL backend and one buffer set per
`(NodeId, PortIndex)` are all in, and any `Curve` is drawn from its own tessellation at a display
tolerance derived from the curve's size rather than from the kernel's 1e-6 default.

Still open: the software renderer, parallel streamed tessellation, picking through the ray
caster, headless thumbnails and CI visual regression. **Selection sync is `Open` with its
mechanism already present** — `RenderPackage` carries `IsSelected` and the renderer honours it;
nothing sets it from the canvas (`E9-T9`).

---

## E10 — Documentation

**Goal.** Documentation that cannot silently rot, because it is either generated from the
code or verified against it by the build.

**In scope.** These project documents; the ADRs; XML documentation comments on every public
member of the contract projects; the hand-written help topics; the worked example graphs;
the generated API reference; the in-product help renderer; the website.

**Out of scope.** The harness that checks all of it — that is
[E11](#e11--quality-and-verification). The distinction matters: this epic writes,
that epic enforces.

**Three tiers.**

1. **API reference — generated.** Nobody writes it; nobody can forget it.
2. **Help topics — hand-written Markdown** in `docs/help/`, one per concept and node family,
   with YAML front matter (`id, title, nodes[], related[], since, examples[]`). **Every
   topic must contain a worked example.** F1 shows this first; the reference is the
   drill-down.
3. **Worked example graphs** — real `.spark` files in `docs/examples/`, openable from the
   help panel and executed headlessly in CI. This is the node-graph analogue of compiling a
   snippet, and the strongest anti-rot mechanism available: a screenshot rots silently, an
   executed graph does not.

**The anti-pattern being avoided.** DoodleSharp's `DocGenerator` is 6,784 lines around
three hand-maintained dictionaries — roughly 1,478 member descriptions keyed by string —
that needed two test suites to stay honest, after 101 of 108 public constructors were found
rendering blank while 7 carefully written entries pointed at dead members. Spark's help
derives from XML doc comments, which cannot drift from the code they annotate. `DocGenerator`
is explicitly not ported.

**Taxonomy boundary, written down so it is not re-litigated.** *ADR = a decision that could
have gone differently. NOTE = a non-obvious implementation fact. Help topic = something a
user needs. XML doc = what this member does.*

**Acceptance criteria**

- [x] PRD, epics, task register, TODO, notes, working agreement, contributing guide and
      README exist and are honest about status (**E10-T1**).
- [x] ADR-0001 to ADR-0019 exist with an index, mostly transcribing decisions already made
      — which is the point of having made them explicitly (**E10-T2**). *ADR-0019 is the
      exception and the interesting one: it supersedes ADR-0009, whose strictly-additive rule
      rested on a public package ecosystem that **D11** establishes will not exist.*
- [x] Every ADR cited anywhere in the repository — Markdown, source comments and build
      files alike — resolves to a record that exists, checked on every `dotnet test`
      (**E11-T1**).
- [ ] `docs/help/` has a skeleton and a front-matter schema (**E10-T3**, **E10-T6**).
- [x] `docs/help/concepts/lacing.md` with the case table exists **before the replication
      engine does** ([E4-T1](#e4--replication-and-lacing)).
- [x] `GenerateDocumentationFile` is on everywhere and CS1591 is an error on the contract
      projects, so undocumented public API does not build
      ([E1-T10](#e1--foundations-build-and-ci)).
- [ ] Every public member of `Spark.Api`, `Spark.Geometry`, `Spark.Geometry.Io` and
      `Spark.Nodes.Core` carries an XML documentation comment (**E10-T8**, **E10-T9**,
      **E10-T10**).
- [ ] Generated API reference pages (**E10-T5**).
- [ ] Every help topic contains a worked example (**E10-T3**, enforced by
      [E11-T2](#e11--quality-and-verification)).
- [ ] Worked example graphs live in `docs/examples/` and are openable from the help panel
      (**E10-T7**).
- [ ] Every `SPK####` code has a help topic (**E10-T11**).
- [ ] Per-PR changelog fragments, so a single changelog file never becomes a merge-conflict
      magnet (**E10-T12**).
- [ ] An in-product Markdown help renderer lives in `Spark.Api`, free of UI dependencies so
      the harness can exercise it anywhere (**E10-T13**).
- [x] `docs/NOTES.md` uses stable numbers that are never renumbered and never reused, with
      gaps left on deletion (**E10-T1**).

**Status.** Partly done. As of 2026-08-27: the eight project documents exist and have been
reconciled against the repository, twenty-one ADRs exist with an index — one of them, ADR-0009,
superseded by ADR-0019 — and **three help topics exist**: `concepts/lacing.md`,
`concepts/geometry-basics.md` (the first topic about the kernel) and
`concepts/design-language.md`, the last of which is owned by `spark-ui`, landed alongside
this reconciliation, and is recorded here rather than reviewed here.

**XML doc comments have started, and started where they are enforced.** All 487 public
members of `Spark.Geometry` carry them — CS1591-as-error makes that structural rather than
diligent — together with an internal `NamespaceDoc` that states the namespace-wide
conventions once instead of thirteen times. They are not minimal: they state units, edge
behaviour at zero, `NaN` and `default`, and in several places why a member behaves as it
does. `Spark.Api`, `Spark.Geometry.Io` and `Spark.Nodes.Core` are still empty projects.

The rest of the help skeleton, the generated reference and the changelog fragments do not
exist, and `docs/examples/` is an empty directory.

**A caution the geometry topic had to observe, and every future topic must.** Its worked
examples were run against the compiled assembly, not written from the signatures. Two of them
came back wrong — `Angle.FullTurn / 3.0` is `119.99999999999999°`, not `120°`, and
`Tolerance.Default.Scaled(10.0).Linear` is `9.999999999999999e-6`, not `1e-5`. Both would
have read as perfectly plausible and both would have been false. Until
[E11-T2](#e11--quality-and-verification) compiles and runs the fences automatically, running
them by hand is the only thing standing between a help topic and a confident lie.

**One defect closed, and it is worth recording how.** Two ADR citations in build files
pointed at real records about unrelated subjects — `Directory.Packages.props` cited the
tolerance ADR for property-based testing, `.gitattributes` cited the replication ADR for the
`.spark` container decision. The diagnosis was that these were two *missing* records rather
than two typos, and that is how it was resolved: **ADR-0017** and **ADR-0018** were written,
the citations re-pointed at them, and the docs harness extended to scan build files and
source comments so that a dangling citation is a red build rather than a thing a reader
trips over. Note the limit, which is stated in the harness itself: it can prove a cited
record **exists**, never that it is about the right subject. Both original citations passed
an existence check. That part stays a review matter.

---

## E11 — Quality and verification

**Goal.** Know that it works, rather than believing it — and make the knowing automatic.

**In scope.** `tests/`, `bench/`, and every gate: the docs harness, the architecture tests,
the reflection-driven consistency tests, property-based tests, golden files, the lacing
corpus, the headless UI smoke test, the regression corpus, and the three M1.5 de-risk
spikes.

**Out of scope.** Writing the documentation those gates check — that is
[E10](#e10--documentation). Creating the CI workflow files — that is
[E1](#e1--foundations-build-and-ci); this epic supplies what they run.

**House conventions.** xunit, with **full PascalCase sentence names and no underscores**.
One flat test project per source project. Non-parallel collections for anything touching
statics. Golden files stored as hashes plus summary stats, and **failures print a readable
diff table** — bounding box, counts, area, volume — because a bare hash mismatch tells you
nothing.

**Acceptance criteria**

- [x] `tests/Spark.Docs.Verify` is green on an **empty corpus** from M0, so the harness can
      never be retrofitted (**E11-T1**). *Five checks that need nothing compiled. The checks
      that need compiled assemblies are deliberately **not stubbed**: a test that passes by
      doing nothing is worse than no test, so they arrive with the milestones that create
      the things they check.*
- [ ] Every ` ```csharp ` fence and every XML `<example>` compiles **using the exact
      references and imports a real code-block node gets**, with `<!-- spark:skip -->` as a
      sparing opt-out (**E11-T2**).
- [ ] Every example graph runs headlessly with no node errors and matches its declared
      expected outputs (**E11-T3**).
- [ ] **Forward node coverage**: every built-in node resolves to a help topic, or is listed
      as deliberately undocumented with a reason. **A new node shipping undocumented fails
      the build** (**E11-T4**).
- [ ] **Reverse coverage**: every `nodes:` front-matter entry resolves to a real node, which
      is what catches renames (**E11-T5**).
- [x] `Spark.Geometry` takes no third-party dependency beyond Clipper2, asserted as a
      **ceiling rather than an exact set** (**E11-T22**). *Relaxed and renamed on 2026-08-27
      when the unused Clipper2 reference came out. An exact-set assertion would have had to be
      edited twice for one round trip and broke a passing test that was not testing anything
      wrong; a ceiling holds before the planar pipeline arrives and after.*
- [ ] Every `SPK####` code in source has a help topic, by source scan (**E11-T6**).
- [ ] Link and asset integrity, plus Markdown renderer parity against a golden corpus
      (**E11-T7**). *Relative-link integrity is done and passing; asset integrity and
      renderer parity wait on there being assets and a renderer.*
- [x] `Spark.Architecture.Tests` scans source and enforces all five reference-graph rules:
      `Spark.Api` sees only the BCL and `Spark.Geometry`; `Spark.Nodes.Core` never
      references `Spark.Engine`; `Spark.Viewport` is Avalonia-free; nothing under `src/`
      references anything under `tests/`; no `-windows` TFM anywhere (**E11-T8**). *Six
      tests passing locally — the five rules plus the Clipper2 ceiling above. It reads the
      `.csproj` files as XML and references none of the projects it
      inspects, because a test that referenced them could not observe a forbidden reference.
      The related rule **views never touch `Spark.Engine`** is not yet enforced: there is no
      `Spark.UI` code to check.*
- [ ] A reflection-driven geometry serialization round-trip test enumerates every concrete
      type (**E11-T9**).
- [ ] Property-based tests on the kernel with CsCheck **from M1 — non-negotiable**
      (**E11-T10**). *`tests/Spark.Geometry.Properties` exists and its 28 properties pass over
      the value layer, with generators spanning 1e-9 to 1e9 log-uniform per **ADR-0018** and a
      whole scene generated at one shared scale. Unticked because the criterion is about the
      kernel and most of the kernel does not exist. **The lesson from the review belongs
      here:** judge a property by its generator, not its assertion — one that cannot reach the
      boundary it tests cannot fail, and reports identically to one that can.*
- [ ] Golden-file geometry tests print readable diff tables on failure (**E11-T11**).
- [ ] The lacing case table asserts value and rank separately (**E11-T12**).
- [ ] The node↔member two-way diff passes in both directions (**E11-T13**).
- [ ] The `docs-freshness` job fails a diff that changes a public-API baseline or touches
      `src/Spark.Nodes.*` without touching `docs/`, overridable only by an explicit
      `docs: none-needed` commit trailer that is **visible in review**. A silent exemption
      is worthless; a loud one is fine (**E11-T14**). *Written in `ci.yml`; being
      `pull_request`-only it cannot have run, and has not.*
- [ ] A headless UI smoke test runs in CI (**E11-T15**).
- [ ] Benchmarks run nightly, gating allocation rather than time (**E11-T16**). *Written and
      never run; the committed time series was deliberately not built, see* **E1-T21** *and*
      [ADR-0023](adr/0023-benchmarks-gate-allocation-not-time.md).
- [ ] `tests/corpus/` grows with every bug found (**E11-T17**).
- [ ] The three M1.5 spikes have **pass/fail criteria written down before the spike starts**
      and are deleted afterwards (**E11-T19**, **E11-T20**, **E11-T21**). *None of the three
      criteria is written. This is the next thing this epic owes, and it must land before M1
      ends, not before M1.5 begins.*

**Status.** Partly done, and materially further along than at the last pass. **Four test
projects exist and `dotnet test Spark.slnx` runs 315 tests**, all passing locally on
2026-08-27: `Spark.Geometry.Tests` (276 example-based), `Spark.Geometry.Properties` (28
CsCheck properties), `Spark.Architecture.Tests` (6, enforcing the reference graph) and
`Spark.Docs.Verify` (5, checking the documents against the repository). The two older ones
were stood up **before the code they now guard**, which was the whole argument for putting
them in M0.

The harness has already earned its keep rather than merely existing: extending its
ADR-citation check to scan build files turned up two citations pointing at records nobody had
written, and closing that produced two real ADRs
([E1-T29](#e1--foundations-build-and-ci)). A gate that finds something on the day it is
switched on is a gate worth having.

**And the epic has now met the limit of what gates do.** The geometry kernel's first slice
passed every gate this epic owns and was rejected on review, with three of its eight claims
false. Two of the tests meant to guard it were **structurally incapable of failing**: the
property drew two independent uniform values and could not produce a case near the boundary
it asserted about — zero violations in five million simulated draws, against the hundred
CsCheck runs per invocation. A generator that straddles the threshold finds 908 in 12,006.

Three things follow, and they are this epic's obligations rather than observations:

1. **A fix is regression-proven by reverting it and naming the test that goes red.** That is
   the standard the repaired slice was held to, and it is the one thing that was missing the
   first time.
2. **A property is judged by its generator.** Ask what fraction of generated cases can reach
   the condition under test. Generators now span 1e-9 to 1e9 log-uniform per **ADR-0018**, and
   widening them turned two further properties red — both naive assertions rather than kernel
   bugs, which is the outcome a widened generator exists to produce.
3. **`reviewer` is no longer an optional agent definition.** It is [E1-T30](#e1--foundations-build-and-ci),
   it is unwritten, and this epic now has direct evidence of what its absence costs.

One honest qualification. Every one of these gates has run **only on a Windows developer
machine**. The CI workflow that would run them on Linux, and run them on every push, is
written and has never executed.

---

## E12 — Embedding and release

**Goal.** `Spark.Host` runs inside a real CAD add-in, and a Windows user can install Spark
from an installer that works.

**In scope.** `Spark.Host`, `Spark.Cli`, the Windows build, the installer, the portable zip,
the release workflow, and the performance and accessibility passes that gate 1.0.

**Out of scope.** macOS and Linux artefacts (**D14**). Any host-specific add-in shipped as
a product — M8 proves the seam, it does not ship a Revit plugin. And — worth saying plainly
here, because an earlier revision of this epic said the opposite — **publishing anything to
nuget.org**. Spark consumes NuGet packages and produces none; `IsPackable` is `false`
repository-wide. Embedders reference `Spark.Host` from an install and node authors reference
`Spark.Api` from an install, which is how CAD add-ins are built anyway. **D11**,
[NOTES.md N14](NOTES.md).

**Acceptance criteria**

- [ ] `SparkSession` is the composition root and owns lifetime; `Spark.Host` has no UI
      (**E12-T1**).
- [ ] `IHostServices` is the CAD embedding seam (**E12-T2**).
- [ ] The host-thread `IEvaluationScheduler` runs evaluation on the host's thread, which is
      the entire embedding mechanism (**E12-T3**).
- [ ] `Spark.Host` is proven inside a real Revit or AutoCAD add-in (**E12-T4**).
- [ ] `spark run`, `check`, `render`, `export`, `pkg`, `docs` and `graph` all work
      headlessly, and `spark run` produces output identical to the desktop app's
      (**E12-T5**).
- [ ] The CLI ships as `spark.exe` inside the installer and the portable zip, beside the
      desktop application (**E12-T5**, **E12-T9**, **E12-T10**). *`Spark.Cli` sets
      `<AssemblyName>spark</AssemblyName>`; it is not a dotnet global tool and there is no
      `Spark.Tool` package — **E12-T6** is withdrawn.*
- [x] Nothing in the repository publishes to nuget.org: `IsPackable` is `false` for every
      project, with no `PackageId`, `PackAsTool` or package metadata anywhere (**E12-T7**,
      withdrawn; **E12-T17**, withdrawn). *Spark consumes NuGet packages and produces none —
      **D11**. This reverses what an earlier revision of this epic said, and the two questions
      it left open about `Spark.Host` and `Spark.Engine` are answered by the reversal rather
      than settled on their own terms.*
- [ ] A self-contained single-file ReadyToRun Windows build, a **signed** Inno Setup
      installer, and a portable zip (**E12-T8**, **E12-T9**, **E12-T10**).
- [ ] The release workflow refuses to publish when the computed version and the tag disagree
      (**E12-T11**).
- [ ] A performance pass and an accessibility pass before 1.0 (**E12-T12**, **E12-T13**).
- [ ] ~~Exact NURBS booleans, and fillet and chamfer on solids, are stated publicly as **out
      of scope for 1.0**~~ — **withdrawn** (**E12-T15**). They are *in* 1.0 under
      [ADR-0020](adr/0020-occt-via-c-abi-shim.md), so the sentence this criterion existed to
      say publicly is no longer true. What replaces it is the **positioning paragraph** in the
      README, which says that Spark ships OpenCascade and why that is not the dependency Spark
      exists to remove (**R13**).
- [ ] **The publish pipeline meets the OCCT licence obligations** — dynamic linking,
      replaceable shared libraries, **no single-file seal and no NativeAOT over OCCT**, the
      LGPL and exception texts shipped, prominent notice in About, README, installer and
      release notes, a source offer against a pinned tag, and any modification kept as a
      numbered patch file ([E13-T16](#e13--occt-provider), **R21**). *This constrains
      **E12-T8**, which was written before the constraint existed. Nothing here is legal
      advice; six questions are with counsel — **Q13**.*

**Status.** Not started.

---

## E13 — OCCT provider

**Goal.** Exact solid modelling — booleans, trimming, filleting, shelling and STEP — reachable
from a Spark graph, through OpenCascade, behind `IBrepKernel`, on Windows and Linux, with a
native surface small enough that one person can maintain it across upstream upgrades.

**Why this epic exists.** The client instructed capability parity with Dynamo's geometry
(FR-81), and [DYNAMO-COVERAGE §6.1](DYNAMO-COVERAGE.md#61-parity-on-solid-and-surface-commits-us-to-exact-solid-modelling)
established that **70 members cannot exist without exact BRep booleans, trimming, filleting and
sewing.** Offered three paths, the client chose to take an existing engine.
[ADR-0020](adr/0020-occt-via-c-abi-shim.md) records the engine and the binding;
[ADR-0021](adr/0021-brep-kernel-residency.md) records what that does to the seam.

**In scope.** `native/spark_occt/` — the C-ABI shim, C++, MIT, ours. `Spark.Geometry.Occt` —
the `LibraryImport` layer and the `IBrepKernel` implementation. The `Import` and `Materialise`
crossings. Booleans, trim, split, fillet, chamfer, shell, thicken, draft, offset, extrude,
revolve, loft, sweep, patch, sew, heal, validate. BRep tessellation. STEP and IGES. The
per-RID build, cache and distribution pipeline. Diagnostics across the boundary. Licence
compliance in the publish pipeline.

**Out of scope.** Everything in front of the seam — values, curves, surfaces, meshes, planar
geometry, evaluation, mesh tessellation, serialisation and the OBJ/STL/PLY/glTF writers stay
[E2](#e2--geometry-kernel), and **must keep working in a build with no native component at
all**. Mesh booleans stay E2 and move to 1.x. A **second** provider: there is one, and building
another to justify the abstraction is explicitly not wanted.

**The cost, stated as two numbers because one would mislead.** Against the plan as written,
**+7 to +11 weeks**. Against what was actually asked for, it **saves years and retires R1**.
Both are true, and the first is positive only because the plan as written never contained the
expensive thing: M6's 14 weeks bought mesh booleans, while exact booleans, fillet, chamfer and
trim sat in PRD §9's out-of-scope list. This epic is roughly **24 weeks**, most of it inside M6,
which goes from 14 weeks to **20–24**.

**Acceptance criteria**

- [ ] **M1.6 passes**: OCCT builds from a pinned tag through a vcpkg manifest on Windows *and*
      Linux, one boolean runs end to end through a minimal shim and `LibraryImport`, and the
      per-RID binary footprint is **measured** rather than bracketed (**E13-T1**).
- [ ] The shim is **hand-written, MIT, and ours**, in `native/spark_occt/`, at an order of
      350–500 exported entry points over roughly 2–3% of OCCT's class surface. **No
      third-party binding is adopted** (**E13-T2**, [ADR-0020](adr/0020-occt-via-c-abi-shim.md)).
- [ ] **Every entry point has a `catch(...)`** and `OSD::SetSignal(false)` is called at
      initialisation, so no C++ exception and no OCCT signal handler ever reaches a managed
      frame (**E13-T2**, **R19**).
- [ ] `Spark.Geometry.Occt` is the **only** project with `AllowUnsafeBlocks=true`, opted in
      with a comment naming ADR-0020, and an architecture test asserts it (**E13-T4**,
      **NFR-15**).
- [ ] `SparkGeometryTakesNoThirdPartyDependencyBeyondClipper` is **unchanged**, and gains a
      *companion* rule asserting `Spark.Geometry.Occt` is referenced only by composition roots
      (**E13-T4**, **NFR-5b**). *Relaxing either test would be the wrong repair.*
- [ ] **Residency is canonical, not cached**: exactly two crossings, a ten-operation chain
      performing zero imports and one materialisation, and **round-trip asserted as
      tolerance-bounded equivalence — never identity** (**E13-T5**, **E13-T6**,
      [ADR-0021](adr/0021-brep-kernel-residency.md)).
- [ ] The evaluation cache tracks a **native budget reported by the shim**, not an estimate of
      managed size (**E13-T3**, **NFR-4**).
- [ ] Equality and hashing of a `Brep` are defined on the **materialised model**, never on the
      handle (**E13-T6**).
- [ ] Every failure carries an OCCT `Message_Report` translated into `SparkDiagnostic`, and a
      **Draw-Harness-compatible `.brep` dump**, so a bug reproduces upstream; `BRepCheck_Analyzer`
      runs in Debug (**E13-T13**, **R16**).
- [ ] The Linux CI leg still runs, on a **cached per-RID artefact keyed on
      `(occt-tag, vcpkg-baseline, shim-source-hash, rid)`**, with the from-clean build nightly
      (**E13-T15**). *ADR-0001 justified the rot-guard as a rot-guard because it was nearly
      free. It is not free any more, and **without the cache it will not survive a busy PR
      queue**.*
- [ ] STEP AP203/AP214/**AP242** and IGES read and write, validated against a public corpus and
      a **third-party viewer, never our own reader** (**E13-T12**). *OCCT wrote the exporter;
      that is not evidence our use of it is right.*
- [ ] The licence obligations are met **by the pipeline rather than by remembering**
      (**E13-T16**, **R21**).
- [ ] **NFR-8 is answered rather than suppressed.** Either the watertightness property holds
      against OCCT's mesher at a deflection we choose, or the requirement is restated to say
      exactly what it guarantees (**E13-T11**).
- [ ] The threading policy is **decided on evidence**, not assumed (**E13-T14**, **R20**,
      **Q14**).

**What is deliberately not claimed here.** Seven things are unknown and are recorded in
[ADR-0020](adr/0020-occt-via-c-abi-shim.md) as open with how to find out: real binary sizes;
whether excluding the Visualization module drops FreeType; whether STEP can avoid XCAF; OCCT's
real thread-safety envelope; the counsel question; whether `OcctNet.Wrapper` has a source
repository at all; and E13-T3's real cost. **None of them is resolved by writing confidently
about it**, and M1.6 exists to answer the first four.

**Status.** Not started. **Nothing of this epic exists in the tree** — there is no `native/`
directory, no `Spark.Geometry.Occt` project and no OCCT anywhere. The decision is recorded; the
work has not begun.
