---
id: concepts.finding-nodes
title: Finding and placing nodes
nodes: []
related: [concepts.undo, concepts.files]
since: "0.1"
---

**Status:** Current. Describes the library panel and the canvas creation box, both of which exist.
**Owner:** `spark-ui`
**Last updated:** 2026-08-28

> **Scope.** How to get a node onto the canvas: the library panel, how its search ranks, and the
> box that opens when you double-click empty canvas. **Double-clicking does not create a code
> block** — Spark does not have one yet, and the last section says why and what it will change.

---

## Three ways to place a node

1. **Double-click empty canvas.** A search box opens where you clicked. Type, press Enter, and the
   node lands at that point. This is the fast one.
2. **The library panel**, on the left. Select a node and press *Place node*, or double-click the
   entry. It lands near the middle of the view.
3. **Undo**, if you placed the wrong one. Every placement is one step ([undo](undo.md)).

## The search ranks; it does not filter

Typing in either box ranks the whole library rather than narrowing it, in this order:

| Rank | Matches when | `circle` finds |
|---|---|---|
| Exact | The name, or the part after the dot, *is* what you typed | `Math.Sin` for `sin` |
| Prefix | The name starts with it — or the part after the dot does, one step behind | `Circle.ByCentreRadius` |
| Camel-hump | You typed the **capitals** | `cbcr` → `Circle.ByCentreRadius` |
| Substring | It appears anywhere in the name | `Arc.ByCircleAndPoint` |
| Category | It appears in the node's category | every geometry node, for `geometry` |
| Description | It appears in the node's description | nodes that merely mention a circle |

Ties are broken by how close the match was, then by the shorter name, then alphabetically. That
last part is not fussiness: it makes the order **total**, so the list does not reshuffle under your
cursor between one keystroke and the next.

**Camel-hump is the one worth learning.** With fifty-seven nodes you can skim; with a few thousand,
which is what installing packages does, you cannot. `pbc` gets you `Point.ByCoordinates`, `cbcr`
gets you `Circle.ByCentreRadius`, `bbc` gets you `BoundingBox.ByCorners`.

## A worked example

Open Spark on the demo graph.

1. **Double-click an empty part of the canvas**, somewhere below the existing nodes. A box appears
   under the pointer with a text field and the hint *Enter places it here · Esc cancels*.

2. **Type `cbcr`.** One result: `Circle.ByCentreRadius`, showing its signature
   `(centre, radius) → circle`. It is already highlighted, so there is nothing to click.

3. **Press Enter.** The node lands exactly where you double-clicked — not in the middle of the
   view, not offset from the pointer — and is selected, with the keyboard back on the canvas ready
   for the next gesture.

4. **Look at its ports.** `centre  Point3d` and `radius  number`: the node tells you what to plug
   in, which is the other half of not having to search for anything.

5. **Press `Ctrl+Z`.** The node goes away, and the Undo tooltip had read *Undo Add
   Circle.ByCentreRadius* before you pressed it.

Try `circle` instead of `cbcr` and the list is longer, with `Circle.ByPlaneRadius` first because it
is the shortest of the equally good matches. Use the arrow keys to move the highlight without
leaving the text field.

## What double-click does *not* do yet

**In Dynamo, double-clicking blank canvas creates a Code Block** — a node you type an expression
into. Spark's code block is a real plan and is not built: it needs a Roslyn compilation pipeline,
nodes whose ports are inferred per instance from what you typed, and somewhere in the `.spark` file
to keep the script. That is a milestone of its own.

So the gesture is here and its result is different: **you name a node instead of writing an
expression.** When the code block arrives, this same box gains the other half — if what you typed
looks like an expression rather than a node name, you get a code block containing it — and the
gesture will not have to be relearned.

Nothing here fakes it in the meantime. A node that looked like a code block and did not compile
anything would be worse than the wait.
