# Spark — Product Requirements

**Status:** M0 — foundations, mostly landed. No product code is implemented; the repository
is scaffolding, gates and specification.
**Owner:** Nicety
**Last updated:** 2026-08-28
**Latest change:** the solid-modelling kernel decision — **D2 reverses**, **D15** is new, R1 and
R12 retire, R15 … R22 arrive, and a new epic **E13** appears. [ADR-0020](adr/0020-occt-via-c-abi-shim.md),
[ADR-0021](adr/0021-brep-kernel-residency.md). **Nothing of it is built.**

---

## 1. Summary

Spark is a node-based visual programming environment for .NET: nodes, wires, ports, a
graph canvas, a 3D viewport, a searchable node library and code blocks. It is open source
under MIT, and it depends on no Autodesk product.

Two deliberate departures from the tool it is most obviously compared to:

1. **C# replaces DesignScript.** Code blocks host real C# through Roslyn. Every .NET
   developer already knows the language, and the whole NuGet ecosystem becomes reachable
   from inside a graph.
2. **The geometry model is ours; the solid-modelling engine is OpenCascade.**
   `Spark.Geometry` is a pure-managed 3D BRep/NURBS model with its own values, curves,
   surfaces, meshes, planar geometry, evaluation and interchange writers, seeded from the
   pure-maths parts of `C2VGeometry`. Exact booleans, trimming, filleting, sewing and STEP
   come from **OCCT**, reached through a C-ABI shim we own — open source, freely
   redistributable, shipped **with** Spark, requiring no account, no licence purchase and no
   other vendor's product. **This reverses D2 and is the largest decision in the project;**
   the reasoning is **D2**, **D15** and [ADR-0020](adr/0020-occt-via-c-abi-shim.md), and
   nothing of it is built.

Because the platform is .NET, package management comes nearly free: NuGet *is* the package
manager, and users can reference arbitrary DLLs with nodes generated from them by
reflection.

**As of this document, almost none of that is built.** M0 has produced a solution, twelve
project stubs, a reference graph, build properties, these documents, twenty-one ADRs, the
replication specification, a CI workflow, public-API baselines, and four test projects that
between them run **315 passing checks** against the repository. There is **no `native/`
directory, no `Spark.Geometry.Occt` project and no OCCT anywhere in this tree** — ADR-0020 is a
decision, not an implementation, and nothing below should be read as saying otherwise.

**One requirement has moved off `Not started` by being built, and only one.** The geometry
kernel's **value layer** has landed, been reviewed, repaired and accepted: thirteen types in
`src/Spark.Geometry` declaring 387 public members, covered by 304 tests. That is FR-47 in
part and FR-56 in full. Everything else below is still scaffolding, gates and specification,
none of which is product — there are **no curves, no surfaces, no meshes and no BRep types**,
and no graph engine at all.

**A second requirement, FR-81, is new and starts at 11.0%.** The client's instruction —
*make sure we have all geometry elements and methods and properties what is there in Dynamo* —
is now a requirement with a register behind it. Measured against the 51 public types and 837
public members of the `ProtoGeometry.dll` installed with Revit 2026, **92 members are reachable
in Spark today**, all of them in the value layer. See [DYNAMO-COVERAGE.md](DYNAMO-COVERAGE.md),
and note what it found: parity on `Solid` and `Surface` commits us to the exact booleans §9
currently places post-1.0 (**R14**, **Q11**), and T-Splines alone is a fifth of the surface and
needs its own decision (**Q12**).

Two distinctions are worth stating before the tables use them.

- The three gates **pass, locally, on Windows**, and that is a fact. The CI workflow has been
  green on Windows and Linux for **earlier commits** and has seen nothing of the geometry
  kernel, and that is a different fact. Nothing in this document treats the second as the
  first.
- **Passing the gates is not the same as being reviewed**, and this project has paid to learn
  it. The kernel's first slice passed all three and was rejected on review, with three of its
  eight claims false. See [NOTES.md N18](NOTES.md).

## 2. Problem

**Dynamo Sandbox is nominally standalone and is not.** It depends on Autodesk's
ProtoGeometry and its related libraries, which in practice forces a user to have at least
one Autodesk product installed. Someone who wants a parametric node graph — a facade
studies tool, a structural layout generator, a fabrication script — must first buy into
a commercial CAD licence for a component they never asked for. That is the dependency
Spark exists to remove.

**DesignScript is a language nobody else uses.** It is competent, and it is a dead end
for the person writing it: no ecosystem, no package manager, no IDE outside the host, no
transferable skill, no Stack Overflow. A .NET developer coming to a node graph already
knows C#. Making them learn a bespoke language to write a three-line lambda is a tax paid
for nothing.

**Node environments hide their semantics.** Replication — what happens when you feed a
list into a port that wanted a scalar — is the single feature that separates a toy graph
editor from a usable one, and it is almost always documented by example rather than
specified. Users learn it by superstition. Spark writes the specification first and uses
it as the test corpus.

## 3. Goals and non-goals

### Goals

- **G1** — Run a full node-based parametric modelling session with no Autodesk software
  installed, on a machine that has only .NET.
- **G2** — Make the scripting language C#, with IntelliSense that knows the type on the
  incoming wire.
- **G3** — Ship a geometry kernel good enough to model with: curves, surfaces, solids,
  booleans, fillets and STEP. `Spark.Geometry` — values, curves, surfaces, meshes, planar
  geometry, evaluation, tessellation and every interchange writer — is **pure managed and
  independently distributable, with no native binaries**. Exact solid operations come from
  **OpenCascade**, which is open source, freely redistributable, ships **with** Spark, and
  needs no account, no licence purchase and no other vendor's product. **D2**, **D15**,
  [ADR-0020](adr/0020-occt-via-c-abi-shim.md).
- **G4** — Make replication semantics specified, documented and testable before they are
  implemented, so they never have to be broken later to fix them.
- **G5** — Make the node library extensible by anyone who can publish a NuGet package,
  with no attributes required and no kernel expertise needed.
- **G6** — Be embeddable. `Spark.Host` must run inside a Revit or AutoCAD add-in without
  a rewrite, because that is where the AEC users are.
- **G7** — Never damage a user's graph. Opening a graph on a machine missing a package
  must lose nothing and re-save byte-identically.
- **G8** — Make documentation impossible to skip: it is generated from, or verified
  against, the code it describes.

### Non-goals

- **N1** — Not Dynamo-compatible, in either direction. No `.dyn` reader, no `.dyn`
  writer, no importer, no seam. See **D8**.
- **N2** — Not a unit-aware modeller. Coordinates are dimensionless world units. See
  **D12**.
- **N3** — Not a drafting or annotation tool. No dimensions, hatches, text, arrows or
  grids — none of these are concepts Dynamo has, and adding them would double the kernel.
  See **D13**.
- **N4** — Not a sandbox. A Spark graph is executable code; opening one from an untrusted
  source is equivalent to running an unknown program. .NET has no code-access security and
  Spark will not pretend otherwise.
- **N5** — Not cross-platform *as a release* for v1. Windows only. Linux is built and
  tested in CI as a rot-guard, and no artefact is published for it. See **D14**.
- **N6** — Not a telemetry collector. No telemetry of any kind in v1.
- **N7** — Not a multi-language host. C# only. No Python, no VB, no DesignScript.

## 4. Users

| User | What they need |
|---|---|
| **AEC computational designer** | Parametric geometry without a CAD licence for the standalone case, and the same graph running inside Revit or AutoCAD when it matters. Primary user; wins any direct UX conflict. See **D9**. |
| **.NET developer** | A visual dataflow front end over their own libraries. They want their existing NuGet package to become nodes without writing a plugin. |
| **Fabricator / maker** | A parametric model out to OBJ, STL or STEP, with a viewport they can trust and no subscription. |
| **Node package author** | A small, stable contract surface (`Spark.Api`) they can build against, changed deliberately and never silently, so a Spark upgrade does not quietly invalidate their DLL. |
| **Educator** | Something free, installable and open that teaches parametric modelling without a licence server. |

## 5. Product principles

Each of these was chosen over a plausible alternative; the reasoning is preserved in the
[decision log](#13-decision-log).

1. **Independence is the point.** If a feature can only be built by depending on a
   commercial CAD product, it does not ship. That includes semantic compatibility with
   one.
2. **`Spark.Geometry` is pure managed and stays that way.** No native binaries in its
   published output, verified by CI rather than promised (**NFR-5**). Clipper2's C#
   distribution is managed and stays isolated behind one internal file so the promise remains
   checkable. **The principle is narrower than it used to be and the narrowing is deliberate.**
   Exact solid operations come from OpenCascade through a separate assembly
   (`Spark.Geometry.Occt`) and ship in the default install. That is a native dependency and
   this document does not pretend otherwise; what makes it consistent with principle 1 is that
   OCCT is open source, freely redistributable, and needs no account, no licence purchase and
   no other vendor's product. **D2**, **D15**, **R13**.
3. **Nothing is ambient.** Tolerance is passed, not global. Geometry has no identity, no
   style and no registry. C2VGeometry's auto-registering `Shape` is precisely the
   anti-pattern being designed out.
4. **Failure is data, not an exception.** Kernel operations return `Result<T>` carrying
   diagnostics and partial results. Kernel failure is normal and must be diagnosable.
5. **Specify the hard part first.** The replication case table is written as a help topic
   before the replication engine exists, and is consumed directly as the test corpus. It is
   deliberately never described by a count: the corpus grows as the engine finds cases the
   document did not anticipate, which is the point of writing it first.
6. **The graph is the user's, not ours.** Missing packages become placeholders that
   preserve every literal and every wire. A graph is never damaged by being opened.
7. **Errors do not cascade.** A failed node greys its downstream as *not evaluated*. A
   fifty-error wall hides the one node that caused it.
8. **Documentation is part of the build.** Undocumented public API on a contract project
   fails compilation. Examples are compiled; example graphs are executed.
9. **Honesty over polish in messaging.** ALC unload may fail, so restart is the documented
   default. `StackOverflowException` kills the process, so we say so.

## 6. Functional requirements

Everything is `Not started` except the three geometry rows that say otherwise — FR-47, in part,
FR-56 in full, and FR-81, at 11.0%. M0 produced scaffolding, not behaviour; M1's first slice
produced the value layer of the kernel and nothing above it.

**FR-81 is new and it is the client's instruction written down as a requirement**, along with
the register that makes it checkable rather than aspirational:
[DYNAMO-COVERAGE.md](DYNAMO-COVERAGE.md). It also surfaced two scope questions that were
previously invisible — **Q11** and **Q12** — and one new risk, **R14**.

### Graph engine

| ID | Requirement | Status |
|---|---|---|
| FR-1 | A canvas on which nodes can be created, deleted, moved, selected, wired and unwired. | Not started (E8) |
| FR-2 | A wire whose types are incompatible is refused **at wire-creation time**, with a message naming the reason and, where one exists, a suggested conversion node. | Not started (E3) |
| FR-3 | Widening, upcasts and rank lifting connect silently; registered and user-defined conversions connect as a **yellow** wire naming the converter and whether it is lossy; narrowing, parsing and lossy conversions never connect implicitly. | Not started (E3) |
| FR-4 | Two ports whose types share a `FullName` but come from different assemblies are refused, with both package identities named. | Not started (E3) |
| FR-5 | Evaluation is a Kahn topological sort over the **dirty subgraph only**, producing levels, parallel within a level. | Not started (E3) |
| FR-6 | Cycles are refused at wire creation with the closing path shown, and detected at load — where every node in the cycle errors and the rest of the graph still evaluates. Evaluation never hangs. | Not started (E3) |
| FR-7 | Results are cached content-addressed **by provenance**, not by value, so undo, A/B wire toggling and slider reverts hit the cache. | Not started (E3) |
| FR-8 | Impure nodes declare themselves and mix a run epoch into their cache key, poisoning downstream keys. | Not started (E3) |
| FR-9 | Run modes: Automatic (debounced ~200 ms), Manual and Periodic, with Manual auto-suggested past a graph-size threshold. | Not started (E3) |
| FR-10 | A run can be cancelled between nodes, between replication elements and inside long kernel loops; completed nodes stay cached. | Not started (E3) |
| FR-11 | A node or group can be frozen. Downstream reports *upstream frozen*, not an error. | Not started (E7) |
| FR-12 | Evaluation never runs on the UI thread; results marshal back over a progress channel so the canvas animates and geometry streams during a run. | Not started (E3) |
| FR-13 | `IEvaluationScheduler` has parallel, sequential-deterministic and host-thread implementations. | Not started (E3) |

### Replication and lacing

| ID | Requirement | Status |
|---|---|---|
| FR-14 | Replication is **rank-based**: `excess(i) = rank(actual) − declaredRank(i)`, `depth = max excess`; at `depth > 0` replicate one level and recurse. Nested structure is preserved exactly; there is no flatten-then-reshape. | Not started (E4) |
| FR-15 | Lacing modes `Shortest`, `Longest`, `CrossProduct` and `Disabled`, per node, with `CrossProduct` raising output rank by *k* — the number of replicating inputs — not by one. **`Auto` is a fifth selectable value but not a fifth algorithm**: it is a sentinel meaning *use this node definition's `DefaultLacing`*, resolved at evaluation time, with no `n` and no output rank of its own. | Not started (E4) |
| FR-16 | Multi-output nodes replicate in lockstep and transpose: two outputs over five items give two lists of five, never one list of five tuples. | Not started (E4) |
| FR-17 | Per-element failure is isolated. A throwing element yields `null` in its slot, the rest evaluate, and the node emits a **Warning** naming the failing indices. | Not started (E4) |
| FR-18 | `SparkList` is a first-class engine type with O(1), unambiguous rank. | Not started (E4) |
| FR-19 | `[NoReplication]` and `[KeepStructure]` let a node opt out of replication at its declared rank. | Not started (E4) |

### Node authoring and library

| ID | Requirement | Status |
|---|---|---|
| FR-20 | **Zero-config reflection import**: an arbitrary .NET assembly with no Spark attributes at all produces a usable node library. | Not started (E5) |
| FR-21 | `[SparkNode]`, `[NodePort]`, `[NodeIgnore]` and friends refine what reflection infers, for those who want to. | Not started (E5) |
| FR-22 | Import rules: methods included; property getters included and **setters excluded**; constructors become `Type.ByParamNames`; static readonly fields become constant nodes; extension methods present as instance methods; `out` parameters become extra outputs; `Task<T>` is awaited; operators are excluded as nodes and harvested as conversions instead. | Not started (E5) |
| FR-23 | **One node per overload**, grouped under a single library entry with a flyout, disambiguated by differing parameter names — never by a numeric suffix. | Not started (E5) |
| FR-24 | A public static `By*`/`From*`/`Create*` returning its own type suppresses the matching constructor, so `new Circle(c, r)` and `Circle.ByCenterRadius(c, r)` collapse to one node. Anything a factory does not cover still emits its constructor. | Not started (E5) |
| FR-25 | Node and port descriptions come from the assembly's sidecar XML documentation file, so any library shipping its `.xml` gets tooltips with no extra work. | Not started (E5) |
| FR-26 | An `Angle` parameter renders as a degree-valued port automatically, for first-party and third-party libraries alike. | Not started (E2, E5) |
| FR-27 | Library search ranks exact → prefix → **camel-hump** (`cbcr` finds `Circle.ByCenterRadius`) → substring → tag → description. | Not started (E8) |

### C# code block

| ID | Requirement | Status |
|---|---|---|
| FR-28 | An inline **Code Block** node and a docked **C# Script Node**, over one compilation pipeline. | Not started (E6) |
| FR-29 | Input ports are inferred **semantically** — compile against the prelude, collect `CS0103`/`CS0117`, take the identifiers in source order. Port identity is the variable name, so reordering usages does not rewire. | Not started (E6) |
| FR-30 | Once a port is connected, the upstream type is injected as a typed local, and **IntelliSense inside the code block knows the type on the incoming wire**. | Not started (E6) |
| FR-31 | Output ports come from a named tuple return: `return (area: a, perimeter: p);`. A plain final expression gives one `result` port. | Not started (E6) |
| FR-32 | Compilation is cached on `Hash(normalizedText, inputPortTypes, referenceCatalogVersion, langVersion)` — resident, so changing an input recompiles nothing, and persistent on disk, so reopening a file does not pay Roslyn cold start. Identical text in ten nodes compiles once. | Not started (E6) |
| FR-33 | Guard weaving bounds loop iterations and recursion depth so a runaway script is cancelled rather than hanging the application. | Not started (E6) |
| FR-34 | `spark run --no-script` refuses to execute script nodes, for CI. | Not started (E6) |

### File format, packages and extensibility

| ID | Requirement | Status |
|---|---|---|
| FR-35 | `.spark` is plain, canonically formatted JSON — stable key order, invariant numbers — so graphs diff and merge in git. `.sparkz` zips a graph with its assets for sharing. | Not started (E3) |
| FR-36 | Save/load round-trips byte-identically. | Not started (E3) |
| FR-37 | `graph.formatVersion` is a single monotonic integer, decoupled from product version; migrations are JSON-to-JSON, never against typed models, are never deleted, and each ships with a golden-file test against a real old graph. | Not started (E3) |
| FR-38 | `.sparkcustom` custom nodes use the same graph schema plus an interface block; ports come from Input/Output nodes placed inside the definition. Graph-in-graph is the same mechanism, not a separate feature. Recursion is refused at save and at load with the containment path reported. | Not started (E7) |
| FR-39 | *Collapse selection to custom node* extracts a subgraph and infers its interface from the cut wires. | Not started (E7) |
| FR-40 | A Spark package is a NuGet package tagged `spark` with a `tools/spark.json` manifest, installable from nuget.org or a private feed. | Not started (E7) |
| FR-41 | One collectible `AssemblyLoadContext` **per package version**, resolving by file presence in the context's own folder, with contract assemblies always resolved from the default context. | Not started (E7) |
| FR-42 | Upgrading a package purges definitions, invokers, cached values, viewport buffers and undo history, unloads, and verifies by weak reference — and **if it does not unload, says so and offers restart**. Restart is the documented default. | Not started (E7) |
| FR-43 | A graph referencing a missing package opens with **placeholder nodes preserving the definition key, every literal and every wire verbatim**, re-saves byte-identically, and shows a banner offering one-click install. | Not started (E7) |
| FR-44 | Install shows publisher, downloads, licence, signature status, transitive dependencies, node count and **whether the package contains native binaries**. | Not started (E7) |
| FR-45 | A local DLL can be referenced directly; it prompts once and records a content hash, re-prompting when the hash changes. Auto-reload on file change is offered, and reading a referenced assembly never locks it. | Not started (E7) |
| FR-46 | Opening a graph never auto-runs it. Manual mode plus a banner listing script nodes and required packages, with a content-hash per-origin trust allowlist. | Not started (E6, E7) |

### Geometry

| ID | Requirement | Status |
|---|---|---|
| FR-47 | Value types: `Point3d`, `Vector3d`, `Point2d`, `Vector2d`, `UV`, `Interval`, `BoundingBox`, `Transform` (4×4), `Plane`, `Angle`, `Tolerance`, `Quaternion`, plus immutable `CoordinateSystem`. **`Rgba` is deliberately not here** — it is a display concern, and the kernel has no styling, no screen awareness and no appearance of any kind. It belongs beside `Appearance` in `Spark.Api`. | **Twelve of thirteen done** (E2) — all but `Quaternion`, which is unwritten. Landed 2026-08-27, reviewed, repaired and accepted; 387 public members, 304 tests |
| FR-48 | Curves — `Line`, `Arc`, `Circle`, `EllipseCurve`, `PolyLine`, `PolyCurve`, `NurbsCurve` — with a common evaluation surface. | **Partly met (E2).** Six of the seven types exist over a common `Curve` base; `NurbsCurve` does not. **The contract shipped is wider than this row in one place and narrower in others, deliberately.** Wider: `LengthAt`, `ParameterAtLength`, `PointAtLength`, `DivideEqually` and `DivideByLength` are in it, because [DYNAMO-COVERAGE §3.2](DYNAMO-COVERAGE.md#32-curves--11-types-187-members-partially-reachable) found the fifteen members named here to be a structural under-count — Dynamo exposes four parameterisations of every query and a ten-member division family, all of which fall out of arc-length reparameterisation, which is cheap at M1 and expensive later. Narrower: `ClosestPoint` waits on the ray caster (`E2-T15`), and `CurvatureAt`, `IsPlanar`, `Split` and `ToNurbsCurve` are M3. |
| FR-49 | Surfaces — `PlaneSurface`, `SphericalSurface`, `CylindricalSurface`, `ConicalSurface`, `ToroidalSurface`, `ExtrusionSurface`, `RevolutionSurface`, `RuledSurface`, `NurbsSurface` — with analytics **first-class, not NURBS in disguise**. | Not started (E2) |
| FR-50 | Index-based BRep topology (`BrepVertex`, `BrepEdge`, `BrepTrim`, `BrepLoop`, `BrepFace`, `BrepShell`, `Brep`) with `readonly ref struct` navigator views for ergonomics. | Not started (E2) |
| FR-51 | `Mesh` with indexed vertices, tri and quad faces, optional normals, UVs and colours, and lazily built halfedge adjacency. Plus `PointCloud` and `GeometryGroup`. | Not started (E2) |
| FR-52 | Modelling: extrude, revolve, loft, sweep; sew, heal, validate. **Behind the seam, provided by OCCT** (**ADR-0020**). The managed implementations are discarded, not descoped. | Not started (E13) |
| FR-52b | **Exact solid operations: boolean union, difference and intersection; split and trim; fillet, chamfer, shell, thicken and draft.** The 70 members of [DYNAMO-COVERAGE §6.1](DYNAMO-COVERAGE.md#61-parity-on-solid-and-surface-commits-us-to-exact-solid-modelling) rest on these, and they are **in 1.0**. **ADR-0020**. | Not started (E13) |
| FR-53 | ~~**Robust mesh boolean**~~ — ported BVH plus adaptive-precision exact predicates, pure managed. **Moves to 1.x**, with `Capabilities` greying it until it lands. **This is a reduction, not an elimination, and must not be over-claimed as one:** OCCT is poor at mesh booleans and Dynamo has them, so the work keeps its purpose and loses only its urgency. | Not started (E2-T27), **1.x** |
| FR-54 | `IBrepKernel` seam with a `Capabilities` flag set. The node library **greys out unsupported operations rather than throwing**. **Amended by [ADR-0021](adr/0021-brep-kernel-residency.md): residency is canonical, not cached.** After a kernel operation the provider's representation is authoritative, our model is materialised lazily on structural demand, and there are exactly two crossings — `Import` and `Materialise`. Round-trip asserts **tolerance-bounded equivalence, never identity**. For 1.0 there is exactly **one** provider and a second is not planned. | Not started (E2-T28, E13) |
| FR-55 | Every kernel operation returns `Result<T>` carrying diagnostics and partial results. | Not started (E2) |
| FR-56 | `Tolerance { Linear, Angular, RelativeEpsilon }` is explicit and passed, defaults per call via `in Tolerance tol = default`, is scale-aware through `Tolerance.ForScale(characteristicLength)`, and is **hashed into every node's cache key**. | **Done in the kernel** (E2); the cache-key half is `Not started` (E3). `Tolerance` exists with all three components, the zero-`Linear` sentinel, `ForScale` and `Scaled`, and `in Tolerance tolerance = default` on every predicate in the assembly. There is no `EvaluationContext` yet, so "the default" is currently a fixed set of components rather than one flowing from a document — see [NOTES.md N9](NOTES.md) |
| FR-57 | Geometry serialization: source-generated `System.Text.Json` with polymorphic discriminators and **per-type `schemaVersion`**, plus a compact binary `.sparkgeo` for bulk data. | Not started (E2) |
| FR-58 | Interchange: OBJ, STL and PLY read and write; glTF write. **These stay ours and are not delegated to OCCT**, because they must work in a build with no native component at all — M1's demoable is `spark` writing an OBJ polyline, which lands long before anything native exists. | Not started (E2) |
| FR-59 | STEP read and write. **Widened and de-risked by ADR-0020**: OCCT gives AP203, AP214 and **AP242** with assemblies, names, colours and units, plus IGES, so the *documented subset* qualifier is gone and **R12 retires**. What survives is the validation discipline — a public corpus and a **third-party viewer, never our own reader** — because *OCCT wrote it* is not evidence that our use of it is correct. | Not started (E13-T12) |
| FR-60 | `Spark.Geometry.Planar`: `Point2d`/`Curve2d`, `Region`, and the Clipper2-backed boolean, offset and simplify pipeline, bridged by `Plane.To2d`/`To3d`. Not a peer 2D API. | Not started (E2) |
| FR-81 | **Capability parity with Dynamo's geometry.** A person who knows Dynamo must never reach for a geometric capability in Spark and find it absent. Parity is of **capability**, not of type names, method names, parameter order, degenerate-case behaviour or tolerances — those are ours to choose, and **D8** removes any obligation to match them. The reference surface is `ProtoGeometry.dll` as installed with Revit 2026: **51 public types, 837 public members**. Progress is tracked member by member in [DYNAMO-COVERAGE.md](DYNAMO-COVERAGE.md) and held true by a two-way diff test against a checked-in manifest, so the register cannot drift from the code (E11-T23). | **92 of 837 reachable — 11.0%** (E2-T40 … E2-T46). 16.0% of the 575 members committed to, once §5's refusals and the undecided T-Spline surface are excluded. All 92 are in the value layer; there are no curves, surfaces, solids, meshes or topology. **Q11 is answered**: the parity promise moves exact solid booleans into 1.0, and they come from OpenCascade (**D2**, **D15**, [ADR-0020](adr/0020-occt-via-c-abi-shim.md)). **Q12 is still open**, and ADR-0020 does not touch it — OCCT has no subdivision modeller either |

### UI, viewport and tools

| ID | Requirement | Status |
|---|---|---|
| FR-61 | The node canvas is **immediate-mode rendering over a retained `SceneIndex`, one Avalonia control for the whole canvas**, with a hybrid overlay giving a real control to the node currently being interacted with. | Not started (E8) |
| FR-62 | Pan, zoom, box select, drag, wire, delete, group, note and align, with LOD below 40% zoom. | Not started (E8) |
| FR-63 | Undo and redo across every graph edit. | Not started (E8) |
| FR-64 | Docking via `Dock.Avalonia` with a serialisable layout model, *reset layout* and named workspace presets. | Not started (E8) |
| FR-65 | Watch nodes and preview bubbles showing a node's output, including its rank. | Not started (E8) |
| FR-66 | A 3D viewport behind `IViewportRenderer`, with an OpenGL 3.3 core backend on Avalonia's `OpenGlControlBase` and a **software fallback**. | Not started (E9) |
| FR-67 | Geometry reaches the viewport as immutable `RenderPackage { NodeId, PortIndex, ElementPath, Positions, Normals, Indices, EdgeIndices, Appearance }`, one GPU buffer set per `(NodeId, PortIndex)`, tessellated in parallel and streamed during a run. | Not started (E9) |
| FR-68 | Selection is synchronised between canvas and viewport, falling out of node-keyed identity with no extra bookkeeping. | Not started (E9) |
| FR-69 | Style is an explicit wrapper — `Appearance` and `Displayable(Geometry, Appearance)` in `Spark.Api`, applied by a `Display.ByGeometryColor` node. Unwrapped geometry renders with defaults. | Not started (E5, E9) |
| FR-70 | `spark run`, `check`, `render`, `export`, `pkg`, `docs` and `graph`, as a `spark.exe` shipping beside the desktop application. | Not started (E12) |
| FR-71 | `Spark.Host` runs inside a Revit or AutoCAD add-in through the host-thread scheduler and `IHostServices`. | Not started (E12) |
| FR-72 | Aggressive autosave and crash recovery. | Not started (E8) |
| FR-73 | A signed Inno Setup installer and a portable zip for Windows. | Not started (E12) |

### Diagnostics and help

| ID | Requirement | Status |
|---|---|---|
| FR-74 | `SparkDiagnostic { Severity, Code, Message, Detail, NodeId, PortIndex, ElementPath, HelpTopicId }`, with codes of the form `SPK1042`. | Not started (E3) |
| FR-75 | Warnings mean output-with-caveats and downstream still evaluates. Errors mean no output, and **downstream is greyed as *not evaluated*, never cascaded as errors**. | Not started (E3) |
| FR-76 | Every `SPK####` code in source has a help topic, asserted by a source-scanning test. | Not started (E10, E11) |
| FR-77 | F1 opens the help topic for the selected node; hand-written topics come first, generated reference is the drill-down. | Not started (E10) |
| FR-78 | Every help topic contains a worked example, enforced by the harness. | Not started (E10) |
| FR-79 | Worked example graphs are real `.spark` files, openable from the help panel and **executed headlessly in CI**. | Not started (E10) |
| FR-80 | API reference pages are generated from XML documentation comments. Nobody writes them; nobody can forget them. | Not started (E10) |

## 7. Non-functional requirements

| ID | Requirement | Status |
|---|---|---|
| NFR-1 | The canvas holds 60 fps while panning and zooming a 2000-node graph. | Not started — the M1.5 spike exists to answer this before it is designed around |
| NFR-2 | Node invocation is an expression-tree-compiled delegate, never `MethodInfo.Invoke`. Under replication over 100k items the reflection path is 50–100× slower, which would make lacing unusable. | Not started |
| NFR-3 | `SparkList` marshalling to and from declared collection types carries a standing benchmark; it is the performance-critical path of the whole engine. | Not started |
| NFR-4 | The evaluation cache is LRU with a memory budget, evicted by last use and estimated size — **and the budget has two halves, managed and native**. A `Brep` resident in the provider (**ADR-0021**) holds OCCT heap that no managed size estimator can see, so a graph caching 200 of them may hold gigabytes while reporting megabytes. The native half is a budget **reported by the shim**, not inferred. | Not started. **The single-budget version of this requirement is wrong as it was specified**, and is recorded as changed rather than quietly corrected |
| NFR-5 | `Spark.Geometry`'s published output contains **no native binaries**, asserted by CI. *Published* here means the `dotnet publish` directory, never nuget.org — nothing is packaged (**D11**). **Unchanged by ADR-0020.** The OCCT dependency lives in `Spark.Geometry.Occt`, a separate assembly; `Spark.Geometry` stays pure managed and independently distributable, and the CI assertion is untouched. | Not started |
| NFR-5b | **The native component is confined to `Spark.Geometry.Occt` and `native/spark_occt`.** No other assembly P/Invokes the shim, no other assembly observes a native handle, and `Spark.Geometry.Occt` is referenced only by composition roots. Asserted by an architecture test that is a **companion** to `SparkGeometryTakesNoThirdPartyDependencyBeyondClipper`, never a relaxation of it. **ADR-0020**, **ADR-0021**. | Not started (E13) |
| NFR-6 | Every change to the public surface of `Spark.Api` or `Spark.Geometry` is visible in a checked-in public-API baseline diff, and a breaking one is a recorded decision with a release note rather than a discovery. Adding is preferred to changing; the baselines are a **review aid, not a compatibility guarantee**. **ADR-0019**, which supersedes ADR-0009's strictly-additive rule. | **Done for the mechanism** — `Microsoft.CodeAnalysis.PublicApiAnalyzers [5.6.0]` is referenced from `Directory.Build.props` for all four contract projects, each with a `PublicAPI.Shipped.txt` and a `PublicAPI.Unshipped.txt`. `Spark.Geometry` declares 387 public members; the other three surfaces are empty. RS0016 is at error severity and **was proved to fire**, not assumed to. The *release note* half awaits a release |
| NFR-7 | A graph containing no script nodes never loads `Spark.Scripting`, so Roslyn cold start is not paid by users who do not script. Background warm-up on idle covers the rest. | Not started |
| NFR-8 | Tessellation of a closed solid is watertight — a property-based test, not a spot check. **Caveat, and it is not a small one: under ADR-0021 the tessellation of a `Brep` happens behind the seam, so this property now tests a third party's mesher.** OCCT's mesher is not guaranteed watertight at default deflection. The requirement stands; what must be settled is whether it holds against OCCT's output at a deflection we choose, or whether it is restated to say precisely what it guarantees. **It must not quietly become a suppressed test.** Mesh tessellation stays ours and is unaffected. | Not started (E13-T11) |
| NFR-9 | Tolerance is scale-aware from the first release of the kernel, not retrofitted. A fixed `1e-6` is wrong for kilometres and wrong for microns. | **Done for the value layer** — `Tolerance.ForScale` and `Scaled` exist, and every geometric `EqualsWithin` in `Spark.Geometry` routes through one hybrid absolute/relative rule (`IsNegligible`) so a comparison keeps meaning at 1e9 as well as at 1e-9. Property generators span that whole range (ADR-0018). Curves, surfaces and meshes must be built to the same rule as they arrive; the requirement is about *not retrofitting*, so it is never fully closed until the kernel is |
| NFR-10 | Undocumented public API on `Spark.Api`, `Spark.Geometry`, `Spark.Geometry.Io` or `Spark.Nodes.Core` fails the build (CS1591 promoted to error). | **Done** — wired in `Directory.Build.props` |
| NFR-11 | The build is clean with `-warnaserror` on Windows and Linux. | **Partly done** — on Windows, on 2026-08-27, `dotnet build Spark.slnx --no-incremental -warnaserror` is clean over all sixteen projects and `dotnet format Spark.slnx --verify-no-changes --severity warn` is clean over the whole solution, kernel included; the IDE1006 findings outstanding at the last revision are closed. **Verify with `--no-incremental` or not at all**: an incremental build can print "0 warnings" from a cached analysis, which is how the public-API findings stayed hidden ([NOTES.md N15](NOTES.md)). CI has been green on both platforms for **earlier commits** and has never run against the kernel, so the Linux half of this row is untested |
| NFR-12 | The software renderer is deterministic, so `spark render` is usable for CI visual regression. GPU output is not testable; software output is. | Not started |
| NFR-13 | No telemetry of any kind in v1. Opt-in crash reporting is considered post-1.0, with graphs excluded from any payload. | **Done by construction** — nothing collects anything |
| NFR-14 | Every package version is pinned exactly; there are no floating ranges. | **Done** — `Directory.Packages.props` |
| NFR-15 | No `-windows` target framework anywhere, and no unsafe code — **with exactly one named exception**. The `LibraryImport` source generator emits unsafe code and requires `AllowUnsafeBlocks=true`, so `Spark.Geometry.Occt` opts in, in its own csproj, with a comment naming **ADR-0020**. The repository default stays `false`, and an architecture test asserts that project is the **only** one opting in. The `-windows` half has no exception at all: the C-ABI shim exists precisely so that it does not need one. | **Done** — `net10.0` and `AllowUnsafeBlocks=false` in `Directory.Build.props`, and the `-windows` half is now **enforced by a passing test** rather than by vigilance (`Spark.Architecture.Tests`). The named exception and the test that bounds it are **Not started** (E13-T4) |

## 8. Constraints and dependencies

Every version below is pinned exactly in
[`Directory.Packages.props`](../Directory.Packages.props). Bumping one is a deliberate,
reviewed change — see [AGENTS.md](../AGENTS.md) for why.

| Item | Value | Note |
|---|---|---|
| SDK | `10.0.100`, `rollForward: latestFeature`, pinned in [`global.json`](../global.json) | Also selects `Microsoft.Testing.Platform` as the test runner. Not a preference: the .NET 10 SDK has removed the VSTest bridge, so a VSTest-shaped test project fails at build. `actions/setup-dotnet` reads this file, so one pin serves everyone. [NOTES.md N11](NOTES.md). |
| Solution format | `Spark.slnx`, not `Spark.sln` | The .NET 10 default, and the diffable one. [NOTES.md N1](NOTES.md). |
| Target framework | `net10.0`, every project including tests | No `-windows` TFM anywhere. **D7**. Enforced by `Spark.Architecture.Tests`. |
| Language / nullability | `latest`, nullable enabled, `WarningsAsErrors=nullable` | Warnings are errors in CI only, not in the csproj. |
| Implicit usings | **Disabled** | Explicit usings matter for a library people script against in code blocks. |
| Unsafe code | `AllowUnsafeBlocks=false` repository-wide, **with one named exception: `Spark.Geometry.Occt`** | `Span<T>`, `ref struct` and `System.Numerics` cover the kernel. The exception exists because the `LibraryImport` source generator *emits* unsafe code and will not run without it; it is opted into in that one csproj with a comment naming ADR-0020, and an architecture test asserts it is the only project doing so. **NFR-15**. |
| Versioning | MinVer **`[7.0.0]`**, SemVer, tag prefix `v` | Embedders reference `Spark.Host` and node authors reference `Spark.Api` from an install, and both need *does upgrading break me?* answerable from the number, which CalVer cannot do. **D11**, ADR-0007. |
| Public API baselines | `Microsoft.CodeAnalysis.PublicApiAnalyzers` **`[5.6.0]`** | The mechanism behind NFR-6, kept as a review aid rather than a compatibility guarantee. **Live** on the four contract projects; RS0016 at error, **RS0026 suppressed** with the reasoning in `.editorconfig` — it protects a source-compatibility promise Spark no longer makes after ADR-0019. |
| Solid modelling | **OpenCascade Technology 8.0.1** (July 2026), built from a **pinned source tag via a vcpkg manifest** — not from nuget.org | **LGPL-2.1 with the Open CASCADE exception.** Linked dynamically, shipped as unmodified replaceable shared libraries, with the LGPL text, the exception text and prominent notice in the About box, README, installer and release notes. Any modification is a numbered patch file, never an edited tree. **D15**, [ADR-0020](adr/0020-occt-via-c-abi-shim.md). **Nothing in this document is legal advice**, and six questions are with counsel — **Q13**. Not consumed from nuget.org because every OCCT package there is stranded at 7.8 or 7.9 while upstream is at 8.0.1. **Does not exist in the tree yet.** |
| Native shim | `native/spark_occt/`, C++, **MIT, ours** | A flat C ABI of an estimated **350–500 entry points** over ~2–3% of OCCT's class surface, called by `Spark.Geometry.Occt` through `LibraryImport`. Deliberately hand-written: a generator cannot *reduce* the ABI surface, and the upgrade strategy depends on that surface being small and chosen (**R17**). `catch(...)` in every entry point and `OSD::SetSignal(false)` (**R19**). **Does not exist in the tree yet.** |
| Native build cache | Keyed on `(occt-tag, vcpkg-baseline, shim-source-hash, rid)` | The mechanism that keeps ADR-0001's Linux rot-guard alive now that it must build native code: steady-state CI downloads the cached artefact and builds only the shim, with the from-clean build nightly. **Without it the rot-guard will not survive a busy PR queue.** Also serves **R22**. Note that OCCT's own CI has **no ARM64 leg** and covers macOS on x64 only. |
| Planar geometry | Clipper2 **`[2.0.0]`** | The **only** third-party dependency `Spark.Geometry` may take — and it does not take it at present. **This is unchanged by ADR-0020**: OCCT is a dependency of `Spark.Geometry.Occt`, a different assembly, and the architecture test guarding this rule gains a *companion*, never a relaxation (**NFR-5b**). The `PackageReference` was removed once it proved unused, leaving the assembly on the BCL alone, and **returns with the planar boolean pipeline**; the version stays pinned meanwhile. Its C# distribution is pure managed and Boost-licensed. Isolated behind one internal file so the no-native-dependencies promise stays checkable. |
| Roslyn | `Microsoft.CodeAnalysis.CSharp`, `.Scripting`, `.Workspaces`, `.Features`, all **`[5.9.0]`** | Confined to `Spark.Scripting`. A floating Roslyn is how CADScript's pinning problem arose. |
| NuGet client | `NuGet.Protocol`, `NuGet.Packaging`, both **`[7.9.0]`** | Confined to `Spark.Packages`. Reusing NuGet wholesale rather than building a registry. |
| UI | Avalonia, `.Desktop`, `.Themes.Fluent`, `.Fonts.Inter` all **`[12.1.1]`**; `Avalonia.AvaloniaEdit` **`[12.0.0]`**; `Dock.Avalonia` and `Dock.Model.Mvvm` **`[12.1.0.4]`**; `CommunityToolkit.Mvvm` **`[8.4.2]`** | Avalonia, not WPF — none of the WPF prior art's UI ports directly. **D1**. MVVM by source generator, not ReactiveUI. |
| Testing | `xunit.v3` **`[4.0.0]`**, `xunit.runner.visualstudio` **`[4.0.0]`**, `Microsoft.NET.Test.Sdk` **`[18.9.0]`**, `Avalonia.Headless.XUnit` **`[12.1.1]`**, CsCheck **`[4.8.0]`** | `xunit.v3` is consumed by both test projects through `tests/Directory.Build.props` and runs eleven tests green. The other four are pinned and still unreferenced. Property-based tests on the kernel from M1 are non-negotiable. |
| Benchmarking | BenchmarkDotNet **`[0.15.8]`** | Nightly, not per-PR — shared runners are too noisy for per-PR benchmarking. |
| C2VGeometry | `DoodleSharp\C2VGeometry\`, net9.0, ~20,300 lines | A 2D **drawing** library, not a kernel. Harvested selectively; see [EPICS.md E2](EPICS.md#e2--geometry-kernel). `Code2Viz` contains a single 0-byte file and is not an ancestor. |
| NuGet publishing | **None.** `IsPackable` is `false` for every project | Spark consumes NuGet packages and loose DLLs and produces neither. No `PackageId`, no `PackAsTool`, no package metadata anywhere; the reasoning is commented in [`Directory.Build.props`](../Directory.Build.props) and recorded as [NOTES.md N14](NOTES.md). A project's assembly name is therefore its only name — there are no package-ID renames — and `Spark.Cli` builds `spark.exe` beside the desktop application rather than installing as a global tool. **D11**. |

## 9. Out of scope

- **Reading or writing `.dyn`.** Not a capability gap — a deliberate refusal. See **D8**.
- **Units and unit conversion.** No `UnitSystem`, no unit types. This does *not* remove
  scale-aware tolerance, which is numerical robustness rather than units. See **D12**.
- **Drafting and annotation** — dimensions, hatches, text, arrows, grids, spatial cells.
  Not concepts Dynamo has. Not salvaged from C2VGeometry, not parked for later. **D13**.
- **~~Exact NURBS booleans, and fillet and chamfer on solids.~~ This is reversed by
  ADR-0020 and is recorded rather than deleted, because the reversal is the whole point.**
  These were post-1.0 and stated publicly, with 1.0 shipping on mesh booleans. They are now
  **in 1.0**, delivered by OpenCascade behind `IBrepKernel`, and what was going to be a
  research programme is integration work. The line that replaces it is the next one.
- **A managed exact-boolean kernel of our own.** Not descoped — **discarded**. So are the
  managed STEP subset, the throwaway SSI spike, managed fillet, chamfer, shell, thicken and
  draft, managed sew, heal and validate, and managed BRep tessellation. **ADR-0020**.
- **Robust mesh booleans at 1.0.** They move to **1.x**, with `Capabilities` greying the
  operation out until they land. This is a reduction, not an elimination, and it must not be
  over-claimed as one: **OCCT is poor at mesh booleans and Dynamo has them**, so the work
  keeps its purpose and loses only its urgency.
- **A second `IBrepKernel` provider.** There is exactly one, and building a second to justify
  the abstraction is explicitly not wanted. The seam is retained for `Result<T>`,
  `Capabilities` and insurance. **ADR-0021**.
- **A single-file publish that seals the native libraries in, and NativeAOT over OCCT.**
  Excluded by the LGPL relink obligation rather than by preference. This constrains E12-T8.
  *Nothing in this document is legal advice; see ADR-0020's licensing section and the counsel
  questions in §14.*
- **`.3dm` (Rhino) interoperability.** Post-1.0. OpenNURBS is MIT and would carry no licence
  obligations, and reading and writing `.3dm` is a pure addition to this plan rather than an
  alternative to it — which is why it is parked here rather than argued about in ADR-0020.
- **Live package hot-swap as a guarantee.** Restart is the documented default; live unload
  is a best-effort optimisation.
- **An out-of-process script worker.** Kept viable by the scheduler and ALC seams,
  deferred past v1 — **but it now serves two risks rather than one**. R11 wanted it for user
  C# taking down the process; R19 wants it because a C++ exception unwinding into managed
  frames is undefined behaviour. Two risks pointing at one mechanism is an argument for
  bringing it forward, and it has not yet been made.
- **macOS and Linux release artefacts.** Linux is built and tested in CI; nothing is
  published for it. macOS is not built at all.
- **A Spark package registry.** NuGet is the registry.
- **Publishing Spark's own assemblies to nuget.org.** Spark consumes NuGet packages; it
  produces none. Embedders and node authors reference the assemblies from an install. **D11**,
  [NOTES.md N14](NOTES.md).

## 10. Success measures

| Measure | Target |
|---|---|
| Autodesk software required to run Spark | None. This is the whole point |
| Native binaries in `Spark.Geometry`'s published output | Zero, asserted by CI |
| Built-in nodes with no help topic | Zero, or listed as deliberately undocumented with a reason. A new node shipping undocumented fails the build |
| Help topics with no worked example | Zero, harness-enforced |
| Example graphs that do not execute in CI | Zero |
| Public members unreachable as a node | Zero, or listed in an exclusions file with a reason. Enforced by a two-way test in both directions |
| Graphs damaged by opening them without a required package | Zero. Re-save must be byte-identical |
| Breaking changes to `Spark.Api` or `Spark.Geometry` during 1.x | Rare, and each one a recorded decision with a release note naming who has to recompile. Not a target of zero: **D11** removed the ecosystem that made zero worth its cost. Zero *unrecorded* ones is the real target |
| Canvas frame rate at 2000 nodes | 60 fps |
| Lacing corpus rows passing | Every row of the case table, asserting value **and** rank separately. Not a fixed count: the table grows, and a target expressed as a number would be met by not adding rows |

## 11. Release plan

Milestones, not dates. Each is independently demoable and independently documented — that
is what makes a multi-year effort directed by one person survivable. Sizes are estimates.

| M | Name | Demoable at the end | Size |
|---|---|---|---|
| **M0** | Foundations and docs | CI green on GitHub over an empty solution; the architecture and documentation gates passing; `docs/` renders | 1–2 wk |
| **M1** | Geometry core | ~500 passing tests; `spark` writes an OBJ polyline a third-party viewer opens | 3–4 wk |
| **M1.5** | De-risk spike — **throwaway, deleted afterwards** | A go/no-go on three architectural bets | 1 wk |
| **M1.6** | **OCCT de-risk spike** — new, and it gates ADR-0020 the way M1.5 gates ADR-0001 | OCCT built from a pinned tag via vcpkg on Windows **and** Linux; one boolean driven through a minimal `spark_occt` and `LibraryImport`; **the per-RID binary footprint measured rather than bracketed** | 2 wk |
| **M2** | Walking skeleton and lacing | **Drag two nodes, wire them, see geometry in the viewport — and it laces over lists** | 10–12 wk |
| **M3** | NURBS curves | A real parametric curve graph | 6 wk |
| **M4** | C# code block | **Type C# in a node and get IntelliSense that knows the type on the incoming wire; drag a slider and watch it recompute live** | 5 wk |
| **M5** | Surfaces and mesh | A shaded 3D model built parametrically | 7 wk *(was 8; the throwaway SSI spike is gone)* |
| **M6** | BRep, modelling and **exact** solid operations | **Solids that can be combined, filleted, shelled, trimmed and exported to STEP** | **20–24 wk** *(was 14)* |
| **M7** | Packages and extensibility | Install a package from nuget.org and use its nodes; open a graph missing a package and lose nothing | 8 wk |
| **M8** | Embedding and 1.0 | **1.0** | 7 wk *(was 8; the managed STEP subset is gone)* |

**The cost of D2, stated as two numbers because one number would mislead.**

**Against the plan as written: +7 to +11 weeks.** The itemised deltas above are +2 for the new
M1.6, −1 at M5, +6 to +10 at M6 and −1 at M8, which accounts for **+6 to +10**; the headline
range carries one further week that is not yet attached to a named milestone, and it is
recorded that way rather than distributed to make the arithmetic tidy. The work is
[EPICS.md E13](EPICS.md#e13--occt-provider), roughly 24 weeks, most of it landing inside M6.

**Against what the client actually asked for: it saves years, and it retires R1.**

Both are true. The intuitive expectation is *buy rather than build, therefore cheaper*, and it
is wrong here for exactly one reason: **the plan as written never contained the expensive
thing.** M6's 14 weeks bought mesh booleans. Exact booleans, fillet, chamfer and trim were in
§9's out-of-scope list — post-1.0, possibly never. Parity was never funded, and this is what
funding it looks like, in the cheapest form available.

**M2 is the walking skeleton and the highest-information milestone.** It simultaneously
validates Avalonia GL, the canvas rendering strategy, the reflection importer, the lacing
engine and the layering split — the five things that could still force an architectural
change. It is attacked immediately after M1.

**Lacing is folded into M2 rather than deferred**, because a graph engine without
replication is a toy to an AEC user, and retrofitting rank semantics into a shipped
evaluator is far more expensive than building them in.

**M1.5 is deliberately throwaway.** Its three spikes have pass/fail criteria written into
[TASKS.md](TASKS.md) *before* M1 starts, so the gate is honest rather than
retrospectively softened. A failed criterion changes the architecture, which is the point.

## 12. Risks

| # | Risk | Impact | Mitigation |
|---|---|---|---|
| R1 | ~~**Exact NURBS surface-surface intersection is a research-grade problem.**~~ **Retired by [ADR-0020](adr/0020-occt-via-c-abi-shim.md).** We are not writing it. OpenCascade has it, and the fallback the seam was designed to absorb has become the plan | — | **This is the largest single risk reduction available to the project, and it is worth stating in those terms.** Retired rather than deleted, because the reason it existed explains most of ADR-0002's shape and half of E2's staging. What does *not* retire with it is **R3**: numerical robustness has not gone away, it has changed owner — see R3 and **R18** |
| R2 | **Scope versus capacity.** A multi-year effort directed by one person | Existential | Every milestone independently demoable and releasable, with a runnable build shipped from M2 rather than only at 1.0; the `Spark.Api` boundary makes third-party node libraries a contribution path needing no kernel expertise |
| R3 | **Kernel numerical robustness** — tolerance-dependent code that passes the corpus and fails on real models at unusual scales. **ADR-0020 does not retire this risk; it changes its owner.** The value, curve and surface evaluation layers stay ours and this risk stays exactly as written for them. For solid operations the failure mode moves inside OCCT, where we can observe it and not fix it — that half is **R18** | High, and discovered late by default | Scale-aware `Tolerance` from M1 rather than retrofitted; property-based tests from M1; watertightness invariants; `Result<T>` so failures are diagnosable rather than silent; a regression corpus that grows with every bug. **Partly realised, and the first slice showed how the mitigation itself can fail:** a property whose generator never reaches the boundary it tests cannot fail and looks exactly like a passing test. Generators now span 1e-9 to 1e9 log-uniform (ADR-0018), and widening them found two more defects. [NOTES.md N18](NOTES.md) |
| R4 | **Lacing semantics get subtly wrong and then become unfixable**, because graphs depend on the wrong behaviour | Permanent | Specification-first: the case table is written as a help topic before implementation and used directly as the test corpus, and it has already earned its keep — writing it settled ten questions the plan left open and overturned one answer the plan had wrong (**D4**, `Auto`); `Disabled` mode always available; `graph.formatVersion` gates any semantics change so a fix never silently alters an existing graph |
| R5 | **The node canvas collapses above ~1000 nodes**, which real graphs exceed | Would force a UI rewrite | Immediate-mode plus `SceneIndex` chosen precisely for this; M1.5 spike with 2000 synthetic nodes; LOD below 40% zoom; benchmarked nightly from M2 |
| R6 | **The Avalonia GL viewport fails or degrades** — driver variance, RDP, virtual machines | Would strand the 3D story | M1.5 spike before committing; the `IViewportRenderer` seam; a software fallback with independent value in headless thumbnails and CI visual regression |
| R7 | **`Spark.Api` or `Spark.Geometry` need a breaking change after users have compiled node DLLs against them.** They cannot be side-by-sided, so a break means every such DLL is recompiled or dropped | Moderate, and per-user rather than ecosystem-wide — see **D11** | Public-API baselines from M0 so the change is visible in the diff that approves it; keep `Spark.Api` deliberately *small* — it is a contract, not a convenience library; prefer adding an interface to changing one; when a break is right, record it and name it in the release notes (**ADR-0019**) |
| R8 | **ALC unloading never works in practice**, so upgrades always need a restart | Low, because it is already the promise | Restart is the documented default. Honest messaging beats a broken promise |
| R9 | **Roslyn cold start makes code blocks feel sluggish** | Undermines the headline feature | `Spark.Scripting` isolated so graphs without scripts never load it; background warm-up on idle; persistent compiled-assembly cache; resident cache for input changes |
| R10 | **The C2VGeometry test harvest sprawls** into a multi-week rewrite | Eats M1 | Timeboxed to one week, hard stop. Harvest only pure-maths-on-values tests; anything needing a `Shape` is discarded without argument |
| R11 | **User C# takes down the process** via `StackOverflowException`, which .NET cannot catch | Data loss | Guard weaving reduces frequency; aggressive autosave and crash recovery limit damage; an out-of-process worker is kept viable by the scheduler seam and deferred past v1 |
| R12 | ~~**STEP is much bigger than budgeted**~~ **Retired by [ADR-0020](adr/0020-occt-via-c-abi-shim.md).** We are not writing a STEP reader or writer. OCCT gives AP203, AP214 and **AP242** — with assemblies, names, colours and units — plus IGES, and it comes with the engine rather than as extra work | — | Retired rather than deleted. The validation discipline survives and moves to E13-T12: still validated against a public corpus and a **third-party viewer, never our own reader**, because *OCCT wrote it* is not evidence that our use of it is correct. **Q7** survives in a downgraded form for the same reason |
| R13 | **Spark's positioning is undermined by its own dependency story.** Reframed, and considerably enlarged, by [ADR-0020](adr/0020-occt-via-c-abi-shim.md). Spark exists because Dynamo Sandbox *forces users to have an Autodesk product installed*, and because solving that by acquiring a different heavyweight dependency would move the problem rather than remove it. **Spark now acquires a heavyweight native dependency.** Clipper2, which is what this risk used to be about, is the smallest instance of it | **Reputational, and this is the consequence with no technical fix.** The failure mode is not that the distinction is indefensible — it is that somebody else frames it first | The distinction is real: OCCT is open source, freely redistributable, installed **with** Spark, and needs no account, no licence purchase and no other vendor's product. **But it only holds if we say it first, clearly, in our own words**, which is why the README carries the positioning paragraph in the same change as the decision rather than as an M8 documentation task. Supporting: OCCT ships in the default install (a Dynamo user finding booleans greyed out on first run is what FR-81 forbids); `Spark.Geometry` stays pure managed and independently distributable; **NFR-5 stands unchanged** with its CI assertion untouched; and the three facts are stated in one paragraph rather than three places. Clipper2's half is unchanged: pure managed, Boost-licensed, pinned, isolated behind one internal file, **not currently referenced at all**, returning with the planar pipeline (`E2-T14`), and guarded by a ceiling rather than an exact set |
| R14 | **Resolved by client decision.** Recorded as [ADR-0020](adr/0020-occt-via-c-abi-shim.md), as **D2** and **D15** in §13, and as the answers to **Q6** and **Q11** in §14. Presented with the three paths this row sets out, the client chose the third: take an existing engine deliberately rather than as a contingency. The 70 members are **in 1.0**, delivered by OpenCascade behind `IBrepKernel`. **The recommendation this row makes — scope parity to the end of 1.x — is not the path that was taken**, and the row is left otherwise unedited so that the argument actually had is legible six months from now. *The original entry follows.* **Capability parity commits us to exact solid booleans, which are currently out of scope for 1.0.** FR-81's promise is that a Dynamo user never finds a capability absent. **32 members of ProtoGeometry cannot exist without exact BRep booleans, trimming, filleting and sewing** — `Solid.Union`, `UnionAll`, `ByUnion`, `Difference`, `DifferenceAll`, `Fillet`, `Chamfer`, `ThinShell`, `Separate`, `Repair`, `ByJoinedSurfaces`, `ProjectInputOnto`; `Surface.ByUnion`, `Difference`, `SubtractFrom`, `TrimWithEdgeLoops` ×2, `Join` ×2, `Thicken` ×2, `Offset`, `Repair`, `ProjectInputOnto`; `PolySurface.Fillet`, `Chamfer`, `ByJoinedSurfaces`; and `Geometry.Intersect`, `IntersectAll`, `DoesIntersect`, `Split`, `Trim` — with a further 38 modelling and intersection members behind the same `IBrepKernel` seam. **This is R1's problem wearing a requirement's clothes**, and §9 currently puts it post-1.0 in writing | Directly contradicts §9, E12-T15 and the M6 estimate. Left unstated, it surfaces at M6 as a slipped milestone rather than as a decision | **Name the contradiction rather than absorb it.** Recommendation: scope FR-81's promise to *the end of 1.x* rather than *at 1.0*, so 1.0 still ships on mesh booleans (E2-T27) with `Capabilities` greying out what is absent, and R1's mitigation survives intact. The alternatives are to accept exact booleans into 1.0 — which requires retiring R1, not mitigating it — or to promote the OCCT-backed optional package from fallback to a shipped option, which is **Q6**. This is a client decision because it trades the headline promise against the release date. **Q11**, E2-T47, [DYNAMO-COVERAGE §6.1](DYNAMO-COVERAGE.md#61-parity-on-solid-and-surface-commits-us-to-exact-solid-modelling) |
| R15 | **Native binary distribution.** A per-RID build matrix, an installer that grows, code signing for binaries we did not compile from our own source tree, and antivirus false positives on freshly signed unfamiliar native DLLs | Slips the release, and lands on the one person who does releases | Build once per RID as a cached artefact; ship OCCT as unmodified replaceable shared libraries, which the licence requires anyway. **The installer size is bracketed at 40–160 MB uncompressed and has not been measured** — M1.6 measures it, and until then the bracket is the honest statement. Excluding the Visualization module may remove FreeType; whether it does is open |
| R16 | **Debugging across the boundary.** A boolean that throws is easy. **A boolean that returns a wrong-but-valid shape is diagnosable only inside code we do not own** | High, and it is the risk most likely to be underestimated by anyone who has not done it | Pipe OCCT's `Message_Report` into `SparkDiagnostic` so its own complaints reach the user; run `BRepCheck_Analyzer` in Debug builds; and **attach a Draw-Harness-compatible `.brep` dump to every failure**, so a bug reproduces upstream in the form the maintainers accept. That last one is what converts *we cannot fix this* into *somebody can* |
| R17 | **Version upgrades.** A pinned OCCT tag is a pinned version of somebody else's numerics, and moving it can change results | Moderate, recurring | **The warning is already in the evidence:** OCCT is at 8.0.1 and every OCCT package on nuget.org is stranded at 7.8 or 7.9 — that ecosystem did not decide to stop, it stopped. Building from a pinned source tag via a vcpkg manifest keeps the choice ours. The mitigation that matters is the **small, deliberately chosen ABI surface**: 350–500 entry points is an upgrade we can read, and a generated binding would not be |
| R18 | **OCCT's own numerical failure modes.** Booleans on tangency, fillet on complex vertex blends. These are known-hard cases and a mature kernel still fails them | Moderate. **This is R3 with a different owner, not a new problem** | `Result<T>` exists for exactly this and was designed before we knew who would need it; `Capabilities` makes an absent operation visible rather than thrown; the regression corpus grows with every failure and each entry carries its `.brep` dump (R16). **R3 does not retire** |
| R19 | **Process crash at the boundary.** C++ exceptions unwinding into managed frames are **undefined behaviour**, and OCCT signals as well as throwing | Data loss, and indistinguishable from R11 to a user | `catch(...)` in **every** entry point, translating to a status code and never letting an exception cross; `OSD::SetSignal(false)` so OCCT does not install handlers that fight the CLR's. **The out-of-process worker now serves R11 and R19 together**, which is the strongest argument yet for bringing it forward from *deferred past v1* |
| R20 | **Threading.** Whether the parallel evaluator may call the shim concurrently, and at what granularity, is **not known** | **A top-three risk**, and unresolved | Nothing to claim yet. OCCT's documented thread-safety guidance is thin; the envelope must be established by reading the source of the specific packages we call and by stressing the shim at the evaluator's real thread count. M1.6 starts this, and a conservative single-writer policy is the fallback if the answer is bad. **Recorded as unresolved rather than mitigated**, because it is |
| R21 | **Licence obligations constrain the publish pipeline permanently.** Dynamic linking only, replaceable shared libraries, no single-file seal, no NativeAOT over OCCT, notice in four places, a standing source offer, and any modification kept as a numbered patch | Moderate, and it is a *standing* obligation rather than a one-off task | The obligations are enumerated in [ADR-0020](adr/0020-occt-via-c-abi-shim.md) and are met in the release pipeline (E13-T16) rather than by remembering. **Six questions go to counsel and two of them must be answered before M6** — see **Q13**. *Nothing in this document is legal advice* |
| R22 | **Build reproducibility.** The artefact a user installs must be traceable to a source state: an OCCT tag, a vcpkg baseline, a shim commit and a toolchain | Moderate, and it compounds quietly | The cache key is the mechanism and it is the same key either way — `(occt-tag, vcpkg-baseline, shim-source-hash, rid)`. Record it in the build output and in the About box, so the source offer under R21 can be honoured against a specific artefact rather than approximately |

## 13. Decision log

Fifteen decisions, each with the alternative that was rejected and why. Anything that
could have gone differently also gets an ADR under `docs/adr/`; this table is the index.

**One decision in this table has reversed, and it is the largest one in the project.** **D2**
previously chose a pure-managed kernel of our own and named wrapping OpenCascade as the
alternative it beat. It now chooses the opposite. The row is **rewritten rather than
annotated**, because a decision log records what is decided; the superseded reasoning is
preserved unedited in [ADR-0002](adr/0002-own-managed-geometry-kernel.md), which keeps its
number and its text and is now *Superseded by ADR-0020*. That is what the two mechanisms are
for, and using both is the point of having both.

| # | Decision | Alternative considered | Why |
|---|---|---|---|
| **D1** | **UI platform is Avalonia** | WPF | The prior art (RCS, CADScript, DoodleSharp) is WPF, so WPF looked cheaper. It is not: none of the WPF UI ports directly regardless, because those UIs are host-embedded palettes rather than an application shell, and WPF forecloses the Linux CI rot-guard that keeps D14 from quietly becoming a mistake. `AvaloniaEdit` is a close API port of AvalonEdit, so the editor controllers — the genuinely expensive part — transfer with moderate effort. |
| **D2** | **Use an existing solid-modelling kernel rather than writing one.** Exact BRep booleans, trimming, filleting, sewing and STEP come from an engine we did not write; evaluation, values, meshes, planar geometry and every interchange writer stay ours. **Reverses this row's previous decision.** [ADR-0020](adr/0020-occt-via-c-abi-shim.md) | Hold the previous staging and scope capability parity to *the end of 1.x* rather than *at 1.0* — which is what the PRD said, what R14 recommended, and what preserved every existing estimate | **The client was told the alternative and its price, and chose this one.** FR-81's instruction is that a Dynamo user must never reach for a geometric capability and find it absent, and [DYNAMO-COVERAGE §6.1](DYNAMO-COVERAGE.md#61-parity-on-solid-and-surface-commits-us-to-exact-solid-modelling) established that **70 members cannot exist without exact BRep booleans, trimming, filleting and sewing**. That is the work ADR-0002 staged last, R1 called research-grade, and §9 stated publicly as post-1.0. Deferring it to 1.x would have made the headline promise conditional on a research programme that might never land; writing it ourselves would have made 1.0 depend on it. Taking an engine is the only path that delivers the promise on a schedule anybody can plan against, and **it retires R1** — the largest risk in the project — rather than mitigating it again. **The cost must be read as two numbers, not one. Against the plan as written it is +7 to +11 weeks; against what was actually asked for it saves years.** Both are true, and the reason the first is positive is that **the plan as written never contained the expensive thing**: M6's 14 weeks bought mesh booleans, while exact booleans, fillet, chamfer and trim sat in the out-of-scope list. Parity was never funded, and this is what funding it looks like. |
| **D3** | **Idiomatic C# core with a `By*` façade.** Both `new Circle(c, r)` and `Circle.ByCenterRadius(c, r)` | Only constructors (idiomatic), or only `By*` factories (Dynamo-familiar) | Constructors alone lose the self-describing node names that make a node library searchable — `Circle.ByCenterRadius` tells you what it wants, `Circle` does not. `By*` alone makes the kernel unpleasant to script against in a code block, where `new` is what a C# developer types. Both, with the importer suppressing the duplicate, costs one dedup rule and a two-way test. Given **D8**, the `By*` names are for human recognition only and carry **no compatibility obligation**. |
| **D4** | **Documents and architecture first** | Prototype first, document later | The prototype-first order produces documentation that is a transcript of what was built rather than a specification of what should be. The lacing case table is the proof: written as a help topic before the engine exists, it is a design instrument and a test corpus; written afterwards it would be neither. It has already changed the design rather than described it — writing it out settled ten open questions and overturned the plan's definition of `Auto`, which under the model as specified would have been a mode that could never differ from `Longest`. That is a defect found by writing a document, at a cost of nothing. The docs harness is a build gate from M0, before there is anything to document, precisely so it can never be retrofitted. |
| **D5** | **Standalone now, embeddable by design.** The `.Api`/`.Engine`/`.Host` split exists from day one | Build standalone, extract a host layer later | The AEC audience's real workflow is inside Revit or AutoCAD, so embedding is not optional in the long run — and a layering split retrofitted after a UI exists is a rewrite, because by then the engine has learned to assume a UI thread, a file dialog and a settings store. Three project boundaries cost nothing to draw now. `IEvaluationScheduler`'s host-thread implementation is the entire mechanism, and it only works if evaluation never assumed it owned its thread. |
| **D6** | **MIT, open source, DCO sign-off** | A CLA, or a copyleft licence | A CLA asks a drive-by contributor to sign a legal document before a one-line typo fix, which loses more contributions than it protects. DCO is a line in the commit message and is what the Linux kernel uses. MIT rather than GPL because Spark wants to be embedded inside commercial CAD add-ins, which copyleft would prevent — and embedding is **D5**, a goal rather than an accident. |
| **D7** | **.NET 10 everywhere, test projects included. No `-windows` TFM** | Multi-target, or `net10.0-windows` for the desktop app | A `-windows` TFM anywhere in the graph poisons everything downstream of it and silently ends the Linux rot-guard. Avalonia does not need one. Multi-targeting doubles the build matrix to serve runtimes nobody has asked for. One TFM keeps the reference graph checkable by a source-scanning test rather than by vigilance. |
| **D8** | **No Dynamo compatibility, in either direction.** No `.dyn`, no importer, no seam | A `.dyn` importer, or a best-effort one with warnings | A `.dyn` file contains **no geometry** — it is JSON holding nodes, connectors and view state, with geometry existing only after evaluation. So reading one never requires ProtoGeometry, and reading was never the hard part. The hard part is **semantic equivalence**: guaranteeing Spark's circle node behaves identically to ProtoGeometry's in every degenerate case, tolerance and lacing rule. That is unprovable without the very dependency Spark exists to remove, and a silently mistranslating importer is worse than none. Dropping it also removes the one force that would have pulled Spark's API toward ProtoGeometry's semantics. Four graph-model properties survive on their own merits: a public graph-construction API (needed by the CLI, by tests and by collapse-to-custom-node), stable string `NodeKey`s separate from display names (save/load round-trip, search index), first-class lacing, and an unresolved-node placeholder. |
| **D9** | **Both audiences, AEC first.** AEC wins any direct UX conflict | .NET developers first, or AEC only | The two audiences want opposite defaults: a developer wants terseness and keyboard flow, an AEC designer wants discoverability and visible state. Refusing to rank them means losing both arguments repeatedly. AEC wins because they are the users with no alternative — a .NET developer can already write a console app. Naming the tiebreak once is cheaper than re-litigating it per feature. |
| **D10** | **Steady, quality-first.** Each milestone independently demoable and documented; at most two or three agents concurrent | Ship a rough end-to-end prototype fast | A rough prototype of a graph engine is a permanent liability, because graphs saved against wrong lacing semantics cannot be fixed later without breaking them (**R4**). Independently demoable milestones also solve the real risk, **R2**: a multi-year one-person effort needs to be releasable at every point, not only at the end. |
| **D11** | **Spark consumes NuGet packages and publishes none of its own** | Publish the contract assemblies — `Spark.Api`, `Spark.Geometry` and the rest — to nuget.org, as an earlier revision of this document assumed | Consuming and publishing are separate directions and only one was ever asked for. **Consuming is a core feature and is untouched:** a user brings any .NET library — a package from nuget.org, a private feed, or a DLL they built this morning — into a graph and gets nodes from it (FR-40 … FR-45, E7). **Publishing is not a feature at all.** Spark is an application, not a library ecosystem: its users open it and build graphs, they do not `PackageReference` it. The two audiences who genuinely compile against Spark are embedders, who reference `Spark.Host`, and node authors, who reference `Spark.Api` and `Spark.Geometry` — and both reference the assemblies **from an install**, which is how CAD add-ins are built anyway, since a Revit or AutoCAD add-in already resolves its assemblies out of a directory rather than restoring them. Publishing would buy a convenience those two get another way, in exchange for permanent ID ownership, package metadata, a signing story, a release cadence tied to nuget.org and a compatibility obligation to strangers. `IsPackable` is therefore `false` repository-wide ([NOTES.md N14](NOTES.md)), the CLI ships as `spark.exe` beside the desktop application rather than as a dotnet global tool, and a project's assembly name is its only name. **The consequence worth naming: ADR-0009 was decided on the assumption this decision reverses**, and is superseded by **ADR-0019**. |
| **D12** | **Unitless.** Coordinates are dimensionless world units | A `UnitSystem` on the document with typed lengths and conversion | Unit systems in modelling tools are a large, permanently leaky feature: every operation must decide what a mixed-unit input means, every import must guess, and every API signature gains a dimension. Dynamo is unitless and its users cope. Import and export assume the file's own units and document that they do. This does **not** remove scale-aware tolerance — `Tolerance.ForScale` is numerical robustness, not units, and survives untouched. |
| **D13** | **Salvage geometry only from C2VGeometry.** Nothing Dynamo lacks | Salvage the drafting types too, since they already exist and are tested | `Hatch/`, `VText`, `VDimension`, `VRadialDimension`, `VArrow`, `VGrid`, `VSpatialGrid` and `VCell` are annotation and drafting concepts. They are free to copy and are not free to *own*: each becomes a public type that must be serialised, versioned, documented, node-ified and kept working in 3D forever, in service of a use case Spark does not have. They are discarded outright rather than parked — a parked type is a type someone eventually revives. |
| **D14** | **v1 releases target Windows only** | Publish Linux and macOS artefacts too | Windows is where the AEC audience is, and each additional release target carries signing, packaging, installer and support cost that a single maintainer pays forever. macOS additionally needs an Apple Developer account and is not built at all. **But an ubuntu build-and-test job stays in CI**, because it is nearly free and it is the only thing that stops cross-platform support rotting silently — which would quietly convert **D1** from a strategic choice into wasted effort. Flagged explicitly as a judgement call rather than buried. |
| **D15** | **The engine is OpenCascade, and the binding is a hand-written C-ABI shim we own.** `spark_occt` in `native/spark_occt/`, MIT, C++, 350–500 exported entry points over roughly 2–3% of OCCT's class surface; `LibraryImport` P/Invoke from a new `Spark.Geometry.Occt`; OCCT built from a pinned source tag via a vcpkg manifest and shipped in the default install. [ADR-0020](adr/0020-occt-via-c-abi-shim.md) | **Engine:** OpenNURBS/`Rhino3dm`; Manifold and the mesh-boolean libraries; CGAL; Parasolid, ACIS or C3D. **Binding:** `OcctNet.Wrapper`; `Occt.NET`; a SWIG-generated binding; C++/CLI. **Packaging:** consume OCCT from nuget.org | **On the engine, it is not close.** OpenNURBS is excellent and is a *representation and file-format* library — no booleans, no fillet, no trimming, since Rhino's modelling lives in commercial RhinoCommon; it returns post-1.0 as `.3dm` interop, which is a pure addition. Manifold is superb at mesh booleans and produces **no BRep**, which is a one-way lossy trip losing analytic faces and making fillet and STEP impossible — the position just rejected. CGAL's boolean packages are **GPL-3.0**, incompatible with MIT and with **D5**, and it is not a BRep kernel either. Parasolid, ACIS and C3D lose for the reason the old **D2** already gave and which is undisturbed: per-seat royalty licensing against an MIT tool users install freely. **On the binding, the interesting argument.** `OcctNet.Wrapper` is architecturally right — a stable C ABI — and its nuspec carries a commit hash **with no repository URL**, making it a 174 MB binary blob, MIT by assertion, from an unnamed author group, with **no auditable source** and no upstream to contribute to; disqualifying on its own. `Occt.NET` has 338k downloads and **declares no licence at all**, and a download count measures reach rather than rights. A generator cannot *reduce* the ABI surface — Macad3D's generated C++/CLI binding is 170 files and 13.35 MB for a subset of 6,951 headers — and our whole upgrade-survival strategy depends on that surface being small and chosen. C++/CLI is proven at application scale by Macad3D under MIT and has the best debugging story of any option, and it is **Windows-only permanently**: it would reverse **D1**, kill the Linux rot-guard for everything downstream of `Spark.Geometry`, and break the `-windows`-free architecture test. The C-ABI shim costs perhaps 15–25% more effort and buys back the entire cross-platform option. **On packaging:** OCCT is at 8.0.1 and every OCCT package on nuget.org is stranded at 7.8 or 7.9 — not immature, abandoned in place. |

## 14. Open questions

| # | Question | Needed by |
|---|---|---|
| Q1 | Do the three M1.5 spikes pass — GL on Windows and Linux, 2000 nodes at 60 fps, AvaloniaEdit completion? A failure changes the architecture, so the criteria must be written into TASKS.md before M1 begins. | Before M2 |
| Q4 | `Directory.Build.props` promotes CS1591 to an error on **four** projects — `Spark.Api`, `Spark.Geometry`, `Spark.Geometry.Io`, `Spark.Nodes.Core` — where the plan named three. Is `Spark.Geometry.Io` deliberately included? **Still open.** The public-API baselines have since been applied to the same four under an identically shaped condition, so two mechanisms now agree on the list — which is evidence it is right, not a record that anyone chose it. Answering it settles both. | M0 |
| Q5 | Is Revit or AutoCAD the host that proves `Spark.Host` at M8? The host-thread scheduler is the same either way, but the add-in shell, licensing and test loop are not. | M8 |
| Q7 | Which public STEP corpus is authoritative, and which third-party viewer is the reference? **Downgraded by [ADR-0020](adr/0020-occt-via-c-abi-shim.md), not closed.** We no longer write a STEP reader or writer — OCCT gives AP203, AP214 and AP242 — so this stops being a question about defining and defending a subset and becomes a question about validating *our use* of somebody else's implementation. That is a smaller question and it still needs an answer, because *OCCT wrote it* is not evidence that a file we produce is correct. The rule survives intact: validate against a public corpus and a **third-party viewer, never our own reader**. | M8, and no longer gating anything upstream |
| Q8 | Where does the website live, and who maintains it? | M8 |
| Q13 | **The six licensing questions for counsel.** The central one: is a thin shim whose entire purpose is to expose OCCT a *work that uses the Library* under the Open CASCADE exception, or a derivative work under LGPL §5? Then: whether single-file, trimmed or AOT publishing is compatible with the relink obligation; whether vcpkg's port declaring `LGPL-2.1-only` — **omitting the exception** — creates exposure; what *prominent notice in supporting documentation* requires concretely; what obligations attach to a user embedding `Spark.Host` in a commercial add-in (**D5**); and whether the source offer is satisfied by a tag reference or needs a hosted archive. **Nothing in this document is legal advice**, and this row exists because it is not. The full list is in [ADR-0020](adr/0020-occt-via-c-abi-shim.md). | **Items 1 and 3 before M6.** The rest before 1.0 |
| Q14 | **What is OCCT's real thread-safety envelope?** May the parallel evaluator call the shim concurrently, and at what granularity? Documented guidance is thin, and **R20** is a top-three risk that cannot be mitigated until this is known. *How to find out:* read the upstream source of the specific packages we call, and stress the shim at the evaluator's real thread count. The conservative fallback is a single-writer policy, which would cost throughput on exactly the workload replication makes common. | M1.6 starts it; **M6** needs the answer |
| Q12 | **Is T-Splines in scope at all?** 169 members across 8 types — 20.2% of the whole ProtoGeometry surface, and `TSplineSurface` alone is 94, more than `Curve`. It is a subdivision-surface modeller and its API is a sculpting editor, not a geometry library: a different data structure, different refinement mathematics and different literature from BRep/NURBS, with its own `.tsm`/`.tss` formats and its own topology layer. Recommendation: **exclude it and state it publicly**, the way §9 already handles STEP's scope — ADR-0003's closing note treats a subdivision backend as *a different decision, not a widening of this one*, so nothing is foreclosed by leaving it out. [DYNAMO-COVERAGE §6.2](DYNAMO-COVERAGE.md#62-t-splines-is-a-second-product-not-a-subsystem). | Before any parity figure is quoted publicly — **the answer is the denominator** |

**Q6 and Q11 are answered, both by the same client decision, and the answer to each is the
opposite of what was recommended.**

**Q11 asked whether FR-81's parity promise moves exact solid booleans into 1.0. It does.** The
recommendation was to scope the promise to *the end of 1.x*, keeping R1's mitigation intact and
every existing estimate with it. The client, having been told that and told its price, chose
instead to take an existing engine. The 70 members are 1.0 requirements, **R1 is retired rather
than mitigated**, and **E12-T15 is not written — it is deleted**, because the sentence it was
going to say publicly is no longer true.

**Q6 asked whether an *optional* OCCT-backed package would breach the no-native-dependencies
promise.** It is answered by the package not being optional. **OCCT ships in the default
install** — a Dynamo user finding booleans greyed out on first run is precisely what FR-81
forbids — so the question is no longer whether an opt-in native package is defensible, but
whether the product's positioning survives acquiring a heavyweight native dependency at all.
That is **R13**, reframed and enlarged, and it has no technical fix: the distinction is real
(open source, freely redistributable, installed *with* Spark, no account, no licence purchase,
no other vendor's product) and **it only holds if we say it first, in our own words**. What
does *not* change is **NFR-5**: `Spark.Geometry`'s published output still contains no native
binaries, still asserted by CI, because the native component lives in `Spark.Geometry.Occt`.
**D2**, **D15**, [ADR-0020](adr/0020-occt-via-c-abi-shim.md).

**Q12 stays open, and ADR-0020 does not answer it.** T-Splines is a subdivision modeller;
OpenCascade does not have one either, so nothing about this decision makes the T-Spline surface
cheaper or more likely. The recommendation is unchanged — exclude it and say so publicly — and
so is the reason it must be decided rather than left: **the answer is the denominator of every
parity figure we quote.**

**Q2, Q3 and Q10 are answered and withdrawn, all three by the same correction.** They asked,
respectively, whether the `Spark.Geometry.Io` package ID was available; whether `Spark.Docs`
mapped to a project or was a defensive reservation; and whether `Spark.Host` becoming
`IsPackable` with no `PackageId` was deliberate or an oversight. **The answer to all three is
that nothing is published**: `IsPackable` is now `false` for every project in the repository,
no `PackageId` exists anywhere, and the assembly-name-versus-package-ID splits the questions
were reasoning about are gone with it. `Spark.Geometry.Io`'s availability does not matter;
`Spark.Docs` mapped to nothing because it was a reservation for a package that will not exist;
`Spark.Host`'s `IsPackable=true` was an oversight, and it is removed rather than reconciled.
See **D11** and [NOTES.md N14](NOTES.md). Recorded rather than deleted, because a question with
its answer beside it stops the same three being asked again by the next reader of a `.csproj`.

**Q9 is withdrawn.** *Is xunit v3 viable, with a costless fallback to 2.9.x?* Both test
projects consume `xunit.v3 [4.0.0]` and eleven tests run green. The fallback turned out to
be moot rather than costless: the .NET 10 SDK has removed the VSTest bridge, so xunit v3 on
Microsoft.Testing.Platform is the only shape that builds. [NOTES.md N11](NOTES.md).
