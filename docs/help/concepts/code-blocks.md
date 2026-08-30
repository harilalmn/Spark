---
id: concepts.code-blocks
title: Code blocks
nodes: []
related: [concepts.finding-nodes, concepts.lacing, concepts.files]
since: "0.1"
---

**Status:** Current. Describes the code block in the running application.
**Owner:** `scripting`
**Last updated:** 2026-08-31

> **Scope.** A code block is a node whose body is C# you type. Its input ports come from the
> identifiers your code uses but does not declare; its output ports come from what it returns.
> This topic covers writing one, and it covers **what stops one that never finishes** — because
> a code block is the only node in a Spark graph whose author can hang the application by
> accident.

---

## Writing one

Put a code block down with **Add code block** on the toolbar, or double-click empty canvas and
pick it from the list. Type into the properties pane. The simplest useful block is one line:

```csharp
return radius * 2;
```

That gives a node with one input port called `radius` and one output port called `result`.
Nothing declared `radius`, so Spark treats it as something the graph supplies — wire a number
into it and the block runs.

**Several free identifiers give several ports, in the order they first appear:**

```csharp
return width * height;
```

`width`, then `height`. A local variable is *not* a port, because it is declared:

```csharp
var doubled = radius * 2;
return doubled;
```

That block still has exactly one input, `radius`.

## Several outputs

Return a named tuple and each element becomes a port:

```csharp
var area = Math.PI * radius * radius;
var circumference = 2 * Math.PI * radius;

return (area: area, circumference: circumference);
```

The node now has two output ports, `area` and `circumference`. Any other return shape gives one
port called `result`.

Names matter more than positions here. **Editing a script re-makes the wires by port name**, so
adding an identifier in the middle of your code does not silently rewire the graph — a port
called `height` stays connected to whatever `height` was connected to.

## Geometry is already imported

`Spark.Geometry` and `Spark.Api` are in scope, along with `System`, `System.Collections.Generic`
and `System.Linq`. You do not need `using` lines for them:

```csharp
var points = new List<Point3d>();

for (var i = 0; i < count; i++)
{
    points.Add(new Point3d(i * spacing, 0, 0));
}

return points;
```

## Loops, and what stops them

**A code block that never finishes would otherwise never be stoppable.** .NET has no safe way to
interrupt a running thread, so Spark does not try: it rewrites your code before compiling it, and
puts a check at the top of every loop.

Three things follow from that, and they are worth knowing before you meet one:

**Cancelling an evaluation actually stops the loop.** Press Escape while a graph is running and a
block sitting in `while (true) { }` stops there, rather than running until you close the
application.

**A loop that runs away with nobody watching is stopped anyway.** There is a ceiling of a hundred
million loop iterations per run of one block. Reaching it is reported on the node:

> The script ran more than 100,000,000 loop iterations and was stopped. If that is genuinely the
> work, do it in a custom node rather than a code block.

That ceiling is a runaway detector, not a quota. A block doing real work per iteration will not
come near it; a block that reaches it has almost always looped by mistake. If the work is genuine,
it belongs in a compiled custom node, where it is not being recompiled and re-run every time you
drag a slider.

**Recursion is bounded at 512 levels deep**, and this one is a hard limit rather than a
conservative one:

```csharp
int depth(int n) => depth(n + 1);

return depth(0);
```

> The script recursed more than 512 levels deep and was stopped before the stack overflowed.

A stack overflow **cannot be caught** in .NET. It ends the whole process, and it would take your
unsaved graph with it — so the limit has to stop you before the stack does, and it cannot be
raised by catching anything.

### What is not bounded

Two cases are deliberately outside this, and it is better to know than to be surprised:

- **Recursion through a lambda written as an expression** — `f = n => f(n - 1);` — is not
  counted. Write the helper as a local function (`int f(int n) => f(n - 1);`) and it is.
- **Recursion inside a library you call** is not counted either. It is not Spark's code, and
  Spark cannot rewrite it.

Both can still overflow the stack and end the application. Neither is a normal thing to write in
a code block.

## A worked example

Divide a circle into points, and report how many you made:

```csharp
var circle = Circle.ByCentreRadius(Point3d.Origin, radius);
var points = new List<Point3d>();
var step = circle.Length / count;

for (var i = 0; i < count; i++)
{
    points.Add(circle.PointAtLength(i * step));
}

return (points: points, made: points.Count);
```

Two inputs, `radius` and `count`; two outputs, `points` and `made`. Wire a number into each,
and wire `points` into a watch node to see them.

## Trust

**A Spark graph containing a code block is a program.** Opening one from a source you do not
trust is the same as running an unknown application: .NET has no code-access security, and Spark
does not pretend otherwise. Running with scripting switched off refuses to open such a graph and
names the node, rather than opening it with the node quietly missing.
