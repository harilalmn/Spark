# Spark — Implementation Notes

Non-obvious implementation facts, numbered. Adopted from DoodleSharp's convention.

**Last updated:** 2026-09-15 (N176 corrected: the measurement was right about the rule and wrong about the family)

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
design commits only after 1.5 seconds have passed with no GL callback ([N64](#n64--two-backends-one-capture-flag-and-a-screenshot-that-photographed-the-wrong-one)) — longer
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

> **NO LONGER REPRODUCES, 2026-09-14 (`E11-T15`).** Shown headlessly today, `InspectorPane` opens
> in under a second and the **real `MainWindow`** composes completely — title bar, menu, a
> `DockControl` laid out at 1480x838, status bar — renders a frame and closes, in under a second.
> Nothing in this repository was changed to achieve that; it was tried because two rows had been
> blocked on this note for a month and nobody had retried it. **A note that records a hang is a
> note that stops people retrying**, which is most of its value and all of its cost. It is kept
> rather than deleted so that a return of the hang is read as a regression against a known-good
> state rather than as a new fault, and `MainWindowSmokeTests` is now the thing that would notice.
> The cause below was never traced past the symptom, so what fixed it is unknown — an Avalonia
> patch between 12.1.0 and 12.1.1 is the likeliest, and nobody should claim more than that.
>
> **Amended 2026-09-09 (`E8-T75`), and the amendment is a narrowing.** This note had been read as
> *panes cannot be shown headlessly*, and that is not what it says. `CanvasPane` shows, lays out and
> hit-tests perfectly well in the headless session — it hosts the canvas, which draws its own text
> and wraps nothing. `E8-T75`'s tests drive it end to end: place a block, commit source onto it,
> open the in-node editor, type, and read the node's height back. What hangs is the wrapping,
> data-bound `TextBlock`, wherever it appears; today that is `InspectorPane`. Assume the pane under
> test works until it demonstrably does not.

**The symptom.** A test that shows `InspectorPane` with a `MainWindowViewModel` as its data context
hangs. It does not fail and it does not time out with a message: the run sits there until the
harness kills it. This killed `InspectorLayoutTests`, which was deleted rather than left in the
suite, and it is why the `App.axaml` fix in [N89](#n89--a-control-with-no-registered-theme-has-no-template-and-renders-nothing) was verified by a person's eyes and nothing
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

---

## N115 — `RequestNextFrameRendering` repaints the GPU surface and nothing else, and `InvalidateVisual` is hidden so it cannot say otherwise

A user's screenshot showed `OpenGL ready. Version 'OpenGL ES 3.0 (ANGLE …)'` sitting across the
middle of a viewport that was drawing a perfectly good scene, like a watermark. The message is the
viewport's own status plate, and `ViewportControl.Render(DrawingContext)` returns before drawing it
whenever the GL renderer is initialised — so the condition that would have removed it was already
right, and had been for the whole life of the message on screen.

**A GL control has two surfaces and they are repainted by two different requests.** The scene is on
a composition surface the compositor owns; `OpenGlControlBase.RequestNextFrameRendering()` queues a
composition update, which ends in `OnOpenGlRender` and repaints *that*. Everything the control draws
itself — the status plate, and on the software path the entire rasterised frame — comes from
`Render(DrawingContext)`, which runs only when the control's **visual** is invalidated. Nothing in
`ViewportControl` ever invalidated it. The plate recorded during startup, while the message still
read *waiting for the OpenGL context*, was therefore the plate for ever: resizing the window cleared
it, because a resize invalidates the visual, and nothing else did.

**Two things made this hard to see.**

1. **The GL callbacks did call something.** `OnOpenGlInit` ended in `RequestNextFrameRendering()`,
   which reads exactly like *and now repaint*. It is not; and it is additionally a no-op when called
   from inside `OnOpenGlInit`, because its own guard is `_initialization == null ||
   IsInitializedSuccessfully` and initialisation has not finished at that point.
2. **`InvalidateVisual` is hidden.** `OpenGlControlBase` declares
   `[Obsolete] public new void InvalidateVisual() => RequestNextFrameRendering();`, so writing the
   obvious call inside the control compiles, warns about the wrong thing, and asks for a GL frame.
   Reaching the real one takes a cast: `((Visual)this).InvalidateVisual()`. A hidden member is not a
   virtual one, so the cast is enough.

**It was never only cosmetic.** The software backend rasterises into `Render(DrawingContext)` as
well, so on a machine with no usable GL — a virtual machine, a remote desktop, `--software-renderer`
— an orbit, a zoom, a re-run and a capture request all reached the compositor and none of them
reached the screen. `ViewportRepaintTests` pins that half, because a capture that never completes and
a message that never goes away are the same missing invalidation seen from two sides.

**The screenshot harness could not have caught it.** `--screenshot` renders the window into a
`RenderTargetBitmap`, and that re-runs `Render(DrawingContext)` from scratch: the stale plate is
re-recorded correctly and does not appear. The bug lives in the compositor's retained recording, so
the only thing that shows it is the running application ([N114](#n114--three-ways-a-screenshot-lies-and-all-three-said-the-feature-was-fine) again, from a fourth direction).

---

## N116 — Dock ships no default host window, so a pane dragged out of the shell was deleted

Dragging any of the four panes off the main window made it vanish. Nothing floated, nothing
appeared behind the window or on another screen, and only *Reset layout* brought the pane back.

`FactoryBase.GetHostWindow` looks the host up in two properties the factory owns —
`HostWindowLocator`, keyed, and `DefaultHostWindowLocator`, the fallback — and **both start null**.
`Dock.Model.Avalonia.Factory` does not fill them in, and neither does `InitLayout`. So the lookup
returns null, and floating carries on regardless: the tool is removed from its dock and handed to a
`DockWindow` with no host to present it in. Every part of that is a successful operation with no
error anywhere.

The fix is three lines in `SparkDockFactory.InitLayout` — both locators returning
`new HostWindow()`. Both, not one: the keyed locator is what Dock asks for first, and leaving the
default null reopens the same hole for any path that creates a window under another key.

**The test that existed asserted the wrong half.** `TheViewportCanBeFloated` called `FloatDockable`
and then asserted nothing at all, and passed for as long as the defect existed. What has to be
asserted is that the window the tool was given has a `Host` — the pane's continued existence on
screen is a property of the window, not of the call returning.

---

## N117 — Three goes at a floated pane's title bar, and the flag that is not what it is named

Every pane's title bar carried a chevron and a pin. The pin was the worse of the two: Dock's pin is
**auto-hide**, so pressing it collapsed the pane into a strip on the edge of the window, and clicking
that strip then showed nothing at all. Auto-hide is a good gesture for a drawer beside a document;
these four panes are not drawers, they *are* the shell — which is also why `SparkDockFactory` leaves
`ToolDock.Alignment` unset, since an aligned `ToolDock` defaults to `AutoHide` with `IsExpanded`
false and draws its title bar over nothing.

**Dock draws a button per capability, so the capability is the control.** `CanPin` false takes the
pin off the bar and the auto-hide entries out of the menu; `CanDockAsDocument` false removes an entry
that could only ever have failed, because there is no `DocumentDock` in this shell to dock into;
`CanClose` was already false. That is the docked half, and it was right first time.

**The floating half took three attempts, and the reason is one badly named property.**

`HostWindow.ToolChromeControlsWholeWindow` reads like a presentation flag. Dock's theme binds it to
*this window holds fewer than two dockables* and uses it to choose `WindowDecorations="BorderOnly"`
and `ExtendClientAreaToDecorationsHint="True"`, so a single floated pane's own header becomes the
window's title bar — a header that can carry maximise and close and has **nowhere to put minimise**,
no system menu and no double-click-to-maximise.

*First attempt:* set it false, and let the operating system decorate the window. It does — minimise,
Aero snap, a taskbar button, all for free. It also **silently kills every drag back into the shell**,
because the flag is not a presentation flag at all:

```csharp
private void MoveDrag(PointerPressedEventArgs e)
{
    if (!ToolChromeControlsWholeWindow) return;
    ...
}
```

`MoveDrag` starts the window drag **and** the dock tracking that raises the indicators, and the drag
helper `ToolChromeControl.AttachToWindow` hangs on the grip is created with
`isEnabled: () => hostWindow.ToolChromeControlsWholeWindow`. Both halves of re-docking, behind a name
that says neither. The window floated, looked correct, and would not dock anywhere.

*Second attempt:* leave the flag alone and override the two properties it happens to set. Docking
came back, and the window grew **two title bars stacked on each other** — the system's, and the
pane's underneath it — of which only the lower one docked. Extending the client area under the system
caption instead gives one bar, but the title draws twice and the caption region is the system's to
handle.

*What shipped:* leave Dock's chrome alone entirely and **put the window buttons on it**.
`Theming/DockChrome.axaml` replaces the `ToolChromeControl` control theme with Dock 12.1.0.4's own,
structurally unchanged — `PART_Grip`, `PART_Title`, `PART_Border`, `PART_Panel` and
`PART_ContentPresenter` all keep their names and bindings, because Dock finds them by name and the
drag area, the grip modes and the deferred content hang off them — with the chevron and pin removed
and a minimise, a maximise/restore and a dock-back button added, all three shown only when floating.

**Minimise could not be styled in.** `ToolChromeControl` declares template parts for
`PART_CloseButton` and `PART_MaximizeRestoreButton` and wires their `Click` in code; there is no
third, and a style cannot attach an event handler. So it is bound as a command to
`SparkDockFactory.MinimiseFloatingWindow` — the same mechanism Dock's own template uses for
`Owner.Factory.FloatDockable` — which is why the factory carries public methods with no visible
caller, and why that dictionary sets `x:CompileBindings="False"`: the methods are not on `IFactory`.

**The close button means *dock back*, and is deliberately not named `PART_CloseButton`.** Dock binds
that name to its own handler, which takes the window away with the pane still inside — `E8-T45`'s
failure arriving through a door that only opened when the window got a close button at all. These
panes cannot be closed, so the only sense the gesture can carry is *stop floating*.
`OnWindowClosing` does the same thing for every other route to a closed window.

**A floating window also outlived the shell.** `DockSettings.CloseFloatingWindowsOnMainWindowClose`
defaults false; closing the main window left a pane on screen showing a graph that had gone, and the
process alive behind it because a window was still up. It is a static, set once in the factory's
static constructor.

**Every one of these three was found by a person dragging a window, and none of them by a gate.**
The headless session runs a bare `Application` with no Dock theme, so the properties involved read
their defaults whether or not anything sets them — the first test written for this passed on the
defect. What the shipped tests assert is that the factory sets **none** of the three chrome
properties, which is the fix stated as a negative and is what a future tidy-up would undo. The
behaviour itself was measured with a throwaway probe against the real `App`, and confirmed by the
client.
## N118 — A press on a port meant one thing, and a wired input needed it to mean two

Dragging the wire off a connected input port drew a **red wire with a `✕`** and then did nothing.
Nothing was broken: `OnPointerPressed` starts a wire from whatever port is under the pointer without
branching on which side it is, so pulling on a wired input started a *new connection from that
input* — and a connection from an input to empty canvas is refused, which is what the red and the
`✕` say ([design language §V1](help/concepts/design-language.md)). Correct feedback about the wrong
gesture.

**The fix is two-stage.** A press on a wired input only *remembers* the wire; it is lifted when the
gesture commits to being one — on the first movement past `ClickSlopScreen` for a drag, and on the
release for a click.

**The click half was left out at first, and the reason did not survive its own design.** The
argument was that a click that removed a wire would be an accident Escape could not undo, since
Escape abandons a pending wire without touching the graph. True of a design where lifting *is*
removing — and this is not one. Nothing is committed until the gesture ends, so a click that lifts a
wire commits nothing either, and `StandDownPendingWire` puts it back for free. The asymmetry was
caution carried over from a version of the code that had already been replaced, and the client found
it in one sitting: the drag disconnected, the two clicks did not (`E8-T50`). **A reason to be careful
is not the same as a reason to be inconsistent, and it stops being either when the thing it was
guarding against is gone.**

**Nothing is committed until the gesture ends.** The wire is lifted visually — skipped in
`EnsureWireVisuals`, which also drops it from `_connectedPorts`, so the port draws unconnected — and
the drag continues from the wire's *source* output, which is what makes the gesture read as picking
a wire up rather than starting a new one. The graph is untouched throughout, so a drag that ends
back on the port it started from costs no edit and no undo entry.

**One `GraphChanged` for the whole gesture, because that is what one undo step is.** Undo here is a
snapshot of the document taken when that event is raised (`MainWindowViewModel.RecordEdit`), so a
disconnect and a reconnect announced separately would be two entries, and putting one wire back
would take two presses of Control+Z. `DropDetachedWire` disconnects and reconnects with nothing
raised in between, and reports *Move wire* or *Disconnect wire* once.

**Where it is filtered matters.** The lifted wire is removed from the list before the loop rather
than skipped inside it: `_wireVisuals[i]` is matched against `wires[i]`, so a hole in the middle
would shift every wire after it and rebuild the lot. The filter allocates, and only while a drag is
in flight — never in the steady state the cache exists to protect.

**What is still not right, and is a design question rather than a defect.** Over empty canvas the
detached wire is drawn in the *rejected* colour with a `✕`, because `EvaluateDrag` answers `Refused`
for a null target. That is accurate about connecting and misleading about consequence: releasing
there **removes the wire**, which is not nothing. Saying so properly means a fourth drag state, and
§V1 deliberately refused a wire-only colour ramp — so it is left alone and written down here rather
than invented on the way past.

---

## N119 — A box's width is not stable under moving the box, and it cost the clean-up its undo guard

`CanvasBounds` stores **corners**, not a corner and a size, so `Width` is `MaxX - MinX`. That is
exact arithmetic on paper and is not exact in binary: `(520 + 209.2) - 520` and `(40 + 209.2) - 40`
are both "209.2" and they are **different doubles**. A node's width therefore changes in its last
bits when the node moves, and nothing else about the node changed at all.

**Why that mattered here.** `CanvasLayout` accumulates a column's x from the widest box in the
column before it. Lay a graph out, and the boxes handed back on the *second* pass are at their new
corners — so the widths come back a few ulps different, the accumulated columns land about `1e-13`
units from where they were, and `GraphCanvas.CleanUpLayout` compared `node.X == x` and concluded
that every node had moved. Pressing `Ctrl+L` twice recorded two undo steps, the second of which
undoes nothing a user can see. That is N19's failure reached by a route N19 does not describe: the
first three cases were *an operation that genuinely moves nothing*, and this one is an operation
that moves everything by a distance no screen can show.

**The fix is `CanvasLayout.Moves`**, a comparison with a `1e-6` floor, used instead of `==`. A world
unit is a pixel at 100 % zoom, so a millionth of one is seven orders of magnitude below anything
visible and seven above the noise; there is no honest threshold in between to argue about.

**The general shape, which is the reason this is a note.** Any geometry stored as corners and read
as a size is unstable under translation. `CanvasAlignment` is not bitten because it computes every
result from the extents it was given rather than from a value it recovered and re-derived — but it
would be, the moment something fed its output back into it. **Ask of any tidy operation whether
running it twice is the same as running it once, and check it with a test that actually feeds the
first result back in.** `CanvasLayoutTests.LayingOutTwiceChangesNothingTheSecondTime` does; it
passed while the canvas was still wrong, because the arithmetic *is* idempotent and the instability
lives in the round trip through `CanvasNode`. `GraphCanvasLayoutTests.CleaningUpTwiceRecordsOneEdit`
is the one that went red.

---

## N120 — One headless session, sixteen xunit threads, and a flake that named a different test every time

`Spark.UI.Tests` has **exactly one** `HeadlessUnitTestSession`, and it has to: `StartNew` twice in a
process leaves both sessions broken with no message that points at the cause, which `E11-T21` paid
for once already. Twenty-six test classes reach it through `HeadlessSession.Run`.

**What was not thought through is that xunit dispatches into it from sixteen threads at once.** Two
threads calling `Dispatch` concurrently raced the lazy construction of the compositor, and the loser
got:

```
System.InvalidOperationException : The calling thread cannot access this object because a
different thread owns it.
   at Avalonia.Rendering.DefaultRenderLoop.Add(IRenderLoopTask i)
   at Avalonia.Rendering.Composition.Server.ServerCompositor..ctor(...)
```

**The stack names nothing in this repository, and the victim changes every time** — one run blamed
`TheWheelDolliesTheCamera`, the next `TheDeferredFitDoesNotRepeatOnEveryLayout` and
`TheWheelDolliesTheCamera` together. That is what a shared-state race looks like from outside, and
it is why three sightings over three weeks read as three unrelated flakes instead of one defect. A
flake that blamed the *same* test every time would have been diagnosed the first day.

**`HeadlessSession.Run` now holds a semaphore for the length of the dispatch.** Only the tests that
need the one UI thread serialise; the arithmetic majority stays parallel. Assembly-wide
`DisableTestParallelization` was the alternative — it would serialise 940 tests to fix a race
between the few dozen that show a window, and cost about 16 seconds a run.

**The measurement, because a flake is only fixed by not recurring.** 2 failures in 16 runs before,
0 in 26 after. And at `v0.3.0`, 10 runs of 10 green — so the two window-showing classes `E8-T51`
added did **not** create the race, they made it likelier to fire. *Adding a test that shows a window
is adding load to a shared, process-global resource*, which is not how a new test file usually
reads.

**What is left, and it is `E11-T27` rather than this note pretending to be finished.** One run in 26
still failed, in `MainWindowViewModelTests` — a pure view-model test that shows no window and so
never passes through the turnstile. `MainWindowViewModel` posts to
`Avalonia.Threading.Dispatcher.UIThread` and lazily builds a `DispatcherTimer`, and **that
dispatcher is process-global state the session owns**, so a plain unit test touching it while a
session test dispatches is the same family of race by construction. The general rule this file is
here to record: **in this assembly, `Avalonia.Threading` is shared state, whether or not a test
opens a window.**

**Seen three times on 2026-09-07, and named on the third.** Two full runs of `Spark.UI.Tests`
reported `Failed: 1` with the name not captured, because the loop running the ten executables
grepped only the `Total:` line. Grepping `[FAIL]` as well caught it:

```
Spark.UI.Tests.CodeBlockOnCanvasTests.TheRoomIsAskedForInScreenPixelsWhateverTheZoom
  asked for 400 screen pixels at 50% and was given <less>
```

**Green 5/5 in isolation and 3/3 in full runs immediately afterwards**, so it is roughly one full
run in five and it does not reproduce on demand. It goes through `HeadlessSession.Run` and asks
`GraphCanvas.ScriptEditorSpace` for a width, which measures text — so a font manager or compositor
that is not ready yet would answer small, and that is the same shared session this note is about.
**That is a hypothesis, not a finding**: nobody has caught it with a stack or a measured value.

**What would settle it** is logging the actual `width` in the assertion message (it already is) and
keeping full run output in CI, then correlating a failure with which other test class was running
in parallel. Until then it is one known-flaky test, named, rather than an unattributed number.

## N121 — Python's `open(..., 'w')` writes CRLF on Windows, and the format gate is the thing that tells you

Editing a source file through a `python -c` heredoc is the fastest way to make a mechanical,
exactly-anchored change to a file this repository's documents are full of. It also silently
rewrites the **whole file** to CRLF: Python's text mode defaults to `newline=None`, which translates
every `
` on write to `os.linesep`. The repository is LF.

Nothing catches it until gate 3:

```
error ENDOFLINE: Fix end of line marker. Replace 2 characters with '
'.
```

— once per line of the file, for four files, which is a wall of output that says nothing about the
cause. The build is clean, the tests are green, and `git diff --stat` shows the *right* line counts,
because Git's `core.autocrlf` normalises on read and hides it. The only other tell is a
`warning: in the working copy of '<file>', CRLF will be replaced by LF` from `git diff`, which is
easy to read as noise.

**Pass `newline=''`** — on both the read and the write — and the file keeps whatever endings it had:

```python
s = io.open(p, encoding='utf-8', newline='').read()
io.open(p, 'w', encoding='utf-8', newline='').write(s)
```

The repair, if it has already happened, is `.replace(b'
', b'
')` over the file in binary.

**And one adjacent trap, which cost more than the CRLF did.** Testing AGENTS.md step 7 means
reverting the change to watch a named test go red — and the revert has to be *only* the temporary
edit. `git checkout -- <file>` reverts to `HEAD`, which is the whole uncommitted step, not the
experiment. Copy the file aside first and restore from the copy; the working tree is the only place
an in-progress step exists.

## N122 — A tree rewrite keeps the line and moves the column, and two of them do it now

`ScriptSourceMap` says columns are not mapped, and gives a reason: *"Nothing the wrapper adds is on
a user line, so a column is already the user's column."* That is true of the **wrapper**. It is not
true of the **rewriters** that run between the parse and the compile, and it has not been true since
`E6-T4`.

Measured, not assumed:

| Script | `;` typed at | Reported at |
|---|---|---|
| `while (true) { var q = 1; } var b = ;` | column 37 | **87** |
| `var a = 0..1..#5; var b = ;` | column 27 | **63** |

The first is `GuardWeaver` putting a `ScriptGuard.Tick()` inside the loop body; the second is
`ScriptRanges` lowering the range to `global::Spark.Api.NumberRange.ByCount(0, 1, 5)`. Both keep the
**line**, which is the thing `ScriptSourceMap` actually maps and the thing the diagnostics panel
navigates by, and both move every column after them on that one line.

**Why this is written down rather than fixed.** A fix is a per-line list of `(column, delta)` built
by whichever rewriter moved something, and it would have to be threaded through both rewriters and
into `ScriptDiagnostic`. That is worth doing when somebody reports it; it is not worth doing on
suspicion, and it is *definitely* not worth doing in only one of the two rewriters, which is what a
reader who noticed it in `ScriptRanges` alone would be tempted to do. The two are one problem.

**What it does not affect:** `ScriptCompletion`, which resolves a caret by flat character offset and
never runs either rewriter. `ScriptRanges` blanks `#` to a space precisely so that the text
completion sees is the same length as what the user typed.

**`E6-T34` widened the first of the two without changing its shape.** The depth guard now brackets
the methods, constructors, operators and accessors of a type a block declares, and bracketing a body
is the same insertion on the same line — so the columns on the *first* line of such a body move by
about fifty characters, and a body written on one line moves entirely. Lines two onward are
untouched, because the body keeps its own newlines. It is the same trade this note already records,
paid in one more place; the title still says two because it is still two rewriters.

## N123 — What compiles in a code block depends on what the host process has loaded

`ReferenceCatalog` builds its reference set from *the assemblies this process already has loaded*,
not from project references. That is deliberate and it is what makes a freshly loaded DLL become
usable without a restart. It also means **a code block's language surface differs between hosts**,
which is not obvious and cost a wrong conclusion:

```csharp
var n = Spark.Nodes.Core.Number.Range(3, 5, 1);
```

- In the **desktop app**: compiles, and evaluates to `[3, 4, 5]`. Verified with
  `--code-block "..." --screenshot`.
- In **`Spark.UI.Tests`**: `The type or namespace name 'Nodes' does not exist in the namespace
  'Spark'` — the test host has no reason to have loaded the node library, and the test helpers touch
  `typeof(Point3d).Assembly` to pull in `Spark.Geometry`, nothing more.

So a probe run from a test proves what compiles **in that host**, and a claim about what a user can
write has to be checked in the app. The first draft of `NumberRange`'s remarks said a code block
"cannot name `Spark.Nodes.Core` even fully qualified", which was a test-host observation stated as a
language fact, and `code-blocks.md` nearly lost a true sentence to it.

**The design conclusion survives the correction, and is worth separating from it.** Code the
*compiler generates on the user's behalf* — the range lowering — must name a project every host
references by construction, because "it happened to be loaded" is not a property a lowering can
depend on. That is why `NumberRange` is in `Spark.Api` and not beside the nodes. What changed is the
reason: not *impossible*, but *not guaranteed*.

## N124 — The headless test platform draws nothing, so no test can assert a pixel

`Spark.UI.Tests` builds its Avalonia application with
`UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })`. That backend
routes layout, input and the visual tree faithfully — which is why every gesture test in the
assembly is worth having — and **implements drawing as a no-op**. `window.CaptureRenderedFrame()`
returns a frame, and `RenderTargetBitmap.Save` writes a **zero-byte file**.

The first draft of `E8-T69`'s tests asserted the exported PNG's dimensions out of its own IHDR
chunk and that the file was large enough not to be a flat rectangle. Both are the right assertions
and both failed with *the export is 0 bytes*, which reads as a defect in the export and is a
property of the platform.

**So a claim about what an image contains has to be checked against the real application**, which
is why `--export-graph` and `--export-size` exist alongside `--screenshot`: the same reasoning
[N112](#n112--one-keystroke-two-text-changes-and-three-verifications-that-all-missed-it) records for input, applied to output. What a headless test can
still assert about a render is everything around it — that the call completes, that it writes a
file, that the view it moved is put back — and those are worth asserting, because they are the
parts this repository wrote.

**The alternative was `Avalonia.Headless.Skia`**, which draws for real, and it was not taken here:
it would make one assembly's rendering depend on a second headless backend whose output is not the
one users see either, in exchange for pixels that the application itself already produces on demand.


---

## N125 — Moving code out of a generated method without moving anything else

A code block's text is emitted inside `Block.Run`, and C# has no local class — so
`public class TestClass { … }`, which is an ordinary thing for a user to write, could not compile.
The declaration has to be lifted out to namespace scope, and the obvious way to lift it is to cut
the text out and append it below.

**Cutting is what makes the rest of the pipeline expensive.** Removing a span shortens every offset
after it and joins the line before it to the line after it, and two things in `Spark.Scripting`
are built on neither of those happening:

- `ScriptSourceMap` is a *subtraction* — a diagnostic's user line is its generated line minus a
  constant. `GuardWeaver` weaves statements with no trivia, and the wrapper puts every line it adds
  before the user's first, precisely so that it can stay one.
- `ScriptRanges` records where each `#` marker stood as a **character offset** into the script, and
  `Wrap` moves them into the generated source's coordinates by adding a single constant. Blanking is
  length-preserving for exactly this reason ([N122](#n122--a-tree-rewrite-keeps-the-line-and-moves-the-column-and-two-of-them-do-it-now)).

So the declaration is **blanked to spaces where it stood** — every character overwritten with a
space, every newline left alone — and re-emitted after the generated class. The statements keep
their lengths, their offsets and their line numbers; nothing above, below or beside a declaration
notices that it left. Only the re-emitted lines need a map entry, and a block that declares no type
produces none at all.

**The property this buys is worth naming**, because a cheaper scheme does not have it: the
declaration may appear *anywhere* in the block, including above the statements that use it. A scheme
that kept the user's text in one contiguous run would have had to close the generated method before
the class and reopen it after, and the block's locals do not survive that.

---

## N126 — The token that carried the diagnostic, and the rewrite that threw it away

`GuardWeaver` turns an expression-bodied member into a block so a depth guard can go inside it:
`double Twice(double x) => x * 2;` becomes `double Twice(double x) { …Enter(); try { return x * 2; }
finally { …Exit(); } }`. Building the block means dropping the member's `=>` clause **and its
semicolon token**.

For `double Twice(double x) => x *;` that semicolon is where the parser hung
`CS1525: Invalid expression term ';'`. Dropping the token dropped the diagnostic, and because it was
the only one, the block reported **nothing at all** — no squiggle, no message — on a line that
plainly does not compile.

It is not obvious from reading the rewrite, and it is invisible to any test written against code
that compiles. It was found while generalising the same rewrite to the members of a declared type
(`E6-T34`), and it had been reachable from a local function since `E6-T4`.

**The fix is to leave a member that does not parse exactly as it is.** Nothing is lost by not
weaving a guard into it: a member that does not parse never runs. The general form of the rule is
worth keeping in mind whenever a syntax rewriter discards a token — *a token you delete takes its
diagnostics with it*, and the parser attaches them to the token nearest the problem rather than to
the node you are thinking about.

---

## N127 — Two assemblies holding the same type name hold two different types

`E6-T35` gathers every code block's type declarations into one shared assembly so that a class
declared in one block can be used in the next. The tempting simplification is to let each block go
on emitting its own copy as well — it keeps `E6-T34`'s code path untouched, it keeps a block
self-contained, and **every block still compiles**.

It is wrong, and the way it is wrong is the expensive kind: nothing fails until an object moves.

A CLR type's identity is its assembly plus its full name. Two assemblies that each contain
`SparkGenerated.Helper` contain two unrelated types. A block that makes a `Helper` and hands it
down a wire to a block that expects one is handing over an object of the wrong type, and the cast
fails with a message that names `Helper` twice and explains nothing:

> Unable to cast object of type 'SparkGenerated.Helper' to type 'SparkGenerated.Helper'.

So the rule is **one declaration, one assembly**: `ScriptDeclarations.Holds` is what
`ScriptNodeFactory.Wrap` asks before it re-emits anything, and a script in the shared set emits
none of its own.

**The test that catches this is not the obvious one.** Compiling both blocks passes under either
design. Running both blocks passes too. What fails is making an instance in one block and reading a
field off it in the other, which is the first thing a user would actually do — so that is the
assertion `SharedDeclarationTests.AnInstanceMadeInOneBlockIsTheSameTypeInAnother` makes.

**A second consequence, found the hard way.** The shared assembly's simple name carries the set's
fingerprint. Without that, sharing a second set loads a second assembly with the same simple name
into the same `AssemblyLoadContext`, and the binder resolves the name to whichever arrived first —
so a block compiled against the *new* metadata binds to the *old* assembly and sees members that
are not there. It surfaced immediately as `FileLoadException: Assembly with same name is already
loaded`, which is the loud version; the quiet version is a block using a stale class.

---

## N128 — Referencing the assembly is half of it; the namespace is the other half

`E6-T35` compiles every code block's type declarations into one shared assembly, in
`namespace SparkGenerated`. `E6-T37` had to make the editor's language service see them, and the
obvious fix — add the assembly to the completion workspace's metadata references — applied cleanly,
reported success, and changed nothing. The completion list for `Helper.` stayed empty.

**The two worlds are not shaped the same, and only one of them is inside the namespace.**

- What the compiler sees is `ScriptNodeFactory.Wrap`'s output: the prelude, then
  `namespace SparkGenerated;`, then the block's statements inside a method inside a class inside
  that namespace. A user's `Helper` is a sibling, so `Helper` resolves unqualified.
- What `ScriptCompletion` sees is the block's text as a Roslyn **script document** in the global
  namespace, with the catalogue's imports supplied as *global usings*. Referencing the assembly
  makes the type exist there and leaves it reachable only as `SparkGenerated.Helper` — which is not
  what the user typed, and not what the compiler will require of them.

So the namespace goes into the project's usings at the same moment the reference does, and the two
are updated together or not at all. **The general shape**: when a language service is configured to
imitate a compilation, every scope the real compilation puts the user's code inside has to be
reproduced — a reference makes a type *available*, and only an import makes it *nameable*.

It is worth noticing how this failed. Nothing threw, `TryApplyChanges` returned true, and the method
reported that it had updated the references, which it had. The only signal was an empty list, which
is also what "no candidates here" looks like. The completion layer answers a great many things with
silence, which is why every claim about it in this repository is written as a test that names the
member it expects rather than as a count.

---

## N129 — A document's node order is a Guid order, so no test may assert it

`GraphDocument.Capture` orders nodes by `node.Id.Value.ToString("D")`, and a `NodeId` is a `Guid`.
That ordering is deliberate and it is what makes a `.spark` file round-trip byte for byte: it is
*stable for a given graph*, because the identities do not change. It is not *predictable*, because
the identities are random.

`E6-T36`'s first test asserted the exact sequence of calls a restore makes:

```
["share:A|B", "create:A", "create:B"]
```

which pins the order two blocks appear in the document. It passed when it was written, passed the
step's own gate run, and failed a full run two steps later for a reason that had nothing to do with
anything that had changed in between — roughly a coin flip per run, and the coin had come up heads
four times.

**The rule this leaves**: assert what a restore *does* — one share, holding every script, before any
create — and never which of two nodes is named first. Where an order genuinely matters, sort before
asserting; `E6-T35` already sorts declarations before hashing them precisely so that node order
cannot reach a compile-cache key.

**The tell to recognise next time**: a test that passes alone, passes ten times alone, and fails in
company is usually blamed on parallelism — this repository has a whole open row about that
(`E11-T27`) and it made a convenient explanation. It was not that. A test whose expectation depends
on a random identity fails at its own rate no matter what else is running, and "passes in isolation"
does not distinguish the two.

---

## N130 — A "did it change?" helper is not a "should I do it?" helper

`E8-T75` made a code block grow while it is typed into. Re-placing the editor is not free — it
re-measures the node and rebuilds the canvas's spatial index — so the pane got a helper that
measures the size the editor wants, stores it, and returns **whether it moved**. On a keystroke that
is exactly right: typing inside a line that is not the longest changes nothing and must cost
nothing.

The same helper was then used on the **open** path, which needs the measurement but not the
verdict:

```csharp
if (!Wanted(e.Text) || !Place(e.Slot))   // wrong
{
    return;
}
```

Opening a block whose editor wants the size the *previous* editor wanted — one line, which is most
blocks — answers false. The editor is never placed, never shown, never focused, and every keystroke
goes to the canvas. The client reported it as *cannot type anything in CodeBlock*.

**Why the tests missed it, which is the part worth keeping.** `E8-T75`'s three tests each opened one
editor, once, in a fresh session — and the remembered size starts at zero, so the first open of the
first block always answers true. The defect lives entirely in the *second* open. A remembered-state
optimisation has no first-time behaviour worth testing; its behaviour is what happens on the
repeat, and a test that never repeats cannot see it.

**The general rule**: when a method both computes something and reports whether it differs, the
report belongs only to callers that are optimising. Any caller that needs the computation must
ignore it — and the ones that ignore it should say so, because `_ = Wanted(...)` invites the
question and a bare call does not.


## N131 — An inner `using` does not collide with an outer one; it silently wins

`E6-T39` hoists a code block's own `using` directives out of the method body, and the question was
where to put them: above `namespace SparkGenerated;`, beside the prelude, or below it, inside the
namespace. Inside looked like the timid choice. It is the one that works, and for a reason worth
writing down.

C# resolves a simple name by walking scopes from the inside out, and **it stops at the first scope
that has an answer**. The prelude sits at compilation-unit scope; a directive emitted after
`namespace SparkGenerated;` sits one scope in. So a block that writes `using Autodesk.Revit.DB;`
does not *add* Revit to what Spark already imported — it **shadows** it, for every name the two
share. `Line` in that block is Revit's `Line`, and there is no `CS0104` to resolve because lookup
never reaches `Spark.Geometry`.

The same rule makes `using Circle = Autodesk.Revit.DB.Circle;` legal beside the prelude's own
`using Circle = Spark.Geometry.Circle;`, which at one scope would be **`CS1537`: the using alias
'Circle' appeared previously in this namespace**.

**Two tests had to be rewritten because of this**, both of which had asserted an ambiguity that
never happens. The rule they were replaced with is one sentence and a user can hold it: *what the
block writes beats what Spark imported for it, in that block and nowhere else.* An ambiguity is only
possible between two directives **the user wrote themselves** — and that is exactly the case an
alias is for, which is what the client asked to be made possible.

**The general shape**: when placing generated code around a user's, scope nesting is a tool and not
just a container. The same two directives are a conflict at one level and an override at two.


## N132 — A NUL byte in a source file compiles, and greps as binary

> **This note contained two of them, from the day it was written until 2026-09-14, and one of the
> two was inside the scan it prescribes.** So the remedy handed to the next reader was itself
> corrupted, and `docs/NOTES.md` was a file `grep` would not search — which is exactly the symptom
> in this note's own title.
>
> **It cost something real before it was found.** A census of this file came back with **111
> entries** when it has **172**: `grep` classified it as binary, stopped reporting, and returned a
> number that looked like a number. It arrived as a *correction* of a figure that was already right,
> which is the most persuasive form a wrong answer can take, and it was one command away from being
> written into the journal. See [N172](#n172--a-limit-is-not-a-census-and-the-truncated-query-confirms-you).
>
> **Three more were in the tree and none was the one this note is about.** `C:\dev\vcpkg` had lost
> its `\v` to a vertical tab in `docs/JOURNAL.md` twice and `docs/TASKS.md` once, and
> `WorkspaceLayoutTests` had `@"C:\feed"` with its `\f` eaten into a form feed — **twice, on both
> sides of the same assertion**, so the test compared the corrupted value to itself and could never
> have failed. A fifth, in `CodeFormatPreferenceTests`, is deliberate: the test writes a NUL to
> prove an unreadable preference falls back to *on*. That one belongs in the **runtime string**
> rather than in the **source bytes**, and is now `"\0not a word at all"` — the same byte at run
> time, in a file tools can read.
>
> **The scan is no longer a command somebody remembers.** `ControlCharacterChecks` in
> `Spark.Docs.Verify` walks every text file in the repository on every run, allowing only tab,
> newline and carriage return, and reports the path, the offset and the byte. `licences/` is
> skipped: `LGPL-2.1.txt`'s fifteen form feeds are the FSF's own page separators, and editing a
> byte of somebody else's licence to satisfy a checker of ours would be the wrong way round.
>
> **And the ad-hoc scan run first had an off-by-one that hid two of the five.** It tested
> `c < 9 or (13 < c < 32)`, which skips 11 and 12 — vertical tab and form feed — the exact two
> characters that turned out to be in the tree. **The quick version of a check is a check whose
> bugs nobody looks for**, which is the argument for the check being code that has its own tests.

`ScriptDeclarations.cs` carried a literal `U+0000` inside a `char` literal — `all.Append('\0')`
where `all.Append(' ')` was meant — written by a shell heredoc whose escaping had mangled a space.
It survived for a day and through several full gate runs.

**Nothing catches this.** It is a valid `char` literal, so the build is clean; the value only fed a
fingerprint separator, so no test could see it; and `dotnet format` has no opinion about it. What
*did* show it was `grep` refusing the file with **Binary file … matches**, which reads like a tool
being unhelpful rather than a finding.

**The lesson is about the tooling, not the byte**: text written to a file through a shell heredoc is
not necessarily the text that was intended, and the failure is silent in both directions. Editing
source through a script now means reading back what landed — and a repository-wide scan for control
characters costs one command:

```python
if b'\0' in io.open(path, 'rb').read(): ...
```

---

## N133 — Changing where a package installs changes who else has to read that folder

`E7-T21`'s **Add as a library** button reuses `PrepareLibraryAsync` unchanged and only points it at
a different `PackageStore` — one rooted at the graph's own `<name>.packages` folder rather than the
machine-wide store. That looked like a one-line difference. It exposed two defects, and both are the
same shape: **a second reader of the folder now exists, and it did not know what the installer
writes into it.**

**One — the dependencies were downloaded and then not referenced.** `StageDependenciesAsync` puts a
package's transitive dependencies in `<package>/.deps/<id>.<version>/lib/<tfm>/`, which is right and
has been since `E7-T2`. `GraphPackages.Discover` looked at `<package>/lib/<tfm>/` and stopped. Under
the global store nothing noticed, because the load context takes the package folder whole. Under the
graph folder the assemblies are handed to the *compiler* one path at a time, so a dependency that is
not in the list is not referenced — and the failure is `CS0012`, on the user's line, naming an
assembly they have never heard of, the first time a dependency's type appears in one of the
library's own signatures.

**Two — a staged download looked like an installed package.** A pending install is written to
`<id>.<version>.installing` *inside* the destination folder, which under the global store is
invisible: `PackageStore.IsInstalled` requires a manifest, so a half-extract reads as absent. A
graph-local library has no manifest by definition, so `Discover` would have offered the contents of
a download **whose disclosure had not been answered yet** — and, after a crash, would have gone on
offering it. The suffix is now a named constant that the finder skips, rather than a string spelled
in one file and unknown to the other.

**The lesson.** *Where* something is stored is not an implementation detail once a second component
reads the same directory. Both defects were invisible to every existing test because both readers
were correct about their own half; what was missing was the statement that they are reading the same
layout. That is why the two tests added for this are about the **folder**, not about either
component: what an install writes is what a graph opened on another machine finds.

---

## N134 — A platform-neutral target framework asks the wrong question at run time

`Nice3point.Revit.Api.RevitAPI` ships its assembly in `ref/net10.0-windows7.0/`. Spark's own target
framework is `net10.0`, with no platform. NuGet's answer to *may a `net10.0` project use a
`net10.0-windows` asset* is **no**, and NuGet is right: that project might be built for Linux, and
the asset would not work there.

**But that is a question about a compilation, and this is not one.** Spark is not deciding what a
portable project may reference; it is a process that has already started, on an operating system
that is already known. A Windows-specific assembly loads and works in it. Asking NuGet with the
platform-neutral framework was asking about a hypothetical future build rather than about the
running program.

So `PackageFrameworks.Current` is the build's framework **plus the platform the process is on** —
`net10.0-windows` on Windows, unchanged elsewhere. It stays a real constraint rather than a bypass:
a `-windows` asset is still refused on Linux, and `-android` and `-ios` are refused everywhere.

**Two more things this cost, and both are the same mistake as [N77](#n77--every-package-test-passed-and-no-real-package-could-be-installed).**
`GraphPackages` had its own hand-written framework ranking, parsing everything after `net` as a
number — which reads `10.0-windows7.0` as nothing and refuses the package. Its own doc comment said
the ranking was crude and that real compatibility is NuGet's resolver; it is deleted now, and both
callers share one rule. And nothing looked in `ref/` at all, though a compile-time-only folder is
exactly what a CAD API package is and exactly what a code block needs.

**Every fixture in the layer built `lib/` with a bare `netN.0` moniker**, so all three defects were
invisible to a green suite — the third time this repository has learned that a helper which
constructs the subject in the convenient shape hides every defect living in the difference.

**The lesson, stated as a rule:** when a question has a correct answer from a package manager and a
correct answer from the running process, work out which of the two you are actually asking. Both
answers here were right; only one of them was to the question at hand.

---

## N135 — A deleted test project kept passing for eleven days

`dotnet test Spark.slnx` does not work on this machine ([N30](#n30--a-test-that-disappears-is-invisible-to-all-three-gates)
is the shape, and AGENTS.md carries the workaround), so the suite is verified by a loop:

```
for p in tests/*/; do n=$(basename "$p"); (cd "$p/bin/Debug/net10.0" && ./"$n.exe"); done
```

**The loop is driven by what is on disk, and `bin/` is gitignored.** `Spark.Geometry.Io.Tests`
was folded into `Spark.Geometry.Tests` and its project deleted from git on some earlier day; the
build output stayed. Every verification run since then executed an **eleven-day-old** binary,
found twelve passing tests in it, and added them to the total. The register audit of 2026-09-09
quoted 2,966 tests across ten projects. There were 2,954 across nine.

**Nothing was actually broken by it**, which is the part worth sitting with. The twelve tests
still exist, in `Spark.Geometry.Tests/ObjWriterTests.cs`, and they pass there. The damage was
entirely to the evidence: a count that nobody could reproduce from the repository, and a green
run that included a project no longer in it. **A number you cannot re-derive from the tree is not
a measurement.**

**Why neither gate saw it.** `dotnet build Spark.slnx` builds what the solution names, and the
solution had already stopped naming it — so the build was correct to ignore it, and correct is
exactly why it was silent. `dotnet format` reads the solution too. The one thing that read the
*directory* was the verification loop, and a loop over `tests/*/` cannot tell a project from its
own leftovers.

**Found by looking for something else.** The audit went looking for a CLI test project, noticed
`Spark.Geometry.Io.Tests` in the loop's output but not in `Spark.slnx`, and assumed the project
had been left out of the solution. Adding it to `Spark.slnx` failed with `MSB3202: the project
file was not found`, which is when it became clear the directory held no project at all. **The
first hypothesis was the more alarming one and the truth was the more embarrassing one**, which
is the usual direction.

**The guard is `SolutionMembershipTests`, and it is two assertions rather than one** because the
two failures are opposite. Every `.csproj` under `tests/` is in `Spark.slnx` — that catches a real
project nothing builds. No directory under `tests/` holds a `bin/` without a `.csproj` beside it —
that catches this. Both were written red: the first flagged `tests/corpus/`, which is data and has
no project by design, and **membership was moved from *is a directory* to *holds a project* before
the guard was a day old**, because a check that flags a legitimate thing is a check somebody
suppresses.

## N136 — A sample that ends in `…` is a sample nobody ever tried to compile

**Date:** 2026-09-10 · **Rows:** `E11-T2`

`SparkNodeAliasAttribute`'s `<example>` had read

```
[SparkNodeAlias("Circle.ByCentreRadius")]
public static Circle FromCentreRadius(Point3d centre, double radius) => …
```

since the attribute was written. It is the first thing a reader of the public API sees about how
to spell an alias, and **it is not C#** — `…` is a single character `U+2026`, not an elision the
compiler forgives. Pasting it gets `Invalid expression term ''`.

**Nothing was wrong with the check that existed; it was pointed at the other half.** Every
` ```csharp ` fence in `docs/help/` has compiled against the real API since `DocumentationSampleTests`
was written. The XML `<example>` blocks were exempt for a reason that expired quietly: when the
harness was built, no contract project used one. By the time there were four, the harness had
stopped being told.

**The general shape, and it is worth more than the fix.** A verification gate is scoped to a
*source* of the thing it checks, and the scope is written down once, at a moment when it happens
to be complete. Sources get added afterwards by people who are not thinking about the gate. So the
question to ask of any harness is not *does it pass* but **what does it not look at, and was that
list ever true?** — and the answer belongs in an assertion, not in a comment. Here that assertion
is `EveryExampleElementInTheSourceYieldsACheckedSample`: the number of `<example>` elements in
`src/` must equal the number of samples the parser handed to the compiler. A parser that quietly
recognises none of them is otherwise green, which is the failure mode a coverage harness has and
an ordinary test does not.

**One sample genuinely could not be a statement, and the fix is a declaration, not a skip.** An
attribute sits on a member, so the alias example has to compile at class scope. The scope is
declared by the author on the element — `<code spark-scope="class">` — rather than guessed from
the text, for exactly the reason `IsCodeBlockTopic` is a topic id and not a heuristic: a guess
that reads a *broken statement* as a *declaration* compiles it and reports success, which is the
one outcome this harness must never produce. **On the element rather than as a marker line inside
the sample**, so that what a reader copies is the declaration and nothing else — the marker is
metadata about the sample, and putting metadata inside the sample makes the sample a lie in a
second, smaller way.

## N137 — A widget and a wire are two authorities over one number

**Date:** 2026-09-10 · **Rows:** `E8-T25`

Reported from a running graph, with a screenshot: a `Number.Slider` whose `min` and `max` were
wired from a code block to −10 and 50, **reading 89.69**.

**Both halves of the bug are the same mistake seen from two sides.** The slider had four wirable
inputs and a widget, and the widget was drawn from the *literals* — the numbers last typed into
the node. A wire never writes to a literal. So wiring a range changed what the node **computed**
and left the track it was **drawn** on at the defaults, 0 to 100. The thumb then swept a range
that did not exist, and the number under it was one the node would never return, because
`Number.Slider` clamps before it returns. The canvas and the graph were both internally consistent
and were describing different graphs.

**The old behaviour was written down, which is why it survived.** `NodeSliderAttribute` said it
plainly: *the track on the canvas is drawn from the typed-in literals; wiring a range drives
evaluation but not the drawing, because the canvas paints before anything has run and has no value
to paint from.* Every clause of that is true and the conclusion is still wrong. **The canvas paints
before the first run exactly once**, and paints after every run thereafter — so *nothing to paint
from* describes a moment, and it had been treated as a permanent condition. The fix is to keep the
last `EvaluationResult` and read the wired ports from it, falling back to the literal. The
fallback covers the one moment the original reasoning was about.

**The second ask removed the question rather than answering it.** *Remove the value input port.*
With `value` wirable, there is no good answer to what the thumb should do when a wire drives it
past the end of the track: follow it and the thumb leaves its track, ignore it and an input is
silently discarded, clamp it and the node reports a number nobody asked for. **A value with two
authorities has to arbitrate, and every arbitration is a surprise to somebody.** One authority has
no such question. `[NodeUnwired]` is the general form — the port keeps its literal, its
serialisation and its place in the signature, and loses only the ability to be a wire's
destination.

**Refuse it in the engine, not by hiding the connector.** `Graph.TryConnect` returns `SPK1015`.
Hiding the target in the canvas would leave the wire creatable from a file, from `spark`, or from
anything added later — a rule enforced only by the thing that draws it is a rule about drawing.

**The cost that was not obvious: port index and drawn row stop being the same number.** Every
geometry method on `CanvasNode` had `index` and `row` as one concept, correctly, for as long as
every port was drawn. Hiding one splits them, and a mapping right in one direction and wrong in
the other draws `min`'s tab on `max`'s row. It is `InputRow` and `InputAtRow`, they are asserted
in both directions, and **the node is now one row shorter than its port count** — which is what an
existing height test caught immediately, and it was the good kind of failure: a test that knew the
old number told the truth about the new one.

## N138 — Formatting on a line break deletes the line break

**Date:** 2026-09-10 · **Rows:** `E8-T84`

`ScriptFormatting.Format` trims trailing newlines. It is right to: a block does not want one, and
`E8-T84` added the trim on purpose so that clicking away did not leave a blank line behind.

**So the obvious implementation of *format on line break* undoes the keystroke that triggered
it.** Press <kbd>Enter</kbd> at the end of a block, the document becomes `var a=1;` plus a newline, the
formatter is handed the whole of it, and it returns `var a = 1;` — newline gone. The caret jumps
back to the end of the previous line. Every line, on every block. **A feature that fights the
person using it on its own trigger** is worse than not having it, and it would have shipped,
because the tidying *does* visibly work: `var a=1;` really does become `var a = 1;`, and a test
asserting the tidying would have passed.

**The test that catches it is written about the newline, not about the tidying.**
`FormattingOnALineBreakKeepsTheLineBreak` asserts that the result still ends in a newline first, and the tidied text
second. Six tests go red against the naive version and that one names the actual defect; the other
five report caret positions, which is a symptom.

**The fix is a boundary, not a special case.** `FormatAbove` treats the caret's own line as the
edge: everything above it is finished work and is formatted, the line the caret is on is being
typed and is not touched, and the newline between them is re-inserted explicitly rather than left
to survive the round trip. The guard against mangling a half-written `if (x) {` needed no code at
all — the text above the caret does not parse, and `Format` already returns unparseable text
untouched.

**Two ways to move a caret, and only one of them is exact.**

- *On a line break*, only the text above the caret's line changed, so everything from the caret
  onwards is the same string at a new offset: **add the length difference.** Exact.
- *On <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd>*, the caret's own line changes too, so that
  does not work. **Count the non-whitespace characters before the caret and walk that many into
  the formatted text.** Also exact, and for a reason worth stating: `NormalizeWhitespace` rewrites
  whitespace and nothing else, so the sequence of non-whitespace characters is invariant. Clamping
  the old offset into the new length — the obvious thing — puts the caret out by however much the
  indentation above it changed.

**And an aside that a screenshot found rather than a test.** The Properties pane has one `*` row,
which held the code block's editor *and* the font picker. A `*` row gets what the `Auto` rows
leave behind, and with a block selected and a watch holding a value there was nothing left — so
the row was squeezed to nothing and painted its contents over the WATCH panel below. **It was
already doing that before this step**; adding a third control made it impossible to keep missing.
Settings are fixed-height chrome and the editor is the elastic part, so the settings moved to an
`Auto` row of their own. That is `E8-T71`'s comment in this same file, earned a second
time: *its own row, because the two are not mutually exclusive.*

## N139 — A clamped sample and an unclamped denominator

**Date:** 2026-09-10 · **Rows:** `E2-T33`, `E11-T10`

`Surface`'s default numeric derivatives took a central difference:

```
derivativeU = (Sample(u + h, v) - Sample(u - h, v)) / (2h)
```

and `Sample` **clamps** an open direction into its domain. So at a domain edge both arms of the
stencil landed on the same side, the numerator spanned `h`, the denominator said `2h`, and **every
first derivative on every open boundary came back at exactly half its true value.**

**The second derivative was much worse than half.** With one arm collapsed onto the centre, the
second difference `(P(u+h) − P(u)) − (P(u) − P(u−h))` became `P(u+h) − P(u)` — still divided by
`h²`. That is `f′/h`, and with `h` a millionth of the domain it is about **a million times too
large**. It fed Newton's Jacobian inside `ClosestPoint` and it fed `PrincipalCurvatures`.

**Neither was found by a test of derivatives.** They were found by a property asking a revolution
surface for the closest point to a point *on its own inner rim*, which came back 1.5e−5 away from
itself. The chain from *this number is slightly wrong* to *the denominator is a lie at the edges*
is four steps long, and no test asserting a derivative directly existed to shorten it — which is
the argument for properties that assert something a person cares about rather than something the
implementation happens to compute.

**The fix divides by the span it actually took.** `Span` narrows the two parameters to what the
domain holds and the difference divides by `high − low`: `2h` in the interior, `h` at an edge. That
turns a second-order central difference into a first-order one-sided difference exactly where the
domain ends — an error of order `h`, replacing an error of a factor of two. For the second
derivative a one-sided version cannot keep its spacing even, so `Triple` **shifts the whole stencil
inwards** instead: the answer then belongs to a point one step inside the boundary, which is a
millionth of the domain away and is the smallest lie on offer.

**A closed direction has no edge and must not be narrowed.** `Sample` wraps there, so the full `2h`
span is real; narrowing it would put a seam into the derivative of a cylinder, which has none.

**What this did not fix, and it is worth separating.** Three further things were wrong with
`ClosestPoint` and each needed its own change: a seed landing *on* a pole, where the Jacobian is
singular and Newton cannot move at all; an iteration that returned wherever it stopped rather than
the best point it had seen; and an iteration budget of eight that was sized for full Newton steps
and became too small once a backtracking line search could halve them. **Four defects, one
symptom.** The reason they were all found at once is that the property compared against something
a reader can check by hand — *a point on a surface is its own closest point* — rather than against
the routine's own idea of success.

## N140 — A rectangle that grew for one reason and was read for another

**Date:** 2026-09-10 · **Rows:** `E8-T40`

Reported with two screenshots: *"trying to select the codeblock, but the number slider below is
getting selected"*, and *"node selection has some issues when codeblocks are present"*.

**`E8-T40` grows a code block while its in-place editor is open**, so the editor is not drawn over
the port tabs either side. That is right, and the row explains why at length. What nobody noticed
is that the growth landed in `CanvasNode.Bounds` — and `Bounds` is what the spatial index is built
from, which is what hit-testing and marquee selection read.

**So a block of 287×70 became 584×305, and the extra 130,000 square units were invisible.** An
empty stretch of canvas below and to the right of the block answered every click with the block,
and any node sitting in that stretch could not be clicked at all. It only happens with a code
block, because a code block is the only node that reserves anything — which is exactly the shape
the report described.

**One rectangle was doing two jobs.** *How big is this node drawn* and *where may it be clicked*
had been the same question for every node in the application, and stayed the same question right
up until one node could be drawn bigger than it really is. `SelectionBounds` splits them:
`Bounds` still grows, because the cull has to keep the drawn node on screen; `SelectionBounds` is
the node without its reservation, and hit-testing and the marquee use that.

**The reservation never needed to be clickable at all, and that is the part worth remembering.**
While the editor is open it is a **real Avalonia control on top of the canvas** — every click
inside it is delivered to the editor and the canvas never sees it. So the enlarged target could
not be hit deliberately by anybody; it could only be hit by accident, from outside, by somebody
aiming at something else. **A hit region that no intended gesture can reach is not a feature with
a bug in it; it is entirely a bug.**

**Found by the client and not by the tests, and the tests were not thin.** There are gesture tests
that press buttons on the canvas ([N88](NOTES.md)), tests for the reservation's geometry, and
tests for the editor's placement. Every one of them asks about the node *being edited*. None asked
what happened to **the canvas around it**, and the defect lived entirely in the space the node was
not. The four new tests are all about the neighbourhood: empty canvas beside an open editor, and a
neighbour under where the phantom used to be.

## N141 — A fire-and-forget run in a constructor is a race every reader of the diagnostics inherits

**Found by the gate run at the start of a session, not by the session that wrote the code.** The
journal said the tree was clean at 3,015 tests with no failures. It was 3,015 with **one** failure
— `ViewportExportTests.ExportingSolidsFromAnEmptySceneRefusesWithAReason` — and the difference is
entirely load: the test passes alone, passes as a class, and fails inside the full suite. That
combination is the signature of a race, and it is also the reason the failure survived: every
natural way to investigate a failing test makes it stop failing.

**The mechanism is three hops from anything the test mentions.** `new MainWindowViewModel()` ends
in `AdoptGraph`, `AdoptGraph` ends in `RequestRun()`, and `RequestRun` under the default Automatic
mode is `_ = EvaluateGraphAsync()` — a run nobody awaits. When it lands it writes
`DiagnosticsText = Summarise(result)`. The test's `TryExportSolids` had already written *There are
no solids in the viewport to export* into the same property; on a loaded machine the constructor's
run landed in the gap between that write and the assertion that reads it, and the assertion saw
`0 nodes evaluated, 0 served from cache. No diagnostics.` instead.

**The fix is a drain, and it is deterministic rather than a sleep.** `await model.EvaluateAsync()`
straight after the constructor. `EvaluateAsync` runs synchronously up to `await
_session.EvaluateAsync()`, so the constructor's run is *already inside* the session by the time
the constructor returns — which means a later call always supersedes it, and a superseded run
returns null and exits before it can touch anything. There is no window in which both apply.
Thirteen tests in `MainWindowViewModelTests` already open this way; what was missing was the
reason, so a fourteenth was written without it.

**A second test had the same defect and had not failed yet.** `RunModeTests.ManualRecordsTheEditAndWaits`
asserts that `StatusText` contains *Manual*, and the constructor's run overwrites `StatusText` with
its own summary on exactly the same path. It was fixed in the same change rather than waiting for
the day it decided to fail. **A flake that has not fired is not a different defect from one that
has** — it is the same defect with better luck, and luck is what a loaded machine takes away.

**What to take from it.** Any test that reads a `MainWindowViewModel` property the evaluation
pipeline also writes — `DiagnosticsText`, `StatusText`, `Inspector`, `Scene` — must drain the
constructor's run first, whether or not it currently passes. And the wider lesson is about the
gates rather than the code: **step 3 of the session protocol exists for this.** Running the suite
before adding anything is what separated *I inherited this* from *I broke this*, and it cost one
run to find out.

**The prediction was tested the same afternoon, and it held.** The next full-suite run — the one
verifying `E2-T40`'s polar construction, a change that touches no UI code at all — failed
`UndoRedoTests.CommittingTheSameLiteralTwiceIsOneStep`, which reads `model.Inspector` after
`ShowSelection`. The constructor's run ends in `RefreshInspector()`, so the row the test is holding
is replaced underneath it and the commit lands on a discarded view model. A grep for the pattern
this note describes — a `MainWindowViewModel` constructed, one of those four properties read, no
`EvaluateAsync` anywhere — found **five** candidates; four were real and were drained, and one was
a false positive where *Inspector* was a pane's name rather than the collection.

**So the count is six tests, and only two of them had ever failed.** That is the argument for
fixing a flake by its *shape* rather than by its symptom: the shape found four more, and the
alternative was meeting them one at a time, months apart, each time in the middle of unrelated
work — which is exactly how this one was met.

## N142 — Two types, two opposite answers to a bad argument, and both are right

`E2-T40` closed on the same afternoon it added polar construction to `Point3d` and a least-squares
fit to `Plane`, which put the two rules side by side where the difference is impossible to miss:

- `Point3d.FromSpherical(double.NaN, …)` returns a point. It answers `false` to `IsValid`.
- `Plane.FromBestFit(collinearPoints)` throws.

**That is not an inconsistency, and writing it down is cheaper than having the argument again.**
`Point3d` has a representable invalid state and it is load-bearing — `Unset` *is* three `NaN`s, and
the whole type is built so that a missing position can travel through arithmetic and be tested for
at the end, where the caller is already looking. Throwing would make the factories the only members
of the type that cannot express *no position*. `Plane` has no such state: `default(Plane)` has a
zero normal, every geometric member throws on it, and the type's design rests on a `Plane` you are
holding being a real one. A factory that handed back an invalid `Plane` would break the guarantee
the rest of the type is built on.

**So the rule is about the type, not about the argument.** *Does an invalid value of this type mean
something?* If yes, return one. If no, refuse. Both are documented on the members themselves, each
pointing at the other, because the next reader will meet one of them first and wonder.

## N143 — A relative threshold is the only kind that survives a change of units

`Plane.FromBestFit` refuses points that are collinear — and, more usefully, points that are *nearly*
collinear, where a plane exists arithmetically and its normal is decided by rounding error. The cut
has to be somewhere, and the tempting version is a small absolute number.

**An absolute cut is wrong here, and it is wrong in a way that only shows up in somebody else's
model.** The quantities being compared are the principal minors of a covariance matrix: sums of
*fourth* powers of coordinates. Change from metres to millimetres and every one of them moves by
10¹². A threshold that correctly rejects a wobbly line in a building model would accept it in a
model of the same building drawn in millimetres, and reject a perfectly good plane in a site plan
drawn in kilometres. The same design in three unit systems would get three different answers, and
nothing in the failure would point at units.

**The cut is therefore a ratio: the largest minor against the square of the covariance trace.** Both
sides scale identically, so the test means the same thing at every size — and Spark's coordinates are
unitless by design, which makes *any* absolute geometric constant in the kernel a suspect. Exactly
collinear points in double arithmetic produce a ratio around `1e-16`; the cut is `1e-12`, four orders
of magnitude of margin, and a named test fits both a millimetre-scale triangle and a
thousand-kilometre one to prove the invariance rather than assert it.

**The general form.** Any comparison in the kernel between two quantities derived from coordinates
should be a ratio of like things, or should take a `Tolerance` and let the caller say what scale they
are working at. A bare constant compared against a length, an area or a moment is a units bug waiting
for a user who works in different ones.

## N144 — Removing a reference is not removing an import

`ReferenceCatalog` derives two things from its references: the list the compiler sees, and the
prelude — the `using` lines every block starts with, which since `E7-T21` includes the namespaces
of libraries a user added. `Add` rebuilt both. `Remove` filtered the first and kept the second, so
a removed library's `using Gadgetry;` stayed in the prelude with no assembly behind it, and
**every** block then failed `CS0246` — on a line the user did not write, naming a namespace they
had not asked for in this graph. The path also stayed in `_libraries`, so the next `Add` would have
put the import straight back even had the snapshot been right.

**Nothing noticed, because nothing removed.** Removing meant a user deleting one row from Local
assemblies, which nobody had done with a library whose namespaces were imported. `E7-T16` made it
the common case: opening a second graph releases the first graph's packages.

**The general form.** When a type derives two things from one list, every mutation of the list
re-derives both, through one method. `PreludeFor` is now that method, called by `Build` and by
`RemoveWhere`, and `RemovingALibraryTakesItsNamespacesWithIt` removes a library and asserts that a
plain block still compiles — watched red before the fix.

## N145 — Two instances of a record over one file overwrite each other

`PackageTrustStore` reads its file once, in its constructor, and `Save` writes its whole in-memory
set back. That is right for one instance and wrong for two. The Packages window built its own, and
`E7-T16`'s gate would have built a second over the same `trusted.json`. Each would miss the other's
decisions — a library added through the window would be asked about again on the next open — and
the next save from either would delete what the other had recorded.

**Both failures point the safe way**, which is why neither would have been reported as a bug: the
user is asked again, and it reads as Spark forgetting rather than as Spark losing data.

**Fixed by ownership rather than by locking or re-reading.** The view model owns one
`PackageTrust` and hands it to both, and `ThePackagesWindowAndTheGateShareOneRecord` asserts they
are the same instance. `ScriptTrustStore` and `LocalReferenceStore` have the same shape and one
instance each today; a second consumer of either needs the same treatment, not a second `new`.

## N146 — A code block's key is written into the file, so it cannot see the catalogue

A code block's `NodeKey` is `Spark.Scripting/CodeBlock#` plus `ContentHash` — the script, its
input types and the shared declarations — and it does two jobs. `CanvasGraph.RebuildScripts`
compares it to decide whether a block's meaning moved, and the evaluation cache keys results on it.
Both jobs want it to change when the assemblies a block compiles against change, and **it does
not**: adding a library leaves every key exactly where it was.

**That looked like the bug, and putting the catalogue into the hash looked like the fix.** It is
not, because the key has a third job: `GraphDocument` writes it into the `.spark` file. A hash that
moved with the session's references would make one graph save differently on a machine that has
its packages than on one that does not — which is `E7-T7`'s byte-for-byte re-save broken for
precisely the graph `E7-T17` is about, the one naming a package you do not have.

**So a catalogue change is signalled, not inferred.** Agreeing to a graph's packages calls
`RebuildScripts(force: true)`, which replaces every block regardless of key, and then
`SparkSession.Replace` on the same graph for a fresh cache epoch — without the second, a rebuilt
block with an unchanged key is served the failure its predecessor computed.
`AgreeingRebuildsTheBlocksThatWereCompiledWithoutIt` asserts both halves and was red against the
unforced rebuild.

**Every other path was exposed too, and `E7-T24` closed them the same day — in one place rather
than one at a time.** `EvaluateAsync` compares the catalogue's version with the one the canvas's
blocks were compiled against and, when it has moved, does exactly what agreeing does: a forced
rebuild and a fresh epoch. *Add as a library…*, Local assemblies, a graph's package folder and a
graph being closed all move that version, and none of them has to know. The version is recorded at
the **start** of adopting a graph, because the graph arrives already compiled and the release of
the previous graph's packages that follows is a change its blocks have not seen.

## N147 — Save asks where before it serialises, because the file depends on its own name

Save used to serialise the graph first and ask where to put it second, which was reasonable while a
`.spark` file's contents were independent of its name. `E7-T17` ended that. The list of packages a
graph expects is written relative to the file, as `<name>.packages/<entry>`, so Save As to
`renamed.spark` has to write `renamed.packages/…` — and the text cannot be produced until the name
is known. The view now asks, then calls `TrySaveDocument(target)`.

**What moved with it.** A graph that cannot be written — a literal the format cannot hold — is now
reported after the file dialog rather than before it. Nothing is written in its place, so nothing is
lost; it costs one dialog the user did not need.

**Do not move it back.** Serialising first and writing the result under a different name produces a
file whose list names a folder beside some other file, and the next open reports every package
absent — correctly, and for no reason the user could see.

## N148 — A screenshot ran the graph it was photographing

`--screenshot` captures the shell once the first evaluation has landed, and to be sure there *is* a
first evaluation, `CaptureWhenReadyAsync` ran one itself — `await model.EvaluateAsync()`,
unconditionally, before the shutter. That is right for a demo graph and was a trust bypass for
anything else. `E6-T16` holds a graph with code blocks back until the user agrees, `E6-T40` made
`--open` do the same — and the screenshot path then ran it anyway, a few hundred milliseconds later,
from the view rather than the view model. Fixing the constructor alone would have closed one door
beside another left open.

**A screenshot is not consent.** The capture now runs the graph only when nothing is waiting for
trust, and a held-back graph is photographed as a user would see it: unrun, with its banner.

**The general form.** `EvaluateAsync` is public and runs whatever is on the canvas. Every caller
outside the trust decision — a pose, a benchmark, a startup switch — has to ask `IsAwaitingTrust`
first, or it is a way round the rule.

## N149 — The command line holds a graph's DLLs to a stricter rule than its code blocks

`spark run` and `spark check` compile and run a graph's code blocks without asking. That is
`E6-T16`'s posture for the command line: a build runs its own graphs, and `--no-script` is how it
declines somebody else's. Since `E7-T25` the same verbs **refuse** a graph whose `.packages` folder
holds an assembly nobody has agreed to, unless `--trust-packages` is given. That looks inconsistent,
and since a code block can already do anything a DLL can, it looks like theatre as well. It is
neither.

**The code is in the file; the DLL is not.** A code block's source is text inside the `.spark` — it
arrives in a diff, a reviewer reads it in a pull request, and `git blame` says who wrote it. An
assembly beside the graph is opaque bytes that can change without the graph changing at all. A
build that runs a reviewed graph has in effect agreed to its code blocks; it has agreed to nothing
about a binary that arrived in the same folder.

**And the desktop application already asks about exactly these bytes.** Consent is per SHA-256,
given once in the window (`E7-T16`), so the ordinary case — a graph somebody opened in Spark and
agreed to — passes on the command line with nothing added. Only what no person has looked at is
refused.

**What would be wrong: making `--trust-packages` record its decision.** The trust file is shared
with the desktop application, so a scheduled job would silently become consent for the window as
well. It references for one run and writes nothing — `GraphPackageGate.AgreeOnce`.

**And do not "fix" the asymmetry by gating code blocks on the command line** as a side effect of
something else. It would break every build script that runs its own graphs today, and whether it
should is `E6-T16`'s decision to revisit on its own terms.

## N150 — No encoder setting writes an emoji literally, and the relaxed one is the only one that leaves `+` alone

Two facts about `System.Text.Encodings.Web` that `E3-T24` ([ADR-0026](adr/0026-spark-file-text-is-written-as-typed.md))
turned on, and that the next person to touch `SparkFile`'s encoder will otherwise rediscover.

**A character beyond the Basic Multilingual Plane is always escaped.** `TextEncoderSettings` and
`UnicodeRanges` describe the BMP only, so no allowed range can include a surrogate pair, and even
`UnsafeRelaxedJsonEscaping` writes `🔥` as `\uD83D\uDD25`. It reads back exactly and re-saves byte
for byte — `ACharacterBeyondTheBasicPlaneRoundTripsAndIsByteStable` asserts all three, the spelling
included, so this note fails a test when it stops being true. Writing one literally would mean
writing JSON strings by hand, which ADR-0026 declined.

**`JavaScriptEncoder.Create(UnicodeRanges.All)` is not the relaxed encoder.** It lets non-ASCII
through, and it still escapes `+`, `<`, `>`, `&`, `'` and the quotation mark, because the built-in
encoders treat the HTML-sensitive characters as forbidden whatever ranges they are given. It looks
like the conservative choice and does half the job; the code block's `+` is the half it misses.

## N151 — Assigning a `TopoDS_Shape` is not a copy, and meshing is not read-only

`spark_occt_tessellate` wrote `TopoDS_Shape meshed = shape->shape;` under a comment saying it made a
copy so that the caller's shape would not change. `TopoDS_Shape` is a handle — a location, an
orientation and a pointer to a shared `TShape` — so the assignment copied the pointer and every face
stayed shared. `BRepMesh_IncrementalMesh` stores each face's triangulation **on the face**, so the
caller's shape changed after all, and the mesher's rule of keeping an existing triangulation finer
than the one requested turned that into a cache keyed on the shape and nothing else: N87's 1,099,460
triangles for a coarse request whose fresh answer is 1,332. Two threads tessellating one shape wrote
into the same faces as well.

**A real copy is `BRepBuilderAPI_Copy(shape, true, false)`** — geometry copied, mesh not. That is
the proper fix, two lines in the shim, and it waits on `E13-T21` because the OpenCascade install the
shim was built against is gone from this machine.

**What ships instead is managed** (`OcctBrepKernel.Place`, `E12-T20`). A held shape's first
tessellation meshes the shape itself; every later tolerance meshes a fresh import of the shape read
back out, which shares nothing with it; and a per-shape, per-tolerance cache serves exact repeats.
Correct at every tolerance, and the viewport's one-tolerance case costs what it always did. If the
fresh import ever fails, the shape itself is meshed, which is the old behaviour and never worse.

**One residue, benign and stated.** A boolean keeps the faces it did not touch as the input's own
faces, so a triangulation written into an input can be inherited by the result's first, in-place
tessellation — finer than asked for in places, never wrong. The deep copy in the shim removes that
too.

**The general form.** In OpenCascade, `=` on a shape shares and `BRepBuilderAPI_Copy` copies — and
anything that caches onto topology, meshing first among them, changes every shape sharing it.

## N152 — Converged is not closest: Newton on a closest-point query can stop on a saddle

`Surface.ClosestPoint` runs Newton on the two orthogonality conditions, `(S − P)·S_u = 0` and
`(S − P)·S_v = 0`. Those hold wherever the distance is **stationary** — at a minimum, and also at a
saddle. A step-size test cannot tell them apart, so "Newton converged" meant "found a stationary
point", and the code read it as "found the answer".

**Where it bit (`E2-T62`).** A line perpendicular to an axis, revolved, sweeps a flat annulus whose
profile runs tangent to its circle at `v = 0`, so the parameterisation folds there: `S_u` and `S_v`
are parallel on that edge. For a point 1% along `v`, the seed landed on the fold, the reseed stepped
half a cell in, and Newton's first step overshot below `v = 0` and was clamped back onto the fold —
where, because the radius changes only at second order in `v`, both conditions hold. Newton converged
in three steps to the fold's nearest point, a saddle 8.5e-5 of the reach from the answer, and did so
at every scale. **Two plausible hypotheses were measured and rejected first** — a line search
comparing with the wrong distance, and an indefinite Hessian refusing every step — and a trace of
the actual iteration is what found it. Neither of those changes shipped.

**The fix** (`Surface.EscapeSaddle`): at a converged point, the distance's Hessian — the very
matrix Newton solves with — has a clearly negative eigenvalue at a saddle and none at a minimum.
Moving along that eigenvector, with halvings, finds a closer point, and Newton resumes from it. A
minimum costs one evaluation of the second derivatives to confirm. The threshold is relative,
because on a fold a true minimum's matrix is singular and rounding gives its zero eigenvalue a sign.

**And exactly on the fold (`E2-T63`), a different mechanism.** A point on the rim itself is a
genuine minimum with a singular Hessian — the distance is quartic in `v` there — and one such point
was answered 1.8e-5 of the reach away, identically before and after the saddle fix. A trace showed the
reseed stepping *off* the rim, because `ReseedOffDegeneracy` treats the smaller derivative as the
collapsed one and on a fold neither is; Newton's first step from there was enormously better than the
reseed and a hair worse than the seed, and `Descend` compared with the seed, so it refused it and
`Contract` crept. **The line search now compares with where the iteration is, and the best point is
kept apart** — the change that was measured and rejected for `E2-T62`, where nothing reached it, and
is proved here by a query that does.

**A third change was tried and taken out.** On the rim Newton goes linear, every step two thirds of the
last, so stretching a step by 1/(1 − r) once the ratio r settled finished one rim point off exactly —
and made a point just past the seam's other end worse by eight orders of magnitude, found only because
the property was measured at a tighter bound than it asserts. So a rim point stops about 1e-10 of the
reach away, which is where the property's bound comes from. **Measure a bound one order tighter than
you assert**: that is where the next defect is.

## N153 — Crash recovery is off in the view model until the window turns it on

`E8-T13` keeps a working copy of every unsaved document so that a process killed by user C# (R11)
loses nothing. The copies live in `Spark/recovery` under the user's local application data, and the
next start offers any left by a session that died. **`MainWindowViewModel.Recovery` defaults to a
store with no folder, which keeps nothing**, and only `MainWindow.OnOpened` calls `EnableRecovery`.

**Why the obvious default is wrong.** Well over a thousand headless tests build a
`MainWindowViewModel` and edit it. Had the default been the real folder, every test run would have
written working copies there, many from models never disposed — and since the test process is gone
by the time Spark next starts, **the real application would have offered the user a test run's graphs
as their own lost work**. A test that wants recovery points `Recovery` at a scratch folder.

**Two more facts that look like choices and are not.**

- **A copy records the process that wrote it, and a live process's copy is never offered**, because
  a second Spark started beside the first must not present the first one's open work as lost. The
  check compares the process *name* as well as its id: ids are reused, and a crashed Spark's id
  belonging to some other program by the next start would otherwise hide the user's work for as long
  as that program ran. A test fakes a crash through `RecoveryStore.At(folder, isRunning: _ => false)`,
  because in a test the process that wrote a copy is the test itself.
- **The copy follows `IsModified`, not the edit count** — `KeepRecovery` runs from `RefreshHistory`
  and `MarkSaved` — so undoing back to the saved state deletes it, and a restored graph opens
  *modified*, because the file on disk does not hold it.

## N154 — A cancellation-token overload reaches every test that calls the method

`E3-T12` gave `Tessellation.Tessellate` and `ToMesh` overloads that take a `CancellationToken`,
beside the existing forms, which is the additive change AGENTS.md asks for. Two analyzers objected,
and both objections are worth knowing before the next overload of this kind.

**RS0027** forbids an overload with more parameters than an existing overload that has optional
ones. It protects the source compatibility of a *published* library's future overloads; Spark
publishes nothing (ADR-0019), so it is off in `.editorconfig` beside RS0026, for the same stated
reason.

**xUnit1051** asks every test that calls a method with a token-taking overload to pass
`TestContext.Current.CancellationToken`, so that a cancelled test run stops promptly. Adding the
overloads therefore made **every existing test call to `ToMesh` an error** — sixteen of them, in a file
nobody had touched. This repository's convention is to comply rather than suppress (the engine tests
already pass the test context's token), so the sites were patched — **by the compiler's own list**,
not by a text search: `Solid.ToMesh` in `Spark.Nodes.Core` shares the name and has no token overload,
and appending a token to it would not have compiled. The one call that must stay token-free, because
it proves the old signature still produces the old mesh, carries a local `#pragma` saying so.

**The general form.** Adding a token overload to a public method is cheap in the library and costs a
pass over every test that calls it. Budget for it, and patch from the diagnostics.

## N155 — The code-example generator assumed every method parameter is a port

`NodeImporter.MethodExample` writes the one-line C# a node's help and code blocks show — `return
Surface.ToMesh(surface, tolerance);` — by walking the method's parameters and taking the next input
port's name for each. That is only right while every non-`out` parameter *is* an input port, and until
`E3-T12` it always was.

**`E3-T12` made the first parameter that is not a port**: a `CancellationToken`, which the importer
now fills from the evaluation rather than exposing. The walk then ran one past the end of the port
list, and the `ArgumentOutOfRangeException` escaped the importer — **aborting the import of the whole
first-party library**, because `Spark.Nodes.Core.Surface.ToMesh` was the first adopter. Two unrelated
tests (`NodeMemberKindTests`, `XmlDocumentationTests`) found it, not the new ones: they import the real
library, and the new tests imported a fixture of their own.

**The fix skips the token there as it is skipped in the port loop** — left out when it is optional,
`CancellationToken.None` when it is required, so the example still compiles. **The general form**:
the importer has three places that map parameters — the port loop, the invoker's argument builder
and this example — and a parameter that is not a port has to be taught to all three. Test a new
kind against the real first-party library, not only against a fixture.

---

## N156 — One row in 89 where the name matched and the capability did not

`E11-T23` step A seeded 89 `Done` rows by an exact member-name match on the mapped Spark type and
marked every one *matched by name; not yet reviewed*, because a name match is a hypothesis, not
evidence. Step B reviewed all 89 by hand. **One was wrong**, and it is worth recording which kind.

`PolySurface.Surfaces()` had matched `Spark.Geometry.Brep.Surfaces()`. Dynamo's returns the
PolySurface's **trimmed** faces as surfaces. Spark's returns the **untrimmed** surface table in face
index order — the geometry a face is a trimmed window onto, which is what an index-based topology
stores. Same name, same return shape, same arity, different answer for any face with a hole in it.
The row is now `Unassessed` with that reason: the faithful equivalent needs a trim's pcurves, which
`E2-T64` carries.

**The other 88 held**, and 24 of them are `ToString`, `Equals` and `GetHashCode` — presence and
nothing more, now said so in their reasons rather than counted as if they closed a gap.

**The rate is the point, not the one row.** One in 89 is low enough that the seeding was worth doing
and high enough that shipping it unreviewed would have put a false claim in a register whose whole
value is that it is consulted with confidence. **The marker in the data is what made the review
finite**: a `Done` row that carries *how it was decided* can be re-examined, and one that does not
cannot be told apart from a row somebody actually checked.

---

## N157 — An exact-match budget is a good guard and a noisy one, and the reason field is what pays for it

`E11-T23` step B gave the Dynamo parity register a **residue budget**: the number of public
`Spark.Geometry` members that no parity row names and no exclusion rule excuses, checked for
**exact** equality rather than as a ceiling. The argument for exactness is sound — a ceiling lets
the number sit where it is for ever, and the point of the number is that it falls as the review
proceeds.

**It rose twice within a day of being written, and both rises were correct.**

- **278 → 287.** `E2-T44` assessed the topology rows and named 13 Spark members. Three of those
  members lived on types the exclusions file excused *wholesale* — `BrepFaceView`, `BrepLoopView`,
  `BrepEdgeView` — and the check refuses a wholesale excuse for a type some row names, on purpose.
  Deleting those three rules moved their remaining members into the residue.
- **287 → 293.** `E2-T65` added `BrepAdjacency`. Eight of its members are named by parity rows; the
  other six — `Of` and five counts — are its own machinery, and they cannot be excused by type
  because the type is named.

**So the budget's number changes whenever the public surface does, not only when the review
advances**, and a session that reads *a rise means drift* without reading further will draw the
wrong conclusion twice on the first day. **The fix was not to loosen the check.** A ceiling would
have absorbed all three changes silently and the register would have learnt nothing. The fix is
that the budget's `Reason` cell carries the *history*: each rise, its date, its task and its cause,
so the next reader sees either a rise with an explanation beside it or a rise with none, and those
mean different things.

**The general form.** An exact-match guard over a derived number is worth having, and its running
cost is a sentence of prose per change. Budget for the prose when you choose exactness — a guard
whose failures are routinely dismissed is worse than no guard, and the only thing that stops
dismissal becoming a habit is that each failure arrives with a place to write down why.

---

## N158 — A register scoped to an assembly measures the assembly, not the capability

`E11-T23` built the Dynamo parity register against `Spark.Geometry.dll`, and both of its
member-level checks read that one assembly: a `Done` row must name a member it declares, and every
member it declares must be named, excused or counted. That was the right scope for values, curves
and topology, and it is `Spark.Docs.Verify`'s whole discipline — reference no Spark project, load
what you police from disk as metadata.

**`E2-T42`'s assessment is where the scope first cost something.** Twelve of `Surface`'s 46 rows are
loft, sweep, thicken and the surface and solid booleans. **Spark does all of them.** They live behind
`Spark.Api.IBrepKernel`, with `Spark.Nodes.Core` families over them, and `--graph solids` has fused,
drilled, hollowed and filleted since M6. But no row could be marked `Done`, because the member the
row would name is in a different assembly — so twelve rows now say `Planned` about capabilities that
already work.

**That is the register lying in the safe direction, and it is still lying.** A reader consulting it
for *what can Spark do* gets a No where the answer is Yes, and the conservative direction of the
error is exactly what makes it survive review. The rows say where the capability actually lives
rather than pretending, and `E11-T30` carries the fix.

**The lesson is about what a scope decision asserts.** Choosing an assembly to measure looked like a
test-plumbing decision — which DLL does the harness open — and it was a decision about the
register's *subject*. FR-81 and [DYNAMO-COVERAGE §1](DYNAMO-COVERAGE.md) both say the subject is
capability parity, so the scope should have followed the capability across the seam from the start.
It did not, because `Spark.Geometry` was the whole of the geometry when the check was written and
the question never came up.

**A second, smaller instance of the same thing, found the same day.** `Surface.ByRevolve` is
answered by `new RevolutionSurface(...)` — a *constructor*, which the inventory's counting rule
deliberately excludes. So the row names the **type**, which is correct and which the rename-catcher
handles. The reverse direction did not: it split every `SparkMember` at its last dot, so
`Spark.Geometry.RevolutionSurface` was filed as a member `RevolutionSurface` of a type
`Spark.Geometry`, and **naming a bare type was a silent way past the rule that a type a row names
cannot be excused wholesale**. One line of parsing, and a hole in the one rule that stops the
exclusions file being a way to make the check green.

**Both have the same shape:** a check that agrees with you is not evidence, and the parts of it you
never questioned are where the disagreement hides. Ask what the check's *scope* claims, not only
what its assertions claim.

**Fixed the same day** (`E11-T30`), and the fix is worth recording because of what it cost and what
it did not. The rename-catcher now reads `Spark.Geometry`, `Spark.Api` and `Spark.Nodes.Core`; **the
reverse direction was deliberately left on `Spark.Geometry` alone**, because it asks a different
question — has the *kernel's own* surface drifted from the plan — and `Spark.Nodes.Core`'s families
exist to be imported by reflection and would swamp the residue budget that makes the check worth
running. **The budget's number was unchanged across the commit, and that is the evidence the two
scopes stayed apart.** Twelve rows became `Done` with no geometry written.

**`Surface.Repair()` is the case to remember.** It stood at `Not planned`, on an argument that is
entirely true: healing and sewing are behind the kernel seam by decision, because OCCT's `ShapeFix`
does them and a second managed implementation would be worse. Every clause of that is correct, and
the conclusion drawn from it — *therefore Spark does not do this* — was wrong. **Behind the seam is
still delivered.** A true premise reached a false conclusion because the unstated middle step, *and
the register measures what `Spark.Geometry` declares*, was never written down anywhere to be
argued with.

---

## N159 — An index that crosses a seam is not the same index on the other side

Spark's BRep topology is index-based by `E2-T22`: a face is an `int` into `Brep.Faces()`, an edge an
`int` into `Brep.Edges()`. `IBrepKernel` takes those ints — `Fillet(solid, edges, …)`,
`Shell(solid, facesToOpen, …)`, `Chamfer`, `Draft` — and `OcctBrepKernel` passes them **straight
through to the shim**, where OCCT numbers its own faces in its own explorer order.

**They are not the same numbers.** On a 3×3×6 block, `BrepFaceView.NormalAt` says managed face 1 is
the top, normal `(0,0,1)`. Shelling `[1]` opens a *side*: the volume comes back 24.256, and a z-face
opening gives 26.896. Managed face 2, which the model calls a side, behaves like a z-face.

**Two things made this invisible for as long as it has existed.**

1. **Every caller in the product passes an empty list, meaning *all of them*.** `Solid.FilletAll`
   rounds every edge; `Solid.Hollow` opens none. **A permutation of a whole set is that set**, so the
   mismatch cancels exactly at every shipped call site. `--graph solids` has been right all along.
2. **So had every test.** `Shell` had two call sites and `Fillet` seven, and all of them passed `[]`.
   The suite had complete coverage of the operations and *zero* coverage of the argument.

**It was found by writing a node that needed the argument.** `Solid.HollowOpen` — hollow, and open
the face that looks a given way — was written, given a test that asserted *which* face by volume on a
block that is not a cube, found broken, and deleted in the same step. Had the test asserted a face
count, or asserted volume on a **cube**, it would have passed and the node would have shipped.

**The general form, and it is not really about OCCT.** An index is meaningful only inside the
collection that defines its order. Handing one across a boundary silently asserts that both sides
agree on that order, and nothing checks it — there is no type error, no null, no exception, just a
plausible answer to a question nobody asked. `ADR-0021` put the managed model and the provider's
model deliberately far apart; **an `int` is the one thing that will cross that distance without
anybody noticing it has.** Where an index must cross a seam, either carry an identity the far side
can verify, or make the near side prove the two orders agree — and if neither is possible, only the
whole-set call is safe, which is exactly the shape the product had stumbled into.

`E13-T22`, pinned by `SolidNodeTests` and blocked on `E13-T21` for the shim rebuild.

---

## N160 — A register can only be wrong in the safe direction, and that is why nobody catches it

`E2-T45` assessed the 65 mesh rows and found two members of the parity harness saying *absent* about
capabilities Spark has shipped for weeks. Neither was a mistake anybody made carelessly, and both
have the same shape as `N158`, which is why this note is about the shape rather than the two bugs.

**The first: the rename-catcher read four assemblies and interchange lives in a fifth.**
`Mesh.ImportFile` and `Mesh.ExportMeshes` are STL, PLY, OBJ and glTF, and all four are in
`Spark.Geometry.Io`. The check's `Delivering` list held `Spark.Geometry`, `Spark.Api` and
`Spark.Nodes.Core`, so two rows that should have been `Done` since M5 could only be marked `Planned`.
This is exactly `E11-T30`'s correction, one assembly along.

**The second: an exclusion with a true premise and a false conclusion.** The exclusions file had
`Spark.Geometry.GeometryJson` down as a whole type Dynamo has no counterpart for — *"Dynamo's is
SAT/SAB and `FromJson`/`ToJson`, refused in §5 [g]"*. The premise is true: §5 [g] does refuse SAT and
SAB. The conclusion is false, because §5 [g] does not mention `ToJson` at all, and §3.8 of the same
document says plainly that `ToJson`/`FromJson` are **planned** under FR-57. Both halves of the
sentence were written by someone reading the right paragraph and finishing it in their head.
`Surface.Repair()` was the same error a day earlier (`N158`), and `PolySurface.Surfaces()` (`N156`)
is its mirror — a name that matched where the capability did not.

**The asymmetry is the point.** A register that claims too much is caught the moment somebody tries
the member: the name is not there, the rename-catcher goes red, a user reports it. **A register that
claims too little is never caught by anything**, because nothing in the world contradicts *we have
not built this yet*. It reads as modest, it survives every test, and its only symptom is work
scheduled that is already done. Four subsystem assessments in three days have produced four of these
and zero of the opposite kind — §3.3 read *0 reachable* while the kernel lofted and swept, §3.4 read
*0* while `--graph solids` drilled holes, §3.6 read *0* while the viewport rendered meshes it had
written to disk.

**So the practice, and it is the only one that works:** an assessment reads the **assembly**, never
the document, and never the last assessment. The document is the thing under test.

**A second-order effect worth knowing before it surprises somebody.** The reverse direction's residue
budget went **up**, 285 to 292, on a step that assessed 60 rows. That is not drift. Naming
`MeshTopology.EdgeCount` from a parity row means `MeshTopology` is a type the register has claimed,
so it may no longer be excused wholesale, so its other nine members come into the count. Assessment
moves members from *excused by rule* into *named or counted*, and the second bucket is the one with a
number on it. **A rise therefore has two causes that look identical** — a member added to a mapped
type with no thought for the register, and an assessment doing precisely what it should — and the
only thing that tells them apart is the history written into the budget row at the time. Write it
then, or it cannot be reconstructed later.

---

## N161 — A capability can be complete on every type but one, and a base-class register cannot see it

`E2-T46` assessed `Geometry`'s 47 members and marked the whole 14-member transformation family
`Done`, correctly: `Transform.Translation`, `Rotation`, `Scale`, `Mirror`, `ChangeBasis` and
`PlaneToPlane` exist, and `TransformedBy` applies them. **And a `Brep` cannot be moved.**

`TransformedBy` is on `Curve`, `Surface`, `Mesh`, `PointCloud`, `PolyCurve`, `PolyLine` and all seven
analytic surfaces. It is not on `Brep`. `IBrepKernel` has no transform operation — the list is
`Union`, `Difference`, `Intersection`, `Extrude`, `Revolve`, `Loft`, `Sweep`, `Patch`, `Fillet`,
`Chamfer`, `Shell`, `Split`, `Trim`, `Offset`, `Thicken`, `Draft`, `Sew`, `Heal`, `ReadFile`,
`WriteFile`, `Tessellate` — and `Spark.Nodes.Core` has no node for it. A user can union two solids,
fillet every edge, hollow the result and write it to STEP, and cannot translate it by a vector.

**The register could not have found this, and it is worth understanding why before trusting the next
green run.** A parity row asks *is this capability reachable*, and for a member on an abstract base
the honest answer is *yes, on the types that carry it*. Dynamo puts `Translate` on `Geometry`, so one
row covers eleven Spark types; eleven of twelve answering is indistinguishable from twelve of twelve
at the granularity the register has. **A per-type matrix would show it and a per-member list cannot**,
and the register is a per-member list on purpose, because a matrix of 837 members against 66 types is
the artefact nobody maintains.

**So the practice that actually catches this class of gap**: when a row is `Done` because *some*
types carry the member, **enumerate the types that do not, in the section, in writing**. It costs a
sentence and it is the only step in the assessment where the answer is not already in the manifest.
§3.8 now carries that sentence for the transformation family.

**And a second-order note, because a transform on a `Brep` is not a matrix multiply.** A `Brep` may
be **resident in the provider** (ADR-0021): the authoritative shape is OCCT's and the managed arrays
are materialised lazily. Multiplying the managed points while the provider holds the real shape
produces a `Brep` whose two halves disagree — which is the exact bug ADR-0021 was written to prevent,
and it will not show up in a test that only reads the managed side. The transform either goes through
the kernel, or it invalidates residency.

**`E2-T70` step A landed the same day and chose the second**, and the choice was already written
down: `BrepResidency.Materialise`'s own remarks name *a transform* as one of the structural demands
that read a shape out of its provider. So `Brep.TransformedBy` materialises and the result is a
managed value. The first option is not available anyway — `IBrepKernel` has no transform operation,
and `E13-T21` blocks adding one while the shim cannot be rebuilt.

**What the fix actually turned up was a live bug somewhere else.** Getting `Brep.TransformedBy`
right meant flipping every `BrepFace.IsReversed` when `Transform.Determinant < 0`, because a mirror
reverses handedness and a surface's own normal is the cross product of its parameter directions.
Asking whether `Mesh.TransformedBy` did the equivalent showed that it did **not**: it moved the
vertices, inverse-transposed the normals correctly, and left every face wound backwards. So a
mirrored mesh had winding disagreeing with its own normals — negative volume, `FaceNormal` answering
inwards, and it rendered black. **Nothing had reported it**, and the reason is the same as for the
missing `Brep` transform: mirroring is rare enough that nobody had done it, and every cheap check
passes. The general form is that **orientation is a property no index checks and no validator
validates**, so it survives every structural test a model has.
---

## N162 — The residue budget's direction says what kind of assessment you just ran

**The number was designed to catch drift and it turned out to measure something else as well.**
`tests/corpus/dynamo-parity-exclusions.tsv` carries a residue budget: the count of public
`Spark.Geometry` members that no parity row names and no rule excuses, checked for **exact**
equality on every build. Its stated job is to stop a member being added to a mapped type without a
thought for the register. Six assessment passes in, the *direction* it moves has turned out to be
the cheapest available summary of what a pass actually did.

**Two things move it, and they move it opposite ways.**

- **Naming a member lowers it by one.** An assessment that works through a type the register
  already watches converts residue into rows, which is the point of the exercise.
- **Naming a *type* raises it by that type's whole surface.** A `Done` row may legitimately name a
  bare type when the capability is a construction — `Surface.ByRevolve` is
  `new RevolutionSurface(...)`, and a constructor is not a member this inventory counts. But a type
  a row names is a type that may no longer be excused wholesale, so every one of its *other* members
  arrives in the residue at once. Constructors are not counted; everything else is, `const` fields
  included.

**The history bears it out, and it is not a small effect.** `E2-T46` assessed §3.8's 89
infrastructure rows and the residue went **292 → 319**: three members named, and two types —
`Transform` with 28 members and `Tessellation` with two — claimed and therefore un-excused.
`E2-T41` step A assessed §3.2's 82 `Curve` rows, a section of almost the same size, and the residue
went **321 → 318**: thirteen members named, two types claimed (`CurveOffset` and `ExtrusionSurface`,
ten members between them). **Same shape of work, a 27-member rise against a 3-member fall**, and the
difference is entirely whether the section's `Done` rows land on a type the register had already
accounted for.

**Why this is worth a note rather than a shrug.** The rise is not a defect and the fall is not a
virtue — `E2-T46` was right to claim `Transform`, because §3.8's whole argument is that Spark puts
the transformation algebra on one type where Dynamo repeats a member per type, and excusing that
type wholesale while claiming it was the contradiction the check exists to catch. What the direction
tells you is **how much unaccounted-for surface the section dragged in with it**, and that is a
number worth knowing *before* planning the next pass rather than after running it. A section whose
capabilities are constructions will cost; a section whose capabilities are methods on types already
in the register will pay.

**The practical rule.** When an assessment's residue rises, the budget entry has to say which type
was claimed and how many members came with it — not as an apology, but because a rise with no
explanation beside it is indistinguishable from the drift the check was built to catch. The
exclusions file keeps that history inline for exactly this reason, and it is the only place it is
written down.

**Corrected the same day, by the step after the one that wrote this note.** The paragraph above
ended with a prediction — *a section whose capabilities are constructions will cost* — and `E2-T41`
step B was exactly such a section: 105 rows over ten concrete curve types, almost every one of them a
constructor. The residue **fell 15**, the largest move it has made in that direction. The prediction
was wrong, and the reason is worth more than the prediction was: **`Line`, `Arc`, `Circle`,
`EllipseCurve`, `PolyLine` and `PolyCurve` were already in the register**, so thirty rows naming
`Circle.FromCenterNormalRadius` and its siblings landed on members the assembly declares. Only one
type came off the excused list — `Spark.Geometry.Planar.Region`, because `Polygon.ContainmentTest` is
`Region.Contains` — and it brought twelve members against twenty-seven named.

**So the rule is not about the shape of the capability, it is about the shape of the register.** What
a pass costs is whether the **types** its `Done` rows name were already accounted for, and nothing
else. §3.8 cost 27 because `Transform` had never been in the register and six rows suddenly claimed
it; §3.2 step B saved 15 because the curve types had been there since M1. A section full of
constructions over types the register already watches is the *cheapest* kind of pass, not the dearest.
**The prediction is left standing above rather than edited out**, because a note that quietly rewrites
its own reasoning teaches nothing about how the reasoning failed: it generalised from one observation
and the second observation broke it.
---

## N163 — A register's error has a direction, and it is always the safe one

**Seven member-by-member passes over the Dynamo parity register, and every one of them found Spark
further ahead than the register claimed.** Not roughly balanced, not mostly: seven for seven, over
837 rows and eleven days.

| Section | Claimed | Measured | |
|---|---:|---:|---|
| §3.3 surfaces | 0 | 48 of 106 | `E2-T42`, `E11-T30` |
| §3.4 solids | 0 | 24 of 55 | `E2-T43` |
| §3.5 topology | 0 | 31 of 33 | `E2-T44` |
| §3.6 mesh | 0 | 41 of 65 | `E2-T45` |
| §3.8 infrastructure | 0 | 24 of 89 | `E2-T46` |
| §3.2 curves | 0 | 111 of 187 | `E2-T41` |
| §3.1 values and frames | **99** | **113 of 133** | `E2-T40` |

**The last row is the one that makes this a note rather than a coincidence.** The first six all
started from *0 reachable*, so an assessment could only move them up and finding a gain proves
nothing. §3.1 was different: its 99 was a real count, made by hand, in prose, and it was the number
the whole document's headline rested on. The pass over it was a **check**, and the honest expectation
going in — written into the journal before the work started, so it could not be revised afterwards —
was that it would find **over**-claims. It found fourteen more reachable members.

**Three mechanisms, and each one is safe in the same direction.**

1. **A register that reads one assembly under-claims.** §3.3 said `Planned` for twenty capabilities
   that had worked since M6, because the check read `Spark.Geometry.dll` and loft, sweep, thicken and
   the booleans are delivered through `Spark.Api.IBrepKernel` ([N158](NOTES.md), `E11-T30`).
2. **A register that reads its own prose under-claims.** §3.1 still called
   `ByCylindricalCoordinates` *planned* three days after the factory landed, because nobody re-read
   the sentence ([N160](NOTES.md)). The fix is to read the assembly every time, and it is a rule
   because it is not a habit.
3. **A register whose subject is *capability* under-claims when the shape differs.** This is the
   subtlest and it produced the largest single error. §3.1 argued at length that Spark splits
   Dynamo's `CoordinateSystem` into an orthonormal frame and a `Transform` carrying the algebra —
   and then counted the split as six missing members. **A capability delivered somewhere else is
   still delivered.** The same shape appears in §3.8, where one `Transform` factory answers a member
   Dynamo repeats on eleven types, and in §3.2, where `Curve.Trimmed` twice answers a `Split` that
   was recorded as a deliberate exclusion.

**Why the error cannot run the other way, which is the actual point.** An over-claim is a row saying
*Done* about a member that is not there, and the rename-catcher turns red on it the moment anyone
builds. An **under**-claim is a row saying `Planned` about a capability that works — and **nothing
fails**. No check, no build, no test. It costs a capability nobody knows they have and, worse, a
schedule padded for work already finished. `Surface.Repair()` sat at `Not planned` for months on a
true premise that had reached a false conclusion ([N158](NOTES.md)).

**So the practice, and it is cheap.** The reverse direction of the check — every public member named
by a row, excused by a rule, or counted against an exact residue budget — exists precisely because
the forward direction can only catch the error that was never going to happen. **Keep the budget
exact rather than a ceiling**, because a ceiling lets an under-claim sit for ever. And when a pass
finds a section further ahead than claimed, **do not congratulate the code**: ask which of the three
mechanisms above hid it, and whether the same one is hiding something else.

---

## N164 — A hand-written list of name collisions goes stale, and the thing that catches it is not where you would look

**What happened.** `E2-T73` added `Spark.Geometry.Helix` and, beside it, the ordinary node façade
`Spark.Nodes.Core.Helix` — the same pairing that already exists for `Arc`, `Circle`, `Line`, `Plane`,
`Surface` and five others. Everything built. Every one of the 3,368 tests passed. The type was
**broken in every code block in the application**, and nothing said so.

**Why.** A code block's prelude imports `Spark.Geometry` *and* `Spark.Nodes.Core`, so every type name
the two share is `CS0104` — ambiguous — unless the prelude also carries
`using Helix = Spark.Geometry.Helix;` to pin it. `ReferenceCatalog.NodeLibraryImports` holds those
pins, and it is **a literal array written by hand**. Its comment said *the nine that collide with
`Spark.Geometry`*. There were ten.

**What actually caught it, and this is the part worth keeping.** Not the geometry suite, which does
not compile code blocks. Not `CodeBlockLibraryReachTests`, whose collision test is
`[InlineData]` over four names somebody thought of in `E6-T30` and which therefore can only ever
re-prove that *those* four are pinned. It was the **help-sample compiler** — the check that compiles
every fenced `csharp` block in `docs/help/` against the real API — and only because the step's help
topic happened to include a worked example that named the type. **Had the façade shipped
undocumented, it would have shipped broken**, and the first report would have come from a user
typing `Helix` into a code block and being told it is ambiguous between two things they did not know
existed.

**The shape of the fault, stated generally.** *A list that enumerates a relationship between two
artefacts, maintained by hand, is wrong the moment either artefact changes — and its failure is
silent by construction, because the list is what would have to notice.* The same shape is why
`ConstructorParityTests` is a reflection diff rather than a list of examples ([`E2-T59`]), why
`SolutionMembershipTests` walks `tests/` rather than trusting `Spark.slnx` ([`E11-T28`]), and why the
parity residue budget is checked for **exact** equality rather than as a ceiling
([N162](NOTES.md), [N163](NOTES.md)). Three of those four guards existed. The fourth did not, and
the gap was a `private static readonly string[]`.

**The fix.** `CodeBlockLibraryReachTests.EveryCollidingNameIsPinned` derives the colliding set by
reflection — the public top-level type names of `Spark.Nodes.Core` intersected with those of
`Spark.Geometry` — and fails with the unpinned names in the message. Proved by deleting the `Helix`
pin and watching it go red, naming `Helix`.

**And the cheaper lesson underneath it.** The hand-written comment was not merely out of date, it
was *load-bearing*: *the nine that collide* was the only statement anywhere of how many there were.
**A count in a comment is a test that never runs.** Where a number is worth writing down, the thing
that computes it is worth writing instead.

---

## N165 — A helix is not a NURBS curve, and the proof is three lines

**The claim I wrote first, in four documents, was wrong.** Closing `E2-T73` I recorded that
`Spark.Geometry.Occt.ModelWriter` exports a `Helix` by interpolating 64 samples, and that this was
the sharpest case yet for `Curve.ToNurbsCurve` **because a helix has an exact rational B-spline
form — a circle's knot structure with the control points lifted linearly along the axis.** That
sounds right, it is the kind of thing everyone half-remembers about NURBS, and it is false.

**The proof.** A NURBS curve is

> `C(t) = ( X(t)/W(t), Y(t)/W(t), Z(t)/W(t) )`

with `X`, `Y`, `Z` and `W` all piecewise **polynomial** in `t`. A helix is the point set
`(r·cos θ, r·sin θ, c·θ)`. Suppose some NURBS curve traces it, under a reparameterisation
`θ = φ(t)`. Then:

1. From the third coordinate, `c·φ(t) = Z(t)/W(t)`, so **`φ` is a rational function of `t`** — and a
   non-constant one, since the curve is traced.
2. From the first, `r·cos(φ(t)) = X(t)/W(t)`, so **`cos ∘ φ` is rational too**.
3. But `cos` is transcendental, and a transcendental function composed with a non-constant rational
   map is never rational. Contradiction.

So no exact representation exists — not at degree 2, not at any degree, not with any knot vector.

**Where the half-memory comes from, and it is a real fact next door.** The *circle* is exactly a
rational quadratic, and the standard construction uses the tangent half-angle: on a 90° span the
control points are `(1,0)`, `(1,1)`, `(0,1)` with weights `1, √2⁄2, 1`, and the parameter runs with
`tan(θ/2)` rather than with `θ`. **That is exactly what breaks the lift.** The helix needs the rise
to be linear in the *angle*, and the rational parameterisation is linear in the half-angle's
tangent, so "lift the control points linearly" produces a curve that is close to a helix and is not
one — closest at the span ends, worst in the middle, and wrong by an amount nobody would notice in a
picture.

**What it changes, which is more than a footnote.** `Curve.ToNurbsCurve` (`E2-T71`) was framed as
five closed-form conversions and a virtual on the base. `Line`, `Arc`, `Circle`, `EllipseCurve` and
`PolyLine` do all convert exactly. `Helix` cannot, ever. So the member **cannot return a bare
`NurbsCurve`**: a caller handed one has no way to tell an exact conversion from a good
approximation, and the difference decides whether a downstream boolean is right or merely plausible.
`ModelWriter` already faces this and answers it with an `Approximated` flag on the writer;
`ToNurbsCurve` needs the same honesty in its result, and the shape of that is `E2-T71`'s to settle.

**And the lesson about the claim itself.** It was written in a journal log entry, copied into the
PRD's FR-48 row, into TODO's next-step note and into the journal's *Next action* — **four places,
inside one step, before anyone checked it.** Nothing in this repository could have caught it: it is
a statement about mathematics, not about the assembly, so no test, no residue budget and no
rename-catcher touches it. [N163](NOTES.md) says an under-claim is invisible because nothing fails;
this is the other half of the same shape — **a claim about what is *possible* fails nothing either,
and it propagates faster than a claim about what exists**, because it reads as background knowledge
rather than as a measurement. Where a document asserts that something can be done exactly, the
assertion is worth the three lines that show it.

## N166 — A constraint can be met exactly and the curve still be wrong, because the caller's vector has a length

**The stated property is not the whole specification.** `NurbsCurve.InterpolatePointsWithTangents`
takes a *direction* at each end. The solver needs a *derivative*, and a derivative has a length the
caller never gave. Every length produces a curve that is exactly tangent to the direction asked for —
so the property the member is named for is satisfied by an unbounded family of curves, most of them
wrong, and a test of that property cannot tell them apart. A wrong length bulges or flattens the
ends. It looks like a bug in the solver and is not.

**Measured, not argued** (2026-09-13, `E2-T72`), by mutating the implementation the way AGENTS.md
step 7 asks: five points on a quarter circle of radius 10, the circle's own end tangents, and the
worst distance of the result from the circle *between* the samples.

| Derivative used | Worst distance from the circle | Tangency tests |
|---|---:|---|
| unit direction × total chord length (shipped) | 0.0014 | green |
| the same, tripled | 0.29 | **green** |
| the end chords, directions ignored | 0.11 | seven red |

The middle row is the one this note exists for: a curve two hundred times worse, and every assertion
about the property in the member's name still passes. The bottom row is the ordinary broken
implementation, and the ordinary tests catch it.

**The rule.** When an argument is a *direction* and the algorithm consumes a *vector*, the missing
magnitude is a design decision, not an implementation detail, and two things follow. It is written
in the member's remarks, because the caller cannot discover it from the signature and it changes the
shape they get. And it is tested through its *consequence* — here, where the curve goes between the
points, which is the only place a wrong magnitude shows — never through the stated property, which
is blind to it by construction. The rule chosen is the standard one: the derivative is the unit
direction scaled by the total chord length, because a chord-length-parameterised curve over
`[0, 1]` travels at roughly that speed everywhere, so the ends are asked to move at the speed the
middle already does.

**Where it comes up next.** `NurbsSurface.ByPointsTangents` (`E2-T66`) takes the same directions and
needs the same rule, along each parametric direction in turn. It is decided once, here, and the
surface form inherits it rather than choosing again.

## N176 — The pattern that caught four rows in a row fires zero times in 120 commits

**2026-09-15, out of `E2-T27`.** Four rows in four days turned out to be reconciliations rather
than builds, and the fourth — `E2-T27`, whose note said ADR-0020 had deferred it while its status
column read `Open` — suggested a rule worth mechanising: **a register row whose note argues with
its own status column.** The journal's *Next action* said to build it as a check rather than sweep
414 rows by hand, on `E10-T17`'s argument that a sweep done once goes stale the next day.

**It was measured before it was written, and the measurement killed it.** The general form — *a
note that opens by declaring the work `done`, `closed`, `deferred`, `withdrawn` or `superseded`,
against a status of `Open`, `In progress` or `Blocked`* — was run over **120 commits of
`docs/TASKS.md`** and flagged **nothing, in any of them**. The version that caught `E2-T27` did it
with a phrase list containing *moves from M6 to 1.x*, which is that row's own wording. That is a
check fitted to its single fixture, and [N13](#n13--sparkdocsverify-deliberately-contains-no-stub-for-a-check-it-cannot-yet-run)
already names what it would have become.

**A broader phrase list is worse, not better, and that is also measured.** Adding *closed 20* — an
obvious generalisation — flagged three rows, and **all three were false positives** of the same
kind: `E2-T68`, `E2-T71` and `E2-T72` are multi-part rows whose notes say *this sub-item closed
2026-09-13*, which is exactly what an `In progress` row is supposed to say. A rule that fires only
on false positives trains the reader to ignore it.

**The invariant standing next to it had 22 real findings.** `EPICS.md` states acceptance as check
boxes and `TASKS.md` states rows as statuses; **154 criteria cite exactly one row**, and on the day
this was written **22 of them disagreed with the row they cite** — two boxes ticked whose rows were
`Blocked` and `Withdrawn`, and twenty unticked whose rows had been `Done` for up to three weeks.
`AcceptanceCriterionChecks` asserts it now, and one of the twenty was not lag at all: **`E8-T15`
had been waiting on a measurement that ran on 2026-09-14** and nobody had gone back to look.

**The rule, and it is about how to choose a check rather than about this check.** *Measure a
proposed rule against history before writing it.* Both candidates took minutes to run over the git
log, and the answer was not guessable: the pattern with four recent instances had a historical
yield of zero, and the one nobody had mentioned had twenty-two. **A rule's plausibility and its
yield are unrelated**, and the difference between them is one `git log` away.

**Corrected the next day, and the correction is the more useful half.** Everything above holds for
the rule that was measured. What did not hold is what was concluded from it — that **no** rule of
that family was worth having. Surveying the remaining `Open` rows for the following step turned up
**two with exactly this fault, in phrasings the measurement never tested**: `E13-T22`, whose note
ended *Blocked on `E13-T21`* under a status of `Open`, and `E12-T16`, whose **title** is
*Crash-reporting decision, deferred* under a status of `Open`. Both had been that way in **every one
of those 120 commits**.

**What the second look changes, precisely.** The first rule read a **note** for a *settled-status
word*, which is ambiguous — a multi-part row's note saying *this sub-item closed* is ordinary and
correct, and that is where the three false positives came from. The two rules now in
`RegisterStatusChecks` read the row's own words unambiguously: *blocked on `E<n>-T<m>`* **names the
row that blocks it**, and a **title** is short enough that a word in it is about the row and nothing
else. And the yield they have is a different shape: **standing rather than churning** — two rows
wrong for weeks, which is the kind nobody notices, rather than a stream of rows that get fixed
anyway.

**So the rule survives with a clause added.** *Measure a proposed rule against history before
writing it* — **and measure the rule you would actually write, not the loosest member of its
family.** A zero over one phrasing is evidence about that phrasing. Generalising it to the family is
the same over-reach this note was written to warn against, committed by the note itself, one day
after it was written. `E11-T33`.

**And the narrow rule produced a false positive within minutes of being written, on the row that
describes it.** `E11-T33`'s own note quotes *blocked on `E13-T21`* while recounting what it caught,
and the rule matched the quotation — the exact ambiguity the narrowing was supposed to remove. **The
fix is not a filter, it is the rule said properly**: a **settled** row cannot be blocked on
anything, so only `Open` and `In progress` rows are candidates. What is being checked is *an
unfinished row that says it is waiting*, not *a row containing a phrase* — and the first wording was
about the phrase without anybody noticing, because the only rows that had it were also unsettled.
**A check's first encounter with a row outside its fixtures is where its actual rule becomes
visible**, and here that took nine minutes.

## N175 — Every arc bounded its whole circle, and 3,947 tests did not mind

**2026-09-15, `E2-T32`.** `CircularArcs.Bounds` computes an exact bounding box for a circle, an arc
or an ellipse on any frame: for each world axis it solves for the angle where that axis's extent is
extreme, and takes it **if it lies on the sweep**. Forcing `CircularArcs.Includes` to return
`true` — so that every extremum counts and every arc reports the box of its **entire circle** —
left **all 3,947 tests green**, across all ten executables.

**The only bounding-box test on the whole family was about a full circle**,
`ACirclesBoundingBoxIsExactOnATiltedPlane`. A full circle includes every extremum, so the sweep
test it runs through is vacuous: the assertion is about the other half of the method.

**Containment is not the assertion; tightness is.** The box of the whole circle *contains* every
point of the arc, so the obvious test — sample the curve, check each point is inside — passes
against the broken implementation. `ArcBoundsTests.AssertBoundsAreTightAround` asserts both
directions: the box holds every sample, **and no face of it stands off the samples** by more than
the sampling error. Only the second one fails.

**Why it would never have been noticed in use.** A box that is too large is *conservative*, not
wrong: selection still selects, culling still draws, a BVH query still returns a superset and the
narrow phase filters it. The symptom is that things get slower and a rubber-band selection catches
more than it should — which is a complaint, not a bug report, and nobody bisects a complaint.

**A fourth mutation survived, and it corrected a comment.** `Includes` carries a 1e-12 slack whose
comment claimed that dropping it *would lose a bound the box needs*. Removing it left everything
green, including the twenty-two tests written the same day about exactly these bounds — and the
reason is structural rather than lucky: **`Bounds` seeds its box with both end points**, so an
angle that only the slack admits is within 1e-12 radians of an end, and the point it contributes is
within `r * 1e-12` of a point the box already holds. No box can move by more than that. The slack
stays — a future caller that did not seed with the end points would need it, and an extremum landing
on an end genuinely does arrive as `-1e-17` rather than as zero — but the comment now says what is
true. **An arc from 30 degrees sweeping 60 is the case**: its end is exactly the `+y` extremum, and
the offset from the start overshoots the sweep by 2.2e-16.

**The second one was found the same day, by the same method, and it names the shape.** Closing
`E2-T32` meant looking for every other place in `Spark.Geometry` that does **a wrapped comparison
deciding whether an angle lies on a sweep**, because that is the one arithmetic Spark's
representation cannot avoid. There is exactly one other:
`AnalyticCurveIntersection.Circular.ParameterAt`, which decides whether an intersection angle is on
an arc. Its main claim was guarded; **both of its tolerance branches were not** — the slack past the
end and the wrapped slack before the start, each `tolerance / radius` wide, each removable with the
whole suite staying green. **That slack is load-bearing where `Includes`' was not**, and the
difference is what makes one a test and the other a corrected comment: nothing else contributes an
arc's end to an intersection, so an end dropped for being 1e-16 outside its own sweep is an
intersection the caller never hears about. One test pins both branches and both directions —
inside the tolerance is the end, a hundred slacks out is nothing —
and it is `ACrossingJustPastAnArcsEndIsTheEndWhenItIsInsideTheTolerance`.

**Why there are only two places to look.** DoodleSharp's `SweepAndOrientationTests` is a whole file
about one cause: *a sweep is a start plus a signed offset, not a pair of points on a circle*, and
folding either end into `[0, 360)` on its own throws away the direction of travel and every sweep
that crosses zero. **Spark stores start plus signed sweep from the beginning**, so that entire bug
family is unrepresentable here — splitting an arc across zero, parameterising a backwards-built one,
mirroring one twice, all correct by construction and confirmed by probe. The family survives only
where the arithmetic is genuinely needed: **asking whether some *other* angle is on the sweep.**
Both of those places had holes.

**The general form.** This was found by harvesting assertions from a **different repository** —
DoodleSharp's `SweepAndOrientationTests`, written there because the bug had actually shipped. The
value of a foreign test suite is not its coverage, which duplicates; it is that **somebody else's
bugs are the ones your conventions do not produce**. That is the same finding as [N173](#n173--an-abstract-class-can-have-a-public-constructor-and-new-on-it-does-not-compile)
one day earlier, where a library nobody here wrote crashed the importer in four minutes, and it is
the argument for `E2-T32` that the row itself does not make: not *an instant regression net*, which
3,947 tests no longer need, but **inputs and assertions from outside this tree's habits**.

## N174 — A rule with nothing behind it survives because nobody ever tries it

**2026-09-15, `E10-T12`.** `CONTRIBUTING.md` has asked every contributor for **a changelog
fragment** since it was written. There was no `changelog.d/`, no format, nothing that read one, and
no `CHANGELOG.md` anywhere in the repository. The instruction had been wrong for its whole life and
nothing had noticed, for the only reason that matters: **two pull requests exist in this
repository's history**, and 206 commits went straight to `main`. A rule is only tested when somebody
follows it.

**What made it visible was working the row, not reading the file.** `E10-T12` is *per-PR changelog
fragments — avoids a single changelog file becoming a merge-conflict magnet*. Checking whether the
premise still held is what turned up the directory that does not exist; the row was in the register
for weeks reading as a sensible piece of process hygiene.

**And the stated reason was the weaker half of the case.** A merge-conflict magnet is real and is
not this project's problem. The argument that held was one the row never mentions: **every release
from `v0.1.0` to `v2026.9.0` shipped with no *what changed* at all.** `.github/release-notes.md` is
install instructions, `--generate-notes` was dropped in `E12-T22`, and nothing replaced it. Somebody
upgrading was told how to install and never what they were getting.

**The shape, and it is the fifth time this week.** A documented rule, a stated motive, or a comment
whose reason expired — and the correct move is neither *do it because it says so* nor *drop it*, but
**work out what is true now and build the smallest thing that makes the existing promise true**.
Three of the five were reasons that had quietly expired: `E2-T64`'s rename justified by a payload
that does not exist, `N132`'s scan that had become a check, and — in the same file as this one —
`release.yml`'s claim that `--generate-notes` was dropped to avoid publishing the private history of
a closed source tree, which stopped being true on 2026-09-14 when the repository went public again.
The conclusion survived on a different argument, which is the outcome to expect: an expired reason
usually means the decision needs re-arguing, not reversing.

**Where to look for the rest of them.** Any sentence in `CONTRIBUTING.md`, `AGENTS.md` or a workflow
comment that describes *what somebody else must do* is unverified by construction — there is no
build step that fails when a rule aimed at a human is false. The ones aimed at this repository's own
machinery are checked; `ChangelogFragmentChecks` now checks this one, by asserting that the release
notes and the workflow actually carry the section the directory produces.

## N173 — An abstract class can have a public constructor, and `new` on it does not compile

**2026-09-14, `E5-T11`.** The node importer crashed on the first assembly this project had ever
pointed it at that somebody else wrote. Not a wrong node, not a missing one: an
`InvalidOperationException` out of `Expression.Compile()` — **`Can't compile a NewExpression with a
constructor declared on an abstract class`** — which takes the whole import down and every node in
the assembly with it.

**The mechanism is two true facts that do not meet anywhere in the type system.**

- `Type.GetConstructors()` on an abstract class returns its **public** constructors, and they
  genuinely are public: that is how a derived class in another assembly chains to them. There is
  nothing irregular about the declaration.
- `ConstructorInfo` carries no hint of it. The check is on `DeclaringType.IsAbstract`, which
  nothing about building an `Expression.New` obliges you to consult.

So `NodeInvoker.ForConstructor` built the expression happily and `Compile()` refused it — at
**import** time, not at evaluation time, which is what made it fatal rather than merely wrong.

**Why it survived the entire life of the importer.** Every assembly ever imported was written
here: `Spark.Nodes.Core`, `Spark.Geometry`, `Spark.Viewport`, and fixture types declared inside the
test beside the assertion. **Not one of them has a public constructor on an abstract class**, and
nothing about our own conventions would have produced one. MathNet.Numerics has several
(`Matrix<T>` and the distribution bases among them), because that is an ordinary way to write a
class hierarchy in a library meant to be extended.

**The fix is one guard with a stated reason**, refusing the constructor the way every other
unimportable member is refused:

> *an abstract class cannot be constructed: its public constructor exists for derived classes to
> chain to.*

**The lesson is about the fixture, not about the bug.** `NodeImporter`'s header says *zero
configuration is the whole design: a public static method is a node with no attribute on it at
all*, and that claim is precisely about assemblies whose authors have never heard of Spark. **It
had only ever been tested against assemblies whose authors wrote it.** A test suite made entirely
of its own project's code cannot find the shapes its own conventions never produce, and the cost of
finding out is one `PackageReference` to a well-known library — `E5-T11`'s own words for this were
*the only honest way to know zero-config actually works*, and it took four minutes to be proved
right.

**What the same fixture also proves, now that it exists.** MathNet.Numerics imports to **3,621
nodes** with no configuration of any kind, and `PackageFrameworks` reduces net10.0 to the package's
`net6.0` folder against five real candidates rather than the single folder every hand-built fixture
offers.

## N172 — A limit is not a census, and the truncated query confirms you

**2026-09-14, `E11-T14`.** Three descriptions of the same CI job were written in one day. The first
two were confident, were copied into three documents each, and were both false — and the second was
written *specifically to correct the first*, using a command, and was wrong in a more interesting
way than the thing it replaced.

**Claim one: “the `docs-freshness` job has never executed once, because this project has never
opened a pull request.”** The second half was read off the shape of the commit log — every commit
on `main` arrived as a push — which is evidence about `main` and none whatever about pull requests.
`gh pr list --state all` answers it in one command and says **two**, both opened 2026-08-28, both
still open.

**Claim two, the correction: “of the 200 runs this repository has ever had, not one was triggered by
a pull request — 188 pushes, 11 schedules, one dispatch.”** That came from

```
gh run list --limit 200 --json event
```

and it is wrong because the repository has **234** runs. The sixteen `pull_request` runs are the
**oldest sixteen**, so the window cut off precisely the evidence that would have refuted the claim.
**The numbers looked like a census. They were the most recent 200 rows.**

**What is actually true**: 206 pushes, 16 pull requests, 11 schedules, one dispatch. *Documentation
freshness* is a green job in all sixteen of those pull-request runs, with real output ending
`Documentation freshness check passed.` The job was never broken and never unexercised. It went
quiet when the pull requests did, and stayed quiet for seventeen days of pushes to `main`.

**The rule.** A paged or limited query is a *sample*, and a sample that happens to be ordered by
recency will systematically hide the oldest counter-example — which, for any question of the form
*has this ever happened*, is the only evidence that matters. **Ask for more rows than you believe
exist and confirm the total**, or do not cite the number:

```
gh run list --limit 400 --json databaseId -q '. | length'   # 234 - so 400 saw all of it
```

**Why this is worse than the claim it replaced.** The first claim was a guess and read like one.
The second arrived with a command attached, which is the form this project uses to mean *checked* —
and it returned exactly the numbers needed to feel confirmed. **A query whose window can silently
exclude the counter-example is not a check; it is the same assertion wearing a command prompt.**

**It recurred twice more in the same step, which is why it is a note.** Proving the extracted gate
script, the fifth case appended to `src/Spark.Engine/Evaluator.cs` — a file that does not exist — so
`git commit -a` committed nothing and the check silently re-read the previous commit's range,
reporting a result that belonged to another case. Caught only because the exit code was 1 where a
pass was expected. **Same shape: a probe that never ran the case, reporting cleanly.** And the
sweep that followed found four *more* documents still saying the nightly *has never run on a hosted
runner* — README, TODO, EPICS and ADR-0023 — five days after it had.

**The cheap habit that beats all of it**: when a claim is about whether something has *ever*
happened, make the query prove its own completeness before you believe its answer.

**It happened a third time the same day, and the tool was `grep`.** A census of `docs/NOTES.md`
came back with **111** `## N` headings against an actual **172** — because two NUL bytes in
[N132](#n132--a-nul-byte-in-a-source-file-compiles-and-greps-as-binary) made `grep` treat the file
as binary and stop reporting. **The wrong number arrived as a correction of a figure that was
already right**, which is the most persuasive form a wrong answer can take: a claim that agrees
with you is checked once, and a claim that corrects you is not checked at all. `grep -c` and
`grep -ac` differ by one flag and by sixty-one entries, and neither says which it did.

**So the rule generalises past paging.** It is not only *ask for more rows than you expect*; it is
**a tool that can stop early must be made to say that it did not**. A `--limit` is one way to stop
early. Binary detection is another. Both return success.

## N171 — `Console` meant System's, on whichever machine ran the tests in the wrong order

**2026-09-14, `E11-T14`.** Four `ConsoleFromCodeTests` failed on a hosted Windows runner and passed
on the development machine. With the diagnostics that [N170](#n170--a-test-that-discarded-the-one-value-that-could-explain-its-failure)
added, the cause named itself: **`CS0815 Cannot assign void to an implicitly-typed variable`**, on a
block reading `var removed = Console.Clear();`.

**Spark's `Console.Clear()` returns the number of lines it removed. `System.Console.Clear()` returns
void.** So on that machine, in that run, `Console` in a code block meant **System's**.

**The mechanism is a prelude assembled from a running process.** `ReferenceCatalog.PreludeFor`
emits the node library's imports — including the pin `Console = Spark.Nodes.Core.Console` — only
when `Spark.Nodes.Core` is among the references, and the references were discovered by sweeping
`AppDomain.CurrentDomain.GetAssemblies()`. That set is **whatever the process has already loaded**,
which in a test run is decided by *which class ran first*. Two machines scheduled the classes
differently and got different languages.

**What a user would have seen is worse than a failing test, because nothing fails.** A block saying
`Console.WriteLine("x")` compiles perfectly against `System.Console` and writes to a terminal a
windowed application does not have. The line simply never appears in the Console pane, and there is
no error anywhere to explain it.

**This is the third time this exact fault has been found in this one method, and the first two are
recorded in its own comments.** `Microsoft.CSharp` was missing until a script using an input port
failed with *Missing compiler required member*. `System.Linq.Expressions` was missing until a test
class *that had loaded neither* compiled one. Both were fixed by anchoring the assembly with a
`typeof(...)` rather than hoping the sweep found it — and both comments say, in effect, *a catalogue
built early enough is missing things*. **The lesson was written down twice and applied twice,
member by member, instead of once to the rule.**

**`Spark.Nodes.Core` could not take the same fix**, which is why it was left out: `Spark.Scripting`
does not reference it and must not — the node library is imported the way any third party's library
would be. So it is now found **beside the running assembly**, by file name, which moves the decision
from *what has already run* to **what is deployed**. A host that does not ship the node library
still gets a prelude without it, which is what `E6-T30` intended; a host that does ship it gets the
same answer on the first block as on the hundredth, which `E6-T30` assumed and did not get.

**The general rule, stated once so it need not be learned a fourth time: a set built by asking a
live process what it has loaded is not a set, it is a snapshot.** Anything derived from it inherits
the timing. If the answer is supposed to depend on the deployment, ask the deployment.

## N170 — A test that discarded the one value that could explain its failure

**2026-09-14.** Four of the five `ConsoleFromCodeTests` failed on Windows CI and pass on this
machine. Each reported the same thing: the console was **empty** when a line was expected. None of
them could say why, and the reason is one line in the helper they share:

    _ = block.Invoke([], CancellationToken.None);

**A script that does not compile and a script that runs and prints nothing produce the same empty
console.** The helper threw away the only value that distinguishes them, so every one of those four
failures reported a symptom and nothing else — from a machine I cannot attach a debugger to.

**The fix is to assert the thing that was being discarded.** `ScriptNodeFactory.Diagnose` already
existed and already returns the compiler's errors; the helper now runs it first and fails with the
diagnostic text. **A test that cannot say why it failed costs more than the line it saved**, and
the cost is paid at the worst moment — on somebody else's machine, in a log, after the fact.

**What is underneath is still open as this is written, and the suspicion is worth recording because
it is about production code rather than tests.** `ReferenceCatalog` builds the reference set a code
block compiles against by sweeping `AppDomain.CurrentDomain.GetAssemblies()` — **whatever happens to
be loaded at that moment**. That set depends on what the process has already done, which in a test
run means *which tests ran first*, and in the application means *what the user did before opening
the block*. A reference set assembled from a snapshot of a running process is not deterministic, and
the two candidates both follow from that: a reference that is missing because nothing has loaded it
yet, or a duplicate simple name because two copies of one assembly are loaded from different paths.

**It turned out to be neither, and the diagnostic said so in one line**: the reference set was not
missing a framework assembly and had no duplicate identity - it was missing **the node library**, so
the prelude quietly dropped the pin that makes `Console` mean Spark's. The whole of
[N171](#n171--console-meant-systems-on-whichever-machine-ran-the-tests-in-the-wrong-order) followed
from the message this change made visible. **The helper's silence is what made a one-line answer
into two round trips through CI.**

## N169 — The second machine found exactly one thing, and it was in a test

**2026-09-14.** CI ran for the first time since 2026-09-09, on sixty-three commits that had been
verified on one Windows machine and nowhere else. The ubuntu leg ran **the same 3,875 tests** the
local runners do and **one** failed:
`PackageFrameworkChoiceTests.ARefOnlyPackageWithAPlatformMonikerIsFound`.

**The code was right and the test was wrong, which is the outcome worth writing down.** The package
under test offers `ref/net10.0-windows7.0` and nothing else. On Windows that is compatible and the
assembly is found; on Linux it is **genuinely incompatible** — NuGet's own `FrameworkReducer` says
so, and `PackageFrameworks.Current` only claims a platform when `OperatingSystem.IsWindows()`.
*Finding* that assembly on Linux would have been the bug. What failed was an assertion written as
though the answer were platform-independent.

**It passed for months because nothing but Windows ever ran it.** Spark is Windows-only by **D16**,
and the ubuntu leg exists as a *second implementation of the same arithmetic* rather than as a
supported target — a different libc, a different floating-point library, a different culture
default. It has earned its cost before, and differently: [N28](#n28--a-script-committed-from-windows-is-not-executable-on-linux-and-ci-is-where-you-find-out)
is a shell script that was executable on the machine that wrote it and not on the machine that ran
it. Both are the same shape of fault - **something true of the author's environment, asserted as
though it were true of the world**.

**The fix states the platform dependence instead of hiding it.** Both arms are asserted — found on
Windows, *not* found anywhere else — rather than skipping the awkward one. **A skip records that a
platform was not tested; an assertion records what that platform is supposed to do**, and only one
of those two survives somebody changing the resolver.

**The general lesson is about what a green local run is evidence of.** Five days of work passed
three gates on every commit — a warnings-as-errors build, ten test executables, a format check —
and all of that was one machine agreeing with itself. It was not wrong; it was *narrow*, and the
narrowness is invisible from inside. **One in 3,875 is also the right order of magnitude to
expect**: not an argument that local gates are weak, and not an argument that a second environment
is optional.

## N168 — A link that works and lands in the wrong place

**2026-09-14, `E11-T7`.** The documentation check `EveryRelativeLinkResolves` has verified since it
was written that a linked *file* exists. It says in its own code, without embarrassment, *we are
checking that the file exists, not the heading*. Adding the heading half found **twelve distinct
broken anchors across twenty-four citations**, every one of them in a document that had been read,
edited and reviewed many times since the anchor went stale.

**They were invisible because a stale anchor is not a broken link.** The page opens. The file is
right. The reader lands at the top instead of at the section named, assumes they mis-clicked, and
scrolls. Nobody files that, and a reviewer reading a diff cannot see it at all, because the link
text is still correct — it is the *target's heading* that moved, in a different file, possibly years
earlier.

**All twelve were the residue of renames.** `## E11 — Testing and quality` became
`## E11 — Quality and verification`, and the citations kept the old slug; the same epic had *three*
different stale spellings in one document, from three successive renames. `#e3--the-file-format`
pointed at what is now `## E3 — Graph engine`. Two `NOTES.md` citations used a shorthand — `#n64` —
that GitHub has never produced, because its slug is the whole heading including the title after the
dash. Nobody wrote any of these wrong; they were right when written.

**The lesson generalises past anchors.** A citation has two halves — *where* and *what* — and a
check that verifies only the first passes forever while the second rots. The same shape appears in
this repository's ADR check, which knows it and says so: it verifies that a cited ADR **exists** and
records that it cannot verify the citation is *about* the right thing. The difference is that an
anchor's *what* **is** machine-checkable, and simply had not been checked.

**And the gate for a gate is a gate.** A checker over real documents that finds nothing looks
identical to a checker that checks nothing — which is [N167](#n167--a-grep-that-finds-a-failure-reports-success-and-it-let-a-red-commit-through)'s
lesson in a different costume. So the anchor check ships with a test that runs it over a synthetic
document whose anchor is deliberately wrong and fails if it is not reported. That test is the one
that goes red when the condition is inverted; the check over the repository stays perfectly green.

**Writing the note broke the check, twice, and both breakages are instructive.** The first: a
literal image link written as an example, inside backticks, was reported as a missing file —
the checker stripped fenced blocks and had never considered inline code spans. The second was
the fix for the first. Removing a code span's *contents* seems right for link scanning and is
wrong for headings, because GitHub keeps the words inside backticks when it makes a slug: it
turned N34's own heading into an anchor with two invented hyphens and reported a citation that
had always been correct as broken. **A stripper that is right for finding links is wrong for
naming sections**, so there are two of them, and the difference is written where both are
defined.

**What this does not cover, stated so nobody assumes it does.** There are currently **no image or
asset references anywhere in the documentation** — zero — so asset integrity is covered by
construction rather than by effort: the existing link regular expression matches `![alt](path)` as
readily as `[text](path)`, and will check the first image the day one appears. Renderer parity
against a golden corpus, the other half of `E11-T7`, needs the help renderer and is not this.

## N167 — A grep that finds a failure reports success, and it let a red commit through

**What happened, 2026-09-13.** A step was verified and committed with one shell line:

```
(cd tests/Spark.Docs.Verify/bin/Debug/net10.0 && ./Spark.Docs.Verify.exe | grep -E "Total:|\[FAIL\]") && git commit ...
```

`Spark.Docs.Verify` was **red**. The commit went in anyway. `grep` exits 0 when it *finds* a
match, and `[FAIL]` was a match — so the gate that was meant to stop a failing suite reported
success precisely because the suite had failed. The more thorough the filter, the more reliably
it passes.

**The rule.** A test run's exit code is the gate; a grep of its output is a *report*. Never chain
a commit behind a grep of test output. Either read the result and then commit as a separate
action — which is what the protocol's step 5 and step 9 always meant by *verify, then commit* —
or gate on the runner's own exit status and keep the grep for display:

```
./Spark.Docs.Verify.exe > run.txt; status=$?; grep -E "Total:|\[FAIL\]" run.txt; exit $status
```

**Why it is worth a note rather than a shrug.** The same shape hides in every `cmd | grep … &&
next`, and it fails in the direction that does damage: it is silent when things are fine and
silent when they are not. It cost one bad commit, caught on the next run and fixed in the commit
after — but a session that had stopped there would have left `main` red with a green-looking
transcript.
