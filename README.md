# Spark

**Node-based visual programming for .NET, where the scripting language is C#.**

Spark is an open-source, independent alternative to Autodesk Dynamo Sandbox: nodes, wires,
ports, a graph canvas, a 3D viewport, a searchable node library and code blocks — with no
Autodesk software required.

MIT licensed. `net10.0`. Solid modelling by [OpenCascade](https://dev.opencascade.org/),
which ships with Spark.

**Last updated:** 2026-08-28

> ## Status: it runs, and it draws curves
>
> **There is an application now.** `dotnet run --project src/Spark.Desktop` opens the shell,
> evaluates a graph and puts geometry in a GPU viewport. The seeded curve demo draws an ellipse
> divided into twenty-four equal *lengths*, eight circles produced by a single node fed a list of
> centres, and a pentagon — three curve families, one of them laced.
>
> **What exists.** `Spark.Geometry`'s **value layer** (thirteen types) and its **curve layer**
> (`Line`, `Arc`, `Circle`, `EllipseCurve`, `PolyLine` and `PolyCurve` over a `Curve` base, with
> arc-length reparameterisation in the contract rather than bolted on). A graph engine with
> topological evaluation, a provenance cache and the full replication engine. A reflection
> importer that turns 57 first-party nodes out of plain static methods, with a two-way diff that
> makes an unreachable public member a red build. An Avalonia shell, an immediate-mode node
> canvas, and an OpenGL viewport.
>
> **Graphs are files.** A `.spark` file is plain JSON, canonically formatted, so a graph reviews
> like code: opening one and saving it again produces no diff at all, which is asserted by a test
> rather than hoped for. `docs/examples/curves.spark` is one.
>
> **Nodes are found by typing.** Double-click empty canvas and a search box opens there; type
> `cbcr`, press Enter, and `Circle.ByCentreRadius` lands at that point. The same ranking runs the
> library panel — exact, prefix, camel-hump, substring, category, description — because a library
> of thousands, which is what packages make, cannot be skimmed. **Dynamo's double-click makes a
> code block and Spark's does not**: the code block is a later milestone, and the gesture will gain
> it rather than be replaced by it.
>
> **A port says what it wants.** Beside each port name is the type it takes — `centre  Point3d`,
> `radius  number`, `sweepAngle  degrees` — in the words you type it in rather than in CLR type
> names, on the node and in the properties panel. A port name alone is a word; a port name and a
> type is an instruction.
>
> **Edits are undoable.** Ctrl+Z steps back through the last sixty-four edits and Ctrl+Y forward
> again — nodes, wires, values and positions alike, because a step is a snapshot of the same
> `.spark` document the save button writes. It is instant for a reason worth knowing: results are
> cached by *provenance* rather than by document, so a former state asks for keys that are still
> resident and the run after an undo recomputes nothing at all.
>
> **What does not exist.** No surfaces, meshes, BRep or solids. No `NurbsCurve`. No `spark run`,
> no packages, no code block. And **no OpenCascade**: there is no `native/` directory and no
> `Spark.Geometry.Occt` project.
>
> What has been run, on Windows, on 2026-08-28:
> `dotnet build Spark.slnx --no-incremental -warnaserror` is clean over sixteen projects;
> `dotnet test Spark.slnx` runs **952 passing tests** across seven projects; and
> `dotnet format Spark.slnx --verify-no-changes --severity warn` is clean. **CI ran all of it on
> Windows and Linux on commit `53596ab` and was green**, 952 tests on each — so the Linux leg is
> no longer a claim, and it has now caught something Windows could not.
>
> **Worth knowing about how this code is accepted.** The kernel's first slice passed all three
> gates and was rejected on review, with three of its eight claims false — most visibly a
> default-constructed `Plane` on which every point in space silently lay, guarded by two tests
> that were structurally incapable of failing. Every fix since is regression-proven by reverting
> it and naming the test that goes red, and every slice gets a mutation sweep. The curve layer's
> sweep found a test that could not fail and a branch that could not be reached, both in code
> that was green ([N19](docs/NOTES.md), [N20](docs/NOTES.md)), and the undo sweep found the same
> shape a third time: a test that could not fail because the gesture it drove never reached the
> guard it was written for.
>
> **One decision dominates everything below, and it is unbuilt.** Spark will use **OpenCascade**
> as its solid-modelling kernel, reached through a C-ABI shim we own, rather than writing its own
> exact BRep kernel — so exact booleans, fillet, chamfer, shell, trim and STEP are **in 1.0**
> rather than post-1.0.
>
> See [ADR-0020](docs/adr/0020-occt-via-c-abi-shim.md) and
> [ADR-0021](docs/adr/0021-brep-kernel-residency.md), and the paragraph below on what that means
> for a project whose whole premise is not depending on somebody else's CAD component.
>
> M2's persistence is in — save, load, undo, redo — so a graph outlives the process and an edit
> can be taken back. What is left of M2 is the polish: camel-hump library search, real docking,
> watch nodes and `spark run`. See [docs/PRD.md §11](docs/PRD.md#11-release-plan) for the plan and
> [docs/TODO.md](docs/TODO.md) for what happens next.

---

## Why Spark exists

**Dynamo Sandbox is nominally standalone, and is not.** It depends on Autodesk's
ProtoGeometry and its related libraries, which in practice forces you to have at least one
Autodesk product installed. Somebody who wants a parametric node graph — a facade study, a
structural layout generator, a fabrication script — must first buy into a commercial CAD
licence for a component they never asked for.

That is the dependency Spark exists to remove. Spark ships its own geometry model,
`Spark.Geometry`, so the only thing you need to run it is Spark.

### A dependency we do ship, and why it is a different thing

**Spark ships OpenCascade**, an open-source solid-modelling kernel, and it is installed with
Spark by default. That deserves saying plainly rather than discovering, because the objection
writes itself: *Spark exists because Dynamo drags in a heavyweight dependency, and now Spark
drags in a heavyweight dependency.*

The difference is the one that mattered in the first place. **The problem with ProtoGeometry
was never that it was large — it was that it was somebody else's commercial product, and using
Spark meant buying into a CAD licence for a component you never asked for.** OpenCascade is
open source (LGPL with the Open CASCADE exception), freely redistributable, installed *with*
Spark, and needs **no account, no licence purchase, no subscription and no other vendor's
software**. You install Spark and everything works. Nothing phones home, nothing expires, and
nothing asks who you are.

Three things follow, and they are all true at once:

- **`Spark.Geometry` is pure managed and stays that way.** Values, curves, surfaces, meshes,
  planar geometry, evaluation, tessellation and every mesh-interchange writer are ours and have
  no native component. A CI check asserts that its published output contains **zero native
  binaries**, and that check is unchanged by any of this.
- **The exact solid operations — boolean, trim, fillet, chamfer, shell, and STEP — come from
  OpenCascade**, through a small C-ABI shim we wrote and own (MIT), in a separate assembly.
  Writing those ourselves was a multi-year research problem; this is the honest way to give
  people fillets.
- **It ships in the default install**, because a capability you have to opt into is a
  capability most people will find missing.

The full reasoning, including the four engines and the four binding strategies that were
rejected, is [ADR-0020](docs/adr/0020-occt-via-c-abi-shim.md).
**Nothing of it is built yet** — the decision is recorded, the code is not written.

**And DesignScript is a language nobody else uses.** It is competent, and it is a dead end
for the person writing it: no ecosystem, no package manager, no IDE outside the host, no
transferable skill. A .NET developer coming to a node graph already knows C#. Making them
learn a bespoke language to write a three-line lambda is a tax paid for nothing.

## C# instead of DesignScript

Code blocks host **real C#** through Roslyn. Three things follow, and the third is the one
worth caring about:

1. **Everything you already know still applies** — LINQ, tuples, pattern matching, `var`,
   local functions, your own types.
2. **The whole NuGet ecosystem is reachable.** Because the platform is .NET, package
   management comes nearly free: NuGet *is* the package manager, and any assembly — a
   package somebody else published or a DLL you built this morning — becomes nodes by
   reflection, with no attributes, no plugin and no manifest required.
3. **IntelliSense inside a code block knows the type on the incoming wire.** Once a port is
   connected, the compiler knows the upstream type, so typing `center.` offers `Point3d`
   members. This is the single most compelling thing Spark can do that Dynamo cannot, and
   it falls out of using a real language with a real compiler rather than a bespoke one.

Planned, not built. It is milestone M4.

## What is deliberately *not* here

Naming these up front, because each is a decision rather than a gap. Full reasoning in the
[decision log](docs/PRD.md#13-decision-log).

- **No Dynamo compatibility, in either direction.** No `.dyn` reader, no writer, no
  importer. A `.dyn` file contains no geometry, so reading one never needed ProtoGeometry —
  but guaranteeing *semantic equivalence*, in every degenerate case and tolerance and lacing
  rule, does. That is unprovable without the very dependency Spark exists to remove, and a
  silently mistranslating importer is worse than none. (**D8**)
- **No units.** Coordinates are dimensionless world units, as in Dynamo. Scale-aware
  tolerance stays — that is numerical robustness, not units. (**D12**)
- **No drafting or annotation** — no dimensions, hatches, text, arrows or grids. (**D13**)
- **No telemetry**, of any kind, in v1.
- **Windows-only releases for v1**, with Linux built and tested in CI as a rot-guard.
  (**D14**)
- **No pure-managed exact solid kernel of our own.** Exact booleans, fillet, chamfer, shell
  and trim are **in 1.0** and come from OpenCascade — see above. Writing them ourselves was a
  research-grade problem that might never have reached production robustness, and choosing not
  to attempt it is the largest decision in this project.
  ([ADR-0020](docs/adr/0020-occt-via-c-abi-shim.md), which supersedes
  [ADR-0002](docs/adr/0002-own-managed-geometry-kernel.md).)
- **Robust *mesh* booleans are 1.x, not 1.0.** This is the one place the trade goes the other
  way: OpenCascade is poor at mesh booleans, Dynamo has them, and so they stay on the list —
  greyed out in the UI until they land rather than throwing when you run your graph.

## Building

You need the **.NET 10 SDK** and nothing else. No Autodesk product, no native toolchain, no
GPU.

That will change once the OpenCascade provider exists: building `native/spark_occt` will need
a C++ toolchain and vcpkg, and CI will build it once per platform and cache the result.
**Neither the directory nor the project exists yet**, so today the sentence above is exactly
true and the whole solution builds with the SDK alone.

```bash
git clone https://github.com/harilalmn/Spark.git
cd Spark
dotnet build Spark.slnx
```

The way CI builds it:

```bash
dotnet build Spark.slnx --no-incremental -warnaserror
dotnet test Spark.slnx
dotnet format Spark.slnx --verify-no-changes --severity warn
```

`--no-incremental` matters more than it looks. An incremental build skips projects MSBuild
considers up to date, analyzers run inside the compilation it skipped, and the summary still
prints `0 Warning(s)` — so a clean build and a skipped one read identically. That is not
hypothetical here: the public-API analyzer findings that produced the baselines were invisible
without the flag ([docs/NOTES.md N15](docs/NOTES.md)).

Everything targets `net10.0` with no `-windows` target framework, so it builds on Windows,
Linux and macOS. Warnings are errors in CI only, never in the project files —
[docs/NOTES.md N3](docs/NOTES.md) explains why. The solution file is `.slnx`, the XML
solution format, which needs a recent SDK and a recent Visual Studio
([N1](docs/NOTES.md)).

`dotnet run --project src/Spark.Desktop` opens the application. Three switches are worth knowing:
`--graph curves` opens the curve demo instead of the point grid, `--open PATH` opens a `.spark`
file, and `--screenshot PREFIX` writes a picture of the shell and a picture of the viewport and
exits — the viewport one is a GPU read-back rather than a window grab, so it works over a locked
session and in CI. The first two exist so that opening a particular graph can be checked without
a human driving a file dialog.

`dotnet test` finds **893 tests** across seven projects. `Spark.Geometry.Tests` (313) and
`Spark.Geometry.Properties` (38) cover the kernel by example and by CsCheck property
respectively; `Spark.Engine.Tests` (289) covers the graph, the replicator and the importer;
`Spark.UI.Tests` (171) drives the canvas headlessly with real pointer gestures;
`Spark.Viewport.Tests` (69) covers the scene and the camera; `Spark.Architecture.Tests` (8)
enforces the reference graph below by reading `.csproj` files as XML; and `Spark.Docs.Verify`
(5) checks these documents against the repository. The last two were deliberately stood up
before the code they now guard: a gate added later is a gate that gets an exemption for
everything already there.

## Repository layout

```text
src/Spark.Geometry/      the kernel: values, curves, surfaces, BRep, mesh, tessellation
src/Spark.Geometry.Io/   OBJ/STL/PLY/glTF/STEP behind reader and writer interfaces
src/Spark.Api/           contracts only: node attributes, SparkList, diagnostics, seams
src/Spark.Engine/        graph model, evaluation, lacing, node importer, serialization
src/Spark.Scripting/     Roslyn: compilation, rewriting, source maps, guards, completion
src/Spark.Packages/      NuGet client, per-package-version load contexts, trust store
src/Spark.Nodes.Core/    the first-party node library
src/Spark.Host/          SparkSession composition root and the CAD-embedding seam. No UI
src/Spark.Cli/           the `spark` command
src/Spark.Viewport/      IViewportRenderer, scene, camera, OpenGL + software backends
src/Spark.UI/            Avalonia controls and view models
src/Spark.Desktop/       the application
tests/  bench/  docs/  scripts/  .github/workflows/
```

Twelve source projects, all `net10.0`, with a reference graph that a test enforces rather than
a convention:

```text
Geometry ─┬─> Geometry.Io
          └─> Api ─> Engine ─┬─> Scripting
                             ├─> Packages
                             └─> Host ─> Cli
Nodes.Core ─> {Geometry, Geometry.Io, Api}
Viewport   ─> {Geometry, Api}
UI ─> {Api, Host, Viewport, Avalonia} ─> Desktop
```

Three of those edges are missing on purpose, and each is load-bearing:

- **`Spark.Nodes.Core` never references `Spark.Engine`**, so the first-party node library
  must be discovered by the same zero-config reflection importer a stranger's NuGet package
  goes through. The importer cannot quietly special-case us and then fail for everyone else.
- **`Spark.Viewport` references no Avalonia package**, so the software renderer runs
  headlessly — which is the only way viewport output becomes testable at all, and gives
  headless thumbnails and a GL fallback for free.
- **`Spark.Api` sees only the BCL and `Spark.Geometry`** — never Roslyn, Avalonia, NuGet or
  the engine. It is a contract, not a convenience library, and because contract assemblies
  cannot be side-by-sided, changes to it are deliberate rather than routine
  ([ADR-0019](docs/adr/0019-deliberate-public-api-change-control.md)).

Two more will be added by [ADR-0020](docs/adr/0020-occt-via-c-abi-shim.md) and **do not exist
yet**: `native/spark_occt/`, the C-ABI shim over OpenCascade, and `src/Spark.Geometry.Occt/`,
the only assembly permitted to P/Invoke it or to hold a native handle. A test will assert that
last part, as a **companion** to the rule that keeps `Spark.Geometry` free of third-party
dependencies — not as a relaxation of it.

## Packages: Spark consumes them, and publishes none

**Spark reads NuGet. It does not write it.** These two directions are easy to conflate, so
they are stated separately.

**Consuming is a core feature.** A Spark package is an ordinary NuGet package tagged `spark`
with a `tools/spark.json` manifest, installed from nuget.org or a private feed — and a loose
DLL you built this morning works the same way. Either becomes nodes by reflection, with no
attributes, no plugin and no manifest required. That is Spark's answer to Dynamo's Package
Manager, and reusing NuGet wholesale means protocol, hosting, auth, SemVer, dependency
resolution and private feeds all come free. It is milestone M7; nothing of it is built yet.

**Publishing is not a feature at all.** Nothing in this repository goes to nuget.org.
`IsPackable` is `false` for every project, with the reasoning in
[`Directory.Build.props`](Directory.Build.props) and
[NOTES.md N14](docs/NOTES.md). Embedders reference `Spark.Host` from an install and node
authors reference `Spark.Api` and `Spark.Geometry` from an install, which is how CAD add-ins
are built anyway. `Spark.Cli` builds `spark.exe` and ships beside the desktop application; it
is not a dotnet global tool. (**D11**)

## Documentation

| Document | What it is |
|---|---|
| [docs/PRD.md](docs/PRD.md) | Requirements, principles, constraints, risks, and the **decision log** — every decision with the alternative it beat and why |
| [docs/EPICS.md](docs/EPICS.md) | Thirteen epics with acceptance criteria |
| [docs/TASKS.md](docs/TASKS.md) | The full task register, `E<n>-T<m>`, with statuses |
| [docs/TODO.md](docs/TODO.md) | What to do next, in priority order — and what is **deliberately accepted** rather than fixed |
| [docs/NOTES.md](docs/NOTES.md) | Numbered implementation notes: the non-obvious facts |
| [AGENTS.md](AGENTS.md) | The working agreement. Read before committing |
| [CONTRIBUTING.md](CONTRIBUTING.md) | MIT, DCO sign-off, how to build, what a PR needs |
| [docs/adr/](docs/adr/README.md) | Twenty-one architecture decision records: what was decided, what was rejected, and what it costs |
| [docs/help/concepts/lacing.md](docs/help/concepts/lacing.md) | How lists, ranks and lacing work — **written before the engine, and the engine will be written to match it** |
| [docs/help/concepts/geometry-basics.md](docs/help/concepts/geometry-basics.md) | Points, vectors, planes, right-handedness, unitless coordinates, `Angle`, and tolerance — aimed at a designer, with every example run against the assembly |
| [docs/help/concepts/design-language.md](docs/help/concepts/design-language.md) | Spark's visual design language — **written before any UI code exists, and the UI is written to match it** |

Still to come: the rest of `docs/help/`, the generated API reference, and `docs/examples/`
for worked example graphs that CI executes.

**Documentation here is a build gate, not a chore.** Undocumented public API on a contract
project does not compile — that one works today. Every help topic must contain a worked
example, every relative link must resolve, and every cited ADR must exist — those work today
too, checked by `tests/Spark.Docs.Verify` on every `dotnet test`. Executing example graphs
and failing the build for a node with no help topic arrive with the milestones that create
nodes and graphs; they are not stubbed in advance, because a test that passes by doing
nothing is worse than no test. The reasoning, and the mechanisms, are in
[AGENTS.md](AGENTS.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). MIT, DCO sign-off (`git commit -s`), no CLA, one
maintainer.

At M0 the most valuable contribution is argument: if one of the decisions in the
[decision log](docs/PRD.md#13-decision-log) is wrong, it is far cheaper to find out now.

## Licence

[MIT](LICENSE). Copyright (c) 2026 Nicety. That covers everything in this repository,
including the `native/spark_occt` shim when it is written.

**Third-party notice — OpenCascade.** Spark's solid-modelling operations are provided by
[Open CASCADE Technology](https://dev.opencascade.org/), which is licensed under **LGPL-2.1
with the Open CASCADE exception**. Spark links it dynamically and ships it as unmodified,
replaceable shared libraries, so you may substitute your own build of it. The licence text,
the exception text, the exact OCCT version and the source offer will be shipped with every
release and shown in the About box. **None of this is built yet**, and this notice is here in
advance of the code rather than after it, which is deliberate:
[ADR-0020](docs/adr/0020-occt-via-c-abi-shim.md) sets out the obligations and the six
questions that are with counsel. *Nothing in this repository's documentation is legal advice.*
