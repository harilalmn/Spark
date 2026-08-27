# ADR-0013 — Immediate-mode node canvas over a retained `SceneIndex`

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

The node canvas is the surface a user spends the entire session looking at, and it has to stay
responsive at the sizes real graphs reach. AEC graphs routinely run to many hundreds of nodes,
and the ones users complain about are larger still.

The obvious implementation in any XAML framework is one control per node inside an
`ItemsControl` over a `Canvas`, bound with MVVM. That approach collapses somewhere between 500
and 2,000 Avalonia controls, because layout and hit-test costs are per-visual and the framework
pays them whether or not the node is visible or interactive. Real graphs exceed that threshold,
so the collapse is not a corner case — it is the expected steady state for a serious user.

The comparison that settles it: drawing a few thousand rounded rectangles and Bézier curves
through Skia is trivial. The expensive thing is not the pixels, it is the framework machinery
per node.

The relevant prior art is `DoodleSharp`'s `SceneIndex.cs`, which is pure managed data-structure
code with no WPF in it — a retained spatial index giving culling and hit-testing — and which
therefore ports to Avalonia unchanged.

## Decision

The canvas is **one Avalonia control for the whole surface**, rendering in immediate mode over
a retained `SceneIndex` that owns the spatial index, culling and hit-testing. Nodes, ports,
wires, groups and notes are drawn, not instantiated.

Input fidelity is preserved by a **hybrid overlay**: only the node currently being interacted
with gets a real Avalonia control, positioned over the drawing — typically one at a time. Below
40% zoom the canvas drops to a level-of-detail representation. There is one Skia backend, and
`SceneIndex` is the seam if profiling ever demands another.

## Alternatives considered

### One Avalonia control per node

Everything comes free: focus, keyboard navigation, tooltips, styling in XAML, text editing,
accessibility, and the MVVM data binding that the rest of the shell already uses. This is the
alternative a contributor will propose, and on a small graph it is strictly better. It lost on
the measured ceiling — between 500 and 2,000 controls before the canvas becomes unusable — which
sits below the size of the graphs the product exists to handle.

### Virtualise the control-per-node approach

Recycle controls for visible nodes only, which is the standard answer to exactly this problem and
keeps most of the fidelity. It lost because it raises the ceiling rather than removing it, and
because the wire layer defeats it: wires connect nodes that are off-screen, so the connection
graph has to be drawn in immediate mode regardless. Having built that renderer, drawing the nodes
into it too is a small addition; maintaining both a virtualising control host and an immediate-mode
wire layer is not.

### Port `DoodleSharp`'s three-backend arbitration with hysteresis

Proven in production, adaptive to hardware, and it would let the canvas pick the fastest path
available. It lost because that complexity was earned by a drawing application pushing tens of
thousands of primitives, and here it would be pure maintenance cost for a workload one backend
handles comfortably. What is taken from `DoodleSharp` instead is the retained index, the culling
discipline and the habit of measuring before optimising.

## Consequences

### Positive

Canvas cost scales with what is visible rather than with graph size, and the 2,000-node target is
a benchmark rather than an aspiration — validated by M1.5 spike (b), which requires panning and
zooming 2,000 synthetic nodes at 60 fps, and benchmarked nightly from M2. Culling and hit-testing
arrive as ported, already-working code. The rendering path has no framework dependency in it, so
it is testable without a UI.

### Negative

We re-implement, by hand, what the framework would otherwise provide: hit-testing, selection
visuals, focus handling, keyboard navigation and accessibility for everything on the canvas. The
accessibility pass is at M8, and until then the canvas is the weakest part of the product for
assistive technology. Theming a node is code rather than XAML, so a contributor cannot restyle the
canvas without touching the renderer. In-place text editing depends entirely on the overlay
mechanism being correct, and overlay positioning and focus transfer are the fiddly part of this
design — the same class of problem that makes the AvaloniaEdit completion popup the budgeted
rework in ADR-0001.

### Neutral

The rest of the shell — library, editor host, docking, help — uses ordinary Avalonia controls with
compiled bindings and `CommunityToolkit.Mvvm`. This decision is scoped to the canvas alone.

## Notes

Revisit only with a measurement. If `SceneIndex` ever needs a second rendering backend, it is
already the seam for one; that would be a smaller change than this ADR describes and would not
reopen the immediate-mode choice itself.
