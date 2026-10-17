# Generates src/iRacingDirector/Assets/director.ico (PNG-compressed, multi-size).
# Usage: powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\iRacingDirector\Assets\director.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$sizes = 256, 64, 48, 32, 24, 16

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function New-Color([int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb(255, $r, $g, $b) }
function New-Points([float[]]$xy) {
    $pts = for ($i = 0; $i -lt $xy.Length; $i += 2) { New-Object System.Drawing.PointF $xy[$i], $xy[$i + 1] }
    [System.Drawing.PointF[]]$pts
}

# GTP / hypercar in side profile, facing right. Drawn on a 256x256 grid and scaled to the icon size.
# Small sizes drop the fine details (fin, stripe) and zoom in a little so the shape still reads.
function Draw-Hypercar($g, [int]$s) {
    $detail = $s -ge 48
    $zoom = if ($s -le 24) { 1.0 } else { 0.9 }
    $g.TranslateTransform($s / 2, $s / 2)
    $g.ScaleTransform($s / 256 * $zoom, $s / 256 * $zoom)
    $g.TranslateTransform(-134, -145)   # car's visual centre (wheels and wing included) to the icon centre

    $white = New-Object System.Drawing.SolidBrush (New-Color 248 250 252)
    $navy = New-Object System.Drawing.SolidBrush (New-Color 11 17 32)
    $wheelY = 168; $wheelR = 22
    $wheelsX = 66, 202

    # Body, rear to nose: tall rear fender wrapping the rear wheel, teardrop canopy set forward,
    # tall front fender wrapping the front wheel, long low nose down to the splitter.
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddLines((New-Points @(18,172, 18,140)))
    $body.AddBezier(18,140, 28,124, 80,116, 100,128)
    $body.AddBezier(100,128, 112,102, 152,96, 172,110)
    $body.AddBezier(172,110, 180,118, 182,124, 186,126)
    $body.AddBezier(186,126, 206,114, 230,118, 238,136)
    $body.AddLines((New-Points @(238,136, 252,156, 254,172, 18,172)))
    $body.CloseFigure()

    # Wheel arches cut out of the body, so the wheels sit inside the fenders.
    $shape = New-Object System.Drawing.Region $body
    foreach ($cx in $wheelsX) {
        $arch = New-Object System.Drawing.Drawing2D.GraphicsPath
        $arch.AddEllipse($cx - $wheelR - 5, $wheelY - $wheelR - 5, ($wheelR + 5) * 2, ($wheelR + 5) * 2)
        $shape.Exclude($arch)
    }
    $g.FillRegion($white, $shape)

    # Rear wing: tall endplate, main plane, swan-neck pillar down to the tail.
    $g.FillPolygon($white, (New-Points @(12,98, 24,98, 24,140, 12,134)))
    $g.FillRectangle($white, 12, 98, 62, 11)
    $g.FillPolygon($white, (New-Points @(52,109, 60,109, 68,124, 60,126)))

    if ($detail) {
        # Red livery stripe along the side, between the arches.
        $g.SetClip($shape, [System.Drawing.Drawing2D.CombineMode]::Replace)
        $stripe = New-Object System.Drawing.SolidBrush (New-Color 239 68 68)
        $g.FillPolygon($stripe, (New-Points @(18,146, 254,140, 254,149, 18,155)))
        $g.ResetClip()
    }

    # Canopy glass.
    $glass = New-Object System.Drawing.SolidBrush (New-Color 30 58 138)
    $g.FillPolygon($glass, (New-Points @(116,128, 128,110, 152,104, 168,112, 162,126)))

    # Wheels: tyre, light rim, hub.
    $rim = New-Object System.Drawing.SolidBrush (New-Color 203 213 225)
    foreach ($cx in $wheelsX) {
        $g.FillEllipse($navy, $cx - $wheelR, $wheelY - $wheelR, $wheelR * 2, $wheelR * 2)
        $g.FillEllipse($rim, $cx - 11, $wheelY - 11, 22, 22)
        if ($detail) { $g.FillEllipse($navy, $cx - 4, $wheelY - 4, 8, 8) }
    }
    $g.ResetTransform()
}

$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Background: rounded square, blue gradient.
    $bg = New-RoundedPath 0 0 ($s - 1) ($s - 1) ($s * 0.22)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 37, 99, 235)), ([System.Drawing.Color]::FromArgb(255, 15, 23, 42)), 45
    $g.FillPath($brush, $bg)

    Draw-Hypercar $g $s
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Classic 32bpp DIB entry (BITMAPINFOHEADER + bottom-up BGRA + empty AND mask)
        # so that every Windows/GDI+ code path can read the small sizes.
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32]($s * 2))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
        $bw.Write([UInt32]($s * $s * 4 + $maskStride * $s))
        $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $s; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $bw.Write((New-Object byte[] ($maskStride * $s)))
        $bw.Flush()
    }
    $bmp.Dispose()
    , $ms.ToArray()
}

$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
"Wrote $out"
