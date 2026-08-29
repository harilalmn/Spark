---
id: concepts.evaluation
title: How a graph evaluates
nodes: []
related: [concepts.lacing, concepts.reading-results, concepts.files]
since: "0.1"
---

**Status:** Current. Describes the engine as built, and the five `SPK101x` codes that resolve
here.
**Owner:** `graph-engine`
**Last updated:** 2026-08-28

> Five diagnostic codes have pointed at this page since M0 and the page did not exist, so
> anyone following one of them arrived nowhere. That is the reason it is written now, and the
> reason the documentation harness has since gained a check that a topic id in the source names
> a topic that is really there.

---

## What happens when you change something

Spark evaluates **the part of the graph your change can reach, in dependency order, and nothing
else**.

1. **The node you changed is marked dirty.** So is everything downstream of it, because a node
   whose input changed cannot keep its old answer.
2. **The dirty set is sorted into dependency order.** A node runs only after every node it draws
   an input from has run.
3. **Each node is asked for its answer.** Before it computes anything, the engine looks for that
   answer in the cache.
4. **Everything else is left alone.** A node the change cannot reach is not re-run, is not
   re-marked, and keeps the value it already produced.

Nothing about that order is configurable, and nothing about it depends on where nodes sit on the
canvas. **Position is layout, not order.** Two graphs that are wired identically evaluate
identically however they are arranged, which is what makes moving a node safe.

## The cache is keyed on inputs, not on nodes

The answer a node produced is stored against **what went into it** — the node's definition, its
literal values, the document tolerance, and the answers of the nodes feeding it. It is not
stored against the node's identity.

That has one surprising and useful consequence:

> **Undo recomputes nothing.** Step back over an edit and the graph is exactly the shape it was
> a moment ago, so every node asks the cache the same question it asked before and gets the same
> answer. The run after an undo evaluates zero nodes and serves every one from cache.

It has one more, which is the same fact seen from the other side: **two identical nodes compute
once.** Put down two `Circle.ByCentreRadius` nodes with the same centre and radius, and the
second is a cache hit. They are the same question.

```
Point.ByCoordinates ──┬──► Circle.ByCentreRadius (radius 5) ──► one computation
                      │
                      └──► Circle.ByCentreRadius (radius 5) ──► a cache hit
```

## Running a graph without the application

`spark run` evaluates a file the way the application does — the same node library, the same
reader, the same engine — and reports what it did:

```bash
spark run docs/examples/curves.spark
# 18 nodes, 18 evaluated, 0 from cache.

spark run docs/examples/curves.spark --export curves.obj
# 18 nodes, 18 evaluated, 0 from cache.
# 79 curves and points written to curves.obj.
```

It exits `0` when the graph evaluated cleanly, `1` when a node reported an error or the file
would not read, and `2` when the command line was wrong. If `spark run` and the application ever
disagree about a document, that is a defect worth reporting: they are the same code.

---

## The codes that bring you here

### `SPK1010` — these two ports cannot be connected

The type flowing out of one port is not something the other port can accept, and no conversion
in Spark's compatibility order applies.

**What to do.** Read the port labels — every port shows the type it wants when you hover it.
Usually the fix is a node between the two that produces the type the input asks for. A port
wanting a `Point3d` will not take a number, and Spark refuses at the wire rather than failing
later with a value in the wrong place.

### `SPK1011` — two types with the same name from different assemblies

Both ports name a type called, say, `Point3d`, and they are **not the same type** — they come
from different assemblies. This happens when two packages each carry their own copy of a
library.

**What to do.** This is a packaging problem rather than a graph problem. The two nodes cannot be
connected at all, and Spark refuses at design time deliberately: allowing it would produce a
run-time failure reading *cannot cast Point3d to Point3d*, which is among the least helpful
messages a program can emit.

### `SPK1012` — that wire would close a cycle

The connection you drew would let a node depend, however indirectly, on its own output.

**What to do.** Something in the chain has to break. A graph is evaluated in dependency order,
and a cycle has no such order — there is no node that could go first. Spark refuses the wire
rather than accepting it and failing at the next run, so the graph on screen is always one that
can evaluate.

### `SPK1013` — this connection loses information

The wire is accepted, and a conversion happens on the way through that cannot be undone. Feeding
a decimal number into a port that wants a whole one is the common case.

**What to do.** Often nothing — a warning is not an error, and the conversion may be exactly
what you meant. It is worth a look when the numbers downstream are not what you expected, since
this is where a `2.7` quietly became a `2`.

### `SPK1014` — this node is in a cycle

Reported when the graph is **evaluated** rather than when a wire is drawn, which in practice
means when a file carrying a cycle is opened: `SPK1012` stops you making one interactively, so
the only way a cycle reaches the engine is that it arrived already made.

**What to do.** The nodes named are the members of the cycle. Delete one of the wires between
them. **The rest of the graph still evaluates** — only the cycle and what depends on it are left
without an answer, and the file opens rather than being refused. A file can carry a cycle when
it was written by an older release, edited by hand, or assembled by something other than the
application, none of which the reader assumes was done in bad faith.

---

## What this page does not cover

- **How a list is spread across a node's inputs** — that is
  [lists, ranks and lacing](lacing.md), which owns the `SPK104x` codes.
- **Where a node's answer is shown** — that is
  [reading what a node produced](reading-results.md).
- **What is in a `.spark` file** — that is [saving and opening graphs](files.md).

## See also

- [Lists, ranks and lacing](lacing.md) — what happens when a node is given a list.
- [Reading what a node produced](reading-results.md) — the result strip and the tooltips.
- [Saving and opening graphs](files.md) — what is in a `.spark` file.
- [Undo and redo](undo.md) — what an undo restores, and what it does not.
