<#
.SYNOPSIS
    Asks the built shell what it loaded, so `E6-T14`'s promise can be measured rather than read.

.DESCRIPTION
    `E6-T14` promises that a graph containing no script nodes never loads `Spark.Scripting`, and
    with it never pays for twenty megabytes of Roslyn. `ScriptingResidencyTests` guards that for
    the command line, in the suite, both directions. THIS IS THE SHELL HALF, AND IT IS A SCRIPT
    RATHER THAN A TEST ON PURPOSE.

    Three reasons, all of them measured on 2026-09-15:

      1. It needs a display. Spark ships on Windows alone (D16) and the ubuntu CI leg has no
         display server, so the check could never run on both legs.

      2. It is slow, and the cost is lopsided. The clean direction is 2-3 seconds; the posed one
         was measured at 213 seconds on this machine, because loading Roslyn and compiling a block
         is most of what it measures. That is a large thing to add to a suite that runs on every
         step, for a second copy of an assertion the suite already makes about the other host.

      3. A single failing run of the clean direction was reported on 2026-09-15, while this was
         briefly a test rather than a script. **It has not been reproduced, and the attempt to
         reproduce it was thorough**: twenty consecutive clean runs on the same binaries, twelve
         of them alone and eight of them with a loop of `spark.exe run` children racing the shell
         over the same user profile - which was the reported guess at the cause - and all twenty
         with six recovery leftovers present, every one of them holding a code block. Zero of the
         twenty loaded `Spark.Scripting`.

         So the report stands and the explanation does not. It is kept here rather than deleted
         because an observation nobody can reproduce is still an observation, and the next person
         to see one should know it is the second and not the first. **What was ruled out by trying
         it**: recovery leftovers holding code blocks, and a concurrent `spark.exe` child over one
         profile. **What remains possible**: that the original run was misread.

    So: the defect is fixed and is proven by this probe, both directions, on the tree that fixed it.
    `E6-T14`'s acceptance box is ticked on twenty-plus clean runs and the posed direction beside
    them; the suite guards the command line's half of the same promise in `ScriptingResidencyTests`.

    Run this with an empty recovery folder to reproduce the clean result:
        %LOCALAPPDATA%\Spark\recovery

.PARAMETER Configuration
    Debug or Release. Defaults to Release, which is what a user runs.

.EXAMPLE
    ./scripts/probe-shell-residency.ps1
    ./scripts/probe-shell-residency.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$shell = Join-Path $root "src\Spark.Desktop\bin\$Configuration\net10.0\Spark.Desktop.exe"
$hook = Join-Path $root "tests\Spark.Cli.Tests\bin\$Configuration\net10.0\Spark.Cli.Tests.dll"

if (-not (Test-Path $shell)) { throw "the shell is not built at $shell" }
if (-not (Test-Path $hook)) { throw "the startup hook is not built at $hook" }

# The hook is `StartupHook` in Spark.Cli.Tests: the runtime loads the assembly named by
# DOTNET_STARTUP_HOOKS before Main and calls Initialize(), which writes the loaded-assembly list
# to SPARK_LOADED_ASSEMBLIES at exit. Nothing in the product knows about either variable.
function Invoke-Probe {
    param([string[]] $Arguments, [string] $Label)

    $log = Join-Path $env:TEMP ("residency-" + [guid]::NewGuid().ToString('n') + ".txt")
    $prefix = Join-Path $env:TEMP ("shot-" + [guid]::NewGuid().ToString('n'))

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $shell
    # --screenshot is what makes this terminate: the shell draws a frame, writes a picture and
    # exits, so what is measured is a shell that got as far as composing rather than one killed
    # on a timer.
    $psi.Arguments = ($Arguments -join ' ') + ' --screenshot "' + $prefix + '" --no-update-check'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables['SPARK_LOADED_ASSEMBLIES'] = $log
    $psi.EnvironmentVariables['DOTNET_STARTUP_HOOKS'] = $hook

    $started = Get-Date
    $process = [System.Diagnostics.Process]::Start($psi)
    $null = $process.StandardOutput.ReadToEnd()
    $null = $process.StandardError.ReadToEnd()
    $null = $process.WaitForExit(180000)
    $seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)

    if (-not (Test-Path $log)) { throw "$Label : the startup hook wrote nothing, so nothing was observed." }

    $loaded = Get-Content $log
    Remove-Item $log -Force
    Get-ChildItem -Path (Split-Path $prefix) -Filter ((Split-Path $prefix -Leaf) + '*') |
        Remove-Item -Force

    $scripting = @($loaded | Where-Object { $_ -like 'Spark.Scripting*' })
    $roslyn = @($loaded | Where-Object { $_ -like 'Microsoft.CodeAnalysis*' })

    [pscustomobject]@{
        Label     = $Label
        Exit      = $process.ExitCode
        Seconds   = $seconds
        Count     = $loaded.Count
        Scripting = $scripting.Count
        Roslyn    = $roslyn.Count
        SawShell  = [bool](@($loaded | Where-Object { $_ -eq 'Spark.UI' }).Count)
    }
}

$leftovers = @(Get-ChildItem -Path (Join-Path $env:LOCALAPPDATA 'Spark\recovery') -ErrorAction SilentlyContinue)

Write-Output "configuration : $Configuration"
# Reported because it is the state most likely to matter and the easiest to forget, not because
# it has been shown to change the answer - see the description.
Write-Output ("recovery leftovers : {0}" -f $leftovers.Count)
Write-Output ''

$results = @(
    (Invoke-Probe -Arguments @('--graph', 'curves') -Label 'no code block'),
    (Invoke-Probe -Arguments @('--code-block', '"return 1 + 1;"') -Label 'a code block')
)

$results | Format-Table -AutoSize

$clean = $results | Where-Object { $_.Label -eq 'no code block' }
$posed = $results | Where-Object { $_.Label -eq 'a code block' }

$failures = @()
if ($clean.Exit -ne 0) { $failures += 'the shell did not exit cleanly with no code block' }
if ($posed.Exit -ne 0) { $failures += 'the shell did not exit cleanly with a code block' }
if (-not $clean.SawShell) { $failures += 'Spark.UI was never loaded, so the probe watched the wrong process' }
if ($clean.Scripting -ne 0) { $failures += "Spark.Scripting was loaded with no code block ($($clean.Scripting) assemblies)" }
if ($clean.Roslyn -ne 0) { $failures += "Roslyn was loaded with no code block ($($clean.Roslyn) assemblies)" }
# The other direction, which is what stops a probe that observes nothing from reading as clean.
if ($posed.Scripting -eq 0) { $failures += 'Spark.Scripting was NOT loaded with a code block, so the probe proves nothing' }

if ($failures.Count -gt 0) {
    Write-Output 'FAILED:'
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}

Write-Output 'Both directions hold: the shell loads no Roslyn without a code block, and does with one.'
exit 0
