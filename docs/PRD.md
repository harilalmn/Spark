# Spark — Product Requirements

**Status:** M0 — foundations, mostly landed. No product code is implemented; the repository
is scaffolding, gates and specification.
**Owner:** Nicety
**Last updated:** 2026-08-27

---

## 1. Summary

Spark is a node-based visual programming environment for .NET: nodes, wires, ports, a
graph canvas, a 3D viewport, a searchable node library and code blocks. It is open source
under MIT, and it depends on no Autodesk product.

Two deliberate departures from the tool it is most obviously compared to:

1. **C# replaces DesignScript.** Code blocks host real C# through Roslyn. Every .NET
   developer already knows the language, and the whole NuGet ecosystem becomes reachable
   from inside a graph.
2. **The geometry kernel is ours.** `Spark.Geometry` is a pure-managed 3D BRep/NURBS
   kernel, seeded from the pure-maths parts of `C2VGeometry` and grown from there.

Because the platform is .NET, package management comes nearly free: NuGet *is* the package
manager, and users can reference arbitrary DLLs with nodes generated from them by
reflection.

**As of this document, almost none of that is built.** M0 has produced a solution, twelve
project stubs, a reference graph, build properties, these documents, nineteen ADRs, the
replication specification, a CI workflow, public-API baselines, and four test projects that
between them run **315 passing checks** against the repository.

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
- **G3** — Ship a geometry kernel good enough to model with: curves, surfaces, solids and
  booleans, pure managed, no native binaries.
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
2. **The kernel is pure managed.** No native binaries, verified by CI rather than
   promised. Clipper2's C# distribution is managed and stays isolated behind one internal
   file so the promise remains checkable.
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
| FR-48 | Curves — `Line`, `Arc`, `Circle`, `EllipseCurve`, `PolyLine`, `PolyCurve`, `NurbsCurve` — with a common evaluation surface: `Domain`, `PointAt`, `TangentAt`, `DerivativeAt`, `FrameAt`, `CurvatureAt`, `Length(tol)`, `ParameterAtLength`, `ClosestPoint`, `Trim`, `Split`, `Reverse`, `IsClosed`, `IsPlanar`, `ToNurbsCurve`. | Not started (E2) |
| FR-49 | Surfaces — `PlaneSurface`, `SphericalSurface`, `CylindricalSurface`, `ConicalSurface`, `ToroidalSurface`, `ExtrusionSurface`, `RevolutionSurface`, `RuledSurface`, `NurbsSurface` — with analytics **first-class, not NURBS in disguise**. | Not started (E2) |
| FR-50 | Index-based BRep topology (`BrepVertex`, `BrepEdge`, `BrepTrim`, `BrepLoop`, `BrepFace`, `BrepShell`, `Brep`) with `readonly ref struct` navigator views for ergonomics. | Not started (E2) |
| FR-51 | `Mesh` with indexed vertices, tri and quad faces, optional normals, UVs and colours, and lazily built halfedge adjacency. Plus `PointCloud` and `GeometryGroup`. | Not started (E2) |
| FR-52 | Modelling: extrude, revolve, loft, sweep; sew, heal, validate. | Not started (E2) |
| FR-53 | **Robust mesh boolean** — ported BVH plus adaptive-precision exact predicates, pure managed. | Not started (E2) |
| FR-54 | `IBrepKernel` seam with a `Capabilities` flag set. The node library **greys out unsupported operations rather than throwing**. | Not started (E2) |
| FR-55 | Every kernel operation returns `Result<T>` carrying diagnostics and partial results. | Not started (E2) |
| FR-56 | `Tolerance { Linear, Angular, RelativeEpsilon }` is explicit and passed, defaults per call via `in Tolerance tol = default`, is scale-aware through `Tolerance.ForScale(characteristicLength)`, and is **hashed into every node's cache key**. | **Done in the kernel** (E2); the cache-key half is `Not started` (E3). `Tolerance` exists with all three components, the zero-`Linear` sentinel, `ForScale` and `Scaled`, and `in Tolerance tolerance = default` on every predicate in the assembly. There is no `EvaluationContext` yet, so "the default" is currently a fixed set of components rather than one flowing from a document — see [NOTES.md N9](NOTES.md) |
| FR-57 | Geometry serialization: source-generated `System.Text.Json` with polymorphic discriminators and **per-type `schemaVersion`**, plus a compact binary `.sparkgeo` for bulk data. | Not started (E2) |
| FR-58 | Interchange: OBJ, STL and PLY read and write; glTF write. | Not started (E2) |
| FR-59 | STEP AP203/AP214 read and write, scoped to a documented subset. | Not started (E2) |
| FR-60 | `Spark.Geometry.Planar`: `Point2d`/`Curve2d`, `Region`, and the Clipper2-backed boolean, offset and simplify pipeline, bridged by `Plane.To2d`/`To3d`. Not a peer 2D API. | Not started (E2) |
| FR-81 | **Capability parity with Dynamo's geometry.** A person who knows Dynamo must never reach for a geometric capability in Spark and find it absent. Parity is of **capability**, not of type names, method names, parameter order, degenerate-case behaviour or tolerances — those are ours to choose, and **D8** removes any obligation to match them. The reference surface is `ProtoGeometry.dll` as installed with Revit 2026: **51 public types, 837 public members**. Progress is tracked member by member in [DYNAMO-COVERAGE.md](DYNAMO-COVERAGE.md) and held true by a two-way diff test against a checked-in manifest, so the register cannot drift from the code (E11-T23). | **92 of 837 reachable — 11.0%** (E2-T40 … E2-T46). 16.0% of the 575 members committed to, once §5's refusals and the undecided T-Spline surface are excluded. All 92 are in the value layer; there are no curves, surfaces, solids, meshes or topology. Two scope questions are open — **Q11** and **Q12** |

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
| NFR-4 | The evaluation cache is LRU with a memory budget, evicted by last use and estimated size. | Not started |
| NFR-5 | `Spark.Geometry`'s published output contains **no native binaries**, asserted by CI. *Published* here means the `dotnet publish` directory, never nuget.org — nothing is packaged (**D11**). | Not started |
| NFR-6 | Every change to the public surface of `Spark.Api` or `Spark.Geometry` is visible in a checked-in public-API baseline diff, and a breaking one is a recorded decision with a release note rather than a discovery. Adding is preferred to changing; the baselines are a **review aid, not a compatibility guarantee**. **ADR-0019**, which supersedes ADR-0009's strictly-additive rule. | **Done for the mechanism** — `Microsoft.CodeAnalysis.PublicApiAnalyzers [5.6.0]` is referenced from `Directory.Build.props` for all four contract projects, each with a `PublicAPI.Shipped.txt` and a `PublicAPI.Unshipped.txt`. `Spark.Geometry` declares 387 public members; the other three surfaces are empty. RS0016 is at error severity and **was proved to fire**, not assumed to. The *release note* half awaits a release |
| NFR-7 | A graph containing no script nodes never loads `Spark.Scripting`, so Roslyn cold start is not paid by users who do not script. Background warm-up on idle covers the rest. | Not started |
| NFR-8 | Tessellation of a closed solid is watertight — a property-based test, not a spot check. | Not started |
| NFR-9 | Tolerance is scale-aware from the first release of the kernel, not retrofitted. A fixed `1e-6` is wrong for kilometres and wrong for microns. | **Done for the value layer** — `Tolerance.ForScale` and `Scaled` exist, and every geometric `EqualsWithin` in `Spark.Geometry` routes through one hybrid absolute/relative rule (`IsNegligible`) so a comparison keeps meaning at 1e9 as well as at 1e-9. Property generators span that whole range (ADR-0018). Curves, surfaces and meshes must be built to the same rule as they arrive; the requirement is about *not retrofitting*, so it is never fully closed until the kernel is |
| NFR-10 | Undocumented public API on `Spark.Api`, `Spark.Geometry`, `Spark.Geometry.Io` or `Spark.Nodes.Core` fails the build (CS1591 promoted to error). | **Done** — wired in `Directory.Build.props` |
| NFR-11 | The build is clean with `-warnaserror` on Windows and Linux. | **Partly done** — on Windows, on 2026-08-27, `dotnet build Spark.slnx --no-incremental -warnaserror` is clean over all sixteen projects and `dotnet format Spark.slnx --verify-no-changes --severity warn` is clean over the whole solution, kernel included; the IDE1006 findings outstanding at the last revision are closed. **Verify with `--no-incremental` or not at all**: an incremental build can print "0 warnings" from a cached analysis, which is how the public-API findings stayed hidden ([NOTES.md N15](NOTES.md)). CI has been green on both platforms for **earlier commits** and has never run against the kernel, so the Linux half of this row is untested |
| NFR-12 | The software renderer is deterministic, so `spark render` is usable for CI visual regression. GPU output is not testable; software output is. | Not started |
| NFR-13 | No telemetry of any kind in v1. Opt-in crash reporting is considered post-1.0, with graphs excluded from any payload. | **Done by construction** — nothing collects anything |
| NFR-14 | Every package version is pinned exactly; there are no floating ranges. | **Done** — `Directory.Packages.props` |
| NFR-15 | No `-windows` target framework anywhere, and no unsafe code. | **Done** — `net10.0` and `AllowUnsafeBlocks=false` in `Directory.Build.props`, and the `-windows` half is now **enforced by a passing test** rather than by vigilance (`Spark.Architecture.Tests`) |

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
| Unsafe code | `AllowUnsafeBlocks=false` | `Span<T>`, `ref struct` and `System.Numerics` cover the kernel. |
| Versioning | MinVer **`[7.0.0]`**, SemVer, tag prefix `v` | Embedders reference `Spark.Host` and node authors reference `Spark.Api` from an install, and both need *does upgrading break me?* answerable from the number, which CalVer cannot do. **D11**, ADR-0007. |
| Public API baselines | `Microsoft.CodeAnalysis.PublicApiAnalyzers` **`[5.6.0]`** | The mechanism behind NFR-6, kept as a review aid rather than a compatibility guarantee. **Live** on the four contract projects; RS0016 at error, **RS0026 suppressed** with the reasoning in `.editorconfig` — it protects a source-compatibility promise Spark no longer makes after ADR-0019. |
| Planar geometry | Clipper2 **`[2.0.0]`** | The **only** third-party dependency `Spark.Geometry` may take — and it does not take it at present. The `PackageReference` was removed once it proved unused, leaving the assembly on the BCL alone, and **returns with the planar boolean pipeline**; the version stays pinned meanwhile. Its C# distribution is pure managed and Boost-licensed. Isolated behind one internal file so the no-native-dependencies promise stays checkable. |
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
- **Exact NURBS booleans, and fillet and chamfer on solids.** Post-1.0, stated publicly.
  1.0 ships on mesh booleans with `IBrepKernel` documented as the extension point.
- **Live package hot-swap as a guarantee.** Restart is the documented default; live unload
  is a best-effort optimisation.
- **An out-of-process script worker.** Kept viable by the scheduler and ALC seams,
  deferred past v1.
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
| **M2** | Walking skeleton and lacing | **Drag two nodes, wire them, see geometry in the viewport — and it laces over lists** | 10–12 wk |
| **M3** | NURBS curves | A real parametric curve graph | 6 wk |
| **M4** | C# code block | **Type C# in a node and get IntelliSense that knows the type on the incoming wire; drag a slider and watch it recompute live** | 5 wk |
| **M5** | Surfaces and mesh | A shaded 3D model built parametrically | 8 wk |
| **M6** | BRep, modelling and mesh booleans | **Solids that can actually be combined** | 14 wk |
| **M7** | Packages and extensibility | Install a package from nuget.org and use its nodes; open a graph missing a package and lose nothing | 8 wk |
| **M8** | Embedding and 1.0 | **1.0** | 8 wk |

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
| R1 | **Exact NURBS surface-surface intersection is a research-grade problem.** Robust SSI with tangential and degenerate cases is what makes commercial kernels cost millions | Could sink the kernel | `IBrepKernel` seam locked from the start; mesh booleans at M6 give working booleans regardless; a throwaway SSI spike at M5 calibrates the estimate while it is still cheap to learn it is hard; exact booleans declared post-1.0. **Fallback: an OCCT-backed optional package, which the seam absorbs without a rewrite** |
| R2 | **Scope versus capacity.** A multi-year effort directed by one person | Existential | Every milestone independently demoable and releasable, with a runnable build shipped from M2 rather than only at 1.0; the `Spark.Api` boundary makes third-party node libraries a contribution path needing no kernel expertise |
| R3 | **Kernel numerical robustness** — tolerance-dependent code that passes the corpus and fails on real models at unusual scales | High, and discovered late by default | Scale-aware `Tolerance` from M1 rather than retrofitted; property-based tests from M1; watertightness invariants; `Result<T>` so failures are diagnosable rather than silent; a regression corpus that grows with every bug. **Partly realised, and the first slice showed how the mitigation itself can fail:** a property whose generator never reaches the boundary it tests cannot fail and looks exactly like a passing test. Generators now span 1e-9 to 1e9 log-uniform (ADR-0018), and widening them found two more defects. [NOTES.md N18](NOTES.md) |
| R4 | **Lacing semantics get subtly wrong and then become unfixable**, because graphs depend on the wrong behaviour | Permanent | Specification-first: the case table is written as a help topic before implementation and used directly as the test corpus, and it has already earned its keep — writing it settled ten questions the plan left open and overturned one answer the plan had wrong (**D4**, `Auto`); `Disabled` mode always available; `graph.formatVersion` gates any semantics change so a fix never silently alters an existing graph |
| R5 | **The node canvas collapses above ~1000 nodes**, which real graphs exceed | Would force a UI rewrite | Immediate-mode plus `SceneIndex` chosen precisely for this; M1.5 spike with 2000 synthetic nodes; LOD below 40% zoom; benchmarked nightly from M2 |
| R6 | **The Avalonia GL viewport fails or degrades** — driver variance, RDP, virtual machines | Would strand the 3D story | M1.5 spike before committing; the `IViewportRenderer` seam; a software fallback with independent value in headless thumbnails and CI visual regression |
| R7 | **`Spark.Api` or `Spark.Geometry` need a breaking change after users have compiled node DLLs against them.** They cannot be side-by-sided, so a break means every such DLL is recompiled or dropped | Moderate, and per-user rather than ecosystem-wide — see **D11** | Public-API baselines from M0 so the change is visible in the diff that approves it; keep `Spark.Api` deliberately *small* — it is a contract, not a convenience library; prefer adding an interface to changing one; when a break is right, record it and name it in the release notes (**ADR-0019**) |
| R8 | **ALC unloading never works in practice**, so upgrades always need a restart | Low, because it is already the promise | Restart is the documented default. Honest messaging beats a broken promise |
| R9 | **Roslyn cold start makes code blocks feel sluggish** | Undermines the headline feature | `Spark.Scripting` isolated so graphs without scripts never load it; background warm-up on idle; persistent compiled-assembly cache; resident cache for input changes |
| R10 | **The C2VGeometry test harvest sprawls** into a multi-week rewrite | Eats M1 | Timeboxed to one week, hard stop. Harvest only pure-maths-on-values tests; anything needing a `Shape` is discarded without argument |
| R11 | **User C# takes down the process** via `StackOverflowException`, which .NET cannot catch | Data loss | Guard weaving reduces frequency; aggressive autosave and crash recovery limit damage; an out-of-process worker is kept viable by the scheduler seam and deferred past v1 |
| R12 | **STEP is much bigger than budgeted** | Slips M8 | Scoped to a documented subset, deferred whole to M8, blocks nothing upstream; validated against a public corpus and a third-party viewer, **never our own reader** |
| R13 | **Clipper2 contradicts "no native dependencies"** if misunderstood | Reputational | Its C# distribution is pure managed and Boost-licensed; pinned exactly, isolated behind one internal file, plus a CI check asserting no native binaries appear in `Spark.Geometry`'s published output. **Not currently referenced at all** — the unused reference was removed and returns with the planar boolean pipeline (`E2-T14`); the architecture test asserts a ceiling rather than an exact set, so it holds on both sides of that |
| R14 | **Capability parity commits us to exact solid booleans, which are currently out of scope for 1.0.** FR-81's promise is that a Dynamo user never finds a capability absent. **32 members of ProtoGeometry cannot exist without exact BRep booleans, trimming, filleting and sewing** — `Solid.Union`, `UnionAll`, `ByUnion`, `Difference`, `DifferenceAll`, `Fillet`, `Chamfer`, `ThinShell`, `Separate`, `Repair`, `ByJoinedSurfaces`, `ProjectInputOnto`; `Surface.ByUnion`, `Difference`, `SubtractFrom`, `TrimWithEdgeLoops` ×2, `Join` ×2, `Thicken` ×2, `Offset`, `Repair`, `ProjectInputOnto`; `PolySurface.Fillet`, `Chamfer`, `ByJoinedSurfaces`; and `Geometry.Intersect`, `IntersectAll`, `DoesIntersect`, `Split`, `Trim` — with a further 38 modelling and intersection members behind the same `IBrepKernel` seam. **This is R1's problem wearing a requirement's clothes**, and §9 currently puts it post-1.0 in writing | Directly contradicts §9, E12-T15 and the M6 estimate. Left unstated, it surfaces at M6 as a slipped milestone rather than as a decision | **Name the contradiction rather than absorb it.** Recommendation: scope FR-81's promise to *the end of 1.x* rather than *at 1.0*, so 1.0 still ships on mesh booleans (E2-T27) with `Capabilities` greying out what is absent, and R1's mitigation survives intact. The alternatives are to accept exact booleans into 1.0 — which requires retiring R1, not mitigating it — or to promote the OCCT-backed optional package from fallback to a shipped option, which is **Q6**. This is a client decision because it trades the headline promise against the release date. **Q11**, E2-T47, [DYNAMO-COVERAGE §6.1](DYNAMO-COVERAGE.md#61-parity-on-solid-and-surface-commits-us-to-exact-solid-modelling) |

## 13. Decision log

Fourteen decisions, each with the alternative that was rejected and why. Anything that
could have gone differently also gets an ADR under `docs/adr/`; this table is the index.

| # | Decision | Alternative considered | Why |
|---|---|---|---|
| **D1** | **UI platform is Avalonia** | WPF | The prior art (RCS, CADScript, DoodleSharp) is WPF, so WPF looked cheaper. It is not: none of the WPF UI ports directly regardless, because those UIs are host-embedded palettes rather than an application shell, and WPF forecloses the Linux CI rot-guard that keeps D14 from quietly becoming a mistake. `AvaloniaEdit` is a close API port of AvalonEdit, so the editor controllers — the genuinely expensive part — transfer with moderate effort. |
| **D2** | **A full BRep/NURBS kernel, pure managed, staged.** Mesh booleans first, then analytic-exact, then general NURBS SSI | Wrap OpenCascade, or ship meshes only | Wrapping OCCT reintroduces exactly the class of native, licence-encumbered dependency Spark exists to remove, and makes the "runs anywhere .NET runs" promise conditional. Meshes only would be honest and small, and would also make Spark unusable for the AEC work that is its primary audience. Staging keeps every milestone shippable: booleans exist at M6 without waiting on research-grade SSI. The `IBrepKernel` seam means an OCCT-backed *optional* package remains possible without a rewrite. |
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

## 14. Open questions

| # | Question | Needed by |
|---|---|---|
| Q1 | Do the three M1.5 spikes pass — GL on Windows and Linux, 2000 nodes at 60 fps, AvaloniaEdit completion? A failure changes the architecture, so the criteria must be written into TASKS.md before M1 begins. | Before M2 |
| Q4 | `Directory.Build.props` promotes CS1591 to an error on **four** projects — `Spark.Api`, `Spark.Geometry`, `Spark.Geometry.Io`, `Spark.Nodes.Core` — where the plan named three. Is `Spark.Geometry.Io` deliberately included? **Still open.** The public-API baselines have since been applied to the same four under an identically shaped condition, so two mechanisms now agree on the list — which is evidence it is right, not a record that anyone chose it. Answering it settles both. | M0 |
| Q5 | Is Revit or AutoCAD the host that proves `Spark.Host` at M8? The host-thread scheduler is the same either way, but the add-in shell, licensing and test loop are not. | M8 |
| Q6 | If **R1** forces the OCCT fallback, does an *optional* OCCT-backed package breach the no-native-dependencies promise, or is "the core is pure managed; this package is not, and says so" acceptable? FR-44 already discloses native binaries, which suggests the latter. | M6 |
| Q7 | Which public STEP corpus is authoritative for validating the AP203/AP214 subset, and which third-party viewer is the reference? | M8 |
| Q8 | Where does the website live, and who maintains it? | M8 |
| Q11 | **Does FR-81's parity promise move exact solid booleans into 1.0?** 32 ProtoGeometry members cannot exist without exact BRep booleans, trimming, filleting and sewing, with 38 more behind the same `IBrepKernel` seam — and §9 currently states publicly that those are post-1.0. The recommendation is to scope the promise to the end of 1.x rather than to 1.0, keeping R1's mitigation intact; the alternatives are to accept them into 1.0, or to promote the OCCT-backed optional package, which is **Q6**. **R14**, [DYNAMO-COVERAGE §6.1](DYNAMO-COVERAGE.md#61-parity-on-solid-and-surface-commits-us-to-exact-solid-modelling). | Before M6 is estimated; **E12-T15** cannot be written until it is answered |
| Q12 | **Is T-Splines in scope at all?** 169 members across 8 types — 20.2% of the whole ProtoGeometry surface, and `TSplineSurface` alone is 94, more than `Curve`. It is a subdivision-surface modeller and its API is a sculpting editor, not a geometry library: a different data structure, different refinement mathematics and different literature from BRep/NURBS, with its own `.tsm`/`.tss` formats and its own topology layer. Recommendation: **exclude it and state it publicly**, the way §9 already handles STEP's scope — ADR-0003's closing note treats a subdivision backend as *a different decision, not a widening of this one*, so nothing is foreclosed by leaving it out. [DYNAMO-COVERAGE §6.2](DYNAMO-COVERAGE.md#62-t-splines-is-a-second-product-not-a-subsystem). | Before any parity figure is quoted publicly — **the answer is the denominator** |

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
