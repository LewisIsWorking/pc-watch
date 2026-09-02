# What size is the PC Watch window ACTUALLY, and at what DPI?
# 2026-08-31  Written because an explicit ClientSize kept being ignored and the only evidence was a
#             screenshot. GetWindowRect is the outer frame; GetClientRect is the drawable area; the
#             DPI decides whether a number in code means the same as a number on screen.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

if (-not ('Win.Probe' -as [type])) {
    Add-Type -Namespace 'Win' -Name 'Probe' -MemberDefinition @'
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
'@
}

$p = Get-Process PcWatch -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { throw 'PcWatch has no window' }
$h = $p.MainWindowHandle

$wr = New-Object Win.Probe+RECT; [void][Win.Probe]::GetWindowRect($h, [ref]$wr)
$cr = New-Object Win.Probe+RECT; [void][Win.Probe]::GetClientRect($h, [ref]$cr)
$dpi = [Win.Probe]::GetDpiForWindow($h)

"pid          : $($p.Id)"
"window rect  : $($wr.R - $wr.L) x $($wr.B - $wr.T)"
"client rect  : $($cr.R - $cr.L) x $($cr.B - $cr.T)"
"window DPI   : $dpi  (scale $([math]::Round(100 * $dpi / 96))%)"
"primary work : $([Windows.Forms.Screen]::PrimaryScreen.WorkingArea)"
