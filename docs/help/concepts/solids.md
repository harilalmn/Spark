---
id: concepts.solids
title: Solids
nodes: []
related: [concepts.geometry-basics, concepts.curves, concepts.files]
since: "0.1"
---

**Status:** Current. Describes solids in the running application.
**Owner:** `geometry-kernel`
**Last updated:** 2026-08-31

> **Scope.** A solid in Spark is a **boundary representation** — exact surfaces, joined along exact
> edges, enclosing a volume. It is not a mesh. This topic covers what you can build, what you can
> do with it, and **why some operations may be greyed out**.

---

## A solid is not a mesh

A cylinder as a mesh is several hundred triangles and an approximation. A cylinder as a solid is
**three faces and two vertices**: an exact cylindrical surface for the wall, two exact planes for
the caps, and a single seam edge shared between the wall and itself.

That difference is what makes a solid worth having. It is exact at any zoom, it can be measured
without approximation, it can be exported to a manufacturing format, and it can be combined with
another solid *exactly* rather than approximately.

```
Solid.Box(plane, 2, 3, 4)     → a solid with 6 faces, 12 edges, 8 vertices
Solid.Cylinder(plane, 1, 5)   → a solid with 3 faces, 3 edges, 2 vertices
```

`Solid.FaceCount` reports the first number. `Solid.IsClosed` says whether the solid really encloses
a volume — every edge used exactly twice, once in each direction — which is what distinguishes a
closed solid from a sheet with a hole in it.

## Building and combining are two different things

**Building** a box or a cylinder is arithmetic: six planes and twelve edges written down. Spark
does that itself and it always works.

**Combining** two solids — union, difference, intersection — or modifying one — fillet, chamfer,
shell — is *exact solid modelling*, and it is a genuinely hard problem: intersecting two curved
surfaces exactly, deciding what is inside and what is outside, and rebuilding the topology around
the result. Spark does not implement that itself. It asks a **kernel provider**.

## When operations are greyed out

**A build with no kernel provider can still do most of what Spark is for**: points, curves,
surfaces, meshes, tessellation, the viewport, and every file format. What it cannot do is combine
solids.

When a provider is missing, those nodes are greyed out in the library rather than failing when you
press them, and a graph that reaches one anyway says so:

> No solid-modelling kernel is installed, so this build cannot union. Exact solid operations need a
> kernel provider. Spark's geometry, curves, surfaces, meshes and every file format work without
> one; booleans, fillets and the rest do not.

**A provider that is installed may still refuse an individual operation, and that is normal.** A
fillet whose radius does not fit in the corner, a difference between two solids that do not touch,
a loft between profiles that cannot be matched — these are the geometry declining, not a bug, and
the node reports what it was asked and why it could not be done.

## Turning a solid into a mesh

Every renderer, every 3D printer and most file formats want triangles, so a solid becomes a mesh on
the way out:

```
Solid.ToMesh(solid, 0.01)
```

The number is the **tolerance**: the greatest distance the mesh may stray from the true surface.
Smaller is closer and slower. The viewport does this for you at a tolerance derived from the
object's own size, so a solid looks smooth whether it is a millimetre or a kilometre across.

`Solid.Volume` measures the mesh rather than the surfaces, and says so: the number approaches the
true volume from below as the tolerance tightens.

## What is not built yet

- **Trimmed faces.** A face in this build is bounded by its surface's own edges. Cutting a hole in
  a face needs the parameter-space curves that a trim does not carry yet.
- **Mesh booleans** — combining two *meshes* rather than two solids. Deferred, and greyed out
  rather than missing.
