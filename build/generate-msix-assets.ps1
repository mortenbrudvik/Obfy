# Generate Microsoft Store visual assets from Src/Obfy.UI/Images/app.png
# Requires Windows PowerShell / PowerShell with WPF assemblies.

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$SourcePng = Join-Path $ProjectRoot "Src\Obfy.UI\Images\app.png"
$OutputDir = Join-Path $ProjectRoot "package\Assets"

if (-not (Test-Path $SourcePng)) {
    throw "Source icon not found: $SourcePng"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

function Get-SourceBitmap {
    $bitmap = New-Object System.Windows.Media.Imaging.BitmapImage
    $bitmap.BeginInit()
    $bitmap.UriSource = [Uri]::new((Resolve-Path $SourcePng).ProviderPath)
    $bitmap.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $bitmap.EndInit()
    $bitmap.Freeze()
    return $bitmap
}

function New-ScaledIcon {
    param(
        [System.Windows.Media.Imaging.BitmapSource]$Source,
        [int]$Size
    )

    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.DrawImage($Source, [System.Windows.Rect]::new(0, 0, $Size, $Size))
    $context.Close()

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    return $bitmap
}

function New-CanvasIcon {
    param(
        [System.Windows.Media.Imaging.BitmapSource]$Source,
        [int]$Width,
        [int]$Height
    )

    $background = [System.Windows.Media.SolidColorBrush]::new(
        [System.Windows.Media.Color]::FromRgb(0x1E, 0x11, 0x36))

    $iconSize = [Math]::Floor([Math]::Min($Height * 0.72, $Width * 0.42))
    $x = ($Width - $iconSize) / 2.0
    $y = ($Height - $iconSize) / 2.0

    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.DrawRectangle($background, $null, [System.Windows.Rect]::new(0, 0, $Width, $Height))
    $context.DrawImage($Source, [System.Windows.Rect]::new($x, $y, $iconSize, $iconSize))
    $context.Close()

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $Width, $Height, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    return $bitmap
}

function Save-Png {
    param(
        [System.Windows.Media.Imaging.BitmapSource]$Bitmap,
        [string]$Path
    )

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($Bitmap))
    $stream = [System.IO.File]::Create($Path)
    try {
        $encoder.Save($stream)
    }
    finally {
        $stream.Dispose()
    }
}

Write-Host "Generating MSIX Store assets..." -ForegroundColor Cyan

$source = Get-SourceBitmap

$squareSizes = @{
    "StoreLogo.png"          = 50
    "Square44x44Logo.png"    = 44
    "Square71x71Logo.png"    = 71
    "Square150x150Logo.png"  = 150
}

foreach ($entry in $squareSizes.GetEnumerator()) {
    $path = Join-Path $OutputDir $entry.Key
    Write-Host "  $($entry.Key) ($($entry.Value)x$($entry.Value))"
    Save-Png -Bitmap (New-ScaledIcon -Source $source -Size $entry.Value) -Path $path
}

Write-Host "  Wide310x150Logo.png (310x150)"
Save-Png -Bitmap (New-CanvasIcon -Source $source -Width 310 -Height 150) -Path (Join-Path $OutputDir "Wide310x150Logo.png")

Write-Host "  SplashScreen.png (620x300)"
Save-Png -Bitmap (New-CanvasIcon -Source $source -Width 620 -Height 300) -Path (Join-Path $OutputDir "SplashScreen.png")

Write-Host "MSIX assets written to $OutputDir" -ForegroundColor Green
