# Spark — Dynamo capability coverage

The register behind the client's instruction: *"Make sure we have all geometry elements and
methods and properties what is there in Dynamo."* It exists to turn that sentence into
something checkable.

**Last updated:** 2026-09-15 (D30 answers Q12: T-Splines is out; 454 of 545, and 29 undecided rows where there were 198)
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
| **Reachable in Spark today** | **35** | **454** | **54.2%** |
| Deliberately not replicated (§5) | 7 + parts of 4 | 123 | 14.7% |
| Excluded by **D30** — T-Splines (§6.2) | 8 | 169 | 20.2% |
| **Committed and still to build** | — | **91** | **10.9%** |

Against the scope we have actually committed to — 837 less the 123 we refuse and the 169 that
§6.2 excludes, so **545 members** — Spark stands at **454 of 545, or 83.3%**. *The T-Splines row
read **awaiting a decision** until 2026-09-15, when [D30](PRD.md#13-decision-log) took the one §6.2
recommended. **The 545 is unchanged by it**, because those 169 were already outside the
denominator; what changed is that they now carry a position instead of a question, and the
manifest's `Needs a decision` count fell from 198 to **29**.* **Thirteen
ProtoGeometry types are answered in full**: `Plane`, `UV`, `Ellipse`, `EllipseArc`, `Rectangle`,
`IndexGroup`, `Topology`, `Vertex`, `Edge`, `Loop` and, all on 2026-09-13, **`Helix`** (`E2-T73`),
**`Line`** and **`Circle`** (`E2-T72`) — the first three this register asked for and then watched
get built.

> **These figures are counted, not estimated, and 2026-09-13 is the first day that is true of all of
> them.** Every row of the manifest now carries a status and a written reason; **none is
> `Unassessed`**. The table above previously read *99 reachable, 11.8%* — a hand count made in prose
> before the curve, surface, solid, topology and mesh layers existed — and the seven member-by-member
> passes that replaced it each found Spark **further ahead than the register claimed**, without
> exception, including the one section (§3.1) where the expectation was the opposite. The register's
> systematic error was in one direction, and [N158](NOTES.md) and [N160](NOTES.md) are why: a
> register that reads one assembly, or reads its own prose, is wrong **safely** — it under-claims,
> and nobody chases an under-claim.

> **The manifest counts these now, and where it differs from this table the manifest is right**
> (2026-09-11, `E11-T23`). [`tests/corpus/dynamo-parity.tsv`](../tests/corpus/dynamo-parity.tsv) holds
> one row per member, generated from the same `ProtoGeometry.dll` metadata, and `Spark.Docs.Verify`
> checks it against this document on every build. **As of 2026-09-13 it stood at `Done` 441,
> `Planned` 75, `Not planned` 123, `Needs a decision` 198 and `Unassessed` 0**, which is 837 — and
> the table above is now taken from those numbers rather than checked against them. **On 2026-09-15
> it stands at `Done` 454, `Planned` 62, `Not planned` 292 and `Needs a decision` 29**, the same
> 837: thirteen rows built since, and [D30](PRD.md#13-decision-log) moving the 169 T-Splines from
> undecided to refused. **Both lines are dated on purpose** — the manifest is the count and these
> are readings of it, so a reading is stamped rather than edited.
> Applying §5's rules gives **123** refused members, not 93 — §5's own lists add to 104, the four
> primitive solids carry 14 parameter-recovery properties rather than 11, §5 [i]'s three flattened
> mesh accessors were counted when `E2-T45` assessed them, `E2-T46` added three under §5 [j],
> `E2-T41` counted §5 [i]'s duplicated trim family once its survivor was chosen, and `E2-T40` added
> `Vector.IsAlmostEqualTo` to §5 [j] and `CoordinateSystem.ByOriginVectors`'s explicit-Z overload to
> §3.1's ground. **That read *of the 198 `Needs a decision`, 169 are the T-Splines and 29 are not*
> until [D30](PRD.md#13-decision-log) decided the 169 on 2026-09-15; there are 29 now**: `Q17`'s six
> oriented-box rows, §6.3's undeducible signatures, and the rest.
> **All seven sections are now assessed member by member**: §3.1 (113 of 133), §3.2 (**141 of 187**,
> the largest), §3.3 (59 of 106), §3.4 (24 of 55), §3.5 (31 of 33), §3.6 (49 of 65) and §3.8 (24 of
> 89). §3.7's 169 T-Spline members are `Q12`'s decision and are not an assessment.
>
> **The move from 392 to 441 on 2026-09-13 and 2026-09-14 is the first this register has made by *building*
> rather than by *measuring*.** Every earlier change to the headline came from a member-by-member
> pass finding Spark further ahead than the prose claimed — seven passes, seven under-claims
> ([N163](NOTES.md)). `Helix`'s seven rows are `Done` because the type was written (`E2-T73`), which
> is what the register is *for*, and is the shape every remaining move has to have now that no row
> anywhere is `Unassessed`.

### What the 441 counts, exactly

A ProtoGeometry member counts as **reachable** when a Spark user can obtain the same result
today through a documented member of one of the **delivering assemblies** — `Spark.Geometry`,
`Spark.Api`, `Spark.Geometry.Io` and `Spark.Nodes.Core` (`E11-T30`, §7). It does **not** require the
same name or the same owning type: `Vector.Transform(cs)` is counted because
`CoordinateSystem.ToWorld` and `Transform.OfVector` between them do the job, and
`CoordinateSystem.Translate` is counted because `Transform.Translation` does.

**Composition counts, and it has a boundary** (`E2-T41` step B, 2026-09-13, where sixteen rows turned
on it). Composition means **the caller passes members' results to another member**:
`Line.ByTangency(curve, t)` is reachable because `Curve.PointAt` and `Curve.TangentAt` feed
`Line.FromStartPointDirectionLength`. It **stops** counting when the caller has to compute the
quantity the member exists to compute: `Arc.ByCenterPointStartPointEndPoint` is *not* reachable,
because getting there means working out the sweep angle from two radii, and the sweep angle is what
the constructor is for. Members that are pure native-session plumbing, or that operate on types
Spark does not have, are not reachable.

**Where the 441 sit.** Every subsystem except T-Splines — which is the sentence this section could
not say for the first year of its life, when it read *all 99 sit in one subsystem, because that is
the only subsystem that exists*. Values and frames is 113 of 133, curves 141 of 187, surfaces 59 of
106, topology 31 of 33, mesh 49 of 65, solids 24 of 55 and infrastructure 24 of 89. Thirteen types
are answered in full. What is **not** here is exact solid modelling without a provider (§6.1) and the
T-Spline paradigm (§6.2), and no count in this document should be read as implying otherwise.

### Why member counts are not fungible, and must not be read as effort

Two warnings, both of which matter for reading the table above honestly.

**A percentage of members is not a percentage of work, and 72% is the most misleading number in
this document.** A large share of the 441 are the easiest members in the inventory: arithmetic on
six-double structs, decided by algebra and verified by property tests. A large share of the rest are
`Done` because they name an `IBrepKernel` operation, which is *one line in this register and an
entire dependency* — `Solid.Difference` is one row of one table and is a multi-year research problem
if it is ever written rather than delegated (§6.1, ADR-0020). And the 104 still to build are not the
easy ones, because the easy ones are what got done. **Any schedule derived from 72% is wrong by an
order of magnitude**, in the same way any schedule derived from the old 17% was.

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

### 3.1 Values and frames — 6 types, 133 members, 113 reachable

**Assessed member by member on 2026-09-13** (`E2-T40`), and it is the **last** section of §3 to be:
with it, **no row anywhere in the manifest is `Unassessed`**.

| Dynamo type | Members | Reachable | Was claimed | Spark equivalent | Status | Milestone |
|---|---:|---:|---:|---|---|---|
| `Point` | 15 | 14 | 13 | `Point3d` | Partial | M1 ✓ |
| `Vector` | 31 | 30 | 29 | `Vector3d` | Partial | M1 ✓ |
| `UV` | 6 | 6 | 6 | `UV` | **Complete** | M1 ✓ |
| `Plane` | 16 | 16 | 16 | `Plane` | **Complete** | M1 ✓ |
| `CoordinateSystem` | 46 | 33 | 27 | `CoordinateSystem` + `Transform` | Partial | M1 ✓ |
| `BoundingBox` | 19 | 14 | 8 | `BoundingBox` | Partial | M1 ✓ |

> **The 99 was 14 low, and this is the section where that matters most.** Every other section in §3
> started from *0 reachable* and the assessment could only move it up. This one started from a number
> **somebody counted by hand, in prose, a year ago** — the number the whole document's headline was
> built on — so the pass was a *check* rather than a first count, and the expectation going in was
> that it would find **over**-claims. It found the opposite, for the seventh section running.
>
> **Where the 14 went.** Six are `CoordinateSystem`, and they are the transformation family: the
> section's own prose said `Rotate`, `Mirror`, `Translate`, `PreMultiplyBy`, `PostMultiplyBy` and
> `Determinant` were the *shape difference* that made Spark split the type in two, and then counted
> the split as a gap — but a capability delivered on `Transform` is still delivered, which is
> `E11-T30`'s finding ([N158](NOTES.md)) reaching the one section that predates it. Six more are
> `BoundingBox`, where `MinPoint`, `MaxPoint`, `ByCorners`, `ByGeometry` ×2 and `IsEmpty` were never
> counted at all. The last two are `ByCylindricalCoordinates` and `BySphericalCoordinates` on
> `CoordinateSystem`, which the prose below still calls *planned* — the factories landed on
> 2026-09-10 and nobody re-read the sentence.

**What the 113 leaves.** One `Planned` (`Point.Project`, which needs `E2-T15`'s ray caster and is the
same gap as `Curve.Project`), **14 refused** and **5 `Needs a decision` — and all five are one
question**, which is now `Q17`: whether Spark wants an `OrientedBox` type. `Done` in the table above
means the type exists and is reviewed; the *Reachable* column is the count from the manifest, and
*Was claimed* is what this section said before anybody counted.

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
axis length means. The other seven were described here as *planned* and **six of the seven are
`Done`**, which the member-by-member pass found on 2026-09-13: `ByMatrix` is the `Transform`
constructor, which takes its sixteen numbers one at a time so a caller cannot pass fifteen;
`ByCylindricalCoordinates` and `BySphericalCoordinates` have been reachable since the `FromCylindrical`
and `FromSpherical` factories landed on 2026-09-10, and are reachable **under either reading** of a
signature that does not say whether the returned frame is merely *positioned* at the point or also
*oriented* to its directions, because Spark has both; and `FromJson`/`ToJson` are
`GeometryJson.Deserialize` and `Serialize`, **one entry point for every geometry type** rather than a
pair per type (FR-57) — which is why one member answers the ten `FromJson`/`ToJson` rows in this
document. Only `ByOriginVectors` with an explicit Z axis is refused, and it is refused for the same
reason as the scale family: Spark **derives** Z, and an independent Z is how a caller gets a
left-handed or non-orthogonal frame by accident. `CoordinateSystem.FromOriginZAxis` is there for
somebody who has a Z and wants the frame built around it.

**`BoundingBox` was called the weakest row here and it is not, and the reason it read that way is
worth keeping.** It said eleven of nineteen were uncovered because nine of them *need geometry that
does not exist* — and that sentence was written when there were no curves, surfaces, solids or
meshes. There are now: `Curve`, `Surface`, `Brep`, `Mesh` and `PointCloud` each carry a
`BoundingBox`, so `ByGeometry` and its sequence form are `Done`, `ToCuboid` and `ToPolySurface` are
both `BrepPrimitives.Box` — **the same member for both, because Spark has one `Brep` and open or
closed is a property of it rather than a second type** — and `FromJson`/`ToJson` are
`GeometryJson`'s. **`BoundingBox` is 14 of 19.** Applying [N161](NOTES.md) to `ByGeometry`, the
enumeration of Spark geometry that *cannot* answer it is a single entry:
`Spark.Geometry.Planar.Region`, which is a planar set rather than a shape. Of the three members this
section called real gaps, one was closed and the other two are `Q17`:

- **`Intersection(BoundingBox)`** — **added 2026-08-29** (`E2-T40`). It was described here as
  four lines and it is; what it is *not* is four lines of its own arithmetic. Each axis is
  delegated to `Interval.Intersection`, which is what makes it impossible for a box and its
  three intervals to disagree about a boundary case — and boundary cases are the only place
  this member is interesting. `Interval.Intersect` was renamed to `Intersection` in the same
  change, so the two set operations on both types are now nouns: `Union` and `Intersection`.
- **`ContextCoordinateSystem` and `ByCornersCoordinateSystem`** — Dynamo's box can be
  expressed in an arbitrary frame, so it is an oriented box in disguise. Spark's
  `BoundingBox` is strictly world-axis-aligned. **Needs a decision**, and the honest question
  is not whether to add a frame to `BoundingBox` but whether Spark wants a separate
  `OrientedBox` type. `Geometry.OrientedBoundingBox` and `BoundingBox.ByMinimumVolume` push
  the same way. **Filed as `Q17` on 2026-09-13**, because it had been *recorded as an open question*
  in prose for a year without being anywhere a decision gets taken. **Six rows turn on it** — these
  two, `ByGeometryCoordinateSystem` ×2, `ByMinimumVolume` and §3.8's
  `Geometry.OrientedBoundingBox` — and they are the **only** five `Needs a decision` rows left in
  this section. `ByMinimumVolume` is the hardest of them and cannot even be stated until `Q17` is
  answered: a minimum-volume box is not a bounding box in a given frame, it is a **search** for the
  frame.

**`Point` and `Vector` are nearly complete.** `ByCylindricalCoordinates` and
`BySphericalCoordinates` were **added 2026-09-10** (`E2-T40`) as `FromCylindrical` and
`FromSpherical` — Spark names factories `From…`, so the Dynamo name is the *mapping* rather than
the member. They are on **both** `Point3d` and `Vector3d`, and each is also a constructor, because
`E2-T59` says every factory is, **and all four are nodes** carrying the Dynamo names as aliases —
because a member of `Spark.Geometry` is reachable from a code block and nowhere else, and FR-81's
promise is about the person at the library panel. The one decision in four arithmetic members is the
spherical convention: the second angle is the **polar** angle from `+Z`, not an elevation from the XY plane,
and the two differ by a quarter turn with nothing in a result to say which was meant. It is stated
on every one of the four and pinned by a named test. What remains on `Point` is
`Point.PruneDuplicates` (planned, and it wants the `KDTree` of E2-T16 rather than an O(n²) loop)
and `Point.Project` onto geometry (planned, M5 — it needs surfaces). Nothing here is a design
difference.

**`Vector`'s row was not moved, and the reason is a discrepancy this document cannot settle on its
own.** The table gives `Vector` 29 of 31, so **two** uncovered; the paragraph above it used to name
**four** — `ByCylindricalCoordinates`, `BySphericalCoordinates`, `FromJson` and `ToJson` — and both
cannot be true. Spark now has the first two either way, so the honest answer depends on whether
ProtoGeometry's `Vector` declares them at all, and **the inventory those counts came from is not in
this repository**; every other number here is a count rather than an estimate, and guessing this one
to make a table tidy would be the first exception. `Vector` therefore stays at 29/31 and the headline
moves by two rather than four. Settling it needs somebody with `ProtoGeometry.dll` in front of them,
and §7's *check the inventory in* row is now doing real work rather than hypothetical.

**`Plane` had four gaps and has two.** `FromOriginNormalXAxis` and `Offset(distance)` were
**added 2026-08-29** (`E2-T40`) — the first because it is the only factory that pins the
in-plane rotation without a second point, the second because a parallel plane at a distance is
asked for constantly and there is nothing to decide about it. Neither turned out to be quite
trivial: `FromOriginNormalXAxis` projects the requested X axis into the plane rather than
demanding one that already lies in it, and `Offset` refuses a non-finite distance rather than
producing a plane whose origin is `NaN`, which would be the only way to obtain an invalid
`Plane` from a factory. **`ByBestFitThroughPoints` and `ByLineAndPoint` were added 2026-09-10**
(`E2-T40`) as `FromBestFit` and `FromLineAndPoint`, and `Plane` is now **16 of 16 — the first
ProtoGeometry type Spark covers completely**. The fit was written **once**, as an internal
`LeastSquares` helper rather than inside `Plane`, because this section already said
`Circle.ByBestFitThroughPoints` and `Line.ByBestFitThroughPoints` want the same arithmetic and
three copies would disagree about a degenerate input first. It is closed-form — the covariance
matrix's smallest eigenvector via its adjugate — so there is no iteration, no convergence
tolerance and no eigensolver; the only threshold is the relative one that decides a set of points
is really a line, and near-collinear input is **refused rather than fitted**, because a normal
decided by rounding error is worse than a message. The normal's sign follows the winding of the
points, matching what `FromThreePoints` promises for three.

**Two Dynamo members on these types have no Spark equivalent and should not get one.**
`Point.ByCartesianCoordinates(cs, x, y, z)` is counted as reachable through
`CoordinateSystem.ToWorld`, and that is the right shape: construction and frame-mapping are
separate concerns and Spark keeps them separate. Similarly `Vector.Scale(x, y, z)` is reachable
through `Transform.Scale(x, y, z).OfVector(v)`; a non-uniform scale is a transformation, and
putting it on the vector implies a frame the vector does not carry.

### 3.2 Curves — 11 types, 187 members, 141 reachable

**All 187 rows were assessed member by member on 2026-09-13** (`E2-T41`, in two steps: `Curve`'s 82,
then the ten concrete types' 105), against the delivering assemblies rather than by eye
([N160](NOTES.md)). Every *Reachable* figure below is counted from the manifest. **`Helix` was then
built the same day** (`E2-T73`), which is why the figure is 118 rather than the 111 the assessment
found.

| Dynamo type | Members | Reachable | Spark equivalent | Status | Milestone |
|---|---:|---:|---|---|---|
| `Curve` (base) | 82 | 52 | `Curve` — the contract, settled against this section | Partial | M1 ✓, M3 |
| `Line` | 6 | 6 | `Line` | **Complete** | M1 ✓ |
| `Arc` | 14 | 12 | `Arc` | **Partial** | M1 ✓ |
| `Circle` | 8 | 8 | `Circle` | **Complete** | M1 ✓ |
| `Ellipse` | 8 | 8 | `EllipseCurve` | **Complete** | M1 ✓ |
| `EllipseArc` | 9 | 9 | `EllipseCurve` over a sub-domain | **Complete** | M1 ✓ |
| `Helix` | 7 | 7 | `Helix` | **Complete** | M1 ✓ |
| `NurbsCurve` | 15 | 14 | `NurbsCurve` | **Partial** | M3 ✓ |
| `PolyCurve` | 21 | 8 | `PolyCurve` | **Partial** | M1 ✓ |
| `Polygon` | 9 | 9 | `PolyLine`, closed | **Partial** | M1 ✓ |
| `Rectangle` | 8 | 8 | A `PolyLine` factory, not a type | **Complete** | M1 ✓ |

**141 of 187**, as `Done` 141, `Planned` 20, `Needs a decision` 18, `Not planned` 8. **`Line` and
`Circle` joined the complete types on 2026-09-13** when `E2-T72` gave them best-fit constructors,
which takes §3.2's complete count from four to six. `Curve` alone is
**45 of 82** — the forty the assessment found, plus `ToNurbsCurve`, which was *the finding of that
pass*, and `IsPlanar`, `Normal` and the two `Extrude` overloads that waited on it, all built the same
day (`E2-T71`) — and it had five `Done` rows before this pass with none of the other 77 ever looked at —
so the reading that mattered, *is the largest type in the inventory mostly a gap*, had never been
tested. It is not: **the contract this section was written to shape now answers half of the type it
was written against.** **Four** of the ten concrete types are **complete**, and two of those four
are the same Spark type answering two Dynamo ones.

**The two decisions this section had been carrying are made, and one of them is already built.**
**`Helix` went in** (D27) and **was written the same day** (`E2-T73`): it was `Needs a decision`
because it is absent from FR-48 and nobody had decided against it, and it is the cheapest type the
curve hierarchy can gain — a helix travels at a **constant speed**, so `LengthAt`,
`ParameterAtLength`, `PointAtLength`, `DivideEqually` and `DivideByLength` are all closed form and
exactly right, which was true of only five of the seven types Spark had before it. It needs no
kernel and no provider, and AEC users draw it: stairs, ramps, threads, spiral ducts. All seven rows
are now `Done`, with two names that do not match Dynamo's and the difference recorded rather than
smoothed over: Spark's total sweep is `SweepAngle`, as it already is on `Arc`, and `AxisPoint`
reports the point on the axis **level with the start** rather than the origin the caller passed,
because only the axis *line* is in the curve. **`Rectangle`
and `Polygon` stay factories on `PolyLine`** rather than becoming types, which is what this section
argued and what Spark built; the rows now record it as built rather than leaving it as an argument.
The price is visible and worth seeing: `Rectangle.Width` and `Height` are `Done` as
`PolyLine.SegmentAt(0).Length` and `SegmentAt(1).Length`, because a rectangle that is a closed
polyline has no width property. That is the trade — no public type to version for ever, and two
parameter-recovery members that are a segment length instead.

**A rule this section had to state, because it decided sixteen rows.** Composition counts, and §2
says so — but **composition means the caller passes members' results to another member, and it stops
counting when the caller has to compute the quantity the member exists to compute.**
`Line.ByTangency(curve, t)` is `Done` because `Curve.PointAt` and `Curve.TangentAt` feed
`Line.FromStartPointDirectionLength`. `Arc.ByCenterPointStartPointEndPoint` is `Planned` because
getting there means working out the sweep angle from two radii, and the sweep angle is what the
constructor is for.

**What was built from this section, and what was not.** The four parameterisations are now
two: **by parameter and by length**, both present on the contract, and the pass narrowed §6.3's
question about them — **the `AtDistance` family *is* arc length and is `Done` by composition
through `ParameterAtLength`; what remains undeducible is what `AtSegmentLength` measures**, which
is why all six segment-length members are `Needs a decision` rather than guesses. Of the trimming
and splitting family, `Trimmed` turns out to answer **all nine** surviving members, seven of them
by composition. The modelling members are **`Done` behind `IBrepKernel`** — `Extrude`,
`ExtrudeAsSolid`, `SweepAsSolid`, `SweepAsSurface` and `Patch` all name a kernel operation that has
worked since M6, which is what `E11-T30` made the register able to say. Of the offset, projection
and pull group, `Offset` is `Done` through `CurveOffset.Offset` and the other six are not built.

**The one prediction in this section that was tested, and held.** It said a Spark contract
offering only *by parameter* would feel missing to every Dynamo user, because *divide a curve
into equal lengths* is the single most common thing anyone does to a curve — and that building
it in at M1 was cheap while retrofitting it was not. It was cheap: arc-length division is
analytic on five of the six types and a Gauss–Legendre integral with a Newton inverse on the
sixth. It also turned out to be the only part of the curve layer whose tests can distinguish a
right answer from a plausible one, because every curve except the ellipse travels at a constant
speed and would pass a division test that was quietly wrong.

**`Curve` alone is 82 members — the largest non-T-Spline type in the inventory, and larger
than every Spark value type put together bar `Transform`.** FR-48 names fifteen members for
the curve contract. That is not wrong, but it is not parity either, and the gap is where this
section earns its keep. The 82 break down roughly as follows.

*Evaluation and frames — 24 members, 13 reachable.* `PointAt*`, `TangentAtParameter`,
`NormalAt*`, `PlaneAt*`, `CoordinateSystemAt*`, `HorizontalFrameAtParameter` and the four
length ↔ parameter conversions. **The distinct finding stood and was the reason the contract has
an `AtLength` family at all: Dynamo exposes four parameterisations of the same query** — by
parameter, by distance, by segment length and by chord length. Spark ships two of the four, and
the two it ships cover **every `AtParameter` and every `AtDistance` member**, the latter by
composing `ParameterAtLength` with the parameter form. The other eleven are the **six
segment-length members and two chord-length members** (`Needs a decision`, §6.3 — the measure is
not deducible), `NormalAtParameter(param, side)` (§6.3 — what `side` selects),
`HorizontalFrameAtParameter` (§6.3 — the tie-break at a vertical tangent *is* the specification)
and `IsPlanar` with `Normal`, which are a pair: **a curve-wide normal presumes planarity, and
`Curve.IsPlanar` does not exist**. Three of Spark's seven curve types can answer it today —
`Arc.Plane`, `Circle.Plane`, `EllipseCurve.Plane` — and `PolyLine`, `PolyCurve`, `NurbsCurve` and
a `Curve`-typed value cannot ([N161](NOTES.md)).

*Division and sampling — 8 members, 1 reachable.* `DivideEqually` is `Done` and is the member
this section argued for. `DivideByDistance(Int32 divisions)` is `Needs a decision` for the reason
§6.3 gives — *the name says distance and the parameter says count*, and Spark has a member for
each reading, so picking one would be inventing the answer. Two `FromParameter` forms and
`PointsAtEqualChordLength` and `PointsAtChordLengthFromPoint` are `Planned`: **Spark has no chord
stepping at all**. The two segment-length forms are `Needs a decision` with the rest of that family.

*Trimming and splitting — 17 members, 9 reachable and 8 refused.* **§5 [i]'s duplicated family had
its survivor chosen on 2026-09-13, and this is where the choice was made**: the `Parameter*` set
survives, the `TrimBy*`/`SplitBy*` set is refused as Dynamo's own backwards compatibility, and
those eight members are now **counted** in §5 rather than left uncounted. Of the nine that
survive, **all nine are `Done` and seven of them by composition** — `Curve.Trimmed(in Interval)`
is abstract on the base, so a split is `Trimmed` twice, an interior trim is `Trimmed` twice, and
`SplitByPoints` is `ClosestParameter` and then `Trimmed`. `E2-T7` recorded *no `Split`* as a
deliberate M1 exclusion and that is still true of the **member**; the **result** has been reachable
since the contract was written, and this register's subject is the result.

*Modelling — 9 members, 6 reachable.* `Extrude` ×3, `ExtrudeAsSolid` ×3, `SweepAsSolid` ×2,
`SweepAsSurface`, `Patch`. They sit behind `IBrepKernel` (ADR-0003) and have worked since M6:
`IBrepKernel.Extrude`, `Sweep` and `Patch` take the profile as an argument where Dynamo takes it as
the receiver. **The two direction-taking `Extrude` overloads are `Spark.Geometry.ExtrusionSurface`,
a first-class analytic extrusion rather than a NURBS approximation** (FR-49), which is the same
reading that made `Surface.ByRevolve` name `RevolutionSurface`. The three that are not reachable
are the two `(double)` overloads — they extrude along the **curve's own normal** and so wait on
`IsPlanar`, not on the kernel — and `SweepAsSolid(Curve, bool)`, whose flag cannot be matched to
`IBrepKernel.Sweep`'s, which is `cap` and is already true for a solid sweep.

*Offset, projection and pull — 7 members, 1 reachable.* `Offset` is `Done` through
`CurveOffset.Offset`, which is exact where a closed form exists and sampled otherwise and **says
which in its result** — a fact Dynamo's signature cannot carry. It asks for the plane normal where
Dynamo infers it, because inferring it means deciding the curve is planar. `OffsetMany`, `Project`,
`PullOntoPlane`, `PullOntoSurface` and `Simplify` are `Planned`;
`ApproximateWithArcAndLineSegments` is `Needs a decision` and **is deliberately not refused under
§5 [j]**, though its signature qualifies: [j] could refuse `Geometry.Approximate()`'s missing
tolerance because the capability had a tolerance-taking home in Spark, and a biarc approximation
has no home at all. Refusing it would file a real gap under a rule about a parameter list.

**`ToNurbsCurve()` is the finding of the pass.** Spark has **no curve-to-NURBS conversion
anywhere** — not on the base, not on a type, not as an extension — while
`Spark.Geometry.SurfaceConversion` converts five of the eight analytic **surfaces** exactly.
`Line`, `Arc`, `Circle`, `EllipseCurve` and `PolyLine` all have exact NURBS forms and none of them
is written, so the half of the kernel that was built first is the half missing the conversion.
`E2-T71`.

**`ToString()` is `Done`, and `Surface.ToString()` is `Planned`, and the difference is [N161](NOTES.md)
working.** The rule is to enumerate the types that do *not* carry a member rather than to accept
*some do*. For curves the enumeration is empty — `Line`, `Arc`, `Circle`, `EllipseCurve`,
`NurbsCurve`, `PolyLine` and `PolyCurve` all override it, so a `Curve`-typed value prints itself.
For surfaces only `NurbsSurface` does. Two rows that look identical, opposite answers, and only the
enumeration tells them apart.

**What the ten concrete types came to, and the three findings in them.**

*Constructors are where the parity is, and Spark's hold up.* `Circle` is 7 of 8, `Ellipse` 8 of 8,
`EllipseArc` 9 of 9 and `Rectangle` 8 of 8, almost entirely through factories Spark already had —
`Circle.FromCenterNormalRadius`, `EllipseCurve.FromPlaneRadiiAngles`, `PolyLine.FromRectangle` and
their siblings — with `Plane.FromOriginNormal`, `Plane.FromOriginXAxisYAxis` and
`CoordinateSystem.ToPlane` bridging the frames. **`EllipseCurve` answers two Dynamo types on its
own**, because an ellipse over a sub-domain is not a second type.

*The first finding: **there is no curve fitting of any kind**.* `Line`, `Arc` and `Circle` each carry
a `ByBestFitThroughPoints` and none of the three is written, while `Plane.FromBestFit` fits a plane
and `NurbsCurve.FitPoints` fits a spline. The machinery and the taste for it are both already there;
the three closed-form fits are simply absent. `E2-T72`.

*The second: **Spark's only fillet takes two lines**.* `CurveOffset.FilletLines(Line, Line, radius)`
is the whole of it. `Arc.ByFillet` takes two arbitrary curves, `Arc.ByFilletTangentToCurve` a third,
`PolyCurve.Fillet` fillets every corner of a chain at once, and `PolyCurve.CloseWithLineAndTangentArcs`
needs the same construction. Four rows, one missing generalisation — and claiming any of them on
`FilletLines` would be [N156](NOTES.md)'s trap, which is why the row says what it takes.

*The third: **`NurbsCurve.IsPeriodic` is [N156](NOTES.md)'s trap for the third time**, after
`Surface.IsClosedU`/`IsPeriodicInU` and `Face.SurfaceGeometry()`.* `NurbsCurve.IsClosed` is not it:
closed is geometric, the two ends meeting in space; periodic is a property of the **knot vector** and
of the control net wrapping smoothly. Spark's `KnotVector` is clamped — `CreateClamped` is its only
factory and `IsClamped` is a property — so **three further rows wait on the same thing**:
`ByControlPoints(points, degree, periodic)`, `ByPoints(points, periodic)` and this one. A fourth,
`ByPointsTangents`, is the *same* algorithm `§3.3` found missing on `NurbsSurface`, where three of
the five gaps are interpolation with tangent constraints — so it is one algorithm short on both
sides of the kernel rather than two.

*`PolyCurve` is the weakest of the ten, at 7 of 21*, and it splits cleanly: **extension** (`ExtendWithArc`,
`ExtendWithEllipse`) goes with `Curve.Extend`, because nothing in Spark lengthens a curve past its own
domain; **thickening and grouping** are constructions Spark has no form of; and three rows are §6.3's
undeducible flags — `Heal(trimLength)`, `CurveAtIndex(index, endOrStart)` and the four-argument
`ByJoinedCurves`. `PolyCurve.FromJoinedCurves` makes **one** polycurve and **refuses** a chain that
does not meet, naming the index and the gap; `ByGroupedCurves` makes **several** and is the opposite
behaviour rather than an overload.

*`Polygon.SelfIntersections()` is `E2-T70`'s finding in a second place.* `Curve.IntersectWith` takes
**another** curve, so a curve cannot be asked about itself — the same shape as §3.8's *every query
takes a point, never another geometry*. Region construction rejects a self-intersecting loop and does
not report where.

**`Helix` was `Needs a decision` and is now `Planned`** (D27). It was absent from FR-48 and nobody had
decided against it, and the section said plainly that either it goes into FR-48 or its absence is
recorded as deliberate, because leaving it unstated is how a gap becomes a surprise at M3. It goes in:
seven members, a clean analytic form, a constant speed that makes every arc-length member closed form,
and no kernel dependency. `E2-T73`.

**`Rectangle` and `Polygon` are types in Dynamo and are factories in Spark, and that is now
recorded as built rather than argued.** A `Rectangle` that is a subclass of `Polygon` which is a
subclass of `PolyCurve` gains nothing over a closed `PolyLine` built by
`PolyLine.FromRectangle(plane, width, length)`, and it costs a public type that must be serialised,
versioned, documented and node-ified forever. **Five of the six capabilities that only live on those
types are reachable today**, and the prediction that they would live *on `PolyLine` or in
`Spark.Geometry.Planar`* held exactly: `Polygon.Corners` and `Points` are `PolyLine.Points`,
`RegularPolygon` is `PolyLine.FromRegularPolygon`, **`ContainmentTest` is
`Spark.Geometry.Planar.Region.Contains`** and **`Center` is `Region.Centroid`** — which is also why
`Region` came off the exclusions list on 2026-09-13 and its other twelve members are counted from
then. `PlaneDeviation` is `Plane.FromBestFit` and `Plane.DistanceTo` composed. Only
`SelfIntersections` is missing.

**`Polygon.Center()` is reachable under either reading of a word Dynamo does not define**, which is
worth recording because it is the rare case where the ambiguity does not matter. If *center* is the
area centroid it is `Region.Centroid()`; if it is the mean of the corners it is the average of
`PolyLine.Points()`. Spark has both, so the register does not have to resolve which Dynamo means —
and saying which it is would be a guess.

### 3.3 Surfaces — 5 types, 106 members, 59 reachable

**Assessed member by member on 2026-09-12** (`E2-T42`), against the manifest rather than by eye. All
81 rows that are not `PanelSurface`'s carry a status and a reason for it.

| Dynamo type | Members | Reachable | Spark equivalent | Status | Milestone |
|---|---:|---:|---|---|---|
| `Surface` (base) | 46 | 36 | `Surface`, `Spark.Api.IBrepKernel` | Partial | M5 |
| `NurbsSurface` | 17 | 12 | `NurbsSurface`, `KnotVector` | Partial | M5 |
| `PolySurface` | 18 | 10 | `Brep` (open shell), `Spark.Api.IBrepKernel` | Partial | M6 |
| `PanelSurface` | 21 | 0 | None — see §5 [d] | Not planned | — |
| `PanelSurfaceBoundaryCondition` | 4 | 0 | None — see §5 [d] | Not planned | — |

**`NurbsSurface` is 12 of 17 and the five that are missing split cleanly in two.** Everything that
*reads* a NURBS surface is there — degrees, rationality, the control points and weights whole or one
at a time, both control-point counts, and both knot vectors. Spark hands back a `KnotVector` where
Dynamo hands back a `double[]`, because a knot vector has invariants a bare array cannot hold, and
`KnotVector.ToArray()` gives Dynamo's shape when a caller wants it. **Three of the missing five are
interpolation** — `ByPoints` and its two tangent-constrained forms — which is a real algorithm and
not the control-point constructor wearing a different name. **The other two are `IsPeriodicInU` and
`IsPeriodicInV`, and they are the trap [N156](NOTES.md) exists to stop.** They look like
`Surface.IsClosedU` and `IsClosedV` and they are not: closed is geometric, the two ends meeting in
space; periodic is a property of the knot vector and of the control net wrapping smoothly. A closed
surface with a seam is not periodic. They are `E2-T66`, not `Done`.

**`PolySurface` is 6 of 18, and one of the six is `Done` because Spark needs no member for it.**
`BySolid` converts a `Solid` to a `PolySurface`; Spark has **one** `Brep` and asks `IsSolid`, so
there is nothing to convert — §3.4 records that Dynamo splits the two types by closure, which is
tolerance-dependent, so under that scheme a healing operation can change an object's type.
`UnconnectedBoundaries()` became reachable **on the day it was assessed**: a naked edge is an edge
used by exactly one trim, and until `E2-T65` built `BrepAdjacency` an edge did not know its trims.

**Eight of `PolySurface`'s twelve gaps are `E11-T30`'s seam again** — loft, sweep, fillet and
chamfer, which `--graph solids` has been performing since M6. `Surfaces()` is `E2-T64`'s pcurves.
`ExtractSolids()` is the shell-level question `E13-T18` asks from the other side: `Brep.Shells()`
gives the shells, but `Brep.IsSolid` asks about the whole model, so a `Brep` of two shells cannot say
which of them is closed.

**The evaluation family is complete, and this document said otherwise.** `E2-T42`'s row claimed the
curvature family — Gaussian, principal values, principal directions — was absent from `E2-T17`'s
contract and was *precisely what a facade or structural graph reaches for*. Two thirds of that is
wrong: `Surface.GaussianCurvature`, `MeanCurvature` and `PrincipalCurvatures` are all on the abstract
base, and the first two are derived from the third. **Only the principal *directions* are missing**,
and `PrincipalCurvatures` already forms the shape operator and discards the eigenvectors it would
return (`E2-T66`). Everything else a graph evaluates — point, normal, both partials, the frame, the
closest point with its `uv`, the isocurves, closure in each direction, area and the untrimmed
perimeter — is there, on the base, so every analytic surface and `NurbsSurface` answer it.

**Twelve rows changed from `Planned` to `Done` on 2026-09-12 without a line of geometry being
written** (`E11-T30`). Loft, sweep, patch, the booleans, thicken, fillet, chamfer and heal live behind
`Spark.Api.IBrepKernel`, and the parity check used to read `Spark.Geometry.dll` alone — so the
register said *Planned* about capabilities `--graph solids` had been performing since M6. §1 and
FR-81 both make the register's subject **capability**, so the rename-catcher now reads `Spark.Api` and
`Spark.Nodes.Core` beside `Spark.Geometry` and each row names the member that actually does the work.
**`Surface.Repair()` is the clearest case**: it stood at `Not planned` with an argument that was
entirely true — healing is behind the seam by decision, because OCCT's `ShapeFix` does it and a second
managed implementation would be worse (`E13-T10`) — and the conclusion drawn from it was wrong.
*Behind the seam* is still *delivered*. It is `IBrepKernel.Heal`.

**Eight rows stayed `Planned`, and now for reasons about the operation rather than the assembly.**
`IBrepKernel.Loft` takes profiles and a closed flag and **no guide**, so the guided lofts are a
different construction; `Sweep` takes **one** rail, so `BySweep2Rails` is not an overload; `Sweep`'s
own `bool` is **cap**, where Dynamo's is *keep profile orientation* — two booleans meaning different
things is exactly the near-match this register exists to refuse; `Thicken` has no both-sides flag; and
`Offset`, `Join` and `ByJoinedSurfaces` all want a `Surface` where the kernel takes a `Brep`, which
means becoming a face with a loop first. All are `E2-T66`.

**Two more are pcurves** (`TrimWithEdgeLoops`, `E2-T64`) — `IBrepKernel.Trim` cuts a `Brep` with tool
`Brep`s, which is a different operation from trimming a surface with parameter-space loops. **Three
need a decision** (`FlipNormalDirection`, because orientation lives on `BrepFace.IsReversed` and not
on the surface; `CurvatureAtParameter`, whose meaning a metadata-only read cannot settle;
`ByPerimeterPoints`, which is a fitting problem past four points), and **eleven are simply missing**
and are `E2-T66`.

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

### 3.4 Solids — 5 types, 55 members, 24 reachable

**Assessed member by member on 2026-09-12** (`E2-T43`), against `Spark.Api.IBrepKernel` as well as
`Spark.Geometry` — which `E11-T30` made possible the same day and which is most of why this section
is not the *0 reachable* it read as for a year.

| Dynamo type | Members | Reachable | Spark equivalent | Status | Milestone |
|---|---:|---:|---|---|---|
| `Solid` (base) | 24 | 16 | `Brep` (closed), `Spark.Api.IBrepKernel` | Partial | M6 + |
| `Cuboid` | 8 | 4 | `BrepPrimitives.Box`, a factory not a type | Partial | M6 |
| `Sphere` | 6 | 1 | None yet — no sphere primitive | Partial | M6 |
| `Cone` | 11 | 1 | None yet — no cone primitive | Partial | M6 |
| `Cylinder` | 6 | 2 | `BrepPrimitives.Cylinder`, a factory not a type | Partial | M6 |

**`Solid`'s operations are 16 of 24 and every one of them is on the kernel interface.** Union,
difference, loft, revolve, sweep, fillet, chamfer, `ThinShell` (`IBrepKernel.Shell`), `Separate`
(`Split`) and `Repair` (`Heal`) are all there, and `--graph solids` has been exercising most of them
since M6. The eight that are missing are the same shapes the surface section found: `Loft` has no
guide, `Sweep` takes one rail and its `bool` is *cap*, `ByRuledLoft` rules rather than interpolates,
`ByJoinedSurfaces` wants surfaces where the kernel takes `Brep`s, and `ProjectInputOnto` has no
member at all.

**`Volume` and `Area` are `Done` and both deserve an asterisk that the rows carry.** They are
measured on the *tessellation* — `Spark.Nodes.Core.Solid.Volume` meshes and sums, `Mesh.Area` does
the same — so they converge with the tolerance rather than equalling the exact figure. `Centroid()`
has no member at all. The three are one gap and one member: `E2-T67`, which asks the kernel once.

**The primitives are the interesting half.** `Cuboid` is 4 of 8 and `Cylinder` 2 of 6, through
`BrepPrimitives.Box` and `BrepPrimitives.Cylinder` — built from explicit topology rather than from an
intersection, so they need no kernel and are exact. **`Sphere` and `Cone` have no primitive at all**,
and that is a real gap rather than a shape difference: `SphericalSurface` and `ConicalSurface` are
*surfaces*, and closing one into a solid means a `Brep` with a seam and poles, which is exactly the
topology a primitive exists to get right once. `E2-T66`.

**The 14 refusals here are §5's, and they line up exactly.** `Cuboid.Length`, `Cylinder.Radius`,
`Cone.RadiusRatio` and the rest are parameter-recovery properties, and Spark's primitives are
factories rather than types — a `Brep` made by `BrepPrimitives.Box` is a `Brep`, and asking it for
the length it was built from is asking it to remember its own history, which the value model does not
do. Fourteen properties over four types, which is the count §5 was corrected to on 2026-09-11.

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

### 3.5 Topology — 6 types, 33 members, 31 reachable

**Assessed member by member on 2026-09-12** (`E2-T44` step A), against the manifest rather than by
eye, which is why this is the one §3 table whose *Reachable* column is measured — **and the 12 the
assessment found missing were built the same day** (`E2-T65`). Two are left, and both are pcurves.

| Dynamo type | Members | Reachable | Spark equivalent | Status | Milestone |
|---|---:|---:|---|---|---|
| `Topology` (base) | 4 | 4 | `Brep.Faces()` / `Edges()` / `Vertices()` | **Done** | M6 |
| `Vertex` | 4 | 4 | `BrepVertex`, `Brep.VertexPoint`, `BrepAdjacency` | **Done** | M6 |
| `Edge` | 6 | 6 | `BrepEdge`, `BrepEdgeView`, `BrepAdjacency` | **Done** | M6 |
| `CoEdge` | 10 | 9 | `BrepTrim`, `BrepLoopView`, `BrepAdjacency` | Partial | M6 |
| `Loop` | 4 | 4 | `BrepLoop`, `BrepLoopView`, `BrepAdjacency` | **Done** | M6 |
| `Face` | 5 | 4 | `BrepFace`, `BrepFaceView`, `BrepAdjacency` | Partial | M6 |

This is the cleanest subsystem in the inventory: 33 members, almost all of them navigation.
`Edge.AdjacentFaces`, `Vertex.AdjacentEdges`, `Loop.CoEdges`, `CoEdge.Next`/`Previous`/
`Partner`/`Reversed`, `Face.Loops`. Parity is achievable in full and should be.

**The 14 the assessment found missing were one gap and one gap only**, which is why 12 of them could
be closed in a single step. Spark's topology stores each relationship exactly once and in one
direction — a face names its loops, a loop names its trims, a trim names its edge, an edge names its
vertices — so every member that asks for the *other* direction had nothing to answer it:
`Vertex.AdjacentEdges`, `Vertex.AdjacentFaces`, `Edge.CoEdges`, `Loop.Face`, `CoEdge.Loop`,
`CoEdge.Partner`, and `Face.Edges` / `Face.Vertices` at one remove.

**`E2-T65` answered them without storing the reverse**, because storing it would be a second
description of the same fact that somebody has to keep in step through every edit, join and
deserialization — and a back-pointer array is how an index-based topology quietly becomes the object
graph `E2-T22` rejected. Instead **`BrepAdjacency`** computes the reverse in one pass over a `Brep`
and is **kept by the caller**, who is the only one in a position to decide how long it is worth
keeping: `BrepEdgeView.AdjacentFaces` still scans, because building an index for one call costs more
than the scan, and a test holds the two to the same answer on every edge of three fixtures.

**Four of the twelve needed no index at all.** `CoEdge.Next`, `Previous`, `StartVertex` and
`EndVertex` are `BrepLoopView.NextPosition`, `PreviousPosition`, `StartVertex` and `EndVertex` —
arithmetic, because a loop's trims are contiguous from `BrepLoop.FirstTrim`. Dynamo answers the same
questions with pointers between objects; here they cost nothing and are stored nowhere.

**The two that are left are pcurves**: `Face.SurfaceGeometry()` and `CoEdge.ParameterCurve`, which
are `E2-T64`.

**`Face.SurfaceGeometry()` is the finding of the assessment.** It looks reachable —
`BrepFaceView.Surface` exists and returns a `Surface` — and it is not. Dynamo returns the
**trimmed** face; `BrepFaceView.Surface` returns the **untrimmed** surface the face is a window
onto. That is the same difference [N156](NOTES.md) had found the day before under
`PolySurface.Surfaces()`, caught twice in two days because the review asks what a member *does*
rather than what it is *called*. It is marked `Planned`, not `Done`.

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

### 3.6 Mesh — 2 types, 65 members, 49 reachable

**Assessed member by member on 2026-09-13** (`E2-T45`), against the assembly rather than by eye, and
this section moved further in one step than any other: **from *0 reachable* to 41 of 65**. Almost none
of that is new code. Most of it is the register finally reading the mesh layer that has existed since
M5, plus one assembly — `Spark.Geometry.Io` — that the rename-catcher was not looking at.

| Dynamo type | Members | Reachable | Spark equivalent | Status | Milestone |
|---|---:|---:|---|---|---|
| `Mesh` | 55 | 39 | `Mesh`, `MeshTopology`, `Spark.Geometry.Io`, `Spark.Api.IBrepKernel` | Partial | M5, M6 |
| `IndexGroup` | 10 | 10 | `MeshFace` | **Done** | M5 |

**`IndexGroup` is `MeshFace`, and the section used to say the type was not needed.** That was wrong in
a small way worth correcting: Spark does have the type, it is `MeshFace`, and all ten of `IndexGroup`'s
members are there. Two differences, neither a gap. Dynamo's corners are `UInt32` and `Count` is 3 or 4;
Spark's are `int`, and a triangle carries `D = MeshFace.NoVertex` (`-1`) rather than a repeated `C`,
because the repeat convention makes a degenerate quad indistinguishable from a triangle and gives four
edges where there are three (`E2-T20`). **And these indices are safe where a `Brep`'s are not**: a mesh
index is managed from end to end and never crosses the kernel seam, which is exactly the difference
[N159](NOTES.md) had to find the hard way under `Solid.HollowOpen`.

**The finding this section was carrying was *eight repair and remeshing members missing*, and it is
confirmed with one correction.** `Repair`, `MakeWatertight`, `Remesh`, `Reduce`, `Smooth` and `Explode`
appear nowhere in FR-51, `E2-T20` or `E2-T27`, and nothing in the assembly answers them — checked,
because `E2-T42`'s row made this same kind of claim about surface curvature and was wrong.
**`CloseCracks()` is the exception: it is already there, as `Mesh.Welded(tolerance)`**, which merges
coincident vertices and closes the topology. It does *more* than Dynamo's, which touches only
near-coincident boundary vertices — and it costs something real, because a vertex carries one normal
and a welded box shades like a ball. So seven, not eight, and they are `E2-T68`. **`Explode()` is the
cheapest of the seven by a wide margin** — a connected-component walk is one pass over
`MeshTopology.AdjacentFaces` — and it is still not written.

**`MakeWatertight` deserves its own sentence, because `Welded` looks like it and is not.** Welding
merges coincident vertices; it does not fill a hole. `MeshTopology.IsClosed` *reports* watertightness
and does not produce it. Dynamo's remeshes through a voxel field, which is a different and much larger
thing, and calling `Welded` watertightness would be exactly the [N156](NOTES.md) error a third time.

**The transformation family is `Mesh.TransformedBy` and this is the first section to apply §3.8's
argument.** Seven of Dynamo's `Mesh` members — `Translate` ×3, `Rotate`, `Scale` ×2, `Mirror` — are one
Spark member taking a `Transform` factory. `Transform.Rotation(axis, Angle.FromDegrees(45))` for
`mesh.Rotate(axis, 45)` is more to type and it is the shape ADR-0011 and ADR-0004 both point at: the
angle carries its unit in the type. §3.8 notes that the `By*` façade should carry the short forms as
node-friendly statics, and that is still the right answer for the node library.

**Interchange is `Done`, and finding that out changed the harness.** `Mesh.ImportFile` and
`Mesh.ExportMeshes` are STL, PLY, OBJ and glTF, all four ours and all four managed (FR-58, `E2-T34`,
`E2-T35`) — Dynamo's one member writes one format. They read as absent because the rename-catcher read
three assemblies and interchange lives in a fourth, `Spark.Geometry.Io`. **That is `E11-T30`'s
correction in the same shape, for the same reason** ([N158](NOTES.md)): the register's subject is
*capability*, so the check must read every assembly that delivers one. `Spark.Geometry.Io` joined the
list on 2026-09-13.

**`ToJson`/`FromJson` were excused on a false premise, and that is the second correction.** The
exclusions file had `GeometryJson` down as a whole type Dynamo has no counterpart for, *"refused in
§5 [g]"* — but §5 [g] refuses **SAT, SAB and native pointers**, and §3.8 says plainly that
`ToJson`/`FromJson` are planned under FR-57. `GeometryJson.Serialize` and `Deserialize` answer them,
one serializer over every geometry type rather than a member per type, each value self-describing with
its own version (`E2-T29`). A true rule reaching a false conclusion, like `Surface.Repair()` the day
before.

**What is genuinely missing, beyond the seven, is two small families and one decision.**

*The mesh primitives (4) — `Cuboid`, `Sphere`, `Cone`, `Plane`.* A Brep primitive tessellated is the
nearest route and it does not reach them: **a mesh primitive's point is its subdivision counts**, which
give a grid to deform, and a tessellation's density comes from a tolerance instead. Two of the four
have no Brep primitive to tessellate either (`E2-T66`).

*The mesh queries (4) — `Nearest`, `Project`, `TriangleCentroids`, `Edges()`.* `Nearest` and `Project`
need the same missing piece: **a spatial index over faces in the kernel rather than in the viewport.**
`Spark.Viewport`'s picker does ray-triangle over a BVH and is a renderer; `PointKdTree` (`E2-T16`)
answers point-to-*point*. `MeshTopology` counts edges and hands out only the boundary ones, so `Edges()`
is a smaller gap than it looks — the halfedge arrays exist and are internal. All four are `E2-T69`.

*The fabrication pair (2) — `MakeHollow` and `GenerateSupport`.* 3D-printing features rather than kernel
primitives, and **needs a decision** (`Q16`) about whether Spark wants a fabrication direction at all.
Drifting into one is how a kernel acquires a taxonomy it owns for ever — the same argument §5 [d] makes
about panelling. Note that `MakeHollow` collides with nothing: `IBrepKernel.Shell` hollows a *solid* to
a wall thickness and is a different operation on a different representation.

**The three mesh booleans are `E2-T27` and 1.x**, under ADR-0020: the exact Brep booleans arrive first
and take the urgency away. *Reduced, not eliminated* — OCCT is poor at mesh booleans and Dynamo has
them. **`Intersect(Plane)` and `PlaneCut` go with them**, because nothing in Spark cuts a *mesh* at all:
`IBrepKernel.Split` and `Trim` take `Brep` tools and cut a `Brep`.

**The three flattened accessors stay refused** — `VerticesAsThreeNumbers`, `EdgesAsSixNumbers`,
`TrianglesAsNineNumbers`, §5 [i]. They exist to feed Dynamo's list plumbing; Spark returns spans of
typed values and the graph handles them (`E4-T2`). Refusing the flattening is not refusing the data.
**This pass is what §5 [i] was waiting for**, so these three now count in the refusals, which is why the
manifest's *Not planned* total moves from 107 to 110.

### 3.7 T-Splines — 8 types, 169 members, 0 reachable, and excluded by **D30**

| Dynamo type | Members | Spark equivalent | Status | Milestone |
|---|---:|---|---|---|
| `TSplineSurface` | 94 | None | Not planned (**D30**) | — |
| `TSplineTopology` | 26 | None | Not planned (**D30**) | — |
| `TSplineVertex` | 11 | None | Not planned (**D30**) | — |
| `TSplineEdge` | 9 | None | Not planned (**D30**) | — |
| `TSplineFace` | 8 | None | Not planned (**D30**) | — |
| `TSplineInitialSymmetry` | 8 | None | Not planned (**D30**) | — |
| `TSplineReflection` | 8 | None | Not planned (**D30**) | — |
| `TSplineUVNFrame` | 5 | None | Not planned (**D30**) | — |

**169 members — 20.2% of the whole inventory, and `TSplineSurface` alone is 94, larger than
`Curve`.** This is not a gap to be filled in passing. §6.2 makes the argument in full.

### 3.8 Infrastructure — 8 types, 89 members, 24 reachable

**Assessed member by member on 2026-09-13** (`E2-T46`), against the assembly. **56 of the 89 were
already refused** under §5's rules and are not re-argued here; the 33 that were open are
`Geometry`'s 29 and `DesignScriptEntity`'s four, and **24 of them are reachable today**.

| Dynamo type | Members | Reachable | Spark equivalent | Status | Milestone |
|---|---:|---:|---|---|---|
| `Geometry` (abstract base) | 47 | 20 | No common base — see below | Partial | M5, M6 |
| `DesignScriptEntity` (abstract base) | 8 | 4 | No common base — see §5 [e] | Partial | — |
| `GeometryExtension` (static) | 15 | 0 | `Angle`, `Tolerance` — see §5 [f] | Not planned | — |
| `Application` | 6 | 0 | None — see §5 [a] | Not planned | — |
| `HostFactory` | 6 | 0 | None — see §5 [a] | Not planned | — |
| `ProtoGeometryConfiguration` | 2 | 0 | None — see §5 [b] | Not planned | — |
| `IProtoGeometryConfiguration` | 2 | 0 | None — see §5 [b] | Not planned | — |
| `Core.EntityTags` | 3 | 0 | Graph provenance — see §5 [c] | Not planned | — |

**Spark has no `Geometry` base class and should not acquire one.** ADR-0002's value types are
`readonly struct`s; a common abstract base would box every one of them and cost them their reason
for existing. **So each row names the member on the type that actually carries it**, and the reading
rule has to be stated once rather than argued 29 times: **a `Geometry` row is `Done` when Spark
delivers the capability on the types that carry it, and this section says which types are missing.**
Anything else would make a base-class member unanswerable by a design that has no base class, which
is refusing the design rather than measuring it.

*Transformation — **14**, not the 12 this section used to claim, and all 14 are `Done`.* `Transform`
×2, `Translate` ×3, `Rotate` ×2, `Scale` ×4, `Mirror`, `Scale1D`, `Scale2D` — that list has always
summed to 14, and the heading said 12. In Spark they are `Transform` factories applied through each
type's `TransformedBy`, which is one mechanism instead of a member repeated on every geometry type.
`curve.Rotate(p, axis, 45)` becomes `Transform.Rotation(axis, Angle.FromDegrees(45), p)` applied to
the curve: more verbose, and the shape ADR-0011 and ADR-0004 both point at. **The `By*` façade should
carry the short forms as node-friendly statics** so the node library reads the way an AEC user
expects even though the kernel reads the way a C# developer expects. Three of the 14 —
`Scale(Plane, …)`, `Scale1D`, `Scale2D` — are a **composition** rather than a member: change basis,
scale, change back, joined with `Transform`'s own `*`. That is three calls for Dynamo's one and is
exactly what the façade is for.

**The cost of claiming that family is visible, and it should be.** Six parity rows now name
`Transform`'s members, so `Transform` may no longer be excused wholesale from the reverse direction,
and its other 28 members joined the residue — **the largest single move that number has ever made**
(292 → 319). It is the price of the claim rather than drift: a type the register says answers
fourteen Dynamo members is a type whose whole surface the register has to account for.

**`Brep` had no transform at all, which was the finding of this section — and it was closed the
same day.** `TransformedBy` was on `Curve`, `Surface`, `Mesh`, `PointCloud`, `PolyCurve`,
`PolyLine` and every analytic surface, **and not on `Brep`**, with no `Transform` operation on
`IBrepKernel` and no node: a user could union two solids and not **move** one. The register would
never have found it from the `Geometry` rows, because they are `Done` on the strength of the types
that *do* have it; it was found by asking which types do not ([N161](NOTES.md)). **`E2-T70` step A
landed on 2026-09-13** — `Brep.TransformedBy`, plus `Solid.Translate`, `Rotate` and `Mirror` as
nodes, with every face flipped when the transform reverses handedness. It works with no provider
installed, and it materialises a resident shape, because `IBrepKernel` still has no transform to
ask for (`E13-T21` blocks adding one).

*Measurement and intersection (10) — six `Done`, three `Planned`, one needing a decision.*
`BoundingBox` is on `Curve`, `Surface`, `Brep`, `Mesh` and `PointCloud` (`Surface`'s is sampled and
cached rather than exact, which is the same class of answer as `Solid.Volume`'s — `E2-T67`).
`Split`, `Trim` and `Explode` are `Done`: the first two are `IBrepKernel.Split` and `Trim`, which
take the *keep* point rather than a side index, and `Explode` is `PolyCurve.Segments()` and
`Brep.Faces()`.

**What is missing here is one thing wearing four names.** `ClosestPointTo`, `DistanceTo`,
`DoesIntersect` and `Intersect`/`IntersectAll` all fail for the same reason: **Spark's queries take a
*point*, never another geometry.** `Curve.ClosestPoint`, `Surface.ClosestPoint`, `Plane.ClosestPoint`,
`BoundingBox.ClosestPoint` and `Ray.ClosestPointTo` all take a `Point3d`, and `E2-T62`/`E2-T63` spent
two whole steps making the surface one correct on a fold — so the point case is not merely present,
it is hard-won. Geometry-to-geometry is absent. The same hole shows in the intersectors: **curve/curve
is done and exact** (`Curve.IntersectWith`, `E2-T11`) and **solid/solid is the kernel's**, but
**curve/surface and surface/surface have nothing at all**, and cutting a curve with a surface is a
thing AEC graphs do constantly. `E2-T70`.

`OrientedBoundingBox` **needs a decision**, and it is §3.1's, reached from the other side: not
whether to give `BoundingBox` a frame, but whether Spark wants a separate `OrientedBox` type.
`BoundingBox.ByCornersCoordinateSystem`, `BoundingBox.ContextCoordinateSystem` and `ByMinimumVolume`
all push the same way, and a minimum-volume box is a materially harder computation than an
axis-aligned one.

*Serialisation and interop (25) — split, and one of the two halves moved.* `ToJson`/`FromJson` are
**`Done`**: `GeometryJson.Serialize` and `Deserialize`, one serializer over every geometry type with
a per-type `schemaVersion` and an unknown version refused rather than guessed at (FR-57, `E2-T29`).
`E2-T45` had already proved this the day before by taking `GeometryJson` off the excused types, where
it sat under a §5 [g] refusal that §5 [g] does not make. **The remaining 18 stay `Not planned`** and
are §5 [g]'s: the SAT/SAB family is ACIS's format and reading it needs the ACIS kernel, and
`FromNativePointer`/`ToNativePointer`/`FromObject` marshal into a native kernel session Spark does
not have. Spark's interchange answer is STEP (FR-59) and OBJ/STL/PLY/glTF (FR-58).

*Three refused on principle rather than on effort, and the ground matters.* `IsAlmostEqualTo` and
`Approximate` both take **no tolerance**, so they compare against an *ambient* one — which is
precisely what ADR-0010 forbids, and precisely the ground §5 [f] refuses `GeometryExtension.EqualsTo`
on while accepting `Equals(x, y, tolerance)`. **The capability is present under an explicit
tolerance**: `EqualsWithin(other, in Tolerance)` on `Point3d`, `Point2d`, `Plane`,
`CoordinateSystem`, `BoundingBox`, `Interval`, `Angle` and `Quaternion`, and
`Surface.ApproximateWithTolerance` / `NurbsCurve.ApproximatePoints`. Only the form is refused.
`ContextCoordinateSystem` is refused on a different ground — §5 [c]'s: a context frame is a fact
about where a value was *created*, so holding it requires geometry to have identity and history, and
two `Point3d`s with the same coordinates are the same value. §6.3 also records that nobody has
established what a context frame means for arbitrary geometry.

**`DesignScriptEntity.Tessellate` was listed as planned and had landed.** §5 [e] says it *"is
planned — it lands in `Spark.Api` and `Spark.Viewport` as `RenderPackage`"*. The shape Spark actually
built is the same shape Dynamo has, one assembly earlier: **`Tessellation.Tessellate(surface,
ITessellationSink, tolerance)`** — the caller provides the sink, `MeshBuilder` implements it, and
`Spark.Viewport`'s `SceneBuilder` consumes the result. Dynamo's `TessellationParameters` bag is
Spark's `Tolerance`, which is ADR-0010 once more. Geometry still has no screen awareness, which is
why the member is not *on* the geometry — and that was the part §5 [e] got right.

---

## 4. Naming

Spark uses idiomatic C# with a `By*` façade (ADR-0004) and its own type names. This section
maps every rename so the mapping never has to be reconstructed from memory.

| Dynamo | Spark | Note |
|---|---|---|
| `Point` | `Point3d` | Renamed — see below |
| `Vector` | `Vector3d` | Renamed — see below |
| `UV` | `UV` | Same |
| — | `Quaternion` | **No Dynamo counterpart.** ProtoGeometry rotates through `CoordinateSystem` and `Rotate` only, so a Spark user gets something a Dynamo user does not: drift-free composition, `Slerp`, and four numbers to store instead of sixteen. It adds nothing to the parity count in §3, which counts *their* members, and that asymmetry is the right one — parity is a floor, not a ceiling |
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
`System.Windows.Point`, `Avalonia.Point` — and a code block is compiled with `Spark.Geometry`
already imported, beside `System` and three others, on a surface where a package author's
namespace is next. A kernel type whose name collides in that position is a support burden we can
decline for the price of two characters. *(Corrected 2026-09-02: this read "E6's code block hosts
arbitrary `using` directives written by users", which it does not — the script is wrapped inside
a method body, where a `using` directive is a syntax error. See [N94](NOTES.md).)*

**`CoEdge` → `BrepTrim`.** A co-edge is one face's use of a shared edge; an edge between two
faces has two of them, running in opposite directions. Dynamo names it for its topological
role. Most kernels name it for what the data actually is — the **trim**, because the entity
carries the curve in the face's own UV parameter space that bounds the face there.
`CoEdge` describes the relationship; `BrepTrim` describes the payload.

> **Corrected 2026-09-14 (`E2-T64`), and the correction weakens the argument rather than the
> conclusion.** This paragraph said *Spark's `BrepTrim` holds a `Curve2d` (E2-T13), which is why
> the planar layer is a prerequisite for BRep rather than a nicety*, and then rested the rename on
> it: *the payload is what users need to reach*. **`BrepTrim` is
> `readonly record struct BrepTrim(int Edge, bool IsReversed)` and holds no pcurve at all.** So the
> name describes a payload this kernel does not carry, and the strongest thing that can honestly be
> said for it is the ordinary one: it is what every other kernel calls this entity, so a reader who
> knows one knows this. **That is a good enough reason and it is a different reason**, and
> [D28](PRD.md#13-decision-log) settles why the payload is not coming before 1.0: a managed pcurve
> has no managed consumer, because a trimmed face is tessellated behind the OpenCascade seam where
> the provider holds pcurves of its own.

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

**And a finding about our own code, since this document had to check it — now closed.** When
this section was written, `Vector3d.Normalised()`, `Interval.Reversed()` and
`Interval.MakeIncreasing()` followed the rule while `Plane.Flip()`, `BoundingBox.Inflate()` and
`Interval.Expand()` did not: all three read as imperatives and all three returned new values.
**They are now `Flipped()`, `Inflated()` and `Expanded()`** (`E2-T49`, 2026-08-29), and the
convention itself is written into `NamespaceDoc` where it binds every type added afterwards
rather than living in a survey document. It asked to be settled *before curves arrived and
multiplied the surface by five*; that timing was missed — the curve layer landed first and
followed the rule anyway — but the rename was still free, because nothing is shipped and the
compiler finds every call site. **The day after 1.0 it would have been an
[ADR-0019](adr/0019-deliberate-public-api-change-control.md) change-control question instead.**

---

## 5. What we will deliberately not replicate

**93 members, 11.1% of the inventory, by this document's count — 123 by the manifest's** (`E11-T23`: the lists below add to 104, and the primitives' parameter-recovery properties are 14, not the 11 [h] says; `E2-T45` then counted [i]'s three flattened mesh accessors, `E2-T46` added three more under [f]'s ground — see [j] — `E2-T41` counted [i]'s eight duplicated trim and split members once their survivor had been chosen, and `E2-T40` added `Vector.IsAlmostEqualTo` to [j] and `CoordinateSystem.ByOriginVectors`'s explicit-Z overload on §3.1's ground). Each with a reason and with what a Spark user does
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
Dynamo's list plumbing. Spark ships one trim family and returns typed spans. **The three mesh
accessors were counted on 2026-09-13**, when `E2-T45`'s member-by-member pass reached them —
refusing the flattening is not refusing the data, and `VertexPositions`, `Edges` and `Triangles`
carry the same information typed. **The trim family was counted on 2026-09-13**, when `E2-T41`
step A reached it and made the choice this entry had been waiting on: **the `Parameter*` set is the
survivor** — it is the set this entry itself names as primary — and the eight `TrimBy*` / `SplitBy*`
members are refused here. The capability is assessed on the `Parameter*` rows, where all nine
surviving members are `Done`, seven of them by composing `Curve.Trimmed(in Interval)`.
`SplitByPoints` has no twin in either set and was assessed on its merits rather than refused with
the family.

**[j] Members that compare against an ambient tolerance or a creation context — 4 members.**
`Geometry.IsAlmostEqualTo(Geometry)` and `Approximate()` take **no tolerance**, so the tolerance
comes from somewhere unstated — almost certainly the session scale factor — and that is exactly the
ground [f] refuses `EqualsTo(a, b)` on. **Only the form is refused**: the capability is
`EqualsWithin(other, in Tolerance)` on eight value types, and `Surface.ApproximateWithTolerance` /
`NurbsCurve.ApproximatePoints`. **A Spark user passes the tolerance they meant** (ADR-0010).
`ContextCoordinateSystem` is [c]'s argument rather than [f]'s: a context frame records where a value
was *created*, which requires geometry to have identity and history, and two `Point3d`s with the same
coordinates are the same value. *(Counted, 2026-09-13, `E2-T46`.)* **`Vector.IsAlmostEqualTo(Vector)`
joined them on 2026-09-13** (`E2-T40`): the same member on the same ground one type down, and the
capability is `Vector3d.EqualsWithin(other, in Tolerance)`. `CoordinateSystem.IsEqualTo` is **not**
refused beside it, and the difference is the whole of [j]: *almost* equal with no tolerance has to
get one from somewhere unstated, and exact equality does not.

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

#### The decision: path (3), taken by the client

**Answered on 2026-08-28. The client chose path (3), not the recommended path (2).** Presented
with all three and with the price of each, the client instructed that Spark take an existing
engine deliberately rather than as a contingency. **The 70 members are 1.0 requirements.**

What was decided, and where it is recorded:

- **The engine is OpenCascade Technology**, and after a full comparison it was not close —
  OpenNURBS is a representation and file-format library with no booleans, Manifold and the
  mesh-boolean libraries produce no BRep at all, CGAL's boolean packages are GPL-3.0, and
  Parasolid, ACIS and C3D lose on the per-seat licensing argument ADR-0002 already made and
  which is undisturbed. [ADR-0020](adr/0020-occt-via-c-abi-shim.md), PRD **D15**.
- **The binding is a hand-written C-ABI shim we own** — `spark_occt`, MIT, in
  `native/spark_occt/`, reached by `LibraryImport` from a new `Spark.Geometry.Occt`. No
  third-party binding is adopted. The alternative that came closest, C++/CLI, was rejected for
  being **Windows-only permanently**.
- **OCCT ships in the default install**, because a Dynamo user finding booleans greyed out on
  first run is precisely what FR-81 forbids. `Spark.Geometry` itself stays pure managed and
  independently distributable, and NFR-5 is unchanged.
- **The seam's residency rule is redrawn** by [ADR-0021](adr/0021-brep-kernel-residency.md),
  and the reason is fidelity rather than speed: a Spark→OCCT→Spark round trip is not identity.
- **ADR-0002 is superseded; ADR-0003 is amended.** Both keep their number and their text.

What it costs, stated as two numbers because one would mislead: **+7 to +11 weeks against the
plan as written, and years saved against what was actually asked for.** The first is positive
only because the plan as written never contained the expensive thing — M6's 14 weeks bought
mesh booleans, while exact booleans, fillet, chamfer and trim sat in PRD §9's out-of-scope
list. Parity was never funded, and this is what funding it looks like.

**R1 and R12 retire. R3 does not — it changes owner**, since the numerical failure modes move
inside OCCT where they can be observed and not fixed (**R18**). Eight new risks, **R15 … R22**,
arrive with the native dependency, and **R13 is reframed and considerably enlarged**: Spark now
acquires a heavyweight native dependency, and the distinction from the one it exists to remove
holds only if **we state it first, in our own words.**

The work is [EPICS.md E13](EPICS.md#e13--occt-provider), `E13-T1` … `E13-T17`, roughly 24
weeks, most of it inside M6, which goes from 14 weeks to 20–24. **E2-T47 is closed.** **Q6 and
Q11 are answered.** **Q12 is not** — OpenCascade has no subdivision modeller either, so nothing
about this decision makes T-Splines cheaper or more likely, and §6.2's recommendation stands
exactly as written.

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

**Excluding it also makes the headline number honest**, which is the other reason to decide it
rather than leave it: *every* coverage percentage in this document depends on which surface is the
denominator, and a fifth of it sitting undecided is a fifth every figure has to explain itself
around.

**Decided 2026-09-15 by [D30](PRD.md#13-decision-log), as recommended**, and the 169 manifest rows
moved from `Needs a decision` to `Not planned`, each citing this section. `Q12` is answered and
`E2-T48` is closed.

> **This section's own closing numbers were wrong and are corrected here rather than quietly
> dropped.** It said *with T-Splines out, the committed surface is 575 members rather than 744*.
> **Neither figure survives the inventory at the top of this document**: 744 matches no count this
> file has ever carried, and the committed surface is 837 less the 123 refused and these 169, which
> is **545** — the number §1's table has been quoting for days while this paragraph said otherwise.
> They predate the member-by-member pass that produced the 837, and the lesson is the one §7 makes
> about the manifest: a number written in prose drifts from the number that is counted, which is why
> the table above is derived and this sentence was not.

### 6.3 What we could not interpret confidently

Honest gaps in this document, from signatures that do not determine behaviour. Each needs
checking against a running Dynamo before the corresponding Spark member is designed — not
before it is *listed*, which is why they do not block this register.

- **`Curve`'s four length parameterisations — half answered on 2026-09-13** (`E2-T41` step A).
  `DistanceAtParameter`, `SegmentLengthAtParameter`, `ParameterAtDistance` and
  `ParameterAtSegmentLength` are four names for what look like two concepts. **The `Distance` pair
  is arc length from the start**, which is what §3.2 has assumed since the curve contract was
  designed against it, and both members are now `Done` on `Curve.LengthAt` and
  `Curve.ParameterAtLength`. **What `SegmentLength` measures is still not deducible**, and it is
  why six members carrying that word are `Needs a decision` rather than claimed: if it is arc length
  they are duplicates of members Spark already has, and if it is a chord walk they are not.
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
  scale factor, which is exactly what ADR-0010 refuses. **They were separated on 2026-09-13**
  (`E2-T41` step A): `Approximate()` is refused under §5 [j] because the capability has a
  tolerance-taking home in Spark, and `ApproximateWithArcAndLineSegments()` is `Needs a decision`
  because it has none — refusing a biarc approximation on the shape of its parameter list would
  file a real gap under a rule about a signature.
- **`Geometry.FromObject(Int64)`, `FromSolidDef(String)`, `ToSolidDef()`** — undocumented
  shapes, probably Autodesk-internal. Not planned regardless (§5 [g]).
- **`DesignScriptEntity.BaseTessellationGuid` and `InstanceInfoAvailable`** — unclear, and not
  planned regardless (§5 [e]).
- **`Geometry.ContextCoordinateSystem`** — what a "context" frame means for arbitrary geometry,
  and how it differs from `BoundingBox.ContextCoordinateSystem`.
- **`BoundingBox.IsEmpty()`** — degenerate, zero-volume, or never-initialised. **Answered as
  reachable under all three readings on 2026-09-13** (`E2-T40`), which is the second row in the
  register to settle that way: `BoundingBox.Empty` is the never-initialised sentinel, `Volume` is
  zero for the second, and `IsValid` answers the degenerate one. The ambiguity stands; it just does
  not decide anything.
- **`PolyCurve.Heal(Double trimLength)`, `PolyCurve.CurveAtIndex(index, Boolean endOrStart)` and
  `PolyCurve.ByJoinedCurves(curves, joinTolerance, Boolean, Double)`** — the role of `trimLength`,
  of `endOrStart` alongside an index, and of the four-argument join's trailing flag and length, which
  reads like the same trimming behaviour as `Heal`'s (`E2-T41` step B, 2026-09-13). `SegmentAt(int)`
  answers `CurveAtIndex`'s index half exactly, so what is open there is the flag and not the
  capability.
- **`Arc.ByStartEndAndTangencies(Point, Vector, Point, Vector)`** (`E2-T41` step B, 2026-09-13). An
  arc has three degrees of freedom in its plane and this gives four constraints, so either the second
  tangent is advisory, or the result is a biarc rather than an arc, or the call fails when the two
  disagree. Which it is decides whether Spark needs a member at all.
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
mechanism below was proposed rather than promised, and it is registered as **E11-T23**, which is
**complete as of 2026-09-12**. The manifest landed 2026-09-11 with three of its four checks — that it
is well formed, that its totals agree with this document, and that every Done row names a member that
exists. **The fourth, the reverse direction, landed 2026-09-12**, and with it the review of the 89
Done rows that had been seeded by a bare name match.

**The reverse direction excuses by rule, never member by member.** `Spark.Geometry` declares **894**
public members by the manifest's own counting rule (measured 2026-09-13; the **877** this line carried
and the 895 the exclusions file carried were both stale, and disagreed with each other, which is what
an unchecked number does), and **735** of them are named by no parity row —
a file holding 790 hand-written excuses would be exactly the drifting artefact this section exists to
prevent. So [`tests/corpus/dynamo-parity-exclusions.tsv`](../tests/corpus/dynamo-parity-exclusions.tsv)
carries **37 rules**: five member names that are .NET plumbing rather than capability (`Equals`,
`GetHashCode`, `ToString`, `Deconstruct` and the operators), and **32 whole types Dynamo has no
counterpart for** — `Transform`, `Tolerance`, `Interval`, `Angle`, `Quaternion`, `Ray`, the analytic
surfaces, the Brep views and the planar layer, each with the §2 or §3 sentence that says why. **A type
some parity row names may not be excused wholesale**, and the check enforces that rather than trusting
it, because otherwise the file would be a way to make the check green.

**What no rule reaches is counted, not waved through.** **321** members sit on types that *do* map to a
Dynamo type, so each is either a member some `Unassessed` row will name once it is assessed or a
genuine gap — and the budget at the foot of the exclusions file is checked for **exact** equality, not
as a ceiling. The number therefore falls as rows are assessed and cannot quietly climb back.
**A rise has two causes and the file's history has to say which**: a member was added to a mapped type
with no thought for this register, or — as on 2026-09-12 and again on 2026-09-13 — an assessment named
a member of a type that had been excused wholesale, so that type's whole surface came into the count.
The second is the register doing its job and looks exactly like the first until somebody writes down
which it was.

**The review found one row in 89 where the name lied.** `PolySurface.Surfaces()` had matched
`Brep.Surfaces()`, which hands back the *untrimmed* face surface table in index order where Dynamo
returns the trimmed faces; it is demoted to `Unassessed` with that reason, and the faithful equivalent
needs pcurves a trim does not carry yet (`E2-T64`). The other 88 are the same capability under the same
name — though **24 of the 88 are .NET plumbing** (22 `ToString()`, one `Equals` and one
`GetHashCode`), which is presence and nothing more and is marked as such in each row's reason. See [N156](NOTES.md).

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
   - a row says `Done` and the named Spark member does not exist — **this is the rename-catcher**,
     and the reason the manifest names members rather than types. **It reads every assembly that
     delivers geometry** — `Spark.Geometry`, `Spark.Api` and `Spark.Nodes.Core` (`E11-T30`) —
     because this document's subject is capability, and loft, sweep and the booleans are delivered
     through `Spark.Api.IBrepKernel`. **The reverse direction below is scoped differently, to
     `Spark.Geometry` alone**, because it asks a different question: has *the kernel's own surface*
     drifted from the plan it was meant to satisfy;
   - a public member of `Spark.Geometry` is named by no row, is excused by no rule in the
     exclusions file, and the count of what is left disagrees with the stated budget — the
     reverse direction, which is what catches a Spark member drifting away from the plan it was
     meant to satisfy;
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
- [TASKS.md](TASKS.md) — E2-T40 … E2-T48, E13-T1 … E13-T17, E11-T23
- [TODO.md](TODO.md) — Q12, Q13 and Q14 under *Decisions waiting on someone*; Q6 and Q11 are
  answered there
- [ADR-0020](adr/0020-occt-via-c-abi-shim.md) — the engine and the binding, superseding ADR-0002
- [ADR-0021](adr/0021-brep-kernel-residency.md) — the residency rule, amending ADR-0003
- [ADR-0002](adr/0002-own-managed-geometry-kernel.md) — own pure-managed kernel, staged
- [ADR-0003](adr/0003-ibrepkernel-seams-operations.md) — `IBrepKernel` seams operations
- [ADR-0004](adr/0004-idiomatic-core-plus-by-facade.md) — idiomatic core plus `By*` façade
- [ADR-0010](adr/0010-explicit-scale-aware-tolerance.md) — tolerance is passed, never ambient
- [ADR-0011](adr/0011-angle-struct-in-public-signatures.md) — `Angle` in every angular signature
- [ADR-0016](adr/0016-no-dynamo-interoperability.md) — no `.dyn` interoperability, either direction
- [ADR-0019](adr/0019-deliberate-public-api-change-control.md) — deliberate public API change control
