<#
.SYNOPSIS
    Does .NET 11 actually make PC Watch faster? Measures rather than guesses.

.DESCRIPTION
    2026-09-02. "Feels faster" is not a measurement, and a runtime upgrade is exactly the kind of
    change everyone reports as an improvement because they expect one. This runs the SAME workload on
    both runtimes and reports the numbers.

    Workload: --self-test, which exercises the analyzer, the samplers, ancestry and 300 GDI icon
    renders. It is the only workload the app has that runs to completion.

    ⚠️ Startup DOMINATES this measurement. The self-test takes a couple of seconds, so a large part
    of what is compared is JIT and runtime init, not steady-state throughput. That is arguably the
    honest comparison for a tray app that is launched and left, but it is not a throughput benchmark
    and must not be reported as one.

    ⚠️ The two builds are NOT otherwise identical: net10 is framework-dependent, net11 is
    self-contained single-file. Single-file has to extract or map its bundle, which costs startup
    time that has nothing to do with the runtime version. Both numbers are real; the difference
    between them is not purely ".NET 11 vs .NET 10".
#>
[CmdletBinding()]
param([int]$Runs = 5)

$ErrorActionPreference = 'Stop'

$targets = @(
    @{ Name = '.NET 10 (framework-dependent)'; Exe = '<repo>\bin-net10\PcWatch.exe'; Root = $null },
    @{ Name = '.NET 11 (self-contained)';      Exe = '<repo>\bin\PcWatch.exe';       Root = $null }
)

foreach ($t in $targets) {
    if (-not (Test-Path -LiteralPath $t.Exe)) {
        Write-Host "SKIP $($t.Name): not built at $($t.Exe)" -ForegroundColor Yellow
        continue
    }

    if ($t.Root) { $env:DOTNET_ROOT = $t.Root } else { Remove-Item Env:\DOTNET_ROOT -ErrorAction SilentlyContinue }

    # One warm-up run that is not recorded: the first launch pays for cold file-system cache and,
    # for the single-file build, bundle extraction. Including it would measure the disk, not the app.
    & $t.Exe --self-test | Out-Null

    $times = @()
    $peak = 0
    foreach ($i in 1..$Runs) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $p = Start-Process -FilePath $t.Exe -ArgumentList '--self-test' -PassThru -Wait -WindowStyle Hidden
        $sw.Stop()
        $times += $sw.Elapsed.TotalMilliseconds
        if ($p.PeakWorkingSet64 -gt $peak) { $peak = $p.PeakWorkingSet64 }
    }

    $avg = ($times | Measure-Object -Average).Average
    $min = ($times | Measure-Object -Minimum).Minimum
    $max = ($times | Measure-Object -Maximum).Maximum

    '{0,-32} avg {1,7:N0} ms   min {2,7:N0}   max {3,7:N0}   peak RAM {4,5:N0} MB' -f `
        $t.Name, $avg, $min, $max, ($peak / 1MB)
}

Write-Host ''
Write-Host 'Startup dominates: the self-test itself is short, so most of this is runtime init.' -ForegroundColor DarkGray
Write-Host 'The net11 build is also single-file, which costs startup independently of the runtime.' -ForegroundColor DarkGray
