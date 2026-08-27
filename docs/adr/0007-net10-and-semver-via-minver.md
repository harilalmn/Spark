# ADR-0007 — `net10.0` everywhere and SemVer via MinVer, not calendar versioning

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

Two conventions have to be fixed before the twelve project stubs are created at M0, because
both are painful to change afterwards: which target framework the solution uses, and what a
version number means.

The prior art pulls in opposite directions. `CADScript` targets .NET 8 because AutoCAD 2025
does, and its D11 records what happens when a plugin's dependencies want a newer BCL than the
host: `System.*` assemblies are on the trusted-platform list, so a newer copy beside the
plugin is never consulted and the load fails outright. `DoodleSharp` uses calendar versioning,
which suits an application nobody compiles against. Spark is neither of those: it is a
standalone application first, and it ships contract assemblies that other people build
against — embedders referencing `Spark.Host`, and node authors referencing `Spark.Api`.

## Decision

Every project targets `net10.0` — source, tests and benchmarks alike. No multi-targeting, and
no `-windows` TFM anywhere, enforced as rule 5 of the architecture tests. Nullable is enabled,
implicit usings are disabled, `AllowUnsafeBlocks` is false, and warnings are errors in CI only
rather than in the csproj. Package versions are managed centrally and pinned exactly.

Versions are SemVer, derived by MinVer from git tags of the form `v1.2.3`, and the release
workflow verifies that the built version matches the tag. `graph.formatVersion` is a separate
monotonic integer, decoupled from the product version entirely.

## Alternatives considered

### Target `net8.0` for host reach

.NET 8 is LTS and is what today's Revit and AutoCAD releases run, so an add-in embedding
`Spark.Host` would load without argument. It lost because Spark is standalone-first: the
embedding milestone is M8, roughly two years out, by which time the host runtimes will have
moved, and choosing today's host runtime now guarantees being one release behind for the
project's whole life. Targeting the current framework also keeps us on the current Roslyn and
BCL, which matters most in `Spark.Scripting`.

### Multi-target `net8.0` and `net10.0`

Reach today plus reach tomorrow, at the cost of a doubled build matrix — the same trade
`CADScript`'s D7 made in the opposite direction. It lost because every `#if` is a place where
kernel behaviour can diverge between targets while the tests only exercise one, and because
the golden-file and property-based test suites would have to be trusted on both. Doubling the
matrix for runtimes nothing currently requires is not a cost worth paying at M0.

### A `-windows` TFM for `Spark.Desktop`

Windows is the only release target under D14, so the TFM would cost nothing today and unlock
Windows-specific APIs if ever needed. It lost because it silently breaks the `ubuntu-latest`
rot-guard job, and that job is the only thing stopping cross-platform support from decaying —
which would quietly convert ADR-0001 from a strategic choice into wasted effort.

### Calendar versioning, as `DoodleSharp` does

CalVer communicates recency honestly and removes every argument about whether a change is
breaking. It lost because `Spark.Api`, `Spark.Geometry`, `Spark.Geometry.Io`, `Spark.Scripting`
and `Spark.Nodes.Core` are contract assemblies that people compile against, and under ADR-0019
the question "does upgrading break me?" has to be answerable from the number. `2026.8.27` does not
answer it.

### Hand-written version numbers in the csproj

Simple and explicit. It lost because the tag and the number can then disagree, and the release
workflow would be verifying a human's diligence. MinVer derives the version from the tag, so
they cannot.

## Consequences

### Positive

One toolchain, one build matrix, one set of behaviour to test. The version number carries
information an embedder or a node author can act on, and the public-API baselines of ADR-0019
are what make a change to the public surface visible in the diff that decides it. Exact pinning means a floating dependency cannot move our BCL
requirements underneath us, which is the shape of `CADScript`'s Roslyn problem.

### Negative

Any host pinned to an older runtime cannot load `Spark.Host` in-process at all, so M8's
embedding proof is constrained to whichever of Revit and AutoCAD is on `net10.0` at the time —
and if neither is, embedding needs an out-of-process approach that is not currently designed.
SemVer also requires actually deciding what is breaking, every release, which is judgement
work that CalVer would have avoided.

### Neutral

Warnings-as-errors in CI but not locally keeps day-to-day development pleasant while leaving
the gate absolute. Implicit usings stay off because explicit usings matter for a library
people script against.

## Notes

Revisit the TFM when a target host's runtime is known and a decision is actually blocked by
it — not before. Revisit nothing about the versioning scheme; changing it after releases ship
is worse than any problem it could solve.
