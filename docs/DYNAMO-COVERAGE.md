# Spark — Dynamo capability coverage

The register behind the client's instruction: *"Make sure we have all geometry elements and
methods and properties what is there in Dynamo."* It exists to turn that sentence into
something checkable.

**Last updated:** 2026-08-27
**Reference surface:** `ProtoGeometry.dll` as installed with Revit 2026
**Status legend:** `Done` · `Planned` · `Not planned` · `Needs a decision`

---

## 1. What this document is

This is a register of **capability parity**, not of file format, not of numerical behaviour,
and not of API shape. The commitment it tracks is narrow and precise: *a person who knows
Dynamo should never reach for a geometric capability in Spark and find it absent.* It does
not commit us to Dynamo's type names, its method names, its parameter order, its degenerate
cases or its tolerances, and it never will.

**This does not reopen ADR-0016, and the two are not in tension.** ADR-0016 refused `.dyn`
reading and writing because an imported graph is only useful if Spark's node behaves
*identically* to ProtoGeometry's in every degenerate case, at every tolerance and under every
lacing rule — and establishing that requires testing against ProtoGeometry, the dependency
Spark exists to remove. That argument is about **equivalence**, which is unprovable here.
This document is about **presence**, which is trivially provable: either a capability exists
in `Spark.Geometry` or it does not, and no reference implementation is needed to tell. We can
promise a user will find a fillet; we cannot promise it produces Autodesk's fillet.

The corollary already recorded in ADR-0004 stands unchanged: the `By*` names carry **no**
compatibility obligation and exist for human recognition only.

### Where the inventory came from

The reference surface was read from the metadata of the installed `ProtoGeometry.dll` — public
types and their public members only. **Nothing was decompiled and no implementation was
examined.** Every description of behaviour below is written from the signature plus general
geometry knowledge, in our own words. Where a signature does not determine what a member does,
this document says so rather than guessing; §6.3 collects those cases.

The raw inventory is deliberately **not** checked into this repository. It is a planning input,
not an artefact we own.

---

## 2. The headline numbers

`ProtoGeometry.dll` exposes **51 public types** carrying **837 public members** — 618 methods,
215 properties and 4 enum values. Every number in this document is a count from that inventory
or from this repository; none is an estimate.

| | Types | Members | Share of 837 |
|---|---:|---:|---:|
| ProtoGeometry public surface | 51 | 837 | 100% |
| Reachable in Spark today | 6 | **92** | **11.0%** |
| Deliberately not replicated (§5) | 7 + parts of 2 | 93 | 11.1% |
| Awaiting a decision — T-Splines (§6.2) | 8 | 169 | 20.2% |
| **Committed and still to build** | **30** | **483** | **57.7%** |

Against the scope we have actually committed to — 837 less the 93 we refuse and the 169 that
need their own decision, so **575 members** — Spark stands at **92 of 575, or 16.0%**.

### What the 92 counts, exactly

A ProtoGeometry member counts as **reachable** when a Spark user can obtain the same result
today through a documented member of `Spark.Geometry`. It does **not** require the same name
or the same owning type: `Vector.Transform(cs)` is counted because `CoordinateSystem.ToWorld`
and `Transform.OfVector` between them do the job, and `CoordinateSystem.Translate` is counted
because `Transform.Translation` does. Members that are pure serialisation, native-session
plumbing, or that operate on types Spark does not yet have, are counted as not reachable.

All 92 sit in one subsystem — values and frames — because that is the only subsystem that
exists. **There are no curves, surfaces, solids, meshes or topology in `Spark.Geometry`**, and
nothing in this document should be read as implying otherwise.

### Why member counts are not fungible, and must not be read as effort

Two warnings, both of which matter for reading the table above honestly.

**A percentage of members is not a percentage of work.** The 92 reachable members are the
easiest 92 in the whole inventory: arithmetic on six-double structs, decided by algebra and
verified by property tests. `Solid.Difference` is one row of one table and is a multi-year
research problem (§6.1). Any schedule derived from 16% is wrong by an order of magnitude.

**Parity is not a subset relation, and Spark's surface is already larger where it exists.**
The six ProtoGeometry types Spark covers declare 133 members between them. Spark's six
equivalents declare **183**, and Spark carries **six further value types** — `Transform`,
`Angle`, `Tolerance`, `Interval`, `Point2d`, `Vector2d` — declaring another **204** members
that ProtoGeometry either has no type for or scatters across other types. Spark's value layer
is 387 public members against ProtoGeometry's 133 for the same job. Adding parity gaps will
not make these two numbers converge, and they were never meant to.

---

## 3. Coverage by subsystem

Eight sections in dependency order. Each carries one row per ProtoGeometry type, the member
count from the inventory, our equivalent, its status and the milestone from
[PRD §11](PRD.md#11-release-plan) at which we expect it.

### 3.1 Values and frames — 6 types, 133 members, 92 reachable

| Dynamo type | Members | Spark equivalent | Status | Milestone |
|---|---:|---|---|---|
| `Point` | 15 | `Point3d` | Done (11/15) | M1 |
| `Vector` | 31 | `Vector3d` | Done (29/31) | M1 |
| `UV` | 6 | `UV` | Done (6/6) | M1 |
| `Plane` | 16 | `Plane` | Done (12/16) | M1 |
| `CoordinateSystem` | 46 | `CoordinateSystem` + `Transform` | Done (27/46) | M1 |
| `BoundingBox` | 19 | `BoundingBox` | Done (7/19) | M1 |

`Done` here means the type exists, is reviewed and is accepted — not that every member of the
Dynamo type is present. The bracketed fraction is the honest number and the prose below covers
every one of the 41 that are not.

**`CoordinateSystem` is the largest genuine shape difference in this subsystem, and it is
deliberate.** Dynamo's `CoordinateSystem` is a general affine frame: it can be scaled, sheared
and inverted, it answers `Determinant`, `IsSingular`, `IsScaledOrtho` and `XScaleFactor`, and
it carries the entire transformation API — `Rotate`, `Scale`, `Scale1D`, `Scale2D`, `Mirror`,
`Translate`, `PreMultiplyBy`, `PostMultiplyBy`. It is a 4×4 matrix wearing a frame's name.
Spark splits that in two: `CoordinateSystem` is an **orthonormal, unscaled** frame — an origin
and a right-handed basis, nothing more — and `Transform` is the 4×4 matrix with the whole
transformation algebra on it. Twelve of the 19 uncovered `CoordinateSystem` members are the
scale family (`XScaleFactor`, `YScaleFactor`, `ZScaleFactor`, `IsScaledOrtho`,
`IsUniscaledOrtho`, `ScaleFactor()`, `Scale` ×4, `Scale1D`, `Scale2D`), and they are
**Not planned**: a scaled frame is a `Transform` in Spark, and giving `CoordinateSystem` a
scale would mean every downstream operation that takes a frame has to decide what a non-unit
axis length means. The other seven are `ByMatrix` (planned — it is a `Transform` constructor
away), `ByCylindricalCoordinates` and `BySphericalCoordinates` (planned), `ByOriginVectors`
with an explicit Z axis (Not planned — Spark derives Z, and an independent Z is how you get a
left-handed or non-orthogonal frame by accident), and `FromJson`/`ToJson` (planned as part of
FR-57, in Spark's own format).

**`BoundingBox` is the weakest row and it is weak for a good reason.** Twelve of its 19
members are uncovered, and nine of those need geometry that does not exist —
`ByGeometry` ×2, `ByGeometryCoordinateSystem` ×2, `ByMinimumVolume`, `ToCuboid`,
`ToPolySurface`, plus `FromJson`/`ToJson`. Three are real gaps in a type we have already
shipped, and they are the useful finding of this section:

- **`Intersection(BoundingBox)`** — Spark has `Union` but no intersection of two boxes. This
  is four lines and should be added; it is not a design difference, it is an omission.
- **`ContextCoordinateSystem` and `ByCornersCoordinateSystem`** — Dynamo's box can be
  expressed in an arbitrary frame, so it is an oriented box in disguise. Spark's
  `BoundingBox` is strictly world-axis-aligned. **Needs a decision**, and the honest question
  is not whether to add a frame to `BoundingBox` but whether Spark wants a separate
  `OrientedBox` type. `Geometry.OrientedBoundingBox` and `BoundingBox.ByMinimumVolume` push
  the same way. Recorded as an open question rather than silently absorbed.

**`Point` and `Vector` are nearly complete.** The six uncovered members are
`ByCylindricalCoordinates` and `BySphericalCoordinates` on both types (planned — polar
construction is a small, real convenience), `Point.PruneDuplicates` (planned, and it wants the
`KDTree` of E2-T16 rather than an O(n²) loop), `Point.Project` onto geometry (planned, M5 —
it needs surfaces), and `Vector.FromJson`/`ToJson` (planned under FR-57). Nothing here is a
design difference.

**`Plane`'s four gaps** are `ByBestFitThroughPoints` (planned — least-squares fitting, and it
is the same machinery `Circle.ByBestFitThroughPoints` and `Line.ByBestFitThroughPoints` want,
so it should be written once), `ByLineAndPoint` (planned, M1, once `Line` exists),
`ByOriginNormalXAxis` (planned — trivial, and worth having because it is the only factory that
pins the in-plane rotation without a second point), and `Offset(distance)` (planned, trivial).

**Two Dynamo members on these types have no Spark equivalent and should not get one.**
`Point.ByCartesianCoordinates(cs, x, y, z)` is counted as reachable through
`CoordinateSystem.ToWorld`, and that is the right shape: construction and frame-mapping are
separate concerns and Spark keeps them separate. Similarly `Vector.Scale(x, y, z)` is reachable
through `Transform.Scale(x, y, z).OfVector(v)`; a non-uniform scale is a transformation, and
putting it on the vector implies a frame the vector does not carry.

### 3.2 Curves — 11 types, 187 members, 0 reachable

| Dynamo type | Members | Spark equivalent | Status | Milestone |
|---|---:|---|---|---|
| `Curve` (base) | 82 | `Curve` — the FR-48 contract | Planned | M1, M3 |
| `Line` | 6 | `Line` | Planned | M1 |
| `Arc` | 14 | `Arc` | Planned | M1 |
| `Circle` | 8 | `Circle` | Planned | M1 |
| `Ellipse` | 8 | `EllipseCurve` | Planned | M1 |
| `EllipseArc` | 9 | `EllipseCurve` over a sub-domain | Planned | M1 |
| `Helix` | 7 | `Helix` | Needs a decision | M3 |
| `NurbsCurve` | 15 | `NurbsCurve` | Planned | M3 |
| `PolyCurve` | 21 | `PolyCurve` | Planned | M1 |
| `Polygon` | 9 | `PolyLine`, closed | Planned | M1 |
| `Rectangle` | 8 | A `PolyLine` factory, not a type | Planned | M1 |

**`Curve` alone is 82 members — the largest non-T-Spline type in the inventory, and larger
than every Spark value type put together bar `Transform`.** FR-48 names fifteen members for
the curve contract. That is not wrong, but it is not parity either, and the gap is where this
section earns its keep. The 82 break down roughly as follows.

*Evaluation and frames (about 20 members).* `PointAtParameter`, `TangentAtParameter`,
`NormalAtParameter`, `PlaneAtParameter`, `CoordinateSystemAtParameter`,
`HorizontalFrameAtParameter`, and the `AtDistance` / `AtSegmentLength` / `AtChordLength`
variants of each. FR-48 covers the parameter-based half. **The distinct finding: Dynamo
exposes four parameterisations of the same query** — by parameter, by distance, by segment
length and by chord length — and a Spark contract offering only *by parameter* will feel
missing to every Dynamo user, because *divide a curve into equal lengths* is the single most
common thing anyone does to a curve. This is cheap once arc-length reparameterisation exists
and expensive if retrofitted, so it belongs in the M1 contract rather than after it.

*Division and sampling (about 10).* `DivideEqually`, `DivideByDistance`,
`DivideByDistanceFromParameter`, `DivideByLengthFromParameter`, `PointsAtEqualChordLength`,
`PointsAtEqualSegmentLength`, `PointsAtChordLengthFromPoint`, `PointsAtSegmentLengthFromPoint`.
All fall out of arc-length reparameterisation. None is in FR-48 and all of them should be.

*Trimming and splitting (about 16).* `ParameterSplit`, `SplitByParameter`, `SplitByPoints`,
`ParameterTrim`, `ParameterTrimStart`, `ParameterTrimEnd`, `ParameterTrimInterior`,
`ParameterTrimSegments`, and a `TrimBy*` family that duplicates the `Parameter*` family
name-for-name. **Dynamo carries two complete sets of trim methods that appear to do the same
thing** — `Curve.ParameterTrim(a, b)` and `Curve.TrimByParameter(a, b)` — which reads as a
deprecated set kept for compatibility. Spark should ship one set. Counting both towards parity
would be counting Autodesk's backwards compatibility as our feature.

*Modelling (9).* `Extrude` ×3, `ExtrudeAsSolid` ×3, `SweepAsSolid` ×2, `SweepAsSurface`,
`Patch`. These are surface and solid construction hanging off the curve type; they are M5/M6
and sit behind `IBrepKernel` (ADR-0003). See §6.1.

*Offset, projection and pull (7).* `Offset`, `OffsetMany`, `Project`, `PullOntoPlane`,
`PullOntoSurface`, `Simplify`, `ApproximateWithArcAndLineSegments`. Planar offset is E2-T12 and
E2-T14; projection onto a surface needs SSI.

**`Helix` is marked `Needs a decision` because it is absent from FR-48 and nobody has decided
against it.** It is a genuine, commonly used Dynamo curve — stairs, ramps, threads — with a
clean analytic form and seven members. Either it goes into FR-48 or its absence is recorded
as deliberate. Leaving it unstated is how a gap becomes a surprise at M3.

**`Rectangle` and `Polygon` are types in Dynamo and should be factories in Spark.** A
`Rectangle` that is a subclass of `Polygon` which is a subclass of `PolyCurve` gains nothing
over a closed `PolyLine` built by `PolyLine.ByRectangle(plane, width, length)`, and it costs a
public type that must be serialised, versioned, documented and node-ified forever. The four
capabilities that only live on those types — `Polygon.Center`, `Polygon.Corners`,
`Polygon.ContainmentTest`, `Polygon.SelfIntersections`, `Polygon.PlaneDeviation` and
`Polygon.RegularPolygon` — are all planned, on `PolyLine` or in `Spark.Geometry.Planar`.

### 3.3 Surfaces — 5 types, 106 members, 0 reachable

| Dynamo type | Members | Spark equivalent | Status | Milestone |
|---|---:|---|---|---|
| `Surface` (base) | 46 | `Surface` — the FR-49/E2-T17 contract | Planned | M5 |
| `NurbsSurface` | 17 | `NurbsSurface` | Planned | M5 |
| `PolySurface` | 18 | `Brep` (open shell) | Planned | M6 |
| `PanelSurface` | 21 | None — see §5 [d] | Not planned | — |
| `PanelSurfaceBoundaryCondition` | 4 | None — see §5 [d] | Not planned | — |

**Dynamo has no analytic surface types at all, and Spark has eight.** This is the one place
where the mapping runs the other way: FR-49 names `PlaneSurface`, `SphericalSurface`,
`CylindricalSurface`, `ConicalSurface`, `ToroidalSurface`, `ExtrusionSurface`,
`RevolutionSurface` and `RuledSurface` as first-class types, because analytic-analytic
intersection is exact and cheap where NURBS-NURBS is neither. ProtoGeometry exposes them only
as construction routes on `Surface` — `ByRevolve`, `ByRuledLoft`, `BySweep` — and a `Surface`
you receive tells you nothing about whether it is a plane. Parity is satisfied by having the
factories; the extra types are ours and are a capability Dynamo lacks.

**Twelve of `Surface`'s 46 members require exact booleans or trimming** and are counted in
§6.1: `ByUnion`, `Difference`, `SubtractFrom`, `TrimWithEdgeLoops` ×2, `Join` ×2, `Thicken`
×2, `Offset`, `Repair`, `ProjectInputOnto`. A further ten are the loft/sweep/revolve/patch
construction family behind the same seam. **That leaves 24 members that are pure evaluation**
— `PointAtParameter`, `NormalAtParameter`, `NormalAtPoint`, `DerivativesAtParameter`,
`CurvatureAtParameter`, `GaussianCurvatureAtParameter`, `PrincipalCurvaturesAtParameter`,
`PrincipalDirectionsAtParameter`, `TangentAtUParameter`, `TangentAtVParameter`,
`CoordinateSystemAtParameter`, `UVParameterAtPoint`, `GetIsoline`, `PerimeterCurves`,
`ToNurbsSurface` ×2, `ApproximateWithTolerance`, `FlipNormalDirection`, `Area`, `Perimeter`,
`Closed`, `ClosedInU`, `ClosedInV` — and these are what M5 must deliver. E2-T17's contract
names ten of them. The curvature family in particular (`Gaussian`, `Principal` values,
`Principal` directions) is absent from E2-T17 and is exactly what a facade-panelling or
structural-analysis graph reaches for.

**`Surface.CurvatureAtParameter` returns a `CoordinateSystem`, and we do not know what it
means.** Returning a frame from a curvature query is unusual — presumably the principal
directions as axes with magnitudes encoded in axis lengths, but that is a guess from the
signature. Flagged in §6.3.

### 3.4 Solids — 5 types, 55 members, 0 reachable

| Dynamo type | Members | Spark equivalent | Status | Milestone |
|---|---:|---|---|---|
| `Solid` (base) | 24 | `Brep` (closed) | Planned / post-1.0 | M6 + |
| `Cuboid` | 8 | `Brep.ByBox` factory, not a type | Planned | M6 |
| `Sphere` | 6 | `Brep.BySphere` factory, not a type | Planned | M6 |
| `Cone` | 11 | `Brep.ByCone` factory, not a type | Planned | M6 |
| `Cylinder` | 6 | `Brep.ByCylinder` factory, not a type | Planned | M6 |

**Spark has no `Solid` type and will not have one.** Dynamo splits `PolySurface` from `Solid`
by *closure*: a `PolySurface` that happens to be watertight is a `Solid`, and a `Solid` that
fails to sew is a `PolySurface`. Spark splits by *representation*: there is one `Brep`, and
`IsSolid` is a query on it. The reason is concrete rather than aesthetic — closure is a
tolerance-dependent predicate, so under Dynamo's scheme a healing operation can change an
object's **type**, and every signature taking a `Solid` becomes a place where a nearly-closed
model is refused for reasons the user cannot see. `Solid.Volume` and `Solid.Centroid` become
`Brep` members that return a `Result<T>` naming the open edge when the shell is not closed,
which is the diagnosable version of the same thing (ADR-0003).

**The four primitives are factories, not types.** `Cuboid`, `Sphere`, `Cone` and `Cylinder`
carry 31 members between them, of which 20 are constructors and 11 are property accessors
recovering the defining parameters (`Cone.RadiusRatio`, `Cylinder.Axis`, `Cuboid.Height`).
Keeping the parameters requires keeping a live parametric type, which is a different and much
larger commitment than producing the `Brep`; the same tension exists in every kernel. Spark
produces a `Brep` from a factory and does not promise to tell you afterwards what its radius
was. **The 11 recovery properties are therefore `Not planned` in that form**; the same
information is available from the analytic faces of the resulting `Brep`, which is where it
actually lives.

**Twelve of `Solid`'s 24 members are exact-boolean work** — the whole of §6.1's argument. See
there.

### 3.5 Topology — 6 types, 33 members, 0 reachable

| Dynamo type | Members | Spark equivalent | Status | Milestone |
|---|---:|---|---|---|
| `Topology` (base) | 4 | `Brep` navigators | Planned | M6 |
| `Vertex` | 4 | `BrepVertex` | Planned | M6 |
| `Edge` | 6 | `BrepEdge` | Planned | M6 |
| `CoEdge` | 10 | `BrepTrim` | Planned | M6 |
| `Loop` | 4 | `BrepLoop` | Planned | M6 |
| `Face` | 5 | `BrepFace` | Planned | M6 |

This is the cleanest subsystem in the inventory: 33 members, almost all of them navigation.
`Edge.AdjacentFaces`, `Vertex.AdjacentEdges`, `Loop.CoEdges`, `CoEdge.Next`/`Previous`/
`Partner`/`Reversed`, `Face.Loops`. Parity is achievable in full and should be.

**Two structural differences, both already decided and neither a gap.**

*Objects versus indices.* Dynamo's topology is a graph of reference objects: `Edge` holds a
`Face[]`, `CoEdge` holds a `CoEdge`. E2-T22 makes Spark's index-based — arrays and `int`
indices, no object references — because it serialises trivially, has no cycles to break for
immutability, is cache-friendly, and makes an OCCT adapter mechanical if R1 ever forces one.
E2-T23's `readonly ref struct` navigators recover the ergonomics: `brep.Edge(i).AdjacentFaces`
reads the same and allocates nothing. Every one of the 33 members has an exact equivalent in
that model.

*`CoEdge` is `BrepTrim`.* This is the one rename in the whole document that a reader could
mistake for a different concept, and §4 explains it.

**`Face.SurfaceGeometry()` and `Edge.CurveGeometry` are the load-bearing members here** — they
are how a user gets from topology back to geometry, and they are the reason a topology
subsystem is useful at all rather than an implementation detail. They are cheap in the
index-based model and must not be forgotten.

### 3.6 Mesh — 2 types, 65 members, 0 reachable

| Dynamo type | Members | Spark equivalent | Status | Milestone |
|---|---:|---|---|---|
| `Mesh` | 55 | `Mesh` | Planned | M5, M6 |
| `IndexGroup` | 10 | Face records on `Mesh` | Planned | M5 |

FR-51 and E2-T20 describe `Mesh` as *indexed vertices, tri and quad faces, optional normals,
UVs and colours, and lazily built halfedge adjacency*. That is the data structure. Dynamo's
55 members are mostly **operations**, and reading them is the useful part of this section.

*Construction (9).* `ByPointsFaceIndices`, `ByPointsIndexGroups`, `ByPointsIndices`,
`ByVerticesAndIndices`, `ByGeometry`, plus primitive generators `Cone`, `Cuboid`, `Sphere`,
`Plane`. All planned; the four `By*` overloads collapse to two in Spark.

*Query and access (16).* `VertexPositions`, `VertexNormals`, `FaceIndices`, `VertexCount`,
`TriangleCount`, `EdgeCount`, `Area`, `Volume`, `BoundingBox`, `TriangleCentroids`,
`TriangleNormals`, `Triangles`, `Edges`, `VertexIndicesByTri`, `Nearest`, `Project`. All
planned. The three flattened accessors — `VerticesAsThreeNumbers`, `EdgesAsSixNumbers`,
`TrianglesAsNineNumbers` — exist for Dynamo's list plumbing and are **Not planned**; Spark
returns spans of typed values and the graph handles them (E4-T2).

*Repair and remeshing (8).* `Repair`, `MakeWatertight`, `CloseCracks`, `Remesh`, `Reduce`,
`Smooth`, `Explode`, `MakeHollow`. **This is the finding of this section.** Not one of these
appears anywhere in FR-51, E2-T20 or E2-T27, and they are not small: mesh repair and
decimation are their own literature. A Dynamo user importing an STL reaches for `Repair` and
`Reduce` immediately. They are planned here and need to be planned in the PRD.

*Booleans (3).* `BooleanUnion`, `BooleanDifference`, `BooleanIntersection` — E2-T27, M6, and
the mechanism by which Spark ships working booleans before exact ones exist (ADR-0002).

*Fabrication (2).* `GenerateSupport` and `MakeHollow` are 3D-printing features. **Needs a
decision** whether Spark wants them; they are a product direction, not a kernel primitive.

*Interchange (2).* `ImportFile`, `ExportMeshes` — FR-58, E2-T34/T35.

**`IndexGroup` is a tri-or-quad index record with an `A`/`B`/`C`/`D`/`Count` shape.** Spark's
`Mesh` carries tri and quad faces directly, so the type is not needed; its capability is.

### 3.7 T-Splines — 8 types, 169 members, 0 reachable

| Dynamo type | Members | Spark equivalent | Status | Milestone |
|---|---:|---|---|---|
| `TSplineSurface` | 94 | None | Needs a decision | — |
| `TSplineTopology` | 26 | None | Needs a decision | — |
| `TSplineVertex` | 11 | None | Needs a decision | — |
| `TSplineEdge` | 9 | None | Needs a decision | — |
| `TSplineFace` | 8 | None | Needs a decision | — |
| `TSplineInitialSymmetry` | 8 | None | Needs a decision | — |
| `TSplineReflection` | 8 | None | Needs a decision | — |
| `TSplineUVNFrame` | 5 | None | Needs a decision | — |

**169 members — 20.2% of the whole inventory, and `TSplineSurface` alone is 94, larger than
`Curve`.** This is not a gap to be filled in passing. §6.2 makes the argument in full.

### 3.8 Infrastructure — 8 types, 89 members, 0 reachable

| Dynamo type | Members | Spark equivalent | Status | Milestone |
|---|---:|---|---|---|
| `Geometry` (abstract base) | 47 | No common base — see below | Mixed | M5, M6 |
| `DesignScriptEntity` (abstract base) | 8 | No common base — see §5 [e] | Mixed | — |
| `GeometryExtension` (static) | 15 | `Angle`, `Tolerance` — see §5 [f] | Not planned | — |
| `Application` | 6 | None — see §5 [a] | Not planned | — |
| `HostFactory` | 6 | None — see §5 [a] | Not planned | — |
| `ProtoGeometryConfiguration` | 2 | None — see §5 [b] | Not planned | — |
| `IProtoGeometryConfiguration` | 2 | None — see §5 [b] | Not planned | — |
| `Core.EntityTags` | 3 | Graph provenance — see §5 [c] | Not planned | — |

**Spark has no `Geometry` base class and should not acquire one.** ADR-0002's value types are
`readonly struct`s; a common abstract base would box every one of them and cost them their
reason for existing. The capability on `Geometry` still has to land somewhere, and the 47
members split three ways.

*Transformation (12) — planned, and the destination is already built.* `Transform` ×2,
`Translate` ×3, `Rotate` ×2, `Scale` ×4, `Mirror`, `Scale1D`, `Scale2D`. In Spark these are
`Transform` factories applied through `Of*`, which is one mechanism instead of twelve members
repeated on every geometry type. Note what this changes for a Dynamo user: `curve.Rotate(p, a,
45)` becomes `Transform.Rotation(axis, Angle.FromDegrees(45), p).OfCurve(curve)`. That is more
verbose and it is the shape ADR-0011 and ADR-0004 both point at. **The `By*` façade should
carry the short forms as node-friendly statics** so the node library reads the way an AEC user
expects even though the kernel reads the way a C# developer expects.

*Measurement and intersection (10) — planned, mostly M5/M6.* `BoundingBox`,
`OrientedBoundingBox`, `ClosestPointTo`, `DistanceTo`, `DoesIntersect`, `Intersect`,
`IntersectAll`, `Split`, `Trim`, `Explode`. Five of these are in §6.1's exact-boolean count.
`OrientedBoundingBox` is the same open question as §3.1's.

*Serialisation and interop (25) — split.* `ToJson`/`FromJson` (2) are planned in Spark's own
format under FR-57. **The remaining 18 are `Not planned`** and are listed in §5 [g]: the
SAT/SAB family is ACIS's format and reading it requires the ACIS kernel, and
`FromNativePointer`/`ToNativePointer`/`FromObject` marshal to a native kernel session Spark
does not have. `Approximate`, `DeserializeFromSAB`, `UpdateDisplay` and `ToSolidDef` complete
that set. Spark's interchange answer is STEP (FR-59) and OBJ/STL/PLY/glTF (FR-58).

---

## 4. Naming

Spark uses idiomatic C# with a `By*` façade (ADR-0004) and its own type names. This section
maps every rename so the mapping never has to be reconstructed from memory.

| Dynamo | Spark | Note |
|---|---|---|
| `Point` | `Point3d` | Renamed — see below |
| `Vector` | `Vector3d` | Renamed — see below |
| `UV` | `UV` | Same |
| `Plane` | `Plane` | Same |
| `CoordinateSystem` | `CoordinateSystem` | Same name, unscaled — §3.1 |
| `BoundingBox` | `BoundingBox` | Same name, world-aligned only — §3.1 |
| `Curve` | `Curve` | Same |
| `Line` | `Line` | Same |
| `Arc` | `Arc` | Same |
| `Circle` | `Circle` | Same |
| `Ellipse`, `EllipseArc` | `EllipseCurve` | Two types collapse to one over a domain |
| `Helix` | `Helix` | Same, pending §3.2's decision |
| `NurbsCurve` | `NurbsCurve` | Same |
| `PolyCurve` | `PolyCurve` | Same |
| `Polygon` | `PolyLine` (closed) | Not a distinct type |
| `Rectangle` | `PolyLine` factory | Not a distinct type |
| `Surface` | `Surface` | Same |
| `NurbsSurface` | `NurbsSurface` | Same |
| — | `PlaneSurface` … `RuledSurface` | Eight analytic types Dynamo has no name for |
| `PolySurface` | `Brep` (open) | Renamed — see below |
| `Solid` | `Brep` (closed) | Renamed — see below |
| `Cuboid`, `Sphere`, `Cone`, `Cylinder` | `Brep` factories | Not distinct types — §3.4 |
| `Topology` | `Brep` | Renamed — see below |
| `Vertex` | `BrepVertex` | Prefixed |
| `Edge` | `BrepEdge` | Prefixed |
| `Face` | `BrepFace` | Prefixed |
| `Loop` | `BrepLoop` | Prefixed |
| `CoEdge` | `BrepTrim` | Renamed — see below |
| `Mesh` | `Mesh` | Same |
| `IndexGroup` | Face record on `Mesh` | Not a distinct type |
| `Geometry` | — | No common base — §3.8 |
| `DesignScriptEntity` | — | No common base — §5 [e] |
| `GeometryExtension` | `Angle`, `Tolerance` | Capability rehoused — §5 [f] |
| `TSpline*` (8 types) | — | §6.2 |

### The three renames that are not obvious

**`Point` → `Point3d` and `Vector` → `Vector3d`.** Two reasons, and the second is the decisive
one. FR-60 commits Spark to a planar supporting layer with `Point2d`, `Curve2d` and `Region`,
so an unqualified `Point` would have collided the moment that arrived, and renaming a type
after node graphs reference it is not something ADR-0019 lets us do casually. More sharply:
`Point` is one of the most-collided type names in .NET — `System.Drawing.Point`,
`System.Windows.Point`, `Avalonia.Point` — and E6's code block hosts arbitrary `using`
directives written by users. A kernel type that a user's own `using` can silently shadow is a
support burden we can decline for the price of two characters.

**`CoEdge` → `BrepTrim`.** A co-edge is one face's use of a shared edge; an edge between two
faces has two of them, running in opposite directions. Dynamo names it for its topological
role. Most kernels name it for what the data actually is — the **trim**, because the entity
carries the curve in the face's own UV parameter space that bounds the face there. Spark's
`BrepTrim` holds a `Curve2d` (E2-T13), which is why the planar layer is a prerequisite for
BRep rather than a nicety. `CoEdge` describes the relationship; `BrepTrim` describes the
payload, and the payload is what users need to reach.

**`Solid`, `PolySurface` and `Topology` all → `Brep`.** Three Dynamo types become one because
Dynamo's distinction is by closure and Spark's is by representation. §3.4 gives the reason:
closure is tolerance-dependent, so under Dynamo's scheme healing a model can change its type.
`Topology` is separately redundant in Spark — it exists in ProtoGeometry as the base carrying
`Vertices`/`Edges`/`Faces` for both `Surface` and `Solid`, and in Spark those are navigators on
`Brep`.

### Member names we will not copy

**Anything implying mutation.** Spark's geometry is immutable values; a name that reads as a
command to change something in place is wrong even when the underlying method returns a new
instance. `PanelSurface.SetTransform` is the clearest case — it reads as a setter and returns a
new `PanelSurface`. `DesignScriptEntity.Dispose` is the other: Spark's geometry is not a handle
into a native session and has no lifetime to end (§5 [e]).

The subtler set is `Mesh.Repair`, `Mesh.MakeWatertight`, `Mesh.CloseCracks`, `Mesh.Reduce`,
`Mesh.Smooth`, `Solid.Repair` and `Surface.Repair`. All of these return new instances in
Dynamo, so the semantics are already right; only the names read as in-place edits. Spark's
convention is the past participle or an explicit `Try*`: `Repaired()`, `Reduced(n)`,
`Smoothed(scale)`.

**And a finding about our own code, since this document had to check it.** `Spark.Geometry`
is not yet consistent about this. `Vector3d.Normalised()`, `Interval.Reversed()` and
`Interval.MakeIncreasing()` follow the rule; `Plane.Flip()`, `BoundingBox.Inflate()` and
`Interval.Expand()` do not — all three read as imperatives and all three return new values.
This is not a defect and none of them is ambiguous in practice, but the convention should be
settled and written into `NamespaceDoc` before curves arrive and multiply the surface by five.
Raised here because this document is the first thing to read both APIs side by side.

---

## 5. What we will deliberately not replicate

**93 members, 11.1% of the inventory.** Each with a reason and with what a Spark user does
instead. These were evaluated on their merits rather than accepted as a list.

**[a] `Application` (6) and `HostFactory` (6) — kernel session lifetime.** `StartUp`,
`ShutDown`, `PreloadAsmLibraries`, `IsExecuting`, `Instance`, `Factory`, `PersistenceManager`.
These exist because ProtoGeometry is a managed façade over a native ASM kernel that must be
loaded, started with a scale factor, and shut down. Spark's geometry is managed values with no
session, so there is nothing to start. **A Spark user does nothing** — they construct a
`Point3d` and it works, in a unit test, in a CLI, in a Revit add-in, with no initialisation
call anywhere. This is the same promise as G1 and NFR-5, expressed at the API level.

**[b] `ProtoGeometryConfiguration` (2) and `IProtoGeometryConfiguration` (2).**
`GeometryFactoryFileName` and `PersistentManagerFileName` name the native DLLs to load. Spark
loads no DLLs to do geometry. **A Spark user does nothing.** If a future OCCT-backed provider
arrives under R1's fallback, its configuration belongs in `Spark.Host`'s composition root and
behind `IBrepKernel` (ADR-0003), never in the geometry API.

**[c] `Core.EntityTags` (3) — arbitrary data attached to a geometry instance.** `AddTag`,
`LookupTag`, `Parent`. This requires geometry to have **identity**: a tag is attached to *this*
instance and retrieved from it later. Spark's geometry is a value with no identity, no
registry and nothing ambient — PRD principle 3, and the specific anti-pattern C2VGeometry's
auto-registering `Shape` is being designed out of. Two `Point3d`s with the same coordinates are
the same value and cannot carry different tags. **A Spark user carries their data alongside the
geometry** — a tuple through the graph, or their own dictionary — and for provenance uses the
`(NodeId, PortIndex, ElementPath)` key the engine already threads through diagnostics, viewport
buffers, selection and the watch panel (E9-T9). That key survives recomputation, which an
instance tag does not.

**[d] `PanelSurface` (21) and `PanelSurfaceBoundaryCondition` (4) — 25 members.** Nine
patterned panelling generators over a surface — quads, staggered quads, diamonds, split
diamonds, hexagons, rhombitrihexagonals, parallelograms, cross-split and diagonally-split
squares — plus accessors for the resulting panels and vertices. **This is the entry in the list
that most deserves a second look, and it still loses.** It is a real capability that AEC users
genuinely want; it is also a *design* feature rather than a kernel primitive. Every one of the
nine patterns is a UV-space tiling plus a `Surface.PointAtParameter` call — it needs nothing
from the kernel that FR-49 does not already provide, and putting it in `Spark.Geometry` would
mean the kernel owns a taxonomy of architectural panelling patterns forever. **A Spark user
gets this from a node package** built over the public surface API, which is exactly the
extensibility story E5 and E7 exist for, and it is how Dynamo users get most panelling
(LunchBox) in practice anyway. Recorded as a strong candidate for a first-party node package
once M5 lands, not as a kernel gap.

**[e] `DesignScriptEntity` — 4 of its 8 members.** `Dispose`, `BaseTessellationGuid`,
`InstanceInfoAvailable` and the static `scaleFactor` are all lifetime and native-interop
machinery: a ProtoGeometry object is a **disposable handle bound to a native kernel session**,
which is why it implements `IDisposable`, why it has a tessellation cache GUID and why there is
a process-wide scale factor. Spark's geometry is immutable values; none of the four has a
meaning. **A Spark user does nothing** — there is no `using` block around geometry, and scale
is handled by `Tolerance.ForScale` per call rather than by a static (ADR-0010). The other four
members of this type are **not** refused: `Equals`, `GetHashCode` and `ToString` are `Done` on
every Spark value type, and `Tessellate(IRenderPackage, TessellationParameters)` is planned —
it lands in `Spark.Api` and `Spark.Viewport` as `RenderPackage` (FR-67, E2-T26, E9-T3) rather
than on the geometry, because geometry has no screen awareness.

**[f] `GeometryExtension` (15) — a public static helper class.** `ToEntity`,
`GetCurveEntity`, `ToPointArray`, `ToPointEntityArray`, `ConvertAll`, `ForEach` ×2,
`AreCoincident`, `LocateFile` and friends. Most of it is internal plumbing that happens to be
public — array marshalling between the façade and the native entity layer. **Not planned as a
type.** The four members that carry real capability already have better homes in Spark:
`DegreesToRadians` and `RadiansToDegrees` are `Angle.FromDegrees`/`Angle.Degrees` (ADR-0011),
and `Equals(x, y, tolerance)` and `LessThanOrEquals(x, y, tolerance)` are `Tolerance.AreEqual`
and `Tolerance.IsLessThan` (ADR-0010). `EqualsTo(a, b)` and `LessThanOrEqualTo(a, b)` — the
overloads with no tolerance parameter — read as comparisons against an **ambient** tolerance,
which is precisely what ADR-0010 forbids, and they are refused on that ground specifically.

**[g] `Geometry`'s ACIS interchange and native marshalling — 18 members.** `ExportToSAT` ×4,
`ImportFromSAT` ×4, `SerializeAsSAB` ×2, `DeserializeFromSAB` ×2, `FromNativePointer`,
`ToNativePointer`, `FromObject`, `FromSolidDef`, `ToSolidDef`, `UpdateDisplay`. SAT and SAB are
ACIS's own formats; writing a conformant reader means implementing ACIS's model, and the
practical reason anyone uses them is to hand geometry to another ACIS host. The native-pointer
members marshal into a kernel session Spark does not have, and `UpdateDisplay` reaches from
geometry into a viewer, which is the coupling E2's scope boundary exists to prevent. **A Spark
user exchanges geometry through STEP AP203/AP214** (FR-59, E2-T36) **and meshes through
OBJ, STL, PLY and glTF** (FR-58) — open formats a third-party viewer can verify, which is also
what R12's validation rule requires.

**[h] `CoordinateSystem`'s scale family — 12 members**, and **`Solid`/`Cone`/`Cylinder`/
`Cuboid`/`Sphere`'s parameter-recovery properties — 11 members**, both argued in §3.1 and §3.4.
*(These 23 are counted in the 93.)*

**[i] Dynamo's duplicated trim family and flattened mesh accessors.** `Curve`'s `TrimBy*`
methods duplicate its `Parameter*` methods name-for-name, and `Mesh`'s
`VerticesAsThreeNumbers` / `EdgesAsSixNumbers` / `TrianglesAsNineNumbers` exist to feed
Dynamo's list plumbing. Spark ships one trim family and returns typed spans. *(Not counted in
the 93 pending the member-by-member pass of §7, because which of the two trim families is the
survivor is a design choice we have not made.)*

---

## 6. The two findings that change scope

### 6.1 Parity on `Solid` and `Surface` commits us to exact solid modelling

**32 members of the inventory cannot exist without exact BRep booleans, trimming, filleting or
sewing.** Named, so that nobody has to take the number on trust:

| Type | Members |
|---|---|
| `Geometry` (5) | `DoesIntersect`, `Intersect`, `IntersectAll`, `Split`, `Trim` |
| `Solid` (12) | `Union`, `UnionAll`, `ByUnion`, `Difference`, `DifferenceAll`, `Fillet`, `Chamfer`, `ThinShell`, `Separate`, `Repair`, `ByJoinedSurfaces`, `ProjectInputOnto` |
| `Surface` (12) | `ByUnion`, `Difference`, `SubtractFrom`, `TrimWithEdgeLoops` ×2, `Join` ×2, `Thicken` ×2, `Offset`, `Repair`, `ProjectInputOnto` |
| `PolySurface` (3) | `Fillet`, `Chamfer`, `ByJoinedSurfaces` |

A further **38 members** — the loft, sweep, revolve, extrude, patch and projection families on
`Curve`, `Surface`, `Solid`, `PolySurface` and `Point` — sit behind the same `IBrepKernel` seam
as ADR-0003 draws it. **70 members in total, 8.4% of the inventory, all of them behind the
single hardest thing in the project.**

**This is precisely the work ADR-0002 stages last and R1 calls research-grade.** ADR-0002 says
it without softening: robust surface-surface intersection with tangential and degenerate cases
handled correctly is a research problem, it is what makes commercial kernels cost millions, and
*it is entirely possible that our implementation never reaches production robustness.* PRD §9
lists **"Exact NURBS booleans, and fillet and chamfer on solids"** as out of scope, post-1.0,
stated publicly. E12-T15 exists to say so publicly at 1.0. TODO.md's *Known and deliberately
accepted* says the same.

**Full capability parity contradicts every one of those, and the contradiction should be stated
in one sentence rather than discovered at M6.** If Spark commits to the instruction as written,
then `Solid.Difference`, `Solid.Fillet`, `Solid.Chamfer` and `Surface.Trim` are **not optional
and not post-1.0** — they are 1.0 requirements, R1 is promoted from a risk that is mitigated to
a risk that must be *retired*, and the M5 SSI spike (E2-T37) stops being a calibration exercise
and becomes a go/no-go gate on the release. The realistic paths are three, and they should be
chosen between deliberately:

1. **Accept the commitment.** Exact booleans are in 1.0. R1 must be retired, the M6 estimate is
   wrong by a large and currently unknown factor, and E12-T15 is deleted rather than written.
2. **Hold the current staging and scope parity to 1.x rather than 1.0.** 1.0 ships mesh
   booleans (E2-T27) with `IBrepKernel`'s `Capabilities` greying out what is absent, exact
   booleans land in a later 1.x, and the parity promise is *by the end of 1.x* rather than *at
   1.0*. This is what the PRD currently says, and it is the only path where the existing
   estimates survive.
3. **Take the OCCT fallback deliberately rather than as a contingency.** ADR-0002 keeps an
   OCCT-backed **optional** package viable through the `IBrepKernel` seam. Choosing it up front
   would deliver all 70 members and would make the no-native-dependencies promise conditional
   for anyone who installs it — which is open question Q6, currently unanswered.

**The technical recommendation is (2)**, because it is the only option that does not require
either revising an estimate we have no basis for revising or reopening ADR-0002 before the M5
spike has told us anything. But this is a client decision, not ours: it trades the headline
promise against the release date, and both belong to the client. It is registered as **Q11**.

### 6.2 T-Splines is a second product, not a subsystem

**169 members across 8 types — 20.2% of the entire ProtoGeometry surface.** `TSplineSurface`
alone carries 94 members, more than `Curve` (82), more than `Mesh` (55), more than `Surface`
(46) and `Solid` (24) combined.

**It is a different discipline from everything else in this document.** T-Splines are a
subdivision-surface representation: a control mesh with T-junctions, refined by a
Catmull-Clark-like scheme, with creases, star points, valence and *smooth mode* versus *box
mode* as first-class concepts. BRep/NURBS modelling and subdivision modelling share the word
*surface* and almost nothing else — different data structure, different refinement
mathematics, different literature, different failure modes. Nothing Spark builds for FR-49 or
FR-50 helps build this, and nothing built for this helps FR-49.

**And the API is a modelling editor, not a geometry library.** Read the verbs on
`TSplineSurface`: `BevelEdges`, `SlideEdges`, `BridgeEdgesToFaces`, `WeldVertices`,
`UnweldEdges`, `CreaseEdges`, `UncreaseVertices`, `FlattenVertices`, `PullVertices`,
`SubdivideFaces`, `DuplicateFaces`, `FillHole`, `MergeEdges`, `Standardize`, `MakeUniform`,
`CompressIndexes`, `EnableSmoothMode`, `CreateMatch`. These are the commands of an interactive
sculpting tool — Autodesk's, acquired with T-Splines Inc. and shared with Fusion — surfaced as
graph nodes. Building them means building the modeller. It also brings its own file formats
(`.tsm`, `.tss`, with six import/export members), its own symmetry and reflection model
(`TSplineInitialSymmetry`, `TSplineReflection`, 16 members), and its own topology layer
(`TSplineTopology`, 26 members, with star-point, T-point and non-manifold queries that have no
BRep analogue).

**The recommendation is to exclude T-Splines from the parity commitment and say so publicly,
the way PRD §9 already handles STEP's scope and exact booleans.** Three supporting reasons:

- It is **close to a product in its own right**. Estimating it at anything less than the M5+M6
  surface-and-BRep budget would not be credible, and that budget is already 22 weeks.
- Its natural users are industrial designers doing organic form-finding. Spark's primary user
  (D9) is the AEC computational designer, whose work is overwhelmingly BRep, mesh and planar.
- Excluding it costs nothing structurally. ADR-0003's note anticipates exactly this case: *a
  subdivision or implicit modelling backend whose data model genuinely cannot be expressed as
  our `Brep`* would be **a different decision, not a widening of this one**. A future
  `Spark.Geometry.Subdivision` remains buildable as a separate assembly with no debt incurred
  by leaving it out now.

**Excluding it also changes the headline number honestly**, which is the other reason to decide
it rather than leave it: with T-Splines out, the committed surface is 575 members rather than
744, and *every* future coverage percentage in this document depends on which of those two is
the denominator. Registered as **Q12**.

### 6.3 What we could not interpret confidently

Honest gaps in this document, from signatures that do not determine behaviour. Each needs
checking against a running Dynamo before the corresponding Spark member is designed — not
before it is *listed*, which is why they do not block this register.

- **`Curve`'s four length parameterisations.** `DistanceAtParameter`,
  `SegmentLengthAtParameter`, `ParameterAtDistance` and `ParameterAtSegmentLength` are four
  names for what look like two concepts. Which pair is arc length from the start and which is
  something else is not deducible.
- **`Curve.DivideByDistance(Int32 divisions)`.** The name says distance, the only parameter is
  named `divisions` and is an `Int32`. Either the name or the parameter is misleading.
- **`Curve.NormalAtParameter(param, Boolean side)`** — what `side` selects.
- **`Curve.HorizontalFrameAtParameter`** — presumably a frame whose X lies in the world XY
  plane, but the tie-break at a vertical tangent is unstated.
- **`Curve.ParameterAtChordLength(chordLength, parameter, forward)`** — the role of the
  starting `parameter` and of `forward` is a guess.
- **`Surface.CurvatureAtParameter(u, v)` returning a `CoordinateSystem`** (§3.3).
- **`Geometry.Approximate()` and `Curve.ApproximateWithArcAndLineSegments()`** take no
  tolerance, so the tolerance comes from somewhere unstated — very likely the ambient session
  scale factor, which is exactly what ADR-0010 refuses.
- **`Geometry.FromObject(Int64)`, `FromSolidDef(String)`, `ToSolidDef()`** — undocumented
  shapes, probably Autodesk-internal. Not planned regardless (§5 [g]).
- **`DesignScriptEntity.BaseTessellationGuid` and `InstanceInfoAvailable`** — unclear, and not
  planned regardless (§5 [e]).
- **`Geometry.ContextCoordinateSystem`** — what a "context" frame means for arbitrary geometry,
  and how it differs from `BoundingBox.ContextCoordinateSystem`.
- **`BoundingBox.IsEmpty()`** — degenerate, zero-volume, or never-initialised.
- **`PolyCurve.Heal(Double trimLength)` and `PolyCurve.CurveAtIndex(index, Boolean endOrStart)`**
  — the role of `trimLength` and of `endOrStart` alongside an index.
- **`Solid.ByRuledLoft(IEnumerable<PolyCurve>, Boolean checkAndRepair)` versus
  `Surface.ByRuledLoft(IEnumerable<Line>)`** — same name, incompatible input types.
- **`Polygon.PlaneDeviation`** — presumably maximum distance from a best-fit plane, but the fit
  is unstated.
- **`Cone.RadiusRatio`** — presumably `EndRadius / StartRadius`, but the direction is a guess.
- **Most of `TSplineSurface`.** `CreateMatch`'s twelve parameters (`continuity` as a bare
  `Int32`, `curvParamWeight`, `usePropagation`), `FillHole`'s untyped `fillMethod`, and
  `Interpolate(Boolean reverse)` need the T-Spline literature rather than a signature. Recorded
  and not guessed at; §6.2 recommends we never need to resolve them.

---

## 7. How this document is kept true

A register that drifts is worse than no register, because it is consulted with confidence. The
mechanism below is proposed rather than promised, and it is registered as **E11-T23**.

**The failure to design against is documented and this project's own.** DoodleSharp's help was
driven by three hand-maintained dictionaries of roughly 1,478 member entries keyed by string.
It drifted badly enough that 101 of 108 public constructors rendered blank while seven
carefully written entries pointed at members that no longer existed, and two dedicated test
suites had to be written to catch it after the damage. A Markdown table of 837 member states,
maintained by hand, is the same artefact in a different file extension.

### What to build

**A checked-in manifest plus a two-way diff test.** The same shape `Spark.Docs.Verify` already
uses for ADR citations and help topics, and the same shape E5-T6 and E11-T13 specify for
node↔member coverage.

1. **`tests/corpus/dynamo-parity.tsv`** — one row per ProtoGeometry member, tab-separated:
   `DynamoType`, `Member`, `Status`, `SparkMember` (fully qualified, or empty), `Reason`
   (required when `Status` is `Not planned` or `Needs a decision`). 837 rows, generated once
   from the inventory and thereafter edited by hand as decisions land. It records *our*
   decisions about a surface we have read; it is not a copy of the surface.
2. **A check in `Spark.Docs.Verify`** that fails when:
   - a row says `Done` and the named Spark member does not exist in `Spark.Geometry` — **this
     is the rename-catcher**, and the reason the manifest names members rather than types;
   - a public member of `Spark.Geometry` is named by no row and is absent from an exclusions
     file with a stated reason — the reverse direction, which is what catches a Spark member
     drifting away from the plan it was meant to satisfy;
   - the totals in §2 and the per-type counts in §3 disagree with the row counts. Arithmetic
     rot is the most likely failure of a document like this one and the cheapest to catch.

**`Spark.Docs.Verify` is the right host and already has the right shape.** Its `.csproj`
deliberately references no Spark project; it inspects the repository as files and, from M1,
loads the real assemblies from disk the way a user's code block will. Reflecting over
`Spark.Geometry.dll` from the publish directory is exactly that pattern and needs no new
project reference — which matters, because a test project that referenced the assembly it
polices would constrain the thing it is meant to observe.

### What this cannot check, and must not be claimed to

**Whether a member does the same thing.** The test can prove `Spark.Geometry.Arc.ByFillet`
exists when the manifest says `Done`. It cannot prove it produces Dynamo's fillet, and per
ADR-0016 nothing can, because proving it would require the dependency Spark exists to remove.
`Done` in this register means *present and documented*, never *equivalent*. That distinction is
the whole of §1 and it must survive into the test's failure messages, or the first person to
read a green run will draw the wrong conclusion from it.

**Nor is a green test a review.** This project already has its own evidence: the kernel's first
slice passed all three gates and was rejected on review with three of its eight claims false.
The manifest makes drift visible; it does not make judgement unnecessary.

---

## Related documents

- [PRD.md](PRD.md) — FR-47 … FR-60 (geometry), §11 release plan, §12 risks, §14 open questions
- [EPICS.md](EPICS.md) — [E2, geometry kernel](EPICS.md#e2--geometry-kernel)
- [TASKS.md](TASKS.md) — E2-T40 … E2-T48, E11-T23
- [TODO.md](TODO.md) — Q11 and Q12 under *Decisions waiting on someone*
- [ADR-0002](adr/0002-own-managed-geometry-kernel.md) — own pure-managed kernel, staged
- [ADR-0003](adr/0003-ibrepkernel-seams-operations.md) — `IBrepKernel` seams operations
- [ADR-0004](adr/0004-idiomatic-core-plus-by-facade.md) — idiomatic core plus `By*` façade
- [ADR-0010](adr/0010-explicit-scale-aware-tolerance.md) — tolerance is passed, never ambient
- [ADR-0011](adr/0011-angle-struct-in-public-signatures.md) — `Angle` in every angular signature
- [ADR-0016](adr/0016-no-dynamo-interoperability.md) — no `.dyn` interoperability, either direction
- [ADR-0019](adr/0019-deliberate-public-api-change-control.md) — deliberate public API change control
