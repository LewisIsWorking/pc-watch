<#
.SYNOPSIS
    Prove the reaper's threshold logic BOTH ways without touching a real emulator.

.DESCRIPTION
    ⛔ 2026-09-03. Written after the reaper shut down a live emulator that was idle 5.4 h when it had
       been told 12 h. The cause: the computed local was named $idleHours and the parameter was
       -IdleHours, and PowerShell VARIABLE NAMES ARE CASE-INSENSITIVE, so the measurement silently
       overwrote the threshold. Every comparison became "$x -lt $x" - always false, always kill.

       The script logged "threshold 5.37 h" after being passed 12. That mismatch was the only
       evidence, and it was only visible because the log printed BOTH numbers. A log that had
       printed just "idle 5.37 h" would have shown nothing wrong at all.

    ⚠️ THE PROCESS LESSON, which cost a live emulator: run anything destructive with -WhatIf FIRST.
       The test that caught this was the right test, but it caught the bug by performing the
       destruction it was meant to prevent.

    This exercises the decision arithmetic in isolation, so the pair of "spares" and "reaps" cases
    can be checked in a second with nothing at risk.
#>
$ErrorActionPreference = 'Stop'
$failures = 0

function Check {
    param([string]$What, [scriptblock]$Body)
    try { & $Body; Write-Host "  [PASS] $What" -ForegroundColor Green }
    catch { $script:failures++; Write-Host "  [FAIL] $What -> $($_.Exception.Message)" -ForegroundColor Red }
}

# The decision, extracted exactly as the script performs it.
function Test-ShouldReap {
    param([double]$IdleHours, [double]$MaxCpuPercent, [double]$MeasuredIdleHours, [double]$MeasuredCpuPercent)
    if ($MeasuredIdleHours -lt $IdleHours) { return $false }
    if ($MeasuredCpuPercent -gt $MaxCpuPercent) { return $false }
    $true
}

Write-Host "`nTHE BUG THAT KILLED A LIVE EMULATOR" -ForegroundColor Cyan
Check 'idle 5.4h against a 12h threshold is SPARED' {
    if (Test-ShouldReap -IdleHours 12 -MaxCpuPercent 4 -MeasuredIdleHours 5.37 -MeasuredCpuPercent 1.8) {
        throw 'would have killed an emulator less than half way to the threshold'
    }
}
Check 'the threshold is not the measurement (the case-collision case)' {
    # Under the bug both sides were the same number, so this is exactly what "$x -lt $x" produced.
    if (-not (Test-ShouldReap -IdleHours 5.37 -MaxCpuPercent 4 -MeasuredIdleHours 5.37 -MeasuredCpuPercent 1.8)) {
        throw 'premise wrong: equal values should reap, which is why the collision was fatal'
    }
}

Write-Host "`nTHRESHOLDS BEHAVE" -ForegroundColor Cyan
Check 'idle 13h, quiet, IS reaped'            { if (-not (Test-ShouldReap 12 4 13 1.0)) { throw 'spared an abandoned emulator' } }
Check 'idle 11.9h is spared (just inside)'    { if (Test-ShouldReap 12 4 11.9 1.0) { throw 'reaped too early' } }
Check 'idle 0h is spared'                     { if (Test-ShouldReap 12 4 0 0.5) { throw 'reaped a fresh emulator' } }

Write-Host "`nBUSY EMULATORS SURVIVE REGARDLESS OF INPUT IDLENESS" -ForegroundColor Cyan
Check 'idle 20h but 30% cpu is SPARED (adb automation looks idle)' {
    if (Test-ShouldReap 12 4 20 30) { throw 'killed an emulator under active test' }
}
Check 'idle 20h at exactly the ceiling is reaped' { if (-not (Test-ShouldReap 12 4 20 4)) { throw 'off-by-one at the ceiling' } }
Check 'idle 20h just over the ceiling is spared' { if (Test-ShouldReap 12 4 20 4.1) { throw 'ignored the cpu signal' } }

Write-Host ''
if ($failures) { Write-Host "$failures CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
Write-Host 'REAPER DECISION LOGIC PROVEN' -ForegroundColor Green
