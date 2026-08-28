---
id: concepts.curves
title: Curves, parameters and arc length
nodes: [Line.ByStartPointEndPoint, Circle.ByCentreRadius, Arc.ByThreePoints, Ellipse.ByPlaneRadii, PolyLine.ByRegularPolygon, PolyCurve.ByJoinedCurves, Curve.PointAtParameter, Curve.PointAtLength, Curve.DivideEqually, Curve.DivideByLength]
related: [concepts.geometry-basics, concepts.lacing]
since: "0.1"
---

**Status:** Current. Describes `Spark.Geometry`'s curve layer, which exists and is tested.
**Owner:** `geometry-kernel`
**Last updated:** 2026-08-28

> **Scope.** Six curve types exist today: `Line`, `Arc`, `Circle`, `EllipseCurve`, `PolyLine`
> and `PolyCurve`. There is no `NurbsCurve` yet, and there is no curve intersection,
> offsetting, projection or closest-point query — those arrive at M3. There are still no
> surfaces, meshes or solids. Every example below was run against the assembly.

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

On a line and on a circle these two agree, because those curves travel at a constant speed. On
an **ellipse** they do not: the curve moves quickly past the ends of the long axis and slowly
past the ends of the short one, so equal steps in parameter cover unequal distances. (An
ellipse's *quarter* marks do agree, because its four quadrants are congruent — which is why
the example below uses an eighth rather than a quarter.)

```csharp
using Spark.Geometry;

EllipseCurve ellipse = EllipseCurve.ByPlaneRadii(Plane.WorldXY, 3.0, 1.0);

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

Circle circle = Circle.ByCentreRadius(Point3d.Origin, 10.0);
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

---

## 3. Each curve has its own domain, and none of them is 0 to 1

Ask, do not assume:

| Curve | Domain | Meaning |
|---|---|---|
| `Line` | 0 → 1 | fraction of the way along |
| `Circle` | 0 → 2π | radians from the plane's x axis |
| `Arc` | 0 → sweep | radians from the arc's **own** start |
| `EllipseCurve` | 0 → sweep | the eccentric angle, not the angle at the centre |
| `PolyLine` | 0 → n | one unit per segment, so whole numbers are the vertices |
| `PolyCurve` | 0 → n | one unit per segment, so whole numbers are the joints |

`Interval.Normalise` and `Interval.Denormalise` convert between a domain parameter and a
fraction, which is exactly what the node layer does for you:

```csharp
using Spark.Geometry;

PolyLine path = PolyLine.ByPoints(
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
Arc bend = Arc.ByThreePoints(
    new Point3d(1.0, 0.0, 0.0),
    new Point3d(0.0, 1.0, 0.0),
    new Point3d(-1.0, 0.0, 0.0));

// Closed shapes are polylines, not their own types: a polygon is a closed polyline, and a
// rectangle is a factory rather than a class of its own.
PolyLine hexagon = PolyLine.ByRegularPolygon(Plane.WorldXY, 2.0, 6);
PolyLine frame = PolyLine.ByRectangle(Plane.WorldXY, 4.0, 3.0);

// Chained. The join tolerance is passed, never assumed, and a chain that does not meet
// within it is refused rather than silently accepted with a gap in it.
PolyCurve chain = PolyCurve.ByJoinedCurves([straight, bend]);
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

Circle circle = Circle.ByCentreRadius(Point3d.Origin, 1.0);

Curve moved = circle.TransformedBy(Transform.Scale(1.0, 1.0, 3.0));   // fine: still a circle
// circle.TransformedBy(Transform.Scale(2.0, 1.0, 1.0));              // throws: that is an ellipse
```

---

## 6. Curves are immutable

Every operation returns a new curve and leaves yours alone. The names say so — `Reversed`,
`Trimmed`, `TransformedBy` — rather than reading as commands to change something in place.

```csharp
using Spark.Geometry;

Circle circle = Circle.ByCentreRadius(Point3d.Origin, 5.0);
Curve half = circle.Trimmed(new Interval(0.0, System.Math.PI));   // an Arc, not a Circle

double untouched = circle.Length;   // still the full circumference
```

Trimming a circle giving back an *arc* is the one place an operation changes the type of the
thing it was given, and it is right: a circle that is not closed is not a circle.

---

## Related

- [Points, vectors, planes and tolerance](geometry-basics.md) — the value layer underneath
- [Lacing](lacing.md) — what happens when you feed a list of centres to one circle node
