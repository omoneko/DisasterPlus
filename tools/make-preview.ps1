# Workshop preview image for "Disaster +".
#
# 512x512 is the house convention (CSWARFRONT / KAIJU both ship that size).
# Three portrait panels — volcano, typhoon, tsunami — under one title band.
#
# Usage:  powershell -File tools\make-preview.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'PreviewImage.png'

# Source shots and the portrait window to take from each.
#
# CentreX is a fraction of the source width: it decides what stays in frame
# once the shot is cropped to a tall, narrow panel.
#
# Weight is the panel's share of the 512 px. They are NOT equal on purpose --
# the eruption is the widest subject (cone plus the plume leaning off it) and
# gets squeezed into an unreadable sliver at one third. The typhoon shot is
# mostly sky, so it survives being the narrowest.
# NOTE: the shots have Japanese file names. Do NOT paste them into this script --
# Windows PowerShell 5.1 reads a .ps1 as ANSI unless it carries a BOM, so the
# literals arrive mojibake'd and FromFile throws FileNotFoundException.
# Match on the timestamp tail instead; that part is pure ASCII.
$panels = @(
    @{ Match = '*105338.png'; CentreX = 0.68; Top = 0.00; Height = 1.00; Weight = 0.38 },
    @{ Match = '*105407.png'; CentreX = 0.62; Top = 0.00; Height = 1.00; Weight = 0.29 },
    @{ Match = '*115328.png'; CentreX = 0.58; Top = 0.00; Height = 1.00; Weight = 0.33 }
)

$size = 512
$gap = 3                     # hairline between panels
$titleBand = 92              # solid strip at the bottom

$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

$g.Clear([System.Drawing.Color]::FromArgb(255, 12, 14, 18))

# ── panels ────────────────────────────────────────────────────────────
$panelH = $size - $titleBand
$totalGap = $gap * ($panels.Count - 1)
$usable = $size - $totalGap
$x = 0

for ($i = 0; $i -lt $panels.Count; $i++) {
    $spec = $panels[$i]
    # last panel absorbs the rounding so the strip ends exactly at 512
    $w = if ($i -eq $panels.Count - 1) { $size - $x }
         else { [int][Math]::Round($usable * $spec.Weight) }

    $found = @(Get-ChildItem -LiteralPath $root -Filter $spec.Match -File)
    if ($found.Count -ne 1) {
        throw ("expected exactly one screenshot matching " + $spec.Match +
               " in " + $root + ", found " + $found.Count)
    }
    $src = [System.Drawing.Image]::FromFile($found[0].FullName)
    try {
        $srcTop = [int]($src.Height * $spec.Top)
        $srcH = [int]($src.Height * $spec.Height)
        if ($srcTop + $srcH -gt $src.Height) { $srcH = $src.Height - $srcTop }

        # widest window that still fills the panel without stretching
        $srcW = [int]([Math]::Round($srcH * ($w / [double]$panelH)))
        if ($srcW -gt $src.Width) { $srcW = $src.Width }

        $srcX = [int]([Math]::Round($src.Width * $spec.CentreX - $srcW / 2.0))
        if ($srcX -lt 0) { $srcX = 0 }
        if ($srcX + $srcW -gt $src.Width) { $srcX = $src.Width - $srcW }

        $dest = New-Object System.Drawing.Rectangle($x, 0, $w, $panelH)
        $g.DrawImage($src, $dest, $srcX, $srcTop, $srcW, $srcH,
                     [System.Drawing.GraphicsUnit]::Pixel)
    } finally {
        $src.Dispose()
    }
    $x += $w + $gap
}

# ── bottom fade into the title band, so the seam is not a hard line ───
$fadeH = 88
$fadeRect = New-Object System.Drawing.Rectangle(0, ($panelH - $fadeH), $size, $fadeH)
$fade = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $fadeRect,
    [System.Drawing.Color]::FromArgb(0, 10, 12, 16),
    [System.Drawing.Color]::FromArgb(255, 10, 12, 16),
    [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
$g.FillRectangle($fade, $fadeRect)
$fade.Dispose()

$bandBrush = New-Object System.Drawing.SolidBrush(
    [System.Drawing.Color]::FromArgb(255, 10, 12, 16))
$g.FillRectangle($bandBrush, 0, $panelH, $size, $titleBand)
$bandBrush.Dispose()

# thin warning-orange rule above the title, echoing the hazard theme
$rule = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle(0, ($panelH - 3), $size, 3)),
    [System.Drawing.Color]::FromArgb(255, 232, 96, 32),
    [System.Drawing.Color]::FromArgb(255, 248, 176, 48),
    [System.Drawing.Drawing2D.LinearGradientMode]::Horizontal)
$g.FillRectangle($rule, 0, ($panelH - 3), $size, 3)
$rule.Dispose()

# ── title ─────────────────────────────────────────────────────────────
function Draw-Centred([string]$text, [string]$family, [single]$emSize,
                      [int]$style, [int]$baselineY, [System.Drawing.Color]$fill,
                      [single]$outline, [System.Drawing.Color]$outlineColor) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fam = New-Object System.Drawing.FontFamily($family)
    try {
        $fmt = New-Object System.Drawing.StringFormat
        $path.AddString($text, $fam, $style, $emSize,
                        (New-Object System.Drawing.PointF(0, 0)), $fmt)
        $b = $path.GetBounds()

        $m = New-Object System.Drawing.Drawing2D.Matrix
        $m.Translate(($size - $b.Width) / 2.0 - $b.X, $baselineY - $b.Y)
        $path.Transform($m)
        $m.Dispose()

        if ($outline -gt 0) {
            $pen = New-Object System.Drawing.Pen($outlineColor, $outline)
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $g.DrawPath($pen, $path)
            $pen.Dispose()
        }
        $br = New-Object System.Drawing.SolidBrush($fill)
        $g.FillPath($br, $path)
        $br.Dispose()
        $fmt.Dispose()
    } finally {
        $path.Dispose(); $fam.Dispose()
    }
}

$bold = [int][System.Drawing.FontStyle]::Bold
$white = [System.Drawing.Color]::White
$ink = [System.Drawing.Color]::FromArgb(255, 10, 12, 16)

Draw-Centred 'DISASTER +' 'Arial Black' 46 $bold ($panelH + 8) $white 5 $ink
Draw-Centred 'EARTHQUAKE - TSUNAMI - TYPHOON - VOLCANO - FIRE WHIRL' `
    'Arial' 12.5 $bold ($panelH + 66) `
    ([System.Drawing.Color]::FromArgb(255, 214, 220, 230)) 0 $ink

$g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

$info = Get-Item $out
Write-Host ("wrote {0}  ({1:N0} bytes)" -f $info.FullName, $info.Length)
