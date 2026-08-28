# Spark — Implementation Notes

Non-obvious implementation facts, numbered. Adopted from DoodleSharp's convention.

**Last updated:** 2026-08-28

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

## N29 — A row can be `Done` and half built, and only using the feature finds the other half

`E5-T7`, *descriptions from the sidecar XML documentation file*, was marked `Done` with the note
"any library shipping its `.xml` gets tooltips free". It read `<summary>` and nothing else.
`XmlDocumentation` had **no `<param>` support at all**, so a port's description could only ever
come from an explicit `[NodePort(Description = …)]` attribute — and almost nothing carries one,
because the XML comment is already compulsory on `Spark.Nodes.Core` under CS1591 and an author who
has written `<param name="centre">The centre.</param>` reasonably assumes that is the description.

Every one of those tags was written, shipped in the `.xml` beside the assembly, and ignored.

**Nothing in the repository could have caught it.** The row was ticked, `FR-25` was written in the
PRD, a test asserted that `Point.Origin` had a description — and it did, from `<summary>`. A
half-built feature and a built one look identical from the outside if the only thing you check is
the half that works. What found it was building the port tooltip §7.2 asks for and watching it
render `centre — Point3d` with nothing after it.

The fix reads `<param>` and `<returns>`, keeps the attribute winning where an author wrote one —
that text is aimed at a graph author, where the XML comment is aimed at a C# caller — and covers
constructors on the same path. The tests that keep it honest assert a **proportion** of the
library's ports rather than one node's, because a single-port assertion would pass with methods
wired and constructors, `out` parameters or receiver ports broken.

The general lesson is the one this project keeps relearning from a different direction: a register
records intent, and only using the thing tells you what the intent missed.

---

## N30 — Character counts size boxes; only measured text may fill them

[N24](NOTES.md) records that a node is sized from character counts because it is built off the
render thread with no typeface to measure against. The result strip repeated that estimate one
step too far: it *truncated* its value lines at forty-four characters as well.

A count is a fixed number against a variable width. Forty-four narrow characters fitted; forty-four
digits did not, so a list like `(5.388942295416207, 0.8793495662309033, 0)` was written straight
out through the right-hand border of its own box. The strip looked correct on the demo graph,
whose values are short, and wrong on the first graph with real coordinates in it.

**The rule the two notes make together:** an estimate may decide how big a box is, because nothing
better is available when the box is made. Only a measurement may decide how much text goes in it,
because by then the renderer knows. `FormattedText.MaxTextWidth` with `CharacterEllipsis` is the
measurement, and the strip now also widens to fit its values so that the ellipsis is rare rather
than routine.

**One implementation detail is worth knowing before changing this.** The fitted runs are cached
under *width and text together*, not constrained in place. `FormattedText` is mutable and the run
cache hands the same object to every caller of the same string, so setting `MaxTextWidth` on a
cached run would leave a port type label ellipsised because a preview headline elsewhere happened
to read the same. That defect would appear on one node in one graph and in no test — headless
drawing is a stub and measures nothing — so it is designed out rather than guarded against.

---

## N31 — A benchmark printed a sample size it had not measured over

`--canvas-benchmark 600` discards a sixth as warm-up and prints `frames=500`. The distribution
printed on the line beneath it — median, p95 and the implied frame rate — came from
`GraphCanvas.Frames`, which is a `FrameTimer` built with its **default 120-frame window** because
that is what the on-screen readout wants.

So the header said 500 and the statistics described the last 120. The two numbers had never
agreed, and nothing said so: a ring buffer does not complain about being overrun, it just forgets.
That is the whole failure — not a wrong formula, but a right formula over a silently smaller
sample than the line above it claimed.

**It is not a rounding difference.** The zoom sweep is deterministic and the tail of it is not
representative of the middle: on the run that found this, the tail-only window read **1.70 ms
median** where the whole 500 frames read **1.15 ms**. Which direction the bias runs is a property
of the sweep rather than a constant, and that is the point — the tail is *a* part of the run, and
the header promised *the* run.

`FrameTimer.Resize` fixes it, and `StartBenchmark` sizes the window to the frames the run will
measure. The printed line now names the window it actually used — `over 500 frames` — so the claim
and the sample are the same number in the same sentence and cannot drift apart again in silence.

**Two things this changes for anyone setting a threshold on these numbers.** First, figures quoted
before this note are not comparable to figures after it; the 0.87 ms median recorded against
`E8-T15` was measured the old way. Second, the median is the noisier statistic: four consecutive
runs on one quiet machine gave medians of 1.04–1.25 ms (±20%) against p95s of 3.04–3.24 ms (±6%).
Guard the p95. It is also the number [ADR-0013](adr/0013-immediate-mode-node-canvas.md) is actually
about, which the `FrameTimer` documentation already said and the benchmark had not been reading.

---

## N32 — Allocation is what a shared runner cannot move, and it is not a consolation prize

The nightly benchmark gates bytes allocated per operation and gates nothing else
([ADR-0023](adr/0023-benchmarks-gate-allocation-not-time.md)). The reasoning is in the record; two
measurements behind it belong here, because both are the kind of thing the next reader would
otherwise assume rather than check.

**Allocation did not move between BenchmarkDotNet job configs.** The worry that justified pinning
the baseline to `--job short` was that bytes-per-operation is total bytes over operation count, so
a short run might amortise one-time allocations over far fewer operations and read high. The four
`EvaluationBenchmarks` cases were measured under the default config and under `--job short`, and
came back byte-identical: 1 593 696 and 103 296 at fifty nodes, 16 155 032 and 1 045 544 at five
hundred. Keep the two configs matched anyway, because it costs nothing — but if they ever disagree
that is a finding, not a nuisance to widen the tolerance around.

**Two ceilings are zero, and that is the sharpest guard in the file.**
`SceneIndexBenchmarks.Cull` and `HitTest` allocate nothing at two thousand nodes. Five per cent of
zero is zero, so *any* allocation on the cull path fails the job. ADR-0013's whole bet is that
culling a few thousand rectangles by hand each frame is cheaper than the framework's per-visual
costs, and an allocation per frame — a closure, a lambda capture, a `ToList()` added in passing —
is precisely how that stops being true while every test stays green.

**The figure that made `E4-T3` a standing guard is now a ceiling rather than an observation.** At
100 000 elements the return path allocates 5 297 836 B against the argument path's 800 144 B, a
factor of 6.6, because `FromClr` boxes every element through a `List<object?>`. That was recorded
as a number to act on later. Later has not come, and it now cannot get worse unnoticed.
