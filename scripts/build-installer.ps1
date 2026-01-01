# Build Obfy Installer
# Requires: Inno Setup 6 (https://jrsoftware.org/isinfo.php)

param(
    [string]$InnoSetupPath,
    [switch]$SkipPublish,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $ProjectRoot "publish"
$InstallerDir = Join-Path $ProjectRoot "installer"
$OutputDir = Join-Path $InstallerDir "installer-output"
$ConsoleProject = Join-Path $ProjectRoot "Src\Obfy.Console\Obfy.Console.csproj"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Obfy Installer Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Find Inno Setup
Write-Host "[1/5] Locating Inno Setup..." -ForegroundColor Yellow

if (-not $InnoSetupPath) {
    $SearchPaths = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $SearchPaths) {
        if (Test-Path $path) {
            $InnoSetupPath = $path
            break
        }
    }
}

if (-not $InnoSetupPath -or -not (Test-Path $InnoSetupPath)) {
    Write-Host "ERROR: Inno Setup 6 not found!" -ForegroundColor Red
    Write-Host "Searched locations:" -ForegroundColor Gray
    Write-Host "  - $env:LOCALAPPDATA\Programs\Inno Setup 6\" -ForegroundColor Gray
    Write-Host "  - C:\Program Files (x86)\Inno Setup 6\" -ForegroundColor Gray
    Write-Host "  - C:\Program Files\Inno Setup 6\" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Install from: https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    Write-Host "Or specify path: .\build-installer.ps1 -InnoSetupPath 'C:\Path\To\ISCC.exe'" -ForegroundColor Gray
    exit 1
}

Write-Host "  Found: $InnoSetupPath" -ForegroundColor Green

# Step 2: Clean publish directory
Write-Host "[2/5] Cleaning publish directory..." -ForegroundColor Yellow

if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
    Write-Host "  Removed existing publish directory" -ForegroundColor Green
}

# Step 3: Build and publish
if (-not $SkipPublish) {
    Write-Host "[3/5] Publishing Obfy (self-contained)..." -ForegroundColor Yellow

    $publishArgs = @(
        "publish",
        $ConsoleProject,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained",
        "-o", $PublishDir
    )

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: dotnet publish failed!" -ForegroundColor Red
        exit 1
    }

    # Verify obfy.exe exists
    $ExePath = Join-Path $PublishDir "obfy.exe"
    if (-not (Test-Path $ExePath)) {
        Write-Host "ERROR: obfy.exe not found in publish output!" -ForegroundColor Red
        exit 1
    }

    $fileCount = (Get-ChildItem -Path $PublishDir -Recurse -File).Count
    Write-Host "  Published $fileCount files" -ForegroundColor Green
} else {
    Write-Host "[3/5] Skipping publish (--SkipPublish)" -ForegroundColor Gray
}

# Step 4: Create output directory
Write-Host "[4/5] Preparing output directory..." -ForegroundColor Yellow

if (-not (Test-Path $OutputDir)) {
    New-Item -Path $OutputDir -ItemType Directory -Force | Out-Null
}
Write-Host "  Output: $OutputDir" -ForegroundColor Green

# Step 5: Build installer
if (-not $SkipBuild) {
    Write-Host "[5/5] Building installer..." -ForegroundColor Yellow

    $IssFile = Join-Path $InstallerDir "ObfySetup.iss"

    if (-not (Test-Path $IssFile)) {
        Write-Host "ERROR: ObfySetup.iss not found!" -ForegroundColor Red
        exit 1
    }

    Push-Location $InstallerDir
    try {
        & $InnoSetupPath $IssFile

        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Inno Setup compilation failed!" -ForegroundColor Red
            exit 1
        }
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "[5/5] Skipping installer build (--SkipBuild)" -ForegroundColor Gray
}

# Report results
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$installerFiles = Get-ChildItem -Path $OutputDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending
if ($installerFiles.Count -gt 0) {
    $latestInstaller = $installerFiles[0]
    $sizeMB = [math]::Round($latestInstaller.Length / 1MB, 2)
    Write-Host ""
    Write-Host "Installer: $($latestInstaller.FullName)" -ForegroundColor Cyan
    Write-Host "Size: $sizeMB MB" -ForegroundColor Cyan
}
