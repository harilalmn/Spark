---
id: concepts.command-line
title: The command line
nodes: []
related: [concepts.files, concepts.evaluation, concepts.code-blocks]
since: "2026.9"
---

**Status:** Current. Describes all seven verbs, which all exist.
**Owner:** `graph-engine`
**Last updated:** 2026-09-15 (`E12-T5`: `spark docs`, and the seventh verb of seven)

> **Scope.** `spark.exe` ships beside the desktop application and does everything **without opening
> a window**. It is the same engine, the same node library and the same value rendering; what it
> does not have is a canvas — and since `render` it does not need one to draw, either. **All seven
> planned verbs now exist**, the last of them on 2026-09-15.

---

## Why there is a command line at all

A graph is a document and an evaluation is a computation. Both of those claims are easy to make
and easy to be wrong about, and the way they stop being claims is that something outside the
application can open a file and get the same answer. That is what `spark` is for: a build that
checks graphs, a script that exports a hundred of them, a diff between yesterday's answer and
today's.

Everything it prints is designed to be **redirected**. Values go to standard output and problems
go to standard error, so `spark run graph.spark > values.txt` captures the answer and still shows
you what went wrong on the way.

---

## `spark run` — what did this graph produce?

```
spark run GRAPH.spark [--all] [--no-script] [--trust-packages]
```

Opens the graph, evaluates it with no window, and prints **what its watch nodes saw**. A watch is
the user saying *this one*; a graph of two thousand nodes has two thousand values and almost none
of them is the one you wanted.

```
$ spark run docs/examples/curves.spark
spark: no watch nodes in this graph. Add a Watch node, or run with --all.
spark: 18 node(s) evaluated, 0 cache hit(s), 0 diagnostic(s)
```

That first line is on standard error, not standard output: a graph with no watches in it ran
perfectly well and simply said nothing, which looks identical to a graph that did nothing. With
`--all` the same file prints every node:

```
$ spark run docs/examples/curves.spark --all
Point.FromCoordinates  rank 1 · 8 items  [(-7, 7, 0), (-5, 7, 0), (-3, 7, 0), …
Circle.FromCenterRadius  rank 1 · 8 items  [Circle(center (-7, 7, 0), radius 0.9), …
Plane.XY  rank 0 · one value  Plane(Origin=(0, 0, 0), Normal=(0, 0, 1))
```

Values print in the **document's** order rather than the graph's, so two runs of one file print the
same values in the same order — which is the only reason to print them at all. The rendering is the
same one the canvas and the properties pane use, from one shared implementation, so what the
command line says and what the application shows cannot drift apart.

`--no-script` refuses a graph containing a code block instead of running it. It **refuses rather
than dropping the executable parts**, because a graph that silently ran with its code blocks
missing would produce a wrong answer quietly, which is worse than an error. See
[code blocks](code-blocks.md).

### A graph's own packages, and `--trust-packages`

A graph can carry libraries in a folder beside it named after the file — `facade.packages` beside
`facade.spark` — and its code blocks compile against them. The window asks before it uses one: it
names each assembly and its hash, and **Trust and load** remembers the answer against those exact
bytes. `run` and `check` use **the same record**, so an assembly you agreed to in Spark is simply
used. They have nobody to ask about the rest, so they **refuse the graph and name what they found**:

```
$ spark check facade.spark
spark: facade.spark: the graph's package folder holds an assembly nobody has agreed to load, so it was not checked:
spark: facade.spark:   Greeter.dll  sha256 3F9A0C1E…
spark: facade.spark: agree to it by opening the graph in Spark, or pass --trust-packages to use it for this run only, recording nothing.
$ echo $?
1
```

The hash is printed in full, where the window shows eight characters, so a build can compare it
with the one it expects. `--trust-packages` uses the folder's assemblies **for that run only**
and writes nothing down — the record is shared with the desktop application, and a scheduled job
should not become your consent in the window. A file that cannot be read is refused even then,
because it has no hash to agree to.

**Only a graph with a code block looks at its folder**, because nothing else compiles against it.
A package the file names that is missing from the folder prints as a warning, since it is the
reason a block that uses it will not compile; `check --strict` fails on it.

Why a DLL is asked about when the code blocks are not: the code is text inside the graph, which a
reviewer reads in a diff, and an assembly is not.

---

## `spark check` — is this graph broken?

```
spark check GRAPH.spark [--strict] [--no-script] [--trust-packages]
```

The same evaluation with the printing taken away. **It says nothing at all when nothing is wrong**,
and its answer is the exit code:

| Exit code | Meaning |
|---|---|
| `0` | Every node evaluated. Warnings may have been printed; none of them failed the run. |
| `1` | A node errored, the file would not open, or the arguments were wrong. |

```
$ spark check docs/examples/solids.spark
$ echo $?
0
```

and when something is wrong:

```
$ spark check broken.spark
spark: broken.spark: error SPK1046: Circle.FromCenterRadius: 'Circle.FromCenterRadius' failed: A circle's radius must be positive and finite. (Parameter 'radius') Actual value was 0.
$ echo $?
1
```

**Three things about that output are deliberate.**

- **Silence on success.** A gate that writes a line every time is a gate whose output stops being
  read, and then the one run that had something to say scrolls past with the rest.
- **The node is named.** `spark run` prints the code and the message; `check` prints the node as
  well, because a build log is read by somebody who was not watching and a diagnostic code with no
  node attached is a message that costs an hour. The name is the same one the canvas draws.
- **Every diagnostic is one line.** Several exceptions put a newline in their message —
  `ArgumentOutOfRangeException` appends *Actual value was 0.* on a line of its own — and a
  diagnostic split in two is one whose second half has lost its file name, its node and its
  severity. The canvas keeps the break, because it has the room; the command line does not.
- **Warnings print and do not fail.** A per-element replication failure is a warning by
  definition — the node produced a value and everything downstream still evaluated — and a gate
  that refused those is a gate somebody turns off. See [evaluation](evaluation.md).

### `--strict`, and the case that makes it necessary

**The warning rule holds when one element of eight fails and also when eight of eight do**, and the
second is very nearly always a broken graph:

```
$ spark check broken.spark
spark: broken.spark: warning SPK1042: Circle.FromCenterRadius: 8 of 8 elements failed; first at [0]: A circle's radius must be positive and finite. (Parameter 'radius') Actual value was 0.
$ echo $?
0
```

The *same* radius of zero on a node that is not replicating is an **error** and fails. That
asymmetry is real, it follows from what a warning means rather than from an oversight, and it is
exactly the kind of thing a gate should not decide on your behalf. So `--strict` fails on **any**
diagnostic at all:

```
$ spark check broken.spark --strict
spark: broken.spark: warning SPK1042: Circle.FromCenterRadius: 8 of 8 elements failed; …
$ echo $?
1
```

Use `--strict` in a build you control and the default when checking graphs other people wrote —
a graph that legitimately warns is common, and a gate that has to be argued with gets removed.

### Using it in a build

```
spark check --strict graphs/facade.spark || exit 1
```

or over a folder, where the shell's own exit code does the work:

```
for f in graphs/*.spark; do spark check "$f" || fail=1; done
exit ${fail:-0}
```

---

## `spark export` — write the geometry out

```
spark export --open GRAPH.spark --out FILE.[obj|stl|ply|glb|step|iges] [--tolerance T]
             [--no-script] [--trust-packages]
```

Evaluates with no window and writes what it produced. The format comes from the extension.

```
$ spark export --open docs/examples/curves.spark --out curves.obj --tolerance 0.001
```

Curves become polylines and surfaces are tessellated, at a tolerance written into the file's own
header. **Solids are not tessellated on the way to STEP or IGES** — those carry the exact
surfaces, which is the entire point of having them. See [solids](solids.md).

**A graph with a code block exports like any other**, and whatever geometry the block makes is
written with the rest. `--no-script` and `--trust-packages` mean exactly what they mean for `run`:
the first refuses such a graph, the second uses the assemblies in its package folder for this one
run. **A refused export writes no file**, so a script that checks for the output file cannot
mistake a refusal for an empty graph.

```
$ spark export --open facade.spark --out facade.obj --no-script
spark: this graph contains a code block and --no-script was given, so it was not exported.
$ echo $?
1
```

---

## `spark render` — a picture of the graph

```
spark render --open GRAPH.spark --out FILE.png [--width N] [--height N]
             [--no-grid] [--no-script] [--trust-packages]
```

Evaluates with no window and draws what it produced, 1280×720 by default.

```
$ spark render --open docs/examples/solids.spark --out solids.png --no-grid
spark: wrote 1280x720 to solids.png (23 renderable(s), 26 node(s) evaluated, 0 cache hit(s))
```

**It draws with the software rasteriser and never the GPU**, and that is the whole reason the verb
is worth having. GPU output varies by driver, by vendor and by day; this path does not, so the same
graph gives the same bytes on a build agent with no display and no driver attached to it. That is
what makes `spark render` usable as a visual check in a build rather than only as a convenience —
two runs of one graph produce identical files, and a test asserts it.

**The camera frames the geometry for you.** There is no flag for a viewpoint, because the output
then depends on the graph alone, which is the property a regression check rests on. `--no-grid`
leaves out the ground grid and the world axes, for a picture of the geometry by itself; the default
keeps them, because the picture is standing in for the viewport.

**Exit 2 means the graph ran and drew nothing.** A graph of pure arithmetic is not broken — it has
no geometry in it — but a build that accepted an empty picture without noticing is exactly the
failure a visual check exists to catch, so it is a distinct code rather than success. **The file is
still written**, because opening it is how you find out why.

```
$ spark render --open arithmetic.spark --out out.png
spark: wrote 1280x720 to out.png (0 renderable(s), 3 node(s) evaluated, 0 cache hit(s))
spark: the graph produced nothing to draw, so the image is the empty viewport.
$ echo $?
2
```

`--no-script` and `--trust-packages` mean exactly what they mean for `run` and `export`. A name that
is not a `.png` is refused rather than written under a lying extension.

---

## `spark pkg` — is this checkout complete?

```
spark pkg list    --open GRAPH.spark
spark pkg restore --open GRAPH.spark
```

A graph records the packages it expects, and they live in `GRAPH.packages` beside it. `list`
reconciles the two.

```
$ spark pkg list --open facade.spark
spark: facade.packages
  present  facade.packages/acme.nodes.1.2.0
  missing  facade.packages/contoso.panels.3.1.0
spark: 2 recorded, 1 missing, 4 assembly(ies) beside the graph
$ echo $?
1
```

**It exits 1 when something is missing**, and that is the reason to run it rather than to look in
the folder. A build that cannot gate here meets the same fact later, as a compile error inside a
code block, two steps and one confusing message from the cause.

**`unrecorded` is the third state**, and it is not a failure. An assembly in the folder that the
file does not record loads and works today — and it will not travel with the graph, because nothing
tells the next machine to fetch it.

**`restore` fetches what the file records and the folder lacks.**

```
$ spark pkg restore --open facade.spark
spark: restored contoso.panels 3.1.0
spark: restored 1 of 1; 0 could not be.
spark: restoring does not agree to load anything. Open the graph in Spark to agree to the
assemblies, or pass --trust-packages to run and check.
```

**Restoring downloads; it does not agree.** A folder of assemblies beside a downloaded graph is
remote code execution, which is why nothing loads without consent recorded per content hash — see
[saving and opening graphs](files.md). A verb that both fetched code and consented to it on your
behalf would be a hole in that, so `run` and `check` go on refusing assemblies nobody has agreed to,
exactly as they did before you restored them.

**A loose `.dll` cannot be restored.** It came from no feed, so there is nowhere to fetch it from;
it is named, and the run fails, because the folder is still incomplete.

---

## `spark graph` — what is in this file, and will it open here?

```
spark graph GRAPH.spark
```

Every other verb *opens* the graph: it binds each node to a definition in the library, compiles any
code block, and then does its work. `spark graph` does none of that. It reads the file and describes
it.

```
$ spark graph curves.spark
spark: curves.spark
  format       1, readable by this build (which writes 5, and this file needs a reader of 1)
  nodes        18
  wires        15
  literals     19
  notes        0
  groups       0
  code blocks  0

  definitions
    present  Spark.Nodes.Core/Circle.FromCenterRadius  x1
    present  Spark.Nodes.Core/Colour.FromRgb  x4
    present  Spark.Nodes.Core/Curve.DivideEqually  x1
    present  Spark.Nodes.Core/Display.FromGeometryColour  x4
    present  Spark.Nodes.Core/Ellipse.FromPlaneRadii  x1
    present  Spark.Nodes.Core/Number.Range  x1
    present  Spark.Nodes.Core/Plane.FromOriginNormal  x1
    present  Spark.Nodes.Core/Plane.XY  x1
    present  Spark.Nodes.Core/Point.FromCoordinates  x2
    present  Spark.Nodes.Core/PolyLine.FromRegularPolygon  x1
    present  Spark.Nodes.Core/Vector.ZAxis  x1
spark: 18 node(s), 15 wire(s); this build can open it
$ echo $?
0
```

**Because it binds nothing, it is the one verb that still works on a graph this build cannot
open** — and that is when you want it. A colleague sends you a file, `spark check` says a node is
unresolved, and the question is *which node, and what do I need to install*:

```
$ spark graph facade.spark
spark: facade.spark
  format       5, readable by this build (which writes 5, and this file needs a reader of 5)
  nodes        41
  wires        52
  literals     37
  notes        2
  groups       1
  code blocks  1

  definitions
    present  Spark.Nodes.Core/Point.FromCoordinates  x12
    missing  Acme.Nodes/Panel.ByOutline  x8
    in file  (code block, its source is its definition)  x1

  packages
    missing  facade.packages/acme.nodes.1.2.0
spark: 41 node(s), 52 wire(s); 2 thing(s) missing, so this build cannot open it as authored
$ echo $?
1
```

Two lines and you know the answer: install `acme.nodes` 1.2.0, or `spark pkg restore`.

**`missing` is a definition this build does not have**, named in full as `package/name` — the same
key the file stores, so it is searchable. **`present` is one it does.** **`in file` is a code
block**, which is neither: its definition *is* its source, carried in the graph, so no library could
hold it and it is never reported as missing.

**It exits 1 when anything it names is absent here**, for `spark pkg list`'s reason — the exit code
is the answer to *will this open on this machine*, which is a thing a build script can act on.

**It never loads the compiler.** Describing a graph full of code blocks costs nothing beyond reading
the file, because the source is text until something compiles it and this verb never does.

**The output is stable**: the definitions are sorted by key, so two runs over one file produce
identical bytes and a build log diffs cleanly.

---

## `spark docs` — the help, from a terminal

```
spark docs [--topic ID] [--out DIR]
```

This is the same help the application shows under F1. Not a copy of it, and not a second generator:
the concept topics you are reading are files, the node pages are produced from the node library
itself, and one piece of code assembles the three for both the window and this verb. A node that
exists has a page here; one that does not, does not.

**With no arguments it lists what there is.**

```
$ spark docs
  written    concepts.code-blocks  Code blocks
  written    concepts.command-line  The command line
  written    concepts.curves  Curves, parameters and arc length
  ...
  generated  nodes.Spark.Nodes.Core/Point.FromCoordinates  Point.FromCoordinates
  generated  nodes.index  Node reference
spark: 234 topic(s); 13 written, 221 generated
```

**`written` and `generated` is the distinction you need**, because only the written ones are files.
A concept topic is something you can edit and send a change to; a node page is produced from the
node and there is nothing to edit — if it is wrong, the node's own documentation is wrong.

**`--topic` prints one page as Markdown**, which is `man` for a Spark node:

```
$ spark docs --topic nodes.Spark.Nodes.Core/Point.FromCoordinates
---
id: nodes.Spark.Nodes.Core/Point.FromCoordinates
title: Point.FromCoordinates
nodes: [Spark.Nodes.Core/Point.FromCoordinates]
related: [concepts.lacing, concepts.code-blocks]
---

# Point.FromCoordinates

Makes a point from its three world coordinates.
...
```

Get the id wrong and it says so and suggests, rather than printing nothing and succeeding:

```
$ spark docs --topic nodes.Point.FromCoordinates
spark: no help topic 'nodes.Point.FromCoordinates'.
spark: did you mean:
  nodes.Spark.Nodes.Core/Point.FromCoordinates
  nodes.Spark.Nodes.Core/Vector.FromCoordinates
$ echo $?
1
```

**`--out` writes them all out as Markdown files** — for publishing, for reading on a machine with no
Spark, or for putting a documentation change in a pull request diff:

```
$ spark docs --out site
spark: wrote 234 topic(s) to site
```

The tree mirrors what a topic id means — kind, then package, then node — so the page above lands at
`site/nodes/Spark.Nodes.Core/Point.FromCoordinates.md`. **Writing twice gives identical bytes**, so
regenerating a published tree produces an empty diff rather than churn, and the files use line feeds
on every platform so a tree written on Windows and one written on Linux are the same.

**Nothing is deleted.** The directory is created if it is missing and files in it are overwritten,
but a topic that no longer exists leaves its old file behind — emptying a directory somebody named
is not this verb's business, and a mistyped path would make that failure silent and total.

---

## `spark --version`

Prints the version, and the third-party notice that the licence requires: which kernel is loaded,
under what licence, and that it is dynamically linked and replaceable. The same text appears in the
application's About box, from one source, because two copies of a licence notice is one copy that
stops matching the build.

---

## All seven exist

This section listed what was missing from the day the topic was written until **2026-09-15**, when
`render`, `pkg`, `graph` and `docs` all landed and emptied it. `spark --help` names every verb it
accepts and accepts every verb it names.

---

## Related

- [Saving and opening graphs](files.md) — what is in the file these verbs read.
- [How a graph evaluates](evaluation.md) — what a diagnostic is, and why errors do not cascade.
- [Code blocks](code-blocks.md) — and why `--no-script` refuses rather than skips.
