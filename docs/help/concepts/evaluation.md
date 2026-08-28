---
id: concepts.evaluation
title: How a graph runs
nodes: []
related: [concepts.nodes-and-wires, concepts.lacing, concepts.design-language]
since: "0.1"
---

**Status:** Current. Describes `Spark.Engine`'s evaluator, which exists and is tested. Every
worked example on this page was **executed against the built assemblies**, and the numbers in
them are what the engine printed.
**Owner:** `graph-engine`
**Last updated:** 2026-08-28

> This is the topic the wiring and evaluation diagnostics resolve to — `SPK1010` through
> `SPK1014`, listed with their meanings in [§8](#8-diagnostic-codes). The replication
> diagnostics, the `SPK1040` block, belong to [`concepts.lacing`](lacing.md) §7 instead.

---

## Why this page exists

You will spend more time reading a graph that went wrong than writing one that goes right, so
the useful thing to know is not *how to run a graph* — you press Run — but **what the engine
did, and what it is telling you when one node has gone red and six have gone grey.**

Five ideas cover it, and every one of them is visible on the canvas:

1. Nodes run in **dependency order**, not in the order you placed them.
2. Editing something marks it and everything downstream **dirty**. Nothing else re-runs.
3. Results are **cached against where they came from**, which is why undo is instant.
4. An **error** means no output. A **warning** means output with caveats.
5. The node downstream of an error goes **grey, not red** — because there is nothing wrong
   with it.

---

## 1. Dependency order, not left to right

A Spark graph is a **dataflow** graph. A node cannot run until every node feeding it has
produced a value, and beyond that constraint the engine is free. It does not care where you
dragged the node, whether it is above or below its neighbour, or which one you created first.

The engine sorts the graph into **levels**. Everything in one level depends only on earlier
levels, and therefore on nothing in its own level — so a level is a set of nodes that can all
run at the same time. The desktop application runs a level's nodes in parallel across the
thread pool; the command line and the documentation harness run them one at a time, in order,
because a run that produces the same diagnostics in the same order every time is a run whose
output can be checked against a file.

```text
Number.Range ──┐
               ├──> Point.ByCoordinates ──> Point.Translate
Number.Range ──┘                                  ▲
                                                  │
                            Vector.ZAxis ─────────┘

level 0   Number.Range, Number.Range, Vector.ZAxis     (nothing feeds them)
level 1   Point.ByCoordinates
level 2   Point.Translate
```

Two consequences worth having:

- **Layout is documentation, not instruction.** Rearranging nodes to make a graph readable
  cannot change what it produces. Feel free.
- **There is no "first" node in a level.** If two nodes in the same level both write to the
  same place outside the graph, the order in which they do it is not defined. That is what
  the side-effect flag on a node definition is for, and it is why nodes that touch the
  outside world are rare.

---

## 2. Dirt — why editing one number does not re-run everything

Every change you make to a graph marks the node you changed, **and every node reachable from
it**, as dirty. Nothing upstream is touched, and nothing off to the side is touched.

Typing a new number into a slider that feeds one branch of a fifty-node graph dirties that
branch and leaves the other forty clean.

```text
Number.Value(a) ──┐
                  ├──> Math.Add ──> Math.Multiply
Number.Value(b) ──┘                      ▲
                                         │
                              literal 10 ┘

edit b from 3 to 4   →   dirty = { Number.Value(b), Math.Add, Math.Multiply }
                         clean = { Number.Value(a) }
```

That is the run the engine actually performed: **3 nodes evaluated, 1 served from cache.**

Deleting a wire dirties the node that lost its input. Adding one dirties the node that gained
it. Changing a node's lacing dirties it, because lacing changes the answer.

---

## 3. The cache, and why undo is instant

Every result the engine computes is stored against a key describing **everything that could
have changed the answer**: which node definition it was, that definition's version, the
node's effective lacing, the document tolerance, and the keys of the values on every input
port. Two nodes with the same key computed the same thing, whoever they are and whenever they
ran.

Three behaviours fall out of that, and all three are things you will notice.

### Undo really is free

Continuing the graph above — edit `b` from 3 to 4, then undo it:

```text
run 1   nothing cached           4 evaluated, 0 from cache   →  50
run 2   nothing changed          0 evaluated, 4 from cache
run 3   b edited 3 → 4           3 evaluated, 1 from cache   →  60
run 4   the edit undone          0 evaluated, 4 from cache   →  50
```

**Run 4 computed nothing at all.** The old results were still in the cache under their old
keys, and putting `b` back to 3 reproduced those keys exactly. This is why undo, redo and
dragging a slider back to where it started are instant rather than merely fast, and it is why
the dirty set in [§2](#2-dirt--why-editing-one-number-does-not-re-run-everything) is a hint
about what to try rather than the mechanism. The engine computes a key for every node on
every run — it has to, because a node's key is built from its inputs' keys — and once it has
one, checking the cache is a single lookup.

### Two identical nodes cost one computation

Place two `Number.Range` nodes, give them the same start, end and step, and feed one into a
point's *x* and the other into its *y*. Their keys are identical, so the second one is a
cache hit:

```text
Number.Range(0, 9, 1) ──> x ┐
                            ├─ Point.ByCoordinates, Cross Product  →  10 × 10 grid
Number.Range(0, 9, 1) ──> y ┘

2 nodes evaluated, 1 served from cache, 0 diagnostics
```

You do not have to plan for this and there is no "reuse" node. Sameness is discovered.

### Changing document tolerance invalidates exactly what it should

The document tolerance is part of every key rather than a setting the engine reads on the
side. Change it and every cached result becomes unreachable, because every key changed —
which is the correct answer, and is the answer you would not get if tolerance were ambient.

**One caveat, stated plainly.** The cache holds a fixed number of results and discards the
least recently used when it is full. The honest budget would be memory rather than a count,
because a thousand points and a thousand meshes are not the same weight — measuring the size
of an arbitrary graph value is its own problem and it is not solved yet. In a session that
produces very large results you may see a node re-compute that you expected to be cached.

---

## 4. Error, warning, information

A node finishes a run in exactly one of five states, and the canvas draws each one
differently ([`concepts.design-language`](design-language.md) §7.7).

| State | What it means | Output? | Downstream |
|---|---|---|---|
| **Evaluated** | It ran and had nothing to say | yes | runs |
| **Warning** | It ran and produced output with a caveat | yes | runs |
| **Error** | It could not produce a value | **no** | *not evaluated* |
| **Not evaluated** | Something it needs produced no value | no | *not evaluated* |
| **Cycle** | It sits on a loop, so it has no order to run in | no | *not evaluated* |

The distinction that matters is the one between **warning** and **error**, and it is entirely
about whether there is a value on the output port:

- A **warning** means *here is your answer, and here is something you should know about it*.
  The most common one by far is a replication failure: 3 of your 4 items worked. Downstream
  carries on.
- An **error** means *there is no answer*. Nothing downstream can run, because there is
  nothing for it to run on.

---

## 5. Why the node after an error goes grey, not red

This is the rule the whole error model is built around:

> **One thing wrong means exactly one thing marked wrong.**

When a node errors, every node downstream of it is marked **not evaluated** — desaturated,
dashed outline, `○` glyph — and given **no diagnostic of its own**. It is not blamed, because
there is nothing wrong with it. It simply never ran.

The alternative, which most tools do, is to let the failure cascade: the node downstream is
handed nothing, complains that it was handed nothing, and passes that complaint on. Break one
node in a fifty-node graph and you get fifty red nodes and forty-nine messages describing the
consequence rather than the cause. **The wall of errors is what hides the error.**

### Worked example — a zero divisor

```text
Math.Divide (a = 10, b = 0) ─────────────────┐
                                             │ distance
Point.Origin ──────────────> point ──> Point.Translate
Vector.ByCoordinates ──────> direction ┘
```

What the engine reported, run against the built assemblies:

```text
Math.Divide          Error          no output
                     SPK1046  'Math.Divide' failed: Divide was given a divisor of zero.

Point.Translate      NotEvaluated   no output, 0 diagnostics
Point.Origin         Evaluated      (0, 0, 0)
Vector.ByCoordinates Evaluated      (0, 0, 0)

total diagnostics: 1
```

**One diagnostic for one mistake.** `Point.Translate` is grey and silent. The two nodes off to
the side that `Math.Divide` does not feed ran normally and kept their values — an error stops
a branch, not a graph.

Note also *why* `Math.Divide` errors rather than returning infinity. An infinity would flow
downstream, become geometry nobody can see at a coordinate nobody can find, and be diagnosed
three nodes later at the wrong node. The exception stops where the mistake is.

### Worked example — a warning that flows

Now the same division, but over a list, so that only some of it fails:

```text
Number.Range(0, 3, 1) ──> b ┐
                            ├─ Math.Divide (a = 10)  ──> Math.Add (+ 100)
                            ┘
```

The divisor list is `[0, 1, 2, 3]`. The first element divides by zero; the other three are
fine. The engine reported:

```text
Math.Divide   Warning   [ null, 10, 5, 3.333… ]
              SPK1042 at [0]  1 of 4 elements failed; first at [0]:
                              Divide was given a divisor of zero.

Math.Add      Warning   [ null, 110, 105, 103.333… ]
              SPK1042 at [0]  1 of 4 elements failed; first at [0]:
                              null cannot be supplied to a port declared Double.
```

Three things to take from that:

1. **The node still produced output**, so `Math.Add` ran. A warning is not an error.
2. **The failed element leaves a hole**, not a shortened list. The result still has four
   items; item `[0]` is empty. Lists keep their shape so that item *n* here still lines up
   with item *n* there.
3. **The hole is reported again downstream**, as a second warning at the same index — which
   is correct rather than noise, because the second node genuinely could not do anything with
   that item either. The index path `[0]` is the same in both, so clicking either diagnostic
   takes you to the same element.

The full rules for per-element failure are in [`concepts.lacing`](lacing.md) §5.

---

## 6. Loops

**You cannot draw a loop.** A wire that would close a cycle is refused at the moment you
release it, with `SPK1012`, and the wire under the cursor is red before you let go. That is
deliberate: refusing the gesture is a hundred times cheaper to understand than a graph that
hangs afterwards.

A loop can still reach the engine, because a `.spark` file can be edited by hand, merged
badly, or written by an older version. When that happens the document **opens** — refusing to
open a file is never the right answer — and the run reports it:

```text
Math.Add ──> Math.Multiply ──> Math.Sin
    ▲              │
    └──────────────┘                        Math.Pi   (untouched, elsewhere)

Math.Add        Cycle          SPK1014
Math.Multiply   Cycle          SPK1014
Math.Sin        NotEvaluated   0 diagnostics
Math.Pi         Evaluated      3.141592653589793
```

Every node in the loop is marked, everything downstream of it is grey, and **the rest of the
graph still runs**. Break the loop by deleting any one wire in it.

---

## 7. Cancelling a run

A run can be interrupted between nodes and between replication elements, which is what lets
the application stay responsive while a large graph evaluates and what lets an edit made
during a run take precedence over the run it interrupted.

**Nothing computed is thrown away.** Everything finished before the cancellation is already in
the cache under its key, so resuming picks up from there rather than starting again.

---

## 8. Diagnostic codes

These are the codes that resolve to this topic. Codes are stable, are never reused, and a
withdrawn code leaves a gap rather than being recycled.

| Code | Severity | Meaning |
|---|---|---|
| `SPK1010` | Error | The two ports cannot be connected: no compatibility rule matched. See [`concepts.nodes-and-wires`](nodes-and-wires.md) §3. |
| `SPK1011` | Error | Both ports name the same type from **different assemblies**. Two packages have shipped the same type. |
| `SPK1012` | Error | The wire was refused because it would close a loop. |
| `SPK1013` | Warning | The connection is accepted through a conversion that may lose information. |
| `SPK1014` | Error | The node is part of a loop found when the graph was loaded, so it has no order to evaluate in. |

The replication codes — `SPK1040` to `SPK1046`, which includes the *a node threw and there was
no list to isolate it in* case you will see most often — are specified in
[`concepts.lacing`](lacing.md) §7 and resolve there.

---

## 9. What is not true yet

Named here rather than discovered, because each one is a real limit of the current build.

- **A conversion checked when you draw the wire is not applied when the graph runs.**
  Registered converters take part in deciding whether a wire is *allowed* — including the
  lossy warning `SPK1013` — but the engine does not currently run the conversion on the value.
  A wire accepted this way fails at run time with `SPK1041` instead, naming the two types. The
  conversions built into the language are unaffected: widening `int` to `double`, and passing
  any value to a port declared `object`, both work.
- **Preview is terminal ports only.** Geometry appears in the viewport for nodes whose output
  feeds nothing else. A node in the middle of a chain does not draw, and there is no per-node
  preview toggle yet.
- **The cache counts entries, not bytes** — see [§3](#3-the-cache-and-why-undo-is-instant).
- **One appearance per output port.** A node's whole output is drawn one way; individual
  elements of a list cannot yet be coloured or selected separately, even though a diagnostic
  can already point at one.

---

## Related

- [`concepts.nodes-and-wires`](nodes-and-wires.md) — what the pieces are, and why a wire is
  refused
- [`concepts.lacing`](lacing.md) — what happens when you give a node a list where it wanted
  one thing, and the `SPK1040` diagnostics
- [`concepts.design-language`](design-language.md) §7.7 — exactly how *error*, *warning* and
  *not evaluated* are drawn
