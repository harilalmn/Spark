# ADR-0012 — Rank-based replication with five lacing modes, specified before implementation

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

Replication — what happens when a list arrives at a port expecting a single value — is the
feature that separates a graph engine from a toy. To an AEC user it is not an advanced
capability; it is how anything gets built, because every real graph makes many of something.

It is also the most dangerous thing in the engine to get wrong. Lacing semantics are load-bearing
for every graph a user ever saves. Once graphs exist that depend on a subtly incorrect behaviour,
fixing it breaks them, and the fix becomes unshippable — the bug is not merely a bug, it is
permanent. That is why lacing is folded into M2 rather than deferred: retrofitting rank semantics
into a shipped evaluator is far more expensive than building them in.

## Decision

Replication is **rank-based**. Value rank is scalar 0, list 1, list-of-lists 2. A port's declared
rank comes from the signature. For each input, `excess(i) = rank(actual) − declaredRank(i)`, and
`depth = max excess`. When `depth > 0` the node replicates **one level and recurses** — never
flatten-then-reshape — so nested structure is preserved exactly. Inputs with zero excess broadcast
unchanged; negative excess promotes a scalar into a one-element list.

Five modes: `Shortest` (zip and truncate), `Longest` (zip, short inputs repeating their last
element), `CrossProduct` (all combinations), `Auto` (`Longest`, but zero-excess inputs never
iterate) and `Disabled`. `Shortest`, `Longest` and `Auto` raise output rank by 1; `CrossProduct`
raises it by *k*, the number of replicating inputs.

Lists are carried by `SparkList`, a first-class engine type. Multi-output nodes replicate in
lockstep and then transpose. Per-element failure is isolated: if element 37 of 500 throws, the
rest still evaluate, slot 37 is `null`, and the node emits a Warning naming the failing indices.

**The 40-case table is written as `docs/help/concepts/lacing.md` before implementation and
consumed directly as the test corpus.** It crosses declared rank × actual rank × input count ×
length relationship × mode, plus specials — promotion, empty-list propagation, null passthrough,
ragged nesting, multi-output transpose, three-way cross product, and the `[NoReplication]` and
`[KeepStructure]` attributes. Each row asserts the expected value **and the expected rank
separately**, because rank bugs are precisely the ones that survive value-only tests.

## Alternatives considered

### Flatten-then-reshape, as the simpler implementations do

Considerably easier to implement and to reason about: flatten every input, zip, reshape the
result. It lost because it does not preserve nested structure exactly, and replicating one level
and recursing does — structure preservation is what makes a graph over lists-of-lists predictable
rather than a guess.

### Ship M2 without replication and add it later

The single largest reduction available in M2's scope, and the walking skeleton would arrive
sooner. It lost twice over: a graph engine without replication is a toy to an AEC user, so the
milestone would not demonstrate what it is supposed to demonstrate; and retrofitting rank into a
shipped evaluator means revisiting every node definition, every cache key and every wire-validation
rule, with saved graphs already depending on the pre-rank behaviour.

### Implement first, write the specification from the implementation

The normal order, and it has the advantage that the specification describes something that
demonstrably works. It lost because a specification derived from an implementation documents the
bugs along with the behaviour, and there is then nothing independent to test against. Writing the
table first makes it the design instrument *and* the test corpus, which is the only arrangement
where a disagreement between them is informative.

### `List<object>` or raw `IEnumerable` instead of `SparkList`

No new type, and it works with everything. It lost because rank has to be O(1) and unambiguous,
and neither of those does. Is a `string` a list of characters? Is a `Point3d[]` a list of points
or an opaque value the node wants whole? `SparkList` answers those questions once, in one place.

## Consequences

### Positive

Nested structure survives replication exactly, which is what makes lacing composable. `Disabled`
is always available as an escape and is required for inherently rank-1 nodes like `List.Count`,
which must never lace at their declared rank. The specification-first approach means the behaviour
is documented before anyone can depend on undocumented behaviour.

### Negative

Five modes is five behaviours users must learn and we must teach, and mode selection is a per-node
setting that will confuse people. `CrossProduct` raising rank by *k* rather than 1 is the part
implementations habitually get wrong — 10 × 10 must yield a 10 × 10 nested list, not a flat 100 —
so it is also where our own bugs will live. `SparkList`'s marshalling to and from declared
collection types is on the hot path and carries a standing benchmark. And because a semantics change
must be gated by `graph.formatVersion` so that a fix never silently alters an existing graph, no
lacing correction is ever free.

### Neutral

The per-element failure fast path runs uncaught until the first failure and then restarts with
catching enabled, so the happy path pays nothing — at the cost of re-running the elements before
the failure when there is one.

## Notes

`Invoke` is an expression-tree-compiled delegate rather than `MethodInfo.Invoke` specifically
because of this decision: under replication over 100k items the reflection path is 50–100× slower,
which would make lacing unusable regardless of how correct it is.
