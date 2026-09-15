# `tests/corpus/` — the fixtures that are data rather than code

**Status:** Current.
**Owner:** `test-engineer`
**Last updated:** 2026-09-15 (the thirteen help-renderer goldens, `E11-T7`)

This directory holds inputs the suites read: files that are checked in, compared against, and
read by tests that live elsewhere. It is **deliberately not an MSBuild project**, which
`Spark.Architecture.Tests.SolutionMembershipTests` asserts — it is data, not code.

## What it is for, since two documents said two different things

[CONTRIBUTING.md](../../CONTRIBUTING.md) says *regression tests go in `tests/corpus/` and stay
there; the corpus grows with every bug found.* [AGENTS.md](../../AGENTS.md) says every migration
ships *with a golden-file test against a real old-version graph in `tests/corpus/`*. **Both are
true, neither mentioned the other, and `AGENTS.md` additionally said this directory did not
exist.** It holds four kinds of thing:

1. **Goldens** — a committed expected result. The check compares and, on failure, prints something
   readable and writes what it actually produced beside the golden.
2. **Real old-version artefacts** — files a previous build genuinely wrote, kept so that
   compatibility is tested against history rather than against a fixture somebody typed today.
3. **Manifests** — tabular claims about the outside world, with a `#` provenance block, a header
   row the reader asserts literally, and a budget row where one applies.
4. **Failing inputs from fixed bugs** — the smallest input that reproduced a defect, kept so the
   defect stays fixed.

## Everything in here, and what reads it

| Path | Kind | Read by | Where it came from |
|---|---|---|---|
| `dynamo-parity.tsv` | Manifest | `Spark.Docs.Verify.DynamoParityChecks`, `NodeClaimChecks`, `ProgressDashboardChecks` | Enumerated from Dynamo's ProtoGeometry surface; carries the **residue budget**, asserted exactly |
| `dynamo-parity-exclusions.tsv` | Manifest | `Spark.Docs.Verify.DynamoParityChecks` | Members deliberately not ported, each with a reason |
| `epic-criterion-exemptions.tsv` | Manifest | `Spark.Docs.Verify.AcceptanceCriterionChecks` | The acceptance criteria in `EPICS.md` that are allowed to disagree with the register row they cite, each with a reason. Written out of the sweep of 2026-09-15, which found 22 disagreements among the 154 criteria citing exactly one row; 17 were the documents lagging and were ticked, five are here |
| `viewport/reference-scene.png` | Golden | `Spark.Viewport.Tests.VisualRegressionTests` | Rendered by the software rasteriser on Windows (`E9-T12`). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `geometry/closed-cube.tsv` | Golden | `Spark.Geometry.Tests.GoldenGeometryTests` | A hand-built closed cube — shared vertices, area 24, volume 8 |
| `geometry/cuboid.tsv` | Golden | `Spark.Geometry.Tests.GoldenGeometryTests` | `MeshPrimitives.Cuboid`, which is **not closed**: twenty-four vertices for eight corners |
| `geometry/plane-grid.tsv` | Golden | `Spark.Geometry.Tests.GoldenGeometryTests` | `MeshPrimitives.Plane`, an open mesh |
| `geometry/cubic-nurbs.tsv` | Golden | `Spark.Geometry.Tests.GoldenGeometryTests` | A degree-3 curve from explicit control points |
| `geometry/rational-nurbs.tsv` | Golden | `Spark.Geometry.Tests.GoldenGeometryTests` | Weights of 1 and ½ — exactly representable, so the hash is stable |
| `geometry/polyline.tsv` | Golden | `Spark.Geometry.Tests.GoldenGeometryTests` | A closed polyline; the sampled branch of the hash |
| `geometry/ruled-surface.tsv` | Golden | `Spark.Geometry.Tests.GoldenGeometryTests` | A ruled surface between two polylines |
| `help/code-blocks.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/code-blocks.md` (The longest topic in the corpus at 170 parts, and the one with the most fenced code). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `help/command-line.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/command-line.md` (The CLI topic; tables of verbs and flags). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `help/curves.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/curves.md` (Curve concepts; the geometry topic with the most inline code). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `help/design-language.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/design-language.md` (The naming and convention topic). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `help/evaluation.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/evaluation.md` (How a graph runs; heavily cross-linked). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `help/files.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/files.md` (Saving, loading and the format). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `help/finding-nodes.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/finding-nodes.md` (The library panel and search). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `help/geometry-basics.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/geometry-basics.md` (Points, vectors and frames). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `help/lacing.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/lacing.md` (286 blocks — the largest topic, and the executable specification the case table lives in). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `help/lists.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/lists.md` (Lists and ranks). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `help/solids.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/solids.md` (Solids and the kernel seam). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `help/undo.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/undo.md` (Undo and redo). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `help/workspace.tsv` | Golden | `Spark.Engine.Tests.HelpCorpusGoldenTests` | What `HelpMarkdown` makes of `docs/help/concepts/workspace.md` (The shell, docking and the canvas). Regenerate with `SPARK_UPDATE_GOLDEN=1` |
| `graphs/curves-2026-08-28.spark` | Real old-version artefact | `Spark.Engine.Tests.CorpusGraphTests` | `docs/examples/curves.spark` **exactly as it stood at `a30e98c`**, 2026-08-28 |

## The August graph, and why it is not a fixture somebody typed

`graphs/curves-2026-08-28.spark` is the reason this directory exists in the form `AGENTS.md`
describes. **Eight of its eleven node keys no longer exist** — `Circle.ByCentreRadius`,
`Colour.ByRgb`, `Display.ByGeometryColour`, `Ellipse.ByPlaneRadii`, `Plane.ByOriginNormal`,
`Point.ByCoordinates` and `PolyLine.ByRegularPolygon`, renamed by `E2-T58` (`By` → `From`) and
`E2-T60` (`Centre` → `Center`).

`NodeAliasTests` already proved the alias *mechanism*, against a synthetic library with one
synthetic renamed definition and a JSON string typed inside the test. **That cannot show that the
real library's aliases cover the renames this project actually made.** This file can, because an
older build wrote it.

**It is never regenerated.** A test that rewrote its own fixture would pass forever and prove
nothing after the first run, so `CorpusGraphTests` asserts the file on disk still names the old
keys after a save-round-trip.

## Adding something

- **Say where it came from.** A fixture with no provenance cannot be judged, regenerated, or
  retired. For a golden, name the command. For a captured artefact, name the commit.
- **Name it in the table above.** `Spark.Docs.Verify.CorpusIndexChecks` fails when a file here is
  not listed — the corpus's failure mode is not a wrong fixture, it is a fixture nobody remembers
  the purpose of.
- **Never regenerate a captured artefact.** Goldens are regenerated deliberately and visibly;
  history is not.
- **Keep it small.** These files are read on every test run.
