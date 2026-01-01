# Generate Obfy Icon (PNG and ICO)
# Uses WPF to draw and export the icon

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$OutputDir = Join-Path $PSScriptRoot "..\Src\Obfy.UI\Images"

function New-ObfyIcon {
    param([int]$Size)

    $scale = $Size / 256.0

    # Create a drawing visual
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()

    # Apply scale transform
    $context.PushTransform((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))

    # Define colors - vibrant purple gradient
    $color1 = [System.Windows.Media.Color]::FromRgb(0x8B, 0x5C, 0xF6)  # Brighter purple
    $color2 = [System.Windows.Media.Color]::FromRgb(0xA7, 0x8B, 0xFA)  # Lighter purple

    # Create gradient brush
    $gradient = New-Object System.Windows.Media.LinearGradientBrush
    $gradient.StartPoint = New-Object System.Windows.Point(0, 0)
    $gradient.EndPoint = New-Object System.Windows.Point(1, 1)
    $gradient.GradientStops.Add((New-Object System.Windows.Media.GradientStop($color1, 0)))
    $gradient.GradientStops.Add((New-Object System.Windows.Media.GradientStop($color2, 1)))

    # Draw a bold "O" ring with pixelated dissolve effect
    $center = New-Object System.Windows.Point(128, 128)
    $outerRadius = 100
    $innerRadius = 50

    # Create donut geometry
    $outerCircle = New-Object System.Windows.Media.EllipseGeometry($center, $outerRadius, $outerRadius)
    $innerCircle = New-Object System.Windows.Media.EllipseGeometry($center, $innerRadius, $innerRadius)
    $donut = New-Object System.Windows.Media.CombinedGeometry(
        [System.Windows.Media.GeometryCombineMode]::Exclude,
        $outerCircle,
        $innerCircle
    )

    # Clip to left portion (solid part) - up to x=160
    $leftClip = New-Object System.Windows.Media.RectangleGeometry(
        (New-Object System.Windows.Rect(0, 0, 160, 256))
    )
    $leftPart = New-Object System.Windows.Media.CombinedGeometry(
        [System.Windows.Media.GeometryCombineMode]::Intersect,
        $donut,
        $leftClip
    )
    $context.DrawGeometry($gradient, $null, $leftPart)

    # Draw pixelated dissolve on right side
    $pixelSize = 8
    if ($Size -eq 16) { $pixelSize = 14 }  # Larger pixels for tiny icon
    elseif ($Size -eq 32) { $pixelSize = 10 }
    elseif ($Size -eq 48) { $pixelSize = 9 }

    $random = New-Object System.Random(42)  # Fixed seed for consistency
    $pixelGap = $pixelSize - 1

    # Generate pixels in the dissolve zone (x: 140 to 228)
    for ($x = 140; $x -lt 228; $x += $pixelSize) {
        for ($y = 28; $y -lt 228; $y += $pixelSize) {
            $px = $x + ($pixelSize / 2)
            $py = $y + ($pixelSize / 2)

            # Check if point is within donut
            $dx = $px - 128
            $dy = $py - 128
            $distFromCenter = [Math]::Sqrt($dx * $dx + $dy * $dy)
            if ($distFromCenter -lt $innerRadius -or $distFromCenter -gt $outerRadius) {
                continue
            }

            # Probability decreases as we go right (dissolve effect)
            $progress = ($x - 140) / 88.0  # 0 to 1
            $probability = 1.0 - ($progress * $progress)  # Quadratic falloff

            if ($random.NextDouble() -lt $probability) {
                $rectGeo = New-Object System.Windows.Media.RectangleGeometry(
                    (New-Object System.Windows.Rect($x, $y, $pixelGap, $pixelGap))
                )
                $context.DrawGeometry($gradient, $null, $rectGeo)
            }
        }
    }

    $context.Pop()  # Pop scale transform
    $context.Close()

    # Render to bitmap
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    return $bitmap
}

function Save-Png {
    param($Bitmap, $Path)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($Bitmap))
    $stream = [System.IO.File]::Create($Path)
    $encoder.Save($stream)
    $stream.Close()
}

Write-Host "Generating Obfy icons..." -ForegroundColor Cyan

# Generate 256x256 PNG
Write-Host "  Creating app.png (256x256)..."
$bitmap256 = New-ObfyIcon -Size 256
Save-Png -Bitmap $bitmap256 -Path (Join-Path $OutputDir "app.png")
Write-Host "  Done!" -ForegroundColor Green

# Generate additional sizes for ICO
Write-Host "  Creating icon sizes for ICO..."
$bitmap16 = New-ObfyIcon -Size 16
$bitmap32 = New-ObfyIcon -Size 32
$bitmap48 = New-ObfyIcon -Size 48

# Save temp PNGs for ICO creation
$tempDir = Join-Path $env:TEMP "obfy-icons"
if (-not (Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir | Out-Null }

Save-Png -Bitmap $bitmap16 -Path (Join-Path $tempDir "16.png")
Save-Png -Bitmap $bitmap32 -Path (Join-Path $tempDir "32.png")
Save-Png -Bitmap $bitmap48 -Path (Join-Path $tempDir "48.png")
Save-Png -Bitmap $bitmap256 -Path (Join-Path $tempDir "256.png")

# Create ICO file from PNGs
Write-Host "  Creating app.ico..."

function New-IcoFile {
    param(
        [string[]]$PngPaths,
        [string]$OutputPath
    )

    # Read all PNG files
    $images = @()
    foreach ($path in $PngPaths) {
        $bytes = [System.IO.File]::ReadAllBytes($path)
        $images += @{
            Data = $bytes
            Size = [System.IO.Path]::GetFileNameWithoutExtension($path)
        }
    }

    # ICO file structure
    $stream = New-Object System.IO.MemoryStream

    # ICONDIR header (6 bytes)
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([uint16]0)           # Reserved
    $writer.Write([uint16]1)           # Type (1 = ICO)
    $writer.Write([uint16]$images.Count) # Number of images

    # Calculate data offset (header + all directory entries)
    $dataOffset = 6 + (16 * $images.Count)

    # Write ICONDIRENTRY for each image (16 bytes each)
    foreach ($img in $images) {
        $size = [int]$img.Size
        $width = if ($size -ge 256) { 0 } else { $size }
        $height = if ($size -ge 256) { 0 } else { $size }

        $writer.Write([byte]$width)      # Width (0 = 256)
        $writer.Write([byte]$height)     # Height (0 = 256)
        $writer.Write([byte]0)           # Color palette
        $writer.Write([byte]0)           # Reserved
        $writer.Write([uint16]1)         # Color planes
        $writer.Write([uint16]32)        # Bits per pixel
        $writer.Write([uint32]$img.Data.Length)  # Size of image data
        $writer.Write([uint32]$dataOffset)       # Offset to image data

        $dataOffset += $img.Data.Length
    }

    # Write image data
    foreach ($img in $images) {
        $writer.Write($img.Data)
    }

    # Save to file
    [System.IO.File]::WriteAllBytes($OutputPath, $stream.ToArray())
    $writer.Close()
    $stream.Close()
}

$pngFiles = @(
    (Join-Path $tempDir "16.png"),
    (Join-Path $tempDir "32.png"),
    (Join-Path $tempDir "48.png"),
    (Join-Path $tempDir "256.png")
)

New-IcoFile -PngPaths $pngFiles -OutputPath (Join-Path $OutputDir "app.ico")
Write-Host "  Done!" -ForegroundColor Green

# Cleanup temp files
Remove-Item -Path $tempDir -Recurse -Force

Write-Host ""
Write-Host "Icon generation complete!" -ForegroundColor Green
Write-Host "Files created:" -ForegroundColor Cyan
Write-Host "  - $OutputDir\app.png (256x256)" -ForegroundColor Gray
Write-Host "  - $OutputDir\app.ico (16, 32, 48, 256)" -ForegroundColor Gray
