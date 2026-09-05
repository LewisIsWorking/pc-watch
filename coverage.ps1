<#
.SYNOPSIS
    Measure PC Watch's real test coverage: both suites, merged, honestly counted.

.DESCRIPTION
    ⛔ 2026-09-04. PC Watch reported 67.5% line coverage. The true figure for code a human wrote was
       54%. Three separate things were inflating it, and every one of them raises the number as you
       add tests, which is the worst possible direction for a metric to be wrong in:

         GENERATED CODE IN THE DENOMINATOR - RegexGenerator.g.cs alone is 1,116 lines. "Covering"
         it means testing Roslyn.

         TEST CODE MEASURING ITSELF - SelfTest*.cs compiles INTO the app, so 1,002 lines of test
         code counted as covered product code.

         ONE SUITE AT A TIME - there are TWO suites. The in-app self-test exercises live-machine
         behaviour; the unit tests exercise pure logic. Measuring either alone understates the
         product and invites someone to "fix" coverage that is already tested elsewhere.

    ⚠️ COVERLET COLLECTS NOTHING HERE. coverlet.collector 6.0.2 silently produced an EMPTY report
       against .NET 10 - no error, no warning, just <packages /> and a 0% score that reads like a
       misconfiguration. dotnet-coverage (Microsoft's, already installed) works. If you switch back,
       verify the report is non-empty before believing any number it gives you.

    ⚠️ CoverageBuild=true is REQUIRED and is not cosmetic. The app sets PathMap for privacy, which
       rewrites source paths to /src/ so no collector can map lines back to files.

.PARAMETER MinimumLine
    Fail below this line percentage. A ratchet: raise it as coverage improves, never lower it.

.PARAMETER MinimumBranch
    Fail below this branch percentage.

.EXAMPLE
    pwsh -File coverage.ps1
#>
[CmdletBinding()]
param(
    # ⭐ A RATCHET. These are the measured values as of 2026-09-04, so any change that LOWERS
    #   coverage fails immediately. Raise them as coverage improves; never lower them to make a
    #   build pass. Target is 100/100.
    [double]$MinimumLine = 60.0,
    [double]$MinimumBranch = 60.7,
    [string]$Tfm = 'net10.0-windows'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'coverage-output'
New-Item -ItemType Directory -Force -Path $out | Out-Null

if (-not (Get-Command dotnet-coverage -ErrorAction SilentlyContinue)) {
    throw 'dotnet-coverage not found. Install with: dotnet tool install --global dotnet-coverage'
}

# Files that must not count toward the product's score. Generated code is not ours to test, and
# test code that measures itself makes every added test raise the number for free.
$excluded = '\.g\.cs$|\.g\.i\.cs$|^SelfTest|Tests\.cs$|^FakeHttp\.cs$'

Write-Host 'building (CoverageBuild=true: privacy PathMap off, portable PDBs on)' -ForegroundColor Cyan
# ⚠️ SelfContained must be OFF for a coverage run. The app ships self-contained (there is no
#    machine-wide .NET 11), but the copy dropped beside the tests has no runtime next to it, so it
#    dies with "hostpolicy.dll not found" the moment the self-test is launched.
dotnet build (Join-Path $root 'tests/PcWatch.Tests/PcWatch.Tests.csproj') `
    -p:PcWatchTfm=$Tfm -p:CoverageBuild=true -p:SelfContained=false -p:PublishSingleFile=false `
    --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$unit = Join-Path $out 'unit.cobertura.xml'
$live = Join-Path $out 'selftest.cobertura.xml'
$merged = Join-Path $out 'merged.cobertura.xml'

Write-Host 'running unit tests under coverage' -ForegroundColor Cyan
dotnet-coverage collect -f cobertura -o $unit -- `
    dotnet test (Join-Path $root 'tests/PcWatch.Tests/PcWatch.Tests.csproj') `
    -p:PcWatchTfm=$Tfm -p:CoverageBuild=true --no-build --nologo -v q | Out-Null
if (-not (Test-Path $unit)) { throw 'unit coverage produced no report' }

Write-Host 'running the in-app self-test under coverage' -ForegroundColor Cyan
$exe = Join-Path $root "tests/PcWatch.Tests/bin/Debug/$Tfm/PcWatch.exe"
if (-not (Test-Path $exe)) { throw "built app not found at $exe" }
dotnet-coverage collect -f cobertura -o $live -- $exe --self-test | Out-Null
if (-not (Test-Path $live)) { throw 'self-test coverage produced no report' }

Write-Host 'merging both suites' -ForegroundColor Cyan
dotnet-coverage merge -f cobertura -o $merged $unit $live | Out-Null
if (-not (Test-Path $merged)) { throw 'merge produced no report' }

# ⚠️ An empty report is what coverlet produced, and it looks like a config error rather than what it
#    is. Refuse to report a percentage over zero measured files.
[xml]$report = Get-Content -LiteralPath $merged
$classes = $report.SelectNodes('//class')
if ($classes.Count -eq 0) { throw 'merged report contains NO measured files - the collector instrumented nothing' }

$files = @{}
foreach ($class in $classes) {
    $name = Split-Path -Leaf ($class.filename -replace '\\', '/')
    if ($name -match $excluded) { continue }

    # A named object, not a 4-element array. The array form silently became nested on the second
    # merge of the same file and failed with "Object[] does not contain op_Addition".
    $entry = $files[$name]
    if ($null -eq $entry) {
        $entry = [pscustomobject]@{ Hit = 0; Total = 0; BranchHit = 0; BranchTotal = 0 }
        $files[$name] = $entry
    }

    foreach ($line in $class.SelectNodes('.//line')) {
        $entry.Total++
        if ([int]$line.hits -gt 0) { $entry.Hit++ }
        if ($line.branch -eq 'True' -and $line.'condition-coverage' -match '\((\d+)/(\d+)\)') {
            $entry.BranchHit += [int]$Matches[1]
            $entry.BranchTotal += [int]$Matches[2]
        }
    }
}

$lineHit = ($files.Values | Measure-Object -Property Hit -Sum).Sum
$lineTot = ($files.Values | Measure-Object -Property Total -Sum).Sum
$brHit = ($files.Values | Measure-Object -Property BranchHit -Sum).Sum
$brTot = ($files.Values | Measure-Object -Property BranchTotal -Sum).Sum

$linePct = if ($lineTot) { 100 * $lineHit / $lineTot } else { 100 }
$branchPct = if ($brTot) { 100 * $brHit / $brTot } else { 100 }

Write-Host ''
Write-Host ('{0,-30} {1,7} {2,8}  {3}' -f 'FILE', 'LINE', 'BRANCH', 'MISSING') -ForegroundColor Cyan
foreach ($name in ($files.Keys | Sort-Object { $files[$_].Hit / [Math]::Max(1, $files[$_].Total) })) {
    $f = $files[$name]
    $lp = if ($f.Total) { 100 * $f.Hit / $f.Total } else { 100 }
    $bp = if ($f.BranchTotal) { 100 * $f.BranchHit / $f.BranchTotal } else { 100 }
    $colour = if ($lp -ge 100) { 'Green' } elseif ($lp -ge 80) { 'Yellow' } else { 'Red' }
    Write-Host ('{0,-30} {1,6:N1}% {2,7:N1}%  {3}' -f $name, $lp, $bp, ($f.Total - $f.Hit)) -ForegroundColor $colour
}

Write-Host ''
Write-Host ('PRODUCTION CODE ONLY, BOTH SUITES MERGED' ) -ForegroundColor Cyan
Write-Host ('  line   {0,6:N1}%  ({1} of {2})' -f $linePct, $lineHit, $lineTot)
Write-Host ('  branch {0,6:N1}%  ({1} of {2})' -f $branchPct, $brHit, $brTot)
Write-Host ('  {0} files, {1} uncovered lines' -f $files.Count, ($lineTot - $lineHit))
Write-Host ''

$failed = $false
if ($linePct -lt $MinimumLine) {
    Write-Host ('FAIL line {0:N1}% is below the {1}% floor' -f $linePct, $MinimumLine) -ForegroundColor Red
    $failed = $true
}
if ($branchPct -lt $MinimumBranch) {
    Write-Host ('FAIL branch {0:N1}% is below the {1}% floor' -f $branchPct, $MinimumBranch) -ForegroundColor Red
    $failed = $true
}
if ($failed) { exit 1 }
Write-Host 'coverage floors met' -ForegroundColor Green
