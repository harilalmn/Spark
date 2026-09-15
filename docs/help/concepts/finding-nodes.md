---
id: concepts.finding-nodes
title: Finding and placing nodes
nodes: []
related: [concepts.undo, concepts.files]
since: "0.1"
---

**Status:** Current. Describes the library panel and the canvas creation box, both of which exist.
**Owner:** `spark-ui`
**Last updated:** 2026-09-15 (`E5-T4`: overloads under one row)

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
| **Create** | green `+` | Makes a new thing out of values that are not one. `Circle.FromCenterRadius`, `Vector.ZAxis`, `Number.Value`. |
| **Action** | amber bolt | Takes one of these and produces another. `Curve.Reverse`, `Solid.Union`, `Math.Divide`. |
| **Query** | blue `?` | Reports something about one without producing another. `Curve.Length`, `Solid.IsClosed`, `List.Count`. |

Each block has a coloured rail down its left edge, so which of the three you are reading is legible
without going back to the heading. The order is always Create, then Action, then Query — the order
a graph is built in, not alphabetical — and a block with nothing in it is not shown at all.

**Why it is worth the two levels.** `Solid` alone holds thirty-eight nodes. Split three ways, a
user who wants to *make* a solid reads sixteen names instead of thirty-eight, and never reads the
five that measure one.

## Overloads share one row

Some members can be called in more than one way. A library that imports a `Combine` taking two
numbers *and* a `Combine` taking three gets **a node for each** — the shapes are different, the
ports are different, and a node whose ports changed under you would break the wires already
attached to it.

They are not listed twice. The panel shows **one row for the member**, with a chevron, and clicking
it opens a small panel beside it listing the ways to call it:

```
  Combine                          ▸
  2 overloads
      ┌──────────────────────────────────┐
      │ Combine(a, b)                    │
      │ (a, b) → result                  │
      │ Combine(a, b, c)                 │
      │ (a, b, c) → result               │
      └──────────────────────────────────┘
```

Click one and it is placed, exactly as clicking any other row places a node. The row itself places
nothing — there is no *the* `Combine` to place, which is the whole reason the list exists.

**How the names are told apart.** By their differing parameters, never by a number. A constructor is
named for what it takes — `Circle.ByCenterRadius` beside `Circle.ByCenterRadiusNormal` — and a
method keeps its name and gains its argument list: `Combine(a, b)` beside `Combine(a, b, c)`. There
is no `Combine_2`, because which overload got the `_2` would depend on the order the types happened
to be read in, and that order is not promised by anything.

**You will not see this in Spark's own library.** Nothing in it is overloaded: every constructor is
named for its parameters, so none of them collide. It is here for node packages you import, which
are written by people who did not have to name anything with Spark in mind.

## The search ranks; it does not filter

Typing in either box ranks the whole library rather than narrowing it, in this order:

| Rank | Matches when | `circle` finds |
|---|---|---|
| Exact | The name, or the part after the dot, *is* what you typed | `Math.Sin` for `sin` |
| Prefix | The name starts with it — or the part after the dot does, one step behind | `Circle.FromCenterRadius` |
| Camel-hump | You typed the **capitals** | `cfcr` → `Circle.FromCenterRadius` |
| Substring | It appears anywhere in the name | `Arc.ByCircleAndPoint` |
| Category | It appears in the node's category | every geometry node, for `geometry` |
| Description | It appears in the node's description | nodes that merely mention a circle |

Ties are broken by how close the match was, then by **Create, then Action, then Query** — the same
order the panel files them in — and then alphabetically. That last part is not fussiness: it makes
the order **total**, so the list does not reshuffle under your cursor between one keystroke and the
next.

**Kind breaks a tie; it does not win one.** Two nodes that answer your query equally well are sorted
Create before Action before Query, so if you typed three letters in order to *make* something, the
nodes that make it are the ones you read first. But a node whose name *is* what you typed always
beats a node that merely mentions it in its description, whatever either of them does — otherwise
searching would bury the node you named.

**Camel-hump is the one worth learning.** With fifty-seven nodes you can skim; with a few thousand,
which is what installing packages does, you cannot. `pfc` gets you `Point.FromCoordinates`, `cfcr`
gets you `Circle.FromCenterRadius`, `bbc` gets you `BoundingBox.FromCorners`.

## A worked example

Open Spark on the demo graph.

1. **Right-click an empty part of the canvas**, somewhere below the existing nodes. A box appears
   under the pointer with a text field and the hint *Enter places it here · Esc cancels*.

2. **Type `cfcr`.** One result: `Circle.FromCenterRadius`, showing its signature
   `(center, radius) → circle`. It is already highlighted, so there is nothing to click.

3. **Press Enter.** The node lands exactly where you double-clicked — not in the middle of the
   view, not offset from the pointer — and is selected, with the keyboard back on the canvas ready
   for the next gesture.

4. **Look at its ports.** `center  Point3d` and `radius  number`: the node tells you what to plug
   in, which is the other half of not having to search for anything.

5. **Press `Ctrl+Z`.** The node goes away, and the Undo tooltip had read *Undo Add
   Circle.FromCenterRadius* before you pressed it.

Try `circle` instead of `cfcr` and the list is longer: `Circle.FromCenterNormalRadius`,
`Circle.FromCenterRadius`, `Circle.FromPlaneRadius`, `Circle.FromThreePoints`, and then
`PolyLine.FromRegularPolygon`. The four `Circle` nodes match equally well and are all **Create**, so
they come out alphabetically; `PolyLine.FromRegularPolygon` is a Create too and still sorts last,
because it matches the word less well. Use the arrow keys to move the highlight without leaving the
text field.

## Why the search box moved to right-click

It used to be on the double-click, and the double-click belongs to code blocks. **In Dynamo,
double-clicking blank canvas creates a code block** — double-click, then type — and a user arriving
with that habit got a search dialog instead. Now the double-click drops a code block at the point
you clicked, and the search box has right-click, which had no other job on the canvas.

Right-clicking a node, a port or a wire does nothing. That is a context menu, which is a feature
with a menu behind it rather than half of one taught now and untaught later.
