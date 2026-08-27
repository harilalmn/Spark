# ADR-0010 — Explicit scale-aware `Tolerance` hashed into cache keys; no ambient tolerance

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

Every non-trivial geometric predicate needs a tolerance, and where that tolerance comes from
is a design decision with consequences far outside the kernel. `C2VGeometry`'s
`GeometryTolerance` holds roughly 25 correct helper bodies worth harvesting, but its model is
`const` epsilon defaults, which is exactly the part that has to change.

Two Spark-specific constraints shape the answer. First, evaluation caching is
content-addressed by *provenance*, not by value — hashing a 2M-triangle mesh costs more than
recomputing it — so a node's cache key is built from its definition, its lacing, its inputs'
keys and whatever else affects its result. Anything that affects a result and is *not* in the
key produces a silently stale answer. Second, D12 makes coordinates unitless: there is no
`UnitSystem`, no unit types and no conversion, so the kernel genuinely cannot know whether a
coordinate of 1.0 means a kilometre or a micron.

## Decision

Tolerance is an explicit, passed value. `Tolerance { Linear, Angular, RelativeEpsilon }` is a
readonly struct, and kernel signatures take `in Tolerance tol = default`, where `Linear == 0`
means "use the document default" — which preserves `C2VGeometry`'s per-call ergonomics without
its baked-in constants.

The document's tolerance lives on the document, flows through `EvaluationContext`, and **is
hashed into every node's cache key**, so changing it invalidates exactly the affected nodes
and nothing else.

Tolerance is scale-aware: `Tolerance.ForScale(characteristicLength)` derives an appropriate
tolerance, because a fixed 1e-6 is wrong for kilometres and wrong for microns. This survives
D12 intact — it is numerical robustness, not units.

## Alternatives considered

### An ambient or static default tolerance

Terser signatures, no plumbing, and closest to the harvested `GeometryTolerance` bodies, which
would shorten the M1 harvest. **This is the decisive rejection.** An ambient tolerance is
invisible to the cache: changing it would not change any node's key, so the graph would go on
serving results computed at the old tolerance with no indication that anything was wrong. A
stale geometry result that looks correct is the worst failure mode this system can have.
Separately, `const` defaults bake into callers at compile time, so a package built against 1.0
would carry 1.0's epsilon forever — an ADR-0009 hazard hiding inside a constant.

### Tolerance as a property on each geometry object

It travels with the data, which is genuinely attractive: geometry built coarsely stays coarse
through every downstream operation. It lost because geometry in Spark has no identity, no
style and no state by construction — that is the whole point of stripping `C2VGeometry`'s
`Shape` coupling — and because a binary operation between two operands with different
tolerances has no principled reconciliation rule. Identity comes from the graph tuple
`(NodeId, PortIndex, ElementPath)`; tolerance comes from the evaluation context. Neither
belongs on the geometry.

### A single fixed absolute tolerance

The simplest thing that could work, and what a great deal of geometry code does. It lost
because unitless coordinates make the working scale unknowable in advance, so any fixed value
is right for one class of model and wrong for the others. Scale-awareness is what replaces the
unit system for robustness purposes.

## Consequences

### Positive

Changing the document tolerance invalidates precisely the nodes it affects, and undo of that
change is instant because the old key is still cached. Per-call override remains available for
the cases that need it. `ForScale` gives a defensible default for models at any scale, and the
property-based tests can exercise the kernel at extreme scales because tolerance is a parameter
rather than a constant.

### Negative

Every kernel signature carries a parameter most callers never set, which is visible noise in
the API and in the generated documentation. The `Linear == 0` sentinel meaning "use the default"
is a subtlety that has to be documented on every such signature and that a careless caller can
misread as "zero tolerance". And because tolerance participates in every key, changing it is a
full recompute of everything tolerance-dependent in the graph — correct, but expensive, and
users will feel it.

### Neutral

Node ports do not expose tolerance by default; it is a document-level setting with per-node
override available where it matters, which keeps the common case clean.

## Notes

Revisit if a real use case appears for two different tolerances within one evaluation — a
coarse preview branch and a precise export branch in the same graph. The current design can
express that with per-node overrides, and the cache keys already distinguish them; what it
cannot do is make the choice automatically.
