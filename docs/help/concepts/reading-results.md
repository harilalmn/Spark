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

> **Scope.** The strip under a node showing what it produced, the tooltips that say what a port
> wants, and the **watch panel** — the pinned pane that shows one node's output in full.

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

## The watch panel

The strip under a node is for glancing. The **watch panel**, at the bottom of the right-hand
pane, is for reading — and the difference between them is not size, it is **what they follow**.

- The strip follows the **node**. Every node has one, and it shows what that node produced.
- The panel follows **nothing**. You pin it to one node, and it stays there while you select,
  edit and move anything else.

That is the whole reason there are two. *What did this node just produce* is a question about
whatever is under your pointer; *what is this one node producing while I change something over
here* is a question about one node and the rest of the graph, and no strip can answer it.

**To pin one:** select a node, then press **Pin**. **Clear** unpins it.

Once pinned, the panel shows things the strip deliberately does not:

| | Strip under a node | Watch panel |
|---|---|---|
| Output ports | The first one | Every one, named |
| Elements of a list | Six, then a count of the rest | All of them |
| A long value | Clipped with an ellipsis | In full |
| Nested lists | The count and rank of the inner list | Expanded, indented, with the rank at every depth |
| Follows | The node it belongs to | The node you pinned |

```
centre — 8 items · rank 1
  [0] (6, 0, 0)
  [1] (4.24, 4.24, 0)
  [2] (0, 6, 0)
  …
radius — 0.9
```

Two things to expect.

**A pinned node that you delete unpins itself.** It does not go on showing what the node used to
produce, because a readout of something that is not there any more is worse than an empty pane.
An undo that removes the node has the same effect.

**A very large list stops.** The panel writes at most two thousand lines and then says how many
it did not write. A list of a million expanded in full is not a readout, it is a frozen window,
and the count is there so that a truncated list never reads as a short one.

---

## What is not here yet

- **Previews of ports other than the first, *on the canvas*.** A node with two outputs shows a
  strip for the first one. The watch panel shows every port, so pin the node when you need the
  second.
- **An open strip can be covered by a node below it.** Nodes are drawn over previews on purpose —
  the graph is the document and the strip is a readout of it — so move the node if the strip is
  in the way.
- **A very long value is cut off with an ellipsis, in the strip.** An open strip widens to fit
  what is in it, but only so far: one enormous string is not allowed to lay a strip across the
  whole graph. The watch panel shows it in full.
