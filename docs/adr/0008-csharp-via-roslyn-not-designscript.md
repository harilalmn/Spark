# ADR-0008 — C# via Roslyn as the scripting language, not DesignScript

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

A node-based environment needs an escape hatch: the place a user goes when no node does what
they want. In Dynamo that is DesignScript, in a code block. Spark has to choose what fills
that slot, and the choice determines far more than the code block itself — it determines what
the whole extensibility story looks like, because the language a user writes in and the
language packages are written in should be the same one.

Spark is a .NET application whose package manager is NuGet — the one Spark *consumes*, so
that a user can bring any .NET library into a graph and get nodes from it. Spark publishes
nothing of its own (ADR-0019). Every .NET developer already knows C#, and choosing it makes the entire NuGet
ecosystem reachable from inside a graph. There is also a large amount of directly relevant
prior art: `RCS` and `CADScript` have between them already built `ScriptRewriter` and its
source maps, `ReferenceCatalog`, `ScriptLoadContext`, `GuardWeaver`, the editor controllers,
a `CompletionEngine` with a completion-must-match-the-compiler invariant, `ScriptTextRepair`
and `ScriptRunner`'s threading model — all of it UI-agnostic and portable near-1:1.

## Decision

Code blocks host real C# compiled by Roslyn, in two node types over one pipeline: an inline
Code Block and a docked C# Script Node. Roslyn is pinned exactly through Central Package
Management. Input ports are inferred semantically — compile once against the prelude, collect
`CS0103` and `CS0117` diagnostics, take the identifiers in source order — and output ports come
from a named tuple return. Once a port is connected, the rewriter injects the upstream type
rather than `object`, so IntelliSense inside the code block knows the type on the incoming wire.

## Alternatives considered

### DesignScript, or a DesignScript-compatible language

It is what Dynamo users already know, it is terse for the one-liners code blocks are mostly
used for, and replication is built into the language rather than bolted onto the evaluator.
It lost because implementing a language and its semantics is a project in itself, and the only
argument that would justify that cost is compatibility with Dynamo — which ADR-0016 removes
deliberately. A bespoke language also arrives with no IDE, no completion, no ecosystem and no
package manager, so every one of those would have to be built too.

### Python, via IronPython or Python.NET

The strongest alternative on user familiarity: Python is the scripting language of the AEC
tooling world, and both Dynamo and Grasshopper users reach for it. It lost on two counts.
Python.NET drags a native CPython runtime into the process, which contradicts ADR-0002's
no-native-dependencies posture directly; and IronPython lags CPython far enough that the
library ecosystem users actually want is largely unavailable, which removes the reason for
choosing Python in the first place. None of the `RCS`/`CADScript` scripting port applies to
either.

### A small expression DSL for code blocks only

Cheap to build, safe to run, and adequate for the arithmetic and list manipulation that most
code blocks contain. It lost because it cannot reach NuGet, so the escape hatch stops being an
escape hatch the moment a user needs a library — and because the typed-input IntelliSense
demonstration has no analogue in a DSL. That demonstration is the single most compelling thing
Spark can show that Dynamo cannot.

## Consequences

### Positive

The escape hatch and the extension mechanism are the same language, so a code block that grows
too large becomes a node package by moving the file. IntelliSense that resolves the type on the
incoming wire is a genuine differentiator and is the M4 public demonstration. The port campaign
from `RCS`, `CADScript` and `DoodleSharp` means most of M4 is porting rather than inventing.

### Negative

Roslyn has a real cold start, which would make code blocks feel sluggish; `Spark.Scripting` is
isolated so graphs without scripts never load it, with background warm-up and both a resident
and a persistent compile cache. A runaway script can still take down the process:
`StackOverflowException` cannot be caught in .NET, and guard weaving reduces the frequency
without eliminating it — the real fix is an opt-in out-of-process worker, kept viable by the
scheduler and load-context seams and deferred past v1. C# is also more verbose than DesignScript
for the short expressions code blocks are mostly used for; that cost is accepted.

Most importantly, **a Spark graph is executable code**, and .NET has no code-access security,
so opening one from an untrusted source is equivalent to running an unknown program. Pretending
otherwise would be dishonest. What actually works is stated instead: opening never auto-runs,
with Manual mode and a banner listing script nodes and required packages; a content-hash
per-origin trust allowlist; and `spark run --no-script` for CI.

### Neutral

Roslyn must be pinned exactly rather than floated. `CADScript`'s D11 records why: a dependency
whose BCL requirements move can make an assembly unloadable in a host that pins the trusted
platform, and a floating Roslyn is the most likely source of that movement.

## Notes

Revisit only if an out-of-process worker becomes necessary for reasons beyond stack overflow —
for instance if a genuine sandboxing requirement appears. The language itself is settled.
