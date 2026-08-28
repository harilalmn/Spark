---
id: concepts.files
title: Saving and opening graphs
nodes: []
related: [concepts.lacing, concepts.curves, concepts.undo]
since: "0.1"
---

**Status:** Current. Describes the `.spark` reader and writer, which exist and are tested.
**Owner:** `graph-engine`
**Last updated:** 2026-08-28

> **Scope.** A `.spark` file holds nodes, wires, lacing, canvas positions and the values typed
> into unwired ports. It holds **no geometry** — geometry exists only after evaluation. Assets,
> the `.sparkz` bundle, custom node definitions and package requirements are later milestones
> and are not in the file yet.

---

## What a `.spark` file is

Plain JSON, formatted the same way every time. That is a deliberate choice with a cost and a
reason ([ADR-0017](../../adr/0017-spark-file-is-plain-json.md)): a zip container would bundle
assets more neatly and write more atomically, but it would make every change an opaque binary
blob — no review, no `git blame`, no three-way merge, and no way to see in a pull request that
somebody changed a number from 5 to 50.

Because Spark is meant to be shared — example repositories, issue reproductions, pull requests
against a shared library — a graph that produces a **readable diff** is worth more than one that
is tidy on disk.

Here is a whole graph, in full:

```json
{
  "formatVersion": 1,
  "nodes": [
    {
      "id": "0e4a1b3c-5d6f-4a7b-8c9d-0e1f2a3b4c5d",
      "key": "Spark.Nodes.Core/Number.Range",
      "lacing": "Auto",
      "x": 30,
      "y": 30,
      "literals": [
        { "port": 0, "kind": "number", "value": 0 },
        { "port": 1, "kind": "number", "value": 9 },
        { "port": 2, "kind": "number", "value": 1 }
      ]
    },
    {
      "id": "1f5b2c4d-6e7a-4b8c-9d0e-1f2a3b4c5d6e",
      "key": "Spark.Nodes.Core/Point.ByCoordinates",
      "lacing": "CrossProduct",
      "x": 300,
      "y": 60
    }
  ],
  "wires": [
    {
      "source": "0e4a1b3c-5d6f-4a7b-8c9d-0e1f2a3b4c5d",
      "sourcePort": 0,
      "target": "1f5b2c4d-6e7a-4b8c-9d0e-1f2a3b4c5d6e",
      "targetPort": 0
    }
  ]
}
```

Read it top to bottom and it says what it does: a range of ten numbers into a point node laced
Cross Product. That legibility is the whole point of the format.

---

## The four things that keep a diff quiet

A file that re-formats itself on every save produces a diff nobody reads, so four rules are
fixed rather than left to the writer:

1. **Keys appear in a fixed order** — `id`, `key`, `lacing`, `x`, `y`, `literals` — written out
   by hand rather than by a serialiser, so that reordering two properties in Spark's own source
   cannot silently change every file it writes.
2. **Nodes are sorted by identity and wires by their endpoints**, so the file never inherits the
   order things happen to sit in memory. Delete a node and re-add it and the file does not
   reshuffle.
3. **Numbers are written in the shortest form that reads back exactly.** One third is not
   rounded to fifteen places, because that is not one third any more.
4. **Two-space indentation, and a trailing newline.**

Together these make **opening a graph and saving it produce no diff at all**, which is asserted
by a test rather than hoped for.

---

## Why each literal carries its kind

A port that expects a whole number and a port that expects a number are different bindings, and
JSON cannot tell `1` from `1.0`. So the kind travels with the value:

```json
{ "port": 0, "kind": "integer", "value": 12 }
{ "port": 1, "kind": "number",  "value": 12 }
{ "port": 2, "kind": "boolean", "value": true }
{ "port": 3, "kind": "text",    "value": "north wing" }
{ "port": 4, "kind": "angle",   "value": 45 }
```

Angles are written **in degrees**, which is the unit the port is edited in — a more faithful
record of what you typed than the radians the kernel holds.

Those five are the only kinds a port can hold. If a graph somehow has something else in a port,
saving is refused **while you still have the value**, rather than succeeding and losing it
quietly (`SPK1063`).

---

## What can go wrong when you open a file

Spark refuses rather than guesses, and every refusal names what it found:

| Code | What happened | What to do |
|---|---|---|
| `SPK1060` | The file is not valid JSON, or is not a graph | Check you opened a `.spark` file; the message says what was missing |
| `SPK1061` | The file was saved by a **newer** build of Spark | Update Spark. Nothing can be recovered safely by guessing at a format this build has never seen |
| `SPK1062` | The file names a node that is not loaded | A package is missing, or the node was renamed. The message names the node |
| `SPK1063` | A port holds a value the format cannot write | Only numbers, whole numbers, true/false, text and angles can be typed into a port |

**A graph containing a cycle still opens.** A file is not a gesture: the canvas refuses to *draw*
a wire that closes a loop, but a file that already contains one — through a hand edit, a bad
merge, or an older build — has to load so you can see it and fix it. Every node on the cycle
reports an error and the rest of the graph evaluates normally.

---

## The version number

`formatVersion` is a single whole number that counts up, and it is **not** the version of Spark.
A format change is a format change; a release is a release. Tying the two together would make
every release a format question.

A file from an older format version is migrated forward when it is opened. A file from a *newer*
one is refused, because the alternative is a build guessing at a shape it has never seen and
silently dropping whatever it did not recognise.

---

## Related

- [Lacing](lacing.md) — the setting each node saves alongside its position
- [Curves, parameters and arc length](curves.md) — what the geometry in a graph is made of
