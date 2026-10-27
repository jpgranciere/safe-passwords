# Generates the app/tray icons (multi-size PNG-in-ICO) with GDI+.
#   Assets\cofre.ico       -> exe icon + tray icon while the vault is locked
#   Assets\cofre-open.ico  -> tray icon while the vault is unlocked (green badge)
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icons.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot "..\src\Cofre.App\Assets"
New-Item -ItemType Directory -Force $outDir | Out-Null
$outDir = (Resolve-Path $outDir).Path

function New-Square([float]$x, [float]$y, [float]$w, [float]$h) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddRectangle([System.Drawing.RectangleF]::new($x, $y, $w, $h))
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$size, [bool]$unlocked) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$size
    $margin = $s * 0.04
    $body = New-Square $margin $margin ($s - 2 * $margin) ($s - 2 * $margin)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $grad = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 77, 97, 59))
    $g.FillPath($grad, $body)

    # keyhole: circle + tapered stem
    $white = [System.Drawing.Brushes]::White
    $cx = $s * 0.5
    $cr = $s * 0.14
    $cy = $s * 0.40
    $g.FillEllipse($white, $cx - $cr, $cy - $cr, 2 * $cr, 2 * $cr)
    $stem = New-Object System.Drawing.Drawing2D.GraphicsPath
    $stem.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($cx - $s * 0.065), ($cy + $cr * 0.55)),
        (New-Object System.Drawing.PointF ($cx + $s * 0.065), ($cy + $cr * 0.55)),
        (New-Object System.Drawing.PointF ($cx + $s * 0.115), ($s * 0.73)),
        (New-Object System.Drawing.PointF ($cx - $s * 0.115), ($s * 0.73))))
    $g.FillPath($white, $stem)

    if ($unlocked) {
        $br = $s * 0.19
        $bx = $s * 0.76
        $by = $s * 0.76
        $ring = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 26, 28, 24))
        $g.FillEllipse($ring, $bx - $br - $s * 0.035, $by - $br - $s * 0.035, 2 * ($br + $s * 0.035), 2 * ($br + $s * 0.035))
        $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 186, 201, 154))
        $g.FillEllipse($green, $bx - $br, $by - $br, 2 * $br, 2 * $br)
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

function Write-Ico([string]$path, [bool]$unlocked) {
    $sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
    $images = foreach ($sz in $sizes) { , (New-IconPng $sz $unlocked) }

    $fs = [System.IO.File]::Create($path)
    $w = New-Object System.IO.BinaryWriter $fs
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $sz = $sizes[$i]
        $dim = if ($sz -ge 256) { 0 } else { $sz }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write($img) }
    $w.Dispose(); $fs.Dispose()
    "{0}  ({1:N0} bytes)" -f $path, (Get-Item $path).Length
}

Write-Ico (Join-Path $outDir "cofre.ico") $false
Write-Ico (Join-Path $outDir "cofre-open.ico") $true

# 256px PNG used as the in-app logo (an ICO's first frame would be the 16px one).
[System.IO.File]::WriteAllBytes((Join-Path $outDir "logo.png"), (New-IconPng 256 $false))
