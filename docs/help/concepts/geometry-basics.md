---
id: concepts.geometry-basics
title: Points, vectors, planes and tolerance
nodes: []
related: [concepts.lacing]
since: "0.1"
---

**Status:** Current. Describes `Spark.Geometry`'s value layer, which exists and is tested.
**Owner:** `geometry-kernel`
**Last updated:** 2026-08-27

> **Scope.** Spark's geometry kernel currently contains **values only** — points, vectors,
> planes, transforms, intervals, boxes, angles and tolerances. There are no curves, no
> surfaces, no meshes and no solids yet; those arrive at M3 and M5. Everything on this page
> is real today, and every example below was run against the assembly rather than written
> from memory.

---

## Why this page exists

You do not need a maths degree to model in Spark, but you do need four ideas, because Spark
is opinionated about all four and will refuse to guess on your behalf:

1. A **point** and a **vector** are different things, and Spark makes you say which you mean.
2. Coordinates are **right-handed** and **unitless**.
3. An **angle** is its own type, not a bare number.
4. **Tolerance** is a value you pass in, not a setting somewhere.

Each of these costs you a few extra keystrokes and buys you a class of bug you will never
have. This page explains what each one is and why Spark takes the position it does.

---

## 1. Points and vectors are not the same thing

A **point** is a position. `(3, 4, 0)` as a point means *the place three units east and four
units north of the origin*. Move the origin and the point means somewhere else.

A **vector** is a displacement — a direction with a magnitude and no position at all.
`(3, 4, 0)` as a vector means *five units in that direction*, and it means exactly that
wherever you happen to be standing. Translating a point moves it; translating a vector does
nothing to it.

Confusing the two is one of the classic hard-to-see geometry bugs, so Spark separates them
into `Point3d` and `Vector3d` and only lets you cross between them with an explicit cast.
The arithmetic that *is* meaningful is available and reads exactly as it should:

```csharp
using Spark.Geometry;

Point3d door = new(0.0, 0.0, 0.0);
Point3d window = new(3.0, 4.0, 0.0);

Vector3d run = window - door;           // point − point = the vector between them: (3, 4, 0)
double span = run.Length;               // 5
double same = door.DistanceTo(window);  // 5, and cheaper to read

Point3d moved = door + run;             // point + vector = another point: (3, 4, 0)
Point3d centre = door.Midpoint(window); // (1.5, 2, 0)
Point3d quarter = Point3d.Lerp(door, window, 0.25); // (0.75, 1, 0)
```

Notice what is missing. There is no `point + point`, because the sum of two positions is not
a position — that question almost always means "give me the average", so use `Midpoint` or
`Lerp` and say so. `Lerp` does **not** clamp its parameter: `t = 1.5` extrapolates past the
end point, which is usually what a caller wanted.

### Missing positions

`Point3d.Origin` is `(0, 0, 0)` and is also what a default-constructed `Point3d` holds. That
is deliberate: a default point is a real position at the origin, not a missing one. When you
need to say "there is no position here", use `Point3d.Unset`, whose coordinates are all
`NaN` — and test for it with `IsValid`, never with `==`:

```csharp
Point3d nothing = Point3d.Unset;

bool valid = nothing.IsValid;      // false  — this is the test you want
bool same  = nothing == nothing;   // false! IEEE says nothing equals a NaN
bool equal = nothing.Equals(nothing); // true — so unset points still work as dictionary keys
```

That `==` returning `false` for a value compared with itself looks like a bug and is not.
`operator ==` on every value type in the kernel is **exact** and follows IEEE 754, which says
a `NaN` is equal to nothing at all, itself included. `Equals` follows `double.Equals` instead
and treats `NaN` as equal to `NaN`, so hashing and dictionary lookup behave sensibly. If you
want a geometric comparison, that is neither of them — it is `EqualsWithin`, covered in
[section 5](#5-tolerance-and-why-you-have-to-pass-it).

---

## 2. Right-handed, and what that actually means for you

Spark is **right-handed** everywhere, with no exceptions anywhere in the kernel. The
practical statements are these:

- X crossed with Y gives **+Z**. Point the fingers of your right hand along X, curl them
  towards Y, and your thumb points along Z.
- A **positive rotation about an axis is counter-clockwise when you look down that axis from
  its positive end back towards the origin.** So a positive rotation about +Z carries +X
  towards +Y.
- A plane's normal defines its positive side, and that is the side a signed distance reports
  as positive.

```csharp
Vector3d up = Vector3d.XAxis.Cross(Vector3d.YAxis);  // (0, 0, 1) — exactly ZAxis

Angle turn = Vector3d.XAxis.SignedAngleTo(Vector3d.YAxis, Vector3d.ZAxis);
double degrees = turn.Degrees;   // +90 — counter-clockwise seen from +Z

Angle other = Vector3d.YAxis.SignedAngleTo(Vector3d.XAxis, Vector3d.ZAxis);
double negative = other.Degrees; // −90 — the same rotation the other way
```

One consequence catches people out, so it is stated plainly: `Plane.WorldXZ` has a normal of
**−Y**, not +Y. Its X axis is the world X axis and its Y axis is the world *Z* axis, and the
only normal that keeps that frame right-handed points along −Y. This is not an oversight and
must not be "corrected".

`Vector3d.AngleTo` is the unsigned partner: it always returns something in `[0, 180]`
degrees and needs no axis, because it is not telling you which way round.

---

## 3. Planes

A **plane** in Spark is an infinite flat surface carried as an origin plus a right-handed
orthonormal frame: an `XAxis`, a `YAxis` and a `Normal`, where `XAxis × YAxis == Normal`. It
is more than the geometric plane, because it also carries a *coordinate system on* that
plane, which is what lets you flatten 3D work into 2D and back.

Every factory orthonormalises whatever you hand it, so you can pass convenient,
non-perpendicular directions and still get a usable frame back.

```csharp
Plane floor = Plane.WorldXY;                 // origin (0,0,0), normal +Z
Point3d light = new(2.0, 3.0, 2.7);

double height = floor.DistanceTo(light);     // 2.7 — signed, positive on the normal's side
Point3d below = floor.ClosestPoint(light);   // (2, 3, 0)
Point2d flat  = floor.To2d(light);           // (2, 3) — the Z component is simply dropped
Point3d back  = floor.To3d(new Point2d(2.0, 3.0)); // (2, 3, 0)

bool onIt   = floor.Contains(light);                          // false — 2.7 away
bool nowOn  = floor.Contains(new Point3d(2.0, 3.0, 0.0));      // true
```

Build a plane from three points when that is how you have the geometry. The normal follows
the right-hand rule for the points **in the order you gave them**, so swapping any two of
them flips it:

```csharp
Plane wall = Plane.ByThreePoints(
    new Point3d(0.0, 0.0, 0.0),
    new Point3d(5.0, 0.0, 0.0),
    new Point3d(5.0, 0.0, 3.0));

Vector3d facing = wall.Normal;                          // (0, −1, 0)
double side = wall.DistanceTo(new Point3d(0.0, 1.0, 0.0)); // −1: one unit behind the wall
Plane turned = wall.Flip();                             // same plane, normal reversed
```

Three collinear or coincident points define no unique plane, and `ByThreePoints` throws
`ArgumentException` rather than inventing one.

### `default(Plane)` is not a plane

`Plane` is a struct, which is the right choice for something that appears once per element
when a node replicates over a list of a hundred thousand — a class would put an allocation
and a pointer chase on that path for nothing. The price of that choice is that
`default(Plane)` exists, and it has a zero normal, and a zero normal is not a plane.

Spark makes that loud rather than quiet. Every geometric question asked of a default plane
throws:

```csharp
Plane nothing = default;

bool valid = nothing.IsValid;    // false — this always works and never throws

// This throws InvalidOperationException:
//   "A default-constructed Plane has no origin, no normal and no frame, so no geometric
//    question can be answered about it."
bool oops = nothing.Contains(new Point3d(1.0, 2.0, 3.0));
```

That behaviour is worth knowing about for a reason beyond the API. In the first version of
this kernel, `default(Plane).Contains(anyPoint)` returned **`true`** — every point in space
silently lay on the null plane, and both tests written to guard the type were structurally
incapable of noticing. The build was green throughout. It was an independent review, not a
gate, that caught it. Where you see a member of this kernel being noisy about a degenerate
input, that is why.

`Vector3d` takes the same line: `Normalised()` on a zero-length vector throws rather than
returning zero and letting a meaningless direction propagate. Use `TryNormalise` where
failure is an expected outcome rather than a mistake.

```csharp
Vector3d flat = Vector3d.Zero;

bool ok = flat.TryNormalise(out Vector3d unit);   // false; unit is left unusable
Vector3d boom = flat.Normalised();                // throws InvalidOperationException
```

---

## 4. Coordinates are unitless, and angles are typed

### Unitless coordinates

A coordinate of `1.0` in Spark is one **unit**. It is not one metre, one foot or one
millimetre, and the kernel neither knows nor can know which you meant. There is no
`UnitSystem`, no unit-carrying numeric type and no conversion anywhere — this is PRD decision
**D12**, and it is a decision rather than an omission.

The reason is that a unit system that is only *nearly* right is worse than none. Import and
export therefore work in the file's own units and say so, and the meaning of "one unit" is
established by your model and your consistency rather than by the software.

What this asks of you in practice:

- **Pick a unit before you start, and stay in it.** If a room is 8000 long, everything else
  in that model is in millimetres too.
- **Convert at the edges, not in the middle.** If a supplier's data is in inches and your
  model is in millimetres, scale it once on the way in.
- **Tell the kernel your scale where robustness depends on it.** That is what
  `Tolerance.ForScale` is for, and it is the subject of the next section.

Unitless does *not* mean scale-blind. It means the scale is information you supply rather
than information the type system carries.

### Angles

`Angle` is a distinct type, and there is deliberately **no implicit conversion from
`double`**. This is ADR-0011, and the argument is one line long: `Rotate(plane, 0.5)` must
not compile, because no reader can tell whether the author meant half a degree or half a
radian.

```csharp
Angle right  = Angle.FromDegrees(90.0);
Angle same   = Angle.FromRadians(Math.PI / 2.0);
double rad   = right.Radians;    // 1.5707963267948966 — radians are the stored form
double deg   = right.Degrees;    // 90

Angle sum    = Angle.FromDegrees(30.0) + Angle.FromDegrees(60.0); // 90°
Angle half   = Angle.HalfTurn;                                     // 180°
Angle third  = Angle.FullTurn / 3.0;                               // 119.99999999999999° — see below
```

An `Angle` is an unbounded *quantity*, not a direction: holding ten full turns or minus three
radians is perfectly legal, and `Angle.Zero` is what a default-constructed one holds. When
you want a canonical representative, ask for one:

```csharp
double wrapped = Angle.FromDegrees(370.0).Normalised().Degrees;
// 9.99999999999999 — [0, 360), and note it is not exactly 10

double signed = Angle.FromDegrees(270.0).NormalisedSigned().Degrees;
// −90 — (−180, 180]
```

That `9.99999999999999` is not a defect and is not hidden from you. Degrees are converted to
radians on the way in and back on the way out, and two conversions of an irrational factor do
not round-trip exactly. It is the first practical reason you will meet for the next section.

---

## 5. Tolerance, and why you have to pass it

### What tolerance is

Computers store coordinates as `double`s, which have about fifteen or sixteen significant
figures. Almost every geometric operation — rotating, intersecting, projecting, normalising —
ends in a number that is very slightly wrong in the last few of those figures. Rotate the X
axis a quarter turn about Z and you do not get `(0, 1, 0)`:

```csharp
Vector3d turned = Vector3d.XAxis.Rotate(Vector3d.ZAxis, Angle.FromDegrees(90.0));
// (6.123233995736766E-17, 1, 0)
```

That `6.1e-17` is not a bug in the rotation. It is `cos(π/2)` as a `double`, and no amount of
care makes it zero. So "is this point on that plane?" and "are these two lines parallel?"
cannot be answered by exact comparison — asked exactly, the answer is almost always *no*, and
uselessly so.

A **tolerance** is how close counts as touching. `Tolerance` in Spark carries three numbers:

| Component | What it answers | Default |
|---|---|---|
| `Linear` | How far apart two positions can be and still count as coincident | `1e-6` |
| `Angular` | How far two directions can diverge and still count as parallel | `0.001°` |
| `RelativeEpsilon` | How many significant figures must agree at large magnitudes | `1e-12` |

The third exists because the first is not enough on its own. At coordinates around `1e12`, an
absolute `1e-6` has fallen below what a `double` can even represent, so a purely absolute test
silently degenerates into bit-equality. Every `EqualsWithin` in the kernel uses the hybrid
rule — the larger of the absolute and the relative threshold — so it keeps meaning something
at both ends of the range.

```csharp
Tolerance tol = Tolerance.Default;   // Linear=1E-06, Angular=0.001°, Relative=1E-12

bool close = tol.AreEqual(2.0, 2.0000001);  // true  — 1e-7 apart, inside 1e-6
bool apart = tol.AreEqual(2.0, 2.000001);   // false — 1e-6 apart, on the far side
bool below = tol.IsLessThan(2.0, 2.000001); // true

Point3d far  = new(1_000_000.0, 0.0, 0.0);
Point3d near = new(1_000_000.0000005, 0.0, 0.0);

bool touching = far.EqualsWithin(near);  // true  — the geometric question
bool identical = far == near;            // false — the exact one, and both are useful
```

`AreEqual`, `IsLessThan` and `IsGreaterThan` are a genuine three-way partition: for any pair
of non-`NaN` operands, exactly one of them is true. That sounds obvious and was not. An
earlier version computed each of the three against a slightly different subtraction, and the
roundings disagreed by one unit in the last place exactly on the boundary — so `2.0` against
`2.000001` fell into **none** of the three buckets while the documentation invited callers to
rely on the partition. All three now compare the same single subtraction against the same
single threshold, which makes the property hold by construction rather than by luck.

### Why you pass it rather than set it

Most CAD software has a document tolerance you set once in a preferences dialogue. Spark does
not, and there is no ambient, static or thread-local default anywhere in the kernel. This is
ADR-0010.

The reason is Spark's evaluation cache. Every node's result is cached against a key derived
from its inputs, and tolerance is part of that key. An ambient tolerance would be invisible to
the key — so changing it would invalidate nothing, and your graph would go on serving you
geometry computed at the old tolerance, silently, with no way of telling from the screen. A
setting that quietly fails to take effect is worse than no setting.

So every predicate takes tolerance as an argument. In return, the ergonomics are kept cheap:
the parameter is optional, and omitting it means the default.

```csharp
Point3d a = new(1.0, 2.0, 3.0);
Point3d b = new(1.0000001, 2.0, 3.0);

bool byDefault = a.EqualsWithin(b);                            // default tolerance
bool explicitly = a.EqualsWithin(b, Tolerance.Default);        // identical, and says so
bool loose = a.EqualsWithin(b, Tolerance.ForScale(1000.0));    // a coarser question
```

One trap is worth naming. A default-constructed `Tolerance` means **"use the default"**, not
"compare exactly". `Tolerance.Default == default` is `true`, and reading `Linear` on a
default-constructed value gives you `1e-6` rather than zero. If you want exact comparison,
that is what `operator ==` is for.

### Scale awareness

Because coordinates are unitless, no single tolerance can be right for every model. A linear
tolerance of `1e-6` is sensible for a model measured in metres, absurdly tight for one
measured in kilometres and uselessly loose for one measured in microns. `ForScale` derives a
tolerance from a characteristic length — typically the diagonal of the bounding box of what
you are working on:

```csharp
Tolerance site = Tolerance.ForScale(1000.0);   // Linear = 0.001
Tolerance detail = Tolerance.ForScale(0.001);  // Linear = 1e-9

Tolerance coarser = Tolerance.Default.Scaled(10.0);  // Linear = 9.999999999999999e-6
```

Only the linear component scales. The angular tolerance and the relative epsilon are both
dimensionless and are therefore already scale-free, so `ForScale` leaves them alone.

Scale-aware tolerance is built into this kernel from its first commit rather than retrofitted,
because retrofitting it means revisiting every predicate that was written assuming a fixed
epsilon. `Tolerance.ForScale` and the hybrid absolute/relative rule are the whole of that
mitigation.

---

## 6. Intervals: direction is not validity

One small type is worth calling out, because its rule is the opposite of what a careful
programmer guesses. An `Interval` is a pair of bounds, and a **decreasing** interval — one
whose `Min` exceeds its `Max` — is a perfectly good value, not a broken one. It is what a
reversed curve's domain looks like.

```csharp
Interval reversed = new(1.0, 0.0);

bool valid = reversed.IsValid;         // true  — both bounds are finite
bool backwards = reversed.IsDecreasing; // true  — this is the question about direction
double length = reversed.Length;        // −1, signed, following the direction
```

`IsValid` asks only whether both bounds are finite. It deliberately does not require
`Min <= Max`, because the guard everybody writes without thinking — `if (!domain.IsValid)
throw` — would then reject every reversed curve in the model.

---

## What is not here yet

Honest scope, so you do not go looking:

| Concept | State |
|---|---|
| `Point3d`, `Vector3d`, `Point2d`, `Vector2d`, `UV` | Implemented and tested |
| `Angle`, `Tolerance`, `Interval`, `BoundingBox` | Implemented and tested |
| `Plane`, `CoordinateSystem`, `Transform` | Implemented and tested |
| Lines, arcs, circles, polylines, NURBS curves | M3 — not written |
| Surfaces, meshes, tessellation | M5 — not written |
| BRep solids, booleans | M6 — not written |

---

## See also

- [Lists, ranks and lacing](lacing.md) — what happens when you feed a node a list of points
  instead of one.
- ADR-0010 — why tolerance is explicit and scale-aware rather than ambient.
- ADR-0011 — why `Angle` is a type with no implicit conversion from `double`.
- ADR-0002 — why Spark owns a managed geometry kernel at all.
