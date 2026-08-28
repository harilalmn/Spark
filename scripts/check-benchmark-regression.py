#!/usr/bin/env python3
"""Compare a BenchmarkDotNet run against the committed baseline and fail on a regression.

WHAT THIS GUARDS, AND WHY IT IS ALLOCATION RATHER THAN TIME.

A nightly runs on a shared virtual machine with no fixed clock speed and no idea what else is on
the host. Timings there move for reasons nobody can act on, and a gate that fails for reasons
nobody can act on is a gate people learn to ignore - which is worse than no gate, because it
discredits the ones that mean something.

Bytes allocated per operation do not have that problem. Allocation is a property of the code and
not of the machine: the same build allocates the same bytes on a laptop and on a datacentre VM.
So the hard gate here is allocation, and it is set tight. Timings are recorded in the artifacts
for a human to read and are deliberately not gated.

The decision and the alternatives it beat are ADR-0023.

This is the standing guard on E4-T3, whose first run found the return path allocating roughly six
times the argument path at 100 000 elements. That figure is now a ceiling rather than an
observation: if somebody makes it worse, this fails.

Python rather than jq so that it can be run - and proven to fire - on a developer machine as well
as on a runner. A gate first exercised in CI is a gate nobody has tested.

Usage: check-benchmark-regression.py RESULTS_DIR BASELINE_JSON
"""

from __future__ import annotations

import json
import pathlib
import sys


def load_reports(results_dir: pathlib.Path) -> list[pathlib.Path]:
    """The compressed full report, one file per benchmark type.

    Everything else BenchmarkDotNet writes to results/ - markdown, CSV, the log - is for humans.
    """
    return sorted(results_dir.glob("*-report-full-compressed.json"))


def case_key(benchmark: dict) -> str:
    """Type.Method(Parameters) - what a person reading the results table sees."""
    return "{}.{}({})".format(
        benchmark.get("Type", "?"),
        benchmark.get("Method", "?"),
        benchmark.get("Parameters") or "",
    )


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        print(__doc__.strip().splitlines()[-1], file=sys.stderr)
        return 2

    results_dir = pathlib.Path(argv[1])
    baseline_file = pathlib.Path(argv[2])

    if not results_dir.is_dir():
        print(f"error: results directory '{results_dir}' does not exist.", file=sys.stderr)
        print(
            "BenchmarkDotNet writes to BenchmarkDotNet.Artifacts/results; "
            "was the run given '--exporters json'?",
            file=sys.stderr,
        )
        return 2

    if not baseline_file.is_file():
        print(f"error: baseline '{baseline_file}' does not exist.", file=sys.stderr)
        return 2

    reports = load_reports(results_dir)
    if not reports:
        print(f"error: no BenchmarkDotNet JSON reports found under '{results_dir}'.", file=sys.stderr)
        print("A run that produced no reports is a failed run, not a clean one.", file=sys.stderr)
        return 2

    baseline = json.loads(baseline_file.read_text(encoding="utf-8"))
    expected_all = baseline.get("allocatedBytes", {})
    tolerance = baseline.get("allocationTolerance", 0.05)

    print(f"Baseline:  {baseline_file}")
    print(f"Tolerance: allocation may exceed the baseline by {tolerance * 100:g}%")
    print()

    failures: list[str] = []
    unguarded: list[str] = []
    checked = 0
    seen: set[str] = set()

    for report in reports:
        document = json.loads(report.read_text(encoding="utf-8"))
        for benchmark in document.get("Benchmarks", []):
            key = case_key(benchmark)
            seen.add(key)
            allocated = (benchmark.get("Memory") or {}).get("BytesAllocatedPerOperation")

            if key not in expected_all:
                print(f"UNGUARDED  {key}")
                print(f"           allocates {allocated} B/op and has no baseline entry.")
                unguarded.append(key)
                continue

            expected = expected_all[key]

            if allocated is None:
                print(f"NO MEMORY  {key}")
                print(f"           the baseline expects {expected} B/op but the report carries no")
                print("           memory figures. Is [MemoryDiagnoser] still on this class?")
                failures.append(key)
                continue

            checked += 1
            ceiling = int(expected * (1 + tolerance))

            if allocated > ceiling:
                print(f"REGRESSED  {key}")
                print(f"           {allocated} B/op against a baseline of {expected} B/op "
                      f"(ceiling {ceiling}).")
                failures.append(key)
            else:
                print(f"ok         {key}")
                print(f"           {allocated} B/op against a baseline of {expected} B/op.")

    print()
    print(f"{checked} benchmark(s) checked against the baseline.")

    # A baseline entry with no benchmark behind it is the mirror of an unguarded benchmark: it
    # reads as coverage and is not. Report it, but do not fail - deleting a benchmark is a
    # legitimate thing to do and the tidy-up belongs in the same change, not in a nightly.
    stale = sorted(set(expected_all) - seen)
    if stale:
        print()
        print(f"note: {len(stale)} baseline entr(y/ies) matched no benchmark in this run:")
        for key in stale:
            print(f"  {key}")
        print("If those benchmarks were removed deliberately, remove their baseline entries too.")

    if unguarded:
        print()
        print(f"::error::{len(unguarded)} benchmark(s) have no baseline entry and are not guarded.")
        print(f"Add them to {baseline_file} using the figures from this run's artifacts. A")
        print("benchmark running without a baseline is a number nobody is watching, which is what")
        print("this job exists to prevent - so it is a failure rather than a warning.")

    if failures or unguarded:
        print()
        print(f"::error::{len(failures) + len(unguarded)} benchmark check(s) failed.")
        return 1

    print("No allocation regressions.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
