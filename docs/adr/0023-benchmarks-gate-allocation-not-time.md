# ADR-0023 — The nightly benchmark gates bytes allocated, not elapsed time

**Status:** Accepted
**Date:** 2026-08-28
**Deciders:** Nicety

## Context

`bench/Spark.Benchmarks` has existed since `ab2f37e` and nothing ran it (`E1-T13`). A benchmark
nobody runs measures rather than guards: it answers the day you ask and stays silent on the day
somebody makes it worse. Three rows in the register were waiting on that schedule — `E1-T21` for
the workflow itself, and the standing halves of `E4-T3` and `E8-T15`.

Two facts about the ground shaped the answer.

**The runner is not a laboratory.** A nightly runs on a shared virtual machine with no fixed
clock speed, unknown neighbours on the host, and no GPU. Elapsed time there moves for reasons
that have nothing to do with this repository. The register already knew half of this — `E1-T21`
said "nightly, not per-PR — shared runners are too noisy to compare per-PR" — but nightly does
not make a noisy machine quiet, it only compares against a more distant commit.

**Local noise was measured before a threshold was proposed, not after.** Four consecutive runs of
`--canvas-benchmark 600` on one quiet developer machine gave medians of 1.04–1.25 ms — a 20%
spread — against p95s of 3.04–3.24 ms, a 6% spread. That is the *floor* on the noise a hosted
runner can be expected to beat, and it is already wide enough that any honest timing threshold
would have to sit far enough out to miss most real regressions.

Preparing the gate also found that the number about to be gated was wrong: the canvas benchmark
reported a frame count it had not measured over ([N31](../NOTES.md)). That is context rather than
cause, but it sharpened the question — a threshold is only as good as the statistic under it.

## Decision

**The nightly gates bytes allocated per operation. It gates nothing else.**

- Allocation is compared against ceilings committed in `bench/baseline.json`, with a 5% tolerance,
  by `scripts/check-benchmark-regression.py`. Exceeding a ceiling fails the job.
- **A benchmark with no baseline entry also fails the job.** A benchmark running unguarded is a
  number nobody is watching, which is the condition this workflow exists to end; discovering it
  silently would reproduce that condition inside the fix for it.
- Timings are exported, published to 90-day artifacts and written into the run summary. They are
  read by people and enforced by nothing.
- The canvas benchmark runs on `windows-latest` and is recorded, never gated, and cannot fail the
  workflow. Setting its p95 threshold from observed data is `E1-T34`.

The reason allocation works where time does not is that **allocation is a property of the code and
not of the host.** The same build allocates the same bytes on a laptop and in a datacentre. It is
the one figure a shared runner cannot move, which makes it the only one worth failing on — and it
is not a consolation prize: the finding that prompted `E4-T3`'s standing guard was an allocation
finding, that the return path allocates roughly six times the argument path at 100 000 elements.

## Alternatives considered

### A timing threshold, set generously

The obvious reading of "turn three numbers into three guards", and what the register implied.

It lost on a specific arithmetic. To survive a hosted runner it would have to tolerate at least
the 20% spread seen on a *quiet* machine, and realistically much more. A threshold that loose
passes almost every regression worth catching, so it buys a red light for host weather and little
else. And the cost of that is not zero but negative: a job that fails for reasons nobody can act
on trains people to override it, and the habit does not stay confined to the job that taught it.
This project has written the same sentence about gates from three other directions already.

### Commit the results to the repository as a git time series

**This is what `E1-T21` actually asked for, and it was deliberately not built.** A history of
numbers is genuinely useful — trends catch the slow regressions a threshold never trips on.

It lost on what it costs to have. The workflow would need `contents: write` and would push to
`main` on a schedule, which is a standing grant of write access to the default branch bought for
a convenience, and a permanent source of commits no human wrote. The 90-day artifacts carry the
same numbers for anyone who wants to plot them, and `bench/baseline.json` — updated by a person,
on purpose, in a reviewable diff — carries the part that has to be durable. If a trend line is
later wanted badly enough to pay for it, the artifacts are the input and this record is the thing
to supersede.

### Gate nothing; publish the numbers and rely on people reading them

Honest, and it is what the canvas half does for now, for a reason particular to it. As a whole
policy it is the status quo the workflow was written to end: `E4-T3` sat `In progress` for exactly
this, and nobody reads a nightly artifact on the morning that matters.

### Run the check with `jq` rather than Python

`jq` is on the hosted image, so this was nearly free. It lost for one reason: `jq` is not on the
maintainer's machine, so the gate could not have been exercised before it was trusted. All five of
the script's paths — regression, unguarded benchmark, absent `[MemoryDiagnoser]`, stale baseline
entry, empty results directory — were run against synthetic reports before the workflow was
committed. A gate first exercised in CI is a gate nobody has tested, and this repository has an
explicit standard about that.

## Consequences

- **Adding a benchmark now means adding its ceiling in the same change**, or the nightly goes red.
  That is a deliberate friction and it is recorded in `AGENTS.md`.
- **The nightly and the baseline both use `--job short`, chosen for runtime.** A full-fidelity
  run over these three suites takes over an hour on a developer machine — the marshalling suite's
  100 000-element cases dominate it — and a hosted runner is slower again. The precision a full
  run buys is precision in the *timings*, which this job does not gate.
- **Keeping the baseline on the same job config is a precaution, not a known correction.** The
  concern was that bytes-per-operation is total bytes over operation count, so a config with far
  fewer operations might amortise one-time allocations differently. **It was checked and it did
  not happen**: the four `EvaluationBenchmarks` cases were measured under both configs and came
  back byte-identical (1 593 696 / 103 296 / 16 155 032 / 1 045 544). Allocation here looks to be
  genuinely per-operation. Regenerate with `--job short` anyway — it costs nothing and removes a
  variable — but a full-run figure is not expected to be wrong, and if the two ever disagree that
  disagreement is itself worth understanding rather than papering over.
- **The baseline is machine-independent but not runtime-independent.** A .NET SDK bump can move
  allocation legitimately. The expected response is to re-measure and update `bench/baseline.json`
  in the same change as the bump, not to widen the tolerance.
- **Slow drift is not caught.** A regression of 4% a night indefinitely stays under a 5% ceiling
  forever. That is the real cost of rejecting the time series, and it is accepted knowingly.
- **The workflow has never run.** Everything above is true of what is written, and none of it is
  evidence until a nightly is green — the distinction `E1-T14` established for CI generally.
