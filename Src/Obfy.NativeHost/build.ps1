$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$repo = Resolve-Path (Join-Path $here "..\..")

function Restore-NetHostPack([string]$Version) {
    $restoreDir = Join-Path ([System.IO.Path]::GetTempPath()) "obfy-hostpack-$Version"
    $extract = Join-Path $restoreDir "pkg"
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

# ilammy/msvc-dev-cmd / vcvars set Platform=x64, which sends SDK-style output
# to bin/x64/Release instead of bin/Release.
$savedPlatform = $env:Platform
Remove-Item Env:Platform -ErrorAction SilentlyContinue
try {
    dotnet build (Join-Path $repo "Src\Obfy.PackedBootstrap\Obfy.PackedBootstrap.csproj") `
        -c Release --nologo -p:Platform=AnyCPU
    if ($LASTEXITCODE -ne 0) { throw "PackedBootstrap build failed with exit $LASTEXITCODE" }
}
finally {
    if ($null -ne $savedPlatform) { $env:Platform = $savedPlatform }
}

$bootstrap = Join-Path $repo "Src\Obfy.PackedBootstrap\bin\Release\net8.0\Obfy.PackedBootstrap.dll"
if (-not (Test-Path $bootstrap)) {
    $x64 = Join-Path $repo "Src\Obfy.PackedBootstrap\bin\x64\Release\net8.0\Obfy.PackedBootstrap.dll"
    if (Test-Path $x64) { $bootstrap = $x64 }
}
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

$dist = Join-Path $here "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$exe = Join-Path $dist "Obfy.NativeHost.exe"
$rc = Join-Path $here "host.generated.rc"
$res = Join-Path $here "host.res"
$implib = Join-Path $here "host.lib"
$escapedBootstrap = $bootstrap -replace '\\', '\\'
Set-Content -Path $rc -Value "BOOTSTRAP RCDATA `"$escapedBootstrap`"" -Encoding ascii

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

Invoke-WithMsvc "rc.exe /nologo /fo `"$res`" `"$rc`""
if ($LASTEXITCODE -ne 0) { throw "rc.exe failed with exit $LASTEXITCODE" }

$native = Join-Path $pack.FullName "runtimes\win-x64\native"
$nethostLib = Join-Path $native "libnethost.lib"
if (-not (Test-Path $nethostLib)) { throw "libnethost.lib missing: $nethostLib" }
if (-not (Test-Path (Join-Path $native "nethost.h"))) { throw "nethost.h missing under $native" }
if (-not (Test-Path (Join-Path $native "hostfxr.h"))) { throw "hostfxr.h missing under $native" }

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
Write-Host "Built $exe (pack $($pack.Name), static libnethost)"
