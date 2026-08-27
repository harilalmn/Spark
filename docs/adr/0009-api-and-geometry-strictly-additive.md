# ADR-0009 — `Spark.Api` and `Spark.Geometry` are strictly additive across 1.x

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

Spark packages are ordinary NuGet packages tagged `spark` with a `tools/spark.json` manifest,
loaded at run time into collectible `AssemblyLoadContext`s — one per package *version*, not per
package (which kills side-by-side) and not per assembly (which kills intra-package type
identity). The `Load` override decides by file existence in the context's own folder rather
than a hardcoded name list.

That design has one mandatory exception. **Contract assemblies always resolve from the default
context.** A `Circle` produced by package A must be the same `Type` as a `Circle` consumed by
package B, or the two cannot be wired together — and wiring is the entire product. Wire
validation compares types across assemblies precisely so that two ports sharing a `FullName`
from different assemblies are refused at design time rather than producing an incomprehensible
`cannot cast Foo to Foo` at run time.

The consequence follows directly: `Spark.Api` and `Spark.Geometry` cannot be side-by-sided.
There is exactly one of each in the process, for every package loaded.

## Decision

`Spark.Api` and `Spark.Geometry` are **strictly additive** for the whole of 1.x. New types,
new members and new interfaces may be added. Existing public types, members, signatures and
semantics may not be changed or removed; a mistake is deprecated with `[Obsolete]` and
superseded, never edited. This is enforced by public-API analyzers with checked-in baselines
from M0, so every public-API addition is a reviewed line in a text file.

Two design rules follow from it. `Spark.Api` is kept deliberately small — it is a contract,
not a convenience library, and anything that can live in `Spark.Engine` does. And a new
interface is always preferred over changing an existing one.

## Alternatives considered

### Allow breaking changes at a major version, as SemVer permits

The normal .NET convention: bump the major, publish migration notes, let consumers move at
their own pace. It lost because the load model does not permit the "at their own pace" part.
A 2.0 `Spark.Geometry` cannot coexist with 1.x in one process, so the day it ships, every
installed package built against 1.x stops working simultaneously — not gradually, and not
per-package. SemVer would describe the breakage accurately; it would not make it survivable.

### Side-by-side the contracts as well

Load each package's own copy of `Spark.Api` and `Spark.Geometry` into its own context, giving
packages genuine independence and letting an old package keep working forever. This is the
alternative with the strongest theoretical appeal, and it is how the rest of the package's
dependencies are handled. It lost on type identity: a `Circle` from package A's context would
not be assignable to a `Circle` port declared by package B, so nothing could be wired between
two packages, and even a single package's output could not reach a first-party node. The whole
value of a shared node graph is that types are shared.

### Type forwarders and compatibility shims

A well-established technique that would let some breaking changes survive — moves between
assemblies and renames in particular. It lost because it addresses only the cheap cases.
Signature changes and semantic changes are the ones that actually hurt, and no forwarder helps
there. Worse, shims make the public-API baseline diff understate what changed, which removes
the one mechanism that tells us a break has happened.

## Consequences

### Positive

A package built against Spark 1.0 works against every 1.x release, which is the guarantee that
makes writing one worthwhile. The baseline files make API growth a deliberate, reviewed act
rather than an accident, and they double as the trigger for the `docs-freshness` CI job.

### Negative

**One breaking change breaks every installed package at once.** There is no gradual migration
path and no per-package opt-in; a 2.0 is a flag day for the entire ecosystem. Within 1.x, some
design mistakes are permanent: an awkward signature shipped in `Spark.Geometry` at M1 is still
there at 1.9, with a better-named replacement beside it, and both must be documented and both
generate nodes unless one is excluded. Deprecated API therefore accumulates. The baseline file
also becomes a review chokepoint that every API-touching pull request passes through.

### Neutral

The constraint pushes API design toward smaller, more considered surfaces, which is a good
outcome reached by an uncomfortable route. `Spark.Engine`, `Spark.Scripting`, `Spark.Packages`,
`Spark.Host` and the UI projects are not bound by this rule and can evolve normally.

## Notes

The pressure release valve, if one is ever needed, is a new additively-introduced interface
that supersedes an old one, with the old one obsoleted and left working. If a change ever
genuinely cannot be expressed that way, the correct response is to schedule it for 2.0 and
accept the flag day — not to break 1.x quietly and hope few packages exist yet.
