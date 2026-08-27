# ADR-0002 — Own pure-managed BRep/NURBS kernel; no native dependencies in the default build

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

Spark exists because Dynamo Sandbox is nominally standalone but depends on Autodesk's
ProtoGeometry, which in practice forces users to have an Autodesk product installed. If
Spark solves that by acquiring a different heavyweight dependency, it has moved the problem
rather than removed it.

The starting material is thin. `DoodleSharp\C2VGeometry\` is 78 files and ~20,300 lines,
but it is a 2D *drawing* library, not a geometry kernel: every shape ignores Z, every
`Shape` constructor auto-registers into a global mutable static registry, `Shape` carries
styling and animation fields and drags a viewport into the core type graph, `VTransform`
has no matrix or composition, and `VSpline` is Catmull-Rom rather than NURBS. There is no
mesh, no solid, no BRep and no NURBS surface anywhere in it. Across all six surveyed
projects there is no 3D kernel at all. This is one of the two places carrying nearly all
the project's risk.

## Decision

`Spark.Geometry` is our own pure-managed BRep/NURBS kernel, built in stages: values and
curves at M1, NURBS curves at M3, surfaces and mesh at M5, BRep topology and robust mesh
booleans at M6. Analytic surfaces are first-class types, not NURBS in disguise. The default
build ships no native binaries — Clipper2's C# distribution is pure managed and Boost-licensed,
is pinned exactly and confined to one internal file, and a CI check asserts that no native
binary appears in `Spark.Geometry`'s published output.

## Alternatives considered

### OCCT via native interop

Open CASCADE is a mature kernel with exact booleans, fillets and STEP already solved — the
three hardest things on our roadmap, available on day one. It lost as the *default* because
it means shipping native binaries per RID, which recreates the deployment shape that Spark
exists to escape, and because it would make the pure-managed promise conditional. It is not
rejected outright: ADR-0003's `IBrepKernel` seam is specified so that an OCCT-backed optional
package can be added later without a rewrite, and that is the documented fallback for the
exact-NURBS-boolean risk.

### A commercial kernel — Parasolid, ACIS, C3D

These solve robustness properly; robust surface-surface intersection is precisely what makes
them cost what they cost. They lost because per-seat licensing is incompatible with an MIT-licensed
open-source tool that users install freely (ADR-0006), and because the resulting product would
have a dependency users must pay for — the original problem in a new currency.

### Port C2VGeometry and extend it

Cheapest by a wide margin: the code exists, builds standalone, and has 897 Fact tests behind
it. It lost on inspection. The auto-registering static registry means constructing geometry
mutates process-wide state, which is incompatible with the parallel evaluator; the styling and
animation fields on `Shape` are exactly the coupling the kernel must not have; and the 2D-only
basis is not extensible to surfaces or solids. What survives is harvested deliberately — `VXYZ`'s
algorithms, `VPlane`, `VCoordinateSystem`, `GeometryTolerance`'s ~25 helper bodies, `VArc`'s eight
constructions, the planar boolean pipeline, and above all `RayCaster.cs` with its BVH, which serves
mesh booleans, viewport picking and intersection seeding alike.

## Consequences

### Positive

`Spark.Geometry` is publishable standalone as a useful package in its own right, with no
Autodesk and no native footprint. Deployment is a self-contained .NET publish with nothing
platform-specific. The kernel's semantics are ours to define rather than ours to match.

### Negative

**This is a multi-year problem and it should be stated without softening.** Exact NURBS
surface-surface intersection with tangential and degenerate cases handled robustly is a
research-grade problem, and it is entirely possible that our implementation never reaches
production robustness. 1.0 therefore ships on mesh booleans, with exact NURBS booleans and
solid fillet and chamfer stated publicly as out of scope for 1.0. STEP is ours to write and
is scoped to a documented subset. Numerical robustness at unusual scales is a standing risk
that property-based tests and a growing corpus mitigate but do not eliminate.

### Neutral

Mesh booleans at M6 mean users get working boolean operations long before the exact ones
exist, and the `Capabilities` flag set makes the gap visible in the UI rather than a
surprise at run time.

## Notes

The M5 throwaway SSI spike exists to calibrate the M7 estimate while it is still cheap to
learn that it is hard. If that spike says the exact path is unreachable on our timescale,
the response is to promote the OCCT-backed optional package from fallback to a shipped
option — not to reopen this decision, because the default build's independence is the point
of the project.
