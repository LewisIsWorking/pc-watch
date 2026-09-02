# Screenshot the running PC Watch window.
#
# 2026-08-31  Uses PrintWindow, NOT CopyFromScreen.
#   Two earlier attempts both produced a confident screenshot of the WRONG THING. CopyFromScreen
#   photographs whatever pixels occupy the rectangle, and Windows blocks SetForegroundWindow from a
#   background process, so the target never actually came to the front: the first grab caught a
#   video, the second a browser page. PrintWindow asks the window to render ITSELF into a DC, so an
#   occluded or even minimised window still captures correctly and cannot capture anything else.
#   PW_RENDERFULLCONTENT (flag 2) is required for DWM-composited content.
[CmdletBinding()]
param([string]$OutPath = "$env:TEMP\pcwatch-window.png")

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not ('Win.Shot' -as [type])) {
    Add-Type -Namespace 'Win' -Name 'Shot' -MemberDefinition @'
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
'@
}

$proc = Get-Process PcWatch -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { throw 'PcWatch is not running, or has no window' }

$h = $proc.MainWindowHandle

# ⛔ 2026-08-31: this used to call ShowWindow(SW_RESTORE) unconditionally, which restores a
#    MAXIMISED window to normal as well as a minimised one. The capture tool was un-maximising the
#    window it was sent to photograph, and then faithfully reporting the size it had just caused:
#    the probe read 2062x1118 and the screenshot came back 921x838. Only un-minimise.
if ([Win.Shot]::IsIconic($h)) { [void][Win.Shot]::ShowWindow($h, 9) }
Start-Sleep -Milliseconds 1500          # let at least one sampling tick land

$r = New-Object Win.Shot+RECT
if (-not [Win.Shot]::GetWindowRect($h, [ref]$r)) { throw 'GetWindowRect failed' }
$w = $r.R - $r.L
$hh = $r.B - $r.T
if ($w -le 0 -or $hh -le 0) { throw "bad window rect ${w}x${hh}" }

$bmp = [Drawing.Bitmap]::new($w, $hh, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
try {
    if (-not [Win.Shot]::PrintWindow($h, $hdc, 2)) { throw 'PrintWindow failed' }
} finally {
    $g.ReleaseHdc($hdc)
    $g.Dispose()
}

$bmp.Save($OutPath, [Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"captured ${w}x${hh} to $OutPath"
