<#
.SYNOPSIS
    Stages a runnable Spark for win-x64: the application, the CLI, and the native provider.

.DESCRIPTION
    `E13-T17`. What this produces is a **folder that runs**, not an installer. The installer,
    the code signing and the antivirus submissions need an organisation with an identity to sign
    with, and a script cannot invent one — what it can do is make the payload reproducible and
    measured, so that the parts which do need a person start from a known quantity.

    It deliberately does NOT use `--self-contained`, `PublishSingleFile` or NativeAOT. That is a
    licence constraint rather than a preference: OpenCascade ships under LGPL-2.1 with the Open
    CASCADE exception, and the relink obligation needs the libraries to stay unmodified and
    replaceable. A single-file bundle that extracts to a temp directory does not obviously
    preserve that, and NativeAOT does not preserve it at all. See THIRD-PARTY-NOTICES.md and
    `E12-T8`. **Nothing here is legal advice.**

.PARAMETER Output
    Where to stage. Defaults to artifacts/publish/win-x64.

.PARAMETER Configuration
    Release (the default) or Debug.

.PARAMETER SkipNative
    Stage the managed application only. The result runs; solid modelling is greyed out, which is
    a supported configuration (ADR-0021) and is what this switch is for testing.
#>
[CmdletBinding()]
param(
    [string] $Output,
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [switch] $SkipNative
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot

if (-not $Output) {
    $Output = Join-Path $repo 'artifacts\publish\win-x64'
}

if (Test-Path $Output) {
    Remove-Item $Output -Recurse -Force
}

New-Item -ItemType Directory -Path $Output -Force | Out-Null

# ------------------------------------------------------------------------------------------------
# The application and the command line, into one folder
#
# They share a folder on purpose: `spark.exe` is documented as shipping beside the application, and
# a user who has the desktop app has the CLI without installing anything else.
# ------------------------------------------------------------------------------------------------

foreach ($project in 'src\Spark.Desktop', 'src\Spark.Cli') {
    $name = Split-Path -Leaf $project
    Write-Host "==> Publishing $name"

    & dotnet publish (Join-Path $repo $project) `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained false `
        --output $Output `
        --nologo `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false

    if ($LASTEXITCODE -ne 0) { throw "publishing $name failed with $LASTEXITCODE." }
}

# ------------------------------------------------------------------------------------------------
# The native provider
#
# Copied flat, beside the managed assemblies, so that `OcctKernel`'s resolver finds it without an
# environment variable and so that every OpenCascade DLL sits where a user can replace it.
# ------------------------------------------------------------------------------------------------

if (-not $SkipNative) {
    $native = Join-Path $repo 'artifacts\native\win-x64'

    if (-not (Test-Path (Join-Path $native 'spark_occt.dll'))) {
        throw @"
The native provider has not been built. Run this first:

    pwsh scripts/build-native.ps1

or pass -SkipNative to stage a build with solid modelling greyed out.
"@
    }

    Write-Host '==> Staging the native provider'
    Copy-Item (Join-Path $native '*') $Output -Recurse -Force
}

# ------------------------------------------------------------------------------------------------
# The notices travel with the binaries, always
# ------------------------------------------------------------------------------------------------

Copy-Item (Join-Path $repo 'LICENSE') $Output -Force
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $Output -Force

$licences = Join-Path $Output 'licences'
if (-not (Test-Path $licences)) {
    New-Item -ItemType Directory -Path $licences -Force | Out-Null
}

Copy-Item (Join-Path $repo 'licences\*') $licences -Force

# And then check they arrived. The LGPL requires the licence text to travel with the binaries, and
# R21/R22 want the build key beside them so the offer of source can be honoured against a specific
# artefact rather than approximately. Every one of these is a Copy-Item above that a future edit
# could drop, and nothing else in the build would notice: the application runs perfectly without
# any of them. A staged folder missing a licence is the one defect here that is not a bug.
$required = @(
    'LICENSE',
    'THIRD-PARTY-NOTICES.md',
    'licences\LGPL-2.1.txt',
    'licences\OpenCascade-LGPL-2.1-with-exception.txt',
    'licences\OpenCascade-exception.txt'
)

if (-not $SkipNative) {
    # The build key identifies WHICH OpenCascade this artefact was built against. Without it the
    # source offer is a promise about a version nobody can name.
    $required += 'spark_occt.buildkey.json'
}

$missing = $required | Where-Object { -not (Test-Path (Join-Path $Output $_)) }

if ($missing) {
    throw ("The staged build is missing files it is obliged to ship: " + ($missing -join ', ') + '.')
}

# ------------------------------------------------------------------------------------------------
# What it weighs
#
# R15's bracket was 40-160 MB uncompressed and unmeasured. This prints the real number every time,
# split so that "the application grew" and "the kernel grew" are different sentences.
# ------------------------------------------------------------------------------------------------

function Measure-Payload([string] $path, [string[]] $include) {
    $files = Get-ChildItem $path -Recurse -File
    if ($include) {
        $files = $files | Where-Object { $include -contains $_.Name }
    }
    return ($files | Measure-Object -Property Length -Sum).Sum
}

$total = Measure-Payload $Output
$occtNames = @()
$native = Join-Path $repo 'artifacts\native\win-x64'
if (Test-Path $native) {
    $occtNames = Get-ChildItem $native -Filter *.dll | ForEach-Object { $_.Name }
}

$occt = if ($occtNames) { Measure-Payload $Output $occtNames } else { 0 }
$managed = $total - $occt

Write-Host ''
Write-Host ('==> Staged {0}' -f $Output)
Write-Host ('    total    {0,8:N1} MB' -f ($total / 1MB))
Write-Host ('    kernel   {0,8:N1} MB   ({1} native DLLs)' -f ($occt / 1MB), $occtNames.Count)
Write-Host ('    the rest {0,8:N1} MB' -f ($managed / 1MB))
Write-Host ''
Write-Host 'This is a folder that runs, not an installer. Signing, the installer itself and the'
Write-Host 'antivirus submissions need an identity to sign with; see E13-T17.'
