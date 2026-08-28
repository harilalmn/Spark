# Working agreement

For anyone changing this repository — human or AI. Read this before committing.

**Last updated:** 2026-08-28

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

Mechanism 1 works, and has been exercised in anger repeatedly: every public member of
`Spark.Geometry` and `Spark.Nodes.Core` carries an XML doc comment because the build refuses to
produce an assembly without one. Mechanism 2 exists and runs, in the reduced form described
above. Mechanism 3 is **written and has never run** — it is `pull_request`-only and every commit
so far has been a push to `main` (`E11-T14`), so for the moment that third rule rests entirely
on you.

There is a **fourth** mechanism that is not on this list because it is not automatable, and
the geometry kernel's first slice is the reason it gets named at all: **somebody reads it.**
That slice passed all three gates and was rejected, with three of its eight claims false. A
gate proves an absence of the failures it was written to detect, and nothing more.

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

1. `dotnet build Spark.slnx --no-incremental -warnaserror` — clean, **zero warnings**. Use
   exactly this form. `--no-incremental` is not optional caution: without it the warning
   count can be a cached result from a compilation that never ran, and it will read as clean.
2. `dotnet test Spark.slnx` — green. This runs the docs harness and the architecture tests;
   there is no separate command for either. **If it reports `Zero tests ran` for every project,
   that is a toolchain fault and not your change** — run `scripts/run-tests.sh` for a second
   opinion before believing either result ([NOTES.md N34](docs/NOTES.md)).
3. `dotnet format Spark.slnx --verify-no-changes --severity warn` — clean. Use exactly this
   form: it is what the `format` CI job runs, and a shorter one can pass locally where the
   gate fails.
   Two further checks are CI's and are runnable locally when you have touched what they guard:
   `scripts/check-no-native-binaries.sh` (NFR-5, and it is in the build job), and
   `dotnet run --project bench/Spark.Benchmarks --configuration Release -- --filter '*'` when you
   have changed marshalling, evaluation or the canvas spatial index. **The benchmarks now run
   nightly** (`.github/workflows/benchmarks.yml`), and what that job gates is **bytes allocated per
   operation and nothing else**: allocation is a property of the code rather than of the host, so
   it is the one figure a shared runner cannot move. Timings are recorded and never gated — read
   them, do not assume them, and do not put a threshold on one.
   If you add a benchmark, **add its allocation ceiling to `bench/baseline.json` in the same
   change**. The nightly fails on a benchmark with no baseline entry, on purpose: one running
   unguarded is a number nobody is watching. Get the figure from
   `BenchmarkDotNet.Artifacts/results/*-report-full-compressed.json`, or from the job's artifacts,
   and check it locally with
   `python scripts/check-benchmark-regression.py BenchmarkDotNet.Artifacts/results bench/baseline.json`.
   **Measure it with `--job short`, which is what the nightly uses.** That is for consistency
   rather than to correct a known difference: the four `EvaluationBenchmarks` cases were measured
   under both job configs and the allocation figures came back byte-identical, so allocation here
   appears genuinely per-operation. `--job short` is what the nightly runs because a full-fidelity
   run takes over an hour; use the full run when you want to read timings
   ([ADR-0023](docs/adr/0023-benchmarks-gate-allocation-not-time.md)).
4. Documents updated per the table above, including their **Last updated** dates.
5. New user-facing behaviour has a help topic **with a worked example**.
6. New public API on a contract project has an XML doc comment and a public-API baseline
   entry. The baselines are live on all four contract projects, so an unrecorded member is a
   build error (RS0016), not an oversight somebody spots later.
7. **A bug fix has been reverted once, to watch a named test go red.** If nothing failed, the
   regression test is not written yet.
8. Commit message says what changed and why, and **names the task IDs it advances**.
9. Sign off with DCO: `git commit -s`. See [CONTRIBUTING.md](CONTRIBUTING.md).

All three commands above have been verified to work as written, on Windows, on 2026-08-28.
Steps 1 through 3 are gates. A red docs harness is a broken build, including when the only
thing broken is a dangling ADR citation in a build-file comment — that is precisely the point
of it, and it has already caught exactly that (`E1-T29`).

**Everything verified for the current tree was verified on Windows.** CI ran the same three
gates on Windows and Linux and was green on **`53596ab`**, with 969 tests passing on each leg —
which now contains the curve layer, save and load, undo, port types, the creation gesture and the
benchmark project. **The Linux leg has stopped being free insurance and started finding things**:
it caught a script committed without its executable bit, which Git Bash on the Windows runner ran
without complaint ([N28](docs/NOTES.md)). The previous green was **`35107f0`**, which did not
contain the curve layer — the half of the solution where a Linux difference actually shows up, in
floating-point results and culture-dependent formatting. **Say which commit a green CI run was
green on**, and do not carry the sentence forward unchanged: an out-of-date commit hash reads as
a stronger claim than it is.

**A tenth step, and it is not optional for anything with real behaviour: run it.** `dotnet run --project
src/Spark.Desktop --  --graph curves --screenshot PREFIX` opens the application, evaluates,
writes a picture of the shell and a GPU read-back of the viewport, and exits. The curve layer
passed 873 tests and its first screenshot still showed an empty viewport, because three
evaluations were racing at startup — a defect no test in the suite was positioned to see.

## Things that will bite you

**A `dotnet build` reporting "0 warnings" may be reusing a cached analysis.**

Always verify with the flag:

```
dotnet build Spark.slnx --no-incremental -warnaserror
```

MSBuild skips a project whose inputs are older than its outputs. Roslyn analyzers run inside
that compilation, so a skipped project's analyzers do not run — and the summary still prints
`0 Warning(s)`, because there genuinely were none *in the work it did*. A clean build and a
skipped build are indistinguishable from that line.

This has already happened here. The public-API analyzer findings that produced
`PublicAPI.Unshipped.txt` were **invisible under a plain `dotnet build` and appeared the
moment `--no-incremental` was added**. It bites hardest right after you add or reconfigure an
analyzer, because that is exactly when the analyzer is new and the code is not. CI is immune —
a fresh runner has no `obj/` — which is why this is a local trap and why nobody else will
catch it for you. **Do not write "the build is clean" into a document, a review or a commit
message on the strength of an incremental build.** [NOTES.md N15](docs/NOTES.md).

**A fix is not finished until it is regression-proven by reverting it.**

Make the fix, then put the bug back and **name the test that goes red**. Not "a test exists
nearby", not "the suite is green" — the specific test, identified by having watched it fail.
If nothing fails, you have not written the regression test yet; you have written a fix that
the next refactor will silently undo.

This is the standard because its absence is exactly what let the geometry kernel's first
slice pass all three gates with three of its eight claims false. Two of the tests guarding it
were **structurally incapable of failing**: the property drew two independent uniform values
and could not produce a case near the boundary it asserted about — zero violations in five
million simulated draws, against the hundred CsCheck runs per invocation. It reported exactly
like a real test. **Judge a property by its generator, not by its assertion**, and ask of any
test you write or inherit: *how would this fail?* [NOTES.md N18](docs/NOTES.md).

**Changing `Spark.Api` or `Spark.Geometry` is a deliberate act, not a routine one.**

Consumed packages load into per-package-version load contexts, but **contract assemblies
always resolve from the default context** — they have to, because a `Circle` from package A
must be the same `Type` as a `Circle` from package B or nothing can be wired together.
Contract assemblies therefore **cannot be side-by-sided**: there is exactly one `Spark.Api`
and one `Spark.Geometry` in the process, for every package loaded.

What follows from that is real but bounded. A user who compiled their own node DLL against
`Spark.Api` may have to recompile it after upgrading Spark, and `Spark.Geometry` is the
foundation every other kernel type sits on, so churn there moves serialization schema
versions, golden files, generated nodes and help topics with it.

So: prefer an overload to a changed signature, and a new interface to an edited one. Keep
`Spark.Api` **deliberately small** — it is a contract, not a convenience library. When a
break is genuinely the better option, it is allowed within 1.x, but it is a decision with a
record and a release note, never something discovered in a diff afterwards. Public-API
baselines (`E1-T23`) exist to make every change to the public surface a visible line in a
text file — a **review aid, not a compatibility guarantee** — and that only works if you read
the diff. Full reasoning in
[ADR-0019](docs/adr/0019-deliberate-public-api-change-control.md), which supersedes ADR-0009's
stricter rule and says why.

**The no-native-dependencies promise, and the one dependency that looks like it breaks it.**

`Spark.Geometry` may take exactly one third-party dependency: **Clipper2**, pinned at
`[2.0.0]` in `Directory.Packages.props`. Its C# distribution is pure managed and
Boost-licensed, so the promise holds — but only while it is confined. Keep it isolated behind
a **single internal file**, exactly as C2VGeometry already does. Do not let a `Clipper2Lib`
type appear in a public signature, and do not add a second dependency to the kernel without a
very good answer.

**It is not referenced at present.** The `PackageReference` was removed once the value layer
proved it unused (`E2-T39`); `Spark.Geometry` currently references nothing but the BCL. It
returns with the planar boolean pipeline (`E2-T14`), and the version stays pinned so that is
one line and no decision. Do not "restore" it because its absence looks like an omission. The
architecture test asserts a **ceiling — no third-party dependency beyond Clipper2 — not an
exact set**, precisely so it holds on both sides of that round trip.

A CI check asserts no native binaries appear in `Spark.Geometry`'s published output
(`E1-T20`) — *published* there meaning `dotnet publish`, never nuget.org. That check is what
turns a promise into a fact, and it is why the package installer also discloses whether a
**third-party** package ships native binaries: users deserve to know when the promise is
being broken on their behalf.

**And the promise is now narrower than it used to be. Read this before you "fix" either
test.** Under [ADR-0020](docs/adr/0020-occt-via-c-abi-shim.md) the *product* ships
OpenCascade — an open-source, freely redistributable native kernel — in its default install,
because exact booleans, fillet, chamfer, trim and STEP come from it rather than from us. The
promise above attaches to **`Spark.Geometry` the assembly**, not to the product, and in that
form it is unchanged: `Spark.Geometry` stays pure managed and independently distributable,
and the CI check is untouched.

Three rules follow, and each will at some point look like a bug to somebody:

- **`SparkGeometryTakesNoThirdPartyDependencyBeyondClipper` stays exactly as it is.** OCCT is
  a dependency of `Spark.Geometry.Occt`, a different assembly. What that test gets is a
  **companion** rule asserting `Spark.Geometry.Occt` is referenced only by composition roots.
  **Relaxing the original would be the wrong repair.**
- **`AllowUnsafeBlocks` stays `false` repository-wide, with one opt-in.** The `LibraryImport`
  source generator emits unsafe code and requires it true, so `Spark.Geometry.Occt` sets it in
  its own csproj with a comment naming ADR-0020 — and an architecture test asserts it is the
  **only** project doing so. Do not move it to `Directory.Build.props`.
- **No `-windows` TFM, still, anywhere.** The whole reason the binding is a C ABI rather than
  C++/CLI is that C++/CLI would have been Windows-only permanently, killing the Linux
  rot-guard and reversing ADR-0001. That is 15–25% more binding effort deliberately spent.

**Nothing in this repository is published to nuget.org, and `IsPackable` is `false`
everywhere.**

Spark **consumes** NuGet packages and loose DLLs so that a user can bring any .NET library
into a graph and get nodes from it; it does not publish its own. There is no `PackageId`, no
`PackAsTool` and no package metadata in any of the twelve projects, and this is deliberate —
the reasoning is in a comment in [`Directory.Build.props`](Directory.Build.props) and in
[NOTES.md N14](docs/NOTES.md). Do not add packaging properties to a `.csproj` because it
looks like an omission. Two corollaries: a project's assembly name is its only name, so
there are no package-ID renames to keep track of; and `Spark.Cli` builds `spark.exe` and
ships beside the desktop application rather than installing as a dotnet global tool.

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
src/Spark.Api/           contracts only. Small, deliberate, changed only on purpose
src/Spark.Engine/        graph model, evaluation, lacing, importer, serialization
src/Spark.Scripting/     Roslyn: compilation, rewriting, source maps, guards, completion
src/Spark.Packages/      NuGet client, per-package-version load contexts, trust store
src/Spark.Nodes.Core/    the first-party node library. NEVER references Spark.Engine
src/Spark.Host/          SparkSession composition root, IHostServices. No UI
src/Spark.Cli/           the `spark` command. Builds spark.exe, ships beside the app
src/Spark.Viewport/      IViewportRenderer, scene, camera, GL + software. Avalonia-free
src/Spark.UI/            Avalonia controls and view models
src/Spark.Desktop/       the application: DI, main window, settings, crash recovery
tests/                   one flat project per source project, plus Docs.Verify,
                         Geometry.Properties, Architecture.Tests and corpus/
bench/Spark.Benchmarks/  BenchmarkDotNet: marshalling, evaluation, the canvas spatial index
docs/                    PRD, EPICS, TASKS, TODO, NOTES, adr/, help/, examples/
scripts/                 repository helper scripts
.github/workflows/       CI
```

Two of those lines are intent rather than description, and are marked. Everything else matches
disk, with one qualification worth knowing before you add a project: `tests/` holds **seven**
projects — `Spark.Architecture.Tests`, `Spark.Docs.Verify`, `Spark.Geometry.Tests`,
`Spark.Geometry.Properties`, `Spark.Engine.Tests`, `Spark.UI.Tests` and `Spark.Viewport.Tests`
— and nothing else. `tests/corpus/` does not exist yet. Each of the last three arrived **with
the code it tests**, not ahead of it, because a test project containing no tests fails the run
outright — [NOTES.md N12](docs/NOTES.md). The two geometry projects remain the only ones granted
`InternalsVisibleTo` on the kernel, and that is a deliberate ceiling of two —
[NOTES.md N10](docs/NOTES.md). `tests/` has its
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
`Spark.Geometry` takes **no third-party dependency beyond Clipper2** — a ceiling, not an
exact set, and it currently takes none at all; nothing under `src/` references anything
under `tests/`; no `-windows` TFM anywhere.

`Spark.Architecture.Tests` reads the `.csproj` files **as XML and references none of the
projects it inspects** — a test that referenced them could not observe a forbidden reference,
because it would be part of the problem. If you add a project, it is checked with no work on
your part; if you add a reference the graph forbids, `dotnet test` tells you before review
does.

One related rule was not enforceable while there was no `Spark.UI` code to check: *views never
touch `Spark.Engine`*. There is now, and `Spark.Architecture.Tests` checks it (`E8-T11`).

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

The definitions live in `.claude/agents/`, and **all eight are written** (`E1-T25`, `E1-T30`).
Every agent's task ends with a report naming **what it did, what it deliberately left out, and
what it could not verify.**

## What has and has not been proven

As of 2026-08-28, in three tiers. The tiers are the point; collapsing them is the failure.

**Confirmed working, on Windows, by running it.**

- `dotnet build Spark.slnx --no-incremental -warnaserror` — **sixteen** projects, zero
  warnings, zero errors. Use the flag: without it the warning count can come from a cached
  analysis (see *Things that will bite you*).
- `dotnet test Spark.slnx` — **1,115 tests across seven projects**, all passing. The same
  1,115 are reported by `scripts/run-tests.sh`, which runs each project as the executable
  Microsoft.Testing.Platform makes it; the two must agree ([NOTES.md N34](docs/NOTES.md)).
  `Spark.Geometry.Tests` (429) covers the kernel by example; `Spark.Geometry.Properties` (56)
  covers it with CsCheck properties over generators spanning 1e-9 to 1e9; `Spark.Engine.Tests`
  (292) covers the graph, the replicator, the importer and the `.spark` format, including a two-way diff against the
  lacing specification and another against `Spark.Nodes.Core`; `Spark.UI.Tests` (256) drives the
  canvas headlessly with real pointer gestures; `Spark.Viewport.Tests` (69) covers the scene
  builder and the camera; `Spark.Architecture.Tests` (8) enforces the reference-graph rules by
  reading `.csproj` files as XML; `Spark.Docs.Verify` (5) checks front matter, worked examples,
  relative links, ADR citations and `Last updated` lines.
- `dotnet format Spark.slnx --verify-no-changes --severity warn` — clean over the whole
  solution.
- **The application.** `dotnet run --project src/Spark.Desktop` opens the shell, evaluates the
  seeded graph and draws geometry through OpenGL. `--graph curves --screenshot PREFIX` captures
  the shell and a GPU read-back of the viewport and exits, which is how the curve demo was
  checked rather than assumed.

**Confirmed working on Linux, by CI, on a named commit.** The build matrix, the test run, the
format check and the no-native-binaries check were green on **`53596ab`** on `windows-latest` and
`ubuntu-latest` (run 33153282431), 969 tests on each. That commit contains everything through the
canvas creation gesture. **Do not describe CI as green without saying which commit it was green
on** — and update the hash when you push, because a stale one reads as a stronger claim than it
is.

**Reviewed, repaired and accepted.** The geometry kernel's **value layer** — thirteen types in
`src/Spark.Geometry` declaring 387 public members, all documented, all in the public-API
baseline. With the curve layer the project now declares **487 members over 19 types**. It is
a stronger claim than the tiers above, and it was earned rather than assumed: the first attempt
passed all three gates and was **rejected**, with three of its eight claims false and both of
its guarding tests structurally incapable of failing ([NOTES.md N18](docs/NOTES.md)). Every fix
since is regression-proven by reverting it and naming the test that goes red.

The **curve layer** was held to the same standard and adds one practice to it: a mutation sweep
per slice. Six deliberate mutations, four killed by named tests, and the two survivors were the
valuable part — a test that asserted a normalised quantity and so could not see an error in that
quantity's scale ([N19](docs/NOTES.md)), and a branch that no input could reach
([N20](docs/NOTES.md)). Both were in code that was green.

**Undo and redo** were swept the same way and produced a third instance of the same shape. Three
mutations, two killed, and the survivor was a test asserting that clicking a node is not an edit:
it passed under a mutation that recorded *every* drag, because a click raises no pointer-move
event and so never reached the guard the test existed to check. It was green, it was about the
right behaviour, and it could not fail. The repair was both halves — the guard became a **net**
displacement rather than a flag set on the first move, and a second test drags a node out and
back to where it started. **Write the mutation before you believe the test.**

**Port type labels** were swept next: four mutations, three killed, and the survivor was the same
lesson a fourth time. A test asserting that a node grows wide enough for its widest port row passed
under a mutation that removed the row measurement entirely — because the node it chose has a long
enough *title* to clear the minimum width on its own, so the assertion "wider than the minimum" was
true either way. The bound is now above what the title alone asks for, and the arithmetic is in the
test. **An assertion that would also hold with the feature removed is not an assertion.**

**Written, and not executed at all.** The `docs-freshness` CI job. It is `pull_request`-only
and every commit so far has been a push to `main`, so it has never run once.

**Not built at all.** No surfaces, meshes, BRep or solids; no `NurbsCurve`; no `Quaternion`.
`Spark.Geometry.Io`, `Spark.Scripting`, `Spark.Packages` and `Spark.Cli` are empty
or stubs. There is no benchmark project, and there is no OpenCascade anywhere in the tree.

Keep the distinction honest in anything you write here. CADScript's experience is the argument
for taking it seriously: compile verification was green the entire time, and the first live run
still found three defects it could never have caught — a BCL version conflict, a Roslyn identity
collision with the host's own copy, and a crash on shutdown. All three were about *what else is
already loaded in the process*, which no compiler can see. This project has now paid the same
lesson twice more: the screenshot that was supposed to confirm the curve demo instead showed
three overlapping evaluations racing at startup, which every test in the suite had passed over.

**"Compile-verified" and "confirmed working" are not the same claim.** Say which one you
mean, every time. A green build is evidence that the code is well-formed, and nothing more.
