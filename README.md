# Spark

**Node-based visual programming for .NET, where the scripting language is C#.**

Spark is an open-source, independent alternative to Autodesk Dynamo Sandbox: nodes, wires,
ports, a graph canvas, a 3D viewport, a searchable node library and code blocks — with no
Autodesk software required, and with its own geometry kernel.

MIT licensed. `net10.0`. No native dependencies.

**Last updated:** 2026-08-27

> ## Status: M1 has started — there is still nothing to run
>
> **This repository is scaffolding, gates, specification, and the first slice of the
> geometry kernel.** It contains a solution, twelve project stubs, a reference graph, build
> properties, the project documents, nineteen ADRs, the lacing specification, a CI workflow,
> public-API baselines, and four test projects.
>
> **The one piece of product code that exists** is `Spark.Geometry`'s **value layer**:
> thirteen types — `Angle`, `Tolerance`, `Point3d`, `Vector3d`, `Point2d`, `Vector2d`, `UV`,
> `Interval`, `BoundingBox`, `Plane`, `Transform`, `CoordinateSystem` and an internal
> `NamespaceDoc` — declaring 387 public members, all documented, landed and **reviewed,
> repaired and accepted**. There are **no curves, no surfaces, no meshes and no solids**, and
> no graph engine, UI or viewport at all.
>
> What has been run, on Windows, on 2026-08-27:
> `dotnet build Spark.slnx --no-incremental -warnaserror` is clean over sixteen projects;
> `dotnet test Spark.slnx` runs **315 passing tests** across four projects; and
> `dotnet format Spark.slnx --verify-no-changes --severity warn` is clean. What has **not**
> been run: CI against this tree. The workflow has been green on Windows and Linux for
> earlier commits and has seen none of the kernel.
>
> **Worth knowing about how that slice was accepted.** Its first version passed all three
> gates and was rejected on review, with three of its eight claims false — most visibly a
> default-constructed `Plane` on which every point in space silently lay. Both tests written
> to guard it were structurally incapable of failing. Every fix in the accepted version is
> regression-proven by reverting it and naming the test that goes red.
>
> The rest of M1 is curves; M2 is the first milestone at which anything is usable — drag two
> nodes, wire them, see geometry. See
> [docs/PRD.md §11](docs/PRD.md#11-release-plan) for the plan and
> [docs/TODO.md](docs/TODO.md) for what happens next.

---

## Why Spark exists

**Dynamo Sandbox is nominally standalone, and is not.** It depends on Autodesk's
ProtoGeometry and its related libraries, which in practice forces you to have at least one
Autodesk product installed. Somebody who wants a parametric node graph — a facade study, a
structural layout generator, a fabrication script — must first buy into a commercial CAD
licence for a component they never asked for.

That is the dependency Spark exists to remove. Spark ships its own geometry kernel,
`Spark.Geometry`, so the only thing you need to run it is .NET.

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
- **Exact NURBS booleans, and fillet and chamfer on solids, are post-1.0.** 1.0 ships on
  robust mesh booleans, with `IBrepKernel` documented as the extension point. Better said
  loudly now than discovered later.

## Building

You need the **.NET 10 SDK** and nothing else. No Autodesk product, no native toolchain, no
GPU.

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

There is still nothing to *run* — no application, no CLI behaviour. `dotnet test` finds **315
tests** across four projects. `Spark.Geometry.Tests` (276) and `Spark.Geometry.Properties`
(28) cover the kernel's value layer by example and by CsCheck property respectively;
`Spark.Architecture.Tests` (6) enforces the reference graph below by reading `.csproj` files
as XML; `Spark.Docs.Verify` (5) checks these documents against the repository. The last two
were deliberately stood up before the code they now guard: a gate added later is a gate that
gets an exemption for everything already there.

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

Twelve projects, all `net10.0`, with a reference graph that a test enforces rather than a
convention:

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
| [docs/EPICS.md](docs/EPICS.md) | Twelve epics with acceptance criteria |
| [docs/TASKS.md](docs/TASKS.md) | The full task register, `E<n>-T<m>`, with statuses |
| [docs/TODO.md](docs/TODO.md) | What to do next, in priority order — and what is **deliberately accepted** rather than fixed |
| [docs/NOTES.md](docs/NOTES.md) | Numbered implementation notes: the non-obvious facts |
| [AGENTS.md](AGENTS.md) | The working agreement. Read before committing |
| [CONTRIBUTING.md](CONTRIBUTING.md) | MIT, DCO sign-off, how to build, what a PR needs |
| [docs/adr/](docs/adr/README.md) | Nineteen architecture decision records: what was decided, what was rejected, and what it costs |
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

[MIT](LICENSE). Copyright (c) 2026 Nicety.
