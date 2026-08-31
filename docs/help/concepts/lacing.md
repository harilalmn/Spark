---
id: concepts.lacing
title: Lists, ranks and lacing
nodes: []
related: [concepts.lists, concepts.evaluation]
since: "0.1"
---

**Status:** Current, and **executed**. Written before the engine as a specification; the engine
now exists and matches it. The **90-row case table** in [section 6](#6-the-case-table) is run twice
over on every build: once against the values it specifies, and once to check that every diagnostic
it raises carries a help topic.
**Owner:** `graph-engine`
**Last updated:** 2026-09-02

> This topic is both an end-user help page and the executable specification for Spark's
> replication engine. The [case table](#the-case-table) at the end is consumed directly as
> xunit `[Theory]` data by `tests/Spark.Engine.Tests`. If the table and the implementation
> disagree, the table is right.

---

## 1. I have ten points and one radius. What happens?

You get ten circles.

This is the single most important thing to know about Spark, and it is the thing that
makes a node graph worth using at all. A node like `Circle.ByCenterRadius` was written to
take **one** point and **one** number. You gave it a list of ten points and one number.
Rather than complaining, Spark ran the node ten times — once per point, reusing the same
radius each time — and handed you a list of ten circles.

That automatic "run it once per item" behaviour is called **replication**. The rules that
decide *how* the items are paired up when more than one input is a list are called
**lacing**.

Now give it ten points and ten radii. What should happen?

There are two entirely reasonable answers, and Spark cannot guess which one you meant:

- **Ten circles.** The first point with the first radius, the second with the second, and
  so on. Pair them up.
- **One hundred circles.** Every point with every radius. All combinations.

So you choose. Every node has a **lacing mode**, and it decides which of those answers you
get. There are five modes, and the two above are called **Longest** and **Cross Product**.

**Where you change it:** select the node, and use the **Lacing** dropdown at the top of the
Properties pane. It is one undo step, and the graph re-runs when you change it — lacing is
part of a node's cache key, so the node and everything downstream of it produce different
answers.

That is the whole idea. Everything below is precision.

### The words you will see

| Word | What it means |
|---|---|
| **Scalar** | A single value. One number, one point, one circle. |
| **List** | Several values in order. `[1, 2, 3]`. |
| **Nested list** | A list whose items are themselves lists. `[[1, 2], [3, 4]]`. A grid. |
| **Rank** | How deeply nested a value is. A scalar is rank 0, a list is rank 1, a list of lists is rank 2. |
| **Replication** | Running a node once per item instead of once. |
| **Lacing** | The rule for pairing items when more than one input is a list. |

**Rank is the concept that makes the rest simple.** Spark does not ask "is this a list?" —
it asks "how much deeper is this than the node wanted?" That difference is the whole
mechanism, and it is why a list of lists of points behaves predictably instead of
mysteriously.

---

## 2. The formal model

### 2.1 Rank of a value

```text
rank(v) = 0                                  if v is not a list
rank(v) = 1 + max(rank(e) for e in v)        if v is a list
rank(v) = 1                                  if v is an empty list literal
```

A `SparkList` stores its own rank, so `rank` is O(1) and never walks the data. Nothing in
Spark infers rank from a .NET type at run time: a `string` is a scalar, a `Point3d` is a
scalar, and a `double[]` handed to Spark by a third-party library is a scalar unless it was
built as a `SparkList`. This is precisely why `SparkList` exists rather than
`IEnumerable<object>` — the question "is this a list or an opaque value the node wants
whole?" gets one answer, once, at the boundary.

> **Decision D9 — rank of a ragged list is the maximum depth of any branch.**
> `[1, [2, 3]]` has rank 2. The alternatives were to take the depth of the first element
> (fast, and wrong the moment the shallow branch comes first) or to refuse ragged lists
> outright (they arise constantly from filters and from `List.GroupBy`, so refusing them
> refuses ordinary work). Maximum depth is safe because replication re-evaluates excess at
> every level, so the shallow branches simply stop replicating sooner and arrive at the node
> whole. Ragged in, ragged out, same shape.

### 2.2 Declared rank of a port

Every input port has a **declared rank**, taken from the C# signature of the member the
node was generated from:

| Parameter type | Declared rank |
|---|---|
| `double x` | 0 |
| `Point3d center` | 0 |
| `object value` | 0 |
| `IReadOnlyList<double> xs` | 1 |
| `IReadOnlyList<Point3d> points` | 1 |
| `IReadOnlyList<IReadOnlyList<Point3d>> grid` | 2 |

Note the third row. **`object` declares rank 0.** A parameter typed `object` will happily
hold a list at run time, but as far as replication is concerned it wants a single value —
so passing it a list replicates. Node authors who want the list itself must say so with
[`[KeepStructure]`](#210-author-attributes).

### 2.3 Excess and depth

For each input port `i`:

```text
excess(i) = rank(supplied value) − declaredRank(i)
depth     = max over all inputs of excess(i)
```

An input with `excess(i) > 0` is a **replicating input**. Everything else broadcasts.

Then, at every step:

- **`depth <= 0`** — promote any negative-excess inputs (§2.5) and **call the node once**.
- **`depth > 0`** — **replicate one level and recurse.** Iterate the replicating inputs by
  one level according to the lacing mode, broadcast every other input unchanged into every
  iteration, and re-run this entire procedure on each resulting set of arguments.

**One level, then recurse.** Spark never flattens a nested list, computes over the flat
form and reshapes the result. Structure is preserved because it is never destroyed. This
is why ragged input produces ragged output of exactly the same shape, and why a rank-4
input does not need a special case.

**Recursion compounds the rank.** The mode table below says Shortest adds `+1` — that is
`+1` *per level of replication*. An input with excess 2 replicates twice, so the output
gains two levels, not one.

### 2.4 Outermost-first alignment

When inputs have *different* positive excesses, every one of them iterates at the outer
level. Excess is then recomputed for the inner call, so an input with smaller excess simply
stops iterating sooner.

> **Decision D1 — positive-but-sub-maximal excess iterates outermost-first.**
> Given `Add(double a, double b)` with `a = [[1,2],[3,4]]` (excess 2) and `b = [10,20]`
> (excess 1), `b` iterates at the outer level alongside `a`. The result is
> `[[11,12],[23,24]]`, not `[[11,22],[13,24]]`.
> The rejected alternative was to align inputs at their *innermost* levels, so that `b`
> would broadcast whole into the outer level and iterate inside. That reading is defensible
> and is what some array languages do, but it makes the pairing depend on a quantity
> (`depth`) computed from a *different* input, which is impossible to predict while looking
> at a canvas. Outermost-first means "everything that is deeper than its port iterates now",
> which is a rule a user can hold in their head.
>
> **This is less a choice than a consequence.** Innermost alignment requires knowing an
> input's *total* remaining depth before the first iteration, so that it can be held back
> for exactly the right number of levels. The recursive formulation in §2.3 deliberately
> does not know that: it computes excess, replicates one level, and recomputes from
> scratch. Outermost-first is the only alignment expressible in those terms. Adopting
> innermost alignment would mean abandoning "replicate one level and recurse" — and with
> it the property that makes ragged and arbitrarily deep input work without special cases.

### 2.5 Promotion — when excess is negative

If `excess(i) < 0`, the value is too shallow for the port. Spark **promotes** it by
wrapping it in a one-element list, repeatedly, until the excess is zero:

```text
Sum(IReadOnlyList<double> xs)    given  5          →  Sum([5])       → 5
Total2d(IReadOnlyList<IReadOnlyList<double>> rows)
                                  given  [1,2]      →  Total2d([[1,2]])  → 3
                                  given  5          →  Total2d([[5]])    → 5
```

> **Decision D2 — promotion happens at the leaf, after replication.**
> Excess is computed once per call, but a negative-excess input is *not* wrapped before
> replication begins; it broadcasts unwrapped and is wrapped only in the leaf call that
> actually invokes the node. This matters because wrapping first would change that input's
> rank, which would change nothing for it but would be observable through `[NoReplication]`
> diagnostics and through error messages. Promoting at the leaf keeps the reported ranks
> equal to the ranks the user can see on the wire.

If the wrapped value still cannot be marshalled into the declared parameter type — a
`string` promoted towards `IReadOnlyList<double>`, say — that is a typed **Error**
(`SPK1040`), the node produces no output, and downstream nodes are greyed as *not
evaluated* rather than flooded with errors of their own.

### 2.6 The five lacing modes

`n` below is the number of iterations at the current level. It is computed **over the
replicating inputs only** — inputs with excess `0` or less never contribute to `n` and
never iterate.

| Mode | `n` | Behaviour | Rank added per level |
|---|---|---|---|
| **Shortest** | `min` | Zip; truncate to the shortest replicating input. | +1 |
| **Longest** | `max` | Zip; shorter inputs repeat their **last** element. | +1 |
| **Cross Product** | `∏` | Every combination, nested. | **+k**, where `k` = the number of replicating inputs |
| **Auto** | *(none of its own)* | **Not a replication algorithm.** A sentinel meaning "use this node definition's `DefaultLacing`". Resolved to one of the four modes above before replication begins. | *(whatever it resolves to)* |
| **Disabled** | — | No replication at all; values pass through whole. | 0 |

**Four of these are algorithms; `Auto` is a sentinel.** `Auto` has no `n`, no pairing rule
and no output rank of its own — every one of those comes from the mode it resolves to. It
is the only entry in the table that cannot be described without reference to a particular
node. §2.9 says how it resolves.

Three of the algorithm rows carry the bugs. They get their own sections.

### 2.7 Cross Product raises rank by *k*, not by 1

**Ten x-values crossed with ten y-values is a 10 × 10 nested list of rank 2. It is not a
flat list of 100.**

This is the mistake every implementation of this idea makes at least once, and it is
invisible in a watch node unless you look at the indentation. Cross Product is nested
loops, and nested loops produce nested output — one level of nesting per replicating
input:

```text
Add(a, b), Cross Product, a = [1,2,3], b = [10,20]

          b[0]=10   b[1]=20
a[0]=1      11        21        →  [ [11,21],
a[1]=2      12        22            [12,22],
a[2]=3      13        23            [13,23] ]      rank 2, shape 3×2
```

With three replicating inputs the output is rank 3. With `k` replicating inputs it is rank
`k`. Inputs that are *not* replicating (excess 0) are not dimensions — they broadcast into
every cell and add nothing to the rank.

The dimension order is the port order, outermost first: port 0 is the outer loop. Override
it with [`[ReplicationGuide(n)]`](#210-author-attributes).

Cross Product also compounds through recursion, exactly like the zip modes. If a cell's
arguments are themselves deep enough to replicate again, the cell's own result adds further
levels. Case 47 in the table is a rank-3 result from a two-input cross product for exactly
this reason.

### 2.8 Disabled is not a niche setting

It is the correct default for an entire category of node.

Consider `List.Count`. Its declared rank is 1 — it wants a list. Now hand it a list of
lists, which is what you get from any Cross Product upstream. Its excess is 1. Under any
replicating mode it would run **once per inner list** and return `[2, 2, 2]` — the count of
each row — when what you asked for was `3`, the number of rows.

Every node that is *about* list structure has this problem: `List.Count`,
`List.Flatten`, `List.Reverse`, `List.Transpose`, `List.GetItemAtIndex`. They are declared
at rank 1 (or over `object`), and they routinely receive rank 2. **They must never lace at
their declared rank.** Spark handles this two ways, and node authors should prefer the
first:

1. **`[KeepStructure]` on the port** — the port's declared rank becomes unbounded, so the
   value arrives verbatim no matter what mode the user selects. This is the author's fix
   and it cannot be broken by a user changing lacing on the node.
2. **`DefaultLacing = Disabled` on the node definition** — the node instance is created
   with lacing off. This is a weaker fix: a user can turn lacing back on and get the wrong
   shape. It is the right choice only when a user might *legitimately* want to lace the
   node.

`Disabled` is also what you reach for when you have a list and you want a node to see the
list, not its items — feeding a list of points to a node that draws one polyline through
them, for instance, when the node's port was declared at rank 0.

**Under `Disabled`, rank is not repaired.** If the node cannot marshal what it is given,
that is a typed Error (`SPK1041`), not a warning and not a silent nothing.

> **Decision D3 — `Disabled` still promotes.**
> `Disabled` switches off replication, not rank reconciliation. A scalar `5` fed to
> `Sum(IReadOnlyList<double>)` under `Disabled` is still promoted to `[5]` and still
> returns `5`. The rejected alternative — `Disabled` means "touch nothing at all" — makes
> the mode useless on exactly the nodes it exists to serve, because it would turn every
> harmless scalar-into-a-list-port case into an error.

### 2.9 Auto means "use this node's default"

**`Auto` is a sentinel, not an algorithm.** It means: *I have not overridden this node's
lacing; use whatever its author chose.* At the start of evaluation the engine resolves it:

```text
if instance.LacingMode == Auto:
    effectiveMode := definition.DefaultLacing
else:
    effectiveMode := instance.LacingMode
```

Everything after that line is one of the four real modes. `Auto` never reaches the
replication procedure.

This is why `Auto` is the value a freshly placed node carries. Placing a node does not
express an opinion about lacing, and `Auto` is how the graph records the absence of one.
It also means a node author's judgement actually reaches users: a node that produces a grid
can declare `DefaultLacing = CrossProduct` and it will *behave* as a grid node the moment
someone drops it on the canvas, without the user having to know that Cross Product was the
right answer.

Two consequences worth stating plainly:

- **Two nodes both set to `Auto` can lace differently.** That is the point, not a bug. What
  they share is "not overridden", not a behaviour.
- **`DefaultLacing` may not itself be `Auto`.** A definition that declares no default gets
  `Longest`, and the reflection importer refuses `Auto` as a declared default rather than
  resolving a chain. There is exactly one hop.

Most cases in the table that use `Auto` sit on a `Longest`-defaulting node, so they assert
that the resolution *happens* and lands on the right mode. [Group J](#group-j--auto-resolution)
asserts that it **matters**: cases 86 and 87 put `Auto` and an explicit `Longest` on the
same `CrossProduct`-defaulting node and get different **ranks**, which is the observation an
`Auto`-as-synonym-for-Longest design could never produce.

> **Decision D4 — `Auto` resolves to the node definition's `DefaultLacing`.**
> The plan describes `Auto` as "Longest, but inputs with excess 0 never iterate". Under the
> core model in §2.3 that is not a distinction at all: inputs with excess 0 never iterate
> under *any* mode, because `n` is computed over replicating inputs only. Read literally,
> `Auto` would be a mode that provably never differs from `Longest` — a wart a user would
> find in the first week and file as a bug.
>
> Two alternatives were rejected. **Keeping `Auto` as a synonym for `Longest`**, justified
> by provenance and forward compatibility, ships a menu entry that does nothing. And
> **giving `Auto` teeth by making Shortest, Longest and Cross Product *force* every
> list-valued input to iterate regardless of declared rank**, leaving `Auto` as the only
> rank-respecting mode, would make all five distinct — but it would also mean that selecting
> `Longest` on `Sum`, a rank-1 node, starts summing individual numbers. An explicit mode
> selection must never be able to violate declared rank.
>
> Deferring to `DefaultLacing` is the correct semantics for a per-instance override of a
> per-definition default, it is what makes `Auto` the sensible stored default, and it keeps
> the name users arriving from other tools expect while giving it something real to do.

### 2.10 Author attributes

| Attribute | Effect |
|---|---|
| `[NoReplication]` | The port is excluded from `depth` and from `n`, and never iterates. Its value broadcasts whole into every leaf call. Promotion still applies. If the supplied rank still exceeds the declared rank at the leaf, that is an Error (`SPK1043`). Use for options and settings objects, where fanning the node out over a list of settings is never what the user meant. |
| `[KeepStructure]` | The port's declared rank is treated as unbounded. It never replicates, never promotes, and never rank-errors; the node receives the value exactly as supplied. Implies `[NoReplication]`. Use for nodes that consume list structure. |
| `[ReplicationGuide(n)]` | Sets this port's Cross Product dimension. Dimensions nest in ascending guide order, outermost first. Ports without a guide keep their port index. Duplicate guides among replicating ports are an Error (`SPK1044`). Has no effect in any other mode. |

> **Decision D5 — `[NoReplication]` and `[KeepStructure]` are not the same attribute.**
> They are close enough to merge, and the plan does not distinguish them. The split above is
> deliberate: `[NoReplication]` says *"do not fan my node out over this port"* while still
> type-checking the port, so a user who wires a list into a settings port gets a clear
> `SPK1043` telling them the port cannot be laced. `[KeepStructure]` says *"this port is
> about structure, hand it over untouched"*, which necessarily disables the rank check as
> well — there is no rank that is wrong for it. Merging them would force `List.Count` to
> accept a nonsensical rank error, or force the settings port to silently swallow a list.

### 2.11 Multi-output nodes replicate in lockstep, then transpose

A node with several outputs replicates **once**, not once per output. The results are
collected as tuples and then transposed on the way out, so each output port carries a list
of that output's values:

```text
Bounds(IReadOnlyList<double> xs) -> (double min, double max)

  input   [[1,2,3],[10,20]]
  leaves  Bounds([1,2,3]) = (1,3)     Bounds([10,20]) = (10,20)
  ports   min = [1, 10]      rank 1
          max = [3, 20]      rank 1
```

Not `[(1,3),(10,20)]` on one port, and never one list of pairs. Every output port of a
laced node has the same shape and the same rank as every other, and that shape is the
shape replication produced. Under Cross Product, every output is rank `+k`.

### 2.12 Per-element failure is isolated

If element 37 of 500 throws, the other 499 still evaluate.

- Slot 37 in the output becomes `null`.
- The node reports a **Warning** (`SPK1042`), not an Error, naming the number of failed
  elements and the index and message of the first failure.
- Downstream nodes still evaluate. A warning means *output with caveats*; only an Error
  means no output.
- The `ElementPath` on the diagnostic is the full index path, so a failure inside a nested
  result reports `[3][1]`, not `4`.

The fast path costs nothing: replication runs with no exception handling until the first
failure, then restarts the level with catching enabled.

> **Decision D6 — all elements failing is still a Warning.**
> `Invert([0,0])` yields `[null, null]` with a Warning, not an Error. The rejected
> alternative — promote to Error when every element fails — was tempting, because a
> list of nothing but nulls is almost certainly a mistake. It was rejected because the
> threshold is arbitrary (why not 99%?), because it makes a node's severity depend on data
> rather than on structure, and because a graph that intermittently flips between Warning
> and Error as a slider moves is worse to debug than one that is consistently loud. The
> warning text names the count, so "500 of 500 elements failed" is not exactly subtle.

### 2.13 Empty lists

> **Decision D8 — an empty list still has a rank, and carries it explicitly.**
> `SparkList` stores its rank rather than deriving it from its contents, so the empty list
> produced by a Cross Product over two dimensions is rank 2, not rank 1. The rejected
> alternative — every empty list is rank 1 — means the rank of a graph changes when a filter
> happens to remove everything, which turns an empty result into a *shape* bug downstream
> rather than an empty one. Rank must be a property of the structure, not of the data that
> survived.

> **Decision D7 — under Longest, empty propagates. It does not pad.**
> `Add([1,2,3], [])` under Longest yields `[]` (rank 1), not `[11,12,13]` and not
> `[null,null,null]`.
> `n = max` would say 3, but the empty input has no last element to repeat, so "repeat the
> last element" is undefined for it. The three candidate answers were: fabricate `null` for
> the missing input (produces a list of failures from a perfectly ordinary situation);
> ignore the empty input and lace over the rest (silently drops a wire's contribution,
> which is the worst kind of bug); or propagate emptiness. Emptiness propagates, because
> "nothing in, nothing out" is the only answer that stays true when the empty list is a
> filter result — which is where empty lists actually come from.
>
> **Because `max` would have suggested otherwise, Longest emits an informational
> Warning (`SPK1045`) when some replicating inputs are empty and others are not.** Shortest
> is silent, because `min = 0` is exactly what a user asked for.

Cross Product does not need a special rule: an empty dimension contributes zero iterations,
so the nested loops naturally produce an empty skeleton. `[1,2] × []` is `[[],[]]` and
`[] × [1,2]` is `[]` — both rank 2.

### 2.14 `null` elements

`null` is a rank-0 value like any other. Replication does not skip it, does not treat it as
an empty list, and does not shorten a list containing it. A node that tolerates `null`
receives it; a node that does not throws, and §2.12 takes over.

### 2.15 The procedure, in full

```text
Evaluate(node, args):
    mode := (node.Instance.LacingMode == Auto)
                ? node.Definition.DefaultLacing        // exactly one hop; never Auto again
                : node.Instance.LacingMode
    return Replicate(node, args, mode)


Replicate(node, args, mode):

    for each input i:
        if port i is [KeepStructure]:  excess(i) := 0
        else:                          excess(i) := rank(args[i]) − declaredRank(i)

    replicating := { i : excess(i) > 0 and port i is not [NoReplication]
                                        and port i is not [KeepStructure] }

    depth := max(excess(i)) over replicating, or 0 if replicating is empty

    if mode == Disabled or depth == 0:
        for each input i that is not [KeepStructure]:
            if excess(i) < 0:  args[i] := Promote(args[i], −excess(i))   // may raise SPK1040
            if excess(i) > 0 and port i is [NoReplication]:  raise SPK1043
            if excess(i) > 0:  raise SPK1041                 // Disabled, or an unlaceable rank
        return Invoke(node, args)                            // may raise SPK1046, or a
                                                             // per-element failure one level up

    switch mode:
        Shortest:  n := min(length(args[i]) for i in replicating)
        Longest:   n := 0 if any length(args[i]) == 0 else max(length(args[i]))
        CrossProduct:
                   dims := replicating ordered by ReplicationGuide, then port index
                   return NestedLoops(dims, ...)             // recurses at each innermost cell

    results := []
    for j in 0 .. n−1:
        cell := args with, for each i in replicating,
                    args[i][ min(j, length(args[i])−1) ]     // Longest repeats the last
        results.append(Replicate(node, cell, mode))          // recurse
    return SparkList(results)
```

Cancellation is checked between elements, so a runaway replication over a million items
stops when you press Escape.

### 2.16 Decisions taken in this document

Each of these is a point the architecture plan leaves open. Each is settled here rather than
left to the implementation, because an undefined case is what ships as a bug.

| # | Decision | Section |
|---|---|---|
| **D1** | Inputs with positive but sub-maximal excess iterate **outermost-first**, not aligned to the innermost levels. | [§2.4](#24-outermost-first-alignment) |
| **D2** | Promotion happens at the **leaf call**, after replication, not before it. | [§2.5](#25-promotion--when-excess-is-negative) |
| **D3** | `Disabled` switches off replication but **still promotes**. | [§2.8](#28-disabled-is-not-a-niche-setting) |
| **D4** | `Auto` is **a sentinel, not an algorithm**: it resolves to the node definition's `DefaultLacing`. Two nodes both set to `Auto` may therefore lace differently. | [§2.9](#29-auto-means-use-this-nodes-default) |
| **D5** | `[NoReplication]` and `[KeepStructure]` are **distinct attributes** with distinct rank-checking behaviour. | [§2.10](#210-author-attributes) |
| **D6** | Every element failing is **still a Warning**, never an Error. | [§2.12](#212-per-element-failure-is-isolated) |
| **D7** | Under Longest an empty replicating input makes the whole result **empty**, with an informational Warning. | [§2.13](#213-empty-lists) |
| **D8** | `SparkList` carries its rank **explicitly**, so an empty list has the rank of the structure that produced it. | [§2.13](#213-empty-lists) |
| **D9** | Rank of a ragged list is its **maximum** branch depth; replication decisions are re-evaluated per branch, so ragged in gives ragged out. | [§2.1](#21-rank-of-a-value), [Group F](#group-f--ragged-nesting) |
| **D10** | When a leaf call of a multi-output node fails, **every** output port receives `null` in that slot, keeping the ports index-aligned. | [Case 85](#group-i--multi-output-nodes) |

---

## 3. Worked examples

The same two inputs, three modes. Four centre points, two radii.

```text
centers = [ A, B, C, D ]        radii = [ 1, 5 ]

Circle.ByCenterRadius(Point center, double radius)
   declared ranks:  center = 0,  radius = 0
   supplied ranks:  center = 1,  radius = 1
   excess:          center = +1, radius = +1     →  both replicate,  depth = 1
```

### Shortest — `n = min(4, 2) = 2`

```text
   A ●───┐
   B ●───┼──┐        1 ──┘  │
   C ●   │  │        5 ─────┘
   D ●   │  │
         ▼  ▼
      [ (A,1), (B,5) ]                        rank 1,  2 circles
```

C and D are dropped. Shortest is the mode that throws data away, and it is the right choice
when the extra items are meaningless — a list of four points paired with a list of
per-point settings that only two of them have.

### Longest — `n = max(4, 2) = 4`

```text
      A     B     C     D
      1     5     5     5      ← radii runs out and repeats its LAST element

   [ (A,1), (B,5), (C,5), (D,5) ]             rank 1,  4 circles
```

Note **last**, not first, and not cycling. `[1, 5]` extended to length 4 is `[1,5,5,5]`,
never `[1,5,1,5]`. Repeating the last element is what makes the common case — a list of
things paired with a single setting written as a one-item list — do the obvious thing.

### Cross Product — `n = 4 × 2 = 8`, arranged 4 × 2

```text
              radius=1   radius=5
   center=A     (A,1)      (A,5)
   center=B     (B,1)      (B,5)
   center=C     (C,1)      (C,5)
   center=D     (D,1)      (D,5)

   [ [ (A,1), (A,5) ],
     [ (B,1), (B,5) ],
     [ (C,1), (C,5) ],
     [ (D,1), (D,5) ] ]                       rank 2,  shape 4×2
```

Eight circles, in four groups of two. **Not** a flat list of eight. If you want a flat list
of eight, add a `List.Flatten` node and say so — that way the intent is visible on the
canvas.

### The classic: a grid of points

```text
xs = [0,1,2,3,4,5,6,7,8,9]      ys = [0,1,2,3,4,5,6,7,8,9]      z = 0

Point.ByCoordinates(x, y, z)  with Cross Product

   excess:      x = +1,  y = +1,  z = 0
   replicating: x, y                   →  k = 2
   result:      rank 2,  shape 10 × 10,  100 points,
                indexed [row][column] = [x index][y index]
```

`z` is not a dimension. It has excess 0, so it broadcasts into all 100 cells and adds
nothing to the rank. Change `z` to `[0, 10]` and it becomes a third dimension: rank 3,
shape 10 × 10 × 2, 200 points.

Switch the same graph to **Longest** and you get **10** points along the diagonal —
`(0,0,0), (1,1,0), … (9,9,0)` — at rank 1. That is a real result and often a real bug; see
[troubleshooting](#5-troubleshooting).

### Nesting is preserved, not flattened

```text
Add(double a, double b)      a = [[1,2],[3,4]]      b = 10

   excess: a = +2,  b = 0        depth = 2

   level 1:  a replicates, b broadcasts
             call Add([1,2], 10)  and  Add([3,4], 10)
   level 2:  a replicates again
             Add(1,10)=11  Add(2,10)=12   |   Add(3,10)=13  Add(4,10)=14

   result:  [[11,12],[13,14]]                       rank 2
```

The output has the same shape as the input, because Spark never took the shape apart. A
rank-6 input would work identically without a single line of code that knows what 6 is.

---

## 4. Choosing a mode

| You want | Mode |
|---|---|
| One list of things paired with one setting | **Longest** (also the default) |
| Two lists that correspond item-for-item, and you know they are the same length | **Longest** or **Shortest** — either works; pick Shortest if a length mismatch should silently truncate rather than pad |
| Two lists that correspond item-for-item, and a mismatch means a mistake | **Shortest**, then compare `List.Count` on both — Shortest at least fails visibly by producing a short list |
| A grid, a matrix, a parameter sweep, every-with-every | **Cross Product** |
| A node that operates on the list itself — count, reverse, flatten, sort | **Disabled**, if the node author has not already handled it with `[KeepStructure]` |
| Whatever the person who wrote the node thought was sensible | **Auto** — it defers to the node's own default, and it records that you have not overridden anything |

### Rules of thumb

- **Reach for Cross Product when you can name two independent axes.** Widths and heights.
  Levels and grid lines. Angles and radii. If you find yourself saying "for every X, for
  every Y", that is Cross Product, and the nesting it produces is a feature: rank 2 output
  is a grid you can index by row and column, and `List.Transpose` works on it.
- **Reach for Shortest when a list is a filter result.** Shortest is the mode that respects
  "there were only two matches".
- **Cross Product with three or more replicating inputs grows fast.** 100 × 100 × 100 is a
  million node invocations and a rank-3 list. Spark will do it; your patience may not.
  Check the counts before you wire the third input.
- **If a node has a `[NoReplication]` port, lacing a list into it is a design smell, not a
  clever trick.** Build the list of results a different way — usually a `List.Map`-shaped
  custom node.

---

## 5. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| **"I got 100 circles instead of a 10 × 10 grid."** | You have 100 circles at rank 1 rather than rank 2. Either a `List.Flatten` is in the chain, or the upstream produced a flat list and Cross Product was never involved. | Check the rank in the watch node — count the opening brackets, or turn on *Show rank* in the watch panel. Add Cross Product where the two axes meet, and remove any flatten between there and the watch. |
| **"I got 10 circles instead of 100."** | Longest, not Cross Product — either chosen explicitly, or reached through `Auto` because the node's own default is Longest. Longest pairs the lists; it does not combine them. | Set the node's lacing to Cross Product. |
| **"This node laced differently from an identical-looking one."** | Both are set to `Auto`, and `Auto` means *use the node definition's default*. Different node definitions declare different defaults, so two nodes that both read "Auto" in the menu can genuinely behave differently. | Look at what each `Auto` resolved to — the node's tooltip names the effective mode. If you want them to match, set both explicitly rather than relying on their defaults. |
| **"I got 1 circle."** | Shortest with a length-1 input somewhere. `min(10, 1) = 1`. | Either switch to Longest — which repeats the single item ten times — or find the upstream node that is producing one item when you expected ten. |
| **"I got 2 circles and I expected 10."** | Shortest, and one input is shorter than you think. Almost always an upstream filter. | `List.Count` every input to the node. The shortest one is your answer. |
| **"My list of lists came back as a list of numbers."** | A rank-2 value went into a rank-1 port and replicated. `List.Count` on a grid returns the count of each row, not the number of rows. | Set lacing to **Disabled** on that node, or report the missing `[KeepStructure]` to the node's author. |
| **"Everything is one level deeper than I expected."** | Under Cross Product, an input you thought was a single value is actually a one-item list. A one-item list is rank 1, so it replicates and becomes a dimension; a scalar is rank 0 and broadcasts, adding nothing. | Check every input's rank in the watch node, not its length. If a node upstream returns a one-item list, add `List.FirstItem` before the Cross Product. |
| **"I only wanted the node to run once, and it ran once per item."** | An `object` port declares rank 0. Wiring a list into one replicates, even though `object` could have held the list. | Set the node's lacing to **Disabled**, or ask the node's author for `[KeepStructure]` on that port. |
| **"Empty in, empty out — but I wanted the other list."** | Decision D7: under Longest, one empty replicating input makes the whole result empty, and `SPK1045` says so. | Filter the empty input out of the graph, or use `List.ReplaceEmpty` to substitute a default before the node. |
| **"One item is null and I do not know why."** | A per-element failure. Warning `SPK1042` names the index and the first exception. | Open the node's diagnostics. The `ElementPath` is the exact index path into the output. |
| **"The node has an Error and everything downstream is grey."** | Errors mean no output. Downstream is *not evaluated*, deliberately — cascading would turn one problem into fifty. | Fix the one red node. Grey nodes have nothing wrong with them. |
| **"It worked yesterday and today the shape is different."** | Something upstream changed rank — a filter that now returns one item instead of several, or a node whose lacing was changed. | Rank changes are invisible in a value preview. Compare ranks, not values. |

---

## 6. The case table

Everything below is the test corpus. Each row is one `[Theory]` case.

Case numbers are stable and are never reused: a new case is appended to its group and takes
the next free number, and a case that is ever withdrawn leaves its number as a gap. The
table is expected to grow as the engine finds situations this document did not anticipate —
which is the point of writing it first — so the corpus is referred to throughout as *the
case table* rather than by a count it will outgrow. The count is a fact you can read off
the table, not a claim made about it.

### Notation

| Written | Means |
|---|---|
| `5`, `2.0`, `"abc"` | A scalar. Rank 0. |
| `[1,2,3]` | A list of three scalars. Rank 1. |
| `[[1,2],[3,4]]` | A nested list. Rank 2. |
| `[]` | An empty list. Its rank is given in the **Rank** column, never inferred from the brackets. |
| `null` | A null value. Rank 0. Not a list, not an empty list. |
| `A`, `B`, `C`, `D` | Distinct `Point3d` values. |
| `p(x,y,z)` | The point with those coordinates. |
| `c(P,r)` | The circle with centre `P` and radius `r`. |
| `S`, `S1`, `S2` | Opaque settings values. |
| `—` | No output. The node errored; the port carries nothing. |
| `E:CODE` | The node reports an Error with that diagnostic code. No output. |
| `W:CODE` | The node reports a Warning with that code. Output is produced. |

The **Rank** column is the rank of the produced value and is asserted **separately from the
value**. This is not optional. A flat list of 100 and a 10 × 10 nested list can both look
right in a watch node, and a rank bug that a value-only test misses is exactly the class of
bug this document exists to prevent. For multi-output cases the rank is given per output
port.

### Nodes used

The right-hand column is each definition's **`DefaultLacing`** — what `Auto` on an instance
of that node resolves to. It is part of the fixture: a case whose mode is `Auto` is
asserting the resolved behaviour, so the default is as load-bearing as the signature.

```text
                                                        declared -> output   DefaultLacing
Add(double a, double b) -> double                        0,0     -> 0        Longest
Sum(IReadOnlyList<double> xs) -> double                  1       -> 0        Longest
Total2d(IReadOnlyList<IReadOnlyList<double>> rows)
        -> double                                        2       -> 0        Longest
Range(double n) -> IReadOnlyList<double>  // 0 .. n-1     0       -> 1        Longest
Point.ByCoordinates(double x, double y, double z)
        -> Point                                         0,0,0   -> 0        Longest
Circle.ByCenterRadius(Point center, double radius)
        -> Circle                                        0,0     -> 0        Longest
Grid.ByXY(double x, double y) -> Point                   0,0     -> 0        CrossProduct
Bounds(IReadOnlyList<double> xs)
        -> (double min, double max)                      1       -> 0,0      Longest
Split(double a, double b) -> (double sum, double diff)   0,0     -> 0,0      Longest
Invert(double x) -> double     // throws when x = 0      0       -> 0        Longest
Echo(object x) -> object       // returns x, allows null 0       -> 0        Longest
Scale(double x, [NoReplication] double factor)
        -> double                                        0,0     -> 0        Longest
List.Count([KeepStructure] object list) -> int           ∞       -> 0        Longest
List.Reverse([KeepStructure] object list) -> object      ∞       -> as given  Longest
List.Flatten(IReadOnlyList<object> list)
        -> IReadOnlyList<object>                         1       -> 1        Disabled
CountNoAttr(IReadOnlyList<object> list) -> int           1       -> 0        Longest
        // illustrative: the author forgot both [KeepStructure] and a default
```

`Grid.ByXY` is a node that produces a grid, so its author declared `CrossProduct` — this is
the node that makes `Auto` observable. `List.Flatten` is a node that consumes list
structure, so its author declared `Disabled`. Everything else takes the default default,
`Longest`.

---

### Group A — depth 0, promotion and rank reconciliation

| # | Description | Node | Declared | Inputs | Mode | Expected output | Rank | Diagnostic |
|---|---|---|---|---|---|---|---|---|
| 1 | Two scalars into two rank-0 ports; nothing replicates | `Add` | a:0, b:0 | a=`3`, b=`4` | Auto | `7` | 0 | — |
| 2 | Same, with lacing off; result must be identical | `Add` | a:0, b:0 | a=`3`, b=`4` | Disabled | `7` | 0 | — |
| 3 | Same, Cross Product with no replicating inputs; k=0 | `Add` | a:0, b:0 | a=`3`, b=`4` | CrossProduct | `7` | 0 | — |
| 4 | List into a rank-1 port; excess 0, no replication | `Sum` | xs:1 | xs=`[1,2,3]` | Auto | `6` | 0 | — |
| 5 | Nested list into a rank-2 port; excess 0 | `Total2d` | rows:2 | rows=`[[1,2],[3,4]]` | Auto | `10` | 0 | — |
| 6 | Promotion, excess −1: scalar wrapped into a one-element list | `Sum` | xs:1 | xs=`5` | Auto | `5` | 0 | — |
| 7 | Promotion, excess −1 into a rank-2 port | `Total2d` | rows:2 | rows=`[1,2]` | Auto | `3` | 0 | — |
| 8 | Promotion, excess −2: wrapped twice | `Total2d` | rows:2 | rows=`5` | Auto | `5` | 0 | — |
| 9 | Promotion still applies under Disabled (Decision D3) | `Sum` | xs:1 | xs=`5` | Disabled | `5` | 0 | — |
| 10 | Promotion that cannot be reconciled: element type is wrong | `Sum` | xs:1 | xs=`"abc"` | Auto | `—` | — | `E:SPK1040` |
| 11 | Node whose natural output rank is 1; no replication | `Range` | n:0 | n=`3` | Auto | `[0,1,2]` | 1 | — |

### Group B — one replicating input

| # | Description | Node | Declared | Inputs | Mode | Expected output | Rank | Diagnostic |
|---|---|---|---|---|---|---|---|---|
| 12 | Excess +1 on one input, scalar broadcast — Shortest | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`10` | Shortest | `[11,12,13]` | 1 | — |
| 13 | Same — Longest | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`10` | Longest | `[11,12,13]` | 1 | — |
| 14 | Same — `Auto` resolves to `Add`'s default, which is Longest | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`10` | Auto | `[11,12,13]` | 1 | — |
| 15 | Same — Cross Product with k=1 adds exactly one level | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`10` | CrossProduct | `[11,12,13]` | 1 | — |
| 16 | Same — Disabled; a list cannot become a `double` | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`10` | Disabled | `—` | — | `E:SPK1041` |
| 17 | Excess +1 on a rank-1 port | `Sum` | xs:1 | xs=`[[1,2],[3,4]]` | Auto | `[3,7]` | 1 | — |
| 18 | Same, Disabled; rank 2 will not marshal into `IReadOnlyList<double>` | `Sum` | xs:1 | xs=`[[1,2],[3,4]]` | Disabled | `—` | — | `E:SPK1041` |
| 19 | Excess +1 on a rank-2 port | `Total2d` | rows:2 | rows=`[[[1,2],[3,4]],[[5,6]]]` | Auto | `[10,11]` | 1 | — |
| 20 | Excess +2 replicates twice; two levels added | `Add` | a:0, b:0 | a=`[[1,2],[3,4]]`, b=`10` | Auto | `[[11,12],[13,14]]` | 2 | — |
| 21 | Excess +2 on a rank-1 port | `Sum` | xs:1 | xs=`[[[1,2],[3,4]],[[5],[6,7]]]` | Auto | `[[3,7],[5,13]]` | 2 | — |
| 22 | Natural output rank 1 plus one replication level | `Range` | n:0 | n=`[2,3]` | Auto | `[[0,1],[0,1,2]]` | 2 | — |

### Group C — two replicating inputs, length relationships

All use `Add(double a, double b)` with both ports declared rank 0.

| # | Description | Node | Declared | Inputs | Mode | Expected output | Rank | Diagnostic |
|---|---|---|---|---|---|---|---|---|
| 23 | Equal lengths — Shortest | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[10,20,30]` | Shortest | `[11,22,33]` | 1 | — |
| 24 | Equal lengths — Longest | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[10,20,30]` | Longest | `[11,22,33]` | 1 | — |
| 25 | Equal lengths — `Auto` resolves to Longest | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[10,20,30]` | Auto | `[11,22,33]` | 1 | — |
| 26 | Equal lengths — Cross Product, k=2, shape 3×3 | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[10,20,30]` | CrossProduct | `[[11,21,31],[12,22,32],[13,23,33]]` | 2 | — |
| 27 | Equal lengths — Disabled | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[10,20,30]` | Disabled | `—` | — | `E:SPK1041` |
| 28 | One shorter — Shortest truncates to 2 | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[10,20]` | Shortest | `[11,22]` | 1 | — |
| 29 | One shorter — Longest repeats b's **last** element | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[10,20]` | Longest | `[11,22,23]` | 1 | — |
| 30 | One shorter — `Auto` resolves to Longest | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[10,20]` | Auto | `[11,22,23]` | 1 | — |
| 31 | One shorter — Cross Product, shape 3×2 | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[10,20]` | CrossProduct | `[[11,21],[12,22],[13,23]]` | 2 | — |
| 32 | One of length 1 — Shortest collapses to a single item | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[10]` | Shortest | `[11]` | 1 | — |
| 33 | One of length 1 — Longest repeats it | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[10]` | Longest | `[11,12,13]` | 1 | — |
| 34 | One of length 1 — Cross Product, shape 3×1 (still rank 2) | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[10]` | CrossProduct | `[[11],[12],[13]]` | 2 | — |
| 35 | A length-1 list is not a scalar: rank 1 replicates, rank 0 broadcasts | `Add` | a:0, b:0 | a=`[10]`, b=`5` | Longest | `[15]` | 1 | — |
| 36 | One empty — Shortest; `min = 0`, silently empty | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[]` | Shortest | `[]` | 1 | — |
| 37 | One empty — Longest; empty propagates (Decision D7) | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[]` | Longest | `[]` | 1 | `W:SPK1045` |
| 38 | One empty — `Auto` resolves to Longest, so D7 applies | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[]` | Auto | `[]` | 1 | `W:SPK1045` |
| 39 | Both empty — Longest; no warning, nothing surprising happened | `Add` | a:0, b:0 | a=`[]`, b=`[]` | Longest | `[]` | 1 | — |
| 40 | Empty inner dimension — Cross Product keeps the skeleton | `Add` | a:0, b:0 | a=`[1,2,3]`, b=`[]` | CrossProduct | `[[],[],[]]` | 2 | — |
| 41 | Empty outer dimension — Cross Product; empty at rank 2, not 1 | `Add` | a:0, b:0 | a=`[]`, b=`[10,20]` | CrossProduct | `[]` | 2 | — |
| 42 | Empty list into a rank-1 port is excess 0, not replication | `Sum` | xs:1 | xs=`[]` | Auto | `0` | 0 | — |

### Group D — three inputs

| # | Description | Node | Declared | Inputs | Mode | Expected output | Rank | Diagnostic |
|---|---|---|---|---|---|---|---|---|
| 43 | Three replicating inputs, equal lengths — Shortest | `Point.ByCoordinates` | x:0, y:0, z:0 | x=`[1,2]`, y=`[3,4]`, z=`[5,6]` | Shortest | `[p(1,3,5),p(2,4,6)]` | 1 | — |
| 44 | Three replicating, mixed lengths — Shortest takes min(3,2,4)=2 | `Point.ByCoordinates` | x:0, y:0, z:0 | x=`[1,2,3]`, y=`[10,20]`, z=`[100,200,300,400]` | Shortest | `[p(1,10,100),p(2,20,200)]` | 1 | — |
| 45 | Same inputs — Longest takes max=4; x and y repeat their last | `Point.ByCoordinates` | x:0, y:0, z:0 | x=`[1,2,3]`, y=`[10,20]`, z=`[100,200,300,400]` | Longest | `[p(1,10,100),p(2,20,200),p(3,20,300),p(3,20,400)]` | 1 | — |
| 46 | **Three replicating inputs — Cross Product gives rank 3**, shape 2×2×2 | `Point.ByCoordinates` | x:0, y:0, z:0 | x=`[0,1]`, y=`[0,1]`, z=`[0,1]` | CrossProduct | `[[[p(0,0,0),p(0,0,1)],[p(0,1,0),p(0,1,1)]],[[p(1,0,0),p(1,0,1)],[p(1,1,0),p(1,1,1)]]]` | 3 | — |
| 47 | Two replicating plus one broadcast — Cross Product k=2, not 3 | `Point.ByCoordinates` | x:0, y:0, z:0 | x=`[1,2]`, y=`[10,20]`, z=`0` | CrossProduct | `[[p(1,10,0),p(1,20,0)],[p(2,10,0),p(2,20,0)]]` | 2 | — |
| 48 | Mixed excess 1 / 0 / 2 — outermost-first alignment (Decision D1) | `Point.ByCoordinates` | x:0, y:0, z:0 | x=`[1,2]`, y=`5`, z=`[[7,8],[9,10]]` | Longest | `[[p(1,5,7),p(1,5,8)],[p(2,5,9),p(2,5,10)]]` | 2 | — |
| 49 | Three inputs, one of length 1 — Longest | `Point.ByCoordinates` | x:0, y:0, z:0 | x=`[1,2,3]`, y=`[0]`, z=`0` | Longest | `[p(1,0,0),p(2,0,0),p(3,0,0)]` | 1 | — |

### Group E — Cross Product specifics and replication guides

| # | Description | Node | Declared | Inputs | Mode | Expected output | Rank | Diagnostic |
|---|---|---|---|---|---|---|---|---|
| 50 | Default dimension order is port order; port 0 is the outer loop | `Add` | a:0, b:0 | a=`[1,2]`, b=`[10,20]` | CrossProduct | `[[11,21],[12,22]]` | 2 | — |
| 51 | `[ReplicationGuide]` reverses the nesting order: b outer, a inner | `Add` | a:0 guide 2, b:0 guide 1 | a=`[1,2]`, b=`[10,20]` | CrossProduct | `[[11,12],[21,22]]` | 2 | — |
| 52 | Duplicate guides on two replicating ports are refused | `Add` | a:0 guide 1, b:0 guide 1 | a=`[1,2]`, b=`[10,20]` | CrossProduct | `—` | — | `E:SPK1044` |
| 53 | **Cross Product compounds through recursion**: k=2 outer plus one inner level | `Add` | a:0, b:0 | a=`[[1,2],[3,4]]`, b=`[10,20]` | CrossProduct | `[[[11,12],[21,22]],[[13,14],[23,24]]]` | 3 | — |
| 54 | The headline geometry case: centres × radii is a grid, not a flat list | `Circle.ByCenterRadius` | center:0, radius:0 | center=`[A,B]`, radius=`[1,5]` | CrossProduct | `[[c(A,1),c(A,5)],[c(B,1),c(B,5)]]` | 2 | — |
| 55 | The same inputs under Longest — 2 circles, rank 1, not 4 | `Circle.ByCenterRadius` | center:0, radius:0 | center=`[A,B]`, radius=`[1,5]` | Longest | `[c(A,1),c(B,5)]` | 1 | — |
| 56 | Cross Product where one input has excess 0 — it is not a dimension | `Add` | a:0, b:0 | a=`[1,2]`, b=`10` | CrossProduct | `[11,12]` | 1 | — |

### Group F — ragged nesting

| # | Description | Node | Declared | Inputs | Mode | Expected output | Rank | Diagnostic |
|---|---|---|---|---|---|---|---|---|
| 57 | Ragged input, scalar broadcast; shape preserved exactly | `Add` | a:0, b:0 | a=`[[1,2],3]`, b=`10` | Longest | `[[11,12],13]` | 2 | — |
| 58 | Ragged on both inputs, branches align independently | `Add` | a:0, b:0 | a=`[[1,2],3]`, b=`[10,[20,30]]` | Longest | `[[11,12],[23,33]]` | 2 | — |
| 59 | Ragged inner lengths — Shortest applies per branch | `Add` | a:0, b:0 | a=`[[1,2],[3]]`, b=`[[10,20],[30,40]]` | Shortest | `[[11,22],[33]]` | 2 | — |
| 60 | Ragged inner lengths — Longest applies per branch | `Add` | a:0, b:0 | a=`[[1,2],[3]]`, b=`[[10,20],[30,40]]` | Longest | `[[11,22],[33,43]]` | 2 | — |
| 61 | Ragged into a rank-1 port: shallow branches promote, deep ones replicate | `Sum` | xs:1 | xs=`[1,[2,3]]` | Auto | `[1,5]` | 1 | — |
| 62 | Ragged under Cross Product; each cell recurses on its own shape | `Add` | a:0, b:0 | a=`[[1,2],3]`, b=`[10,20]` | CrossProduct | `[[[11,12],[21,22]],[13,23]]` | 3 (ragged: outer item 0 is rank 2, outer item 1 is rank 1) | — |

### Group G — `null` and per-element failure

| # | Description | Node | Declared | Inputs | Mode | Expected output | Rank | Diagnostic |
|---|---|---|---|---|---|---|---|---|
| 63 | `null` is a rank-0 element and passes through untouched | `Echo` | x:0 | x=`[1,null,3]` | Auto | `[1,null,3]` | 1 | — |
| 64 | `null` as the whole input — depth 0, so it is a node error, not per-element | `Add` | a:0, b:0 | a=`null`, b=`10` | Auto | `—` | — | `E:SPK1041` |
| 65 | **1 of 4 elements fails**; the other 3 survive, slot 2 is null | `Invert` | x:0 | x=`[1,2,0,4]` | Auto | `[1,0.5,null,0.25]` | 1 | `W:SPK1042` "1 of 4 elements failed; first at [2]" |
| 66 | A failing element inside a list; the cast failure is per-element | `Add` | a:0, b:0 | a=`[1,null,3]`, b=`10` | Longest | `[11,null,13]` | 1 | `W:SPK1042` "1 of 3 elements failed; first at [1]" |
| 67 | Failure inside nested structure reports the full `ElementPath` | `Invert` | x:0 | x=`[[1,0],[2]]` | Auto | `[[1,null],[0.5]]` | 2 | `W:SPK1042` "1 of 3 elements failed; first at [0][1]" |
| 68 | Every element fails — still a Warning, never an Error (Decision D6) | `Invert` | x:0 | x=`[0,0]` | Auto | `[null,null]` | 1 | `W:SPK1042` "2 of 2 elements failed" |
| 69 | A failure at depth 0 is an Error, not a Warning — nothing was isolated | `Invert` | x:0 | x=`0` | Auto | `—` | — | `E:SPK1046` |

### Group H — author attributes

| # | Description | Node | Declared | Inputs | Mode | Expected output | Rank | Diagnostic |
|---|---|---|---|---|---|---|---|---|
| 70 | `[NoReplication]` port broadcasts normally when given a scalar | `Scale` | x:0, factor:0 `[NoReplication]` | x=`[1,2,3]`, factor=`2` | Auto | `[2,4,6]` | 1 | — |
| 71 | `[NoReplication]` port given a list — refused, not laced | `Scale` | x:0, factor:0 `[NoReplication]` | x=`[1,2,3]`, factor=`[2,3]` | Auto | `—` | — | `E:SPK1043` |
| 72 | `[NoReplication]` does not contribute to `n` under Cross Product | `Scale` | x:0, factor:0 `[NoReplication]` | x=`[1,2]`, factor=`2` | CrossProduct | `[2,4]` | 1 | — |
| 73 | `[KeepStructure]` — the node sees the outer list, counts rows not items | `List.Count` | list:∞ `[KeepStructure]` | list=`[[1,2],[3,4],[5]]` | Auto | `3` | 0 | — |
| 74 | `[KeepStructure]` cannot be overridden by choosing an explicit mode | `List.Count` | list:∞ `[KeepStructure]` | list=`[[1,2],[3,4],[5]]` | Longest | `3` | 0 | — |
| 75 | `[KeepStructure]` under Cross Product — still not a dimension | `List.Count` | list:∞ `[KeepStructure]` | list=`[[1,2],[3,4],[5]]` | CrossProduct | `3` | 0 | — |
| 76 | `[KeepStructure]` never promotes: a scalar arrives as a scalar | `List.Count` | list:∞ `[KeepStructure]` | list=`5` | Auto | `1` | 0 | — (node-defined: `List.Count` treats a scalar as one item) |
| 77 | `[KeepStructure]` returns the supplied structure unchanged | `List.Reverse` | list:∞ `[KeepStructure]` | list=`[[1,2],[3,4]]` | Auto | `[[3,4],[1,2]]` | 2 | — |
| 78 | **The bug the attribute prevents** — same node without it, rank 2 in | `CountNoAttr` | list:1 | list=`[[1,2],[3,4],[5]]` | Auto | `[2,2,1]` | 1 | — |
| 79 | …and the same node rescued by Disabled instead of the attribute | `CountNoAttr` | list:1 | list=`[[1,2],[3,4],[5]]` | Disabled | `3` | 0 | — |

### Group I — multi-output nodes

| # | Description | Node | Declared | Inputs | Mode | Expected output | Rank | Diagnostic |
|---|---|---|---|---|---|---|---|---|
| 80 | Multi-output at depth 0 — both ports scalar | `Bounds` | xs:1 | xs=`[1,2,3]` | Auto | `min`=`1`, `max`=`3` | `min`:0, `max`:0 | — |
| 81 | **Multi-output transpose** — two lists of 2, never one list of pairs | `Bounds` | xs:1 | xs=`[[1,2,3],[10,20]]` | Auto | `min`=`[1,10]`, `max`=`[3,20]` | `min`:1, `max`:1 | — |
| 82 | Multi-output, two replicating inputs — Longest, lockstep | `Split` | a:0, b:0 | a=`[1,2]`, b=`[10,20]` | Longest | `sum`=`[11,22]`, `diff`=`[-9,-18]` | `sum`:1, `diff`:1 | — |
| 83 | Multi-output under Cross Product — **every port is rank +k** | `Split` | a:0, b:0 | a=`[1,2]`, b=`[10,20]` | CrossProduct | `sum`=`[[11,21],[12,22]]`, `diff`=`[[-9,-19],[-8,-18]]` | `sum`:2, `diff`:2 | — |
| 84 | Multi-output nested twice — both ports keep the same shape | `Bounds` | xs:1 | xs=`[[[1,2]],[[10,20,30]]]` | Auto | `min`=`[[1],[10]]`, `max`=`[[2],[30]]` | `min`:2, `max`:2 | — |
| 85 | Multi-output with a per-element failure — the null lands on **both** ports | `Bounds` | xs:1 | xs=`[[1,2,3],[]]` | Auto | `min`=`[1,null]`, `max`=`[3,null]` | `min`:1, `max`:1 | `W:SPK1042` "1 of 2 elements failed; first at [1]" |

> **Decision D10 — a failed leaf of a multi-output node nulls every output port.**
> Case 85 settles a question the model does not otherwise reach. When one leaf call of a
> multi-output node throws, all of that node's output ports receive `null` in that slot, so
> the ports stay the same length and stay index-aligned with each other and with the input.
> The rejected alternative — omit the slot from ports that have nothing to say — produces
> ports of different lengths from one node, which silently breaks every downstream `Longest`
> zip that pairs them. A visible `null` is far better than an invisible off-by-one.

### Group J — `Auto` resolution

`Auto` is not a replication algorithm; it resolves to the node definition's `DefaultLacing`
(§2.9). These cases assert that the resolution happens, that it reaches the *right* mode,
and — cases 86 and 87 — that it is observable. That pair is the one that would have caught
an `Auto` defined as a synonym for Longest: same node, same inputs, different mode, and the
**ranks differ**.

| # | Description | Node | Declared | Inputs | Mode | Expected output | Rank | Diagnostic |
|---|---|---|---|---|---|---|---|---|
| 86 | **`Auto` on a node whose definition declares `CrossProduct`** — resolves to Cross Product | `Grid.ByXY` (default `CrossProduct`) | x:0, y:0 | x=`[0,1]`, y=`[0,1]` | Auto | `[[p(0,0,0),p(0,1,0)],[p(1,0,0),p(1,1,0)]]` | 2 | — |
| 87 | **The same node with an explicit `Longest`** — the instance overrides the default | `Grid.ByXY` (default `CrossProduct`) | x:0, y:0 | x=`[0,1]`, y=`[0,1]` | Longest | `[p(0,0,0),p(1,1,0)]` | 1 | — |
| 88 | `Auto` on a node whose definition declares `Disabled` — replication is off, so Flatten sees the whole list | `List.Flatten` (default `Disabled`) | list:1 | list=`[[1,2],[3,4]]` | Auto | `[1,2,3,4]` | 1 | — |
| 89 | The same node forced to `Longest` — it flattens each row instead, and nothing is flattened | `List.Flatten` (default `Disabled`) | list:1 | list=`[[1,2],[3,4]]` | Longest | `[[1,2],[3,4]]` | 2 | — |
| 90 | `Auto` on a `Longest`-defaulting node with a `CrossProduct`-defaulting node's inputs — defaults do not travel along wires | `Add` (default `Longest`) | a:0, b:0 | a=`[0,1]`, b=`[0,1]` | Auto | `[0,2]` | 1 | — |

Cases 86 and 90 together are the user-visible consequence: **two node instances both
reading "Auto" in the lacing menu, given the same inputs, produce different ranks.** That is
correct, and it is the entry the [troubleshooting](#5-troubleshooting) section covers.

---

## 7. Diagnostic codes

| Code | Severity | Meaning |
|---|---|---|
| `SPK1040` | Error | A value could not be promoted to the port's declared rank and type. |
| `SPK1041` | Error | A value could not be marshalled into the port's declared type — usually a rank that replication was not permitted to reduce. |
| `SPK1042` | Warning | Some elements failed during replication. Names the failed count, the total, and the index path and message of the first failure. |
| `SPK1043` | Error | A list was supplied to a `[NoReplication]` port. |
| `SPK1044` | Error | Two replicating ports declared the same `[ReplicationGuide]` value. |
| `SPK1045` | Warning | Under Longest, some replicating inputs were empty and others were not, so the result is empty. |
| `SPK1046` | Error | The node threw during evaluation at replication depth 0. There was no per-element isolation to fall back on, so there is no output. |

---

## 8. These are Spark's semantics

The rules on this page are Spark's own. They are not a reimplementation of any other tool's
lacing, they carry no compatibility obligation to one, and where a familiar name appears —
Shortest, Longest, Cross Product — it is because the name is the clearest available English,
not because the behaviour is guaranteed to match. Spark reads no other tool's file format
and makes no equivalence claim in either direction.

That freedom is what allows this document to be strict. Rank is explicit, empty propagates,
Cross Product nests by `k`, and every judgement call is written down and numbered rather
than inherited. Where an existing habit and a defensible rule disagreed, the rule won.

**Changing anything on this page changes the meaning of graphs that already exist.** Any
future revision to these semantics is gated by `graph.formatVersion`, so an old graph keeps
the behaviour it was built against and a new graph gets the new rule. There is no silent
correction path, by design: a lacing fix that quietly reshapes someone's facade panel layout
is not a fix.

---

## See also

- `concepts.lists` — building, indexing and reshaping lists.
- `concepts.evaluation` — ordering, caching, dirty propagation and run modes.
- ADR — *rank-based replication*, recording the alternatives rejected in §2.16. To be written
  alongside the engine; every decision it must capture is already numbered here.
