---
id: concepts.workspace
title: Panes and the workspace
nodes: []
related: [concepts.finding-nodes, concepts.evaluation, concepts.undo]
since: "0.2"
---

**Status:** Current. Describes the shell in the running application.
**Owner:** `shell`
**Last updated:** 2026-09-07 (E8-T48: the Library and Properties toggles)

> **Scope.** The four panes — **Library**, **Canvas**, **Viewport** and **Properties** — how to
> show, hide, resize, float and re-dock them, the named workspaces, and the one command that puts
> everything back. Pane sizes are not part of the document, so none of this is undoable
> ([Undo and redo](concepts.undo)) and none of it is saved into a graph.

---

## The four panes

| Pane | What it is for |
|---|---|
| **Library** | Every node, by category, with a search box. Double-click an entry to place it |
| **Canvas** | The graph. Nodes, wires, notes and groups |
| **Viewport** | What the graph produced, in 3D |
| **Properties** | The selected node's values, the diagnostics from the last run, and the watch panel |

Drag the bar between two panes to resize them. **A docked pane's title bar carries its title and
nothing else** — no buttons; showing and hiding is done from the **View** menu, and sizing with the
bar between panes.

## Showing and hiding one pane

**View → Library** and **View → Properties** each show and hide that pane on its own. A tick beside
the entry means the pane is showing.

| | |
|---|---|
| **View → Library** | The node library, on the left |
| **View → Properties** | The properties pane, on the right — **`F4`** |

**`F4` is the quick one**, because the properties pane is the one you hide and show most: it earns
its width while you are setting a node up and wastes it while you are arranging the graph.

These change **one** pane and leave the other three exactly as they are, sizes included — which is
what the named workspaces below cannot do, since each of them sets all four at once. The two work
together: apply **Modelling**, then press `F4` when you do want the properties back, and only that
pane moves.

## Floating a pane, and putting it back

**Drag a pane's title bar out of the main window**, or right-click it and choose **Float**. The
pane becomes an ordinary window of its own, which is what a second monitor is for: the viewport on
one screen and the graph on the other.

Its title bar carries three buttons: **minimise**, **maximise/restore**, and one that **docks it
back** into the main window.

**Dragging that title bar is how you put it back by hand.** Drag it over the main window and Spark
shows you where the pane will land — an edge of the window, or into another pane. **View → Reset
layout** puts everything back if you would rather not aim.

> **The last button docks the pane; it does not close it.** These four panes *are* the shell, so
> there is nothing sensible for closing one to mean — it would leave you with a window missing a
> third of itself and no obvious way back. To stop showing a pane, use **View → Workspace**.
> Closing a floating pane any other way — the keyboard, the taskbar — docks it back too, and
> closing the main window closes it along with everything else.

A returning pane arrives beside its neighbour in the column it belongs to: the viewport comes back
next to the canvas. That is not always the exact slot it left from, and **View → Reset layout** is
the way to the original arrangement.

## Named workspaces

**View → Workspace** has four arrangements:

| Workspace | What it shows |
|---|---|
| **Default** | All four panes, canvas over viewport |
| **Modelling** | The viewport takes most of the height; the properties pane is hidden |
| **Authoring** | The graph takes most of the height; all four panes |
| **Presenting** | Canvas and viewport only |

## Reset layout

**View → Reset layout** rebuilds the shell from nothing and returns to **Default**. It is the way
back from any arrangement at all — panes dragged into each other, floated out, or both. Nothing in
the document changes: the same graph, the same selection, the same undo history.

---

## Worked example: put the viewport on the other screen

1. Open a graph with geometry in it — **File → Open surfaces example** will do.
2. The viewport is the lower half of the middle column. **Drag its title bar** out of the main
   window and let go.
3. It is now a window. Maximise it on your second screen and orbit with the right mouse button, pan
   with the middle, zoom with the wheel.
4. Minimise it, and find it again in the taskbar.
5. Restore it, then drag its title bar back over the bottom of the main window's middle column.
   Spark shows you where it will land.
6. Or skip the aiming: press the **✕** on its title bar and it docks itself back beside the canvas.
7. **View → Reset layout** puts all four panes back exactly where they started, and
   **View → Workspace → Modelling** is the viewport-heavy shell without any of this.
