# Fails when host.c, build.ps1, or PackedBootstrap sources disagree with the
# committed stamp. Compare against git HEAD (-FromGitHead) after a rebuild:
# build.ps1 rewrites the working-tree stamp, and that rewrite must not hide a
# stale committed stamp.
[CmdletBinding()]
param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string] $StampPath,
    [switch] $FromGitHead
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if ($FromGitHead) {
    $show = & git -C $RepoRoot --no-pager show "HEAD:Src/Obfy.NativeHost/dist/sources.sha256" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Committed native host source stamp is missing (Src/Obfy.NativeHost/dist/sources.sha256)."
        exit 1
    }
    $expected = (@($show) -join "`n").Trim()
}
elseif (-not [string]::IsNullOrWhiteSpace($StampPath)) {
    if (-not (Test-Path -LiteralPath $StampPath -PathType Leaf)) {
        Write-Error "Native host source stamp is missing: $StampPath"
        exit 1
    }
    $expected = (Get-Content -Raw -LiteralPath $StampPath).Trim()
}
else {
    Write-Error "Pass -StampPath or -FromGitHead."
    exit 1
}

$actual = & (Join-Path $PSScriptRoot 'Get-NativeHostSourceStamp.ps1') -RepoRoot $RepoRoot
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace("$actual")) {
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Error "Native host source stamp was empty."
    exit 1
}

$actual = ("$actual").Trim()
if ($actual -ne $expected) {
    Write-Error "Native host sources do not match the committed stamp. Run Src/Obfy.NativeHost/build.ps1 and commit dist/Obfy.NativeHost.exe together with dist/sources.sha256."
    exit 1
}

Write-Output $actual
exit 0
