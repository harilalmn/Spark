# Spark — Implementation Notes

Non-obvious implementation facts, numbered. Adopted from DoodleSharp's convention.

**Last updated:** 2026-08-30 (N42 added)

---

## How this file works

**A note records a non-obvious implementation fact.** Something a reader of the code would
otherwise have to work out, or worse, would get wrong. It is not a decision log and it is
not user documentation.

The taxonomy, written down here so it is not re-litigated every time someone has something
to write down:

| Where it goes | What it is |
|---|---|
| `docs/adr/` | A **decision** that could have gone differently. Names the alternative and why it lost |
| `docs/NOTES.md` | A **non-obvious implementation fact**. Why the code is shaped the way it is |
| `docs/help/` | Something a **user** needs to know |
| XML doc comment | **What this member does** |

### Numbering rules

1. **Numbers are stable.** `N7` means the same thing for the life of the repository.
2. **Numbers are never reused.** If `N7` is deleted, `N7` stays deleted. Nothing else ever
   becomes `N7`.
3. **Notes are never renumbered.** Not to close a gap, not to reorder by topic, not ever.
   Every commit message, code comment and pull-request discussion that cites `N7` must keep
   pointing at the same fact.
4. **Gaps are left on deletion.** A missing number is information — something used to be
   there. Write `## N7 — *(withdrawn: reason)*` rather than silently closing up.
5. New notes take the next unused number, whatever the file's current ordering.

Cite notes from code comments where the fact is load-bearing: `// See NOTES.md N5.`

---

## N1 — The solution is `Spark.slnx`, not `Spark.sln`

`.slnx` is the XML solution format. `.sln` is a hand-rolled, comma-delimited text format
carrying a `Project(...) = ...` line, an `EndProject`, a GUID for the project, a *second*
GUID for its type, a configuration entry per configuration per project, and a nesting entry
if it lives in a solution folder. Adding one project to a twelve-project solution touches
five separate regions of the file, and two of them are ordered by GUID.

Spark expects drive-by pull requests, and several of the twelve projects will gain
siblings — two test projects have arrived, more will follow as the code they test lands
(`N12`), and a benchmark project is still to come (`E1-T13`). Every one of those is a merge
conflict in `.sln` and a one-line addition in `.slnx`:

```xml
<Project Path="src/Spark.Geometry/Spark.Geometry.csproj" />
```

This is the same reasoning that makes `.spark` plain canonical JSON rather than a zip
container: for a project whose users collaborate through git, a diffable and mergeable file
is worth more than a tidy one.

The cost is tooling age. `.slnx` needs a recent SDK and a recent Visual Studio. Given
**D7** — `net10.0` everywhere, no exceptions — nobody building Spark has an old toolchain
anyway, so the cost is zero here specifically.

`dotnet build Spark.slnx` works exactly as `dotnet build Spark.sln` would.

## N2 — Implicit usings are disabled, deliberately

`Directory.Build.props` sets `<ImplicitUsings>disable</ImplicitUsings>`. This is not an
oversight and it is not a style preference.

Two reasons, both specific to what Spark is.

**`Spark.Geometry` is a library people script against.** A user writing a C# code block sees
the namespace layout through the `using` lines in the examples they copy. If our own source
has no `using` lines because the SDK injected them, the examples in the XML doc comments
have no `using` lines either — and a code block does not get the SDK's implicit set. A
sample that compiles in our repository and fails in the product is exactly the class of rot
the docs harness exists to prevent, and this is the cheapest way to not create it.

**A global using can silently collide with a user type.** The implicit set for a library
includes `System.Linq`, `System.Collections.Generic` and friends. When a package author
defines their own `Enumerable` or their own `Task`, the resulting ambiguity error points at
a `using` line that appears nowhere in the file. Explicit usings make the collision visible
at the point that caused it.

CADScript reached the same conclusion for the same reason, and its `ScriptImports.Default`
list is a single source of truth shared between the runtime and the verifier. Spark will
need the same thing for `Spark.Scripting`'s prelude (`E6-T5`).

## N3 — Warnings are errors in CI, never in the csproj

`Directory.Build.props` sets `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>`. CI
passes `-warnaserror` on the command line instead (`E1-T15`).

The failure mode being avoided is behavioural, not technical. With warnings-as-errors baked
into the project file, a developer who declares a variable and has not yet used it cannot
build — mid-edit, mid-thought. The rational response is to pass `-warnaserror:false` or
`-p:TreatWarningsAsErrors=false` locally, and once that is in someone's shell history it is
in every build they run. The gate then protects nothing while appearing to protect
everything, which is worse than no gate: it is a gate people have learned to route around.

Putting it on the command line inverts the pressure. Local development stays pleasant, and
the gate is absolute in the only place that decides whether code merges. There is no flag a
contributor can pass to make a red CI build green.

One exception is baked in permanently:

```xml
<WarningsAsErrors>$(WarningsAsErrors);nullable</WarningsAsErrors>
```

Nullable warnings are errors everywhere, local included. A nullability warning is a real
defect being announced, not a housekeeping note, and unlike an unused variable it is never
a transient state of a half-written line.

The same reasoning is why the `.editorconfig` is deliberately small: a wall of warnings
trains people to ignore warnings.

## N4 — CS1591 is an error on the contract projects only

`Directory.Build.props` promotes CS1591 — *missing XML comment for publicly visible type or
member* — to an error, conditioned on the project name:

```xml
<PropertyGroup Condition="'$(MSBuildProjectName)' == 'Spark.Api'
                       Or '$(MSBuildProjectName)' == 'Spark.Geometry'
                       Or '$(MSBuildProjectName)' == 'Spark.Geometry.Io'
                       Or '$(MSBuildProjectName)' == 'Spark.Nodes.Core'">
```

**Why these four.** They are the projects whose public surface is somebody else's input.
`Spark.Api` and `Spark.Geometry` are the contract assemblies a third-party node library
compiles against; `Spark.Geometry.Io` sits directly beside them in what a code block scripts
against; and every public member of `Spark.Nodes.Core` becomes a **node**, whose XML summary
becomes its runtime tooltip. An undocumented member there is not an untidy library, it is a node in the product
with an empty description panel.

**Why not everywhere.** `Spark.Engine`, `Spark.UI`, `Spark.Desktop` and the rest have
public members only because C# has no better word for "visible to the next assembly up".
Nobody outside this repository compiles against them, and forcing prose onto every internal
plumbing type would produce a great deal of `/// <summary>The graph.</summary>` — which is
worse than nothing, because it makes the documentation look complete.

`GenerateDocumentationFile=true` is on for **every** project regardless, so the `.xml`
sidecars exist everywhere and IntelliSense benefits wherever comments have been written.
Only the *enforcement* is scoped.

**This is the mechanism that makes the standing documentation instruction real rather than
aspirational.** It is one of three: this, the docs harness (`E11-T1`), and the
`docs-freshness` CI job (`E11-T14`). Do not suppress CS1591 to unblock yourself; write the
comment.

**A second mechanism has since converged on the same four.** The public-API baselines
(`E1-T23`, [N17](#n17--rs0026-is-suppressed-rs0016-is-the-rule-that-matters)) are applied by
`Directory.Build.props` to exactly this list, under an identically shaped condition. Keep the
two conditions in step: a project that is a contract for CS1591's purposes is a contract for
RS0016's purposes, and a divergence between the two lists would be an accident rather than a
position.

**Discrepancy on record.** The approved plan named *three* projects here;
`Directory.Build.props` covers four, adding `Spark.Geometry.Io`. Including it looks correct —
its readers and writers are part of the surface a code block scripts against, and a user
exporting an OBJ meets it directly — but it is an unreviewed divergence and is logged as
[PRD Q4](PRD.md#14-open-questions). The original justification for including it was that it
was `IsPackable` beside the other two; that justification is gone (`N14`), and the question
is left open on the merits rather than closed on a premise that no longer holds.

## N5 — `Spark.Nodes.Core` must never reference `Spark.Engine`

The rule is commented in `src/Spark.Nodes.Core/Spark.Nodes.Core.csproj` and is enforced by
`Spark.Architecture.Tests` (`E11-T8`), which asserts that the project's references are
exactly `{Spark.Api, Spark.Geometry, Spark.Geometry.Io}` — an equality, not an absence, so
the rule cannot be eroded one convenient reference at a time. It looks like an arbitrary
layering purity rule. It is not; it is a functional requirement wearing a layering costume.

`Spark.Nodes.Core` references `Spark.Geometry`, `Spark.Geometry.Io` and `Spark.Api`, and
nothing else. It therefore **cannot** know that a graph engine exists, cannot register a
node with it, and cannot hand it a hand-built `NodeDefinition`. The only way the first-party
node library can reach the product is by being discovered by the same **zero-config
reflection importer** that a stranger's NuGet package goes through (`E5-T2`).

That is the entire point. If first-party nodes had a private door, every gap in the
importer would be invisible to us and fatal to everybody else: an overload the importer
mishandles, a generic method it skips, a `Task<T>` it fails to await, an `out` parameter it
drops. We would ship a beautiful built-in library over a broken extensibility story and
never notice, because our own nodes would work.

Two consequences follow, and both are intended:

- **When the importer cannot express something, that is a bug in the importer**, not a
  reason to reach for a reference. `Spark.Nodes.Core` is the importer's most demanding
  customer and its permanent test case.
- **Anything a node needs from the engine must be a contract in `Spark.Api`** —
  `SparkList`, `SparkDiagnostic`, the node attributes, `Appearance` and `Displayable`.
  Which is a second reason to keep `Spark.Api` small and deliberate: it is a contract, not
  a convenience library, it cannot be side-by-sided, and every type added to it is a type a
  user's own node DLL may end up compiled against — so changes to it are deliberate rather
  than routine (`E7-T4`, `R7`, ADR-0019).

The inconvenience is real and is accepted; it is listed in
[TODO.md](TODO.md#known-and-deliberately-accepted) so it is not rediscovered as friction.

## N6 — `Spark.Viewport` references no Avalonia package

Commented in `src/Spark.Viewport/Spark.Viewport.csproj` and enforced by
`Spark.Architecture.Tests` (`E11-T8`), which asserts that no package reference in the
project has a name beginning `Avalonia` — a prefix test rather than a list, so
`Avalonia.Skia` or a package nobody has heard of yet is caught the same way `Avalonia` is.
`Spark.Viewport` depends on `Spark.Api` and `Spark.Geometry` only. `Spark.UI` is the sole adapter, wiring the renderer to Avalonia's
`OpenGlControlBase`.

The rule pays for itself in one place: **the software renderer is only usable headlessly if
nothing in its project needs a UI toolkit to start.** That headless capability is not a
nicety — it is the only route by which viewport output becomes testable at all. GPU output
varies by driver, by machine and by whether anyone is logged in; software output is
deterministic, so `spark render` can produce a picture a CI job diffs against a golden image
(`E9-T5`, `E9-T12`). It also gives headless thumbnails for free, and a real fallback when
GL initialisation fails on a virtual machine or over RDP.

Take an Avalonia reference for one convenience type — a `Color`, a `Point`, a `Rect` — and
all of that quietly stops working, in a way no test notices until someone tries to run the
renderer on a build agent.

## N7 — Every package version is pinned exactly, including transitively

`Directory.Packages.props` uses Central Package Management with
`CentralPackageTransitivePinningEnabled`, and every version is written in exact-match
bracket notation: `[5.9.0]`, not `5.9.0`.

Plain `5.9.0` in NuGet means *5.9.0 or newer*, not *5.9.0*. Two developers restoring on
different days can get different builds from identical source, and the difference surfaces
at run time on somebody else's machine.

CADScript paid for this lesson twice, and both times the package itself was innocent — what
broke was **what the package dragged in**. A Roslyn bump moved
`System.Collections.Immutable` to a major version the host runtime would not bind; the build
was green throughout and the failure appeared on the first real script run. The rule that
came out of it applies here unchanged:

> **Before adding or bumping any package, check what it drags in transitively.**

Exact pinning does not prevent that problem. It makes hitting it a deliberate, reviewed,
single-line change in one file, instead of a surprise arriving on a Tuesday.

## N8 — `.spark` and `.sparkcustom` are `text eol=lf` in `.gitattributes`

```gitattributes
*.spark       text eol=lf
*.sparkcustom text eol=lf
*.sparkz   binary
*.sparkgeo binary
```

`.spark` is plain, canonically formatted JSON — stable key order, invariant number
formatting — specifically so graphs **diff and merge in git**. For an open-source tool whose
users share work on GitHub, that is worth more than container tidiness. Marking the
extension `text` is what completes it: without the attribute, git treats an unknown
extension conservatively and a graph authored on Windows shows as a whole-file change to
someone on Linux.

`eol=lf` rather than plain `text` because the writer emits `\n` and the round-trip must be
**byte-identical** (`E3-T18`). Letting git normalise line endings per platform would make a
byte-identical round trip untestable across the CI matrix, which is exactly where it is
tested.

The two genuinely binary formats are marked binary: `.sparkz`, which is a zip, and
`.sparkgeo`, the compact bulk-geometry format that exists because JSON for a
500k-triangle mesh is roughly 30× the size and 50× the parse time.

## N9 — Tolerance is passed, never ambient, because it is hashed into cache keys

Signatures take `in Tolerance tol = default`, where `Linear == 0` means *use the document
default*. There is no ambient tolerance, no static `Tolerance.Current`, no thread-local.
`GeometryTolerance` in C2VGeometry has `const` epsilon defaults, and those bake into every
caller at compile time; the ~25 helper *bodies* are worth extracting, the model around them
is not (`E2-T4`).

The usual argument for explicit tolerance is testability. Spark's argument is stronger and
more specific.

Document tolerance lives on the document, flows through `EvaluationContext`, and is
**hashed into every node's cache key** (`E3-T22`). Changing it therefore invalidates exactly
the nodes it affects and nothing else. An ambient tolerance would be **invisible to the
cache**: the key would not change, the cache would hit, and the graph would silently serve
results computed at the old tolerance. Silently wrong geometry, with no error and no way for
a user to tell.

That is the decisive argument, and it also explains why per-call ergonomics were preserved
rather than sacrificed. `default` is not a hidden global — it resolves against the context
the node is already being evaluated in, and that context is part of the key.

Separately, `Tolerance` is **scale-aware**: `Tolerance.ForScale(characteristicLength)`,
because a fixed `1e-6` is wrong for kilometres and wrong for microns. This survives **D12**
untouched — it is numerical robustness, not units.

**What is implemented today, as distinct from what is described above.** `Tolerance` landed
in `src/Spark.Geometry` on 2026-08-27 (`E2-T4`) and the sentinel works exactly as described:
a zero backing `Linear` resolves to `Tolerance.Default`, `Tolerance.Default == default` is
`true`, and reading `Linear` on a default-constructed value returns `1e-6` rather than zero.
There is no `EvaluationContext` yet, so "the document default" is currently a **fixed** set of
components — linear `1e-6`, angular `0.001°`, relative epsilon `1e-12` — rather than one that
flows from a document. The cache-key argument is the reason the shape was chosen; it is not
yet a mechanism that exists, and `E3-T22` is where it becomes one.

Two implementation facts about the type are worth having here rather than only in its XML
docs. First, the default components are **private** consts, not public ones: a public `const`
bakes into every consuming assembly at compile time, so a node DLL built against one version
would carry that version's epsilon forever — precisely the C2VGeometry problem this note opens
with, hiding inside a constant. Second, `ForScale` and `Scaled` floor the derived linear
tolerance at `1e-15`, because a derived value of zero would be read straight back as the
"use the default" sentinel and silently widen the tolerance instead of tightening it.

## N10 — `Spark.Geometry` grants `InternalsVisibleTo` to exactly two test assemblies

```xml
<InternalsVisibleTo Include="Spark.Geometry.Tests" />
<InternalsVisibleTo Include="Spark.Geometry.Properties" />
```

**Both projects now exist.** When this note was first written neither did, and it recorded
the two entries as a declaration of intent about test shape so that a reader would not delete
them as dead configuration. The intent has been carried out: `tests/Spark.Geometry.Tests`
holds 276 example-based tests and `tests/Spark.Geometry.Properties` holds 28 CsCheck
properties (`E2-T33`, `E11-T10`), and both are in `Spark.slnx`.

The rule the two entries encode is unchanged and still worth stating. The kernel grants
privileged access to **exactly two** test assemblies — one conventional, one property-based —
and both target `Spark.Geometry` specifically. `Spark.Architecture.Tests` and
`Spark.Docs.Verify` are deliberately not named, because neither needs internals: one reads
`.csproj` files as XML and the other reads Markdown.

Every other test project sees the public surface only, which is the same surface a user sees.
Anything wanting a third entry here is a signal to look again at whether the thing being
tested should be public — or at whether the test should be written against the public surface
instead.

## N11 — `global.json` pins the SDK *and* selects the test runner, and the second half is not optional

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

The `sdk` block is ordinary hygiene: one pin, honoured by local builds and by
`actions/setup-dotnet` in CI alike, so nobody builds Spark on a toolchain nobody else has.

**The `test` block is the part worth writing down, because it looks like a preference and is
not.** The .NET 10 SDK has removed the VSTest bridge. There is no longer a supported path
where `dotnet test` drives a VSTest-shaped test project, so a project built the way every
tutorial written before 2026 describes does not degrade to a slower path — it fails at build
with an explicit error. Microsoft.Testing.Platform is the only shape available, and under it
a test project is a **real executable that hosts its own runner**, which is why
`tests/Directory.Build.props` sets `<OutputType>Exe</OutputType>` on projects that produce no
program anyone runs by hand.

Two consequences follow that are otherwise puzzling:

- `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` remain pinned in
  `Directory.Packages.props` and are **referenced by nothing**. They are the VSTest-era
  packages. They are kept pinned rather than deleted only so that adding one back is a
  reviewed decision rather than a fresh version choice made in a hurry.
- The question the plan carried — *is xunit v3 viable, and is the fallback to 2.9.x
  costless?* (`E1-T27`, formerly PRD Q9) — turned out to be moot rather than answerable.
  There is no fallback. xunit v3 on Microsoft.Testing.Platform is the only configuration that
  builds.

## N12 — A test project containing no tests **fails**, so test projects arrive with the code they test

Under Microsoft.Testing.Platform, running a test project that discovers zero tests is an
error, not a vacuous pass. The run exits non-zero and takes `dotnet test` down with it.

This contradicted the original plan directly. `E1-T12` called for **nine** test projects
created up front, one per source project, so that the harness could never be retrofitted.
Three of them were created as empty stubs and then deleted, because an empty stub is a red
build every single time anyone runs the tests, for as long as the project it shadows has no
code — which for most of them is months.

The policy that replaced it: **a test project is created alongside the code it tests.** Two
exist because both had something real to check on day one — `Spark.Architecture.Tests` checks
the `.csproj` files, and `Spark.Docs.Verify` checks the documents; neither needs a line of
product code to exist.

Nothing about the *argument* for early gates is withdrawn. A gate added after the code exists
is a gate that gets an exemption for everything already there, and that remains true. What
changed is only the mechanism: the gate arrives with the first thing it can check, not before
it.

The same reasoning is why `Spark.Docs.Verify` contains no placeholder for the checks it
cannot yet perform — see `N13`.

## N13 — `Spark.Docs.Verify` deliberately contains no stub for a check it cannot yet run

Sample compilation, node-to-topic coverage in both directions, and `SPK####`-code coverage
are all named in `E11-T2` … `E11-T6` and none of them is in the file. Each needs compiled
Spark assemblies to check anything, and there are none.

The temptation is to write them now as tests that enumerate an empty set and pass. That is
worse than leaving them out, for a reason DoodleSharp demonstrated at length: **a test that
passes by doing nothing is indistinguishable, from the outside, from a test that passes by
checking something.** It appears in the run output, it is counted, it makes the suite look
complete, and the day the thing it was supposed to guard finally exists nobody notices that
it is still enumerating nothing. DoodleSharp's help had 101 of 108 public constructors
rendering blank underneath a green test suite.

So the checks live in `TASKS.md` as work, and the class comment in `DocumentationChecks.cs`
says why the file is shorter than the task register implies. A gap that is written down is a
gap; a green stub is a lie with a tick beside it.

The corollary applies when adding one: a check arrives **with** the first thing it can check,
and its first run must be able to fail. If a new check passes the moment it is written, prove
it can fail before committing it.

## N14 — Nothing in this repository is published to nuget.org

`Directory.Build.props` sets `<IsPackable>false</IsPackable>` for every project, with the
reasoning in a comment beside it. There is no `PackageId`, no `PackAsTool`, no
`ToolCommandName` and no package metadata anywhere in the twelve projects. This is a
deliberate, checked position, not an oversight waiting to be corrected by whoever next opens
a `.csproj`.

**Spark consumes NuGet; it does not produce it.** The two directions are easy to conflate and
the requirement only ever pointed one way:

- **Consuming is a core feature and is unaffected.** `Spark.Packages` is a NuGet client, a
  Spark package is an ordinary NuGet package tagged `spark`, and a user brings any .NET
  library — a package from nuget.org, a private feed, or a DLL they built this morning — into
  a graph and gets nodes from it by reflection. That is E7, FR-40 to FR-45, and it is the
  equivalent of Dynamo's Package Manager.
- **Producing is not a feature at all.** Nothing here goes to nuget.org. Embedders reference
  `Spark.Host` from an install, and node authors reference `Spark.Api` and `Spark.Geometry`
  from an install — which is how CAD add-ins are built anyway, because a Revit or AutoCAD
  add-in is already resolving assemblies out of a directory rather than restoring them.

Three things follow that a reader would otherwise get wrong:

1. **A project's assembly name is its only name.** There is no package ID to be distinct
   from it, so the assembly-name-versus-package-ID splits that earlier revisions of these
   documents described — `Spark.Cli` → `Spark.Tool`, `Spark.Nodes.Core` → `Spark.Nodes`,
   `Spark.Engine` → `Spark.Graph` — do not exist. `Spark.Cli` builds `spark.exe` through
   `<AssemblyName>spark</AssemblyName>` and ships beside the desktop application; it is not
   a dotnet global tool.
2. **"Published output" in this repository means `dotnet publish`, never nuget.org.** The
   no-native-binaries CI check (`E1-T20`, NFR-5) inspects the publish directory of
   `Spark.Geometry`. Do not read it as a claim about packaging.
3. **Public-API baselines are kept as a review aid, not as a compatibility guarantee.**
   ADR-0019 explains what change control on `Spark.Api` and `Spark.Geometry` is now for, and
   why the superseded ADR-0009 argued something stronger.

`docs/adr/0019-deliberate-public-api-change-control.md` is the decision record; this note
exists because the fact is discoverable only from a comment in a build file, and a fact that
lives in one comment is a fact that gets re-litigated.

---

## N15 — A `dotnet build` reporting "0 warnings" may be reusing a cached analysis

**Verify a clean build with `--no-incremental`, or do not claim it.**

```
dotnet build Spark.slnx --no-incremental -warnaserror
```

MSBuild's incremental build skips a project whose inputs are older than its outputs. Roslyn
analyzers run as part of that compilation, so when the compilation is skipped the analyzers do
not run either — and the build summary still prints `0 Warning(s)`, because there were no
warnings *in the work it did*. The number is true and the conclusion a reader draws from it is
false.

This is not hypothetical here. The public-API analyzer findings that produced
`PublicAPI.Unshipped.txt` were **invisible under a plain `dotnet build` and appeared the
moment `--no-incremental` was added**. Nothing about the earlier output looked wrong; a clean
build and a skipped build are indistinguishable from the summary line.

Three consequences worth internalising:

1. **CI is not exposed to this**, because a fresh runner has no `obj/` to reuse. That is
   precisely why the trap is a *local* one: the thing that catches it is the thing you were
   not running.
2. **It is worst exactly when it matters most** — after adding or reconfiguring an analyzer.
   The analyzer is new, the code is not, so the projects that would surface its findings are
   the projects MSBuild is most confident it can skip.
3. **"It built clean" is a claim about a compilation, not about a command.** Before writing
   that sentence into a document, a review or a commit message, run the full command above.
   The same applies to any statement in `docs/` that a gate is green.

`dotnet test` and `dotnet format` do not share this failure mode in the same way — `format`
re-reads sources, and a test run that skips a project also reports fewer tests, which is
visible. It is the warning count that lies quietly.

---

## N16 — Private `const` fields are PascalCase, and the rule must come first in `.editorconfig`

Private fields in Spark are `_camelCase`. Private `const` fields are **`PascalCase`**, as are
private `static readonly` fields. Both exceptions are deliberate and both are enforced.

The reason is not aesthetic. It is that the alternative sets two analyzers arguing:

- **CA1802 actively pushes code towards `const`.** It flags a `static readonly` field whose
  value is a compile-time constant and tells you to make it `const`.
- If private consts required an underscore, taking CA1802's advice would immediately raise
  IDE1006 on the field you just changed, and satisfying IDE1006 would mean renaming a field
  for no reason a reader could see.

So the rules are aligned rather than left to fight: things that cannot change are PascalCase,
and **the underscore is reserved for mutable instance state**, which makes the underscore
itself informative. A reader seeing `_linear` knows it can change; a reader seeing
`DefaultLinearTolerance` knows it cannot.

**The ordering in the file is load-bearing.** `.editorconfig` naming rules are evaluated in
file order and **the first matching rule wins**, so `private_const_pascal` must appear *before*
the general private-field underscore rule. Move it below and it stops doing anything, silently
and with no diagnostic: the general rule matches first and the const rule is never consulted.

This gap was found by the geometry value layer rather than by inspection. `Tolerance` carries
four private consts, `dotnet format --verify-no-changes` failed IDE1006 on them, and the rule
was written in response. Related: `E1-T32`.

---

## N17 — RS0026 is suppressed; RS0016 is the rule that matters

`Microsoft.CodeAnalysis.PublicApiAnalyzers` is referenced from `Directory.Build.props` for the
four contract projects — `Spark.Api`, `Spark.Geometry`, `Spark.Geometry.Io` and
`Spark.Nodes.Core` — each with a `PublicAPI.Shipped.txt` and a `PublicAPI.Unshipped.txt` as
`AdditionalFiles`. Two of its rules are worth telling apart, because they are aimed at very
different things and only one of them fits Spark.

**RS0016 — "Symbol is not part of the declared API" — stays at its default error severity.**
It is the whole point of having baselines: a public member that is not written down in
`PublicAPI.Unshipped.txt` does not build, so every change to a public surface arrives as a
reviewable line in a text file rather than as something noticed a month later. It has been
proved to fire rather than assumed to — adding a public member to `Spark.Geometry` and
rebuilding fails the build with RS0016.

**RS0026 — "Do not add multiple public overloads with optional parameters" — is off**, with
the reasoning recorded in `.editorconfig` beside the suppression. It exists to prevent a
future **source-breaking** change in a library that other people compile against: where two
overloads both carry optional parameters, adding a parameter later can silently change which
one a caller binds to.

It does not fit here, for three independent reasons, any one of which would be enough:

1. **Spark publishes nothing** (ADR-0019, N14). The source-compatibility promise the rule
   protects is one Spark no longer makes.
2. **The overloads it flags differ in a required parameter type.**
   `Contains(in Point3d, in Tolerance = default)` and
   `Contains(in BoundingBox, in Tolerance = default)` are distinguished by their first
   argument, which is never omitted, so every call site resolves unambiguously.
3. **Genuine ambiguity is still caught**, by the compiler, as CS0121. Turning RS0026 off
   removes a speculative warning, not a safety net.

The alternative was to rename one of each such pair — `ContainsPoint`, `ContainsBox` — making
the API worse to read in order to satisfy a rule aimed at a constraint the project does not
have. Related: `E1-T33`.

---

## N18 — Three green gates are not a review, and a passing test is not evidence a test can fail

The geometry kernel's first slice **passed `build -warnaserror`, `test` and `format`, and was
rejected.** An independent review found three of its eight claims false. This note exists so
that the specific failure mode is remembered, not just the general moral.

**What the gates could not see:**

- `default(Plane).Contains(anyPoint)` returned **`true`**. Every point in space silently lay
  on the null plane — no throw, no diagnostic, and a class-level doc comment that said the
  opposite.
- `Tolerance` documented a three-way partition and invited callers to depend on it, while
  `2.0` against `2.000001` fell into **none** of the three buckets. The cause was two
  roundings: `AreEqual` compared `a - b` against a threshold, the ordering predicates compared
  `a` against `b` plus or minus that threshold, and the two subtractions disagreed by an ulp
  exactly on the boundary.
- `Interval.IsValid` required `Min <= Max`, so the guard everybody writes without thinking —
  `if (!domain.IsValid) throw` — would have rejected every reversed curve domain at M3.

**Why the tests did not catch it, which is the part worth keeping.** Both tests guarding the
tolerance partition were **structurally incapable of failing**. The property drew two
independent uniform values and asserted a relationship that only breaks when they land within
a tolerance of one another; simulating that generator gave **zero violations in five million
draws**, against the hundred draws CsCheck performs per run. A generator that deliberately
straddles the threshold finds **908 violations in 12,006 pairs**. The test was not weak — it
was decorative, and it was indistinguishable from a real one in every report anybody would
look at.

Three practices follow, and they are requirements rather than suggestions:

1. **Every fix is regression-proven by reverting it and naming the test that goes red.** Not
   "a test exists nearby"; the specific test, identified by having watched it fail. This is
   the standard the repaired slice was held to, for every fix in it.
2. **Judge a property by its generator, not by its assertion.** Ask what fraction of generated
   cases can reach the condition being tested. If the answer is "essentially none", the
   property is decorative. Generators here span **1e-9 to 1e9, log-uniform**, per ADR-0018,
   and widening them from the original narrow range turned two further properties red — both
   naive assertions rather than kernel bugs, which is exactly what a widened generator is for.
3. **A degenerate input gets an explicit answer or a loud failure, never a quiet default.**
   `default(Plane)` now throws from every geometric member; `Vector3d.Normalised()` throws on
   a zero vector rather than returning zero; `Interval.Includes` and `BoundingBox.Intersects`
   reject `NaN`. The seed library returned quiet defaults for all of these and let meaningless
   values propagate far from their cause.

Also repaired in the same pass, and listed because each is a distinct class of defect: a sign
flip in `Vector3d.SignedAngleTo` where the cross product underflows near 1e-170; an
`ArgumentException` in `Plane.ByThreePoints` whose `ParamName` named a parameter absent from
its own signature, forwarded up from the factory it delegated to; and four round-trip doc
comments claiming an exactness that floating-point conversion does not provide.

The general moral — *gates are necessary and not sufficient* — was already believed here. What
this note adds is that the project now has its own evidence for it, at a cost of one rejected
slice, and that the cheapest available check on a test suite is to ask of each test **how it
would fail**.

---

## N19 — A test that normalises a quantity cannot see an error in that quantity's scale

`PolyCurve` maps one unit of its own parameter onto the whole of a segment's domain, so the
chain rule requires its derivative to be the segment's derivative multiplied by that domain's
length. A test called `APolyCurveTangentIsUnitLengthDespiteTheChainRule` was written to guard
exactly this. **Deleting the factor left all 312 tests passing.**

The reason is obvious once seen and invisible before: every public route to a curve's
derivative — `TangentAt`, `NormalAt`, `PlaneAt`, `CoordinateSystemAt` — normalises, and
normalising divides out precisely the factor under test. The test's name described the defect
it could not detect. `Length` could not catch it either, because `PolyCurve` overrides
`ComputeLength` to sum its segments and never integrates its own speed.

Two things follow, and the second is the reusable one:

1. The replacement test reaches through an internal seam — `Curve.DerivativeWithin` — and
   asserts the derivative's **magnitude** against the rate of change of arc length measured
   through the public `LengthAt`. It also pins two closed-form values, 4 and π/2, which is
   what makes the failure message say what is wrong rather than merely that something is.
   The seam exists because C# does not let a derived type reach a `protected` member through a
   base-class reference, so a polycurve cannot call its segments' `EvaluateDerivative`.
2. **Ask what a test divides out.** A normalised vector, a ratio, a unit direction and a
   percentage all discard magnitude, and an assertion made after that discarding cannot see a
   magnitude error. This is the same class of defect as [N18](#n18--three-green-gates-are-not-a-review-and-a-passing-test-is-not-evidence-a-test-can-fail)'s
   decorative property, arrived at from a different direction: there the generator never
   reached the condition, here the assertion threw the evidence away before looking.

The mutation sweep that found it was five deliberate mutations run against the curve layer.
Four were killed by named tests. This one survived, and so did a sixth — see [N20](#n20--a-branch-that-cannot-be-reached-is-a-claim-that-was-never-true).

---

## N20 — A branch that cannot be reached is a claim that was never true

`Arc.ByThreePoints` originally tested whether the second point was reached before the third
when sweeping anticlockwise, and swept the other way if not. Mutating that test to a constant
`true` **killed no test**, and the reason turned out to be that the branch was unreachable.

The circumcircle's normal is built from `(second − first) × (third − first)`, which is the
right-handed normal of the triangle *in the order the caller gave its corners*. In that frame,
sweeping anticlockwise from the first point always reaches the second before the third. The
middle point therefore steers the method through the plane's orientation, and the branch was
re-deciding something the frame had already decided.

The branch is gone. What replaced it is a property test that samples three points in order
around a circle at nine decades of scale and asserts the arc passes through all three —
because the invariant that actually needed pinning was the **orientation**, and a sign error
in that cross product still produces an arc through the first and third points by a
completely different path. Only the middle point can see it. Flipping the cross product's
operands now fails three named tests; before, it failed none.

The general form: when a mutation survives, the first question is not *which test should have
caught this* but *can this code path be reached at all*. Dead code that looks like a decision
is worse than no decision, because it reads as one.

---

## N21 — A curve's closed-ness must not wrap the end of its own domain

`Curve.CheckParameter` wraps out-of-range parameters on a closed curve, which is right: on a
circle, a parameter of 2.5π means the same place as π/2. The first version wrapped
unconditionally, and that broke the **end** of the domain: `PointAt(2π)` and `PointAt(0)` are
indeed the same point, but `LengthAt(2π)` is the full circumference and `LengthAt(0)` is zero.

Wrapping the domain's own maximum turned the last step of every division on a closed curve
into a negative length, which is how it was found — `DivideEqually` on an ellipse reported a
final segment of −12.5 against an expected 0.835. A parameter is only wrapped when it is
genuinely outside `[Domain.Min, Domain.Max]`; the ends are left alone.

The lesson generalises past curves: **two parameters that evaluate to the same position are
not therefore interchangeable**, because position is not the only question the parameter is
asked. Anything that accumulates along a curve — length, a running index, a sweep — can tell
the seam's two sides apart even when the geometry cannot.

---

## N22 — A window must adopt exactly one graph at startup, and "synchronously" does not help

Adopting a graph starts an evaluation. The shell's view model adopts one in its constructor, so
**anything that adopts a second one afterwards leaves two runs in flight against a single
session**, and the one that finishes last wins. What that looks like is not a crash: it is a
window showing the right graph on the canvas, the *previous* graph's diagnostics, and an empty
viewport, with a status line reading `Ran 7` for a graph of eighteen nodes.

This has now happened twice, and the second time is the reason the note exists.

- **`--graph curves`** called `LoadCurves()` from the window's `Opened` handler. Fixed by making
  the startup graph a constructor parameter, so only one graph is ever adopted.
- **`--open PATH`** then did the same thing again, from the same handler, with a comment
  explaining that doing it *synchronously* made it safe. It did not. Synchronous or not, it is
  still a second adoption, and `AdoptGraph` fires its evaluation with `_ = EvaluateGraphAsync()`
  either way. The comment was confidently wrong, which is worse than no comment.

The rule is therefore about the count, not the timing: **the constructor decides what is open,
and nothing else adopts a graph before the first evaluation completes.** The file path is a
constructor parameter for the same reason the seeded graph's name is.

Two supporting facts worth keeping. The session cancels a run in flight when a graph is replaced,
which is why this *looks* fine most of the time and fails under the exact timing a screenshot
happens to catch. And **no test in the suite could see either failure**: every test drives the
view model directly, which is the correct thing for a test to do and is precisely why it cannot
observe a defect that lives in the window's startup sequence. Both were found by running the
application and looking at the picture.


---

## N23 — An undo reopens the document, so canvas slots and node objects do not survive it

Undo restores a `.spark` snapshot through `CanvasDocument.Open`
([ADR-0022](adr/0022-undo-by-document-snapshot.md)), which is the same path a file takes. That is
the point of it — there is one definition of what a document is, and undo cannot drift from it —
but it has a consequence that will bite anybody holding a reference across the step.

**Node identities survive. Everything else about a canvas node does not.**

- `CanvasNode` instances are **new objects**. A variable holding one from before the undo now
  refers to a node that is not in the graph, and writing to its `X` changes nothing anybody can
  see. Find the node again by `NodeId`, through `CanvasGraph.SlotOf`.
- **Slots renumber.** `CanvasDocument.Open` adopts nodes in the document's order, which is sorted
  by identity so that the file has a stable diff. The canvas draw order after an undo is
  therefore the canonical order, not the order the nodes were created in. For non-overlapping
  nodes this is invisible; for overlapping ones the z-order can change.
- **Selection is dropped**, because `GraphCanvas.Graph`'s setter clears it, and a slot held from
  before the undo would otherwise point at whichever node now occupies that index.

The first version of the test for "undo puts a node back where it was" asserted on
`Nodes[0]` before and after, and failed with `Expected: 30, Actual: 270` — not because the
position was wrong but because slot 0 was a different node. It now looks the node up by
identity, which is what any code crossing an undo has to do.

The cache does not care about any of this. A cache key is built from a node's definition, its
lacing, the document tolerance and the keys of everything upstream — never from its identity and
never from its slot — so a reopened document re-derives the same keys and the run after an undo
hits every one of them.

---

## N24 — A node is sized before any of its text has been measured

`CanvasNode` computes its width in its constructor, from character counts and a per-character
estimate: 6.8 px for the 12 px header title, 6.2 px for an 11 px port name, 5.6 px for a 10 px type
label. It is not laziness and it is not a placeholder for a measurement to be added later. A node
is built off the render thread, from a `NodeDefinition`, with no `DrawingContext`, no typeface and
no font manager — Avalonia's `FormattedText` needs all three, and the node has to have a size
before the first frame so the spatial index can be built and the graph can be hit-tested.

**The consequence is that width is a preference, not a guarantee**, and the drawing must survive
the estimate being wrong. It does: each type label is drawn only if the row still has room for it
with a gap to spare, and skipped otherwise. An estimate that runs narrow therefore loses a type
label on one row of one node, rather than printing an input's type on top of its output's name.

**No test in the suite can check the estimate.** The headless Avalonia platform draws through a
stub, so `FormattedText.Width` there is the stub's metric and not Inter's — a test measuring it
looks rigorous and is checking nothing about the running application. One was written and deleted
for exactly that reason: it reported an eleven-character type label as 104 px wider than the node
that comfortably fits it on screen. What checks this is
`dotnet run --project src/Spark.Desktop -- --graph curves --screenshot PREFIX --zoom 1.15`, and
looking at the picture.

`--zoom` recentres the canvas on the graph as part of the same fix, because setting a zoom scales
about the world origin and every zoom above the fit had been taking the nodes off screen — a
screenshot switch that reliably photographs empty canvas is worse than none.

---

## N25 — The view model applies one run's results at a time, and may not rely on a dispatcher

`MainWindowViewModel.EvaluateAsync` runs the graph off the calling thread and then applies the
result: node states onto the canvas, geometry into the scene, literals into the inspector. The
run is superseded-if-superseded; **the apply is serialised behind a semaphore**, and the two are
deliberately not the same span.

- The gate is taken **after** `SparkSession.EvaluateAsync` returns, never before. Taking it first
  would make a new edit queue behind a long evaluation instead of cancelling it, which is the one
  property `SparkSession` exists to provide.
- The gate exists at all because **the view model must not assume a UI dispatcher**. In the
  application every continuation lands back on the UI thread, so applies are serialised for free.
  In a headless host — a test, `spark run`, an embedder that supplies its own scheduler — they are
  not, and `Inspector` and the published-key set are ordinary collections that do not survive two
  writers.

The symptom, before the gate, was an intermittent failure in an undo test with no assertion
message: two fire-and-forget runs, started by an undo and the redo after it, applying at once.
`ViewportScene` is genuinely thread-safe and was never the problem, which is what made it look
like a flake rather than a defect. Undo is what made it reachable — it is the first feature that
replaces the whole document twice in a row at a user's typing speed.

---

## N26 — Two benchmarks were wrong before they were right, and the numbers said so both times

`bench/Spark.Benchmarks` was written, run, and found to be measuring the wrong thing twice. Both
mistakes are the benchmark equivalent of a test that cannot fail, and both were caught by reading
the numbers rather than by reviewing the code — which is the reason to run a benchmark before
ticking the row that says it exists.

**A benchmark that could not regress.** `ValueMarshal.FromClr(array, declaredRank: 0)` returns its
argument untouched: rank 0 means the port does not declare a list, so there is nothing to convert.
It measured 0.6 ns at 10 elements, at 1 000 and at 100 000 — flat across four orders of magnitude,
allocating nothing. **The tell was the flatness**, which is exactly what the three sizes are there
for. A port returning `IReadOnlyList<double>` declares rank **1**, and at rank 1 the same call
costs 8.1 ms and 5.3 MB at 100 000 elements.

**A benchmark that measured something else entirely.** The first evaluation benchmark built a
`SparkSession` inside the timed region, so every iteration reflected over `Spark.Nodes.Core` to
import fifty-seven nodes. It reported **fifty nodes as slower than five hundred** — 43.9 ms against
30.0 ms — because the importer's fixed cost swamped the evaluation and the noise did the rest. The
library is now imported in `[GlobalSetup]`, and coldness comes from a fresh `EvaluationContext`,
which brings a fresh cache with it, rather than from a fresh session.

The same benchmark also revealed that **`DemoGraphs.Synthetic` cannot be evaluated meaningfully**.
It wires whatever ports will accept each other and is deliberately never run — `LoadSynthetic`
says so — so a good fraction of its nodes error on their default literals. Benchmarking it
measured `throw`: BenchmarkDotNet reported dozens of exceptions per iteration. `BenchmarkGraphs`
builds a chain of replicating nodes instead, and `[GlobalSetup]` now **fails the run** if the graph
produces any diagnostic or leaves a node unevaluated. A benchmark that guards itself is worth the
six lines; a benchmark quietly measuring exception handling is worse than none, because it will be
quoted.

What the corrected pair says is worth keeping: a 500-node chain over 100 elements costs **8.5 ms
cold and 0.32 ms warm**, a 27-fold difference, which is the provenance cache's central claim
([ADR-0010](adr/0010-explicit-scale-aware-tolerance.md), `E3-T8`) as a number rather than a
sentence.

---

## N27 — `DoubleTapped` arrives after the release that completes it, so nothing needs standing down

The canvas's double-click handler began with a defensive reset — cancel the marquee the first
click started, drop the pointer capture, repaint — and a test asserting that a double-click leaves
no rubber band behind. **All of it was unreachable, and the test could not fail.**

Avalonia raises `DoubleTapped` from the gesture recogniser on the *second* pointer release, and
`GraphCanvas.OnPointerReleased` already clears `_mode` and the capture unconditionally at the end
of every release. So by the time the handler runs, there is nothing to stand down; and the test
passed whether or not the reset was there, because the release handler had done the work either
way.

It was caught by a mutation, in the shape this repository has now met four times: deleting the
reset changed no test. The repair was to delete the reset and the test rather than to strengthen
the test, because there is no input sequence that reaches the state the code was defending
against — the same conclusion as [N20](NOTES.md), reached the same way.

What survives is a comment saying why the handler is only three lines, so the next person does not
add the guard back.

---

## N28 — A script committed from Windows is not executable on Linux, and CI is where you find out

`scripts/check-no-native-binaries.sh` was added, run locally, proven to detect what it guards
against, wired into both CI legs — and failed on the first push with exit 126,
`Permission denied`, on Linux only.

The cause is one line of local configuration: this repository is maintained from Windows, where
`core.filemode` is `false`. A `chmod +x` there changes the working tree and **never reaches the
index**, so the file was committed as mode `100644`. Git Bash on the Windows runner ignores the
bit and ran the script happily; Linux would not. The Windows leg was green and the Linux leg was
red, which is precisely the class of difference [ADR-0001](adr/0001-avalonia-not-wpf.md) keeps the
Linux job for — and the first time it has actually earned its place.

The fix is two things on purpose. `git update-index --chmod=+x` sets the bit in the index, which
is the correct state for a script in `scripts/`; and CI invokes it as `bash scripts/...` rather
than executing it, so the *next* script added from Windows cannot fail this way at all.

**The wider lesson is about what "the gate was proven" means.** It had been proven to *detect* —
pointed at `Spark.Desktop` it fails on Avalonia's Skia and HarfBuzz natives. It had not been
proven to *run*, and those are different claims. A gate's first execution in CI is part of adding
it, not a formality afterwards.

---

## N29 — Only one of a benchmark's three numbers means the same thing on another machine

`bench/budgets.jsonc` gives three kinds of budget three deliberately different strengths, and the
reason is not caution — it is that the numbers are not equally portable.

**Allocated bytes per operation is deterministic.** For a given build it is a property of the
code, identical on a developer laptop and on a shared GitHub runner. It is therefore budgeted
tightly, and it is the only figure here that can be. It is also what most real regressions show
up in first: `FromClr` boxing every element through a `List<object?>` costs 5.3 MB at 100 000
elements, and it would cost 5.3 MB anywhere.

**Wall-clock on a hosted runner is a property of the code and of a virtual machine** of unknown
vintage, contention and thermal state. Those ceilings are set an order of magnitude above the
measurement, and what they are allowed to catch is *an algorithm changed* — never *this got 20%
slower*. A tighter one produces a nightly that fails at random, which is a guard everybody learns
to ignore inside a fortnight.

**A ratio between two cases in the same run is machine-independent, and is the sharpest thing in
the file.** Both halves are measured on the same runner seconds apart, so the machine cancels.
*Warm evaluation costs a fraction of cold* is 27-fold on a developer machine ([N26](#n26--two-benchmarks-were-wrong-before-they-were-right-and-the-numbers-said-so-both-times)) and
will be about 27-fold anywhere — which means the provenance cache's central claim, the one that
makes undo instant, is expressible as exactly the kind of number a hosted runner can be trusted
for. **Where a claim can be written as a ratio, write it as a ratio.** The same trick guards
linearity: the 100 000-element marshalling case divided by the 1 000-element one is a scaling
factor, and an accidental O(n²) moves it by two orders of magnitude on any hardware.

**The budget key is BenchmarkDotNet's `FullName`, and it contains the parameter values.**
`Spark.Benchmarks.MarshallingBenchmarks.NumbersToClr(Count: 10)` is built from the *method* name
and the `[Params]` values, not from the `[Benchmark(Description = …)]` text. So rewording a
description costs nothing, and **changing a `[Params]` value or renaming a method renames every
case it appears in** — at which point the check reports the old key as budgeted-but-not-measured
and the new one as measured-but-not-budgeted. That is the intended behaviour rather than an
inconvenience: the two-way diff is there so that a benchmark cannot quietly stop being covered,
and a re-parameterised suite is precisely the case where somebody has to look at the numbers
again anyway.

The matching decision, and the alternatives it beat, are [ADR-0023](adr/0023-performance-budgets-not-a-benchmark-time-series.md).

---

## N30 — A test that disappears is invisible to all three gates

While adding `BoundingBox.Intersection` a scripted edit truncated
`tests/Spark.Geometry.Tests/InvalidValueTests.cs` to **zero bytes**. The cause is a Python
footgun and not interesting — `open(p, 'w').write(open(p).read()...)` truncates the file before
the read runs — but what happened next is.

**The build stayed clean. `dotnet format` stayed clean. The suite went green.** A hundred and
fourteen lines of tests, including every assertion about `default(Plane)` refusing coordinate
conversions, had ceased to exist, and nothing in the repository objected. Deleting a test is not
a compile error, not a style violation and not a failure; it is a smaller number.

**The only signal was the count**, and it was caught by arithmetic rather than by a gate: fourteen
tests had been added, `Spark.Geometry.Tests` should have read 327, and it read 319. Had the step
added six tests instead of fourteen, the numbers would have agreed and the loss would have been
committed.

Two things follow, and the second matters more than the first.

**The count is load-bearing, so quote it.** Every log entry, commit message and status paragraph
in this repository that names a test total is doing real work — it is the only place a
disappearance can show up. Writing *the suite is green* instead of *952 tests pass* removes the
only detector.

**A gate would be cheap.** Nothing asserts that a test project reports a non-zero count, and that
one line would have caught this, would catch a truncated file, and would also catch the
`dotnet test` discovery failure recorded in [AGENTS.md](../AGENTS.md#before-you-commit) — which is
the same defect wearing different clothes: a run that discovers nothing looks exactly like a run
where nothing is wrong. It is queued.

The wider shape is [N18](#n18--three-green-gates-are-not-a-review-and-a-passing-test-is-not-evidence-a-test-can-fail)'s
again, from the other end. N18 is about a test that cannot fail. This is about a test that is not
there at all, and the gates cannot tell the two apart from a test that passes.

---

## N31 — The slab test's correctness lives entirely in what it does with NaN

`Ray.Intersects(BoundingBox)` is the standard slab test, and it looks like six divisions and four
comparisons. The part that is easy to get wrong is not visible in that description.

**Dividing by a zero direction component is correct and must not be guarded against.** A ray
parallel to an axis produces `±∞` for that slab's two parameters, the comparisons that follow do
exactly the right thing with infinities, and the branchless form is both faster and simpler than
the *is this component zero* special case people reach for.

**But `0 × ∞` is `NaN`, and that case is reachable.** It happens when the direction is parallel to
a slab **and** the origin lies exactly on one of its planes: `(min - origin)` is exactly zero, the
reciprocal is infinite, and the product is `NaN`. Every comparison against `NaN` is false, so a
naive `near = Math.Max(near, first)` propagates it and the test returns a **miss** for a ray that
plainly grazes the box.

That is not an exotic input. It is what a click along an edge does, what an axis-aligned ray
through a grid of axis-aligned cells does at every cell boundary, and what a picking ray in a
plan view does constantly — so the failure would show up as *sometimes the thing directly under
the cursor is not selected*, which is a bug nobody reports precisely.

The fix is one line per bound: ignore a `NaN` rather than letting it narrow the interval, because
a `NaN` here means *this axis places no constraint*, which is exactly what a parallel ray on the
plane should contribute. `RayTests.ARayLyingExactlyOnAFaceStillHits` pins it, and removing either
guard turns it red — checked, not assumed.

---

## N32 — `BoundingBox.Empty` cannot survive its own public constructor

`new BoundingBox(corner, oppositeCorner)` sorts the two corners per axis. That is right, and it
is why a caller with two opposite points of a region does not have to work out which one is the
minimum. It also means the constructor **cannot reproduce every value of the type it constructs**.

`BoundingBox.Empty` is the *inverted* infinite box — `Min` at `+∞`, `Max` at `−∞` — and it is a
real value with a real job: it contains nothing, intersects nothing, and is the identity for
`Union`, which makes it the correct seed for accumulating a box over a sequence. Feed its two
corners back through the public constructor and the sort reverses them, producing the **infinite
box**: the value that contains everything. The exact opposite, silently, with no error anywhere.

The serialization round-trip test found this on its first run, which is the argument for writing
that kind of test at all. It is not a defect anybody would find by reading `BoundingBox`, and it
would have shown up much later as *a graph that opens with everything selected*, or as an
accumulated bound that swallowed the model.

The fix is an `internal static BoundingBox FromSortedCorners`, used by the deserializer and
nothing else. It is deliberately not public: a caller who wants an inverted box wants
`Empty`, and a caller with two corners wants the sorting. **The general shape is worth carrying
to every value type added after this one** — *can this type's public constructors reproduce every
value the type can hold?* — because whenever the answer is no, something that reconstructs values
needs a door the ordinary caller does not.

---

## N33 — Roslyn completion fails silently twice before it works

M1.5 spike (c) asked whether Roslyn can supply a completion list for a code block. It can, and it
answered *nothing at all* twice on the way there. **Neither failure raised an error**, which is
what makes them worth a note: an empty completion list looks exactly like a caret with nothing to
suggest.

**One — the host services must include the Features layer.**
`MefHostServices.Create(MefHostServices.DefaultAssemblies)` composes the *workspace* layer only,
and `CompletionService` lives in Features. With it missing, `CompletionService.GetService(document)`
returns **null**, and the obvious `if (service is null) return []` turns a composition mistake into
a permanent empty popup. The composition has to name
`Microsoft.CodeAnalysis.Features`, `Microsoft.CodeAnalysis.CSharp.Features` and
`Microsoft.CodeAnalysis.CSharp.Workspaces` explicitly. The code now throws instead of returning
empty, because a missing service is a wiring bug and should read like one.

**Two — the *document* carries its own `SourceCodeKind`, and it defaults to `Regular`.** Setting
the project's parse options to `SourceCodeKind.Script` is not enough:
`DocumentInfo.Create(..., sourceCodeKind: SourceCodeKind.Script)` is the one that counts. Parsed as
`Regular`, a snippet like `var p = new Point3d(1, 2, 3);` is a file of syntax errors, the semantic
model has nothing to say about `p`, and completion returns an empty list — again with no error.

With both right, `p.` completes to `X`, `DistanceTo`, `EqualsWithin` and the rest, against a type
that came from an expression rather than from anything the user declared. That is the case the M4
code block actually needs — *IntelliSense that knows the type on the incoming wire* — and it works.

**The general shape, since this is the second note this week about it:** an API that answers
*nothing* where it means *I am not configured* costs more to debug than one that throws.
[N30](#n30--a-test-that-disappears-is-invisible-to-all-three-gates) is the same shape from the test
side. When wrapping one, convert the silence into a failure at the boundary.


---

## N34 — Dock's `Tool.Content` is `[TemplateContent]`, so pane markup inside it loses the window's names

`Dock.Model.Avalonia.Controls.Tool` carries its content as `[Content]`, `[TemplateContent]` and
`[ResolveByName]`. The middle one is the load-bearing part: markup written **inline** inside a
`<Tool>` is not built as part of the surrounding file, it is compiled into a *template* and built
later into its own namescope.

The consequence is easy to miss and expensive to find, because it is not a compile error. A window
that declares its panes inline inside `Tool`s still builds; its generated `x:Name` fields —
`Canvas`, `Viewport`, `LibraryList`, `CreateBox` — are simply **never assigned**, and the first
line of code-behind that touches one throws a `NullReferenceException` at runtime. Roughly seven
hundred lines of `MainWindow.axaml.cs` reach through exactly those fields.

**So the panes had to become `UserControl`s before the shell could become a `DockControl`**, and
that ordering is the whole reason `E8-T2` landed as two commits rather than one. A `UserControl`
brings its own namescope with it, so its `x:Name`s resolve against itself and survive being built
inside a template; the window then holds the pane, and reaches the canvas through it.

**How this was established, and the general shape:** by reflecting over the property's attributes
before writing any code, rather than by writing the layout and debugging the nulls —

```
p.GetCustomAttributes(true)  // → ContentAttribute, TemplateContentAttribute, ResolveByNameAttribute
```

**When a container takes arbitrary content, check whether it takes it as a value or as a
template.** The two are indistinguishable in the XAML that fills them in and completely different
in where the names inside end up.

---

## N35 — Dock puts the *dockable* on the pane's `DataContext`, and compiled bindings say nothing about it

A `Tool`'s content is presented inside Dock's own controls, and those set their `DataContext` to
the **dockable** — the `Tool` — not to whatever the `Tool.Context` is. A pane that relied on
`DataContext` inheritance from the window (every pane did, before the shell was a `DockControl`)
therefore resolves its bindings against a `Tool`.

**Nothing reports this.** `x:CompileBindings="True"` with `x:DataType="vm:MainWindowViewModel"`
compiles a binding that expects a `MainWindowViewModel` and simply produces nothing when handed
something else. The visible result is a pane that draws its *static* markup — its heading, its
buttons, its search box — with every *bound* row missing: a library list with 57 entries in the
view model and no rows on screen, under a heading that says `LIBRARY`. It reads as a layout
problem, and the layout is fine.

The fix is one line and the diagnosis is the expensive part, so: **when content moves into a
container that owns its own `DataContext`, set the context on the control explicitly.**
`SparkDockFactory.SetContext` sets both `Tool.Context` and the pane control's `DataContext`, and
`SparkDockFactoryTests.SettingTheContextReachesEachPaneControlAndNotOnlyItsTool` goes red if
either half is dropped.

This is [N33](#n33--roslyn-completion-fails-silently-twice-before-it-works)'s shape again from a
third direction: an API that answers *nothing* where it means *that is not the type I was told to
expect*.

---

## N36 — `HideDockable` leaves `Owner` set, so `Owner is not null` is not "is it showing?"

Dock's `HideDockable` moves a dockable out of its owner's `VisibleDockables` and records it on the
root — but it **keeps `Owner`**, and it has to, because that is where `RestoreDockable` puts the
dockable back.

So `tool.Owner is not null` answers *has this ever been in the tree*, not *is it in the tree now*.
Written as a visibility predicate it is wrong in exactly one direction: every pane always reports
as showing. That makes hiding look correct — the pane does disappear, because the hide branch
still runs — while **restoring silently never runs at all**, since the restore branch is guarded by
`!showing`. *Presenting* worked; *Reset layout* afterwards did nothing, and the two side panes were
gone until the application was restarted.

Ask the containment question instead:

```csharp
dock.VisibleDockables?.Contains(tool) == true
```

The general shape: **a predicate that is wrong only in the direction that looks like success will
survive every screenshot you take of it.** This one was found by a unit test asserting the
round trip — hide, restore, and check — which is the assertion a screenshot cannot make.

---

## N37 — A headless window left open renders after the fonts are gone

A test that shows an Avalonia window through `HeadlessUnitTestSession` and **does not close it**
can fail with an `ObjectDisposedException` raised inside `DrawText`. The stack names
`FontManager`, `TextFormatterImpl` and the control's own `Render`, and nothing in the test file.

The sequence is: the test body invalidates the visual (any edit does), the dispatch ends with that
render job still queued, and the session's teardown drains the queue from
`Dispatcher.ResetForUnitTests` **after** disposing the application — fonts included. The frame is
then drawn against a disposed font manager.

It is worth writing down because of how it presents. The failure is **not deterministic per test**:
it lands on whichever tests happen to still be draining when teardown runs, so adding an unrelated
test class can turn five green tests red and reverting one line can turn them green again. That
reads as flakiness in the new code, and it is not.

```csharp
window.Show();
try { body(window, canvas); } finally { window.Close(); }
```

**Close every headless window you open**, in a `finally`. The cost is one line; the alternative is
a class of failure that appears to come from somewhere else entirely.

---

## N38 — A format version is the minimum version that can *read* the file, not a stamp of the writer

When `.spark` grew notes and then groups, the obvious move was to bump `CurrentFormatVersion` to 2
and write 2 from then on. That is wrong, and what makes it wrong is a constraint two documents
away from the format:
[ADR-0016](adr/0016-no-dynamo-interoperability.md) requires a graph referencing a missing package
to re-save **byte-identically**. Stamping every save with the writer's version rewrites the first
line of every version-1 graph in existence the first time somebody opens one.

So `GraphDocument.MinimumReaderVersion` derives the version from the **content**:

- no notes and no groups → `1`, and the file is byte-for-byte what earlier builds wrote;
- either of them → `2`, which a version-1 build refuses loudly.

Refusing is the point. A version-1 reader does not know the `notes` key exists; it would open the
file, show the graph correctly, and throw every note away on the next save. **A reader that
silently drops what it does not understand is worse than one that will not open the file at all.**

Two consequences worth keeping:

- **New arrays are omitted when empty, never written as `[]`.** `"notes": []` would add two lines
  to the diff of every graph that has never had a note in it, and ADR-0017 bought text precisely
  for the diffs.
- **Fields landing in the same release share a version.** Groups arrived days after notes and both
  are version 2. Inventing a version 3 for the second one would refuse a file to a reader that can
  in fact read it.

---

## N39 — A guard that returns silently is a bug waiting for a layout change

`GraphCanvas.ZoomToFit` began:

```csharp
if (Bounds.Width < 1 || Bounds.Height < 1) { return; }
```

which is correct — you cannot fit a graph into a control with no size — and was fine for months.
Then the shell became a `DockControl`, Dock laid its content out later than the `Grid` had, and the
startup fit began arriving **before the canvas's first arrange**. The guard did its job, the
request evaporated, and the application opened at 100% showing a third of the graph.

**Nothing failed.** No exception, no warning, no red test, and the gate that eventually noticed was
a human reading `zoom 100%, 7/18 nodes drawn` in the corner of a screenshot that was expected to
look different for an unrelated reason — three commits later. The screenshot said so the whole
time.

The repair is to make the impossible request **pending** rather than discarded: record it, and
perform it on the first arrange that produces a real size. And to put it on the *canvas* rather
than re-timing the call from the window — asking the shell to call `ZoomToFit` later would put the
container's layout schedule into the window's head, and the next container change would break it
again exactly as silently.

This is [N26](#n26--two-benchmarks-were-wrong-before-they-were-right-and-the-numbers-said-so-both-times)
and [N33](#n33--roslyn-completion-fails-silently-twice-before-it-works) and
[N35](#n35--dock-puts-the-dockable-on-the-panes-datacontext-and-compiled-bindings-say-nothing-about-it)
a fourth time, and the pattern is stable enough to state as a rule: **when a precondition cannot be
met yet, decide between *refuse loudly* and *defer*. Returning quietly is neither, and it is the
one that survives every test you have.**

---

## N40 — `Math.Sign` of a near-zero value is a third answer, not the other sign

`ValueLayerProperties.TheSignedAngleBetweenTwoVectorsDoesNotDependOnTheirLengths` failed roughly
once in forty runs, in a project nothing had touched. The counterexample, once captured:

```
Axis = (-0.1495…, 0.9190…, -0.3647…)   Turn = -3.844e-15°   scales 0.01 and 4.05e-5
```

The turn is **vanishingly small**, so the two vectors are the same direction to within about
`1e-17` radians. Scaling them sends the cross product to *exactly zero*, and the angle comes back
as `0.0`. The assertion was:

```csharp
Assert.Equal(Math.Sign(atUnitLength.Radians), Math.Sign(atOtherLengths.Radians));
```

`Math.Sign(+1e-17)` is 1 and `Math.Sign(0.0)` is **0** — and 0 is not the opposite sign, it is
*no sign*. The assertion read a value too small to have a direction as a disagreement about
direction. The property being tested — that the angle does not depend on the lengths — held
throughout, which is why the failure was rare and looked like nothing.

**Two lessons, and the second is the expensive one.**

**A sign is only a fact when the magnitude is above the tolerance.** Guard the comparison, or use a
three-way test that treats zero as its own case. This is the same shape as
[N26](#n26--two-benchmarks-were-wrong-before-they-were-right-and-the-numbers-said-so-both-times)'s
three-way partition, from the assertion side.

**Do not guess at a randomised failure.** Two plausible hypotheses here — both about angles near a
multiple of 360° — survived *four hundred thousand* trials of a hand-rolled search and were wrong,
because CsCheck deliberately generates values like `1e-15` that a uniform draw essentially never
produces. Running the suite forty times and reading the printed counterexample took two minutes and
gave the answer outright. **The generator's seed is the evidence; a reproduction that stops failing
is not the same as a cause.**

---

## N41 — A placeholder for *something that does not exist* must be something that cannot come to exist

Twice now a test has broken because the value it used to mean *this does not exist* came to exist.

- `GeometryJsonTests.AnUnknownTypeIsRefused` deserialised a type named `"NurbsCurve"` to prove an
  unknown type is refused. It broke the day `NurbsCurve` was added.
- `GraphNoteTests.AVersionNewerThanThisBuildIsStillRefused` read a file at `formatVersion: 3` to
  prove a future version is refused. It broke the day the code block made version 3 current.

Both failures are maximally confusing: the test names something real, the assertion is about
something else entirely, and the failure arrives in a commit that had no business touching it. Both
cost a few minutes of *is this a real regression?* at exactly the moment attention was elsewhere.

**Do not reach for the next plausible name or number.** A planned type, the next version integer,
the next error code — these are all things somebody will implement, and the test is a landmine with
their name on it. Use something that cannot be overtaken: `999999` for a version,
`"NotATypeThisBuildKnows"` for a type name. The absurdity is the point, and it wants a comment
saying so, or a tidy-minded reader will make it plausible again.

## N42 — Reflective invocation wraps the one exception the engine reads for meaning

The replicator's two broad catch filters both end with `exception is not
OperationCanceledException`, so cancellation propagates out of a node instead of being recorded as
a failure. That works only while cancellation arrives **bare**.

A code block's entry point used to be reached through `MethodInfo.Invoke`, which wraps whatever the
script threw in a `TargetInvocationException`. A `TargetInvocationException` does not match those
filters. So the sequence was: the user presses stop, the token is cancelled, the script's
`ThrowIfCancellationRequested` fires, the wrapper hides it, and the replicator reports
`'CodeBlock' failed: Exception has been thrown by the target of an invocation` — **and then carries
on to the next node**. A stop button that logs an error and does not stop.

Binding the entry point with `CreateDelegate` instead removes the wrapper entirely, and it is a
faster call besides — but speed is the lesser reason and would have been the wrong one to write
down.

**The general shape, and it is not confined to cancellation:** any time control flow is expressed
by an exception *type* and the call is made reflectively, the wrapper silently changes the meaning.
Nothing fails; the wrong branch is simply taken. `Assert.Throws<T>` is exact rather than assignable,
which makes it the right tool to pin this down — `ScriptNodeFactoryTests
.AScriptsExceptionIsNotWrappedByReflection` fails outright if the wrapper comes back.
