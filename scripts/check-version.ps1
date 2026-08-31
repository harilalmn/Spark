<#
.SYNOPSIS
    Refuses to release when the built assemblies disagree with the tag being released.

.DESCRIPTION
    `E12-T11`. Spark's version comes from MinVer, derived from the nearest git tag (ADR-0007), so
    in principle the tag and the assemblies cannot disagree. In practice there is one way they can,
    and it is the default: **a shallow checkout has no tags**, MinVer finds nothing, and every
    assembly is stamped `0.0.0-alpha.0` while the workflow cheerfully publishes them as `v1.0.0`.

    That produces the worst kind of release. It installs, it runs, and every bug report from it
    names a version that does not exist. A package author asking *does this build have the API I
    need* gets an answer about a version nobody ever cut.

    So this reads the version out of a built assembly and compares it with the tag. It is a gate on
    the artefact rather than on the build inputs, because the artefact is what ships.

.PARAMETER Tag
    The tag being released, with or without a leading `v`.

.PARAMETER Assembly
    The assembly to read. Defaults to the staged Spark.Desktop.dll.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Tag,

    [string] $Assembly
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot

if (-not $Assembly) {
    $Assembly = Join-Path $repo 'artifacts\publish\win-x64\Spark.Desktop.dll'
}

if (-not (Test-Path $Assembly)) {
    throw "No assembly at $Assembly. Run scripts/publish.ps1 first."
}

$expected = $Tag.TrimStart('v', 'V')

if ([string]::IsNullOrWhiteSpace($expected)) {
    throw "The tag '$Tag' has no version in it."
}

$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path $Assembly).Path)

# ProductVersion carries the informational version, which is the one that keeps a prerelease
# suffix. FileVersion drops it, so `1.0.0-rc.1` and `1.0.0` would compare equal - which is exactly
# the pair worth telling apart.
$actual = $info.ProductVersion

if ([string]::IsNullOrWhiteSpace($actual)) {
    throw "The assembly at $Assembly declares no product version."
}

# MinVer appends build metadata after a '+'. It is not part of the version's identity and it
# changes with the commit, so it is trimmed rather than compared.
$actual = ($actual -split '\+')[0]

Write-Host ('==> tag      {0}' -f $Tag)
Write-Host ('    expected {0}' -f $expected)
Write-Host ('    assembly {0}' -f $actual)

if ($actual -ne $expected) {
    Write-Host ''
    Write-Host 'These do not match, so nothing is released.'
    Write-Host ''
    Write-Host 'The usual cause is a checkout with no tags: MinVer finds none, stamps'
    Write-Host '0.0.0-alpha.0, and the workflow publishes it under the tag anyway. Check that the'
    Write-Host 'checkout used fetch-depth: 0, and that the tag is on the commit being built.'

    throw "Version mismatch: the tag says '$expected' and the assemblies say '$actual'."
}

Write-Host ''
Write-Host 'The tag and the artefact agree.'
