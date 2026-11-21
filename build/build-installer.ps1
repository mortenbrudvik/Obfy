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
$RedistDir = Join-Path $BuildDir "redist"
$WebView2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
$WebView2BootstrapperName = "MicrosoftEdgeWebview2Setup.exe"

# The bootstrapper runs elevated on every install that lacks WebView2, so a corrupt or substituted binary is a
# real hazard. Anything smaller than this is a truncated download or an HTML error page, not the ~1.7 MB signed
# installer.
$MinBootstrapperBytes = 100KB

function Test-Bootstrapper {
    # Returns $null when the file is trustworthy, otherwise a reason string.
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return "file does not exist"
    }

    $length = (Get-Item $Path).Length
    if ($length -lt $MinBootstrapperBytes) {
        return "only $length bytes - truncated download or an error page"
    }

    $signature = Get-AuthenticodeSignature $Path
    if ($signature.Status -ne "Valid") {
        return "Authenticode signature is not valid (status: $($signature.Status))"
    }
    if ($signature.SignerCertificate.Subject -notmatch "O=Microsoft Corporation") {
        return "not signed by Microsoft (subject: $($signature.SignerCertificate.Subject))"
    }
    return $null
}

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

# The product version comes from version.json only (Get-ProductVersion.ps1 validates it). It is read before the
# long publish so a bad file fails fast; the publish itself takes the same version from Directory.Build.props, and
# ObfySetup.iss has no default and stops unless it is given /DMyAppVersion. -SkipBuild never runs the compiler.
$InstallerVersion = $null
if (-not $SkipBuild) {
    $InstallerVersion = (& (Join-Path $BuildDir "Get-ProductVersion.ps1") | Out-String).Trim()
    Write-Host "  Version: $InstallerVersion (version.json)" -ForegroundColor Green
}

# Step 2: Clean publish directories
if (-not $SkipPublish) {
    Write-Host "[2/7] Cleaning publish directories..." -ForegroundColor Yellow

    if (Test-Path $PublishDir) {
        Remove-Item -Path $PublishDir -Recurse -Force
        Write-Host "  Removed existing CLI publish directory" -ForegroundColor Green
    }

    if (Test-Path $PublishDirUI) {
        Remove-Item -Path $PublishDirUI -Recurse -Force
        Write-Host "  Removed existing UI publish directory" -ForegroundColor Green
    }
} else {
    # -SkipPublish reuses the existing publish output; deleting it here would leave the installer nothing to package.
    Write-Host "[2/7] Keeping existing publish directories (--SkipPublish)" -ForegroundColor Gray
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

    # The web UI is loaded from Assets\ beside ObfyUI.exe. The publish builds the frontend itself (the
    # frontend build is never skipped here), so a missing index.html means the frontend did not build.
    $UIIndexPath = Join-Path $PublishDirUI "Assets\index.html"
    if (-not (Test-Path $UIIndexPath)) {
        Write-Host "ERROR: Assets\index.html not found in UI publish output - the frontend did not build!" -ForegroundColor Red
        exit 1
    }

    # The installer excludes these too, but a source map must never be published next to the page.
    $strayWebSymbols = @(Get-ChildItem -Path (Join-Path $PublishDirUI "Assets") -Recurse -File -Include *.map, *.pdb)
    if ($strayWebSymbols.Count -gt 0) {
        Write-Host "ERROR: source maps or symbols found in Assets: $($strayWebSymbols.FullName -join ', ')" -ForegroundColor Red
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
            $obfuscatedPath = Join-Path $ObfuscatedCliDir $asmName
            & $TempObfyExe $asmPath -o $ObfuscatedCliDir -l standard --string-encrypt --control-flow --rename --strip-metadata
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path $obfuscatedPath)) {
                throw "Self-obfuscation failed for $asmName (exit $LASTEXITCODE). The installer will not package the original assembly."
            }
            Copy-Item $obfuscatedPath $PublishDir -Force
            Write-Host "    Obfuscated: $asmName" -ForegroundColor Green
        }
    }

    # Obfuscate UI assemblies to separate directory
    Write-Host "  Obfuscating UI assemblies..." -ForegroundColor Gray
    foreach ($asmName in $OurAssemblies) {
        $asmPath = Join-Path $PublishDirUI $asmName
        if (Test-Path $asmPath) {
            $obfuscatedPath = Join-Path $ObfuscatedUiDir $asmName
            & $TempObfyExe $asmPath -o $ObfuscatedUiDir -l standard --string-encrypt --control-flow --rename --strip-metadata
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path $obfuscatedPath)) {
                throw "Self-obfuscation failed for UI $asmName (exit $LASTEXITCODE). The installer will not package the original assembly."
            }
            Copy-Item $obfuscatedPath $PublishDirUI -Force
            Write-Host "    Obfuscated: $asmName" -ForegroundColor Green
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

# The installer bundles the WebView2 Evergreen bootstrapper (ObfySetup.iss installs it only when the runtime is absent).
if (-not $SkipBuild) {
    if (-not (Test-Path $RedistDir)) {
        New-Item -Path $RedistDir -ItemType Directory -Force | Out-Null
    }
    $BootstrapperPath = Join-Path $RedistDir $WebView2BootstrapperName

    # The cached copy is re-verified every run: a file that failed mid-download would otherwise be reused forever.
    $Problem = Test-Bootstrapper -Path $BootstrapperPath
    if (-not $Problem) {
        Write-Host "  WebView2 bootstrapper (cached): $BootstrapperPath" -ForegroundColor Green
    } else {
        if (Test-Path $BootstrapperPath) {
            Write-Host "  Rejecting cached bootstrapper ($Problem) - re-downloading." -ForegroundColor Yellow
            Remove-Item $BootstrapperPath -Force
        }

        # Download to a staging name so a failed transfer never lands on the real path.
        $StagingPath = "$BootstrapperPath.download"
        Remove-Item $StagingPath -Force -ErrorAction SilentlyContinue

        Write-Host "  Downloading $WebView2BootstrapperName ..." -ForegroundColor Gray
        try {
            Invoke-WebRequest -Uri $WebView2BootstrapperUrl -OutFile $StagingPath -UseBasicParsing
        } catch {
            Remove-Item $StagingPath -Force -ErrorAction SilentlyContinue
            Write-Host "ERROR: Failed to download WebView2 bootstrapper: $_" -ForegroundColor Red
            Write-Host "  Place $WebView2BootstrapperName in build\redist\ manually and re-run." -ForegroundColor Yellow
            exit 1
        }

        $Problem = Test-Bootstrapper -Path $StagingPath
        if ($Problem) {
            Remove-Item $StagingPath -Force -ErrorAction SilentlyContinue
            Write-Host "ERROR: Downloaded WebView2 bootstrapper rejected: $Problem" -ForegroundColor Red
            exit 1
        }

        Move-Item -Path $StagingPath -Destination $BootstrapperPath -Force
        Write-Host "  Downloaded and verified: $BootstrapperPath" -ForegroundColor Green
    }
}

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
        & $InnoSetupPath "/DMyAppVersion=$InstallerVersion" $IssFile

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
