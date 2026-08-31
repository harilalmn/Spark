<#
.SYNOPSIS
    Builds spark_occt — Spark's C ABI over OpenCascade — and stages it for the managed build.

.DESCRIPTION
    ADR-0020 takes OpenCascade as the solid-modelling kernel and reaches it through a C shim we
    own. This script is the whole of "how do I build that", and it is a script rather than a
    paragraph in a document because a paragraph goes stale and a script fails loudly.

    It needs, once:

        vcpkg install opencascade:x64-windows

    which takes a long time and is why this script does not do it for you: a build that silently
    starts a two-hour dependency compile is a build that looks hung. If OpenCascade is missing the
    script says exactly that and stops.

    Output lands in artifacts/native/win-x64/, which is gitignored. Nothing native is ever
    committed — see scripts/check-no-native-binaries.sh and NFR-5.

.PARAMETER Configuration
    Release (the default) or Debug.

.PARAMETER VcpkgRoot
    Where vcpkg lives. Defaults to $env:VCPKG_ROOT, then C:\dev\vcpkg, then C:\vcpkg.

.PARAMETER SkipTest
    Build but do not run the smoke test.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [string] $VcpkgRoot,
    [switch] $SkipTest
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'native\spark_occt'
$build = Join-Path $repo "artifacts\native\build-$($Configuration.ToLowerInvariant())"
$stage = Join-Path $repo 'artifacts\native\win-x64'

# ------------------------------------------------------------------------------------------------
# Find vcpkg, and be specific about what is missing rather than failing inside CMake.
# ------------------------------------------------------------------------------------------------

$candidates = @($env:VCPKG_ROOT, 'C:\dev\vcpkg', 'C:\vcpkg') | Where-Object { $_ }

function Test-Occt([string] $root) {
    if (-not $root) { return $false }
    return Test-Path (Join-Path $root 'installed\x64-windows\share\opencascade\OpenCASCADEConfig.cmake')
}

if (-not $VcpkgRoot) {
    # The one that HAS OpenCascade wins, not the first one that exists. A Visual Studio developer
    # prompt sets VCPKG_ROOT to the copy bundled with Visual Studio, which is a real vcpkg with
    # nothing installed in it - so preferring the first hit finds an empty tree and reports a
    # library missing that is in fact sitting in the other one.
    $VcpkgRoot = $candidates | Where-Object { Test-Occt $_ } | Select-Object -First 1

    if (-not $VcpkgRoot) {
        $VcpkgRoot = $candidates |
            Where-Object { Test-Path (Join-Path $_ 'vcpkg.exe') } |
            Select-Object -First 1
    }
}

if (-not $VcpkgRoot -or -not (Test-Path (Join-Path $VcpkgRoot 'vcpkg.exe'))) {
    throw "vcpkg was not found. Set VCPKG_ROOT or pass -VcpkgRoot. Looked in: $($candidates -join ', ')"
}

$installed = Join-Path $VcpkgRoot 'installed\x64-windows'

if (-not (Test-Occt $VcpkgRoot)) {
    throw @"
OpenCascade is not installed in $VcpkgRoot. Run this once, and expect it to take a while:

    & '$VcpkgRoot\vcpkg.exe' install opencascade:x64-windows

Then run this script again. Roots searched: $($candidates -join ', ').
"@
}

$toolchain = Join-Path $VcpkgRoot 'scripts\buildsystems\vcpkg.cmake'

foreach ($tool in 'cmake', 'ninja') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool is not on PATH. Open a Visual Studio developer prompt, or install $tool."
    }
}

# ------------------------------------------------------------------------------------------------
# Configure and build
#
# Manifest mode is OFF and VCPKG_INSTALLED_DIR points at the classic tree. The manifest in
# native/spark_occt/vcpkg.json is still the record of what version we build against — ADR-0020
# asks for a pinned one — but resolving it here would start a second OpenCascade build beside the
# one already on the machine, which costs two hours to arrive at the same library.
# ------------------------------------------------------------------------------------------------

Write-Host "==> Configuring $Configuration in $build"

$arguments = @(
    '-S', $source,
    '-B', $build,
    '-G', 'Ninja',
    "-DCMAKE_BUILD_TYPE=$Configuration",
    "-DCMAKE_TOOLCHAIN_FILE=$toolchain",
    '-DVCPKG_MANIFEST_MODE=OFF',
    "-DVCPKG_INSTALLED_DIR=$(Join-Path $VcpkgRoot 'installed')",
    '-DVCPKG_TARGET_TRIPLET=x64-windows'
)

& cmake @arguments
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed with $LASTEXITCODE." }

Write-Host "==> Building"
& cmake --build $build
if ($LASTEXITCODE -ne 0) { throw "cmake build failed with $LASTEXITCODE." }

# ------------------------------------------------------------------------------------------------
# Stage
#
# The shim, and every OpenCascade DLL it needs beside it. Copying the whole bin directory is
# blunt and is the right blunt: the alternative is a hand-maintained list of transitive native
# dependencies, which is a list that is wrong the first time OpenCascade adds one.
# ------------------------------------------------------------------------------------------------

Write-Host "==> Staging into $stage"

if (-not (Test-Path $stage)) {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
}

Copy-Item (Join-Path $build 'spark_occt.dll') $stage -Force

$binaries = Join-Path $installed 'bin'
if (Test-Path $binaries) {
    Copy-Item (Join-Path $binaries '*.dll') $stage -Force
}

$count = (Get-ChildItem $stage -Filter *.dll).Count
Write-Host "    $count DLLs staged."

# ------------------------------------------------------------------------------------------------
# Test
# ------------------------------------------------------------------------------------------------

if (-not $SkipTest) {
    Write-Host "==> Smoke test"

    $smoke = Join-Path $build 'spark_occt_smoke.exe'
    $env:PATH = "$binaries;$build;$env:PATH"

    & $smoke
    if ($LASTEXITCODE -ne 0) { throw "The smoke test failed with $LASTEXITCODE." }
}

Write-Host "Done. spark_occt is in $stage"
