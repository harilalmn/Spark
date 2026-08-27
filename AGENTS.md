# Working agreement

For anyone changing this repository — human or AI. Read this before committing.

**Last updated:** 2026-08-27

---

## The standing instruction

> **Documentation is updated after every change, before committing. Everything is
> documented as end-user help topics, with worked examples. No change is done until the
> affected documents reflect it.**

This is not a nice-to-have and not a cleanup task for later. **A change whose documentation
has not been updated is an unfinished change.** If you are short of time, do less work —
not less documentation.

Spark backs the rule with three mechanisms rather than trusting it, because a rule nobody
enforces is a preference:

1. **CS1591 is an error** on `Spark.Api`, `Spark.Geometry`, `Spark.Geometry.Io` and
   `Spark.Nodes.Core`. Undocumented public API does not build. See
   [NOTES.md N4](docs/NOTES.md).
2. **The docs harness** (`tests/Spark.Docs.Verify`) runs inside `dotnet test`. **Today** it
   checks help-topic front matter, that every help topic contains a worked example, that
   every relative Markdown link resolves, that every cited `ADR-NNNN` exists — in build files
   and source comments as well as Markdown — and that every core document carries a
   `Last updated` line. **It will also**, once there is an API to check against, compile every
   sample, execute every example graph, and fail the build when a node has no help topic or a
   help topic names a node that no longer exists. Those checks are not stubbed in advance,
   deliberately: see [NOTES.md N13](docs/NOTES.md).
3. **The `docs-freshness` CI job** fails a diff that changes a public-API baseline or
   touches `src/Spark.Nodes.*` without touching `docs/`, `README.md` or `AGENTS.md`. It is
   overridable only by an explicit `docs: none-needed` commit trailer, which is **visible in
   review**. A silent exemption is worthless; a loud one is fine.

Mechanism 1 works. Mechanism 2 exists and runs, in the reduced form described above.
Mechanism 3 is **written but has never run** — CI has not executed once (`E1-T14`,
`E11-T14`), so for the moment that third rule rests entirely on you.

## What "update the documents" means

Not every change touches every document. Work out which ones it touches and update those
properly; a token edit to satisfy the rule is worse than none.

| Document | Update it when |
|---|---|
| [README.md](README.md) | Build or install steps change · a dependency or version pin changes · the repository layout moves · **the honest status changes** · behaviour a newcomer needs on day one changes |
| [docs/PRD.md](docs/PRD.md) | A requirement is added, met or dropped — change the status in the FR/NFR table · scope moves in or out · a **decision is made**: add a row to the decision log naming the alternative you rejected and why · a risk changes · an open question is answered |
| [docs/EPICS.md](docs/EPICS.md) | An acceptance criterion is met — tick it · an epic changes status · a new epic appears |
| [docs/TASKS.md](docs/TASKS.md) | Any task starts, finishes or is discovered. Every task gets an ID (`E<epic>-T<n>`), a status and a note explaining anything non-obvious. **Update the summary counts at the top** |
| [docs/TODO.md](docs/TODO.md) | Priorities shift · something is done — remove it · something is deliberately accepted rather than fixed — move it to *Known and deliberately accepted* so nobody rediscovers it as a bug |
| [docs/NOTES.md](docs/NOTES.md) | You discover a non-obvious implementation fact the next reader would get wrong. Take the next unused number. **Never renumber, never reuse, leave gaps on deletion** |
| `docs/adr/` | A decision that **could have gone differently**. Name the alternative and why it lost. Never renumber an ADR |
| `docs/help/` | Anything user-facing: a new node, a changed port, a new concept, a new `SPK####` code. **Every topic contains a worked example**, and every node family gets one. A node nobody can find is a node nobody uses |
| `docs/examples/` | A concept is easier shown than told. These are real `.spark` files and CI executes them |
| XML doc comments | You add or change a public member on a contract project. This is not optional; the compiler enforces it |

Every document carries a **Last updated** date. Change it when you change the document.

**The taxonomy, so it is not re-litigated:** *ADR = a decision that could have gone
differently. NOTE = a non-obvious implementation fact. Help topic = something a user needs.
XML doc = what this member does.*

## Before you commit

1. `dotnet build Spark.slnx -warnaserror` — clean, **zero warnings**.
2. `dotnet test Spark.slnx` — green. This runs the docs harness and the architecture tests;
   there is no separate command for either.
3. `dotnet format Spark.slnx --verify-no-changes --severity warn` — clean. Use exactly this
   form: it is what the `format` CI job runs, and a shorter one can pass locally where the
   gate fails.
4. Documents updated per the table above, including their **Last updated** dates.
5. New user-facing behaviour has a help topic **with a worked example**.
6. New public API on a contract project has an XML doc comment and a public-API baseline
   entry.
7. Commit message says what changed and why, and **names the task IDs it advances**.
8. Sign off with DCO: `git commit -s`. See [CONTRIBUTING.md](CONTRIBUTING.md).

All three commands above have been verified to work as written, on Windows, on 2026-08-27.
Steps 1 through 3 are gates. A red docs harness is a broken build, including when the only
thing broken is a dangling ADR citation in a build-file comment — that is precisely the point
of it, and it has already caught exactly that (`E1-T29`).

**Everything verified so far was verified on Windows.** CI is written and has never run, so
nothing about this repository is yet known to hold on Linux.

## Things that will bite you

**`Spark.Api` and `Spark.Geometry` must be strictly additive across all of 1.x.**

This is the single most consequential rule in the repository, and it is easy to break by
accident because nothing about it feels dangerous while you are doing it. Packages load
into per-package-version load contexts, but **contract assemblies always resolve from the
default context** — they have to, because a `Circle` from package A must be the same `Type`
as a `Circle` from package B or nothing can be wired together. Contract assemblies therefore
**cannot be side-by-sided**, and one breaking change to either breaks *every installed
package at once*.

So: add an overload, never change a signature. Add a new interface, never change an existing
one. Add a member to a class, never to an interface anyone might have implemented. Keep
`Spark.Api` **deliberately small** — it is a contract, not a convenience library, and every
type added to it is a type that can never change. Public-API baselines (`E1-T23`) make each
addition a reviewed line in a text file; that is the mechanism, and it only works if you
read the diff.

**The no-native-dependencies promise, and the one dependency that looks like it breaks it.**

`Spark.Geometry` has exactly one third-party dependency: **Clipper2**, pinned at `[2.0.0]`.
Its C# distribution is pure managed and Boost-licensed, so the promise holds — but only
while it is confined. Keep it isolated behind a **single internal file**, exactly as
C2VGeometry already does. Do not let a `Clipper2Lib` type appear in a public signature, and
do not add a second dependency to the kernel without a very good answer.

A CI check asserts no native binaries appear in `Spark.Geometry`'s published output
(`E1-T20`). That check is what turns a promise into a fact, and it is why the package
installer also discloses whether a **third-party** package ships native binaries: users
deserve to know when the promise is being broken on their behalf.

**`Spark.Nodes.Core` must never reference `Spark.Engine`.**

It references `Spark.Geometry`, `Spark.Geometry.Io` and `Spark.Api`, and nothing else. This
forces first-party nodes through the same zero-config reflection importer that a stranger's
NuGet package goes through, so **the importer cannot quietly special-case us and then fail
for everyone else**. When the importer cannot express what you need, that is a bug in the
importer, not a reason to reach for a reference. Anything a node needs from the engine must
become a contract in `Spark.Api` — and see the additivity rule above before you put it
there. Full reasoning in [NOTES.md N5](docs/NOTES.md).

**`Spark.Viewport` must stay Avalonia-free.**

`Spark.UI` is the only project that adapts the renderer to Avalonia. Take an Avalonia
reference in `Spark.Viewport` for one convenience type and the software renderer stops
working headlessly — which silently removes the only mechanism by which viewport output is
testable at all, plus headless thumbnails, plus the fallback for GL failures on VMs and over
RDP. Nothing will fail loudly when you do this. [NOTES.md N6](docs/NOTES.md).

**Tolerance is passed, never ambient — because it is hashed into cache keys.**

There is no `Tolerance.Current`, no static default, no thread-local, and there must never be
one. Document tolerance flows through `EvaluationContext` and is hashed into **every node's
cache key**, so changing it invalidates exactly the affected nodes. An ambient tolerance
would be invisible to the cache: the key would not change, the cache would hit, and the
graph would serve geometry computed at the old tolerance — **silently wrong, with no error
and no way for a user to tell**. Signatures take `in Tolerance tol = default`, where
`Linear == 0` means "use the context's". [NOTES.md N9](docs/NOTES.md).

**Never `MethodInfo.Invoke` on a node's evaluation path.**

Node invocation is an expression-tree-compiled delegate. Under replication over 100k
elements the reflection path is 50–100× slower, which does not make lacing slow — it makes
lacing unusable. If you find yourself reaching for reflection inside a loop, you are on the
wrong path.

**Caching is by provenance, not by value.**

`Key(n) = Hash(DefinitionKey, DefinitionVersion, Lacing, Tolerance, RunEpochIfImpure,
∀input: connected ? Key(upstream) : Hash(literal))`. Never hash a value to key a cache
entry: hashing a 2M-triangle mesh costs more than recomputing it. The corollary that trips
people up is that **an impure node must declare itself** — nothing can detect impurity, and
an undeclared one poisons nothing and therefore serves stale results forever.

**Errors must not cascade.**

Downstream of a failed node is greyed as *not evaluated*, never marked as errored. Cascading
turns a one-node problem into a fifty-error wall that hides the cause. Warnings are
different: they mean output-with-caveats, and downstream still evaluates.

**Per-element failure is a Warning, not an Error.**

If element 37 of 500 throws, the other 499 still evaluate, slot 37 is `null`, and the node
emits a Warning naming the failing indices. The fast path runs uncaught until the first
failure and then restarts with catching enabled, so the happy path pays nothing. Do not
"simplify" this by wrapping every element in a try/catch.

**`CrossProduct` raises output rank by *k*, not by 1.**

Ten items crossed with ten items is a 10×10 nested list, not a flat list of 100. This is the
part every implementation gets wrong, and every row of the lacing corpus asserts **value
and rank separately** for exactly this reason — rank bugs are precisely the ones that
survive value-only tests. Do not change lacing semantics without bumping
`graph.formatVersion`; users' graphs depend on the behaviour they were saved against.

**Migrations are JSON-to-JSON, never against typed models.**

A migration written against typed models silently changes meaning every time those models
change. Migrations are never deleted, and each ships with a golden-file test against a real
old-version graph in `tests/corpus/`.

**Callback registries must be cleared before an ALC unload.**

Delegates into user code pin the collectible context. DoodleSharp's resident-assembly cache
came with this warning attached, and it is repeated here because the symptom — a load
context that simply never unloads, with no error anywhere — gives you nothing to search for.
Restart remains the documented default for package upgrades; live unload is a best-effort
optimisation, not a promise.

**`StackOverflowException` cannot be caught, and terminates the process.**

Guard weaving reduces the frequency; it does not fix it, and nothing in .NET does. This is
why autosave is aggressive and why the out-of-process worker seam is kept viable even though
it is deferred past v1. Do not write anything that assumes an orderly shutdown.

**Before adding or bumping *any* package, check what it drags in transitively.**

Every version is pinned exactly — `[5.9.0]`, not `5.9.0` — in
[`Directory.Packages.props`](Directory.Packages.props). CADScript was broken twice by a
package's *dependencies* rather than by the package, and both times the build stayed green
until a real run. [NOTES.md N7](docs/NOTES.md).

**No `-windows` target framework, anywhere.**

One appearing anywhere in the reference graph poisons everything downstream of it and
silently ends the Linux rot-guard job — which would quietly convert the choice of Avalonia
from a strategy into wasted effort. Avalonia does not need one. `Spark.Architecture.Tests`
enforces this (`E11-T8`): it greps every `.csproj` outside `obj/` for the string `-windows`,
so it catches one arriving through a template as readily as one added on purpose.

**No unsafe code.** `AllowUnsafeBlocks=false` everywhere. `Span<T>`, `ref struct` and
`System.Numerics` cover the kernel's needs. Revisit only with a benchmark showing a real
cost, never on taste.

## Code conventions

- **Full words for names.** `tolerance`, not `tol`, outside signatures where `tol` is the
  published parameter name.
- **Comments explain *why*, not *what*.** If a line needs a comment to say what it does,
  rename something instead. Non-obvious numerical behaviour always deserves a comment —
  the next reader will not know why an epsilon is relative rather than absolute there.
- **Explicit usings.** Implicit usings are disabled deliberately;
  [NOTES.md N2](docs/NOTES.md) says why. Do not re-enable them.
- **Nullable reference types are enabled and nullable warnings are errors everywhere**,
  local builds included. Do not silence one with `!` unless you can say why it is safe, in
  a comment.
- **Values are readonly structs** implementing `IEquatable<T>` and passed by `in`. Curves,
  surfaces, meshes and BReps are sealed and immutable, with backing state never handed out —
  `ReadOnlySpan<T>` on hot paths. Mutable **builders** are the only mutable things and
  never escape into the graph. Lazy internal caches are fine: immutability is observable,
  not bitwise.
- **`Angle` in every public angular signature.** No implicit conversion from `double`. This
  is not only safety — it is the typed hook that lets the node generator render a degree
  port automatically, for third-party libraries as well as ours.
- **Kernel operations return `Result<T>`**, carrying diagnostics and partial results.
  Kernel failure is normal and must be diagnosable, not thrown.
- **Geometry has no identity, no style and no screen awareness.** Identity comes from the
  graph, as the tuple `(NodeId, PortIndex, ElementPath)`. Style comes from an explicit
  `Displayable(Geometry, Appearance)` wrapper in `Spark.Api`. C2VGeometry's
  auto-registering `Shape` is the anti-pattern being designed out; do not reintroduce a
  registry, a global counter or a colour field.
- **Test names are full PascalCase sentences with no underscores.** One flat test project
  per source project. Non-parallel collections for anything touching statics.
- **Golden-file failures print a readable diff table** — bounding box, counts, area,
  volume. A bare hash mismatch tells you nothing.

## Repository layout

```text
src/Spark.Geometry/      the kernel: values, curves, surfaces, BRep, mesh, tessellation
src/Spark.Geometry.Io/   OBJ/STL/PLY/glTF/STEP behind reader and writer interfaces
src/Spark.Api/           contracts only. Small, deliberate, strictly additive across 1.x
src/Spark.Engine/        graph model, evaluation, lacing, importer, serialization
src/Spark.Scripting/     Roslyn: compilation, rewriting, source maps, guards, completion
src/Spark.Packages/      NuGet client, per-package-version load contexts, trust store
src/Spark.Nodes.Core/    the first-party node library. NEVER references Spark.Engine
src/Spark.Host/          SparkSession composition root, IHostServices. No UI
src/Spark.Cli/           the `spark` command. Publishes as Spark.Tool
src/Spark.Viewport/      IViewportRenderer, scene, camera, GL + software. Avalonia-free
src/Spark.UI/            Avalonia controls and view models
src/Spark.Desktop/       the application: DI, main window, settings, crash recovery
tests/                   one flat project per source project, plus Docs.Verify,
                         Geometry.Properties, Architecture.Tests and corpus/
bench/Spark.Benchmarks/  nightly, not per-PR                          (not created yet)
docs/                    PRD, EPICS, TASKS, TODO, NOTES, adr/, help/, examples/
scripts/                 repository helper scripts                    (not created yet)
.github/workflows/       CI
```

Two of those lines are intent rather than description, and are marked. Everything else
matches disk, with one qualification worth knowing before you add a project: `tests/` holds
`Spark.Architecture.Tests` and `Spark.Docs.Verify` and nothing else. The remaining test
projects arrive **with the code they test**, not ahead of it, because a test project
containing no tests fails the run outright — [NOTES.md N12](docs/NOTES.md). `tests/` has its
own `Directory.Build.props` carrying `OutputType=Exe`, the xunit v3 reference and the global
`using Xunit`, so a new test project is a near-empty `.csproj` plus a line in `Spark.slnx`.

The reference graph, which `Spark.Architecture.Tests` enforces:

```text
Geometry ─┬─> Geometry.Io
          └─> Api ─> Engine ─┬─> Scripting
                             ├─> Packages
                             └─> Host ─> Cli
Nodes.Core ─> {Geometry, Geometry.Io, Api}
Viewport   ─> {Geometry, Api}
UI ─> {Api, Host, Viewport, Avalonia} ─> Desktop
```

Six rules, all enforced by a passing test rather than by vigilance: `Spark.Api` references
only the BCL and `Spark.Geometry` — never Roslyn, Avalonia, NuGet or `Spark.Engine`;
`Spark.Nodes.Core` never references `Spark.Engine`; `Spark.Viewport` is Avalonia-free;
`Spark.Geometry` depends on nothing but Clipper2; nothing under `src/` references anything
under `tests/`; no `-windows` TFM anywhere.

`Spark.Architecture.Tests` reads the `.csproj` files **as XML and references none of the
projects it inspects** — a test that referenced them could not observe a forbidden reference,
because it would be part of the problem. If you add a project, it is checked with no work on
your part; if you add a reference the graph forbids, `dotnet test` tells you before review
does.

One related rule is **not** yet enforced: *views never touch `Spark.Engine`*. There is no
`Spark.UI` code to check. It returns to the test at M2 (`E8-T11`); until then it is on you.

## Agent ownership

File ownership is **disjoint**, so parallel agents never conflict. This is a lesson learned
the expensive way in DoodleSharp, by splitting `README.md` and `DocGenerator.cs` across two
agents. At most two or three agents run concurrently.

| Agent | Owns | Never touches |
|---|---|---|
| `geometry-kernel` | `src/Spark.Geometry`, `src/Spark.Geometry.Io`, their tests | Anything above `Spark.Api` |
| `graph-engine` | `src/Spark.Engine`, `src/Spark.Api`, `src/Spark.Nodes.Core` | Geometry internals, UI |
| `scripting` | `src/Spark.Scripting`, `src/Spark.Packages` | Geometry, UI rendering |
| `ui-shell` | `src/Spark.UI`, `src/Spark.Desktop` | Engine internals, the kernel |
| `viewport` | `src/Spark.Viewport` | Avalonia — the project is Avalonia-free by rule |
| `docs-author` | `docs/`, `README.md`, `CONTRIBUTING.md`, all XML doc comments | Implementation logic |
| `test-engineer` | `tests/`, `bench/`, `.github/workflows/` | `src/` implementation |
| `reviewer` | Nothing — reviews only | — |

The definitions live in `.claude/agents/`. **Five of the eight are written** —
`geometry-kernel`, `graph-engine`, `scripting`, `docs-author` and `test-engineer` (`E1-T25`).
The three that are not — `ui-shell`, `viewport` and `reviewer` — cover work that has not
started, and they are needed by M2, which is the first milestone to touch any of those areas
(`E1-T30`). Every agent's task ends with a report naming **what it did, what it deliberately
left out, and what it could not verify.**

## What has and has not been proven

As of 2026-08-27, in three tiers. The tiers are the point; collapsing them is the failure.

**Confirmed working, on Windows, by running it.**

- `dotnet build Spark.slnx -warnaserror` — twelve projects, zero warnings, zero errors.
- `dotnet test Spark.slnx` — eleven tests across two projects, all passing.
  `Spark.Architecture.Tests` enforces six reference-graph rules by reading `.csproj` files as
  XML; `Spark.Docs.Verify` checks front matter, worked examples, relative links, ADR
  citations and `Last updated` lines.
- `dotnet format Spark.slnx --verify-no-changes --severity warn` — clean over the M0
  scaffolding. **It is failing as this is written**, on IDE1006 in the in-flight
  `Spark.Geometry` value types (`Angle.cs`, `Tolerance.cs`) — a primary-constructor parameter
  captured into a field does not pick up the `_` prefix the rule expects. That belongs to
  whoever is writing those files, not to this document.

**Written, and never executed.** `.github/workflows/ci.yml`, in full: the
windows-plus-ubuntu build matrix, the `format` job and the `docs-freshness` job. It is
committed. Nobody has watched it run. Its YAML is unvalidated, its Linux leg is unexercised,
and the `docs-freshness` job is `pull_request`-only so it cannot run until a PR exists. **Do
not describe CI as existing without saying it has never run.**

**Not built at all.** Almost every line of product code. Eleven of the twelve `src/` projects
are empty stubs that compile; the first value types are being written into `Spark.Geometry`
and have not been reviewed, documented or reflected in any status table. There is no
benchmark project, there are no public-API baselines, and `docs/examples/` has not been
created.

Keep the distinction honest in anything you write here. CADScript's experience is the
argument for taking it seriously: compile verification was green the entire time, and the
first live run still found three defects it could never have caught — a BCL version
conflict, a Roslyn identity collision with the host's own copy, and a crash on shutdown. All
three were about *what else is already loaded in the process*, which no compiler can see.

**"Compile-verified" and "confirmed working" are not the same claim.** Say which one you
mean, every time. A green build is evidence that the code is well-formed, and nothing more.
