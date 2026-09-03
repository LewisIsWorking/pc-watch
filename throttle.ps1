<#
.SYNOPSIS
    Hard-cap the CPU that build and agent processes may use, so foreground work stays responsive.

.DESCRIPTION
    2026-09-02. Written because the machine oscillated between 40% and 99% CPU with 56 dotnet and 16
    node processes all at NORMAL priority - competing with the user's own apps as equals.

    Two mechanisms, and they solve different problems:

      PRIORITY (BelowNormal) makes background work yield the instant you touch anything. Total CPU
      still reads high, which is correct: an idle core is wasted work. This fixes how the machine
      FEELS without throwing capacity away.

      A JOB OBJECT CPU CAP is a genuine ceiling enforced by the kernel. Use it when you want the
      number itself lower - for heat, noise, or battery - accepting that builds then take longer.

    ⭐ The cap is applied to a JOB OBJECT rather than to individual processes because CHILDREN
      INHERIT IT. Setting priority on 76 processes lasts until the next build spawns 76 more; a job
      applied to the shell that spawns them covers everything it will ever start.

    ⚠️ THE HANDLE MUST STAY OPEN. A job object is destroyed when its last handle closes, and the cap
       vanishes with it - the processes keep running, uncapped, looking exactly as though the
       throttle were still working. That is why this script LOOPS rather than exiting.

.PARAMETER CpuPercent
    Hard ceiling for everything in the job, as a percentage of the whole machine. Omit for
    priority-only mode, which is usually the better trade.

.PARAMETER Names
    Process names to govern. Defaults to the .NET and Node build toolchain.

.PARAMETER Once
    Apply priority once and exit, without creating a job or looping.

.EXAMPLE
    pwsh -File throttle.ps1 -Once
    Lower the priority of every current build process. Instant, reversible, no cap.

.EXAMPLE
    pwsh -File throttle.ps1 -CpuPercent 70
    Hold build processes under 70% of the machine, re-applying to new ones. Leave it running.
#>
[CmdletBinding()]
param(
    [ValidateRange(5, 100)][int]$CpuPercent,
    [string[]]$Names = @('dotnet', 'MSBuild', 'VBCSCompiler', 'csc', 'testhost', 'node'),
    [switch]$Once,
    [int]$IntervalSeconds = 5
)

$ErrorActionPreference = 'Stop'

if (-not ('Win.Job' -as [type])) {
    Add-Type -Namespace 'Win' -Name 'Job' -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)]
public struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION { public uint ControlFlags; public uint CpuRate; }

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern IntPtr CreateJobObjectW(IntPtr security, string name);

[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_CPU_RATE_CONTROL_INFORMATION info, int length);

[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

[DllImport("kernel32.dll", SetLastError = true)]
public static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool CloseHandle(IntPtr handle);
'@
}

# JobObjectCpuRateControlInformation. ENABLE|HARD_CAP means "never exceed", as opposed to the
# weight-based mode which only matters under contention.
$INFO_CLASS = 15
$ENABLE = 0x1
$HARD_CAP = 0x4
$PROCESS_SET_QUOTA = 0x0100
$PROCESS_TERMINATE = 0x0001

function Set-LowPriority {
    param([string[]]$Names)

    $count = 0
    foreach ($p in Get-Process -Name $Names -ErrorAction SilentlyContinue) {
        try {
            if ($p.PriorityClass -ne [Diagnostics.ProcessPriorityClass]::BelowNormal) {
                $p.PriorityClass = [Diagnostics.ProcessPriorityClass]::BelowNormal
                $count++
            }
        } catch {
            # Exited between enumeration and assignment, or protected. Neither is worth reporting.
        }
    }
    $count
}

if ($Once) {
    "lowered priority on $(Set-LowPriority -Names $Names) process(es)"
    return
}

$job = [IntPtr]::Zero
if ($PSBoundParameters.ContainsKey('CpuPercent')) {
    $job = [Win.Job]::CreateJobObjectW([IntPtr]::Zero, "PcWatchThrottle_$PID")
    if ($job -eq [IntPtr]::Zero) { throw "CreateJobObject failed: $([ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error()).Message)" }

    $info = New-Object Win.Job+JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
    $info.ControlFlags = $ENABLE -bor $HARD_CAP
    $info.CpuRate = [uint32]($CpuPercent * 100)   # units of 1/100 of a percent

    $size = [Runtime.InteropServices.Marshal]::SizeOf($info)
    if (-not [Win.Job]::SetInformationJobObject($job, $INFO_CLASS, [ref]$info, $size)) {
        throw "SetInformationJobObject failed: $([ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error()).Message)"
    }
    Write-Host "hard cap: $CpuPercent% of the machine, shared by everything in the job" -ForegroundColor Cyan
} else {
    Write-Host 'priority-only mode (no cap). Pass -CpuPercent to enforce a ceiling.' -ForegroundColor Cyan
}

Write-Host "governing: $($Names -join ', ')"
Write-Host 'leave this running - closing it releases the job and the cap silently disappears.' -ForegroundColor DarkGray
Write-Host ''

$assigned = [Collections.Generic.HashSet[int]]::new()
try {
    while ($true) {
        $lowered = Set-LowPriority -Names $Names
        $added = 0

        if ($job -ne [IntPtr]::Zero) {
            foreach ($p in Get-Process -Name $Names -ErrorAction SilentlyContinue) {
                if (-not $assigned.Add($p.Id)) { continue }
                $h = [Win.Job]::OpenProcess($PROCESS_SET_QUOTA -bor $PROCESS_TERMINATE, $false, $p.Id)
                if ($h -eq [IntPtr]::Zero) { continue }
                try { if ([Win.Job]::AssignProcessToJobObject($job, $h)) { $added++ } } finally { [void][Win.Job]::CloseHandle($h) }
            }
        }

        $total = (Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor | Where-Object Name -eq '_Total').PercentProcessorTime
        $live = (Get-Process -Name $Names -ErrorAction SilentlyContinue | Measure-Object).Count
        '{0}  CPU {1,3}%   governed {2,3}   +{3} capped   +{4} deprioritised' -f (Get-Date -Format 'HH:mm:ss'), $total, $live, $added, $lowered

        Start-Sleep -Seconds $IntervalSeconds
    }
} finally {
    if ($job -ne [IntPtr]::Zero) { [void][Win.Job]::CloseHandle($job) }
}
