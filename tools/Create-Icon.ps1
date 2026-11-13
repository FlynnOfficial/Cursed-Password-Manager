Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$frames = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size / 256.0, $size / 256.0)
    $g.Clear([Drawing.Color]::Transparent)
    $shadow = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(55,20,10,45))
    $g.FillEllipse($shadow,36,219,195,25)
    $arc = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#545F85'),23)
    $g.DrawArc($arc,68,22,118,153,180,180)
    $silver = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#DCEAFF'),14)
    $g.DrawArc($silver,66,19,118,153,180,180)
    $rect = [Drawing.Rectangle]::new(35,103,188,126)
    $brush = [Drawing.Drawing2D.LinearGradientBrush]::new($rect,[Drawing.ColorTranslator]::FromHtml('#8C77EC'),[Drawing.ColorTranslator]::FromHtml('#2349AA'),60.0)
    $g.FillRectangle($brush,$rect)
    $outline = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#24356E'),4)
    $g.DrawRectangle($outline,$rect)
    $shine = [Drawing.Pen]::new([Drawing.Color]::FromArgb(180,220,230,255),4)
    $g.DrawLine($shine,40,108,216,108)
    $g.DrawLine($shine,40,108,40,219)
    $gold = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#FFDD61'))
    $g.FillEllipse($gold,104,138,46,46)
    $points = [Drawing.Point[]]@([Drawing.Point]::new(118,169),[Drawing.Point]::new(136,169),[Drawing.Point]::new(145,202),[Drawing.Point]::new(109,202))
    $g.FillPolygon($gold,$points)
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $root 'assets/cursed.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $g.Dispose(); $bitmap.Dispose(); $stream.Dispose()
    foreach ($item in @($shadow,$arc,$silver,$brush,$outline,$shine,$gold)) { $item.Dispose() }
}
$file = [IO.File]::Create((Join-Path $root 'assets/cursed.ico'))
$writer = [IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
$sizes = @(16,24,32,48,64,128,256)
for ($i=0; $i -lt $frames.Count; $i++) {
    $dimension = $sizes[$i] % 256
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
$writer.Dispose()
