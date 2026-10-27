# One geometric vault/C mark for Windows, browser toolbar and form fields.
# Run after editing this file: powershell -NoProfile -File tools/make-icons.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$outDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\Cofre.App\Assets'))
$extensionDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\extension\icons'))
New-Item -ItemType Directory -Force $outDir, $extensionDir | Out-Null
function New-IconPng([int]$size, [bool]$unlocked) {
    $bmp = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)
    $bg = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#242a20'))
    $ink = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#c4d3a8'))
    $bolt = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#f1f1e9'))
    $s = [single]$size
    $g.FillRectangle($bg, 0, 0, $s, $s)
    # The C is cut from an octagonal vault outline. A separate horizontal bolt closes its opening.
    $g.TranslateTransform($s * 0.10, $s * 0.10)
    $g.ScaleTransform($s * 0.80 / 32, $s * 0.80 / 32)
    $points = [Drawing.PointF[]]@(
        [Drawing.PointF]::new(9,3), [Drawing.PointF]::new(26,3), [Drawing.PointF]::new(26,9),
        [Drawing.PointF]::new(12,9), [Drawing.PointF]::new(9,12), [Drawing.PointF]::new(9,20),
        [Drawing.PointF]::new(12,23), [Drawing.PointF]::new(26,23), [Drawing.PointF]::new(26,29),
        [Drawing.PointF]::new(9,29), [Drawing.PointF]::new(3,23), [Drawing.PointF]::new(3,9))
    $g.FillPolygon($ink, $points)
    $g.FillRectangle($bolt, [single]17, [single]13, [single]12, [single]6)
    $g.ResetTransform()
    if ($unlocked) {
        $g.FillEllipse($bg, $s * 0.70, $s * 0.70, $s * 0.29, $s * 0.29)
        $g.FillEllipse($ink, $s * 0.75, $s * 0.75, $s * 0.19, $s * 0.19)
    }
    $g.Dispose(); $bg.Dispose(); $ink.Dispose(); $bolt.Dispose()
    $ms = [IO.MemoryStream]::new()
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $bytes = $ms.ToArray(); $ms.Dispose()
    return ,$bytes
}
function Write-Ico([string]$path, [bool]$unlocked) {
    $sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
    $images = foreach ($size in $sizes) { ,(New-IconPng $size $unlocked) }
    $stream = [IO.File]::Create($path)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dim = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
            $offset += $images[$i].Length
        }
        foreach ($image in $images) { $writer.Write($image) }
    } finally { $writer.Dispose(); $stream.Dispose() }
}
Write-Ico (Join-Path $outDir 'cofre.ico') $false
Write-Ico (Join-Path $outDir 'cofre-open.ico') $true
[IO.File]::WriteAllBytes((Join-Path $outDir 'logo.png'), (New-IconPng 256 $false))
foreach ($size in 16,32,48,128) {
    [IO.File]::WriteAllBytes((Join-Path $extensionDir "cofre-$size.png"), (New-IconPng $size $false))
}
Write-Output 'Marca atualizada no aplicativo e na extensão.'
