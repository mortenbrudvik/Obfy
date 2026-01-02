# Build Obfy Installer
# Requires: Inno Setup 6 (https://jrsoftware.org/isinfo.php)

param(
    [string]$InnoSetupPath,
    [switch]$SkipPublish,
    [switch]$SkipObfuscation,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$BuildDir = $PSScriptRoot
$PublishDir = Join-Path $BuildDir "publish"
$PublishDirUI = Join-Path $BuildDir "publish-ui"
$OutputDir = Join-Path $BuildDir "output"
$ConsoleProject = Join-Path $ProjectRoot "Src\Obfy.Console\Obfy.Console.csproj"
$UIProject = Join-Path $ProjectRoot "Src\Obfy.UI\Obfy.UI.csproj"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Obfy Installer Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Find Inno Setup
Write-Host "[1/7] Locating Inno Setup..." -ForegroundColor Yellow

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

# Step 2: Clean publish directories
Write-Host "[2/7] Cleaning publish directories..." -ForegroundColor Yellow

if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
    Write-Host "  Removed existing CLI publish directory" -ForegroundColor Green
}

if (Test-Path $PublishDirUI) {
    Remove-Item -Path $PublishDirUI -Recurse -Force
    Write-Host "  Removed existing UI publish directory" -ForegroundColor Green
}

# Step 3: Build and publish CLI
if (-not $SkipPublish) {
    Write-Host "[3/7] Publishing Obfy CLI (self-contained)..." -ForegroundColor Yellow

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
    Write-Host "  Published $fileCount CLI files" -ForegroundColor Green
} else {
    Write-Host "[3/7] Skipping CLI publish (--SkipPublish)" -ForegroundColor Gray
}

# Step 4: Build and publish UI
if (-not $SkipPublish) {
    Write-Host "[4/7] Publishing Obfy UI (self-contained)..." -ForegroundColor Yellow

    $publishArgsUI = @(
        "publish",
        $UIProject,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained",
        "-o", $PublishDirUI
    )

    & dotnet @publishArgsUI

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: dotnet publish UI failed!" -ForegroundColor Red
        exit 1
    }

    # Verify ObfyUI.exe exists
    $UIExePath = Join-Path $PublishDirUI "ObfyUI.exe"
    if (-not (Test-Path $UIExePath)) {
        Write-Host "ERROR: ObfyUI.exe not found in publish output!" -ForegroundColor Red
        exit 1
    }

    $fileCountUI = (Get-ChildItem -Path $PublishDirUI -Recurse -File).Count
    Write-Host "  Published $fileCountUI UI files" -ForegroundColor Green
} else {
    Write-Host "[4/7] Skipping UI publish (--SkipPublish)" -ForegroundColor Gray
}

# Step 5: Obfuscate published binaries
if (-not $SkipObfuscation -and -not $SkipPublish) {
    Write-Host "[5/7] Obfuscating assemblies..." -ForegroundColor Yellow

    $TempObfyDir = Join-Path $BuildDir "temp-obfy"
    $ObfuscatedCliDir = Join-Path $BuildDir "obfuscated-cli"
    $ObfuscatedUiDir = Join-Path $BuildDir "obfuscated-ui"

    # Create temp directory with copy of obfy for bootstrap
    if (Test-Path $TempObfyDir) { Remove-Item $TempObfyDir -Recurse -Force }
    if (Test-Path $ObfuscatedCliDir) { Remove-Item $ObfuscatedCliDir -Recurse -Force }
    if (Test-Path $ObfuscatedUiDir) { Remove-Item $ObfuscatedUiDir -Recurse -Force }

    Copy-Item $PublishDir $TempObfyDir -Recurse
    New-Item -Path $ObfuscatedCliDir -ItemType Directory -Force | Out-Null
    New-Item -Path $ObfuscatedUiDir -ItemType Directory -Force | Out-Null

    $TempObfyExe = Join-Path $TempObfyDir "obfy.exe"

    # Define which assemblies to obfuscate (our own DLLs only - EXEs are native app hosts)
    $OurAssemblies = @("obfy.dll", "Obfy.Core.dll", "Settings.Core.dll", "Logging.Core.dll")

    # Obfuscate CLI assemblies to separate directory
    Write-Host "  Obfuscating CLI assemblies..." -ForegroundColor Gray
    foreach ($asmName in $OurAssemblies) {
        $asmPath = Join-Path $PublishDir $asmName
        if (Test-Path $asmPath) {
            & $TempObfyExe $asmPath -o $ObfuscatedCliDir -l standard --string-encrypt --control-flow --rename --strip-metadata
            if ($LASTEXITCODE -eq 0) {
                # Copy obfuscated file back to publish directory
                $obfuscatedPath = Join-Path $ObfuscatedCliDir $asmName
                if (Test-Path $obfuscatedPath) {
                    Copy-Item $obfuscatedPath $PublishDir -Force
                    Write-Host "    Obfuscated: $asmName" -ForegroundColor Green
                }
            } else {
                Write-Host "    WARNING: Failed to obfuscate $asmName" -ForegroundColor Yellow
            }
        }
    }

    # Obfuscate UI assemblies to separate directory
    Write-Host "  Obfuscating UI assemblies..." -ForegroundColor Gray
    foreach ($asmName in $OurAssemblies) {
        $asmPath = Join-Path $PublishDirUI $asmName
        if (Test-Path $asmPath) {
            & $TempObfyExe $asmPath -o $ObfuscatedUiDir -l standard --string-encrypt --control-flow --rename --strip-metadata
            if ($LASTEXITCODE -eq 0) {
                # Copy obfuscated file back to publish-ui directory
                $obfuscatedPath = Join-Path $ObfuscatedUiDir $asmName
                if (Test-Path $obfuscatedPath) {
                    Copy-Item $obfuscatedPath $PublishDirUI -Force
                    Write-Host "    Obfuscated: $asmName" -ForegroundColor Green
                }
            } else {
                Write-Host "    WARNING: Failed to obfuscate UI $asmName" -ForegroundColor Yellow
            }
        }
    }

    # Cleanup temp directories
    Remove-Item $TempObfyDir -Recurse -Force
    Remove-Item $ObfuscatedCliDir -Recurse -Force
    Remove-Item $ObfuscatedUiDir -Recurse -Force

    Write-Host "  Obfuscation complete" -ForegroundColor Green
} elseif ($SkipObfuscation) {
    Write-Host "[5/7] Skipping obfuscation (--SkipObfuscation)" -ForegroundColor Gray
} else {
    Write-Host "[5/7] Skipping obfuscation (no publish)" -ForegroundColor Gray
}

# Step 6: Create output directory
Write-Host "[6/7] Preparing output directory..." -ForegroundColor Yellow

if (-not (Test-Path $OutputDir)) {
    New-Item -Path $OutputDir -ItemType Directory -Force | Out-Null
}
Write-Host "  Output: $OutputDir" -ForegroundColor Green

# Step 7: Build installer
if (-not $SkipBuild) {
    Write-Host "[7/7] Building installer..." -ForegroundColor Yellow

    $IssFile = Join-Path $BuildDir "ObfySetup.iss"

    if (-not (Test-Path $IssFile)) {
        Write-Host "ERROR: ObfySetup.iss not found!" -ForegroundColor Red
        exit 1
    }

    Push-Location $BuildDir
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
    Write-Host "[7/7] Skipping installer build (--SkipBuild)" -ForegroundColor Gray
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
