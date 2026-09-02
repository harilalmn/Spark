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

.PARAMETER CheckToolchain
    Find and report the C++ toolchain, then exit without building. Does not need OpenCascade, and
    that is the point: CI runs it BEFORE the hour-long dependency install, so a machine with no
    MSVC on it says so at second zero rather than after the install has been paid for.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [string] $VcpkgRoot,
    [switch] $SkipTest,
    [switch] $CheckToolchain
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'native\spark_occt'
$build = Join-Path $repo "artifacts\native\build-$($Configuration.ToLowerInvariant())"
$stage = Join-Path $repo 'artifacts\native\win-x64'

# ------------------------------------------------------------------------------------------------
# Find the C++ toolchain, and make sure it is the RIGHT one
#
# THIS SECTION EXISTS BECAUSE v0.1.1 FAILED, AND SEVEN CI RUNS FAILED BEHIND IT.
#
# vcpkg's x64-windows triplet is MSVC. Every .lib under installed/x64-windows carries MSVC's C++
# name mangling, so only MSVC can link them. The check that used to live further down asked for
# `cmake` and `ninja`, and then told you to "open a Visual Studio developer prompt" if either was
# missing - it checked two of the three tools it needed and trusted the caller for the third.
#
# A hosted GitHub runner has cmake and ninja on PATH, no developer environment, and MinGW sitting
# at C:\mingw64. So the check passed, CMake chose GNU g++, and the link died on every OpenCascade
# symbol at once:
#
#     undefined reference to `OSD::SetSignal(OSD_SignalMode, bool)'
#
# against a TKernel.lib that contains it, under a name GNU ld does not spell the same way. The
# libraries were on the link line throughout, which is exactly why this reads like a missing
# target_link_libraries and is nothing of the kind.
#
# So the script enters the MSVC environment itself rather than requiring that the caller did. It
# then behaves identically from a developer prompt, a plain PowerShell window and a hosted runner,
# which is the only version of this that cannot drift apart again.
#
# It runs BEFORE the vcpkg block on purpose: -CheckToolchain has to work on a machine that has no
# OpenCascade yet, because that is the machine CI is when it runs the preflight.
# ------------------------------------------------------------------------------------------------

function Import-MsvcEnvironment {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { return $false }

    $install = & $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath

    if (-not $install) { return $false }

    $vcvars = Join-Path $install 'VC\Auxiliary\Build\vcvars64.bat'
    if (-not (Test-Path $vcvars)) { return $false }

    # `cmd /c "call <bat> && set"` is the only supported way to find out what a vcvars script did:
    # it edits its own process environment, and there is nothing to import from PowerShell. Split
    # on the FIRST `=` only - PATH is full of them.
    & cmd.exe /c "call `"$vcvars`" >nul 2>&1 && set" | ForEach-Object {
        $name, $value = $_ -split '=', 2
        if ($name -and $null -ne $value) { Set-Item -Path "Env:$name" -Value $value }
    }

    return [bool](Get-Command cl -ErrorAction SilentlyContinue)
}

if (-not (Get-Command cl -ErrorAction SilentlyContinue)) {
    Write-Host '==> No cl.exe on PATH. Entering the MSVC environment.'

    if (-not (Import-MsvcEnvironment)) {
        throw @'
No MSVC C++ toolchain was found, and vcpkg's x64-windows libraries can only be linked by MSVC.

Install "Desktop development with C++" from the Visual Studio Installer, or run this from a
Visual Studio developer prompt. MinGW will not do: it configures and compiles happily, then
fails to resolve every OpenCascade symbol, because the two compilers mangle C++ names
differently.
'@
    }
}

foreach ($tool in 'cmake', 'ninja', 'cl') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool is not on PATH. Open a Visual Studio developer prompt, or install $tool."
    }
}

Write-Host "==> C++ toolchain: $((Get-Command cl).Source)"

if ($CheckToolchain) {
    Write-Host 'Toolchain only. Nothing was built.'
    return
}

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
    # Named rather than left to CMake's search order. Entering the MSVC environment above is what
    # makes cl available; saying so here is what stops a MinGW earlier on PATH from being chosen
    # anyway. Belt and braces, and the braces cost three hours the day they were missing.
    '-DCMAKE_C_COMPILER=cl',
    '-DCMAKE_CXX_COMPILER=cl',
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
# The build key
#
# R22: a standing source offer has to be honourable against a SPECIFIC artefact, not approximately.
# The key records what this DLL was built from, beside the DLL, so an offer made a year from now
# resolves to an exact OpenCascade tag, an exact vcpkg baseline and an exact shim commit. Writing it
# here rather than at release time is E13-T16's whole point: an obligation met by the pipeline
# rather than by remembering.
# ------------------------------------------------------------------------------------------------

Write-Host "==> Recording the build key"

$manifest = Get-Content (Join-Path $source 'vcpkg.json') -Raw | ConvertFrom-Json

# The shim's own hash: every source file that goes into the DLL, in a fixed order, hashed together.
# It is not a git commit on purpose - an uncommitted edit must change the key, because it changes
# the artefact.
$sources = Get-ChildItem -Path (Join-Path $source 'include'), (Join-Path $source 'src') -File |
    Sort-Object -Property Name

$combined = New-Object System.Text.StringBuilder
foreach ($file in $sources) {
    [void]$combined.Append((Get-FileHash -Algorithm SHA256 -Path $file.FullName).Hash)
}

$bytes = [System.Text.Encoding]::UTF8.GetBytes($combined.ToString())
$stream = New-Object System.IO.MemoryStream(, $bytes)
$shimHash = (Get-FileHash -Algorithm SHA256 -InputStream $stream).Hash

$commit = ''
try { $commit = (& git -C $repo rev-parse HEAD 2>$null) } catch { }

$key = [ordered]@{
    rid              = 'win-x64'
    configuration    = $Configuration
    occtVersion      = ($manifest.overrides | Where-Object { $_.name -eq 'opencascade' }).version
    vcpkgBaseline    = $manifest.'builtin-baseline'
    vcpkgRoot        = $VcpkgRoot
    shimSourceHash   = $shimHash
    shimSourceFiles  = @($sources | ForEach-Object { $_.Name })
    sparkCommit      = $commit
    builtUtc         = (Get-Date).ToUniversalTime().ToString('o')
    licences         = @{
        opencascade = 'LGPL-2.1-only WITH Open CASCADE exception; texts in licences/'
        sparkOcct   = 'MIT'
    }
    notices          = 'THIRD-PARTY-NOTICES.md'
}

$key | ConvertTo-Json -Depth 5 |
    Out-File -FilePath (Join-Path $stage 'spark_occt.buildkey.json') -Encoding utf8

Write-Host "    shim source hash $($shimHash.Substring(0, 16))..., OpenCascade $($key.occtVersion)."

# The notices and the licence texts travel with the binaries they are about. A notice file left
# behind in a source tree is a notice nobody who received the software can read.
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $stage -Force

$licences = Join-Path $stage 'licences'
if (-not (Test-Path $licences)) {
    New-Item -ItemType Directory -Path $licences -Force | Out-Null
}

Copy-Item (Join-Path $repo 'licences\*') $licences -Force

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
