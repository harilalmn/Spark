# ADR-0014 — `OpenGlControlBase` viewport behind `IViewportRenderer`, with a software fallback

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

Spark needs a 3D viewport, and ADR-0001 removes the easy answer: there is no HelixToolkit for
Avalonia, so the viewport is ours to build. Across all six surveyed projects there is no 3D
viewport of any kind, so there is nothing to port — this and the kernel are the two places
carrying nearly all of the project's risk.

The problem has two halves that are easy to conflate. One is the graphics API — what draws the
triangles. The other is *surface interop* — how a GPU surface gets composited into an Avalonia
control, correctly, on each platform, with the right lifetime and resize behaviour. The second
half is the one that actually costs time, and it is the one Avalonia's built-in
`OpenGlControlBase` already solves.

There is also a testing constraint that GPU rendering cannot satisfy. `spark render` is meant to
provide CI visual regression, and GPU output varies by driver and vendor, so it is not comparable
between runs.

## Decision

The viewport renders through Avalonia's built-in `OpenGlControlBase` against GL 3.3 core, behind
an **`IViewportRenderer`** interface, with a **software rasteriser** as a second implementation of
the same interface. `Spark.Viewport` is Avalonia-free by architecture-test rule 3; only `Spark.UI`
adapts it.

Geometry reaches the viewport as immutable `RenderPackage { NodeId, PortIndex, ElementPath,
Positions, Normals, Indices, EdgeIndices, Appearance }`, with one GPU buffer set per
`(NodeId, PortIndex)`, so re-evaluating one node re-uploads one buffer. Tessellation is parallel
and streams during a run.

## Alternatives considered

### Silk.NET

Complete, current, actively maintained bindings for GL, Vulkan and more — a better API surface
than hand-declared GL entry points, and the obvious choice if the problem were graphics bindings.
It lost because it adds a dependency without solving surface interop: we would still need an
Avalonia control to present into, which is exactly what `OpenGlControlBase` provides, so the
dependency buys convenience on the half of the problem that is not hard.

### Veldrid

The closest fit on paper: a genuine cross-backend abstraction over Direct3D 11, Vulkan, Metal and
OpenGL, which is precisely the shape `IViewportRenderer` describes. On a one-year horizon it would
be a reasonable bet. It lost because it has been effectively unmaintained since around 2023, and
Spark is a multi-year project whose viewport is one of its highest-risk components. Depending on
an unmaintained abstraction for the risky part is the wrong direction of bet.

### GPU only, with no software path

Less code, one renderer to maintain, and the abstraction could be added if it were ever needed. It
lost three times over. GL initialisation fails outright on some virtual machines and over RDP,
which is a support burden with no answer. Headless thumbnail generation has no GPU to use. And CI
visual regression needs deterministic output, which only a software rasteriser gives — the
software renderer is not a consolation prize, it earns its place independently.

### WPF with HelixToolkit

The whole problem solved by an existing library. Foreclosed by ADR-0001, and noted here because it
is the honest reason this ADR is as expensive as it is.

## Consequences

### Positive

Surface interop is Avalonia's problem, not ours. `IViewportRenderer` gives a real substitution
point if the GL path disappoints, and the software path makes `spark render` deterministic, which
turns geometry rendering into something CI can assert on. Node-keyed buffers mean re-evaluating one
node re-uploads one buffer, and selection synchronisation falls out of the same identity tuple with
no extra bookkeeping.

### Negative

**GL 3.3 core is an old target, and Apple deprecated OpenGL** — so the macOS path is built on a
deprecated API that may stop working. The only real mitigations are that macOS is not a release
target under D14 and that `IViewportRenderer` is where a Metal or Vulkan backend would go if it
ever mattered; neither is a plan, and this should be recorded as an accepted exposure rather than a
solved problem. Two renderers must be kept in visual agreement, and they will drift. The software
renderer will be far slower than the GPU path and is not something a user would enjoy working in for
real models. Driver variance remains a support burden that no amount of abstraction removes.

### Neutral

`Spark.Viewport` being Avalonia-free means the viewport is usable from the CLI and from tests
without a window, which is what makes the headless assertions possible.

## Notes

M1.5 spike (a) gates this: `OpenGlControlBase` must render a shaded lit triangle on Windows **and**
Linux, with the criterion written down before the spike starts. A failure there is what
`IViewportRenderer` exists for, and the response would be to reconsider the GPU backend — not the
seam or the software renderer, both of which stand on their own.
