<#
.SYNOPSIS
    Compiles the Inno Setup installer over a staged Spark, and checksums it.

.DESCRIPTION
    `E13-T17`. Spark was a portable zip and is now something a user installs. This drives `ISCC`
    over `installer/spark.iss` and the folder `publish.ps1` staged, and writes the result beside
    the portable zip so that a release carries both: the installer for people who want one, the zip
    for people who cannot run one.

    **The version is passed in rather than read here.** It comes from the built assembly, which is
    where MinVer put it, which is where `check-version.ps1` already reads it from on the release
    path. Deriving it a second way is how a release ends up with an installer named for one version
    that installs another.

    **Two versions, because Windows needs two.** `AppVersion` is what a person reads - the full
    SemVer, `0.2.0-alpha.3` and all - and `VersionInfoVersion` is what goes in the PE header, which
    accepts only four numbers. Passing the SemVer to both makes ISCC fail on the second, which is
    a good failure to have already had.

.PARAMETER Staged
    The folder to install. Defaults to artifacts/publish/win-x64, which is what publish.ps1 makes.

.PARAMETER Version
    The version to stamp. Defaults to the InformationalVersion of the staged Spark.Desktop.dll.

.PARAMETER Output
    Where to write the installer. Defaults to artifacts.
#>
[CmdletBinding()]
param(
    [string] $Staged,
    [string] $Version,
    [string] $Output
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot

if (-not $Staged) { $Staged = Join-Path $repo 'artifacts\publish\win-x64' }
if (-not $Output) { $Output = Join-Path $repo 'artifacts' }

if (-not (Test-Path $Staged)) {
    throw "Nothing staged at $Staged. Run scripts/publish.ps1 first."
}

# ------------------------------------------------------------------------------------------------
# ISCC
#
# Preinstalled on GitHub's windows-latest images, which is most of why Inno Setup was chosen over
# WiX: no tooling step in the workflow, and nothing to pin. It is NOT on PATH there, so the two
# usual locations are probed before giving up with a sentence that says what to install.
# ------------------------------------------------------------------------------------------------
$iscc = (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue).Source

if (-not $iscc) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    $iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

if (-not $iscc) {
    throw 'ISCC.exe was not found. Install Inno Setup 6 (winget install JRSoftware.InnoSetup), or put ISCC.exe on PATH.'
}

# ------------------------------------------------------------------------------------------------
# The version, out of the artefact
#
# The same rule check-version.ps1 states and for the same reason: the artefact is what ships, so
# the artefact is what is asked. InformationalVersion rather than AssemblyVersion, because MinVer
# stamps the first with the full SemVer and truncates the second to major.0.0.0 - releasing
# `0.2.0-alpha.3` as `0.0.0.0` would be a silent and very confusing wrong answer.
# ------------------------------------------------------------------------------------------------
if (-not $Version) {
    $assembly = Join-Path $Staged 'Spark.Desktop.dll'

    if (-not (Test-Path $assembly)) {
        throw "No Spark.Desktop.dll at $assembly, so there is no version to read."
    }

    $Version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($assembly).ProductVersion

    # A source-linked build appends `+<commit>`. It belongs in the assembly and not in a file name.
    if ($Version -match '^(.*?)\+') { $Version = $Matches[1] }
}

if (-not $Version) {
    throw 'Could not determine a version to stamp the installer with.'
}

# Four numbers for the PE header. The prerelease tail has nowhere to go there, which is fine: it is
# in AppVersion, which is what Add/Remove Programs and the wizard both show.
$fileVersion = if ($Version -match '^(\d+)\.(\d+)\.(\d+)') {
    '{0}.{1}.{2}.0' -f $Matches[1], $Matches[2], $Matches[3]
} else {
    '0.0.0.0'
}

if (-not (Test-Path $Output)) {
    New-Item -ItemType Directory -Path $Output -Force | Out-Null
}

$script = Join-Path $repo 'installer\spark.iss'

Write-Host ('==> Compiling the installer for {0}' -f $Version)

& $iscc `
    "/DAppVersion=$Version" `
    "/DFileVersion=$fileVersion" `
    "/DStaged=$Staged" `
    "/DOutputDir=$Output" `
    $script

if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE."
}

$installer = Join-Path $Output ("spark-$Version-setup.exe")

if (-not (Test-Path $installer)) {
    throw "ISCC reported success but $installer is not there."
}

# ------------------------------------------------------------------------------------------------
# The checksum
#
# Same format pack-portable.ps1 writes, so a release's two artefacts are verified the same way:
# `<sha256>  <filename>`, which is what `sha256sum -c` and `Get-FileHash` both expect to see.
# ------------------------------------------------------------------------------------------------
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash.ToLowerInvariant()
$name = Split-Path -Leaf $installer

Set-Content -LiteralPath "$installer.sha256" -Value ("$hash  $name") -Encoding ascii -NoNewline

$size = (Get-Item -LiteralPath $installer).Length

Write-Host ''
Write-Host ('==> {0}' -f $installer)
Write-Host ('    version  {0}' -f $Version)
Write-Host ('    size     {0,8:N1} MB' -f ($size / 1MB))
Write-Host ('    sha256   {0}' -f $hash)
Write-Host ''
Write-Host 'This installer is NOT signed. The first run shows a SmartScreen warning, and that is'
Write-Host 'expected until there is an identity to sign with; see E13-T17.'
