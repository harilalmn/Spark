# ADR-0018 — Property-based tests on the kernel from M1, not later

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

Geometry kernels fail in a characteristic way. The code passes every example a developer thought
to write, ships, and then fails on a real model — at an unusual scale, on a degenerate input, on
a coincident edge, on a surface that is very nearly but not quite planar. The defect was never in
the anticipated cases, because anticipated cases are the ones that got tested.

Example-based tests can only encode what someone imagined. That is not a criticism of them; it is
their shape. A property encodes an invariant that must hold for *all* inputs, and the generator
supplies the inputs a person would not think of: zero-length vectors, coordinates at 1e-9 and at
1e9, curves whose split parameter lands exactly on an internal knot.

Two things make this urgent rather than optional for Spark. **ADR-0002 committed us to writing our
own kernel rather than wrapping a mature one**, which means we do not inherit decades of someone
else's field-hardening — every robustness bug that OCCT or ProtoGeometry fixed in 2004 is a bug we
get to find ourselves. And ADR-0010 made tolerance an explicit parameter rather than a constant,
which is what makes it possible to *drive* the kernel across scales from a test at all.

## Decision

The geometry kernel carries property-based tests using **CsCheck**, from **M1**, in a dedicated
`tests/Spark.Geometry.Properties` project — **alongside, not instead of, example-based tests**.

The invariants are the actual content of this decision, and they are what the record is for:

- a transform composed with its inverse is the identity, within tolerance;
- a curve split at *t* and rejoined equals the original, within tolerance;
- closest-point never returns a point farther from the query than any sampled alternative;
- boolean union volume is at least the largest input volume;
- **tessellation of a closed solid is watertight — every edge shared by exactly two triangles.**

The last one deserves dwelling on. It is nearly impossible to test by example, because the failure
appears on specific meshes at specific tolerances rather than on any solid a developer would think
to write down. It is also exactly the defect that makes downstream 3D printing, volume calculation
or analysis fail *confusingly* rather than obviously: the model looks right on screen and the
slicer produces nonsense.

## Alternatives considered

### Example-based tests only

Cheaper to write, far faster to run, and trivially debuggable — a failure names one input. This is
what the seed library C2VGeometry does, with roughly 900 tests, and **those are genuinely good
tests**: the ~400 pure-math ones are being harvested precisely because they are a real regression
net from day one. The argument here is **additive, not a criticism**. Example tests pin down the
cases we know are right; they cannot cover a space nobody enumerated, and a kernel's failures live
in exactly that space.

### Adding properties later, once the kernel stabilises

Superficially sensible — write the API first, test it hard once it stops moving — and it is what
most projects do. It is a trap. By the time the kernel has stabilised, the API shape has already
been chosen without any regard to testability: types that cannot be constructed randomly,
operations whose preconditions are undocumented, results that cannot be compared except by eye.
Retrofitting then means generators fighting the design, and the usual outcome is a thin property
suite over the parts that happened to be easy. Having generators from M1 means every new type gets
a generator as part of being finished, which is a design pressure worth having.

### Formal verification

Real, and used in earnest for geometric predicates — exact arithmetic and verified orientation
tests are established practice. It is also wildly disproportionate here: it applies to a small
fraction of the kernel, and the effort would come directly out of building the kernel at all.
Adaptive-precision exact predicates in the mesh boolean (M6) are the part of this we do take.

### A different property library — FsCheck or Hedgehog

FsCheck is better known and has the larger literature. It lost on fit rather than quality: CsCheck
is C#-first, carries no F# dependency into a C#-only solution, and shrinks well.

## Consequences

### Positive

The kernel is exercised across scales, degeneracies and inputs nobody wrote down, which is where
its bugs are. Watertightness becomes a testable property rather than a hope. Generators existing
from M1 pushes the API toward being constructible and comparable, which is a benefit even when no
property fails.

### Negative

Property runs are slow — orders of magnitude slower than the example suite — which is why they
live in a separate project rather than in the inner loop; the fast suite must stay fast enough to
run on every save. Failures arrive as **shrunk counterexamples** and read quite differently from
example failures: a minimal input that violates an invariant, with no narrative about intent, and
the first reaction to one is usually to doubt the property rather than the code. That needs a note
in the test conventions, not folklore. And a flaky property is worse than no property, because it
trains people to re-run the suite: any non-determinism must be **hunted down, never tolerated**.

### Neutral

A **seed is recorded on every failure** so a counterexample can be replayed deterministically, and
a reproducing seed that survives a fix is promoted into the example suite as a permanent
regression test. Properties do not replace golden-file tests, which cover the outputs that have no
invariant to state.

## Notes

Revisit the project boundary, not the decision, if the property suite grows past what CI can
afford per push — the answer is likely a smaller per-push run with a full nightly one, not fewer
properties.
