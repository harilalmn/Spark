---
id: concepts.curves
title: Curves, parameters and arc length
nodes: [Line.FromStartPointEndPoint, Circle.FromCenterRadius, Arc.FromThreePoints, Ellipse.FromPlaneRadii, Helix.FromAxis, Curve.Fillet, PolyLine.FromRegularPolygon, PolyCurve.FromJoinedCurves, Curve.PointAtParameter, Curve.PointAtLength, Curve.DivideEqually, Curve.DivideByLength, Curve.DivideByChordLength, Curve.DivideEquallyByChord, Curve.Extended, Curve.IntersectWith, Curve.IntersectWithSurface, NurbsCurve.InterpolatePoints, NurbsCurve.InterpolatePointsWithTangents]
related: [concepts.geometry-basics, concepts.lacing]
since: "0.1"
---

**Status:** Current. Describes `Spark.Geometry`'s curve layer, which exists and is tested.
**Owner:** `geometry-kernel`
**Last updated:** 2026-09-13

> **Scope.** Eight curve types exist today: `Line`, `Arc`, `Circle`, `EllipseCurve`, `Helix`,
> `NurbsCurve`, `PolyLine` and `PolyCurve`. Curve intersection, offsetting and the closest-point
> queries are all here; **projection
> and pull are not**, and neither is a planarity test. Surfaces, meshes and solids arrived at M5
> and M6. Every example below was run against the assembly.

---

## Why this page exists

Curves introduce one idea that points and vectors do not have, and getting it wrong is the
single most common source of "why are my points bunched up at the ends?":

> **Where you are on a curve can be measured two different ways, and they are not the same
> place.**

Everything else on this page follows from that.

---

## 1. A parameter is not a distance

Every curve has a **domain** — the range of numbers you can hand it — and asking for the
point at a parameter walks that range. Every curve also has a **length**, and asking for the
point at a length walks the curve itself with a tape measure.

On a line, a circle and a helix these two agree, because those curves travel at a constant
speed. On an **ellipse** they do not: the curve moves quickly past the ends of the long axis and slowly
past the ends of the short one, so equal steps in parameter cover unequal distances. (An
ellipse's *quarter* marks do agree, because its four quadrants are congruent — which is why
the example below uses an eighth rather than a quarter.)

```csharp
using Spark.Geometry;

EllipseCurve ellipse = EllipseCurve.FromPlaneRadii(Plane.WorldXY, 3.0, 1.0);

// An eighth of the way through the domain, and an eighth of the way along the curve.
Point3d byParameter = ellipse.PointAt(ellipse.Domain.Denormalise(0.125));
Point3d byLength = ellipse.PointAtLength(ellipse.Length * 0.125);

double apart = byParameter.DistanceTo(byLength);   // 0.48 — not a rounding difference
```

Spark gives you both and makes you say which you meant. In the node library the split is by
name: anything called `AtParameter` runs from 0 to 1 through the curve's own parameter space,
and anything called `AtLength` is measured in real distance from the start.

**Which do you want?** Almost always the length one. *Twelve fence posts evenly spaced along
this path* is a length question. Parameters are for when you are working with the curve's own
maths — matching a point to a tangent you already computed, for instance.

---

## 2. Dividing a curve

`Curve.DivideEqually` cuts by **arc length**, so the pieces really are the same length:

```csharp
using Spark.Geometry;

Circle circle = Circle.FromCenterRadius(Point3d.Origin, 10.0);
Point3d[] posts = circle.DivideEqually(8);   // 9 points: eight gaps, and the loop closes

// The last point repeats the first, because the circle is closed. That is deliberate: it
// makes the result a closed loop rather than a loop with a gap in it.
bool closed = posts[0] == posts[^1];         // true
```

Two things worth knowing:

- **You get one more point than you asked for divisions.** Eight divisions, nine points, both
  ends included.
- **`DivideByLength` drops the remainder.** Asking for a point every 3 units along a curve
  10 units long gives you points at 0, 3, 6 and 9 — not a stubby 1-unit piece at the end.

**And there is a second way to measure, which is not the same one.** Everything above walks the
curve with a tape measure. `DivideByChordLength` and `DivideEquallyByChord` measure the
**straight line** between consecutive points instead:

```csharp
using Spark.Geometry;

Circle circle = Circle.FromCenterRadius(Point3d.Origin, 5.0);

Point3d[] alongTheCurve = circle.DivideByLength(2.0);      // 2 units walked, each step
Point3d[] straightLine = circle.DivideByChordLength(2.0);  // 2 units as the crow flies

int walked = alongTheCurve.Length;    // 16
int flown = straightLine.Length;      // 16 - but not the same sixteen points
double apart = alongTheCurve[8].DistanceTo(straightLine[8]);   // 0.11 - a real difference
```

**Which do you want?** If the pieces between the points are going to be *straight* — equal struts,
equal panels, a chain of equal members — you want the chord. If what matters is distance travelled
along the path, you want the length. On a straight line the two are identical; the tighter the
curve, the further apart their answers.

`DivideEquallyByChord(n)` is the chord counterpart of `DivideEqually(n)`. Around a circle it gives
the inscribed regular polygon, which is the clearest picture of the difference there is:

```csharp
using Spark.Geometry;

Circle circle = Circle.FromCenterRadius(Point3d.Origin, 5.0);

Point3d[] corners = circle.DivideEquallyByChord(6);
double side = corners[0].DistanceTo(corners[1]);   // 5 - a hexagon's side is its radius
```

One caution: an equal-chord division has to *solve* for the chord that fits a whole number of
times, and on a curve that doubles back sharply within one step the search can fail. It says so
with an error rather than returning separations that are only nearly equal — `DivideEqually`
divides by arc length and always succeeds.

---

## 3. Each curve has its own domain, and none of them is 0 to 1

Ask, do not assume:

| Curve | Domain | Meaning |
|---|---|---|
| `Line` | 0 → 1 | fraction of the way along |
| `Circle` | 0 → 2π | radians from the plane's x axis |
| `Arc` | 0 → sweep | radians from the arc's **own** start |
| `EllipseCurve` | 0 → sweep | the eccentric angle, not the angle at the center |
| `Helix` | 0 → sweep | radians turned about the axis, so 2π is one whole turn |
| `PolyLine` | 0 → n | one unit per segment, so whole numbers are the vertices |
| `PolyCurve` | 0 → n | one unit per segment, so whole numbers are the joints |

`Interval.Normalise` and `Interval.Denormalise` convert between a domain parameter and a
fraction, which is exactly what the node layer does for you:

```csharp
using Spark.Geometry;

PolyLine path = PolyLine.FromPoints(
[
    Point3d.Origin,
    new Point3d(3.0, 0.0, 0.0),
    new Point3d(3.0, 4.0, 0.0),
]);

Point3d corner = path.PointAt(1.0);                       // parameter 1 is the second vertex
Point3d third = path.PointAt(path.Domain.Denormalise(0.5)); // half way through the domain
double length = path.Length;                              // 7 — measured along the path
```

Note that `path.PointAt(1.0)` and *half way along* are different places here too: the first
segment is 3 long and the second is 4.

---

## 4. Making curves

```csharp
using Spark.Geometry;

// Straight.
Line straight = new(Point3d.Origin, new Point3d(10.0, 0.0, 0.0));

// Circular. Three points define an arc; the middle one decides which way round it goes.
Arc bend = Arc.FromThreePoints(
    new Point3d(1.0, 0.0, 0.0),
    new Point3d(0.0, 1.0, 0.0),
    new Point3d(-1.0, 0.0, 0.0));

// Closed shapes are polylines, not their own types: a polygon is a closed polyline, and a
// rectangle is a factory rather than a class of its own.
PolyLine hexagon = PolyLine.FromRegularPolygon(Plane.WorldXY, 2.0, 6);
PolyLine frame = PolyLine.FromRectangle(Plane.WorldXY, 4.0, 3.0);

// Turning and rising at once. The start point says where it begins and how far out it is; the
// pitch says how much it climbs each turn.
Helix spiral = Helix.FromAxis(
    Point3d.Origin,
    Vector3d.ZAxis,
    new Point3d(1.5, 0.0, 0.0),
    0.5,
    Angle.FromDegrees(1080.0));   // three turns

// Chained. The join tolerance is passed, never assumed, and a chain that does not meet
// within it is refused rather than silently accepted with a gap in it.
PolyCurve chain = PolyCurve.FromJoinedCurves([straight, bend]);
```

---

## 5. What a curve will refuse to do

Spark's kernel answers or fails loudly; it does not return a plausible-looking default.

- **A zero-length line is refused.** Two identical points have no direction, and every tangent
  query on such a line would be a division by zero wearing a disguise.
- **A parameter outside an open curve's domain throws.** It is not quietly clamped and not
  extrapolated. On a *closed* curve it wraps instead, because there it means something.
- **A non-uniform scale on a circle throws.** Squashing a circle produces an ellipse, and a
  `Circle` cannot represent one. Scaling *along* a circle's own axis is fine and is allowed.
- **A polycurve with a gap is refused**, and the message names the segment index and the size
  of the gap.

```csharp
using Spark.Geometry;

Circle circle = Circle.FromCenterRadius(Point3d.Origin, 1.0);

Curve moved = circle.TransformedBy(Transform.Scale(1.0, 1.0, 3.0));   // fine: still a circle
// circle.TransformedBy(Transform.Scale(2.0, 1.0, 1.0));              // throws: that is an ellipse
```

---

## 6. Curves are immutable

Every operation returns a new curve and leaves yours alone. The names say so — `Reversed`,
`Trimmed`, `TransformedBy` — rather than reading as commands to change something in place.

```csharp
using Spark.Geometry;

Circle circle = Circle.FromCenterRadius(Point3d.Origin, 5.0);
Curve half = circle.Trimmed(new Interval(0.0, System.Math.PI));   // an Arc, not a Circle

double untouched = circle.Length;   // still the full circumference
```

Trimming a circle giving back an *arc* is the one place an operation changes the type of the
thing it was given, and it is right: a circle that is not closed is not a circle.

---

## 7. Cutting a curve with a surface

`Curve.IntersectWithSurface` gives the points where a curve passes through a surface. It is the
node you reach for when a graph has to find where a line of sight meets a roof, or where a duct
crosses a slab.

```csharp
using Spark.Geometry;

// A wall as a vertical plane two metres across, and a sight line crossing it.
Plane wall = Plane.FromOriginNormal(new Point3d(1.0, 0.0, 0.0), Vector3d.XAxis);
PlaneSurface panel = PlaneSurface.FromPlaneSize(wall, 2.0, 2.0);
Line sight = Line.FromStartPointEndPoint(new Point3d(0.0, 0.0, 0.3), new Point3d(3.0, 0.0, 0.3));

CurveSurfaceIntersections hits = sight.IntersectWith(panel);

Point3d where = hits.Points[0].Point;        // (1, 0, 0.3)
double alongTheLine = hits.Points[0].Parameter;   // the parameter on `sight`
UV onThePanel = hits.Points[0].Uv;                // where it landed on the panel
```

**Both parameters come back, and that is deliberate.** If you want the part of the curve before
the wall, you want `Parameter` and `Trimmed`; if you want to mark the panel, you want `Uv`.
Getting either back afterwards would mean doing the intersection again — so the same snippet,
carried on:

```csharp
using Spark.Geometry;

Plane wall = Plane.FromOriginNormal(new Point3d(1.0, 0.0, 0.0), Vector3d.XAxis);
PlaneSurface panel = PlaneSurface.FromPlaneSize(wall, 2.0, 2.0);
Line sight = Line.FromStartPointEndPoint(new Point3d(0.0, 0.0, 0.3), new Point3d(3.0, 0.0, 0.3));

CurveSurfaceIntersections hits = sight.IntersectWith(panel);
double alongTheLine = hits.Points[0].Parameter;

Curve beforeTheWall = sight.Trimmed(new Interval(sight.Domain.Min, alongTheLine));
double reach = beforeTheWall.Length;   // 1.0 - the line stops at the wall
```

**Three things it will not do, and they are worth knowing before you rely on it.**

- **A crossing outside the surface's patch is not a crossing.** A `Plane` is infinite; a
  `PlaneSurface` is the rectangle you asked for. Move the sight line past the end of the panel
  and the answer is empty, which is the right answer to the question you asked.
- **A curve lying *in* the surface gives no points.** That is a shared curve rather than a set of
  crossings, and `hits.LiesOnSurface` says so.
- **A curve that grazes the surface and turns back may be missed.** The intersector finds a
  crossing by looking for a change of side; a tangency touches without changing side. This is the
  same limit curve/curve intersection has, and it is stated rather than hidden.

**Surface against surface is not here.** That is a much harder problem and it belongs to the
solid modelling kernel, not to this layer — see [Solids](solids.md).

---

## 8. A helix, and the one curve where §1's warning does not bite

A **helix** turns about an axis at a fixed radius while rising along it at a fixed rate. It is
what you reach for when a graph has to lay out a spiral stair, a ramp, a thread or a spiral duct.

It is also the curve that makes §1's distinction easiest to see the *other* way round, because a
helix travels at a **constant speed**: a point that has gone a third of the way along it has also
turned a third of the way round it and climbed a third of the way up it. So dividing by length
and dividing by parameter give the same points — which on an ellipse they emphatically do not.

A stair, then. Sixteen risers up a 3-metre storey, at a going that puts the treads on a
1.2 m radius:

```csharp
using Spark.Geometry;

// One full turn, climbing three metres. The start point is where the bottom riser sits, and its
// distance from the axis is the radius — you do not pass the radius separately.
Helix flight = Helix.FromAxis(
    Point3d.Origin,
    Vector3d.ZAxis,
    new Point3d(1.2, 0.0, 0.0),
    3.0,
    Angle.FullTurn);

Point3d[] nosings = flight.DivideEqually(16);   // 17 points: sixteen risers, both ends

double rise = nosings[1].Z - nosings[0].Z;      // 0.1875 - every riser the same, by construction
double walk = flight.Length;                    // 8.11 - the distance actually walked
```

**`walk` is the number a stair needs and the one a plan drawing cannot give you.** A helix
unrolls to the hypotenuse of a right triangle whose legs are the arc it turned through
(`2π × 1.2 = 7.54`) and the height it climbed (`3.0`) — so the walking line is longer than
either. Spark computes it in closed form rather than by adding up chords.

Three things about the type that are worth knowing before you rely on it:

- **The pitch is the rise per *turn*, and it may be negative.** A negative pitch is a left-handed
  helix — it descends as it turns anticlockwise. It may **not** be zero: a helix that does not
  rise is an arc, and asking for one gets you an error that says so rather than a degenerate
  helix.
- **A negative sweep flips the axis rather than running the domain backwards.** Turning the other
  way about `+Z` is the same curve as turning this way about `-Z`, so that is what you get, and
  `AxisDirection` reports the axis it settled on. This is exactly what `Arc` does with a negative
  sweep.
- **`AxisPoint` is reported beside the start, not where you put the origin.** Only the axis
  *line* is part of the curve — the start point is what fixes the height — so an origin higher or
  lower on the same line describes the same helix and gets normalised to the same one.

```csharp
using Spark.Geometry;

// The same flight, with the axis origin given a hundred metres below. Same curve.
Helix elsewhere = Helix.FromAxis(
    new Point3d(0.0, 0.0, -100.0),
    Vector3d.ZAxis,
    new Point3d(1.2, 0.0, 0.0),
    3.0,
    Angle.FullTurn);

Point3d axisPoint = elsewhere.AxisPoint;   // (0, 0, 0) - beside the start, not (0, 0, -100)
double pitch = elsewhere.Pitch;            // 3
double radius = elsewhere.Radius;          // 1.2 - taken from the start point
```

---

## 9. Turning any curve into a NURBS curve, and knowing whether you lost anything

A lot of downstream work wants **one** curve representation — a solid modelling kernel converts
everything to NURBS as a matter of course, and so does every interchange format.
`Curve.ToNurbsCurve` does that, and it hands back two things rather than one:

```csharp
using Spark.Geometry;

Circle circle = Circle.FromCenterRadius(Point3d.Origin, 5.0);
NurbsConversion converted = circle.ToNurbsCurve();

NurbsCurve asNurbs = converted.Curve;
bool lossless = converted.IsExact;
```

**`IsExact` is not a formality, and it is the reason the member does not simply return a curve.**

| Curve | Converts | Why |
|---|---|---|
| `Line`, `PolyLine` | **exactly** | a degree-1 B-spline *is* a polyline |
| `NurbsCurve` | **exactly** | itself |
| `Arc`, `Circle`, `EllipseCurve` | **exactly** | a rational quadratic traces a conic exactly |
| `Helix` | **never exactly** | see below |
| `PolyCurve` | **exactly** | its segments are converted and joined, corners and all |

**A helix is the only row that is not *exactly*, and it is not a gap waiting to be filled — it is
a theorem.** A NURBS curve's coordinates are ratios of polynomials in its parameter. A helix needs
its height to be proportional to the *angle* it has turned through, and its x coordinate to be the
*cosine* of that same angle, at once — and no function is both rational and the cosine of something
rational. So no NURBS curve of any degree, with any knot vector, is a helix. Spark gives you the
best approximation it can and tells you that is what it is:

```csharp
using Spark.Geometry;

Helix stair = Helix.FromAxis(
    Point3d.Origin, Vector3d.ZAxis, new Point3d(1.2, 0.0, 0.0), 3.0, Angle.FullTurn);

NurbsConversion converted = stair.ToNurbsCurve();

bool lossless = converted.IsExact;                    // false, and it always will be
double strayed = stair.DistanceTo(converted.Curve.PointAt(0.37));   // how far off, at one point
```

Pass a tolerance to trade accuracy for size — a tighter one samples the original more finely and
gives a heavier curve. It is a sampling target rather than a promise about the worst error, so
where the error matters, measure it with `DistanceTo` as above.

**A converted circle has nine control points, not three**, and that is not padding: the rational
form is only valid up to a half turn — at exactly half a turn the middle weight reaches zero — so a
full circle is four spans joined end to end. An arc of less than 90° is a single span and three
points.

**One thing that surprises people, and it is true of every exact conversion above degree 1.**
*Exact* means the converted curve is the **same set of points**. It does **not** mean
`circle.PointAt(t)` and `converted.Curve.PointAt(t)` are the same point — a rational quadratic
walks a circular arc by a projective function of the angle rather than by the angle itself, so the
parameters do not line up. `Line` and `PolyLine` are the exception: at degree 1 the
parameterisation survives as well.

---

## 10. Is this curve flat, and which way does it face?

Two questions, and Spark makes you notice they are different:

```csharp
using Spark.Geometry;

PolyLine outline = PolyLine.FromRegularPolygon(Plane.WorldXY, 3.0, 6);

bool flat = outline.IsPlanar();          // true
Plane? plane = outline.PlaneOf();        // the plane it lies in
Vector3d facing = plane!.Value.Normal;   // (0, 0, 1)
```

**`IsPlanar` asks whether *some* plane contains the curve. `PlaneOf` asks *which* plane.** Most of
the time they agree and you only need the second. The case that separates them is a **straight
line**:

```csharp
using Spark.Geometry;

Line line = new(Point3d.Origin, new Point3d(1.0, 2.0, 3.0));

bool flat = line.IsPlanar();     // true - a line lies in a plane
Plane? plane = line.PlaneOf();   // null - it lies in infinitely many, and we will not pick one
```

That is an answer, not a gap. Every plane through the line contains it, so returning one would mean
inventing a rotation you did not ask for and could not predict. `PlaneOf` returns `null` in two
situations — *no* plane (a helix) and *too many* planes (a line, or a polyline whose vertices are
collinear) — and `IsPlanar` is what tells them apart.

**Flat is a question about tolerance, and the answer changes with it.** A curve a millimetre out of
plane is flat for a floor slab and is not flat for a bearing surface, so `IsPlanar` takes the
tolerance rather than assuming one:

```csharp
using Spark.Geometry;

// A gentle bow out of the xy plane - one control point lifted by five hundredths.
NurbsCurve bowed = new(
    3,
    [
        new Point3d(0.0, 0.0, 0.0),
        new Point3d(1.0, 1.0, 0.0),
        new Point3d(2.0, 1.0, 0.05),
        new Point3d(3.0, 0.0, 0.0),
    ],
    [0, 0, 0, 0, 1, 1, 1, 1]);

bool strict = bowed.IsPlanar(new Tolerance(1e-4, Angle.FromDegrees(0.001), 1e-12));   // false
bool relaxed = bowed.IsPlanar(new Tolerance(1.0, Angle.FromDegrees(0.001), 1e-12));   // true
```

**Two things worth knowing about the plane you get back.**

- **On a closed curve the normal follows the winding.** A loop and its reverse face opposite ways,
  which is what you want when you are extruding along it and is worth remembering when you are not.
- **The plane's rotation is not pinned.** `PlaneOf` promises the *normal* and the plane it defines;
  which direction ends up as the plane's x axis is not something to depend on. Use
  `Plane.FromOriginNormalXAxis` on the result if you need it fixed.

If you are looking for `Curve.Normal` from another package — this is it. Spark spells it
`PlaneOf()?.Normal`, because `NormalAt(parameter)` already means something quite different: the
direction the curve is *bending* at a point, which has nothing to do with what plane it lies in.

---

## 11. Fitting a curve through measured points

Survey points, scan points, points a user clicked — none of them lie exactly on anything.
`FromBestFit` finds the curve that comes closest:

```csharp
using System.Collections.Generic;
using Spark.Geometry;

List<Point3d> surveyed =
[
    new Point3d(0.00, 0.05, 0.0),
    new Point3d(1.00, -0.02, 0.0),
    new Point3d(2.00, 0.04, 0.0),
    new Point3d(3.00, -0.03, 0.0),
];

Line centreline = Line.FromBestFit(surveyed);   // trimmed to the span of the points
```

There are three, one per shape: `Line.FromBestFit`, `Circle.FromBestFit` and `Arc.FromBestFit`
(and `Plane.FromBestFit`, which the other two use). Each is also a constructor, so
`new Circle(points)` works in a code block.

**They refuse rather than guess, and the refusals are the useful part.**

| Ask for | Given | What happens |
|---|---|---|
| a line | points spread evenly in a ring or a ball | **refused** — every line through the middle fits equally well |
| a circle | collinear points | **refused** — no circle passes near them |
| an arc | points that double back | **refused** — they have a circle but not an arc |

That last one is worth dwelling on. Shuffled points still have a perfectly good *circle* through
them, so a naive arc fit succeeds and returns a sweep that is nonsense. `Arc.FromBestFit` checks
that the points advance around the circle in one direction, and says so when they do not — so the
order you pass them in is data, not a hint.

```csharp
using System.Collections.Generic;
using Spark.Geometry;

Circle guide = Circle.FromCenterRadius(Point3d.Origin, 2.0);

List<Point3d> alongTheArc = [];
for (int index = 0; index <= 6; index++)
{
    alongTheArc.Add(guide.PointAt(index * 0.1));
}

Arc fitted = Arc.FromBestFit(alongTheArc);
double sweep = fitted.SweepAngle.Degrees;   // about 34 - the arc the points actually cover
```

**One thing to know before you trust a fitted circle.** A radius is recovered from how much the arc
*bulges* away from its own chord, not from how long it is — so a short arc carries very little
information about its radius, and noise comparable to that bulge makes the radius genuinely
uncertain rather than merely imprecise. Spark fits it as well as it can be fitted (the naive
algebraic answer is refined until it minimises the real distance to the points, which stops the
radius coming out short on short arcs), but no method can recover what the measurements do not
contain. If your points cover twenty degrees, treat the radius as an estimate.

---

## 12. Rounding a corner

`Curve.Fillet` replaces the sharp corner where two curves cross with an arc of a given radius, and
hands back the three curves that join:

```csharp
using Spark.Geometry;

Line wall = new(new Point3d(-4.0, 0.0, 0.0), Point3d.Origin);
Line returnWall = new(Point3d.Origin, new Point3d(0.0, 4.0, 0.0));

(Arc corner, Curve first, Curve second) =
    CurveOffset.Fillet(wall, returnWall, 1.5, Vector3d.ZAxis);

double reach = first.Length;    // 2.5 - the wall, trimmed back to where the arc starts
```

**It works between any two curves**, not just two straight ones — an arc against a line, two arcs,
a spline against anything. **The arc is genuinely tangent to both**, not merely touching them at
plausible points, which is the difference between a corner that machines correctly and one that
only looks right.

Three things to know:

- **You pass the plane's normal.** Two straight lines have no plane of their own to read — which is
  why `Line.PlaneOf()` returns `null` — so the fillet asks instead of guessing. For curves that do
  have a plane, `curve.PlaneOf()!.Value.Normal` is the answer.
- **A radius too big for the corner is an error.** There is no fillet of radius 10 in a corner 2
  across, and you get a message rather than an arc that overshoots both curves.
- **It rounds the corner nearest where the curves cross.** Two curves crossing have four corners,
  and all four have a valid tangent arc; the one you can see is the one you get.

---

## 13. Making a curve longer

`Trimmed` only ever makes a curve shorter. `Extended` is the other direction — and it is what you
reach for when two curves nearly meet and you need them to actually meet:

```csharp
using Spark.Geometry;

Line wall = new(new Point3d(-4.0, 0.0, 0.0), new Point3d(-1.0, 0.0, 0.0));

Curve longer = wall.Extended(0.0, 2.0);   // two units added past the end
double reach = longer.Length;             // 5 - it was 3
```

Both numbers are **distances along the curve**, and both must be zero or more. Extending by a
negative amount would be trimming, and a member that quietly did the opposite of its name is worse
than one that refuses.

**What you get back depends on what the curve could do.**

| Curve | Extended | Result |
|---|---|---|
| `Line` | along itself | a longer `Line` |
| `Arc` | a wider sweep | a wider `Arc`, stopping at a full turn |
| `Helix` | more turns | a longer `Helix` |
| anything else | along the end tangent | a `PolyCurve` with straight tails |

**A straight tail is not a poor relation.** It meets the original smoothly — the join is
tangent-continuous, so nothing kinks — and it is the honest answer for a curve whose own
continuation is not cheap to compute. `EllipseCurve` is the case worth knowing: an ellipse carries
on perfectly well past its ends mathematically, but it does not travel at a constant speed, so
turning *two units of extension* into *how much more sweep* is an integral rather than a division.
Spark gives you the straight tail and tells you so by handing back a `PolyCurve`.

**A closed curve has no ends**, so extending a `Circle` is an error rather than a bigger circle.

```csharp
using Spark.Geometry;

// Two lines that do not meet, and a fillet that therefore cannot be made.
Line first = new(new Point3d(-4.0, 0.0, 0.0), new Point3d(-1.0, 0.0, 0.0));
Line second = new(new Point3d(0.0, 1.0, 0.0), new Point3d(0.0, 4.0, 0.0));

// Extend both until they cross, then round the corner.
Curve longerFirst = first.Extended(0.0, 2.0);
Curve longerSecond = second.Extended(2.0, 0.0);

(Arc corner, Curve trimmedFirst, Curve trimmedSecond) =
    CurveOffset.Fillet(longerFirst, longerSecond, 0.5, Vector3d.ZAxis);
```

---

## 14. A smooth curve through points, and which way it sets off

`PolyLine.FromPoints` joins points with straight segments. `NurbsCurve.InterpolatePoints` passes a
smooth curve *through* them. Its control points are solved for and are not the points you gave,
which is the difference between a curve through your data and a curve merely shaped by it:

```csharp
using System.Collections.Generic;
using Spark.Geometry;

List<Point3d> waypoints =
[
    new Point3d(0.0, 0.0, 0.0),
    new Point3d(2.0, 3.0, 0.0),
    new Point3d(5.0, 3.0, 1.0),
    new Point3d(9.0, 0.0, 1.0),
];

NurbsCurve smooth = NurbsCurve.InterpolatePoints(waypoints);
double miss = smooth.DistanceTo(waypoints[1]);   // 0 - the curve passes through every point
```

That leaves the curve to choose how it leaves the first point and arrives at the last. When those
directions matter — a road that has to meet an existing one square-on, a path that must leave a
wall at right angles — say so:

```csharp
using System.Collections.Generic;
using Spark.Geometry;

List<Point3d> waypoints =
[
    new Point3d(0.0, 0.0, 0.0),
    new Point3d(2.0, 3.0, 0.0),
    new Point3d(5.0, 3.0, 1.0),
    new Point3d(9.0, 0.0, 1.0),
];

NurbsCurve steered = NurbsCurve.InterpolatePointsWithTangents(
    waypoints,
    Vector3d.YAxis,     // sets off straight up the page
    Vector3d.XAxis);    // arrives travelling along x

Vector3d atStart = steered.TangentAt(steered.Domain.Min);   // (0, 1, 0)
Vector3d atEnd = steered.TangentAt(steered.Domain.Max);     // (1, 0, 0)
```

**Only the direction of each tangent counts.** Its length is ignored, so `Vector3d.YAxis * 5.0`
steers the same curve as `Vector3d.YAxis`. How *hard* the curve holds the direction is chosen for
you: the derivative is scaled to the total chord length of the points, which is the speed the middle
of the curve already travels at, and it is the choice that makes points sampled from an arc, given
the arc's own end tangents, come back on the arc between the samples. Choose it larger and the ends
bulge; smaller and they flatten — and both stay perfectly tangent, which is why it is not a knob.

The end tangent points **along** the curve, the way you are travelling as you arrive, not back
towards it. The degree is 3 unless you say otherwise and must be at least 2: a degree-1 curve is a
polyline and has no tangent to pin. A zero vector is refused, because it is no direction at all.

---

## Related

- [Points, vectors, planes and tolerance](geometry-basics.md) — the value layer underneath
- [Solids](solids.md) — surfaces, solids, and what the kernel does rather than this layer
- [Lacing](lacing.md) — what happens when you feed a list of centers to one circle node
