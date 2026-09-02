# Workshop preview image for "Disaster +".
#
# 512x512 is the house convention (CSWARFRONT / KAIJU both ship that size).
# One hero shot -- the eruption -- with the title over a fade at the bottom.
#
# It was a three-panel triptych first (volcano / typhoon / tsunami). The owner
# cut it down to the eruption alone: at 512 px a Workshop tile is read at a
# glance, and one strong image beats three weak slivers. The typhoon shot in
# particular was a white cloud on grey sky, which says nothing at thumbnail size.
#
# Usage:  powershell -File tools\make-preview.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'PreviewImage.png'

# NOTE: the shots have Japanese file names. Do NOT paste them into this script --
# Windows PowerShell 5.1 reads a .ps1 as ANSI unless it carries a BOM, so the
# literals arrive mojibake'd and FromFile throws FileNotFoundException.
# Match on the timestamp tail instead; that part is pure ASCII.
$match = '*105338.png'

# The square window taken from the source, as fractions.
#   Side    - side length, as a fraction of the source height.
#             0.93 crops in slightly so the cone fills the frame edge to edge.
#   CentreX - horizontal centre. 0.70 puts the summit just right of centre and
#             keeps the whole plume, which leans left as it rises.
#   Top     - top edge. 0 keeps the plume; the foreground grass is what falls off.
$side = 0.93
$centreX = 0.70
$top = 0.00

$size = 512
$fadeH = 190                 # tall, so the title sits on a soft ground
$titleBaseline = 372         # top of the "DISASTER +" glyphs

$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear([System.Drawing.Color]::FromArgb(255, 12, 14, 18))

# ── hero ──────────────────────────────────────────────────────────────
$found = @(Get-ChildItem -LiteralPath $root -Filter $match -File)
if ($found.Count -ne 1) {
    throw ("expected exactly one screenshot matching " + $match +
           " in " + $root + ", found " + $found.Count)
}

$src = [System.Drawing.Image]::FromFile($found[0].FullName)
try {
    $win = [int]([Math]::Round($src.Height * $side))
    if ($win -gt $src.Width) { $win = $src.Width }

    $srcY = [int]([Math]::Round($src.Height * $top))
    if ($srcY + $win -gt $src.Height) { $srcY = $src.Height - $win }
    if ($srcY -lt 0) { $srcY = 0 }

    $srcX = [int]([Math]::Round($src.Width * $centreX - $win / 2.0))
    if ($srcX -lt 0) { $srcX = 0 }
    if ($srcX + $win -gt $src.Width) { $srcX = $src.Width - $win }

    $dest = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $g.DrawImage($src, $dest, $srcX, $srcY, $win, $win,
                 [System.Drawing.GraphicsUnit]::Pixel)
} finally {
    $src.Dispose()
}

# ── fade, so the title reads over the bright grass ────────────────────
$fadeRect = New-Object System.Drawing.Rectangle(0, ($size - $fadeH), $size, $fadeH)
$fade = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $fadeRect,
    [System.Drawing.Color]::FromArgb(0, 8, 10, 14),
    [System.Drawing.Color]::FromArgb(248, 8, 10, 14),
    [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
$g.FillRectangle($fade, $fadeRect)
$fade.Dispose()

# ── title ─────────────────────────────────────────────────────────────
function Draw-Centred([string]$text, [string]$family, [single]$emSize,
                      [int]$style, [int]$topY, [System.Drawing.Color]$fill,
                      [single]$outline, [System.Drawing.Color]$outlineColor) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fam = New-Object System.Drawing.FontFamily($family)
    try {
        $fmt = New-Object System.Drawing.StringFormat
        $path.AddString($text, $fam, $style, $emSize,
                        (New-Object System.Drawing.PointF(0, 0)), $fmt)
        $b = $path.GetBounds()

        $m = New-Object System.Drawing.Drawing2D.Matrix
        $m.Translate(($size - $b.Width) / 2.0 - $b.X, $topY - $b.Y)
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
$ink = [System.Drawing.Color]::FromArgb(255, 8, 10, 14)

# hazard-orange rule, sized to the title, sitting just above it
$ruleW = 300
$ruleRect = New-Object System.Drawing.Rectangle((($size - $ruleW) / 2), ($titleBaseline - 22), $ruleW, 3)
$rule = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $ruleRect,
    [System.Drawing.Color]::FromArgb(255, 232, 88, 24),
    [System.Drawing.Color]::FromArgb(255, 250, 182, 56),
    [System.Drawing.Drawing2D.LinearGradientMode]::Horizontal)
$g.FillRectangle($rule, $ruleRect)
$rule.Dispose()

Draw-Centred 'DISASTER +' 'Arial Black' 52 $bold $titleBaseline `
    ([System.Drawing.Color]::White) 5 $ink
Draw-Centred 'EARTHQUAKE - TSUNAMI - TYPHOON - VOLCANO - FIRE WHIRL' `
    'Arial' 12.5 $bold ($titleBaseline + 72) `
    ([System.Drawing.Color]::FromArgb(255, 208, 216, 228)) 3 $ink

$g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

$info = Get-Item $out
Write-Host ("wrote {0}  ({1:N0} bytes)" -f $info.FullName, $info.Length)
