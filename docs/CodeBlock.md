# Getting started with the code block

**Last updated:** 2026-09-08

A **code block** is a node whose body is C# you type. It is the escape hatch: when the library
does not have the node you want, or when five nodes would say what one line says, you write the
line instead.

This is a tutorial — read it top to bottom once and you will know how to write one. The reference
that ships inside the application is [`help/concepts/code-blocks.md`](help/concepts/code-blocks.md);
it says the same things in less order and more detail.

> **Every example below has been run.** The code is copied out of this file, put into a real
> graph, and evaluated. Where a value is quoted, that is what came back.

---

## 1. Your first block

**Double-click empty canvas.** A block lands where you clicked, empty, already in edit mode with
the caret in it — start typing. *Insert → Code block* does the same at the next free spot.

Type this:

```csharp
2 + 2;
```

Click away to commit. The block now has one output port carrying `4`.

Three things just happened, and each is a rule you will use constantly:

1. **A line ending in `;` that is just a value becomes an output port.** No `return` needed.
2. **Clicking away is what commits.** The block recompiles, its ports are worked out again, and
   the graph re-runs. It does not recompile on every keystroke — but the red squiggles do keep up.
3. **One click gets you back in**, with the caret after the last character. Click the block to
   edit it; drag it to move it. A drag is a click that moved.

**Escape also commits and closes.** Ctrl+Z takes the edit back if you did not mean it — nothing can
bring typing back the other way, which is why Escape does not discard.

---

## 2. Inputs: names you use but do not declare

You never add an input port with a button. **You add one by using a name your code has not
declared.**

```csharp
radius * 2;
```

That block has one input port called `radius` and one output. Nothing declared `radius`, so Spark
treats it as something the graph supplies. Wire a number in, and the block runs.

Several free names give several ports, in the order they first appear:

```csharp
width * height;
```

`width`, then `height`. That one line is a whole node: two inputs, one output.

**A variable you declare is not an input**, because you declared it:

```csharp
var doubled = radius * 2;
doubled * 3;
```

Still one input, `radius`.

> **The rule in one sentence:** if the compiler cannot find the name, it becomes an input port.
> That is literally how it works — Spark compiles your code, and turns each "the name `radius`
> does not exist" error into a port.

---

## 3. Outputs: one port per line that makes something

This is Dynamo's Code Block rule, and Spark follows it exactly.

```csharp
5;
5.0 + 6;
"hello";
var n = 100;
var t = 0..1..#10;
0..#6..10;
```

Six lines, six ports, in order:

| Line | Port | Carries |
|---|---|---|
| `5;` | `integer` | `5` |
| `5.0 + 6;` | `function` | `11` |
| `"hello";` | `string` | `hello` |
| `var n = 100;` | `n` | `100` |
| `var t = 0..1..#10;` | `t` | ten numbers |
| `0..#6..10;` | `list` | six numbers |

**A line that declares a variable gives a port named after the variable.** That is the one to
reach for when you want a port that says something:

```csharp
var area = 3.0 * 4.0;
var perimeter = 2.0 * (3.0 + 4.0);
```

Two ports, `area` and `perimeter`.

**A line that declares nothing gets a port named after the *kind* of thing it is** — `integer`,
`double`, `string`, `boolean`, `list`, or `function` for anything else. `function` is what an
operator gives you, because an operator is a function; it is Dynamo's word and Spark uses the same
one.

**If you would rather the port said something, name the line.** `var sum = 5.0 + 6;` gives a port
called `sum` instead of `function`.

**Two lines of the same kind are numbered:**

```csharp
1;
2;
3;
```

Ports `integer`, `integer2`, `integer3`. The first keeps the bare name, so adding a second integer
line does not disturb a wire already attached to the first.

> **Port names are read from what you typed, never from what it computed.** A port called `11`
> would be impossible: ports exist before the graph runs, and editing a script re-makes the wires
> *by port name* — so a name that changed with the value would drop every wire every time a number
> moved.

---

## 4. Which lines count, and which do not

Only an expression **C# would refuse to accept as a statement** becomes a port. That sounds
technical and it is the most useful rule in this document, because it is what makes code blocks
safe to write normally.

**These become ports** — C# would reject each one on its own:

```csharp
n + p;
$"{count} items";
total;
```

**A call does not**, because discarding a call's value is usually the point:

```csharp
var points = new List<Point3d>();

points.Add(new Point3d(0, 0, 0));
points.Add(new Point3d(1, 0, 0));
```

One port, `points`. If `points.Add(...)` became a port, every block that builds a list would break.

**So how do you get a call's value onto a port?** Put it in a variable:

```csharp
var points = new List<Point3d>();

points.Add(new Point3d(0, 0, 0));

var made = points.Count;
```

Two ports, `points` and `made`.

Assignments, `++` and `--` are statements too, for the same reason:

```csharp
var total = 1;
total = total + 4;
```

One port, `total`, carrying `5`.

---

## 5. `return` when you want to decide the ports yourself

Write a `return` and the per-line rule stops; the `return` says exactly what the ports are.

```csharp
var a = 1.0;
var b = 2.0;
var c = 3.0;

return b;
```

One port, `result`, carrying `2`. The scratch variables are gone from the canvas.

**A named tuple gives one port per element**, with the names you chose:

```csharp
var area = 12.0;
var perimeter = 14.0;

return (area: area, perimeter: perimeter);
```

Two ports, `area` and `perimeter`.

That is the tool for a block with eleven working variables and three you actually want on the
canvas. Any other `return` shape gives one port called `result`.

---

## 6. Types, and what a wire teaches the block

**Before anything is wired in, an input port has no type**, so Spark declares it `dynamic`: your
code reads like C#, and what `radius` turns out to be is worked out while the graph runs.

**Wire something in and the block is recompiled with that type.** Wire a `Point.FromCoordinates`
into a port called `center` and the block compiles as though you had written
`Point3d center = …`, so this works:

```csharp
center.X + center.Y;
```

It works because the compiler knows what `center` is — which is also what makes the completion
list useful. Type `center.` with a point wired in and you get `X`, `Y`, `Z`, `DistanceTo` and the
rest. With nothing wired in you get nothing, because the block really will be compiled with that
input as `dynamic`, and a list promising members the compiler will not find is worse than no list.

**You can say what a port is before wiring it.** Every input port has a type dropdown in the
Properties pane, under its value box. It starts on *from the wire*; choose anything else and the
block recompiles immediately as though that type had been wired in. That is how you write the code
first and wire it up second. A type you choose beats a type a wire brings; put it back to *from the
wire* to hand control back.

---

## 7. Ranges, as you would write them in Dynamo

All three of Dynamo's range forms work as typed:

```csharp
var evenlySpaced = 3..5..#8;
var counted = 3..#8..0.25;
var stepped = 3..5..0.25;
```

`#` counts; no `#` steps. So that is *eight numbers from 3 to 5*, *eight numbers from 3 stepping
0.25*, and *from 3 towards 5 stepping 0.25*.

Eight values including both ends have **seven** gaps between them, so `#8` divides the span by
seven — and the last value is exactly the bound you asked for, which is not true of the arithmetic
written by hand.

Each is one call on `NumberRange` underneath, the same code the `Number.Range*` nodes run, so a
range cannot mean one thing on the canvas and another in a block. Write the call yourself if you
prefer: `NumberRange.ByCount(3, 5, 8)`.

**Two-part ranges are ordinary C# and untouched.** `arr[1..^1]` still slices and `var r = 0..1;` is
still a `System.Range`. Only the three-part forms are rewritten, and they are safe to claim because
a `System.Range` has no `..` operator — `a..b..c` cannot mean anything else in C#.

---

## 8. What is in scope

`System`, `System.Collections.Generic`, `System.Linq`, `Spark.Api`, `Spark.Geometry` and
`Spark.Nodes.Core` are all imported. You do not need `using` lines:

```csharp
var points = new List<Point3d>();

for (var i = 0; i < 5; i++)
{
    points.Add(new Point3d(i * 2.0, 0, 0));
}

points;
```

**The whole node library is callable.** Anything you can drop on the canvas you can call in a
block:

```csharp
var box = Solid.Box(Plane.WorldXY, 2.0, 2.0, 2.0);
var hole = Solid.Cylinder(Plane.WorldXY, 0.5, 4.0);

Solid.Difference(box, hole);
```

**Where a library name would have collided, the name you already knew wins.** `Circle`, `Curve`,
`Line`, `Plane`, `Arc`, `Surface`, `PolyCurve`, `PolyLine` and `BoundingBox` are the geometry
types, and **`Math` is `System.Math`** — so `Math.PI` is what you expect. The library's own version
is still there when you want it, by its full name:

```csharp
var one = Spark.Nodes.Core.Circle.FromCenterRadius(Point3d.Origin, 1.0);
var two = Circle.FromCenterRadius(Point3d.Origin, 2.0);
```

**Geometry can also be built with `new`.** Every construction the library offers as a named
factory is a constructor as well, so whichever you reach for first is there, and the two are the
same thing:

```csharp
var factory = Circle.FromCenterRadius(Point3d.Origin, 2.0);
var constructed = new Circle(Point3d.Origin, 2.0);
```

Two are deliberately missing, and they are the two where a constructor could not say what you
meant. `Angle.FromDegrees` and `Angle.FromRadians` both take a single `double`, so `new Angle(90)`
would have to quietly pick one — and the line would read the same either way. `Plane` has the same
problem with `FromOriginXAxisYAxis` and `FromOriginNormalXAxis`, which both take a point and two
vectors but disagree about what the second vector is. Name those four; everything else takes
`new`.

### Two things that will catch you once

**Four node families are spelled differently in a block**, because their node name is a type C#
already uses:

| On the canvas | In a code block |
|---|---|
| `List.Count` | `ListNodes.Count` |
| `String.FromNumber` | `Text.FromNumber` |
| `DateTime.Now` | `DateAndTime.Now` |
| `TimeSpan.FromDateDifference` | `Duration.FromDateDifference` |

**A list node counts a *graph* list, not a C# one.** Rank is something the graph puts around a
node, and a code block is inside the node:

```csharp
var mine = new List<object> { 1, 2, 3 };

var counted = ListNodes.Count(mine);
var plain = mine.Count;
```

`counted` is **1** — the node sees one value, not a list of three. `plain` is **3**. Use ordinary
C# for a list you made yourself.

---

## 9. Loops, and what stops them

A code block is the only node whose author can hang the application by accident, so Spark rewrites
your code before compiling it and puts a check at the top of every loop.

**Escape actually stops a running block.** A block sitting in `while (true) { }` stops there rather
than running until you kill the application.

**A runaway loop is stopped anyway**, at a hundred million iterations per run of one block:

> The script ran more than 100,000,000 loop iterations and was stopped. If that is genuinely the
> work, do it in a custom node rather than a code block.

That is a runaway detector, not a quota — a block doing real work per iteration will not come near
it.

**Recursion is bounded at 512 levels**, and this one is hard rather than conservative:

```csharp
int depth(int n) => depth(n + 1);

return depth(0);
```

> The script recursed more than 512 levels deep and was stopped before the stack overflowed.

A stack overflow **cannot be caught** in .NET — it ends the whole process and takes your unsaved
graph with it, so the limit has to stop you before the stack does.

**Two cases are deliberately outside this**, and it is better to know: recursion through a lambda
written as an expression (`f = n => f(n - 1);`) is not counted — write it as a local function and it
is — and recursion inside a library you call is not counted either, because it is not Spark's code
to rewrite. Both can still end the application. Neither is a normal thing to write.

---

## 10. A worked example

Divide a circle into points, and report how many you made:

```csharp
var circle = Circle.FromCenterRadius(Point3d.Origin, radius);
var points = new List<Point3d>();
var step = circle.Length / count;

for (var i = 0; i < count; i++)
{
    points.Add(circle.PointAtLength(i * step));
}

return (points: points, made: points.Count);
```

Two inputs, `radius` and `count` — neither is declared. Two outputs, `points` and `made`, because
the `return` says so. Wire a number into each and wire `points` into a watch node to see them.

**Delete that last line** and the same block has three outputs instead: `circle`, `points` and
`step`, one per line that made something. The `for` loop adds none — a loop is not an expression —
and neither does `points.Add(...)`, because a call is a statement.

Which of the two you want is the whole of the choice. The `return` is there to say *these* and not
the rest.

---

## 11. The editor

It is the same editor on the node and in the Properties pane, and it is a real one: C# syntax
highlighting, line numbers, a completion list, signature help, squiggles under errors as you type,
and VS Code's Selection commands on the context menu.

- **Type a dot, or press Ctrl+Space**, and the list opens at the caret. Keep typing to narrow it —
  `center.Di` selects `DistanceTo` — then **Enter** or **Tab** to accept, **Escape** to dismiss.
- **Signature help appears when you type `(`.** If the method has overloads it says `↑↓ 2/2`, and
  the arrow keys move between them. Escape dismisses it and gives the arrows back to the caret.
- **Panning, zooming and dragging the block** all leave the editor open and carry it along.
- **Code is set in Source Code Pro**, the face Dynamo uses. The **Font** dropdown under the editor
  in the Properties pane changes it for every block, and the choice is remembered between sessions.
  The list is Source Code Pro plus the monospaced Latin faces on your machine — CJK families are
  left out even where their Latin happens to be equal width, because a block set in one is not
  what anybody means by a code font.

---

## 12. One thing to know before you share a graph

**A Spark graph containing a code block is a program.** Opening one from a source you do not trust
is the same as running an unknown application.

So Spark does not run a graph because you opened it. A file with code blocks in it appears on the
canvas, drawn, with its values empty and a banner in the Properties pane offering **Run once** or
**Always trust this file** — and *this file saying exactly this*, so changing a line asks again.

To refuse scripting entirely, start with `--no-script`. A graph containing a code block then fails
to open, naming what it contains, rather than opening with the code blocks quietly missing.

---

## Where to go next

- [Code blocks](help/concepts/code-blocks.md) — the reference, in the application's help.
- [Finding nodes](help/concepts/finding-nodes.md) — the library, search, and the two canvas
  gestures.
- [Lacing](help/concepts/lacing.md) — what happens when a list arrives at a port that wanted one
  value.
- [Lists](help/concepts/lists.md) — rank, and the list nodes.
