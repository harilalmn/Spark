# ADR-0021 — Kernel residency is canonical, not cached

**Status:** Accepted
**Date:** 2026-08-28
**Deciders:** Nicety
**Amends:** [ADR-0003](0003-ibrepkernel-seams-operations.md)

## Context

[ADR-0003](0003-ibrepkernel-seams-operations.md) seams operations and not the data model, and
that judgement survives [ADR-0020](0020-occt-via-c-abi-shim.md) intact. Every argument it made
still holds: abstracting the geometry types would cost the readonly structs their reason for
existing, break reflection-driven serialization, and make cross-assembly `Type` identity a
property of the provider rather than of Spark.

What does not survive is the third of its three supports, and the sentence that carries it:

> providers may cache an opaque handle against our immutable geometry, so a chain of ten
> operations converts once rather than twenty times.

That is written as an **optimisation**, and it is framed in terms of **speed**. With a real
OCCT provider in front of us, it is neither. **A Spark → OCCT → Spark round trip is not
identity.**

- OCCT carries per-vertex, per-edge and per-face tolerances that our model does not have.
- `ShapeFix` may legitimately merge vertices, re-parameterise edges and split faces at seams.
- Trim curves are recomputed rather than carried through.
- A face produced by intersection may come back as a B-spline where the input was a cylinder.

None of that is a defect. It is what a tolerant BRep kernel does, and it is part of why OCCT
works at all. But it means a ten-operation chain with convert-in and convert-out does not
merely cost twenty conversions — **it re-sews and re-tolerances the model twenty times, and
the user watches their geometry drift while doing nothing.** That is a correctness failure,
not a performance one, and a handle cache described as an optimisation does not obviously
prevent it: an optimisation is by definition something you are allowed to turn off.

**The residency rule has to be redrawn, and the reason is fidelity, not speed.**

## Decision

**Residency is canonical, not cached.**

After a kernel operation, **the provider's representation is authoritative**. Our index-based
`Brep` is materialised **lazily, on structural demand** — a topology query, tessellation,
serialisation, a transform, a bounding box, an equality comparison, or the value being handed
to a node that is not a kernel node. A chain of ten operations therefore performs **zero
imports and one materialisation**.

Exactly **two crossings** exist, and they are named:

- **`Import`** — our model becomes the provider's.
- **`Materialise`** — the provider's becomes ours.

**Round-trip is not required to be identity, and no test may assert that it is.** What is
asserted is **tolerance-bounded equivalence**: volume, area, bounding box, topology counts and
watertightness. Anything stronger would be a test of OCCT's internals dressed up as a test of
Spark's.

**Only `Spark.Geometry.Occt` may observe the token.** It is opaque to `Spark.Geometry`,
opaque to `Spark.Api`, and invisible in serialisation.

The sentence to carry away: **the data model does not cross the seam; residency does.**

### Two seam moves

ADR-0003's clean split of *evaluation in front, operations behind* needs two corrections, and
both are cases the original framing does not obviously predict.

**Curve and surface extraction partially crosses.** `Face.SurfaceGeometry` and
`Edge.CurveGeometry` are how a user gets from topology back to geometry, and they are
load-bearing members in the parity register. On a resident shape they are a provider query,
not a lookup in our arrays. ADR-0003's "operations only" wording does not predict this,
because extraction is not an operation in any ordinary sense.

**Tessellation of `Brep` moves behind the seam.** Tessellating a trimmed BRep face is
genuinely hard, and OCCT solves it. The consequence must be said out loud rather than
discovered: **NFR-8's watertightness property now tests a third party's mesher**, and OCCT's
mesher is not guaranteed watertight at default deflection. Mesh tessellation stays ours.

## Alternatives considered

### ADR-0003 as literally written — convert at every operation

Its genuine advantage is that it is the simplest possible thing, and it keeps `Brep` a pure
value with no native resource attached to it, which is worth a great deal. It lost because it
is **wrong, not merely slow**: ten operations means ten re-sewings and ten re-tolerancings, and
the user's geometry drifts under an idle graph. The performance cost was always the stated
objection and it turns out to be the smaller one.

### Keep the handle cache, but as an optimisation

The honest middle position, and the one ADR-0003 actually took. It lost on a definition: an
optimisation is something that may be disabled, and a cache that may be disabled is a
correctness property that may be disabled. If turning the cache off changes the geometry the
user sees, it was never a cache.

### Move the data model behind the seam after all

With a single provider that is now genuinely plausible — `IBrepKernel` could own its own
topology type and we would never materialise at all. It lost for the reasons ADR-0003 already
gave, all of which still apply: reflection-driven serialization, wire-validation `Type`
identity, the `By*` façade's parameter-type dedup, and `RenderPackage` keying. It also loses
on a new one: **`Spark.Geometry` must remain useful with no native component present**, since
M1's demoable is `spark` writing an OBJ polyline and the OBJ, STL, PLY and glTF writers are
ours.

## Consequences

### Positive

**Fidelity is preserved by construction rather than by care.** A chain of operations does not
degrade, because nothing round-trips in the middle of it.

**The performance argument is won as a side effect.** One materialisation instead of twenty
was ADR-0003's goal; it is now a consequence of the correctness rule rather than a separate
thing to remember.

**The crossings are countable.** Two named functions is a surface small enough to test, to
profile, and to reason about when a shape comes back wrong.

### Negative

**`Brep` stops being a pure value.** It carries a finalizable native resource, with everything
that implies: lifetime, disposal, finaliser ordering, and the possibility of a shape outliving
the provider that made it. This is a real cost and it is the price of the decision.

**The evaluation cache is wrong as specified. NFR-4 must change.** An LRU evicting by
*estimated managed size* cannot see native bytes, so a graph holding 200 cached `Brep`s may be
holding **gigabytes of OCCT heap while reporting megabytes**. The cache must track a **native
budget reported by the shim**, alongside the managed one.

**Equality and hashing must be defined on the materialised model, never on the handle.** Two
handles to equivalent geometry are not equal handles, and a hash of a pointer is a hash that
changes between runs. This forces materialisation on equality, which is a cost, and it is the
correct cost.

**Threading is unresolved and is a top-three risk (R20).** Whether the parallel evaluator may
call the shim concurrently at all, and at what granularity, is not known. Recorded as an open
question rather than assumed either way.

**NFR-8 now tests somebody else's mesher.** The watertightness property must either hold
against OCCT's output at a deflection we choose, or be restated to say precisely what it
guarantees. **It must not quietly become a suppressed test** — that is the outcome this
paragraph exists to prevent.

### Neutral

**One provider, for 1.0 and as far as anyone can see.** The seam is retained for `Result<T>`,
for `Capabilities`, and as insurance. A second provider is not planned and must not be built
to justify the abstraction.

**`Capabilities` changes character but not shape.** ADR-0003 designed it so users could see
what a staged kernel did not have yet. Under ADR-0020 most of the gaps it was designed to
expose are filled on day one, and what it greys out instead is mesh booleans, deferred to 1.x
because OCCT is poor at them.

## Notes

**What this record does not change.** ADR-0003 keeps its number, its text and its argument.
The operations-not-types decision is right and is untouched; what is amended is the third
support and the residency rule that follows from it. Read the two together: ADR-0003 says
*where* the seam goes, this record says *what lives on which side of it and for how long*.

**Two things to measure at M1.6 rather than assume.** First, what a materialisation actually
costs on a shape of realistic size, because the whole rule is built on it being paid once.
Second, whether `ShapeFix` can be constrained — configured to a policy we choose — or whether
it must be accepted as it comes. If it can be constrained, the drift argument weakens and this
record's framing should be revisited; if it cannot, the framing is right and the rule is
load-bearing. Nobody has run either experiment.
