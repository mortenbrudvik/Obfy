$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$repo = Resolve-Path (Join-Path $here "..\..")

function Restore-NetHostPack([string]$Version) {
    $restoreDir = Join-Path ([System.IO.Path]::GetTempPath()) "obfy-hostpack-$Version"
    $extract = Join-Path $restoreDir $Version
    $native = Join-Path $extract "runtimes\win-x64\native\libnethost.lib"
    if (-not (Test-Path $native)) {
        New-Item -ItemType Directory -Force -Path $restoreDir | Out-Null
        $nupkg = Join-Path $restoreDir "Microsoft.NETCore.App.Host.win-x64.$Version.nupkg"
        $url = "https://www.nuget.org/api/v2/package/Microsoft.NETCore.App.Host.win-x64/$Version"
        Write-Host "Microsoft.NETCore.App.Host.win-x64 $Version not installed; downloading $url"
        Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing
        if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $extract)
    }
    if (-not (Test-Path $native)) {
        throw "Restored Microsoft.NETCore.App.Host.win-x64 $Version is missing libnethost.lib at $native"
    }
    return Get-Item $extract
}

# -o pins the output so Platform=x64 from vcvars/msvc-dev-cmd cannot hide the DLL.
$bootstrapDir = Join-Path $repo "Src\Obfy.PackedBootstrap\bin\Release\net8.0"
dotnet build (Join-Path $repo "Src\Obfy.PackedBootstrap\Obfy.PackedBootstrap.csproj") -c Release --nologo -o $bootstrapDir
if ($LASTEXITCODE -ne 0) { throw "PackedBootstrap build failed with exit $LASTEXITCODE" }

$bootstrap = Join-Path $bootstrapDir "Obfy.PackedBootstrap.dll"
if (-not (Test-Path $bootstrap)) { throw "PackedBootstrap.dll missing: $bootstrap" }

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetRoot = if ($dotnetCmd) { Split-Path $dotnetCmd.Source } else { $null }
$packCandidates = @(
    (Join-Path ${env:ProgramW6432} "dotnet\packs\Microsoft.NETCore.App.Host.win-x64"),
    (Join-Path ${env:ProgramFiles} "dotnet\packs\Microsoft.NETCore.App.Host.win-x64")
)
if ($dotnetRoot) {
    $packCandidates = @((Join-Path $dotnetRoot "packs\Microsoft.NETCore.App.Host.win-x64")) + $packCandidates
}
# Pin 8.0.6: later host packs need MSVC 14.42+ (__std_find_end_2) and would churn CI dist.
# 8.0.6 is the runtime/host-pack version (there is no SDK 8.0.6).
$packVersion = "8.0.6"
# Pin the exact official Microsoft.NETCore.App.Host.win-x64 8.0.6 native pack files.
$expectedPackHashes = @{
    "libnethost.lib"      = "812C7B0CA2B9B5783565A2632AE63F7ACFAA3ECD6ACF0307C2BB82A836EF47FE"
    "nethost.h"           = "D5254A53C8C95CF1878D39D88A5274F94B31717543AA5FB6A8EF005BC1007DEE"
    "hostfxr.h"           = "D510F774012576AB31B6964223F41491C5326998CA8AEBBD90F0422C239140C9"
    "coreclr_delegates.h" = "344E5467128AFFF9074B4FE65285E810FD65C9C9823366C53EA0A1793060F2A8"
}

$pack = $null
foreach ($root in $packCandidates) {
    if (-not $root -or -not (Test-Path $root)) { continue }
    $candidate = Join-Path $root $packVersion
    if (Test-Path $candidate) {
        $pack = Get-Item $candidate
        break
    }
}
if (-not $pack) {
    $pack = Restore-NetHostPack $packVersion
}

$native = Join-Path $pack.FullName "runtimes\win-x64\native"
foreach ($entry in ($expectedPackHashes.GetEnumerator() | Sort-Object Key)) {
    $packFile = Join-Path $native $entry.Key
    if (-not (Test-Path -LiteralPath $packFile -PathType Leaf)) {
        throw "Microsoft.NETCore.App.Host.win-x64 $packVersion is missing $($entry.Key) at $packFile"
    }
    $actualHash = (Get-FileHash -LiteralPath $packFile -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualHash, $entry.Value, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 mismatch for $($entry.Key) at $packFile (expected $($entry.Value), got $actualHash)"
    }
}
$nethostLib = Join-Path $native "libnethost.lib"

$dist = Join-Path $here "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$exe = Join-Path $dist "Obfy.NativeHost.exe"
$rc = Join-Path $here "host.generated.rc"
$res = Join-Path $here "host.res"
$implib = Join-Path $here "host.lib"
$escapedBootstrap = $bootstrap -replace '\\', '\\'
$rcContent = "BOOTSTRAP RCDATA `"$escapedBootstrap`"`n"
[System.IO.File]::WriteAllText($rc, $rcContent, [System.Text.UTF8Encoding]::new($false))

function Invoke-WithMsvc([string]$CommandLine) {
    $full = "cd /d `"$here`" && $CommandLine"
    # Prefer an already-configured x64 MSVC prompt (CI: ilammy/msvc-dev-cmd).
    # Do not pick up a 32-bit cl.exe from PATH; the stub must be PE32+.
    if ($env:VSCMD_ARG_TGT_ARCH -eq 'x64' -and (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
        cmd.exe /c $full
    }
    else {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
        if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found: $vswhere" }
        $vs = & $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if (-not $vs) { throw "MSVC x64 tools not found (vswhere)" }
        $vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
        if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found: $vcvars" }
        cmd.exe /c "call `"$vcvars`" && $full"
    }
}

Invoke-WithMsvc "rc.exe /nologo /c 65001 /fo `"$res`" `"$rc`""
if ($LASTEXITCODE -ne 0) { throw "rc.exe failed with exit $LASTEXITCODE" }

$compile = @(
    "cl.exe /nologo /O2 /Brepro /W3 /WX /MT /DUNICODE /D_UNICODE /I `"$native`"",
    "host.c /Fe:`"$exe`"",
    "/link /Brepro /INCREMENTAL:NO /SUBSYSTEM:CONSOLE /IMPLIB:`"$implib`"",
    "`"$res`" bcrypt.lib advapi32.lib user32.lib `"$nethostLib`""
) -join " "

Write-Host "Linking libnethost.lib from pack $($pack.Name)"
Invoke-WithMsvc $compile
if ($LASTEXITCODE -ne 0) {
    throw "Failed to statically link libnethost.lib from Microsoft.NETCore.App.Host.win-x64 $($pack.Name) (exit $LASTEXITCODE)"
}
if (-not (Test-Path $exe)) { throw "Native host exe missing: $exe" }

$stampScript = Join-Path $repo "build\Get-NativeHostSourceStamp.ps1"
$stamp = & $stampScript -RepoRoot "$repo"
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace("$stamp")) {
    throw "Native host source stamp failed"
}
$stampPath = Join-Path $dist "sources.sha256"
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($stampPath, ("$stamp".Trim() + "`n"), $utf8NoBom)

Write-Host "Built $exe (pack $($pack.Name), static libnethost)"
Write-Host "Stamped $stampPath"
