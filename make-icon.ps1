Add-Type -AssemblyName System.Drawing

function Draw-PingIcon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode    = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.CompositingMode  = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    $g.Clear([System.Drawing.Color]::Transparent)

    $cx = $size / 2.0
    $cy = $size / 2.0

    # ── Background rounded square ────────────────────────────────────────────
    $r   = [int]($size * 0.22)
    $bg  = [System.Drawing.Color]::FromArgb(255, 8, 8, 14)
    $bgB = New-Object System.Drawing.SolidBrush($bg)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r*2, $r*2, 180, 90)
    $path.AddArc($size - $r*2, 0, $r*2, $r*2, 270, 90)
    $path.AddArc($size - $r*2, $size - $r*2, $r*2, $r*2, 0, 90)
    $path.AddArc(0, $size - $r*2, $r*2, $r*2, 90, 90)
    $path.CloseFigure()
    $g.FillPath($bgB, $path)

    # ── Subtle inner glow (dark green tint) ──────────────────────────────────
    if ($size -ge 32) {
        $glowR = [int]($size * 0.52)
        for ($i = $glowR; $i -gt 0; $i -= 2) {
            $alpha = [int](18 * ($i / $glowR))
            $gb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($alpha, 74, 222, 128))
            $g.FillEllipse($gb, $cx - $i, $cy - $i, $i*2, $i*2)
            $gb.Dispose()
        }
    }

    # ── Radar rings ──────────────────────────────────────────────────────────
    $ringSpecs = @(
        @{ R = 0.44; A = 40; W = [Math]::Max(1.0, $size * 0.018) },
        @{ R = 0.31; A = 70; W = [Math]::Max(1.0, $size * 0.022) },
        @{ R = 0.18; A = 100; W = [Math]::Max(1.0, $size * 0.026) }
    )

    foreach ($spec in $ringSpecs) {
        if ($size -lt 24 -and $spec.R -gt 0.35) { continue }
        $rd  = [float]($size * $spec.R)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb($spec.A, 74, 222, 128), $spec.W)
        $pen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Solid
        $g.DrawEllipse($pen, [float]($cx - $rd), [float]($cy - $rd), [float]($rd * 2), [float]($rd * 2))
        $pen.Dispose()
    }

    # ── EKG pulse line (only 48px+) ──────────────────────────────────────────
    if ($size -ge 48) {
        $s  = $size / 256.0
        $pts = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(  30 * $s, 128 * $s),
            [System.Drawing.PointF]::new(  76 * $s, 128 * $s),
            [System.Drawing.PointF]::new(  96 * $s,  72 * $s),
            [System.Drawing.PointF]::new( 116 * $s, 192 * $s),
            [System.Drawing.PointF]::new( 136 * $s, 104 * $s),
            [System.Drawing.PointF]::new( 156 * $s, 152 * $s),
            [System.Drawing.PointF]::new( 176 * $s, 128 * $s),
            [System.Drawing.PointF]::new( 226 * $s, 128 * $s)
        )
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, 74, 222, 128), [float]($size * 0.052))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawLines($pen, $pts)
        $pen.Dispose()

        # Fade ends with dark overlay strips
        $fade = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(200, 8, 8, 14))
        $fadeW = [int]($size * 0.12)
        $g.FillRectangle($fade, 0, 0, $fadeW, $size)
        $g.FillRectangle($fade, $size - $fadeW, 0, $fadeW, $size)
        $fade.Dispose()

        # Clip everything to rounded rect
        $g.SetClip($path)
    }

    # ── Center dot ───────────────────────────────────────────────────────────
    $dotR = [float]([Math]::Max(2.5, $size * 0.072))
    # Outer glow of dot
    $glowPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 74, 222, 128), $dotR * 1.4)
    $g.DrawEllipse($glowPen, [float]($cx - $dotR * 0.7), [float]($cy - $dotR * 0.7), [float]($dotR * 1.4), [float]($dotR * 1.4))
    $glowPen.Dispose()
    # Solid dot
    $dotB = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 74, 222, 128))
    $g.FillEllipse($dotB, [float]($cx - $dotR), [float]($cy - $dotR), [float]($dotR * 2), [float]($dotR * 2))
    $dotB.Dispose()

    $g.Dispose()
    $bgB.Dispose()
    return $bmp
}

# ── Write multi-size .ico ────────────────────────────────────────────────────
function Write-Ico([string]$outPath, [int[]]$sizes) {
    $stream = New-Object System.IO.MemoryStream

    # ICO header: RESERVED(2) TYPE(2) COUNT(2)
    $count = $sizes.Count
    [byte[]]$header = @(0,0, 1,0, [byte]($count -band 0xFF), [byte](($count -shr 8) -band 0xFF))
    $stream.Write($header, 0, $header.Length)

    # Reserve space for directory entries (16 bytes each)
    $dirSize   = $count * 16
    $dataOffset = 6 + $dirSize
    $stream.Write((New-Object byte[] $dirSize), 0, $dirSize)

    $bitmaps   = @()
    $offsets   = @()
    $lengths   = @()

    foreach ($sz in $sizes) {
        $bmp     = Draw-PingIcon $sz
        $bmpStream = New-Object System.IO.MemoryStream
        $bmp.Save($bmpStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes   = $bmpStream.ToArray()
        $bitmaps += $bmpStream
        $offsets += $dataOffset
        $lengths += $bytes.Length
        $dataOffset += $bytes.Length
        $bmp.Dispose()
    }

    # Write directory
    $stream.Position = 6
    for ($i = 0; $i -lt $count; $i++) {
        $sz = $sizes[$i]
        [byte]$w  = if ($sz -ge 256) { 0 } else { [byte]$sz }
        [byte]$h  = if ($sz -ge 256) { 0 } else { [byte]$sz }
        [byte[]]$entry = @(
            $w, $h,          # width, height (0 = 256)
            0, 0,            # color count, reserved
            1, 0,            # planes
            32, 0,           # bit count
            [byte]($lengths[$i] -band 0xFF),
            [byte](($lengths[$i] -shr 8)  -band 0xFF),
            [byte](($lengths[$i] -shr 16) -band 0xFF),
            [byte](($lengths[$i] -shr 24) -band 0xFF),
            [byte]($offsets[$i] -band 0xFF),
            [byte](($offsets[$i] -shr 8)  -band 0xFF),
            [byte](($offsets[$i] -shr 16) -band 0xFF),
            [byte](($offsets[$i] -shr 24) -band 0xFF)
        )
        $stream.Write($entry, 0, 16)
    }

    # Write PNG data
    $stream.Position = $stream.Length
    for ($i = 0; $i -lt $count; $i++) {
        $bytes = $bitmaps[$i].ToArray()
        $stream.Write($bytes, 0, $bytes.Length)
        $bitmaps[$i].Dispose()
    }

    [System.IO.File]::WriteAllBytes($outPath, $stream.ToArray())
    $stream.Dispose()
    Write-Host "OK: $outPath"
}

$out = Join-Path $PSScriptRoot "PingGuard\pinggua.ico"
Write-Ico $out @(16, 24, 32, 48, 64, 128, 256)
Write-Host "Icon saved to $out"
