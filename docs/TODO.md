# Spark — TODO

What to do next, in priority order. Full context in [EPICS.md](EPICS.md), full inventory in
[TASKS.md](TASKS.md), the reasoning in [PRD.md](PRD.md).

**Last updated:** 2026-09-01 (E6-T22 and E6-T23 closed: signature help, and a list that opens after `=` and `new`)

**M0 through M6 have all landed. M7 has started.** The application opens, a graph evaluates,
and geometry appears in the viewport — curves, surfaces, meshes, and **solids that are
combined exactly**. `--graph solids` fuses a box to a cylinder, drills a hole through both, hollows
a second box and rounds every edge of a third. **F1 opens help for the selected node.** Selecting a
node outlines its geometry in the viewport. A graph naming a package you do not have still opens,
keeps everything, and re-saves byte for byte. A user can define a node by drawing a graph.

`dotnet build --no-incremental -warnaserror`, the per-project test executables (**1,893 tests over
nine projects**) and `dotnet format` are all clean on Windows as of 2026-08-31, with the native
shim built and **nothing skipped**. **CI has not run since the provider landed**, and its Linux leg
is now a question rather than a habit — see `Q15(c)`.

**The benchmarks stopped being a report and became a guard on 2026-08-29.** A nightly workflow
runs the three suites on both operating systems and the application's own canvas benchmark on
Windows, and checks every number against budgets committed in `bench/budgets.jsonc` — allocation
tightly, ratios sharply, wall-clock loosely and for stated reasons
([ADR-0023](adr/0023-performance-budgets-not-a-benchmark-time-series.md), [N29](NOTES.md)). It is
green locally end to end and **has never run on a hosted runner**, which is the difference between
proven to detect and proven to run.

Three distinctions still do the work in what follows:

- **What is built.** The value layer, the curve layer including `NurbsCurve`, the surface layer
  including `NurbsSurface`, meshes and adaptive tessellation, BRep topology, the graph engine and
  replicator, the reflection importer with its two-way diff, Roslyn code blocks on the canvas, the
  Avalonia shell, the immediate-mode canvas, the GL viewport, 108 nodes in `Spark.Nodes.Core`,
  OBJ/STL/PLY/glTF on the way out, a `.spark` file a graph survives a round trip through byte for
  byte, and a 64-step undo stack over that same file format. **And the OpenCascade provider**:
  `native/spark_occt` and `Spark.Geometry.Occt`, with union, difference, intersection, extrude,
  revolve, loft, fillet, chamfer, shell, sew, heal and tessellate behind `IBrepKernel`.
  **1,707 tests over eight projects** as of 2026-08-31.
- **What is not.** No STEP or IGES, though the provider can do both. No split, trim, thicken,
  draft or offset. No packages. No mesh booleans. Trimmed faces come *back* from the provider but
  cannot be authored. The software renderer and the CI visual-regression check are deliberately
  deferred past M6.
- **M2 finished on 2026-08-30.** Real docking (`E8-T2`), group, note and align (`E8-T6`), watch
  nodes and preview bubbles (`E8-T10`) and `spark run` (`E12-T5`) all landed that day, which was
  the whole of what the milestone still owed. The shell is a `DockControl` whose presets rearrange
  it, the canvas annotates and aligns, a node's rank is visible where the graph is, and a graph
  evaluates from the command line with no window and prints what its watches saw.
- **Gates are not review, and this project now has its own proof three times over.** The
  kernel's first slice passed all three gates and was rejected on review ([NOTES N18](NOTES.md)).
  The curve layer's mutation sweep then found a test that could not fail and a branch that could
  not be reached ([N19](NOTES.md), [N20](NOTES.md)). The undo sweep found the same shape a third
  time: a test asserting that clicking a node is not an edit passed under a mutation that recorded
  *every* drag, because a click raises no pointer-move event and never reached the guard the test
  was written for. All of it in code that was green.

**The largest decision in the project is unchanged, and it is now built.** The client chose to take an
existing solid-modelling kernel rather than write one: **OpenCascade, reached through a C-ABI
shim we own** ([ADR-0020](adr/0020-occt-via-c-abi-shim.md),
[ADR-0021](adr/0021-brep-kernel-residency.md), PRD **D2** and **D15**). It retires **R1** and
**R12**, adds **R15 … R22**, adds a two-week spike **M1.6**, adds epic
[E13](EPICS.md#e13--occt-provider) of roughly 24 weeks, and costs **+7 to +11 weeks against the
plan as written while saving years against what was actually asked for**. `50a9935` measured
what comparable projects actually ship: a full win-x64 OCCT build is 52.1 MiB across 47
toolkits plus 9.9 MiB of optional third-party libraries, so **R15's 40–160 MB bracket should be
read as 55–70 MB** — but that is a survey, not a build.

**The spike was taken on 2026-08-31, and ADR-0020 stands.** Nine criteria, `M1.6-C1` …
`M1.6-C9` in [TASKS.md](TASKS.md#m16--the-passfail-criteria-written-before-the-spike), written
ahead of the work rather than beside it, and each carrying what a failure would mean. **Three are
answered.** `C2` — the only one that could have reopened ADR-0020 — **passed**: a boolean runs end
to end and measures 42.0 against arithmetic's 42. `C1` passed on Windows, whose
*two-operating-system* half is void under **D17**. `C3` is **measured at 52.0 MB staged and 28.4 MB
linked**, replacing R15's unmeasured 40–160 MB bracket. **`C4` through `C9` are not taken**, and
`C6` — whether `ShapeFix` can be constrained to a policy we choose — matters more than it did,
because `ShapeFix` is now on the *import* path rather than only behind `Heal`
([N50](NOTES.md), [N51](NOTES.md)).

**One row on this page came from using the product rather than from planning it**, and it is worth
saying so where the plan lives: `E8-T18`, port type labels. Nothing in the PRD asked for them and
nothing in EPICS was short without them. Somebody opened the application, put down a
`Circle.ByCentreRadius`, and could not tell what `centre` wanted. The requirement was written
afterwards (**FR-82**), which is the right order for a defect nobody predicted and the wrong order
for anything else.

---

## Where the run stands

**M5 closed on 2026-08-31.** The software renderer, headless thumbnails and the CI visual
regression — `E9-T5`, `E9-T11`, `E9-T12` — were the three things it still owed after
being deferred past M6. Viewport output is now comparable between machines and therefore testable,
and a fixed scene is diffed against a committed PNG on every test run.

**The order to 1.0 is settled by [D19](PRD.md#13-decision-log): finish the product, then write the
Help.** The end-user Help is **reordered, not descoped** — what that costs and what it must
not break is [below](#after-10--the-help-pass), and the standing instruction in
[AGENTS.md](../AGENTS.md#the-standing-instruction) is amended to match rather than left to be
broken every step.

**D19 said the Help harness had to exist before any bulk writing, and it now does** — which
is why several `E10` and `E11` rows are closed ahead of the pass they belong to. Every C# fence in
the help compiles against the real API (`E11-T2`); every example graph is opened, evaluated and
re-saved on every test run (`E11-T3`); every node resolves to a topic and every node named in a
topic still exists (`E11-T4`, `E11-T5`); every `SPK####` code has a page (`E11-T6`). **The node and
diagnostic reference pages are generated at runtime from the live library**, so they cannot drift
from the code (`E10-T5`, `E10-T11`), and the in-product renderer is built (`E10-T13`).

## Now — what is next, in order

- [ ] **`E7-T12` — collapse selection to custom node.** The engine half is built and tested:
      `.sparkcustom` is the graph format plus an interface block, ports come from Input/Output
      nodes placed in the definition graph, and recursion is refused at build time with the
      containment path named (`E7-T11`, `E7-T13`, `E7-T15`). **What is missing is the gesture** "
      + D + " take a selection, cut it out, and infer the interface from the wires that crossed the
      boundary. `E7-T13`'s save-side refusal belongs with it, because collapse is what can build a
      recursive definition by accident.
- [ ] **The rest of M7, which is network-facing.** `E7-T1` (the package convention), `E7-T2` (the
      NuGet client), `E7-T8` (trust and install disclosure), `E7-T9` (local DLLs with hot reload)
      and `E7-T10` (the package manager UI). **The load layer underneath them is done and proven**:
      one collectible context per package *version*, contract assemblies always shared, side-by-side
      versions demonstrated (`E7-T3`, `E7-T4`).
- [ ] **`E7-T5`'s purge half.** The unload mechanism is built and proven by weak reference; the
      registries it has to empty do not exist until `E7-T2`.
- [ ] **`E9-T7` and `E9-T8`** — parallel streamed tessellation, and picking through the
      kernel's BVH ray caster. Both are M2-era viewport work rather than anything M5 owed.
- [x] ~~**`E6-T20` — a rendering test for the surfaces a person touches.**~~ **Closed 2026-09-02 in
      the half that is reachable, and the other half is not ours to fix.** A data-bound `TextBlock`
      with `TextWrapping="Wrap"` inside a `Grid` hangs Avalonia's headless `Window.Show()` —
      reproduced in nine lines outside `InspectorPane` entirely ([N90](NOTES.md)). It hangs
      *before* any frame, so neither capture nor a layout assertion is available for that pane.
      The real application lays it out correctly and nothing was changed for it.
      `CanvasWidgetGestureTests` covers what *is* reachable: `GraphCanvas` wraps no text, so it
      shows and hit-tests normally, and six tests press actual buttons on the slider and the value
      field. **The properties pane, and with it the type dropdown, stays verified by eye** — which
      is now a stated limitation with a named cause rather than an unexplained gap.
- [ ] **M8, and 1.0.** See [EPICS.md](EPICS.md) `E12`. Note how much of it needs a person rather
      than a commit — the list below is most of the milestone.

**Waiting on a person, and no amount of further work substitutes.** This is the honest shape of
what remains: **three of these cannot be closed by writing code at all**, and two of them gate the
release rather than a feature.

- [ ] **`E13-T12`'s acceptance.** STEP output is checked by a round trip and by reading the file's
      own text — it names `CYLINDRICAL_SURFACE` and `ADVANCED_FACE` and never names `POLY_LOOP`.
      The row asks for **a public corpus and a third-party viewer, never our own reader**, because
      OpenCascade wrote both ends of that round trip. **This is the single largest unclosed thing
      in M6** and it is an errand, not a commit.
- [ ] **`Q13`'s six counsel questions**, the first being whether `spark_occt` is a *work that uses
      the Library* under the Open CASCADE exception or a derivative work under LGPL §5. *Nothing
      in this repository is legal advice.*
- [ ] **`E13-T17`'s installer, code signing and antivirus submissions.** The payload is staged and
      measured — 224.4 MB, of which OpenCascade is 52.0 MB — and a script cannot invent an identity
      to sign with.
- [x] ~~**`E12-T18`'s About box.**~~ **Done 2026-08-31, and it was never a person's job.** It sat under *waiting on a person* because the dialog did not exist — which is code, not an errand. `ProductNotice` in `Spark.Api` now holds one text that both `spark --version` and the About dialog print, because two copies of a licence notice is one copy that stops matching the build. Seven tests assert the obligation itself: with a kernel loaded the notice **must** name Open CASCADE, LGPL-2.1, dynamic linking and replaceability; without one it must **not** claim to link something absent.
- [ ] **Opening an exported OBJ or STEP in a third-party viewer**, which is also M1's stated
      acceptance and has never been done.
- [ ] **Watching the first nightly benchmark run.** It is green locally end to end and has never
      run on a hosted runner.

**And one that was on this list and should not have been.** `E12-T4`, proving `Spark.Host` inside a
real Revit or AutoCAD add-in, does need a licence and a person — but it proves a **second**
claim, that the engine can be embedded, and **Spark ships standalone without it**. `Spark.Desktop`
references `Spark.UI` and the provider and nothing else; the only mentions of either CAD product in
the source tree are doc comments on `IEvaluationScheduler`. [D20](PRD.md#13-decision-log) moves it
and `E12-T2` past 1.0. Listing it beside the signing identity implied Spark could not ship without a
CAD licence, which was wrong.

---

## After 1.0 — the Help pass

**Deferred by [D19](PRD.md#13-decision-log) on 2026-08-31, at the client's direction.** Not
descoped. This section exists so that *later* does not quietly become *never*, which is the only
failure mode a sequencing decision has.

**What exists today, updated 2026-09-01 when the pass began:** **eleven** concept topics under
`docs/help/concepts/` (4,000+ lines), **115 node pages and 18 diagnostic pages generated from the
live library**, an in-product renderer with context-sensitive F1 and search, worked example graphs
in `docs/examples/`, and XML doc comments on `Spark.Geometry`'s 387 public members. **Much of what
this section said 1.0 would ship without has since been built** — `E10-T5`, `E10-T7`,
`E10-T11`, `E10-T13` and the whole `E11` harness are `Done`.

**The two `Specification` topics were the debt D19 predicted**, and both said something false: they
claimed to predate an engine and a UI that had existed for months. Reconciled 2026-09-01 by reading
each against its code rather than by editing a line — see [N84](NOTES.md).

**What 1.0 therefore ships without, stated plainly so nobody is surprised at release:** a
reference for **108 nodes** (one topic names ten of them; the rest name none), the generated API
reference, an in-product help renderer — **F1 does nothing** — and a topic for any of the **18**
`SPK####` diagnostic codes, every one of which already carries a `HelpTopicId` seam pointing at a
document that does not exist.

**The order within the pass is not free to choose, and this is the part D19 cannot reorder.**
Deferring guarantees the Help is written **in bulk**, and a bulk write with nothing checking it is
`DocGenerator` again. So the harness comes first:

- [ ] **`E11-T2`** — compile every ` ```csharp ` fence and every XML `<example>`, with the exact
      references a real code-block node gets. Two samples in `geometry-basics.md` were already
      caught wrong by hand, and both read as perfectly plausible.
- [ ] **`E11-T3`** — execute every example graph headlessly, asserting no node errors.
- [ ] **`E11-T4`** — forward coverage: a node with no help topic fails the build.
- [ ] **`E11-T5`** — reverse coverage: a `nodes:` entry naming a node that no longer exists fails
      the build. This is what catches renames.
- [ ] **`E11-T6`** — every `SPK####` code has a topic.

Only then the writing:

- [x] **`E10-T6`** — the front-matter schema the topics already followed informally, now
      written down and enforced by `HelpTopicSchemaTests`. **`E10-T3`**, the help index, is what
      remains of that pair.
- [x] **`E10-T4`** — the topic authoring guide. **Done 2026-09-01**: [docs/HELP-AUTHORING.md](HELP-AUTHORING.md).
- [ ] **`E10-T9`, `E10-T10`** — XML doc comments across `Spark.Api` and `Spark.Nodes.Core`. These
      become runtime node tooltips as well as documentation, so they are worth more than their row
      count suggests.
- [ ] **`E10-T5`** — the generated API reference. Nobody writes it; nobody can forget it.
      **`DocGenerator.cs` is explicitly not ported.**
- [ ] **`E10-T13`** — the in-product Markdown help renderer, in `Spark.Api` and free of UI
      dependencies so the harness can exercise it anywhere. This is what makes F1 do something.
- [ ] **`E10-T7`** — more worked example graphs, openable from the help panel.
- [ ] **`E10-T11`** — a topic per `SPK####` code.
- [ ] **`E10-T12`** — per-PR changelog fragments.
- [ ] **`E10-T14`** — the website. [PRD Q8](PRD.md#14-open-questions) is still unanswered.

---

## Earlier — the M1.6 spike, and the last M1.5 one

- [x] **Walk TASKS.md against E3, E4, E5, E8 and E9** — done, and it moved 41 rows. Ten of them
      came back **`In progress` rather than `Done`**, which is the useful output: the cache
      evicts by entry count rather than by bytes, no node can declare itself impure, the
      host-thread scheduler is missing, cancellation does not reach inside a kernel operation,
      the shell has no real docking, the library panel filters without ranking, and three rows
      wait on the empty `bench/`. Two more are `Open` **with a stated reason** rather than by
      omission, because the importer's two-way diff will not let a public member go unaccounted
      for. Details on the rows and in [EPICS.md](EPICS.md).
- [x] **Save and load a `.spark` file** — `E3-T17`, `E3-T18`, done. Open and Save are on the
      toolbar, `--open PATH` opens one at startup, and `docs/examples/curves.spark` is committed
      as a golden file that the suite re-derives from the seeded demo. Read-then-write is
      byte-identical, and so is the longer path the application takes.
- [x] **Undo and redo** — `E8-T9`, done. A 64-step stack of whole-document `.spark` snapshots
      on Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z, and on the toolbar with the step named in the tooltip
      ([ADR-0022](adr/0022-undo-by-document-snapshot.md)). The choice of a snapshot over an
      inverse-command stack was made on **coverage**: a node's position never enters the engine
      graph, so a command stack over the engine's own mutations would have missed moves and would
      go on missing every future canvas-side edit. **It also closes a claim that had been repeated
      since M0 without a test behind it** — the run after an undo now recomputes zero nodes and
      serves every one from the provenance cache, measured rather than claimed (`E3-T8`).
- [x] **Create `bench/Spark.Benchmarks`** — `E1-T13`, done. Three suites: marshalling (`E4-T3`'s
      standing guard), evaluation cold against warm, and the canvas spatial index at 2 000 nodes.
      Every one was run before the row was ticked, and **two of the three were measuring the wrong
      thing** until the numbers gave them away ([N26](NOTES.md)).
- [x] **Add the no-native-binaries CI check** — `E1-T20`, done, and **inside the window**.
      `scripts/check-no-native-binaries.sh` runs in the CI build job on both operating systems and
      was proven to fire before being trusted: pointed at `Spark.Desktop` it fails on the Skia and
      HarfBuzz natives Avalonia brings.
- [x] **Run the benchmarks on a schedule** — `E1-T21` and `E4-T3`, done; `E8-T15` short only its
      first run. `.github/workflows/nightly.yml` runs the suites on both operating systems, drives
      `--canvas-benchmark` on Windows, and checks every number against budgets committed in
      `bench/budgets.jsonc`. **The storage half of the M0 plan is reversed and the cadence half is
      not**: budgets, not a committed time series
      ([ADR-0023](adr/0023-performance-budgets-not-a-benchmark-time-series.md)). What makes the
      budgets worth anything is that they are not all the same strength — allocation is
      deterministic and is held to ten per cent, ratios between two cases are machine-independent
      and are the sharpest guards in the file, and wall-clock ceilings on a shared runner are an
      order of magnitude out and catch a step change only ([N29](NOTES.md)). Proven to fire before
      being trusted, on eight deliberate breakages including **a case that vanished and a case
      nobody budgeted**, both of which fail the run.
- [ ] **Watch the first nightly, and treat it as part of adding the guard.** The workflow is green
      locally end to end and **has never run on a hosted runner**. Two things can only be learnt
      there: whether a GitHub Windows runner can open a window at all — if it cannot, the honest
      answers are a headless measurement or deleting that step, never `continue-on-error` — and
      whether the wall-clock ceilings, set an order of magnitude above one laptop's numbers, are
      loose enough for a shared machine. `E8-T15` closes on the first green run, not before.
      [N28](NOTES.md) is why *proven to detect* and *proven to run* are different claims.
- [x] **Write the M1.6 pass/fail criteria into TASKS.md** — `E13-T1`, done on 2026-08-29. Nine
      criteria, `M1.6-C1` … `M1.6-C9`, in
      [TASKS.md](TASKS.md#m16--the-passfail-criteria-written-before-the-spike). **The bars were the
      easy half; what each failure would mean is the half that had to be settled while nobody had a
      result to defend** — `M1.6-C2`, one boolean end to end, is the only criterion that can reopen
      ADR-0020, and it reopens the binding rather than the engine; `C1`, `C3` and `C4` change the
      plan without touching the decision; `C5` … `C9` cannot fail on their answers at all, only on
      not being asked. It also writes down what must **not** count as a failure — OCCT being hard
      to debug or slow to upgrade are costs ADR-0020 has already accepted — and the stop rule, since
      a spike that overruns is stopped and reported rather than extended into the implementation.
- [ ] **Take the M1.6 spike** — `E13-T1`, two weeks, and it is now the largest unstarted thing in
      the project. Priority order if the two weeks run out: **`C1`, `C2` and `C3` are the gate** and
      must land, then `C5`'s first read of the threading envelope, because `R20` is a top-three risk
      and M6 needs the answer rather than the reading. The manifest and the build recipe are kept;
      the shim written here is a probe and is deleted. **Two of ADR-0020's seven open items are not
      on this critical path at all** — the counsel question and whether `OcctNet.Wrapper` has a
      source repository — and neither should be reported as *not done* afterwards. The shape of the
      work is unchanged: OCCT builds from a pinned tag through a vcpkg manifest on Windows *and*
      Linux; one boolean runs end to end through a minimal `spark_occt` and `LibraryImport`; the
      per-RID footprint is **measured** against `50a9935`'s 55–70 MB expectation; a `Materialise`
      on a realistic shape is timed, because ADR-0021's whole rule rests on it being paid once; and
      a first read is taken on the threading envelope (`Q14`) and on whether `ShapeFix` can be
      constrained to a policy we choose.
- [x] **The third M1.5 spike** — `E11-T21`, taken 2026-08-30, and **the verdict is go: M1.5 is
      complete.** AvaloniaEdit hosts headlessly, Roslyn completes `p.` against a type that came from
      an expression, and the caret's visual position survives scrolling. Two of the five criteria
      were reached only after Roslyn answered **nothing at all, twice, with no error** — the Features
      layer must be named in the MEF composition, and the *document* carries its own
      `SourceCodeKind`, which the project's parse options do not override ([N33](NOTES.md)).
      `ScriptCompletion` is the first code in `Spark.Scripting`.

## Then — the rest of M1, the geometry core

**Done since the last revision:**

- [x] `Line`, `Arc`, `Circle`, `EllipseCurve`, `PolyLine` and `PolyCurve` over a `Curve` base —
      `E2-T7` … `E2-T9`. The contract was settled against the parity register **before** the
      types were written (`E2-T41`), so the `AtLength` family and arc-length division are in it
      rather than retrofitted onto it. Exclusions are named on the types rather than left to be
      discovered: no closest point, no split, no curvature, no NURBS conversion, and no value
      equality on curves.
- [x] Value types, `Transform`, `Plane`, `CoordinateSystem`, `Tolerance`, `Angle` — `E2-T2` …
      `E2-T6`, with `E2-T1` short only its `Quaternion`.
- [x] Property-based tests with CsCheck — now 38 properties, generators spanning 1e-9 to 1e9 per
      **ADR-0018**, covering curves as well as values.

**Still to do, in rough order:**

- [x] `Quaternion` — `E2-T1`, done 2026-08-29, and the value layer is complete. Composition,
      `Slerp`, axis-angle both ways, and `IsSameRotation` beside `EqualsWithin` because **`q` and
      `-q` are the same rotation while their components are not** — a trap that belongs in the API
      rather than in a comment. Matrix-to-quaternion extraction and Euler angles are excluded
      **on the type**, so their absence reads as a decision rather than an omission.
      **`Rgba` was settled earlier and is not in scope here**: it lives beside `Appearance` in
      `Spark.Api` (`E5`), because the kernel carries no appearance.
- [x] **Settle the past-participle naming rule and apply it** — `E2-T49`, done 2026-08-29.
      `Plane.Flip` → `Flipped`, `BoundingBox.Inflate` → `Inflated`, `Interval.Expand` → `Expanded`,
      across twelve call sites, the public-API baseline and a worked example in `docs/help/`. **The
      rule now lives in `NamespaceDoc.cs` rather than in a survey document**, so it binds every type
      added after it instead of having to be remembered — which is the half of this item that was
      worth more than the rename. Free today because nothing is shipped and the compiler finds every
      call site; an ADR-0019 change-control question the day after 1.0.
- [x] Extract `RayCaster.cs` and its BVH — `E2-T15`, done 2026-08-29 as **`Ray` and
      `BoundingVolumeHierarchy`**. The seed file bundled a ray, an acceleration structure and
      ray-triangle maths; **there are no triangles yet**, so the honest extraction is the first
      two — and splitting them is better regardless, because a hierarchy over *boxes* serves mesh
      booleans, viewport picking, intersection seeding and `Curve.ClosestPoint` from one
      implementation. Immutable, so parallel queries are safe by construction rather than by
      convention. **`Curve.ClosestPoint` is unblocked.**
- [x] Geometry serialization v1 and the reflection-driven round-trip test — `E2-T29`, `E2-T31`,
      done 2026-08-29 with twenty-two types on the books. **The test earned itself on its first
      run**: `BoundingBox.Empty` did not survive a round trip, because the public two-corner
      constructor sorts its corners and so turns the inverted infinite box into the infinite box
      ([N32](NOTES.md)). Every value carries its own `type` and `version`, so types version
      independently; an unknown version is refused by name rather than read approximately; and
      non-finite numbers are written as strings, because a value a caller can legally hold must be
      saveable.
- [ ] **The C2VGeometry test harvest, timeboxed to one week with a hard stop** — `E2-T32`.
      Harvest only pure-maths-on-values tests; anything needing a `Shape` is discarded without
      argument. **Harvest the assertions, not the generators** — a harvested test whose inputs
      never approach the boundary it checks is a test that cannot fail, which is the trap this
      project has already fallen into twice.
- [x] **Close the three small parity gaps in the value layer** — `E2-T40`, done 2026-08-29.
      `BoundingBox.Intersection`, `Plane.Offset` and `Plane.ByOriginNormalXAxis`. Parity moves
      from 92 to 95 of 837. **Two of the three were not as trivial as the register said**:
      `Intersection` delegates each axis to `Interval.Intersection` rather than reimplementing
      the tolerance rule, which is what stops a box and its three intervals disagreeing about a
      boundary case, and `Offset` rejects a non-finite distance because that is otherwise the one
      route to an invalid `Plane` from a factory. `Interval.Intersect` was renamed `Intersection`
      with them, so both types name their set operations with nouns.
- [x] `spark` writes an OBJ polyline — **M1's demoable**, `E2-T34` and `E12-T5`, done 2026-08-29.
      `spark export --open docs/examples/curves.spark --out curves.obj --tolerance 0.001` opens the
      graph, evaluates it **with no window anywhere**, and writes ten curves and 1,255 vertices.
      `Spark.Geometry.Io` is no longer an empty project. **The half a machine cannot do is still
      outstanding**: opening the file in a third-party viewer and looking at it. The file's
      structure is asserted by tests — one-based file-global indices, invariant-culture numbers,
      no byte-order mark — but *a human has not looked at it yet*, and that is what the criterion
      actually asks for.

## After that — finishing M2, and M1.6

M2 was the highest-information milestone in the project: it simultaneously validated Avalonia
GL, the canvas rendering strategy, the reflection importer, the lacing engine and the layering
split — the five things that could still have forced an architectural change. **All five held.**
What is left of it is the part that makes the skeleton usable rather than demonstrable.

**Done:**

- [x] The two M1.5 spikes that gate M2 — `E11-T19`, `E11-T20`. Both bets held, and the GL one
      returned a finding that changes how every shader is written: Avalonia on Windows defaults
      to ANGLE, so the surface is OpenGL ES 3.0 over Direct3D 11, never desktop GL 3.3.
- [x] Graph model, topological evaluation, provenance cache — `E3-T1` … `E3-T8`.
- [x] **The full replication engine against the lacing specification** — `E4-T2` … `E4-T12`,
      with a corpus test that diffs the corpus against the specification document in both
      directions, and which found two errors in the specification itself.
- [x] The zero-config reflection importer over `Spark.Geometry` — `E5-T2` … `E5-T5`, with the
      two-way diff that makes an unreachable public member a red build.
- [x] Avalonia shell, docking, `GraphCanvas` with drag, wire, pan, zoom, select, delete —
      `E8-T1` … `E8-T6`.
- [x] GL viewport for points, lines and curves — `E9-T1` … `E9-T6`. Curves arrived with the
      curve layer and are drawn from their own tessellation at a display tolerance derived from
      the curve's size, not from the kernel's default.
- [x] **Manual acceptance, run and captured:** the application opens, the graph evaluates, and
      an ellipse divided by arc length, eight circles laced from one node, and a pentagon appear
      in the viewport. `--graph curves --screenshot PREFIX` reproduces it without a human.

**Still to do:**

- [x] Save and load — `E3-T17`, `E3-T18`. A graph now outlives the process.
- [x] Undo and redo — `E8-T9`. Over the same file format, which is what makes it cover a node
      position as readily as a wire.
- [x] Every port shows the type it wants — `E8-T18`. Not in the plan; found by opening the
      application and looking at `Circle.ByCentreRadius`, where a port called `centre` gave no way
      to learn that a `Point3d` belongs in it.
- [x] Library search with camel-hump ranking — `E8-T8`. `cbcr` finds `Circle.ByCentreRadius`.
- [x] Double-click empty canvas to create a node there — `E8-T19`. Asked for as *"let
      double-clicking a blank space add the code block, as in Dynamo"*, and delivered as the half
      that is not blocked: the gesture, and a ranked search box at the pointer. **The code block
      itself is M4** — `Spark.Scripting` is empty, the engine has no per-instance node definitions
      and the file format has nowhere to put script text. When it lands, the same box gains "if
      what you typed is an expression rather than a name, make one of those", which is exactly
      what Dynamo does.
- [ ] `spark run` — `E12-T5`. `Spark.Cli` is still a stub, and it has nothing to run until a
      graph can be loaded from a file.
- [ ] **The M1.6 OCCT spike, against criteria written down beforehand** — `E13-T1`. It answers
      four of the seven things [ADR-0020](adr/0020-occt-via-c-abi-shim.md) records as open, and
      **it is the only place they can be answered** — the rest of that list needs counsel or a
      publisher, not a build. Its scaffolding is throwaway in the M1.5 sense, but the vcpkg
      manifest and the build recipe are kept.

**Deliberately excluded from M2:** code blocks, packages, surfaces, custom nodes. Naming the
exclusions is what keeps a walking skeleton from becoming a death march.

## Later — M3 onward

- [ ] `NurbsCurve` complete, curve intersection, offset, fillet; `Planar` regions and 2D
      boolean; List/Math/String/Logic categories; watch nodes and preview bubbles — M3.
- [ ] The C# code block: the RCS/CADScript/DoodleSharp port campaign, input inference with
      wire-typed IntelliSense, tuple-named outputs, resident and persistent compile caches,
      guard weaving — M4, `E6`. **Demo this publicly when it lands** — it is the signature
      differentiator.
- [ ] Surfaces, `NurbsSurface`, `Mesh`, mesh tessellation and `RenderPackage` streaming,
      shaded viewport, OBJ/STL/PLY read and write, glTF write, the software renderer and CI
      visual regression — M5. **The throwaway SSI spike is gone** (`E2-T37`, withdrawn): there
      is no longer a managed exact-boolean estimate for it to calibrate, and its de-risk budget
      moves to M1.6.
- [ ] BRep, `IBrepKernel` with `Capabilities` gating the UI, and **exact solid operations
      through OCCT** — M6, `E2-T22` … `E2-T28` plus most of `E13`. **M6 is now 20–24 weeks
      rather than 14**, and its demoable improves from *solids that can be combined* to
      *solids that can be combined, filleted, shelled, trimmed and exported to STEP*. The
      robust **mesh** boolean (`E2-T27`) moves out of M6 to 1.x, greyed by `Capabilities` —
      reduced, not cancelled, because OCCT is poor at mesh booleans and Dynamo has them.
- [ ] Packages, per-package-version ALCs, missing-package placeholders, the trust store,
      local DLLs with hot reload, custom nodes, groups, notes, freeze — M7, `E7`.
- [ ] `Spark.Host` proven inside a real Revit or AutoCAD add-in; STEP and IGES through OCCT
      (`E13-T12`); the Windows installer, **now carrying native binaries and the licence
      obligations that come with them** (`E13-T16`, `E13-T17`, `E12-T18`); the website;
      performance and accessibility passes — M8, `E12`. **1.0.**

## Decisions waiting on someone

| # | Question | Blocks |
|---|---|---|
| Q1 | **Two of the three M1.5 spikes are answered and both bets held** (`85e3183`): the GL viewport initialises and draws on the platform we ship to, and the immediate-mode canvas holds 2,000 nodes at 0.87 ms median. The third — AvaloniaEdit plus a Roslyn completion popup, `E11-T21` — is not taken; it gates the M4 code block. | M4 design |
| Q4 | `Directory.Build.props` promotes CS1591 to an error on **four** projects; the plan named three. Is `Spark.Geometry.Io` deliberately included? | `E10-T8` scope |
| Q5 | Revit or AutoCAD as the M8 embedding proof host? The scheduler is the same either way; the add-in shell, licensing and test loop are not. | `E12-T4` |
| **Q13** | **The six licensing questions for counsel, and this is the item on this page with an outside clock on it.** The central one: **is a thin shim whose entire purpose is to expose OCCT a *work that uses the Library* under the Open CASCADE exception, or a derivative work under LGPL §5?** Then — whether single-file, trimmed or AOT publishing is compatible with the relink obligation; whether **vcpkg's port declaring `LGPL-2.1-only`, omitting the exception**, creates exposure; what *prominent notice in supporting documentation* requires concretely; what obligations attach to a user embedding `Spark.Host` in a commercial add-in (`D5`); and whether the source offer is satisfied by a tag reference or needs a hosted archive. **None of this is legal advice and no amount of further reading settles it** — it is a question for a lawyer, and it is on this page for that reason. [ADR-0020](adr/0020-occt-via-c-abi-shim.md) | **Items 1 and 3 before M6.** The rest before 1.0 |
| **Q14** | **What is OCCT's real thread-safety envelope?** May the parallel evaluator call the shim concurrently, and at what granularity? Documented guidance is thin, and `R20` is a **top-three risk that cannot be mitigated until this is known**. *How to find out:* read the upstream source of the packages we actually call, and stress the shim at the evaluator's real thread count — `E13-T14`, started at M1.6. The conservative fallback is a single-writer policy, which would cost throughput on exactly the workload replication makes common. | `E13-T14`, M6 |
| Q7 | Which public STEP corpus is authoritative, and which third-party viewer is the reference? **Downgraded, not closed.** We no longer write a STEP writer, so this stops being about defending a subset and becomes about validating *our use* of OCCT's — smaller, and still necessary, because *OCCT wrote it* is not evidence that a file we produce is correct. | `E13-T12`, and no longer gating anything upstream |
| Q8 | Where does the website live, and who maintains it? | `E10-T14` |
| **Q15** | **ANSWERED AND CLOSED, 2026-08-31 - see D17 and D18.** **(c)** The ubuntu leg survives, managed-only: the rot-guard argument is void under D16, the second-implementation argument stands on its own and never depended on shipping Linux, and building OpenCascade for `linux-x64` would cost an hour per cache miss to guard a platform nobody ships. The leg becomes a standing test of the no-provider configuration for free. *The earlier partial answer follows.* **Answered in part, 2026-08-31 - see D17.** **(a)** The C-ABI shim stays: D16 devalues one of the three things the premium bought and not the other two, and C++/CLI would reverse D7 as well. **(b)** `M1.6-C1`/`C2`'s two-OS requirement is void; Windows alone satisfies them, so **M1.6 is not blocked on WSL**. **(c) is still open** and is the only part that needs anybody: whether the ubuntu CI job survives is a question about a test technique rather than a release commitment. *The original question follows.* **What does D16 reopen?** The client has decided Spark supports **Windows and nothing else, ever** (**D16**). Two things were bought with the cross-platform option that decision gives up. **(a)** [ADR-0020](adr/0020-occt-via-c-abi-shim.md) paid a **15–25% effort premium** for a C-ABI shim over C++/CLI, and the payoff it names is buying back exactly that option — though the shim's *other* reasons, a small chosen ABI surface and upgrade survival, are not about operating systems and still stand. **Ask now: `spark_occt` is unwritten, so this is cheap today and expensive at M6.** **(b)** `M1.6-C1` and `M1.6-C2` require two operating systems, and **that is what blocks M1.6 today**. **(c)** Does the ubuntu CI job survive? Its rot-guard justification is void under D16, but it caught a real defect on its own merits ([NOTES.md N28](NOTES.md)). **Supporting an OS is a release commitment; running CI on one is a test technique** — D16 settled the first only | **Closed.** All three parts answered |
| Q12 | **Is T-Splines in scope at all?** 169 members across 8 types — **20.2% of the entire ProtoGeometry surface**, with `TSplineSurface` alone at 94, more than `Curve`. It is a subdivision-surface modeller, a different discipline from BRep/NURBS, and its API is a sculpting editor (bevel, bridge, weld, crease, slide, fill hole) with its own file formats and its own topology layer. Recommendation: exclude it and say so publicly, as PRD §9 already does for STEP's scope. `ADR-0003`'s closing note calls a subdivision backend *a different decision, not a widening of this one*, so nothing is foreclosed. **The answer sets the denominator of every coverage figure we quote.** [DYNAMO-COVERAGE §6.2](DYNAMO-COVERAGE.md#62-t-splines-is-a-second-product-not-a-subsystem), `E2-T48` | Every parity figure; M5 planning |

*Q6 and Q11 are answered, both by the same client decision, and **the answer to each is the
opposite of what was recommended**. **Q11** asked whether parity moves exact solid booleans into
1.0. It does: the 70 members are 1.0 requirements, delivered by OpenCascade, and **`R1` retires
rather than being mitigated again**. **Q6** asked whether an *optional* OCCT-backed package
would breach the no-native-dependencies promise, and is answered by the package not being
optional — **OCCT ships in the default install**, because a Dynamo user finding booleans greyed
out on first run is precisely what FR-81 forbids. What survives untouched is **NFR-5**:
`Spark.Geometry`'s published output still contains no native binaries, still asserted by CI,
because the native component lives in `Spark.Geometry.Occt`. What replaces the question is
**`R13`, reframed and enlarged** — Spark now acquires a heavyweight native dependency, the
distinction from the one it exists to remove is real, and **it only holds if we say it first,
in our own words**. `E2-T47`, PRD **D2** and **D15**,
[ADR-0020](adr/0020-occt-via-c-abi-shim.md).*

*Q12 stays open and ADR-0020 does not answer it. OCCT has no subdivision modeller either, so
nothing about this decision makes T-Splines cheaper or likelier. The recommendation is
unchanged — exclude it and say so publicly — and so is the reason it must be decided rather
than left: **the answer is the denominator of every parity figure we quote.***

*Q2, Q3 and Q10 — all three about package IDs and which projects would publish — are answered
and withdrawn together. Nothing publishes: `IsPackable` is `false` for every project and no
`PackageId` exists anywhere, so `Spark.Geometry.Io`'s availability does not matter,
`Spark.Docs` was a reservation for a package that will not exist, and `Spark.Host`'s
`IsPackable=true` was an oversight now removed. PRD decision **D11**,
[NOTES.md N14](NOTES.md).*

*Q9 — whether xunit v3 was viable — is withdrawn. All four test projects consume it and 315
tests run green; the 2.9.x fallback turned out to be moot rather than costless, because the
.NET 10 SDK has removed the VSTest bridge entirely. See [NOTES.md N11](NOTES.md).*

*Q4 — whether `Spark.Geometry.Io`'s inclusion in the CS1591 promotion was deliberate — is
**answered by precedent rather than by decision, and stays open until somebody says so.**
`Directory.Build.props` now applies the public-API baselines to the same four projects, which
means two independent mechanisms have converged on the same list. That is evidence the list is
right; it is not a record that anyone chose it.*

## Known and deliberately accepted

Not bugs. Recorded so nobody rediscovers them as surprises, or spends an afternoon
"fixing" a decision.

- **No Dynamo compatibility, in either direction.** No `.dyn` reader, no writer, no
  importer, no seam. A `.dyn` file contains no geometry, so reading one never needed
  ProtoGeometry — but **semantic equivalence** does, and that is unprovable without the
  very dependency Spark exists to remove. A silently mistranslating importer is worse than
  none. PRD decision **D8**. Corollary: the `By*` names carry **no** compatibility
  obligation; they exist for human recognition only.
- **Capability parity with Dynamo is tracked, and it does not reopen D8.** The two are about
  different things and will be confused repeatedly, so: D8 refused `.dyn` because *semantic
  equivalence* is unprovable without ProtoGeometry, and it stands. Capability parity is about
  *presence* — whether a Dynamo user reaches for something and finds it absent — which needs
  no reference implementation to check. [DYNAMO-COVERAGE.md](DYNAMO-COVERAGE.md) is the
  register: 51 ProtoGeometry types, 837 members, 92 reachable today. `Done` in it means
  *present and documented*, never *equivalent*, and the test that keeps it honest
  (`E11-T23`) must say so in its own failure messages.
- **Six curve types, and the exclusions are named on the types rather than discovered.** There
  is no `NurbsCurve` and no `Helix`; no closest-point query, split, curvature, planarity test
  or NURBS conversion on the curve contract; no offset, projection or pull; and **no value
  equality on curves**, because two curves drawing the same path through different
  parameterisations are a tolerance question rather than an `Equals` question, and answering it
  wrongly by default is worse than not answering it. `Curve.ClosestPoint` in particular waits on
  the ray caster and its BVH (`E2-T15`) rather than getting a second implementation.
- **A general affine transform of an ellipse is refused, not approximated.** `TransformedBy`
  accepts similarities. A shear does take an ellipse to an ellipse, but recovering the new axes
  from the mapped conjugate pair is Rytz's construction, and doing it approximately would return
  a curve that is quietly the wrong shape. A non-uniform scale on a `Circle` is refused for the
  same reason — the answer is an ellipse, and a `Circle` cannot hold one.
- **An undo reopens the document, so nothing about a canvas node survives it except its
  identity.** `CanvasNode` objects are new, slots renumber into the file's canonical order, and
  the selection is dropped ([N23](NOTES.md)). This is the price of undo being defined by the same
  writer that saves the file rather than by a second definition of what a document is, and the
  price is worth paying: the alternative drifts. Code that crosses an undo looks a node up by
  `NodeId`, never by slot.
- **The node canvas runs a real Gaussian blur per node between 81% and 83% zoom**, where the
  drop shadow crosses its threshold, costing 57→40 fps at 2,000 nodes. The design language
  already specifies the fix — a sprite cache keyed on a fixed set of blur radii, eight sprites
  in total. Scoped, specified, and not yet built (`E8`).
- **`concepts.evaluation` is a help topic id with no file behind it.** Five diagnostic codes
  resolve to it and `docs/help/concepts/evaluation.md` does not exist, so a user following an
  `SPK101x` code has nowhere to land. The docs harness does not currently check that a topic id
  names a real topic, which is why nobody noticed; both halves are worth fixing together
  (`E10`, `E11-T14`).
- **Coordinates are unitless.** No `UnitSystem`, no unit types, no conversion. Import and
  export assume the file's own units and document that they do. PRD decision **D12**. This
  does **not** remove scale-aware tolerance, which is numerical robustness rather than
  units and stays.
- **No drafting or annotation types, ever.** No dimensions, hatches, text, arrows, grids or
  spatial cells — none of them are concepts Dynamo has. C2VGeometry's versions are
  discarded outright rather than parked, because a parked type is a type someone eventually
  revives. PRD decision **D13**.
- **v1 releases are Windows only.** A signed Inno Setup installer plus a portable zip. An
  ubuntu build-and-test job stays in CI as a rot-guard, because it is nearly free and it is
  the only thing that stops cross-platform support rotting silently — but **no Linux or
  macOS artefact is published**, and macOS is not built at all. PRD decision **D14**.
- **Exact NURBS booleans, and fillet and chamfer on solids, are in 1.0 — and they come from
  OpenCascade.** This reverses what this bullet said, and the reversal is the point rather
  than an embarrassment: robust surface-surface intersection *is* a research-grade problem and
  is what makes commercial kernels cost millions, which is exactly why we are not writing one.
  `R1` **retires**. PRD **D2**, **D15**, [ADR-0020](adr/0020-occt-via-c-abi-shim.md).
- **Spark ships a heavyweight native dependency, and this is the thing to be honest about
  first.** Spark exists because Dynamo Sandbox forces users to have an Autodesk product
  installed, and because solving that by acquiring a different heavyweight dependency would
  move the problem rather than remove it. The distinction is real — **OCCT is open source,
  freely redistributable, installed *with* Spark, and needs no account, no licence purchase and
  no other vendor's product** — and it only holds if we say it first, clearly, in our own
  words. The README says it. `Spark.Geometry` stays pure managed and independently
  distributable, **NFR-5 is unchanged**, and OCCT ships in the default install because a Dynamo
  user finding booleans greyed out on first run is what FR-81 forbids. `R13`.
- **Robust mesh booleans move to 1.x. Reduced, not cancelled.** OCCT is **poor** at mesh
  booleans and Dynamo has them, so `E2-T27` keeps its purpose and loses only its urgency;
  `Capabilities` greys the operation until it lands. Do not read the deferral as a deletion.
- **The Linux CI job is no longer nearly free, and that was its entire justification.**
  ADR-0001 kept it as a rot-guard because it cost almost nothing; it must now build native
  code. The mitigation is a cached per-RID artefact keyed on
  `(occt-tag, vcpkg-baseline, shim-source-hash, rid)`, with the from-clean build nightly —
  **without it the rot-guard will not survive a busy PR queue**, and losing it would quietly
  convert `D1` into wasted effort. `E13-T15`.
- **`Brep` is no longer a pure value.** Under [ADR-0021](adr/0021-brep-kernel-residency.md)
  residency is **canonical, not cached**: after a kernel operation the provider's
  representation is authoritative and ours is materialised lazily. The reason is **fidelity,
  not speed** — a Spark→OCCT→Spark round trip is not identity, because OCCT carries tolerances
  we do not, `ShapeFix` may legitimately merge vertices and split faces at seams, and a face
  from an intersection may come back a B-spline where the input was a cylinder. Ten
  convert-in/convert-out operations would re-sew and re-tolerance the model ten times, and the
  user would watch their geometry drift while doing nothing. Consequences to accept, not fix:
  a finalizable native resource on a geometry value; equality and hashing defined on the
  materialised model and **never on the handle**; and **`NFR-4`'s cache must track a native
  budget reported by the shim**, because a managed size estimator cannot see OCCT's heap.
- **`NFR-8`'s watertightness property now tests a third party's mesher.** `Brep` tessellation
  moved behind the seam because tessellating a trimmed BRep face is genuinely hard and OCCT
  solves it — and OCCT's mesher is not guaranteed watertight at default deflection. Either the
  property holds at a deflection we choose or the requirement is restated to say what it
  guarantees. **It must not quietly become a suppressed test**, which is the failure mode this
  bullet exists to name. `E13-T11`.
- **There will be exactly one `IBrepKernel` provider, and a second is not planned.** The seam
  is retained for `Result<T>`, `Capabilities` and insurance. **Do not build a second provider
  to justify the abstraction.**
- **`AllowUnsafeBlocks` is `false` everywhere except `Spark.Geometry.Occt`.** The
  `LibraryImport` source generator emits unsafe code and will not run without it. The
  repository default stays `false`, the opt-in is in one csproj with a comment naming
  ADR-0020, and an architecture test asserts it is the only one. Likewise
  `SparkGeometryTakesNoThirdPartyDependencyBeyondClipper` **stays exactly as it is** and gains
  a *companion* rule for `Spark.Geometry.Occt`. **Relaxing either test would be the wrong
  repair**, and it will look like the obvious one.
- **Upgrading a package restarts the application by default.** An ALC is pinned by node
  definitions, compiled invokers, cached values, viewport buffers and undo history. Upgrade
  purges all of them, unloads, and verifies by weak reference — and when it does not
  unload, the UI **says so and offers restart**. Live unload is a best-effort optimisation,
  not a promise. Declaring this on day one avoids the entire "why is the old version still
  loaded" bug class. `R8`.
- **`StackOverflowException` kills the process.** It cannot be caught in .NET, and no
  amount of guard weaving changes that — weaving only reduces the frequency. Aggressive
  autosave and crash recovery limit the damage. The real fix is an opt-in out-of-process
  worker, kept viable by the scheduler and ALC seams and **deliberately deferred past
  v1**. `R11`.
- **A Spark graph is executable code.** Opening one from an untrusted source is equivalent
  to running an unknown program. .NET has no code-access security and Spark will not
  pretend otherwise. What actually works, and is what ships: opening never auto-runs
  (Manual mode plus a banner listing script nodes and required packages), a content-hash
  per-origin trust allowlist, and `spark run --no-script` for CI.
- **No telemetry of any kind in v1.** Not anonymous, not opt-out, not "just crash counts".
  Opt-in crash reporting is considered post-1.0, with graphs excluded from any payload.
- **Nothing here is published to nuget.org, and `IsPackable` is `false` everywhere.** Spark
  **consumes** NuGet packages and loose DLLs — that is a core feature and it is untouched —
  and **produces** none. Spark is an application, not a library ecosystem; the two audiences
  who compile against it, embedders and node authors, reference the assemblies from an
  install, which is how CAD add-ins are built anyway. So a project's assembly name is its
  only name, `Spark.Cli` builds `spark.exe` and ships beside the desktop application rather
  than as a dotnet global tool, and *published output* in these documents always means
  `dotnet publish`. Do not add packaging metadata to a `.csproj` because it looks like an
  omission — it is a decision. PRD decision **D11**, [NOTES.md N14](NOTES.md).
- **`Spark.Api` and `Spark.Geometry` are not strictly additive across 1.x**, which reverses
  what ADR-0009 said. That rule was built on a public package ecosystem that will not exist.
  What replaces it is proportionate deliberate change control: prefer adding, break only when
  it is genuinely better, and record it when you do. The real remaining cost of a break is
  that a user recompiles their own node DLL — an annoyance for one person, not an ecosystem
  fracture. Public-API baselines stay, as a **review aid rather than a compatibility
  guarantee**. **ADR-0019**, superseding ADR-0009.
- **`Auto` is not a synonym for `Longest`, and must not be "simplified" into one.** It is a
  sentinel meaning *use this node definition's `DefaultLacing`*, resolved to one of the four
  real modes before replication begins; it has no `n` and no output rank of its own. The
  plan's original reading — "`Longest`, but zero-excess inputs never iterate" — was
  overturned in review, because zero-excess inputs never iterate under *any* mode, which
  would have made `Auto` a menu entry that provably never did anything. Two nodes both set
  to `Auto` may therefore lace differently, and that is the feature. `lacing.md` §2.9 and
  its decision **D4**.
- **A test project containing no tests fails the run**, so test projects are created
  alongside the code they test rather than as empty stubs ahead of it. Three stubs were
  created and deleted before this was understood. It follows from Microsoft.Testing.Platform,
  which the .NET 10 SDK makes the only option — [NOTES.md N11](NOTES.md),
  [N12](NOTES.md).
- **`Spark.Nodes.Core` may not reference `Spark.Engine`,** and this will occasionally be
  inconvenient. That is the point: first-party nodes are forced through the same
  zero-config importer as third-party ones, so the importer cannot quietly special-case us
  and then fail for everyone else.
- **Warnings are errors in CI only, never in the csproj.** A red build on a stray unused
  variable mid-edit trains people to pass `-warnaserror:false`, which defeats the gate
  entirely. Local development stays pleasant; the gate stays absolute.
- **Clipper2 is an accepted third-party dependency of `Spark.Geometry`, but it is not
  referenced right now.** Its C# distribution is pure managed and Boost-licensed, and the
  version stays pinned exactly in `Directory.Packages.props`. The `PackageReference` was
  **removed** on 2026-08-27 because nothing in the value layer used it, and an unused
  reference costs restore time, licence surface and audit noise while making the reference
  graph lie about what the assembly needs. It returns with the planar boolean pipeline
  (`E2-T14`), isolated behind a single internal file as always. The architecture test asserts
  a **ceiling — no third-party dependency beyond Clipper2 — not an exact set**, so it holds
  on both sides of that round trip. Do not "restore" the reference because its absence looks
  like an omission. `R13`.
- **RS0026 is suppressed in `.editorconfig`, deliberately and with the reasoning recorded
  there.** It forbids two overloads of one name both carrying optional parameters, in order to
  prevent a future **source-breaking** change in a published library. Spark publishes nothing
  (**ADR-0019**), so that promise is one Spark no longer makes; the overloads it flagged —
  `Contains(point)` and `Contains(box)`, each with an optional tolerance — differ in a
  **required** parameter type, so every call site resolves unambiguously, and genuine
  ambiguity is caught by the compiler as CS0121 regardless. Mangling a good API to satisfy a
  rule aimed at a constraint we do not have is the tail wagging the dog. **RS0016 — undeclared
  public symbol — stays at its default error severity**, and that is the rule the baselines
  exist for. [NOTES.md N17](NOTES.md).
- **A `dotnet build` reporting "0 warnings" may be reusing a cached analysis.** This one is a
  genuine trap and it will catch somebody. Incremental builds can skip re-running analyzers
  over a project MSBuild considers up to date and still print a clean summary, so a warning
  that a fresh compilation would raise never appears. The public-API findings that led to the
  baselines were **invisible under a plain `dotnet build` and appeared the moment
  `--no-incremental` was added**. Before you believe a clean build — and always before
  claiming one in a document, a review or a commit message — run
  `dotnet build Spark.slnx --no-incremental -warnaserror`. "It built clean" is not a claim
  worth making about an incremental build. [NOTES.md N15](NOTES.md).
- **Private `const` fields are PascalCase, not `_underscored`.** CA1802 actively pushes code
  towards `const`, so requiring an underscore on private consts would have set two analyzers
  arguing with each other over the same field. The rule must sit **before** the underscore
  rule in `.editorconfig`, because naming rules are evaluated in file order and the first
  match wins. It sits beside the same rule for private `static readonly` fields, and the pair
  makes the underscore itself informative: it marks something that can change.
  [NOTES.md N16](NOTES.md).
- **A fix to the kernel is not finished until it is regression-proven by reverting it and
  naming the test that goes red.** Not "a test exists nearby", not "the suite is green" — the
  specific test, identified by having watched it fail. A fix with no test that notices its
  absence is a fix the next refactor silently undoes. This is the standard because its absence
  is exactly what let the kernel's first slice pass three gates while three of its eight
  claims were false. [NOTES.md N18](NOTES.md).
