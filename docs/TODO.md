# Spark — TODO

What to do next, in priority order. Full context in [EPICS.md](EPICS.md), full inventory in
[TASKS.md](TASKS.md), the reasoning in [PRD.md](PRD.md).

**Last updated:** 2026-08-28

**M0 and most of M1.5 have landed, M2's walking skeleton runs, M1's geometry core now has curves,
a graph can be saved and opened, and every edit can be undone.** The application opens, a graph evaluates, and an ellipse,
eight circles and a polygon appear in the GPU viewport — from a seeded demo or from a file, and
Ctrl+Z steps back through every edit. `dotnet build`, the test suite (**1,167 tests over eight
projects**) and `dotnet format` are all clean — though `dotnet test` itself now reports
`Zero tests ran` on SDK 10.0.400 and the 1,167 are counted by `scripts/run-tests.sh`
([N34](NOTES.md)) — and **CI ran green on Windows and Linux on `53596ab`**, 969 tests on each leg — and the Linux leg has now caught something Windows could
not, which is the first time it has been worth more than it cost ([N28](NOTES.md)).

Three distinctions still do the work in what follows:

- **What is built.** The value layer (16 types, `Quaternion`, `Ray` and a generic `Bvh<T>`
  included), the curve layer (`Line`, `Arc`, `Circle`,
  `EllipseCurve`, `PolyLine`, `PolyCurve` over a `Curve` base, with arc-length
  reparameterisation), the graph engine and replicator, the reflection importer with its
  two-way diff, the Avalonia shell, the immediate-mode canvas, the GL viewport, 57 nodes in
  `Spark.Nodes.Core`, a `.spark` file a graph survives a round trip through byte for byte, and a
  64-step undo stack over that same file format.
- **What is not.** No surfaces, meshes, BRep or solids. No `NurbsCurve`. No
  packages and no code block. And **no OCCT**: there is no `native/` directory and no
  `Spark.Geometry.Occt` project.
- **Gates are not review, and this project now has its own proof three times over.** The
  kernel's first slice passed all three gates and was rejected on review ([NOTES N18](NOTES.md)).
  The curve layer's mutation sweep then found a test that could not fail and a branch that could
  not be reached ([N19](NOTES.md), [N20](NOTES.md)). The undo sweep found the same shape a third
  time: a test asserting that clicking a node is not an edit passed under a mutation that recorded
  *every* drag, because a click raises no pointer-move event and never reached the guard the test
  was written for. All of it in code that was green.

**The largest decision in the project is unchanged and unbuilt.** The client chose to take an
existing solid-modelling kernel rather than write one: **OpenCascade, reached through a C-ABI
shim we own** ([ADR-0020](adr/0020-occt-via-c-abi-shim.md),
[ADR-0021](adr/0021-brep-kernel-residency.md), PRD **D2** and **D15**). It retires **R1** and
**R12**, adds **R15 … R22**, adds a two-week spike **M1.6**, adds epic
[E13](EPICS.md#e13--occt-provider) of roughly 24 weeks, and costs **+7 to +11 weeks against the
plan as written while saving years against what was actually asked for**. `50a9935` measured
what comparable projects actually ship: a full win-x64 OCCT build is 52.1 MiB across 47
toolkits plus 9.9 MiB of optional third-party libraries, so **R15's 40–160 MB bracket should be
read as 55–70 MB** — but that is a survey, not a build.

**One row on this page came from using the product rather than from planning it**, and it is worth
saying so where the plan lives: `E8-T18`, port type labels. Nothing in the PRD asked for them and
nothing in EPICS was short without them. Somebody opened the application, put down a
`Circle.ByCentreRadius`, and could not tell what `centre` wanted. The requirement was written
afterwards (**FR-82**), which is the right order for a defect nobody predicted and the wrong order
for anything else.

---

## Now — schedule the guards, and name the M1.6 criteria

- [x] **Walk TASKS.md against E3, E4, E5, E8 and E9** — done, and it moved 41 rows. Ten of them
      came back **`In progress` rather than `Done`**, which is the useful output: the cache
      evicts by entry count rather than by bytes, no node can declare itself impure, the
      host-thread scheduler is missing, cancellation does not reach inside a kernel operation,
      the shell has no real docking, the library panel filters without ranking, and three rows
      wait on the empty `bench/`. **Two of those ten have since closed and one of them closed by
      being disproved**: the cache holds a byte budget now, and the impure-node declaration was
      never missing — only its test was. Two more are `Open` **with a stated reason** rather than by
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
- [x] **Run the benchmarks on a schedule** — `E1-T21`, and the standing halves of `E4-T3` and
      `E8-T15`. `.github/workflows/benchmarks.yml` runs nightly: the three managed suites on
      `ubuntu-latest`, `--canvas-benchmark` on `windows-latest`. **What it gates is bytes
      allocated per operation, and only that**
      ([ADR-0023](adr/0023-benchmarks-gate-allocation-not-time.md)) — allocation is a property of the code rather than
      of the host, so it is the one figure a shared runner cannot move, whereas a timing threshold
      on a noisy VM fails for reasons nobody can act on and teaches people to override the job.
      Timings are recorded to 90-day artifacts and to the run summary, never gated.
      `scripts/check-benchmark-regression.py` was **proven to fire on all five of its paths**
      before being trusted. **`E1-T21` asked for the series to be committed to the repository and
      that was deliberately not built**: it needs `contents: write` on a scheduled job, and a
      committed `bench/baseline.json` that a person updates on purpose does the same work without
      putting an automated writer on `main`. Two things follow rather than finish here — the
      workflow **has never run**, and the canvas threshold is `E1-T34`, waiting on data rather
      than on effort.
- [ ] **Preparing that gate found the number it was going to gate was wrong** — `E8-T21`, done,
      and it is the reason the row above is not simply ticked. `--canvas-benchmark 600` printed
      `frames=500` over a 120-frame window, so its median described the tail of the zoom sweep
      rather than the run: 1.70 ms against the 1.15 ms the whole run gives. **Every canvas figure
      quoted in these documents before today was measured that way**, including the 0.87 ms this
      page and `E8-T15` both cited, and they are withdrawn rather than restated ([N31](NOTES.md)).
      What is left here is only to re-measure and re-quote once the nightly has run on hardware
      worth quoting.

- [x] **`dotnet test` stopped reporting the suite, and the suite was never the problem** —
      unplanned, found on the first gate run of this session. On SDK 10.0.400 every one of the
      seven projects reports `Zero tests ran` and exit code 5 in about 130 ms; run directly, the
      same binaries discover and pass 981. `dotnet test` drives a Microsoft.Testing.Platform
      project over a named-pipe server protocol and the handshake never completes, so the SDK
      reports its own empty inbox. **Neither package is stale** — `xunit.v3` 4.0.0 and
      `Microsoft.Testing.Platform` 2.3.3 are both the newest published — so there was no upgrade
      to reach for, and tightening `rollForward` is the real lever but is **deliberately not
      pulled yet**, because 10.0.100 is not installed here and the pin would trade a misleading
      run for a build that does not start. `scripts/run-tests.sh` is a **second opinion, not a
      replacement**: `dotnet test Spark.slnx` stays the documented gate because it is what CI
      runs, and the two must agree ([N34](NOTES.md)).
- [x] **The application has a mark, and shows it while it loads** — `E8-T22`, asked for rather
      than planned. `assets/spark-icon.svg` is the master; the shell draws the same geometry from
      `Theming/SparkLogo.axaml`, which carries that file's path strings verbatim, and the window
      icon is rendered from that drawing at startup so **no `.ico` exists in the tree to fall out
      of date**. A test fails if the two ever disagree. The splash covers the second or so the
      shell takes to build, and is suppressed for both measurement modes. Three non-obvious
      startup facts came out of it ([N33](NOTES.md)), the sharpest being that a splash **cannot**
      report step-by-step progress: the thread it would report on is the one that is blocked.

## Next — the criteria are named; the spike is not taken

- [x] **Write the M1.6 pass/fail criteria into TASKS.md** — `E13-T1`, done. Seven criteria in
      [TASKS.md](TASKS.md#e13--occt-provider), fixed before any OCCT source is fetched. **C1 and
      C2 are hard gates** — the pinned-tag vcpkg build on both platforms, and one boolean end to
      end through the shim with its volume checked analytically; failing either falsifies
      ADR-0020 as written. **C3 … C7 are measurements**, and the part that needed deciding
      beforehand was not what they measure but what a bad number *does*: above 100 MB per RID,
      `E13-T17` stops being a distribution row and becomes a design one; above ~250 ms for a
      200-face `Materialise`, lazy materialisation becomes load-bearing for interactivity and
      `E13-T3` inherits partial materialisation; an inconclusive threading read means
      single-writer **by default and recorded as conservative**, because the failure to avoid is
      neither answer but a shrug. **The two-week box is real** — if C1 and C2 are not both met
      inside it the spike stops and reports *ADR-0020 is not cheap to prove*, which is itself a
      result.
- [ ] **The third M1.5 spike is still outstanding** — `E11-T21`. AvaloniaEdit plus a Roslyn
      completion popup. It gates the M4 code block rather than anything M2 needs, which is why
      the other two were taken first, but it is the last unproven part of M1.5.

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

- [x] `Quaternion` — `E2-T1`, done, and **the row is closed**. It is there for the two jobs a
      matrix does badly: composing rotations without drift, and interpolating orientations along
      the shortest arc. `ToTransform` and `ByRotation` are the only crossings; everything else
      goes on using `Transform`. Three decisions live on the type rather than waiting to be
      discovered — `q` and `-q` are one rotation, so equality is componentwise like every other
      value here and `RepresentsSameRotationAs` is the separately named question;
      `default(Quaternion)` is **not** the identity, because a default meaning *leave it as it
      is* is what lets a missing assignment ship; and `Inverse()` is the algebraic inverse, so
      `q * q.Inverse()` is the identity for any valid `q` rather than the identity scaled.
      Writing the tests turned up a fourth: **an axis read straight off a negative-scalar
      quaternion is the opposite axis with a positive angle**, which describes the rotation
      backwards, and taking the absolute value of the scalar fixes the number without fixing the
      sign. **`Rgba` is settled and no longer in scope here**: it lives beside `Appearance` in
      `Spark.Api` (`E5`), because the kernel carries no appearance.
- [x] **Settle the past-participle naming rule and apply it** — done, and the rule is in
      `NamespaceDoc` rather than only in
      [DYNAMO-COVERAGE §4](DYNAMO-COVERAGE.md#member-names-we-will-not-copy), which is where a
      convention has to live to be applied by anyone who has not read that document.
      `Plane.Flip`, `BoundingBox.Inflate` and `Interval.Expand` are now `Flipped`, `Inflated`
      and `Expanded`. The argument that settled it is not aesthetics: `plane.Flip();` as a
      statement compiles, does nothing, and reads as though it worked. Factories and queries
      are named as the two deliberate exemptions, and `Interval.MakeIncreasing` keeps its name
      because `Increased` would mean something else. All three were unshipped, so it was free
      today and an ADR-0019 change-control question the day after 1.0.
- [x] **A property test was asserting through a boundary, and it would have been blamed on
      something else for years** — found while running the gates for the row below.
      `TheSignedAngleBetweenTwoVectorsDoesNotDependOnTheirLengths` failed twice in about
      twenty-five runs, different seed each time, no kernel change between them. At
      `CsCheck_Iter=50000` it fails on every run and the shrunk case says why: at a turn of
      9e-56 degrees the two angles agree to fifty digits, so `EqualsWithin` passes, while one is
      exactly zero and the other a denormal above it, so `Math.Sign` returns 0 against 1. **The
      sign of a quantity at the noise floor is not a fact about the geometry.** The property was
      right; a second and false claim had been smuggled in beside it. One in twenty-five thousand
      samples is a red build every few weeks on an unrelated commit, which is the exact profile
      of a test that eventually gets suppressed ([N35](NOTES.md)).
- [x] **The ray caster and its BVH** — `E2-T15`, done, and **written rather than extracted**.
      The C2VGeometry original casts against triangles and Spark has no mesh to cast against, so
      what landed is the part every consumer shares: a `Ray`, and a generic `Bvh<T>` over
      anything that can be given a box. It is a **broad phase and says so on the type**, because
      a broad phase quietly taken for an exact answer is a picking bug that reproduces only at
      certain camera angles. Three things are worth knowing about it. Splitting is a binned
      surface-area heuristic that **falls back to a median split rather than to a leaf**, since
      the SAH honestly loses to a leaf on coincident boxes and an oversized leaf is scanned
      linearly by every query thereafter. Nothing is written after `Build` and every traversal
      keeps its stack local, so **many threads may query one tree** — asserted rather than
      assumed. And the slab test **branches on a zero direction component rather than dividing
      through it**: the branchless form makes `0 × ∞` when the origin lies exactly on a slab
      plane, which is `NaN`, which reports a miss on precisely the alignments axis-aligned work
      produces most often. Checked against the linear scan it replaces, by example and by
      property, and the shape test asserts a **lower** bound on depth as well as an upper one —
      a tree collapsed to one leaf would pass every other test in the file.
      **`Curve.ClosestPoint` is next, and now has what it was waiting for.**
- [x] **`Curve.ClosestPoint`, on that hierarchy** — the member the curve contract named as an
      exclusion and said was waiting for the ray caster. `ClosestPoint`,
      `ParameterAtClosestPoint` and `DistanceTo`, one implementation for every curve type: an
      exact projection on `Line`, a plane-and-angle argument on `Circle` and a general search
      for the rest is three pieces of code that must agree at their boundaries, and a
      `PolyCurve` of a line and an arc is such a boundary, probed from either side, in one
      query. **Newton's method was written first and is wrong at a corner** — the derivative at
      a vertex belongs to the segment after it, so for a target just before the vertex the
      gradient is a backward offset dotted with a perpendicular direction, which is zero;
      Newton declines to move and the query returns the corner. Found by a test that compares
      the query against a 4,001-sample scan that cannot beat a real minimiser and therefore
      should never win. Fixed twice over: spans are now cut on the curve's own seed boundaries
      so none straddles a corner, and the narrow phase is a **golden-section search** that
      needs no derivative ([N36](NOTES.md)). The tolerance is a promise about the answer rather
      than a hint — the search stops when a further step would move the point less than
      `Tolerance.Linear` — so the default resolves to 1e-6 and a caller who needs more asks.
- [x] **Geometry serialization v1 and the reflection-driven round-trip test** — `E2-T29`,
      `E2-T31`, done, and it was written at twenty types rather than at forty, which was the
      whole point of the row. `GeometryJson.Write` and `Read`, with a **per-type `$v`** rather
      than a document version, because a `NurbsCurve` at v2 and a `Mesh` at v1 have to coexist
      and a single version forces every type to move together. **Round-tripping is byte-identical
      and the format is designed backwards from that**: a `Plane` stores all four of its vectors
      rather than the two that generate them, because re-deriving a frame on read
      re-orthonormalises it and a file whose diff is floating-point noise cannot be reviewed.
      Three types gained an `internal` exact constructor for that reason, and one of the three
      matters more than it sounds — `BoundingBox.Empty` is an **inverted** box and the public
      constructor sorts its corners, so a naive reader turns the box containing nothing into the
      box containing everything, silently. Source generation was **rejected with an argument
      rather than skipped**: immutable types with no parameterless constructor would each need a
      mutable DTO and two mappings, which is a second definition of the type and therefore the
      thing that drifts, and hand-written converters use no reflection at all. The diff runs both
      ways and **was proven to fire** — a sample was removed and the build went red naming the
      type — which is the same discipline the node importer's two-way diff already applies.
- [ ] **The C2VGeometry test harvest, timeboxed to one week with a hard stop** — `E2-T32`.
      Harvest only pure-maths-on-values tests; anything needing a `Shape` is discarded without
      argument. **Harvest the assertions, not the generators** — a harvested test whose inputs
      never approach the boundary it checks is a test that cannot fail, which is the trap this
      project has already fallen into twice.
- [x] **Close the three small parity gaps in the value layer** — `E2-T40`, done.
      `BoundingBox.Intersection`, `Plane.Offset` and `Plane.ByOriginNormalXAxis` were omissions
      rather than design differences, and **two of the three carried a decision the word
      "trivial" had hidden**. `Intersection` returns `Empty` for disjoint boxes rather than the
      inverted box the crossed-over bounds give, because an inverted box is invalid but not
      *canonical*, so two disjoint pairs would otherwise return two unequal answers that both
      mean "no overlap"; and it takes **no tolerance**, because `Intersects` answers a question
      about touching while `Intersection` returns a region, and widening a region by a tolerance
      hands back space neither box occupies. Parity in the value layer goes from 92 to
      **95 of 133**.
- [x] **The cache holds a memory budget, and one row that said something was missing was wrong**
      — `E3-T9` and `E3-T10`. The cache now evicts against **two bounds**: an estimated byte
      budget, 256 MiB by default, and the entry ceiling it already had. The bytes are the bound
      that matters, since four thousand meshes and four thousand numbers are the same cache by
      count and are not the same cache. `GraphValueSize` estimates, and **names its three blind
      spots on itself**: no native memory, no sharing (the same curve twice is charged twice, and
      that error is in the safe direction), and no walking of a curve's tessellation. The
      **native** half stays open and belongs to `E13-T3`, exactly as ADR-0021 requires — a
      provider *reports* its budget and nothing here infers one. A single result larger than the
      whole budget is kept rather than evicted, because evicting it empties the cache and then
      evicts the thing just computed.
      **`E3-T10` turned out to be already built**, and the row saying otherwise is the more
      useful half of this. `NodeSideEffectAttribute` existed, the importer read it, the key mixed
      the epoch and the evaluator honoured it. What was missing was a **test through the
      attribute**: the only one that existed built a definition by hand with
      `isSideEffect: true`, exercising the engine and skipping the one step a node author ever
      takes — so deleting the importer's check would have left the suite green and made every
      impure node in every package silently pure. That is the worst failure a provenance cache
      has, because it poisons nothing and therefore never looks wrong.
- [x] **The rest of the near-term parity list, and `E2-T16` withdrawn rather than built** —
      `Point3d`/`Vector3d` cylindrical and spherical construction, and
      `Point3d.PruneDuplicates`. That last one is what `E2-T16`'s KD-tree existed for, and the
      KD-tree is **not built**: `E2-T15`'s hierarchy answers welding, dedup and point queries
      alike, and a second spatial index is a second thing to get right, to test and to keep
      true. It should be revived on a **measurement** — pure point sets at scale — and not on
      an opinion. Two decisions came out of writing these and both are on the members: spherical
      **inclination is measured from the normal rather than from the plane**, because that is the
      convention under which a sphere sweeps half a turn and the alternative differs by a sign
      as well as an offset; and pruning compares a point only against points that were **kept**,
      never following a dropped one through to its own survivor. The second was got wrong first
      and a test caught it: following the chain makes coincidence transitive, a chain has no
      length limit, and a point can end up merged into a representative arbitrarily far away.
      As built, **no point moves by more than one tolerance**, and a property asserts it.
      Parity in the value layer reaches **98 of 133**.
- [x] **`spark` writes an OBJ polyline** — `E2-T34`'s writer half and `E12-T5`'s `run`.
      `Spark.Geometry.Io` stops being an empty project and `Spark.Cli` stops being a stub:
      `spark run docs/examples/curves.spark --export curves.obj` evaluates the demo graph
      headlessly through the same `SparkSession`, node library and `SparkFile` reader the
      desktop application uses, and writes 20 polylines and 59 points. **The last clause of
      this row is not ticked**: *a third-party viewer opens it* is a manual acceptance nobody
      has performed, and it is the only part of the M1 demo that a test in this repository
      cannot stand in for. What is done instead is the strongest proxy available — the OBJ is
      read back by a deliberately naive parser written in the test project, which refuses any
      directive it does not know, because a writer verified by its own reader agrees with
      itself and with nobody.
      Three decisions are recorded on the writer rather than left to be found: OBJ has **no
      curve entity**, so curves are tessellated and the chord tolerance goes into the header;
      **nothing is welded**, because welding is a tolerance decision belonging to whoever knows
      what the model is; and numbers are invariant-culture with `
` endings on every platform,
      since a comma decimal separator turns `1,5` into two fields that no reader complains
      about. **Running it found a real defect**: `--tolerance` meant a *characteristic length*,
      so asking for a coarser export with a bigger number produced a file eight times larger —
      649,105 vertices against 79,361. It now means the linear tolerance itself.

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
- [x] Preview bubbles and hover tooltips — the built half of `E8-T10`, plus the tooltip design
      language §7.2 has specified since M0. **The watch panel is still open**, which is why the
      row stays `In progress`.
- [x] Port descriptions from `<param>` — `E5-T7`, which had been marked `Done` while reading only
      `<summary>` ([N29](NOTES.md)). Found by building a port tooltip that had nothing to show.
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
  register: 51 ProtoGeometry types, 837 members, 98 reachable today. `Done` in it means
  *present and documented*, never *equivalent*, and the test that keeps it honest
  (`E11-T23`) must say so in its own failure messages.
- **Six curve types, and the exclusions are named on the types rather than discovered.** There
  is no `NurbsCurve` and no `Helix`; no closest-point query, split, curvature, planarity test
  or NURBS conversion on the curve contract; no offset, projection or pull; and **no value
  equality on curves**, because two curves drawing the same path through different
  parameterisations are a tolerance question rather than an `Equals` question, and answering it
  wrongly by default is worse than not answering it. **`Curve.ClosestPoint` is no longer among
  them**: it waited for the ray caster and its BVH rather than getting a second implementation,
  and now that `E2-T15` exists it has arrived on top of it.
- **A general affine transform of an ellipse is refused, not approximated.** `TransformedBy`
  accepts similarities. A shear does take an ellipse to an ellipse, but recovering the new axes
  from the mapped conjugate pair is Rytz's construction, and doing it approximately would return
  a curve that is quietly the wrong shape. A non-uniform scale on a `Circle` is refused for the
  same reason — the answer is an ellipse, and a `Circle` cannot hold one.
- **A node drawn later covers an open result strip.** Nodes win over previews when they overlap,
  because the graph is the document and the strip is a readout of it. The alternative — previews
  floating over nodes — hides the thing being worked on in order to show a readout of it. Move
  the node if the strip is in the way. Design language §7.6.2.
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
- **`concepts.evaluation` had no file behind it, and the harness could not have known.**
  *Fixed.* Five diagnostic codes resolved to it from M0 and the topic did not exist, so a user
  following an `SPK101x` code landed nowhere. **Nothing was broken in any way a test could
  see** — the id was a well-formed string, the codes were registered, and every topic that did
  exist passed its front-matter check; the gap was between two things nobody was comparing. The
  topic is written, and the harness gained three checks: a topic id in the source must name a
  real topic, a `related` id must too, and no two topics may share an id. All three were proven
  to fire. The second found a second dangling reference immediately — `concepts.lacing` related
  to `concepts.lists`, and lacing *is* the topic that covers lists.
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
