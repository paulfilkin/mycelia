# Makes familyTree\Resources\Mycelia.ico from the icon drawings in familyTree\Ui\Theme.xaml, at 16, 20, 24, 32, 40,
# 48, 64, 128 and 256 pixels, each stored as PNG: IconSmallImage (three sources, heavier strokes) up to 40 pixels and
# IconImage from 48, as the Mycelia artwork sheet (Docs\Design\Artwork) says.  Run it with Windows PowerShell 5.1,
# which runs WPF on the single-threaded apartment it needs:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
# Run it again whenever the icon drawings change.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$root = Split-Path -Parent $PSScriptRoot
$theme = Join-Path $root 'familyTree\Ui\Theme.xaml'
$output = Join-Path $root 'familyTree\Resources\Mycelia.ico'

# The whole theme, so the drawings find the shapes they share.  The fonts' pack addresses are only text here.
$dictionary = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText($theme))
$large = $dictionary['IconImage'].Drawing
$small = $dictionary['IconSmallImage'].Drawing
if ($null -eq $large -or $null -eq $small) { throw "IconImage or IconSmallImage was not found in $theme." }

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($size in $sizes) {
    $drawing = if ($size -le 40) { $small } else { $large }
    $bounds = $drawing.Bounds
    $visual = New-Object Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $scale = $size / [Math]::Max($bounds.Width, $bounds.Height)
    $context.PushTransform((New-Object Windows.Media.ScaleTransform $scale, $scale))
    $context.PushTransform((New-Object Windows.Media.TranslateTransform (-$bounds.X), (-$bounds.Y)))
    $context.DrawDrawing($drawing)
    $context.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    , $stream.ToArray()
}

# ICO: a 6-byte header, a 16-byte entry per image, then the PNG data.
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
$file = [IO.File]::Create($output)
$writer = New-Object IO.BinaryWriter $file
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$pngs[$i].Length); $writer.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $writer.Write($png) }
$writer.Close()
Write-Host "Wrote $output ($((Get-Item $output).Length) bytes, $($sizes.Count) sizes)."
