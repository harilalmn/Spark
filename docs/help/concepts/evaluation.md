---
id: concepts.evaluation
title: How a graph evaluates
nodes: []
related: [concepts.lacing, concepts.files, concepts.undo]
since: "0.1"
---

**Status:** Current. Describes the evaluator in the running application.
**Owner:** `graph-engine`
**Last updated:** 2026-08-31

> **Scope.** What happens between pressing run and seeing geometry: the order nodes run in, what
> gets skipped, what gets reused, and what the five wiring diagnostics mean. How a node handles a
> *list* is a separate subject — see [Lists, ranks and lacing](lacing.md).

---

## The short version

Spark works out which nodes depend on which, runs them in dependency order, and reuses any answer
it has already computed. Nothing runs before its inputs are ready, and nothing runs twice for the
same reason.

## Order comes from the wires, not the canvas

Where a node sits on the canvas has no effect on when it runs. The wires are the only thing that
decides. Spark sorts the graph into **levels**: level 0 is every node that depends on nothing,
level 1 is everything that depends only on level 0, and so on.

**Everything in one level is independent of everything else in that level.** That is what makes a
level the unit of parallelism — Spark may run a level's nodes in any order, on any threads, and
you cannot tell which it chose from the result.

A worked example. Take this graph:

```
Number.Range ──┐
               ├──> Point.ByCoordinates ──> Circle.ByCentreRadius ──> Watch
Number(5) ─────┘
```

| Level | Nodes | Why |
|---|---|---|
| 0 | `Number.Range`, `Number` | Nothing feeds them |
| 1 | `Point.ByCoordinates` | Both its inputs are level 0 |
| 2 | `Circle.ByCentreRadius` | Its input is level 1 |
| 3 | `Watch` | Its input is level 2 |

Move any node anywhere on the canvas and the table is unchanged.

## An answer already computed is not computed again

Spark caches results **by provenance**. A node's answer is filed under what produced it — its
definition, the keys of its inputs, and the document tolerance — and never under which document it
was in or when it ran.

Two consequences you will notice:

- **Editing one node re-runs that node and what is downstream of it, and nothing else.** Change a
  radius and the points feeding it are not recomputed, because nothing about them changed.
- **Going back to a state you were in before costs nothing.** Undo, or retyping a value you had a
  minute ago, asks for results that are still filed under exactly the keys they had then. See
  [Undo and redo](undo.md); the measured claim there is that a run after an undo recomputes
  **zero** nodes.

## Cycles

A wire that would make a node depend on itself, directly or through others, is **refused as you
draw it** — that is `SPK1012`. You cannot build a cycle by hand.

You can still *open* one, because a `.spark` file is a file and a file can be edited or produced by
something else. When that happens the graph opens rather than being refused, every node on the
cycle reports `SPK1014`, and everything downstream of the cycle is left un-evaluated. **The rest of
the graph still runs.** Break the loop by deleting one of its wires and the affected nodes recover
on the next run.

Spark never hangs on a cycle. The sort terminates on any graph, cyclic or not, and reports what it
could not order instead of following it round.

## The five wiring diagnostics

These are the codes about *structure* — whether a wire can exist at all. They are raised when you
draw a wire, not when you run.

| Code | What it means | What to do |
|---|---|---|
| [`SPK1010`](diagnostics.SPK1010) | The two ports' types have no rule that connects them | Insert a node that converts, or wire something else |
| [`SPK1011`](diagnostics.SPK1011) | Both types have the same full name but come from **different assemblies** | Two packages ship the same type. Remove one, or use nodes from a single one |
| [`SPK1012`](diagnostics.SPK1012) | The wire would close a cycle | Nothing to fix — the wire is simply not possible |
| [`SPK1013`](diagnostics.SPK1013) | Accepted, but the conversion may lose information | A warning, not an error. Check the result is still what you want |
| [`SPK1014`](diagnostics.SPK1014) | This node is on a cycle found when the file was opened | Delete one wire in the loop |

`SPK1011` is worth a sentence of its own. It is refused at design time so that it can never become
a runtime *cannot cast `Foo` to `Foo`*, which is the single most confusing error a plugin system
can produce.

## Cancelling a run

A run can be interrupted, and the check happens **between nodes and between the elements of a
replicated node** — so a graph of a thousand cheap nodes stops almost immediately, and one node
doing a single very long operation stops when that operation finishes.

Everything already computed stays in the cache, so resuming after a cancel is cheap: the work
already done is not repeated.

## Freezing a branch

Select some nodes and press **Freeze**. Frozen nodes are skipped when the graph runs, and so is
everything downstream of them.

This is for the case where one branch of a graph is slow and you are working on another. Rather
than deleting the slow part and putting it back, you switch it off and leave it exactly where it
is: the nodes keep their wires, their values and their positions, and the file saves with the
freeze recorded, so it is still frozen when you open it tomorrow.

**Frozen nodes are greyed and carry a `""" + chr(0x2016) + """` mark.** Nodes downstream of them are greyed as well and carry
a `""" + chr(0x25CB) + """`, the same mark any node gets when it did not run. Spark reports the freeze **once**, on
the node you froze, as information rather than as a problem:

```text
SPK1070  Number.Range
'Number.Range' is frozen, so it was not evaluated and nothing downstream of it ran.
Unfreeze it to bring the branch back.
```

Nothing downstream reports anything of its own. One frozen node at the head of a long branch would
otherwise fill the diagnostics pane with fifty copies of a situation you created on purpose.

**Freezing a node in a group freezes the whole group.** A group is your own statement that those
nodes are one thing, and leaving half of it running would give you a branch that is neither on nor
off.

Press **Unfreeze** to bring it back. The button says which of the two it will do before you press
it, and it offers to unfreeze only when everything selected is already frozen.

| Question | Answer |
|---|---|
| Does a frozen node keep its last value? | No. It produces nothing, and downstream produces nothing. |
| Does freezing change the file? | Yes """ + D + """ the flag is saved, so the freeze survives reopening. A graph with nothing frozen saves exactly as it did before. |
| Does a frozen node still show errors? | No. It did not run, so it has nothing to report. |
| Can I freeze part of a group? | No. Selecting one member freezes all of them. |

## What this does not cover

- **How a node handles a list** — that is replication, and it has its own topic:
  [Lists, ranks and lacing](lacing.md).
- **Why a node produced the wrong number.** Evaluation decides *when* a node runs; what it computes
  is the node's own business. Put a Watch node on its output and look.
