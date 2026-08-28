---
id: concepts.reading-results
title: Reading what a node produced
nodes: []
related: [concepts.lacing, concepts.finding-nodes, concepts.undo]
since: "0.1"
---

**Status:** Current. Describes the result strip and the hover tooltips, both of which exist.
**Owner:** `spark-ui`
**Last updated:** 2026-08-28

> **Scope.** The strip under a node showing what it produced, and the tooltips that say what a
> port wants. The **watch panel** — a docked pane showing one node's output in full — is a
> different thing and is not built yet.

---

## The strip under a node

Every node that produced something has a strip beneath it. Closed, it is one line:

```text
8 items · rank 1                                              ▸
```

Click it and it opens:

```text
8 items · rank 1                                              ▾
    Circle(centre (-7, 7, 0), radius 0.9)
    Circle(centre (-5, 7, 0), radius 0.9)
    Circle(centre (-3, 7, 0), radius 0.9)
    Circle(centre (-1, 7, 0), radius 0.9)
    Circle(centre (1, 7, 0), radius 0.9)
    Circle(centre (3, 7, 0), radius 0.9)
    and 2 more
```

A node that produced a single value is headlined by its type instead — `Circle`, `number`,
`Point3d` — and opens to show the value.

**Why the rank is on the line you cannot close.** `8 items · rank 1` is eight circles. `8 items ·
rank 2` is eight *lists* of circles. Both draw the same thing in the viewport and both behave
completely differently the moment you wire them into anything, because rank is what decides how a
node replicates ([lacing](lacing.md) §2.2). It is the single fact people most often have wrong
about their own graph, so it is never hidden behind the toggle.

Opening a strip is not an edit. It is not saved into the `.spark` file and `Ctrl+Z` will not
close it, for the same reason undo does not un-scroll the canvas: it is what you are looking at,
not what you have made.

## Hovering tells you what a port wants

Hold the pointer over a port and you get its name, its type and the description its author wrote:

```text
centre — Point3d

The centre.
```

The description is the node author's own XML comment, read straight out of the `.xml` file beside
the assembly — so **any library you load gets these for free**, with no extra work by whoever
wrote it.

Hovering the body of a node names the node and describes it, and that works **at every zoom
level, including zoomed so far out that nodes are plain coloured rectangles with no text on them
at all**. At that scale the tooltip is the only thing left that can tell you what you are looking
at, which is exactly when you need it.

## A worked example

Open Spark and press *Curves* on the toolbar.

1. **Find the `Circle.ByCentreRadius` node.** Under it is a strip reading `8 items · rank 1`.
   That is the answer to "did that make one circle or eight?" without opening anything.

2. **Click the strip.** It opens and lists six circles, then `and 2 more`. Now you know their
   centres march along y = 7, which the viewport shows you as a row of rings but does not put in
   numbers.

3. **Click it again** to close it, and press `Ctrl+Z`. Nothing happens to the strip — undo has
   the graph to look after, not your view of it.

4. **Hover the `centre` port.** `centre — Point3d`, and the author's description under it. Hover
   the node's header instead and you get the node's own description.

5. **Zoom out** with the wheel until the nodes become plain coloured rectangles. Hover one: it
   still names itself.

## What is not here yet

- **The watch panel.** A docked pane pinned to one node's output, so you can keep an eye on it
  while working elsewhere. The strip is per-node and lives on the canvas; the panel is a
  different tool for a different job, and is planned rather than built.
- **Previews of ports other than the first.** A node with two outputs shows a strip for the
  first one. Reading the second means wiring it into something, for now.
- **An open strip can be covered by a node below it.** Nodes are drawn over previews on purpose —
  the graph is the document and the strip is a readout of it — so move the node if the strip is
  in the way.
