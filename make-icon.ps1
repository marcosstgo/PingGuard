Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

# ── Color helpers ─────────────────────────────────────────────────────────────
function C([int]$a,[int]$r,[int]$g,[int]$b) {
    [System.Windows.Media.Color]::FromArgb($a,$r,$g,$b)
}
function Brush([System.Windows.Media.Color]$c) {
    [System.Windows.Media.SolidColorBrush]::new($c)
}

# ── Render one size ───────────────────────────────────────────────────────────
function Render-Frame([int]$size) {

    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()

    $cx   = $size / 2.0
    $cy   = $size / 2.0
    $full = [System.Windows.Rect]::new(0, 0, $size, $size)
    $r    = $size * 0.205   # rounded corner radius

    # ── 1. Push rounded-rect clip ─────────────────────────────────────────────
    $clipGeo = New-Object System.Windows.Media.RectangleGeometry($full, $r, $r)
    $dc.PushClip($clipGeo)

    # ── 2. Background gradient (top-dark → bottom-darker) ────────────────────
    $bgBrush = New-Object System.Windows.Media.LinearGradientBrush
    $bgBrush.StartPoint = [System.Windows.Point]::new(0.5, 0)
    $bgBrush.EndPoint   = [System.Windows.Point]::new(0.5, 1)
    $bgBrush.GradientStops.Add([System.Windows.Media.GradientStop]::new((C 255 11 11 18), 0.0))
    $bgBrush.GradientStops.Add([System.Windows.Media.GradientStop]::new((C 255  4  4  8), 1.0))
    $dc.DrawRectangle($bgBrush, $null, $full)

    # ── 3. Radial green inner glow ────────────────────────────────────────────
    if ($size -ge 24) {
        $rg = New-Object System.Windows.Media.RadialGradientBrush
        $rg.GradientOrigin = [System.Windows.Point]::new(0.5, 0.5)
        $rg.Center         = [System.Windows.Point]::new(0.5, 0.5)
        $rg.RadiusX = 0.58; $rg.RadiusY = 0.58
        $rg.GradientStops.Add([System.Windows.Media.GradientStop]::new((C 55 74 222 128), 0.0))
        $rg.GradientStops.Add([System.Windows.Media.GradientStop]::new((C  0 74 222 128), 1.0))
        $dc.DrawRectangle($rg, $null, $full)
    }

    # ── 4. Radar rings ────────────────────────────────────────────────────────
    $rings = @(
        @{ Radius = 0.430; Alpha =  40; Width = [Math]::Max(0.7, $size * 0.009) },
        @{ Radius = 0.305; Alpha =  70; Width = [Math]::Max(0.7, $size * 0.012) },
        @{ Radius = 0.175; Alpha = 105; Width = [Math]::Max(0.7, $size * 0.016) }
    )
    $ctr = [System.Windows.Point]::new($cx, $cy)
    foreach ($ring in $rings) {
        if ($size -lt 32 -and $ring.Radius -gt 0.35) { continue }
        if ($size -lt 16) { continue }
        $rad = $size * $ring.Radius
        $pen = New-Object System.Windows.Media.Pen(
            (Brush (C $ring.Alpha 74 222 128)), $ring.Width)
        $dc.DrawEllipse($null, $pen, $ctr, $rad, $rad)
    }

    # ── 5. EKG line with neon glow (48px+) ───────────────────────────────────
    if ($size -ge 48) {

        # Points in normalized 0..1 space — sharp classic ECG shape
        $norm = @(
            @(0.118, 0.500),   # flat left
            @(0.292, 0.500),
            @(0.348, 0.400),   # slight pre-rise
            @(0.380, 0.082),   # R peak (tall spike)
            @(0.424, 0.768),   # S wave (below baseline)
            @(0.458, 0.500),   # return
            @(0.508, 0.392),   # T wave bump
            @(0.552, 0.548),
            @(0.596, 0.500),   # back to baseline
            @(0.882, 0.500)    # flat right
        )

        $geo = New-Object System.Windows.Media.StreamGeometry
        $sgc = $geo.Open()
        $first = [System.Windows.Point]::new($norm[0][0] * $size, $norm[0][1] * $size)
        $sgc.BeginFigure($first, $false, $false)
        for ($i = 1; $i -lt $norm.Length; $i++) {
            $sgc.LineTo([System.Windows.Point]::new($norm[$i][0]*$size, $norm[$i][1]*$size), $true, $false)
        }
        $sgc.Close()

        # Glow passes: wide+faint → narrow+bright
        $glowPasses = @(
            @{ Alpha = 20; Width = $size * 0.22  },   # far glow
            @{ Alpha = 42; Width = $size * 0.13  },   # mid glow
            @{ Alpha = 75; Width = $size * 0.075 }    # inner glow
        )
        foreach ($gp in $glowPasses) {
            $pen = New-Object System.Windows.Media.Pen(
                (Brush (C $gp.Alpha 74 222 128)), $gp.Width)
            $pen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
            $pen.EndLineCap   = [System.Windows.Media.PenLineCap]::Round
            $pen.LineJoin     = [System.Windows.Media.PenLineJoin]::Round
            $dc.DrawGeometry($null, $pen, $geo)
        }

        # Core line (crisp, bright)
        $corePen = New-Object System.Windows.Media.Pen(
            (Brush (C 240 74 222 128)), [Math]::Max(1.5, $size * 0.036))
        $corePen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
        $corePen.EndLineCap   = [System.Windows.Media.PenLineCap]::Round
        $corePen.LineJoin     = [System.Windows.Media.PenLineJoin]::Round
        $dc.DrawGeometry($null, $corePen, $geo)

        # White highlight on top of core (gives it that bright neon look)
        if ($size -ge 64) {
            $hlPen = New-Object System.Windows.Media.Pen(
                (Brush (C 90 210 255 220)), [Math]::Max(0.8, $size * 0.013))
            $hlPen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
            $hlPen.EndLineCap   = [System.Windows.Media.PenLineCap]::Round
            $hlPen.LineJoin     = [System.Windows.Media.PenLineJoin]::Round
            $dc.DrawGeometry($null, $hlPen, $geo)
        }

        # Fade strips left + right (mask line ends into background)
        $fadeW = $size * 0.155
        $bgMid = C 255 7 7 12

        $lf = New-Object System.Windows.Media.LinearGradientBrush
        $lf.StartPoint = [System.Windows.Point]::new(0, 0.5)
        $lf.EndPoint   = [System.Windows.Point]::new(1, 0.5)
        $lf.GradientStops.Add([System.Windows.Media.GradientStop]::new($bgMid, 0.0))
        $lf.GradientStops.Add([System.Windows.Media.GradientStop]::new((C 0 7 7 12), 1.0))
        $dc.DrawRectangle($lf, $null, [System.Windows.Rect]::new(0, 0, $fadeW, $size))

        $rf = New-Object System.Windows.Media.LinearGradientBrush
        $rf.StartPoint = [System.Windows.Point]::new(0, 0.5)
        $rf.EndPoint   = [System.Windows.Point]::new(1, 0.5)
        $rf.GradientStops.Add([System.Windows.Media.GradientStop]::new((C 0 7 7 12), 0.0))
        $rf.GradientStops.Add([System.Windows.Media.GradientStop]::new($bgMid, 1.0))
        $dc.DrawRectangle($rf, $null, [System.Windows.Rect]::new($size - $fadeW, 0, $fadeW, $size))
    }

    # ── 6. Center dot with multi-layer radial glow ────────────────────────────
    $dotR = [Math]::Max(2.8, $size * 0.062)

    # Outer glow
    foreach ($gf in @(3.8, 2.6, 1.7)) {
        if ($size -lt 32 -and $gf -gt 2.0) { continue }
        $alpha = switch ($gf) { 3.8 { 28 } 2.6 { 58 } 1.7 { 95 } }
        $gb = New-Object System.Windows.Media.RadialGradientBrush
        $gb.GradientOrigin = [System.Windows.Point]::new(0.5, 0.5)
        $gb.Center         = [System.Windows.Point]::new(0.5, 0.5)
        $gb.RadiusX = 1.0; $gb.RadiusY = 1.0
        $gb.GradientStops.Add([System.Windows.Media.GradientStop]::new((C $alpha 74 222 128), 0.0))
        $gb.GradientStops.Add([System.Windows.Media.GradientStop]::new((C 0 74 222 128), 1.0))
        $gr = $dotR * $gf
        $dc.DrawEllipse($gb, $null, $ctr, $gr, $gr)
    }

    # Solid dot
    $dc.DrawEllipse((Brush (C 255 90 238 148)), $null, $ctr, $dotR, $dotR)

    # Specular highlight (top-left sparkle)
    if ($size -ge 48) {
        $hlR = $dotR * 0.38
        $hlCtr = [System.Windows.Point]::new($cx - $dotR * 0.22, $cy - $dotR * 0.22)
        $dc.DrawEllipse((Brush (C 170 210 255 225)), $null, $hlCtr, $hlR, $hlR)
    }

    # ── 7. Top-edge glass sheen ───────────────────────────────────────────────
    if ($size -ge 48) {
        $gh = $size * 0.20
        $gb = New-Object System.Windows.Media.LinearGradientBrush
        $gb.StartPoint = [System.Windows.Point]::new(0.5, 0)
        $gb.EndPoint   = [System.Windows.Point]::new(0.5, 1)
        $gb.GradientStops.Add([System.Windows.Media.GradientStop]::new((C 22 255 255 255), 0.0))
        $gb.GradientStops.Add([System.Windows.Media.GradientStop]::new((C  0 255 255 255), 1.0))
        $dc.DrawRectangle($gb, $null, [System.Windows.Rect]::new(0, 0, $size, $gh))
    }

    $dc.Pop()   # pop clip
    $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)

    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return $ms.ToArray()
}

# ── Write .ico (PNG-based, multi-size) ───────────────────────────────────────
function Write-Ico([string]$path, [int[]]$sizes) {
    $frames  = @(); $offsets = @(); $lengths = @()
    $count   = $sizes.Count
    $dirSize = $count * 16
    $offset  = 6 + $dirSize

    foreach ($sz in $sizes) {
        $bytes   = Render-Frame $sz
        $frames += ,$bytes
        $offsets += $offset
        $lengths += $bytes.Length
        $offset  += $bytes.Length
    }

    $ms = New-Object System.IO.MemoryStream
    # ICO header
    $ms.Write([byte[]](0,0, 1,0, [byte]($count -band 0xFF), [byte](($count -shr 8) -band 0xFF)), 0, 6)
    # Directory
    for ($i = 0; $i -lt $count; $i++) {
        $sz = $sizes[$i]
        [byte]$w = if ($sz -ge 256) { 0 } else { [byte]$sz }
        [byte]$h = if ($sz -ge 256) { 0 } else { [byte]$sz }
        $entry = [byte[]]@(
            $w, $h, 0, 0, 1, 0, 32, 0,
            [byte]($lengths[$i] -band 0xFF), [byte](($lengths[$i] -shr  8) -band 0xFF),
            [byte](($lengths[$i] -shr 16) -band 0xFF), [byte](($lengths[$i] -shr 24) -band 0xFF),
            [byte]($offsets[$i] -band 0xFF), [byte](($offsets[$i] -shr  8) -band 0xFF),
            [byte](($offsets[$i] -shr 16) -band 0xFF), [byte](($offsets[$i] -shr 24) -band 0xFF)
        )
        $ms.Write($entry, 0, 16)
    }
    # Data
    foreach ($bytes in $frames) { $ms.Write($bytes, 0, $bytes.Length) }

    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
    Write-Host "ICO  → $path"
}

# ── Save PNG ──────────────────────────────────────────────────────────────────
function Write-Png([string]$path, [int]$size) {
    [System.IO.File]::WriteAllBytes($path, (Render-Frame $size))
    Write-Host "PNG  → $path  (${size}px)"
}

# ── Run ───────────────────────────────────────────────────────────────────────
$base = $PSScriptRoot

Write-Ico  (Join-Path $base "PingGuard\pinggua.ico")        @(16, 24, 32, 48, 64, 128, 256)
Write-Png  (Join-Path $base "PingGuard\pinggua-256.png")    256
Write-Png  (Join-Path $base "PingGuard\pinggua-512.png")    512

Write-Host "Done."
