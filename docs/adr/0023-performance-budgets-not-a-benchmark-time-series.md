# ADR-0023 — Performance is guarded by committed budgets, not by a benchmark time series

**Status:** Accepted
**Date:** 2026-08-29
**Deciders:** Nicety

## Context

`bench/Spark.Benchmarks` has existed since `ab2f37e` with three suites — marshalling
(`E4-T3`'s standing guard), evaluation cold against warm, and the canvas spatial index — and the
application has carried its own `--canvas-benchmark` since M2. Every one of them was run before
its row was ticked, and two of the three were measuring the wrong thing until the numbers gave
them away ([N26](../NOTES.md)).

**Nothing ran any of them afterwards.** A number nobody compares to anything cannot go red, so
what the repository actually had was a report — read once, on the day it was written — filed under
the word *guard*. Three rows said so honestly rather than claiming otherwise: `E1-T21`, `E4-T3`
and `E8-T15`.

The plan of record, written at M0, was **"benchmarks run nightly, not per-PR, with results
committed as a git time series"** (`E1-T21`, [EPICS E1](../EPICS.md#e1--foundations-build-and-ci)).
The cadence half of that was right and is unchanged. The storage half was decided before anybody
had run these benchmarks on a shared machine, and it is the half this ADR revisits.

Two facts about the measurements decide most of it, and neither was known when the plan was
written:

- **Allocation is deterministic; wall-clock on a hosted runner is not.**
  `BytesAllocatedPerOperation` for a given build is the same number on a laptop and on a shared
  GitHub runner — it is a property of the code. The times are a property of the code *and* of a
  virtual machine of unknown vintage, unknown contention and unknown thermal state.
- **A ratio between two cases in the same run cancels the machine out.** *Warm evaluation costs a
  fraction of cold* is 27-fold on a developer machine ([N26](../NOTES.md)) and will be roughly
  27-fold anywhere, because both halves are measured on the same runner within seconds of each
  other. That is the provenance cache's central claim, and it turns out to be expressible as the
  one kind of number a hosted runner can be trusted for.

## Decision

**The nightly workflow runs the benchmarks and checks them against budgets committed in
`bench/budgets.jsonc`. It does not commit results.**

`.github/workflows/nightly.yml` runs the suites on Windows and Linux, drives the 2 000-node
canvas benchmark on Windows, and then runs `Spark.Benchmarks`' own `check` verb, which fails the
job when a budget is broken. The JSON reports are uploaded as build artifacts with thirty days'
retention, which is where *how far, and since when* is answered.

Three kinds of budget, held to three deliberately different standards:

1. **Allocation ceilings are the real guard**, set close to the measurement.
2. **Ratios between two cases are the sharpest guard**, because they survive a change of machine,
   and any claim expressible as a ratio should be written as one.
3. **Wall-clock ceilings catch a step change and nothing finer.** They are set an order of
   magnitude above the measurement, and the number they are allowed to catch is *an algorithm
   changed*, never *this got 20% slower*.

**A benchmark with no budget fails the check**, in the same way an unaccounted public member fails
the reflection importer's two-way diff (`E5-T4`). So does a budget with no benchmark. A guard that
quietly stops covering something is the failure mode this project has already met three times.

## Alternatives considered

### Results committed as a git time series — the plan of record

The attraction is real: a per-commit history of every number, diffable, with no artifact
retention limit and no external service.

It lost on three counts. **A scheduled job that pushes to `main` is a write-capable workflow**,
which is a materially larger security surface than `contents: read` and one that has to be got
right rather than merely written. **The series would be dominated by runner noise** — a hosted
runner varies by more than any regression worth catching, so the series records the fleet's mood
and hides the signal inside it. And **it decides nothing**: a time series still needs somebody to
look at it and form a judgement, which is exactly the failing that left three benchmarks
unwatched for a milestone. A budget makes the judgement once, writes it down where it is
reviewable, and then holds it without anybody's attention.

The idea is not discarded, and `E1-T21` keeps it: if a series is ever wanted, the artifacts the
nightly already keeps are its raw material, and the honest version of it is a median of several
runs rather than one.

### Benchmarks in the pull-request build

Fifteen to twenty minutes per operating system, against a full CI build that costs less than
that today. A gate that slow is bypassed rather than fixed, and per-PR numbers on shared runners
are noisy enough that the first three false failures would train everybody to ignore the fourth.
Nightly names the day something changed, which is what this class of guard is for.

### A hosted benchmark service

Bencher, Codspeed and their kind solve exactly this problem, with statistical thresholds and
instruction-count measurement that removes most of the noise. Rejected for now on dependency
grounds rather than technical ones: it is a third-party account, a token in the repository, and a
service in the critical path of a build, in exchange for something a committed JSON file and forty
lines of C# already do at this scale. Worth revisiting when the suites are ten times larger — and
worth revisiting *sooner* if the wall-clock ceilings prove to be the useless third of this scheme.

### Only allocation and ratios, with no wall-clock ceilings at all

Tempting, and defensible: they are the two trustworthy kinds. Rejected because a
step change in time with no change in allocation is a real and reachable regression — an
accidental O(n²) over a pre-allocated buffer allocates nothing extra — and a ceiling an order of
magnitude out still catches it. The rule that keeps this honest is that the loose ceilings are
**documented as loose**, in the budgets file itself, so nobody quotes them as precision.

## Consequences

### Positive

- Three measurements become three guards, and the day one of them changes has a name.
- The budgets file is a **reviewed** statement of what the project expects its own performance to
  be. Changing a budget is a diff with a reason attached, which is the same standard the
  public-API baselines set for the API surface ([ADR-0019](0019-deliberate-public-api-change-control.md)).
- The check runs identically on a developer machine and in CI, because it is a verb on the
  benchmark host rather than a shell script wrapped around `jq`.
- The Linux leg genuinely re-tests something rather than repeating it: the allocation half of
  every budget is machine-independent, so it holds there or the build is wrong.

### Negative

- **Wall-clock drift is not caught.** A change that makes marshalling 30% slower will pass, and
  the budgets file says so rather than implying otherwise. Catching drift needs either the hosted
  service or a self-hosted runner, and both are decisions for a later scale.
- The budgets need re-baselining whenever a change legitimately alters allocation. That is
  intended — it makes the change visible — but it is friction on exactly the commits that are
  already doing performance work.
- Artifact retention is thirty days, so *how far, and since when* has a horizon. A time series
  would not.

### Neutral

- The canvas benchmark is Windows-only in the nightly, because the Linux runner has no display
  server and driving Avalonia under `xvfb` would measure a software rasteriser nobody ships on.
- **The canvas step has never run on a hosted runner.** It has been proven to *detect*, not
  proven to *run*, and [N28](../NOTES.md) is the note about why those are different claims. Its
  first nightly execution is part of adding it, not a formality afterwards.

## Notes

The `check` verb lives in `Spark.Benchmarks` rather than in `scripts/` for a reason worth
recording: `scripts/check-no-native-binaries.sh` is bash, and the equivalent here would have
needed `jq` on both operating systems to read BenchmarkDotNet's JSON. A verb on a project that
already exists needs no new dependency, no new project, and no second implementation of "what
counts as broken".

Budget values in `bench/budgets.jsonc` were measured on a 13th-generation Intel Core i9-13900H
under Windows 11, on 2026-08-29, with the run that produced them recorded in the file's header
comment. The point of naming the machine is that it makes the wall-clock numbers legible as what
they are: one machine's figures, widened by an order of magnitude before anybody was allowed to
depend on them.
