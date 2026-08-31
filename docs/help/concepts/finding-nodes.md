---
id: concepts.finding-nodes
title: Finding and placing nodes
nodes: []
related: [concepts.undo, concepts.files]
since: "0.1"
---

**Status:** Current. Describes the library panel and the canvas creation box, both of which exist.
**Owner:** `spark-ui`
**Last updated:** 2026-09-02

> **Scope.** How to get a node onto the canvas: the library panel, how it is grouped, how its search
> ranks, and the two canvas gestures — **right-click** for the search box, **double-click** for a
> code block.

---

## Three ways to place a node

1. **Right-click empty canvas.** A search box opens where you clicked. Type, press Enter, and the
   node lands at that point. This is the fast one.
2. **The library panel**, on the left. Select a node and press *Place node*, or double-click the
   entry. It lands near the middle of the view.
3. **Double-click empty canvas** for a [code block](code-blocks.md), which is the other fast one:
   a number, a formula or a list is quicker written than hunted for. This is Dynamo's gesture and
   it does the same thing here.

And **undo**, if you placed the wrong one. Every placement is one step ([undo](undo.md)).

## The library panel is grouped twice

The panel files every node under its **category** — `Curve`, `Solid`, `Point`, the same ten the
canvas colours node headers by — and then splits each category three ways:

| | | |
|---|---|---|
| **Create** | green `+` | Makes a new thing out of values that are not one. `Circle.ByCentreRadius`, `Vector.ZAxis`, `Number.Value`. |
| **Action** | amber bolt | Takes one of these and produces another. `Curve.Reverse`, `Solid.Union`, `Math.Divide`. |
| **Query** | blue `?` | Reports something about one without producing another. `Curve.Length`, `Solid.IsClosed`, `List.Count`. |

Each block has a coloured rail down its left edge, so which of the three you are reading is legible
without going back to the heading. The order is always Create, then Action, then Query — the order
a graph is built in, not alphabetical — and a block with nothing in it is not shown at all.

**Why it is worth the two levels.** `Solid` alone holds thirty-eight nodes. Split three ways, a
user who wants to *make* a solid reads sixteen names instead of thirty-eight, and never reads the
five that measure one.

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

1. **Right-click an empty part of the canvas**, somewhere below the existing nodes. A box appears
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

## Why the search box moved to right-click

It used to be on the double-click, and the double-click belongs to code blocks. **In Dynamo,
double-clicking blank canvas creates a code block** — double-click, then type — and a user arriving
with that habit got a search dialog instead. Now the double-click drops a code block at the point
you clicked, and the search box has right-click, which had no other job on the canvas.

Right-clicking a node, a port or a wire does nothing. That is a context menu, which is a feature
with a menu behind it rather than half of one taught now and untaught later.
