<#
.SYNOPSIS
    Shut down Android emulators that nobody has used for a long time.

.DESCRIPTION
    2026-09-03. An abandoned emulator costs about 6 GB of RAM and 12-20% CPU indefinitely, and they
    routinely outlive the session that started one: the tool that launched it exits, the emulator
    does not.

    ⭐ THE IDLE MEASURE IS ANDROID'S OWN, NOT OURS. `dumpsys power` reports
      mLastUserActivityTime against SystemClock.uptimeMillis(), so idle time is a subtraction the
      DEVICE can answer at any moment. That makes this script effectively STATELESS.

      A watchdog that accumulates its own observations has a nasty failure mode: every restart -
      reboot, logoff, crash, scheduled-task hiccup - resets its counter, so on a machine that
      restarts daily it can never reach a 12 hour threshold and silently never fires. Asking the
      device removes that entire class of bug.

    ⚠️ WHAT THE SIGNAL CANNOT SEE: adb-driven automation does not generate input events, so a test
       suite hammering the emulator for hours still reads as "no user activity". That is why a low
       CPU reading is ALSO required before anything is killed - measured over a real window at
       decision time, not a single instant.

    Shutdown is graceful (`adb emu kill`) so Android can flush state; force-kill is a fallback only.

.PARAMETER IdleHours
    How long with no user activity before an emulator is considered abandoned.

.PARAMETER MaxCpuPercent
    Ceiling, as a share of the whole machine, below which the emulator counts as doing nothing.
    Above this something is driving it and it is left alone regardless of input idleness.

.PARAMETER WhatIf
    Report the decision without acting.

.EXAMPLE
    pwsh -File emulator-reaper.ps1 -WhatIf
    Show what would happen, change nothing.
#>
[CmdletBinding()]
param(
    [double]$IdleHours = 12,
    [double]$MaxCpuPercent = 4,
    [int]$CpuSampleSeconds = 45,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$logPath = Join-Path $env:APPDATA 'PcWatch\emulator-reaper.log'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

function Write-Log {
    param([string]$Message)
    $line = '{0}  {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $line
    Add-Content -LiteralPath $logPath -Value $line
}

function Find-Adb {
    foreach ($candidate in @(
        (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'),
        (Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe'),
        'adb.exe')) {
        if ($candidate -and (Get-Command $candidate -ErrorAction SilentlyContinue)) { return $candidate }
    }
    $null
}

$adb = Find-Adb
if (-not $adb) { Write-Log 'adb not found - nothing to do'; return }

$devices = @(& $adb devices 2>$null | Select-Object -Skip 1 |
             Where-Object { $_ -match '^(emulator-\d+)\s+device' } |
             ForEach-Object { ($_ -split '\s+')[0] })

if ($devices.Count -eq 0) { Write-Log 'no running emulators'; return }

foreach ($serial in $devices) {
    try {
        # Android's own record. uptime is seconds with a decimal; the activity stamp is milliseconds
        # on the same clock, so the difference is genuine idle time no matter when we ask.
        $uptimeRaw = (& $adb -s $serial shell cat /proc/uptime 2>$null) -split '\s+'
        $uptimeMs = [double]$uptimeRaw[0] * 1000

        $dump = & $adb -s $serial shell dumpsys power 2>$null
        $match = [regex]::Match(($dump -join "`n"), 'mLastUserActivityTime(?:\(excludingAttention\))?=(\d+)')
        if (-not $match.Success) { Write-Log "$serial : could not read last-activity time, leaving alone"; continue }

        # ⛔ 2026-09-03: this local was called $idleHours, and PowerShell VARIABLE NAMES ARE
        #    CASE-INSENSITIVE - so it was the same variable as the -IdleHours parameter and silently
        #    overwrote the threshold with the measured value. Every comparison then read
        #    "$x -lt $x" = false, and the script shut down a live emulator that was well inside its
        #    window. It logged "threshold 5.37 h" while having been passed 12, which is the only
        #    reason it was caught.
        #
        #    A destructive script must never reuse a parameter's name for a computed value. The
        #    measured quantity is now named for what it is.
        $measuredIdleHours = ($uptimeMs - [double]$match.Groups[1].Value) / 3600000
        Write-Log ('{0} : idle {1:N2} h (threshold {2} h)' -f $serial, $measuredIdleHours, $IdleHours)

        if ($measuredIdleHours -lt $IdleHours) { Write-Log "$serial : still within the window, leaving alone"; continue }

        # ⚠️ Second signal. adb automation never touches the input subsystem, so an emulator under
        #    test looks perfectly idle by the measure above. CPU is what distinguishes them.
        $qemu = Get-Process qemu-system-x86_64 -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $qemu) { Write-Log "$serial : no qemu process found, leaving alone"; continue }

        $before = $qemu.TotalProcessorTime.TotalSeconds
        Start-Sleep -Seconds $CpuSampleSeconds
        $qemu.Refresh()
        $measuredCpuPercent = 100 * ($qemu.TotalProcessorTime.TotalSeconds - $before) / $CpuSampleSeconds / [Environment]::ProcessorCount
        Write-Log ('{0} : cpu {1:N1}% over {2}s (ceiling {3}%)' -f $serial, $measuredCpuPercent, $CpuSampleSeconds, $MaxCpuPercent)

        if ($measuredCpuPercent -gt $MaxCpuPercent) {
            Write-Log "$serial : busy despite no input - something is driving it, leaving alone"
            continue
        }

        $ram = [math]::Round($qemu.WorkingSet64 / 1GB, 1)
        if ($WhatIf) {
            Write-Log ('{0} : WOULD SHUT DOWN (idle {1:N1} h, {2:N1}% cpu, {3} GB)' -f $serial, $measuredIdleHours, $measuredCpuPercent, $ram)
            continue
        }

        Write-Log ('{0} : shutting down - idle {1:N1} h, {2:N1}% cpu, reclaiming {3} GB' -f $serial, $measuredIdleHours, $measuredCpuPercent, $ram)
        & $adb -s $serial emu kill 2>$null | Out-Null
        Start-Sleep -Seconds 10

        if (Get-Process -Id $qemu.Id -ErrorAction SilentlyContinue) {
            Write-Log "$serial : graceful shutdown did not take, forcing"
            Stop-Process -Id $qemu.Id -Force -ErrorAction SilentlyContinue
        }
        Write-Log "$serial : done"
    } catch {
        # One bad emulator must not stop the others being checked.
        Write-Log "$serial : error - $($_.Exception.Message)"
    }
}
