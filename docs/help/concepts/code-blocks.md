---
id: concepts.code-blocks
title: Code blocks
nodes: []
related: [concepts.finding-nodes, concepts.lacing, concepts.files]
since: "0.1"
---

**Status:** Current. Describes the code block in the running application.
**Owner:** `scripting`
**Last updated:** 2026-09-02

> **Scope.** A code block is a node whose body is C# you type. Its input ports come from the
> identifiers your code uses but does not declare; its output ports are the variables it
> declares, or whatever it returns when it returns something.
> This topic covers writing one, and it covers **what stops one that never finishes** — because
> a code block is the only node in a Spark graph whose author can hang the application by
> accident.

---

## Writing one

**Double-click empty canvas.** A code block lands where you clicked, with its source on it.
That is Dynamo's gesture and it does the same thing here. **Insert → Code block** does the
same, at the next free spot.

**Double-click the block to type in it.** The editor opens on the node, over the source it was
already showing. The same source is also in the **Properties** pane, which is the better place
for a long script and the only place the input-port type dropdowns are.

**A new block starts with no input ports.** You do not add one with a button; you add one by
using a name the code has not declared. That is the whole rule, and everything below is it
applied.

The simplest useful block is one line:

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

**A block that returns nothing has one output port per variable it declares**, named after the
variable, in the order the lines appear:

```csharp
var area = Math.PI * radius * radius;
var circumference = 2 * Math.PI * radius;
```

Two output ports, `area` and `circumference`. This is how Dynamo's code block behaves, and it is
the quickest way to get a value out of every line: write the lines, wire the ports.

Only variables declared at the **top level** of the block count. One declared inside a `for`, an
`if` or a lambda does not, because it no longer exists when the block finishes:

```csharp
var total = 0.0;

for (var i = 0; i < count; i++)
{
    var step = i * 2.0;
    total += step;
}
```

One port, `total`.

**Write a `return` and you decide the ports instead.** A named tuple gives one port per element:

```csharp
var area = Math.PI * radius * radius;
var circumference = 2 * Math.PI * radius;

return (area: area, circumference: circumference);
```

Two ports again — but now they are the two you named, so a block with eleven working variables can
put three of them on the canvas. Any other return shape gives one port called `result`.

**The two rules do not compete.** Returning is how a block says exactly what its ports are, and the
per-variable reading is what it gets when it says nothing. A scratch variable added to a block that
returns nothing does add a port — visible, named, and connected to nothing; the same variable added
to a block that returns a tuple changes nothing at all.

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

## The editor

**It is the same editor in both places** — on the node and in the **Properties** pane — and it is
a real one: C# syntax highlighting, line numbers, a completion list, signature help, squiggles
under errors as you type, and VS Code's Selection commands on the context menu.

On the canvas it opens over the block's own source, and **the block widens to hold it** — the
editor is drawn at a readable size whatever the canvas is zoomed to, so the node makes room
rather than the editor covering the ports. It goes back to its own size when you close it.

It closes when you click away. **Escape closes it and keeps what you typed** — Ctrl+Z takes the edit back if you did not want it, which
is not something that can bring typing back the other way. **Enter is a newline**, so it is
clicking away, or Escape, that commits.

**Type a dot, or press Ctrl+Space, and the list opens at the caret.** Keep typing to narrow it —
`centre.Di` selects `DistanceTo` — then **Enter** or **Tab** to accept, **Escape** to dismiss, and
the arrow keys to move through it. The editor keeps the keyboard the whole time, so the list never
interrupts typing.

Editing is committed when the editor loses focus. That is when the block is recompiled, its ports
are worked out again, and the graph re-runs — never on every keystroke, which would recompile
the graph while you were still half way through a word. The red squiggles do keep up with your
typing, because underlining an error costs a compile that is thrown away rather than a rebuild
of the node.

## What a wire teaches the block

**Before anything is connected, an input port has no type**, so Spark declares it `dynamic`: the
script reads like C#, and what `radius` turns out to be is worked out while the graph runs.

**Once you wire something in, the port has a type, and the block is recompiled with it.** Wire a
`Point.ByCoordinates` into a port called `centre` and the block is compiled as though you had
written `Point3d centre = …;` — so this works:

```csharp
return centre.X + centre.Y;
```

and it works because the compiler knows what `centre` is, not because it found out at run time.
The port label on the canvas changes to match, and pulling the wire out puts the port back to
`dynamic`, because an unwired port has no type to claim.

**This is what the completion list is built from.** With a point wired into `centre`, typing
`centre.` lists `X`, `Y`, `Z`, `DistanceTo` and the rest of `Point3d`. With nothing wired in, it
lists nothing — not because Spark is being unhelpful, but because the block really will be compiled
with that input as `dynamic`, and a list that promised members the compiler will not find would be
worse than no list at all. **Wire the port first, and the editor knows what you are working with —
or tell it, which is the next section.**

## Saying what a port is, before you wire it

Wiring is not the only way to give a port a type, and it is the wrong way round when you are
writing the code first. **Every input port on a code block has a type dropdown in the properties
pane, underneath its value box.** It starts on *from the wire*, which is the behaviour above.
Choose anything else and the block is recompiled immediately as though that type had been wired
in — the port label on the canvas changes, and completion starts working straight away.

So to write this before there is anything to wire into it:

```csharp
return centre.X + centre.Y;
```

put a code block down, type the line, and set `centre` to `Point3d` in the dropdown. Typing
`centre.` now lists the members of a point.

**A type you choose beats a type a wire brings.** The wire is the better source whenever there is
one, which is why *from the wire* is the default — but a setting that was quietly overruled would
be worse than no setting, so once you have said what a port is, that is what it is. Put the
dropdown back to *from the wire* to hand it back.

The choice is saved with the graph, and it survives editing the script: declarations are held by
port **name**, so adding a line above does not move them onto the wrong port.

**There is no button that adds an input.** A port appears because you used a name the code has not
declared, and it disappears when you stop using it — the dropdown says what an existing port *is*,
not whether it exists. That keeps one answer to "what are this block's inputs?", and the answer is
the code.

## What arrives, and what it is called

Numbers are widened where a graph would expect them to be: an integer arriving on a port the
script uses as a `double` is converted rather than refused. When something genuinely wrong
arrives, the message names the port rather than two CLR types:

> The port 'centre' received a String, but the script uses it as a Point3d.

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

Delete that last line and the same block has **three** outputs instead — `circle`, `points` and
`step`, one for each line that made something. Which of the two you want is the whole of the
choice: the `return` is there to say *these* and not the rest.

## Trust

**A Spark graph containing a code block is a program.** Opening one from a source you do not
trust is the same as running an unknown application: .NET has no code-access security, and Spark
does not pretend otherwise.

**So Spark does not run a graph because you opened it.** Open a file containing code blocks and it
appears on the canvas, drawn, with its values empty and a banner in the properties pane:

> This graph contains 2 code blocks, which is a program. It has been opened but not run.

Two buttons sit under it. **Run once** runs it now and asks again next time. **Always trust this
file** runs it and remembers — for *this file saying exactly this*. Change a line and you are asked
again; send the file to somebody else and they are asked too. A graph with no code blocks in it is
never asked about, because there is nothing to decide.

**To refuse scripting entirely**, start with `--no-script`:

```
spark run graph.spark --no-script
```

A graph containing a code block then **fails to open**, naming what it contains. It does not open
with the code blocks quietly missing — that would produce a wrong answer silently, which is worse
than an error. The desktop application takes the same switch, and once scripting has been refused
in a session it cannot be turned back on.
