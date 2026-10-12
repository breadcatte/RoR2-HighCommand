# Draws the package icon (256x256, drawn in code: no game assets). Output: package\icon.png
Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'

# background: dark blue-grey with a soft vignette
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 0, $size), [System.Drawing.Color]::FromArgb(255, 22, 30, 44), [System.Drawing.Color]::FromArgb(255, 8, 11, 18))
$g.FillRectangle($bg, 0, 0, $size, $size)

# map grid
$gridPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 120, 150, 190)), 1
for ($i = 16; $i -lt $size; $i += 32) {
    $g.DrawLine($gridPen, $i, 0, $i, $size)
    $g.DrawLine($gridPen, 0, $i, $size, $i)
}

# selection rectangle and three "heroes"
$green = [System.Drawing.Color]::FromArgb(255, 110, 230, 120)
$boxPen = New-Object System.Drawing.Pen $green, 3
$boxPen.DashStyle = 'Dash'
$g.DrawRectangle($boxPen, 40, 120, 176, 100)
$heroBrush = New-Object System.Drawing.SolidBrush $green
foreach ($p in @(@(72, 170), @(128, 150), @(184, 176))) {
    $g.FillEllipse($heroBrush, $p[0] - 14, $p[1] - 14, 28, 28)
}

# move order marker
$orange = [System.Drawing.Color]::FromArgb(255, 255, 163, 46)
$orangePen = New-Object System.Drawing.Pen $orange, 5
$g.DrawEllipse($orangePen, 100, 28, 56, 56)
$g.DrawLine($orangePen, 128, 84, 128, 118)

# title
$font = New-Object System.Drawing.Font 'Arial', 30, ([System.Drawing.FontStyle]::Bold)
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = 'Center'
$g.DrawString('HIGH COMMAND', $font, (New-Object System.Drawing.SolidBrush $orange), (New-Object System.Drawing.RectangleF 0, 224, 256, 36), $fmt)

$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'package'
New-Item -ItemType Directory -Force $out | Out-Null
$bmp.Save((Join-Path $out 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "icon written to $out\icon.png"

