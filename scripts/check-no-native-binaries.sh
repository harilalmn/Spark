#!/usr/bin/env bash
#
# NFR-5: Spark.Geometry's published output contains no native binaries.
#
# Spark exists because Dynamo Sandbox forces a heavyweight dependency on its users, and
# ADR-0020 has since committed the *application* to shipping one of its own — OpenCascade,
# in Spark.Geometry.Occt. What survives that decision unchanged is this: the managed
# geometry kernel stays pure managed and independently distributable. A promise nobody
# checks is a preference, and the window for adding this check closes at M1.6, which is the
# commit that first puts native binaries in the tree. A gate added after the thing it guards
# against is a gate that never guarded.
#
# Usage: scripts/check-no-native-binaries.sh [project ...]
#
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
projects=("$@")

if [ ${#projects[@]} -eq 0 ]; then
    # The projects the promise names. Spark.Geometry.Occt is deliberately absent: it is the
    # one project that is *supposed* to carry native binaries (ADR-0020).
    projects=("src/Spark.Geometry/Spark.Geometry.csproj")
fi

# Extensions that are native code on some platform we care about. `.dll` is not here and
# cannot be: on Windows a managed assembly is a .dll too, so that case is caught below by
# what the publish manifest says rather than by what a file is called.
native_globs=(-name '*.so' -o -name '*.so.*' -o -name '*.dylib' -o -name '*.a' -o -name '*.lib'
              -o -name '*.node' -o -name '*.pyd' -o -name '*.exp' -o -name '*.o')

status=0

for project in "${projects[@]}"; do
    name="$(basename "$project" .csproj)"
    out="$(mktemp -d)"
    trap 'rm -rf "$out"' EXIT

    echo "==> Publishing $name"
    dotnet publish "$root/$project" --configuration Release --output "$out" --nologo --verbosity quiet

    echo "    Published files:"
    (cd "$out" && find . -type f | sed 's|^\./|      |' | sort)

    # 1. A runtimes/ directory is where the SDK puts per-RID native assets. Its presence is
    #    the single clearest signal that something native arrived, and it arrives through a
    #    transitive package reference rather than through anything visible in the csproj.
    if [ -d "$out/runtimes" ]; then
        echo "::error::$name published a runtimes/ directory, which is where native assets land."
        (cd "$out" && find runtimes -type f | sed 's/^/      /')
        status=1
    fi

    # 2. Anything named like native code on any platform.
    found_native="$(cd "$out" && find . -type f \( "${native_globs[@]}" \) | sed 's|^\./||' || true)"
    if [ -n "$found_native" ]; then
        echo "::error::$name published files that are native binaries:"
        echo "$found_native" | sed 's/^/      /'
        status=1
    fi

    # 3. The publish manifest is the authoritative record of what the output contains, and it
    #    names native assets explicitly. This is what catches a native .dll on Windows, where
    #    the file name says nothing.
    deps="$out/$name.deps.json"
    if [ ! -f "$deps" ]; then
        echo "::error::$name published no $name.deps.json, so its dependencies cannot be checked."
        status=1
    elif grep -qE '"(native|runtimeTargets)"' "$deps"; then
        echo "::error::$name's deps.json declares native or per-RID assets:"
        grep -nE '"(native|runtimeTargets)"' "$deps" | sed 's/^/      /'
        status=1
    fi

    rm -rf "$out"
    trap - EXIT
done

if [ $status -eq 0 ]; then
    echo "No native binaries in the published output. NFR-5 holds."
fi

exit $status
