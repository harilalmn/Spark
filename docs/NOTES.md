# Spark — Implementation Notes

Non-obvious implementation facts, numbered. Adopted from DoodleSharp's convention.

**Last updated:** 2026-08-27

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

## N10 — `Spark.Geometry` declares `InternalsVisibleTo` for two projects that do not exist yet

```xml
<InternalsVisibleTo Include="Spark.Geometry.Tests" />
<InternalsVisibleTo Include="Spark.Geometry.Properties" />
```

Neither project exists. `tests/` now holds `Spark.Architecture.Tests` and
`Spark.Docs.Verify`, and neither is named here — deliberately, because neither needs
internals: one reads `.csproj` files as XML and the other reads Markdown. This is not a
mistake and it does not break the build — `InternalsVisibleTo` names an assembly that may or
may not ever be produced.

It is recorded here because it is the kind of thing a reader flags as dead configuration and
deletes. It is a **declaration of intent about test shape**: the kernel gets exactly two
test assemblies with privileged access — one for conventional unit tests, one for the
CsCheck property-based suite (`E2-T33`, `E11-T10`) — and both target `Spark.Geometry`
specifically. Any other test project sees the public surface only, which is the same surface
a user sees.

Anything needing a third entry here is a signal to look again at whether the thing being
tested should be public.

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
