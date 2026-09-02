# make-icon.ps1 - build a real multi-resolution PcWatch.ico.
#
# 2026-08-31  Windows picks a DIFFERENT size for each surface: 16 px in the window title, 32 in the
#   Alt-Tab list, 48 on a large taskbar, 256 in the file dialog. A single-size .ico (which is all
#   Icon.FromHandle(bitmap.GetHicon()).Save() can produce) leaves Windows to rescale, and a 256 px
#   image squeezed to 16 px is unreadable mush. So this writes the ICO container by hand with one
#   PNG per size, each DRAWN at that size rather than downscaled.
#
# Run once; the build only needs the .ico. Re-run after changing the artwork.
[CmdletBinding()]
param([string]$OutPath = (Join-Path $PSScriptRoot 'PcWatch.ico'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256

function New-IconBitmap {
    param([int]$Size)

    $bmp = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([Drawing.Color]::Transparent)

        # Rounded dark plate.
        $radius = [math]::Max(2, [int]($Size * 0.18))
        $path = [Drawing.Drawing2D.GraphicsPath]::new()
        $d = $radius * 2
        $path.AddArc(0, 0, $d, $d, 180, 90)
        $path.AddArc($Size - $d - 1, 0, $d, $d, 270, 90)
        $path.AddArc($Size - $d - 1, $Size - $d - 1, $d, $d, 0, 90)
        $path.AddArc(0, $Size - $d - 1, $d, $d, 90, 90)
        $path.CloseFigure()

        $plate = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 26, 26, 32))
        $g.FillPath($plate, $path)
        $plate.Dispose()
        $path.Dispose()

        # A rising bar chart: green, amber, red. Reads as "load" even at 16 px, where text cannot.
        $colours = @(
            [Drawing.Color]::FromArgb(255,  70, 200, 110),
            [Drawing.Color]::FromArgb(255, 240, 190,  70),
            [Drawing.Color]::FromArgb(255, 235,  80,  80)
        )
        $pad = [math]::Max(1, [int]($Size * 0.16))
        $gap = [math]::Max(1, [int]($Size * 0.07))
        $barW = [int](($Size - (2 * $pad) - (2 * $gap)) / 3)
        if ($barW -lt 1) { $barW = 1 }
        $heights = 0.30, 0.58, 0.86

        for ($i = 0; $i -lt 3; $i++) {
            $h = [int](($Size - 2 * $pad) * $heights[$i])
            if ($h -lt 1) { $h = 1 }
            $x = $pad + $i * ($barW + $gap)
            $y = $Size - $pad - $h
            $brush = [Drawing.SolidBrush]::new($colours[$i])
            $g.FillRectangle($brush, $x, $y, $barW, $h)
            $brush.Dispose()
        }
    } finally {
        $g.Dispose()
    }
    $bmp
}

# --- Write the ICO container ------------------------------------------------------------------
# Layout: 6-byte header, then one 16-byte directory entry per image, then the image payloads.
# PNG payloads are legal in ICO from Vista onward and keep the 256 px entry small.
$pngs = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap -Size $size
    $ms = [IO.MemoryStream]::new()
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $pngs.Add($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}

$out = [IO.MemoryStream]::new()
$w = [IO.BinaryWriter]::new($out)
$w.Write([uint16]0)               # reserved
$w.Write([uint16]1)               # type: icon
$w.Write([uint16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    # 256 is encoded as 0 in the single width/height byte - the format has no room for 256.
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim)          # width
    $w.Write([byte]$dim)          # height
    $w.Write([byte]0)             # palette count (0 = truecolour)
    $w.Write([byte]0)             # reserved
    $w.Write([uint16]1)           # colour planes
    $w.Write([uint16]32)          # bits per pixel
    $w.Write([uint32]$pngs[$i].Length)
    $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Flush()

[IO.File]::WriteAllBytes($OutPath, $out.ToArray())
$w.Dispose(); $out.Dispose()

$info = Get-Item $OutPath
"wrote $($info.FullName) - $($info.Length) bytes, $($sizes.Count) sizes: $($sizes -join ', ')"

# Prove it parses back as an icon rather than trusting the bytes we just wrote.
$check = [Drawing.Icon]::new($OutPath, 16, 16)
"verified: loads at $($check.Width)x$($check.Height)"
$check.Dispose()
