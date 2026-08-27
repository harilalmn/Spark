# Contributing to Spark

Thank you for looking. Spark is MIT-licensed, open to contributions, and maintained by one
person — which shapes most of what follows.

**Last updated:** 2026-08-27

---

## Before you start: Spark is at M0

The repository contains a solution, twelve project stubs, build properties, a CI workflow
that has not yet run, two test projects that check the repository against itself, and the
project documents. The first geometry value types are being written now; everything else is
empty. If you were hoping to fix a bug, there is almost nothing to fix; if you were hoping to
add a node, there is no node library to add it to.

What is genuinely useful right now is review: of the
[decision log](docs/PRD.md#13-decision-log), of the
[eighteen ADRs](docs/adr/README.md), of the
[lacing specification](docs/help/concepts/lacing.md) — which is written and which the engine
will be built to match — and of the architecture in [EPICS.md](docs/EPICS.md). An argument
that one of those decisions is wrong is worth far more at M0 than at M4, when a graph
somewhere already depends on it.

## Licence and sign-off

Spark is **MIT**. Contributions are accepted under the same licence.

**We use DCO, not a CLA.** The [Developer Certificate of Origin](https://developercertificate.org/)
is a statement that you wrote the patch or otherwise have the right to submit it under the
project's licence. You agree to it by signing off your commit:

```bash
git commit -s -m "Add NurbsCurve.ParameterAtLength"
```

which appends a line to the commit message:

```text
Signed-off-by: Your Name <you@example.com>
```

Every commit in a pull request needs one. `git rebase --signoff` fixes a branch that is
missing them.

**Why DCO rather than a CLA.** A CLA asks a drive-by contributor to read and sign a legal
document before a one-line typo fix. That loses more contributions than it protects, and
the thing it protects — the ability to relicense later — is something Spark does not want.
MIT is the licence, permanently. DCO is one line in a commit message, and it is what the
Linux kernel uses.

**Why MIT rather than a copyleft licence.** Spark is designed to be embedded inside
commercial CAD add-ins; that is a stated goal, not an accident. A copyleft licence would
prevent it.

## Building

You need the **.NET 10 SDK** and nothing else. No Autodesk product, no native toolchain, no
GPU.

```bash
git clone https://github.com/harilalmn/Spark.git
cd Spark
dotnet build Spark.slnx
```

Everything targets `net10.0` — no `-windows` target framework anywhere — so the solution
builds on Windows, Linux and macOS. Windows is the only *release* target for v1, but the
Linux build is a first-class CI job and a broken Linux build is a broken build.

The solution file is `.slnx`, the XML solution format, which needs a recent SDK and a recent
Visual Studio. [NOTES.md N1](docs/NOTES.md) explains why.

### Building the way CI does

```bash
dotnet build Spark.slnx -warnaserror
dotnet test Spark.slnx
dotnet format Spark.slnx --verify-no-changes --severity warn
```

All three work as written, and the third is quoted in the form the CI job uses, because a
shorter one can pass locally where the gate fails. Run all three; they are gates, and the
third is the one people forget. `dotnet test` runs the documentation
harness and the architecture tests — there is no separate command for either.

**Warnings are errors in CI only, never in the project files.** Local development stays
pleasant; the gate stays absolute. Do not add `TreatWarningsAsErrors` to a csproj to "help"
— [NOTES.md N3](docs/NOTES.md) explains what that breaks. Nullable warnings are the one
exception: they are errors everywhere, including locally.

## Testing

Two test projects exist — `tests/Spark.Architecture.Tests`, which enforces the reference
graph, and `tests/Spark.Docs.Verify`, which checks the documents against the repository.
Neither tests product code, because there is none. The rest arrive **with the code they
test**, not ahead of it: under Microsoft.Testing.Platform a test project containing no tests
fails the run outright ([NOTES.md N12](docs/NOTES.md)).

The conventions:

- **xunit v3**, one flat test project per source project. `tests/Directory.Build.props`
  carries the shared settings, so a new test project is a near-empty `.csproj` plus a line
  in `Spark.slnx`.
- **Full PascalCase sentence names, no underscores.**
  `TransformInverseAppliedTwiceReturnsTheOriginal`, not `Transform_Inverse_Twice`.
- **Non-parallel collections** for anything touching static state.
- **Property-based tests with CsCheck on the kernel** — from M1, non-negotiable. Round
  trips, invariants and bounds, not examples.
- **Golden files stored as hashes plus summary statistics**, with failures printing a
  readable diff table: bounding box, counts, area, volume. A bare hash mismatch tells you
  nothing.
- **Regression tests go in `tests/corpus/`** and stay there. The corpus grows with every
  bug found.

## The documentation rule

This is the part most likely to surprise you, so it is stated plainly:

> **A change whose documentation has not been updated is an unfinished change.**

It is not aspirational. Three mechanisms enforce it, and a pull request that trips any of
them does not merge:

1. **CS1591 is an error** on `Spark.Api`, `Spark.Geometry`, `Spark.Geometry.Io` and
   `Spark.Nodes.Core`. A new public member without an XML doc comment **does not compile**.
   Do not suppress the warning; write the comment.
2. **The docs harness** (`tests/Spark.Docs.Verify`, run by `dotnet test`). It checks
   help-topic front matter, that every help topic contains a worked example, that every
   relative Markdown link resolves, that every cited `ADR-NNNN` exists, and that every core
   document carries a `Last updated` line. Once there is an API to check against it will also
   compile every ` ```csharp ` fence and every XML `<example>` using the exact references and
   imports a real code-block node gets, execute every example graph headlessly, and check
   node coverage in both directions.
3. **The `docs-freshness` job** fails a diff that changes a public-API baseline or touches
   `src/Spark.Nodes.*` without touching `docs/`, `README.md` or `AGENTS.md`. It can be
   overridden with an explicit `docs: none-needed` commit trailer, which is deliberately
   **visible in review**.

Mechanism 2 exists and runs, in the reduced form described. Mechanism 3 is written and **has
never run**, because CI has not executed once (`E1-T14`). The rule applies regardless — that
is the point of stating it here rather than relying on the gate.

[AGENTS.md](AGENTS.md) has the which-document-when table. In short: user-facing behaviour
gets a **help topic with a worked example**; a decision that could have gone differently
gets an **ADR**; a non-obvious implementation fact gets a numbered **note**; a task starting
or finishing updates **TASKS.md** including its summary counts.

## Pull requests

**Branching is trunk-based.** Branch from `main`, open a PR, squash merge when CI is green.
There is no develop branch and no release branch.

**Keep it small.** One epic's worth of change at most, and preferably one task's. A large PR
from a stranger is not a gift to a single maintainer; it is a review backlog.

**Name the task IDs.** Commit messages and PR descriptions should say what changed, why, and
which `E<n>-T<m>` it advances. If there is no task for what you are doing, add one to
[TASKS.md](docs/TASKS.md) in the same PR.

**Say what you could not verify.** This is a house rule, inherited from CADScript and taken
seriously: *compile-verified* and *confirmed working* are different claims. A PR that says
"builds clean; I could not test the GL path because I have no discrete GPU" is more useful
than one that implies more than it proved.

**A changelog fragment** rather than editing a single changelog file, so PRs do not collide
(`E10-T12`).

### What will get a PR rejected on principle

These are settled decisions, not oversights. Please read the reasoning before arguing with
them — most are in the [decision log](docs/PRD.md#13-decision-log) or in
[TODO.md's *Known and deliberately accepted*](docs/TODO.md#known-and-deliberately-accepted).

- **A `.dyn` importer or exporter.** Decision **D8**. The blocker is semantic equivalence,
  not file parsing, and it is unprovable without the dependency Spark exists to remove.
- **Units, unit types or a `UnitSystem`.** Decision **D12**.
- **Dimensions, hatches, text, arrows or grids.** Decision **D13**.
- **A native dependency in `Spark.Geometry`**, or a second managed one without a very good
  argument.
- **A reference from `Spark.Nodes.Core` to `Spark.Engine`**, or an Avalonia reference in
  `Spark.Viewport`. Both are enforced by test.
- **An ambient or static tolerance.** It would be invisible to the evaluation cache, which
  would then silently serve geometry computed at the old tolerance.
- **A `-windows` target framework**, or unsafe code.
- **Telemetry of any kind.**
- **A breaking change to `Spark.Api` or `Spark.Geometry`** during 1.x. Add an overload; add
  an interface. Never change one.

## Reporting a bug

Include the `.spark` graph if you can — it is plain JSON, it diffs, and it is usually the
whole reproduction. Include the `SPK####` code if one was shown. Say which of Windows or
Linux, and whether the GL or software renderer was in use.

Please do **not** attach a graph you would mind being public. Nobody at this project can
make a public issue private again.

## Security

A Spark graph is executable code. Opening one from an untrusted source is equivalent to
running an unknown program, and this is stated in the product rather than papered over —
.NET has no code-access security and Spark will not pretend otherwise. Graphs never
auto-run on open, and `spark run --no-script` exists for CI.

If you find something that undermines *those* guarantees — a graph that runs on open, a
trust prompt that can be bypassed — please report it privately to the maintainer rather than
in a public issue.

## Who maintains this

One person. Reviews may take a while, and an issue with no reply has not been ignored on
purpose.

The practical consequence for you: **open an issue before writing a large change.** A
rejected 2000-line PR is a bad afternoon for you and an awkward one for the maintainer, and
almost all of it is avoidable with a paragraph up front.
