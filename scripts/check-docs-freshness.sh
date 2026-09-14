#!/usr/bin/env bash
#
# The standing documentation instruction, enforced mechanically: a change to the public API
# surface or the node library must also touch a document.
#
# This lived inline in `.github/workflows/ci.yml` until 2026-09-14 and it was moved here for one
# reason: a gate written inside a workflow can only be exercised by triggering that workflow, and
# this one went seventeen days without firing while the register counted it as coverage
# (`E11-T14`). Here it can be run against any range on any machine, which is how the four cases
# below were proven rather than asserted. `ci.yml` calls this script and no longer contains a copy
# of the rule - a rule that exists in two places is two rules ([N102](../docs/NOTES.md)).
#
# The escape hatch is deliberate and deliberately loud: a `docs: none-needed` trailer in a commit
# message is visible in review, where a silent exemption would not be.
#
# Usage: scripts/check-docs-freshness.sh <git range>
#
#   scripts/check-docs-freshness.sh 'origin/main...HEAD'     # what a pull request compares
#   scripts/check-docs-freshness.sh 'HEAD~1..HEAD'           # what a single commit changed
#
# Exit 0 when the change is documented, exempted, or touches nothing that needs documenting.
# Exit 1 when it touches a contract and no document.
#
set -uo pipefail

if [ $# -ne 1 ]; then
    echo "usage: $(basename "$0") <git range>" >&2
    exit 2
fi

range="$1"

echo "Comparing $range"
changed=$(git diff --name-only "$range")

echo "Changed files:"
echo "$changed" | sed 's/^/  /'

# ANY CONTRACT SURFACE, NOT JUST THE NODE LIBRARY. An earlier version keyed only on
# Spark.Nodes and the API baselines, so the entire geometry kernel could change without
# tripping the gate - which is exactly what happened on its first real use.
needs_docs=$(echo "$changed" | grep -E '^(src/Spark\.(Api|Geometry|Geometry\.Io|Nodes\.Core)/.*\.cs|.*PublicAPI\.(Shipped|Unshipped)\.txt)$' || true)
touched_docs=$(echo "$changed" | grep -E '^(docs/|README\.md|AGENTS\.md)' || true)

if [ -n "$needs_docs" ] && [ -z "$touched_docs" ]; then
    if git log "$range" --format=%B | grep -qi '^docs: none-needed'; then
        echo "::notice::Documentation exemption claimed via 'docs: none-needed' trailer."
        exit 0
    fi

    echo "::error::This change touches the public API surface or the node library but no documentation."
    echo "Update the relevant documents, or add a 'docs: none-needed' trailer to a commit and say why."
    echo "Offending files:"
    echo "$needs_docs" | sed 's/^/  /'
    exit 1
fi

echo "Documentation freshness check passed."
