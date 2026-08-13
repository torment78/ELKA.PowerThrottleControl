param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

function New-RoundedRectanglePath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $scale = $size / 256.0
    $bitmap = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([Drawing.Color]::Transparent)

    $backgroundPath = New-RoundedRectanglePath (8 * $scale) (8 * $scale) (240 * $scale) (240 * $scale) (47 * $scale)
    $backgroundBrush = New-Object Drawing.Drawing2D.LinearGradientBrush(
        ([Drawing.PointF]::new(20 * $scale, 18 * $scale)),
        ([Drawing.PointF]::new(236 * $scale, 238 * $scale)),
        ([Drawing.Color]::FromArgb(255, 29, 37, 49)),
        ([Drawing.Color]::FromArgb(255, 11, 15, 21)))
    $graphics.FillPath($backgroundBrush, $backgroundPath)
    $borderPen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255, 55, 69, 87)), (4 * $scale)
    $graphics.DrawPath($borderPen, $backgroundPath)

    $trackPen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255, 48, 59, 73)), (25 * $scale)
    $trackPen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $trackPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $arcRect = [Drawing.RectangleF]::new(46 * $scale, 49 * $scale, 164 * $scale, 164 * $scale)
    $graphics.DrawArc($trackPen, $arcRect, 205, 130)

    $segments = @(
        @{ Start = 205; Sweep = 27; Color = [Drawing.Color]::FromArgb(255, 37, 184, 220) },
        @{ Start = 239; Sweep = 27; Color = [Drawing.Color]::FromArgb(255, 72, 191, 105) },
        @{ Start = 273; Sweep = 27; Color = [Drawing.Color]::FromArgb(255, 242, 140, 40) },
        @{ Start = 307; Sweep = 27; Color = [Drawing.Color]::FromArgb(255, 220, 72, 82) }
    )
    foreach ($segment in $segments) {
        $pen = New-Object Drawing.Pen $segment.Color, (17 * $scale)
        $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawArc($pen, $arcRect, $segment.Start, $segment.Sweep)
        $pen.Dispose()
    }

    $center = [Drawing.PointF]::new(128 * $scale, 150 * $scale)
    $needleAngle = 316 * [Math]::PI / 180
    $needleEnd = [Drawing.PointF]::new(
        ($center.X + [Math]::Cos($needleAngle) * 67 * $scale),
        ($center.Y + [Math]::Sin($needleAngle) * 67 * $scale))
    $needleShadow = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(150, 0, 0, 0)), (15 * $scale)
    $needleShadow.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $needleShadow.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($needleShadow, $center.X + 2 * $scale, $center.Y + 3 * $scale, $needleEnd.X + 2 * $scale, $needleEnd.Y + 3 * $scale)
    $needlePen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255, 242, 140, 40)), (10 * $scale)
    $needlePen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $needlePen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($needlePen, $center, $needleEnd)

    $hubOuter = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 224, 231, 239))
    $hubInner = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 242, 140, 40))
    $graphics.FillEllipse($hubOuter, 105 * $scale, 127 * $scale, 46 * $scale, 46 * $scale)
    $graphics.FillEllipse($hubInner, 114 * $scale, 136 * $scale, 28 * $scale, 28 * $scale)

    $boltPoints = [Drawing.PointF[]]@(
        ([Drawing.PointF]::new(119 * $scale, 182 * $scale)),
        ([Drawing.PointF]::new(143 * $scale, 182 * $scale)),
        ([Drawing.PointF]::new(132 * $scale, 201 * $scale)),
        ([Drawing.PointF]::new(145 * $scale, 201 * $scale)),
        ([Drawing.PointF]::new(119 * $scale, 230 * $scale)),
        ([Drawing.PointF]::new(127 * $scale, 207 * $scale)),
        ([Drawing.PointF]::new(114 * $scale, 207 * $scale))
    )
    $boltBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 37, 184, 220))
    $graphics.FillPolygon($boltBrush, $boltPoints)

    $boltBrush.Dispose(); $hubInner.Dispose(); $hubOuter.Dispose()
    $needlePen.Dispose(); $needleShadow.Dispose(); $trackPen.Dispose()
    $borderPen.Dispose(); $backgroundBrush.Dispose(); $backgroundPath.Dispose(); $graphics.Dispose()
    return $bitmap
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) {
        $bitmap.Save((Join-Path $OutputDirectory 'ELKA.PowerThrottleControl.png'), [Drawing.Imaging.ImageFormat]::Png)
    }
    $frames += ,$stream.ToArray()
    $stream.Dispose(); $bitmap.Dispose()
}

$iconPath = Join-Path $OutputDirectory 'ELKA.PowerThrottleControl.ico'
$fileStream = [IO.File]::Create($iconPath)
$writer = New-Object IO.BinaryWriter $fileStream
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)
$offset = 6 + (16 * $frames.Count)
for ($index = 0; $index -lt $frames.Count; $index++) {
    $size = $sizes[$index]
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$index].Length)
    $writer.Write([uint32]$offset)
    $offset += $frames[$index].Length
}
foreach ($frame in $frames) {
    $writer.Write($frame)
}
$writer.Dispose(); $fileStream.Dispose()

Write-Output $iconPath

