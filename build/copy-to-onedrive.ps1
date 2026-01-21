# Obfy OneDrive Copy Script
# Copies release files to OneDrive Personal Apps/Obfy folder
# Files copied: installer, checksum, latest.json

param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot

Write-Host "=== Copy Release to OneDrive ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Find OneDrive Personal path
Write-Host "[1/5] Locating OneDrive Personal..." -ForegroundColor Yellow

$OneDrivePath = $null

# OneDrive Personal is always at $USERPROFILE\OneDrive (no suffix)
# Work/school accounts have suffixes like "OneDrive - CompanyName"
$PersonalOneDrive = Join-Path $env:USERPROFILE "OneDrive"

if (Test-Path $PersonalOneDrive) {
    $OneDrivePath = $PersonalOneDrive
    Write-Host "  Found OneDrive Personal" -ForegroundColor Gray
}

if (-not $OneDrivePath) {
    Write-Host "ERROR: OneDrive Personal folder not found!" -ForegroundColor Red
    Write-Host "  Expected: $PersonalOneDrive" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Note: This script targets OneDrive Personal (no suffix)." -ForegroundColor Yellow
    Write-Host "  Work/school accounts (OneDrive - CompanyName) are not supported." -ForegroundColor Yellow
    exit 1
}

Write-Host "  Path: $OneDrivePath" -ForegroundColor Gray

# Step 2: Ensure Apps/Obfy folder exists
Write-Host "[2/5] Checking Apps/Obfy folder..." -ForegroundColor Yellow

$ObfyFolder = Join-Path $OneDrivePath "Apps\Obfy"

if (-not (Test-Path $ObfyFolder)) {
    Write-Host "  Creating Apps/Obfy folder..." -ForegroundColor Gray
    New-Item -Path $ObfyFolder -ItemType Directory -Force | Out-Null
    Write-Host "  Created: $ObfyFolder" -ForegroundColor Gray
} else {
    Write-Host "  Found: $ObfyFolder" -ForegroundColor Gray
}

# Step 3: Find installer
Write-Host "[3/5] Finding installer..." -ForegroundColor Yellow

$InstallerDir = Join-Path $PSScriptRoot "output"

if (-not (Test-Path $InstallerDir)) {
    Write-Host "ERROR: Installer output directory not found!" -ForegroundColor Red
    Write-Host "  Expected: $InstallerDir" -ForegroundColor Gray
    Write-Host "  Run .\build\build-installer.ps1 first" -ForegroundColor Yellow
    exit 1
}

$InstallerFile = Get-ChildItem -Path $InstallerDir -Filter "ObfySetup-*.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $InstallerFile) {
    Write-Host "ERROR: No installer found in $InstallerDir" -ForegroundColor Red
    Write-Host "  Run .\build\build-installer.ps1 first" -ForegroundColor Yellow
    exit 1
}

# Extract version from filename
$Version = "unknown"
if ($InstallerFile.Name -match "ObfySetup-(\d+\.\d+\.\d+)\.exe") {
    $Version = $Matches[1]
}

$SourceSize = $InstallerFile.Length / 1MB
Write-Host "  Found: $($InstallerFile.Name)" -ForegroundColor Gray
Write-Host "  Version: $Version" -ForegroundColor Gray
Write-Host "  Size: $([math]::Round($SourceSize, 2)) MB" -ForegroundColor Gray

# Step 4: Copy installer to OneDrive
Write-Host "[4/5] Copying installer to OneDrive..." -ForegroundColor Yellow

$InstallerDestPath = Join-Path $ObfyFolder $InstallerFile.Name

# Check if file already exists
if ((Test-Path $InstallerDestPath) -and -not $Force) {
    $ExistingSize = (Get-Item $InstallerDestPath).Length / 1MB
    Write-Host "  File already exists: $InstallerDestPath" -ForegroundColor Yellow
    Write-Host "  Existing size: $([math]::Round($ExistingSize, 2)) MB" -ForegroundColor Gray

    $Overwrite = Read-Host "  Overwrite? (y/N)"
    if ($Overwrite -ne "y" -and $Overwrite -ne "Y") {
        Write-Host "  Cancelled." -ForegroundColor Yellow
        exit 0
    }
}

Copy-Item -Path $InstallerFile.FullName -Destination $InstallerDestPath -Force

# Verify installer copy
if (Test-Path $InstallerDestPath) {
    $DestSize = (Get-Item $InstallerDestPath).Length / 1MB

    if ([math]::Abs($SourceSize - $DestSize) -lt 0.01) {
        Write-Host "  Copied: $($InstallerFile.Name)" -ForegroundColor Gray
    } else {
        Write-Host "WARNING: File sizes don't match!" -ForegroundColor Yellow
        Write-Host "  Source: $([math]::Round($SourceSize, 2)) MB" -ForegroundColor Gray
        Write-Host "  Destination: $([math]::Round($DestSize, 2)) MB" -ForegroundColor Gray
    }
} else {
    Write-Host "ERROR: Copy failed - file not found at destination!" -ForegroundColor Red
    exit 1
}

# Step 5: Generate and copy additional files
Write-Host "[5/5] Generating release files..." -ForegroundColor Yellow

# Generate SHA256 checksum (use .NET for PowerShell 5.1 compatibility)
$Stream = [System.IO.File]::OpenRead($InstallerFile.FullName)
$Sha256 = [System.Security.Cryptography.SHA256]::Create()
$HashBytes = $Sha256.ComputeHash($Stream)
$Stream.Close()
$Hash = [BitConverter]::ToString($HashBytes) -replace '-', ''
$ChecksumFileName = "$($InstallerFile.BaseName).sha256"
$ChecksumContent = "$Hash  $($InstallerFile.Name)"
$ChecksumDestPath = Join-Path $ObfyFolder $ChecksumFileName
$ChecksumContent | Out-File -FilePath $ChecksumDestPath -Encoding utf8 -NoNewline
Write-Host "  Generated: $ChecksumFileName" -ForegroundColor Gray

# Generate latest.json manifest
$LatestJson = @{
    version = $Version
    releaseDate = (Get-Date -Format "yyyy-MM-dd")
    files = @{
        installer = $InstallerFile.Name
        checksum = $ChecksumFileName
    }
} | ConvertTo-Json -Depth 3
$LatestDestPath = Join-Path $ObfyFolder "latest.json"
$LatestJson | Out-File -FilePath $LatestDestPath -Encoding utf8
Write-Host "  Generated: latest.json" -ForegroundColor Gray

# Summary
Write-Host ""
Write-Host "=== Copy Complete ===" -ForegroundColor Green
Write-Host "  Destination: $ObfyFolder" -ForegroundColor White
Write-Host ""
Write-Host "  Files:" -ForegroundColor Gray
Write-Host "    - $($InstallerFile.Name) ($([math]::Round($SourceSize, 2)) MB)" -ForegroundColor Gray
Write-Host "    - $ChecksumFileName" -ForegroundColor Gray
Write-Host "    - latest.json" -ForegroundColor Gray
Write-Host ""
Write-Host "  OneDrive will sync these files automatically." -ForegroundColor Cyan
Write-Host "  Create share links via OneDrive web when needed." -ForegroundColor Cyan
Write-Host ""
