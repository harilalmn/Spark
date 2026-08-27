# ADR-0003 — `IBrepKernel` seams operations, not the data model

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

ADR-0002 commits to our own pure-managed kernel while keeping an OCCT-backed optional
package as the documented fallback for the operations we may never get exactly right.
That fallback is only real if the seam is designed before either side of it is built —
retrofitting a kernel abstraction into a shipped codebase means changing every signature
that touches geometry, and under ADR-0019 changing those signatures during 1.x is a deliberate
and expensive act rather than a routine one.

The question is where the seam goes. Everything in Spark is keyed off concrete geometry
types: serialization enumerates every concrete type by reflection, the node importer
generates ports from parameter types, `RenderPackage` buffers are keyed by
`(NodeId, PortIndex)` over concrete tessellation output, wire validation compares `Type`
identity across assemblies, and the `By*` façade dedup of ADR-0004 matches parameter *type*
sequences. A seam that abstracts the types themselves would cut through all of that.

## Decision

`IBrepKernel` abstracts **operations only**. The data model never crosses it.

In front of the seam, always ours: every value type; analytic and NURBS evaluation,
derivatives and knot operations; the `Brep` and `Mesh` models and their validation;
tessellation; bounding boxes; transforms; all serialization; all of `Planar`; and ray
casting with its BVH.

Behind the seam: curve/curve, curve/surface and surface/surface intersection; extrude,
revolve, loft, sweep, offset, thicken and shell; fillet, chamfer, split and trim; boolean;
and sew, heal and validate.

Three supports make the seam usable. Every operation returns `Result<T>` carrying
diagnostics and partial results, because kernel failure is normal and must be diagnosable
rather than thrown. A `Capabilities` flag set lets the node library grey out unsupported
operations instead of throwing, which is what makes staged delivery honest. And providers
may cache an opaque handle against our immutable geometry, so a chain of ten operations
converts once rather than twenty times.

## Alternatives considered

### Abstract the whole kernel, data model included

An `IGeometryKernel` owning its own `ICurve`, `ISurface` and `IBrep` would allow a provider
to be swapped wholesale, bringing its own exact evaluation and its own topology
representation with it — the cleanest possible substitution. It lost because every geometry
type would become an interface, which costs the readonly-struct value types their entire
reason for existing, forces reflection-driven serialization to work against implementations
it cannot enumerate, and makes cross-assembly `Type` identity — the thing wire validation
depends on — a property of the provider rather than of Spark. The abstraction would be
paid for on every operation in the product to buy a substitution that happens at most once.

### No seam at all

Simplest: concrete calls, no `Result<T>` plumbing, no `Capabilities`, no handle caching.
Its genuine advantage is that nothing is designed speculatively. It lost because it deletes
the fallback for the project's largest risk, and because without `Capabilities` a staged
kernel has no honest way to tell a user that loft exists and shell does not — the node
simply throws when they run their graph.

### Seam at the node level

Ship alternative node packages backed by different kernels. Attractive because it needs no
new abstraction at all. It lost because two providers' node sets would diverge in naming,
lacing and port shape, so a graph would not survive switching providers — and because
`Spark.Nodes.Core` is generated from `Spark.Geometry` by the same zero-config importer as
third-party packages, which means the node layer is downstream of this decision rather than
a place to make it.

## Consequences

### Positive

The OCCT fallback is absorbed without a rewrite. Staged delivery is honest: M6 formalises
`IBrepKernel` with `Capabilities` gating the UI, and users see what is not there yet rather
than discovering it by exception. `Result<T>` makes kernel failure diagnosable, which
directly serves the numerical-robustness risk.

### Negative

`Result<T>` on every operation is verbose at every call site and inside the node importer,
which must unwrap it into a `SparkDiagnostic`. `Capabilities` means the effective node
library depends on the active provider, so a graph authored against one provider may open
with greyed-out nodes against another — a real portability limitation that must be documented
rather than hidden. The opaque handle cache is a lifetime and invalidation burden on
otherwise-immutable geometry, and getting it wrong produces stale-conversion bugs that look
like kernel bugs.

### Neutral

Because the model stays ours, an OCCT provider is an adapter that converts at the seam and
caches the handle — mechanical work, and the index-based BRep representation was chosen
partly to keep it that way.

## Notes

Revisit if a provider is ever wanted for reasons of *representation* rather than operations —
for instance a subdivision or implicit modelling backend whose data model genuinely cannot
be expressed as our `Brep`. That would be a different decision, not a widening of this one.
