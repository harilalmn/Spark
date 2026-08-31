<#
.SYNOPSIS
    Packs a staged Spark into a deterministic portable zip, and checksums it.

.DESCRIPTION
    `E12-T10`. The portable zip is the release for people who cannot or will not run an installer:
    unzip it anywhere, run `Spark.Desktop.exe`, delete the folder to uninstall. It is also what
    `E12-T14` uploads from every build, so that *is it releasable* is a question answered
    continuously rather than discovered at the end.

    **The zip is deterministic and that is the whole point of writing it by hand.** `Compress-Archive`
    stamps each entry with the file's last-write time, so it is stable across two runs over one
    folder and **not** stable across a rebuild: the same source, compiled again, produces
    byte-identical assemblies with new timestamps and therefore a different archive and a different
    checksum. That was measured rather than assumed - two rebuilds, `Compress-Archive` differing and
    this script not. A release whose hash changes when nothing did is a release nobody can verify.

    Entries here are sorted by ordinal path, so the order does not depend on the file system, and
    stamped with one fixed timestamp. The stamp is 1980-01-01, the earliest a zip can represent: it
    is visibly not a real build date, which is better than a plausible wrong one.

.PARAMETER Staged
    The folder to pack. Defaults to artifacts/publish/win-x64, which is what publish.ps1 produces.

.PARAMETER Output
    The .zip to write. Defaults to artifacts/spark-portable-win-x64.zip.
#>
[CmdletBinding()]
param(
    [string] $Staged,
    [string] $Output
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot

if (-not $Staged) { $Staged = Join-Path $repo 'artifacts\publish\win-x64' }
if (-not $Output) { $Output = Join-Path $repo 'artifacts\spark-portable-win-x64.zip' }

if (-not (Test-Path $Staged)) {
    throw "Nothing staged at $Staged. Run scripts/publish.ps1 first."
}

$parent = Split-Path -Parent $Output
if ($parent -and -not (Test-Path $parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}

if (Test-Path $Output) { Remove-Item $Output -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = (Resolve-Path $Staged).Path.TrimEnd('\') + '\'

# Sorted by ordinal path, so the order is the same on every file system.
$files = Get-ChildItem $Staged -Recurse -File |
    Sort-Object -Property { $_.FullName.Substring($root.Length) -replace '\\', '/' }

$stamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

$stream = [System.IO.File]::Create($Output)
try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $stream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($root.Length) -replace '\\', '/'

            $entry = $archive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $stamp

            $source = [System.IO.File]::OpenRead($file.FullName)
            try {
                $target = $entry.Open()
                try { $source.CopyTo($target) } finally { $target.Dispose() }
            }
            finally { $source.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
finally { $stream.Dispose() }

$hash = (Get-FileHash -Path $Output -Algorithm SHA256).Hash
$size = (Get-Item $Output).Length

Set-Content -Path ($Output + '.sha256') -Encoding utf8 -Value ('{0}  {1}' -f $hash, (Split-Path -Leaf $Output))

Write-Host ''
Write-Host ('==> Packed {0}' -f $Output)
Write-Host ('    files    {0,8:N0}' -f $files.Count)
Write-Host ('    size     {0,8:N1} MB' -f ($size / 1MB))
Write-Host ('    sha256   {0}' -f $hash)
Write-Host ''
Write-Host 'Deterministic: the same staged folder produces the same bytes, so this hash is worth'
Write-Host 'publishing beside the file.'
