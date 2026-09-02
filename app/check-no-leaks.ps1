<#
.SYNOPSIS
    Fail if the built binary, or any tracked file, leaks a home directory, a machine name or a secret.

.DESCRIPTION
    2026-09-02. Written after the published 1.1.0 exe was found to contain

        C:\Users\<name>\tools\pc-watch\app\obj\Release\net11.0-windows\win-x64\PcWatch.pdb

    in its debug directory. Nothing warns about that. It is simply what the compiler emits, and it
    means every downloader of a release learns the builder's Windows username.

    ⚠️ This scans the ACTUAL BUILT ARTEFACT, not the source. A source-only check would have passed
    happily while the binary leaked, because the offending string is written by the compiler and
    exists nowhere in the repository.

    ⚠️ It also derives what to search for from the CURRENT machine ($env:USERNAME, computer name)
    rather than hardcoding one person's name, so it protects whoever builds it - a hardcoded needle
    would silently stop finding anything the moment someone else cloned the project.

.PARAMETER BinaryPath
    Built exe to scan. Defaults to ..\bin\PcWatch.exe relative to this script.
#>
[CmdletBinding()]
param([string]$BinaryPath)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $BinaryPath) { $BinaryPath = Join-Path $root 'bin\PcWatch.exe' }

$failures = 0
function Fail([string]$message) { $script:failures++; Write-Host "  [FAIL] $message" -ForegroundColor Red }
function Pass([string]$message) { Write-Host "  [PASS] $message" -ForegroundColor Green }

# What must never appear in a published artefact. Derived from this machine, so the check keeps
# working for anyone who clones the project rather than protecting one specific name.
$needles = [ordered]@{
    "home directory ($env:USERNAME)" = [regex]::Escape("C:\Users\$env:USERNAME")
    "user profile path"              = [regex]::Escape($env:USERPROFILE)
    "computer name ($env:COMPUTERNAME)" = "\b$([regex]::Escape($env:COMPUTERNAME))\b"
    "an obj or bin build directory"   = '[A-Za-z]:\\[^\x00]{0,60}\\obj\\(Debug|Release)\\'
}

Write-Host "`nBINARY: $BinaryPath" -ForegroundColor Cyan
if (-not (Test-Path -LiteralPath $BinaryPath)) {
    Write-Host '  [SKIP] not built yet' -ForegroundColor Yellow
} else {
    $bytes = [IO.File]::ReadAllBytes($BinaryPath)
    # Both encodings: .NET string literals land as UTF-16, native/metadata strings as ASCII. Checking
    # only one would miss half the file and report a clean binary.
    $haystacks = @{
        ASCII = [Text.Encoding]::ASCII.GetString($bytes)
        UTF16 = [Text.Encoding]::Unicode.GetString($bytes)
    }

    foreach ($name in $needles.Keys) {
        $hits = @()
        foreach ($enc in $haystacks.Keys) {
            $hits += [regex]::Matches($haystacks[$enc], $needles[$name]) |
                     Select-Object -Expand Value -Unique |
                     ForEach-Object { "$enc : $_" }
        }
        if ($hits) { Fail "$name found in the binary:`n        $($hits -join "`n        ")" }
        else { Pass "no $name in the binary" }
    }
}

Write-Host "`nTRACKED FILES" -ForegroundColor Cyan
Push-Location $root
try {
    $tracked = git ls-files 2>$null
    if (-not $tracked) {
        Write-Host '  [SKIP] not a git repository' -ForegroundColor Yellow
    } else {
        foreach ($name in $needles.Keys) {
            $hits = $tracked | ForEach-Object { Select-String -Path $_ -Pattern $needles[$name] -List -EA SilentlyContinue }
            if ($hits) { Fail "$name in: $(($hits | Select-Object -Expand Path) -join ', ')" }
            else { Pass "no $name in tracked files" }
        }

        $secrets = @{
            'GitHub token'   = 'gh[pousr]_[A-Za-z0-9]{16,}'
            'AWS access key' = 'AKIA[0-9A-Z]{16}'
            'private key'    = '-----BEGIN [A-Z ]*PRIVATE KEY'
            'Slack token'    = 'xox[baprs]-[A-Za-z0-9-]{10,}'
        }
        foreach ($name in $secrets.Keys) {
            $hits = $tracked | ForEach-Object { Select-String -Path $_ -Pattern $secrets[$name] -List -EA SilentlyContinue }
            if ($hits) { Fail "$name in: $(($hits | Select-Object -Expand Path) -join ', ')" }
        }
        Pass 'no credential patterns in tracked files'
    }
} finally {
    Pop-Location
}

Write-Host ''
if ($failures -gt 0) { Write-Host "$failures LEAK CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
Write-Host 'NO LEAKS FOUND' -ForegroundColor Green
