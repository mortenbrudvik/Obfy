# Build a sideload/Store MSIX package for Obfy (UI + CLI).
# Requires the Windows SDK (MakeAppx, MakePri, SignTool).
#
# Default identity is local/sideload (Subject = -Publisher, default CN=Obfy).
# For Store, pass -Store (reads package/store-identity.json and implies -SkipSign)
# or Partner Center -Name / -Publisher / -PublisherDisplayName; Microsoft re-signs
# the upload. Unlike the Inno installer, this payload is not self-obfuscated —
# Store malware scan and Defender often flag packers.

param(
    [string]$Name = "Obfy.Obfy",
    [string]$Publisher = "CN=Obfy",
    [string]$PublisherDisplayName = "Obfy",
    [switch]$Store,
    [switch]$SkipPublish,
    [switch]$SkipSign,
    [switch]$SkipPri,
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
$RequiredAssets = @(
    "StoreLogo.png",
    "Square44x44Logo.png",
    "Square71x71Logo.png",
    "Square150x150Logo.png",
    "Wide310x150Logo.png",
    "SplashScreen.png"
)

if ($Store) {
    $storeIdentityPath = Join-Path $ProjectRoot "package\store-identity.json"
    if (-not (Test-Path $storeIdentityPath)) {
        throw "package/store-identity.json not found (required with -Store)."
    }

    $storeIdentity = Get-Content $storeIdentityPath -Raw | ConvertFrom-Json
    foreach ($required in @("name", "publisher", "publisherDisplayName")) {
        if (-not $storeIdentity.$required) {
            throw "package/store-identity.json missing '$required'"
        }
    }

    if (-not $PSBoundParameters.ContainsKey("Name")) {
        $Name = [string]$storeIdentity.name
    }
    if (-not $PSBoundParameters.ContainsKey("Publisher")) {
        $Publisher = [string]$storeIdentity.publisher
    }
    if (-not $PSBoundParameters.ContainsKey("PublisherDisplayName")) {
        $PublisherDisplayName = [string]$storeIdentity.publisherDisplayName
    }
    $SkipSign = $true
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Obfy MSIX Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

function Find-SdkTool {
    param([string]$ToolName)

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $candidates = @()
    if (Test-Path $kitsRoot) {
        $versioned = foreach ($dir in Get-ChildItem -Path $kitsRoot -Directory) {
            $parsed = [version]$null
            if ([version]::TryParse($dir.Name, [ref]$parsed)) {
                [pscustomobject]@{ Version = $parsed; Path = Join-Path $dir.FullName "x64\$ToolName" }
            }
        }
        foreach ($item in @($versioned | Sort-Object Version -Descending)) {
            if ($item.Path) { $candidates += $item.Path }
        }
        $candidates += Join-Path $kitsRoot "x64\$ToolName"
    }

    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) { return $path }
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
    foreach ($part in @("major", "minor", "patch")) {
        $value = $version.$part
        if ($null -eq $value) {
            throw "version.json '$part' must be an integer, got ''"
        }
        try {
            $intValue = [int]$value
        } catch {
            throw "version.json '$part' must be an integer, got '$value'"
        }
        if ($intValue -lt 0 -or $intValue -gt 65535) {
            throw "version.json '$part'=$intValue is outside the MSIX 0..65535 range"
        }
    }
    return "$($version.major).$($version.minor).$($version.patch).0"
}

function Get-PublishFileCount([string]$Path) {
    return @(Get-ChildItem -Path $Path -Recurse -File).Count
}

function Invoke-NativeLogged {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$LogPath,
        [string]$FailureMessage
    )

    & $FilePath @ArgumentList *> $LogPath
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path $LogPath) {
            Get-Content $LogPath | Write-Host
        }
        throw $FailureMessage
    }
}

function Get-InstalledPackages([string]$PackageName) {
    try {
        return @(Get-AppxPackage -Name $PackageName)
    } catch {
        Write-Host "  WARNING: Get-AppxPackage failed ($($_.Exception.Message))" -ForegroundColor Yellow
        return @()
    }
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
if ($SkipPri) {
    Write-Host "  MakePri skipped (-SkipPri)" -ForegroundColor Gray
} elseif ($MakePri) {
    Write-Host "  MakePri:  $MakePri" -ForegroundColor Green
} else {
    throw "makepri.exe not found. Install the Windows SDK, or pass -SkipPri."
}

$SignTool = Find-SdkTool "signtool.exe"
if ($SkipSign) {
    Write-Host "  Signing skipped (-SkipSign). Partner Center re-signs Store uploads." -ForegroundColor Gray
} elseif ($SignTool) {
    Write-Host "  SignTool: $SignTool" -ForegroundColor Green
}
if (-not $SkipSign -and -not $SignTool) {
    Write-Host "ERROR: signtool.exe not found. Install Windows SDK Signing Tools," -ForegroundColor Red
    Write-Host "or re-run with -SkipSign (unsigned packages cannot be sideloaded)." -ForegroundColor Yellow
    exit 1
}

$PackageVersion = Get-PackageVersion
Write-Host "  Version: $PackageVersion" -ForegroundColor Green

# Step 2: Clean layout
Write-Host "[2/7] Preparing package layout..." -ForegroundColor Yellow
$uiExe = Join-Path $LayoutDir "ObfyUI.exe"
$cliExe = Join-Path $LayoutDir "CLI\obfy.exe"
if (-not $SkipPublish) {
    $registeredHere = Get-InstalledPackages $Name | Where-Object {
        $_.InstallLocation -and $_.InstallLocation.StartsWith($LayoutDir, [System.StringComparison]::OrdinalIgnoreCase)
    }
    foreach ($pkg in $registeredHere) {
        Write-Host "  Uninstalling $($pkg.PackageFullName) so the layout can be rebuilt..." -ForegroundColor Gray
        Remove-AppxPackage -Package $pkg.PackageFullName
    }
    if (Test-Path $LayoutDir) {
        Remove-Item $LayoutDir -Recurse -Force
    }
    New-Item -Path $LayoutDir -ItemType Directory -Force | Out-Null
} else {
    $missing = @($uiExe, $cliExe) | Where-Object { -not (Test-Path $_) }
    if ($missing.Count -gt 0) {
        throw "-SkipPublish requires an existing layout. Missing: $($missing -join ', '). Re-run without -SkipPublish."
    }
}
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

    if (-not (Test-Path $uiExe)) {
        throw "ObfyUI.exe not found in $LayoutDir"
    }
    Write-Host "  Published $(Get-PublishFileCount $LayoutDir) UI files" -ForegroundColor Green
} else {
    Write-Host "[3/7] Skipping UI publish (-SkipPublish)" -ForegroundColor Gray
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

$missingAssets = @()
foreach ($fileName in $RequiredAssets) {
    $assetPath = Join-Path $AssetsSource $fileName
    if (-not (Test-Path $assetPath)) {
        $missingAssets += $assetPath
    }
}
if ($missingAssets.Count -gt 0) {
    throw "Required Store assets missing. Run .\build\generate-msix-assets.ps1. Missing: $($missingAssets -join ', ')"
}

$layoutAssets = Join-Path $LayoutDir "Assets"
Copy-Item $AssetsSource $layoutAssets -Recurse -Force

[xml]$manifest = Get-Content $ManifestSource
$ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$ns.AddNamespace("def", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
$identity = $manifest.SelectSingleNode("/def:Package/def:Identity", $ns)
if (-not $identity) {
    throw "AppxManifest.xml is missing Package/Identity (check xmlns)"
}
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

$pdbsToRemove = @(Get-ChildItem -Path $LayoutDir -Recurse -Filter *.pdb -File)
if ($pdbsToRemove.Count -gt 0) {
    $pdbsToRemove | Remove-Item -Force
}
$pdbs = @(Get-ChildItem -Path $LayoutDir -Recurse -Filter *.pdb -File)
if ($pdbs.Count -gt 0) {
    throw "PDB files remain in the layout (close the registered app or uninstall first): $($pdbs.FullName -join ', ')"
}

if ($MakePri -and -not $SkipPri) {
    Write-Host "  Generating resources.pri..." -ForegroundColor Gray
    $priConfig = Join-Path $LayoutDir "priconfig.xml"
    $priLog = Join-Path $OutputDir "makepri.log"
    $priOutput = Join-Path $LayoutDir "resources.pri"
    Invoke-NativeLogged -FilePath $MakePri -ArgumentList @("createconfig", "/cf", $priConfig, "/dq", "en-US", "/o") `
        -LogPath $priLog -FailureMessage "makepri createconfig failed"
    Invoke-NativeLogged -FilePath $MakePri -ArgumentList @("new", "/pr", $LayoutDir, "/cf", $priConfig, "/of", $priOutput, "/o") `
        -LogPath $priLog -FailureMessage "makepri new failed"
    if (Test-Path $priConfig) {
        Remove-Item $priConfig -Force
    }
    if (-not (Test-Path $priOutput)) {
        throw "makepri reported success but resources.pri was not created"
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
Invoke-NativeLogged -FilePath $MakeAppx -ArgumentList @("pack", "/d", $LayoutDir, "/p", $PackagePath, "/o") `
    -LogPath $packLog -FailureMessage "makeappx pack failed"
Write-Host "  Packed: $PackagePath" -ForegroundColor Green

# Step 7: Sign
Write-Host "[7/7] Signing package..." -ForegroundColor Yellow
$signed = $false
$cerPath = Join-Path $OutputDir "Obfy-test.cer"
if ($SkipSign) {
    Write-Host "  Skipped (-SkipSign). Partner Center re-signs Store uploads." -ForegroundColor Gray
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

    if (Test-Path $cerPath) {
        Remove-Item $cerPath -Force
    }
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
    & $SignTool sign /fd SHA256 /sha1 $cert.Thumbprint $PackagePath
    if ($LASTEXITCODE -ne 0) {
        throw "signtool sign failed"
    }
    $signed = $true
    Write-Host "  Signed with $($cert.Thumbprint)" -ForegroundColor Green
    Write-Host "  Sideload of the .msix requires this cert in LocalMachine\TrustedPeople (admin)." -ForegroundColor Yellow
    Write-Host "  Import-Certificate -FilePath `"$cerPath`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople" -ForegroundColor Gray
}

if ($Install) {
    Write-Host "Registering loose package layout for this user..." -ForegroundColor Yellow
    $allowSideload = (Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
    if ($allowSideload -ne 1) {
        Write-Host "  WARNING: Developer Mode / sideloading may be disabled (HRESULT 0x80073CFF if register fails)." -ForegroundColor Yellow
    }

    $existing = Get-InstalledPackages $Name
    foreach ($pkg in $existing) {
        Remove-AppxPackage -Package $pkg.PackageFullName
    }
    $manifestToRegister = Join-Path $LayoutDir "AppxManifest.xml"
    Add-AppxPackage -Register $manifestToRegister
    if (-not (Get-InstalledPackages $Name)) {
        throw "Add-AppxPackage -Register returned success but $Name is not installed"
    }
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
if ($Install) {
    Write-Host "Registered loose layout for $Name. Uninstall with:" -ForegroundColor Gray
    Write-Host "  Get-AppxPackage -Name $Name | Remove-AppxPackage" -ForegroundColor Gray
} elseif ($signed) {
    Write-Host "Sideload:  import $cerPath into LocalMachine\TrustedPeople (admin), then:" -ForegroundColor Gray
    Write-Host "  Add-AppxPackage -Path `"$PackagePath`"" -ForegroundColor Gray
    Write-Host "Local try-out without the .msix: .\build\build-msix.ps1 -Install" -ForegroundColor Gray
} else {
    Write-Host "UNSIGNED: do not sideload this file. Use -SkipSign only for Partner Center uploads." -ForegroundColor Yellow
}
Write-Host "Store:     rebuild with Partner Center identity; Microsoft re-signs the upload" -ForegroundColor Gray
