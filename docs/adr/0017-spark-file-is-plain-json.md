# ADR-0017 — `.spark` is canonically-formatted JSON, not a container

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

A `.spark` file holds nodes, wires, groups, notes, input literals, required packages and run
settings. It holds no geometry — geometry exists only after evaluation, which is the same
observation that underpins ADR-0016 — so the file is small, structured and text-shaped by
nature. Nothing forces a container on us; the container is a choice.

What shapes the choice is who the file belongs to. Spark is MIT (ADR-0006) and its users will
put graphs on GitHub: in example repositories, in issue reproductions, in pull requests against
`docs/examples/`, and in whatever shared libraries the community builds. For that audience, a
graph that produces a readable diff and can be merged is worth more than a graph that is tidy
on disk. Two further constraints are already fixed: a graph referencing a missing package must
re-save **byte-identically** (ADR-0016), and some graphs reference assets — images, CSV inputs,
imported meshes — that need to travel with them.

## Decision

A `.spark` graph file is **plain JSON, canonically formatted**: stable key order, two-space
indentation, and numbers written with invariant culture and a round-trip format. Canonical
formatting is not cosmetic and is not the writer's discretion — it is what makes the diff
*meaningful* rather than merely textual.

Assets larger than 64 KB are written to a sibling `<name>.assets/` folder, keyed by content
hash. A separate **`.sparkz`** zip bundles the graph, its assets, its custom node definitions
and a thumbnail into one file for sharing, produced by `spark pack`.

`graph.formatVersion` is a **single monotonic integer, decoupled from the product version**, so
that a format change is a format change and a release is a release. **Migrations run
JSON-to-JSON**, never against typed models.

## Alternatives considered

### A zip container throughout

The mainstream choice, and genuinely better at several things we care about. It bundles assets
with no folder discipline required, it makes writes closer to atomic, it compresses well, and it
gives the user one file that cannot be half-emailed. It lost because it makes every change an
opaque binary blob: no review, no `git blame`, no three-way merge, no bisect, and no way to see
in a pull request that someone changed a number from 5 to 50. Those are precisely the things an
open-source tool's users get for free by keeping the graph in text, and they are not recoverable
later.

### A binary or otherwise compact format

Faster to parse and much smaller, and we are not dismissing that in general — it is exactly why
`.sparkgeo` exists for bulk geometry, where JSON for a 500k-triangle mesh is roughly 30× the
size and 50× the parse time. That measurement is real and it decides the geometry case. It does
not decide this one: a graph is thousands of small records, not millions of floats, so the parse
cost is not where the time goes. The decision is therefore not "JSON is always right" but **JSON
for the graph, binary for bulk data beside it**.

### SQLite as the graph file

Real advantages, and more than people expect: transactional writes, partial loads for very large
graphs, indexed queries over nodes, and no rewrite-the-world on save. It lost the same way the
zip did — a `.sqlite` diff is a binary diff — and it adds a dependency and a schema-migration
discipline in exchange for benefits that only appear at graph sizes we can meet with careful
JSON handling.

### JSON without canonical formatting

The cheapest option: serialise and be done. It lost because the benefit evaporates silently.
Without stable key order and invariant numbers, opening an untouched graph and saving it again
produces a diff of reordered keys and re-rendered floats, and a diff that is noisy every time is
a diff nobody reads. It would also break the byte-identical re-save that ADR-0016 depends on.

## Consequences

### Positive

Graphs review like code. A pull request shows which node moved, which literal changed and which
wire was added; conflicts in disjoint parts of a graph merge; `git bisect` works over a graph's
history. Byte-identical re-save falls out of canonical formatting rather than needing separate
machinery. Migrations that operate on JSON stay correct indefinitely, because JSON from 2026
still parses as JSON in 2031.

### Negative

Large graphs produce large text files, and a 5,000-node graph is a slower save and a heavier
working tree than a compressed container would be. Canonical formatting must be enforced *by the
writer*; if it is ever weakened — a new field appended out of order, a number formatted with the
current culture — the diffs degrade quietly and nobody notices until a merge goes wrong. And
assets beside the file make a graph a folder-ish thing: a user who emails only the `.spark` loses
them. That is exactly what `.sparkz` exists to fix, but it only works if it is discoverable, and
if it is buried in the CLI people will hit this before they find it.

### Neutral

`.sparkcustom` uses the same schema and the same rules, since graph-in-graph is the same
mechanism rather than a separate feature.

## Notes

Migrations are never deleted, and each ships with a golden-file test against a real old-version
graph in the corpus. Revisit only if measured save or load times on real graphs become the
complaint — and note that the answer then is probably a faster JSON path, not a container,
because the container gives up something we cannot buy back.
