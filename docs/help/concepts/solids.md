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
the result. Spark does not implement that itself. It asks a **kernel provider**, and the one that
ships with Spark is **OpenCascade**.

## What the operations do

| Node | What it does |
|---|---|
| `Solid.Union` | Everything in either solid |
| `Solid.Difference` | The first solid with the second taken out of it |
| `Solid.Intersection` | Only what is in both |
| `Solid.Split` | Cuts a solid into pieces and keeps **all** of them |
| `Solid.Trim` | Cuts a solid and keeps only the piece a point is in |
| `Solid.Extrude` | Sweeps a closed profile along a direction |
| `Solid.FilletAll` | Rounds every edge, to a radius |
| `Solid.Hollow` | Turns a solid into a shell of a given wall thickness |
| `Solid.Offset` | Moves every face outwards or inwards |
| `Solid.Thicken` | Gives an open sheet a thickness, making a solid of it |

**`Split` and `Difference` are not the same operation with different names.** A block cut by a
plate:

```
Solid.Difference(block, plate)   → one solid, and the plate's slice is gone
Solid.Split(block, plate)        → three solids, whose volumes add back up to the block's
```

Use `Difference` when you want a hole. Use `Split` when both halves matter, and `Trim` when only
one does and you can point at it — which is how you say *this* side without knowing what order the
pieces come back in.

**Rounding an edge is the one that shows what exactness buys.** A fillet has to build a new surface
tangent to the two faces that meet at the edge, and then rebuild the corners where three of those
meet. On a triangle soup there is nothing to be tangent to. This is why Spark takes a solid kernel
rather than a mesh one.

## What a solid keeps

An exact solid stays exact through an operation:

```
Solid.Cylinder(plane, 2, 6)     → the wall is a cylindrical surface
Solid.Difference(that, a box)   → the wall is STILL a cylindrical surface
```

The cut end becomes a new planar face; the part of the wall that survives is the same exact
cylinder it was, at the same radius. Nothing is refitted, and nothing drifts. That property is
what makes a chain of ten operations mean what it says.

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
the node reports what it was asked and why it could not be done:

> The kernel could not fillet. A fillet of radius 0.250000 does not fit these edges.

**Two refusals, two codes.** `SPK1080` means *nothing here can do this*; `SPK1081` means *the
geometry does not permit this*. They call for completely different responses, which is why they are
not the same code.

## Getting a solid out of Spark, exactly

**A mesh format throws the exactness away, and STEP does not.** That is the whole difference:

```
spark export --open model.spark --out part.step
spark export --open model.spark --out part.stl
```

The first writes the exact surfaces — a cylinder stays a cylindrical surface, and the CAD system
on the other end can measure it, offset it and machine from it. The second writes triangles.

Spark reads and writes **STEP** (`.step`, `.stp`) and **IGES** (`.iges`, `.igs`), through the same
kernel provider as the booleans. STEP goes out as **AP214**, which is what most CAD systems read
most reliably. An extension the build does not know is refused by name rather than guessed at.

**Only solids go into a STEP file.** A graph of curves and no solids gets a message saying so and
pointing at `.obj`, rather than an empty file with a confident name.

## Turning a solid into a mesh

Every renderer, every 3D printer and most file formats want triangles, so a solid becomes a mesh on
the way out:

```
Solid.ToMesh(solid, 0.01)
```

The number is the **tolerance**: the greatest distance the mesh may stray from the true surface.
Smaller is closer and slower. The viewport does this for you at a tolerance derived from the
object's own size, so a solid looks smooth whether it is a millimetre or a kilometre across.

**A tolerance is a request for work, and a curved solid will honour it without limit.** Asking for
a hundredth of a millimetre on a two-metre sphere is a legal request whose answer is hundreds of
millions of triangles. Spark clamps a tessellation to a hundred-thousandth of the object's own
size — finer than any screen, printer or file format needs, and finite. If you want more detail
than that, you want a smaller object or a different question.

`Solid.Volume` measures the mesh rather than the surfaces, and says so: the number approaches the
true volume from below as the tolerance tightens.

## Where the provider comes from, and what to do when it is missing

The provider is a native component. A normal install has it. If you are running Spark from a
source clone, build it once:

```
pwsh scripts/build-native.ps1
```

That needs `vcpkg install opencascade:x64-windows` to have been done first, which takes a while —
the script tells you so rather than starting it behind your back. The result lands in
`artifacts/native/win-x64/` and Spark looks there automatically. Setting `SPARK_OCCT_PATH` to a
directory overrides where it looks.

## When an operation is wrong rather than refused

**A refusal is legible; a wrong answer is not.** If a boolean gives you a shape that is subtly
wrong — a face missing, a volume that cannot be right — the useful thing is to capture what went
in, in the format the kernel's own developers can load.

```
set SPARK_OCCT_DUMP=C:\temp\spark-dumps
```

With that set, any operation that **fails** writes its inputs as `.brep` files and names them in
the diagnostic. `.brep` is OpenCascade's own format and its Draw test harness reads it, so a bug
report can carry the exact shapes rather than a description of them.

It is off by default and that is deliberate: an exact kernel refuses constantly and correctly, and
a build that wrote a file on every refusal would fill a disk with evidence of things working as
designed.

## What is not built yet

- **Trimmed faces in Spark's own model.** A face Spark *builds* is bounded by its surface's own
  edges. Faces that come *back* from the provider are fully trimmed, so the result of a boolean is
  as trimmed as it needs to be; what is missing is the ability to author a trimmed face directly.
- **Draft angles** — greyed out rather than missing.
- **AP242.** STEP goes out as AP214. AP242 carries assemblies, names and colours, and Spark has
  none of those to put in a file yet.
- **Mesh booleans** — combining two *meshes* rather than two solids. Deferred.
