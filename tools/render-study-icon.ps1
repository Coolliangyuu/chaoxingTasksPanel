param(
    [Parameter(Mandatory = $true)][string]$IconPath,
    [Parameter(Mandatory = $true)][string]$PreviewDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function HexColor([string]$Hex) {
    [System.Drawing.ColorTranslator]::FromHtml($Hex)
}

function RoundedPath([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    $path
}

function RenderIcon([int]$Size) {
    $side = [Math]::Max(512, $Size * 3)
    $canvas = [System.Drawing.Bitmap]::new($side, $side, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($side / 256.0, $side / 256.0)

    $outerShadow = RoundedPath 11 16 234 233 55
    $shadowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(36, (HexColor '#071E2B')))
    $g.FillPath($shadowBrush, $outerShadow)
    $tile = RoundedPath 10 9 236 234 55
    $tileGradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new(10, 9, 236, 234),
        (HexColor '#177D91'), (HexColor '#123D59'), [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($tileGradient, $tile)
    $rim = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(85, (HexColor '#C6F5F4')), 2.4)
    $g.DrawPath($rim, $tile)

    $sheet = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $sheet.StartFigure()
    $sheet.AddLine(74, 41, 132, 41)
    $sheet.AddBezier(132, 41, 138, 41, 142, 44, 147, 49)
    $sheet.AddLine(147, 49, 172, 74)
    $sheet.AddBezier(172, 74, 177, 79, 179, 83, 179, 90)
    $sheet.AddLine(179, 90, 179, 179)
    $sheet.AddBezier(179, 179, 179, 190, 172, 197, 161, 197)
    $sheet.AddLine(161, 197, 74, 197)
    $sheet.AddBezier(74, 197, 63, 197, 56, 190, 56, 179)
    $sheet.AddLine(56, 179, 56, 59)
    $sheet.AddBezier(56, 59, 56, 48, 63, 41, 74, 41)
    $sheet.CloseFigure()

    $paperShadow = $sheet.Clone()
    $offset = [System.Drawing.Drawing2D.Matrix]::new()
    $offset.Translate(4, 6)
    $paperShadow.Transform($offset)
    $paperShadowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(82, (HexColor '#092A3D')))
    $g.FillPath($paperShadowBrush, $paperShadow)
    $paper = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new(56, 41, 123, 156),
        (HexColor '#FFFDFC'), (HexColor '#D9EEEF'), [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($paper, $sheet)

    $fold = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $fold.AddLine(133, 42, 133, 69)
    $fold.AddBezier(133, 69, 133, 79, 139, 85, 149, 85)
    $fold.AddLine(149, 85, 178, 85)
    $fold.CloseFigure()
    $foldBrush = [System.Drawing.SolidBrush]::new((HexColor '#96CDCF'))
    $g.FillPath($foldBrush, $fold)

    $check = [System.Drawing.Pen]::new((HexColor '#1E8995'), 8)
    $check.StartCap = $check.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $check.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $rule = [System.Drawing.Pen]::new((HexColor '#3F8290'), 8)
    $rule.StartCap = $rule.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($lineY in @(99, 126, 153)) {
        $g.DrawLines($check, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(77, $lineY),
            [System.Drawing.PointF]::new(82, $lineY + 5),
            [System.Drawing.PointF]::new(91, $lineY - 5)))
        $lineEnd = if ($lineY -eq 153) { 122 } else { 151 }
        $g.DrawLine($rule, 106, $lineY, $lineEnd, $lineY)
    }

    $clockRim = [System.Drawing.SolidBrush]::new((HexColor '#123F55'))
    $g.FillEllipse($clockRim, 126, 122, 101, 101)
    $clockFace = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new(134, 130, 85, 85),
        (HexColor '#FFE1A4'), (HexColor '#F3A849'), [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillEllipse($clockFace, 134, 130, 85, 85)
    $hand = [System.Drawing.Pen]::new((HexColor '#183F55'), 9)
    $hand.StartCap = $hand.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $hand.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($hand, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(176, 149),
        [System.Drawing.PointF]::new(176, 173),
        [System.Drawing.PointF]::new(193, 183)))

    foreach ($item in @($outerShadow, $shadowBrush, $tile, $tileGradient, $rim, $sheet, $paperShadow, $offset,
                       $paperShadowBrush, $paper, $fold, $foldBrush, $check, $rule, $clockRim, $clockFace, $hand, $g)) {
        $item.Dispose()
    }
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $downsample = [System.Drawing.Graphics]::FromImage($bitmap)
    $downsample.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $downsample.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $downsample.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $downsample.DrawImage($canvas, 0, 0, $Size, $Size)
    $downsample.Dispose()
    $canvas.Dispose()
    $bitmap
}

New-Item -ItemType Directory -Force -Path $PreviewDirectory | Out-Null
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$images = foreach ($size in $sizes) {
    $bitmap = RenderIcon $size
    $memory = [System.IO.MemoryStream]::new()
    try {
        $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
        if ($size -in 16, 32, 256) {
            $bitmap.Save((Join-Path $PreviewDirectory "icon-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        [pscustomobject]@{ Size = $size; Png = $memory.ToArray() }
    }
    finally {
        $memory.Dispose()
        $bitmap.Dispose()
    }
}

$stream = [System.IO.File]::Create($IconPath)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)
    $position = 6 + $images.Count * 16
    foreach ($entry in $images) {
        $encodedSize = [byte]($entry.Size % 256)
        $writer.Write($encodedSize)
        $writer.Write($encodedSize)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Png.Length)
        $writer.Write([uint32]$position)
        $position += $entry.Png.Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry.Png) }
}
finally {
    $writer.Dispose()
}

