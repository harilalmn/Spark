---
id: concepts.lists
title: Building and reshaping lists
nodes: [List.Count, List.GetItemAtIndex, List.FirstItem, List.LastItem, List.Reverse, List.Join, List.TakeItems, List.UniqueItems, List.OfRepeatedItem, List.Flatten, Number.Range]
related: [concepts.lacing, concepts.evaluation]
since: "0.1"
---

**Status:** Current. Describes the list nodes in the running application.
**Owner:** `graph-engine`
**Last updated:** 2026-08-31

> **Scope.** Making lists, and changing their shape. What happens when a list arrives at a node
> that expected a single value is a different subject and has its own topic:
> [Lists, ranks and lacing](lacing.md). Read that one second; it is the one that explains why a
> graph produced 30 things instead of 3.

---

## A list is a value

There is no separate list mode and no list wire. A node that produces many things produces a list,
and a list travels down an ordinary wire like anything else. `Number.Range` is the usual way one
starts:

| `start` | `end` | `step` | Result |
|---|---|---|---|
| 0 | 4 | 1 | `0, 1, 2, 3, 4` |
| 0 | 1 | 0.25 | `0, 0.25, 0.5, 0.75, 1` |
| 5 | 1 | -2 | `5, 3, 1` |
| 0 | 10 | 0 | *error* — a step of zero never reaches the end |

The end is included when the step lands on it exactly. `0 to 1 step 0.3` gives `0, 0.3, 0.6, 0.9`
and stops, because 1.2 is past the end.

## Lists can hold lists

A list of lists is ordinary and is what Cross Product lacing produces. Its **rank** is how deeply
nested it is: a number has rank 0, a list of numbers rank 1, a list of lists of numbers rank 2.
Rank is the single idea the whole of [lacing](lacing.md) is built on, which is why it is named
here rather than left implicit.

## The nodes, and what each is for

| Node | Takes | Gives |
|---|---|---|
| `List.Count` | any list | how many items are in it |
| `List.GetItemAtIndex` | a list, an index | the item at that index, counting from zero |
| `List.FirstItem` | a list | its first item |
| `List.LastItem` | a list | its last item |
| `List.Reverse` | a list | the same items, opposite order |
| `List.Join` | two lists | the two end to end |
| `List.TakeItems` | a list, a count | the first *n* items, or the **last** *n* when the count is negative |
| `List.UniqueItems` | a list | duplicates removed, keeping the first of each |
| `List.OfRepeatedItem` | a value, a count | that value, *n* times |
| `List.Flatten` | a nested list | one flat list of everything in it |

## A worked example

Ten points along a line, keeping every third one:

1. **Place `Number.Range`.** Set `start` to `0`, `end` to `9`, `step` to `1`. Its output is ten
   numbers. Put a Watch node on it and you will see them.

2. **Place `Point.ByCoordinates`** and wire the range into `x`. Leave `y` and `z` at zero. Ten
   points appear in the viewport, in a row along the world x axis. *That is lacing doing its work
   — one node, ten results — and it is [lacing.md](lacing.md)'s subject, not this topic's.*

3. **Place `List.TakeItems`** and wire the points into it. Set `count` to `4`. The viewport drops
   to the first four points.

4. **Change `count` to `-4`.** The viewport shows the **last** four instead. A negative count takes
   from the end, which is the one thing about this node worth remembering.

5. **Place `List.Reverse`** after it and watch nothing move. The points are the same points in the
   opposite order; order matters to what comes next, not to where they are.

## Flatten is the one to be careful with

`List.Flatten` collapses **every** level of nesting, not one. Given a list of three lists of four
points it gives twelve points, and the grouping that told you which four belonged together is
gone for good — there is no node that puts it back.

That is usually what you want after a Cross Product, and it is almost never what you want before
one. If you are reaching for Flatten to make an error go away, the error is more likely telling you
about a rank mismatch that [lacing](lacing.md) explains.

## Indexing counts from zero, and from the end

`List.GetItemAtIndex` with index `0` gives the first item. **A negative index counts back from the
end**, so `-1` is the last item and `-2` the one before it — which exists because the alternative is
`List.GetItemAtIndex(list, List.Count(list) - 1)` every time somebody wants the last item, three
nodes for something that should be one.

| Index into `10, 20, 30` | Result |
|---|---|
| `0` | `10` |
| `2` | `30` |
| `-1` | `30` |
| `-3` | `10` |
| `3` | *error* — the list has 3 items |

An index past either end is an **error**, not a silently empty result. An empty result downstream
looks exactly like a list that was always empty, and you would go looking in the wrong place. Use
`List.Count` when you need to know how far you can go.

`List.FirstItem` and `List.LastItem` are the same idea without the arithmetic, and both error on an
empty list for the same reason.
