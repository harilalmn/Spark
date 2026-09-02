# Spark — Implementation Notes

Non-obvious implementation facts, numbered. Adopted from DoodleSharp's convention.

**Last updated:** 2026-09-03 (N114 added)

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


## N43 — `Missing compiler required member 'Binder.BinaryOperation'` names the wrong assembly

A code block with an input port declares it `dynamic`, and the compiler then needs two assemblies
that nothing else in the process has a reason to load: `Microsoft.CSharp`, which holds the binder,
and `System.Linq.Expressions`, which holds the `CallSite` the binder dispatches through. The
reference catalogue is built from *what is already loaded*, so both can be missing.

Only the first was named explicitly. The second went unnoticed for as long as it did because the
existing scripting tests share a process with tests that pull in `System.Linq.Expressions` for
their own reasons — so the missing reference was invisible until a **new test class** compiled
`return count * 2;` before anything else had.

**The trap is the diagnostic.** It says `Missing compiler required member
'Microsoft.CSharp.RuntimeBinder.Binder.BinaryOperation'`, which names a type that *is* referenced.
Half an hour went into the wrong assembly on the strength of that sentence. The member is missing
because the assembly it forwards into is absent, and the message never mentions it.

**The general rule this belongs to:** a catalogue built by sweeping loaded assemblies is
order-dependent, and order-dependence in a test process is hidden by every other test. Anything the
generated code needs is named by `typeof(...).Assembly.Location`, not hoped for — and the way to
find out whether it really is named is a test class that touches nothing else.

## N44 — A `static` local function is exactly what a woven guard cannot live in

`E6-T4` weaves `ScriptGuard.Tick(__token)` into every loop body, and `__token` is a parameter of
the generated entry point. `static` on a local function or a lambda is a promise not to capture
anything from the enclosing scope — which is precisely what a woven check does.

So a user who wrote a perfectly ordinary `static int total() { for (…) … }` would have got
`CS8421: a static local function cannot contain a reference to '__token'`, naming an identifier
they had never seen, in code they had written correctly, because of a rewrite they did not know
happened. That is the worst class of error a code block can produce.

The weaver drops the modifier. Dropping it only widens what is legal — nothing that compiled
before stops compiling — and the only thing lost is an allocation guarantee on a lambda whose
enclosing method is now allocating a closure regardless.

**The general shape:** a rewrite that adds a reference to enclosing state is incompatible with
every language feature that exists to forbid such references. `static` is the one C# has today;
the next one will need the same treatment, and it will announce itself the same way — as a
compiler error naming a generated identifier.


## N45 — Rebuilding a node is not the same as deleting and re-adding it, and the difference is everything attached to it

`CanvasGraph.ReplaceDefinition` swaps a node's definition by removing the node and adding it back
under the same identity. Removal is thorough, correctly: it drops the node's wires and takes it out
of every group, and deletes a group it emptied.

That was written for one caller — editing a code block's source — and it restored the wires
*into* the node by port name, which was visibly the hard case. It restored nothing else. So editing
a script silently detached everything **downstream** of the block, and dropped the block out of any
group it was in. Nobody noticed, because the path ran once per deliberate edit and the user was
looking at the properties pane rather than at the wire they had drawn ten minutes earlier.

`E6-T6` made the same path run **every time a wire lands on a code block**, and a defect that
happens on a deliberate edit is a bug while a defect that happens on every connect is unusable.

**The general rule:** a remove-then-add rebuild has to restore everything the removal was correct
to destroy, and *everything* means every relationship anything else holds by identity — wires in,
wires out, group membership, and whatever the next feature attaches. The safer shape is a real
in-place swap on the engine's `NodeInstance`; it was not taken here because resizing a node's
literal array is engine surgery and this is a canvas concern. **The list is therefore a list, and
it will need adding to.**

## N46 — A Roslyn workspace that gains a document per keystroke goes quiet, not slow

`ScriptCompletion` answered each request by adding a new `Document` to its `AdhocWorkspace` and
never removing one. The comment above it said this was deliberate — *an editor sends a new snapshot
on every keystroke anyway* — and it is wrong in a way that is invisible from one call.

Two script documents in one project are two sets of **top-level statements**. From the second
request onwards the semantic model is looking at duplicate definitions, and completion returns an
empty list. Not an error, not a slow list: **nothing**.

It survived because every test in the M1.5 spike constructed its own `ScriptCompletion`, so no test
ever made a second request against the same instance. The application would have made thousands.

**It was found by accident, and by the only method that could have found it.** A repair utility was
written to make half-typed text parseable, and before trusting it, it was measured with and
without — eight snippets, hit or miss. Everything after the first snippet missed *either way*,
which is not what a repair-shaped problem looks like. Reusing one document made seven of the eight
hit with the repair **and without it**, which retired the repair and exposed the real defect in the
same measurement.

**Two rules, and the second is the one worth carrying:**

- One document, replaced through `TryApplyChanges`, is not an optimisation over one per request. It
  is the correct behaviour and the other is wrong.
- **Measure a fix before believing it.** The repair looked like it worked because the thing it was
  measured against was broken for an unrelated reason. A test written at that moment would have
  passed, for the wrong reason, and pinned a hundred lines of code nothing could falsify.

## N47 — A `Popup` costs the headless test session, and the trade is not close

The completion list wants to be an Avalonia `Popup`. A popup can extend past its parent's bounds,
which matters in an inspector pane narrow enough that a member list is clipped to about twenty
characters.

It does not work in the headless session every UI test in this project runs in:
`Unable to create IPopupImpl and no overlay layer is found for the target control`. A popup needs a
window overlay layer, and the headless platform does not supply one.

**The failure is worse than it looks, and that is the part worth recording.** Setting `IsOpen`
throws *after* the property has taken its value, and the control opened the list from a
fire-and-forget `_ = RequestCompletionAsync()` — so `IsCompletionOpen` answered `true` while an
exception was being swallowed into an abandoned task. Eight of the twelve tests **passed over a
thrown exception**. Only the two that awaited the request saw it.

The list is a `Border` on a `Canvas` inside the control instead. It is clipped to the pane, which
is a real loss, and everything about it is testable: placement, filtering, what each key does, and
that a click on the list reaches the list while a click through the empty part of the layer reaches
the editor.

**The general rule this belongs to:** when a framework feature cannot be exercised by the harness,
the cost is not the feature — it is every future assertion about it becoming a manual check on a
running application. That is the same reasoning ADR-0013 used to make the canvas one hand-drawn
control, and it comes out the same way here. **And check what a fire-and-forget task is hiding
before trusting a green run**: `_ = SomethingAsync()` in a UI handler converts an exception into a
test that passes.

## N48 — "Exact" for a NURBS conversion means the sheet, never the parameterisation

A sphere, a cylinder, a cone and a torus are all rational quadrics, so each converts to a NURBS
surface with **no approximation error at all**. That is the whole reason `ToNurbsSurface` exists:
a BRep kernel converts everything to one representation constantly, and a conversion that fitted
instead of converting would make that quietly destructive.

**It is easy to over-claim what that exactness covers.** The first version of the conversion tests
asserted `original.PointAt(u, v) == converted.PointAt(u, v)` on a grid. Six of them failed, and the
code was right: **a rational quadratic traces a circular arc exactly, and its parameter is a
projective function of the angle rather than the angle.** Halfway along a quarter circle's knot span
is the arc's midpoint; a quarter of the way along is not 22.5°. There is no way to have both — a
representation whose parameter *is* the angle is not a polynomial or rational one, and would not be
exact.

**What is preserved is the domain, and therefore the extent.** The knot vectors span the original's
domains, so the corners and edges line up and a patch converts to a patch. That is what trimming and
a BRep face actually rely on.

**Two things follow for anything that compares two surfaces:**

- **Assert the implicit equation, not the parameterisation.** *Every point is one radius from the
  centre* is a statement about the sheet that a wrong construction cannot satisfy and that does not
  care how the surface is parameterised. It is also stronger: it caught a deliberately broken weight
  in eight tests where a point-for-point comparison would have caught it in six.
- **Sample on an odd grid.** An even grid lands on span boundaries, which is exactly where a wrong
  rational construction is still right, because the control points are on the curve there.

## N49 — A hand-written C ABI over OpenCascade is 30 entry points, not 350, and the difference is one struct

[ADR-0020](adr/0020-occt-via-c-abi-shim.md) estimated **350–500 exported entry points**, calibrated
against `opencascade-rs`, which declares 538. The shim that landed exports **about thirty** and does
everything M6 needs: construction, the three booleans, extrude, revolve, loft, fillet, chamfer,
shell, sew, heal, tessellate, and both directions of the model conversion.

**The estimate was not wrong about the work; it was wrong about the shape.** A binding that exposes
OpenCascade *types* needs a call per type per operation — a getter for a cylinder's radius, another
for its axis, another for a cone's half-angle, three more for the domain — and that is where 538
comes from. This one exposes **one flat tagged encoding** instead: a curve or a surface crosses as
`(kind, int[], double[])`, and a whole BRep crosses as one `spark_model_desc` of seventeen arrays.
Reading a shape is `spark_occt_read`, `spark_occt_model_sizes`, `spark_occt_model_read` — three
calls, whatever the shape contains.

**What that buys is exactly what D17 says the shim is for.** Thirty entry points is thirty things
that have to keep working across an OpenCascade upgrade. The encoding itself can grow a surface kind
without growing the ABI, because a kind is a number in an array rather than a function.

**What it costs is that the encoding is checked by nobody.** Two compilers see two halves of it and
neither can see the other, so an off-by-one in an offset table is not a build error in either
language. That is what the round-trip tests are for, and they are not optional: send a shape, read
it back, compare the geometry. The C smoke test does the same trip in C, so a failure there and a
failure in managed code point at different halves.

## N50 — An imported solid's inside is decided by asking, not by the order the faces arrived in

`BRepBuilderAPI_Sewing` orients a shell **consistently** and picks the global sign **arbitrarily**.
Whether the resulting solid's material is inside or outside is therefore an accident of how the
faces were built, and it changed underneath a working import when nothing about the geometry did:
bounding each face by its surface's domain gave the right sign, and bounding the same faces by their
loops gave the wrong one. **Every imported box then measured −24 instead of 24.**

**Two fixes were tried and the first one is not enough, which is the useful part.**
`BRepLib::OrientClosedSolid` flips the **solid's orientation flag**. That is sufficient to mesh
correctly — the explorer composes the container's orientation with each face's, so the triangles
come out wound the right way — and it is **not** sufficient for the boolean operators, which read
the faces. With only the flag flipped, a union of two 24-unit boxes came back as **50** and a
difference removed material that was never inside; the shapes were being treated as their own
complements. `ShapeFix_Solid` with `FixShellOrientationMode` reverses the **faces**, and the same
tests then give 42 and 60.

**The lesson generalises past this call.** A shape that meshes correctly has not been shown to be
correctly oriented, because meshing and modelling read different things. The test that separates
them is a **boolean**, not a picture: `AnImportedBoxKeepsThePositiveVolumeItHad` catches the sign,
and `TwoOverlappingBoxesFuse` catches whether anything downstream believes it.

## N51 — Spark's trims carry no pcurve, so the importer computes them, and skipping the loops produces a wrong cylinder that looks right

A `BrepTrim` names an edge and a direction and nothing else — `Spark.Geometry` has no
parameter-space curves — while OpenCascade will not work with a face until every edge on it has one.
The first importer sidestepped this by ignoring the loops entirely and bounding each face by its
surface's own domain.

**That produces a correct box and a wrong cylinder, and the wrong one is convincing.** A cylinder's
caps are planes; bounded by their domains they are *rectangles*, so the import is a tube with two
square plates. It sews, it meshes, it draws — and every boolean on it refuses, because it is not
closed. Nothing about the mesh says so. **The demo graph is what found it**, which is an argument
for demo graphs.

**The fix is the path an IGES or STL import already takes**: build the wires from the 3D edges, hang
them on the face, and let `ShapeFix_Face` project each edge onto the surface to make the pcurve.
`FixOrientation` then decides which wire is outer and orients the rest against it.

**One consequence is worth stating because it looks like a bug.** `BrepFace.IsReversed` must **not**
be applied on top of a loop-built face. Spark winds a loop anticlockwise seen from outside the
solid, so a reversed face's wire runs clockwise in its surface's parameter space — which is
precisely the fact `ShapeFix_Face` reads. Applying the flag as well flips the face twice. A face
with **no** loops still takes the domain path, and there the flag is the only thing that carries the
orientation, so it still applies.

## N52 — A tolerance is a request for work, and a curved solid will honour it without limit

`spark_occt_tessellate` was asked for a linear deflection of `1e-6` on a two-metre sphere by a test
that had reused the tolerance it used for the booleans. That is a legal request. OpenCascade began
answering it, and the test process reached **31 GB** before it was killed.

`Spark.Geometry`'s own tessellator has always had `Tessellation.MaximumSamplesPerDirection`, for
exactly this reason. The provider path now has the same kind of floor, expressed the way a kernel
can: **the deflection is clamped to a hundred-thousandth of the shape's bounding-box diagonal.**
That is far finer than any display, export or printer needs, and it is finite.

**The general shape of the mistake is worth naming.** A tolerance that is right for an operation is
not automatically right for a tessellation: a boolean's tolerance says *how close two things must be
to count as touching*, and a mesh's says *how many triangles do you want*. They are different
questions with the same units, and code that carries one `Tolerance` value from a node to both is
the place the confusion lands.

## N53 — STEP cannot be shipped without XCAF, and the trimmed payload is 45 MB

`M1.6-C7` and `M1.6-C8` both asked what OpenCascade's interchange really drags in, and both were to
be answered *from the link*. They now are, by walking the transitive DLL closure of
`spark_occt.dll` with `dumpbin /dependents` rather than by reading documentation.

**`spark_occt.dll` imports fifteen OpenCascade DLLs directly**, and `TKXCAF` is not one of them —
which was the encouraging half and is not the answer. **The closure is thirty-three DLLs and
45.1 MB**, and `TKXCAF`, `TKLCAF`, `TKCAF`, `TKVCAF` and `TKCDF` are all in it, pulled in by
`TKDESTEP`. **So `M1.6-C8`'s answer is no: STEP cannot be used without XCAF**, at the level that
matters for a payload, whatever the compiler was asked for.

`M1.6-C7`'s answer is the same shape. `freetype.dll`, `TKV3d` and `TKService` are also in the
closure, arriving through the interchange toolkits rather than through anything Spark asks for
directly. **Excluding the Visualization module would not drop FreeType while STEP is in the
build.** The vcpkg port compounds this by installing `opencascade[core,freetype]` — FreeType is a
default *feature*, not only a consequence of a module.

**Both answers are the unwelcome one and neither costs anything**, which is why the criteria said
in advance that a finding either way passes. 45.1 MB is *smaller* than the 52.0 MB the build
script stages, and both are far under the 100 MB that would reopen shipping OCCT by default. The
number to plan `E13-T17` against is **45.1 MB**, and the way to reproduce it is to walk the closure
rather than to weigh the directory.

## N54 — A library reached through a C ABI must not own the caller's stdout

OpenCascade's default messenger writes progress to `cout`: a transfer banner per shape, then
`** WorkSession : Sending all data`, then a line naming the file and its entity count. Inside a
CAD application with a console that is helpful. Inside `spark export` it lands in the middle of the
command's own output and makes it undiffable, which is the property `spark run` and `spark export`
exist to have.

`Message::DefaultMessenger()->RemovePrinters(STANDARD_TYPE(Message_PrinterOStream))` at
initialisation, beside `OSD::SetSignal(false)`, and for the same reason: **a library on the far
side of a C ABI has no business owning the caller's process-wide state.** Signal handlers and
stdout are both process-wide, both are grabbed by default, and both have to be given back.

What the shim has to say still gets out — through `spark_occt_last_error`, which is thread-local
and is read by the caller when a call fails. That is the whole channel, and it is deliberate.

## N55 — A BRep's mesh is geometrically closed and topologically split, and NFR-8 is about the second

**NFR-8 asks for a watertight mesh, and the provider's mesh of a box has twenty-four naked edges.**
That is not a defect and welding it by default would be the wrong repair.

Every kernel tessellates a BRep **face by face** — ours and OpenCascade's alike — so every vertex on
an edge shared by two faces exists twice, once per face. **Nothing leaks through**: the two copies
are at the same coordinates to the last bit. But `MeshTopology.IsClosed` counts *edges*, and two
coincident vertices make two edges, so a perfectly sound box reports naked ones. **The mesh is
geometrically closed and topologically split.**

**The split is what makes shading right, and welding costs exactly that.** A vertex carries one
normal. Weld a cube's corners and each corner has one normal, so the cube shades like a ball. There
is no representation in which a mesh with per-vertex normals is both closed and correctly creased,
so the choice has to be made per use rather than once.

So `Mesh.Welded(tolerance)` is an **operation**, and the answer to NFR-8 is: *ask for it when you
need the topology* — a volume, an STL for a printer, a watertightness check — and not when you need
the shading. Measured on the provider's output: a box goes 24 vertices → 8 and 24 naked edges → 0; a
cylinder 1442 → 720; a drilled plate 8676 → 4328. Every one closes.

**One implementation detail that is a correctness detail.** The merge hashes positions into a grid,
and **a grid is not a metric**: two points a hair apart can land in adjacent cells. So all
twenty-seven neighbouring cells are checked. Without that, whether two vertices welded would depend
on which side of a cell boundary they fell, and the same mesh translated by half a cell would weld
differently — which `WeldingIsNotSensitiveToWhereTheGridFalls` is the test for.

## N56 — The threading envelope, measured: independent shapes are independent

**Q14 and `M1.6-C5` asked whether the parallel evaluator may call the shim concurrently, and the
answer measured on this machine is yes, on distinct shapes.** Twenty threads × twenty-five
union-and-tessellate = **500 results in 2.73 seconds, zero failures**, and every one of the five
hundred volumes came back 42. A race that corrupted a shared table would show up there as a *wrong
number*, not only as a crash, which is why the assertion is on the volume rather than on the absence
of an exception.

**The thread-local error channel is checked rather than assumed.** Twenty threads failing at once
each read their own reason — if `spark_occt_last_error` were process-wide, some of those would come
back empty or carrying another thread's message.

**What is *not* claimed, and the distinction matters.** A single handle used from two threads at
once is still undefined, and the header says so. What has been shown is that *independent work is
independent*, which is the shape replication actually produces: one node, a list of inputs, a value
each. The conservative single-writer fallback R20 named is not needed for that case and is still the
right policy for a shared shape.

## N57 — A materialisation costs half a millisecond, which is why residency is worth having

`M1.6-C4` asked what a `Materialise` costs, because [ADR-0021](adr/0021-brep-kernel-residency.md)'s
whole rule rests on it being paid **once**. Measured on a drilled plate — six holes cut into a
20 × 12 × 2 block, twelve faces and thirty edges after the cuts:

- **first structural question: 0.44 ms** (the read, the decode, the nine arrays)
- **two thousand further questions: 0.04 ms**

So the arrays are built once and everything after is a field access, which is what the design
claims. The number worth remembering is the *ratio*, not the milliseconds: a bound on the absolute
time would be a bound on this machine, and the claim being tested is *paid once*.

**And it says something about the alternative.** Converting after every operation — the design
ADR-0021 rejected — would have added that 0.44 ms to every step of a chain, plus a re-import, plus
the drift the record is actually about. The time is the smallest of the three costs and is the only
one anybody would have noticed.

## N58 — A failed `Add` poisons `BRepOffsetAPI_DraftAngle`, so decide before asking

Drafting a box pulled along +Z refused **all six faces**, and the reason took three attempts to
find because each attempt hid the next one.

**OpenCascade only tapers planar, cylindrical and conical faces**, and a box's top and bottom are
parallel to the neutral plane — there is no line to tilt about, so those two cannot be drafted. That
much is expected. What is not documented anywhere obvious is the consequence: **a failed `Add`
leaves the algorithm in a state where every later `Add` raises `Standard_ConstructionError`** until
`Remove` cancels the bad one. So one undraftable face turns into a solid on which nothing can be
drafted.

**And recovering is not enough.** Catching the raise and calling `Remove` got past the per-face
problem and then `Build()` itself raised, with an empty message — the algorithm had been handed a
face it could not use and the recovery did not fully undo it.

**The fix is to not ask.** Look at each face's surface first: skip a plane whose normal is parallel
to the pull, skip anything that is not planar, cylindrical or conical, and only then call `Add`.
That is both simpler than the recovery and the behaviour a moulder means by *draft this part* —
refusing a whole solid because its top is flat would be the wrong answer to the right question.

**The general shape, which is not specific to drafting:** when a library's failure mode is *poisons
the object* rather than *returns false*, a precondition check is not defensive programming, it is
the only correct structure.

## N59 — The docs harness was right about a document it had never seen

`scripts/build-native.ps1` stages `THIRD-PARTY-NOTICES.md` beside the binaries, because a notice
left behind in a source tree is a notice nobody who received the software can read. The next run of
the documentation harness went red: three broken relative links, all in
`artifacts/native/win-x64/THIRD-PARTY-NOTICES.md`.

**It was right.** That copy is the same document with different neighbours, so a relative link
written as *`licences/`* resolves from the repository root and not from where the file lands. The repair is to exclude
`artifacts/` from the harness's scan, not to make the links absolute — a staged copy is a build
output and the harness's job is the documents somebody wrote.

Worth recording because the same shape will recur: **anything the build copies into `artifacts/`
becomes a second copy of a file some other gate has opinions about.**

## N60 — OpenCascade is 23% of the payload, and the payload is 224 MB

**R15 bracketed the installer growth at 40–160 MB uncompressed and unmeasured, and the whole
bracket was about the wrong thing.** The first staged `win-x64` build weighs:

| | |
|---|---|
| **total** | **224.4 MB** |
| the solid-modelling kernel | 52.0 MB (58 native DLLs) |
| everything else | 172.4 MB |

**OpenCascade is 23% of it.** The other 77% is the framework-dependent .NET publish: Roslyn for
code blocks, Avalonia and its Skia and HarfBuzz natives, and the rest of the managed surface — all
of which was there before ADR-0020 and none of which anybody had weighed either.

**Two things follow, and the second is the useful one.** The kernel's contribution is well inside
R15's bracket and nowhere near the 100 MB that would have reopened *shipping OCCT by default*. And
**if the installer is ever too big, OpenCascade is not where to look first** — which is the opposite
of what R15's framing would have led somebody to do.

**The kernel's number can come down and has not been made to.** The transitive DLL closure of
`spark_occt.dll` is **45.1 MB** ([N53](NOTES.md)); the staging step copies all 52.0 MB because that
is what the vcpkg port installed, and trimming to the closure is `E13-T17` work that has been
measured rather than done. The 6.9 MB difference is `TKXMesh`, `TKRWMesh`, `TKBin*`, `TKXml*`,
`TKOpenGl`, `TKMeshVS` and friends: real, and small next to the 172.4.

## N61 — `--self-contained` is a licence decision here, not a packaging one

`scripts/publish.ps1` publishes **framework-dependent**, with `PublishSingleFile=false` and
`PublishTrimmed=false`, and it is worth writing down that this is not a default nobody revisited.

The LGPL relink obligation needs OpenCascade to ship as **unmodified, replaceable shared
libraries**. A single-file bundle that unpacks to a temp directory does not obviously preserve a
user's ability to replace one; NativeAOT does not preserve it at all. So the two switches that
would most obviously shrink or tidy the payload are the two that are foreclosed, and an
architecture test (`NothingPublishesSingleFileOrNativeAot`) stops either being turned on by
somebody optimising in good faith.

**This is `E12-T8` constrained by a decision taken after it was written**, which is exactly the
shape ADR-0020 warned its consequences would have. *Nothing here is legal advice — `Q13` item 2 is
with counsel.*

## N62 — A profile is a wire, and reading the loop table made a polycurve exact

`spark_occt`'s profile encoding is the same `spark_model_desc` a whole BRep uses, and `build_wires`
was reading only the curve table: **one curve, one wire**. Everything else in the struct — the
edges, the trims, the loops — was ignored on that path.

**The consequence was an approximation nobody asked for.** A `PolyCurve` or a `PolyLine` has no
single NURBS that represents it without work, so `ModelWriter` fell back to *interpolating* one
through sampled points. Extruding a square drawn as four lines therefore produced a shape with a
curved wall, several extra faces and a volume that was nearly but not exactly its area times its
height — for a profile every piece of which was exactly representable.

**The encoding already had the answer.** A loop is a list of trims, a trim names an edge, an edge
names a curve: that is a circuit, which is what a wire is. Honouring the loop table on the profile
path costs about forty lines and removes the fallback entirely for polycurves and polylines, which
now go out as their own segments — lines as lines, arcs as arcs.

**What proves it is a face count, not a tolerance.** A square extruded from four lines has **six
planar faces**; the interpolated version had a NURBS wall. A mixed chain of line-arc-line extrudes
into two planes and **one cylindrical surface**. Neither of those numbers is reachable by a spline
that merely passes close to the right points, which is why they are the assertions rather than a
distance.

**The general lesson is about encodings rather than geometry.** When a format is shared between two
paths and one path reads a subset of it, the subset is invisible: nothing fails, and the missing
information is quietly replaced by a worse answer. The tell here was `ModelWriter.Approximated`
being true for shapes that had no business being approximate.

## N63 — A cleared depth buffer is not a zeroed one, and the difference renders an empty frame

`SoftwareFramebuffer` allocates `new float[Width * Height]` for depth. A fresh float array is all
zeroes, and in this projection's convention **zero is the nearest representable depth, not the
furthest**. A buffer that has never been cleared therefore rejects every fragment offered to it,
and what reaches the screen is a correctly drawn background with no geometry on it — which is
indistinguishable from a scene that is genuinely empty, from a camera pointing the wrong way, and
from a tessellator that produced nothing.

It was caught by the one test that renders **without** calling `Render` first —
`AnUninitialisedRendererDrawsNothingAndDoesNotThrow` — because every other test clears depth as
its first act and so could never have seen it. The fix is one line in the constructor and one in
`Resize`: clear on allocation, so the invariant holds from the first instant rather than from the
first frame.

**The general shape, which is worth more than the bug.** A default value that is *valid but
extreme* is more dangerous than one that is invalid. `0.0` is a perfectly legal depth; nothing
throws, nothing warns, and the failure presents as an absence. Had the sentinel been `NaN` the
first comparison would have behaved visibly oddly instead.

**Also recorded here because it will be rediscovered otherwise:**
`System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView` is **right-handed with a Direct3D depth
range**, so normalised device z runs **0..1**, not the −1..1 an OpenGL reflex expects. Spark's
camera has always produced that matrix and the GL backend has always fed it to GL, which maps
`[-1, 1]` into the depth range by default — so the GL path uses only the far half of its depth
buffer and has done since it was written. It is correct, it is consistent, and it costs one bit of
depth precision. The software rasteriser matches the convention deliberately rather than
"fixing" it, because a backend that disagreed with the other about what a depth means would make
every cross-check meaningless.

## N64 — Two backends, one capture flag, and a screenshot that photographed the wrong one

Adding the software fallback to `ViewportControl` gave the control **two** places that could
service a `RequestCapture()`: `OnOpenGlRender`, reading back off the GPU, and the new
`DrawSoftwareFrame`, copying its own framebuffer. Whichever ran first consumed the flag. On a
machine with a perfectly healthy GPU, the software path ran first — because **Avalonia paints the
control before `OnOpenGlInit` has fired**, so `_renderer` is still null at that moment and nothing
inside `Render(DrawingContext)` can tell "GL has not arrived yet" from "GL is never arriving".

`--graph solids --screenshot` therefore wrote a CPU-rendered image while reporting
`OpenGL ready. Version 'OpenGL ES 3.0 (ANGLE ...)'` on the line below it. Both statements were
true and together they were a lie.

**How it was caught, which is the part worth keeping.** Nothing failed. The picture was correct —
the same scene, the same camera, the same colours — because the two renderers agree by design.
What did not survive scrutiny was a *coincidence*: the software and GL runs reported
`663 distinct colours, mean luminance 34.7/255`, identically, and the two PNGs had the same MD5.
Two renderers with different dither functions and different line rules cannot produce identical
bytes. **The evidence of the bug was that the outputs agreed too well**, and the instinct worth
generalising is that an implausible agreement deserves the same suspicion as an implausible
disagreement. A probe printing which branch serviced the capture settled it in one run.

**The fix is a committed-backend rule**, `IsSoftwarePresenting`, with three ways to become
committed — the `--software-renderer` switch, a GL callback that ran and left no renderer, or
**no GL callback at all within 1.5 seconds of the control being attached**. The third needs a
timer rather than an event, because a context that fails to be created never calls anything: the
absence is the signal, and an absence has to be waited for.

**Two things were tidied under the same fix.** `TakeCapture` now normalises to **top-down rows**
whichever backend drew the frame, because `glReadPixels` returns bottom-up and the rasteriser
returns top-down, and which one drew a given frame is exactly what a caller should not have to
know — `MainWindow` used to flip unconditionally, which was right for GL and would have silently
inverted every software capture. And the software path renders at one device pixel per layout
unit rather than multiplying by `RenderScaling`: a quarter of the fragments on a 200% display, on
the one code path that runs when the machine has already proved it has no usable GPU.

## N65 — Every port description was written, and nothing read it

`XmlDocumentation` was built to answer one question — *what is this member's summary?* —
and it answered it well. `NodeImporter` used it for a node's description and took each **port's**
description from an optional `[NodePort(Description: ...)]` attribute instead.

**There are zero `[NodePort]` attributes carrying a description in the entire node library.** So
every port on all 115 nodes had a null description, and the generated reference page showed a full
column of port names, types and defaults beside a completely empty Description column. The text a
reader wanted was in the source the whole time — `<param name="radius">The radius. Must be
positive.</param>` — and CS1591-as-error had made writing it mandatory. Nothing ever read it.

**It went unnoticed because nothing displayed it.** Port descriptions had no consumer: the canvas
shows port *names*, the tooltip shows a signature. The first thing that ever asked for the
description was the generated help page, and it asked on the day the help page was written. A
field that is populated by nobody and read by nobody is invisible until something finally reads it.

**The fix is small and the lesson is not.** `XmlDocumentation` now also collects `<param>` and
`<returns>`, and `NodeImporter` prefers an explicit attribute and falls back to the doc comment.
Roughly 380 input ports gained a description without a word being written.

**Two implementation details worth keeping.** The `name` attribute of a `<param>` element must be
read **before** `ReadInnerXml`, which advances past the element and takes its attributes with it.
And the attribute still wins over the doc comment where both exist, because they address different
readers: `[NodePort]` is what an author says to somebody looking at a node, and `<param>` is what
they say to somebody looking at the API.

**Proven without reverting anything:** a grep for `[NodePort(... Description ...)]` across
`src/Spark.Nodes.Core/` returns **zero**, so the test asserting that more than nine ports in ten
carry a description would have measured 0% before this change.

## N66 — A screenshot that waited for a clock instead of a frame

`--screenshot` requested a capture and then read it after a fixed 600 + 400 ms. That delay was
tuned when the only backend was OpenGL on a warm driver, and it held for months. It stopped
holding, and the failure was total: **no viewport image at all**, with the message *no viewport
read-back: neither backend produced a frame.*

**The cause was not what it looked like.** The obvious suspect was the software fallback, which by
design commits only after 1.5 seconds have passed with no GL callback ([N64](#n64)) — longer
than the capture waited. But the fallback was never reached: the real answer was that **OpenGL
came up perfectly well, just later than one second** on a machine that had been running builds all
day. The fixed delay had always been a race and had simply always won.

**Two things made it hard to see, and one is worth fixing on its own.** The failure path printed
*neither backend produced a frame* and then **returned before printing the viewport status** —
so the single most useful line, `viewport status: no GL callback ran`, was emitted in every case
except the one that needed it. The status is now printed on the failure path too, along with
whether the software backend is presenting.

**The fix is to wait for a frame rather than for a clock.** `ViewportControl.HasCapture` reports
whether one has completed; `MainWindow` polls every 150 ms up to six seconds, re-requesting each
time because a viewport with nothing changing produces no frames. Which backend services the
capture is not something the caller can predict — GL may initialise at once, or never —
and a delay tuned to one of them is a test of the machine's mood.

**The general shape.** A fixed sleep standing in for a condition is a race that has not failed
*yet*. It reads as settled because it has always passed, and the day it stops the symptom is an
absence rather than an error.

## N67 — The last root was the local in the asserting frame

`ScriptLoadContextTests.UnloadingReleasesTheScriptAssemblies` failed roughly **one full-suite run
in four** — the worst frequency there is: often enough to break a build, rare enough to be
dismissed as noise. In isolation it passed every time.

The test already knew about this class of problem. `Compile` is marked
`MethodImplOptions.NoInlining` precisely so no local roots the compiled definition, and the class
doc explains why. What it missed is that **the factory itself was still a live local in the
asserting frame**: under a debug JIT a local is rooted until its method returns, past the point
where the source says it is dead. Whether the context could be collected therefore depended on how
hard the collector happened to work that run, which is why more assemblies loaded into the test
process made it surface.

The whole create-compile-unload now happens in a `NoInlining` helper that returns only the
`WeakReference`, so nothing survives into the frame that asserts — the same shape
`PackageLoadContextTests` uses. Four consecutive full-suite runs are clean.

**Worth generalising:** for a collectible-context test, isolating the *thing being collected* is
not enough. Every local on the path, including the factory that made it, has to be out of scope
before the assertion, and "out of scope" means *in a frame that has returned*, not *past its last
use*.

## N68 — A package install is an unzip, and an unzip writes wherever the archive says

A `.nupkg` is a zip, and installing one is extracting it. A zip entry's name is **data supplied by
whoever built the archive**, and nothing stops it being `../../something`: an extractor that joins
that onto a destination and writes the result has turned *install a package* into *write an
arbitrary file*, with the application's own privileges, before a single line of the package's code
has run.

`NuGetPackageClient.Extract` therefore resolves each entry to a full path and refuses anything that
does not start with the destination. **The check is on the resolved path, not on the entry name**,
because `a/../../b` is the same attack spelled differently and a name-based check that looks for
`..` misses it.

**Proven load-bearing rather than assumed.** With the guard disabled, the test that installs a
package containing `../../escaped.txt` reports *no exception was thrown* — the extract
succeeded and wrote outside the package folder. With it, the install is refused, nothing is
written, and the folder is not created.

**The general shape:** every field of an archive, an image header or a file format is input from
whoever produced the file, including the fields that look structural. An entry name reads like
metadata and is a filename.

## N69 — A NuGet folder name does not split at the first dot

Packages are installed to `id.version`, matching NuGet's own convention. Recovering the identity
from the folder name looks like `IndexOf('.')` and is not: **a package id contains dots too**, so
`Acme.Nodes.Geometry.2.1.0` would come back as a package called `Acme` at version
`Nodes.Geometry.2.1.0`.

The split is before the **first segment that starts with a digit**, which is what the convention
actually means. It is a named method rather than an inline expression precisely because the obvious
version is wrong and would look right in review.

It matters more than it sounds: the id is what `PackageLoadContext` is keyed on and what an
uninstall names, so getting it wrong would produce a store that lists packages nobody can remove.

## N70 — Side-by-side is about dependencies, not about two versions of the same node library

`E7-T3` says one collectible load context per package **version**, *not per package, which kills
side-by-side*. Building `PackageManager` made it worth writing down **which** side-by-side that
buys, because the phrase reads as promising more than it does.

**What it does buy, and it is the case that matters.** Package A depends on `Foo 1.0`; package B
depends on `Foo 2.0`. Both load, each resolving its own `Foo` from its own folder, and neither
knows the other exists. Without per-version contexts, whichever loaded first would win and the
other would break in a way that names a type it never mentioned.

**What it does not buy: two versions of the same node library both contributing nodes.** They
would claim the same node keys — `Acme.Nodes/Point.ByX` carries no version — and the
library refuses a duplicate. `PackageManager` reports the clash rather than picking a winner,
because either rule would leave a user with a node that quietly changed meaning and no way to find
out why.

**And that is correct rather than a limitation to fix.** A `.spark` file names
`Acme.Nodes/Point.ByX`. If two versions could both be active, that name would be ambiguous, and a
graph's meaning would depend on load order. The version belongs to the *install*, not to the
reference — which is the same reason `E7-T6`'s placeholder keeps the key and not a version.

The distinction is worth the paragraph because *side-by-side* is exactly the phrase somebody will
later quote when asking why two versions of one package cannot both be switched on.

## N71 — Awaiting inside the headless Avalonia dispatcher deadlocks, and it looks like a hang

`HeadlessSession.Run` calls `Dispatch(body).GetAwaiter().GetResult()`: the caller blocks until the
body finishes on the UI thread. A body that then awaits anything posts its continuation to that
same thread, which is not going to run it, because it is waiting for the body. The first run of
`PackageBrowserTests` hung for seven minutes and was killed; there is no message, no stack, and no
failed assertion — the process simply stops.

**So the asynchronous half happens outside the dispatcher and only the window is driven within.**
Preparing an install, searching a feed, confirming — all of that runs on the test's own thread
first, and `HeadlessSession.Run` is then handed a model already in the state the window is supposed
to show. That is also a better test: it separates *does the view model do the work* from *does the
window show what the view model says*, and the second is the only part that needs a dispatcher at
all.

**The same shape is why the window has no async of its own beyond its click handlers.** Everything
it does is `Sync()`, which reads the model and writes controls, and every button handler is the
thinnest possible `await` on a view-model method.

## N72 — One `GC.Collect` does not release a package's files, and the symptom blames the wrong thing

Removing an installed package unloads its `AssemblyLoadContext`, collects, and then deletes the
folder. With a single `GC.Collect()` the delete failed on Windows: the context had not finished
unloading, the `.dll` was still mapped, and `Directory.Delete` threw part-way through — leaving a
**half-deleted folder** and a status line saying the package was locked and to restart.

The status line was true and useless. The package was locked, but only for another few
milliseconds, and restarting was not what the user needed to do.

**A collectible context needs more than the collection that drops the last reference to it**, which
`PackageManagerTests` already knew — it loops up to twenty times waiting for its weak reference to
die. `PackageBrowserViewModel.Remove` now does the same before it deletes anything, and the restart
advice is reserved for the case where the reference really is still alive after all of them.

**Found by a test, and only because the test asserted the message.** An assertion on
`library.Count == 0` passed throughout: purging the library is the half that always works. The
defect lived entirely in what happened afterwards and in what the user was told about it.

## N73 — Compiling against an assembly and running against it are two different open handles

`E7-T9` says *reading a referenced assembly never locks it, so users can rebuild their library
while Spark is open*. That reads as one property. It is two, and only one of them was already
true.

**The compile side was safe by Roslyn's grace.** `MetadataReference.CreateFromFile` opens metadata
with `FileShare.ReadWrite | FileShare.Delete`, so the file can be rewritten and deleted underneath
it. A first probe seemed to prove even more than that — until it turned out `CreateFromFile` is
lazy and had not opened the file at all. The honest test forces `GetMetadata()` first; without that
it asserts only that Roslyn had not got round to it yet.

**The load side was not safe, and nothing said so.** A script calling into a user's DLL compiles
perfectly and then fails at evaluation with `Could not load file or assembly 'Acme.Maths'`, because
`ScriptLoadContext.Load` returns null — the deliberate policy that keeps `Point3d` one type — and
the default context has never heard of a file in some folder of the user's. The fix resolves it on
the `Resolving` event, which fires **only after the default context has failed**, so nothing found
this way can shadow a contract assembly. And it loads **from bytes**: `LoadFromAssemblyPath` maps
the file for the life of the context, which would mean the user cannot rebuild the very library
they added. Switching that one line back made the rebuild fail with *the process cannot access the
file*, which is the proof the byte load is load-bearing.

**The lesson is about the test, not the code.** Every test up to that point asserted that a path
had reached a list. The one that found this called a method that exists only in the referenced
assembly and asserted it returned 84.

## N74 — `ReferenceCatalog.Add` reports how much the catalogue grew, which is not what it was asked

`Add` returns `replacement.References.Length - _current.References.Length`. Rebuilding the snapshot
also sweeps `AppDomain.CurrentDomain.GetAssemblies()`, so anything the process loaded since the last
snapshot arrives in the same count. Add one assembly and get back two.

It bit twice in one sitting. First in `LocalReferencesViewModel.Apply`, which reported having
applied more references than it had; then in a test asserting `Add` returned 1, which **passed
alone and failed in the full suite**, because running the other five hundred tests first loads more
assemblies. Both now ask a question the catalogue can answer honestly: `Reload` says whether a
particular path is referenced, and a test asserts the reference is present rather than counting.

A count that is right in isolation and wrong in company is worse than no count, and this one is on
a public method whose summary said *how many were added*. It now says what it actually returns.

## N75 — The catalogue promised an import it could fail to reference

`DefaultImports` puts `using Spark.Geometry;` in front of every script. The references, though, came
almost entirely from sweeping what the process had already loaded — and a referenced assembly does
not load until something touches a type in it. A catalogue built early enough therefore promises an
import it cannot satisfy, and the user sees *the type or namespace name 'Geometry' does not exist
in the namespace 'Spark'* on a line they did not write.

`Microsoft.CSharp` and `System.Linq.Expressions` were already added by name for exactly this reason,
each with a comment about the message it produces. `Spark.Api` and `Spark.Geometry` are now added
the same way. **Anything the prelude names must be referenced by name, not hoped for**, because the
sweep is an optimisation and the prelude is a promise.

## N76 — Two tests that asserted more than the code promises, and both said so only under load

Both landed green in isolation and failed one run in three or one in six in a full parallel suite,
which is the worst shape a test can have: it looks like a regression somewhere else.

**`RemovingTakesTheNodesBackOutOfTheLibrary` asserted the package folder was always deleted.**
Removal cannot promise that. It purges the library — which it *can* promise, and which is the half
a user sees on the canvas — and then tries to delete files whose load context may not have finished
unloading. The method already reported that honestly and offered a restart; the test simply did not
believe it. It now asserts the purge unconditionally, and the folder **or** the restart message.

Two real improvements came out of it. `PackageStore.Uninstall` now retries the delete for up to
200ms, because unmapping lags the collection that freed it — a single attempt could fail, or worse
half-succeed and leave a folder with some of its files gone. And `PackageBrowserViewModel.Remove`
collects in a bounded loop rather than once ([N72](NOTES.md)).

**`TheFingerprintMovesWithTheReferences` compared two catalogues built moments apart.** A catalogue
is a sweep of what the process has loaded, and the other tests in the assembly load assemblies
while this one runs, so the two legitimately differ. It was asserting a fact about the process, not
about the fingerprint. Adding a warm-up did not fix it and could not have. It now asserts that one
catalogue's fingerprint is stable while its references are, which is the property a cache key
actually needs, and the test's own name is still what it checks.

**The pattern worth remembering**: when a test asserts a *consequence* of best-effort machinery
rather than the machinery's own guarantee, it passes until the machine is busy. Both of these were
written by the same hand in the same sitting, and both were caught only by running the suite eight
times in a row rather than once.

## N77 — Every package test passed, and no real package could be installed

`PackageLoadContext` resolved assemblies from exactly one path, `<folder>/<name>.dll`. Extraction is
verbatim by design — *a package version's folder is a copy of the `.nupkg`'s contents* — and
`dotnet pack` puts assemblies at `lib/{tfm}/Name.dll`. So **every package on nuget.org would have
failed to load**, with the message *Package 'X' has no assembly 'Y.dll'*.

Fifty-eight tests covered this layer. All of them passed. Every one of them built its package by
hand, and every hand-built package put the assembly at the root, because that is the shortest thing
to write in a test.

**The test that found it was one sentence long**: build the package the way `dotnet pack` builds
one, then load it. It went red immediately. The fix uses `FrameworkReducer` rather than a
hand-written ordering, because choosing between `net8.0`, `netstandard2.0` and `net472` for a
`net10.0` host looks like three lines of string comparison and is not.

**The lesson is about fixtures, not about layout.** A test helper that constructs the subject in the
convenient shape rather than the real one hides every defect that lives in the difference, and it
hides them uniformly, so the suite's greenness is evidence of nothing. The dependency tests written
straight afterwards build their packages with `lib/net10.0/`, and the final check installed a
package genuinely produced by `dotnet pack` from a project written for the purpose.

## N78 — Dependencies live inside the package's folder, and that is the trade-off restated

`E7-T2` installs a package's dependencies into `.deps/<id>.<version>/` **inside the package's own
folder**, rather than sharing one copy between packages the way NuGet's global packages folder does.

Two packages depending on the same library therefore each get their own copy. That is the same
trade-off this layer already made when it chose download-and-extract over restore, and the reasons
are the same: **removing a package removes exactly what it brought**, no package can be broken by
another package's uninstall, and the load context stays a rule about file existence in known folders
rather than a resolver with a graph in it.

The cost is disk. The thing bought is that `PackageLoadContext` remains readable, and that a
question a user might ask — *what did installing this put on my machine* — has an answer that is one
folder.

## N79 — A packaging check run inside the repository proves nothing

`OcctKernel` walks up from the executable looking for `artifacts/native/win-x64`, which is a
deliberate convenience: a developer running out of a build tree gets the kernel without setting
`SPARK_OCCT_PATH`. It also makes every in-tree check of a *packaged* build vacuous.

Measured, not assumed. A build staged with `publish.ps1 -SkipNative` — **zero native DLLs in the
folder** — ran `spark export --open docs/examples/solids.spark` from inside the repository and
wrote nine solids and seventy-four faces. Copied to a temporary directory outside the tree, the
same build failed with `SPK1080: No solid-modelling kernel is installed`, exit code 1.

**The CI runner is not immune**, which is the part worth writing down. The portable job downloads
the shim into `artifacts/native/win-x64` so that `publish.ps1` can stage it — and that is exactly
the folder the resolver walks up to find. A check that unpacked the zip into the workspace and ran
it would pass on a zip containing no native payload at all.

So both the CI job and the release workflow unpack into `RUNNER_TEMP` and run from there.

**And `--version` is no help either.** It prints `Solid modelling: OpenCascade 8.0.1` whether or not
the provider loaded, because it reports the configured provider rather than a loaded one. The first
draft of the CI step asserted on that string and would have passed on an empty zip. What
distinguishes them is doing something that needs the kernel: exporting a solid.

## N80 — `Compress-Archive` is stable across two runs and not across a rebuild

The portable zip is written by hand rather than with `Compress-Archive`, and the reason is narrower
than *it is not deterministic*.

Zipping one folder twice with `Compress-Archive` produces identical bytes; that was checked, and it
does. What it does not survive is a **rebuild**: it stamps each entry with the file's last-write
time, so the same source compiled again — producing byte-identical assemblies — yields a different
archive and a different checksum. Touching every timestamp in a staged folder and re-zipping
demonstrates it in a second.

A release whose hash changes when nothing changed is a release nobody can verify by hash, which is
the only way anybody verifies one. `pack-portable.ps1` therefore sorts entries by ordinal path and
stamps them all 1980-01-01 — the earliest a zip can represent, and visibly not a real build date,
which is better than a plausible wrong one.

**The first version of this note claimed `Compress-Archive` differs between two runs over one
folder.** It does not, and the claim was in the script's own documentation before it was checked.

## N81 — The canvas benchmark prints two numbers and only one of them answers the claim

`--canvas-benchmark` prints a **render pass** figure and a **wall clock** figure. On this machine,
Release, 2 000 nodes: render pass **1.2-1.4 ms median, 2.8-3.7 ms p95** — against ADR-0013's ceiling
of 16.7 ms, one frame at 60 fps — and wall clock **41 ms, 24 fps**.

Read cold, that looks like the headline claim being missed by a factor of two and a half. It is not.
`bench/budgets.jsonc` already explains why the render pass is the number judged, but **nobody reads
a budget file while looking at a benchmark's output**, so the output now says so itself.

**The evidence that settles it is that the wall-clock floor does not scale with node count.**
Measured at three sizes: 100 nodes 28.5 ms (35 fps), 500 nodes 36.5 ms (27 fps), 2 000 nodes 41.1 ms
(24 fps). The canvas contributes the difference — about 12 ms across a twentyfold increase in nodes
— and something else contributes a fixed ~27 ms that is there when the canvas is nearly empty. That
residual is the whole window composed and presented plus the frame scheduler's cadence, none of
which the canvas governs.

**A second measurement worth keeping**: Release renders *faster* than Debug on the render pass
(1.32 ms against 1.75 ms) and reports a *worse* wall clock (45.7 ms against 31.8 ms). Two numbers
moving in opposite directions between two builds of the same code is on its own enough to show they
are not measuring the same thing.

**What was changed is the output, not the budget.** Widening a claim to fit a measurement is the
failure this note exists to prevent; the claim and its ceiling are untouched, and the nightly's
regexes were re-run against the new output to confirm they still match.

## N82 — Startup was measured by nothing, and measuring it needed the right harness

Nothing in `bench/` measured startup, which for an end-user application is the first impression it
makes. Measured 2026-09-01, Release, five runs each:

| | |
|---|---|
| `spark --version` | **48 ms** median (46 min) |
| Desktop launch to a rendered shell with geometry, PNG on disk, process exited | **3.0 s** median (2.0 s min) |

The desktop figure is an **upper bound**, not a startup time: it goes through the screenshot path,
which waits for a full evaluation and then polls for a GL frame at 150 ms granularity. It is still
the honest end-to-end number a user would feel, and it is the one worth quoting until something
measures the window appearing.

**The first attempt measured 4 ms and was wrong.** `Measure-Command { & $exe ... }` does not wait for
a GUI process — `Spark.Desktop` is a `WinExe`, so the shell returns immediately. Five runs of "4 ms"
looked plausible enough to believe. `Start-Process -Wait` gives the real figure. **A startup
measurement that comes back implausibly good has usually measured the launch, not the start.**

**Deliberately not budgeted in CI.** Wall-clock startup on a hosted runner is dominated by disk
cache and antivirus, and [N29](NOTES.md) already argues that wall-clock ceilings there are only good
for catching a step change. It is recorded here so a regression has something to be compared against.

## N83 — The accessibility bar has to be two checkable sentences or the pass never ends

*Make it accessible* is not a task anybody can finish. The bar this pass set itself is two
sentences, both properties of the markup rather than matters of taste: **every gesture reachable
without a mouse**, and **every control named**.

**What was already done was the colour half**, and it was done properly: the design language carries
contrast figures, `PaletteContrastTests` asserts them against the real tokens, and Principle 4
already forbids colour being the only carrier of a state — which is why a frozen node gets a mark
as well as a desaturation.

**What was missing was everything else.** `AutomationProperties` appeared **nowhere** in the
application, so every control was anonymous to a screen reader. And the only keyboard bindings were
undo and redo: opening, saving and running a graph — the three things a user does most — were
reachable by mouse alone.

**The test is text, and deliberately.** Instantiating the window to walk its visual tree needs a
dispatcher and returns only realised controls; the `.axaml` file is the whole truth and it is what a
future edit changes. The risk with a text test is that a regex matching nothing passes silently, so
there is a second test asserting the toolbar has at least twenty buttons — without it,
`EveryToolbarButtonIsNamed` would go green the day somebody renamed the class.

**It found one immediately**: the missing-package banner's button, whose label is built at runtime.
Its name is now set in code beside its content, because a static name saying *find the missing
package* while the button says *Find Acme.Nodes* is worse than either alone.

**A name that repeats the label earns nothing**, so a third test refuses that too — `Open…` read
aloud is *open ellipsis*. Undo and Redo are the exceptions and they are the right ones: the word is
the action.

**What this pass cannot claim.** No screen reader was run; none is available here. What is asserted
is that a name exists and is not the label repeated. Whether it reads well aloud is a judgement a
person makes with a screen reader running, and nobody has made it.

## N84 — A status line that nobody re-reads becomes decoration

Two help topics carried **`Status: Specification. Written before the engine exists`** and
**`written before any UI code exists`**. The engine has existed since M2 and the UI since M3. Both
sentences were false, in the two topics a reader is most likely to treat as authoritative, and
[D19](PRD.md#13-decision-log) predicted exactly this when it deferred the Help pass.

**The fix is not editing the line.** `Specification` means *this page came first and the code is
written to match it*, so retiring it means **re-reading the page against the code**, and the answer
was different for each:

- **`lacing.md` is fully executed.** Its 90-row case table is `LacingCaseTable`, run twice over by
  `LacingCaseTests` — once against the values it specifies and once to check every diagnostic it
  raises carries a help topic. 2 x 90 + 1 = the 181 tests that class reports. The topic's own claim
  that *if the table and the implementation disagree, the table is right* is enforced.
- **`design-language.md` is only partly executed**, and saying so was the honest outcome.
  `PaletteContrastTests` asserts the contrast arithmetic — thirty assertions across twelve tests.
  The **colour tables are not asserted in full**, and a naive check comparing every `#RRGGBB` in the
  topic against the palette reports 25 unmatched values, of which most are worked examples, rejected
  candidates or derived ladder steps rather than tokens. **A test that cannot tell those apart would
  cry wolf**, so none was written and the topic now says which half is enforced.

**The general shape**: a document that claims to lead the code has a debt attached, and the debt is
only visible in a line nobody re-reads. `HelpTopicSchemaTests` now at least requires the status to
be one of the two words, so a third state cannot appear quietly; it cannot tell you the word is out
of date, and the note says so.

## N85 — The docs harness stopped a guide from being filed as a help topic

`docs/HELP-AUTHORING.md` was first written to `docs/help/AUTHORING.md`, which seemed the obvious
place for a guide about writing help. `Spark.Docs.Verify` failed immediately:
*docs/help/AUTHORING.md: no YAML front matter.*

The check was right and the file was in the wrong place. **Everything under `docs/help/` is
end-user help**: the help window lists all of it, `HelpTopicSchemaTests` checks it as a topic, and
`NodeTopicCoverageTests` checks its node coverage. A contributor guide is none of those things, and
the two ways to make the build pass were to move the file or to weaken the check.

Moving it was right, and the guide now says so in its own first paragraph so the next person does
not repeat it. **The tempting fix — narrowing the harness to `docs/help/concepts/` — would have
traded a real invariant for one file's convenience.**

## N86 — The solids demo stalls for 15 seconds, and it is not the solver

Staging a build for a hands-on and opening `--graph solids` took **18 seconds**. Measured against
the other two demos and against the same graph through other paths:

| | |
|---|---|
| Desktop, points demo | 2.1 s |
| Desktop, curves demo | 3.1 s |
| **Desktop, solids demo** | **18.2 s**, three runs 15.1 / 22.2 / 19.1 |
| `spark export` on the same file, 4 runs | 649 ms cold, then **~290 ms** |
| `GraphEvaluator.Evaluate` on the same file, kernel installed | **31-77 ms** |

**So it is not the solid modelling and not the evaluator.** The CLI opens the same `.spark`, runs
the same 26 nodes through the same OCCT provider and writes STEP in under a third of a second. The
engine's own evaluation of it is under a tenth of that.

**Nor is it the scheduler**, which was the first hypothesis, because the desktop runs
`ParallelEvaluationScheduler` and the CLI runs `SequentialEvaluationScheduler`. Timed side by side
on the same graph with the kernel installed: sequential 77 ms, parallel 33 ms. The parallel one is
faster.

**A probe that measures the wrong thing looks like an answer.** The first run of that comparison
reported 3 ms and 76 ms — and **three diagnostics**, because the test host had not installed the
kernel, so every solid operation failed immediately. Both numbers were real and neither was about
solids. Asserting the diagnostic count is what caught it.

**What is left is the path between an evaluated solid and a frame**: tessellating a BRep into a
mesh and building the viewport's buffers. The status bar reports **three objects** for that graph,
so it is fifteen seconds for three solids. `spark export` never goes there — STEP carries BRep,
not triangles — which is exactly why the CLI does not show it.

**This is the gap `E12-T12` named and did not measure.** That pass said in as many words that it did
not cover *the viewport's own render*; nothing in `bench/budgets.jsonc` touches tessellation. The
one demo that exercises it is fifteen seconds slower than the two that do not, and no test would
have said so.

## N87 — Half a degree, and the third wrong hypothesis in a row

`E12-T19` — the solids demo taking 18 s against 2 s for points — was one line:

```csharp
new Tolerance(Math.Max(diagonal * 0.001, 1e-12), Angle.FromDegrees(0.5), 1e-12)
```

**Half a degree reads like a sensible smoothness figure.** It is roughly fifty-seven times finer
than the half a *radian* a mesher of this kind conventionally defaults to, and the mesher's cost
against it is nowhere near linear. On the nine solids of `docs/examples/solids.spark`, sag held at
a thousandth of the diagonal throughout:

| angular deflection | time | triangles |
|---|---|---|
| **0.5 deg** | **17,440 ms** | **1,110,772** |
| 2 deg | 266 ms | 79,092 |
| 4 deg | 97 ms | 23,204 |
| **6 deg** | **61 ms** | **11,636** |
| 12 deg | 42 ms | 3,924 |

Six degrees is **286 times faster** and gives a cylinder sixty segments. The rendered demo is
indistinguishable from the old one: the cylinder is still round and the fillet still reads as a
fillet, which was checked by looking rather than by reasoning. Desktop wall clock **18.2 s to
2.0 s**, four runs, identical to the points demo.

**Three hypotheses, three wrong, and each one was killed by a measurement rather than by thought.**

1. **The scheduler.** The desktop runs parallel and the CLI sequential, and `Q14` had established
   that OCCT tolerates concurrency only under conditions. Timed: sequential 77 ms, parallel 33 ms.
   Parallel is the faster one.
2. **The probe that tested it.** Its first run reported 3 ms — and **three diagnostics**, because
   the test host had never installed the kernel, so every solid operation failed instantly. Both
   numbers were real; neither was about solids. Printing the diagnostic count beside the timing is
   the only reason that did not become the answer.
3. **The first tolerance sweep**, which reported that the angle barely mattered: 0.5 deg gave
   1,110,772 triangles and 2 deg gave 1,102,132. **Every row after the first was a cache hit.**
   `Tessellate` caches against the shape and **not against the tolerance**, so one set of solids
   swept through six tolerances is one tessellation and five lookups. Putting the coarse row
   *first* is what exposed it: 35 ms and 1,332 triangles, and then the same coarse request after a
   fine one returned 1,099,460.

**The general lesson is about sweeps.** A parameter sweep over a cached function measures the cache.
The fix is to rebuild the input for every row, and the tell is a result that does not vary when it
obviously should.

**And one number that looked wrong and was not.** `CurveDrawable` tessellates with
`Angle.FromDegrees(0.001)`, five hundred times finer again. Measured on the curves demo: 2 ms, 63
points, and **the point count is identical at 0.001, 0.5, 2 and 6 degrees** — sag dominates for a
curve. It was left alone. Not every odd-looking constant is a defect, and changing one on suspicion
would have been the fourth wrong hypothesis.

---

## N88 — Avalonia hit-tests against what a control *drew*, so a control that draws nothing is invisible to the pointer

`ViewportControl` had a wheel handler, a middle-button pan and a right-button orbit. All three were
correctly written. **None of them had ever run**, and the viewport had ignored the mouse since the
day it was written.

`Render` returned early once the GL renderer was initialised — reasonably, since there was nothing
left for the software path to draw. The consequence is not obvious: the control's 3D content is a
**compositor-owned GL surface that is not in Avalonia's scene graph at all**, so on any machine
where GL initialises, `Render` drew *nothing*, the control had no geometry to hit, and every
pointer event fell through to whatever was behind it.

The fix is one line before the early return:

```csharp
context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
```

It has to be `Transparent` rather than a colour — anything opaque is drawn **over** the GL surface
and hides the model. Transparent is invisible and still hit-tests, which is the whole trick.
`GraphCanvas` never hit this because it fills itself with a real background.

**The general rule, worth more than the fix:** in Avalonia, "is this control hit-testable" is a
question about its *rendered output*, not its bounds. A `Background` of `null` is the usual way to
trip over this; drawing nothing at all is the same bug wearing a different coat. Anything hosting
foreign content — GL, a native handle, a compositor surface — needs an explicit transparent fill or
it is inert.

**And how it was found: a person opened the application and tried to orbit.** The viewport had
tests for its camera, its renderer, its read-back and its tessellation. Not one of them pressed a
button. `ViewportNavigationTests` now does, and `TheViewportIsHitTestable` asserts the property the
others depend on so a regression names its own cause. Note that a headless test must call
`window.CaptureRenderedFrame()` before `InputHitTest` — hit-testing is against what was drawn, and
until something renders, nothing was.

---

## N89 — A control with no registered theme has no template and renders nothing

The code block editor was invisible: a grey rectangle where `TextEditor` should have been, with no
caret, no text and no error.

**I fixed the wrong thing first, and said so to the client before checking.** Four controls did
genuinely share `Grid.Row="3"` in `InspectorPane.axaml`, and the port-literal list had no
`IsVisible` and was declared last, so it painted over the editor. That was a real defect. It was
not *this* defect. I relaunched without confirming the editor had appeared — my harness was
hanging — and it was still invisible.

The cause: **AvaloniaEdit's control theme was never included**, so `TextEditor` had no template and
rendered nothing at all. One line in `App.axaml`, above `SparkStyles`:

```xml
<StyleInclude Source="avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml" />
```

The precedent was already in that file — the `DockFluentTheme` include carries a comment saying
exactly this about Dock. A third-party control library ships its theme separately from its
assembly, and referencing the package is not the same as registering the theme.

**Two things to carry forward.** First: *nothing renders* is a different symptom from *renders
wrongly*, and it points at the theme, not at the layout. Second, and the expensive one: when a
control is invisible, **look at the running window before claiming a fix**. A plausible defect
found while hunting a symptom is not evidence that it *was* the symptom, and two defects can sit
one behind the other.

**Owed:** this is verified by a person's eyes and by nothing else. The regression test hung in the
headless dispatcher and was deleted rather than left in the suite, which fails AGENTS.md step 7.

---

## N90 — A wrapped, data-bound `TextBlock` inside a `Grid` hangs Avalonia's headless `Window.Show()`

**The symptom.** A test that shows `InspectorPane` with a `MainWindowViewModel` as its data context
hangs. It does not fail and it does not time out with a message: the run sits there until the
harness kills it. This killed `InspectorLayoutTests`, which was deleted rather than left in the
suite, and it is why the `App.axaml` fix in [N89](#n89) was verified by a person's eyes and nothing
else.

**The cause, bisected.** Not the session, the window, the pane's construction, the data context,
the code editor, the port rows, the row `DataTemplate`, a `ToolTip.Tip`, the `Border.pane` style,
or the grid's `*` row — every one of those was eliminated by rendering it on its own. What
reproduces it, outside `InspectorPane` entirely, is this:

```csharp
TextBlock text = new() { TextWrapping = TextWrapping.Wrap };
text.Bind(TextBlock.TextProperty, new Binding("SelectionDescription"));

Grid grid = new() { RowDefinitions = new RowDefinitions("Auto,Auto") };
grid.Children.Add(text);

new Window { Content = grid, DataContext = model }.Show();   // never returns
```

The same `TextBlock` as the window's **direct content** renders and captures normally. Without
`TextWrapping.Wrap`, the grid version renders normally. It is the combination, and it hangs in
`Show()` — before any frame is captured — so **neither `CaptureRenderedFrame` nor a layout
assertion is available**: there is nothing to hook after `Show()` because `Show()` does not return.

**It is a headless-platform limitation, not a Spark defect.** The real application lays these panes
out correctly; every pane in Spark is on screen and readable. Nothing was changed in the product
for this.

**What it costs, stated plainly.** Every properties-pane defect found by a person in this session
was a *rendering* defect — an editor with no theme drawing nothing, four controls sharing one grid
row — and `InspectorPane` wraps text in a grid in four places, so it cannot be shown headlessly at
all. That whole surface is verified by eye.

**What it does not cost.** `GraphCanvas` draws its own text with `DrawingContext` and wraps
nothing, so it shows, renders and hit-tests normally — which is where
`CanvasWidgetGestureTests` lives and why the on-canvas slider and value field *are* covered by
tests that press buttons. A bound `ItemsControl`, `TextBox`, `ComboBox` and the `CodeBlockEditor`
were each shown headlessly during the bisection, so the limitation is narrow and the technique is
available to any control that does not wrap text inside a grid.

**One more finding, recorded because it wasted a cycle.** `CaptureRenderedFrame` returns **null**
in this backend even when it has plainly rendered — the hit-testing tests depend on it having
rendered and get that. So a frame can be *driven* but not *inspected*: asserting on the returned
bitmap is asserting on null.

## N91 — Recolouring a syntax theme must sweep every named colour, not the ones you listed

The code editor was put on Spark's palette by naming twenty-four of AvaloniaEdit's highlighting
colours and remapping exactly those. The reasoning written into the code was that a name it did not
recognise should be left alone, because the `.xshd` belongs to somebody else and is entitled to
change.

**The reasoning was backwards, and a person found out before a test did.** Three names were missed.
Measured against `surface.sunken` at `#1A1E24`:

| Colour | Stock | Contrast |
|---|---|---|
| `StringInterpolation` | `#000000` | **1.26:1** |
| `SemanticKeywords` | `#008B8B` | 4.04:1 |
| `NullOrValueKeywords` | *(inherits)* | — |

So the code inside a `$"{...}"` hole was **black on near-black**, and the first person to type an
interpolated string could not see what they had written. The body-text floor is 4.5:1.

**A name the code does not know is the dangerous one, not the safe one.** It was chosen against a
white page by somebody who has never seen this editor, so "leave it alone" means "keep whatever a
light theme wanted". `EditorHighlightPalette.Apply` now walks
`IHighlightingDefinition.NamedHighlightingColors` and gives anything outside its table
`text.primary` — legible by construction. A colour that sets *no* foreground is still left alone,
and that one is safe: it inherits the editor's, which is `text.primary` too.

**The test had to walk the definition, not the table.** `EditorHighlightContrastTests` enumerates
the named colours and holds every one to 4.5:1. A test that checked the same twenty-four names the
code already maps would have passed while the defect was on screen — it would only have restated
the map. Reverting the sweep makes it name both failures, with their measured ratios.

**And `Apply` has to be idempotent**, because `HighlightingManager` hands out one definition per
language and every code block placed runs this over the same object. Every assignment is absolute
rather than relative, and `ApplyingTwiceChangesNothing` holds it.

---

## N92 — Two traps in writing a number back into a typed port

Both were caught by the same test, and neither is visible by reading the code.

**A conditional whose branches are `int` and `double` produces a `double`.**

```csharp
object typed = port.ValueType == typeof(int)
    ? (int)Math.Round(value)     // looks like it boxes an int
    : value;                     // it does not
```

C# finds the branches' common type first — `double` — converts the `int` straight back, and boxes
that. So an integer slider stored `43.0` in a port declared `int`. It compiles, it evaluates, and
everything downstream that asks the port its type is told `int` while holding a `double`. The fix
is `(object)` on the branch that needs it. **What made it visible was asserting the literal's
runtime type rather than its value** — `Assert.IsType<int>` rather than `Assert.Equal(43, …)`,
which would have passed.

**`Math.Round` is banker's rounding by default.**

`Math.Round(42.5)` is 42 and `Math.Round(43.5)` is 44: adjacent midpoints go opposite ways. That is
correct for statistics and wrong for a slider, where it is a thumb that sometimes sticks and
sometimes jumps for no reason visible to the person dragging it.
`MidpointRounding.AwayFromZero` is what a person means by rounding.

**The general lesson is about the test, not the two bugs.** A test asserting a *value* passes
against a port holding the wrong type, and a test asserting a *type* passes against the wrong
rounding. Both had to be asserted for either to be found, and the second one only surfaced because
the first was fixed first.

---

## N93 — A node key is a display name, not a code path

The node reference now prints the C# a code block would write to call each node. The obvious
implementation reads the key — `Spark.Nodes.Core/Number.Range` is package and name, and the name
already looks like `Type.Member` — and it is wrong for three of the 136 nodes on the first run:

| Key | The member it is |
|---|---|
| `Integer.Slider` | `Number.IntegerSlider` |
| `List.Count` | `ListNodes.Count` |
| `TimeSpan.Components` | `Duration.Components` |

A key is what the library panel shows and what a `.spark` file stores; `[SparkNode(Name = …)]`
sets it freely, and the importer's own naming does not have to agree with the CLR either. **The
`MemberInfo` is the only thing that knows**, and the only place it exists is `NodeImporter` —
`NodeLibrary` keeps definitions. So the example is written at import and carried on the
definition, which is also why it comes free for a third-party package.

**Three wrong examples out of 136 is the worst possible failure rate**: high enough to mislead,
low enough that spot-checking a page or two finds nothing. Compiling all of them is what makes
the difference, and that is `NodeCodeExampleTests` — every example goes through a real
`ScriptNodeFactory` and its inferred ports are asserted against the node's own.

---

## N94 — A code block cannot host a `using` directive, and `Spark.Nodes.Core` must not be imported

Two facts that decide how a generated example has to be written, and the first of them was
written down backwards.

**`ScriptNodeFactory.Wrap` puts the user's script inside a method body.** So a leading
`using Spark.Nodes.Core;` is not a using *directive* at all — it is parsed as a using *statement*
and fails with `Identifier expected` and `You must provide an initializer in a fixed or using
statement declaration`, neither of which names the real problem.
[DYNAMO-COVERAGE §5](DYNAMO-COVERAGE.md) said the opposite — "E6's code block hosts arbitrary
`using` directives written by users" — as half the justification for the `Point` → `Point3d`
rename. The rename stands on its other half, which is FR-60's planar layer; the sentence was
wrong and is now corrected.

**A user therefore cannot add an import, so the imports are the five in
`ReferenceCatalog.DefaultImports`**: `System`, `System.Collections.Generic`, `System.Linq`,
`Spark.Api`, `Spark.Geometry`. Adding `Spark.Nodes.Core` to that list is the obvious way to
shorten every generated example and **it must not be done**: that namespace declares a `Math`,
so importing it makes `Math.PI` ambiguous in every script already written — including the worked
example in `concepts/code-blocks.md`. Generated examples are fully qualified instead, which is
also the only thing that can work for a package whose namespace nobody can predict.

---

## N95 — Roslyn publishes completion as a service and signature help not at all

`CompletionService.GetService(document)` is public, documented and is what `ScriptCompletion`
uses. Its sibling for signature help is not: `ISignatureHelpProvider`, `SignatureHelpItems` and
every provider in `Microsoft.CodeAnalysis.CSharp.Features` are `internal`, reachable only through
`InternalsVisibleTo` on assemblies we are not. There is no public equivalent, and looking for one
costs an afternoon.

**The semantic model answers the same question directly**, and `ScriptSignature` is forty lines of
it: find the innermost `ArgumentList` whose parentheses straddle the caret, ask
`GetMemberGroup` for the expression being called — or the type's `InstanceConstructors` for a
`new` — and count the argument separators before the caret for the active parameter.

**Use the member group, not the resolved symbol, and this is the part that is easy to get
backwards.** Signature help is wanted precisely while a call is *unfinished*:
`Circle.ByCentreNormalRadius(` has no arguments at all, overload resolution therefore fails, and
`GetSymbolInfo(...).Symbol` is null exactly when the popup should appear. `GetMemberGroup` answers
with every accessible overload regardless of whether the call binds, which is also the list the
popup cycles through. `SymbolInfo.CandidateSymbols` is the fallback for the positions where a
*finished* call has an empty member group.

**The caret is one past the character just typed, so the token is looked up at `caret - 1`.**
`FindToken(caret)` on `Foo(` lands past the call entirely; `FindToken(caret - 1)` lands on the
open parenthesis, whose parent is the argument list. And the closing parenthesis is normally
*missing* while typing — a missing token has a zero-width span at the end of the text, so treating
it as the far edge of the list is right in the finished and the unfinished case alike.

---

## N96 — IDE0055 can be true of code that has been in the tree for days and silent until you touch the file

`ScriptCompletion.cs` had a comment block sitting inside a fluent chain, after a blank line:

```csharp
ProjectInfo project = ProjectInfo
    .Create(...)
    .WithCompilationOptions(...)

    // A code block is a *script*, not a compilation unit, and Roslyn has to be told: ...
    .WithParseOptions(...);
```

That is an IDE0055 violation. The build did not say so — not with `--no-incremental`, not with
`-warnaserror`, and `dotnet format --verify-no-changes` did not want to change it either.
**Adding any member to the file made all five diagnostics appear at once**, pointing at lines
nobody had edited, which reads exactly like a change breaking unrelated code. Reverting the
addition made them vanish again; a trivial `public int Foo() => 1;` brought them back.

**So a green formatting gate is evidence about the files that changed, and weaker evidence about
the rest.** The fix here was to hoist the comment above the statement, which is where it belonged.
The thing to remember is the diagnosis: *formatting errors on lines you did not touch, in a file
you did touch, are probably older than your change* — read them before assuming your edit caused
them, and do not "fix" your own new code to make them go away.

---

## N97 — Multiple carets on AvaloniaEdit: anchors, one update, and the two things that stay single

AvaloniaEdit has one caret and no multi-caret support at all, and eight of VS Code's fourteen
Selection commands are about having several. The layer that makes them work is smaller than it
looks, and it rests on three facts.

**`TextDocument.CreateAnchor` is what makes it tractable.** A secondary caret is a pair of
`TextAnchor` — the selection anchor and the caret — and the document moves both through every
edit, ours or AvaloniaEdit's own. Set `SurviveDeletion = true` so a caret whose text is deleted
collapses to the deletion point instead of vanishing, and `MovementType = AfterInsertion` so
typing at a caret leaves it after what was typed rather than before it. Storing plain offsets
instead works right up until an edit arrives from a path you did not write.

**Edit ascending, carry one delta, and wrap the lot in `BeginUpdate`/`EndUpdate`.** In document
order every later offset is stale by exactly the length the earlier edits changed, which is one
number rather than a re-sort per edit. The single update is not cosmetic: without it Ctrl+Z undoes
a five-caret edit five times, which makes the feature a trap rather than a convenience.

**The editing path only diverges when there is more than one caret.** With one caret every
keystroke goes to AvaloniaEdit exactly as before. The alternative — always routing text input
through our own code — trades the common case against the rare one.

**Two things deliberately stay single.** The *primary* caret is still AvaloniaEdit's own, so its
selection, its blinking and its scrolling are unmodified; the extras are drawn by a background
renderer on the `Selection` and `Caret` layers and do not blink. And any key the multi-caret path
does not understand — a word jump, a page, a keyboard selection — **drops the extra carets** rather
than guessing, because a wrong guess at several carets is several wrong edits at once.

---

## N98 — A popup that is clipped to its pane has to be pulled back inside it

`CodeBlockEditor` draws its completion list on a `Canvas` over the editor rather than in a
`Popup`, because the headless session every UI test runs in has no window overlay layer
([N47](NOTES.md)). The cost of that trade was known — the list is clipped to the pane instead of
to the screen — and the consequence was not noticed until the signature popup was photographed:
the caret was at the end of a long line in a properties pane about 280 px wide, so **both popups
were a two-pixel sliver at the right edge**. On screen it read as a rendering fault; in the tests
it read as nothing at all, because headless drawing has no font metrics and every glyph measures
zero wide, which is precisely the axis that was wrong.

Three things fix it, and none of them is exotic: measure the frame and clamp its origin into the
control's bounds; hang the signature *below* the caret's line when there is no room above it, with
the completion list moved down to clear it; and give the signature `MaxWidth` the pane's width with
`TextWrapping="Wrap"` — **inside a `DockPanel`, not a horizontal `StackPanel`**, because a stack
measures its children with infinite width and wrapping never takes effect.

**The lesson is about the harness rather than the popup.** A headless test can assert the vertical
placement, which is why the scroll-offset subtraction has been guarded since the M1.5 spike; it
cannot assert the horizontal one at all. Anything that depends on measured text width is verified
by photographing it — which is what `--code-block` and `--code-block-command` exist for.

---

## N99 — A code block's output type is inferable, and the disk cache has to carry it

Every code block output port was `typeof(object)`, whatever the script returned. That is not a
cosmetic gap: `object` into a port declared `Curve` is a **narrowing**, and `TypeCompatibility`
refuses narrowing when the wire is drawn — deliberately, so a downcast is a node on the canvas
rather than a silent cast inside a wire. So a block returning a `Circle` could not be connected to
anything that wanted a curve, while its own watch displayed the circle. A user found it by trying
to draw the wire.

**The type is already known at the only moment it is cheap to ask.** The generated frame's `Run`
returns `object` — the invocation contract requires it — but the *expression* in the user's
`return` has a natural type, and the compilation that is about to be emitted has a semantic model.
`ScriptOutputTypes.Infer` reads it: the return statements whose nearest enclosing **function** is
`Run` (a `return` inside a lambda belongs to the lambda), one distinct type or nothing, and one
element per port for a tuple return.

**Mapping an `ITypeSymbol` to a `System.Type` is where the care goes, and the rule is: when in
doubt, `object`.** A port typed *wrongly* is worse than a port typed `object`, because it refuses
wires that should be legal and names a type the user never wrote. So an error type, `dynamic`, an
anonymous type, a pointer, a nullable value type (`double?` is not assignable from `double`, which
would refuse the very wire it was inferred for) and any type from an assembly this process has not
loaded all come back as `object`. Named types are resolved through **loaded assemblies** rather
than through the reference files, because a type loaded twice from one file is two types to
`IsAssignableFrom` — the same trap the same-name rule in `TypeCompatibility` exists to explain.

**The disk cache had to change, and that is the part that is easy to miss.** Inferring the type
needs the compilation that `E6-T10`'s cache exists to skip. Without storing it, a port would be
`Circle` in the session that compiled the block and `object` in every session that reopened the
file — a wire that works until you close Spark. So `CachedScript` carries the output ports,
`GeneratorVersion` went to 2 so older entries are ignored, and the `.outputs` file is written
*before* the `.ports` file, which stays the marker that says an entry is complete. Port **names**
still come from the syntax on a cache hit; only the types come from the entry, and an entry whose
port count disagrees with the script is discarded rather than trusted.

---

## N100 — A detach is not a death: re-parenting disposes what a control expected to keep

Dragging the viewport pane out of its dock exited the application. The stack ends in
`Dispatcher.MainLoop`, which is what an exception thrown *inside rendering* looks like:

```
System.ObjectDisposedException: Cannot access a disposed object.
Object name: 'Spark.Viewport.Software.SoftwareViewportRenderer'.
   at SoftwareViewportRenderer.Initialise()
   at ViewportControl.DrawSoftwareFrame(DrawingContext)
   at ViewportControl.Render(DrawingContext)
   at Avalonia.Rendering.Composition.CompositingRenderer.UpdateCore()
```

**Docking re-parents a control**, and re-parenting is a detach followed immediately by an attach.
`ViewportControl.OnDetachedFromVisualTree` disposed the CPU rasteriser, on the reasonable-sounding
grounds that a control is not `IDisposable` and a detach is the only hook there is. But a disposed
rasteriser is disposed for good: `Initialise` starts with `ObjectDisposedException.ThrowIf`, so the
first frame after the re-attach threw — and a `Render` override runs on the compositor's dispatch,
where **there is no handler anywhere above it**. The application does not report an error; it
stops.

**The fix is one word: replace rather than dispose.** The field stops being `readonly`, and the
detach hands back a fresh renderer. The GL path never had the bug because it already worked this
way — `OnOpenGlDeinit` nulls the renderer and `OnOpenGlInit` builds another.

**Two general things worth keeping.** (1) `OnDetachedFromVisualTree` is a *transition*, not a
destructor; anything released there has to be re-creatable, because docking, tab switching and
virtualisation all re-attach. (2) An exception in `Render` is fatal in a way an exception in a
click handler is not, so the render path deserves the same suspicion as a background thread.

**And why no test caught it:** the headless session has no OpenGL, so a viewport in a test is
still *waiting for a context* when it draws and never reaches the software branch at all. Setting
`ForceSoftwareRenderer` is what puts it into the state a real machine is in immediately after a
re-dock — GL de-initialised, software drawing until a new context arrives — and with that one line
the crash reproduces in a test in under a second.

---

## N101 — Dock rebuilds the tree as you drag, so a layout command that edits the old one edits nothing

A user dragged all four panes around, ended up with an empty window, and *View → Reset layout* did
nothing at all.

**`SparkDockFactory` recorded the `Tool` and `ToolDock` objects it built** and every later
operation — the presets, the visibility toggles, `Reset layout` — went through those records. That
is correct exactly until somebody drags a tool: Dock **removes a `ToolDock` from the tree when its
last tool leaves**, and makes a new one wherever the tool is dropped. The recorded docks are then
orphans, still perfectly valid objects, no longer attached to anything. Setting `Proportion` on
them succeeds and changes nothing anybody can see, and `RestoreDockable` puts a tool back into an
owner that is not in the tree — which is *worse* than an error, because the command reports
success.

**So the recovery command has to rebuild rather than adjust.** `Reset layout` now raises a distinct
event, the window builds the layout again from the same four pane controls and assigns it to the
`DockControl`, and the factory closes any floating windows the dragging produced first — they hold
panes, and a rebuilt shell that left them open would be showing the same controls twice.

**Two details that are easy to get wrong.** The pane controls must be *the same instances*: they
hold the canvas, the viewport's scene and the text the user is part-way through typing, and a fresh
set would recover the layout by discarding the work. And the old layout has to be dropped
(`Layout = null`) before the new one is assigned, because a control cannot be in two visual trees
at once.

**Nothing about the dock arrangement is persisted**, which is why the immediate workaround was to
restart: `WorkspaceLayout` has `ToJson`/`FromJson` and nothing calls either. That is worth knowing
before somebody adds persistence — a saved *broken* layout would turn a restart from the escape
hatch into the trap.

---

## N102 — A release workflow that builds native code has to install the native dependencies itself

`v0.1.0` tagged, built, tested, formatted — and then stopped at *Build the native provider*, so no
installer was packed, `gh release create` never ran, and the release page had nothing on it but the
source archives GitHub generates on its own.

**The cause is a difference between a developer machine and a hosted runner that reads as no
difference at all.** `release.yml` called `scripts/build-native.ps1` bare, exactly as a person does
here. The script looks for vcpkg in `VCPKG_ROOT`, then `C:\dev\vcpkg`, then `C:\vcpkg` — and a
`windows-latest` image *has* vcpkg at `C:\vcpkg`, with **no ports built in it**. So the script found
a vcpkg, and OpenCascade was not in it.

`ci.yml` had this right from the beginning: compute a cache key from the port manifest, restore or
`vcpkg install opencascade:x64-windows`, then call the script with
`-VcpkgRoot $env:VCPKG_INSTALLATION_ROOT`. The release job simply never got the same three steps.

**Two things worth keeping.**

*The failing step was one nobody would have chosen to test.* Everything in that workflow up to it
was exercised by CI on every push; the native build was exercised by CI's own job, in CI's own
environment, with CI's own preparation. The release job's copy of it had never run anywhere.
**A step that exists in two workflows is two steps.**

*The fix is a new tag, never a moved one.* `v0.1.1` is the release that ships; `v0.1.0` stays where
it is, pointing at a commit whose workflow failed, because a machine that already fetched a tag
keeps whatever it fetched.

**And why the two jobs are still copies rather than a shared composite action:** they are allowed to
diverge — CI measures the payload against `E13-T17`'s budget, the release job ships it — and a
shared action that silently changed both is a worse failure than two files somebody has to keep in
agreement by reading them.

---

## N103 — A build script that asks for `cmake` and `ninja` has not asked whether the compiler is the right one

`v0.1.1` failed, and so did every CI run behind it, on a link that named every OpenCascade symbol
the shim uses:

```
undefined reference to `OSD::SetSignal(OSD_SignalMode, bool)'
undefined reference to `Message::DefaultMessenger()'
```

`TKernel.lib` contains both, and it was on the link line. **The compiler was wrong, not the
linking.** CMake had selected MinGW — `C:/mingw64/bin/c++.exe`, GNU 15.2.0 — and vcpkg's
`x64-windows` triplet builds OpenCascade with MSVC. The two mangle C++ names differently, so GNU
`ld` searched a library that holds `OSD::SetSignal` for a symbol spelled another way and did not
find it.

**This failure impersonates a missing `target_link_libraries`,** which is what makes it worth a
note. `find_package(OpenCASCADE CONFIG REQUIRED)` and the `target_link_libraries` call had both
been in `native/spark_occt/CMakeLists.txt` since the shim was written. Three tells separate the two
diagnoses, and all three are in the log:

| | Wrong compiler | Genuinely unlinked |
|---|---|---|
| Message | `undefined reference` (GNU) | `unresolved external symbol` (MSVC) |
| Flags | `-shared -Wl,--out-implib` | `/DLL /IMPLIB` |
| Reporter | `ld.exe` | `link.exe` |

**Why `scripts/build-native.ps1` did not catch it.** It required `cmake` and `ninja` on `PATH` and
said *"Open a Visual Studio developer prompt"* when either was missing. A developer does open one,
so `cl` is there and CMake picks MSVC. A `windows-latest` runner ships `cmake`, `ninja` **and**
MinGW at `C:\mingw64`, and no MSVC environment — so the check passed on two of the three tools it
actually needed and the third was assumed. **A guard that is satisfied by the wrong thing is not a
guard.**

The fix is that the script enters the MSVC environment itself — `vswhere` to find the install,
`vcvars64.bat` imported through `cmd /c "call ... && set"`, which is the only supported way to read
what a vcvars script did — rather than requiring the caller to have done it. It then behaves the
same from a developer prompt, a plain shell and a runner, and `-DCMAKE_CXX_COMPILER=cl` makes a
MinGW earlier on `PATH` unable to win even when `cl` is present.

**And a stale `CMakeCache.txt` will hide all of this from you.** Re-running the script on a
developer machine kept passing, because the build directory already recorded `cl.exe` from an
earlier developer-prompt run and CMake does not re-detect a compiler it has cached. The fault only
reproduces after `Remove-Item -Recurse artifacts/native/build-release` — which is what a runner
does every time by starting empty.

**The cost was in the ordering, not the bug.** `Build the native provider` ran *after* `Install
OpenCascade`, so a compiler fault that takes seconds to detect was discovered three hours in, on
every one of the eight runs. All three workflows now run `build-native.ps1 -CheckToolchain` before
the dependency install. **Put the cheap check that can fail before the expensive step that cannot
fix it.**

---

## N104 — A node measured from its port *names* is too narrow for its port *tabs*

`Math.Divide` drew its output type label `number` four pixels inside the `result` tab it sits
beside. Two independent faults produced that, and the first one hid behind the second.

**The row's right-hand edge came from the wrong geometry.** `DrawPortLabels` derived it from the
width of the output's *name text*:

```csharp
FormattedText name = LabelRun(node.Outputs[row].Name);
rightStart -= name.Width;
```

But a name is drawn inside a lozenge with `PortTabPadding` either side of it, so the tab reaches
8 px further left than the word does. The type was then placed clear of the word and painted over
the tab. The **input** branch three lines above had always been right — it asks `PortTab` where the
lozenge ends. The two sides of the same row disagreed about what a port is.

**And that block drew the output name a second time.** `DrawPortTab` already draws it, for inputs
and outputs alike. Every output name in the graph was painted twice, at two slightly different
x — the tab right-aligns on `PortTabTextInset`, this right-aligned on `PortLabelInset`. Both are
9 and 8, so it read as a faint bold rather than as double vision.

**The width estimate had the same blind spot.** `SideWidth` measured `name.Length * PortCharWidth`
where `PortTab` computes `name.Length * PortCharWidth + 2 * PortTabPadding`. Every side was
16 px short and every row 32, so `Math.Divide` was measured at 168 and wanted 194. Under the old
renderer that surfaced as an overlap; under a correct one it would surface as the second type
label being silently dropped, which is the failure mode that never gets reported.

**The shape of the lesson.** *Anything that measures a thing must be written against the same
geometry that draws it.* `PortLabelRow` now exists on `CanvasNode` and asks `PortTab` for both
edges, so the renderer cannot reach a different answer than the tabs do; `SideWidth` mirrors
`PortTab`'s formula and says so. Estimating is still fine — [N24](#) explains why the canvas
cannot measure text off the render thread — but estimating a *different quantity* than the one
drawn is not estimating, it is guessing.

---

## N105 — A panel filled on selection and never refreshed is a panel that lies

The properties panel showed `16.83` in the `value` box while the node beside it read `31.79`, and
its *Output* line said `14.16` while the canvas bubble on the same node said `31.79`. Nothing had
gone wrong with the slider: the panel had simply never been told.

`ShowSelection` builds a `PortLiteralViewModel` per input from `instance.Literal(index)` and sets
`WatchRank`/`WatchText` from the node — **once**, when the selection changes. `RefreshInspector`
runs after every evaluation and did this and only this:

```csharp
foreach (PortLiteralViewModel editor in Inspector)
{
    if (editor.Slot >= 0 && editor.Slot < _graph.Nodes.Count) { continue; }
    Inspector.Clear();
    ...
}
```

It pruned rows whose node had been deleted. It read nothing back. So every path that changes a
literal *without going through the panel* left it stale — a slider drag, an undo, a redo, the
in-place field on the node itself. The slider is merely the one that does it sixty times a second
in front of you.

**Refresh by reading, not by being told.** `RefreshInspector` now re-reads the literals out of the
engine for the selected slot and re-derives the watch lines. The alternative — having every editing
gesture notify the panel — is a longer road to the same place with one more way to miss a case
each time somebody adds a gesture.

**Two things it must not do, and both are why this is not a two-line change.**

*It must not commit.* `PortLiteralViewModel.Show` assigns `Text` and returns; it never calls
`_commit`. Committing on refresh would turn every evaluation into an edit and put one undo entry
per pixel of slider travel on the stack — precisely what `GraphEditedEventArgs.RecordsUndo` exists
upstream to prevent.

*It must not overwrite typing.* A run landing between two keystrokes would otherwise take the
half-typed number away. `IsEditing` is set by the pane on `GotFocus` and cleared on `LostFocus`,
and `Show` returns false without touching a box that has the caret. **A view model cannot see
focus, so the view has to say so** — there is no way to infer it from the model alone.

---

## N106 — Roslyn tells you which candidate it expects, in a field next to the one you are sorting by

Typing `Point2d p2d = new ` offered `AccessViolationException`, `Action`, `Action`, `Activator`,
`AggregateException`, `Angle` — an alphabet. `Point2d` was in the list, a long way down it, and Tab
inserted an exception type.

`CompletionList.ItemsList` arrives ordered by `SortText`, which is alphabetical for types. **What
Roslyn knows about relevance is not in that order — it is in `item.Rules.MatchPriority`.** For a
target-typed expression the expected type is marked `MatchPriority.Preselect` (100) against a
default of 0, and a real IDE applies it. `ScriptCompletion` projected `DisplayText`, `Kind` and
`SortText` into its own record and never read `Rules`, so the signal was fetched and discarded
one line before it was needed.

```csharp
.OrderByDescending(item => item.Rules.MatchPriority)
.ThenBy(item => item.SortText, StringComparer.Ordinal)
```

**Where the ordering had to live is the interesting half.** `ScriptCompletionItem` deliberately
carries nothing but strings — [C5](../tests/Spark.UI.Tests/CodeEditorSpikeTests.cs) asserts it
structurally, so that no Roslyn type crosses into `Spark.UI` (ADR-0005). Adding `MatchPriority`
to the record and sorting in the editor would have broken that test, and rightly: **which
candidate is likeliest is a language question**, and language questions stay behind
`Spark.Scripting`. Sorting before the projection keeps the boundary and needs no new field.

**A list that is right but ordered wrong is a list that is wrong.** The candidate was always
there. Completion is a one-keystroke feature — the value is entirely in what Tab does, so ranking
is not a refinement on top of membership, it *is* the feature.

---

## N107 — A Spark code block is a method body, so most of a Visual Studio snippet set cannot go in it

Porting RCS's 36 C# snippets across, 19 of them insert code that cannot compile here, and the
reason is one line in `ScriptNodeFactory.Wrap`:

```csharp
source.AppendLine("public static class Block {");
source.AppendLine("public static object Run(object[] __in, CancellationToken __token) {");
```

**The user's text is a method body, not a script.** C# does not allow a type, a namespace, a
property, an indexer, a constructor, a finalizer or a member method to be declared inside one — so
`class`, `struct`, `interface`, `enum`, `namespace`, `ctor`, `~`, `attribute`, `exception`,
`prop`, `propfull`, `propg`, `propi`, `indexer`, `equals`, `iterator`, `svm` and `sim` are all
out. `unsafe` goes with them because `AllowUnsafeBlocks` is off, and `cw` reached RCS's console,
which Spark has no equivalent of: a block's output is what it returns.

**A snippet that inserts an error is worse than no snippet**, because the user has to work out
that the tool was wrong rather than their code. Sixteen survive — the control-flow and resource
ones — plus `ret` and `lf`, which are what `cw` and `iterator` would have been if they had been
written for a method body.

**The claim is checked, not asserted.** `ScriptSnippetTests` parses every shipped snippet inside
the wrapper and requires no syntax errors, and parses seven of the nineteen and requires that
there *are* some. Syntax rather than semantics, deliberately: the fields expand to placeholders
like `condition` that nothing declares, so binding was never the question — whether the construct
is *allowed* in a method body is, and the parser answers exactly that. If C# ever gains local
classes, the negative test fails and the catalogue can grow.

**A related thing this turned up and did not fix.** `ScriptCompletion` parses with
`SourceCodeKind.Script` while the compiler wraps in a method body. Completion will therefore offer
things the compiler rejects, which is the exact failure `E6-T13` says is worse than no list —
"a completion list which disagrees with the compiler". Nobody has hit it because the offer has to
be something only legal at script top level. Worth closing before somebody does.

---

## N108 — `ReferenceCatalog.Fingerprint` says "stable across runs" and moves with the process's load order

The on-disk compile key carries it:

```csharp
_references.Fingerprint,   // ScriptNodeFactory.DiskKey
```

and its own summary promises *"A hash of the references themselves, stable across runs"*, with
`ScriptAssemblyCache` explaining that this is precisely why the disk key cannot use `Version` —
a per-process counter "would let two different sets of references share a cache entry across
runs". The reasoning is right. The value does not deliver it.

**A catalogue is built from what the process has loaded**, and `Add`'s own summary says so in
passing: *"rebuilding the snapshot also picks up assemblies the process has loaded since the last
one."* So the fingerprint is not a property of the references a caller asked for — it is a
property of **when** the catalogue happened to be built.

**How it surfaced.** `ScriptOutputTypeTests.AnEntryWithNoTypesFallsBackToObject` passed when run
with its class and failed when run alone, and CI went red on three pushes out of six with nothing
relevant between them. The test builds a factory, compiles — which loads Roslyn and the geometry
kernel — then builds a second factory. The second catalogue lists more references than the first,
fingerprints differently, and misses the cache entry the first one wrote. It recompiles, re-infers,
and answers `double` where the test asks whether a missing `.outputs` falls back to `object`. Run
after a sibling the process is already warm, both catalogues agree, and it passes.

**The test is fixed by sharing one catalogue**, which is also what the application does — a
session builds it once. **The contract is not fixed by that**, and the question it leaves is worth
asking directly: should the fingerprint cover the references a caller *declared*, rather than
every assembly the runtime happens to have loaded? As written, two runs of the same application
that load assemblies in a different order have different fingerprints and share no cache entries —
which is the reopen `E6-T10` exists to make fast.

**The shape.** *A value documented as stable, derived from something that is not.* The doc comment
was checked by review and the derivation by nobody, and the two drifted in the only direction that
is invisible: the cache still returns correct answers, just far less often than anybody believes.

---

## N109 — Port the policy, not the control: RCS's find bar against AvaloniaEdit's

RCS's `FindReplacePanel` is 593 lines — a `Border` subclass, a hand-built toolbar, a highlight
renderer and an `AdornerHost` — and it exists because **AvalonEdit's own `SearchPanel` finds but
cannot replace**. That is a good reason to write one in WPF.

It is not a reason to write one in Avalonia. AvaloniaEdit's `SearchPanel` has `IsReplaceMode`,
`SearchPattern`, `MatchCase` and `UseRegex`, and Avalonia has **no adorner layer** to float a
hand-built panel over — so a faithful port would have meant reimplementing a control that ships in
the box, on top of a hosting mechanism that does not exist, to reach a feature the box already has.

What was worth porting was everything around it: **Ctrl+F, Ctrl+H, and seeding the box from the
selection.** That last one is the whole of "select a word, press Ctrl+F"; without it the bar opens
empty and the word is typed twice, which is the difference between a shortcut and a dialog. A
multi-line selection deliberately does not seed it, because a find box is one line.

The same judgement applied to `EditorZoom`. RCS holds the text size in a static with a `Changed`
event, because several editor tabs share one preference and Ctrl+wheel over any of them resizes
all. **A Spark code block is one editor in a properties panel and there is never a second to keep
in step**, so the state is on the control and the event does not exist. The step-rather-than-factor
choice was kept, and that one is not incidental: a multiplier moves one point at the small end and
four at the large, so the same gesture feels different depending on where you started.

**The rule this file is really about.** Three things were ported here — the gestures, the seeding,
the step — and two were dropped: the panel and the static. **What travels between two applications
is the decision somebody made, not the code they wrote to carry it out**, and a port that cannot
tell the difference reimplements the second framework's built-ins in the first framework's idiom.

---

## N110 — Two bugs that could not be seen until a code block had eight output ports

`E6-T26` made every variable a code block declares into an output port, which is Dynamo's rule.
It is a small change — a naming rule and a generated `return` — and it exposed two defects that
had been in the tree since `E6-T8`, neither of which anything could have hit before.

**A `ValueTuple` holds seven fields and nests the rest.** `Unpack` split the returned tuple by
reflecting for `Item1`, `Item2`, … `ItemN`. That is right up to seven and wrong immediately after:
an eleven-element tuple is `ValueTuple<T1…T7, ValueTuple<T8…T11>>`, there is no `Item8` on the
outer one, and `GetField` returns null rather than throwing — so ports 8 to 11 came out null and
**nothing said anything**. The named-tuple syntax `E6-T8` gave users made eight ports possible in
principle; writing eight of them by hand is unusual enough that nobody had. Reading eleven lines
as eleven ports makes it the *first* thing a user does. The fix walks `Rest`.

**A method that returns `object` and contains no `return` is `CS0161`.** Every code block was
wrapped in `public static object Run(...)` and nothing appended a return — so a script that did
not write one did not compile. That had been true since the first code block; what hid it is
where Roslyn puts the diagnostic. `CS0161` is reported against the **method declaration**, which
is in the generated frame, and `ScriptSourceMap.UserLine` deliberately maps a frame position to 0
and drops it rather than blaming the user's first line — the reasoning is written out on
`UserLine` itself, and it is right. So `Diagnose` reported nothing, the editor drew no squiggle, and the
failure surfaced only as *The script did not compile* when something asked the node for a value.
`E6-T18` had recorded the opposite in `TASKS.md` — "an empty script is legal: zero inputs, one
`result` output" — and the ports really were right; it was the assembly that was never emitted.

**What the two have in common is worth more than either.** Both are silent, and both are silent
for a defensible local reason: `GetField` returning null is how reflection says *not present*, and
dropping an unmappable diagnostic is the rule that stops a user being sent to a correct line. A
defensible silence is still a silence, and it survives exactly as long as nothing exercises the
path. The thing that found both was not a review — it was one screenshot with eleven ports in it.

---

## N111 — `Opened` fires before the first layout, so a screen rectangle taken there is a world one

`E8-T39` puts a real code editor over the rectangle the canvas drew a block's source in. The
canvas is immediate-mode and hosts nothing, so the pane above positions the editor in **screen**
coordinates, and the canvas supplies them by running the node's world rectangle through its pan
and zoom.

The screenshot switch that photographs this opened the editor from `Window.Opened`, and
photographed it in the top-left corner of the pane. The second attempt posted the call to the
dispatcher and photographed the same thing. The third used a 500 ms timer and photographed no
editor at all, because the shutter is not on a clock either.

**`Opened` fires before the first layout pass.** The canvas's `Bounds` are `0, 0, 0, 0`, so the
`ZoomToFit` beside it computes a fit into no space and leaves the transform alone — and the
transform starts as the identity. `ToScreenX` on an identity transform is `x`. Every number was
therefore correct, arithmetically: the editor was placed at the node's **world** position, which
for a graph seeded near the origin is the top-left corner. A world rectangle wearing a screen
rectangle's name looks exactly like a placement bug and is not one.

**The fix is to wait for a view rather than for a moment.** The pose subscribes to
`LayoutUpdated`, does nothing until `Bounds.Width > 0`, and re-places only when the zoom, the
offsets or the width have actually changed since last time — because positioning a control inside
a layout pass raises the next one, so re-placing unconditionally is a loop that never settles.

**The general rule, which is the reason this is written down:** any code that converts world
coordinates to screen coordinates has a precondition that the view exists, and an identity
transform satisfies the *types* while satisfying nothing else. A transform that has never been
told the size of its viewport should be treated as unusable rather than as a transform that
happens to be 1:1.

---

## N112 — One keystroke, two text changes, and three verifications that all missed it

Typing `(` in a code block produced no signature help, in the properties pane as much as on a
node, from the day bracket completion landed. The mechanism is worth stating exactly, because
every part of it is behaving correctly on its own.

**The user types one character and the document changes twice.** The `(` goes in, and
`TextChanged` fires with the caret between the parentheses — the trigger rule sees `(` and starts a
signature request, which is right. Then `OnTextEntered` inserts the matching `)`, and *that* raises
`TextChanged` again while the caret is still after the closer it just added. The trigger rule sees
`)`, which is also a signature trigger, and starts a second request. The second request cancels the
first — deliberately, because a stale popup is worse than none — and asks from a caret that is
outside the argument list. Roslyn answers nothing, correctly. The popup closes.

The caret is then put back between the pair, which is not a text change, so nothing asks again.
The fix is to ask again there.

**Now the part that matters more.** This feature had three verifications and none of them could
have caught it:

- **`E6-T22`'s acceptance was a pose.** `PoseCodeEditor` calls `RequestSignatureAsync` directly, so
  it proves the popup can be filled and placed. It says nothing about what opens it.
- **The screenshot switch is the same pose**, so it inherits the same blind spot and looks like
  independent evidence.
- **Every editor test types through a helper that writes into the document.** A document write
  raises `TextChanged` and never `TextEntered`, so no test in the file had ever run bracket
  completion — the feature and the defect were both invisible to the suite.

**And a fourth thing, which nearly made the regression test useless too.** The obvious signature
stub answers with a signature whatever it is asked. A test built on it passes whether or not the
request was made from the right caret, because the wrong request gets an answer too. The stub had
to be taught the one thing the real service does that matters here — **no help for a caret outside
the parentheses** — before the test could fail.

**The rule:** a feature verified only by asking for it directly has been verified as a mechanism
and not as a behaviour, and a test double that never says no cannot test the code that handles no.

---

## N113 — CI results never needed `gh`, and this file said they did four times

The journal has recorded *needs `gh` or a paste* as a blocker four times: the first nightly, then
`Release #2`, then `Release #3`, then `Release #4`. `gh` is not authenticated on this machine and
there is no token, so every one of those ended unread rather than reported, and the release step
was left half-finished each time on that basis.

**It was never true.** Spark's repository is public, and GitHub's REST API answers a public
repository's workflow runs and releases with no credentials at all:

- `https://api.github.com/repos/<owner>/<repo>/actions/runs?per_page=6` — every run's `name`,
  `display_title`, `status`, `conclusion` and `html_url`.
- `https://api.github.com/repos/<owner>/<repo>/releases/latest` — the published release, its
  `draft` and `prerelease` flags, and every asset with its size.

That is enough to answer all three questions the journal kept deferring: did the run finish, did
it pass, and is there an installer attached to the tag. `gh` is a convenience over the same
endpoints; what it adds is authentication, which is exactly what these calls do not need.

**Why it went unexamined for so long is the part worth keeping.** The first session tried `gh`,
got `gh auth login`, and wrote down *needs credentials* — which is true of `gh` and was then
copied forward as though it were true of the goal. Three later sessions inherited the sentence and
none re-derived it. **A blocker recorded as a tool failure rather than as a question is one nobody
re-tests**, because the note answers "have we tried?" instead of "what do we actually need?"

The rule: write a blocker as the thing that is unavailable, not as the command that failed.

---

## N114 — Three ways a screenshot lies, and all three said the feature was fine

`--screenshot` is listed in this journal's *Verify with* row. Four steps running were checked with
it and it showed none of them. The Roslyn race the queue item blamed was real and was fixed; it
was one of four faults, and the smallest.

**1. The window has no size when the capture path runs.** `Opened` fires before the first layout.
`Canvas.Bounds` is `0,0,0,0`, so `ZoomToFit` fits into nothing, `CentreOn` centres against a zero
viewport, and any world-to-screen conversion returns the world coordinate unchanged — the identity
transform satisfies the types and nothing else ([N111](#n111--opened-fires-before-the-first-layout-so-a-screen-rectangle-taken-there-is-a-world-one)).
**`UpdateLayout()` does not fix this**, which is the part worth writing down: the platform has not
sized the window yet, so asking it to measure measures nothing. You have to *wait* for a layout,
not ask for one.

**2. `RenderTargetBitmap.Render` does not run layout.** A control made visible a moment earlier has
never been measured or arranged: its `Bounds` are `0,0,0,0` and it draws nothing — while
`IsVisible` is true, its position is set, and every property a test could assert says it is open.
That is the worst shape a bug can take, because the evidence and the image disagree and the
evidence is more convenient to read.

**3. Two requests, and the second cancels the first.** The pose was asked for from a layout handler
*and* awaited in the capture path. Each completion request cancels the one before it, so the
fire-and-forget one issued later left the list closed at the moment of the shutter — and the
awaited call, which looked like the careful fix, was awaiting work that had already been
superseded.

**4. And the pose is not the behaviour** ([N112](#n112--one-keystroke-two-text-changes-and-three-verifications-that-all-missed-it)).
Asking the language service directly photographs a mechanism. Typing photographs what a user gets.

**What they have in common.** Every one of them produces a *plausible* image — a real window, real
nodes, the right graph — with the subject absent. A capture that failed would have been noticed on
the first run. A capture that succeeds and omits the thing it was taken for gets pasted into a
journal entry as evidence. **A verification step that cannot fail is worse than no verification
step**, and the tell is that it has never once been red.
