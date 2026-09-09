---
id: concepts.code-blocks
title: Code blocks
nodes: []
related: [concepts.finding-nodes, concepts.lacing, concepts.files]
since: "0.1"
---

**Status:** Current. Describes the code block in the running application.
**Owner:** `scripting`
**Last updated:** 2026-09-09

> **Scope.** A code block is a node whose body is C# you type. Its input ports come from the
> identifiers your code uses but does not declare; it gets one output port per line that makes
> something — Dynamo's Code Block rule — or exactly what it says when it writes a `return`.
> This topic covers writing one, and it covers **what stops one that never finishes** — because
> a code block is the only node in a Spark graph whose author can hang the application by
> accident.

> **New to code blocks?** [Getting started with the code block](../../CodeBlock.md) is a tutorial
> that goes through the same ground in order, with every example run rather than merely written.
> This page is the reference.

---

## Writing one

**Double-click empty canvas.** A code block lands where you clicked, empty and ready to type
into — the editor is already open with the caret in it, so you can start typing without a second
click. That is Dynamo's gesture and it does the same thing here. **Insert → Code block** does
the same, at the next free spot.

**Click the block to type in it.** One click opens the editor on the node, over the source it was
already showing, with the caret after the last character — dragging the block still drags it,
because a drag is a click that moved. The same source is also in the **Properties** pane, which is
the better place for a long script and the only place the input-port type dropdowns are.

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

Two ports again — but now they are the two you named. Any other return shape gives one port called
`result`.

**A line that is just an expression is a result too**, and gets a port of its own:

```csharp
var n = 10 / 5;
var p = 8 / 4;

n + p;
```

Three ports — `n`, `p`, and one carrying `4`. Every line that makes something gets a port, in the
order the lines appear. This is Dynamo's Code Block rule, and Spark follows it.

## What the ports are called

**A line that declares a variable gives a port named after it.** A line that does not gives a port
named after the *kind* of thing the expression is:

```csharp
5;
5.0 + 6;
"hello";
var n = 100;
var t = 0..1..#10;
0..#6..10;
```

Six lines, six ports: `integer`, `function`, `string`, `n`, `t`, `list`.

`function` is what an operator gives you, because an operator is a function — that is Dynamo's
name for it and Spark uses the same one. **If you would rather the port said something, name the
line:** `var sum = 5.0 + 6;` gives a port called `sum`.

**Two lines of the same kind are numbered** — `integer`, `integer2`, `integer3` — and the first
keeps the bare name, so adding a second integer line does not disturb the wire on the first.

**The port name is read from what you typed, never from what it computed.** A port called `14`
would be impossible: ports exist before the graph runs, and editing a script re-makes the wires by
port *name*, so a name that changed with the value would drop every wire every time a number moved.

**The rule for which lines count is narrower than it looks, and deliberately so.** Only an
expression C# would refuse to accept as a statement gets a port — `n + p;`, `$"..."`, `total;`. A
**call** is still a statement, because discarding its value is usually the point:

```csharp
var points = new List<Point3d>();

points.Add(new Point3d(0, 0, 0));
```

One port, `points`. `new List<int>();` on a line of its own is a statement for the same reason. If
you want such a value on a port, put it in a variable — `var made = points.Count;` — or write
`return`. Nothing that compiled before this rule existed changed meaning, because every line it
claims was an error until it did.

**A `return` overrules all of it.** That is Spark's own escape hatch and Dynamo has no equivalent:
write one and the ports are exactly what it says, so a block with eleven working variables can put
three of them on the canvas.

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

## Dynamo's range syntax works as typed

All three of Dynamo's range forms can be written straight into a code block:

```csharp
// 8 numbers evenly spaced from 3 to 5 — the ends included
var numbers = 3..5..#8;
```

```csharp
// 8 numbers from 3, stepping 0.25
var numbers = 3..#8..0.25;
```

```csharp
// from 3 towards 5, stepping 0.25
var numbers = 3..5..0.25;
```

Each is one call on [`NumberRange`](lists.md) underneath — `ByCount`, `ByCountAndStep` and `ByStep`
respectively — which is the same code the `Number.Range*` nodes run, so a range cannot mean one
thing on the canvas and another in a block. You can write the call yourself if you prefer it:
`NumberRange.ByCount(3, 5, 8)`.

`#` counts; no `#` steps. Eight values including both ends have **seven** gaps between them, so
`#8` divides the span by seven — and the last value is the bound you asked for rather than the
double next door, which is not true of the arithmetic written by hand.

**Two-part ranges are untouched.** `arr[1..^1]` is ordinary C# and still slices; `var r = 0..1;` is
still a `System.Range`. Only the three-part forms are rewritten, and they are safe to claim because
a `System.Range` has no `..` operator — `a..b..c` cannot mean anything else in C#.

A `#` inside a string or a comment is left alone, so `var label = "#3";` is a string and nothing
else. Each block above declares one variable and so has one output port, `numbers` — the rule
described under [Several outputs](#several-outputs). Written without the variable, as
`0..1..#8;`, the port is called `list`.

## The library is in scope too

**Every node in the library can be called from a block.** `Solid`, `Logic`, `Colour`, `Number`,
`Display` and the rest are in scope by name:

```csharp
var box = Solid.Box(Plane.WorldXY, 2, 2, 2);
var hole = Solid.Cylinder(Plane.WorldXY, 0.5, 4);

Solid.Difference(box, hole);
```

**Where a library name would have collided, the name you already knew wins.** `Circle`, `Curve`,
`Line`, `Plane`, `Arc`, `Surface`, `PolyCurve`, `PolyLine` and `BoundingBox` are the geometry
types, exactly as they always were, and **`Math` is `System.Math`** — so `Math.PI` is what you
expect. The library's own version is still there in full when you want it:
`Spark.Nodes.Core.Circle.FromCenterRadius(...)`.

**Four have a different name in a block**, because their node name is a type C# already uses:

| On the canvas | In a code block |
|---|---|
| `List.Count` | `ListNodes.Count` |
| `String.FromNumber` | `Text.FromNumber` |
| `DateTime.Now` | `DateAndTime.Now` |
| `TimeSpan.FromDateDifference` | `Duration.FromDateDifference` |

**And a list node counts a graph list, not a C# one.** `ListNodes.Count(new List<object> {1, 2, 3})`
is `1`, because rank is something the graph adds around a node and a code block is inside the
node. Use ordinary C# — `xs.Count` — for a list you made yourself.

## Printing to the console

**`Console.WriteLine` works in a code block, with no `using` to type**, and the text appears in the
Console pane, under **Properties** on the right. It is there by default; **View ▸ Console** hides
and shows it, and **View ▸ Reset layout** brings it back.

```csharp
Console.WriteLine("starting");
Console.Write("a");
Console.Write("b");
Console.WriteLine("c");        // one line: abc
Console.Clear();               // and it is empty again
```

**`Console` means Spark's console here, not `System.Console`.** Spark is a windowed application with
no terminal behind it, so `System.Console.WriteLine` writes where nobody can see it — the name is
pinned to the one that puts the line where you can read it, exactly as `Circle` and the other eight
are pinned. `System.Console` is still there under its full name if you have a reason to want it.

**The same three are nodes**, under **Display** in the library: `Console.Write`, `Console.WriteLine`
and `Console.Clear`. Each **passes its text through** as its output, so a console node sits in the
middle of a chain rather than ending it — wire a value in, read it on the way past, and carry on.
`Console.Clear` outputs how many lines it removed.

**They run every time the graph runs**, unlike every other node. Spark normally serves a node whose
inputs have not changed from its cache, which for these would mean printing once and never again;
they are marked as having a side effect, so they re-evaluate on each run.

**The console keeps the last 10,000 lines.** A loop that writes more drops the oldest, and the pane
says how many it dropped rather than letting you read from the top of something incomplete.

## Using a library from nuget.org

**File ▸ Packages…**, untick **Spark packages only**, search, select the row, and press **Add as a
library…**. You will see what the package is — publisher, licence, whether it is signed, what else
it will install, and whether it carries native code — before anything lands on your disk. Agreeing
puts it in the folder beside your graph, and code blocks can use its types straight away.

**Two buttons, two different things.** **Install…** is for a *Spark package*: it carries a manifest
saying which of its assemblies hold nodes, and installing it puts new nodes in the library panel.
**Add as a library…** is for any ordinary .NET package — most of nuget.org — and it adds **types**
you can write code against, not nodes. A library needs no manifest, which is why the two buttons
exist rather than one that sometimes refuses.

**It goes beside your graph**, in `<name>.packages`, not into a machine-wide folder — so the graph
and what it needs travel together. That is also why the window asks you to save an untitled graph
first: there is no file to name the folder after. See
[Saving and opening graphs](files.md#the-folder-beside-the-file).

**You do not have to write the `using`.** The namespaces of a library you added are imported for
you, so this compiles the moment the package is in:

<!-- spark:skip -->
```csharp
// After adding Humanizer as a library — no `using` typed anywhere.
var words = 42.ToWords();
```

**Unless a name in it already means something else.** One clash is enough to keep the whole
namespace out, because *why does `Sprocket` resolve and `Line` not* is a worse question than a
namespace that plainly was not imported. When that happens the window says so and names the types
that stopped it:

> `Autodesk.Revit.DB` was not imported: it defines `Arc`, `Curve`, `Line`, `Mesh`, `Plane`,
> `Point` and `Transform`, which already mean something in a code block. Write
> `using Autodesk.Revit.DB;` in the block to use it there.

**Write that `using` at the top of your block and it wins inside that block**, which is the way
out. C# stops at the innermost scope that has an answer, so your directive shadows what Spark
imported rather than colliding with it:

<!-- spark:skip -->
```csharp
using Autodesk.Revit.DB;

// `Line` means Revit's here, and only here.
var line = Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
```

And if you want both, alias the one you want to rename:

<!-- spark:skip -->
```csharp
using RevitLine = Autodesk.Revit.DB.Line;

// `Line` is still Spark's; `RevitLine` is Revit's.
var spark = Line.FromPoints(Point3d.Origin, new Point3d(10, 0, 0));
```

**Nothing is loaded because it is there.** A folder of DLLs beside a graph that arrived by email is
somebody else's code, so Spark asks before compiling against it and remembers your answer against
the assembly's **contents** — rebuild it and it asks again, because it is not the file you agreed
to.

## The editor

**It is the same editor in both places** — on the node and in the **Properties** pane — and it is
a real one: C# syntax highlighting, line numbers, a completion list, signature help, squiggles
under errors as you type, and VS Code's Selection commands on the context menu.

**Code is set in Source Code Pro**, which is the face Dynamo draws its Code Block in. Spark ships
the font rather than asking your machine for one, so a block looks the same everywhere.

**The Font dropdown under the editor in the Properties pane changes it** — for every code block
rather than the selected one, and Spark remembers your choice between sessions. The list is Source
Code Pro plus the monospaced fonts installed on your machine. Proportional fonts are left out
because a block is sized by counting characters, and one whose characters are different widths
would draw its code over its own ports.

On the canvas it opens over the block's own source, and **the block widens to hold it** — the
editor is drawn at a readable size whatever the canvas is zoomed to, so the node makes room
rather than the editor covering the ports. It goes back to its own size when you close it.

**Panning, zooming and dragging the block leave it open** and carry it with the block, which stays
big enough to hold it: the editor is drawn at a readable size whatever the canvas is at, so zooming
out grows the block rather than shrinking the text. Drag the block by its title with the editor
open and the editor goes with it.

It closes when you click away. **Escape closes it and keeps what you typed** — Ctrl+Z takes the edit back if you did not want it, which
is not something that can bring typing back the other way. **Enter is a newline**, so it is
clicking away, or Escape, that commits.

**Type a dot, or press Ctrl+Space, and the list opens at the caret.** Keep typing to narrow it —
`center.Di` selects `DistanceTo` — then **Enter** or **Tab** to accept, **Escape** to dismiss, and
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
`Point.FromCoordinates` into a port called `center` and the block is compiled as though you had
written `Point3d center = …;` — so this works:

```csharp
return center.X + center.Y;
```

and it works because the compiler knows what `center` is, not because it found out at run time.
The port label on the canvas changes to match, and pulling the wire out puts the port back to
`dynamic`, because an unwired port has no type to claim.

**This is what the completion list is built from.** With a point wired into `center`, typing
`center.` lists `X`, `Y`, `Z`, `DistanceTo` and the rest of `Point3d`. With nothing wired in, it
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
return center.X + center.Y;
```

put a code block down, type the line, and set `center` to `Point3d` in the dropdown. Typing
`center.` now lists the members of a point.

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

> The port 'center' received a String, but the script uses it as a Point3d.

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
var circle = Circle.FromCenterRadius(Point3d.Origin, radius);
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

The `for` loop adds no port, and neither does `points.Add(...)` inside it: a loop is not an
expression and a call is a statement. Only the three declarations make something.

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
