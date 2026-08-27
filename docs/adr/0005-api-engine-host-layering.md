# ADR-0005 — `Api`/`Engine`/`Host` layering for embeddability

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

D5 commits Spark to being standalone now and embeddable by design. The embedding proof is
M8 — `Spark.Host` running inside a real Revit or AutoCAD add-in through the host-thread
scheduler — which is roughly two years out. The temptation is therefore to build one
assembly now and split it when embedding actually matters.

Two facts make that temptation expensive. First, ADR-0019 puts `Spark.Api` and
`Spark.Geometry` under deliberate change control across 1.x, and a boundary retrofitted after
third-party packages and user node DLLs exist is a breaking change by definition. Second, the prior art shows
what host coupling costs when it is not designed out: `RCS` and `CADScript` each solved the
scripting problem against a hostile host, and the parts that ported cleanly between them are
exactly the parts that never referenced host types.

## Decision

Twelve source projects with an enforced reference graph, from M0, before there is any code
to layer.

```
Geometry ─┬─> Geometry.Io
          └─> Api ─> Engine ─┬─> Scripting
                             ├─> Packages
                             └─> Host ─> Cli
Nodes.Core ─> {Geometry, Geometry.Io, Api}
Viewport   ─> {Geometry, Api}
UI ─> {Api, Host, Viewport, Avalonia} ─> Desktop
```

`Spark.Api` holds contracts only — node attributes, `SparkList`, `SparkDiagnostic`,
`IBrepKernel`, `IEvaluationScheduler`, `IHostServices`, `HelpRenderer`, the graph DTO schema.
`Spark.Host` is the `SparkSession` composition root with no UI in it. Five rules are enforced
by a source-scanning `Spark.Architecture.Tests`:

1. `Spark.Api` references only the BCL and `Spark.Geometry` — never Roslyn, Avalonia, NuGet
   or `Spark.Engine`.
2. `Spark.Nodes.Core` never references `Spark.Engine`, which forces first-party nodes through
   the same zero-config importer as third-party ones so the importer cannot quietly
   special-case us.
3. `Spark.Viewport` is Avalonia-free; only `Spark.UI` adapts it.
4. Nothing under `src/` references anything under `tests/`.
5. No `-windows` TFM anywhere.

## Alternatives considered

### One `Spark` assembly, split later

Least ceremony, fastest refactoring, and no cross-project friction while the design is still
moving — which at M0 it certainly is. It lost because the split cannot be deferred past the
moment `Spark.Api` is a surface anyone builds against, which is M2, and because rule 2 in
particular only works if
it has always been true. Once a first-party node has quietly used an engine internal, the
importer has a special case in it and nobody notices until a third-party package hits the
same path and fails.

### Layer by convention, not by assembly

Namespaces and code review, with a DI container to keep the host abstraction honest. Its
advantage is that refactoring across a layer stays cheap. It lost because conventions are not
checkable and assembly references are: `Spark.Architecture.Tests` can only assert what the
project graph actually expresses. Convention-only discipline works for one author, which is
the same argument that puts analyzers into an OSS repo with drive-by PRs.

### Split only `Spark.Api` out, keep the rest together

A reasonable middle: the contract assembly is isolated, everything else is one assembly.
It lost on the Avalonia boundary specifically. `Spark.Viewport` must be Avalonia-free for the
software renderer to run headlessly in CI and for `spark render` to work from the CLI, and
that separation does not survive being inside an assembly that also holds `Spark.UI`.

## Consequences

### Positive

Embedding is a supported shape from day one rather than a late port: a CAD add-in references
`Spark.Host` and supplies `IHostServices` and a host-thread `IEvaluationScheduler`, with no
Avalonia anywhere in that path. The CLI, the headless docs harness and the CI visual
regression all fall out of the same layering. `Spark.Api` stays small because it has nowhere
convenient to grow, which directly serves ADR-0019.

### Negative

Twelve projects is a lot of scaffolding for a solution with no features in it yet, and every
cross-cutting change costs more than it would in one assembly. Rule 2 has a real price:
first-party nodes cannot use engine conveniences even where it would be simpler, and
occasionally something genuinely useful will have to move into `Spark.Api` or be duplicated.
Agents and contributors must learn the graph before they can place a new type.

### Neutral

File ownership for the agent team is drawn along the same boundaries, so parallel work does
not conflict — which is a side benefit of the split rather than a reason for it.

## Notes

The architecture tests are the load-bearing part of this ADR. If a rule ever becomes
inconvenient enough that someone wants to relax it, that discussion belongs in a new ADR,
not in a test edit.
