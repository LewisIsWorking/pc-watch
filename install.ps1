<#
.SYNOPSIS
    Create the Start Menu shortcut for PC Watch (and optionally start it with Windows).

.DESCRIPTION
    2026-09-02. Windows offers "Pin to taskbar" only for shortcuts to EXECUTABLES, and pinning is far
    easier from the Start Menu than from a folder: Win key, type "PC Watch", right-click, Pin.

    Every path is resolved RELATIVE TO THIS SCRIPT. The first version hardcoded one machine's user
    profile, which made the installer useless to anyone who cloned the repository - and a hardcoded
    home directory is the kind of thing that works perfectly right up until someone else runs it.

    Running it twice is safe: the shortcut is overwritten, not duplicated.

.PARAMETER StartWithWindows
    Also drop a shortcut in shell:startup so it launches at logon.

.PARAMETER Monitor
    Pass "right", "left", "primary" or a 1-based index to place the window on first launch. The
    position is saved on exit, so this only has to be set once.

.PARAMETER Remove
    Delete both shortcuts.
#>
[CmdletBinding()]
param(
    [switch]$StartWithWindows,
    [string]$Monitor,
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'bin\PcWatch.exe'
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'PC Watch.lnk'
$startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'PC Watch.lnk'

if ($Remove) {
    foreach ($path in $startMenu, $startup) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force; "removed $path" }
    }
    return
}

if (-not (Test-Path -LiteralPath $exe)) {
    throw @"
PcWatch.exe not found at:
  $exe

Build it first, from the app folder:
  dotnet publish -c Release -r win-x64 -o ..\bin
"@
}

function New-Shortcut {
    param([string]$Path)

    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath = $exe
    $link.WorkingDirectory = Split-Path -Parent $exe
    $link.IconLocation = "$exe,0"
    $link.Description = 'Real CPU load, and what is causing it'
    if ($Monitor) { $link.Arguments = "--monitor $Monitor" }
    $link.Save()
    [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
    "wrote $Path"
}

New-Shortcut -Path $startMenu
if ($StartWithWindows) { New-Shortcut -Path $startup }

Write-Host ''
Write-Host 'To pin it: press Win, type "PC Watch", right-click the result, Pin to taskbar.' -ForegroundColor Cyan
Write-Host 'Clicking the pin again shows the running window rather than starting a second copy.' -ForegroundColor DarkGray
