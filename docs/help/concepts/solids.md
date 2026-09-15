---
id: concepts.solids
title: Solids
nodes: [Surface.PrincipalCurvatures, Surface.PrincipalDirections, Surface.ToNurbs, Surface.Offset]
related: [concepts.geometry-basics, concepts.curves, concepts.files]
since: "0.1"
---

**Status:** Current. Describes solids in the running application.
**Owner:** `geometry-kernel`
**Last updated:** 2026-09-16 (`E2-T28`: the greying this page already described now exists)

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
| `Solid.Sweep` | Sweeps a profile along a curved path |
| `Solid.Patch` | Fills a closed boundary with a surface |
| `Solid.FilletAll` | Rounds every edge, to a radius |
| `Solid.Hollow` | Turns a solid into a shell of a given wall thickness |
| `Solid.Offset` | Moves every face outwards or inwards |
| `Solid.Thicken` | Gives an open sheet a thickness, making a solid of it |
| `Solid.Translate` | Moves a solid along a direction |
| `Solid.Rotate` | Turns a solid about an axis through a point |
| `Solid.Mirror` | Reflects a solid in a plane |

## Moving a solid

**`Translate`, `Rotate` and `Mirror` are not kernel operations and need no provider.** They move
the geometry a solid is made of and leave its topology exactly as it was, because moving a shape
does not change what is joined to what. They work in a build with no OpenCascade at all — which is
worth knowing, because everything else in the table above does not.

```
box    = Solid.Box(Plane.WorldXY, 2, 3, 4)
moved  = Solid.Translate(box, Vector.ByCoordinates(10, 0, 0), 1)
turned = Solid.Rotate(moved, Point.Origin(), Vector.ZAxis(), 45)
pair   = Solid.Union(box, Solid.Mirror(box, Plane.WorldYZ))
```

**`Mirror` flips the solid the right way out.** Reflecting a shape reverses its handedness, so
every face would end up pointing inwards if nothing corrected it — a box that renders black and
reports a negative volume. Spark flips each face as it mirrors, so the result is a solid you can
union with the original, which is the usual reason to mirror one at all.

**A non-uniform scale is refused when the solid has round geometry.** Scaling a cylinder twice as
wide in x as in y would make an *elliptic* cylinder, and Spark has no type for that — so it
declines and says so, rather than handing back a shape that is not the one you asked for. A uniform
scale is fine.

**Moving a solid reads it back out of the provider.** After a union or a fillet the shape lives
inside OpenCascade; moving it brings it into Spark's own arrays first, which costs one conversion.
It is worth knowing when a graph moves a solid in a loop, and it is not worth avoiding: do the
kernel work first and the moving afterwards.

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

When a provider is missing, those nodes are **greyed out in the library** rather than failing when
you press them. The row stays in the list — what you need to know is that Spark has a `Solid.Union`
and that this build cannot run it — and instead of its ports it says why:

```
  Solid.Union
  This build has no solid-modelling kernel, so this node cannot run.
```

Hovering says the same thing in full, and the node cannot be placed. A graph that already uses one
still opens, still shows every node, and still saves unchanged; the node reports the refusal when it
runs:

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

## How a surface bends, and which way

Every point on a surface has two special directions in its tangent plane, at right angles to each
other: the direction it bends *least* in and the direction it bends *most* in. The two curvatures
are `PrincipalCurvatures` and the two directions are `PrincipalDirections`, and they come paired —
the first direction goes with the first curvature:

```csharp
using Spark.Geometry;

CylindricalSurface pipe = new(Plane.WorldXY, 2.0, new Interval(0.0, 5.0));

(double least, double most) = pipe.PrincipalCurvatures(1.0, 2.5);
(Vector3d alongLeast, Vector3d alongMost) = pipe.PrincipalDirections(1.0, 2.5);

// A cylinder is flat along its axis and bends around it: one curvature is 0 and the other is
// 1 / radius in size, and the direction paired with the 0 runs along the axis.
```

A curvature is one over the radius of the tightest circle that fits the surface in that direction,
so a flat direction reads zero, a sphere of radius two reads a half every way, and the sign says
whether the surface bends towards its normal or away from it. The curvatures are what tell you a
surface is developable — one of them is zero everywhere, so it unrolls flat, as a cylinder or a cone
does — or a saddle, where they have opposite signs. The directions are what tell you *which way* it
unrolls, or which way a flat panel laid on it will want to curl.

**Where the surface bends the same amount every way** — anywhere on a sphere, anywhere on a plane,
and at isolated points on most other surfaces — there is no least or most, and every direction is a
principal direction. Spark returns a pair that follows the surface's own parameter lines rather than
refusing, and the two curvatures are equal there, which is how to tell.

The nodes take the parameters as fractions from 0 to 1 across each direction of the surface, as every
surface node does, and hand back each pair as a two-item list; the kernel members take the surface's
own parameters.

## Turning any surface into a NURBS surface

Every kernel, every interchange format and most downstream operations want one kind of surface, so
`ToNurbsSurface` turns any of Spark's nine into a NURBS surface — and tells you two things about
the result rather than one:

```csharp
using Spark.Geometry;

SphericalSurface globe = new(Plane.WorldXY, 3.0);

NurbsSurfaceConversion converted = globe.ToNurbsSurface();

NurbsSurface asNurbs = converted.Surface;
bool sameSheet = converted.IsExact;                          // true - a sphere is a rational quadric
bool samePoints = converted.PreservesParameterisation;       // false - see below
```

**`IsExact` means the same set of points.** A plane, a cylinder, a cone, a sphere and a torus are
all rational, so each converts to the last bit, with no tolerance and no sampling. An extrusion, a
revolution and a ruled surface convert exactly when the curves they are built from do.

**`PreservesParameterisation` is the second, stronger claim**, and it is the one that surprises
people. A rational quadratic traces a circular arc exactly and walks it by a projective function of
the angle rather than by the angle, so a converted sphere is the *same sheet* visited at *different
parameters*: the corners line up and `PointAt(u, v)` does not. A plane's bilinear form keeps both.

| Surface | Converted | Same parameters |
|---|---|---|
| `PlaneSurface` | **exactly** | yes |
| `NurbsSurface` | itself | yes |
| `CylindricalSurface`, `ConicalSurface`, `SphericalSurface`, `ToroidalSurface` | **exactly** | no |
| `ExtrusionSurface`, `RevolutionSurface` | as exactly as its profile converts | as its profile does |
| `RuledSurface` | **only if both rails keep their parameters** | only if neither rail is rational |

**The ruled surface is the row worth reading twice.** A ruling joins its two rails at *equal
parameters*. If a rail converts to the same curve visited in a different order — which is exactly
what happens to an arc — then the rulings join different pairs of points, and the sheet between them
is a different sheet even though both edges are perfectly right. So a ruled surface between a line
and a circle reports `IsExact` as false, and that is not a gap waiting to be filled: it is the
honest answer.

```csharp
using Spark.Geometry;

RuledSurface skirt = new(
    new Line(new Point3d(-2.0, 0.0, 5.0), new Point3d(2.0, 0.0, 5.0)),
    Circle.FromCenterRadius(Point3d.Origin, 2.0));

NurbsSurfaceConversion converted = skirt.ToNurbsSurface();
bool sameSheet = converted.IsExact;   // false - the rails are exact and the rulings are not
```

Where a conversion cannot be exact, pass a tolerance: it is a sampling target for the profile
curve, not a proved bound, and tightening it tightens the surface.

## A surface at a constant distance from another

`Offset` moves a surface along its own normal, by the distance you give it:

```csharp
using Spark.Geometry;

SphericalSurface globe = new(Plane.WorldXY, 3.0);

Surface thicker = globe.Offset(0.5);      // a sphere of radius 3.5
Surface thinner = globe.Offset(-0.5);     // a sphere of radius 2.5
```

**Positive follows the surface's normal and negative goes the other way**, which is the only
convention there is to get wrong, so it is worth saying once.

**An offset is exact for every surface, and a curve offset is not.** That asymmetry surprises
people, and the reason is not geometry: the offset of a NURBS curve is genuinely not a NURBS
curve, so `CurveOffset` has to fit one and hand you an approximation. A surface in Spark is
something that can be evaluated rather than a particular representation, so the offset of any
surface is just another surface and no accuracy is lost. You only meet an approximation if you
ask an offset surface for its NURBS form, and it will tell you it has none rather than inventing
one.

A plane offsets to a plane, a sphere to a sphere, a cylinder and a torus likewise — you get the
same kind of surface back. Everything else, including a cone, gives you a surface that follows
the original at the distance you asked for.

**Offsetting inwards by more than the surface has to give is an error**, not a surface turned
inside out: a sphere of radius two offset by −2 has nothing left to be, and says so.

**An offset bigger than the surface's tightest curve will fold**, crossing itself near the tight
parts, exactly as an offset curve does. That is a property of offsetting rather than of Spark,
and it is why an offset far larger than the detail on a surface is rarely what you want.

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

## Where OpenCascade comes into it

**Spark's exact solid modelling makes use of facilities provided by the Open CASCADE Technology
software.** OpenCascade is LGPL-2.1 with the Open CASCADE exception; its licence texts ship in
`licences/`, the full notice is in `THIRD-PARTY-NOTICES.md`, and `spark --version` prints which
version this build carries. The libraries ship unmodified and replaceable, and the build records
exactly what they were built from, so a request for the corresponding source can be answered
against *this* build rather than approximately.

## What is not built yet

- **Trimmed faces in Spark's own model.** A face Spark *builds* is bounded by its surface's own
  edges. Faces that come *back* from the provider are fully trimmed, so the result of a boolean is
  as trimmed as it needs to be; what is missing is the ability to author a trimmed face directly.
- **AP242.** STEP goes out as AP214. AP242 carries assemblies, names and colours, and Spark has
  none of those to put in a file yet.
- **Mesh booleans** — combining two *meshes* rather than two solids. **Deferred to after 1.0**
  ([ADR-0020](../../adr/0020-occt-via-c-abi-shim.md)), and deferred rather than dropped: the
  exact solid booleans arrived first and took the urgency away, not the requirement.

  **Do the boolean on solids and mesh the result.** `Solid.Union`, `Solid.Difference` and
  `Solid.Intersection` are exact, and `Solid.ToMesh` tessellates what comes back to whatever
  tolerance you ask for:

  ```csharp
  using Spark.Geometry;
  using Spark.Nodes.Core;

  Brep block = Solid.Box(Plane.WorldXY, 4.0, 4.0, 1.0);
  Brep hole = Solid.Cylinder(Plane.WorldXY, 1.0, 2.0);

  Mesh drilled = Solid.ToMesh(Solid.Difference(block, hole), tolerance: 0.005);
  ```

  **The limit, and it is why this is not a general answer.** Nothing turns a mesh back into a
  solid. A shape that *arrived* as a mesh — imported, or built by `Mesh.Sphere` and its
  neighbours — has no route to the exact booleans, so the advice above is *keep it a solid until
  you are finished with it*, not *convert when you need a boolean*.
- **Moving a solid without leaving the kernel.** `Translate`, `Rotate` and `Mirror` work on Spark's
  own arrays, so a solid that was living inside OpenCascade is read out first. Asking the provider
  to move it instead would be faster and would keep the shape exactly as the kernel built it.
