# Architecture Decision Records

**Last updated:** 2026-08-27

This directory holds Spark's architecture decision records. An ADR captures a decision that
could have gone differently: what forced it, what we chose, what we rejected and why, and what
the choice costs us. Records are written once and are not edited to reflect later opinion — a
decision that changes is superseded by a new record that says so, and the old one keeps its
number and its status changes to *Superseded by ADR-NNNN*.

Numbers are stable, never reused and never renumbered. A gap in the sequence means a record was
withdrawn, and the gap stays.

## Index

| # | Title | Status | Date |
|---|---|---|---|
| [0001](0001-avalonia-not-wpf.md) | Avalonia as the UI framework, not WPF | Accepted | 2026-08-27 |
| [0002](0002-own-managed-geometry-kernel.md) | Own pure-managed BRep/NURBS kernel; no native dependencies in the default build | Accepted | 2026-08-27 |
| [0003](0003-ibrepkernel-seams-operations.md) | `IBrepKernel` seams operations, not the data model | Accepted | 2026-08-27 |
| [0004](0004-idiomatic-core-plus-by-facade.md) | Idiomatic C# core plus `By*` façade, with parameter-type-sequence dedup for node generation | Accepted | 2026-08-27 |
| [0005](0005-api-engine-host-layering.md) | `Api`/`Engine`/`Host` layering for embeddability | Accepted | 2026-08-27 |
| [0006](0006-mit-licence-dco-not-cla.md) | MIT licence, DCO rather than a CLA | Accepted | 2026-08-27 |
| [0007](0007-net10-and-semver-via-minver.md) | `net10.0` everywhere and SemVer via MinVer, not calendar versioning | Accepted | 2026-08-27 |
| [0008](0008-csharp-via-roslyn-not-designscript.md) | C# via Roslyn as the scripting language, not DesignScript | Accepted | 2026-08-27 |
| [0009](0009-api-and-geometry-strictly-additive.md) | `Spark.Api` and `Spark.Geometry` are strictly additive across 1.x | **Superseded by [0019](0019-deliberate-public-api-change-control.md)** | 2026-08-27 |
| [0010](0010-explicit-scale-aware-tolerance.md) | Explicit scale-aware `Tolerance` hashed into cache keys; no ambient tolerance | Accepted | 2026-08-27 |
| [0011](0011-angle-struct-in-public-signatures.md) | An `Angle` struct in every public angular signature; radians internally | Accepted | 2026-08-27 |
| [0012](0012-rank-based-replication-specified-first.md) | Rank-based replication with five lacing modes, specified before implementation | Accepted | 2026-08-27 |
| [0013](0013-immediate-mode-node-canvas.md) | Immediate-mode node canvas over a retained `SceneIndex` | Accepted | 2026-08-27 |
| [0014](0014-opengl-viewport-with-software-fallback.md) | `OpenGlControlBase` viewport behind `IViewportRenderer`, with a software fallback | Accepted | 2026-08-27 |
| [0015](0015-xml-docs-as-single-source-of-truth.md) | XML doc comments as the single source of truth for API documentation | Accepted | 2026-08-27 |
| [0016](0016-no-dynamo-interoperability.md) | No Dynamo `.dyn` interoperability in either direction, and no importer seam | Accepted | 2026-08-27 |
| [0017](0017-spark-file-is-plain-json.md) | `.spark` is canonically-formatted JSON, not a container | Accepted | 2026-08-27 |
| [0018](0018-property-based-tests-on-the-kernel.md) | Property-based tests on the kernel from M1, not later | Accepted | 2026-08-27 |
| [0019](0019-deliberate-public-api-change-control.md) | Deliberate change control on `Spark.Api` and `Spark.Geometry`, not permanent additivity | Accepted | 2026-08-27 |

Statuses are *Proposed*, *Accepted*, *Superseded by ADR-NNNN* or *Withdrawn*. Every record in
this set was accepted at M0, before implementation, which is the point of having made the
decisions explicitly.

**One supersession has already happened, and it is worth reading as a pair.** ADR-0009 made
`Spark.Api` and `Spark.Geometry` strictly additive for the whole of 1.x, on the premise that
Spark would publish its contract assemblies to nuget.org and that a breaking change would
therefore break every installed package at once. That premise was a misread requirement: Spark
**consumes** NuGet packages and loose DLLs, and publishes nothing. ADR-0019 replaces the rule
with proportionate deliberate change control and records why. ADR-0009 keeps its number and its
text — the argument it made was sound given what it believed, and the correction is more useful
beside it than in place of it.

## Where a piece of writing belongs

This boundary is written down so that it is not re-litigated in review:

- **ADR** — a decision that could have gone differently.
- **NOTE** — a non-obvious implementation fact. These live in `docs/NOTES.md` with stable
  numbers that are never renumbered and never reused, with gaps left on deletion.
- **Help topic** — something a user needs. These live in `docs/help/`, one per concept and node
  family, each with a worked example.
- **XML doc comment** — what this member does. On the member itself, in source, and the single
  source of truth for API reference (ADR-0015).

If a piece of writing fits two of these, it is usually because it is really two pieces of
writing. A decision with a user-visible consequence gets an ADR *and* a help topic, and they
say different things: the ADR says why, the help topic says how to use the result.

## Format

Each record follows the same headings, and the alternatives section is the part that matters
most six months later:

```
# ADR-NNNN — Title

**Status:** Accepted
**Date:** YYYY-MM-DD
**Deciders:** Nicety

## Context
## Decision
## Alternatives considered
## Consequences
### Positive / ### Negative / ### Neutral
## Notes
```

Rejected alternatives are stated fairly, with their genuine advantages named before the reason
they lost. An ADR that strawmans its alternatives is worthless when someone reopens the question,
because the argument it records is not the argument that was actually had. Negative consequences
are stated plainly for the same reason: a record that only lists benefits is marketing, and
nobody trusts it the second time.

## New records

Copy the format above, take the next unused number, and add a row to the index table in the same
commit. `docs/NOTES.md` and the help topics are separate systems with their own conventions;
see the taxonomy boundary above before writing in the wrong one.
