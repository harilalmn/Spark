#!/usr/bin/env bash
#
# Run every test project by executing it, rather than through `dotnet test`.
#
# Under Microsoft.Testing.Platform a test project IS an executable that hosts its own
# runner (NOTES N11), so this script is not a workaround for a shape the platform does
# not support - it is the platform's own shape, driven directly.
#
# It exists because `dotnet test` interposes a server protocol between the SDK and that
# executable (`--server dotnettestcli --dotnet-test-pipe ...`), and that handshake fails
# silently on some SDK feature bands: every project reports "Zero tests ran" and exit
# code 5 while the same binaries, run directly, discover and pass everything. NOTES N34
# records the diagnosis. `dotnet test Spark.slnx` stays the documented gate; this is the
# second opinion that tells you whether a red run is the code or the toolchain, and the
# two must agree before a claim about the suite is worth making.
#
# Usage: scripts/run-tests.sh [-c CONFIGURATION] [--no-build]
#
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
configuration="Debug"
build=1

while [ $# -gt 0 ]; do
    case "$1" in
        -c|--configuration) configuration="$2"; shift 2 ;;
        --no-build) build=0; shift ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

if [ "$build" -eq 1 ]; then
    echo "Building $configuration ..."
    if ! dotnet build "$root/Spark.slnx" -c "$configuration" -v q --nologo; then
        echo "BUILD FAILED" >&2
        exit 1
    fi
fi

total=0
failed=0
projects_run=0
projects_failed=()

for project in "$root"/tests/*/; do
    name="$(basename "$project")"
    [ -f "$project/$name.csproj" ] || continue

    assembly="$project/bin/$configuration/net10.0/$name.dll"
    if [ ! -f "$assembly" ]; then
        echo "MISSING  $name — no assembly at $assembly" >&2
        projects_failed+=("$name (not built)")
        failed=$((failed + 1))
        continue
    fi

    output="$(cd "$root" && dotnet exec "$assembly" 2>&1)"
    status=$?
    projects_run=$((projects_run + 1))

    # The platform's own summary line is the only thing worth parsing: it is stable,
    # and re-deriving the counts from the per-test lines would invent a second source
    # of truth for a number the runner already knows.
    summary="$(printf '%s\n' "$output" | grep -E '^\s+'"$name"'\s+Total:' | tail -1)"
    if [ -n "$summary" ]; then
        count="$(printf '%s' "$summary" | sed -E 's/.*Total: ([0-9]+).*/\1/')"
        fails="$(printf '%s' "$summary" | sed -E 's/.*Failed: ([0-9]+).*/\1/')"
        total=$((total + count))
        failed=$((failed + fails))
        printf '%-32s %s\n' "$name" "${summary#*Total:}" | sed 's/^\(.\{32\}\) /\1 Total:/'
    else
        printf '%-32s no summary line — see output below\n' "$name"
        printf '%s\n' "$output" | tail -20
    fi

    if [ $status -ne 0 ]; then
        projects_failed+=("$name")
        printf '%s\n' "$output" | grep -E '\[FAIL\]|error' | head -20
    fi
done

echo
echo "-----------------------------------------------------------------"
printf '%d test projects, %d tests, %d failed\n' "$projects_run" "$total" "$failed"

if [ ${#projects_failed[@]} -ne 0 ]; then
    printf 'FAILED: %s\n' "${projects_failed[*]}"
    exit 1
fi

exit 0
