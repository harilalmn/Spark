# ADR-0024 — A graph's packages live beside the graph

**Status:** Accepted, 2026-09-09
**Tasks:** `E7-T16`, `E7-T17`, `E7-T18`, `E7-T19`, `E7-T20`

## Context

`E7` gave Spark a package layer: NuGet is the registry, a package is a NuGet package carrying the
`spark` tag and a `tools/spark.json` manifest, and installs land in one global store under
`%LOCALAPPDATA%\Spark\packages`. `E7-T9` added a second, separate path for a plain library — pick a
`.dll` by absolute path and its types become visible to code blocks.

The client asked the question those two do not answer between them: **how do I use a NuGet
package?** Searching the Packages window for `nice3point` returns *Nothing found on nuget.org.
Spark packages carry the tag 'spark'.* — which is correct and useless. That package is not a Spark
node package and never will be; it is an ordinary .NET library somebody wants to write code
against.

The honest answer today is: download the `.nupkg`, rename it to `.zip`, extract the assembly from
`lib/`, and add it through Local assemblies — repeating for every dependency, by hand.

The client proposed the alternative and drew the layout:

```
Sample\
  project1.spark
  project1.packages\
    custom_nuget_package1\custom_nuget_package1.dll
    custom_nuget_package2\custom_nuget_package2.dll
    custom_user_package.dll
  project2.spark
  project2.packages\
    custom_nuget_package1\custom_nuget_package1.dll
    custom_user_package.dll
```

## Decision

**A graph's packages live in `<name>.packages`, beside `<name>.spark`, and are loaded when the file
opens.** The folder holds one directory per NuGet package and loose `.dll` files for anything added
by hand. Both shapes are accepted: a NuGet extraction with `lib/<tfm>/`, and a bare assembly at the
top level.

**Graph-local resolves before the global store**, so a graph can pin a version without disturbing
anything else on the machine.

**Nothing loads without consent, recorded per content hash.**

## Why, and what it is better than

**The alternative was the global store alone** — install once, available everywhere, which is what
`E7` already built and what most applications do.

Three things decided against it:

1. **Portability.** A graph and the libraries it needs travel together. Zip the folder, send it, it
   opens. Under a global store a `.spark` is a graph plus verbal instructions about what to install
   first, and the instructions are not in the file.
2. **Two graphs may disagree.** One needs `MathNet.Numerics` 5, another needs 4. A global store
   makes that a conflict to be resolved; a folder each makes it a non-event — and `E7-T3` already
   built the mechanism, one collectible load context per package *version*, so the loader has been
   ready for this since before there was a reason.
3. **It costs almost nothing to build.** `PackageStore` already takes its root as a constructor
   argument; `Default()` is one factory among possible others. A graph-local store is
   `new PackageStore(folder)`, not a second subsystem.

**What the global store keeps.** Node packages a user wants everywhere, installed once. This ADR
adds a location; it does not remove one.

## The part that is a hazard, stated plainly

**A folder of DLLs beside a downloaded `.spark`, loaded on open, is remote code execution.**
Somebody sends `facade.spark` and `facade.packages\helper.dll`; the graph opens; the assembly
loads. Loading is not passive — module initialisers and static constructors run, and Spark's node
importer reflects over types, which triggers them.

Spark has already taken the opposite position on a *weaker* version of this risk. `E6-T16` refuses
to auto-run a graph containing code blocks, shows a banner naming how many, and records trust by
content hash. **A code block is text the user can read before it runs. A DLL is not.** A folder that
loaded binaries silently would walk around a door the product deliberately locked.

So the trust gate ships **in the same commit** as the loader, not in a follow-up. `E7-T16` is one
row for that reason.

**Consent is per content hash**, at the client's instruction: a rebuilt DLL asks again, an unchanged
one never does. `PackageTrustStore` already records per package and version; this is the same idea
keyed on bytes, which is the only key that means anything for a file a user dropped in themselves.

## Four questions the client settled

Recorded here because each removes a class of follow-up question, and because the reasoning is
theirs.

| Question | Decision | What it avoids |
|---|---|---|
| A graph that has never been saved has no filename — where do its packages go? | **The Packages window does not open.** It says to save the file first. | Inventing a location, moving it on first save, and explaining both |
| Save As, and rename | **Save As copies the folder.** A rename outside Spark is not tracked | Guessing which nearby folder was meant by a renamed file |
| The folder is missing on open | **Fail loudly, naming what is absent** | A graph that opens looking fine and fails later with a message about a *type* |
| How often to ask for trust | **Once per new content hash** | Either a prompt on every open, or a blanket yes that outlives the bytes it was given for |

## Consequences

**The `.spark` format gains a section, and the format version goes to 5.** *Fail loudly with a named
list* is impossible without a record of what was expected: a folder with less in it is not missing
anything. This is the consequence of the third decision above and it was not obvious when the
decision was made — it is written here because the next reader will wonder why a convention-based
folder needed anything in the file at all.

**The references go first in the file, and that is load order rather than tidiness.** The format is
JSON written in source order with `formatVersion` first, and `GraphDocument.Restore` builds node
definitions as it reads nodes — building a code block's definition *compiles* it. The assemblies
must therefore be loaded before the first node is touched, so the reader has to see the list before
it sees the graph. It also means a person opening a `.spark` in a text editor reads what it needs
before what it does.

**It is a record, not a lock.** The folder is still discovered by convention, so a user may drop in
a DLL nobody wrote down and it will load. The record exists to make an *absence* nameable, not to
refuse an addition.

**Two questions left open deliberately, because guessing either would be worse than asking.**
*How relative is relative* — confined to the sibling `<name>.packages` folder, which keeps Save As
able to know what to copy and stops a recorded path escaping into a system directory; or free,
which permits one shared folder across several graphs and makes Save As unable to carry
dependencies. The recommendation is confined now, shared later as its own feature. And *whether a
reference carries a hash* — it would catch **changed** as well as **missing**, but a rebuilt DLL is
ordinary during development and `E7-T9` already hot-reloads one; the recommendation is name and path
only, with trust keyed on the hash separately at load time.

**`E7-T7` has to be re-proved.** It promises a graph naming a package you do not have re-saves byte
for byte. That promise now covers a new section, and re-proving it is part of `E7-T17` rather than
an assumption inherited from it.

**Save As stops being instant** for a graph carrying large packages, because it copies them. Copy
rather than move, because Save As leaves the original file where it was and must leave it working.

**Two search paths, not one changed path.** `NuGetPackageClient` searches `tags:spark` deliberately;
that filter is right for finding node packages and wrong for referencing a library. `E7-T20` adds a
second search rather than widening the first, so neither answer degrades the other.

## What building the gate settled (2026-09-11, `E7-T16`)

Four things the decision above left to the implementation, each of which could have gone the other
way.

- **One button, and it remembers.** `E6-T16`'s banner offers *Run once* beside *Always trust*. The
  package banner offers only **Trust and load**, which records the hashes — the client's rule is
  that an unchanged assembly never asks twice, and a once-only answer would ask on every open.
- **Referenced, not loaded, and settled before the document is built.** The gate runs ahead of
  `CanvasDocument.Open` on both doors, File ▸ Open and `--open`. Agreeing hands metadata references
  to the compiler; code in an agreed assembly runs only when a code block calls it, and code blocks
  run only under `E6-T16`'s rule. The two gates are independent, and a downloaded graph can show
  both banners at once.
- **Releasing goes by folder.** Opening another graph takes every reference under the previous
  folder out of the catalogue however it got there, because *Add as a library…* references what it
  installs without going through the gate. That is what lets two graphs disagree about a version
  in one session.
- **The folder holds libraries, not node packages.** A Spark package with a manifest inside
  `<name>.packages` is referenced like any library and contributes no nodes. Importing nodes means
  reflecting over types, which runs static constructors, and that is a larger decision than this
  one — the global store still installs node packages.

## What building the record settled (2026-09-11, `E7-T17`)

The two questions this ADR left open were taken as it recommended — paths **confined** to the
sibling folder, and **no hash** — and four more came up in the building.

- ***Fail loudly* means the graph opens with a named list, not that it is refused.** `E7-T6`'s
  promise is that nobody's graph is damaged by opening it on a machine without a package, and a
  refused graph is one nobody can repair. The absent packages are named in the banner that already
  names absent node packages, before anything is built and before any compile error can appear.
- **A recorded path is looked up under the file's current name.** The file writes
  `tower.packages/Helpers.dll`, but the lookup always goes to the folder named after the file as it
  is now. A rename in Explorer therefore names both folders — the one Spark looked in and the one
  the file names — which is this ADR's *a rename outside Spark is not tracked*, made actionable.
- **Save never forgets a missing package, and records what is there.** The list written is what
  the file already named plus what is in the folder now. Dropping the missing ones would turn
  opening a graph on the wrong machine into silently editing it, and would break `E7-T7`'s
  byte-for-byte re-save. Adding what is there makes a hand-dropped DLL nameable on the next machine
  without anybody writing it down.
- **Save asks where before it writes.** The list is relative to the file's own name, so the text
  of a `.spark` now depends on what it is called, and Save As cannot produce it until it knows. A
  graph the format cannot write is reported after the dialog rather than before it — one dialog
  the user did not need, and nothing lost.
