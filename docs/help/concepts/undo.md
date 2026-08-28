---
id: concepts.undo
title: Undo and redo
nodes: []
related: [concepts.files, concepts.lacing]
since: "0.1"
---

**Status:** Current. Describes the undo stack in the running application.
**Owner:** `shell`
**Last updated:** 2026-08-28

> **Scope.** Undo covers everything in the document: nodes added and deleted, wires drawn and
> removed, values typed into unwired ports, and where nodes sit on the canvas. It does **not**
> cover what you are looking at — pan, zoom, selection and pane sizes are not document changes —
> and it does not reach across opening a file.

---

## How it works

**Ctrl+Z** steps back. **Ctrl+Y** or **Ctrl+Shift+Z** steps forward again. Both are on the
toolbar as well, and each button's tooltip names the step it would take: *Undo Move node*,
*Redo Change radius*. Sixty-four steps are kept.

A step is one completed edit. Spark records it when the edit finishes — when you let go of a
node you were dragging, when you press Enter in a value box, when a wire lands on a port — not
while it is in progress, so dragging a node across the canvas is one step and not two hundred.

**An edit that changed nothing is not a step.** Press Enter twice in a value box and there is one
step, not two. Drag a node in a circle back to where it started and there is none at all, because
the document is exactly as it was. This matters more than it sounds: the alternative is a Ctrl+Z
that appears to do nothing, and a user who then presses it four more times and loses real work.

## A worked example

Open Spark. It starts on the demo graph: a `Number.Range` feeding a `Point.ByCoordinates`, and a
hundred points in the viewport.

1. **Change a value.** Select the `Number.Range` node. In the properties pane, set `end` to `2`
   and press Enter. The viewport drops to thirty points, and the Undo button's tooltip reads
   *Undo Change end*.

2. **Place a node.** In the library, find `Point.Origin` and press *Place node*. It appears on the
   canvas and a dot appears at the origin in the viewport. The tooltip now reads
   *Undo Add Point.Origin*.

3. **Move it.** Drag the new node a hundred units to the right. Nothing happens in the viewport —
   a position cannot change a value — but the tooltip reads *Undo Move node*.

4. **Step back three times.** `Ctrl+Z` puts the node back where it was. `Ctrl+Z` removes it, and
   its dot leaves the viewport. `Ctrl+Z` puts `end` back to `9`, and the hundred points return.

   Each of those is instant, including the last one, and that is not an accident of the graph
   being small — see below.

5. **Step forward.** `Ctrl+Y` three times reapplies all three edits in order. Now press `Ctrl+Z`
   once, and then place a different node: the redo branch is gone, because you have started a new
   one. That is how every editor behaves, and it is worth knowing rather than discovering.

## Why undo is instant

Spark caches results **by provenance**: a node's cached answer is filed under what produced it —
its definition, its inputs' keys, the document tolerance — and never under which document it was
in ([saving and opening graphs](files.md), and `Spark.Engine.CacheKey`).

Stepping back to a former state therefore asks for results that are still in the cache under
exactly the keys they had before. Nothing is recomputed. The same is true of drawing a wire,
removing it and drawing it again, or typing a value, changing it and typing the first one back.

The measured form of that claim: after an undo, the run Spark performs recomputes **zero** nodes
and serves every one of them from the cache. There is a test that asserts it.

## What undo does not do

- **It does not cross a document boundary.** Opening a file, or loading one of the demo graphs,
  starts a new history. Undo will not bring back the graph you just closed — save it instead.
- **It does not undo the view.** Panning, zooming, selecting and resizing panes are not document
  changes, and a Ctrl+Z that scrolled the canvas instead of reversing your last edit would be
  worse than one that did nothing.
- **It keeps sixty-four steps.** Beyond that, the oldest step falls off the end.
- **It does not survive closing the document.** History is not saved into the `.spark` file.

## What you will notice after an undo

Your selection is cleared, and nodes that overlap may be drawn in a different order. Undo
restores the document by reopening it, exactly as if it had been saved and opened again, and a
reopened document is laid out in the file's canonical order. Nothing moves and no value changes;
only which node is drawn on top of which.
