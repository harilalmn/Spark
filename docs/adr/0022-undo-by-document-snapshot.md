# ADR-0022 — Undo is a stack of document snapshots, not a stack of inverse commands

**Status:** Accepted
**Date:** 2026-08-28
**Deciders:** Nicety

## Context

M2 needed undo and redo (`E8-T9`, [FR-63](../PRD.md#8-functional-requirements)). Two things
about Spark's shape were already settled before the question was asked, and between them they
decide most of it.

The first is that **a document already has a canonical text**. `.spark` is canonically-formatted
JSON ([ADR-0017](0017-spark-file-is-plain-json.md)), a graph round-trips through it
byte-identically, and that round trip is a committed test (`E3-T17`, `E3-T18`). There is
therefore already a total, tested definition of *what a document is* — nodes, wires, lacing,
canvas positions and the literals typed into unwired ports — and it is not a definition undo
would have to invent.

The second is that **the cache is keyed by provenance rather than by document**
([ADR-0010](0010-explicit-scale-aware-tolerance.md), `Spark.Engine.CacheKey`). Returning to a
former state re-derives the keys that state had, and those keys are still resident, so the run
that follows an undo computes nothing. That was written down as the cache's central
justification at M0 and, until there was an undo stack, nothing in the repository exercised it
(`E3-T8`).

What was genuinely open is the shape of the stack itself, and the deciding consideration turned
out to be **coverage, not memory** — because the edits Spark has are not all engine mutations.
Moving a node is the case that shows it: a position lives on the canvas node, never enters the
engine graph, and reaches the file only through `CanvasDocument`.

## Decision

**Undo and redo are a bounded stack of whole-document snapshots, taken as `.spark` text.**

`DocumentHistory` holds a present snapshot, a past stack and a future stack, capped at 64 steps,
dropping the oldest. The shell records one snapshot per completed edit and labels it with what
the edit did; undo reopens the previous snapshot through `CanvasDocument.Open` — the same path a
file takes — and adopts it as the document.

Three rules come with it, and each exists because the alternative is a defect a user would meet:

1. **An edit that changed nothing is not a step.** Comparison is on the snapshot text, so
   committing the same literal twice, or dragging a node out and back, records nothing.
2. **An edit whose document cannot be written clears the history.** Keeping it would let a later
   undo jump *over* the unrecordable edit to a state before it, silently discarding work still
   visible on screen.
3. **Replacing the document starts a new history.** Opening a file or loading a demo is a
   boundary; undoing across it would bring back a graph the user had closed.

## Alternatives considered

### A stack of inverse commands

The mainstream choice, and better at the two things people notice first. It is far smaller in
memory — an `AddNode` inverse is a node id, not a document — and it gives precise labels and
precise repair, because undoing a move can put one node back rather than re-adopting a graph.
It also leaves object identity alone, which a snapshot does not.

It lost on **coverage**. An inverse-command stack is exactly as complete as the set of commands
somebody remembered to write an inverse for, and the failure mode when one is missing is not a
compile error or a red test — it is a user pressing Ctrl+Z and watching part of their edit stay
put. Spark's edits are also not all in one place: the engine owns nodes, wires and literals, but
the canvas owns positions and the shell owns which document is loaded, so the command set would
have had to span three layers and stay complete across every future edit type — groups, notes,
freeze, alignment, package upgrades. A snapshot of the canonical file cannot be partially
complete, because the file is already the definition of a document, and it is a definition that
is tested and that every future edit type must satisfy anyway in order to be saveable.

The memory argument, which is the real one against snapshots, is bounded by the cap and is
small in the units that matter: a graph of ordinary size is a few kilobytes of JSON, so a full
64-step history is a few hundred kilobytes. A very large graph makes it a few tens of megabytes,
against a process already holding that graph's evaluated geometry.

### A persistent (immutable) graph model, with undo as a pointer swap

Genuinely the most elegant option: make `Graph` immutable, and undo becomes assigning a
reference. It is instant, it is exact, it removes the coverage question entirely, and it would
compose well with the evaluator's existing habit of reading the whole graph.

It lost on **timing and blast radius**, not on merit. `Graph`, `NodeInstance`, the replicator and
the canvas are all built around a mutable graph edited through a mutation gate, and this would
rewrite that seam in the milestone whose purpose was to make the skeleton usable. It also
interacts with the reflection importer and with the residency rule of
[ADR-0021](0021-brep-kernel-residency.md), where a value carries a native handle and cheap
structural sharing stops being obviously cheap. Worth reopening if the graph model is ever
rewritten for another reason; not worth forcing one.

### Journalling every mutation through the session gate automatically

Attractive because the gate already exists: `SparkSession.Mutate` is the single choke point for
engine edits, so a journal could be recorded there with no cooperation from callers, and no edit
could be forgotten. It lost for a simple reason — **it is not the whole choke point.** Node
positions never pass through it, and neither would groups, notes or any other canvas-side state,
so it would be an automatic mechanism with a manual exception, which is the worst of both. The
snapshot is taken at the same places a journal would have been triggered from, and covers what
the gate cannot see.

## Consequences

### Positive

- Every edit is undoable by construction, including ones the engine never sees, and a future
  edit type needs no undo work beyond being saveable — which it must be anyway.
- Undo restores exactly what saving would have written. There is no second definition of a
  document to drift from the first, and the round-trip test already guards it.
- `E3-T8`'s claim is now exercised: the run after an undo recomputes nothing, and a test asserts
  it rather than a document asserting it.
- Restoring is a small JSON read plus a cache sweep, so it stays instant on graphs far larger
  than the ones it was written against.

### Negative

- **Memory is proportional to graph size times history depth**, where an inverse-command stack
  would have been proportional to edit size. The cap bounds it; it does not remove it.
- **Object identity does not survive an undo.** The document is reopened, so `CanvasNode`
  instances are new, and the canvas draw order becomes the file's canonical order rather than
  the order nodes happened to be created in ([N23](../NOTES.md)). Selection is dropped, and
  anything holding a canvas slot across an undo is holding a stale index.
- Labels are supplied by the caller rather than derived, so a raise site that passes a vague
  label produces a vague menu entry. Nothing checks that the label matches what changed.
- The stack cannot merge or coalesce steps that a command stack could describe structurally —
  a continuous slider drag, when sliders exist, will need its own coalescing rule rather than
  getting one for free.

### Neutral

- Undo depth is a number in one place, and changing it is a one-line change with a known cost.
- The history is not persisted. Closing the document ends it, which is what every comparable
  editor does and what `R8` already assumes for a package upgrade.

## Notes

The cap is 64 rather than "unlimited" deliberately, and the number is a judgement rather than a
measurement: it is far beyond the depth a user reaches in practice and far below the depth at
which a large graph's snapshots would matter. If it ever needs to be defended with numbers, the
measurement to take is the snapshot size of a real graph, not a synthetic one.

`E8-T13`'s autosave and crash recovery are a different mechanism with a related shape, and the
snapshot is the obvious thing for it to write. That is a reason to keep `DocumentHistory`
ignorant of graphs and sessions, which it is: it deals in strings.
