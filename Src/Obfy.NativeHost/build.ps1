$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$repo = Resolve-Path (Join-Path $here "..\..")

dotnet build (Join-Path $repo "Src\Obfy.PackedBootstrap\Obfy.PackedBootstrap.csproj") -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "PackedBootstrap build failed with exit $LASTEXITCODE" }

$bootstrap = Join-Path $repo "Src\Obfy.PackedBootstrap\bin\Release\net8.0\Obfy.PackedBootstrap.dll"
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
$packRoot = $packCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $packRoot) { throw "Host pack missing. Looked in: $($packCandidates -join '; ')" }
$packs = @(Get-ChildItem $packRoot -Directory | Sort-Object { [version]$_.Name } -Descending)
if ($packs.Count -eq 0) { throw "No Microsoft.NETCore.App.Host.win-x64 versions found" }

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

# libnethost.lib from 8.0.22+ needs MSVC 14.42+ (__std_find_end_2). Try newest
# pack first (CI); fall back until static link succeeds (local 14.40 → 8.0.6).
$builtPack = $null
foreach ($pack in $packs) {
    $native = Join-Path $pack.FullName "runtimes\win-x64\native"
    $nethostLib = Join-Path $native "libnethost.lib"
    if (-not (Test-Path $nethostLib)) { continue }
    if (-not (Test-Path (Join-Path $native "nethost.h"))) { continue }
    if (-not (Test-Path (Join-Path $native "hostfxr.h"))) { continue }

    $compile = @(
        "cl.exe /nologo /O2 /Brepro /W3 /WX /MT /DUNICODE /D_UNICODE /I `"$native`"",
        "host.c /Fe:`"$exe`"",
        "/link /Brepro /INCREMENTAL:NO /SUBSYSTEM:CONSOLE /IMPLIB:`"$implib`"",
        "`"$res`" bcrypt.lib advapi32.lib `"$nethostLib`""
    ) -join " "

    Write-Host "Linking libnethost.lib from pack $($pack.Name)"
    Invoke-WithMsvc $compile
    if ($LASTEXITCODE -eq 0) {
        $builtPack = $pack.Name
        break
    }
    Write-Host "Pack $($pack.Name) failed to link (exit $LASTEXITCODE); trying an older host pack."
}

if (-not $builtPack) {
    throw "Failed to statically link libnethost.lib from any Microsoft.NETCore.App.Host.win-x64 pack"
}
if (-not (Test-Path $exe)) { throw "Native host exe missing: $exe" }
Write-Host "Built $exe (pack $builtPack, static libnethost)"
