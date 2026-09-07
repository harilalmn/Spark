---
id: concepts.workspace
title: Panes and the workspace
nodes: []
related: [concepts.finding-nodes, concepts.evaluation, concepts.undo]
since: "0.2"
---

**Status:** Current. Describes the shell in the running application.
**Owner:** `shell`
**Last updated:** 2026-09-07

> **Scope.** The four panes — **Library**, **Canvas**, **Viewport** and **Properties** — how to
> resize, float and re-dock them, the named workspaces, and the one command that puts everything
> back. Pane sizes are not part of the document, so none of this is undoable
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
nothing else** — no buttons. Which panes you see is **View → Workspace**, and how big they are is
the bar between them.

## Floating a pane, and putting it back

**Drag a pane's title bar out of the main window**, or right-click it and choose **Float**. The
pane becomes an ordinary window of its own, which is what a second monitor is for: the viewport on
one screen and the graph on the other.

A floating pane is a normal window in every respect. It has the **minimise**, **maximise/restore**
and **close** buttons this operating system puts on every window, it appears in the taskbar, it
snaps to the screen edges, and double-clicking its title bar maximises it.

**Two title bars, two jobs.** The window's own title bar at the top moves the *window*. The pane's
title bar just below it moves the *pane* — drag it back over the main window and the shell shows
you where it will land. **View → Reset layout** puts everything back if you would rather not aim.

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
3. It is now a window. Maximise it on your second screen — the button is where it is on every
   other window — and orbit with the right mouse button, pan with the middle, zoom with the wheel.
4. Minimise it, and find it again in the taskbar.
5. Restore it, then drag **the pane's** title bar — the lower of the two — back over the bottom of
   the main window's middle column. The shell shows you where it will land.
6. If the aim goes wrong, **View → Reset layout** puts all four panes back where they started.
7. For a viewport-heavy shell without any of that, there is **View → Workspace → Modelling**.
