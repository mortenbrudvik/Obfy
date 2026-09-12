# Build a Store-ready MSIX package for Obfy (UI + CLI).
# Requires the Windows SDK (MakeAppx). Signing uses a local CN=Obfy test certificate.
#
# The Store re-signs the package after certification. Do not obfuscate this
# payload — Defender and Store certification scan the product binary.

param(
    [string]$Name = "Obfy.Obfy",
    [string]$Publisher = "CN=Obfy",
    [string]$PublisherDisplayName = "Obfy",
    [switch]$SkipPublish,
    [switch]$SkipSign,
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$BuildDir = $PSScriptRoot
$LayoutDir = Join-Path $BuildDir "msix-layout"
$OutputDir = Join-Path $BuildDir "msix-output"
$ConsoleProject = Join-Path $ProjectRoot "Src\Obfy.Console\Obfy.Console.csproj"
$UIProject = Join-Path $ProjectRoot "Src\Obfy.UI\Obfy.UI.csproj"
$ManifestSource = Join-Path $ProjectRoot "package\AppxManifest.xml"
$AssetsSource = Join-Path $ProjectRoot "package\Assets"
$VersionFile = Join-Path $ProjectRoot "version.json"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Obfy MSIX Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

function Find-SdkTool {
    param([string]$ToolName)

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path $kitsRoot) {
        $match = Get-ChildItem -Path $kitsRoot -Directory |
            Sort-Object { [version]($_.Name -replace '[^0-9.].*$', '') } -Descending -ErrorAction SilentlyContinue |
            ForEach-Object {
                $candidate = Join-Path $_.FullName "x64\$ToolName"
                if (Test-Path $candidate) { $candidate }
            } |
            Select-Object -First 1
        if ($match) { return $match }
    }

    $cmd = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Get-PackageVersion {
    if (-not (Test-Path $VersionFile)) {
        throw "version.json not found at $VersionFile"
    }

    $version = Get-Content $VersionFile -Raw | ConvertFrom-Json
    return "$($version.major).$($version.minor).$($version.patch).0"
}

function Get-PublishFileCount([string]$Path) {
    return @(Get-ChildItem -Path $Path -Recurse -File).Count
}

# Step 1: Locate Windows SDK tools
Write-Host "[1/7] Locating Windows SDK tools..." -ForegroundColor Yellow
$MakeAppx = Find-SdkTool "makeappx.exe"
if (-not $MakeAppx) {
    Write-Host "ERROR: makeappx.exe not found." -ForegroundColor Red
    Write-Host "Install the Windows 10/11 SDK (Windows SDK Signing Tools / MSIX packaging)." -ForegroundColor Yellow
    exit 1
}
Write-Host "  MakeAppx: $MakeAppx" -ForegroundColor Green

$MakePri = Find-SdkTool "makepri.exe"
if ($MakePri) {
    Write-Host "  MakePri:  $MakePri" -ForegroundColor Green
} else {
    Write-Host "  MakePri not found; packing without resources.pri" -ForegroundColor Gray
}

$SignTool = Find-SdkTool "signtool.exe"
if ($SkipSign) {
    Write-Host "  Signing skipped (-SkipSign)" -ForegroundColor Gray
} elseif ($SignTool) {
    Write-Host "  SignTool: $SignTool" -ForegroundColor Green
} else {
    Write-Host "  SignTool not found; package will be unsigned" -ForegroundColor Yellow
}

$PackageVersion = Get-PackageVersion
Write-Host "  Version: $PackageVersion" -ForegroundColor Green

# Step 2: Clean layout
Write-Host "[2/7] Preparing package layout..." -ForegroundColor Yellow
if (Test-Path $LayoutDir) {
    Remove-Item $LayoutDir -Recurse -Force
}
New-Item -Path $LayoutDir -ItemType Directory -Force | Out-Null
New-Item -Path $OutputDir -ItemType Directory -Force | Out-Null
Write-Host "  Layout: $LayoutDir" -ForegroundColor Green

# Step 3: Publish UI into package root
if (-not $SkipPublish) {
    Write-Host "[3/7] Publishing Obfy UI (self-contained, no symbols)..." -ForegroundColor Yellow
    & dotnet publish $UIProject -c Release -r win-x64 --self-contained true -o $LayoutDir `
        -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish UI failed"
    }

    $uiExe = Join-Path $LayoutDir "ObfyUI.exe"
    if (-not (Test-Path $uiExe)) {
        throw "ObfyUI.exe not found in $LayoutDir"
    }
    Write-Host "  Published $(Get-PublishFileCount $LayoutDir) UI files" -ForegroundColor Green
} else {
    Write-Host "[3/7] Skipping UI publish (-SkipPublish)" -ForegroundColor Gray
    if (-not (Test-Path (Join-Path $LayoutDir "ObfyUI.exe"))) {
        throw "Layout is missing ObfyUI.exe; run without -SkipPublish"
    }
}

# Step 4: Publish CLI into CLI\
$CliDir = Join-Path $LayoutDir "CLI"
if (-not $SkipPublish) {
    Write-Host "[4/7] Publishing Obfy CLI (self-contained, no symbols)..." -ForegroundColor Yellow
    New-Item -Path $CliDir -ItemType Directory -Force | Out-Null
    & dotnet publish $ConsoleProject -c Release -r win-x64 --self-contained true -o $CliDir `
        -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish CLI failed"
    }

    $cliExe = Join-Path $CliDir "obfy.exe"
    if (-not (Test-Path $cliExe)) {
        throw "obfy.exe not found in $CliDir"
    }
    Write-Host "  Published $(Get-PublishFileCount $CliDir) CLI files" -ForegroundColor Green
} else {
    Write-Host "[4/7] Skipping CLI publish (-SkipPublish)" -ForegroundColor Gray
}

# Step 5: Stage manifest and assets
Write-Host "[5/7] Staging manifest and Store assets..." -ForegroundColor Yellow
if (-not (Test-Path $ManifestSource)) {
    throw "Manifest not found: $ManifestSource"
}
if (-not (Test-Path $AssetsSource)) {
    throw "Assets folder not found: $AssetsSource"
}

$layoutAssets = Join-Path $LayoutDir "Assets"
Copy-Item $AssetsSource $layoutAssets -Recurse -Force

[xml]$manifest = Get-Content $ManifestSource
$ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$ns.AddNamespace("def", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
$identity = $manifest.SelectSingleNode("/def:Package/def:Identity", $ns)
$identity.SetAttribute("Name", $Name)
$identity.SetAttribute("Publisher", $Publisher)
$identity.SetAttribute("Version", $PackageVersion)
$identity.SetAttribute("ProcessorArchitecture", "x64")

$publisherDisplay = $manifest.SelectSingleNode("/def:Package/def:Properties/def:PublisherDisplayName", $ns)
if ($publisherDisplay) {
    $publisherDisplay.InnerText = $PublisherDisplayName
}

$manifestPath = Join-Path $LayoutDir "AppxManifest.xml"
$manifest.Save($manifestPath)
Write-Host "  Identity: $Name $PackageVersion ($Publisher)" -ForegroundColor Green

Get-ChildItem -Path $LayoutDir -Recurse -Include *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue

if ($MakePri) {
    Write-Host "  Generating resources.pri..." -ForegroundColor Gray
    $priConfig = Join-Path $LayoutDir "priconfig.xml"
    & $MakePri createconfig /cf $priConfig /dq en-US /o | Out-Null
    if ($LASTEXITCODE -eq 0) {
        & $MakePri new /pr $LayoutDir /cf $priConfig /of (Join-Path $LayoutDir "resources.pri") /o | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  WARNING: makepri new failed; continuing without resources.pri" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  WARNING: makepri createconfig failed; continuing without resources.pri" -ForegroundColor Yellow
    }
    if (Test-Path $priConfig) {
        Remove-Item $priConfig -Force
    }
}

# Step 6: Pack
Write-Host "[6/7] Packing MSIX..." -ForegroundColor Yellow
$PackageFileName = "Obfy-$PackageVersion-x64.msix"
$PackagePath = Join-Path $OutputDir $PackageFileName
if (Test-Path $PackagePath) {
    Remove-Item $PackagePath -Force
}

$packLog = Join-Path $OutputDir "makeappx.log"
& $MakeAppx pack /d $LayoutDir /p $PackagePath /o *> $packLog
if ($LASTEXITCODE -ne 0) {
    Get-Content $packLog | Write-Host
    throw "makeappx pack failed"
}
Write-Host "  Packed: $PackagePath" -ForegroundColor Green

# Step 7: Sign
Write-Host "[7/7] Signing package..." -ForegroundColor Yellow
if ($SkipSign) {
    Write-Host "  Skipped (-SkipSign). Partner Center re-signs Store uploads." -ForegroundColor Gray
} elseif (-not $SignTool) {
    Write-Host "  Skipped (signtool.exe not found)." -ForegroundColor Yellow
} else {
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $Publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert) {
        Write-Host "  Creating self-signed certificate $Publisher..." -ForegroundColor Gray
        $cert = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $Publisher `
            -KeyUsage DigitalSignature `
            -FriendlyName "Obfy MSIX test signing" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @(
                "2.5.29.37={text}1.3.6.1.5.5.7.3.3",
                "2.5.29.19={text}"
            )
    }

    $trusted = Get-ChildItem Cert:\CurrentUser\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if (-not $trusted) {
        $exportPath = Join-Path $env:TEMP "ObfyMsixTest.cer"
        Export-Certificate -Cert $cert -FilePath $exportPath | Out-Null
        Import-Certificate -FilePath $exportPath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
        Remove-Item $exportPath -Force
        Write-Host "  Imported test cert into CurrentUser\TrustedPeople" -ForegroundColor Gray
    }

    & $SignTool sign /fd SHA256 /sha1 $cert.Thumbprint $PackagePath
    if ($LASTEXITCODE -ne 0) {
        throw "signtool sign failed"
    }
    Write-Host "  Signed with $($cert.Thumbprint)" -ForegroundColor Green
}

if ($Install) {
    Write-Host "Registering loose package layout for this user..." -ForegroundColor Yellow
    $existing = Get-AppxPackage -Name $Name -ErrorAction SilentlyContinue
    if ($existing) {
        Remove-AppxPackage -Package $existing.PackageFullName
    }
    Add-AppxPackage -Register (Join-Path $LayoutDir "AppxManifest.xml")
    Write-Host "  Registered from $LayoutDir" -ForegroundColor Green
    Write-Host "  Note: this install points at the build layout. Rebuild or uninstall before deleting it." -ForegroundColor Gray
}

$sizeMB = [math]::Round((Get-Item $PackagePath).Length / 1MB, 2)
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  MSIX build complete" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Package: $PackagePath" -ForegroundColor Cyan
Write-Host "Size:    $sizeMB MB" -ForegroundColor Cyan
Write-Host ""
Write-Host "Sideload:  Add-AppxPackage -Path `"$PackagePath`"" -ForegroundColor Gray
Write-Host "Store:     upload the .msix in Partner Center (Microsoft re-signs it)" -ForegroundColor Gray
