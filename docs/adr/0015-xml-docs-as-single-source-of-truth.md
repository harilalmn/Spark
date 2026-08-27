# ADR-0015 — XML doc comments as the single source of truth for API documentation

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

The client's first standing instruction is that everything is documented as end-user help
topics with worked examples, and the second is that documentation is updated after every
change — no change is done until the affected documents reflect it. A documentation system
that depends on discipline alone will not satisfy either.

This codebase has already run the experiment. `DoodleSharp`'s `DocGenerator.cs` is 6,784 lines
built around three hand-maintained dictionaries — roughly 1,478 member descriptions keyed by
string — and it needed two dedicated test suites merely to stay honest. Even with those, an
audit found **101 of 108 public constructors rendering blank** while **7 carefully written
entries pointed at members that no longer existed**. The descriptions were good. The keys were
strings, and strings do not move when code does.

`DocGenerator` also emits WPF `FlowDocument`s, which have no Avalonia analogue, so ADR-0001
already makes it unportable. That is a welcome forcing function rather than a loss.

## Decision

XML doc comments on the real API are the single source of truth.
`GenerateDocumentationFile=true` everywhere, and **CS1591 is promoted to an error in CI** on
`Spark.Api`, `Spark.Geometry` and `Spark.Nodes.Core`: undocumented public API does not build.
One input yields four outputs — IDE IntelliSense, runtime node tooltips, generated reference
pages, and compile-verified examples — and descriptions for third-party packages come from
their sidecar `.xml` file, so any library shipping one gets tooltips free.

Three tiers sit on top. **API reference is generated**, so nobody writes it and nobody can
forget it. **Help topics are hand-written Markdown** in `docs/help/` with YAML front-matter
(`id, title, nodes[], related[], since, examples[]`), one per concept and node family, each
required to contain a worked example. **Worked example graphs** are real `.spark` files in
`docs/examples/`, openable from the help panel and executed headlessly in CI.

`tests/Spark.Docs.Verify` enforces it: every ` ```csharp ` fence and every XML `<example>`
compiles using the exact references a real code-block node gets; every example graph runs with
no node errors and matches its declared outputs; a new node shipping undocumented fails the
build, and so does a `nodes:` entry naming a node that no longer exists; every `SPK####` code
has a help topic. A `docs-freshness` CI job asserts that a diff touching a public-API baseline
or `src/Spark.Nodes.*` also touches `docs/`, overridable only by an explicit `docs: none-needed`
commit trailer that is visible in review.

## Alternatives considered

### Hand-maintained description dictionaries, as `DocGenerator` does

Genuinely advantageous in ways worth stating: descriptions can be richer and longer than an XML
comment, they can be revised without touching source or triggering a rebuild, and a
documentation author can own them without ever opening a code file. It lost on the measured
outcome above. String keys drift silently, and the two test suites written to catch that drift
caught it only after 101 of 108 constructors had already been shipping blank.

### A separate hand-written documentation site

Better narrative than reference comments can ever be, and full control over structure. It lost
as a *replacement* — it cannot feed IntelliSense or runtime node tooltips, so every member
description would be written twice and would diverge on the first rename. It survives as tier 2,
where narrative is exactly what is wanted and there is nothing for it to duplicate.

### Reflection-generated reference with no prose

Complete, always accurate, zero maintenance. It lost because a list of signatures tells a user
what the parameters are called and nothing about what the member is for.

## Consequences

### Positive

Descriptions move with renames, are `cref`-validated by the compiler, and cannot silently go
blank. A screenshot rots invisibly; an executed example graph does not — this is the strongest
anti-rot mechanism available for a node-based tool, and it is the node-graph analogue of
compiling a snippet. The harness is a build gate from M0, before there is anything to document,
so it can never be retrofitted or quietly skipped.

### Negative

CS1591-as-error means every public member needs a comment before CI passes, including the ones
where the name says everything, and the predictable failure mode is boilerplate comments written
to satisfy the compiler rather than the reader. Review has to police that, and review is the
weakest link in the chain. XML comments also cannot carry cross-cutting narrative, so tier 2
remains hand-written and can still fall behind — the coverage checks catch missing topics, not
stale ones. And because the docs harness gates the build from M0, a broken example blocks work
that has nothing to do with documentation.

### Neutral

`DocGenerator.cs` is not ported. Its 6,784 lines are replaced by generation from XML plus
hand-written Markdown, which is a smaller system doing a strictly larger job.

## Notes

The `docs: none-needed` trailer is deliberately loud rather than silent. A silent exemption is
worthless because it will be used reflexively; a visible one shows up in review and has to be
defended. If it starts appearing on most pull requests, that is the signal to tighten the
freshness rule, not to remove it.
