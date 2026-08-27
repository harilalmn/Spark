# ADR-0001 — Avalonia as the UI framework, not WPF

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

Spark needs a desktop shell: a node canvas, a docked layout, a code editor, a library
panel and a 3D viewport. Three prior projects in this codebase have already built most
of that shell — `RCS` against Revit, `CADScript` against AutoCAD, and `DoodleSharp` as a
standalone drawing application. All three are WPF. The obvious move is therefore to stay
on WPF and reuse them.

The survey does not support that move. The scripting layers of `RCS` and `CADScript` are
UI-agnostic and port near-1:1 regardless of the chosen framework, and `DoodleSharp`'s
`SceneIndex.cs` is pure managed data-structure code with no WPF in it. What is genuinely
WPF-shaped is `RCS`'s hand-rolled dock manager, the XAML windows, `DocGenerator.cs`'s
`FlowDocument` output, the `DrawingVisual` and Vortice/D3D11 render backends, and the
WPF-specific input handling inside the IntelliSense controller. Of those, the dock manager
is replaced by `Dock.Avalonia` rather than ported, the three-backend arbitration is
deliberately not carried forward (ADR-0013), and `DocGenerator` is deliberately abandoned
(ADR-0015). The reusable UI surplus from choosing WPF is close to zero.

D14 makes Windows the only *release* target for v1, so WPF would be sufficient on
release-target grounds alone. The counterweight is that a WPF choice is permanent: it
converts "Windows is what we ship" into "Windows is what we can ever ship".

## Decision

Spark's UI is built on Avalonia 11. `Spark.UI` and `Spark.Desktop` are the only projects
that reference it. No project anywhere in the solution uses a `-windows` TFM, and CI runs
an `ubuntu-latest` build-and-test job as a rot-guard so cross-platform viability cannot
decay silently into a wasted choice.

## Alternatives considered

### WPF

Its advantages are real and were the reason this decision needed making at all. It is the
framework all three prior projects use; `AvalonEdit`, `HelixToolkit` and mature commercial
docking libraries are available immediately; the tooling and the designer experience are
better; and `DocGenerator`'s 6,784 lines of `FlowDocument` emission would still have a
renderer to target. It lost because the reuse it promises is mostly illusory once the WPF
UI code is examined item by item, because it permanently forecloses the Linux and macOS
options that D1 wants kept open, and because it would require a `-windows` TFM, which is
exactly the thing the architecture tests forbid.

### WPF now, Avalonia later

A defensible sequencing argument: ship the Windows-only v1 on familiar ground, port when
cross-platform actually matters. It lost because it means writing the shell twice, and the
second write lands after third-party packages exist and the API surface has hardened under
ADR-0019. The canvas, viewport and editor integrations are the three highest-risk pieces of
UI work in the project; doing them twice is worse than doing them once on the less familiar
framework.

## Consequences

### Positive

Cross-platform stays technically viable at nearly no cost, and the Linux CI job proves it
continuously rather than by assertion. Skia is available directly, which is what makes the
immediate-mode canvas of ADR-0013 cheap. Compiled bindings are on by default, so binding
errors become compile errors.

### Negative

There is no `HelixToolkit` for Avalonia, so the entire 3D viewport is ours to write on top
of `OpenGlControlBase` (ADR-0014) — camera, picking, shading, buffers and all. `Dock.Avalonia`
is less mature than the commercial WPF docking libraries and we will live with its rough
edges. The `AvalonEdit`→`AvaloniaEdit` port is mechanical *except* completion-popup placement
and focus, where the two APIs diverge most; that rework is budgeted explicitly. And Avalonia
is a smaller ecosystem, so fewer answers exist when something behaves oddly.

### Neutral

MVVM is done with `CommunityToolkit.Mvvm` source generators rather than ReactiveUI, which
Avalonia 11 no longer presumes.

## Notes

The M1.5 de-risk spike gates this in part: criterion (a) requires `OpenGlControlBase` to
render a shaded lit triangle on Windows *and* Linux, and criterion (c) requires the
AvaloniaEdit plus Roslyn completion popup to be acceptable. A failure there would revisit
ADR-0014 or the editor plan, not necessarily this decision — but two failures out of three
would be grounds to reopen it. Nothing else should.
