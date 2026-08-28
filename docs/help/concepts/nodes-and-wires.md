---
id: concepts.nodes-and-wires
title: Nodes, ports and wires
nodes: []
related: [concepts.evaluation, concepts.lacing, concepts.design-language]
since: "0.1"
---

**Status:** Current. Describes the node model in `Spark.Engine` and the first-party library in
`Spark.Nodes.Core`, both of which exist and are tested. Every count and every message quoted
below was **read out of the built assemblies**, not written from memory.
**Owner:** `graph-engine`
**Last updated:** 2026-08-28

---

## The three pieces

A Spark graph has exactly three kinds of thing in it, and nothing else.

| | What it is |
|---|---|
| **Node** | One operation. `Point.ByCoordinates` makes a point; `Math.Add` adds two numbers. |
| **Port** | One named slot on a node. Inputs on the left, outputs on the right. |
| **Wire** | A value travelling from one output port to one input port. |

That is the whole vocabulary. Everything else in this topic is a rule about how those three
behave.

---

## 1. Nodes

A node is one operation with a name, a category, some input ports and at least one output
port. A node with no output port cannot exist — even an operation that only has an effect on
the outside world carries one, so that you have somewhere to attach a wire and say *do this
after that*.

### Where node names come from

**A node is a public method, property or constructor in a .NET assembly, and its name is the
type and member it came from.** `Point.ByCoordinates` is the `ByCoordinates` method on the
`Point` type. Nothing registers it, nothing lists it in a manifest, and there is no file
anywhere mapping members to node names.

The first-party library goes through exactly the same door a stranger's NuGet package goes
through. It cannot do otherwise — `Spark.Nodes.Core` is forbidden from referencing the engine,
so it has no way to register anything even if someone wanted to. If the importer breaks for
your assembly, it broke for ours first.

**A node's description is the `///` comment its author wrote.** Not a lookup table, not a
wiki page — the XML documentation comment the compiler already produced, read from the `.xml`
file beside the assembly. Rename the method and the comment moves with it. Delete the method
and the description goes with it. It is not possible for a node's description to be about a
member that no longer exists.

```text
/// <summary>Makes a point from its three world coordinates.</summary>
public static Point3d ByCoordinates(double x = 0, double y = 0, double z = 0)

becomes

  node        Point.ByCoordinates
  tooltip     Makes a point from its three world coordinates.
  inputs      x  (double, default 0)
              y  (double, default 0)
              z  (double, default 0)
  output      point (Point3d)
  category    Point
```

An assembly that ships without its `.xml` file is not an error — plenty of NuGet packages do.
Its nodes simply have no descriptions, which is a visible gap rather than a wrong answer.

### Every node carries the package it came from

Two packages may both publish a `Curve.Offset`. A saved graph therefore stores
`package/name` — `Spark.Nodes.Core/Point.ByCoordinates` — not just the display name, so that
reopening it on another machine binds to the definition it was authored against or reports
that it cannot find it. The failure mode this prevents is the worst one available: silently
binding to somebody else's node and producing geometry rather than an error.

### What is in the library today

`Spark.Nodes.Core` contributes **27 nodes**:

```text
Math          7    Add, Subtract, Multiply, Divide, Sin, Cos, Pi
Vector        7    ByCoordinates, XAxis, YAxis, ZAxis, ByTwoPoints, Scale, Length
Point         5    ByCoordinates, Origin, Translate, Distance, Coordinates
BoundingBox   2    ByCorners, Centre
Plane         2    ByOriginNormal, XY
Number        2    Value, Range
Colour        1    ByRgb
Display       1    ByGeometryColour
```

**There are no curve, surface, solid or mesh nodes**, because there are no curves, surfaces,
solids or meshes in the geometry kernel yet. See
[`concepts.geometry-basics`](geometry-basics.md) for what the kernel does hold.

### Some public members do not become nodes, and each says why

The importer never skips a member silently. Every public member of an assembly comes back as
either a node or an exclusion with a stated reason, and you can read the reasons. In
`Spark.Nodes.Core` there is exactly one:

```text
Number.MaximumRangeCount
  a field is a value rather than an operation; a node that returns a constant
  is written as a method.
```

The categories of exclusion, each with the reason the importer gives:

| Not imported | Because |
|---|---|
| Generic types and generic methods | a canvas has no way to bind a type argument |
| Extension methods | not surfaced on their receiver in this slice |
| Operators | operator harvesting is a later slice; the named method it forwards to is the node |
| Nested types | hoist the type to import it |
| Indexers | a list-item node covers the same ground |
| `ref` and `in` parameters | both an input and an output, which no port shape expresses |
| Write-only properties | they only mutate, and graph values are immutable |
| `void` methods with no `out` parameter | they produce no value a graph can carry |
| Enums, interfaces, delegates, attributes | a value set, no implementation, a function value, and authoring metadata respectively |

A **constructor is suppressed** when a `By*`, `From*` or `Create*` factory on the same type
takes the same parameter types and returns the same type — so you get `Point.ByCoordinates`
rather than both that and a bare `Point.Point`. The match is on parameter *types*, not names,
because `centre` against `center` would fail a name comparison and emit both nodes, which is
exactly what the rule exists to prevent.

---

## 2. Ports

A port has a name, a type, and a **rank**.

**Rank is how deeply nested a value the port wants.** A port declared `double` is rank 0 — one
number. A port declared `IReadOnlyList<double>` is rank 1 — a list of numbers. Rank is what
decides whether handing a node a list runs it once or runs it ten times, and the full rules
are in [`concepts.lacing`](lacing.md).

Two answers that look wrong and are deliberate: `string` is **rank 0**, even though it is a
sequence of characters, and `object` is **rank 0**, even though it can hold anything. A port
declared `object` wants a single value, so giving it a list makes the node run once per item.

### An unwired port holds a literal

Every input port that has nothing wired to it holds a value you can type, edited in the
properties panel. A port whose author gave the parameter a default starts at that default;
one with no default starts at the type's zero value, so that a node you have just placed
produces something rather than erroring before you have touched it.

**Wiring a port hides its literal.** The wire wins, the box greys out, and the literal is kept
so that deleting the wire restores what you had typed.

### Output ports, and nodes with more than one

Most nodes have one output. A node can have several — the importer gives every `out`
parameter an output port of its own:

```text
Point.Coordinates          in: point        out: x, y, z

  Point.ByCoordinates(3, 4)  →  (3, 4, 0)
  Point.Coordinates          →  x = 3   y = 4   z = 0
```

---

## 3. Wires: refused, accepted, or accepted with a warning

**A wire is checked when you draw it, not when the graph runs.** That is the single most
important thing about wiring in Spark. The wire under your cursor is already red before you
release the button, and a graph that opens is a graph whose wires are all legal.

When you drop a wire the engine tries seven rules **in order**, and the first that matches
wins.

| # | Rule | Result |
|---|---|---|
| 0 | Both ports name the same type from **different assemblies** | **refused**, `SPK1011` |
| 1 | The types are the same, or the source is assignable to the target | accepted |
| 2 | A numeric widening that cannot lose information — `int` into `double` | accepted |
| 3 | A conversion registered with the session | accepted, and **warned** if lossy |
| 4 | The source or target type declares an `implicit operator` between them | accepted, warned |
| 5 | **Rank lifting** — a list into a scalar port, or a scalar into a list port | accepted |
| 6 | The target port takes any value | accepted |
| — | Nothing matched | **refused**, `SPK1010` |

### Refused

```text
Point.ByCoordinates ──> Number.Value

  SPK1010  'Point3d' cannot be connected to a port declared 'Double'.
           Insert a conversion node between them — narrowing and parsing are
           never applied automatically, so that the conversion is visible on
           the canvas.
```

That last sentence is the policy, and it is deliberate. **Widening is automatic; narrowing and
parsing are not.** Turning a `double` into an `int`, or text into a number, throws information
away or can fail outright, and Spark's position is that a decision like that should be a node
you can see rather than something a wire did quietly inside itself.

The rule at position 0 is worth its place on its own. Two packages can each define
`Acme.Widget`, and wiring one into the other would otherwise produce *cannot cast Widget to
Widget* at run time — a message nobody can act on. Caught here, it becomes a design-time
message naming both packages, which is a bug report someone can actually file.

### Accepted

```text
Number.Range ──> Point.ByCoordinates.x        rule 5, rank lifting
                 (IReadOnlyList<double> into a double port)

Point.Origin ──> Point.Translate.point        rule 1, direct
Point.Origin ──> Display.ByGeometryColour.geometry
                 (Point3d into an object port — still rule 1, because
                  every value is assignable to object)
```

Rank lifting is not a special case, it is the ordinary way a graph is built. A list of ten
numbers into a port that wanted one number means *run the node ten times*, which is the whole
point of a node graph. A single number into a port that wanted a list means *make a list of
one*. Both are silent and both are expected.

### Accepted, with a warning

A conversion that may lose information is allowed — you get to decide, having been told. The
wire is drawn in the warning colour and its tooltip names the conversion:

```text
  SPK1013  'Double' is converted to 'Int32', which may lose information.
```

> **A limit of the current build, stated plainly.** Registered conversions take part in
> deciding whether a wire is *allowed*, but the engine does not yet apply the conversion to
> the value when the graph runs. A wire accepted through rule 3 or rule 4 therefore fails at
> run time with `SPK1041`, naming the two types. The conversions built into the language —
> rule 1, rule 2 and rule 6 — are unaffected and work end to end.

### Loops are refused

```text
  SPK1012  Connecting 'Math.Multiply' to 'Math.Add' would close a cycle. A
           dataflow graph has no way to evaluate one, so the wire is refused
           rather than the graph hanging later.
```

A wire onto a node's own input is refused for the same reason. What happens when a loop
arrives inside a saved file instead is covered in
[`concepts.evaluation`](evaluation.md#6-loops).

### How many wires a port takes

- **An input port takes at most one wire.** Drop a second onto an occupied port and it
  **replaces** the first rather than being refused — that is what everybody expects, and the
  old wire disappears as you release. If you need to combine two values, that is what a list
  node is for.
- **An output port feeds as many wires as you like.** The value is computed once and shared.

Wires have no identity of their own. Two wires between the same four things are the same wire,
and there is nothing you can set on one.

---

## 4. Category colours

Every node belongs to a **category**, and the category decides the colour of its header. This
is not decoration: below 40% zoom a node is drawn as a plain coloured rectangle with no text
at all, so at that scale colour is the *only* thing carrying identity.

| Category | Colour | What lives there |
|---|---|---|
| Input & constants | `#E8C45A` amber | `Number.Value`, `Number.Range` |
| Logic | `#B6C455` olive | *(nothing yet)* |
| Display & preview | `#71C862` green | `Display.ByGeometryColour`, `Colour.ByRgb` |
| Geometry · surface & solid | `#33B992` teal | `Plane.*`, `BoundingBox.*` |
| Geometry · curve | `#4CBCD4` cyan | *(nothing yet — there are no curves)* |
| Geometry · point & vector | `#5AA2EA` blue | `Point.*`, `Vector.*` |
| Script & code | `#7789EA` indigo | *(nothing yet)* |
| Lists | `#E489C4` pink | *(nothing yet)* |
| Math | `#DE7B50` orange | `Math.*` |
| Custom & uncategorised | `#9AA3B2` grey | anything else |

There are ten and not fifteen because ten mutually distinguishable hues that all clear 3:1
against the canvas is close to the limit of what is achievable. The colours are separated in
**lightness** as well as hue — at least 2.77 L\* apart — so the set survives a greyscale
screenshot and protanopia.

**A category is a plain name, not a fixed list.** A package can file its nodes under a
category Spark has never heard of; it gets the grey *Custom* colour, which is a legible
outcome rather than a failure.

**No task in Spark requires telling two categories apart by colour alone.** A category glyph
appears in the header at 73% zoom and above, a tooltip names the node on hover at any zoom
including the colour-only one, and the library tree is the authoritative statement of what
category a node is in.

Two rules that keep the palette readable, and which a theme may not break: category colours
are only ever **fills**, never strokes; and semantic colours — error red, warning amber — are
only ever **strokes**, never fills. That is what stops the green of *Display* from reading as
success and the orange of *Math* from reading as a warning.

---

## 5. A worked example, start to finish

The graph the application opens on. Two ranges, a point, and a deliberate mistake:

```text
Number.Range (0, 9, 1) ─────> x ┐
Number.Range (0, 9, 1) ─────> y ├─ Point.ByCoordinates ──> Display.ByGeometryColour
Number.Value (1)       ─────> z ┘        │                        ▲
                                         │      Colour.ByRgb ─────┘
                                         │
                                         └────> Point.Translate.point
        Vector.ZAxis ─────────────────────────> Point.Translate.direction
        Math.Divide (1 ÷ 0) ──────────────────> Point.Translate.distance
```

`Point.ByCoordinates` is set to **Cross Product** lacing, so ten *x* values crossed with ten
*y* values give **100 points** — a ten-by-ten grid, which is what appears in the viewport.
Switch that one node to Longest and the same two ranges give a ten-point diagonal instead:
same inputs, same wires, different lacing.

`Math.Divide` has been given a divisor of zero on purpose. It wears a red error ring and is
the only node on the canvas that does. `Point.Translate` downstream is greyed, dashed and
marked *not evaluated* — it is not blamed, because there is nothing wrong with it. The rest of
the graph, including all 100 points, evaluates and draws normally.

Why that is the behaviour, and what to do about it, is
[`concepts.evaluation`](evaluation.md#5-why-the-node-after-an-error-goes-grey-not-red).

---

## Related

- [`concepts.evaluation`](evaluation.md) — what happens when the graph runs, and what the
  colours on a node mean afterwards
- [`concepts.lacing`](lacing.md) — rank, replication, and what happens when you give a node a
  list where it wanted one thing
- [`concepts.geometry-basics`](geometry-basics.md) — the types that travel along the wires
- [`concepts.design-language`](design-language.md) §7 — the full colour and state specification
