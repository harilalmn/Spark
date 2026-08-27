# ADR-0016 — No Dynamo `.dyn` interoperability in either direction, and no importer seam

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

Spark is positioned as an independent alternative to Dynamo Sandbox, so the first question
anyone asks — the client asked it, and the technical lead had already worked it through — is
whether existing `.dyn` graphs will open. It is the obvious adoption lever, and refusing it
needs a better reason than difficulty.

**The client and the technical lead reached the same conclusion independently, before
comparing notes.** That is recorded here because it is the strongest evidence available that
this is a considered position rather than a scope cut, and because it means the decision does
not rest on one person's judgement.

The reason is not the one people assume. **A `.dyn` file contains no geometry.** It is JSON
holding nodes, connectors and view state; geometry exists only after evaluation. Reading one
therefore never requires ProtoGeometry and is not technically hard. Writing one is no harder.

The hard part was never reading. It is **semantic equivalence**: an imported graph is only
useful if Spark's `Circle` node behaves identically to ProtoGeometry's in every degenerate
case, at every tolerance, and under every lacing rule. Establishing that requires testing
against ProtoGeometry — the very dependency Spark exists to remove. Without it, equivalence is
unprovable, and an importer that silently mistranslates is strictly worse than no importer:
the user gets a graph that opens, looks right, and produces subtly different geometry, and does
not find out until the model is built.

## Decision

No Dynamo compatibility in either direction. No `.dyn` reading, no `.dyn` writing, no importer,
and **no importer seam** — no abstraction, extension point or reserved schema field held open
against a future importer.

Because there is no compatibility obligation, **the `By*` names of ADR-0004 are for human
recognition only and carry no semantic contract with ProtoGeometry.**

## Alternatives considered

### A full `.dyn` importer

The strongest adoption argument the project has: existing users arrive with existing work, and
a tool that opens it is one they can try without commitment. It lost on unprovable equivalence,
as above — and the failure mode is silent and delayed, which is the worst combination.

### A best-effort importer with prominent warnings

The pragmatic version: import what maps cleanly, mark the rest as unresolved, and tell the user
plainly that the result needs checking. This is the alternative that deserves the most respect,
because warnings do shift responsibility honestly and a partial graph is a real head start. It
lost because the warning cannot be specific. We would be able to say "this may differ" but never
where or how, since we cannot test against ProtoGeometry to find out. A warning attached to
everything is a warning attached to nothing, and users would learn to dismiss it — after which
the failure mode is identical to the silent one.

### An importer as a third-party package, with a seam kept open

Superficially the best of both: Spark ships nothing, and anyone who wants interop builds it
against a documented extension point. It lost because the seam is not free. Held-open extension
points shape the design around them, they appear in `Spark.Api` and are therefore frozen for
1.x under ADR-0009, and they create a standing expectation that someone will fill them. A third
party can already build an importer against the public graph-construction API with no special
accommodation, which is the correct amount of support for it.

### Export to `.dyn`

Fewer equivalence problems in principle, since we would be emitting rather than interpreting.
It lost the same way in the other direction: we cannot verify that an exported graph evaluates
equivalently in Dynamo, and a broken export damages confidence in Spark rather than in the
exporter.

## Consequences

### Positive

The one force that would have pulled Spark's API toward ProtoGeometry's semantics is removed.
Node names, port shapes, degenerate-case behaviour, tolerance handling and lacing rules are
free to be what is right for Spark rather than what matches an unobservable reference
implementation. No support burden for translation defects, and no class of bug report that
cannot be diagnosed.

### Negative

There is no migration path for existing Dynamo work, which is a real adoption cost and the
first objection Spark will meet publicly. Users rebuild their graphs. This is the price of the
decision and should be stated plainly rather than softened.

### Neutral — the audit

Four properties of the graph model could be mistaken for importer scaffolding. Each was
audited and each is justified by something else entirely:

- **A public graph-construction API** exists because the CLI, the tests and
  collapse-to-custom-node all need to build graphs programmatically.
- **Stable string `NodeKey`s separate from display names** exist for save/load round-tripping
  and for the library search index; a display name that a user can see must be free to change
  without breaking a saved graph.
- **First-class lacing** is a core requirement in its own right (ADR-0012) and would be built
  identically if Dynamo did not exist.
- **An unresolved-node placeholder** exists so a graph referencing a missing package opens
  without damage — the headline package behaviour, where nodes load preserving the definition
  key, every literal and every wire verbatim, and re-save byte-identically.

None of the four is retained for interop, and none should be extended for interop reasons.

## Notes

The only thing that would reopen this is a way to establish semantic equivalence without
depending on ProtoGeometry — for instance a published, testable specification of its node
semantics. Absent that, the answer does not change, and it should not be re-litigated when the
adoption argument is raised again, which it will be.
